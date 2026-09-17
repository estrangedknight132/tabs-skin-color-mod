using System.IO;
using System.Text;
using BepInEx;
using Landfall.TABS;
using UnityEngine;

namespace TabsSkinColorMod.Plugin
{
    /// <summary>
    /// Runtime diagnostics: dumps everything needed to tune skin detection for a unit
    /// (renderer paths, material names, shader names, UnitColorHandler entries).
    /// </summary>
    internal static class DevDumper
    {
        public static void DumpLastUnit()
        {
            var plugin = SkinColorModPlugin.Instance;
            if (plugin == null) return;
            var unit = plugin.Tracker != null ? plugin.Tracker.LastUnit : null;
            if (unit == null)
            {
                SkinColorModPlugin.Log.LogInfo("[SkinColor] no unit has spawned yet; nothing to dump");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("unit key: " + Refs.UnitKey(unit));
            sb.AppendLine("unit name: " + unit.name);
            sb.AppendLine("blueprint: " + (unit.unitBlueprint != null ? unit.unitBlueprint.name : "<null>"));
            sb.AppendLine();

            sb.AppendLine("== renderers ==");
            foreach (var rend in unit.GetComponentsInChildren<Renderer>(true))
            {
                if (rend == null) continue;
                sb.AppendLine("renderer: " + rend.name + "  (" + rend.GetType().Name + ")");
                var mats = rend.sharedMaterials;
                if (mats == null) continue;
                foreach (var m in mats)
                {
                    if (m == null) { sb.AppendLine("    <null material>"); continue; }
                    var shader = m.shader != null ? m.shader.name : "<null shader>";
                    var col = m.HasProperty("_Color") ? m.color.ToString("F3") : "<no _Color>";
                    sb.AppendLine("    material: '" + m.name + "'  shader: " + shader + "  _Color: " + col);
                }
            }

            sb.AppendLine();
            sb.AppendLine("== TeamColor components ==");
            foreach (var tc in unit.GetComponentsInChildren<Landfall.TABS.TeamColor>(true))
            {
                if (tc == null) continue;
                var rend = tc.GetComponent<Renderer>();
                var cur = rend != null && tc.materialID >= 0 && rend.sharedMaterials != null && tc.materialID < rend.sharedMaterials.Length ? rend.sharedMaterials[tc.materialID] : null;
                sb.AppendLine("teamColor on: " + tc.name + "  slot: " + tc.materialID);
                sb.AppendLine("    redMat: " + (tc.redMaterial != null ? tc.redMaterial.name + " _Color=" + (tc.redMaterial.HasProperty("_Color") ? tc.redMaterial.color.ToString("F3") : "?") : "<null>"));
                sb.AppendLine("    blueMat: " + (tc.blueMaterial != null ? tc.blueMaterial.name + " _Color=" + (tc.blueMaterial.HasProperty("_Color") ? tc.blueMaterial.color.ToString("F3") : "?") : "<null>"));
                sb.AppendLine("    current: " + (cur != null ? cur.name + " _Color=" + (cur.HasProperty("_Color") ? cur.color.ToString("F3") : "?") : "<null>"));
            }

            sb.AppendLine();
            sb.AppendLine("== UnitColorHandler entries ==");
            foreach (var handler in unit.GetComponentsInChildren<UnitColorHandler>(true))
            {
                sb.AppendLine("handler on: " + handler.name + "  initiated: " + Refs.IsHandlerInitiated(handler));
                if (handler.colors != null)
                    foreach (var c in handler.colors)
                        if (c != null)
                            sb.AppendLine("    colorName: '" + c.colorName + "'  color: " + c.color.ToString("F3") + "  value: " + c.currentValue);
            }

            string dir = Path.Combine(Paths.PluginPath, "TabsSkinColorMod");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "dump_" + Sanitize(Refs.UnitKey(unit)) + ".txt");
            File.WriteAllText(file, sb.ToString());
            SkinColorModPlugin.Log.LogInfo("[SkinColor] wrote unit dump: " + file);
        }

        /// <summary>Dumps every scene-loaded Canvas hierarchy (names + UI components) to a file.
        /// Used to find the team swatches when the automatic injection can't see them.</summary>
        public static void DumpUiHierarchy()
        {
            var sb = new StringBuilder();
            int canvases = 0, nodes = 0;
            foreach (var canvas in UnityEngine.Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (canvas == null || canvas.gameObject == null) continue;
                if (!canvas.gameObject.scene.isLoaded) continue;
                if (canvas.transform.parent != null) continue; // root canvases only
                canvases++;
                sb.AppendLine("CANVAS: '" + canvas.name + "'  order: " + canvas.sortingOrder + "  active: " + canvas.gameObject.activeInHierarchy);
                DumpNode(canvas.transform, 1, sb, ref nodes);
                sb.AppendLine();
            }
            sb.Insert(0, "root canvases: " + canvases + "  total nodes: " + nodes + "\n\n");

            string dir = Path.Combine(BepInEx.Paths.PluginPath, "TabsSkinColorMod");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "dump_ui_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(file, sb.ToString());
            SkinColorModPlugin.Log.LogInfo("[SkinColor] wrote UI hierarchy dump (" + canvases + " canvases, " + nodes + " nodes): " + file);
        }

        private static void DumpNode(Transform t, int depth, StringBuilder sb, ref int nodes)
        {
            if (depth > 12 || nodes > 3000) return;
            nodes++;
            var comps = new StringBuilder();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var type = c.GetType().Name;
                if (type == "Transform" || type == "RectTransform" || type == "CanvasRenderer") continue;
                comps.Append(type).Append(" ");
            }
            var pad = new string(' ', depth * 2);
            sb.AppendLine(pad + t.name + (t.gameObject.activeInHierarchy ? "" : "  [INACTIVE]") + (comps.Length > 0 ? "   <" + comps.ToString().TrimEnd() + ">" : ""));
            foreach (Transform child in t) DumpNode(child, depth + 1, sb, ref nodes);
        }

        /// <summary>Dumps every scene renderer using skin/palette materials, with its
        /// full transform path — pinpoints where display bodies actually live.</summary>
        public static void DumpSceneSkinRenderers()
        {
            var sb = new StringBuilder();
            int count = 0;
            foreach (var rend in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (rend == null) continue;
                var mats = rend.sharedMaterials;
                if (mats == null) continue;
                bool relevant = false;
                foreach (var m in mats)
                {
                    if (m == null || m.shader == null) continue;
                    if (m.name.Contains("TABS_") || m.name.Contains("SkinTinted") ||
                        m.shader.name.StartsWith("TFBG/", System.StringComparison.OrdinalIgnoreCase))
                    { relevant = true; break; }
                }
                if (!relevant) continue;
                count++;
                sb.AppendLine("renderer: " + rend.name + "  path: " + AncestorPath(rend.transform));
                var tc = rend.GetComponent<Landfall.TABS.TeamColor>();
                if (tc != null) sb.AppendLine("    has TeamColor (slot " + tc.materialID + ")");
                foreach (var m in mats)
                {
                    if (m == null) { sb.AppendLine("    <null material>"); continue; }
                    var col = m.HasProperty("_Color") ? m.color.ToString("F3") : "<no _Color>";
                    sb.AppendLine("    material: '" + m.name + "'  shader: " + (m.shader != null ? m.shader.name : "?") + "  _Color: " + col);
                }
                sb.AppendLine();
            }
            sb.Insert(0, "skin-relevant renderers in scene: " + count + "\n\n");

            string dir = Path.Combine(BepInEx.Paths.PluginPath, "TabsSkinColorMod");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "dump_scene_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(file, sb.ToString());
            SkinColorModPlugin.Log.LogInfo("[SkinColor] wrote scene skin-renderer dump (" + count + " renderers): " + file);
        }

        private static string AncestorPath(Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            while (t != null) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unit";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }
    }
}
