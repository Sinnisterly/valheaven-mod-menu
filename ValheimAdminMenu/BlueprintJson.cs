using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ValheimAdminMenu
{
    /// <summary>Hand-rolled blueprint JSON. JsonUtility dropped pieces[] on .vbp export.</summary>
    internal static class BlueprintJson
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal static string WriteLibrary(BlueprintLibrary library)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\n  \"version\": ").Append(library == null ? 1 : Math.Max(1, library.version));
            sb.Append(",\n  \"blueprints\": [");
            var list = library?.blueprints;
            bool first = true;
            if (list != null)
            {
                foreach (var bp in list)
                {
                    if (bp == null || string.IsNullOrWhiteSpace(bp.name)) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('\n');
                    WriteRecordBody(sb, bp, "    ");
                }
            }
            sb.Append("\n  ]\n}\n");
            return sb.ToString();
        }

        internal static string WriteRecord(BlueprintRecord record)
        {
            var sb = new StringBuilder(1024);
            WriteRecordBody(sb, record, "");
            sb.Append('\n');
            return sb.ToString();
        }

        private static void WriteRecordBody(StringBuilder sb, BlueprintRecord record, string indent)
        {
            var pieces = record?.pieces ?? Array.Empty<BlueprintPieceDto>();
            string name = record?.name ?? "";
            string saved = record?.savedUtc ?? "";
            sb.Append(indent).Append("{\n");
            sb.Append(indent).Append("  \"name\": ").Append(Quote(name)).Append(",\n");
            sb.Append(indent).Append("  \"savedUtc\": ").Append(Quote(saved)).Append(",\n");
            sb.Append(indent).Append("  \"pieceCount\": ").Append(pieces.Length).Append(",\n");
            sb.Append(indent).Append("  \"pieces\": [");
            for (int i = 0; i < pieces.Length; i++)
            {
                var p = pieces[i] ?? new BlueprintPieceDto();
                if (i > 0) sb.Append(',');
                sb.Append('\n').Append(indent).Append("    {");
                sb.Append("\"prefab\":").Append(Quote(p.prefab ?? ""));
                sb.Append(",\"x\":").Append(F(p.x));
                sb.Append(",\"y\":").Append(F(p.y));
                sb.Append(",\"z\":").Append(F(p.z));
                sb.Append(",\"qx\":").Append(F(p.qx));
                sb.Append(",\"qy\":").Append(F(p.qy));
                sb.Append(",\"qz\":").Append(F(p.qz));
                sb.Append(",\"qw\":").Append(F(p.qw == 0 && p.qx == 0 && p.qy == 0 && p.qz == 0 ? 1f : p.qw));
                sb.Append(",\"sx\":").Append(F(p.sx == 0 ? 1f : p.sx));
                sb.Append(",\"sy\":").Append(F(p.sy == 0 ? 1f : p.sy));
                sb.Append(",\"sz\":").Append(F(p.sz == 0 ? 1f : p.sz));
                sb.Append('}');
            }
            if (pieces.Length > 0) sb.Append('\n').Append(indent).Append("  ");
            sb.Append(']');
            var terrain = record?.terrain;
            if (terrain != null && terrain.heights != null && terrain.heights.Length > 0 && terrain.width > 1)
            {
                sb.Append(",\n").Append(indent).Append("  \"terrain\": {\n");
                sb.Append(indent).Append("    \"version\": ").Append(terrain.version).Append(",\n");
                sb.Append(indent).Append("    \"width\": ").Append(terrain.width).Append(",\n");
                sb.Append(indent).Append("    \"spacing\": ").Append(F(terrain.spacing)).Append(",\n");
                sb.Append(indent).Append("    \"radius\": ").Append(F(terrain.radius)).Append(",\n");
                sb.Append(indent).Append("    \"originX\": ").Append(F(terrain.originX)).Append(",\n");
                sb.Append(indent).Append("    \"originZ\": ").Append(F(terrain.originZ)).Append(",\n");
                sb.Append(indent).Append("    \"heights\": [");
                for (int i = 0; i < terrain.heights.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (i % 16 == 0) sb.Append('\n').Append(indent).Append("      ");
                    sb.Append(F(terrain.heights[i]));
                }
                sb.Append('\n').Append(indent).Append("    ]\n");
                sb.Append(indent).Append("  }");
            }
            sb.Append('\n').Append(indent).Append('}');
        }

        internal static BlueprintLibrary ParseLibrary(string json)
        {
            var lib = new BlueprintLibrary { version = 1, blueprints = new List<BlueprintRecord>() };
            if (string.IsNullOrWhiteSpace(json)) return lib;
            var versionMatch = Regex.Match(json, "\"version\"\\s*:\\s*(\\d+)");
            if (versionMatch.Success && int.TryParse(versionMatch.Groups[1].Value, out int ver))
                lib.version = Math.Max(1, ver);

            // Prefer blueprints array; else treat whole file as one record.
            int arr = IndexOfKey(json, "blueprints");
            if (arr >= 0)
            {
                int start = json.IndexOf('[', arr);
                int end = FindMatchingBracket(json, start);
                if (start >= 0 && end > start)
                {
                    foreach (var obj in SplitTopObjects(json.Substring(start + 1, end - start - 1)))
                    {
                        var rec = ParseRecordObject(obj);
                        if (rec != null) lib.blueprints.Add(rec);
                    }
                    return lib;
                }
            }

            var single = ParseRecord(json);
            if (single != null && !string.IsNullOrWhiteSpace(single.name))
                lib.blueprints.Add(single);
            return lib;
        }

        internal static BlueprintRecord ParseRecord(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            int start = json.IndexOf('{');
            int end = FindMatchingBracket(json, start);
            if (start < 0 || end <= start) return null;
            return ParseRecordObject(json.Substring(start, end - start + 1));
        }

        private static BlueprintRecord ParseRecordObject(string obj)
        {
            if (string.IsNullOrWhiteSpace(obj)) return null;
            var record = new BlueprintRecord
            {
                name = ReadString(obj, "name") ?? "",
                savedUtc = ReadString(obj, "savedUtc") ?? "",
                pieces = Array.Empty<BlueprintPieceDto>()
            };
            int piecesKey = IndexOfKey(obj, "pieces");
            if (piecesKey >= 0)
            {
                int start = obj.IndexOf('[', piecesKey);
                int end = FindMatchingBracket(obj, start);
                if (start >= 0 && end > start)
                {
                    var list = new List<BlueprintPieceDto>();
                    foreach (var pieceObj in SplitTopObjects(obj.Substring(start + 1, end - start - 1)))
                    {
                        var dto = ParsePiece(pieceObj);
                        if (dto != null) list.Add(dto);
                    }
                    record.pieces = list.ToArray();
                }
            }
            record.pieceCount = record.pieces.Length;
            record.terrain = ParseTerrain(obj);
            if (string.IsNullOrWhiteSpace(record.name) && record.pieceCount == 0) return null;
            return record;
        }

        private static BlueprintTerrainDto ParseTerrain(string obj)
        {
            int key = IndexOfKey(obj, "terrain");
            if (key < 0) return null;
            int start = obj.IndexOf('{', key);
            int end = FindMatchingBracket(obj, start);
            if (start < 0 || end <= start) return null;
            string body = obj.Substring(start, end - start + 1);
            int width = (int)ReadFloat(body, "width");
            if (width < 2) return null;
            int heightsKey = IndexOfKey(body, "heights");
            if (heightsKey < 0) return null;
            int arrStart = body.IndexOf('[', heightsKey);
            int arrEnd = FindMatchingBracket(body, arrStart);
            if (arrStart < 0 || arrEnd <= arrStart) return null;
            var heights = new List<float>();
            string arr = body.Substring(arrStart + 1, arrEnd - arrStart - 1);
            int i = 0;
            while (i < arr.Length)
            {
                while (i < arr.Length && (char.IsWhiteSpace(arr[i]) || arr[i] == ',')) i++;
                if (i >= arr.Length) break;
                int begin = i;
                if (arr[i] == '-' || arr[i] == '+') i++;
                while (i < arr.Length && (char.IsDigit(arr[i]) || arr[i] == '.' || arr[i] == 'e' || arr[i] == 'E' || arr[i] == '+' || arr[i] == '-'))
                    i++;
                if (i > begin && float.TryParse(arr.Substring(begin, i - begin), NumberStyles.Float, Inv, out float h))
                    heights.Add(h);
            }
            if (heights.Count != width * width) return null;
            return new BlueprintTerrainDto
            {
                version = (int)ReadFloat(body, "version", 1f),
                width = width,
                spacing = ReadFloat(body, "spacing", 1f),
                radius = ReadFloat(body, "radius"),
                originX = ReadFloat(body, "originX"),
                originZ = ReadFloat(body, "originZ"),
                heights = heights.ToArray()
            };
        }

        private static BlueprintPieceDto ParsePiece(string obj)
        {
            if (string.IsNullOrWhiteSpace(obj)) return null;
            string prefab = ReadString(obj, "prefab");
            if (string.IsNullOrWhiteSpace(prefab)) return null;
            return new BlueprintPieceDto
            {
                prefab = prefab,
                x = ReadFloat(obj, "x"),
                y = ReadFloat(obj, "y"),
                z = ReadFloat(obj, "z"),
                qx = ReadFloat(obj, "qx"),
                qy = ReadFloat(obj, "qy"),
                qz = ReadFloat(obj, "qz"),
                qw = ReadFloat(obj, "qw", 1f),
                sx = ReadFloat(obj, "sx", 1f),
                sy = ReadFloat(obj, "sy", 1f),
                sz = ReadFloat(obj, "sz", 1f)
            };
        }

        private static string ReadString(string json, string key)
        {
            int at = IndexOfKey(json, key);
            if (at < 0) return null;
            int colon = json.IndexOf(':', at);
            if (colon < 0) return null;
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '"') return null;
            i++;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i++];
                if (c == '\\' && i < json.Length)
                {
                    char n = json[i++];
                    switch (n)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 3 < json.Length &&
                                int.TryParse(json.Substring(i, 4), NumberStyles.HexNumber, Inv, out int code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(n); break;
                    }
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static float ReadFloat(string json, string key, float fallback = 0f)
        {
            int at = IndexOfKey(json, key);
            if (at < 0) return fallback;
            int colon = json.IndexOf(':', at);
            if (colon < 0) return fallback;
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            int start = i;
            if (i < json.Length && (json[i] == '-' || json[i] == '+')) i++;
            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '.' || json[i] == 'e' || json[i] == 'E' || json[i] == '+' || json[i] == '-'))
                i++;
            if (i <= start) return fallback;
            return float.TryParse(json.Substring(start, i - start), NumberStyles.Float, Inv, out float v) ? v : fallback;
        }

        private static int IndexOfKey(string json, string key)
        {
            string token = "\"" + key + "\"";
            int from = 0;
            while (from < json.Length)
            {
                int at = json.IndexOf(token, from, StringComparison.Ordinal);
                if (at < 0) return -1;
                // Must be a key (optional space then ':'), not a string value.
                int after = at + token.Length;
                while (after < json.Length && char.IsWhiteSpace(json[after])) after++;
                if (after < json.Length && json[after] == ':') return at;
                from = at + 1;
            }
            return -1;
        }

        private static int FindMatchingBracket(string json, int open)
        {
            if (open < 0 || open >= json.Length) return -1;
            char openCh = json[open];
            char closeCh = openCh == '{' ? '}' : openCh == '[' ? ']' : '\0';
            if (closeCh == '\0') return -1;
            int depth = 0;
            bool inString = false;
            for (int i = open; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == openCh) depth++;
                else if (c == closeCh)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static IEnumerable<string> SplitTopObjects(string body)
        {
            var list = new List<string>();
            int i = 0;
            while (i < body.Length)
            {
                while (i < body.Length && (char.IsWhiteSpace(body[i]) || body[i] == ',')) i++;
                if (i >= body.Length) break;
                if (body[i] != '{') break;
                int end = FindMatchingBracket(body, i);
                if (end < 0) break;
                list.Add(body.Substring(i, end - i + 1));
                i = end + 1;
            }
            return list;
        }

        private static string Quote(string value)
        {
            if (value == null) value = "";
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.AppendFormat(Inv, "\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string F(float v) => v.ToString("R", Inv);
    }
}
