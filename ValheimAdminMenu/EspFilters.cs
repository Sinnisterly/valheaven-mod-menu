using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimAdminMenu
{
    [Serializable]
    public sealed class EspFilter { public int category; public string prefab; }
    internal sealed partial class Overlay
    {
        internal sealed class FilterOption { internal string Id, Label, Group; internal Kind Category; }
        internal static string PrefabId(GameObject obj)=>Utils.GetPrefabName(obj);
        private readonly Dictionary<string,FilterOption> filterCatalog=new Dictionary<string,FilterOption>();
        private readonly HashSet<string> ignored=new HashSet<string>(StringComparer.Ordinal);
        private EspFilter[] loadedFilters;
        private static string FilterKey(Kind kind,string id)=>(int)kind+":"+id;
        private void SyncFilters()
        {
            var data=app.Settings.Data.espFilters;if(ReferenceEquals(data,loadedFilters))return;
            loadedFilters=data;ignored.Clear();
            foreach(var f in data)if(f!=null && f.prefab!=null)ignored.Add(FilterKey((Kind)f.category,f.prefab));
        }
        internal bool Hidden(Kind kind,string id) { SyncFilters();return ignored.Contains(FilterKey(kind,id)); }
        internal void Hide(Kind kind,IEnumerable<string> ids,bool value)
        {
            var chosen=new HashSet<string>(ids);
            var values=app.Settings.Data.espFilters.Where(f=>f!=null && !(f.category==(int)kind && chosen.Contains(f.prefab))).ToList();
            if(value)values.AddRange(chosen.Select(id=>new EspFilter {category=(int)kind,prefab=id}));
            app.Settings.Data.espFilters=values.ToArray();nextScan=0;app.Settings.Save(false);
        }
        private void Register(GameObject obj,string raw,Kind kind)
        {
            string id=PrefabId(obj),group="";
            if(kind==Kind.Monster)foreach(var species in new[]{"Greydwarf","Draugr","Skeleton","Goblin","Fuling","Seeker","Dverger","Wolf","Blob"})
                if(id.StartsWith(species,StringComparison.OrdinalIgnoreCase)){group=species;break;}
            filterCatalog[FilterKey(kind,id)]=new FilterOption {Id=id,Label=Name(raw),Category=kind,Group=group};
        }
        internal List<FilterOption> Catalog(Kind kind)
        {
            filterCatalog.Clear();
            var prefabs=new List<GameObject>();
            if(ZNetScene.instance)prefabs.AddRange(ZNetScene.instance.m_prefabs);
            if(ObjectDB.instance)prefabs.AddRange(ObjectDB.instance.m_items);
            foreach(var obj in prefabs.Where(p=>p).Distinct())
            {
                var c=obj.GetComponent<Character>();if(c)Register(obj,c.m_name,c is Player?Kind.Player:Kind.Monster);
                var p=obj.GetComponent<Pickable>();if(p)Register(obj,p.m_itemPrefab?p.m_itemPrefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name??obj.name:obj.name,Kind.Pickable);
                var item=obj.GetComponent<ItemDrop>();if(item)Register(obj,item.m_itemData.m_shared.m_name,Kind.Drop);
                if(obj.GetComponent<TombStone>())Register(obj,obj.name,Kind.Tombstone);
                if(obj.GetComponent<Container>() && !obj.GetComponent<TombStone>())Register(obj,obj.GetComponent<Container>().GetHoverName(),Kind.Container);
                if(obj.GetComponent<Location>())Register(obj,obj.name,Kind.POI);
                if(DepositObject(obj))Register(obj,DepositName(obj),Kind.Deposit);
            }
            foreach(var location in Resources.FindObjectsOfTypeAll<Location>())if(location)Register(location.gameObject,location.name,Kind.POI);
            foreach(var f in app.Settings.Data.espFilters.Where(f=>f!=null && f.category==(int)kind))
                if(!filterCatalog.ContainsKey(FilterKey(kind,f.prefab)))filterCatalog[FilterKey(kind,f.prefab)]=new FilterOption {Id=f.prefab,Label=f.prefab,Group="",Category=kind};
            return filterCatalog.Values.Where(f=>f.Category==kind).OrderBy(f=>f.Label,StringComparer.OrdinalIgnoreCase).ToList();
        }
        internal static bool DepositObject(GameObject obj,int depth=0)=>ResolveDepositName(obj,depth)!=null;
        private static string ResolveDepositName(GameObject obj,int depth)
        {
            if(!obj || depth>3)return null;
            var a=obj.GetComponent<MineRock>();
            var b=obj.GetComponent<MineRock5>();
            string resource=a?DepositResource(obj.name,a.m_dropItems):null;
            if(resource==null && b)resource=DepositResource(obj.name,b.m_dropItems);
            if(resource==null)
            {
                var dropped=obj.GetComponent<DropOnDestroyed>();
                if(dropped)resource=DepositResource(obj.name,dropped.m_dropWhenDestroyed);
            }
            if(resource!=null)return resource;
            var d=obj.GetComponent<Destructible>();
            if(d)
            {
                resource=ResolveDepositName(d.m_spawnWhenDestroyed,depth+1)??ResolveDepositName(d.m_spawnWhenDamaged,depth+1);
                if(resource!=null)return resource;
            }
            return depth==0?NaturalCatalogRules.OreNodeLabel(obj.name):NaturalCatalogRules.OreItemLabel(PrefabId(obj));
        }
        private static string DepositName(GameObject obj)=>ResolveDepositName(obj,0)??PrefabId(obj);

    }
    internal sealed partial class MenuView
    {
        private int filterCategory,filterPage;
        private string filterQuery="";
        private readonly HashSet<string> expandedSpecies=new HashSet<string>();
        private void EspFiltersPage(RectTransform page)
        {
            var o=app.Overlay;var kind=(Overlay.Kind)filterCategory;
            string[] categories={"Players","Creatures","Pickables","Ore deposits","Ground drops","Locations (POI)","Tombstones","Containers"};
            SelectRow(page,L.T("ui.category"),()=>categories[filterCategory],()=>categories.Select((name,i)=>Choice(name,()=>{filterCategory=i;filterPage=0;rebuild=true;})));
            var search=InputBox(page,filterQuery,v=>filterQuery=v,"Search names or prefab IDs",80);Fixed((RectTransform)search.transform,36);
            ActionRow(page,L.T("ui.search_filters"),"Search",()=>{filterPage=0;rebuild=true;},L.T("ui.find_a_creature_or_resource"));
            Note(page,L.T("ui.choose_what_to_hide_applies"));
            var options=o.Catalog(kind);var visible=options.Where(f=>CatalogRules.Matches(f.Id,f.Label,filterQuery)).ToList();
            ActionRow(page,L.T("ui.restore_category"),L.T("ui.show_all"),()=>{o.Hide(kind,options.Select(f=>f.Id),false);rebuild=true;},L.T("ui.show_everything_in_this_category"));
            var grouped=visible.GroupBy(f=>string.IsNullOrEmpty(f.Group)?f.Id:f.Group).ToList();
            int pages=Math.Max(1,(grouped.Count+59)/60);filterPage=Mathf.Clamp(filterPage,0,pages-1);
            Note(page,visible.Count+" matching types. Page "+(filterPage+1)+" / "+pages);
            if(filterPage>0)ActionRow(page,L.T("ui.previous_page"),"Back",()=>{filterPage--;rebuild=true;},L.T("ui.previous_filter_groups"));
            if(filterPage+1<pages)ActionRow(page,L.T("ui.next_page"),L.T("ui.next"),()=>{filterPage++;rebuild=true;},L.T("ui.more_filter_groups"));
            foreach(var group in grouped.Skip(filterPage*60).Take(60))
            {
                var values=group.ToList();string key=filterCategory+":"+group.Key;
                if(values.Count>1) {
                    var allVariants=options.Where(f=>f.Group==group.Key).ToList();
                    ToggleRow(page,"Hide all "+group.Key,()=>allVariants.All(f=>o.Hidden(kind,f.Id)),v=>o.Hide(kind,allVariants.Select(f=>f.Id),v),L.T("ui.hide_the_whole_group"));
                    ActionRow(page,group.Key+" variants",expandedSpecies.Contains(key)?L.T("ui.collapse"):L.T("ui.expand"),()=>{if(!expandedSpecies.Add(key))expandedSpecies.Remove(key);rebuild=true;},L.T("ui.show_individual_variants"));
                    if(!expandedSpecies.Contains(key) && string.IsNullOrWhiteSpace(filterQuery))continue;
                }
                foreach(var option in values) { var f=option;ToggleRow(page,"Hide "+f.Label+" ("+f.Id+")",()=>o.Hidden(kind,f.Id),v=>o.Hide(kind,new[]{f.Id},v),L.T("ui.hide_this_type_including_starred")); }
            }
            if(visible.Count==0)Note(page,L.T("ui.no_matching_entries_enter_a"));
        }
    }
}
