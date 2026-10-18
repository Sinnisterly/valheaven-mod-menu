using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ValheimAdminMenu
{
    /// <summary>Local-player feature patches. No m_cheated stamps.</summary>
    internal static class FeatureSuite
    {
        private static readonly int RestedHash = "Rested".GetStableHashCode();

        internal static void MaintainRested(Player player)
        {
            if (!player) return;
            var seman = player.GetSEMan();
            if (seman == null) return;
            var effect = seman.GetStatusEffect(RestedHash);
            if (effect == null)
            {
                seman.AddStatusEffect(RestedHash, true);
                effect = seman.GetStatusEffect(RestedHash);
            }
            effect?.ResetTime();
        }

        internal static void ApplyStatusEffect(string name)
        {
            var player = Plugin.Instance.RequirePlayer();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Choose a status effect.");
            if (!ObjectDB.instance) throw new InvalidOperationException("ObjectDB is not ready.");
            var se = ObjectDB.instance.GetStatusEffect(name.GetStableHashCode());
            if (!se) throw new ArgumentException("Unknown status effect \"" + name + "\".");
            player.GetSEMan().AddStatusEffect(se.NameHash(), true);
            Plugin.Instance.Report("Applied " + StatusLabel(se) + ".");
        }

        internal static void RemoveStatusEffect(string name)
        {
            var player = Plugin.Instance.RequirePlayer();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Choose a status effect.");
            player.GetSEMan().RemoveStatusEffect(name.GetStableHashCode(), true);
            Plugin.Instance.Report("Removed status effect " + name + ".");
        }

        internal static void RemoveAllStatusEffects()
        {
            var player = Plugin.Instance.RequirePlayer();
            player.GetSEMan().RemoveAllStatusEffects(true);
            Plugin.Instance.Report(L.T("ui.cleared_active_status_effects"));
        }

        internal static string StatusLabel(StatusEffect se)
        {
            if (!se) return "";
            string localized = Localization.instance != null ? Localization.instance.Localize(se.m_name) : se.m_name;
            if (string.IsNullOrWhiteSpace(localized) || localized.StartsWith("$", StringComparison.Ordinal) || localized.StartsWith("??", StringComparison.Ordinal))
                return se.name;
            return localized + " (" + se.name + ")";
        }

        internal static void TeleportToCrosshair()
        {
            var player = Plugin.Instance.RequirePlayer();
            var cam = GameCamera.instance ? GameCamera.instance.transform : Camera.main?.transform;
            if (!cam) throw new InvalidOperationException("Camera is not ready.");
            Vector3 origin = cam.position;
            Vector3 dir = cam.forward;
            int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle", "character_trigger", "character", "item");
            if (Physics.Raycast(origin, dir, out var hit, 400f, mask, QueryTriggerInteraction.Ignore))
            {
                Vector3 point = hit.point + hit.normal * 0.35f + Vector3.up * 0.15f;
                Plugin.Instance.Features.Teleport(point);
                return;
            }
            Plugin.Instance.Features.Teleport(origin + dir.normalized * 40f);
        }

        private static void ScaleDamage(ref HitData.DamageTypes d, float mult)
        {
            d.m_damage *= mult;
            d.m_blunt *= mult;
            d.m_slash *= mult;
            d.m_pierce *= mult;
            d.m_chop *= mult;
            d.m_pickaxe *= mult;
            d.m_fire *= mult;
            d.m_frost *= mult;
            d.m_lightning *= mult;
            d.m_poison *= mult;
            d.m_spirit *= mult;
        }

        internal static void ModifyOutgoingHit(HitData hit)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || hit == null) return;
            if (hit.GetAttacker() != Player.m_localPlayer) return;
            float dmg = Mathf.Clamp(phase.OutgoingDamageMult, 0f, 50f);
            if (Mathf.Abs(dmg - 1f) > .001f) ScaleDamage(ref hit.m_damage, dmg);
            float push = Mathf.Clamp(phase.HitPushMult, 0f, 20f);
            if (Mathf.Abs(push - 1f) > .001f) hit.m_pushForce *= push;
        }

        internal static bool ModifyIncomingHit(Character target, HitData hit)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || hit == null || target != Player.m_localPlayer) return true;
            if ((phase.NoFallDamage || phase.FreecamBuild || phase.FlyingMount) && hit.m_hitType == HitData.HitType.Fall)
            {
                hit.m_damage = new HitData.DamageTypes();
                return false;
            }
            float dmg = Mathf.Clamp(phase.IncomingDamageMult, 0f, 10f);
            if (Mathf.Abs(dmg - 1f) > .001f) ScaleDamage(ref hit.m_damage, dmg);
            return true;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class DamageMultApplyPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            FeatureSuite.ModifyOutgoingHit(hit);
            FeatureSuite.ModifyIncomingHit(__instance, hit);
        }
    }

    [HarmonyPatch(typeof(Character), "AddStaggerDamage")]
    internal static class StaggerProtectPatch
    {
        private static bool Prefix(Character __instance)
        {
            return Plugin.Instance?.Phase.StaggerProtection != true || __instance != Player.m_localPlayer;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Stagger))]
    internal static class StaggerBlockPatch
    {
        private static bool Prefix(Character __instance)
        {
            return Plugin.Instance?.Phase.StaggerProtection != true || __instance != Player.m_localPlayer;
        }
    }

    [HarmonyPatch(typeof(Skills), nameof(Skills.OnDeath))]
    internal static class PreventSkillLossPatch
    {
        private static bool Prefix(Skills __instance)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || (!phase.PreventSkillLoss && !phase.FreezeSkills)) return true;
            var player = Player.m_localPlayer;
            return !player || __instance != player.GetSkills();
        }
    }

    [HarmonyPatch(typeof(Skills), nameof(Skills.LowerAllSkills))]
    internal static class LowerSkillsBlockPatch
    {
        private static bool Prefix(Skills __instance)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || (!phase.PreventSkillLoss && !phase.FreezeSkills)) return true;
            var player = Player.m_localPlayer;
            return !player || __instance != player.GetSkills();
        }
    }

    [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
    internal static class FreezeXpPatch
    {
        private static bool Prefix(Skills __instance)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || (!phase.FreezeXp && !phase.FreezeSkills)) return true;
            var player = Player.m_localPlayer;
            return !player || __instance != player.GetSkills();
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.UpdateAI))]
    internal static class FreezeAiPatch
    {
        private static bool Prefix(BaseAI __instance)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || (!phase.FreezeAiGlobal && !phase.FreezeAiNearby)) return true;
            Character character = null;
            try { character = Reflection.Get<Character>(__instance, "m_character"); } catch { }
            if (!character || character is Player || character.IsTamed()) return true;
            if (phase.FreezeAiGlobal) return false;
            var player = Player.m_localPlayer;
            if (!player) return true;
            float radius = Mathf.Max(8f, Plugin.Instance.World.Radius);
            return (player.transform.position - character.transform.position).sqrMagnitude > radius * radius;
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class BuildDistancePatch
    {
        private struct State { public bool active; public float max; }
        private static void Prefix(Player __instance, out State __state)
        {
            __state = default;
            var phase = Plugin.Instance?.Phase;
            if (phase == null || __instance != Player.m_localPlayer || !phase.ExtendedBuildDistance) return;
            float mult = Mathf.Clamp(phase.BuildDistanceMult, 1f, 20f);
            if (mult <= 1.001f) return;
            __state.active = true;
            __state.max = __instance.m_maxPlaceDistance;
            __instance.m_maxPlaceDistance = __state.max * mult;
        }
        private static void Finalizer(Player __instance, State __state)
        {
            if (__state.active) __instance.m_maxPlaceDistance = __state.max;
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class FineRotationPatch
    {
        private struct State { public bool active; public float degrees; }
        private static void Prefix(Player __instance, out State __state)
        {
            __state = default;
            var phase = Plugin.Instance?.Phase;
            if (phase?.FineBuildRotation != true || __instance != Player.m_localPlayer) return;
            try
            {
                __state.degrees = Reflection.Get<float>(__instance, "m_placeRotationDegrees");
                Reflection.Set(__instance, "m_placeRotationDegrees", 1f);
                __state.active = true;
            }
            catch { __state = default; }
        }
        private static void Finalizer(Player __instance, State __state)
        {
            if (!__state.active) return;
            try { Reflection.Set(__instance, "m_placeRotationDegrees", __state.degrees); } catch { }
        }
    }

    [HarmonyPatch(typeof(Smelter), "UpdateSmelter")]
    internal static class SmelterSpeedPatch
    {
        private struct State { public bool active; public float sec; }
        private static void Prefix(Smelter __instance, out State __state)
        {
            __state = default;
            var phase = Plugin.Instance?.Phase;
            if (phase == null || (!phase.InstantSmelt && Mathf.Abs(phase.ProductionSpeedMult - 1f) < .01f)) return;
            var view = __instance.GetComponent<ZNetView>();
            if (!view || !view.IsValid() || !view.IsOwner()) return;
            __state.active = true;
            __state.sec = __instance.m_secPerProduct;
            if (phase.InstantSmelt) __instance.m_secPerProduct = .05f;
            else __instance.m_secPerProduct = Mathf.Max(.05f, __state.sec / Mathf.Clamp(phase.ProductionSpeedMult, 1f, 50f));
        }
        private static void Finalizer(Smelter __instance, State __state)
        {
            if (__state.active) __instance.m_secPerProduct = __state.sec;
        }
    }

    [HarmonyPatch(typeof(Fermenter), "SlowUpdate")]
    internal static class FermenterSpeedPatch
    {
        private struct State { public bool active; public float dur; }
        private static void Prefix(Fermenter __instance, out State __state)
        {
            __state = default;
            var phase = Plugin.Instance?.Phase;
            if (phase == null || (!phase.InstantFerment && Mathf.Abs(phase.ProductionSpeedMult - 1f) < .01f)) return;
            var view = __instance.GetComponent<ZNetView>();
            if (!view || !view.IsValid() || !view.IsOwner()) return;
            __state.active = true;
            __state.dur = __instance.m_fermentationDuration;
            if (phase.InstantFerment) __instance.m_fermentationDuration = 1f;
            else __instance.m_fermentationDuration = Mathf.Max(1f, __state.dur / Mathf.Clamp(phase.ProductionSpeedMult, 1f, 50f));
        }
        private static void Finalizer(Fermenter __instance, State __state)
        {
            if (__state.active) __instance.m_fermentationDuration = __state.dur;
        }
    }

    [HarmonyPatch(typeof(Plant), "GetGrowTime")]
    internal static class PlantGrowSpeedPatch
    {
        private static void Postfix(Plant __instance, ref float __result)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null) return;
            if (phase.InstantPlantGrow) { __result = .1f; return; }
            float mult = Mathf.Clamp(phase.ProductionSpeedMult, 1f, 50f);
            if (mult > 1.01f) __result = Mathf.Max(.1f, __result / mult);
        }
    }

    // Instant growth only shortens GetGrowTime. Vanilla still marks wrong-biome plants unhealthy, so Grow never runs.
    [HarmonyPatch(typeof(Plant), "UpdateHealth")]
    internal static class InstantPlantHealthPatch
    {
        private static void Postfix(Plant __instance, ref Plant.Status ___m_status)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase?.InstantPlantGrow != true || !__instance) return;
            if (PlantEverything.Decorative(__instance)) return;
            Release250.PreparePlantForForcedGrowth(__instance);
            ___m_status = Plant.Status.Healthy;
        }
    }

    [HarmonyPatch(typeof(CookingStation), "UpdateCooking")]
    internal static class CookingSpeedPatch
    {
        private struct State
        {
            public bool active;
            public float[] times;
        }

        private static void Prefix(CookingStation __instance, out State __state)
        {
            __state = default;
            var phase = Plugin.Instance?.Phase;
            if (phase == null || (!phase.InstantCook && Mathf.Abs(phase.ProductionSpeedMult - 1f) < .01f)) return;
            var view = __instance.GetComponent<ZNetView>();
            if (!view || !view.IsValid() || !view.IsOwner()) return;
            var list = __instance.m_conversion;
            if (list == null || list.Count == 0) return;
            __state.active = true;
            __state.times = new float[list.Count];
            float mult = phase.InstantCook ? 200f : Mathf.Clamp(phase.ProductionSpeedMult, 1f, 50f);
            for (int i = 0; i < list.Count; i++)
            {
                var conv = list[i];
                if (conv == null) continue;
                __state.times[i] = conv.m_cookTime;
                conv.m_cookTime = phase.InstantCook ? .05f : Mathf.Max(.05f, conv.m_cookTime / mult);
            }
        }

        private static void Finalizer(CookingStation __instance, State __state)
        {
            if (!__state.active || __state.times == null || __instance?.m_conversion == null) return;
            var list = __instance.m_conversion;
            for (int i = 0; i < list.Count && i < __state.times.Length; i++)
            {
                var conv = list[i];
                if (conv != null) conv.m_cookTime = __state.times[i];
            }
        }
    }

    // Scale velocity after aim assist / magic bullet / gravity patches.
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    [HarmonyPriority(Priority.Last)]
    internal static class ProjectileSpeedSetupPatch
    {
        private static void Finalizer(Projectile __instance, Character owner)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || owner != Player.m_localPlayer || !__instance) return;
            float mult = Mathf.Clamp(phase.ProjectileSpeedMult, 1f, 10f);
            if (mult <= 1.001f) return;
            try
            {
                Vector3 vel = Reflection.Get<Vector3>(__instance, "m_vel");
                Reflection.Set(__instance, "m_vel", vel * mult);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Character), "UnderWorldCheck")]
    internal static class UnderWorldCheckPatch
    {
        private static bool Prefix(Character __instance)
        {
            if (__instance == Player.m_localPlayer && Plugin.Instance != null)
            {
                var phase = Plugin.Instance.Phase;
                if (phase != null && phase.Noclip)
                {
                    float gh = ZoneSystem.instance.GetGroundHeight(__instance.transform.position);
                    if (__instance.transform.position.y < gh)
                    {
                        var pos = __instance.transform.position;
                        pos.y = gh;
                        __instance.transform.position = pos;
                        var rb = __instance.GetComponent<Rigidbody>();
                        if (rb) rb.linearVelocity = new UnityEngine.Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
                    }
                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
    internal static class GameCameraWaterPatch
    {
        private static void Prefix(GameCamera __instance, ref float __state)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase != null && (phase.Noclip || phase.MoveUnderwater || (Player.m_localPlayer && Player.m_localPlayer.IsDebugFlying())))
            {
                __state = __instance.m_minWaterDistance;
                __instance.m_minWaterDistance = -10000f;
            }
            else
            {
                __state = -10000f;
            }
        }
        private static void Postfix(GameCamera __instance, float __state)
        {
            if (__state != -10000f)
                __instance.m_minWaterDistance = __state;
        }
    }
    [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
    internal static class CharacterDropPatch
    {
        private static void Postfix(CharacterDrop __instance, ref System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<UnityEngine.GameObject, int>> __result)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase != null && phase.ForceTrophy)
            {
                if (__result == null) __result = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<UnityEngine.GameObject, int>>();
                var drops = Reflection.Get<System.Collections.IList>(__instance, "m_drops");
                if (drops == null) return;
                foreach (object drop in drops)
                {
                    var prefab = Reflection.Get<UnityEngine.GameObject>(drop, "m_prefab");
                    if (prefab && prefab.name.StartsWith("Trophy", StringComparison.OrdinalIgnoreCase))
                    {
                        bool exists = false;
                        for (int i = 0; i < __result.Count; i++)
                        {
                            if (__result[i].Key == prefab) { exists = true; break; }
                        }
                        if (!exists)
                            __result.Add(new System.Collections.Generic.KeyValuePair<UnityEngine.GameObject, int>(prefab, phase.ForceTrophyCount));
                    }
                }
            }
        }
    }
    [HarmonyPatch(typeof(RandEventSystem), "StartRandomEvent")]
    internal static class RandEventSystemPatch
    {
        private static bool Prefix()
        {
            var phase = Plugin.Instance?.Phase;
            if (phase != null && phase.DisableRaids) return false;
            return true;
        }
    }
    [HarmonyPatch(typeof(CharacterDrop), "OnDeath")]
    internal static class CharacterDropOnDeathPatch
    {
        private static void Postfix(CharacterDrop __instance)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || phase.OnKillDrops.Count == 0 || !Player.m_localPlayer) return;
            var character=__instance.GetComponent<Character>();
            var hit=character?Reflection.Get<HitData>(character,"m_lastHit"):null;
            if(hit==null || hit.GetAttacker()!=Player.m_localPlayer)return;
            
            var pos = __instance.transform.position + UnityEngine.Vector3.up * 0.5f;
            foreach (var q in phase.OnKillDrops)
            {
                var prefab = ObjectDB.instance?.GetItemPrefab(q.Prefab);
                if (!prefab) continue;
                
                SpawnPresets.Drop(prefab,pos,q.Amount,q.Quality,q.Crafter,q.NoCrafter,q.Edits);
            }
            if (!phase.PersistentOnKillDrops) phase.OnKillDrops.Clear();
        }
    }
}