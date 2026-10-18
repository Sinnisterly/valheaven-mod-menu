using System;
using System.Collections.Generic;
using System.Text;

namespace ValheimAdminMenu
{
    /// <summary>Flat string-dict JSON for locale files.</summary>
    internal static class FlatJson
    {
        internal static Dictionary<string, string> ParseObject(string json)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) return map;
            int i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') return map;
            i++;
            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == '}') break;
                if (!TryReadString(json, ref i, out string key)) break;
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++;
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == '{')
                {
                    // Skip nested objects (e.g. "_meta").
                    SkipValue(json, ref i);
                }
                else if (TryReadString(json, ref i, out string value))
                {
                    if (!string.IsNullOrEmpty(key) && key[0] != '_')
                        map[key] = value;
                }
                else break;
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ',') { i++; continue; }
                if (i < json.Length && json[i] == '}') break;
            }
            return map;
        }

        internal static string WriteObject(IEnumerable<KeyValuePair<string, string>> pairs, IEnumerable<KeyValuePair<string, string>> meta = null)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("{");
            bool first = true;
            if (meta != null)
            {
                foreach (var m in meta)
                {
                    if (!first) sb.AppendLine(",");
                    first = false;
                    sb.Append("  ").Append(Quote("_" + m.Key)).Append(": ").Append(Quote(m.Value));
                }
            }
            foreach (var pair in pairs)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Key[0] == '_') continue;
                if (!first) sb.AppendLine(",");
                first = false;
                sb.Append("  ").Append(Quote(pair.Key)).Append(": ").Append(Quote(pair.Value ?? ""));
            }
            sb.AppendLine();
            sb.Append('}');
            return sb.ToString();
        }

        private static string Quote(string s)
        {
            if (s == null) s = "";
            var sb = new StringBuilder(s.Length + 8);
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static void SkipWs(string json, ref int i)
        {
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        }

        private static bool TryReadString(string json, ref int i, out string value)
        {
            value = null;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '"') return false;
            i++;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i++];
                if (c == '"') { value = sb.ToString(); return true; }
                if (c != '\\' || i >= json.Length) { sb.Append(c); continue; }
                char e = json[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 <= json.Length && int.TryParse(json.Substring(i, 4), System.Globalization.NumberStyles.HexNumber, null, out int code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return false;
        }

        private static void SkipValue(string json, ref int i)
        {
            SkipWs(json, ref i);
            if (i >= json.Length) return;
            if (json[i] == '{')
            {
                i++;
                int depth = 1;
                while (i < json.Length && depth > 0)
                {
                    char c = json[i++];
                    if (c == '"')
                    {
                        while (i < json.Length)
                        {
                            char s = json[i++];
                            if (s == '\\' && i < json.Length) { i++; continue; }
                            if (s == '"') break;
                        }
                    }
                    else if (c == '{') depth++;
                    else if (c == '}') depth--;
                }
                return;
            }
            if (json[i] == '"') { TryReadString(json, ref i, out _); return; }
            while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']') i++;
        }
    }
}
