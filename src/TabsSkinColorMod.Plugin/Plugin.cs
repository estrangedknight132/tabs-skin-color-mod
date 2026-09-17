using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Landfall.TABS;
using TabsSkinColorMod.Core;
using UnityEngine;

namespace TabsSkinColorMod.Plugin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("TotallyAccurateBattleSimulator.exe")]
    public class SkinColorModPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.freebuff.tabs.skincolor";
        public const string PluginName = "TABS Skin Color Mod";
        public const string PluginVersion = "0.3.8";

        internal static SkinColorModPlugin Instance;
        internal static ManualLogSource Log;

        internal ConfigEntry<KeyboardShortcut> EditorHotkey;
        internal ConfigEntry<KeyboardShortcut> DumpHotkey;
        internal ConfigEntry<KeyboardShortcut> DumpUiHotkey;
        internal ConfigEntry<KeyboardShortcut> DumpSceneHotkey;
        internal ConfigEntry<bool> UcButtonEnabled;
        internal ConfigEntry<bool> EnableInBattle;
        internal ConfigEntry<bool> EnableInUnitCreator;
        internal ConfigEntry<bool> StickyReapply;
        internal ConfigEntry<bool> LogSkinMaterials;
        internal ConfigEntry<string> MaterialNameFilter;

        internal UnitTracker Tracker { get; private set; }
        internal SkinEditorUI Editor { get; private set; }

        internal ColorStore Store { get; private set; }

        internal string StorePath => System.IO.Path.Combine(Paths.PluginPath, "TabsSkinColorMod", "skin_colors.json");

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            EditorHotkey = Config.Bind("General", "EditorHotkey", new KeyboardShortcut(KeyCode.F8),
                "Toggle the skin color editor window.");
            DumpHotkey = Config.Bind("General", "DumpHotkey", new KeyboardShortcut(KeyCode.F9),
                "Dump debug info (materials, shader names) for the last spawned unit.");
            DumpUiHotkey = Config.Bind("General", "DumpUiHotkey", new KeyboardShortcut(KeyCode.F10),
                "Dump the UI hierarchy (canvases, toggles, buttons) for debugging the Unit Creator button.");
            DumpSceneHotkey = Config.Bind("General", "DumpSceneHotkey", new KeyboardShortcut(KeyCode.F11),
                "Dump every scene renderer using skin/palette materials (with its transform path) — finds where display bodies live.");
            UcButtonEnabled = Config.Bind("General", "UcButtonEnabled", true,
                "Inject the SKIN COLOR button into the Unit Creator's UI canvas.");
            EnableInBattle = Config.Bind("General", "EnableInBattle", true,
                "Apply saved skin colors to units spawned in battles.");
            EnableInUnitCreator = Config.Bind("General", "EnableInUnitCreator", true,
                "Apply saved skin colors to the Unit Creator preview unit.");
            StickyReapply = Config.Bind("General", "StickyReapply", true,
                "Re-apply the custom color every second if the game overwrites it (skips dead units).");
            LogSkinMaterials = Config.Bind("Debug", "LogSkinMaterials", false,
                "Log material names of skin-tinted units (useful for tuning MaterialNameFilter).");
            MaterialNameFilter = Config.Bind("Colors", "MaterialNameFilter", "",
                "Optional comma-separated material name patterns for skin, e.g. 'skin, body, -cloth'. Empty = auto-detect (unit base prefab renderers, TFBG shaders).");

            Store = LoadStore();

            try
            {
                _harmony = new Harmony(PluginGuid);
                _harmony.Patch(
                    AccessTools.Method(typeof(Unit), "Awake"),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(UnitHooks), nameof(UnitHooks.UnitAwakePostfix))));
                Log.LogInfo("Harmony: hooked Landfall.TABS.Unit.Awake");
            }
            catch (Exception e)
            {
                Log.LogError("Failed to hook Unit.Awake: " + e);
            }

            // Make every game-driven team re-tint (spawn, pooled re-use, team switch,
            // UC preview refresh) resolve to our tinted material when one is registered.
            try
            {
                _harmony.Patch(
                    AccessTools.Method(typeof(TeamColor), "GetTeamMaterial"),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(TeamHooks), nameof(TeamHooks.GetTeamMaterialPostfix))));
                Log.LogInfo("Harmony: hooked Landfall.TABS.TeamColor.GetTeamMaterial");
            }
            catch (Exception e)
            {
                Log.LogError("Failed to hook TeamColor.GetTeamMaterial: " + e);
            }

            // Re-apply saved colors when the game (re)assigns a team color: this is the
            // only per-spawn hook for pooled battle units (Unit.Awake fires once per
            // pool instance only), and it also catches UC preview rebuilds.
            try
            {
                _harmony.Patch(
                    AccessTools.Method(typeof(TeamColor), "SetTeamColor"),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(TeamHooks), nameof(TeamHooks.SetTeamColorPostfix))));
                Log.LogInfo("Harmony: hooked Landfall.TABS.TeamColor.SetTeamColor");
            }
            catch (Exception e)
            {
                Log.LogError("Failed to hook TeamColor.SetTeamColor: " + e);
            }

            // UC preview body rebuild: re-tint the fresh GameObjects the same frame so
            // there is no flash of the original team color.
            try
            {
                var respawn = AccessTools.Method(AccessTools.TypeByName("Landfall.TABS.UnitEditor.UnitEditorManager"), "RespawnUnit");
                if (respawn != null)
                {
                    _harmony.Patch(respawn,
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(TeamHooks), nameof(TeamHooks.RespawnUnitPostfix))));
                    Log.LogInfo("Harmony: hooked UnitEditorManager.RespawnUnit");
                }
            }
            catch (Exception e)
            {
                Log.LogError("Failed to hook UnitEditorManager.RespawnUnit: " + e);
            }

            // After every game-driven team re-tint, force the tinted materials back into
            // the renderer slots — kills any residual flicker regardless of the caller.
            try
            {
                foreach (var m in AccessTools.GetDeclaredMethods(typeof(TeamColor)))
                {
                    if (m.Name != "UpdateTeamColors") continue;
                    _harmony.Patch(m,
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(TeamHooks), nameof(TeamHooks.UpdateTeamColorsPostfix))));
                }
                Log.LogInfo("Harmony: hooked Landfall.TABS.TeamColor.UpdateTeamColors");
            }
            catch (Exception e)
            {
                Log.LogError("Failed to hook TeamColor.UpdateTeamColors: " + e);
            }

            var go = new GameObject("TabsSkinColorMod");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            Tracker = go.AddComponent<UnitTracker>();
            Editor = go.AddComponent<SkinEditorUI>();
            go.AddComponent<UcButtonInjector>();

            Log.LogInfo(PluginName + " " + PluginVersion + " loaded. Store: " + StorePath);
        }

        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { /* ignore */ }
        }

        internal void SaveStore()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StorePath));
                System.IO.File.WriteAllText(StorePath, Store.Serialize());
            }
            catch (Exception e)
            {
                Log.LogError("Failed to save color store: " + e);
            }
        }

        private ColorStore LoadStore()
        {
            try
            {
                if (System.IO.File.Exists(StorePath))
                    return ColorStore.Deserialize(System.IO.File.ReadAllText(StorePath));
            }
            catch (Exception e)
            {
                Log.LogWarning("Failed to load color store: " + e);
            }
            return new ColorStore();
        }
    }

    internal static class UnitHooks
    {
        public static void UnitAwakePostfix(Unit __instance)
        {
            try
            {
                var tracker = SkinColorModPlugin.Instance != null ? SkinColorModPlugin.Instance.Tracker : null;
                tracker?.QueueUnit(__instance);
            }
            catch (Exception e)
            {
                SkinColorModPlugin.Log?.LogWarning("UnitAwakePostfix failed: " + e);
            }
        }
    }

    internal static class TeamHooks
    {
        public static void GetTeamMaterialPostfix(TeamColor __instance, Material __result)
        {
            try
            {
                if (__result == null) return;
                // UC preview bodies carry Units with stale template keys — never tint
                // them from the store; the editor's own wanted-color path owns them.
                var unit = __instance.GetComponentInParent<Unit>();
                if (SkinEditorUI.IsUcPreviewUnit(unit)) return;
                if (unit != null && !SkinApplier.StoreColorAllowedForKey(Refs.UnitKey(unit))) return;
                if (SkinApplier.TryGetTinted(__instance, __result, out var tinted))
                    __result = tinted;
            }
            catch { /* never break the game's color pipeline */ }
        }

        public static void SetTeamColorPostfix(TeamColor __instance)
        {
            try
            {
                if (__instance == null) return;
                var unit = __instance.GetComponentInParent<Unit>();
                if (unit == null) return;
                if (SkinEditorUI.IsUcPreviewUnit(unit) || SkinEditorUI.IsUnderUcManager(unit.transform))
                {
                    SkinEditorUI.MarkPreviewUnit(unit);
                    return; // preview body — the editor owns its color, not the store
                }
                var tracker = SkinColorModPlugin.Instance?.Tracker;
                if (tracker == null) return;
                tracker.ApplySavedNow(unit); // same frame — no original-color flash
                tracker.QueueUnit(unit);     // plus the delayed sticky-tracking pass
            }
            catch { }
        }

        public static void RespawnUnitPostfix(object __instance)
        {
            try
            {
                SkinColorModPlugin.Instance?.Editor?.OnPreviewRespawned(__instance);
            }
            catch { }
        }

        public static void UpdateTeamColorsPostfix(TeamColor __instance)
        {
            try
            {
                SkinApplier.ReapplyTints(__instance);
            }
            catch { }
        }
    }
}
