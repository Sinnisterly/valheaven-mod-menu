using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimAdminMenu
{
    [Serializable]
    public sealed class ItemEdits
    {
        public float weight=-1,armor=-1,block=-1,maxDurability=-1;
        public bool damageOverride;
        // Zero mask = legacy whole-profile override.
        public int damageMask;
        public bool extraStats;
        public float knockback=-1,backstab=-1;
        public HitData.DamageTypes damage;
        internal bool Valid()
        {
            if(extraStats && (!Rules.Finite(knockback) || !Rules.Finite(backstab) || knockback < -1 || backstab < -1 || knockback > 99999 || backstab > 99999)) return false;
            foreach(float value in new[]{weight,armor,block,maxDurability})if(!Rules.Finite(value) || value< -1 || value>1000000)return false;
            foreach(float value in new[]{damage.m_damage,damage.m_blunt,damage.m_slash,damage.m_pierce,damage.m_fire,damage.m_frost,damage.m_lightning,damage.m_poison,damage.m_spirit,damage.m_chop,damage.m_pickaxe,damage.m_nonPlayer})
                if(!Rules.Finite(value) || value<0 || value>1000000)return false;
            return true;
        }
    }
    internal static class ItemEditor
    {
        internal const string Key="valheaven.item.v1";
        internal const int MaxQuality = 99999;
        private sealed class Cache{internal string Json;internal ItemEdits Data;}
        private static readonly ConditionalWeakTable<ItemDrop.ItemData,Cache> cache=new ConditionalWeakTable<ItemDrop.ItemData,Cache>();
        internal static ItemEdits Read(ItemDrop.ItemData item)
        {
            if(item?.m_customData==null || !item.m_customData.TryGetValue(Key,out var json))return null;
            var value=cache.GetValue(item,_=>new Cache());
            if(value.Json!=json)
            {
                value.Json=json;value.Data=null;
                try{var data=JsonUtility.FromJson<ItemEdits>(json);if(data!=null && data.Valid())value.Data=data;}catch (ArgumentException caught) { AppLog.Swallow(caught); }
            }
            return value.Data;
        }
        internal static void Apply(ItemDrop.ItemData item,ItemEdits edits,float durability,int quality)
        {
            var p=Plugin.Instance.RequirePlayer();
            if(item==null || !p.GetInventory().GetAllItems().Contains(item))throw new ArgumentException("Select an item in your inventory.");
            if(edits==null || !edits.Valid() || !Rules.Finite(durability) || durability<0 || quality<1 || quality>MaxQuality)throw new ArgumentException("Check the entered item values.");
            item.m_customData[Key]=JsonUtility.ToJson(edits);cache.Remove(item);SpawnPresets.ApplyShared(item);item.m_quality=quality;
            item.m_durability=Mathf.Clamp(durability,0,item.GetMaxDurability());
            item.m_cheated=false;
            Changed(p);Plugin.Instance.Report(L.T("ui.item_changes_saved"));
        }
        internal static void Reset(ItemDrop.ItemData item)
        {
            var p=Plugin.Instance.RequirePlayer();
            if(item==null || !p.GetInventory().GetAllItems().Contains(item))throw new ArgumentException("Select an item in your inventory.");
            item.m_customData.Remove(Key);cache.Remove(item);SpawnPresets.ApplyShared(item);item.m_cheated=false;
            item.m_durability=Mathf.Min(item.m_durability,item.GetMaxDurability());Changed(p);Plugin.Instance.Report(L.T("ui.item_stat_overrides_removed"));
        }
        // Second bool is cheatedStateChanged; true falsely pops "$achievements_picked_up_cheated_item".
        private static void Changed(Player p)=>Reflection.Method(typeof(Inventory),"Changed",typeof(bool),typeof(bool)).Invoke(p.GetInventory(),new object[]{true,false});
    }
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.GetDamage),new[]{typeof(int),typeof(float)})]
    internal static class EditedDamagePatch
    {private static void Postfix(ItemDrop.ItemData __instance,ref HitData.DamageTypes __result){var e=ItemEditor.Read(__instance);if(e?.damageOverride==true)__result=SpawnPresets.MergeDamage(__result,e);}}
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
    /// <summary>Vanilla free-craft stays clean with bypass on, but Load/AddItem re-stamps m_cheated when GetTotalDamage()&gt;10000. Clear on every AddItem/Move/Load path, including chests.</summary>
    internal static class ItemCheatFlags
    {
        internal static void Clear(ItemDrop.ItemData item)
        {
            if (item != null) item.m_cheated = false;
        }
        internal static void ClearAll(Inventory inv)
        {
            if (inv == null) return;
            foreach (var item in inv.GetAllItems()) Clear(item);
        }
    }
    [HarmonyPatch(typeof(Inventory), "AddItem", new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) })]
    internal static class AddItemAmountCheatPatch
    {
        private static void Prefix(ItemDrop.ItemData item) => ItemCheatFlags.Clear(item);
        private static void Postfix(ItemDrop.ItemData item) => ItemCheatFlags.Clear(item);
    }
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData) })]
    internal static class AddItemCheatPatch
    {
        private static void Prefix(ItemDrop.ItemData item) => ItemCheatFlags.Clear(item);
        private static void Postfix(ItemDrop.ItemData item) => ItemCheatFlags.Clear(item);
    }
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData), typeof(Vector2i) })]
    internal static class AddItemAtCheatPatch
    {
        private static void Prefix(ItemDrop.ItemData item) => ItemCheatFlags.Clear(item);
        private static void Postfix(ItemDrop.ItemData item) => ItemCheatFlags.Clear(item);
    }
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(int), typeof(ItemDrop.ItemData), typeof(bool) })]
    internal static class AddItemHashDataCheatPatch
    {
        private static void Prefix(ItemDrop.ItemData itemData) => ItemCheatFlags.Clear(itemData);
        private static void Postfix(ItemDrop.ItemData itemData) => ItemCheatFlags.Clear(itemData);
    }
    // Craft/console create: force cheated arg false (same as free-craft + bypass).
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(long), typeof(string), typeof(bool), typeof(bool) })]
    internal static class AddItemNamedCheatPatch
    {
        private static void Prefix(ref bool cheated) => cheated = false;
        private static void Postfix(ItemDrop.ItemData __result) => ItemCheatFlags.Clear(__result);
    }
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(long), typeof(string), typeof(Vector2i), typeof(bool), typeof(bool), typeof(bool) })]
    internal static class AddItemNamedAtCheatPatch
    {
        private static void Prefix(ref bool cheated) => cheated = false;
        private static void Postfix(ItemDrop.ItemData __result) => ItemCheatFlags.Clear(__result);
    }
    // Load reconstruct stamps m_cheated when cheated || GetTotalDamage()>10000. Force arg false; Prefix/Postfix clear after.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(int), typeof(int), typeof(float), typeof(Vector2i), typeof(bool), typeof(int), typeof(int), typeof(long), typeof(string), typeof(System.Collections.Generic.Dictionary<string, string>), typeof(int), typeof(bool), typeof(bool), typeof(bool) })]
    internal static class AddItemLoadHashCheatPatch
    {
        private static void Prefix(ref bool cheated) => cheated = false;
    }
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(string), typeof(int), typeof(float), typeof(Vector2i), typeof(bool), typeof(int), typeof(int), typeof(long), typeof(string), typeof(System.Collections.Generic.Dictionary<string, string>), typeof(int), typeof(bool), typeof(bool), typeof(bool) })]
    internal static class AddItemLoadNameCheatPatch
    {
        private static void Prefix(ref bool cheated) => cheated = false;
    }
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), new[] { typeof(Inventory), typeof(ItemDrop.ItemData) })]
    internal static class MoveItemCheatPatch
    {
        private static void Prefix(ItemDrop.ItemData item) => ItemCheatFlags.Clear(item);
        private static void Postfix(ItemDrop.ItemData item) => ItemCheatFlags.Clear(item);
    }
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
    internal static class MoveItemAmountCheatPatch
    {
        private static void Prefix(ItemDrop.ItemData item) => ItemCheatFlags.Clear(item);
        private static void Postfix(ItemDrop.ItemData item) => ItemCheatFlags.Clear(item);
    }
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.Load), new[] { typeof(ZPackage), typeof(ItemDrop.ItemData), typeof(Version.Item) })]
    internal static class ItemDataLoadCheatPatch
    {
        private static void Postfix(ItemDrop.ItemData itemData) => ItemCheatFlags.Clear(itemData);
    }
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnCreateNew), new[] { typeof(ItemDrop), typeof(bool) })]
    internal static class ItemDropCreateCheatPatch
    {
        private static void Prefix(ref bool cheated) => cheated = false;
        private static void Postfix(ItemDrop item) { if (item != null) ItemCheatFlags.Clear(item.m_itemData); }
    }
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnCreateNew), new[] { typeof(GameObject), typeof(bool) })]
    internal static class ItemDropCreateGoCheatPatch
    {
        private static void Prefix(ref bool cheated) => cheated = false;
        private static void Postfix(GameObject go)
        {
            var drop = go ? go.GetComponent<ItemDrop>() : null;
            if (drop != null) ItemCheatFlags.Clear(drop.m_itemData);
        }
    }
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AnyCheatedItem))]
    internal static class AnyCheatedItemPatch
    {
        private static void Postfix(Inventory __instance, ref bool __result)
        {
            ItemCheatFlags.ClearAll(__instance);
            __result = false;
        }
    }
}
