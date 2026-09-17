using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Landfall.TABS;
using TabsSkinColorMod.Core;
using UnityEngine;

namespace TabsSkinColorMod.Plugin
{
    /// <summary>A renderer + material slot identified as part of the unit's skin (body).</summary>
    internal sealed class SkinTarget
    {
        public Renderer Renderer;
        public int SlotIndex;
        public Material Material;
    }

    /// <summary>
    /// Finds the "skin" (body) materials of a unit or preview root and applies per-unit colors.
    ///
    /// The game drives the skin color through Landfall.TABS.TeamColor components on the
    /// body renderers: they swap the renderer's whole material between shared red/blue
    /// palette materials (redMaterial/blueMaterial fields). Writing material._Color
    /// alone is therefore not enough (and tinting the shared palette material would
    /// recolor every unit). So:
    ///   1. Skin renderers are identified as those carrying a TeamColor component (the
    ///      game's own definition); fallback to TFBG-shader heuristic.
    ///   2. Tinted copies of the palette materials are created per (source, wanted
    ///      color) in ONE global cache and assigned to the renderer slots, so team
    ///      switches keep the custom color.
    ///   3. Remaining skin materials without a TeamColor get a direct _Color tint.
    ///   4. UnitColorHandler.rendColors is kept in sync so the game's UpdateColors()
    ///      fades (death gray etc.) lerp from our color.
    ///
    /// Every tint flows through the (source, color) cache and the Harmony hooks consult
    /// TryGetWantedColor (the saved store color for the unit behind a TeamColor, or the
    /// editor's live color for Unit Creator previews). When no color is wanted the
    /// hooks hand back vanilla materials — so nothing can stay tinted: no color leaks
    /// between units, and reset is instantly vanilla everywhere.
    /// </summary>
    internal static class SkinApplier
    {
        // source palette material -> (wanted color -> tinted instance)
        private static readonly Dictionary<Material, Dictionary<Color32, Material>> TintCache =
            new Dictionary<Material, Dictionary<Color32, Material>>();
        // tinted instance -> its source palette material
        private static readonly Dictionary<Material, Material> InstanceToSource =
            new Dictionary<Material, Material>();
        // every material seen as a team palette source (red/blueMaterial)
        private static readonly HashSet<Material> KnownPaletteSources = new HashSet<Material>();
        // base name (without Unity's " (Instance)" suffix) -> source palette material.
        // The game instantiates renderer materials (rend.materials), producing copies of
        // our tinted instances named e.g. "TABS_Red_SkinTinted (Instance)" — those copies
        // are unknown to the instance map, so they must be recognized by name.
        private static readonly Dictionary<string, Material> TintedNameToSource =
            new Dictionary<string, Material>(StringComparer.Ordinal);

        // original _Color of directly tinted materials (for restore)
        private static readonly ConditionalWeakTable<Material, StrongBox<Color>> Originals =
            new ConditionalWeakTable<Material, StrongBox<Color>>();

        // ------------------------------------------------------------- wanted color

        /// <summary>
        /// The color that SHOULD be shown for the body owning this TeamColor right now:
        /// the saved store color for real units, or the editor's live color while a tint
        /// is actively wanted in the Unit Creator. False = show vanilla.
        /// </summary>
        public static bool TryGetWantedColor(TeamColor tc, out Color32 color)
        {
            color = default;
            if (tc == null) return false;
            try
            {
                for (var t = tc.transform; t != null; t = t.parent)
                {
                    var unit = t.GetComponent<Unit>();
                    if (unit != null)
                    {
                        // The UC's preview body can carry a Unit whose blueprint key is a
                        // stale/template name (e.g. "CustomWobbler" while editing a new
                        // unit) — the editor's key is the authority there, otherwise the
                        // wrong saved color fights the editor's color every frame.
                        if (SkinEditorUI.IsUcPreviewUnit(unit))
                            return SkinEditorUI.TryGetUcWantedColor(out color);
                        var plugin = SkinColorModPlugin.Instance;
                        if (plugin == null) return false;
                        var key = Refs.UnitKey(unit);
                        // UC test mode: only the tested unit itself may use its saved
                        // color — enemies spawned for the test stay vanilla.
                        if (!StoreColorAllowedForKey(key)) return false;
                        if (!plugin.Store.TryGetColor(key, out var hex)) return false;
                        if (!RgbaColor.TryParse(hex, out var rgba)) return false;
                        color = rgba.ToColor32();
                        return true;
                    }
                    // Unit Creator preview bodies are plain GameObjects (not Units) under
                    // the editor manager; the displayed model may instead live under the
                    // preview manager. Both get the editor's wanted color.
                    if (t.GetComponent<Landfall.TABS.UnitEditor.UnitEditorManager>() != null ||
                        t.GetComponent<Landfall.TABS.UnitPreviewManager>() != null)
                    {
                        return SkinEditorUI.TryGetUcWantedColor(out color);
                    }
                }
            }
            catch { /* never break the game's color pipeline */ }
            return false;
        }

        /// <summary>In UC TEST UNIT mode only the unit being tested may use its saved
        /// color — enemy/other units spawned for the test stay vanilla.</summary>
        public static bool StoreColorAllowedForKey(string key)
        {
            try
            {
                if (Landfall.TABS.UnitEditor.UnitEditorManager.isTestingUnit)
                {
                    var ucKey = SkinEditorUI.CurrentUcKey;
                    return !string.IsNullOrEmpty(ucKey) &&
                           string.Equals(ucKey, key, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { /* field missing in older builds — allow */ }
            return true;
        }

        // ------------------------------------------------------------- tint cache

        private static void RegisterSource(Material source)
        {
            if (source == null) return;
            if (KnownPaletteSources.Add(source) && !Originals.TryGetValue(source, out _))
                Originals.Add(source, new StrongBox<Color>(source.color));
        }

        /// <summary>The tinted instance of a palette material for a specific color.</summary>
        public static Material TintOf(Material source, Color32 color)
        {
            if (source == null) return null;
            RegisterSource(source);
            if (!TintCache.TryGetValue(source, out var byColor))
            {
                byColor = new Dictionary<Color32, Material>();
                TintCache[source] = byColor;
            }
            if (!byColor.TryGetValue(color, out var inst) || inst == null)
            {
                inst = new Material(source);
                inst.name = source.name + "_SkinTinted";
                inst.color = color; // belt-and-braces: the game's UpdateColors also paints _Color from rendColors
                byColor[color] = inst;
                InstanceToSource[inst] = source;
                TintedNameToSource[inst.name] = source;
            }
            return inst;
        }

        /// <summary>
        /// If `m` is one of our tinted instances — or a Unity-instantiated copy of one
        /// (name suffix " (Instance)") — returns its source palette material, else null.
        /// </summary>
        public static Material FindTintedSource(Material m)
        {
            if (m == null) return null;
            if (InstanceToSource.TryGetValue(m, out var src)) return src;
            var name = m.name;
            if (name != null && name.EndsWith(" (Instance)", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - " (Instance)".Length);
            return TintedNameToSource.TryGetValue(name ?? "", out src) ? src : null;
        }

        /// <summary>
        /// Puts the tinted instance for `color` into every renderer slot that carries a
        /// known palette material (or a tinted instance of one — handles color changes).
        /// </summary>
        public static void ReplaceSlotsWithColor(Renderer rend, Color32 color)
        {
            if (rend == null) return;
            var mats = rend.sharedMaterials;
            if (mats == null) return;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                var src = FindTintedSource(m);
                if (src != null) m = src;
                if (m != null && KnownPaletteSources.Contains(m))
                {
                    var inst = TintOf(m, color);
                    if (inst != null && !ReferenceEquals(mats[i], inst))
                    {
                        mats[i] = inst;
                        changed = true;
                    }
                }
            }
            if (changed) rend.sharedMaterials = mats;
        }

        // ------------------------------------------------------------- public API

        public static List<SkinTarget> FindSkinTargets(Unit unit, PatternMatcher nameFilter, bool logMaterials)
        {
            if (unit == null) return new List<SkinTarget>();
            return FindSkinTargetsInRoot(unit.transform, nameFilter, logMaterials, CollectPropRendererIds(unit), null);
        }

        public static List<SkinTarget> FindSkinTargetsInRoot(Transform root, PatternMatcher nameFilter, bool logMaterials)
        {
            return FindSkinTargetsInRoot(root, nameFilter, logMaterials, null, null);
        }

        /// <summary>Applies a color to a real unit's skin. Returns true if anything was tinted.</summary>
        public static bool ApplyColor(Unit unit, Color32 color, PatternMatcher nameFilter, out List<SkinTarget> applied)
        {
            var propIds = CollectPropRendererIds(unit);
            bool ok = ApplyColorToRoot(unit != null ? unit.transform : null, color, nameFilter, out applied, propIds);
            if (ok && unit != null)
                SyncHandlerOriginals(unit.transform, applied, color);
            return ok;
        }

        /// <summary>Applies a color to any root (unit or Unit Creator preview GameObjects).</summary>
        public static bool ApplyColorToRoot(Transform root, Color32 color, PatternMatcher nameFilter, out List<SkinTarget> applied)
        {
            return ApplyColorToRoot(root, color, nameFilter, out applied, null);
        }

        /// <summary>Applies a color to a root, skipping equipment (CharacterItem subtrees).
        /// Used for the UC roots, where clothes/weapons live next to the body.</summary>
        public static bool ApplyColorToRootExcludingEquipment(Transform root, Color32 color, PatternMatcher nameFilter, out List<SkinTarget> applied)
        {
            return ApplyColorToRoot(root, color, nameFilter, out applied, CollectCharacterItemRendererIds(root));
        }

        /// <summary>Every renderer belonging to an equipped item (clothes/weapons).
        /// Covers bone-attached pieces re-parented outside the item's transform via the
        /// item's own m_gameObjects list.</summary>
        private static HashSet<int> CollectCharacterItemRendererIds(Transform root)
        {
            var ids = new HashSet<int>();
            if (root == null) return ids;
            foreach (var item in root.GetComponentsInChildren<CharacterItem>(true))
            {
                if (item == null) continue;
                CollectItemRendererIds(item, ids);
            }
            return ids;
        }

        private static void CollectItemRendererIds(CharacterItem item, HashSet<int> ids)
        {
            foreach (var rend in item.GetComponentsInChildren<Renderer>(true))
                ids.Add(rend.GetInstanceID());
            var gos = Refs.GetItemGameObjects(item);
            if (gos != null)
                foreach (var go in gos)
                    if (go != null)
                        foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
                            ids.Add(rend.GetInstanceID());
        }

        /// <summary>True if this renderer belongs to equipment (clothes/weapons), not
        /// the body: either registered in the prop set, or carrying a CharacterItem in
        /// its ancestry (covers items parented outside the scanned root).</summary>
        private static bool IsPropRenderer(Renderer rend, HashSet<int> propIds)
        {
            if (propIds != null && propIds.Contains(rend.GetInstanceID())) return true;
            try { return rend.GetComponentInParent<CharacterItem>() != null; }
            catch { return false; }
        }

        /// <summary>Restores the pre-mod colors of a unit's skin materials.</summary>
        public static void RestoreOriginals(Unit unit, PatternMatcher nameFilter)
        {
            if (unit != null)
                RestoreOriginalsInRoot(unit.transform, nameFilter);
        }

        public static void RestoreOriginalsInRoot(Transform root, PatternMatcher nameFilter)
        {
            if (root == null) return;
            RestoreRenderers(root.GetComponentsInChildren<Renderer>(true));
        }

        /// <summary>
        /// Swaps any tinted-instance slots back to their source palette material and
        /// undoes direct _Color tints. Covers TeamColor-driven renderers AND renderers
        /// that only share the palette materials (hands, and the UC's TeamColor-less
        /// display models) — those were missed before, which left stale colors visible
        /// after every reset.
        /// </summary>
        private static void RestoreRenderers(Renderer[] rends)
        {
            foreach (var rend in rends)
            {
                if (rend == null) continue;
                var mats = rend.sharedMaterials;
                if (mats == null) continue;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    var src = FindTintedSource(m);
                    if (src != null)
                    {
                        if (!ReferenceEquals(mats[i], src)) { mats[i] = src; changed = true; }
                        continue;
                    }
                    if (KnownPaletteSources.Contains(m)) continue;
                    if (Originals.TryGetValue(m, out var box) && m.color != box.Value)
                        m.color = box.Value;
                }
                if (changed) rend.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// Safety net for renderers OUTSIDE the roots we track (display models placed
        /// elsewhere in the scene): any slot still holding a tinted instance on a body
        /// with no wanted color goes back to vanilla. Runs only on unit switches/resets.
        /// </summary>
        public static void RestoreOrphanTintsInScene()
        {
            try
            {
                foreach (var rend in UnityEngine.Object.FindObjectsOfType<Renderer>())
                {
                    if (rend == null) continue;
                    var mats = rend.sharedMaterials;
                    if (mats == null) continue;
                    bool hasInstance = false;
                    for (int i = 0; i < mats.Length && !hasInstance; i++)
                        if (mats[i] != null && FindTintedSource(mats[i]) != null) hasInstance = true;
                    if (!hasInstance) continue;
                    if (BodyHasWantedColor(rend)) continue;
                    RestoreRenderers(new[] { rend });
                }
            }
            catch { /* best effort */ }
        }

        private static bool BodyHasWantedColor(Renderer rend)
        {
            try
            {
                for (var t = rend.transform; t != null; t = t.parent)
                {
                    var unit = t.GetComponent<Unit>();
                    if (unit != null)
                    {
                        if (SkinEditorUI.IsUcPreviewUnit(unit))
                            return SkinEditorUI.TryGetUcWantedColor(out _);
                        var plugin = SkinColorModPlugin.Instance;
                        return plugin != null && plugin.Store.TryGetColor(Refs.UnitKey(unit), out _);
                    }
                    if (t.GetComponent<Landfall.TABS.UnitEditor.UnitEditorManager>() != null ||
                        t.GetComponent<Landfall.TABS.UnitPreviewManager>() != null)
                        return SkinEditorUI.TryGetUcWantedColor(out _);
                }
            }
            catch { /* best effort */ }
            return false;
        }

        /// <summary>
        /// Updates UnitColorHandler.rendColors so the game's own UpdateColors() (death-gray
        /// fade etc.) lerps from our color instead of overwriting it.
        /// </summary>
        public static void SyncHandlerOriginals(Transform root, List<SkinTarget> targets, Color32 color)
        {
            if (root == null || targets == null || targets.Count == 0) return;
            var handlers = root.GetComponentsInChildren<UnitColorHandler>(true);
            foreach (var handler in handlers)
            {
                var rends = Refs.GetHandlerRenderers(handler);
                var rendColors = Refs.GetHandlerRendColors(handler);
                if (rends == null || rendColors == null) continue;

                foreach (var t in targets)
                {
                    for (int i = 0; i < rends.Length; i++)
                    {
                        if (!ReferenceEquals(rends[i], t.Renderer)) continue;
                        var slots = rendColors[i];
                        if (slots != null && t.SlotIndex < slots.Length)
                            slots[t.SlotIndex] = color;
                    }
                }
            }
        }

        public static bool TryGetOriginalColor(Material mat, out Color color)
        {
            if (Originals.TryGetValue(mat, out var box)) { color = box.Value; return true; }
            color = default;
            return false;
        }

        /// <summary>
        /// GetTeamMaterial Harmony postfix helper: if a color is wanted for this
        /// TeamColor's body, the game receives our tinted instance instead of the
        /// vanilla palette material — so every game-driven re-tint (spawn, pooled
        /// re-use, team switch, UC refresh) automatically carries the custom color.
        /// </summary>
        public static bool TryGetTinted(TeamColor tc, Material original, out Material tinted)
        {
            tinted = null;
            if (tc == null || original == null) return false;
            if (InstanceToSource.TryGetValue(original, out var src)) original = src;
            if (!TryGetWantedColor(tc, out var color)) return false;
            tinted = TintOf(original, color);
            return tinted != null;
        }

        /// <summary>
        /// UpdateTeamColors/SetTeamColor Harmony postfix helper: re-force the tinted
        /// instance into the renderer slots — but only when a color is wanted. When no
        /// color is wanted, vanilla materials stay untouched (true reset).
        /// </summary>
        public static void ReapplyTints(TeamColor tc)
        {
            try
            {
                if (tc == null) return;
                if (!TryGetWantedColor(tc, out var color)) return;
                var rend = tc.GetComponent<Renderer>();
                if (rend == null) return;
                ReplaceSlotsWithColor(rend, color);
            }
            catch { /* never break the game's color pipeline */ }
        }

        // ------------------------------------------------------------- internals

        private static bool ApplyColorToRoot(Transform root, Color32 color, PatternMatcher nameFilter, out List<SkinTarget> applied, HashSet<int> propIds)
        {
            applied = new List<SkinTarget>();
            if (root == null) return false;
            bool log = SkinColorModPlugin.Instance != null && SkinColorModPlugin.Instance.LogSkinMaterials.Value;

            // 1) TeamColor-driven skin (the real pipeline for body colors).
            var teamHandled = new HashSet<int>();
            foreach (var tc in root.GetComponentsInChildren<TeamColor>(true))
            {
                if (tc == null) continue;
                var rend = tc.GetComponent<Renderer>();
                if (rend == null) continue;
                if (IsPropRenderer(rend, propIds)) continue; // equipment, not body
                if (!PassesNameFilter(rend, nameFilter)) continue;

                RegisterSource(tc.redMaterial);
                RegisterSource(tc.blueMaterial);
                ReplaceSlotsWithColor(rend, color);
                teamHandled.Add(rend.GetInstanceID());
                var curMats = rend.sharedMaterials;
                int slot = Mathf.Max(0, tc.materialID);
                applied.Add(new SkinTarget
                {
                    Renderer = rend,
                    SlotIndex = slot,
                    Material = curMats != null && slot < curMats.Length ? curMats[slot] : null
                });
            }

            // 1b) Body parts that share the palette materials but have no TeamColor of
            // their own (e.g. hands) — swap every matching slot under the root.
            foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
            {
                if (rend == null) continue;
                if (IsPropRenderer(rend, propIds)) continue;
                if (teamHandled.Contains(rend.GetInstanceID())) continue;
                var mats = rend.sharedMaterials;
                if (mats == null) continue;
                bool relevant = false;
                for (int i = 0; i < mats.Length && !relevant; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (InstanceToSource.TryGetValue(m, out var src)) m = src;
                    if (m != null && KnownPaletteSources.Contains(m)) relevant = true;
                }
                if (!relevant) continue;

                ReplaceSlotsWithColor(rend, color);
                teamHandled.Add(rend.GetInstanceID());
                var cur = rend.sharedMaterials;
                applied.Add(new SkinTarget
                {
                    Renderer = rend,
                    SlotIndex = 0,
                    Material = cur != null && cur.Length > 0 ? cur[0] : null
                });
            }

            // 2) Direct tint for skin materials not driven by a TeamColor.
            var targets = FindSkinTargetsInRoot(root, nameFilter, log, propIds, teamHandled);
            foreach (var t in targets)
            {
                if (t.Material == null) continue;
                if (teamHandled.Contains(t.Renderer.GetInstanceID())) continue;
                if (!Originals.TryGetValue(t.Material, out var box))
                    Originals.Add(t.Material, new StrongBox<Color>(t.Material.color));
                t.Material.color = color;
                applied.Add(t);
            }

            return applied.Count > 0;
        }

        private static List<SkinTarget> FindSkinTargetsInRoot(Transform root, PatternMatcher nameFilter, bool logMaterials, HashSet<int> propIds, HashSet<int> skipRends)
        {
            var results = new List<SkinTarget>();
            if (root == null) return results;

            if (propIds == null) propIds = new HashSet<int>();
            var teamRendererIds = CollectTeamColorRendererIds(root);

            foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
            {
                if (rend == null) continue;
                if (IsPropRenderer(rend, propIds)) continue;
                if (skipRends != null && skipRends.Contains(rend.GetInstanceID())) continue;

                if (teamRendererIds.Count > 0)
                {
                    // The game's own definition of skin: renderers with a TeamColor.
                    if (!teamRendererIds.Contains(rend.GetInstanceID())) continue;
                }
                else
                {
                    if (!HasTfbgMaterial(rend)) continue;
                }

                var materials = rend.materials; // per-unit instances
                for (int i = 0; i < materials.Length; i++)
                {
                    var mat = materials[i];
                    if (mat == null) continue;
                    if (mat.shader == null || !mat.shader.name.StartsWith("TFBG/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!mat.HasProperty("_Color")) continue;
                    if (nameFilter.HasAnyPositive && !nameFilter.IsMatch(mat.name)) continue;
                    results.Add(new SkinTarget { Renderer = rend, SlotIndex = i, Material = mat });
                }
                rend.materials = materials;
            }

            if (logMaterials && results.Count > 0)
            {
                var names = new HashSet<string>();
                foreach (var t in results) names.Add(t.Renderer.name + ":" + (t.Material != null ? t.Material.name : "<null>"));
                SkinColorModPlugin.Log.LogInfo("[SkinColor] skin (" + (teamRendererIds.Count > 0 ? "TeamColor-driven" : "heuristic") + "): " + string.Join(", ", names));
            }
            return results;
        }

        /// <summary>Renderers that carry a TeamColor component — exactly how the game
        /// decides what counts as team/skin-colored body parts.</summary>
        private static HashSet<int> CollectTeamColorRendererIds(Transform root)
        {
            var ids = new HashSet<int>();
            if (root == null) return ids;
            foreach (var tc in root.GetComponentsInChildren<TeamColor>(true))
            {
                if (tc == null) continue;
                var rend = tc.GetComponent<Renderer>();
                if (rend != null) ids.Add(rend.GetInstanceID());
            }
            return ids;
        }

        private static bool PassesNameFilter(Renderer rend, PatternMatcher nameFilter)
        {
            if (!nameFilter.HasAnyPositive) return true;
            var shared = rend.sharedMaterials;
            if (shared == null) return false;
            foreach (var m in shared)
                if (m != null && nameFilter.IsMatch(m.name)) return true;
            return false;
        }

        private static bool HasTfbgMaterial(Renderer rend)
        {
            var shared = rend.sharedMaterials;
            if (shared == null) return false;
            foreach (var m in shared)
                if (m != null && m.shader != null && m.shader.name.StartsWith("TFBG/", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static HashSet<int> CollectPropRendererIds(Unit unit)
        {
            var ids = new HashSet<int>();
            if (unit == null) return ids;
            var spawned = unit.spawnedObjects;
            if (spawned == null) return ids;
            foreach (var go in spawned)
            {
                if (go == null) continue;
                // Only actual equipment (CharacterItem-driven clothes/weapons) is a prop.
                // spawnedObjects can also contain body parts (hands, base meshes), which
                // must stay tintable — blanket-excluding the list killed battle applies.
                var item = go.GetComponent<CharacterItem>() ?? go.GetComponentInChildren<CharacterItem>(true);
                if (item == null) continue;
                CollectItemRendererIds(item, ids);
            }
            return ids;
        }
    }
}
