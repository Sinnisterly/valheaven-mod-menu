using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using ValheimAdminMenu;

internal static class Program
{
    private static int count;
    private static int Main(string[] args)
    {
        string game = args.Length == 0 ? @"D:\SteamLibrary\steamapps\common\Valheim" : args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            string managed = Path.Combine(game, "valheim_Data", "Managed");
            string path = Path.Combine(managed, name);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
            return null;
        };
        try { Run(); System.Console.WriteLine("PASS: " + count + " checks. No game scene or world was opened."); return 0; }
        catch (Exception ex) { System.Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool success, string message)
    {
        if (!success) throw new Exception("FAIL: " + message);
        count++; System.Console.WriteLine("PASS: " + message);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        ValheimAdminMenu.Reflection.Check();
        Check(true, "All private runtime signatures used by compatibility check resolve");
        Check(Rules.ValidPosition(new Vector3(0, 100, 0)), "Ordinary teleport destination accepted");
        Check(Rules.ValidPosition(new Vector3(-20000, 10000, 20000)), "Coordinate boundary accepted");
        foreach (var p in new[] { new Vector3(float.NaN,0,0), new Vector3(0,float.PositiveInfinity,0), new Vector3(0,0,float.NegativeInfinity), new Vector3(20001,0,0), new Vector3(0,10001,0), new Vector3(0,0,-20001) })
            Check(!Rules.ValidPosition(p), "Invalid or out-of-range coordinate rejected: " + p);
        Check(Rules.Text("<b>test</b>\nnext") == "[b]test[/b] next", "User text cannot inject rich-text markup or new lines");
        Check(Rules.Text(new string('x', 500)).Length == 240, "Broadcast text length bounded");
        Check(Rules.Text(null) == "", "Null message handled");
        Check(Rules.ValidHotkey(KeyCode.F8), "F8 is a valid menu binding");
        Check(Rules.ValidHotkey(KeyCode.Insert), "Insert is a valid menu binding");
        foreach(var key in new[]{ KeyCode.Escape,KeyCode.None,KeyCode.Mouse0,KeyCode.Mouse1,(KeyCode)99999 })
            Check(!Rules.ValidHotkey(key),"Invalid or reserved binding rejected: "+key);
        var settings=new SettingsData { hotkey=(int)KeyCode.Escape, scale=float.NaN, espFont=400, espMax=-1, espOpacity=float.PositiveInfinity,
            espRadius=900, espRefresh=0, categories=null,colors=null, worldRadius=-1,brushRadius=500,interactionDistance=999,
            fullbrightIntensity=float.NaN };
        settings.Validate();
        Check(settings.hotkey==(int)KeyCode.F8,"Invalid saved hotkey recovers to F8");
        Check(settings.scale==1,"Invalid saved scale recovers to default");
        Check(settings.espFont==24 && settings.espMax==10,"Saved label limits are bounded");
        Check(settings.espOpacity==.3f && settings.espRadius==500 && settings.espRefresh==.25f,"Saved ESP values are finite and bounded");
        Check(settings.categories.Length==8 && settings.colors.Length==8,"Missing category settings recover to complete defaults");
        Check(settings.worldRadius==1 && settings.brushRadius==50 && settings.interactionDistance==50,"Saved gameplay ranges are bounded");
        Check(settings.fullbrightIntensity==.5f,"Saved fullbright intensity is finite and bounded");
        Check(GateBypass.IsBypassKey("$item_cryptkey") && GateBypass.IsBypassKey("SEALBREAKER") && !GateBypass.IsBypassKey("Stone"),"Gate bypass recognizes inventory item names only");
        Check(ValheimAdminMenu.MenuView.LocationLabel("GDKing")=="The Elder" && ValheimAdminMenu.MenuView.LocationLabel("location_dvergrboss")=="The Queen","Boss marker prefab names become player-facing names");
        Check(ValheimAdminMenu.MenuView.LocationLabel("Vendor_BlackForest")=="Haldor" && ValheimAdminMenu.MenuView.LocationLabel("UpgradeStation")=="Forge of Potential","Trader and forge markers use player-facing names");
        Check(CatalogRules.IconIndex(0,0)==-1,"Missing icons do not cause an array access");
        Check(CatalogRules.IconIndex(2,9)==1,"Oversized item variant uses a valid icon index");
        Check(CatalogRules.IconIndex(2,-3)==0,"Negative item variant uses a valid icon index");
        Check(CatalogRules.Matches("SwordIron","Iron sword"," SWORD "),"Catalog search ignores case and surrounding spaces");
        Check(CatalogRules.Matches("SwordIron","Iron sword","SwordIron"),"Catalog matches prefab ID");
        Check(!CatalogRules.Matches("SwordIron","Iron sword","wood"),"Catalog excludes unrelated search results");
        var skillEntry=new Skills.Skill(new Skills.SkillDef { m_skill=Skills.SkillType.Swords });
        skillEntry.m_level=87.75f; skillEntry.m_accumulator=9;
        PlayerFeatures.SetBaseSkill(skillEntry,25.5f);
        Check(skillEntry.m_level==25.5f && skillEntry.m_accumulator==0,"Exact skill edit lowers fractional base level and clears progress");
        PlayerFeatures.SetBaseSkill(skillEntry,72);
        Check(skillEntry.m_level==72,"Exact skill edit raises to requested level without maximizing");
        foreach(float invalid in new[]{float.NaN,-1f,101f})
        {
            bool rejected=false;
            try { PlayerFeatures.SetBaseSkill(skillEntry,invalid); } catch(ArgumentException) { rejected=true; }
            Check(rejected && skillEntry.m_level==72,"Invalid skill edit rejected without changing level: "+invalid);
        }
        Check(ExpansionFeatures.TrainingFactor(100,0,1,1,1)==1,"Fast training respects the level cap");
        Check(ExpansionFeatures.TrainingFactor(20,0,1,0,1)==1,"Fast training preserves disabled XP gain");
        Check(new ItemEdits().Valid(),"Default item overrides are valid");
        Check(!new ItemEdits{weight=float.NaN}.Valid(),"Item editor rejects non-finite values");
        Check(!new ItemEdits{damage=new HitData.DamageTypes{m_slash=-1}}.Valid(),"Item editor rejects negative damage");
        Check(ItemEditor.MaxQuality==99999,"Item quality cap allows high refinement values");
        var vanillaDamage=new HitData.DamageTypes { m_slash=40,m_fire=10,m_chop=7 };
        var selective=new ItemEdits { damageOverride=true,damageMask=1,damage=new HitData.DamageTypes {m_slash=500} };
        var merged=SpawnPresets.MergeDamage(vanillaDamage,selective);
        Check(merged.m_slash==500 && merged.m_fire==10 && merged.m_chop==7,"Selective damage override preserves untouched damage types");
        vanillaDamage.m_fire=20;
        Check(SpawnPresets.MergeDamage(vanillaDamage,selective).m_fire==20,"Untouched damage retains changes from quality scaling");
        selective.damage.m_slash=0;
        Check(SpawnPresets.MergeDamage(vanillaDamage,selective).m_slash==0,"Explicit zero damage differs from Vanilla");
        selective.damageMask=0;
        Check(SpawnPresets.MergeDamage(vanillaDamage,selective).m_fire==0,"Legacy full-profile overrides retain their behavior");
        Check(new ItemEdits { extraStats=true,knockback=0,backstab=99999 }.Valid(),"Extra stat boundaries accepted");
        Check(!new ItemEdits { extraStats=true,knockback=float.PositiveInfinity }.Valid(),"Non-finite knockback rejected");
        Check(!new ItemEdits { extraStats=true,backstab=100000 }.Valid(),"Oversized backstab rejected");
        var stackA=new ItemDrop.ItemData {m_quality=1,m_crafterID=1,m_crafterName="Maker"};
        var stackB=new ItemDrop.ItemData {m_quality=1,m_crafterID=1,m_crafterName="Maker"};
        Check(SpawnPresets.SameMetadata(stackA,stackB),"Identical item metadata can stack");
        stackA.m_customData[ItemEditor.Key]="edited";
        Check(!SpawnPresets.SameMetadata(stackA,stackB),"Edited and vanilla item stacks stay separate");
        stackB.m_customData[ItemEditor.Key]="edited";
        Check(SpawnPresets.SameMetadata(stackA,stackB),"Matching edited item metadata can stack");
        stackB.m_crafterName="Other";
        Check(!SpawnPresets.SameMetadata(stackA,stackB),"Different crafters cannot merge stacks");
        var queuedSettings=new SettingsData {itemSpawnMode=3,spawnPresets=null,espFilters=null};queuedSettings.Validate();
        Check(queuedSettings.itemSpawnMode==3 && queuedSettings.spawnPresets!=null && queuedSettings.espFilters!=null,"On-kill destination survives settings validation and new arrays migrate");
        var groundMethod=HarmonyLib.AccessTools.Method(typeof(ItemDrop),"AutoStackItems");
        var originalInstructions=HarmonyLib.PatchProcessor.GetOriginalInstructions(groundMethod);
        var groundTranspiler=typeof(PresetGroundStackPatch).GetMethod("Transpiler",BindingFlags.NonPublic|BindingFlags.Static);
        var rewritten=((System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction>)groundTranspiler.Invoke(null,new object[]{originalInstructions})).ToList();
        Check(rewritten.Count(i=>i.operand is MethodInfo mi && mi.DeclaringType==typeof(PresetGroundStackPatch))==1,"Actual game IL receives exactly one metadata-aware ground stacking comparison");

        Check(NaturalCatalogRules.OreNodeLabel("rock4_forest")==null,"Forest rocks do not match ore by substring");
        Check(NaturalCatalogRules.OreNodeLabel("forest_mine_rock")==null,"Generic mine rocks require a mineral resource");
        Check(NaturalCatalogRules.OreNodeLabel("rock4_copper")=="$item_copperore","Copper node fallback uses the resource name");
        Check(NaturalCatalogRules.OreNodeLabel("silvervein_frac")=="$item_silverore","Silver vein replacements retain a resource label");
        Check(NaturalCatalogRules.OreItemLabel("CopperOre")=="$item_copperore","Copper drops identify copper deposits");
        Check(NaturalCatalogRules.OreItemLabel("Stone")==null,"Stone drops do not identify ore deposits");
        Check(NaturalCatalogRules.OreItemLabel("IronScrap")=="$item_ironscrap","Iron scrap identifies iron deposits");
        foreach(var resource in new[]{"Raspberry","Blueberries","Cloudberry","MushroomJotunPuffs","MushroomMagecap","Carrot","Turnip","Onion","Barley","Flax","Dandelion","Thistle"})
            Check(NaturalCatalogRules.HarvestablePlant(resource),"Cultivator includes plant resource: "+resource);
        foreach(var decoration in new[]{"Bush01","Bush02_en","Shrub_2","Heath","Fern","Vines","Reed","beech_small1","SwampBush1","YggdrasilShoot"})
            Check(NaturalCatalogRules.DecorativePlant(decoration),"Cultivator includes decoration: "+decoration);
        Check(!NaturalCatalogRules.DecorativePlant("vfx_BushDestroyed"),"Plant catalog excludes foliage effects");
        Check(!NaturalCatalogRules.HarvestablePlant("SurtlingCore") && !NaturalCatalogRules.DecorativePlant("Bush01_destroyed"),"Plant list excludes unrelated resources and broken vegetation");
        Check(NaturalCatalogRules.PlantGroup(true,false,false,false,false)=="Crops","Crop plants are included without berry or mushroom names");
        Check(NaturalCatalogRules.PlantGroup(false,false,true,false,false)=="Trees","Mature trees are included");
        Check(NaturalCatalogRules.PlantGroup(false,false,false,false,true)=="Decorative plants","Bushes get a decorative category");

        var rect=Overlay.LabelBounds(100,100,60,12,3);
        Check(rect.x==67 && rect.y==91 && rect.width==66 && rect.height==18,"Compact label bounds fit text plus spacing");
        Check(!new SettingsData().espBackground && new SettingsData().espFont==11,"Default ESP uses small text without fixed background bars");
        var pluginAssembly=typeof(Plugin).Assembly;
        Check(!pluginAssembly.GetTypes().Any(t=>t.Name=="PeerRegistrationPatch"),"Custom server handshake patch removed");
        Check(typeof(Plugin).GetMethod("RequireAdmin",BindingFlags.Instance|BindingFlags.NonPublic)==null,"World administrator gate removed");
        Check(typeof(Plugin).GetCustomAttributesData().All(a=>a.AttributeType.Name!="BepInDependency"),"No companion plugin dependency added");
        Check(pluginAssembly.GetType("ValheimAdminMenu.HostTools")!=null,"Host authority helper present");
        Check(typeof(ExpansionFeatures).GetMethod("ApplyWeather",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Weather apply API present");
        Check(typeof(ExpansionFeatures).GetMethod("ClearWeather",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Weather clear API present");
        Check(Plugin.ProductVersion=="3.0.6","Product version is 3.0.6");
        Check(pluginAssembly.GetType("ValheimAdminMenu.FeatureSuite")!=null,"FeatureSuite helper present");
        Check(pluginAssembly.GetType("ValheimAdminMenu.InventoryCapacity")!=null,"InventoryCapacity helper present");
        Check(typeof(SettingsData).GetField("playerInvRows")!=null && typeof(SettingsData).GetField("stackSizeMult")!=null,"Inventory capacity settings persist");
        Check(BlueprintStore.ValidateSerializationRoundTrip(48),"Blueprint JSON round-trip keeps 48 pieces");
        Check(pluginAssembly.GetType("ValheimAdminMenu.BlueprintJson")!=null,"BlueprintJson writer present");
        Check(typeof(InventoryCapacity).GetField("PlayerRows",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Player inventory rows field present");
        Check(typeof(InventoryCapacity).GetField("ChestBonusRows",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Chest bonus rows field present");
        Check(typeof(InventoryCapacity).GetMethod("ApplyAll",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Inventory capacity ApplyAll present");
        Check(pluginAssembly.GetType("ValheimAdminMenu.VfxSuite")!=null,"VfxSuite helper present");
        Check(pluginAssembly.GetType("ValheimAdminMenu.BlueprintStore")!=null,"Blueprint store present");
        Check(typeof(Release250).GetField("IncludeWorldStructures",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"World structure copy toggle present");
        Check(typeof(Release250).GetMethod("FitSelectionToNearbyLocation",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Fit selection to Location API present");
        Check(typeof(BlueprintStore).GetMethod("SaveFromClipboard",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Blueprint save API present");
        Check(typeof(BlueprintStore).GetMethod("Export",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Blueprint export API present");
        Check(typeof(BlueprintStore).GetMethod("ImportFile",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Blueprint import API present");
        Check(typeof(AppPaths).GetProperty("BlueprintsPath",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Blueprints path present");
        Check(Release250.MaxClipboardPieces==25000,"Clipboard piece cap is 25000");
        Check(typeof(VfxSuite).GetField("AttachWeapon",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"VFX attach modes present");
        Check(typeof(VfxSuite).GetField("DoEffect",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"VFX DoEffect toggle present");
        Check(typeof(VfxSuite).GetField("Scale",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"VFX Scale field present");
        Check(typeof(VfxSuite).GetField("ReplaySpeed",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"VFX ReplaySpeed field present");
        Check(typeof(Release250).GetMethod("BeginPastePreview",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Paste preview API present");
        Check(typeof(Release250).GetMethod("ConfirmPaste",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Paste confirm API present");
        Check(typeof(Release250).GetMethod("GrowCopyToOverlapping",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Copy grow selection API present");
        Check(pluginAssembly.GetType("ValheimAdminMenu.MenuWrapFit")!=null,"Menu wrap-fit helper present");
        Check(typeof(SettingsData).GetField("vfxReplaySpeed")!=null,"VFX replay speed persists");
        Check(pluginAssembly.GetType("ValheimAdminMenu.MenuSearchIndex")!=null,"Menu search index present");
        Check(typeof(PhaseFeatures).GetField("OneHitStructures",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!=null,"One-hit structures field present");
        Check(typeof(PhaseFeatures).GetField("KeepFoodOnDeath",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Keep food on death field present");
        Check(typeof(PhaseFeatures).GetField("TpToPing",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!=null,"TP to ping field present");
        Check(typeof(SettingsData).GetField("itemSpawnMode")!=null && typeof(SettingsData).GetField("junkExtra")!=null,"2.5.x settings fields persist");
        Check(typeof(Release250).GetMethod("SetCornerA",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Copy corner A API present");
        Check(typeof(Release250).GetMethod("DrawCopyVolume",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Copy volume draw API present");
        Check(typeof(Release250).GetField("CornerA",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Copy CornerA field present");
        Check(typeof(SettingsData).GetField("version")!=null,"Settings version field present");
        Check(new SettingsData().version==10,"Default settings schema is v10");
        Check(new SettingsData().menuSectionFolds,"Default section layout is dropdowns");
        Check(new SettingsData().language==LocaleCodes.Auto,"Default menu language follows Valheim");
        Check(LocaleCodes.FromValheim("Russian")==LocaleCodes.Russian,"Valheim Russian maps to menu ru");
        Check(LocaleCodes.FromValheim("Portuguese_Brazilian")==LocaleCodes.PortugueseBrazil,"Valheim Brazilian Portuguese maps to pt-BR");
        Check(LocaleCodes.FromValheim("Chinese")==LocaleCodes.ChineseSimplified,"Valheim Chinese maps to menu zh-CN");
        Check(LocaleCodes.FromValheim("Japanese")==LocaleCodes.Japanese,"Valheim Japanese maps to menu ja");
        Check(LocaleCodes.NormalizePreference("RU")==LocaleCodes.Russian,"Language codes normalize case-insensitively");
        Check(LocaleCodes.NormalizePreference("bogus")==LocaleCodes.Auto,"Unknown language preference resets to auto");
        var langSettings = new SettingsData { language = "not-a-lang" };
        langSettings.Validate();
        Check(langSettings.language==LocaleCodes.Auto,"Invalid saved language recovers to auto");
        Check(typeof(ValheimAdminMenu.MenuLanguage).GetMethod("ApplyPreference", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!=null,"Menu language resolver present");
        Check(pluginAssembly.GetType("ValheimAdminMenu.MenuFonts")!=null,"Menu font resolver present");
        Check(pluginAssembly.GetType("ValheimAdminMenu.LocaleCodes")!=null,"Locale code map present");
        Check(pluginAssembly.GetType("ValheimAdminMenu.ChestInventory")!=null,"Chest inventory helper present");
        Check(pluginAssembly.GetType("ValheimAdminMenu.HotkeyRegistry")!=null,"Hotkey registry present");
        Check(typeof(PhaseFeatures).GetField("CraftFromChests",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Craft from chests field present");
        Check(pluginAssembly.GetType("ValheimAdminMenu.TeleportBookmarks")!=null,"Teleport bookmarks helper present");
        Check(typeof(PhaseFeatures).GetField("ProjectileSpeedMult",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Projectile speed field present");
        Check(typeof(PhaseFeatures).GetField("BoatFly",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Boat fly field present");
        Check(pluginAssembly.GetType("ValheimAdminMenu.ForceSleepAllPatch")==null,"Force-sleep host patch removed");
        Check(typeof(PhaseFeatures).GetField("ForceSleepAll",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)==null,"ForceSleepAll field removed");
        Check(typeof(SettingsData).GetField("noSpawn")==null && typeof(SettingsData).GetField("forceSleepAll")==null,"Host-only settings fields removed");
        Check(typeof(WorldTools).GetMethod("UndoTerrain",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Terrain undo API present");
        Check(typeof(SettingsData).GetField("precisionBrush")!=null && typeof(SettingsData).GetField("paintMode")!=null,"Terrain brush settings persist");
        // Compile new patch wrappers without game callbacks. Plant.Grow embeds Unity ECalls and requires the game runtime; its target is checked below.
        var featureHarmony=new HarmonyLib.Harmony("valheaven.validation.featurepack");
        try {
            foreach(var patchType in new[]{typeof(GateBypass),typeof(PresetStackEqualityPatch),typeof(PresetStackContextPatch),typeof(PresetFindStackPatch),typeof(PresetStackSpacePatch),typeof(PresetLoadSharedPatch),typeof(PresetNetworkSharedPatch),typeof(PresetCloneSharedPatch),typeof(PresetGroundStackPatch),typeof(PlantRecipeListPatch),typeof(PlantRequirementsPatch),typeof(PlantPlacementPatch),typeof(PlantRemovalPatch),typeof(PlantedHealthPatch)}) {
                featureHarmony.CreateClassProcessor(patchType).Patch();Check(true,"Harmony wrapper compiles: "+patchType.Name);
            }
        } finally { featureHarmony.UnpatchAll("valheaven.validation.featurepack"); }

        // Resolve all declared Harmony patch targets without applying patches or invoking Unity callbacks.
        var assembly = typeof(Plugin).Assembly;
        foreach (var type in assembly.GetTypes())
        {
            var targetMethods = type.GetMethod("TargetMethods", BindingFlags.Static | BindingFlags.NonPublic);
            if (targetMethods != null)
            {
                var methods = (System.Collections.Generic.IEnumerable<MethodBase>)targetMethods.Invoke(null, null);
                foreach (var method in methods) Check(method != null, "Dynamic Harmony target: " + method.DeclaringType.Name + "." + method.Name);
            }
            foreach (var patch in type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false).Cast<HarmonyLib.HarmonyPatch>())
            {
                if (patch.info.declaringType != null && patch.info.methodName != null)
                    Check(patch.info.declaringType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Any(m => m.Name == patch.info.methodName), "Harmony target: " + patch.info.declaringType.Name + "." + patch.info.methodName);
            }
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var patch in method.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false).Cast<HarmonyLib.HarmonyPatch>())
                {
                    if (patch.info.declaringType == null || patch.info.methodName == null) continue;
                    MethodBase target;
                    if (patch.info.argumentTypes != null)
                        target = patch.info.declaringType.GetMethod(patch.info.methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, patch.info.argumentTypes, null);
                    else
                        target = patch.info.declaringType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(m => m.Name == patch.info.methodName);
                    Check(target != null, "Method-level Harmony target: " + patch.info.declaringType.Name + "." + patch.info.methodName);
                }
            }
        }
    }
}
