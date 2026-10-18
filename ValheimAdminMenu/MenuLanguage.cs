using System;
using HarmonyLib;

namespace ValheimAdminMenu
{
    /// <summary>Menu locale preference vs Valheim language.</summary>
    internal static class MenuLanguage
    {
        private static string preference = LocaleCodes.Auto;
        private static string effective = LocaleCodes.English;
        private static string valheimLanguage = "English";

        internal static string Preference => preference;
        internal static string EffectiveCode => effective;
        internal static string ValheimLanguage => valheimLanguage;

        internal static event Action Changed;

        internal static void ApplyPreference(string code)
        {
            preference = LocaleCodes.NormalizePreference(code);
            RefreshEffective(notify: true);
        }

        internal static void RefreshEffective(bool notify = false)
        {
            string prior = effective;
            valheimLanguage = ReadValheimLanguage();
            effective = preference == LocaleCodes.Auto
                ? LocaleCodes.FromValheim(valheimLanguage)
                : preference;
            L.SetActiveCode(effective);
            if (notify && !string.Equals(prior, effective, StringComparison.OrdinalIgnoreCase))
                Changed?.Invoke();
        }

        internal static string SettingsLabel(string storedPreference) =>
            LocaleCodes.SettingsLabel(storedPreference, effective, valheimLanguage);

        internal static void OnValheimLanguageChanged()
        {
            if (preference != LocaleCodes.Auto) return;
            RefreshEffective(notify: true);
        }

        private static string ReadValheimLanguage()
        {
            try
            {
                if (Localization.instance == null) return valheimLanguage;
                string lang = Localization.instance.GetSelectedLanguage();
                return string.IsNullOrWhiteSpace(lang) ? "English" : lang;
            }
            catch
            {
                return valheimLanguage;
            }
        }
    }

    [HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
    internal static class MenuLanguageValheimPatch
    {
        private static void Postfix() => MenuLanguage.OnValheimLanguageChanged();
    }
}
