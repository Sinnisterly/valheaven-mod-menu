using HarmonyLib;
using System.Collections.Generic;

namespace ValheimAdminMenu
{
    [HarmonyPatch]
    internal static class GateBypass
    {
        private static readonly HashSet<string> BypassKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "CryptKey",
            "$item_cryptkey",
            "Sealbreaker",
            "$item_sealbreaker"
        };

        internal static bool IsBypassKey(string name) => !string.IsNullOrEmpty(name) && BypassKeys.Contains(name);

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveItem), new[] { typeof(string), typeof(bool) })]
        [HarmonyPrefix]
        private static bool BypassKeyRequirement(string name, ref bool __result, Inventory __instance)
        {
            if (!Plugin.Instance || !Plugin.Instance.Settings.Data.bypassDungeonGates) return true;

            if (Player.m_localPlayer && Player.m_localPlayer.GetInventory() == __instance)
            {
                if (IsBypassKey(name))
                {
                    __result = true;
                    return false;
                }
            }
            return true;
        }
    }
}
