using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace ValheimAdminMenu
{
    internal static class CatalogRules
    {
        internal static int IconIndex(int count,int variant)=>count<=0?-1:Math.Max(0,Math.Min(count-1,variant));
        internal static bool Matches(string id,string name,string query)=>string.IsNullOrWhiteSpace(query)
            || (id??"").IndexOf(query.Trim(),StringComparison.OrdinalIgnoreCase)>=0 || (name??"").IndexOf(query.Trim(),StringComparison.OrdinalIgnoreCase)>=0;
    }
    internal sealed partial class MenuView
    {
        private sealed class Entry {internal string Id,Name;internal Sprite Icon;internal int Quality;}
        private readonly List<Entry> catalog=new List<Entry>();
        private readonly List<KeyValuePair<Entry,UnityEngine.UI.Image>> catalogRows=new List<KeyValuePair<Entry,UnityEngine.UI.Image>>();
        private string catalogQuery="",quantity="1",quality="1",level="1";
        private int catalogPage;
        private Entry selectedEntry;
        private RectTransform catalogList,details;
        private TextMeshProUGUI catalogCount;
        private UnityEngine.UI.Button previousPage,nextPage;
        private bool itemMode;
        private UnityEngine.UI.ScrollRect catalogScroll;
        private void BuildCatalog(bool items)
        {
            itemMode=items;catalogPage=0;selectedEntry=null;
            var search=InputBox(body,catalogQuery,v=>{catalogQuery=v;catalogPage=0;FilterCatalog();},items?"Search items or prefab IDs":"Search entities",100);
            var sr=(RectTransform)search.transform;sr.anchorMin=new Vector2(0,1);sr.anchorMax=new Vector2(.63f,1);sr.pivot=new Vector2(0,1);sr.sizeDelta=new Vector2(-14,38);
            var listHost=Node(body,"Catalog list");listHost.anchorMin=Vector2.zero;listHost.anchorMax=new Vector2(.63f,1);listHost.offsetMin=new Vector2(0,40);listHost.offsetMax=new Vector2(-14,-50);
            catalogScroll=Scroll(listHost,"Items",out catalogList);Stretch((RectTransform)catalogScroll.transform);
            var bottom=Node(body,"Page controls");bottom.anchorMin=Vector2.zero;bottom.anchorMax=new Vector2(.63f,0);bottom.pivot=new Vector2(0,0);bottom.sizeDelta=new Vector2(-14,30);
            catalogCount=Label(bottom,"",13,Muted);Stretch(catalogCount.rectTransform,0,0,160,0);
            previousPage=ActionButton(bottom,"‹",()=>{catalogPage--;FilterCatalog();},"Previous page.");At((RectTransform)previousPage.transform,331,0,32,30);
            nextPage=ActionButton(bottom,"›",()=>{catalogPage++;FilterCatalog();},"Next page.");At((RectTransform)nextPage.transform,369,0,32,30);
            var refresh=ActionButton(bottom,"Reload",()=>{LoadCatalog();FilterCatalog();},"Refresh the catalog.");At((RectTransform)refresh.transform,407,0,70,30);
            details=Box(body,"Selection",new Color(.075f,.074f,.065f),false);details.anchorMin=new Vector2(.65f,0);details.anchorMax=Vector2.one;details.offsetMin=Vector2.zero;details.offsetMax=Vector2.zero;
            LoadCatalog();FilterCatalog();ShowEntry(null);
        }
        private void LoadCatalog()
        {
            catalog.Clear();
            if(itemMode && ObjectDB.instance)
            {
                foreach(var prefab in ObjectDB.instance.m_items.Where(WorldTools.IsInventoryItem).GroupBy(p=>p.name).Select(g=>g.First()))
                {
                    var data=prefab.GetComponent<ItemDrop>().m_itemData;var shared=data.m_shared;
                    string name=Localization.instance!=null?Localization.instance.Localize(shared.m_name):shared.m_name;
                    int index=CatalogRules.IconIndex(shared.m_icons?.Length??0,data.m_variant);
                    catalog.Add(new Entry {Id=prefab.name,Name=name,Quality=Math.Max(1,shared.m_maxQuality),Icon=index<0?null:shared.m_icons[index]});
                }
            }
            else if(!itemMode && ZNetScene.instance)
                foreach(var prefab in ZNetScene.instance.m_prefabs.Where(p=>p && p.GetComponent<ZNetView>() && !p.GetComponent<Player>() && !WorldTools.IsInventoryItem(p)).GroupBy(p=>p.name).Select(g=>g.First()))
                    catalog.Add(new Entry {Id=prefab.name,Name=prefab.name,Quality=1});
            catalog.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.Name,b.Name));
        }
        private void FilterCatalog()
        {
            if(!catalogList)return;
            ClearHover();Clear(catalogList);catalogRows.Clear();
            var matches=catalog.Where(e=>CatalogRules.Matches(e.Id,e.Name,catalogQuery)).ToList();
            const int pageSize=80;int pages=Math.Max(1,(matches.Count+pageSize-1)/pageSize);catalogPage=Mathf.Clamp(catalogPage,0,pages-1);
            foreach(var entry in matches.Skip(catalogPage*pageSize).Take(pageSize))
            {
                var e=entry;var button=ActionButton(catalogList,"",()=>ShowEntry(e));var rect=(RectTransform)button.transform;Fixed(rect,59);
                var background=(UnityEngine.UI.Image)button.targetGraphic;background.color=e==selectedEntry?Selected:new Color(.115f,.112f,.098f);
                catalogRows.Add(new KeyValuePair<Entry,UnityEngine.UI.Image>(e,background));
                var icon=Box(rect,"Item icon",new Color(.06f,.06f,.055f),false);At(icon,7,8,43,43);
                if(e.Icon){var graphic=Box(icon,"Sprite",Color.white).GetComponent<UnityEngine.UI.Image>();Stretch((RectTransform)graphic.transform,3,3,3,3);graphic.sprite=e.Icon;graphic.preserveAspect=true;}
                else {var mark=Label(icon,itemMode?"?":"E",23,GoldDim);Stretch(mark.rectTransform);mark.alignment=TextAlignmentOptions.Center;}
                var name=Label(rect,e.Name,17,Ink);Stretch(name.rectTransform,62,5,10,27);
                var id=Label(rect,e.Id,12,Muted);Stretch(id.rectTransform,62,31,10,5);
            }
            if(matches.Count==0)
            {
                var text=Label(catalogList,catalog.Count==0?"Catalog is not ready. Try Refresh.":"No matches. Try another search.",16,Muted);Fixed(text.rectTransform,60);text.textWrappingMode=TextWrappingModes.Normal;
            }
            catalogCount.text=matches.Count+" results  ·  "+(catalogPage+1)+" / "+pages;
            previousPage.interactable=catalogPage>0;nextPage.interactable=catalogPage+1<pages;
            catalogScroll.verticalNormalizedPosition=1;needsLayout=true;
        }
        private void ShowEntry(Entry entry)
        {
            ClearHover();selectedEntry=entry;Clear(details);
            foreach(var row in catalogRows)if(row.Value)row.Value.color=row.Key==entry?Selected:new Color(.115f,.112f,.098f);
            if(entry==null)
            {
                var title=Label(details,itemMode?"Select an item":"Select an entity",23,Ink);At(title.rectTransform,20,29,230,38);
                var hint=Label(details,itemMode?"Choose an item from the list to set its quantity and quality.":"Choose an entity from the list to set its quantity and level.",16,Muted);
                Stretch(hint.rectTransform,20,82,20,200);hint.textWrappingMode=TextWrappingModes.Normal;hint.alignment=TextAlignmentOptions.TopLeft;return;
            }
            var icon=Box(details,"Preview",FieldColor);At(icon,20,20,76,76);
            if(entry.Icon){var image=Box(icon,"Sprite",Color.white).GetComponent<UnityEngine.UI.Image>();Stretch((RectTransform)image.transform,5,5,5,5);image.sprite=entry.Icon;image.preserveAspect=true;}
            else {var initial=Label(icon,itemMode?"?":"E",35,GoldDim);Stretch(initial.rectTransform);initial.alignment=TextAlignmentOptions.Center;}
            var name=LabelAt(details,entry.Name,20,112,233,56,23,Ink);name.textWrappingMode=TextWrappingModes.Normal;name.font=displayFont;
            LabelAt(details,entry.Id,20,174,233,28,13,Muted);
            LabelAt(details,"Quantity",20,220,230,24,15,Muted);
            var amount=InputBox(details,quantity,v=>quantity=v,"1",5);At((RectTransform)amount.transform,20,248,233,36);
            LabelAt(details,itemMode?"Quality (1-"+entry.Quality+")":"Creature level",20,301,233,24,15,Muted);
            var option=InputBox(details,itemMode?quality:level,v=>{if(itemMode)quality=v;else level=v;},"1",4);At((RectTransform)option.transform,20,329,233,36);
            Action action=()=>
            {
                int count=Integer(quantity,"Quantity",1,itemMode?9999:20);
                if(itemMode)app.World.Give(entry.Id,count,Integer(quality,"Quality",1,entry.Quality));
                else app.World.Spawn(entry.Id,count,Integer(level,"Creature level",1,10));
            };
            var button=ActionButton(details,itemMode?"Add to inventory":"Spawn entity",()=>
            {if(itemMode)Run(action);else Confirm("Spawn "+entry.Name,"Places the selected entity near your character. It may remain in the saved world.",action);},itemMode?"Adds the selected item to your inventory.":"Spawns the selected entity near your character.");
            At((RectTransform)button.transform,20,397,233,39);
        }
    }
}
