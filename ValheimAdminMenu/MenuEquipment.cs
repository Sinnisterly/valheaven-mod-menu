using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
namespace ValheimAdminMenu
{
    internal sealed partial class MenuView
    {
        private ItemDrop.ItemData editedItem;
        private void EquipmentPage(RectTransform page)
        {
            if(!RequireCharacter(page))return;
            EquipmentContent(page,Player.m_localPlayer.GetInventory().GetAllItems());
        }
        private void EquipmentContent(RectTransform page,System.Collections.Generic.List<ItemDrop.ItemData> items)
        {
            Section(page,"Durability");
            ToggleRow(page,"Unlimited durability",()=>app.Expansion.Durability,v=>app.Expansion.Durability=v,"Keeps weapons, tools, and armor repaired.");
            Section(page,"Item editor");
            if(editedItem!=null && !items.Contains(editedItem))editedItem=null;
            Func<ItemDrop.ItemData,string> title=i=>Localization.instance.Localize(i.m_shared.m_name)+" ["+(i.m_gridPos.x+1)+", "+(i.m_gridPos.y+1)+"]";
            SelectRow(page,"Inventory item",()=>editedItem==null?"Choose item":title(editedItem),()=>items.Select(i=>Choice(title(i),()=>{editedItem=i;rebuild=true;})));
            ActionRow(page,"Inventory list","Refresh",()=>rebuild=true,"Refreshes the inventory and displayed item values.");
            if(editedItem==null)return;
            var item=editedItem;
            var prior=ItemEditor.Read(item);
            var edits=prior==null?new ItemEdits():JsonUtility.FromJson<ItemEdits>(JsonUtility.ToJson(prior));
            string quality=item.m_quality.ToString(),durability=item.m_durability.ToString("0.##",CultureInfo.InvariantCulture);
            Section(page,"Condition");
            Field(page,"Quality (1 to "+Math.Max(1,item.m_shared.m_maxQuality)+")",quality,v=>quality=v,"Upgrade quality for this item.",8);
            Field(page,"Current durability",durability,v=>durability=v,"Remaining durability, clamped to the maximum when applied.",16);
            Section(page,"Stat overrides");
            var values=new System.Collections.Generic.Dictionary<string,string>();
            Action<string,float> stat=(name,value)=>{values[name]=value.ToString("0.##",CultureInfo.InvariantCulture);Field(page,name,values[name],v=>values[name]=v,"Enter -1 to use the item's original stat.",16);};
            stat("Weight per item",edits.weight);stat("Armor",edits.armor);stat("Block power",edits.block);stat("Maximum durability",edits.maxDurability);
            Section(page,"Damage");
            ToggleRow(page,"Override damage",()=>edits.damageOverride,v=>edits.damageOverride=v,"Uses the entered base damage values for this item.");
            var damageFields=typeof(HitData.DamageTypes).GetFields().Where(f=>f.FieldType==typeof(float) && f.Name.StartsWith("m_")).ToArray();
            var damageValues=new System.Collections.Generic.Dictionary<string,string>();
            object source=edits.damageOverride?edits.damage:item.GetDamage();
            foreach(var field in damageFields){string key=field.Name;damageValues[key]=((float)field.GetValue(source)).ToString("0.##",CultureInfo.InvariantCulture);Field(page,char.ToUpperInvariant(key[2])+key.Substring(3),damageValues[key],v=>damageValues[key]=v,"Base damage amount before attack and resistance modifiers.",16);}
            Section(page,"Apply changes");
            ActionRow(page,"Selected item","Save item",()=>{
                edits.weight=Number(values["Weight per item"],"Weight",-1,1000000);edits.armor=Number(values["Armor"],"Armor",-1,1000000);
                edits.block=Number(values["Block power"],"Block",-1,1000000);edits.maxDurability=Number(values["Maximum durability"],"Durability",-1,1000000);
                object boxed=new HitData.DamageTypes();foreach(var field in damageFields)field.SetValue(boxed,Number(damageValues[field.Name],field.Name,0,1000000));edits.damage=(HitData.DamageTypes)boxed;
                float q=Number(quality,"Quality",1,Math.Max(1,item.m_shared.m_maxQuality));if(q!=(int)q)throw new ArgumentException("Quality must be a whole number.");
                ItemEditor.Apply(item,edits,Number(durability,"Durability",0,1000000),(int)q);rebuild=true;
            },"Saves the changes on this item or stack.");
            DangerRow(page,"Original stats","Restore","Removes this item's stat overrides. Quality is kept.",()=>{ItemEditor.Reset(item);rebuild=true;});
        }
    }
}
