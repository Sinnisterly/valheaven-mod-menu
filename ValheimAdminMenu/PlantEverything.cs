using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ValheimAdminMenu
{
    internal sealed class PlantRecipe : MonoBehaviour
    {
        public GameObject Prefab;
        public bool Decorative;
    }
    internal static class PlantEverything
    {
        internal const string OwnerKey="valheaven.planted.owner", DecorativeKey="valheaven.planted.decorative";
        private static PieceTable table;
        private static GameObject root;
        private static readonly List<GameObject> recipes=new List<GameObject>();
        private static bool originalRemoval;
        private static List<Piece.PieceCategory> originalCategories;
        private static List<string> originalLabels;
        private static readonly Dictionary<string,Piece.PieceCategory> groups=new Dictionary<string,Piece.PieceCategory>();
        private static float nextTry;
        internal static bool Enabled=>Plugin.Instance && Plugin.Instance.Settings.Data.plantEverything;
        internal static bool UsingCultivator(Player player)=>player && player==Player.m_localPlayer && player.GetInventory().GetAllItems().Any(i=>i.m_equipped && i.m_dropPrefab && Utils.GetPrefabName(i.m_dropPrefab)=="Cultivator");
        internal static void Tick()
        {
            if(!Enabled) { if(root)Clear();return; }
            if(root && table)return;
            if(Time.realtimeSinceStartup<nextTry)return;nextTry=Time.realtimeSinceStartup+2;
            if(!ObjectDB.instance || !ZNetScene.instance)return;
            var cultivator=ObjectDB.instance.GetItemPrefab("Cultivator");
            if(!cultivator)return;
            table=cultivator.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
            if(!table)return;
            root=new GameObject("Valheaven planting recipes");root.SetActive(false);
            originalRemoval=table.m_canRemovePieces;table.m_canRemovePieces=true;
            originalCategories=new List<Piece.PieceCategory>(table.m_categories);
            originalLabels=new List<string>(table.m_categoryLabels);
            groups.Clear();
            foreach(string group in new[]{"Harvestable plants","Crops","Trees","Decorative plants"})
            {
                var category=Enumerable.Range(0,(int)Piece.PieceCategory.Max).Select(i=>(Piece.PieceCategory)i).First(c=>!table.m_categories.Contains(c));
                groups[group]=category;table.m_categories.Add(category);table.m_categoryLabels.Add(group);
            }
            var icon=cultivator.GetComponent<ItemDrop>().m_itemData.GetIcon();
            var candidates=new List<GameObject>(ZNetScene.instance.m_prefabs);
            if(ZoneSystem.instance)candidates.AddRange(ZoneSystem.instance.m_vegetation.Where(v=>v.m_prefab).Select(v=>v.m_prefab));
            // Grown prefabs may only appear via Plant.m_grownPrefabs on the sapling.
            foreach(var prefab in candidates.Where(p=>p).ToArray())
            {
                var plant=prefab.GetComponent<Plant>();
                if(plant?.m_grownPrefabs!=null)candidates.AddRange(plant.m_grownPrefabs);
            }
            foreach(var prefab in candidates.Where(p=>p && p.GetComponent<ZNetView>()).GroupBy(p=>p.name).Select(g=>g.First()).OrderBy(p=>p.name,StringComparer.OrdinalIgnoreCase))
            {
                // Skip unregistered prefabs (won't persist for vanilla clients).
                if(!ZNetScene.instance.GetPrefab(prefab.name))continue;
                var plant=prefab.GetComponent<Plant>();
                var pickable=prefab.GetComponentInChildren<Pickable>(true);
                bool growsTree=plant && plant.m_grownPrefabs!=null && plant.m_grownPrefabs.Any(p=>p && p.GetComponent<TreeBase>());
                bool tree=prefab.GetComponent<TreeBase>();
                bool harvest=pickable && pickable.m_itemPrefab && NaturalCatalogRules.HarvestablePlant(pickable.m_itemPrefab.name);
                string group=NaturalCatalogRules.PlantGroup(plant,growsTree,tree,harvest,NaturalCatalogRules.DecorativePlant(prefab.name));
                if(group==null)continue;
                AddRecipe(prefab,group=="Decorative plants",group,icon);
                if(growsTree)AddRecipe(prefab,true,"Decorative plants",icon);
            }
            if(Player.m_localPlayer)Reflection.Method(typeof(Player),"UpdateAvailablePiecesList").Invoke(Player.m_localPlayer,null);
        }
        private static void AddRecipe(GameObject prefab,bool decorative,string group,Sprite fallback)
        {
            bool prior=ZNetView.m_forceDisableInit;ZNetView.m_forceDisableInit=true;
            try
            {
                var proxy=UnityEngine.Object.Instantiate(prefab,root.transform,false);
                proxy.name="VHPlant_"+prefab.name+(decorative?"_Decorative":"");
                // Ghost is local; Place() instantiates the real prefab.
                foreach(var behaviour in proxy.GetComponentsInChildren<MonoBehaviour>(true))
                    if(!(behaviour is Piece))UnityEngine.Object.DestroyImmediate(behaviour);
                var piece=proxy.GetComponent<Piece>()??proxy.AddComponent<Piece>();
                piece.enabled=true;piece.m_enabled=true;piece.m_category=groups[group];
                var plant=prefab.GetComponent<Plant>();var pick=prefab.GetComponentInChildren<Pickable>(true);
                var harvest=pick && pick.m_itemPrefab?pick.m_itemPrefab.GetComponent<ItemDrop>():null;
                string label=plant?plant.m_name:harvest?harvest.m_itemData.m_shared.m_name:prefab.GetComponent<HoverText>()?.m_text;
                if(string.IsNullOrWhiteSpace(label))label=NaturalCatalogRules.FriendlyPrefabName(prefab.name);
                piece.m_name=(Localization.instance!=null?Localization.instance.Localize(label):label)+(decorative && plant?" (decoration)":"");
                piece.m_description=decorative?(plant?"Stays small. Does not grow into a tree.":"A decoration for your garden."):plant?"Grows normally after planting.":"Place this in your garden for free.";
                if(harvest && harvest.m_itemData.GetIcon())piece.m_icon=harvest.m_itemData.GetIcon();
                piece.m_icon=piece.m_icon?piece.m_icon:fallback;
                piece.m_resources=new Piece.Requirement[0];piece.m_craftingStation=null;piece.m_dlc="";
                piece.m_groundOnly=true;piece.m_groundPiece=true;piece.m_cultivatedGroundOnly=false;
                piece.m_onlyInBiome=Heightmap.Biome.None;piece.m_spaceRequirement=0;piece.m_noClipping=false;
                piece.m_canBeRemoved=false;piece.m_notOnWood=false;piece.m_vegetationGroundOnly=false;
                var recipe=proxy.AddComponent<PlantRecipe>();recipe.Prefab=prefab;recipe.Decorative=decorative;
                proxy.SetActive(true);recipes.Add(proxy);table.m_pieces.Add(proxy);
            }
            finally { ZNetView.m_forceDisableInit=prior; }
        }
        internal static void Refresh()
        {
            Clear();nextTry=0;Tick();
            Plugin.Instance.Report("Plant list refreshed: "+recipes.Count+" choices.");
        }
        internal static void Clear()
        {
            if(table) {
                foreach(var recipe in recipes)table.m_pieces.Remove(recipe);table.m_canRemovePieces=originalRemoval;
                if(originalCategories!=null) {table.m_categories.Clear();table.m_categories.AddRange(originalCategories);}
                if(originalLabels!=null) {table.m_categoryLabels.Clear();table.m_categoryLabels.AddRange(originalLabels);}
            }
            recipes.Clear();if(root)UnityEngine.Object.Destroy(root);root=null;table=null;
            if(Player.m_localPlayer)Reflection.Method(typeof(Player),"UpdateAvailablePiecesList").Invoke(Player.m_localPlayer,null);
        }
        internal static bool IsOurTable(PieceTable value)=>Enabled && table && table==value;
        internal static long Owner(Component component)
        { var view=component?component.GetComponent<ZNetView>():null;return view && view.IsValid()?view.GetZDO().GetLong(OwnerKey,0):0; }
        internal static bool Decorative(Component component)
        { var view=component?component.GetComponent<ZNetView>():null;return view && view.IsValid() && view.GetZDO().GetBool(DecorativeKey,false); }
        internal static void Place(Player player,PlantRecipe recipe,Vector3 pos,Quaternion rot)
        {
            if(!Enabled || !UsingCultivator(player))throw new InvalidOperationException("Enable Plant Everything and equip the cultivator.");
            if(!PrivateArea.CheckAccess(pos,0,true,true))throw new InvalidOperationException("Planting is blocked by a ward.");
            if(Vector3.Distance(player.transform.position,pos)>12)throw new InvalidOperationException("Plant closer to your character.");
            // Need terrain even if free-build skips placement checks.
            if(!Physics.Raycast(pos+Vector3.up,Vector3.down,out var surface,2f,LayerMask.GetMask("terrain"),QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("Plants require a terrain surface.");
            var obj=UnityEngine.Object.Instantiate(recipe.Prefab,surface.point,rot);
            var view=obj.GetComponent<ZNetView>();
            if(!view || !view.IsValid()) { UnityEngine.Object.Destroy(obj);throw new InvalidOperationException("Plant could not be networked."); }
            var zdo=view.GetZDO();zdo.Set(OwnerKey,player.GetPlayerID());zdo.Set(DecorativeKey,recipe.Decorative);
            // Far-future plantTime so decorations never mature (vanilla clients honor it).
            if(recipe.Decorative && obj.GetComponent<Plant>())zdo.Set(ZDOVars.s_plantTime,DateTime.MaxValue.Ticks);
            var plant=obj.GetComponent<Plant>();if(plant)ConfigureGrowth(plant);
        }
        internal static void ConfigureGrowth(Plant plant)
        {
            if(Owner(plant)==0)return;
            plant.m_needCultivatedGround=false;plant.m_destroyIfCantGrow=false;
            plant.m_growRadius=0;plant.m_tolerateCold=true;plant.m_tolerateHeat=true;
        }
        internal static bool Remove(Player player)
        {
            var camera=GameCamera.instance?GameCamera.instance.transform:Camera.main?.transform;
            if(!camera || !Physics.Raycast(camera.position,camera.forward,out var hit,12,LayerMask.GetMask("Default","static_solid","Default_small","piece","terrain"),QueryTriggerInteraction.Ignore))return false;
            var view=hit.collider.GetComponentInParent<ZNetView>();
            if(!view || !view.IsValid() || view.GetZDO().GetLong(OwnerKey,0)!=player.GetPlayerID())return false;
            if(Vector3.Distance(player.transform.position,view.transform.position)>12 || !PrivateArea.CheckAccess(view.transform.position,0,true,true))return false;
            view.ClaimOwnership();if(!view.IsOwner())return false;
            view.Destroy();return true;
        }
    }
    [HarmonyPatch(typeof(PieceTable),nameof(PieceTable.UpdateAvailable))]
    internal static class PlantRecipeListPatch
    { private static void Prefix(PieceTable __instance,ref bool noPlacementCost) { if(PlantEverything.IsOurTable(__instance))noPlacementCost=true; } }
    [HarmonyPatch(typeof(Player),nameof(Player.HaveRequirements),new[]{typeof(Piece),typeof(Player.RequirementMode)})]
    internal static class PlantRequirementsPatch
    { private static void Postfix(Player __instance,Piece piece,ref bool __result) { if(PlantEverything.Enabled && PlantEverything.UsingCultivator(__instance) && piece && piece.GetComponent<PlantRecipe>())__result=true; } }
    [HarmonyPatch(typeof(Player),"PlacePiece")]
    internal static class PlantPlacementPatch
    {
        private static bool Prefix(Player __instance,Piece piece,Vector3 pos,Quaternion rot)
        {
            var recipe=piece?piece.GetComponent<PlantRecipe>():null;if(!recipe)return true;
            try { PlantEverything.Place(__instance,recipe,pos,rot); }
            catch(Exception ex) { Plugin.Instance.Report(ex.Message); }
            return false;
        }
    }
    [HarmonyPatch(typeof(Player),"RemovePiece")]
    internal static class PlantRemovalPatch
    {
        private static bool Prefix(Player __instance,ref bool __result)
        { if(!PlantEverything.Enabled || !PlantEverything.UsingCultivator(__instance))return true;__result=PlantEverything.Remove(__instance);return false; }
    }
    [HarmonyPatch(typeof(Plant),"UpdateHealth")]
    internal static class PlantedHealthPatch
    {
        private static bool Prefix(Plant __instance,ref Plant.Status ___m_status)
        { if(PlantEverything.Owner(__instance)==0)return true;PlantEverything.ConfigureGrowth(__instance);___m_status=Plant.Status.Healthy;return false; }
    }
    [HarmonyPatch(typeof(Plant),nameof(Plant.Grow))]
    internal static class PlantedGrowPatch
    {
        private static bool Prefix(Plant __instance,out long __state) { __state=PlantEverything.Owner(__instance);return !PlantEverything.Decorative(__instance); }
        private static void Postfix(GameObject __result,long __state)
        { if(__state==0 || !__result)return;var view=__result.GetComponent<ZNetView>();if(view && view.IsValid())view.GetZDO().Set(PlantEverything.OwnerKey,__state); }
    }
}
