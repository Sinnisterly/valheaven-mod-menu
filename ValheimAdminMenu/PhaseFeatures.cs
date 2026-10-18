using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ValheimAdminMenu
{
    internal sealed class PhaseFeatures
    {
        private readonly Plugin app;
        internal bool OneHitKill, OneHitCreatures, OneHitStructures, InfiniteJumps, MoveUnderwater, NoWaterStaminaDamage;
        internal bool AlwaysNearFire, NeverFreeze, KeepInventory, KeepFoodOnDeath, NoBedRestrictions;
        internal bool InfFood, InfFoodBuff, NoFoodDecay, AlwaysCanEat, NoFoodCooldown;
        internal bool InstantGuardianCooldown, HoldGuardianPower;
        internal bool DrawBowImmediate, InfArrows, NoToolDelay;
        internal bool MultiArrow, MeleeMultiHit;
        internal bool PierceShots, MeleeCleave, AutoParry, NoSpecialCooldown;
        internal bool BowAimAssist, MagicBullet, BowAimTargetPlayers, ShowAimPredict, NoArrowGravity;
        internal int BowAimTargetMode;
        internal float BowAimStrength = 75, BowAimAngle = 28, BowAimRange = 50;
        internal float WalkSpeedMult = 1, RunSpeedMult = 1, SwimSpeedMult = 1, SneakSpeedMult = 1;
                internal sealed class QueuedDrop {
            internal string Prefab;
            internal int Amount, Quality, Variant;
            internal string Crafter;
            internal bool NoCrafter;
            internal ItemEdits Edits;
        }
        internal System.Collections.Generic.List<QueuedDrop> OnKillDrops = new System.Collections.Generic.List<QueuedDrop>();
        internal bool PersistentOnKillDrops = false;
        internal bool ForceTrophy; internal int ForceTrophyCount = 1;
        internal bool UnlimitedBait, AlwaysBite, AttractFish, InstantHook, InstantReel, NoFishEscape, UnbreakableLine;
        internal bool BoatBoost, BoatStable, BoatFly, EasyCart, BoatInfHealth, CartInfHealth;
        internal bool FreeCraft, CraftFromChests, BuildEverywhere, IgnoreUnstable, IgnorePlayerBlock, IgnoreMysticalForce;
        internal float CraftChestRadius = 15;
        internal bool ForgeAlwaysSucceed, ForgeNeverBreak;
        internal bool InfWorkbenchRadius, WorkbenchNoRoof, StationLevel10;
        internal bool IndestructibleBuildings, NoRainDamage, NoUnderwaterBuildingDamage;
        internal bool InfStationFuel, FireplaceNoWet, InstantCharcoal, StationSlots999, StationAlwaysOn;
        internal bool NoEncumber, ItemMagnet, InfStackSplit;
        internal bool ForceWind, DisableMist, HideHud;
        internal bool NoFallDamage, Noclip, LockRested, PreventSkillLoss, FreezeSkills, FreezeXp;
        internal bool FreezeAiNearby, FreezeAiGlobal;
        internal bool DisableRaids;
        internal bool AllowOverlap, FineBuildRotation, ExtendedBuildDistance, StaggerProtection;
        internal bool InstantCook, InstantFerment, InstantSmelt, InstantPlantGrow;
        internal float OutgoingDamageMult = 1, IncomingDamageMult = 1, ProjectileSpeedMult = 1;
        internal float HitPushMult = 1, BuildDistanceMult = 1, ProductionSpeedMult = 1;
        internal float CarryMultiplier = 1, MagnetRadius = 20, StackPickupMultiplier = 1, PickaxeRadius = 1, ExploreRadius = 1;
        internal float FovScale = 1, CameraDistance = 1, BoatCameraDistance = 1;
        internal float ArrowCount = 3, ArrowSpread = 5, MeleeHits = 3;
        internal float CleaveAngleBonus = 20, PerfectBlockWindow = 1, BoatSpeedMult = 2, CartEase = 3;
        internal int ItemSpawnMode; // 0 inventory, 1 chest, 2 ground
        internal bool WeaponVfx, FeetTrailVfx, AutoPickupFilter, RepairHammerAoe;
        internal bool AutoFeedTames, Freecam, PhotoMode, PhotoFreeze, FreecamBuild;
        internal bool MinimapZoomControl, TpToPing, DisablePortalFade, SitMountAnywhere;
        internal bool MeleeTargetLock, MeleeLockPlayers, FlyingMount, MountAnyCreature;
        internal bool GroundSpawnAtCrosshair = true;
        private float magnetAt, originalCamMax = -1, originalBoatMax = -1, originalFov = -1;
        private float lastCameraDistance = 1, lastBoatCameraDistance = 1, lastFovScale = 1;
        private float mistScanAt, cartScanAt, cartMassAt;
        private float foodAt, nearFireAt, restedAt, freezeAt, wetAt, guardianHoldAt;
        private float lastWindAngle = float.NaN;
        private bool windOwned;
        private bool hudHiddenByUs;
        private bool cameraDirty = true;
        private readonly List<ParticleMist> mistHidden = new List<ParticleMist>();
        private readonly List<Collider> noclipDisabled = new List<Collider>();
        private bool noclipOwned;
        private bool freecamForcedNoclip;
        private Vagon cartRef;
        private float cartBaseMass, cartWeightFactor, cartBreak, cartSpring;
        private float appliedCartEase = float.NaN;
        private bool cartOwned;
        private static FieldInfo foodItemField, foodTimeField;
        private static Type foodEntryType;
        private static readonly int FreezingHash = "Freezing".GetStableHashCode();
        internal PhaseFeatures(Plugin app) { this.app = app; }
        internal void Reset()
        {
            
            lastCameraDistance = lastBoatCameraDistance = lastFovScale = 1;
            appliedCartEase = float.NaN;
            lastWindAngle = float.NaN;
            cameraDirty = true;
            foodAt = nearFireAt = restedAt = freezeAt = wetAt = guardianHoldAt = 0;
            if (windOwned && EnvMan.instance) EnvMan.instance.ResetDebugWind();
            windOwned = false;
            RestoreCart();
            RestoreMist();
            RestoreNoclip();
            RestoreCameraDefaults();
            Release250.ResetSession();
            InventoryCapacity.Reset();
            if (Mathf.Abs(Time.timeScale - 1f) > .001f) Time.timeScale = 1f;
            // New GameCamera after scene change needs fresh baselines.
            originalCamMax = originalBoatMax = originalFov = -1;
            ApplyHud();
        }
        internal void Tick()
        {
            var p = Player.m_localPlayer;
            if (!p || p.IsDead()) { RestoreNoclip(); ApplyHud(); Release250.Tick(); return; }
            float now = Time.unscaledTime;
            // Fire/rested/wet/freeze don't need per-frame writes.
            if (AlwaysNearFire && now >= nearFireAt) { nearFireAt = now + 0.2f; Reflection.Set(p, "m_nearFireTimer", 0f); }
            if (LockRested && now >= restedAt) { restedAt = now + .5f; FeatureSuite.MaintainRested(p); }
            if (FreecamBuild && !Noclip) { freecamForcedNoclip = true; Noclip = true; }
            if (!FreecamBuild && freecamForcedNoclip) { freecamForcedNoclip = false; Noclip = false; }
            if (Noclip) ApplyNoclip(p); else RestoreNoclip();
            if (NeverFreeze && now >= freezeAt)
            {
                freezeAt = now + .5f;
                p.GetSEMan()?.RemoveStatusEffect(FreezingHash, true);
            }
            if (InstantGuardianCooldown) p.m_guardianPowerCooldown = 0;
            if (HoldGuardianPower && now >= guardianHoldAt)
            {
                guardianHoldAt = now + 1f;
                string power = p.GetGuardianPowerName();
                if (!string.IsNullOrEmpty(power))
                {
                    var effect = ObjectDB.instance?.GetStatusEffect(power.GetStableHashCode());
                    if (effect && p.GetSEMan().GetStatusEffect(effect.NameHash()) == null)
                        p.GetSEMan().AddStatusEffect(effect.NameHash(), true);
                }
            }
            if ((InfFood || InfFoodBuff || NoFoodDecay) && now >= foodAt)
            {
                foodAt = now + .35f;
                TopFood(p);
            }
            if (NoWaterStaminaDamage && now >= wetAt)
            {
                wetAt = now + .5f;
                p.GetSEMan()?.RemoveStatusEffect(SEMan.s_statusEffectWet, true);
            }
            // Infinite jumps / instant bow are Harmony-only. Don't force ground/draw every tick.
            if (ItemMagnet)
            {
                // Prefer ItemDrop.s_instances; throttle the FindObjects fallback.
                if (!PullItems(p) && now >= magnetAt)
                {
                    magnetAt = now + .25f;
                    PullItemsFallback(p);
                }
            }
            if (ForceWind && EnvMan.instance)
            {
                float angle = p.transform.eulerAngles.y;
                if (!windOwned || float.IsNaN(lastWindAngle) || Mathf.Abs(Mathf.DeltaAngle(lastWindAngle, angle)) > .5f)
                {
                    EnvMan.instance.m_debugWind = true;
                    EnvMan.instance.m_debugWindAngle = angle;
                    lastWindAngle = angle;
                }
                windOwned = true;
            }
            else if (windOwned)
            {
                if (EnvMan.instance) EnvMan.instance.ResetDebugWind();
                windOwned = false;
                lastWindAngle = float.NaN;
            }
            if (DisableMist) HideMist();
            else if (mistHidden.Count > 0) RestoreMist();
            ApplyCart(p);
            ApplyCamera();
            ApplyHud();
            try { Release250.Tick(); } catch { }
            try { BowAim.TickPredict(); }
            catch { BowAim.ClearPredictMarker(); }
            try { BowAim.TickMeleeLock(); }
            catch { BowAim.ClearMeleeLock(); }
        }
        private void ApplyCart(Player p)
        {
            if (!EasyCart)
            {
                RestoreCart();
                return;
            }
            // Prefer cached hitch over FindObjectsByType every frame.
            if (cartOwned && cartRef && cartRef.IsAttached())
            {
                GameObject hitch = null;
                try { hitch = Reflection.Get<GameObject>(cartRef, "m_attachedObject"); } catch { }
                if (hitch == p.gameObject)
                {
                    ApplyCartEase();
                    return;
                }
            }
            if (Time.unscaledTime < cartScanAt) return;
            cartScanAt = Time.unscaledTime + .5f;
            Vagon attached = null;
            foreach (var wagon in Object.FindObjectsByType<Vagon>(FindObjectsSortMode.None))
            {
                if (!wagon || !wagon.IsAttached()) continue;
                // m_attachedObject is private on Vagon.
                GameObject hitch = null;
                try { hitch = Reflection.Get<GameObject>(wagon, "m_attachedObject"); } catch { }
                if (hitch == p.gameObject) { attached = wagon; break; }
            }
            if (!attached)
            {
                RestoreCart();
                return;
            }
            if (!cartOwned || cartRef != attached)
            {
                RestoreCart();
                cartRef = attached;
                cartBaseMass = attached.m_baseMass;
                cartWeightFactor = attached.m_itemWeightMassFactor;
                cartBreak = attached.m_breakForce;
                cartSpring = attached.m_spring;
                cartOwned = true;
            }
            ApplyCartEase();
        }
        private void ApplyCartEase()
        {
            if (!cartOwned || !cartRef) return;
            float ease = Mathf.Clamp(CartEase, 1f, 10f);
            bool valuesChanged = float.IsNaN(appliedCartEase) || Mathf.Abs(appliedCartEase - ease) > .001f;
            if (valuesChanged)
            {
                cartRef.m_baseMass = Mathf.Max(.1f, cartBaseMass / ease);
                cartRef.m_itemWeightMassFactor = Mathf.Max(.01f, cartWeightFactor / ease);
                cartRef.m_breakForce = cartBreak * ease;
                cartRef.m_spring = cartSpring * Mathf.Lerp(1f, 1.5f, (ease - 1f) / 9f);
                appliedCartEase = ease;
            }
            // UpdateMass on ease change or a slow heartbeat (cargo can change).
            if (!valuesChanged && Time.unscaledTime < cartMassAt) return;
            cartMassAt = Time.unscaledTime + .5f;
            try { Reflection.Method(typeof(Vagon), "UpdateMass").Invoke(cartRef, null); } catch { }
        }
        private void RestoreCart()
        {
            if (!cartOwned || !cartRef) { cartOwned = false; cartRef = null; appliedCartEase = float.NaN; return; }
            cartRef.m_baseMass = cartBaseMass;
            cartRef.m_itemWeightMassFactor = cartWeightFactor;
            cartRef.m_breakForce = cartBreak;
            cartRef.m_spring = cartSpring;
            try { Reflection.Method(typeof(Vagon), "UpdateMass").Invoke(cartRef, null); } catch { }
            cartOwned = false;
            cartRef = null;
            appliedCartEase = float.NaN;
        }
        private void HideMist()
        {
            if (Time.unscaledTime < mistScanAt) return;
            mistScanAt = Time.unscaledTime + .5f;
            foreach (var mist in Object.FindObjectsByType<ParticleMist>(FindObjectsSortMode.None))
            {
                if (!mist || !mist.gameObject.activeSelf) continue;
                mist.gameObject.SetActive(false);
                if (!mistHidden.Contains(mist)) mistHidden.Add(mist);
            }
        }
        private void RestoreMist()
        {
            for (int i = 0; i < mistHidden.Count; i++)
            {
                var mist = mistHidden[i];
                if (mist) mist.gameObject.SetActive(true);
            }
            mistHidden.Clear();
        }
        private void ApplyNoclip(Player p)
        {
            if (!p) return;
            if (!noclipOwned)
            {
                noclipDisabled.Clear();
                foreach (var col in p.GetComponentsInChildren<Collider>(true))
                {
                    if (!col || col.isTrigger || !col.enabled) continue;
                    noclipDisabled.Add(col);
                    col.enabled = false;
                }
                noclipOwned = true;
            }
        }
        private void RestoreNoclip()
        {
            if (!noclipOwned) return;
            for (int i = 0; i < noclipDisabled.Count; i++)
            {
                var col = noclipDisabled[i];
                if (col) col.enabled = true;
            }
            noclipDisabled.Clear();
            noclipOwned = false;
        }
        private void TopFood(Player p)
        {
            var foods = Reflection.Get<IList>(p, "m_foods");
            if (foods == null || foods.Count == 0) return;
            for (int i = 0; i < foods.Count; i++)
            {
                var entry = foods[i];
                if (entry == null) continue;
                var type = entry.GetType();
                if (foodEntryType != type)
                {
                    foodEntryType = type;
                    foodItemField = type.GetField("m_item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    foodTimeField = type.GetField("m_time", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (foodItemField == null || foodTimeField == null) continue;
                var item = foodItemField.GetValue(entry) as ItemDrop.ItemData;
                float max = item?.m_shared?.m_foodBurnTime ?? 0;
                if (max <= 0) continue;
                foodTimeField.SetValue(entry, max);
            }
        }
        /// <summary>False if s_instances is missing (caller can fallback-scan).</summary>
        private bool PullItems(Player p)
        {
            List<ItemDrop> drops = null;
            try { drops = Reflection.GetStatic<List<ItemDrop>>(typeof(ItemDrop), "s_instances"); } catch { drops = null; }
            if (drops == null) return false;
            PullItemsCore(p, drops);
            return true;
        }
        private void PullItemsFallback(Player p)
        {
            PullItemsCore(p, Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None));
        }
        private static void PullItemsCore(Player p, IList<ItemDrop> drops)
        {
            if (drops == null || drops.Count == 0) return;
            float r = Mathf.Clamp(Plugin.Instance.Phase.MagnetRadius, 1, 100);
            float r2 = r * r;
            Vector3 target = p.transform.position + Vector3.up;
            Vector3 origin = p.transform.position;
            float step = 14f * Time.deltaTime;
            for (int i = 0; i < drops.Count; i++)
            {
                var drop = drops[i];
                if (!drop || !drop.m_autoPickup) continue;
                if (!Release250.AllowAutoPickup(drop)) continue;
                if ((drop.transform.position - origin).sqrMagnitude > r2) continue;
                var view = drop.GetComponent<ZNetView>();
                if (!view || !view.IsValid()) continue;
                if (!view.IsOwner()) view.ClaimOwnership();
                drop.transform.position = Vector3.MoveTowards(drop.transform.position, target, step);
            }
        }
        private void ApplyCamera()
        {
            if (!GameCamera.instance) return;
            var cam = GameCamera.instance;
            if (originalCamMax < 0)
            {
                originalCamMax = cam.m_maxDistance;
                originalBoatMax = cam.m_maxDistanceBoat;
                originalFov = cam.m_fov > 1f ? cam.m_fov : 65f;
                cameraDirty = true;
            }
            float distScale = Mathf.Clamp(CameraDistance, .5f, 5f);
            float boatScale = Mathf.Clamp(BoatCameraDistance, .5f, 5f);
            float fovScale = Mathf.Clamp(FovScale, .5f, 2f);
            bool scaleChanged = cameraDirty
                || Mathf.Abs(distScale - lastCameraDistance) > .001f
                || Mathf.Abs(boatScale - lastBoatCameraDistance) > .001f
                || Mathf.Abs(fovScale - lastFovScale) > .001f;
            if (!scaleChanged) return;

            float prevMax = cam.m_maxDistance;
            float prevBoatMax = cam.m_maxDistanceBoat;
            cam.m_maxDistance = originalCamMax * distScale;
            cam.m_maxDistanceBoat = originalBoatMax * boatScale;
            // UpdateCamera copies m_fov onto the Unity cameras every frame from these fields.
            float fov = Mathf.Clamp(originalFov * fovScale, 30f, 120f);
            cam.m_fov = fov;
            try
            {
                Reflection.Set(cam, "m_fovBase", fov);
                Reflection.Set(cam, "m_fovTarget", fov);
                float distance = Reflection.Get<float>(cam, "m_distance");
                bool onShip = Player.m_localPlayer && Player.m_localPlayer.GetControlledShip();
                float activeMax = onShip ? cam.m_maxDistanceBoat : cam.m_maxDistance;
                float prevActive = onShip ? prevBoatMax : prevMax;
                bool zoomScaleChanged = Mathf.Abs(distScale - lastCameraDistance) > .001f || Mathf.Abs(boatScale - lastBoatCameraDistance) > .001f;
                if (zoomScaleChanged && prevActive > cam.m_minDistance)
                {
                    float t = Mathf.InverseLerp(cam.m_minDistance, prevActive, distance);
                    distance = Mathf.Lerp(cam.m_minDistance, activeMax, t);
                }
                else
                    distance = Mathf.Clamp(distance, cam.m_minDistance, activeMax);
                Reflection.Set(cam, "m_distance", distance);
            }
            catch { }
            lastCameraDistance = distScale;
            lastBoatCameraDistance = boatScale;
            lastFovScale = fovScale;
            cameraDirty = false;
        }
        private void RestoreCameraDefaults()
        {
            if (!GameCamera.instance || originalCamMax < 0) return;
            var cam = GameCamera.instance;
            cam.m_maxDistance = originalCamMax;
            cam.m_maxDistanceBoat = originalBoatMax;
            if (originalFov > 0)
            {
                cam.m_fov = originalFov;
                try
                {
                    Reflection.Set(cam, "m_fovBase", originalFov);
                    Reflection.Set(cam, "m_fovTarget", originalFov);
                    float distance = Reflection.Get<float>(cam, "m_distance");
                    Reflection.Set(cam, "m_distance", Mathf.Clamp(distance, cam.m_minDistance, cam.m_maxDistance));
                }
                catch { }
            }
        }
        private void ApplyHud()
        {
            if (!Hud.instance) { hudHiddenByUs = false; return; }
            var go = Hud.instance.gameObject;
            bool hide = HideHud || PhotoMode;
            if (hide)
            {
                if (go.activeSelf)
                {
                    go.SetActive(false);
                    hudHiddenByUs = true;
                }
            }
            else if (hudHiddenByUs)
            {
                go.SetActive(true);
                hudHiddenByUs = false;
            }
        }
        internal void RepairNearby()
        {
            app.RequireWorld();
            var center = app.RequirePlayer().transform.position;
            float r = app.World.Radius; int n = 0;
            foreach (var wear in Object.FindObjectsByType<WearNTear>(FindObjectsSortMode.None))
            {
                if (!wear || (wear.transform.position - center).sqrMagnitude > r * r) continue;
                var view = wear.GetComponent<ZNetView>();
                if (view && view.IsValid() && !view.IsOwner()) view.ClaimOwnership();
                wear.Repair();
                n++;
            }
            app.Report("Repaired " + n + " nearby objects.");
        }
        internal void DestroyNearby()
        {
            app.RequireWorld();
            var center = app.RequirePlayer().transform.position;
            float r = app.World.Radius; int n = 0;
            foreach (var wear in Object.FindObjectsByType<WearNTear>(FindObjectsSortMode.None).Where(w => w && (w.transform.position - center).sqrMagnitude <= r * r).Take(500).ToList())
            {
                var view = wear.GetComponent<ZNetView>();
                if (!view || !view.IsValid()) continue;
                if (!view.IsOwner()) view.ClaimOwnership();
                view.Destroy();
                n++;
            }
            app.Report("Destroyed " + n + " nearby objects.");
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class OneHitKillPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || hit == null || __instance is Player) return;
            // OneHitKill is the legacy name; OneHitCreatures is the split toggle.
            if (!phase.OneHitKill && !phase.OneHitCreatures) return;
            if (hit.GetAttacker() != Player.m_localPlayer) return;
            float health = __instance.GetHealth();
            if (health > 0) hit.m_damage.m_damage = Mathf.Max(hit.m_damage.m_damage, health + 1f);
        }
    }
    [HarmonyPatch(typeof(Attack), "UseAmmo")]
    internal static class InfArrowPatch
    {
        // Still resolve ammo for the projectile; only skip the consume.
        private static bool Prefix(Attack __instance, ref ItemDrop.ItemData ammoItem, ref bool __result)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null) return true;
            var character = Reflection.Get<Humanoid>(__instance, "m_character");
            if (character != Player.m_localPlayer) return true;
            var weapon = Reflection.Get<ItemDrop.ItemData>(__instance, "m_weapon");
            bool fishingRod = weapon?.m_shared != null
                && weapon.m_shared.m_animationState == ItemDrop.ItemData.AnimationState.FishingRod;
            if (!phase.InfArrows && !(phase.UnlimitedBait && fishingRod)) return true;
            ammoItem = null;
            if (weapon != null && !string.IsNullOrWhiteSpace(weapon.m_shared.m_ammoType))
            {
                ammoItem = character.GetAmmoItem();
                if (ammoItem != null && (!character.GetInventory().ContainsItem(ammoItem) || ammoItem.m_shared.m_ammoType != weapon.m_shared.m_ammoType))
                    ammoItem = null;
                if (ammoItem == null)
                    ammoItem = character.GetInventory().GetAmmoItem(weapon.m_shared.m_ammoType);
                if (ammoItem == null)
                {
                    character.Message(MessageHud.MessageType.Center, "$msg_outof " + weapon.m_shared.m_ammoType);
                    __result = false;
                    return false;
                }
            }
            Reflection.Set(__instance, "m_ammoItem", ammoItem);
            __result = true;
            return false;
        }
    }
    [HarmonyPatch(typeof(Attack), "FireProjectileBurst")]
    internal static class MultiArrowPatch
    {
        private struct State
        {
            internal bool active;
            internal int projectiles;
            internal float accuracy, accuracyMin;
            internal bool destroyPrevious;
            internal int fired;
        }
        private static void Prefix(Attack __instance, out State __state)
        {
            __state = default;
            var phase = Plugin.Instance?.Phase;
            if (phase?.MultiArrow != true) return;
            if (Reflection.Get<Humanoid>(__instance, "m_character") != Player.m_localPlayer) return;
            int count = Mathf.Clamp(Mathf.RoundToInt(phase.ArrowCount), 2, 12);
            float spread = Mathf.Clamp(phase.ArrowSpread, 0, 45);
            try
            {
                __state.projectiles = Reflection.Get<int>(__instance, "m_projectiles");
                __state.accuracy = Reflection.Get<float>(__instance, "m_projectileAccuracy");
                __state.accuracyMin = Reflection.Get<float>(__instance, "m_projectileAccuracyMin");
                __state.destroyPrevious = Reflection.Get<bool>(__instance, "m_destroyPreviousProjectile");
                Reflection.Set(__instance, "m_projectiles", count);
                Reflection.Set(__instance, "m_projectileAccuracy", __state.accuracy + spread);
                Reflection.Set(__instance, "m_projectileAccuracyMin", __state.accuracyMin + spread);
                Reflection.Set(__instance, "m_destroyPreviousProjectile", false);
                __state.fired = count;
                __state.active = true;
            }
            catch { __state = default; }
        }
        private static void Finalizer(Attack __instance, State __state)
        {
            if (!__state.active) return;
            try
            {
                Reflection.Set(__instance, "m_projectiles", __state.projectiles);
                Reflection.Set(__instance, "m_projectileAccuracy", __state.accuracy);
                Reflection.Set(__instance, "m_projectileAccuracyMin", __state.accuracyMin);
                Reflection.Set(__instance, "m_destroyPreviousProjectile", __state.destroyPrevious);
            }
            catch { }
            // Extra shots each drain durability (vanilla already drained once on trigger).
            if (__state.fired <= 1 || Plugin.Instance?.Expansion.Durability == true) return;
            var weapon = Reflection.Get<ItemDrop.ItemData>(__instance, "m_weapon");
            var character = Reflection.Get<Humanoid>(__instance, "m_character");
            if (weapon?.m_shared == null || !weapon.m_shared.m_useDurability || character == null || !character.IsPlayer()) return;
            weapon.m_durability -= weapon.m_shared.m_useDurabilityDrain * Game.m_durabilityRate * (__state.fired - 1);
        }
    }
    [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
    internal static class MeleeMultiHitPatch
    {
        private static int reentry;
        private static void Postfix(Attack __instance)
        {
            if (reentry > 0) return;
            var phase = Plugin.Instance?.Phase;
            if (phase?.MeleeMultiHit != true) return;
            if (Reflection.Get<Humanoid>(__instance, "m_character") != Player.m_localPlayer) return;
            int hits = Mathf.Clamp(Mathf.RoundToInt(phase.MeleeHits), 2, 10);
            var method = Reflection.Method(typeof(Attack), "DoMeleeAttack");
            reentry = 1;
            try
            {
                for (int i = 1; i < hits; i++)
                    method.Invoke(__instance, null);
            }
            finally { reentry = 0; }
        }
    }
    [HarmonyPatch(typeof(Attack), nameof(Attack.StartDraw))]
    internal static class InstantBowDrawStartPatch
    {
        private static void Postfix(Humanoid character, bool __result)
        {
            if (!__result || Plugin.Instance?.Phase.DrawBowImmediate != true || character != Player.m_localPlayer) return;
            try { Reflection.Set(character, "m_attackDrawTime", 100f); } catch { }
        }
    }
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetAttackDrawPercentage))]
    internal static class InstantBowDrawPercentPatch
    {
        private static void Postfix(Humanoid __instance, ref float __result)
        {
            if (Plugin.Instance?.Phase.DrawBowImmediate != true || __instance != Player.m_localPlayer) return;
            if (__result > 0f) __result = 1f;
        }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
    [HarmonyPriority(Priority.First)]
    internal static class InfiniteJumpPatch
    {
        // Clear ground-touch only inside Jump. Forcing it every tick clamps Y vel to 3 (kills super jump).
        private static void Prefix(Character __instance)
        {
            if (Plugin.Instance?.Phase.InfiniteJumps != true || __instance != Player.m_localPlayer) return;
            try { Reflection.Set(__instance, "m_lastGroundTouch", 0f); } catch { }
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.CanEat))]
    internal static class AlwaysEatPatch
    {
        private static void Postfix(Player __instance, ItemDrop.ItemData item, ref bool __result)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || __instance != Player.m_localPlayer || item == null) return;
            if (phase.NoFoodCooldown || phase.AlwaysCanEat) __result = true;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.EatFood))]
    internal static class EatFoodPhasePatch
    {
        // CanEat isn't enough; EatFood still checks CanEatAgain / GetMostDepletedFood.
        private static bool Prefix(Player __instance, ItemDrop.ItemData item, ref bool __result)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || __instance != Player.m_localPlayer || item == null) return true;
            if (!phase.AlwaysCanEat && !phase.NoFoodCooldown) return true;
            var foods = Reflection.Get<System.Collections.IList>(__instance, "m_foods");
            if (foods == null) return true;
            foreach (var entry in foods)
            {
                if (entry == null) continue;
                var type = entry.GetType();
                var existing = type.GetField("m_item")?.GetValue(entry) as ItemDrop.ItemData;
                if (existing == null || existing.m_shared.m_name != item.m_shared.m_name) continue;
                if (phase.NoFoodCooldown || phase.AlwaysCanEat)
                {
                    type.GetField("m_time")?.SetValue(entry, item.m_shared.m_foodBurnTime);
                    type.GetField("m_health")?.SetValue(entry, item.m_shared.m_food);
                    type.GetField("m_stamina")?.SetValue(entry, item.m_shared.m_foodStamina);
                    type.GetField("m_eitr")?.SetValue(entry, item.m_shared.m_foodEitr);
                    Reflection.Method(typeof(Player), "UpdateFood", typeof(float), typeof(bool)).Invoke(__instance, new object[] { 0f, true });
                    __result = true;
                    return false;
                }
            }
            if (!phase.AlwaysCanEat || foods.Count < 3) return true;
            object weakest = null; float weakestTime = float.MaxValue;
            foreach (var entry in foods)
            {
                if (entry == null) continue;
                float time = (float)(entry.GetType().GetField("m_time")?.GetValue(entry) ?? 0f);
                if (time < weakestTime) { weakestTime = time; weakest = entry; }
            }
            if (weakest == null) return true;
            var w = weakest.GetType();
            w.GetField("m_name")?.SetValue(weakest, item.m_dropPrefab ? item.m_dropPrefab.name : item.m_shared.m_name);
            w.GetField("m_item")?.SetValue(weakest, item);
            w.GetField("m_time")?.SetValue(weakest, item.m_shared.m_foodBurnTime);
            w.GetField("m_health")?.SetValue(weakest, item.m_shared.m_food);
            w.GetField("m_stamina")?.SetValue(weakest, item.m_shared.m_foodStamina);
            w.GetField("m_eitr")?.SetValue(weakest, item.m_shared.m_foodEitr);
            Reflection.Method(typeof(Player), "UpdateFood", typeof(float), typeof(bool)).Invoke(__instance, new object[] { 0f, true });
            __result = true;
            return false;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.CreateTombStone))]
    internal static class KeepInventoryPatch
    {
        private static bool Prefix(Player __instance)
        {
            return Plugin.Instance?.Phase.KeepInventory != true || __instance != Player.m_localPlayer;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.IsEncumbered))]
    internal static class NoEncumberPatch
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (Plugin.Instance && __instance == Player.m_localPlayer && Plugin.Instance.Phase.NoEncumber) __result = false;
        }
    }
    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class BuildEverywherePatch
    {
        private static void Postfix(Player __instance)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || __instance != Player.m_localPlayer) return;
            if (!phase.BuildEverywhere && !phase.IgnoreUnstable && !phase.IgnorePlayerBlock && !phase.IgnoreMysticalForce && !phase.AllowOverlap) return;
            try { Reflection.Set(__instance, "m_placementStatus", 0); } catch { }
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), new[] { typeof(Piece), typeof(Player.RequirementMode) })]
    internal static class FreeCraftPiecePatch
    {
        // Skip build costs only. Never IsKnown, or Free craft unlocks every piece/recipe.
        private static void Postfix(Player __instance, Piece piece, Player.RequirementMode mode, ref bool __result)
        {
            if (__result || mode != Player.RequirementMode.CanBuild) return;
            if (!Plugin.Instance || __instance != Player.m_localPlayer) return;
            var phase = Plugin.Instance.Phase;
            if (phase.FreeCraft) { __result = true; return; }
            if (phase.CraftFromChests) __result = ChestInventory.HavePieceMaterials(__instance, piece);
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), new[] { typeof(Recipe), typeof(bool), typeof(int), typeof(int) })]
    internal static class FreeCraftRecipePatch
    {
        private static void Postfix(Player __instance, bool discover, ref bool __result)
        {
            // discover:true unlocks recipes. Don't bypass that path.
            if (__result || discover) return;
            if (!Plugin.Instance || __instance != Player.m_localPlayer) return;
            if (Plugin.Instance.Phase.FreeCraft) __result = true;
        }
    }
    [HarmonyPatch(typeof(Player), "HaveRequirementItems")]
    internal static class CraftFromChestsHaveItemsPatch
    {
        private static void Postfix(Player __instance, Recipe piece, bool discover, int qualityLevel, int amount, ref bool __result)
        {
            if (__result || discover) return;
            if (!Plugin.Instance || __instance != Player.m_localPlayer) return;
            var phase = Plugin.Instance.Phase;
            if (phase.FreeCraft || !phase.CraftFromChests) return;
            __result = ChestInventory.HaveRecipeMaterials(__instance, piece, qualityLevel, amount);
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    internal static class CraftFromChestsConsumePatch
    {
        private static bool Prefix(Player __instance, Piece.Requirement[] requirements, int qualityLevel, int itemQuality, int multiplier)
        {
            if (!Plugin.Instance || __instance != Player.m_localPlayer) return true;
            var phase = Plugin.Instance.Phase;
            // Still calls ConsumeResources after a no-cost place. Skip that removal.
            if (phase.FreeCraft || __instance.PlacementCostDisabled) return false;
            if (!phase.CraftFromChests) return true;
            ChestInventory.ConsumeCraftMaterials(__instance, requirements, qualityLevel, itemQuality, multiplier);
            return false;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.NoCostCheat))]
    internal static class FreeCraftNoCostPatch
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            // Skips material consume without setting m_noPlacementCost (keeps recipe lists known-only).
            if (Plugin.Instance && __instance == Player.m_localPlayer && Plugin.Instance.Phase.FreeCraft) __result = true;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.GetAvailableRecipes))]
    internal static class FreeCraftRecipeListPatch
    {
        private static void Postfix(Player __instance, List<Recipe> available)
        {
            // Free craft must not show undiscovered recipes.
            if (!Plugin.Instance || __instance != Player.m_localPlayer || Plugin.Instance.Phase.FreeCraft != true) return;
            if (__instance.PlacementCostDisabled) return; // Free build still uses vanilla no-cost listing.
            if (available == null) return;
            available.RemoveAll(r => r?.m_item?.m_itemData?.m_shared == null || !__instance.IsRecipeKnown(r.m_item.m_itemData.m_shared.m_name));
        }
    }
    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    internal static class ForgeOfPotentialPatch
    {
        private struct ChanceState
        {
            internal ItemDrop.ItemData.SharedData shared;
            internal float upgrade, breakChance;
        }
        private static readonly List<ChanceState> restored = new List<ChanceState>();
        private static void Prefix(InventoryGui __instance)
        {
            restored.Clear();
            var phase = Plugin.Instance?.Phase;
            if (phase == null || (!phase.ForgeAlwaysSucceed && !phase.ForgeNeverBreak)) return;
            if (!Player.m_localPlayer) return;
            var station = Player.m_localPlayer.GetCurrentCraftingStation();
            if (!station || !station.m_upgrader) return;
            var recipe = Reflection.Get<Recipe>(__instance, "m_craftRecipe");
            if (recipe?.m_resources == null) return;
            foreach (var req in recipe.m_resources)
            {
                if (req == null || !req.m_upgraderResource || req.m_resItem?.m_itemData?.m_shared == null) continue;
                var shared = req.m_resItem.m_itemData.m_shared;
                restored.Add(new ChanceState { shared = shared, upgrade = shared.m_upgradeChance, breakChance = shared.m_breakChance });
                if (phase.ForgeAlwaysSucceed) shared.m_upgradeChance = 1f;
                if (phase.ForgeNeverBreak) shared.m_breakChance = 0f;
            }
        }
        private static void Finalizer()
        {
            foreach (var state in restored)
            {
                if (state.shared == null) continue;
                state.shared.m_upgradeChance = state.upgrade;
                state.shared.m_breakChance = state.breakChance;
            }
            restored.Clear();
        }
    }
    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.CheckUsable))]
    internal static class WorkbenchNoRoofPatch
    {
        private static void Postfix(Player player, ref bool __result)
        {
            if (Plugin.Instance?.Phase.WorkbenchNoRoof == true && player == Player.m_localPlayer) __result = true;
        }
    }
    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetLevel))]
    internal static class StationLevelPatch
    {
        private static void Postfix(ref int __result)
        {
            if (Plugin.Instance?.Phase.StationLevel10 == true) __result = Math.Max(__result, 10);
        }
    }
    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class ShipMovementBoostPatch
    {
        private struct State
        {
            public bool active;
            public float sail, back, stear, stearVel;
            public float angular, damp, side, forward;
        }

        // Propulsion is m_sailForceFactor / m_backwardForce / m_stearForce.
        // m_force is buoyancy, not thrust. Mutating it does nothing for speed.
        private static void Prefix(Ship __instance, out State __state)
        {
            __state = default;
            var phase = Plugin.Instance?.Phase;
            var player = Player.m_localPlayer;
            if (phase == null || !player || !__instance) return;
            if (!phase.BoatBoost && !phase.BoatStable) return;
            if (player.GetControlledShip() != __instance) return;

            __state.active = true;
            __state.sail = __instance.m_sailForceFactor;
            __state.back = __instance.m_backwardForce;
            __state.stear = __instance.m_stearForce;
            __state.stearVel = __instance.m_stearVelForceFactor;
            __state.angular = __instance.m_angularDamping;
            __state.damp = __instance.m_damping;
            __state.side = __instance.m_dampingSideway;
            __state.forward = __instance.m_dampingForward;

            if (phase.BoatBoost)
            {
                float speed = Mathf.Clamp(phase.BoatSpeedMult, 1f, 8f);
                __instance.m_sailForceFactor = __state.sail * speed;
                __instance.m_backwardForce = __state.back * speed;
                __instance.m_stearForce = __state.stear * Mathf.Lerp(1f, speed, .65f);
                __instance.m_stearVelForceFactor = __state.stearVel * Mathf.Lerp(1f, speed, .45f);
            }
            if (phase.BoatStable)
            {
                __instance.m_angularDamping = Mathf.Max(__state.angular * 3f, __state.angular + 1.5f);
                __instance.m_damping = Mathf.Max(__state.damp * 1.8f, __state.damp + .5f);
                __instance.m_dampingSideway = Mathf.Max(__state.side * 2f, __state.side + .5f);
                // Forward damping fights propulsion; skip while boosting.
                if (!phase.BoatBoost)
                    __instance.m_dampingForward = Mathf.Max(__state.forward * 1.5f, __state.forward + .3f);
            }
        }

        private static void Finalizer(Ship __instance, State __state)
        {
            if (!__state.active || !__instance) return;
            __instance.m_sailForceFactor = __state.sail;
            __instance.m_backwardForce = __state.back;
            __instance.m_stearForce = __state.stear;
            __instance.m_stearVelForceFactor = __state.stearVel;
            __instance.m_angularDamping = __state.angular;
            __instance.m_damping = __state.damp;
            __instance.m_dampingSideway = __state.side;
            __instance.m_dampingForward = __state.forward;
        }
    }

    // Boat fly: Jump must not exit the wheel (vanilla SetControls: jump while doodad -> StopDoodadControl).
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class BoatFlyKeepHelmPatch
    {
        private static void Prefix(Player __instance, ref bool jump)
        {
            if (!jump) return;
            var phase = Plugin.Instance?.Phase;
            if (phase?.BoatFly != true || __instance != Player.m_localPlayer) return;
            if (!__instance.GetControlledShip()) return;
            jump = false;
        }
    }

    // Boat fly: above water, CustomFixedUpdate early-returns past AddForce (depth > m_disableLevel).
    // ApplyControlls still sets m_speed / m_rudderValue before that; we drive the rigidbody from those.
    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class ShipBoatFlyPatch
    {
        private static Ship GravityOwnedShip;
        private static bool GravityOwnedValue = true;

        private static Rigidbody Body(Ship ship)
        {
            if (!ship) return null;
            try { return Reflection.Get<Rigidbody>(ship, "m_body"); }
            catch { return null; }
        }

        private static void RestoreGravity()
        {
            var body = Body(GravityOwnedShip);
            if (!body) { GravityOwnedShip = null; return; }
            body.useGravity = GravityOwnedValue;
            GravityOwnedShip = null;
        }

        private static float ThrustFactor(Ship.Speed speed)
        {
            switch (speed)
            {
                case Ship.Speed.Full: return 1f;
                case Ship.Speed.Half: return .55f;
                case Ship.Speed.Slow: return .35f;
                case Ship.Speed.Back: return -.5f;
                default: return 0f;
            }
        }

        private static void Postfix(Ship __instance)
        {
            var phase = Plugin.Instance?.Phase;
            var player = Player.m_localPlayer;
            bool active = phase?.BoatFly == true && player && __instance
                && !Plugin.Capturing
                && player.GetControlledShip() == __instance
                && __instance.IsOwner();

            if (!active)
            {
                if (GravityOwnedShip == __instance) RestoreGravity();
                return;
            }

            var body = Body(__instance);
            if (!body) return;

            if (GravityOwnedShip != __instance)
            {
                if (GravityOwnedShip) RestoreGravity();
                GravityOwnedShip = __instance;
                GravityOwnedValue = body.useGravity;
            }

            float flySpeed = 22f;
            if (phase.BoatBoost)
                flySpeed *= Mathf.Lerp(1f, Mathf.Clamp(phase.BoatSpeedMult, 1f, 8f), .5f);

            bool up = false;
            bool down = false;
            try
            {
                up = ZInput.GetButton("Jump") || ZInput.GetButton("JoyJump");
                down = ZInput.GetKey(KeyCode.LeftControl, true) || ZInput.GetButtonPressedTimer("JoyCrouch") > .33f;
            }
            catch { }

            Vector3 current = body.linearVelocity;
            float y = current.y;
            if (up) y = flySpeed;
            else if (down) y = -flySpeed;
            else y = Mathf.Lerp(current.y, 0f, .28f);

            // Drive XZ from sail/paddle; vanilla forces never run above water.
            float thrust = ThrustFactor(__instance.GetSpeedSetting()) * flySpeed;
            Vector3 fwd = __instance.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > .0001f) fwd.Normalize();
            else fwd = Vector3.forward;

            Vector3 desiredXZ = fwd * thrust;
            Vector3 curXZ = new Vector3(current.x, 0f, current.z);
            Vector3 xz = Vector3.Lerp(curXZ, desiredXZ, thrust == 0f ? .14f : .5f);
            body.linearVelocity = new Vector3(xz.x, y, xz.z);

            // Rudder yaw (vanilla steering AddForce also skipped above disableLevel).
            float rudder = __instance.GetRudderValue();
            float turnRate = 1.25f; // rad/s at full rudder
            Vector3 ang = body.angularVelocity;
            body.angularVelocity = new Vector3(
                Mathf.Lerp(ang.x, 0f, .45f),
                -rudder * turnRate,
                Mathf.Lerp(ang.z, 0f, .45f));

            // Flatten pitch/roll while airborne (no buoyancy).
            Quaternion rot = body.rotation;
            Vector3 euler = rot.eulerAngles;
            Quaternion flat = Quaternion.Euler(0f, euler.y, 0f);
            if (Quaternion.Angle(rot, flat) > .35f)
                body.MoveRotation(Quaternion.Slerp(rot, flat, .18f));

            body.useGravity = false;
        }
    }

    [HarmonyPatch(typeof(Character), "UpdateWalking")]
    internal static class PlayerMoveSpeedWalkPatch
    {
        private struct State
        {
            public bool active;
            public float speed, walk, run, crouch;
        }

        private static void Prefix(Character __instance, out State __state)
        {
            __state = default;
            var phase = Plugin.Instance?.Phase;
            if (phase == null || __instance != Player.m_localPlayer) return;
            if (__instance is Player p && p.IsDebugFlying()) return;
            float walk = Mathf.Clamp(phase.WalkSpeedMult, .1f, 10f);
            float run = Mathf.Clamp(phase.RunSpeedMult, .1f, 10f);
            float sneak = Mathf.Clamp(phase.SneakSpeedMult, .1f, 10f);
            if (Mathf.Abs(walk - 1f) < .001f && Mathf.Abs(run - 1f) < .001f && Mathf.Abs(sneak - 1f) < .001f) return;

            __state.active = true;
            __state.speed = __instance.m_speed;
            __state.walk = __instance.m_walkSpeed;
            __state.run = __instance.m_runSpeed;
            __state.crouch = __instance.m_crouchSpeed;
            // Walk mult hits both jog (m_speed) and walk-toggle (m_walkSpeed).
            __instance.m_speed = __state.speed * walk;
            __instance.m_walkSpeed = __state.walk * walk;
            __instance.m_runSpeed = __state.run * run;
            __instance.m_crouchSpeed = __state.crouch * sneak;
        }

        private static void Finalizer(Character __instance, State __state)
        {
            if (!__state.active || !__instance) return;
            __instance.m_speed = __state.speed;
            __instance.m_walkSpeed = __state.walk;
            __instance.m_runSpeed = __state.run;
            __instance.m_crouchSpeed = __state.crouch;
        }
    }

    [HarmonyPatch(typeof(Character), "UpdateSwimming")]
    internal static class PlayerMoveSpeedSwimPatch
    {
        private static void Prefix(Character __instance, out float? __state)
        {
            __state = null;
            var phase = Plugin.Instance?.Phase;
            if (phase == null || __instance != Player.m_localPlayer) return;
            if (__instance is Player p && p.IsDebugFlying()) return;
            float swim = Mathf.Clamp(phase.SwimSpeedMult, .1f, 10f);
            if (Mathf.Abs(swim - 1f) < .001f) return;
            __state = __instance.m_swimSpeed;
            __instance.m_swimSpeed = __state.Value * swim;
        }

        private static void Finalizer(Character __instance, float? __state)
        {
            if (!__state.HasValue || !__instance) return;
            __instance.m_swimSpeed = __state.Value;
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
    internal static class IndestructibleHitPatch
    {
        private static bool Prefix(WearNTear __instance) => !WearNTearInvuln.ShouldBlock(__instance);
    }
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.ApplyDamage))]
    internal static class IndestructibleWearPatch
    {
        private static bool Prefix(WearNTear __instance) => !WearNTearInvuln.ShouldBlock(__instance);
    }
    internal static class WearNTearInvuln
    {
        internal static bool ShouldBlock(WearNTear wear)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || !wear) return false;
            if (phase.IndestructibleBuildings) return true;
            if (phase.BoatInfHealth && IsLocalControlledShip(wear)) return true;
            if (phase.CartInfHealth && IsLocalAttachedCart(wear)) return true;
            return false;
        }

        private static bool IsLocalControlledShip(WearNTear wear)
        {
            var player = Player.m_localPlayer;
            if (!player) return false;
            var ship = player.GetControlledShip();
            if (!ship) return false;
            var wearShip = wear.GetComponentInParent<Ship>();
            return wearShip && wearShip == ship;
        }

        private static bool IsLocalAttachedCart(WearNTear wear)
        {
            var player = Player.m_localPlayer;
            if (!player) return false;
            var wagon = wear.GetComponentInParent<Vagon>();
            if (!wagon || !wagon.IsAttached()) return false;
            GameObject hitch = null;
            try { hitch = Reflection.Get<GameObject>(wagon, "m_attachedObject"); } catch { }
            return hitch == player.gameObject;
        }
    }
    [HarmonyPatch(typeof(WearNTear), "IsWet")]
    internal static class BuildingWetWearPatch
    {
        private static void Postfix(ref bool __result)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase != null && __result && (phase.NoRainDamage || phase.NoUnderwaterBuildingDamage)) __result = false;
        }
    }
    [HarmonyPatch(typeof(Smelter), "UpdateSmelter")]
    internal static class SmelterFuelPatch
    {
        private static void Prefix(Smelter __instance)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || (!phase.InfStationFuel && !phase.StationAlwaysOn && !phase.StationSlots999)) return;
            var view = __instance.GetComponent<ZNetView>();
            if (!view || !view.IsValid() || !view.IsOwner()) return;
            try
            {
                if (phase.InfStationFuel && __instance.m_maxFuel > 0)
                    view.GetZDO().Set(ZDOVars.s_fuel, Mathf.Max(10f, __instance.m_maxFuel));
                if (phase.StationAlwaysOn)
                {
                    Reflection.Set(__instance, "m_haveRoof", true);
                    Reflection.Set(__instance, "m_blockedSmoke", false);
                }
                if (phase.StationSlots999 && __instance.m_conversion != null && __instance.m_conversion.Count > 0)
                {
                    int queued = view.GetZDO().GetInt(ZDOVars.s_queued);
                    if (queued <= 0)
                    {
                        string ore = __instance.m_conversion[0].m_from?.m_itemData?.m_shared?.m_name;
                        if (!string.IsNullOrEmpty(ore))
                        {
                            // QueueOre writes item{N} + s_queued; s_queued alone leaves an empty bake.
                            view.GetZDO().Set("item0", __instance.m_conversion[0].m_from.gameObject.name);
                            view.GetZDO().Set(ZDOVars.s_queued, 1);
                            view.GetZDO().Set(ZDOVars.s_cheatedQueued, false);
                        }
                    }
                }
            }
            catch { }
        }
    }
    [HarmonyPatch(typeof(Fireplace), "UpdateFireplace")]
    internal static class FireplaceFuelPatch
    {
        private static void Prefix(Fireplace __instance)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || (!phase.InfStationFuel && !phase.FireplaceNoWet)) return;
            var view = __instance.GetComponent<ZNetView>();
            if (!view || !view.IsValid() || !view.IsOwner()) return;
            try
            {
                if (phase.InfStationFuel) view.GetZDO().Set(ZDOVars.s_fuel, __instance.m_maxFuel);
                if (phase.FireplaceNoWet) Reflection.Set(__instance, "m_wet", false);
            }
            catch { }
        }
    }
    [HarmonyPatch(typeof(Fireplace), "CheckWet")]
    internal static class FireplaceWetPatch
    {
        private static void Postfix(Fireplace __instance)
        {
            if (Plugin.Instance?.Phase.FireplaceNoWet == true)
                try { Reflection.Set(__instance, "m_wet", false); } catch { }
        }
    }
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Pickup))]
    internal static class StackPickupPatch
    {
        private static void Prefix(ItemDrop __instance)
        {
            float mult = Plugin.Instance?.Phase.StackPickupMultiplier ?? 1f;
            if (mult <= 1.01f || __instance?.m_itemData?.m_shared == null) return;
            int max = Math.Max(1, __instance.m_itemData.m_shared.m_maxStackSize);
            __instance.m_itemData.m_stack = Mathf.Clamp(Mathf.RoundToInt(__instance.m_itemData.m_stack * mult), 1, max);
            __instance.m_itemData.m_cheated = false;
        }
    }
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
    internal static class StackSplitMovePatch
    {
        private static int restoreAmount;
        private static ItemDrop.ItemData fullClone;
        private static void Prefix(Inventory fromInventory, ItemDrop.ItemData item, int amount)
        {
            restoreAmount = 0;
            fullClone = null;
            if (Plugin.Instance?.Phase.InfStackSplit != true || item == null || !Player.m_localPlayer) return;
            if (fromInventory != Player.m_localPlayer.GetInventory() || amount <= 0) return;
            if (amount >= item.m_stack)
            {
                fullClone = item.Clone();
                fullClone.m_stack = item.m_stack;
                fullClone.m_gridPos = item.m_gridPos;
                fullClone.m_cheated = false;
            }
            else restoreAmount = amount;
        }
        private static void Postfix(Inventory fromInventory, ItemDrop.ItemData item, bool __result)
        {
            if (!__result)
            {
                restoreAmount = 0; fullClone = null;
                return;
            }
            if (restoreAmount > 0 && item != null)
            {
                item.m_stack += restoreAmount;
                Reflection.Method(typeof(Inventory), "Changed", typeof(bool), typeof(bool)).Invoke(fromInventory, new object[] { true, false });
            }
            else if (fullClone != null && fromInventory != null && !fromInventory.ContainsItem(fullClone))
            {
                var slot = fullClone.m_gridPos;
                if (!fromInventory.AddItem(fullClone, slot))
                    fromInventory.AddItem(fullClone);
            }
            restoreAmount = 0; fullClone = null;
        }
    }
    [HarmonyPatch(typeof(Minimap), "Explore", new[] { typeof(Vector3), typeof(float) })]
    internal static class ExploreRadiusPatch
    {
        private static void Prefix(ref float radius)
        {
            float mult = Plugin.Instance?.Phase.ExploreRadius ?? 1f;
            if (mult > 0) radius *= Mathf.Clamp(mult, .1f, 20f);
        }
    }
    [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
    internal static class PickaxeRadiusPatch
    {
        private static void Prefix(Attack __instance, out float? __state)
        {
            __state = null;
            float mult = Plugin.Instance?.Phase.PickaxeRadius ?? 1f;
            if (Mathf.Abs(mult - 1f) < .01f) return;
            if (Reflection.Get<Humanoid>(__instance, "m_character") != Player.m_localPlayer) return;
            var weapon = Reflection.Get<ItemDrop.ItemData>(__instance, "m_weapon");
            if (weapon?.m_shared == null) return;
            if (weapon.m_shared.m_skillType != Skills.SkillType.Pickaxes && weapon.m_shared.m_skillType != Skills.SkillType.WoodCutting) return;
            try
            {
                __state = Reflection.Get<float>(__instance, "m_attackRange");
                Reflection.Set(__instance, "m_attackRange", __state.Value * Mathf.Clamp(mult, .5f, 5f));
            }
            catch { __state = null; }
        }
        private static void Finalizer(Attack __instance, float? __state)
        {
            if (!__state.HasValue) return;
            try { Reflection.Set(__instance, "m_attackRange", __state.Value); } catch { }
        }
    }
    [HarmonyPatch(typeof(Character), "ApplyLiquidResistance")]
    internal static class MoveUnderwaterPatch
    {
        private static bool Prefix(Character __instance)
        {
            return Plugin.Instance?.Phase.MoveUnderwater != true || __instance != Player.m_localPlayer;
        }
    }
    [HarmonyPatch(typeof(TreeBase), "Damage")]
    internal static class HardTreePatch
    {
        private static void Prefix(ref HitData hit)
        {
            if (Plugin.Instance?.Expansion.InstantHarvest != true || hit == null || hit.GetAttacker() != Player.m_localPlayer) return;
            hit = hit.Clone();
            hit.m_toolTier = 100;
            hit.m_damage.m_chop = 1000000;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.GetMaxCarryWeight))]
    internal static class CarryWeightPhasePatch
    {
        private static void Postfix(Player __instance, ref float __result)
        {
            if (!Plugin.Instance || __instance != Player.m_localPlayer) return;
            var phase = Plugin.Instance.Phase;
            if (Plugin.Instance.Features.Weight) { __result = 1000000000f; return; }
            float mult = Mathf.Clamp(phase.CarryMultiplier, .1f, 100f);
            if (Mathf.Abs(mult - 1f) > .001f) __result *= mult;
            if (phase.NoEncumber) __result = Mathf.Max(__result, __instance.GetInventory().GetTotalWeight() + 50f);
        }
    }
    [HarmonyPatch(typeof(Bed), "CheckFire")]
    internal static class BedFirePatch
    {
        private static void Postfix(ref bool __result)
        {
            if (Plugin.Instance?.Phase.NoBedRestrictions == true) __result = true;
        }
    }
    [HarmonyPatch(typeof(Bed), "CheckExposure")]
    internal static class BedExposurePatch
    {
        private static void Postfix(ref bool __result)
        {
            if (Plugin.Instance?.Phase.NoBedRestrictions == true) __result = true;
        }
    }
    [HarmonyPatch(typeof(Bed), "CheckEnemies")]
    internal static class BedEnemiesPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (Plugin.Instance?.Phase.NoBedRestrictions == true) __result = true;
        }
    }
    [HarmonyPatch(typeof(Bed), "CheckWet")]
    internal static class BedWetPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (Plugin.Instance?.Phase.NoBedRestrictions == true) __result = true;
        }
    }
    [HarmonyPatch(typeof(Humanoid), "StartAttack")]
    internal static class NoToolDelayPatch
    {
        private static void Prefix(Humanoid __instance, bool secondaryAttack)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || __instance != Player.m_localPlayer) return;
            // Chain readiness uses m_timeSinceLastAttack; StartAttack zeros m_lastCombatTimer.
            if (phase.NoToolDelay || (phase.NoSpecialCooldown && secondaryAttack))
            {
                try { Reflection.Set(__instance, "m_timeSinceLastAttack", 999f); } catch { }
            }
            if (phase.NoSpecialCooldown && secondaryAttack && __instance is Player player)
            {
                try
                {
                    Reflection.Set(player, "m_queuedAttackTimer", 0f);
                    Reflection.Set(player, "m_queuedSecondAttackTimer", 0f);
                    Reflection.Set(player, "m_blockReload", 0f);
                }
                catch { }
            }
        }
    }
    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetStationBuildRange))]
    internal static class InfWorkbenchRadiusPatch
    {
        private static void Postfix(ref float __result)
        {
            if (Plugin.Instance?.Phase.InfWorkbenchRadius == true) __result = Math.Max(__result, 100f);
        }
    }

    [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
    internal static class MeleeCleavePatch
    {
        private struct State { internal bool active; internal bool multi; internal float angle; }
        private static void Prefix(Attack __instance, out State __state)
        {
            __state = default;
            var phase = Plugin.Instance?.Phase;
            if (phase?.MeleeCleave != true) return;
            if (Reflection.Get<Humanoid>(__instance, "m_character") != Player.m_localPlayer) return;
            try
            {
                __state.multi = __instance.m_multiHit;
                __state.angle = __instance.m_attackAngle;
                __instance.m_multiHit = true;
                __instance.m_attackAngle = Mathf.Clamp(__state.angle + Mathf.Clamp(phase.CleaveAngleBonus, 0, 90), 10f, 180f);
                __state.active = true;
            }
            catch { __state = default; }
        }
        private static void Finalizer(Attack __instance, State __state)
        {
            if (!__state.active) return;
            try
            {
                __instance.m_multiHit = __state.multi;
                __instance.m_attackAngle = __state.angle;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class PierceShotSetupPatch
    {
        private static void Postfix(Projectile __instance, Character owner)
        {
            if (Plugin.Instance?.Phase.PierceShots != true || owner != Player.m_localPlayer || !__instance) return;
            // Stop on terrain/statics only so the bolt continues through creatures.
            __instance.m_onlyStopOnTerrain = true;
            __instance.m_hitFriendly = false;
            __instance.m_noDamageFriendly = true;
        }
    }

    [HarmonyPatch(typeof(Humanoid), "UpdateBlock")]
    internal static class AutoParryBlockPatch
    {
        private static void Prefix(Humanoid __instance, out float __state)
        {
            __state = -1f;
            var phase = Plugin.Instance?.Phase;
            if (phase == null || __instance != Player.m_localPlayer) return;
            float window = Mathf.Clamp(phase.PerfectBlockWindow, 1f, 8f);
            try
            {
                float baseline = Reflection.Get<float>(__instance, "m_perfectBlockInterval");
                if (baseline <= 0) return;
                __state = baseline;
                if (window > 1.001f)
                    Reflection.Set(__instance, "m_perfectBlockInterval", baseline * window);
                if (phase.AutoParry && __instance.IsBlocking())
                    Reflection.Set(__instance, "m_blockTimer", 0f);
            }
            catch { __state = -1f; }
        }
        private static void Finalizer(Humanoid __instance, float __state)
        {
            if (__state < 0 || __instance != Player.m_localPlayer) return;
            try { Reflection.Set(__instance, "m_perfectBlockInterval", __state); } catch { }
        }
    }

    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    internal static class AutoParryOnHitPatch
    {
        private static void Prefix(Humanoid __instance)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase?.AutoParry != true || __instance != Player.m_localPlayer) return;
            if (!__instance.IsBlocking()) return;
            try { Reflection.Set(__instance, "m_blockTimer", 0f); } catch { }
        }
    }

    internal static class FishingHooks
    {
        internal static Character GetOwner(FishingFloat ff)
        {
            if (!ff) return null;
            try { return (Character)Reflection.Method(typeof(FishingFloat), "GetOwner").Invoke(ff, null); }
            catch { return null; }
        }
        internal static bool LocalFloat(FishingFloat ff) =>
            ff && Player.m_localPlayer && GetOwner(ff) == Player.m_localPlayer;
    }

    // If bait was never consumed, cancel must not refund a duplicate.
    [HarmonyPatch(typeof(FishingFloat), "ReturnBait")]
    internal static class FishingReturnBaitPatch
    {
        private static bool Prefix(FishingFloat __instance)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || !FishingHooks.LocalFloat(__instance)) return true;
            if (phase.InfArrows || phase.UnlimitedBait) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Fish), "TestBate", typeof(FishingFloat))]
    internal static class FishingAlwaysBitePatch
    {
        private static void Postfix(FishingFloat ff, ref bool __result)
        {
            if (__result) return;
            if (Plugin.Instance?.Phase?.AlwaysBite != true || !FishingHooks.LocalFloat(ff)) return;
            __result = true;
        }
    }

    // Fish.FindFloat(), not FishingFloat.FindFloat overloads.
    [HarmonyPatch(typeof(Fish), "FindFloat", new Type[0])]
    internal static class FishingAttractPatch
    {
        private static void Prefix(Fish __instance, out float __state)
        {
            __state = -1f;
            if (Plugin.Instance?.Phase?.AttractFish != true || !__instance) return;
            __state = __instance.m_baseHookChance;
            __instance.m_baseHookChance = 1f;
            try { Reflection.Set(__instance, "m_failedBait", null); } catch { }
        }
        private static void Finalizer(Fish __instance, float __state)
        {
            if (__state < 0 || !__instance) return;
            __instance.m_baseHookChance = __state;
        }
    }

    [HarmonyPatch(typeof(FishingFloat), nameof(FishingFloat.Nibble), typeof(Fish), typeof(bool))]
    internal static class FishingInstantHookPatch
    {
        private static void Postfix(FishingFloat __instance, bool correctBait)
        {
            if (!correctBait || Plugin.Instance?.Phase?.InstantHook != true || !FishingHooks.LocalFloat(__instance)) return;
            try { Reflection.Method(typeof(FishingFloat), "TryToHook").Invoke(__instance, null); } catch { }
        }
    }

    [HarmonyPatch(typeof(FishingFloat), "TryToHook")]
    internal static class FishingHookWindowPatch
    {
        // Instant hook: keep nibble timer fresh so TryToHook doesn't miss the 0.5s window.
        private static void Prefix(FishingFloat __instance)
        {
            if (Plugin.Instance?.Phase?.InstantHook != true || !FishingHooks.LocalFloat(__instance)) return;
            try { Reflection.Set(__instance, "m_nibbleTime", Time.time); } catch { }
        }
    }

    [HarmonyPatch(typeof(Fish), "Escape")]
    internal static class FishingNoEscapePatch
    {
        private static bool Prefix(Fish __instance)
        {
            if (Plugin.Instance?.Phase?.NoFishEscape != true || !__instance) return true;
            FishingFloat ff = null;
            try { ff = Reflection.Get<FishingFloat>(__instance, "m_fishingFloat"); } catch { }
            return !FishingHooks.LocalFloat(ff);
        }
    }

    [HarmonyPatch(typeof(Fish), nameof(Fish.GetStaminaUse))]
    internal static class FishingStaminaPatch
    {
        private static void Postfix(Fish __instance, ref float __result)
        {
            if (Plugin.Instance?.Phase?.NoFishEscape != true || !__instance) return;
            FishingFloat ff = null;
            try { ff = Reflection.Get<FishingFloat>(__instance, "m_fishingFloat"); } catch { }
            if (FishingHooks.LocalFloat(ff)) __result = 0f;
        }
    }

    [HarmonyPatch(typeof(FishingFloat), "FixedUpdate")]
    internal static class FishingFloatTickPatch
    {
        private struct State { internal bool active; internal float breakDist, maxDist; }
        private static bool Prefix(FishingFloat __instance, out State __state)
        {
            __state = default;
            var phase = Plugin.Instance?.Phase;
            if (phase == null || !FishingHooks.LocalFloat(__instance)) return true;

            if (phase.InstantReel)
            {
                Fish hooked = null;
                try { hooked = __instance.GetCatch(); } catch { }
                if (hooked)
                {
                    var owner = FishingHooks.GetOwner(__instance);
                    try
                    {
                        if (owner) FishingFloat.Catch(hooked, owner);
                        Reflection.Method(typeof(FishingFloat), "SetCatch", typeof(Fish)).Invoke(__instance, new object[] { null });
                        // Don't tear down ZNetView inside FixedUpdate Prefix.
                        Object.Destroy(__instance.gameObject);
                    }
                    catch { }
                    return false;
                }
            }

            if (phase.UnbreakableLine)
            {
                __state.active = true;
                __state.breakDist = __instance.m_breakDistance;
                __state.maxDist = __instance.m_maxDistance;
                __instance.m_breakDistance = 9999f;
                __instance.m_maxDistance = 9999f;
            }
            return true;
        }
        private static void Finalizer(FishingFloat __instance, State __state)
        {
            if (!__state.active || !__instance) return;
            __instance.m_breakDistance = __state.breakDist;
            __instance.m_maxDistance = __state.maxDist;
        }
    }
}
