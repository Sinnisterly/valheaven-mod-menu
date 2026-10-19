using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ValheimAdminMenu
{
    /// <summary>Menu strings. Embedded Locales/*.json, optional disk overrides in locales/.</summary>
    internal static class L
    {
        private static readonly object Gate = new object();
        private static bool loaded;
        private static string activeCode = LocaleCodes.English;
        private static Dictionary<string, string> english = new Dictionary<string, string>(StringComparer.Ordinal);
        private static Dictionary<string, string> active = new Dictionary<string, string>(StringComparer.Ordinal);

        internal static string ActiveCode
        {
            get { Ensure(); return activeCode; }
        }

        internal static void Ensure()
        {
            if (loaded) return;
            lock (Gate)
            {
                if (loaded) return;
                english = LoadPack(LocaleCodes.English) ?? new Dictionary<string, string>(StringComparer.Ordinal);
                ApplyCode(MenuLanguage.EffectiveCode, notify: false);
                loaded = true;
            }
        }

        internal static void SetActiveCode(string code)
        {
            lock (Gate)
            {
                loaded = true;
                if (english.Count == 0)
                    english = LoadPack(LocaleCodes.English) ?? new Dictionary<string, string>(StringComparer.Ordinal);
                ApplyCode(code, notify: false);
            }
        }

        internal static void Reload()
        {
            lock (Gate)
            {
                english = LoadPack(LocaleCodes.English) ?? new Dictionary<string, string>(StringComparer.Ordinal);
                ApplyCode(MenuLanguage.EffectiveCode, notify: false);
                loaded = true;
            }
        }

        internal static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            Ensure();
            if (active.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value)) return value;
            if (english.TryGetValue(key, out value) && !string.IsNullOrEmpty(value)) return value;
            return key;
        }

        // Older pages pass English UI text instead of a key. Map via the English pack.
        internal static string TValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            Ensure();
            foreach (var pair in english)
            {
                if (!pair.Key.StartsWith("_", StringComparison.Ordinal) &&
                    string.Equals(pair.Value, value, StringComparison.Ordinal))
                    return T(pair.Key);
            }
            return value;
        }

        internal static string Tf(string key, params object[] args)
        {
            string template = T(key);
            try { return args == null || args.Length == 0 ? template : string.Format(template, args); }
            catch (Exception caught) { AppLog.Swallow(caught); return template; }
        }

        /// <summary>Write packs into locales/ for community editing.</summary>
        internal static string ExportForCommunity()
        {
            Ensure();
            string dir = AppPaths.LocalesDir;
            Directory.CreateDirectory(dir);
            var codes = new List<string> { LocaleCodes.English };
            foreach (string code in LocaleCodes.Selectable)
                if (!codes.Contains(code)) codes.Add(code);

            int written = 0;
            foreach (string code in codes)
            {
                var pack = LoadPack(code) ?? new Dictionary<string, string>(StringComparer.Ordinal);
                // English key order; fill gaps from English so reviewers see every key.
                var ordered = new List<KeyValuePair<string, string>>();
                foreach (var pair in english.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    string text = pack.TryGetValue(pair.Key, out string v) && !string.IsNullOrEmpty(v) ? v : pair.Value;
                    ordered.Add(new KeyValuePair<string, string>(pair.Key, text));
                }
                foreach (var pair in pack.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (english.ContainsKey(pair.Key)) continue;
                    ordered.Add(pair);
                }

                var meta = new[]
                {
                    new KeyValuePair<string, string>("language", code),
                    new KeyValuePair<string, string>("name", LocaleCodes.DisplayName(code)),
                    new KeyValuePair<string, string>("version", "1"),
                    new KeyValuePair<string, string>("notes", "Edit values, keep keys. Place this file in locales/ next to valheim.exe to override. Missing keys fall back to English.")
                };
                string path = Path.Combine(dir, code + ".json");
                File.WriteAllText(path, FlatJson.WriteObject(ordered, meta), new UTF8Encoding(false));
                written++;
            }

            string readme = Path.Combine(dir, "README.txt");
            File.WriteAllText(readme,
                "Valheaven locale files\r\n" +
                "======================\r\n" +
                "1. Edit ru.json (or another language). Keep the keys on the left unchanged.\r\n" +
                "2. Send the edited .json back for review, or drop it in this locales/ folder.\r\n" +
                "3. Restart Valheim (or change Language in Settings) to reload overrides.\r\n" +
                "4. Keys starting with _ are metadata and are ignored by the menu.\r\n" +
                "5. Use {0}, {1} for placeholders. Keep them in the same order as English.\r\n",
                new UTF8Encoding(false));

            return dir + " (" + written + " language files)";
        }

        private static void ApplyCode(string code, bool notify)
        {
            activeCode = string.IsNullOrWhiteSpace(code) ? LocaleCodes.English : code;
            if (string.Equals(activeCode, LocaleCodes.English, StringComparison.OrdinalIgnoreCase))
            {
                active = new Dictionary<string, string>(english, StringComparer.Ordinal);
                return;
            }
            var pack = LoadPack(activeCode);
            active = pack ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private static Dictionary<string, string> LoadPack(string code)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            // Disk override wins (community / WIP).
            try
            {
                string disk = Path.Combine(AppPaths.LocalesDir, code + ".json");
                if (File.Exists(disk))
                    Merge(map, FlatJson.ParseObject(File.ReadAllText(disk, Encoding.UTF8)));
            }
            catch (Exception ex)
            {
                AppLog.Warning("Locale disk load failed (" + code + "): " + ex.Message);
            }

            // Embedded baseline fills gaps.
            try
            {
                string embedded = ReadEmbedded(code);
                if (!string.IsNullOrEmpty(embedded))
                    MergeMissing(map, FlatJson.ParseObject(embedded));
            }
            catch (Exception ex)
            {
                AppLog.Warning("Locale embed load failed (" + code + "): " + ex.Message);
            }
            return map;
        }

        private static void Merge(Dictionary<string, string> target, Dictionary<string, string> source)
        {
            foreach (var pair in source)
                if (!string.IsNullOrEmpty(pair.Key)) target[pair.Key] = pair.Value ?? "";
        }

        private static void MergeMissing(Dictionary<string, string> target, Dictionary<string, string> source)
        {
            foreach (var pair in source)
            {
                if (string.IsNullOrEmpty(pair.Key)) continue;
                if (!target.ContainsKey(pair.Key) || string.IsNullOrEmpty(target[pair.Key]))
                    target[pair.Key] = pair.Value ?? "";
            }
        }

        private static string ReadEmbedded(string code)
        {
            var asm = typeof(L).Assembly;
            string resource = "ValheimAdminMenu.Locales." + code + ".json";
            // Accept root Locales.name if the default namespace differs.
            using (var stream = asm.GetManifestResourceStream(resource)
                ?? asm.GetManifestResourceStream(asm.GetName().Name + ".Locales." + code + ".json"))
            {
                if (stream == null)
                {
                    string match = asm.GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith(".Locales." + code + ".json", StringComparison.OrdinalIgnoreCase)
                            || n.EndsWith("Locales." + code + ".json", StringComparison.OrdinalIgnoreCase));
                    if (match == null) return null;
                    using (var s2 = asm.GetManifestResourceStream(match))
                    {
                        if (s2 == null) return null;
                        using (var reader = new StreamReader(s2, Encoding.UTF8))
                            return reader.ReadToEnd();
                    }
                }
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }
    }
}
