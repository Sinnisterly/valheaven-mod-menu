using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

namespace ValheimAdminMenu
{
    /// <summary>Menu TMP fonts plus script fallbacks (Cyrillic / CJK).</summary>
    internal static class MenuFonts
    {
        internal sealed class Resolved
        {
            internal TMP_FontAsset Body;
            internal TMP_FontAsset Display;
            internal TMP_FontAsset ScriptFallback;
        }

        internal static Resolved Resolve(string localeCode)
        {
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Where(f => f).ToArray();
            var body = Pick(fonts, "Valheim-AveriaSansLibre", "AveriaSans")
                ?? TMP_Settings.defaultFontAsset
                ?? fonts.OrderBy(f => f.name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (!body) throw new InvalidOperationException("Menu fonts are not ready. Try opening the menu again.");
            var display = Pick(fonts, "Valheim-Norse", "Norse") ?? body;
            var scriptFallback = PickScriptFallback(fonts, body, display, localeCode);
            if (scriptFallback == null && (string.Equals(localeCode, "zh-CN", StringComparison.OrdinalIgnoreCase) || string.Equals(localeCode, "zh-TW", StringComparison.OrdinalIgnoreCase)
                || string.Equals(localeCode, "ja", StringComparison.OrdinalIgnoreCase)))
                scriptFallback = LoadValheimCjkFont(fonts, body, display);
            if (scriptFallback != null)
            {
                AttachFallback(body, scriptFallback);
                AttachFallback(display, scriptFallback);
            }
            return new Resolved { Body = body, Display = display, ScriptFallback = scriptFallback };
        }

        private static TMP_FontAsset Pick(TMP_FontAsset[] fonts, params string[] nameHints)
        {
            for (int h = 0; h < nameHints.Length; h++)
            {
                string hint = nameHints[h];
                var exact = fonts.FirstOrDefault(f => f && string.Equals(f.name, hint, StringComparison.OrdinalIgnoreCase));
                if (exact) return exact;
            }
            for (int h = 0; h < nameHints.Length; h++)
            {
                string hint = nameHints[h];
                var partial = fonts.Where(f => f && f.name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(f => f.name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (partial) return partial;
            }
            return null;
        }

        private static TMP_FontAsset PickScriptFallback(TMP_FontAsset[] fonts, TMP_FontAsset body, TMP_FontAsset display, string localeCode)
        {
            var skip = new HashSet<TMP_FontAsset>();
            if (body) skip.Add(body);
            if (display) skip.Add(display);
            string[] preferred = string.Equals(localeCode, "zh-CN", StringComparison.OrdinalIgnoreCase) || string.Equals(localeCode, "zh-TW", StringComparison.OrdinalIgnoreCase)
                ? new[] { "NotoSansCJK", "SourceHanSans", "MicrosoftYaHei", "SimHei", "Noto", "Segoe" }
                : string.Equals(localeCode, "ja", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "NotoSansCJK", "SourceHanSans", "YuGothic", "Meiryo", "Noto", "Segoe" }
                    : new[] { "LiberationSans", "Arial", "Noto", "Segoe", "Roboto", "OpenSans", "Sans" };
            for (int i = 0; i < preferred.Length; i++)
            {
                var match = fonts.FirstOrDefault(f => f && !skip.Contains(f)
                    && f.name.IndexOf(preferred[i], StringComparison.OrdinalIgnoreCase) >= 0
                    && f.name.IndexOf("Valheim", StringComparison.OrdinalIgnoreCase) < 0
                    && f.name.IndexOf("Norse", StringComparison.OrdinalIgnoreCase) < 0
                    && f.name.IndexOf("Averia", StringComparison.OrdinalIgnoreCase) < 0);
                if (match) return match;
            }
            if (TMP_Settings.defaultFontAsset && !skip.Contains(TMP_Settings.defaultFontAsset))
                return TMP_Settings.defaultFontAsset;
            return fonts.FirstOrDefault(f => f && !skip.Contains(f)
                && f.name.IndexOf("Valheim", StringComparison.OrdinalIgnoreCase) < 0);
        }

        private static void AttachFallback(TMP_FontAsset primary, TMP_FontAsset fallback)
        {
            if (!primary || !fallback || primary == fallback) return;
            if (primary.fallbackFontAssetTable == null) return;
            if (!primary.fallbackFontAssetTable.Contains(fallback))
                primary.fallbackFontAssetTable.Add(fallback);
        }

        private static TMP_FontAsset LoadValheimCjkFont(TMP_FontAsset[] fonts, TMP_FontAsset body, TMP_FontAsset display)
        {
            try
            {
                string bundlePath = Path.Combine(Application.dataPath, "StreamingAssets", "tmp_fonts");
                if (!File.Exists(bundlePath)) return null;
                var bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null) return null;
                var candidates = bundle.LoadAllAssets<TMP_FontAsset>();
                var match = candidates.FirstOrDefault(f => f && f.name.IndexOf("Prstartk", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? candidates.FirstOrDefault(f => f && f.name.IndexOf("CJK", StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) AppLog.Info("Loaded Valheim CJK font asset: " + match.name);
                else AppLog.Warning("Valheim tmp_fonts bundle loaded, but no CJK TMP font asset was found.");
                return match;
            }
            catch (Exception ex)
            {
                AppLog.Warning("Valheim CJK font bundle load failed: " + ex.Message);
                return null;
            }
        }

    }
}
