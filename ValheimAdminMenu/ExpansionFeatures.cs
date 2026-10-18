using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimAdminMenu
{
    internal sealed class ExpansionFeatures
    {
        private readonly Plugin app;
        internal bool FastTraining,SuperJump,Durability,InstantHarvest;
        private EnvMan environment;
        private bool originalDebugTime;
        private float originalTime;
        private string pendingLocation;
        private ZNet pendingWorld;
        private float locationDeadline;
        internal ExpansionFeatures(Plugin app){this.app=app;}
        internal void Tick()
        {
            var env=EnvMan.instance;
            if(environment && (environment!=env || !app.Settings.Data.alwaysDay || !Player.m_localPlayer))RestoreDay();
            if(env && Player.m_localPlayer && app.Settings.Data.alwaysDay)
            {
                if(!environment){environment=env;originalDebugTime=env.m_debugTimeOfDay;originalTime=env.m_debugTime;}
                env.m_debugTimeOfDay=true;env.m_debugTime=.5f;
            }
            if(Durability && Player.m_localPlayer)
                foreach(var item in Player.m_localPlayer.GetInventory().GetAllItems())if(item.m_shared.m_useDurability)item.m_durability=item.GetMaxDurability();
            if(pendingLocation!=null && (ZNet.instance!=pendingWorld || Time.unscaledTime>locationDeadline))
            {pendingLocation=null;app.Report("No location response. Try another destination.");}
        }
        private void RestoreDay()
        {if(environment){environment.m_debugTimeOfDay=originalDebugTime;environment.m_debugTime=originalTime;}environment=null;}
        internal void Reset(){RestoreDay();FastTraining=SuperJump=Durability=InstantHarvest=false;pendingLocation=null;}
        internal void UnlockRecipes()
        {
            var p=app.RequirePlayer();var known=Reflection.Get<HashSet<string>>(p,"m_knownRecipes");
            int before=known.Count;
            foreach(var recipe in ObjectDB.instance.m_recipes)
                if(recipe && recipe.m_enabled && recipe.m_item && recipe.m_item.m_itemData?.m_shared!=null)known.Add(recipe.m_item.m_itemData.m_shared.m_name);
            var materials=Reflection.Get<HashSet<string>>(p,"m_knownMaterial");
            foreach(var item in ObjectDB.instance.m_items.Where(WorldTools.IsInventoryItem))materials.Add(item.GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
            foreach(var prefab in ObjectDB.instance.m_items.Where(WorldTools.IsInventoryItem))
            {
                var table=prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
                if(!table)continue;
                foreach(var piece in table.m_pieces){var data=piece?piece.GetComponent<Piece>():null;if(data && data.m_enabled)known.Add(data.m_name);}
            }
            Reflection.Method(typeof(Player),"UpdateAvailablePiecesList").Invoke(p,null);
            app.Report("Unlocked "+(known.Count-before)+" recipes and building pieces.");
        }
        internal void EquipPower(string name)
        {
            var p=app.RequirePlayer();
            if(!ObjectDB.instance.m_StatusEffects.Any(s=>s && s.name==name && name.StartsWith("GP_",StringComparison.Ordinal)))throw new ArgumentException("Choose a Guardian Power.");
            p.SetGuardianPower(name);p.m_guardianPowerCooldown=0;app.Report("Guardian Power equipped.");
        }
        internal void TeleportToLocation(string name)
        {
            var p=app.RequirePlayer();
            if(!ZoneSystem.instance || string.IsNullOrWhiteSpace(name))throw new ArgumentException("Choose a world location.");
            if(ZoneSystem.instance.FindClosestLocation(name,p.transform.position,out var closest))
            {app.Features.Teleport(closest.m_position+Vector3.up*2);return;}
            pendingLocation="valheaven:"+Guid.NewGuid().ToString("N");pendingWorld=ZNet.instance;locationDeadline=Time.unscaledTime+12;
            Game.instance.DiscoverClosestLocation(name,p.transform.position,pendingLocation,(int)Minimap.PinType.Icon0,false,false);
            app.Report("Finding nearest "+name+"…");
        }
        internal bool ReceiveLocation(string token,Vector3 position)
        {
            if(token!=pendingLocation || pendingLocation==null || ZNet.instance!=pendingWorld)return false;
            pendingLocation=null;
            app.Enqueue(()=>app.Features.Teleport(position+Vector3.up*2));return true;
        }
        internal static float TrainingFactor(float level,float progress,float step,float rate,float original)
        {
            if(level>=100 || original<=0 || step<=0 || rate<=0)return original;
            double needed=Math.Pow(Math.Floor(level+1),1.5)*.5+.5;
            return Math.Max(original,(float)Math.Min(100000000,(needed-progress)/(step*rate)+1));
        }
    }
    [HarmonyPatch(typeof(Skills),nameof(Skills.RaiseSkill))]
    internal static class FastTrainingPatch
    {
        private static void Prefix(Skills __instance,Skills.SkillType skillType,ref float factor)
        {
            if(Plugin.Instance?.Expansion.FastTraining!=true || !Player.m_localPlayer || Player.m_localPlayer.GetSkills()!=__instance || skillType==Skills.SkillType.None || factor<=0)return;
            __instance.GetSkillLevel(skillType);
            var skill=__instance.GetSkillList().FirstOrDefault(s=>s.m_info!=null && s.m_info.m_skill==skillType);
            if(skill!=null)factor=ExpansionFeatures.TrainingFactor(skill.m_level,skill.m_accumulator,skill.m_info.m_increseStep,Game.m_skillGainRate,factor);
        }
    }
    [HarmonyPatch(typeof(Character),nameof(Character.Jump))]
    internal static class SuperJumpPatch
    {
        private static void Prefix(Character __instance,out float? __state)
        {
            __state=null;
            if(Plugin.Instance?.Expansion.SuperJump==true && __instance==Player.m_localPlayer)
            {__state=__instance.m_jumpForce;__instance.m_jumpForce*=Plugin.Instance.Settings.Data.jumpMultiplier;}
        }
        private static Exception Finalizer(Character __instance,float? __state,Exception __exception)
        {if(__state.HasValue)__instance.m_jumpForce=__state.Value;return __exception;}
    }
    [HarmonyPatch(typeof(Humanoid),"DrainEquipedItemDurability")]
    internal static class DurabilityDrainPatch
    {
        private static bool Prefix(Humanoid __instance,ItemDrop.ItemData item)
        {
            if(Plugin.Instance?.Expansion.Durability!=true || __instance!=Player.m_localPlayer)return true;
            item.m_durability=item.GetMaxDurability();return false;
        }
    }
    [HarmonyPatch]
    internal static class AttackDurabilityPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {foreach(string name in new[]{"DoMeleeAttack","DoAreaAttack","DoNonAttack","FireProjectileBurst"})yield return Reflection.Method(typeof(Attack),name);}
        private static void Postfix(Attack __instance)
        {
            if(Plugin.Instance?.Expansion.Durability!=true || Reflection.Get<Humanoid>(__instance,"m_character")!=Player.m_localPlayer)return;
            var weapon=Reflection.Get<ItemDrop.ItemData>(__instance,"m_weapon");if(weapon!=null && weapon.m_shared.m_useDurability)weapon.m_durability=weapon.GetMaxDurability();
        }
    }
    [HarmonyPatch]
    internal static class InstantHarvestPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {foreach(var type in new[]{typeof(MineRock),typeof(MineRock5),typeof(TreeBase),typeof(TreeLog)})yield return Reflection.Method(type,"Damage",typeof(HitData));}
        private static void Prefix(ref HitData hit)
        {
            if(Plugin.Instance?.Expansion.InstantHarvest!=true || hit==null || !Player.m_localPlayer || hit.GetAttacker()!=Player.m_localPlayer)return;
            hit=hit.Clone();hit.m_toolTier=100;hit.m_damage.m_chop=1000000;hit.m_damage.m_pickaxe=1000000;
        }
    }
    [HarmonyPatch(typeof(Game),"RPC_DiscoverLocationResponse")]
    internal static class LocationResponsePatch
    {
        private static bool Prefix(string pinName,Vector3 pos)=>Plugin.Instance?.Expansion.ReceiveLocation(pinName,pos)!=true;
    }
}
