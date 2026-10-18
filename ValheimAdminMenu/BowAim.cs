using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimAdminMenu
{
    internal enum BowAimTargetMode
    {
        ClosestCrosshair = 0,
        HighestHealth = 1,
        ClosestPlayer = 2,
    }

    /// <summary>Local bow/crossbow assist, ballistic predict, magic-bullet. Soft assist rewrites vel in Projectile.Setup so spread can't undo a lock.</summary>
    internal static class BowAim
    {
        private static readonly List<Character> Nearby = new List<Character>();
        private static Texture2D MarkerTex;

        internal static bool HasPredictMarker;
        internal static Vector3 PredictWorldPoint;
        internal static Vector3 PredictOrigin;

        internal static bool IsProjectileAttack(Attack attack)
        {
            if (attack == null) return false;
            return attack.m_bowDraw
                || attack.m_attackType == Attack.AttackType.Projectile
                || attack.m_attackType == Attack.AttackType.TriggerProjectile;
        }

        internal static bool LocalProjectileContext(Humanoid human, out Attack attack, out float drawPct)
        {
            attack = null;
            drawPct = 0f;
            if (human == null || human != Player.m_localPlayer) return false;
            try { attack = Reflection.Get<Attack>(human, "m_currentAttack"); } catch { attack = null; }
            drawPct = human.GetAttackDrawPercentage();

            if (attack == null || !IsProjectileAttack(attack))
            {
                var weaponAttack = human.GetCurrentWeapon()?.m_shared?.m_attack;
                if (weaponAttack != null && IsProjectileAttack(weaponAttack))
                    attack = weaponAttack;
            }

            if (attack != null)
            {
                float snap = 0f;
                try { snap = Reflection.Get<float>(attack, "m_attackDrawPercentage"); } catch { snap = 0f; }
                if (snap > 0.01f && (drawPct < 0.01f || human.InAttack()))
                    drawPct = snap;
            }

            if (drawPct > 0.01f) return true;
            try { if (human.IsDrawingBow()) return true; } catch { }
            if (attack != null && human.InAttack() && IsProjectileAttack(attack)) return true;
            return false;
        }

        /// <summary>Spawn + aim like FireProjectileBurst. Do not write Attack.m_character on shared templates; GetProjectileSpawnPoint only on live attacks.</summary>
        internal static void ResolveSpawnAim(Humanoid human, Attack attack, out Vector3 spawn, out Vector3 aim)
        {
            aim = human ? human.GetLookDir() : Vector3.forward;
            spawn = human ? human.transform.position + Vector3.up * 1.2f : Vector3.zero;
            if (!human || attack == null) return;

            Humanoid owner = null;
            try { owner = Reflection.Get<Humanoid>(attack, "m_character"); } catch { owner = null; }
            if (owner == human)
            {
                try
                {
                    object[] args = { Vector3.zero, Vector3.forward };
                    Reflection.Method(typeof(Attack), "GetProjectileSpawnPoint", typeof(Vector3).MakeByRefType(), typeof(Vector3).MakeByRefType())
                        .Invoke(attack, args);
                    spawn = (Vector3)args[0];
                    aim = (Vector3)args[1];
                    return;
                }
                catch { }
            }

            try
            {
                // Approximate GetProjectileSpawnPoint without touching shared Attack.m_character.
                // Vanilla falls back to character transform when no origin joint resolves.
                Transform body = human.transform;
                spawn = body.position
                    + body.up * attack.m_attackHeight
                    + body.forward * attack.m_attackRange
                    + body.right * attack.m_attackOffset;
                aim = human.GetLookDir();
            }
            catch { }
        }

        internal static bool IsRangedSkill(Skills.SkillType skill)
        {
            return skill == Skills.SkillType.Bows || skill == Skills.SkillType.Crossbows || skill == Skills.SkillType.Spears;
        }

        internal static float ProjectileSpeed(Attack attack, float drawPct)
        {
            if (attack == null) return 50f;
            float min = attack.m_projectileVelMin;
            float max = attack.m_projectileVel;
            if (max < 0.1f) max = 50f;
            if (min < 0.1f) min = max * 0.5f;
            float t = 1f;
            if (attack.m_bowDraw)
            {
                t = Mathf.Clamp01(drawPct);
                if (attack.m_drawVelocityCurve != null && attack.m_drawVelocityCurve.length > 0)
                    t = Mathf.Clamp01(attack.m_drawVelocityCurve.Evaluate(t));
            }
            return Mathf.Lerp(min, max, t);
        }

        internal static float EffectiveProjectileSpeed(Attack attack, float drawPct, PhaseFeatures phase)
        {
            return ProjectileSpeed(attack, drawPct) * Mathf.Clamp(phase?.ProjectileSpeedMult ?? 1f, 1f, 10f);
        }

        internal static float ProjectileGravity(Attack attack)
        {
            if (attack?.m_attackProjectile)
            {
                var proj = attack.m_attackProjectile.GetComponent<Projectile>();
                if (proj) return Mathf.Max(0f, proj.m_gravity);
            }
            return 5f; // typical arrow prefab gravity
        }

        /// <summary>Assist or NoArrowGravity force g=0 so predict matches the shot.</summary>
        internal static float EffectiveGravity(PhaseFeatures phase, Attack attack, Projectile live = null)
        {
            if (phase != null && (phase.BowAimAssist || phase.NoArrowGravity)) return 0f;
            if (live) return Mathf.Max(0f, live.m_gravity);
            return ProjectileGravity(attack);
        }

        internal static bool ShouldZeroProjectileGravity(PhaseFeatures phase, Character owner, Projectile proj)
        {
            if (phase == null || owner != Player.m_localPlayer || !proj) return false;
            if (!phase.NoArrowGravity && !phase.BowAimAssist) return false;
            var skill = proj.m_skill;
            if (skill == Skills.SkillType.None)
            {
                try
                {
                    var weapon = Reflection.Get<ItemDrop.ItemData>(proj, "m_weapon");
                    skill = weapon?.m_shared?.m_skillType ?? Skills.SkillType.None;
                }
                catch { skill = Skills.SkillType.None; }
            }
            return IsRangedSkill(skill);
        }

        internal static Character FindTarget(Character self, Vector3 from, Vector3 aimDir, float maxRange, float maxAngle, bool targetPlayers, BowAimTargetMode mode)
        {
            if (!self || maxRange < 1f || maxAngle < 0.5f) return null;
            Nearby.Clear();
            Character.GetCharactersInRange(from, maxRange, Nearby);
            Character best = null;
            float bestAngle = maxAngle;
            float bestDist = maxRange + 1f;
            float bestHealth = -1f;
            Vector3 forward = aimDir.sqrMagnitude > 1e-6f ? aimDir.normalized : self.GetLookDir();
            for (int i = 0; i < Nearby.Count; i++)
            {
                var c = Nearby[i];
                if (!c || c == self || c.IsDead()) continue;
                if (c is Player)
                {
                    if (!targetPlayers) continue;
                }
                else if (!BaseAI.IsEnemy(self, c))
                    continue;

                Vector3 center = c.GetCenterPoint();
                Vector3 delta = center - from;
                float dist = delta.magnitude;
                if (dist < 0.75f || dist > maxRange) continue;
                float ang = Vector3.Angle(forward, delta);
                if (ang > maxAngle) continue;

                switch (mode)
                {
                    case BowAimTargetMode.HighestHealth:
                    {
                        float hp = 0f;
                        try { hp = c.GetHealth(); } catch { hp = 0f; }
                        if (hp > bestHealth + 0.01f || (Mathf.Abs(hp - bestHealth) <= 0.01f && dist < bestDist))
                        {
                            bestHealth = hp;
                            bestDist = dist;
                            bestAngle = ang;
                            best = c;
                        }
                        break;
                    }
                    case BowAimTargetMode.ClosestPlayer:
                    {
                        if (dist < bestDist - 0.05f || (Mathf.Abs(dist - bestDist) <= 0.05f && ang < bestAngle))
                        {
                            bestDist = dist;
                            bestAngle = ang;
                            best = c;
                        }
                        break;
                    }
                    default: // ClosestCrosshair
                        if (ang < bestAngle - 0.05f || (Mathf.Abs(ang - bestAngle) <= 0.05f && dist < bestDist))
                        {
                            bestAngle = ang;
                            bestDist = dist;
                            best = c;
                        }
                        break;
                }
            }
            Nearby.Clear();
            return best;
        }

        /// <summary>Ballistic aim with lead. Gravity matches Projectile.FixedUpdate (vel += down * g * dt).</summary>
        internal static Vector3 PredictAimDir(Vector3 from, Character target, float speed, float gravity, out Vector3 leadPoint)
        {
            leadPoint = target ? target.GetCenterPoint() : from + Vector3.forward;
            if (!target || speed < 0.5f) return Vector3.forward;
            Vector3 aim = target.GetCenterPoint();
            Vector3 vel = Vector3.zero;
            try { vel = target.GetVelocity(); } catch { vel = Vector3.zero; }

            Vector3 dir = (aim - from).normalized;
            for (int i = 0; i < 4; i++)
            {
                float dist = Vector3.Distance(from, aim);
                float t = dist / speed;
                Vector3 lead = target.GetCenterPoint() + vel * t;
                if (!TryBallisticDir(from, lead, speed, gravity, out dir))
                {
                    // Unreachable: loft toward lead.
                    Vector3 delta = lead - from;
                    float xz = new Vector3(delta.x, 0f, delta.z).magnitude;
                    float loft = gravity > 0.01f ? (gravity * xz * xz) / (2f * speed * speed) : 0f;
                    dir = (delta + Vector3.up * loft).normalized;
                }
                // Time along chosen trajectory for next lead pass.
                float flat = new Vector3(dir.x, 0f, dir.z).magnitude * speed;
                t = flat > 0.1f ? new Vector3(lead.x - from.x, 0f, lead.z - from.z).magnitude / flat : dist / speed;
                aim = target.GetCenterPoint() + vel * Mathf.Clamp(t, 0f, 8f);
            }
            leadPoint = aim;
            return dir.sqrMagnitude > 1e-6f ? dir.normalized : (target.GetCenterPoint() - from).normalized;
        }

        internal static Vector3 PredictAimDir(Vector3 from, Character target, float speed, float gravity)
        {
            return PredictAimDir(from, target, speed, gravity, out _);
        }

        internal static bool TryBallisticDir(Vector3 from, Vector3 to, float speed, float gravity, out Vector3 dir)
        {
            Vector3 delta = to - from;
            float xz = new Vector3(delta.x, 0f, delta.z).magnitude;
            float y = delta.y;
            if (xz < 0.05f)
            {
                dir = delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector3.up;
                return true;
            }
            // Zero-g: straight line.
            if (gravity <= 0.01f)
            {
                dir = delta.normalized;
                return true;
            }
            float g = gravity;
            float v2 = speed * speed;
            float under = v2 * v2 - g * (g * xz * xz + 2f * y * v2);
            if (under < 0f)
            {
                dir = delta.normalized;
                return false;
            }
            // Low arc (minus root); feels more like a real bow than the high lob.
            float tanTheta = (v2 - Mathf.Sqrt(under)) / (g * xz);
            float cos = 1f / Mathf.Sqrt(1f + tanTheta * tanTheta);
            float sin = tanTheta * cos;
            Vector3 flat = new Vector3(delta.x, 0f, delta.z).normalized;
            dir = (flat * cos + Vector3.up * sin).normalized;
            return true;
        }

        internal static bool TryComputeDesiredAim(
            Vector3 currentAim,
            Vector3 from,
            Attack attack,
            float drawPct,
            PhaseFeatures phase,
            bool hardSnap,
            out Vector3 desired,
            out Vector3 lead,
            out float strength,
            out float cone)
        {
            desired = currentAim;
            lead = from;
            strength = 0f;
            cone = 0f;
            if (phase == null) return false;
            float range = Mathf.Clamp(phase.BowAimRange, 10f, 100f);
            cone = Mathf.Clamp(phase.BowAimAngle, 5f, 75f);
            if (hardSnap) cone = Mathf.Min(90f, cone * 1.5f);
            strength = Mathf.Clamp(phase.BowAimStrength, 10f, 100f) / 100f;
            // Near-max strength: widen cone like magic bullet so the same target wins.
            if (strength >= 0.995f) cone = Mathf.Min(90f, cone * 1.5f);
            float speed = EffectiveProjectileSpeed(attack, drawPct, phase);
            float gravity = EffectiveGravity(phase, attack);

            var mode = (BowAimTargetMode)Mathf.Clamp(phase.BowAimTargetMode, 0, 2);
            var target = FindTarget(Player.m_localPlayer, from, currentAim, range, cone, phase.BowAimTargetPlayers, mode);
            if (!target) return false;

            desired = PredictAimDir(from, target, speed, gravity, out lead);
            return desired.sqrMagnitude > 1e-6f;
        }

        internal static Vector3 AssistAim(Vector3 currentAim, Vector3 from, Attack attack, float drawPct, PhaseFeatures phase, bool hardSnap)
        {
            if (!TryComputeDesiredAim(currentAim, from, attack, drawPct, phase, hardSnap, out Vector3 desired, out Vector3 lead, out float strength, out float cone))
                return currentAim;

            PredictOrigin = from;
            PredictWorldPoint = lead;
            HasPredictMarker = phase.ShowAimPredict;

            if (hardSnap || strength >= 0.995f) return desired.normalized;
            float ang = Vector3.Angle(currentAim, desired);
            float falloff = cone > 0.01f ? Mathf.Clamp01(1f - ang / cone) : 1f;
            // Soft blend at low strength; near-full still mostly snaps.
            float t = strength >= 0.9f
                ? Mathf.Lerp(strength * Mathf.Max(0.5f, falloff), strength, (strength - 0.9f) / 0.1f)
                : strength * Mathf.Max(0.35f, falloff);
            return Vector3.Slerp(currentAim.normalized, desired.normalized, Mathf.Clamp01(t)).normalized;
        }

        /// <summary>Marker/predict while drawing. GetAimDir only runs at fire.</summary>
        internal static void TickPredict()
        {
            var phase = Plugin.Instance?.Phase;
            if (phase?.BowAimAssist != true || !phase.ShowAimPredict)
            {
                ClearPredictMarker();
                return;
            }
            var player = Player.m_localPlayer;
            if (!player || player.IsDead() || Plugin.Capturing)
            {
                ClearPredictMarker();
                return;
            }
            if (!LocalProjectileContext(player, out var attack, out float drawPct))
            {
                ClearPredictMarker();
                return;
            }
            if (attack != null && !IsProjectileAttack(attack))
            {
                ClearPredictMarker();
                return;
            }
            var skill = attack?.GetWeapon()?.m_shared?.m_skillType
                ?? player.GetCurrentWeapon()?.m_shared?.m_skillType
                ?? Skills.SkillType.None;
            if (skill != Skills.SkillType.None && !IsRangedSkill(skill))
            {
                ClearPredictMarker();
                return;
            }

            ResolveSpawnAim(player, attack, out Vector3 from, out Vector3 aim);
            if (!TryComputeDesiredAim(aim, from, attack, drawPct, phase, hardSnap: false, out _, out Vector3 lead, out _, out _))
            {
                ClearPredictMarker();
                return;
            }
            PredictOrigin = from;
            PredictWorldPoint = lead;
            HasPredictMarker = true;
        }

        internal static void ClearPredictMarker()
        {
            HasPredictMarker = false;
        }

        internal static void DrawPredictMarker()
        {
            var phase = Plugin.Instance?.Phase;
            if (phase?.BowAimAssist != true || !phase.ShowAimPredict || !HasPredictMarker) return;
            if (!Player.m_localPlayer || Event.current.type != EventType.Repaint) return;
            var camera = Camera.main;
            if (!camera) return;

            Vector3 screen = camera.WorldToScreenPoint(PredictWorldPoint);
            if (screen.z <= 0f) return;
            float x = screen.x;
            float y = Screen.height - screen.y;
            if (x < -40f || x > Screen.width + 40f || y < -40f || y > Screen.height + 40f) return;

            if (MarkerTex == null)
            {
                MarkerTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                MarkerTex.SetPixel(0, 0, Color.white);
                MarkerTex.Apply(false, true);
            }

            Color prior = GUI.color;
            try
            {
                GUI.color = new Color(1f, .82f, .28f, .9f);
                float arm = 10f;
                float thick = 2f;
                GUI.DrawTexture(new Rect(x - arm, y - thick * .5f, arm * 2f, thick), MarkerTex);
                GUI.DrawTexture(new Rect(x - thick * .5f, y - arm, thick, arm * 2f), MarkerTex);
                GUI.color = new Color(1f, .95f, .55f, .95f);
                GUI.DrawTexture(new Rect(x - 3f, y - 3f, 6f, 6f), MarkerTex);
            }
            finally { GUI.color = prior; }
        }

        internal static Skills.SkillType ProjectileSkill(Projectile proj)
        {
            if (!proj) return Skills.SkillType.None;
            var skill = proj.m_skill;
            if (skill != Skills.SkillType.None) return skill;
            try
            {
                var weapon = Reflection.Get<ItemDrop.ItemData>(proj, "m_weapon");
                return weapon?.m_shared?.m_skillType ?? Skills.SkillType.None;
            }
            catch { return Skills.SkillType.None; }
        }

        /// <summary>Redirect fired vel toward predicted aim (after accuracy spread).</summary>
        internal static void ApplyProjectileRedirect(Projectile proj, Character owner, Vector3 velocity, PhaseFeatures phase, bool hardSnap)
        {
            if (phase == null || owner != Player.m_localPlayer || !proj) return;
            var skill = ProjectileSkill(proj);
            if (skill != Skills.SkillType.None && !IsRangedSkill(skill)) return;

            Vector3 currentVel = proj.GetVelocity();
            float speed = velocity.magnitude;
            if (speed < 0.5f) speed = currentVel.magnitude;
            if (speed < 0.5f) return;

            Vector3 from = proj.transform.position;
            Vector3 aim = currentVel.sqrMagnitude > 1e-4f ? currentVel.normalized : velocity.normalized;
            float range = Mathf.Clamp(phase.BowAimRange, 10f, 100f);
            float strength = Mathf.Clamp(phase.BowAimStrength, 10f, 100f) / 100f;
            float cone = Mathf.Clamp(phase.BowAimAngle, 5f, 75f);
            if (hardSnap || strength >= 0.995f) cone = Mathf.Min(90f, cone * 1.5f);
            var mode = (BowAimTargetMode)Mathf.Clamp(phase.BowAimTargetMode, 0, 2);

            var target = FindTarget(owner, from, aim, range, cone, phase.BowAimTargetPlayers, mode);
            if (!target) return;

            float gravity = EffectiveGravity(phase, null, proj);
            Vector3 dir = PredictAimDir(from, target, speed, gravity, out Vector3 lead);
            if (dir.sqrMagnitude < 1e-6f) return;

            Vector3 finalDir;
            if (hardSnap || strength >= 0.995f)
                finalDir = dir.normalized;
            else
            {
                float ang = Vector3.Angle(aim, dir);
                float falloff = cone > 0.01f ? Mathf.Clamp01(1f - ang / cone) : 1f;
                float t = strength >= 0.9f
                    ? Mathf.Lerp(strength * Mathf.Max(0.5f, falloff), strength, (strength - 0.9f) / 0.1f)
                    : strength * Mathf.Max(0.35f, falloff);
                finalDir = Vector3.Slerp(aim.normalized, dir.normalized, Mathf.Clamp01(t)).normalized;
            }

            try { Reflection.Set(proj, "m_vel", finalDir * speed); } catch { }
            if (phase.BowAimAssist && phase.ShowAimPredict)
            {
                PredictOrigin = from;
                PredictWorldPoint = lead;
                HasPredictMarker = true;
            }
        }

        internal static Character MeleeLock;
        internal static bool HasMeleeMarker;

        internal static void ClearMeleeLock()
        {
            MeleeLock = null;
            HasMeleeMarker = false;
        }

        internal static void TickMeleeLock()
        {
            var phase = Plugin.Instance?.Phase;
            var player = Player.m_localPlayer;
            if (phase?.MeleeTargetLock != true || !player || player.IsDead())
            {
                ClearMeleeLock();
                return;
            }
            float range = MeleeReach(player);
            if (MeleeLock && !MeleeLock.IsDead())
            {
                float keep = Vector3.Distance(player.transform.position, MeleeLock.GetCenterPoint());
                if (keep <= range + 1.25f)
                {
                    HasMeleeMarker = true;
                    return;
                }
            }
            var mode = (BowAimTargetMode)Mathf.Clamp(phase.BowAimTargetMode, 0, 2);
            MeleeLock = FindTarget(player, player.GetEyePoint(), player.GetLookDir(), range, 70f, phase.MeleeLockPlayers, mode);
            HasMeleeMarker = MeleeLock;
        }

        internal static float MeleeReach(Player player)
        {
            float range = 2.4f;
            try
            {
                var weapon = player.GetCurrentWeapon();
                var attack = weapon?.m_shared?.m_attack;
                if (attack != null && !IsProjectileAttack(attack))
                    range = Mathf.Max(1.5f, attack.m_attackRange);
            }
            catch { }
            return range;
        }

        internal static void FaceMeleeLock(Player player)
        {
            if (!MeleeLock || !player) return;
            float range = MeleeReach(player);
            Vector3 to = MeleeLock.GetCenterPoint() - player.transform.position;
            if (new Vector2(to.x, to.z).magnitude > range + 0.75f) return;
            Vector3 flat = to;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return;
            player.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            try { Reflection.Set(player, "m_lookDir", to.normalized); } catch { }
        }

        internal static void DrawMeleeLock()
        {
            if (!HasMeleeMarker || !MeleeLock) return;
            if (!Player.m_localPlayer || Event.current == null || Event.current.type != EventType.Repaint) return;
            var camera = Camera.main;
            if (!camera) return;
            Vector3 screen = camera.WorldToScreenPoint(MeleeLock.GetCenterPoint());
            if (screen.z <= 0f) return;
            float x = screen.x;
            float y = Screen.height - screen.y;
            if (MarkerTex == null)
            {
                MarkerTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                MarkerTex.SetPixel(0, 0, Color.white);
                MarkerTex.Apply(false, true);
            }
            Color prior = GUI.color;
            try
            {
                GUI.color = new Color(1f, .35f, .3f, .9f);
                float arm = 8f;
                float thick = 2f;
                GUI.DrawTexture(new Rect(x - arm, y - thick * .5f, arm * 2f, thick), MarkerTex);
                GUI.DrawTexture(new Rect(x - thick * .5f, y - arm, thick, arm * 2f), MarkerTex);
            }
            finally { GUI.color = prior; }
        }
    }

    [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
    internal static class MeleeTargetLockPatch
    {
        private static void Prefix(Attack __instance)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase?.MeleeTargetLock != true) return;
            Humanoid human = null;
            try { human = Reflection.Get<Humanoid>(__instance, "m_character"); } catch { return; }
            if (human != Player.m_localPlayer) return;
            if (BowAim.IsProjectileAttack(__instance)) return;
            BowAim.TickMeleeLock();
            BowAim.FaceMeleeLock(Player.m_localPlayer);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetAimDir))]
    internal static class BowAimDirPatch
    {
        private static void Postfix(Humanoid __instance, Vector3 fromPoint, ref Vector3 __result)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase?.BowAimAssist != true) return;
            if (!BowAim.LocalProjectileContext(__instance, out var attack, out float drawPct)) return;
            if (attack != null && !BowAim.IsProjectileAttack(attack)) return;
            var skill = attack?.GetWeapon()?.m_shared?.m_skillType
                ?? __instance.GetCurrentWeapon()?.m_shared?.m_skillType
                ?? Skills.SkillType.None;
            if (skill != Skills.SkillType.None && !BowAim.IsRangedSkill(skill)) return;
            __result = BowAim.AssistAim(__result, fromPoint, attack, drawPct, phase, hardSnap: false);
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class ArrowGravitySetupPatch
    {
        private static void Postfix(Projectile __instance, Character owner)
        {
            var phase = Plugin.Instance?.Phase;
            if (!BowAim.ShouldZeroProjectileGravity(phase, owner, __instance)) return;
            __instance.m_gravity = 0f;
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class BowProjectileRedirectPatch
    {
        private static void Postfix(Projectile __instance, Character owner, Vector3 velocity)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || owner != Player.m_localPlayer || !__instance) return;
            bool magic = phase.MagicBullet;
            bool soft = phase.BowAimAssist;
            if (!magic && !soft) return;
            // Magic bullet hard-snaps; soft assist blends (full snap at strength 100).
            BowAim.ApplyProjectileRedirect(__instance, owner, velocity, phase, hardSnap: magic);
        }
    }
}
