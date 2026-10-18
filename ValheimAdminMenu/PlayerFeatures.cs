using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimAdminMenu
{
    internal sealed class PlayerFeatures
    {
        private readonly Plugin app;
        private Player player;
        private bool originalGod, originalGhost, originalFly, originalCost;
        private float originalDistance;
        private bool touchedGod, touchedGhost, touchedFly, touchedCost, touchedDistance;
        internal bool Stamina, Weight, FastCraft, PortalItems;
        internal bool DistanceEnabled;
        internal float Distance = 10;
        internal bool InfiniteStamina => Stamina;
        internal PlayerFeatures(Plugin app) { this.app = app; }
        internal void Tick(Player current)
        {
            if (!current) return;
            if (player != current)
            {
                Restore(); player = current;
                originalGod = current.InGodMode(); originalGhost = current.InGhostMode();
                originalFly = current.IsDebugFlying(); originalCost = current.PlacementCostDisabled;
                originalDistance = current.m_maxInteractDistance;
            }
            if (InfiniteStamina && !current.IsDead()) current.AddStamina(current.GetMaxStamina());
            if (DistanceEnabled) { touchedDistance = true; current.m_maxInteractDistance = Mathf.Clamp(Distance, 1, 50); }
            else if (touchedDistance) { current.m_maxInteractDistance = originalDistance; touchedDistance = false; }
        }
        internal void God(bool value) { app.RequirePlayer().SetGodMode(value); touchedGod = true; }
        internal void Ghost(bool value) { app.RequirePlayer().SetGhostMode(value); touchedGhost = true; }
        internal void Fly(bool value)
        {
            var p = app.RequirePlayer();
            if (p.IsDebugFlying() != value) p.ToggleDebugFly();
            touchedFly = true;
        }
        internal void NoCost(bool value) { app.RequirePlayer().SetNoPlacementCost(value); touchedCost = true; }
        internal void Restore()
        {
            if (player)
            {
                if (touchedGod) player.SetGodMode(originalGod);
                if (touchedGhost) player.SetGhostMode(originalGhost);
                if (touchedCost) player.SetNoPlacementCost(originalCost);
                if (touchedDistance) player.m_maxInteractDistance = originalDistance;
                var view = player.GetComponent<ZNetView>();
                if (touchedFly && view && view.IsValid() && view.IsOwner() && player.IsDebugFlying() != originalFly) player.ToggleDebugFly();
            }
            player = null;
            touchedGod = touchedGhost = touchedFly = touchedCost = touchedDistance = false;
            Stamina = Weight = FastCraft = PortalItems = DistanceEnabled = false;
        }
        internal void Heal() { var p = app.RequirePlayer(); p.Heal(p.GetMaxHealth()); app.Report("Local player healed."); }
        internal void Power()
        {
            var p = app.RequirePlayer(); string power = p.GetGuardianPowerName();
            if (string.IsNullOrEmpty(power)) throw new InvalidOperationException("Equip a Guardian Power first.");
            var effect = ObjectDB.instance.GetStatusEffect(power.GetStableHashCode());
            if (!effect) throw new InvalidOperationException("The equipped Guardian Power has no status effect.");
            p.GetSEMan().AddStatusEffect(effect.NameHash(), true);
            app.Report("Equipped Guardian Power applied to the local player.");
        }
        internal void SetSkill(Skills.SkillType skill, float level)
        {
            if (skill == Skills.SkillType.None || skill == Skills.SkillType.All || !Enum.IsDefined(typeof(Skills.SkillType), skill)) throw new ArgumentException("Select an individual skill.");
            var skills = app.RequirePlayer().GetSkills();
            if (!Rules.Finite(level) || level<0 || level>100) throw new ArgumentException("Skill level must be between 0 and 100.");
            // GetSkillLevel creates the skill if missing; don't use its return (buffs + rounding).
            skills.GetSkillLevel(skill);
            foreach (var entry in skills.GetSkillList())
                if (entry.m_info!=null && entry.m_info.m_skill==skill)
                {
                    SetBaseSkill(entry,level);
                    app.Report(skill+" base level set to "+level.ToString("0.##")+".");
                    return;
                }
            throw new InvalidOperationException("This character does not have a definition for the selected skill.");
        }
        internal static void SetBaseSkill(Skills.Skill entry,float level)
        {
            if (entry==null || !Rules.Finite(level) || level<0 || level>100) throw new ArgumentException("Skill level must be between 0 and 100.");
            entry.m_level=level;
            entry.m_accumulator=0;
        }
        internal void Teleport(Vector3 position)
        {
            if (!Rules.ValidPosition(position)) throw new ArgumentException("Coordinates must be finite and within +/- 20,000 on X/Z and +/- 10,000 on Y.");
            var p = app.RequirePlayer();
            if (!p.TeleportTo(position, p.transform.rotation, true)) throw new InvalidOperationException("Game rejected teleport. Finish your current action and retry.");
            app.SetOpen(false); app.Report("Teleport started to " + position + ".");
        }
        internal void Explore()
        {
            app.RequirePlayer();
            Minimap.instance.ExploreAll(); Minimap.instance.SaveMapData();
            app.Report("Full map explored and staged in character map data. This action has no automatic undo.");
        }
        internal void Damage(float amount)
        {
            var p = app.RequirePlayer();
            var hit = new HitData { m_ignorePVP = true, m_point = p.transform.position, m_dodgeable = false, m_blockable = false };
            hit.m_damage.m_damage = Mathf.Clamp(amount, 0, 100000);
            p.Damage(hit);
        }
        internal void Kill()
        {
            var p = app.RequirePlayer();
            p.SetHealth(0);
            Reflection.Method(typeof(Character), "CheckDeath").Invoke(p, null);
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
    internal static class StaminaPatch
    {
        private static bool Prefix(Player __instance) => !Plugin.Instance || __instance != Player.m_localPlayer || !Plugin.Instance.Features.InfiniteStamina;
    }
    [HarmonyPatch(typeof(Player), "RPC_UseStamina")]
    internal static class StaminaRpcPatch
    {
        private static bool Prefix(Player __instance) => !Plugin.Instance || __instance != Player.m_localPlayer || !Plugin.Instance.Features.InfiniteStamina;
    }
    [HarmonyPatch(typeof(Player), nameof(Player.HaveStamina))]
    internal static class HaveStaminaPatch
    {
        private static void Postfix(Player __instance, ref bool __result) { if (Plugin.Instance && __instance == Player.m_localPlayer && Plugin.Instance.Features.InfiniteStamina) __result = true; }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.GetMaxCarryWeight))]
    internal static class WeightPatch
    {
        private static void Postfix(Player __instance, ref float __result) { if (Plugin.Instance && __instance == Player.m_localPlayer && Plugin.Instance.Features.Weight) __result = 1000000000f; }
    }
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.IsTeleportable))]
    internal static class PortalPatch
    {
        private static void Postfix(Inventory __instance, ref bool __result)
        {
            if (Plugin.Instance && Plugin.Instance.Features.PortalItems && Player.m_localPlayer && __instance == Player.m_localPlayer.GetInventory()) __result = true;
        }
    }
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
    internal static class CraftPatch
    {
        private static void Prefix(InventoryGui __instance)
        {
            if (Plugin.Instance && Plugin.Instance.Features.FastCraft && Player.m_localPlayer && Reflection.Get<float>(__instance, "m_craftTimer") >= 0)
                Reflection.Set(__instance, "m_craftTimer", 1000000f);
        }
    }
    internal static class Rules
    {
        internal static bool ValidHotkey(KeyCode key) => Enum.IsDefined(typeof(KeyCode), key) && key != KeyCode.None && key != KeyCode.Escape && key != KeyCode.Mouse0 && key != KeyCode.Mouse1 && key < KeyCode.JoystickButton0;
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool ValidPosition(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z) && Mathf.Abs(p.x) <= 20000 && Mathf.Abs(p.z) <= 20000 && Mathf.Abs(p.y) <= 10000;
        internal static string Text(string input, int max = 240)
        {
            string value = (input ?? "").Replace('<', '[').Replace('>', ']').Replace('\n', ' ').Replace('\r', ' ');
            return value.Length <= max ? value : value.Substring(0, max);
        }
    }
}
