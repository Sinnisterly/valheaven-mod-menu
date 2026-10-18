using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimAdminMenu
{
    [Serializable]
    internal sealed class ItemEdits
    {
        public float weight=-1,armor=-1,block=-1,maxDurability=-1;
        public bool damageOverride;
        public HitData.DamageTypes damage;
        internal bool Valid()
        {
            foreach(float value in new[]{weight,armor,block,maxDurability})if(!Rules.Finite(value) || value< -1 || value>1000000)return false;
            foreach(float value in new[]{damage.m_damage,damage.m_blunt,damage.m_slash,damage.m_pierce,damage.m_fire,damage.m_frost,damage.m_lightning,damage.m_poison,damage.m_spirit,damage.m_chop,damage.m_pickaxe,damage.m_nonPlayer})
                if(!Rules.Finite(value) || value<0 || value>1000000)return false;
            return true;
        }
    }
    internal static class ItemEditor
    {
        internal const string Key="valheaven.item.v1";
        private sealed class Cache{internal string Json;internal ItemEdits Data;}
        private static readonly ConditionalWeakTable<ItemDrop.ItemData,Cache> cache=new ConditionalWeakTable<ItemDrop.ItemData,Cache>();
        internal static ItemEdits Read(ItemDrop.ItemData item)
        {
            if(item?.m_customData==null || !item.m_customData.TryGetValue(Key,out var json))return null;
            var value=cache.GetValue(item,_=>new Cache());
            if(value.Json!=json)
            {
                value.Json=json;value.Data=null;
                try{var data=JsonUtility.FromJson<ItemEdits>(json);if(data!=null && data.Valid())value.Data=data;}catch(ArgumentException){}
            }
            return value.Data;
        }
        internal static void Apply(ItemDrop.ItemData item,ItemEdits edits,float durability,int quality)
        {
            var p=Plugin.Instance.RequirePlayer();
            if(item==null || !p.GetInventory().GetAllItems().Contains(item))throw new ArgumentException("Select an item in your inventory.");
            if(edits==null || !edits.Valid() || !Rules.Finite(durability) || durability<0 || quality<1 || quality>Math.Max(1,item.m_shared.m_maxQuality))throw new ArgumentException("Check the entered item values.");
            item.m_customData[Key]=JsonUtility.ToJson(edits);cache.Remove(item);item.m_quality=quality;
            item.m_durability=Mathf.Clamp(durability,0,item.GetMaxDurability());item.m_cheated=true;
            Changed(p);Plugin.Instance.Report("Item changes saved.");
        }
        internal static void Reset(ItemDrop.ItemData item)
        {
            var p=Plugin.Instance.RequirePlayer();
            if(item==null || !p.GetInventory().GetAllItems().Contains(item))throw new ArgumentException("Select an item in your inventory.");
            item.m_customData.Remove(Key);cache.Remove(item);item.m_durability=Mathf.Min(item.m_durability,item.GetMaxDurability());Changed(p);Plugin.Instance.Report("Item stat overrides removed.");
        }
        private static void Changed(Player p)=>Reflection.Method(typeof(Inventory),"Changed",typeof(bool),typeof(bool)).Invoke(p.GetInventory(),new object[]{true,true});
    }
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.GetDamage),new[]{typeof(int),typeof(float)})]
    internal static class EditedDamagePatch
    {private static void Postfix(ItemDrop.ItemData __instance,ref HitData.DamageTypes __result){var e=ItemEditor.Read(__instance);if(e?.damageOverride==true)__result=e.damage;}}
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.GetArmor),new[]{typeof(int),typeof(float)})]
    internal static class EditedArmorPatch
    {private static void Postfix(ItemDrop.ItemData __instance,ref float __result){var e=ItemEditor.Read(__instance);if(e!=null && e.armor>=0)__result=e.armor;}}
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.GetBaseBlockPower),new[]{typeof(int)})]
    internal static class EditedBlockPatch
    {private static void Postfix(ItemDrop.ItemData __instance,ref float __result){var e=ItemEditor.Read(__instance);if(e!=null && e.block>=0)__result=e.block;}}
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.GetMaxDurability),new[]{typeof(int)})]
    internal static class EditedMaxDurabilityPatch
    {private static void Postfix(ItemDrop.ItemData __instance,ref float __result){var e=ItemEditor.Read(__instance);if(e!=null && e.maxDurability>=0)__result=e.maxDurability;}}
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.GetWeight))]
    internal static class EditedWeightPatch
    {private static void Postfix(ItemDrop.ItemData __instance,int stackOverride,ref float __result){var e=ItemEditor.Read(__instance);if(e!=null && e.weight>=0)__result=e.weight*(stackOverride<0?__instance.m_stack:stackOverride);}}
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.GetNonStackedWeight))]
    internal static class EditedUnitWeightPatch
    {private static void Postfix(ItemDrop.ItemData __instance,ref float __result){var e=ItemEditor.Read(__instance);if(e!=null && e.weight>=0)__result=e.weight;}}
}
