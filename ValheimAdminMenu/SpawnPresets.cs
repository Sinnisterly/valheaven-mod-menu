using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimAdminMenu
{
    [Serializable]
    public sealed class SpawnPreset
    {
        public string prefab;
        public bool enabled, noCrafter;
        public string crafter = "";
        public int quality = 1;
        public ItemEdits edits = new ItemEdits();
    }
    internal static class SpawnPresets
    {
        internal static readonly string[] DamageFields = { "m_slash", "m_blunt", "m_pierce", "m_fire", "m_frost", "m_lightning", "m_poison", "m_spirit", "m_chop", "m_pickaxe" };
        private sealed class SharedState { internal ItemDrop.ItemData.SharedData Original, Copy; }
        private static readonly ConditionalWeakTable<ItemDrop.ItemData, SharedState> shared = new ConditionalWeakTable<ItemDrop.ItemData, SharedState>();
        internal static SpawnPreset Get(string id)
        {
            var settings=Plugin.Instance.Settings.Data;
            var p=settings.spawnPresets.FirstOrDefault(x=>x!=null && x.prefab==id);
            if(p!=null) { if(p.edits==null || !p.edits.Valid()) p.edits=new ItemEdits(); return p; }
            p=new SpawnPreset { prefab=id };
            settings.spawnPresets=settings.spawnPresets.Concat(new[]{p}).ToArray();return p;
        }
        internal static HitData.DamageTypes MergeDamage(HitData.DamageTypes vanilla, ItemEdits e)
        {
            if(!e.damageOverride) return vanilla;
            if(e.damageMask==0) return e.damage;
            object result=vanilla, source=e.damage;
            for(int i=0;i<DamageFields.Length;i++) if((e.damageMask & (1<<i))!=0)
            { var field=typeof(HitData.DamageTypes).GetField(DamageFields[i]);field.SetValue(result,field.GetValue(source)); }
            return (HitData.DamageTypes)result;
        }
        internal static ItemEdits Snapshot(SpawnPreset p)
        {
            var e=p.edits;
            if(!p.enabled || (!e.damageOverride && e.weight<0 && e.armor<0 && e.block<0 && e.maxDurability<0 && (!e.extraStats || (e.knockback<0 && e.backstab<0))))return null;
            return JsonUtility.FromJson<ItemEdits>(JsonUtility.ToJson(e));
        }
        internal static void Configure(ItemDrop.ItemData item, int quality, string crafter, bool noCrafter, ItemEdits edits)
        {
            if(edits!=null && !edits.Valid()) throw new ArgumentException("Invalid item stat overrides.");
            item.m_quality=Mathf.Clamp(quality,1,ItemEditor.MaxQuality);
            item.m_crafterName=noCrafter?"":string.IsNullOrWhiteSpace(crafter)?Player.m_localPlayer.GetPlayerName():Rules.Text(crafter,30);
            item.m_crafterID=noCrafter?0:Player.m_localPlayer.GetPlayerID();
            item.m_customData=new Dictionary<string,string>(item.m_customData ?? new Dictionary<string,string>());
            if(edits!=null) item.m_customData[ItemEditor.Key]=JsonUtility.ToJson(edits);
            else item.m_customData.Remove(ItemEditor.Key);
            ApplyShared(item);
            item.m_cheated=false;item.m_pickedUp=true;item.m_worldLevel=Game.m_worldLevel;
            item.m_durability=item.GetMaxDurability();
        }
        // Attack force / backstab live on SharedData; clone so we never mutate the prefab.
        internal static void ApplyShared(ItemDrop.ItemData item)
        {
            if(item?.m_shared==null) return;
            if(shared.TryGetValue(item,out var prior) && ReferenceEquals(item.m_shared,prior.Copy)) item.m_shared=prior.Original;
            shared.Remove(item);
            var e=ItemEditor.Read(item);
            if(e==null || !e.extraStats || (e.knockback<0 && e.backstab<0)) return;
            var original=item.m_shared;
            var copy=(ItemDrop.ItemData.SharedData)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(original,null);
            if(e.knockback>=0) copy.m_attackForce=e.knockback;
            if(e.backstab>=0) copy.m_backstabBonus=e.backstab;
            item.m_shared=copy;shared.Add(item,new SharedState { Original=original,Copy=copy });
        }
        internal static void RestoreClone(ItemDrop.ItemData source,ItemDrop.ItemData clone)
        {
            if(shared.TryGetValue(source,out var state))clone.m_shared=state.Original;
            ApplyShared(clone);
        }
        internal static bool SameMetadata(ItemDrop.ItemData a,ItemDrop.ItemData b)
        {
            if(a==null || b==null || a.m_quality!=b.m_quality || a.m_variant!=b.m_variant || a.m_crafterID!=b.m_crafterID || a.m_crafterName!=b.m_crafterName) return false;
            var x=a.m_customData;var y=b.m_customData;
            return (x?.Count??0)==(y?.Count??0) && (x==null || x.All(k=>y!=null && y.TryGetValue(k.Key,out var v) && v==k.Value));
        }
        internal static int Add(Inventory inv,GameObject prefab,int amount,int quality,string crafter,bool noCrafter,ItemEdits edits)
        {
            int remaining=amount;
            while(remaining>0)
            {
                var item=prefab.GetComponent<ItemDrop>().m_itemData.Clone();item.m_dropPrefab=prefab;
                Configure(item,quality,crafter,noCrafter,edits);
                item.m_stack=Math.Min(remaining,Math.Max(1,item.m_shared.m_maxStackSize));
                int requested=item.m_stack;
                bool complete=inv.AddItem(item);
                int added=complete?requested:requested-item.m_stack;
                remaining-=added;if(!complete || added<=0)break;
            }
            return amount-remaining;
        }
        internal static void Drop(GameObject prefab,Vector3 position,int amount,int quality,string crafter,bool noCrafter,ItemEdits edits)
        {
            int max=Math.Max(1,prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
            for(int remaining=amount;remaining>0;remaining-=Math.Min(remaining,max))
            {
                var obj=UnityEngine.Object.Instantiate(prefab,position+Vector3.up*.15f,Quaternion.identity);
                var drop=obj.GetComponent<ItemDrop>();
                Configure(drop.m_itemData,quality,crafter,noCrafter,edits);
                drop.m_itemData.m_stack=Math.Min(remaining,max);
                Reflection.Method(typeof(ItemDrop),"Save").Invoke(drop,null);
            }
        }
    }
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.IsSameType))]
    internal static class PresetStackEqualityPatch
    { private static void Postfix(ItemDrop.ItemData __instance,ItemDrop.ItemData other,ref bool __result) { if(__result)__result=SpawnPresets.SameMetadata(__instance,other); } }
    // Vanilla FindFreeStack* matches by name and drops custom metadata.
    [HarmonyPatch]
    internal static class PresetStackContextPatch
    {
        [ThreadStatic] internal static ItemDrop.ItemData Incoming;
        private static IEnumerable<MethodBase> TargetMethods()=>typeof(Inventory).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)
            .Where(m=>(m.Name=="AddItem" || m.Name=="CanAddItem") && m.GetParameters().Any(p=>p.ParameterType==typeof(ItemDrop.ItemData)));
        private static void Prefix(object[] __args,out ItemDrop.ItemData __state) { __state=Incoming;Incoming=__args.OfType<ItemDrop.ItemData>().FirstOrDefault(); }
        private static Exception Finalizer(Exception __exception,ItemDrop.ItemData __state) { Incoming=__state;return __exception; }
    }
    [HarmonyPatch(typeof(Inventory),"FindFreeStackItem")]
    internal static class PresetFindStackPatch
    {
        private static void Postfix(Inventory __instance,ref ItemDrop.ItemData __result)
        {
            var incoming=PresetStackContextPatch.Incoming;if(incoming==null)return;
            __result=__instance.GetAllItems().FirstOrDefault(i=>i.IsSameType(incoming) && i.m_stack<i.m_shared.m_maxStackSize);
        }
    }
    [HarmonyPatch(typeof(Inventory),"FindFreeStackSpace")]
    internal static class PresetStackSpacePatch
    {
        private static void Postfix(Inventory __instance,ref int __result)
        {
            var incoming=PresetStackContextPatch.Incoming;if(incoming==null)return;
            __result=__instance.GetAllItems().Where(i=>i.IsSameType(incoming)).Sum(i=>Math.Max(0,i.m_shared.m_maxStackSize-i.m_stack));
        }
    }
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.Load),new[]{typeof(ZPackage),typeof(ItemDrop.ItemData),typeof(Version.Item)})]
    internal static class PresetLoadSharedPatch
    { private static void Postfix(ItemDrop.ItemData itemData)=>SpawnPresets.ApplyShared(itemData); }
    [HarmonyPatch(typeof(ItemDrop),"LoadFromZDO")]
    internal static class PresetNetworkSharedPatch
    { private static void Postfix(ItemDrop.ItemData itemData)=>SpawnPresets.ApplyShared(itemData); }
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.Clone))]
    internal static class PresetCloneSharedPatch
    { private static void Postfix(ItemDrop.ItemData __instance,ItemDrop.ItemData __result)=>SpawnPresets.RestoreClone(__instance,__result); }
    [HarmonyPatch(typeof(ItemDrop),"AutoStackItems")]
    internal static class PresetGroundStackPatch
    {
        private static bool Different(ItemDrop.ItemData a,ItemDrop.ItemData b)=>!a.IsSameType(b);
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.ToList();int patched=0;
            for(int i=0;i<code.Count;i++)
            {
                if(!(code[i].operand is MethodInfo method) || method.DeclaringType!=typeof(string) || method.Name!="op_Inequality")continue;
                // Ground AutoStack compares shared.name; swap for IsSameType-aware Different().
                if(i<8 || !(code[i-1].operand is FieldInfo rightName) || rightName.Name!="m_name" || !(code[i-5].operand is FieldInfo leftName) || leftName.Name!="m_name")continue;
                foreach(int offset in new[]{1,2,5,6}) {code[i-offset].opcode=OpCodes.Nop;code[i-offset].operand=null;}
                code[i].operand=AccessTools.Method(typeof(PresetGroundStackPatch),nameof(Different));patched++;
            }
            if(patched!=1)throw new InvalidOperationException("Ground item stack compatibility check changed; refusing unsafe stacking patch.");
            return code;
        }
    }
}
