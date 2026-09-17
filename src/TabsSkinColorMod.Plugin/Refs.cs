using System;
using System.Reflection;
using HarmonyLib;
using Landfall.TABS;
using TabsSkinColorMod.Core;
using UnityEngine;

namespace TabsSkinColorMod.Plugin
{
    /// <summary>Conversion helpers between the engine-free Core color and Unity colors.</summary>
    internal static class ColorConvertExtensions
    {
        public static Color32 ToColor32(this RgbaColor c) => new Color32(c.R, c.G, c.B, c.A);

        public static RgbaColor ToRgba(this Color32 c) => new RgbaColor(c.r, c.g, c.b, c.a);
    }
    /// <summary>Cached reflection access to private game members (verified via offline metadata dump).</summary>
    internal static class Refs
    {
        private static readonly FieldInfo BpEntity = AccessTools.Field(typeof(UnitBlueprint), "m_entity");
        private static readonly FieldInfo BpUnitBase = AccessTools.Field(typeof(UnitBlueprint), "m_unitBase");
        private static readonly FieldInfo UchRends = AccessTools.Field(typeof(UnitColorHandler), "rends");
        private static readonly FieldInfo UchRendColors = AccessTools.Field(typeof(UnitColorHandler), "rendColors");
        private static readonly FieldInfo UchInitiated = AccessTools.Field(typeof(UnitColorHandler), "initiated");
        private static readonly FieldInfo ItemGameObjects = AccessTools.Field(typeof(CharacterItem), "m_gameObjects");

        /// <summary>The unit base prefab of a blueprint (null if unavailable).</summary>
        public static GameObject UnitBasePrefab(UnitBlueprint bp) => BpUnitBase?.GetValue(bp) as GameObject;

        /// <summary>Stable display key for a unit: entity name, falling back to the asset name / GUID.</summary>
        public static string UnitKey(Unit unit)
        {
            if (unit == null) return "unknown";
            var bp = unit.unitBlueprint;
            if (bp == null) return "unknown";

            try
            {
                var entity = BpEntity?.GetValue(bp);
                if (entity != null)
                {
                    var nameProp = AccessTools.Property(entity.GetType(), "Name");
                    var name = nameProp?.GetValue(entity) as string;
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch (Exception) { /* fall through */ }

            if (!string.IsNullOrEmpty(bp.name)) return bp.name.Replace("(Clone)", "").Trim();
            return "unknown";
        }

        /// <summary>Stable display key for a blueprint (Unit Creator preview has no Unit).</summary>
        public static string BlueprintKey(UnitBlueprint bp)
        {
            if (bp == null) return "unknown";
            try
            {
                var entity = BpEntity?.GetValue(bp);
                if (entity != null)
                {
                    var nameProp = AccessTools.Property(entity.GetType(), "Name");
                    var name = nameProp?.GetValue(entity) as string;
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch (Exception) { /* fall through */ }
            if (!string.IsNullOrEmpty(bp.name)) return bp.name.Replace("(Clone)", "").Trim();
            return "unknown";
        }

        public static Renderer[] GetHandlerRenderers(UnitColorHandler handler)
            => UchRends?.GetValue(handler) as Renderer[];

        public static Color[][] GetHandlerRendColors(UnitColorHandler handler)
            => UchRendColors?.GetValue(handler) as Color[][];

        public static bool IsHandlerInitiated(UnitColorHandler handler)
        {
            if (handler == null) return false;
            var v = UchInitiated?.GetValue(handler);
            return v is bool b && b;
        }

        /// <summary>All GameObjects belonging to an equipped item — includes pieces the
        /// game bone-attached OUTSIDE the item's transform (clothes panels, straps).</summary>
        public static GameObject[] GetItemGameObjects(CharacterItem item)
            => ItemGameObjects?.GetValue(item) as GameObject[];
    }
}
