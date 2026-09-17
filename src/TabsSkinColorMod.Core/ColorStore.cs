using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TabsSkinColorMod.Core
{
    /// <summary>Serializable state of the mod's persisted skin colors.</summary>
    public sealed class ColorStoreData
    {
        public int Version = 1;
        public string DefaultSkinColor = "#C7A17A";
        public string LastSelectedUnit = "";
        public Dictionary<string, string> Units = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Persists per-unit skin colors as JSON. The (de)serializer is hand-rolled so the
    /// core library has zero external dependencies; the file is plain JSON that can be
    /// edited by hand:
    /// {
    ///   "Version": 1,
    ///   "DefaultSkinColor": "#C7A17A",
    ///   "LastSelectedUnit": "Huskarl",
    ///   "Units": { "Huskarl": "#8C5A3CFF" }
    /// }
    /// </summary>
    public sealed class ColorStore
    {
        private ColorStoreData _data = new ColorStoreData();

        public string DefaultSkinColor
        {
            get => _data.DefaultSkinColor;
            set { if (RgbaColor.TryParse(value, out var c)) _data.DefaultSkinColor = c.ToHex(true); }
        }

        public string LastSelectedUnit
        {
            get => _data.LastSelectedUnit;
            set => _data.LastSelectedUnit = value ?? "";
        }

        public int UnitCount => _data.Units.Count;

        public bool TryGetColor(string unitId, out string hex)
        {
            hex = null;
            return !string.IsNullOrEmpty(unitId) && _data.Units.TryGetValue(unitId, out hex);
        }

        public void SetColor(string unitId, string hex)
        {
            if (string.IsNullOrEmpty(unitId)) return;
            if (!RgbaColor.TryParse(hex, out var c)) return;
            _data.Units[unitId] = c.ToHex(true);
            LastSelectedUnit = unitId;
        }

        public bool RemoveColor(string unitId)
            => !string.IsNullOrEmpty(unitId) && _data.Units.Remove(unitId);

        public string Serialize() => Json.Write(_data);

        public static ColorStore Deserialize(string json)
        {
            var store = new ColorStore();
            if (!string.IsNullOrWhiteSpace(json)) Json.Read(json, store._data);
            return store;
        }
    }

    /// <summary>Minimal JSON writer/reader for ColorStoreData (flat object, flat string map).</summary>
    internal static class Json
    {
        public static string Write(ColorStoreData d)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\n  \"Version\": ").Append(d.Version.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\n  \"DefaultSkinColor\": ").Append(Quote(d.DefaultSkinColor));
            sb.Append(",\n  \"LastSelectedUnit\": ").Append(Quote(d.LastSelectedUnit));
            sb.Append(",\n  \"Units\": {\n");
            var first = true;
            foreach (var kv in d.Units)
            {
                if (!first) sb.Append(",\n");
                sb.Append("    ").Append(Quote(kv.Key)).Append(": ").Append(Quote(kv.Value));
                first = false;
            }
            sb.Append(first ? "}\n" : "\n  }\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        public static void Read(string json, ColorStoreData d)
        {
            // Handles exactly the subset written by Write(): an object with
            // string/int values plus a flat "Units" object of string:string.
            var i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') return;
            i++;
            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == '}') { i++; break; }
                var key = ParseString(json, ref i);
                SkipWs(json, ref i);
                if (key == null || i >= json.Length || json[i] != ':') return;
                i++;
                SkipWs(json, ref i);
                if (key == "Units" && i < json.Length && json[i] == '{')
                {
                    i++;
                    while (i < json.Length)
                    {
                        SkipWs(json, ref i);
                        if (i < json.Length && json[i] == '}') { i++; break; }
                        var uk = ParseString(json, ref i);
                        SkipWs(json, ref i);
                        if (i < json.Length && json[i] == ':') i++;
                        SkipWs(json, ref i);
                        if (uk == null)
                        {
                            // Malformed key: skip the junk wholesale so parsing always makes progress.
                            SkipValue(json, ref i);
                        }
                        else
                        {
                            var uv = ParseString(json, ref i);
                            if (uv == null)
                            {
                                // Non-string value (number/bool/object) or malformed: skip it.
                                SkipValue(json, ref i);
                            }
                            else
                            {
                                d.Units[uk] = uv;
                            }
                        }
                        SkipWs(json, ref i);
                        if (i < json.Length && json[i] == ',') i++;
                    }
                }
                else if (key == "Version")
                {
                    var end = i;
                    while (end < json.Length && char.IsDigit(json[end])) end++;
                    int.TryParse(json.Substring(i, end - i), NumberStyles.None, CultureInfo.InvariantCulture, out d.Version);
                    i = end;
                }
                else if (key == "DefaultSkinColor")
                {
                    var v = ParseString(json, ref i);
                    if (v != null) d.DefaultSkinColor = v;
                }
                else if (key == "LastSelectedUnit")
                {
                    var v = ParseString(json, ref i);
                    d.LastSelectedUnit = v ?? "";
                }
                else
                {
                    SkipValue(json, ref i);
                }
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ',') i++;
            }
        }

        private static string Quote(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 32) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string ParseString(string json, ref int i)
        {
            if (i >= json.Length || json[i] != '"') return null;
            i++;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                var c = json[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && i < json.Length)
                {
                    var e = json[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 <= json.Length)
                            {
                                sb.Append((char)int.Parse(json.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static void SkipValue(string json, ref int i)
        {
            if (i >= json.Length) return;
            if (json[i] == '"') { ParseString(json, ref i); return; }
            var depth = 0;
            while (i < json.Length)
            {
                var c = json[i];
                if (c == '"') { ParseString(json, ref i); continue; }
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') { depth--; i++; if (depth <= 0) return; continue; }
                else if (depth == 0 && c == ',') return;
                i++;
            }
        }

        private static void SkipWs(string json, ref int i)
        {
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        }
    }
}
