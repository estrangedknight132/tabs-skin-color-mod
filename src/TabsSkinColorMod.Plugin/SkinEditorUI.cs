using System;
using System.Collections.Generic;
using HarmonyLib;
using Landfall.TABS;
using TabsSkinColorMod.Core;
using UnityEngine;

namespace TabsSkinColorMod.Plugin
{
    /// <summary>
    /// IMGUI overlay: a "SKIN COLOR" button pinned next to the team color swatches
    /// (top-right of the Unit Creator screen), plus a draggable editor window that
    /// works both in the Unit Creator and on the battlefield (last spawned unit).
    /// </summary>
    internal sealed class SkinEditorUI : MonoBehaviour
    {
        private const int WinId = 0x5C10;
        private static readonly Rect ButtonRect = new Rect(0, 12, 150, 34); // x computed per-frame (right aligned)

        private static readonly Color32[] Swatches =
        {
            new Color32(199, 161, 122, 255), // default wobbler tan
            new Color32(140, 90, 60, 255),
            new Color32(255, 228, 196, 255),
            new Color32(240, 180, 140, 255),
            new Color32(205, 133, 63, 255),
            new Color32(139, 69, 19, 255),
            new Color32(255, 105, 180, 255),
            new Color32(0, 191, 255, 255),
            new Color32(50, 205, 50, 255),
            new Color32(148, 0, 211, 255),
            new Color32(255, 215, 0, 255),
            new Color32(20, 20, 20, 255),
        };

        private static readonly Color32 DefaultColor = new Color32(199, 161, 122, 255); // vanilla tan

        private bool _open;
        private Rect _winRect = new Rect(0, 60, 320, 480);

        private Color32 _current = new Color32(199, 161, 122, 255);
        private string _hexText = "#C7A17AFF";
        private string _keyLabel = "-";
        private string _targetLabel = "-";
        private float _lastResolve;
        private Unit _targetUnit;
        private bool _pickingByClick;
        private Texture2D _previewTex;
        private bool _dirtyColor;

        // Unit Creator preview (gamepad-style UC: preview bodies are plain GameObjects,
        // not Units, managed by UnitEditorManager.spawnedBase).
        private object _ucManager;
        private Transform[] _previewBases;
        private string _seededKey;
        private bool _previewDirty;

        public static SkinEditorUI Instance { get; private set; }
        private void Awake() { Instance = this; }
        private void OnDestroy() { if (ReferenceEquals(Instance, this)) Instance = null; }

        /// <summary>The color the Unit Creator preview should show right now — only while
        /// a tint is actively wanted (being edited or saved) and the UC session is alive.</summary>
        public static bool TryGetUcWantedColor(out Color32 color)
        {
            color = default;
            var inst = Instance;
            if (inst == null || !inst._previewDirty) return false;
            if (!(inst._ucManager is UnityEngine.Object ucAlive) || ucAlive == null) return false;
            color = inst._current;
            return true;
        }

        // ---- UC preview-unit registry --------------------------------------------
        // The visible UC display body can be a SCENE-ROOT GameObject carrying a Unit
        // whose blueprint key is a stale template name (e.g. the unit it was cloned
        // from). The store-driven hooks must never treat it as a real unit, or the
        // previous unit's saved color keeps being re-applied to it (the stuck green).

        private static readonly HashSet<Unit> UcPreviewUnits = new HashSet<Unit>();

        /// <summary>True while the gamepad-style UC manager is alive — any Unit the
        /// hooks see right now is a UC preview body, not a battlefield unit.</summary>
        public static bool IsUcSceneOpen
        {
            get
            {
                var inst = Instance;
                if (inst == null) return false;
                return inst._ucManager is UnityEngine.Object ucAlive && ucAlive != null;
            }
        }

        public static bool IsUcPreviewUnit(Unit unit)
        {
            if (unit == null) return false;
            try { return UcPreviewUnits.Contains(unit); }
            catch { return false; }
        }

        /// <summary>The save key the editor is currently working with ("?"/null when
        /// unknown) — used to restrict store colors during UC TEST UNIT mode.</summary>
        public static string CurrentUcKey
        {
            get
            {
                var inst = Instance;
                if (inst == null) return null;
                var k = inst._keyLabel;
                return (k == "-" || k == "?") ? null : k;
            }
        }

        /// <summary>True while the UC's TEST UNIT mode is running (the manager exposes
        /// this as a public static — the game's own flag).</summary>
        public static bool IsTestingUnitNow()
        {
            try { return Landfall.TABS.UnitEditor.UnitEditorManager.isTestingUnit; }
            catch { return false; }
        }

        /// <summary>True if `t` sits under a UnitEditorManager — i.e. it belongs to the
        /// Unit Creator, not the battlefield. Used to keep store-driven tinting off
        /// preview bodies without blanket-blocking real units when the UC is open.</summary>
        public static bool IsUnderUcManager(Transform t)
        {
            try
            {
                for (; t != null; t = t.parent)
                    if (t.GetComponent<Landfall.TABS.UnitEditor.UnitEditorManager>() != null)
                        return true;
            }
            catch { /* best effort */ }
            return false;
        }

        public static void MarkPreviewUnit(Unit unit)
        {
            if (unit == null) return;
            try { UcPreviewUnits.Add(unit); } catch { /* best effort */ }
        }

        /// <summary>(Re)discovers the Units belonging to the UC preview body: the
        /// spawnedBase GameObjects and everything under UnitSpawnPoint.</summary>
        private static void RefreshUcPreviewUnits(object mgr)
        {
            try
            {
                UcPreviewUnits.RemoveWhere(u => u == null);
                var spawnedBase = AccessTools.Field(mgr.GetType(), "spawnedBase")?.GetValue(mgr) as GameObject[];
                if (spawnedBase != null)
                    foreach (var go in spawnedBase)
                        if (go != null)
                            foreach (var u in go.GetComponentsInChildren<Unit>(true))
                                UcPreviewUnits.Add(u);
                var spawnPoint = AccessTools.Field(mgr.GetType(), "UnitSpawnPoint")?.GetValue(mgr) as Transform;
                if (spawnPoint != null)
                    foreach (var u in spawnPoint.GetComponentsInChildren<Unit>(true))
                        UcPreviewUnits.Add(u);
                // The visible display body can live at SCENE ROOT (the F11 dump showed
                // Humanoid(Clone) parented nowhere) — in the UC scene every Unit IS a
                // preview unit, so register them all. EXCEPT during TEST UNIT mode:
                // the spawned enemies are real battlefield units and must keep their
                // own (or no) saved colors.
                if (!IsTestingUnitNow())
                    foreach (var u in UnityEngine.Object.FindObjectsOfType<Unit>())
                        UcPreviewUnits.Add(u);
            }
            catch { /* best effort */ }
        }

        private void Update()
        {
            var plugin = SkinColorModPlugin.Instance;
            if (plugin == null) return;

            if (plugin.EditorHotkey.Value.IsDown())
            {
                _open = !_open;
                if (_open) ResolveTarget(force: true);
            }
            if (plugin.DumpHotkey.Value.IsDown())
                DevDumper.DumpLastUnit();
            if (plugin.DumpUiHotkey.Value.IsDown())
                DevDumper.DumpUiHierarchy();
            if (plugin.DumpSceneHotkey.Value.IsDown())
                DevDumper.DumpSceneSkinRenderers();

            if (Time.unscaledTime - _lastResolve > 0.5f)
                ResolveTarget(force: false);

            if (_pickingByClick && Input.GetMouseButtonDown(0))
                PickUnitUnderMouse();

            if (_dirtyColor)
            {
                _dirtyColor = false;
                UpdatePreviewTexture();
                ApplyToTarget();
            }

            // While in the Unit Creator, keep the preview pinned to the chosen color
            // every frame — but only when a color is actually wanted (saved for this
            // unit, or being edited). Otherwise the unit stays vanilla.
            if (_previewDirty && _previewBases != null && _previewBases.Length > 0)
                ApplyToTarget();
        }

        /// <summary>Opens/closes the editor window; called by the injected canvas button and F8.</summary>
        public void ToggleWindow()
        {
            _open = !_open;
            if (_open) ResolveTarget(force: true);
        }

        private void OnGUI()
        {
            var plugin = SkinColorModPlugin.Instance;
            if (plugin == null) return;

            if (_open)
                _winRect = GUILayout.Window(WinId, _winRect, DrawWindow, "TABS Skin Color Mod");
        }

        private void DrawWindow(int id)
        {
            var plugin = SkinColorModPlugin.Instance;
            if (plugin == null) return;

            GUILayout.Label("TARGET: " + _targetLabel, GUI.skin.box);
            GUILayout.Label("SAVE KEY: " + _keyLabel);

            // Preview + RGB sliders
            GUILayout.BeginHorizontal();
            var previewRect = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64));
            if (_previewTex != null) GUI.DrawTexture(previewRect, _previewTex);
            GUILayout.BeginVertical();
            _current.r = (byte)SliderByte("R", _current.r);
            _current.g = (byte)SliderByte("G", _current.g);
            _current.b = (byte)SliderByte("B", _current.b);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            // Hex field
            GUILayout.BeginHorizontal();
            GUILayout.Label("HEX", GUILayout.Width(40));
            var newHex = GUILayout.TextField(_hexText);
            if (newHex != _hexText)
            {
                _hexText = newHex;
                if (RgbaColor.TryParse(newHex, out var parsed))
                    SetCurrent(parsed.ToColor32(), syncHex: false);
            }
            GUILayout.EndHorizontal();

            // Swatches
            int perRow = 6;
            for (int row = 0; row < Mathf.CeilToInt(Swatches.Length / (float)perRow); row++)
            {
                GUILayout.BeginHorizontal();
                for (int i = row * perRow; i < Mathf.Min((row + 1) * perRow, Swatches.Length); i++)
                {
                    var prev = GUI.backgroundColor;
                    GUI.backgroundColor = Swatches[i];
                    if (GUILayout.Button("", GUILayout.Height(24)))
                        SetCurrent(Swatches[i], syncHex: true);
                    GUI.backgroundColor = prev;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("RANDOM"))
            {
                var c = ColorMath.HsvToRgb(UnityEngine.Random.value, 0.35f + 0.5f * UnityEngine.Random.value, 0.6f + 0.4f * UnityEngine.Random.value);
                SetCurrent(c.ToColor32(), syncHex: true);
            }
            if (GUILayout.Button("COPY HEX"))
                GUIUtility.systemCopyBuffer = _hexText;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("SAVE TO UNIT"))
            {
                if (_keyLabel != "-" && _keyLabel != "?")
                {
                    plugin.Store.SetColor(_keyLabel, _hexText);
                    plugin.SaveStore();
                    _previewDirty = true;
                    _targetLabel = _targetLabel + "  [saved]";
                }
            }
            if (GUILayout.Button("CLEAR SAVED"))
                FullReset();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("RESET ORIGINAL"))
                FullReset();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _pickingByClick = GUILayout.Toggle(_pickingByClick, "PICK UNIT BY CLICK (battlefield)");
            GUILayout.EndHorizontal();

            GUILayout.Label("F8: toggle  |  F9: dump materials  |  F11: dump scene renderers");
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        private float SliderByte(string label, byte value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(18));
            var v = GUILayout.HorizontalSlider(value / 255f, 0f, 1f);
            GUILayout.Label(value.ToString(), GUILayout.Width(32));
            GUILayout.EndHorizontal();
            var b = (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
            if (b != value)
                SetCurrent(new Color32(b, _current.g, _current.b, 255), syncHex: true);
            return b;
        }

        private void SetCurrent(Color32 c, bool syncHex)
        {
            _current = c;
            if (syncHex) _hexText = c.ToRgba().ToHex(includeAlpha: false);
            _dirtyColor = true;
            _previewDirty = true;
        }

        private void UpdatePreviewTexture()
        {
            if (_previewTex == null)
            {
                _previewTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _previewTex.hideFlags = HideFlags.HideAndDontSave;
            }
            _previewTex.SetPixels32(new[] { _current, _current, _current, _current });
            _previewTex.Apply(false);
        }

        private void ResolveTarget(bool force)
        {
            _lastResolve = Time.unscaledTime;
            var plugin = SkinColorModPlugin.Instance;
            if (plugin == null) return;

            // 1) Gamepad-style Unit Creator preview (UnitEditorManager + spawnedBase).
            try
            {
                var mgr = UnityEngine.Object.FindObjectOfType<Landfall.TABS.UnitEditor.UnitEditorManager>();
                if (mgr != null)
                {
                    bool ucJustOpened = _previewBases == null; // (re)entering the UC
                    RefreshUcPreviewUnits(mgr);
                    var loaded = AccessTools.Field(mgr.GetType(), "loadedUnit")?.GetValue(mgr) as UnitBlueprint;

                    // Tint root-wide under the manager (plus the preview manager when the
                    // display model lives there) — the visible body is not always under
                    // UnitSpawnPoint, which left stale tints visible after resets.
                    var live = new List<Transform> { mgr.transform };
                    try
                    {
                        var prevMgr = UnityEngine.Object.FindObjectOfType<Landfall.TABS.UnitPreviewManager>();
                        if (prevMgr != null) live.Add(prevMgr.transform);
                    }
                    catch { /* preview manager not present in this scene */ }

                    // The visible display body may be a SCENE-ROOT GameObject (e.g.
                    // Humanoid(Clone)) under neither tracked root — add the roots of all
                    // registered preview Units so the per-frame apply reaches it wherever
                    // the game places it.
                    foreach (var u in UcPreviewUnits)
                    {
                        if (u == null) continue;
                        var r = u.transform.root;
                        if (!live.Contains(r)) live.Add(r);
                    }

                    {
                        _ucManager = mgr;
                        string key = loaded != null ? Refs.BlueprintKey(loaded) : null;
                        if (string.IsNullOrEmpty(key) || key == "unknown")
                            key = ReadUnitNameField(mgr) ?? "?";
                        _previewBases = live.ToArray();
                        _targetUnit = null;
                        _keyLabel = key;
                        _targetLabel = "UNIT CREATOR '" + key + "'";

                        if (_seededKey != key || ucJustOpened)
                        {
                            _seededKey = key;
                            // Drop "color wanted" BEFORE restoring: the orphan sweep must
                            // see the preview body as vanilla-wanted, or it would skip it
                            // and the previous unit's tint would survive the switch.
                            _previewDirty = false;
                            // The UC reuses the same body/TeamColor components across unit
                            // edits — clear any tint left by the previously edited unit so
                            // a fresh unit starts vanilla.
                            foreach (var r in live)
                                SkinApplier.RestoreOriginalsInRoot(r, new PatternMatcher(plugin.MaterialNameFilter.Value));
                            SkinApplier.RestoreOrphanTintsInScene();
                            SeedColorForKey(plugin, key);
                        }

                        // Keep the color through preview re-spawns (the UC re-creates the
                        // body whenever anything changes) — but only when a color is wanted.
                        if (_previewDirty)
                            ApplyToTarget();
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                SkinColorModPlugin.Log?.LogWarning("[SkinColor] preview resolve failed: " + e);
            }
            _previewBases = null;
            UcPreviewUnits.Clear(); // UC not present — preview units are no longer special

            // 2) Classic Unit Creator.
            Unit ucUnit = null;
            try
            {
                var handler = UnitEditorHandler.Instance ?? UnityEngine.Object.FindObjectOfType<UnitEditorHandler>();
                if (handler != null && handler.UnitRoot != null)
                    ucUnit = handler.UnitRoot.GetComponentInChildren<Unit>(true);
            }
            catch { /* not in UC */ }

            if (ucUnit != null)
            {
                var key = ucUnit.unitBlueprint != null ? Refs.UnitKey(ucUnit) : "?";
                SetTarget(ucUnit, "UNIT CREATOR", key);
                return;
            }

            // 3) Battlefield fallback.
            var tracker = plugin.Tracker;
            var last = tracker != null ? tracker.LastUnit : null;
            if (last != null)
            {
                SetTarget(last, "BATTLEFIELD (last spawned)", Refs.UnitKey(last));
                return;
            }

            if (force)
            {
                _targetUnit = null;
                _targetLabel = "none yet (spawn a unit)";
                _keyLabel = "-";
            }
        }

        /// <summary>Called (via Harmony) right after the UC rebuilds its preview body —
        /// re-applies the current color the same frame so there is no flash.</summary>
        public void OnPreviewRespawned(object mgr)
        {
            if (_previewBases == null || _ucManager == null || !ReferenceEquals(_ucManager, mgr)) return;
            RefreshUcPreviewUnits(mgr); // fresh body may carry a fresh Unit — mark it
            if (!_previewDirty) return; // no color wanted — leave the fresh body vanilla
            try { ApplyToTarget(); } catch { /* best effort */ }
        }

        /// <summary>Reads the UC's unit-name input ("unit name" box) via reflection —
        /// the fallback save key when no blueprint is loaded yet (brand-new units).</summary>
        private static string ReadUnitNameField(object mgr)
        {
            try
            {
                var nf = AccessTools.Field(mgr.GetType(), "nameField")?.GetValue(mgr);
                if (nf == null) return null;
                var textProp = AccessTools.Property(nf.GetType(), "text");
                var text = textProp?.GetValue(nf) as string;
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            catch { /* best effort */ }
            return null;
        }

        private void SeedColorForKey(SkinColorModPlugin plugin, string key)
        {
            if (plugin.Store.TryGetColor(key, out var hex) && RgbaColor.TryParse(hex, out var saved))
            {
                SetCurrent(saved.ToColor32(), syncHex: true);
                return;
            }
            // No saved color: reset the picker to the default tan and leave the preview
            // fully vanilla. Never seed from the palette material's _Color — the game's
            // TFBG shaders don't take the body look from it, so it reads as black.
            SetCurrentSilent(DefaultColor);
        }

        /// <summary>RESET ORIGINAL / CLEAR SAVED: back to vanilla everywhere — restore the
        /// live visuals AND drop the saved color, so battle spawns revert too.</summary>
        private void FullReset()
        {
            var plugin = SkinColorModPlugin.Instance;
            if (plugin == null) return;
            var filter = new PatternMatcher(plugin.MaterialNameFilter.Value);

            if (_previewBases != null && _previewBases.Length > 0)
            {
                foreach (var b in _previewBases)
                    if (b != null)
                        SkinApplier.RestoreOriginalsInRoot(b, filter);
            }
            else if (_targetUnit != null)
            {
                SkinApplier.RestoreOriginals(_targetUnit, filter);
            }

            if (_keyLabel != "-" && _keyLabel != "?")
            {
                plugin.Store.RemoveColor(_keyLabel);
                plugin.SaveStore();
            }
            _seededKey = null;
            _dirtyColor = false;
            _previewDirty = false;

            // Clean up tinted instances that ended up on renderers outside our tracked
            // roots (display models placed elsewhere in the scene).
            SkinApplier.RestoreOrphanTintsInScene();
        }

        /// <summary>Sets the picker color without marking the preview dirty (no tinting).</summary>
        private void SetCurrentSilent(Color32 c)
        {
            _current = c;
            _hexText = c.ToRgba().ToHex(includeAlpha: false);
            _dirtyColor = false;
            _previewDirty = false;
            UpdatePreviewTexture();
        }

        private void SetTarget(Unit unit, string mode, string key)
        {
            bool changed = !ReferenceEquals(unit, _targetUnit);
            _targetUnit = unit;
            _keyLabel = key;
            _targetLabel = mode + "  '" + key + "'";

            if (changed && unit != null)
            {
                // Seed the picker from the unit's saved color; without one, reset the
                // picker to the default tan (never seed from material._Color — the game's
                // TFBG shaders don't take the body look from it, so it reads as black).
                var plugin = SkinColorModPlugin.Instance;
                if (plugin != null && plugin.Store.TryGetColor(key, out var hex) && RgbaColor.TryParse(hex, out var c))
                    SetCurrent(c.ToColor32(), syncHex: true);
                else
                    SetCurrentSilent(DefaultColor);
            }
        }

        private void ApplyToTarget()
        {
            var plugin = SkinColorModPlugin.Instance;
            if (plugin == null) return;
            var filter = new PatternMatcher(plugin.MaterialNameFilter.Value);

            // Unit Creator preview root (spawn point — body + equipment; equipment excluded).
            if (_previewBases != null && _previewBases.Length > 0)
            {
                foreach (var b in _previewBases)
                    if (b != null)
                        SkinApplier.ApplyColorToRootExcludingEquipment(b, _current, filter, out _);
                return;
            }

            if (_targetUnit == null) return;
            SkinApplier.ApplyColor(_targetUnit, _current, filter, out _);
        }

        private void PickUnitUnderMouse()
        {
            _pickingByClick = false;
            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            var hits = Physics.RaycastAll(ray, 1000f);
            Unit best = null;
            float bestDist = float.MaxValue;
            foreach (var hit in hits)
            {
                var unit = hit.collider.GetComponentInParent<Unit>();
                if (unit != null && hit.distance < bestDist)
                {
                    best = unit;
                    bestDist = hit.distance;
                }
            }
            if (best != null)
                SetTarget(best, "PICKED", Refs.UnitKey(best));
        }
    }
}
