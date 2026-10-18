using System;
using System.Collections.Generic;
using System.Globalization;
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
        private sealed class Entry {internal string Id,Name;internal Sprite Icon;internal int Quality;internal bool IsCharacter;}
        private readonly List<Entry> catalog=new List<Entry>();
        private readonly List<KeyValuePair<Entry,UnityEngine.UI.Image>> catalogRows=new List<KeyValuePair<Entry,UnityEngine.UI.Image>>();
        private string catalogQuery="",quantity="1",level="1";
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
            var detailScroll=Scroll(details,"Item details",out var detailContent);Stretch((RectTransform)detailScroll.transform,10,10,5,10);details=detailContent;
            LoadCatalog();FilterCatalog();ShowEntry(null);
        }
        private void LoadCatalog()
        {
            catalog.Clear();
                        if(itemMode)
            {
                var allPrefabs = new System.Collections.Generic.List<GameObject>();
                if (ObjectDB.instance) allPrefabs.AddRange(ObjectDB.instance.m_items);
                if (ZNetScene.instance) allPrefabs.AddRange(ZNetScene.instance.m_prefabs);
                
                foreach(var prefab in allPrefabs.Where(p => p != null && WorldTools.IsInventoryItem(p)).GroupBy(p=>p.name).Select(g=>g.First()))
                {
                    var data=prefab.GetComponent<ItemDrop>().m_itemData;var shared=data.m_shared;
                    string name=Localization.instance!=null?Localization.instance.Localize(shared.m_name):shared.m_name;
                    if(string.IsNullOrEmpty(name)||name.StartsWith("??",StringComparison.Ordinal)) name=prefab.name;
                    int index=CatalogRules.IconIndex(shared.m_icons?.Length??0,data.m_variant);
                    catalog.Add(new Entry {Id=prefab.name,Name=name,Quality=ItemEditor.MaxQuality,Icon=index<0?null:shared.m_icons[index],IsCharacter=false});
                }
            }
            else if(!itemMode && ZNetScene.instance)
                foreach(var prefab in ZNetScene.instance.m_prefabs.Where(p=>p && p.GetComponent<ZNetView>() && !p.GetComponent<Player>() && !WorldTools.IsInventoryItem(p)).GroupBy(p=>p.name).Select(g=>g.First()))
                    catalog.Add(new Entry {Id=prefab.name,Name=prefab.name,Quality=1,IsCharacter=prefab.GetComponent<Character>()!=null});
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
            if(entry==null){Note(details,itemMode?L.T("ui.select_an_item_to_configure"):L.T("ui.select_an_entity"));return;}
            Section(details,entry.Name);Note(details,entry.Id);
            var invalidFields=new HashSet<string>();
            void Field(string caption,string value,Action<string> changed,string placeholder="",int length=30) {
                var label=Label(details,caption,15,Muted);Fixed(label.rectTransform,38);
                var input=InputBox(details,value,_=>{},placeholder,length);input.onEndEdit.AddListener(v=>Run(()=>{try{changed(v);invalidFields.Remove(caption);}catch{invalidFields.Add(caption);throw;}}));Fixed((RectTransform)input.transform,32);
            }
            Field("Quantity",quantity,v=>quantity=v,"1",4);
            if(!itemMode) {
                if(entry.IsCharacter)Field("Creature level",level,v=>level=v,"1",3);
                Fixed((RectTransform)ActionButton(details,"Spawn entity",()=>Confirm("Spawn "+entry.Name,"Places the entity in the world.",()=>app.World.Spawn(entry.Id,Integer(quantity,"Quantity",1,20),Integer(level,"Level",1,999)))).transform,38);
                needsLayout=true;return;
            }
            var preset=SpawnPresets.Get(entry.Id);
            void Save()=>app.Settings.Save(false);
            Field(L.T("ui.quality_scales_stats_without_overrides"),preset.quality.ToString(),v=>{preset.quality=Integer(v,L.T("ui.quality"),1,ItemEditor.MaxQuality);Save();},"1",5);
            Note(details,L.T("ui.explicit_overrides_stay_fixed_vanilla"));
            ToggleRow(details,L.T("ui.no_crafter"),()=>preset.noCrafter,v=>{preset.noCrafter=v;Save();ShowEntry(entry);});
            if(!preset.noCrafter)Field(L.T("ui.crafter_name"),preset.crafter,v=>{preset.crafter=v;Save();},L.T("ui.your_character_name"));
            Section(details,"Spawn destination");
            foreach(var pair in new[]{new KeyValuePair<string,int>("Inventory",0),new KeyValuePair<string,int>("Chest",1),new KeyValuePair<string,int>("Ground",2),new KeyValuePair<string,int>("On kill",3)}) {
                int mode=pair.Value;var button=ActionButton(details,(app.Phase.ItemSpawnMode==mode?"Selected: ":"")+pair.Key,()=>{app.Phase.ItemSpawnMode=mode;ShowEntry(entry);});Fixed((RectTransform)button.transform,30);
            }
            if(app.Phase.ItemSpawnMode==3)ToggleRow(details,L.T("ui.every_kill"),()=>app.Phase.PersistentOnKillDrops,v=>app.Phase.PersistentOnKillDrops=v);
            Fixed((RectTransform)ActionButton(details,app.Phase.ItemSpawnMode==3?"Queue item drop":"Spawn item",()=>Run(()=>{
                if(invalidFields.Count>0)throw new ArgumentException("Correct these fields before spawning: "+string.Join(", ",invalidFields));
                int amount=Integer(quantity,"Quantity",1,9999);var edits=SpawnPresets.Snapshot(preset);
                if(app.Phase.ItemSpawnMode==3) {
                    app.Phase.OnKillDrops.Add(new PhaseFeatures.QueuedDrop { Prefab=entry.Id,Amount=amount,Quality=preset.quality,Crafter=preset.noCrafter?"":string.IsNullOrWhiteSpace(preset.crafter)?app.RequirePlayer().GetPlayerName():preset.crafter,NoCrafter=preset.noCrafter,Edits=edits });
                    app.Report("Queued "+amount+" "+entry.Name+".");ShowEntry(entry);
                } else Release250.Give(entry.Id,amount,preset.quality,app.Phase.ItemSpawnMode,preset.crafter,preset.noCrafter,edits);
            })).transform,40);
            if(app.Phase.OnKillDrops.Count>0)Fixed((RectTransform)ActionButton(details,"Clear queued drops",()=>{app.Phase.OnKillDrops.Clear();ShowEntry(entry);}).transform,32);
            ToggleRow(details,L.T("ui.edit_before_spawn"),()=>preset.enabled,v=>{preset.enabled=v;Save();ShowEntry(entry);});
            if(preset.enabled) {
                Note(details,L.T("ui.blank_vanilla_overrides_0_to"));
                var e=preset.edits;
                void Stat(string caption,float value,Action<float> set)=>Field(caption,value<0?"":value.ToString(CultureInfo.InvariantCulture),v=>{set(string.IsNullOrWhiteSpace(v)?-1:Number(v,caption,0,99999));Save();},L.T("ui.vanilla"),12);
                foreach(var key in new[]{"weight","armor","block","maxDurability","knockback","backstab"}) {
                    var field=typeof(ItemEdits).GetField(key);Stat(new Dictionary<string,string>{{"weight",L.T("ui.weight")},{"armor","Armor"},{"block","Block power"},{"maxDurability","Maximum durability"},{"knockback",L.T("ui.knockback")},{"backstab",L.T("ui.backstab_multiplier")}}[key],(float)field.GetValue(e),v=>{field.SetValue(e,v);e.extraStats=true;});
                }
                Section(details,L.T("ui.damage_types"));
                for(int i=0;i<SpawnPresets.DamageFields.Length;i++) {
                    int index=i;var field=typeof(HitData.DamageTypes).GetField(SpawnPresets.DamageFields[i]);
                    Stat(field.Name.Substring(2),(e.damageMask&(1<<i))!=0?(float)field.GetValue(e.damage):-1,v=>{
                        object damage=e.damage;field.SetValue(damage,Math.Max(0,v));e.damage=(HitData.DamageTypes)damage;
                        if(v<0)e.damageMask&=~(1<<index);else e.damageMask|=1<<index;e.damageOverride=e.damageMask!=0;
                    });
                }
            }
            Fixed((RectTransform)ActionButton(details,"Reset this item",()=>{app.Settings.Data.spawnPresets=app.Settings.Data.spawnPresets.Where(p=>p.prefab!=entry.Id).ToArray();Save();ShowEntry(entry);}).transform,32);
            Fixed((RectTransform)ActionButton(details,"Clear all presets",()=>Confirm("Clear all spawn presets","Resets every saved item spawn preset. Existing items are unchanged.",()=>{app.Settings.Data.spawnPresets=new SpawnPreset[0];Save();ShowEntry(entry);})).transform,32);
            needsLayout=true;
        }

        private void BuildVfxCatalog()
        {
            itemMode=false;catalogPage=0;selectedEntry=null;
            const float listFrac=.48f;
            var search=InputBox(body,catalogQuery,v=>{catalogQuery=v;catalogPage=0;FilterVfxCatalog();},"Search VFX / effect prefabs",100);
            var sr=(RectTransform)search.transform;sr.anchorMin=new Vector2(0,1);sr.anchorMax=new Vector2(listFrac,1);sr.pivot=new Vector2(0,1);sr.sizeDelta=new Vector2(-10,38);
            var listHost=Node(body,"Catalog list");listHost.anchorMin=Vector2.zero;listHost.anchorMax=new Vector2(listFrac,1);listHost.offsetMin=new Vector2(0,40);listHost.offsetMax=new Vector2(-10,-50);
            catalogScroll=Scroll(listHost,"VFX",out catalogList);Stretch((RectTransform)catalogScroll.transform);
            var bottom=Node(body,"Page controls");bottom.anchorMin=Vector2.zero;bottom.anchorMax=new Vector2(listFrac,0);bottom.pivot=new Vector2(0,0);bottom.sizeDelta=new Vector2(-10,30);
            catalogCount=Label(bottom,"",13,Muted);Stretch(catalogCount.rectTransform,0,0,120,0);
            previousPage=ActionButton(bottom,"‹",()=>{catalogPage--;FilterVfxCatalog();},"Previous page.");At((RectTransform)previousPage.transform,210,0,32,30);
            nextPage=ActionButton(bottom,"›",()=>{catalogPage++;FilterVfxCatalog();},"Next page.");At((RectTransform)nextPage.transform,244,0,32,30);
            var refresh=ActionButton(bottom,"Reload",()=>{LoadVfxCatalog();FilterVfxCatalog();},"Refresh the VFX list from the game.");At((RectTransform)refresh.transform,282,0,70,30);
            details=Box(body,"Selection",new Color(.075f,.074f,.065f),false);details.anchorMin=new Vector2(listFrac+.02f,0);details.anchorMax=Vector2.one;details.offsetMin=Vector2.zero;details.offsetMax=Vector2.zero;
            var vfxScroll=Scroll(details,"VFX details",out var vfxContent);Stretch((RectTransform)vfxScroll.transform,8,8,8,8);
            details=vfxContent;
            LoadVfxCatalog();FilterVfxCatalog();ShowVfxEntry(null);
        }
        private void LoadVfxCatalog()
        {
            catalog.Clear();
            foreach(var pair in Release250.EnumerateVfx())
                catalog.Add(new Entry{Id=pair.Key,Name=pair.Value,Quality=1});
        }
        private void FilterVfxCatalog()
        {
            if(!catalogList)return;
            ClearHover();Clear(catalogList);catalogRows.Clear();
            var matches=catalog.Where(e=>CatalogRules.Matches(e.Id,e.Name,catalogQuery)).ToList();
            const int pageSize=80;int pages=Math.Max(1,(matches.Count+pageSize-1)/pageSize);catalogPage=Mathf.Clamp(catalogPage,0,pages-1);
            foreach(var entry in matches.Skip(catalogPage*pageSize).Take(pageSize))
            {
                var e=entry;var button=ActionButton(catalogList,"",()=>ShowVfxEntry(e));var rect=(RectTransform)button.transform;Fixed(rect,48);
                var background=(UnityEngine.UI.Image)button.targetGraphic;background.color=e==selectedEntry?Selected:new Color(.115f,.112f,.098f);
                catalogRows.Add(new KeyValuePair<Entry,UnityEngine.UI.Image>(e,background));
                var mark=Label(rect,"FX",16,GoldDim);At(mark.rectTransform,10,10,36,28);mark.alignment=TextAlignmentOptions.Center;
                var name=Label(rect,e.Name,16,Ink);Stretch(name.rectTransform,52,5,10,10);
            }
            if(matches.Count==0)
            {
                var text=Label(catalogList,catalog.Count==0?"VFX catalog empty. Enter a world and Reload.":"No matches.",16,Muted);
                Fixed(text.rectTransform,60);text.textWrappingMode=TextWrappingModes.Normal;
            }
            catalogCount.text=matches.Count+" effects  ·  "+(catalogPage+1)+" / "+pages;
            previousPage.interactable=catalogPage>0;nextPage.interactable=catalogPage+1<pages;
            catalogScroll.verticalNormalizedPosition=1;needsLayout=true;
        }
        private void ShowVfxEntry(Entry entry)
        {
            ClearHover();selectedEntry=entry;Clear(details);
            foreach(var row in catalogRows)if(row.Value)row.Value.color=row.Key==entry?Selected:new Color(.115f,.112f,.098f);
            if(entry==null)
            {
                Note(details,"Search effects by name (particles, one-shots, and FX-style prefabs).");
                Note(details,"World spawns and attachments try to sync to other players when the effect supports it; otherwise they stay local.");
                Section(details,"Clear");
                ActionRow(details,"Last world VFX","Clear",VfxSuite.ClearLast,"Destroys the most recently spawned world effect.");
                ActionRow(details,"All VFX","Clear all",()=>VfxSuite.ClearAll(true),"Clears world spawns and all attachments.");
                return;
            }
            VfxSuite.SelectedId=entry.Id;
            Section(details,entry.Name);
            Note(details,"World placement is for free-standing spawns. Attach to puts the effect on your character. One-shots replay until duration ends.");
            SliderRow(details,"Duration (s)",()=>VfxSuite.Duration,v=>VfxSuite.Duration=v,0,300,"0 = until cleared. Otherwise replays until the timer ends.");
            SliderRow(details,"Scale",()=>VfxSuite.Scale,v=>VfxSuite.Scale=v,.25f,5f,"Visual scale of the effect (0.25-5x).");
            SliderRow(details,"Replay speed",()=>VfxSuite.ReplaySpeed,v=>VfxSuite.ReplaySpeed=v,.25f,4f,"How fast one-shots re-trigger and particles play.");
            ToggleRow(details,"Do effect (gameplay)",()=>VfxSuite.DoEffect,v=>VfxSuite.DoEffect=v,"OFF (default) = visual only (no damage or status areas). ON keeps gameplay effects.");
            SelectRow(details,"World placement",()=>VfxSuite.PlaceLabel(VfxSuite.PlaceMode),()=>new[]{
                Choice("On ground",()=>{VfxSuite.PlaceMode=VfxSuite.PlaceGround;ShowVfxEntry(selectedEntry);}),
                Choice("At crosshair",()=>{VfxSuite.PlaceMode=VfxSuite.PlaceCrosshair;ShowVfxEntry(selectedEntry);}),
                Choice("At player",()=>{VfxSuite.PlaceMode=VfxSuite.PlacePlayer;ShowVfxEntry(selectedEntry);})
            });
            SelectRow(details,"Attach to",()=>VfxSuite.AttachLabel(VfxSuite.AttachMode),()=>new[]{
                Choice("World spawn (no attach)",()=>{VfxSuite.AttachMode=VfxSuite.AttachNone;ShowVfxEntry(selectedEntry);}),
                Choice("Right hand",()=>{VfxSuite.AttachMode=VfxSuite.AttachRightHand;ShowVfxEntry(selectedEntry);}),
                Choice("Left hand",()=>{VfxSuite.AttachMode=VfxSuite.AttachLeftHand;ShowVfxEntry(selectedEntry);}),
                Choice("Both hands",()=>{VfxSuite.AttachMode=VfxSuite.AttachBothHands;ShowVfxEntry(selectedEntry);}),
                Choice("Feet",()=>{VfxSuite.AttachMode=VfxSuite.AttachFeet;ShowVfxEntry(selectedEntry);}),
                Choice("Chest",()=>{VfxSuite.AttachMode=VfxSuite.AttachChest;ShowVfxEntry(selectedEntry);}),
                Choice("Head",()=>{VfxSuite.AttachMode=VfxSuite.AttachHead;ShowVfxEntry(selectedEntry);}),
                Choice("Back",()=>{VfxSuite.AttachMode=VfxSuite.AttachBack;ShowVfxEntry(selectedEntry);}),
                Choice("Equipped weapon",()=>{VfxSuite.AttachMode=VfxSuite.AttachWeapon;ShowVfxEntry(selectedEntry);}),
                Choice("Feet while moving",()=>{VfxSuite.AttachMode=VfxSuite.AttachFeetMoving;ShowVfxEntry(selectedEntry);})
            });
            ActionRow(details,"Selected effect","Spawn / attach",()=>VfxSuite.SpawnOrAttachSelected(),
                VfxSuite.AttachMode==VfxSuite.AttachNone
                    ?"Spawns in the world at World placement."
                    :"Attaches to the chosen point. Synced effects follow for other players; others stay local.");
            Section(details,"Clear");
            ActionRow(details,"Last world VFX","Clear",VfxSuite.ClearLast,"Destroys the most recently spawned world effect.");
            ActionRow(details,"All VFX","Clear all",()=>VfxSuite.ClearAll(true),"Clears world spawns and all attachments.");
        }
        private static string VfxPlaceLabel(int mode)=>VfxSuite.PlaceLabel(mode);
    }
}
