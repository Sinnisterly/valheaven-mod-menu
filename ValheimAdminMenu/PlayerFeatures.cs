using System;
using System.Reflection;
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
        private float refillAt, reapplyAt;
        private bool fallbackFlyOwned;
        private Rigidbody fallbackBody;
        private bool fallbackPrevGravity;
        /// <summary>Menu/hotkey intent. Survives mods that clear vanilla debug flags each tick.</summary>
        internal bool WantGod, WantFly;
        internal bool Stamina, Eitr, Weight, FastCraft, PortalItems;
        internal bool DistanceEnabled;
        internal float Distance = 10;
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
            if (!current.IsDead() && Time.unscaledTime >= reapplyAt)
            {
                // Mods often overwrite SetGodMode / ToggleDebugFly; re-assert wanted state ~2Hz.
                reapplyAt = Time.unscaledTime + .5f;
                if (WantGod && !RawInGodMode(current))
                {
                    try { current.SetGodMode(true); } catch { }
                }
                if (WantFly && !RawIsDebugFlying(current))
                {
                    try { current.ToggleDebugFly(); } catch { }
                }
            }
            if (WantFly && !RawIsDebugFlying(current)) DriveFallbackFly(current);
            else ClearFallbackFly();
            if (!current.IsDead() && Time.unscaledTime >= refillAt)
            {
                // Harmony blocks spend; soft top-up keeps bars full without per-frame Add*.
                refillAt = Time.unscaledTime + .2f;
                if (WantGod || current.InGodMode()) current.Heal(current.GetMaxHealth());
                if (Stamina) current.AddStamina(current.GetMaxStamina());
                if (Eitr) current.AddEitr(current.GetMaxEitr());
            }
            if (DistanceEnabled) { touchedDistance = true; current.m_maxInteractDistance = Mathf.Clamp(Distance, 1, 50); }
            else if (touchedDistance) { current.m_maxInteractDistance = originalDistance; touchedDistance = false; }
        }
        internal void God(bool value)
        {
            WantGod = value;
            touchedGod = true;
            try { app.RequirePlayer().SetGodMode(value); } catch { }
        }
        internal void Ghost(bool value) { app.RequirePlayer().SetGhostMode(value); touchedGhost = true; }
        internal void Fly(bool value)
        {
            WantFly = value;
            touchedFly = true;
            var p = app.RequirePlayer();
            try
            {
                if (RawIsDebugFlying(p) != value) p.ToggleDebugFly();
            }
            catch { }
            if (!value) ClearFallbackFly();
        }
        internal void NoCost(bool value) { app.RequirePlayer().SetNoPlacementCost(value); touchedCost = true; }
        internal void Restore()
        {
            ClearFallbackFly();
            if (player)
            {
                if (touchedGod) try { player.SetGodMode(originalGod); } catch { }
                if (touchedGhost) player.SetGhostMode(originalGhost);
                if (touchedCost) player.SetNoPlacementCost(originalCost);
                if (touchedDistance) player.m_maxInteractDistance = originalDistance;
                var view = player.GetComponent<ZNetView>();
                if (touchedFly && view && view.IsValid() && view.IsOwner() && RawIsDebugFlying(player) != originalFly)
                {
                    try { player.ToggleDebugFly(); } catch { }
                }
            }
            player = null;
            WantGod = WantFly = false;
            touchedGod = touchedGhost = touchedFly = touchedCost = touchedDistance = false;
            Stamina = Eitr = Weight = FastCraft = PortalItems = DistanceEnabled = false;
        }
        /// <summary>Read god without our InGodMode postfix (avoids reapply loops).</summary>
        private static bool RawInGodMode(Player p)
        {
            if (!p) return false;
            if (TryRawBool(p, "m_godMode", out bool god)) return god;
            if (TryRawBool(p, "m_debugGod", out god)) return god;
            // Can't read the field; force reapply while WantGod is on.
            return false;
        }
        private static bool RawIsDebugFlying(Player p)
        {
            if (!p) return false;
            if (TryRawBool(p, "m_debugFly", out bool fly)) return fly;
            // Field missing: treat as off while WantFly so fallback can drive.
            return Plugin.Instance?.Features.WantFly != true && p.IsDebugFlying();
        }
        private static bool TryRawBool(object obj, string name, out bool value)
        {
            value = false;
            try
            {
                for (var type = obj.GetType(); type != null; type = type.BaseType)
                {
                    var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (field == null || field.FieldType != typeof(bool)) continue;
                    value = (bool)field.GetValue(obj);
                    return true;
                }
            }
            catch { }
            return false;
        }
        private void DriveFallbackFly(Player p)
        {
            if (!p) return;
            var body = p.GetComponent<Rigidbody>();
            if (!body) return;
            if (!fallbackFlyOwned || fallbackBody != body)
            {
                ClearFallbackFly();
                fallbackBody = body;
                fallbackPrevGravity = body.useGravity;
                fallbackFlyOwned = true;
            }
            body.useGravity = false;
            Vector3 vel = body.linearVelocity;
            bool up = ZInput.GetButton("Jump");
            bool down = ZInput.GetButton("Crouch");
            float run = ZInput.GetButton("Run") ? 1.75f : 1f;
            vel.y = up ? 10f * run : (down ? -10f * run : 0f);
            Vector3 wish = FallbackWish(p);
            if (wish.sqrMagnitude > 0.01f)
            {
                wish.Normalize();
                float speed = 8f * run;
                vel.x = wish.x * speed;
                vel.z = wish.z * speed;
            }
            else
            {
                vel.x = 0f;
                vel.z = 0f;
            }
            body.linearVelocity = vel;
            try { Reflection.Set(p, "m_maxAirAltitude", p.transform.position.y); } catch { }
        }
        private void ClearFallbackFly()
        {
            if (!fallbackFlyOwned) { fallbackBody = null; return; }
            if (fallbackBody) fallbackBody.useGravity = fallbackPrevGravity;
            fallbackFlyOwned = false;
            fallbackBody = null;
        }
        private static Vector3 FallbackWish(Player p)
        {
            Vector3 fwd = p.GetLookDir();
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = p.transform.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 wish = Vector3.zero;
            if (ZInput.GetButton("Forward")) wish += fwd;
            if (ZInput.GetButton("Backward")) wish -= fwd;
            if (ZInput.GetButton("Left")) wish -= right;
            if (ZInput.GetButton("Right")) wish += right;
            return wish;
        }
        internal void Heal() { var p = app.RequirePlayer(); p.Heal(p.GetMaxHealth()); app.Report(L.T("ui.local_player_healed")); }
        internal void Power()
        {
            var p = app.RequirePlayer(); string power = p.GetGuardianPowerName();
            if (string.IsNullOrEmpty(power)) throw new InvalidOperationException("Equip a Guardian Power first.");
            var effect = ObjectDB.instance.GetStatusEffect(power.GetStableHashCode());
            if (!effect) throw new InvalidOperationException("The equipped Guardian Power has no status effect.");
            p.GetSEMan().AddStatusEffect(effect.NameHash(), true);
            app.Report(L.T("ui.equipped_guardian_power_applied_to"));
        }
        internal void SetSkill(Skills.SkillType skill, float level)
        {
            if (skill == Skills.SkillType.None || skill == Skills.SkillType.All || !Enum.IsDefined(typeof(Skills.SkillType), skill)) throw new ArgumentException("Select an individual skill.");
            var skills = app.RequirePlayer().GetSkills();
            if (!Rules.Finite(level) || level<0 || level>100) throw new ArgumentException("Skill level must be between 0 and 100.");
            // GetSkillLevel creates the entry if missing, but its return includes buffs/rounding.
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
        internal void SetAllSkills(float level)
        {
            if (!Rules.Finite(level) || level < 0 || level > 100) throw new ArgumentException("Skill level must be between 0 and 100.");
            var skills = app.RequirePlayer().GetSkills();
            int count = 0;
            foreach (var def in skills.m_skills)
            {
                if (def == null || def.m_skill == Skills.SkillType.None || def.m_skill == Skills.SkillType.All) continue;
                skills.GetSkillLevel(def.m_skill);
                foreach (var entry in skills.GetSkillList())
                {
                    if (entry.m_info == null || entry.m_info.m_skill != def.m_skill) continue;
                    SetBaseSkill(entry, level);
                    count++;
                    break;
                }
            }
            app.Report("Set " + count + " skills to base level " + level.ToString("0.##") + ".");
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
            app.Report(L.T("ui.full_map_explored_and_staged"));
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
    [HarmonyPatch(typeof(Character), nameof(Character.InGodMode))]
    internal static class WantGodModePatch
    {
        private static void Postfix(Character __instance, ref bool __result)
        {
            if (__result || Plugin.Instance == null) return;
            if (__instance is Player player && player == Player.m_localPlayer && Plugin.Instance.Features.WantGod)
                __result = true;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.IsDebugFlying))]
    internal static class WantFlyModePatch
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (__result || Plugin.Instance == null) return;
            if (__instance == Player.m_localPlayer && Plugin.Instance.Features.WantFly)
                __result = true;
        }
    }
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class GodDamageRpcPatch
    {
        private static bool Prefix(Character __instance)
        {
            if (!Plugin.Instance || !(__instance is Player player) || player != Player.m_localPlayer) return true;
            return !Plugin.Instance.Features.WantGod && !player.InGodMode();
        }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class GodApplyDamagePatch
    {
        private static bool Prefix(Character __instance)
        {
            if (!Plugin.Instance || !(__instance is Player player) || player != Player.m_localPlayer) return true;
            return !Plugin.Instance.Features.WantGod && !player.InGodMode();
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
    internal static class StaminaPatch
    {
        private static bool Prefix(Player __instance) => !Plugin.Instance || __instance != Player.m_localPlayer || !Plugin.Instance.Features.Stamina;
    }
    [HarmonyPatch(typeof(Player), "RPC_UseStamina")]
    internal static class StaminaRpcPatch
    {
        private static bool Prefix(Player __instance) => !Plugin.Instance || __instance != Player.m_localPlayer || !Plugin.Instance.Features.Stamina;
    }
    [HarmonyPatch(typeof(Player), nameof(Player.HaveStamina))]
    internal static class HaveStaminaPatch
    {
        private static void Postfix(Player __instance, ref bool __result) { if (Plugin.Instance && __instance == Player.m_localPlayer && Plugin.Instance.Features.Stamina) __result = true; }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.UseEitr))]
    internal static class EitrPatch
    {
        private static bool Prefix(Player __instance) => !Plugin.Instance || __instance != Player.m_localPlayer || !Plugin.Instance.Features.Eitr;
    }
    [HarmonyPatch(typeof(Player), "RPC_UseEitr")]
    internal static class EitrRpcPatch
    {
        private static bool Prefix(Player __instance) => !Plugin.Instance || __instance != Player.m_localPlayer || !Plugin.Instance.Features.Eitr;
    }
    [HarmonyPatch(typeof(Player), nameof(Player.HaveEitr))]
    internal static class HaveEitrPatch
    {
        private static void Postfix(Player __instance, ref bool __result) { if (Plugin.Instance && __instance == Player.m_localPlayer && Plugin.Instance.Features.Eitr) __result = true; }
    }
    // Carry weight handled in CarryWeightPhasePatch (PhaseFeatures).
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
