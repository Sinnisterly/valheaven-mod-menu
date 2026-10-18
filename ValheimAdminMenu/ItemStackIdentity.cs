using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
namespace ValheimAdminMenu
{
    internal static class ItemStackIdentity
    {
        [ThreadStatic] internal static ItemDrop.ItemData Incoming;
        internal static string Signature(ItemDrop.ItemData item)=>item?.m_customData!=null && item.m_customData.TryGetValue(ItemEditor.Key,out var value)?value:null;
        internal static bool Matches(ItemDrop.ItemData a,ItemDrop.ItemData b)=>Signature(a)==Signature(b);
    }
    [HarmonyPatch]
    internal static class ItemStackContextPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()=>typeof(Inventory).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Where(m=>(m.Name=="AddItem" || m.Name=="CanAddItem") && m.GetParameters().Length>0 && m.GetParameters()[0].ParameterType==typeof(ItemDrop.ItemData));
        private static void Prefix(ItemDrop.ItemData item,out ItemDrop.ItemData __state){__state=ItemStackIdentity.Incoming;ItemStackIdentity.Incoming=item;}
        private static Exception Finalizer(ItemDrop.ItemData __state,Exception __exception){ItemStackIdentity.Incoming=__state;return __exception;}
    }
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.IsSameType))]
    internal static class ItemSameTypePatch
    {private static void Postfix(ItemDrop.ItemData __instance,ItemDrop.ItemData other,ref bool __result){if(__result && !ItemStackIdentity.Matches(__instance,other))__result=false;}}
    [HarmonyPatch(typeof(Inventory),"FindFreeStackItem")]
    internal static class EditedStackSearchPatch
    {
        private static void Postfix(Inventory __instance,string name,int quality,float worldLevel,ref ItemDrop.ItemData __result)
        {
            if(__result==null || ItemStackIdentity.Matches(__result,ItemStackIdentity.Incoming))return;
            __result=__instance.GetAllItems().FirstOrDefault(i=>i.m_shared.m_name==name && i.m_quality==quality && i.m_worldLevel==worldLevel && i.m_stack<i.m_shared.m_maxStackSize && ItemStackIdentity.Matches(i,ItemStackIdentity.Incoming));
        }
    }
    [HarmonyPatch(typeof(Inventory),nameof(Inventory.FindFreeStackSpace))]
    internal static class EditedStackSpacePatch
    {
        private static void Postfix(Inventory __instance,string name,float worldLevel,ref int __result)
        {__result=__instance.GetAllItems().Where(i=>i.m_shared.m_name==name && i.m_worldLevel==worldLevel && i.m_stack<i.m_shared.m_maxStackSize && ItemStackIdentity.Matches(i,ItemStackIdentity.Incoming)).Sum(i=>i.m_shared.m_maxStackSize-i.m_stack);}
    }
}
