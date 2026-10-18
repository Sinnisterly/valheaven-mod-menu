using System;
using System.Collections.Generic;

namespace ValheimAdminMenu
{
    /// <summary>Menu locale codes and Valheim language folder map.</summary>
    internal static class LocaleCodes
    {
        internal const string Auto = "auto";
        internal const string English = "en";
        internal const string Russian = "ru";
        internal const string German = "de";
        internal const string French = "fr";
        internal const string Spanish = "es";
        internal const string PortugueseBrazil = "pt-BR";
        internal const string ChineseSimplified = "zh-CN";
        internal const string ChineseTraditional = "zh-TW";
        internal const string Japanese = "ja";

        internal static readonly string[] Selectable =
        {
            English, Russian, German, French, Spanish, PortugueseBrazil, ChineseSimplified, ChineseTraditional, Japanese
        };

        internal static readonly string[] Preferences =
        {
            Auto, English, Russian, German, French, Spanish, PortugueseBrazil, ChineseSimplified, ChineseTraditional, Japanese
        };

        private static readonly Dictionary<string, string> ValheimToCode =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "English", English },
                { "Russian", Russian },
                { "German", German },
                { "French", French },
                { "Spanish", Spanish },
                { "Portuguese_Brazilian", PortugueseBrazil },
                { "Portuguese - Brazil", PortugueseBrazil },
                { "Polish", English },
                { "Ukrainian", English },
                { "Turkish", English },
                { "Italian", English },
                { "Chinese", ChineseSimplified },
                { "Chinese_Trad", ChineseTraditional },
                { "Japanese", Japanese },
                { "Korean", English }
            };

        internal static bool IsValidPreference(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            for (int i = 0; i < Preferences.Length; i++)
                if (string.Equals(Preferences[i], code, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static string NormalizePreference(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return Auto;
            if (string.Equals(code, Auto, StringComparison.OrdinalIgnoreCase)) return Auto;
            for (int i = 0; i < Selectable.Length; i++)
                if (string.Equals(Selectable[i], code, StringComparison.OrdinalIgnoreCase)) return Selectable[i];
            return Auto;
        }

        internal static string FromValheim(string valheimLanguage)
        {
            if (string.IsNullOrWhiteSpace(valheimLanguage)) return English;
            if (ValheimToCode.TryGetValue(valheimLanguage.Trim(), out string code)) return code;
            return English;
        }

        internal static bool UsesCyrillic(string code) =>
            string.Equals(code, Russian, StringComparison.OrdinalIgnoreCase);

        internal static string DisplayName(string code)
        {
            switch (NormalizePreference(code))
            {
                case Auto: return L.T("lang.auto");
                case Russian: return L.T("lang.ru");
                case German: return L.T("lang.de");
                case French: return L.T("lang.fr");
                case Spanish: return L.T("lang.es");
                case PortugueseBrazil: return L.T("lang.pt-BR");
                case ChineseSimplified: return L.T("lang.zh-CN");
                case ChineseTraditional: return L.T("lang.zh-TW");
                case Japanese: return L.T("lang.ja");
                default: return L.T("lang.en");
            }
        }

        internal static string SettingsLabel(string preference, string effectiveCode, string valheimLanguage)
        {
            preference = NormalizePreference(preference);
            if (preference == Auto)
            {
                string game = string.IsNullOrWhiteSpace(valheimLanguage) ? "English" : valheimLanguage;
                return L.Tf("lang.auto_label", game, DisplayName(effectiveCode));
            }
            return DisplayName(preference);
        }
    }
}
