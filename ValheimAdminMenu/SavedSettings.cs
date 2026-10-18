using System;
using System.IO;
using UnityEngine;

namespace ValheimAdminMenu
{
    [Serializable]
    internal sealed class SettingsData
    {
        public int version = 9, hotkey = (int)KeyCode.F8, espFont = 11, espMax = 70;
        public string language = LocaleCodes.Auto;
        public SpawnPreset[] spawnPresets = new SpawnPreset[0];
        public EspFilter[] espFilters = new EspFilter[0];
        public bool plantEverything;
        public bool hammerAllPrefabs;
        /// <summary>True = normal AI for hammer-spawned creatures; false = idle in place.</summary>
        public bool hammerCreatureFullAi = true;
        public bool forceEnableMap, walkOnWater, fullbright, bypassDungeonGates;
        public float fullbrightIntensity = 0.5f;
        public bool alwaysDay,fastTraining,superJump,infiniteDurability,instantHarvest;
        public float jumpMultiplier=2, skillXpMult=1;
        public bool autoSave = true, rememberPlayer, tooltips = true, blockGameInputs = true;
        public float scale = 1, windowX = -1, windowY = -1;
        public bool esp, autoPin, espDistance = true, espBackground, espOutline = true, espAvoidOverlap = true, espHideInMenu = true, espContainerCount = true, espContainerItems;
        public int espContainerLines = 64;
        public float espRadius = 150, espOpacity = .3f, espSpacing = 3, espOffset = 0, espRefresh = 1;
        public bool[] categories = { true, true, false, true, false, false, true, false };
        public Color[] colors = { new Color(.65f,.87f,1), new Color(1,.62f,.49f), new Color(.65f,.9f,.66f), new Color(1,.82f,.43f), new Color(.83f,.74f,1), new Color(1,1,0), new Color(1,0,0), new Color(.95f,.62f,.28f) };
        public float worldRadius = 30, brushRadius = 5, brushAmount = 1, paintStrength = 1, interactionDistance = 10;
        public bool square, precisionBrush, brushAtLook;
        public int paintMode;
        public bool god, ghost, fly, noCost, stamina, eitr, weight, fastCraft, portal, interaction;
        public bool oneHitKill, oneHitCreatures, oneHitStructures, infiniteJumps, alwaysNearFire, neverFreeze, keepInventory, keepFoodOnDeath, noEncumber, itemMagnet, forceWind, disableMist, hideHud;
        public bool multiArrow, meleeMultiHit, forgeAlwaysSucceed, forgeNeverBreak;
        public float carryMultiplier=1, magnetRadius=20, stackPickupMultiplier=1, pickaxeRadius=1, exploreRadius=1, fovScale=1, cameraDistance=1, boatCameraDistance=1;
        public float arrowCount=3, arrowSpread=5, meleeHits=3;
        // Was runtime-only before; now persisted.
        public bool moveUnderwater, noWaterStaminaDamage, noBedRestrictions;
        public bool infFood, infFoodBuff, noFoodDecay, alwaysCanEat, noFoodCooldown;
        public bool instantGuardianCooldown, holdGuardianPower;
        public bool drawBowImmediate, infArrows, noToolDelay;
        public bool pierceShots, meleeCleave, autoParry, noSpecialCooldown;
        public bool bowAimAssist, magicBullet, bowAimTargetPlayers, showAimPredict, noArrowGravity;
        public int bowAimTargetMode;
        public float bowAimStrength=75, bowAimAngle=28, bowAimRange=50;
        public float walkSpeedMult=1, runSpeedMult=1, swimSpeedMult=1, sneakSpeedMult=1;
        public bool unlimitedBait, alwaysBite, attractFish, instantHook, instantReel, noFishEscape, unbreakableLine;
        public bool boatBoost, boatStable, boatFly, easyCart, boatInfHealth, cartInfHealth;
        public float cleaveAngleBonus=20, perfectBlockWindow=1, boatSpeedMult=2, cartEase=3;
        public bool freeCraft, craftFromChests, buildEverywhere, ignoreUnstable, ignorePlayerBlock, ignoreMysticalForce;
        public float craftChestRadius = 15, chestRadius = 15;
        public bool depositMaterialsOnly, depositSkipEquipped = true;
        public string withdrawSearch = "";
        public HotkeyBindData[] featureBinds;
        // JsonUtility skips internal class arrays; these primitive copies are what actually persist.
        public string[] featureBindIds = new string[0];
        public int[] featureBindKeys = new int[0];
        public int[] espFilterCategories = new int[0];
        public string[] espFilterPrefabs = new string[0];
        public bool infWorkbenchRadius, workbenchNoRoof, stationLevel10;
        public bool indestructibleBuildings, noRainDamage, noUnderwaterBuildingDamage;
        public bool infStationFuel, fireplaceNoWet, stationSlots999, stationAlwaysOn;
        public bool infStackSplit;
        public bool noFallDamage, noclip, lockRested, preventSkillLoss, freezeSkills, freezeXp;
        public bool freezeAiNearby, freezeAiGlobal, disableRaids;
        public bool allowOverlap, fineBuildRotation, extendedBuildDistance, staggerProtection;
        public bool instantCook, instantFerment, instantSmelt, instantPlantGrow;
        public float outgoingDamageMult=1, incomingDamageMult=1, projectileSpeedMult=1;
        public float hitPushMult=1, buildDistanceMult=1, productionSpeedMult=1;
        public bool forceTrophy, persistentOnKillDrops;
        public int forceTrophyCount = 1;
        public int itemSpawnMode;
        public bool weaponVfx, feetTrailVfx, autoPickupFilter, repairHammerAoe;
        public bool autoFeedTames, freecam, photoMode, photoFreeze;
        public bool minimapZoomControl, tpToPing, disablePortalFade, sitMountAnywhere;
        public bool freecamBuild, meleeTargetLock, meleeLockPlayers, flyingMount, mountAnyCreature;
        public bool pasteLevelGround, pasteLevelPerPiece, levelOnPlace;
        public bool autoPastePreview = true, pasteSnapToGround = true, pasteAnchorSnap, applyTerrainProfile;
        public bool freeBuildRotation, freeBuildRotationLocal, universalSnap = true, dragBuilding, placementHud = true;
        public bool hudRotation = true, hudAxisStep = true, hudPieceCount = true, hudLockState = true, hudGroundSnap = true, hudInTerrain = true, hudPrefabName = true;
        public int rotationSnapDivisions = 45, dragMaxPieces = 500;
        public float pasteFeather = 6f;
        public float brushFeather = 6f;
        public bool groundSpawnAtCrosshair = true;
        public float pieceScale = 1, repairHammerRadius = 8, vfxDuration = 12, vfxScale = 1, vfxReplaySpeed = 1, autoFeedRadius = 20, farmHealRadius = 30, minimapZoom = 1;
        public int vfxPlaceMode;
        public bool vfxDoEffect;
        public string junkExtra = "", junkBlacklist = "", pickupWhitelist = "", pickupBlacklist = "";
        public string weaponVfxId = "", feetVfxId = "", vfxSelectedId = "";
        public int playerInvRows = 4, playerInvExtraCols, chestBonusRows, stackSizeOverride;
        public float stackSizeMult = 1;
        public bool playerInvOverride, playerInvOverrideSet;
        internal void Validate()
        {
            if (spawnPresets == null) spawnPresets = new SpawnPreset[0];
            if (espFilters == null) espFilters = new EspFilter[0];
            fullbrightIntensity = Clamp(fullbrightIntensity, .1f, 2f, .5f);
            foreach(var preset in spawnPresets)if(preset!=null) { preset.quality=Mathf.Clamp(preset.quality,1,ItemEditor.MaxQuality);if(preset.crafter==null)preset.crafter="";if(preset.edits==null || !preset.edits.Valid())preset.edits=new ItemEdits(); }
            if (!Rules.ValidHotkey((KeyCode)hotkey)) hotkey = (int)KeyCode.F8;
            jumpMultiplier=Clamp(jumpMultiplier,1,8,2);
            // Legacy Fast training when skillXpMult was never saved (JsonUtility default 0).
            if (skillXpMult < 1f) skillXpMult = fastTraining ? 25f : 1f;
            skillXpMult=Clamp(skillXpMult,1,25,1);
            fastTraining = skillXpMult >= 24.5f;
            scale = Clamp(scale,.7f,1.6f,1); windowX = Clamp(windowX,-1,10000,-1); windowY = Clamp(windowY,-1,10000,-1);
            espFont = Mathf.Clamp(espFont,8,24); espMax = Mathf.Clamp(espMax,10,200);
            espRadius = Clamp(espRadius,10,500,150); espOpacity = Clamp(espOpacity,0,1,.3f);
            espSpacing = Clamp(espSpacing,0,20,3); espOffset = Clamp(espOffset,-60,60,0); espRefresh = Clamp(espRefresh,.25f,5,1);
            worldRadius = Clamp(worldRadius,1,200,30); brushRadius = Clamp(brushRadius,1,50,5); brushAmount = Clamp(brushAmount,.05f,8,1);
            paintStrength = Clamp(paintStrength,.1f,1,1);
            if (paintMode < 0 || paintMode > 4) paintMode = 0;
            interactionDistance = Clamp(interactionDistance,1,50,10);
            carryMultiplier=Clamp(carryMultiplier,.1f,100,1);magnetRadius=Clamp(magnetRadius,1,100,20);
            stackPickupMultiplier=Clamp(stackPickupMultiplier,1,20,1);pickaxeRadius=Clamp(pickaxeRadius,.5f,5,1);
            exploreRadius=Clamp(exploreRadius,.1f,20,1);fovScale=Clamp(fovScale,.5f,2,1);
            cameraDistance=Clamp(cameraDistance,.5f,5,1);boatCameraDistance=Clamp(boatCameraDistance,.5f,5,1);
            arrowCount=Clamp(arrowCount,2,12,3);arrowSpread=Clamp(arrowSpread,0,45,5);meleeHits=Clamp(meleeHits,2,10,3);
            bowAimStrength=Clamp(bowAimStrength,10,100,75);bowAimAngle=Clamp(bowAimAngle,5,75,28);bowAimRange=Clamp(bowAimRange,10,100,50);
            if (bowAimTargetMode < 0 || bowAimTargetMode > 2) bowAimTargetMode = 0;
            walkSpeedMult=Clamp(walkSpeedMult,.1f,10,1);runSpeedMult=Clamp(runSpeedMult,.1f,10,1);
            swimSpeedMult=Clamp(swimSpeedMult,.1f,10,1);sneakSpeedMult=Clamp(sneakSpeedMult,.1f,10,1);
            cleaveAngleBonus=Clamp(cleaveAngleBonus,0,90,20);perfectBlockWindow=Clamp(perfectBlockWindow,1,8,1);
            boatSpeedMult=Clamp(boatSpeedMult,1,8,2);cartEase=Clamp(cartEase,1,10,3);
            outgoingDamageMult=Clamp(outgoingDamageMult,0,50,1);incomingDamageMult=Clamp(incomingDamageMult,0,10,1);
            projectileSpeedMult=Clamp(projectileSpeedMult,1,10,1);hitPushMult=Clamp(hitPushMult,0,20,1);
            buildDistanceMult=Clamp(buildDistanceMult,1,20,1);productionSpeedMult=Clamp(productionSpeedMult,1,50,1);
            craftChestRadius=Clamp(craftChestRadius,5,40,15);chestRadius=Clamp(chestRadius,1,50,15);
            if (itemSpawnMode < 0 || itemSpawnMode > 3) itemSpawnMode = 0;
            if (vfxPlaceMode < 0 || vfxPlaceMode > 2) vfxPlaceMode = 0;
            pieceScale=Clamp(pieceScale,.25f,4,1);repairHammerRadius=Clamp(repairHammerRadius,1,40,8);
            vfxDuration=Clamp(vfxDuration,0f,600,12);vfxScale=Clamp(vfxScale,.25f,5,1);vfxReplaySpeed=Clamp(vfxReplaySpeed,.25f,4,1);
            autoFeedRadius=Clamp(autoFeedRadius,1,100,20);
            farmHealRadius=Clamp(farmHealRadius,1,100,30);minimapZoom=Clamp(minimapZoom,.5f,2.5f,1);
            playerInvRows = Mathf.Clamp(playerInvRows <= 0 ? 4 : playerInvRows, InventoryCapacity.MinRows, InventoryCapacity.MaxRows);
            playerInvExtraCols = Mathf.Clamp(playerInvExtraCols, 0, InventoryCapacity.MaxExtraCols);
            chestBonusRows = Mathf.Clamp(chestBonusRows, 0, InventoryCapacity.MaxChestBonusRows);
            stackSizeMult = Clamp(stackSizeMult, 1f, InventoryCapacity.MaxStackMult, 1f);
            stackSizeOverride = Mathf.Clamp(stackSizeOverride, 0, InventoryCapacity.MaxStackOverride);
            pasteFeather = Clamp(pasteFeather, 0f, 12f, 6f);
            brushFeather = Clamp(brushFeather, 0f, 12f, 6f);
            if (rotationSnapDivisions < 2 || rotationSnapDivisions > 256) rotationSnapDivisions = 45;
            if (dragMaxPieces < 10 || dragMaxPieces > 500) dragMaxPieces = 500;
            espContainerLines = Mathf.Clamp(espContainerLines, 1, 64);
            if (!playerInvOverrideSet)
            {
                playerInvOverride = playerInvRows > InventoryCapacity.DefaultRows || playerInvExtraCols > 0;
                playerInvOverrideSet = true;
            }
            if (withdrawSearch == null) withdrawSearch = "";
            if (junkExtra == null) junkExtra = "";
            if (junkBlacklist == null) junkBlacklist = "";
            if (pickupWhitelist == null) pickupWhitelist = "";
            if (pickupBlacklist == null) pickupBlacklist = "";
            if (weaponVfxId == null) weaponVfxId = "";
            if (feetVfxId == null) feetVfxId = "";
            if (vfxSelectedId == null) vfxSelectedId = "";
            language = LocaleCodes.NormalizePreference(language);
            if (featureBinds == null) featureBinds = new HotkeyBindData[0];
            if (featureBindIds == null) featureBindIds = new string[0];
            if (featureBindKeys == null) featureBindKeys = new int[0];
            if (espFilterCategories == null) espFilterCategories = new int[0];
            if (espFilterPrefabs == null) espFilterPrefabs = new string[0];
            if (categories == null || categories.Length != 8) {
                var old = categories;
                categories = new bool[] { true, true, false, true, false, false, true, false };
                if (old != null) for (int i = 0; i < old.Length && i < categories.Length; i++) categories[i] = old[i];
            }
            if (colors == null || colors.Length != 8) {
                var old = colors;
                colors = new Color[] { new Color(.65f,.87f,1), new Color(1,.62f,.49f), new Color(.65f,.9f,.66f), new Color(1,.82f,.43f), new Color(.83f,.74f,1), new Color(1,1,0), new Color(1,0,0), new Color(.95f,.62f,.28f) };
                if (old != null) for (int i = 0; i < old.Length && i < colors.Length; i++) colors[i] = old[i];
            }
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(Clamp(colors[i].r,0,1,1),Clamp(colors[i].g,0,1,1),Clamp(colors[i].b,0,1,1),1);
        }
        private static float Clamp(float v,float min,float max,float fallback) => Rules.Finite(v) ? Mathf.Clamp(v,min,max) : fallback;
    }
    internal sealed class SavedSettings
    {
        private readonly Plugin app;
        internal SettingsData Data = new SettingsData();
        internal bool PendingPlayer = true;
        /// <summary>False after scene/player reset until toggles re-apply. Don't save that blank state over the file.</summary>
        internal bool CaptureCharacterState;
        internal string PathName => AppPaths.SettingsPath;
        internal SavedSettings(Plugin app) { this.app = app; }
        internal void Load()
        {
            var loaded = new SettingsData { version = 1, hotkey = (int)app.Hotkey, scale = app.UiScale };
            if (File.Exists(PathName)) JsonUtility.FromJsonOverwrite(File.ReadAllText(PathName), loaded);
            if (loaded.version < 2) { loaded.version = 2; loaded.windowX = loaded.windowY = -1; }
            if (loaded.version < 3) loaded.version = 3;
            if (loaded.version < 4)
            {
                // v4: XP slider replaces Fast training; combat/boat/cart toggles persist.
                if (loaded.skillXpMult < 1f) loaded.skillXpMult = loaded.fastTraining ? 25f : 1f;
                loaded.version = 4;
            }
            if (loaded.version < 5)
            {
                loaded.version = 5;
                if (loaded.craftChestRadius < 1f) loaded.craftChestRadius = 15f;
                if (loaded.chestRadius < 1f) loaded.chestRadius = 15f;
            }
            if (loaded.version < 6)
            {
                // v6: migrate legacy one-hit into creatures toggle.
                if (loaded.oneHitKill && !loaded.oneHitCreatures) loaded.oneHitCreatures = true;
                loaded.version = 6;
            }
            if (loaded.version < 7)
            {
                loaded.language = LocaleCodes.Auto;
                loaded.version = 7;
            }
            if (loaded.version < 8)
            {
                if (!loaded.playerInvOverrideSet)
                    loaded.playerInvOverride = loaded.playerInvRows > InventoryCapacity.DefaultRows || loaded.playerInvExtraCols > 0;
                loaded.playerInvOverrideSet = true;
                if (loaded.sitMountAnywhere) loaded.mountAnyCreature = true;
                loaded.version = 8;
            }
            if (loaded.version < 9)
            {
                // 3.0.0 drag-build default broke normal place; force off once for existing sidecars.
                loaded.dragBuilding = false;
                loaded.version = 9;
            }
            loaded.featureBinds = UnpackBinds(loaded);
            loaded.espFilters = UnpackFilters(loaded);
            loaded.Validate(); Data = loaded; Apply(); app.Report(L.T("toast.settings_loaded"));
        }
        internal void Apply()
        {
            Data.Validate(); app.Hotkey = (KeyCode)Data.hotkey; app.UiScale = Data.scale;
            MenuLanguage.ApplyPreference(Data.language);
            app.Menu.SetPosition(Data.windowX,Data.windowY);
            app.Overlay.Enabled = Data.esp; app.Overlay.AutoPin = Data.autoPin; app.Overlay.Radius = Data.espRadius;
            app.Overlay.MaxLabels = Data.espMax; app.Overlay.Categories = (bool[])Data.categories.Clone();
            app.World.Radius = Data.worldRadius; app.World.BrushRadius = Data.brushRadius; app.World.BrushAmount = Data.brushAmount; app.World.Square = Data.square;
            app.World.PaintStrength = Data.paintStrength; app.World.Precision = Data.precisionBrush; app.World.BrushAtLook = Data.brushAtLook; app.World.PaintMode = Data.paintMode; app.World.BrushFeather = Data.brushFeather;
            app.Features.Distance = Data.interactionDistance;
            ChestInventory.Radius = Data.chestRadius;
            ChestInventory.DepositMaterialsOnly = Data.depositMaterialsOnly;
            ChestInventory.DepositSkipEquipped = Data.depositSkipEquipped;
            ChestInventory.WithdrawSearch = Data.withdrawSearch ?? "";
            HotkeyRegistry.ApplyBinds(Data.featureBinds);
            Release250.JunkExtra = Data.junkExtra ?? "";
            Release250.JunkBlacklist = Data.junkBlacklist ?? "";
            Release250.PickupWhitelist = Data.pickupWhitelist ?? "";
            Release250.PickupBlacklist = Data.pickupBlacklist ?? "";
            Release250.PieceScale = Data.pieceScale;
            Release250.RepairHammerRadius = Data.repairHammerRadius;
            Release250.VfxDuration = Data.vfxDuration;
            Release250.VfxPlaceMode = Data.vfxPlaceMode;
            VfxSuite.DoEffect = Data.vfxDoEffect;
            VfxSuite.Scale = Data.vfxScale;
            VfxSuite.ReplaySpeed = Data.vfxReplaySpeed;
            Release250.AutoFeedRadius = Data.autoFeedRadius;
            Release250.FarmHealRadius = Data.farmHealRadius;
            Release250.MinimapZoom = Data.minimapZoom;
            Release250.LoadVfxPrefs(Data.weaponVfxId, Data.feetVfxId, Data.vfxSelectedId);
            PendingPlayer = true; CaptureCharacterState = false; app.Overlay.Clear();
        }
        internal void ApplyPlayer()
        {
            var p = Player.m_localPlayer;
            if (!PendingPlayer || !p || p.IsDead() || p.IsTeleporting()) return;
            PendingPlayer = false;
            if (!Data.rememberPlayer) return;
            ApplyCharacterToggles();
        }
        /// <summary>Push saved toggles into live feature hosts (remember-player / manual reload).</summary>
        internal void ApplyCharacterToggles()
        {
            var f = app.Features;
            var p2 = app.Phase;
            app.Expansion.SkillXpMult=Data.skillXpMult;app.Expansion.FastTraining=Data.fastTraining||Data.skillXpMult>=24.5f;
            app.Expansion.SuperJump=Data.superJump;app.Expansion.Durability=Data.infiniteDurability;app.Expansion.InstantHarvest=Data.instantHarvest;
            if (Player.m_localPlayer)
            {
                f.God(Data.god); f.Ghost(Data.ghost); f.Fly(Data.fly); f.NoCost(Data.noCost);
            }
            f.Stamina = Data.stamina; f.Eitr = Data.eitr; f.Weight = Data.weight; f.FastCraft = Data.fastCraft; f.PortalItems = Data.portal;
            f.DistanceEnabled = Data.interaction; f.Distance = Data.interactionDistance;
            p2.OneHitKill=Data.oneHitKill;p2.OneHitCreatures=Data.oneHitCreatures||Data.oneHitKill;p2.OneHitStructures=Data.oneHitStructures;
            p2.InfiniteJumps=Data.infiniteJumps;p2.AlwaysNearFire=Data.alwaysNearFire;p2.NeverFreeze=Data.neverFreeze;
            p2.KeepInventory=Data.keepInventory;p2.KeepFoodOnDeath=Data.keepFoodOnDeath;p2.NoEncumber=Data.noEncumber;p2.ItemMagnet=Data.itemMagnet;
            p2.ForceWind=Data.forceWind;p2.DisableMist=Data.disableMist;p2.HideHud=Data.hideHud;
            p2.MultiArrow=Data.multiArrow;p2.MeleeMultiHit=Data.meleeMultiHit;
            p2.ForgeAlwaysSucceed=Data.forgeAlwaysSucceed;p2.ForgeNeverBreak=Data.forgeNeverBreak;
            p2.CarryMultiplier=Data.carryMultiplier;p2.MagnetRadius=Data.magnetRadius;p2.StackPickupMultiplier=Data.stackPickupMultiplier;
            p2.PickaxeRadius=Data.pickaxeRadius;p2.ExploreRadius=Data.exploreRadius;
            p2.FovScale=Data.fovScale;p2.CameraDistance=Data.cameraDistance;p2.BoatCameraDistance=Data.boatCameraDistance;
            p2.ArrowCount=Data.arrowCount;p2.ArrowSpread=Data.arrowSpread;p2.MeleeHits=Data.meleeHits;
            p2.MoveUnderwater=Data.moveUnderwater;p2.NoWaterStaminaDamage=Data.noWaterStaminaDamage;p2.NoBedRestrictions=Data.noBedRestrictions;
            p2.InfFood=Data.infFood;p2.InfFoodBuff=Data.infFoodBuff;p2.NoFoodDecay=Data.noFoodDecay;p2.AlwaysCanEat=Data.alwaysCanEat;p2.NoFoodCooldown=Data.noFoodCooldown;
            p2.InstantGuardianCooldown=Data.instantGuardianCooldown;p2.HoldGuardianPower=Data.holdGuardianPower;
            p2.DrawBowImmediate=Data.drawBowImmediate;p2.InfArrows=Data.infArrows;p2.NoToolDelay=Data.noToolDelay;
            p2.PierceShots=Data.pierceShots;p2.MeleeCleave=Data.meleeCleave;p2.AutoParry=Data.autoParry;p2.NoSpecialCooldown=Data.noSpecialCooldown;
            p2.BowAimAssist=Data.bowAimAssist;p2.MagicBullet=Data.magicBullet;p2.BowAimTargetPlayers=Data.bowAimTargetPlayers;
            p2.ShowAimPredict=Data.showAimPredict;p2.NoArrowGravity=Data.noArrowGravity;p2.BowAimTargetMode=Data.bowAimTargetMode;
            p2.BowAimStrength=Data.bowAimStrength;p2.BowAimAngle=Data.bowAimAngle;p2.BowAimRange=Data.bowAimRange;
            p2.WalkSpeedMult=Data.walkSpeedMult;p2.RunSpeedMult=Data.runSpeedMult;p2.SwimSpeedMult=Data.swimSpeedMult;p2.SneakSpeedMult=Data.sneakSpeedMult;
            p2.UnlimitedBait=Data.unlimitedBait;p2.AlwaysBite=Data.alwaysBite;p2.AttractFish=Data.attractFish;
            p2.InstantHook=Data.instantHook;p2.InstantReel=Data.instantReel;p2.NoFishEscape=Data.noFishEscape;p2.UnbreakableLine=Data.unbreakableLine;
            p2.BoatBoost=Data.boatBoost;p2.BoatStable=Data.boatStable;p2.BoatFly=Data.boatFly;p2.EasyCart=Data.easyCart;
            p2.BoatInfHealth=Data.boatInfHealth;p2.CartInfHealth=Data.cartInfHealth;
            p2.CleaveAngleBonus=Data.cleaveAngleBonus;p2.PerfectBlockWindow=Data.perfectBlockWindow;
            p2.BoatSpeedMult=Data.boatSpeedMult;p2.CartEase=Data.cartEase;
            p2.FreeCraft=Data.freeCraft;p2.CraftFromChests=Data.craftFromChests;p2.CraftChestRadius=Data.craftChestRadius;
            p2.BuildEverywhere=Data.buildEverywhere;p2.IgnoreUnstable=Data.ignoreUnstable;
            p2.IgnorePlayerBlock=Data.ignorePlayerBlock;p2.IgnoreMysticalForce=Data.ignoreMysticalForce;
            p2.InfWorkbenchRadius=Data.infWorkbenchRadius;p2.WorkbenchNoRoof=Data.workbenchNoRoof;p2.StationLevel10=Data.stationLevel10;
            p2.IndestructibleBuildings=Data.indestructibleBuildings;p2.NoRainDamage=Data.noRainDamage;p2.NoUnderwaterBuildingDamage=Data.noUnderwaterBuildingDamage;
            p2.InfStationFuel=Data.infStationFuel;p2.FireplaceNoWet=Data.fireplaceNoWet;p2.StationSlots999=Data.stationSlots999;p2.StationAlwaysOn=Data.stationAlwaysOn;
            p2.InfStackSplit=Data.infStackSplit;
            p2.NoFallDamage=Data.noFallDamage;p2.Noclip=Data.noclip;p2.LockRested=Data.lockRested;
            p2.PreventSkillLoss=Data.preventSkillLoss;p2.FreezeSkills=Data.freezeSkills;p2.FreezeXp=Data.freezeXp;
            p2.FreezeAiNearby=Data.freezeAiNearby;p2.FreezeAiGlobal=Data.freezeAiGlobal;
            p2.DisableRaids=Data.disableRaids;p2.ForceTrophy=Data.forceTrophy;p2.ForceTrophyCount=Data.forceTrophyCount;p2.PersistentOnKillDrops=Data.persistentOnKillDrops;
            p2.AllowOverlap=Data.allowOverlap;p2.FineBuildRotation=Data.fineBuildRotation;p2.ExtendedBuildDistance=Data.extendedBuildDistance;
            p2.StaggerProtection=Data.staggerProtection;
            p2.InstantCook=Data.instantCook;p2.InstantFerment=Data.instantFerment;p2.InstantSmelt=Data.instantSmelt;p2.InstantPlantGrow=Data.instantPlantGrow;
            p2.OutgoingDamageMult=Data.outgoingDamageMult;p2.IncomingDamageMult=Data.incomingDamageMult;p2.ProjectileSpeedMult=Data.projectileSpeedMult;
            p2.HitPushMult=Data.hitPushMult;p2.BuildDistanceMult=Data.buildDistanceMult;p2.ProductionSpeedMult=Data.productionSpeedMult;
            p2.ItemSpawnMode=Data.itemSpawnMode;
            p2.WeaponVfx=Data.weaponVfx||!string.IsNullOrEmpty(Data.weaponVfxId);
            p2.FeetTrailVfx=Data.feetTrailVfx||!string.IsNullOrEmpty(Data.feetVfxId);
            p2.AutoPickupFilter=Data.autoPickupFilter;p2.RepairHammerAoe=Data.repairHammerAoe;
            p2.AutoFeedTames=Data.autoFeedTames;p2.Freecam=Data.freecam;p2.PhotoMode=Data.photoMode;p2.PhotoFreeze=Data.photoFreeze;
            p2.MinimapZoomControl=Data.minimapZoomControl;p2.TpToPing=Data.tpToPing;p2.DisablePortalFade=Data.disablePortalFade;
            p2.SitMountAnywhere=Data.sitMountAnywhere;p2.GroundSpawnAtCrosshair=Data.groundSpawnAtCrosshair;
            p2.FreecamBuild=Data.freecamBuild;p2.MeleeTargetLock=Data.meleeTargetLock;p2.MeleeLockPlayers=Data.meleeLockPlayers;
            p2.FlyingMount=Data.flyingMount;p2.MountAnyCreature=Data.mountAnyCreature;
            Release250.PasteLevelGround=Data.pasteLevelGround;Release250.PasteLevelPerPiece=Data.pasteLevelPerPiece;Release250.LevelOnPlace=Data.levelOnPlace;Release250.PasteFeather=Data.pasteFeather;
            Release250.AutoPastePreview=Data.autoPastePreview;Release250.PasteSnapToGround=Data.pasteSnapToGround;Release250.PasteAnchorSnap=Data.pasteAnchorSnap;Release250.ApplyTerrainProfile=Data.applyTerrainProfile;
            FreeBuildRotation.Enabled=Data.freeBuildRotation;FreeBuildRotation.LocalFrame=Data.freeBuildRotationLocal;FreeBuildRotation.SnapDivisions=Mathf.Clamp(Data.rotationSnapDivisions,2,256);
            PlacementSnap.UniversalSnap=Data.universalSnap;
            DragBuilding.Enabled=Data.dragBuilding;DragBuilding.MaxPieces=Mathf.Clamp(Data.dragMaxPieces,10,500);
            PlacementHud.Enabled=Data.placementHud;PlacementHud.ShowRotation=Data.hudRotation;PlacementHud.ShowAxisStep=Data.hudAxisStep;PlacementHud.ShowPieceCount=Data.hudPieceCount;
            PlacementHud.ShowLockState=Data.hudLockState;PlacementHud.ShowGroundSnap=Data.hudGroundSnap;PlacementHud.ShowInTerrain=Data.hudInTerrain;PlacementHud.ShowPrefabName=Data.hudPrefabName;
            InventoryCapacity.OverrideBagSize = Data.playerInvOverride;
            InventoryCapacity.PlayerRows = Data.playerInvRows;
            InventoryCapacity.PlayerExtraCols = Data.playerInvExtraCols;
            InventoryCapacity.ChestBonusRows = Data.chestBonusRows;
            InventoryCapacity.StackMult = Data.stackSizeMult;
            InventoryCapacity.StackOverride = Data.stackSizeOverride;
            InventoryCapacity.ApplyStackSizes();
            if (InventoryCapacity.OverrideBagSize)
                InventoryCapacity.ApplyPlayerInventory();
            if (Data.freecam && GameCamera.instance && !GameCamera.InFreeFly())
                try { Reflection.Method(typeof(GameCamera), "ToggleFreeFly").Invoke(GameCamera.instance, null); } catch { try { GameCamera.instance.ToggleFreeFly(); } catch { } }
            CaptureCharacterState = true;
        }
        internal void CaptureFromRuntime()
        {
            Data.hotkey = (int)app.Hotkey; Data.scale = app.UiScale; Data.language = MenuLanguage.Preference;
            var pos = app.Menu.Position; Data.windowX = pos.x; Data.windowY = pos.y;
            var o = app.Overlay; Data.esp = o.Enabled; Data.autoPin = o.AutoPin; Data.espRadius = o.Radius; Data.espMax = o.MaxLabels; Data.categories = (bool[])o.Categories.Clone();
            Data.worldRadius = app.World.Radius; Data.brushRadius = app.World.BrushRadius; Data.brushAmount = app.World.BrushAmount; Data.square = app.World.Square;
            Data.paintStrength = app.World.PaintStrength; Data.precisionBrush = app.World.Precision; Data.brushAtLook = app.World.BrushAtLook; Data.paintMode = app.World.PaintMode; Data.brushFeather = app.World.BrushFeather;
            Data.interactionDistance = app.Features.Distance;
            Data.chestRadius = ChestInventory.Radius; Data.depositMaterialsOnly = ChestInventory.DepositMaterialsOnly;
            Data.depositSkipEquipped = ChestInventory.DepositSkipEquipped; Data.withdrawSearch = ChestInventory.WithdrawSearch ?? "";
            Data.featureBinds = HotkeyRegistry.CaptureBinds();
            PackBinds(Data);
            PackFilters(Data);
            Data.junkExtra = Release250.JunkExtra ?? ""; Data.junkBlacklist = Release250.JunkBlacklist ?? "";
            Data.pickupWhitelist = Release250.PickupWhitelist ?? ""; Data.pickupBlacklist = Release250.PickupBlacklist ?? "";
            Data.pieceScale = Release250.PieceScale; Data.repairHammerRadius = Release250.RepairHammerRadius;
            Data.vfxDuration = Release250.VfxDuration; Data.vfxPlaceMode = Release250.VfxPlaceMode;
            Data.autoFeedRadius = Release250.AutoFeedRadius; Data.farmHealRadius = Release250.FarmHealRadius;
            Data.minimapZoom = Release250.MinimapZoom;
            Data.vfxDoEffect = VfxSuite.DoEffect;
            Data.vfxScale = VfxSuite.Scale;
            Data.vfxReplaySpeed = VfxSuite.ReplaySpeed;
            Data.weaponVfxId = Release250.WeaponVfxId ?? ""; Data.feetVfxId = Release250.FeetVfxId ?? "";
            Data.vfxSelectedId = Release250.VfxSelectedId ?? "";
            if (CaptureCharacterState)
            {
            var f = app.Features;
            var p2 = app.Phase;
            Data.skillXpMult=app.Expansion.SkillXpMult;Data.fastTraining=app.Expansion.FastTraining||app.Expansion.SkillXpMult>=24.5f;
            Data.superJump=app.Expansion.SuperJump;Data.infiniteDurability=app.Expansion.Durability;Data.instantHarvest=app.Expansion.InstantHarvest;
            Data.stamina = f.Stamina; Data.eitr = f.Eitr; Data.weight = f.Weight; Data.fastCraft = f.FastCraft; Data.portal = f.PortalItems; Data.interaction = f.DistanceEnabled;
            Data.oneHitKill=p2.OneHitKill;Data.oneHitCreatures=p2.OneHitCreatures||p2.OneHitKill;Data.oneHitStructures=p2.OneHitStructures;
            Data.infiniteJumps=p2.InfiniteJumps;Data.alwaysNearFire=p2.AlwaysNearFire;Data.neverFreeze=p2.NeverFreeze;
            Data.keepInventory=p2.KeepInventory;Data.keepFoodOnDeath=p2.KeepFoodOnDeath;Data.noEncumber=p2.NoEncumber;Data.itemMagnet=p2.ItemMagnet;
            Data.forceWind=p2.ForceWind;Data.disableMist=p2.DisableMist;Data.hideHud=p2.HideHud;
            Data.multiArrow=p2.MultiArrow;Data.meleeMultiHit=p2.MeleeMultiHit;
            Data.forgeAlwaysSucceed=p2.ForgeAlwaysSucceed;Data.forgeNeverBreak=p2.ForgeNeverBreak;
            Data.carryMultiplier=p2.CarryMultiplier;Data.magnetRadius=p2.MagnetRadius;Data.stackPickupMultiplier=p2.StackPickupMultiplier;
            Data.pickaxeRadius=p2.PickaxeRadius;Data.exploreRadius=p2.ExploreRadius;
            Data.fovScale=p2.FovScale;Data.cameraDistance=p2.CameraDistance;Data.boatCameraDistance=p2.BoatCameraDistance;
            Data.arrowCount=p2.ArrowCount;Data.arrowSpread=p2.ArrowSpread;Data.meleeHits=p2.MeleeHits;
            Data.moveUnderwater=p2.MoveUnderwater;Data.noWaterStaminaDamage=p2.NoWaterStaminaDamage;Data.noBedRestrictions=p2.NoBedRestrictions;
            Data.infFood=p2.InfFood;Data.infFoodBuff=p2.InfFoodBuff;Data.noFoodDecay=p2.NoFoodDecay;Data.alwaysCanEat=p2.AlwaysCanEat;Data.noFoodCooldown=p2.NoFoodCooldown;
            Data.instantGuardianCooldown=p2.InstantGuardianCooldown;Data.holdGuardianPower=p2.HoldGuardianPower;
            Data.drawBowImmediate=p2.DrawBowImmediate;Data.infArrows=p2.InfArrows;Data.noToolDelay=p2.NoToolDelay;
            Data.pierceShots=p2.PierceShots;Data.meleeCleave=p2.MeleeCleave;Data.autoParry=p2.AutoParry;Data.noSpecialCooldown=p2.NoSpecialCooldown;
            Data.bowAimAssist=p2.BowAimAssist;Data.magicBullet=p2.MagicBullet;Data.bowAimTargetPlayers=p2.BowAimTargetPlayers;
            Data.showAimPredict=p2.ShowAimPredict;Data.noArrowGravity=p2.NoArrowGravity;Data.bowAimTargetMode=p2.BowAimTargetMode;
            Data.bowAimStrength=p2.BowAimStrength;Data.bowAimAngle=p2.BowAimAngle;Data.bowAimRange=p2.BowAimRange;
            Data.walkSpeedMult=p2.WalkSpeedMult;Data.runSpeedMult=p2.RunSpeedMult;Data.swimSpeedMult=p2.SwimSpeedMult;Data.sneakSpeedMult=p2.SneakSpeedMult;
            Data.unlimitedBait=p2.UnlimitedBait;Data.alwaysBite=p2.AlwaysBite;Data.attractFish=p2.AttractFish;
            Data.instantHook=p2.InstantHook;Data.instantReel=p2.InstantReel;Data.noFishEscape=p2.NoFishEscape;Data.unbreakableLine=p2.UnbreakableLine;
            Data.boatBoost=p2.BoatBoost;Data.boatStable=p2.BoatStable;Data.boatFly=p2.BoatFly;Data.easyCart=p2.EasyCart;
            Data.boatInfHealth=p2.BoatInfHealth;Data.cartInfHealth=p2.CartInfHealth;
            Data.cleaveAngleBonus=p2.CleaveAngleBonus;Data.perfectBlockWindow=p2.PerfectBlockWindow;
            Data.boatSpeedMult=p2.BoatSpeedMult;Data.cartEase=p2.CartEase;
            Data.freeCraft=p2.FreeCraft;Data.craftFromChests=p2.CraftFromChests;Data.craftChestRadius=p2.CraftChestRadius;
            Data.buildEverywhere=p2.BuildEverywhere;Data.ignoreUnstable=p2.IgnoreUnstable;
            Data.ignorePlayerBlock=p2.IgnorePlayerBlock;Data.ignoreMysticalForce=p2.IgnoreMysticalForce;
            Data.infWorkbenchRadius=p2.InfWorkbenchRadius;Data.workbenchNoRoof=p2.WorkbenchNoRoof;Data.stationLevel10=p2.StationLevel10;
            Data.indestructibleBuildings=p2.IndestructibleBuildings;Data.noRainDamage=p2.NoRainDamage;Data.noUnderwaterBuildingDamage=p2.NoUnderwaterBuildingDamage;
            Data.infStationFuel=p2.InfStationFuel;Data.fireplaceNoWet=p2.FireplaceNoWet;Data.stationSlots999=p2.StationSlots999;Data.stationAlwaysOn=p2.StationAlwaysOn;
            Data.infStackSplit=p2.InfStackSplit;
            Data.noFallDamage=p2.NoFallDamage;Data.noclip=p2.Noclip;Data.lockRested=p2.LockRested;
            Data.preventSkillLoss=p2.PreventSkillLoss;Data.freezeSkills=p2.FreezeSkills;Data.freezeXp=p2.FreezeXp;
            Data.freezeAiNearby=p2.FreezeAiNearby;Data.freezeAiGlobal=p2.FreezeAiGlobal;
            Data.disableRaids=p2.DisableRaids;Data.forceTrophy=p2.ForceTrophy;Data.forceTrophyCount=p2.ForceTrophyCount;Data.persistentOnKillDrops=p2.PersistentOnKillDrops;
            Data.allowOverlap=p2.AllowOverlap;Data.fineBuildRotation=p2.FineBuildRotation;Data.extendedBuildDistance=p2.ExtendedBuildDistance;
            Data.staggerProtection=p2.StaggerProtection;
            Data.instantCook=p2.InstantCook;Data.instantFerment=p2.InstantFerment;Data.instantSmelt=p2.InstantSmelt;Data.instantPlantGrow=p2.InstantPlantGrow;
            Data.outgoingDamageMult=p2.OutgoingDamageMult;Data.incomingDamageMult=p2.IncomingDamageMult;Data.projectileSpeedMult=p2.ProjectileSpeedMult;
            Data.hitPushMult=p2.HitPushMult;Data.buildDistanceMult=p2.BuildDistanceMult;Data.productionSpeedMult=p2.ProductionSpeedMult;
            Data.itemSpawnMode=p2.ItemSpawnMode;
            Data.weaponVfx=p2.WeaponVfx;Data.feetTrailVfx=p2.FeetTrailVfx;Data.autoPickupFilter=p2.AutoPickupFilter;
            Data.repairHammerAoe=p2.RepairHammerAoe;Data.autoFeedTames=p2.AutoFeedTames;
            Data.freecam=p2.Freecam||(GameCamera.instance&&GameCamera.InFreeFly());
            Data.photoMode=p2.PhotoMode;Data.photoFreeze=p2.PhotoFreeze;
            Data.minimapZoomControl=p2.MinimapZoomControl;Data.tpToPing=p2.TpToPing;Data.disablePortalFade=p2.DisablePortalFade;
            Data.sitMountAnywhere=p2.SitMountAnywhere;Data.groundSpawnAtCrosshair=p2.GroundSpawnAtCrosshair;
            Data.freecamBuild=p2.FreecamBuild;Data.meleeTargetLock=p2.MeleeTargetLock;Data.meleeLockPlayers=p2.MeleeLockPlayers;
            Data.flyingMount=p2.FlyingMount;Data.mountAnyCreature=p2.MountAnyCreature;
            if (Player.m_localPlayer)
            {
                var p = Player.m_localPlayer;
                Data.god = p.InGodMode(); Data.ghost = p.InGhostMode(); Data.fly = p.IsDebugFlying(); Data.noCost = p.PlacementCostDisabled;
            }
            }
            Data.pasteLevelGround=Release250.PasteLevelGround;Data.pasteLevelPerPiece=Release250.PasteLevelPerPiece;Data.levelOnPlace=Release250.LevelOnPlace;Data.pasteFeather=Release250.PasteFeather;
            Data.autoPastePreview=Release250.AutoPastePreview;Data.pasteSnapToGround=Release250.PasteSnapToGround;Data.pasteAnchorSnap=Release250.PasteAnchorSnap;Data.applyTerrainProfile=Release250.ApplyTerrainProfile;
            Data.freeBuildRotation=FreeBuildRotation.Enabled;Data.freeBuildRotationLocal=FreeBuildRotation.LocalFrame;Data.rotationSnapDivisions=FreeBuildRotation.SnapDivisions;
            Data.universalSnap=PlacementSnap.UniversalSnap;
            Data.dragBuilding=DragBuilding.Enabled;Data.dragMaxPieces=DragBuilding.MaxPieces;
            Data.placementHud=PlacementHud.Enabled;Data.hudRotation=PlacementHud.ShowRotation;Data.hudAxisStep=PlacementHud.ShowAxisStep;Data.hudPieceCount=PlacementHud.ShowPieceCount;
            Data.hudLockState=PlacementHud.ShowLockState;Data.hudGroundSnap=PlacementHud.ShowGroundSnap;Data.hudInTerrain=PlacementHud.ShowInTerrain;Data.hudPrefabName=PlacementHud.ShowPrefabName;
            Data.playerInvOverride = InventoryCapacity.OverrideBagSize;
            Data.playerInvOverrideSet = true;
            Data.playerInvRows = InventoryCapacity.PlayerRows;
            Data.playerInvExtraCols = InventoryCapacity.PlayerExtraCols;
            Data.chestBonusRows = InventoryCapacity.ChestBonusRows;
            Data.stackSizeMult = InventoryCapacity.StackMult;
            Data.stackSizeOverride = InventoryCapacity.StackOverride;
        }
        private static void PackBinds(SettingsData data)
        {
            var binds = data.featureBinds ?? new HotkeyBindData[0];
            data.featureBindIds = new string[binds.Length];
            data.featureBindKeys = new int[binds.Length];
            for (int i = 0; i < binds.Length; i++)
            {
                data.featureBindIds[i] = binds[i]?.id ?? "";
                data.featureBindKeys[i] = binds[i] == null ? 0 : binds[i].key;
            }
        }

        private static HotkeyBindData[] UnpackBinds(SettingsData data)
        {
            if (data.featureBindIds != null && data.featureBindKeys != null
                && data.featureBindIds.Length > 0 && data.featureBindIds.Length == data.featureBindKeys.Length)
            {
                var list = new HotkeyBindData[data.featureBindIds.Length];
                for (int i = 0; i < list.Length; i++)
                    list[i] = new HotkeyBindData { id = data.featureBindIds[i] ?? "", key = data.featureBindKeys[i] };
                return list;
            }
            return data.featureBinds;
        }

        private static void PackFilters(SettingsData data)
        {
            var filters = data.espFilters ?? new EspFilter[0];
            data.espFilterCategories = new int[filters.Length];
            data.espFilterPrefabs = new string[filters.Length];
            for (int i = 0; i < filters.Length; i++)
            {
                data.espFilterCategories[i] = filters[i] == null ? 0 : filters[i].category;
                data.espFilterPrefabs[i] = filters[i]?.prefab ?? "";
            }
        }

        private static EspFilter[] UnpackFilters(SettingsData data)
        {
            if (data.espFilterCategories != null && data.espFilterPrefabs != null
                && data.espFilterCategories.Length > 0 && data.espFilterCategories.Length == data.espFilterPrefabs.Length)
            {
                var list = new EspFilter[data.espFilterCategories.Length];
                for (int i = 0; i < list.Length; i++)
                    list[i] = new EspFilter { category = data.espFilterCategories[i], prefab = data.espFilterPrefabs[i] ?? "" };
                return list;
            }
            return data.espFilters;
        }

        internal void Save(bool notify = true)
        {
            CaptureFromRuntime();
            Data.Validate();
            string directory = Path.GetDirectoryName(PathName);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = PathName + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(Data, true));
            if (File.Exists(PathName)) File.Replace(temp, PathName, PathName + ".bak"); else File.Move(temp, PathName);
            if (notify) app.Report(L.T("toast.settings_saved"));
        }
        internal void Reset()
        {
            Data = new SettingsData();
            InventoryCapacity.Reset();
            Apply();
            app.Report(L.T("toast.defaults_restored"));
        }
    }
}
