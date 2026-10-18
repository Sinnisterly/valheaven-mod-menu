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
        /// <summary>1 = vanilla XP; 25 = one level per gain (legacy fast training).</summary>
        internal float SkillXpMult = 1f;
        private EnvMan environment;
        private bool originalDebugTime;
        private float originalTime;
        private bool weatherOwned;
        private string originalDebugEnv="";
        private float weatherUntil; // unscaledTime deadline; <=0 = until cleared
        private string pendingLocation;
        private ZNet pendingWorld;
        private float locationDeadline;
        private float durabilityAt;
        private bool dayApplied;
        /// <summary>EnvSetup.m_name for the weather picker (one-shot apply).</summary>
        internal string SelectedWeather="";
        /// <summary>0 = hold until Clear; else force duration in seconds.</summary>
        internal float WeatherDuration;
        internal ExpansionFeatures(Plugin app){this.app=app;}
        internal void Tick()
        {
            var env=EnvMan.instance;
            if(environment && (environment!=env || !app.Settings.Data.alwaysDay || !Player.m_localPlayer))
            {
                RestoreDay();
                dayApplied=false;
            }
            if(env && Player.m_localPlayer && app.Settings.Data.alwaysDay)
            {
                if(!environment){environment=env;originalDebugTime=env.m_debugTimeOfDay;originalTime=env.m_debugTime;dayApplied=false;}
                if(!dayApplied || !env.m_debugTimeOfDay || Mathf.Abs(env.m_debugTime-.5f)>.001f)
                {
                    env.m_debugTimeOfDay=true;env.m_debugTime=.5f;
                    dayApplied=true;
                }
            }
            if(weatherOwned)
            {
                if(!env){weatherOwned=false;weatherUntil=0;}
                else if(weatherUntil>0 && Time.unscaledTime>=weatherUntil) ClearWeather(notify:true);
                else if(!string.IsNullOrEmpty(SelectedWeather) && env.m_debugEnv!=SelectedWeather)
                    env.m_debugEnv=SelectedWeather;
            }
            if(Durability && Player.m_localPlayer && Time.unscaledTime>=durabilityAt)
            {
                durabilityAt=Time.unscaledTime+.5f;
                var items=Player.m_localPlayer.GetInventory().GetAllItems();
                for(int i=0;i<items.Count;i++)
                {
                    var item=items[i];
                    if(item!=null && item.m_shared.m_useDurability)item.m_durability=item.GetMaxDurability();
                }
            }
            if(pendingLocation!=null && (ZNet.instance!=pendingWorld || Time.unscaledTime>locationDeadline))
            {pendingLocation=null;app.Report(L.T("ui.no_location_response_try_another"));}
        }
        private void RestoreDay()
        {if(environment){environment.m_debugTimeOfDay=originalDebugTime;environment.m_debugTime=originalTime;}environment=null;dayApplied=false;}
        /// <summary>Stops Always daytime. Caller already cleared the saved preference.</summary>
        internal void ClearAlwaysDay()
        {
            RestoreDay();
        }
        internal void Reset()
        {
            ClearWeather(notify:false);
            RestoreDay();
            FastTraining=SuperJump=Durability=InstantHarvest=false;
            SkillXpMult=1f;
            pendingLocation=null;
            SelectedWeather="";
            WeatherDuration=0;
        }

        /// <summary>Sets <see cref="EnvMan.m_debugEnv"/> (vanilla <c>env</c>). Host-only; clients don't get the field.</summary>
        internal void ApplyWeather(string envName, float durationSeconds, bool instant)
        {
            app.RequireWorld();
            if (!EnvMan.instance) throw new InvalidOperationException("Environment manager is not ready.");
            if (string.IsNullOrWhiteSpace(envName)) throw new ArgumentException("Choose a weather / environment first.");
            if (!HostTools.IsHost())
                throw new InvalidOperationException("Weather override is host-authoritative (same as vanilla env). Host the world, or run singleplayer.");
            var match = FindEnv(envName);
            if (match == null)
                throw new ArgumentException("Unknown environment \"" + envName + "\". Pick one from the list.");

            // Always daytime stamps m_debugTimeOfDay; clear so weather lighting isn't stuck at noon.
            if (app.Settings.Data.alwaysDay)
            {
                app.Settings.Data.alwaysDay = false;
                ClearAlwaysDay();
            }

            var env = EnvMan.instance;
            if (!weatherOwned)
            {
                originalDebugEnv = env.m_debugEnv ?? "";
                weatherOwned = true;
            }
            env.m_debugEnv = match.m_name;
            SelectedWeather = match.m_name;
            durationSeconds = Mathf.Clamp(durationSeconds, 0, 3600);
            WeatherDuration = durationSeconds;
            weatherUntil = durationSeconds > 0 ? Time.unscaledTime + durationSeconds : 0;
            if (instant)
                env.ForceInstantEnvironmentSwitch();
            string dur = durationSeconds > 0 ? (" for " + durationSeconds.ToString("0") + "s") : " until cleared";
            app.Report("Weather set to " + match.m_name + dur + ". Host only; other players keep their own weather.");
        }

        internal void ClearWeather(bool notify)
        {
            weatherUntil = 0;
            if (weatherOwned && EnvMan.instance)
                EnvMan.instance.m_debugEnv = originalDebugEnv ?? "";
            weatherOwned = false;
            originalDebugEnv = "";
            if (notify) app.Report(L.T("ui.weather_override_cleared"));
        }

        internal IEnumerable<string> EnvironmentNames()
        {
            if (!EnvMan.instance || EnvMan.instance.m_environments == null)
                return Enumerable.Empty<string>();
            return EnvMan.instance.m_environments
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.m_name))
                .Select(e => e.m_name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase);
        }

        internal static readonly string[] WeatherPresets =
        {
            "Clear", "Rain", "LightRain", "ThunderStorm", "Snow", "SnowStorm", "Misty", "Twilight_Clear", "Twilight_Snow", "Twilight_SnowStorm"
        };

        private static EnvSetup FindEnv(string name)
        {
            if (!EnvMan.instance || EnvMan.instance.m_environments == null || string.IsNullOrWhiteSpace(name)) return null;
            foreach (var env in EnvMan.instance.m_environments)
            {
                if (env != null && string.Equals(env.m_name, name, StringComparison.OrdinalIgnoreCase))
                    return env;
            }
            return null;
        }
        /// <summary>Eat by ObjectDB name. Uses an inventory stack, or adds one clean copy then eats.</summary>
        internal void EatFoodByName(string prefabName)
        {
            var player = app.RequirePlayer();
            if (string.IsNullOrWhiteSpace(prefabName)) throw new ArgumentException("Choose a food first.");
            if (!ObjectDB.instance) throw new InvalidOperationException("ObjectDB is not ready.");
            var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
            if (!drop || drop.m_itemData?.m_shared == null || drop.m_itemData.m_shared.m_food <= 0f)
                throw new ArgumentException("That item is not eatable food.");
            string sharedName = drop.m_itemData.m_shared.m_name;
            var inv = player.GetInventory();
            ItemDrop.ItemData stack = null;
            foreach (var item in inv.GetAllItems())
            {
                if (item?.m_shared == null || item.m_shared.m_name != sharedName) continue;
                if (item.m_stack <= 0) continue;
                stack = item;
                break;
            }
            if (stack == null)
            {
                var item = drop.m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_stack = 1;
                item.m_quality = 1;
                item.m_variant = 0;
                item.m_durability = item.GetMaxDurability();
                item.m_crafterID = player.GetPlayerID();
                item.m_crafterName = player.GetPlayerName();
                item.m_worldLevel = (byte)Game.m_worldLevel;
                item.m_cheated = false;
                item.m_pickedUp = true;
                if (!inv.CanAddItem(item, 1) || !inv.AddItem(item))
                    throw new InvalidOperationException("Inventory is full.");
                stack = inv.GetAllItems().LastOrDefault(i => i != null && i.m_shared != null && i.m_shared.m_name == sharedName && i.m_stack > 0);
                if (stack == null) stack = item;
            }
            stack.m_cheated = false;
            if (!player.CanEat(stack, false) && !app.Phase.AlwaysCanEat)
                throw new InvalidOperationException("Cannot eat that right now. Enable Always can eat, or free a food slot.");
            if (!player.EatFood(stack))
                throw new InvalidOperationException("Eat failed. Try Always can eat / No food cooldown.");
            app.Report("Ate " + Localization.instance.Localize(sharedName) + ".");
        }
        internal void UnlockRecipes()
        {
            var p = app.RequirePlayer();
            if (!ObjectDB.instance) throw new InvalidOperationException("ObjectDB is not ready.");
            var known = Reflection.Get<HashSet<string>>(p, "m_knownRecipes");
            var materials = Reflection.Get<HashSet<string>>(p, "m_knownMaterial");
            var stations = Reflection.Get<Dictionary<string, int>>(p, "m_knownStations");
            int recipesBefore = known.Count, matsBefore = materials.Count, stationsBefore = stations.Count;

            void RememberStation(CraftingStation station)
            {
                if (!station || string.IsNullOrEmpty(station.m_name)) return;
                stations[station.m_name] = Math.Max(stations.TryGetValue(station.m_name, out int level) ? level : 0, 10);
            }

            void RememberPiece(Piece piece)
            {
                if (!piece || string.IsNullOrEmpty(piece.m_name)) return;
                if (piece.m_enabled) known.Add(piece.m_name);
                RememberStation(piece.m_craftingStation);
                if (piece.m_resources == null) return;
                foreach (var req in piece.m_resources)
                {
                    var shared = req?.m_resItem?.m_itemData?.m_shared;
                    if (shared != null && !string.IsNullOrEmpty(shared.m_name)) materials.Add(shared.m_name);
                }
            }

            foreach (var recipe in ObjectDB.instance.m_recipes)
            {
                if (!recipe || !recipe.m_item || recipe.m_item.m_itemData?.m_shared == null) continue;
                // Include disabled seasonal entries; UI still filters by season/DLC.
                known.Add(recipe.m_item.m_itemData.m_shared.m_name);
                RememberStation(recipe.m_craftingStation);
                RememberStation(recipe.m_repairStation);
                if (recipe.m_resources == null) continue;
                foreach (var req in recipe.m_resources)
                {
                    var shared = req?.m_resItem?.m_itemData?.m_shared;
                    if (shared != null && !string.IsNullOrEmpty(shared.m_name)) materials.Add(shared.m_name);
                }
            }

            foreach (var prefab in ObjectDB.instance.m_items)
            {
                if (!prefab) continue;
                var drop = prefab.GetComponent<ItemDrop>();
                if (!drop || drop.m_itemData?.m_shared == null) continue;
                materials.Add(drop.m_itemData.m_shared.m_name);
                var table = drop.m_itemData.m_shared.m_buildPieces;
                if (!table || table.m_pieces == null) continue;
                foreach (var piecePrefab in table.m_pieces)
                {
                    if (piecePrefab) RememberPiece(piecePrefab.GetComponent<Piece>());
                }
            }

            if (ZNetScene.instance)
            {
                foreach (var prefab in ZNetScene.instance.m_prefabs)
                {
                    if (!prefab) continue;
                    RememberPiece(prefab.GetComponent<Piece>());
                    RememberStation(prefab.GetComponent<CraftingStation>());
                }
            }

            Reflection.Method(typeof(Player), "UpdateAvailablePiecesList").Invoke(p, null);
            try
            {
                if (InventoryGui.instance)
                    Reflection.Method(typeof(InventoryGui), "UpdateCraftingPanel", typeof(bool)).Invoke(InventoryGui.instance, new object[] { true });
            }
            catch { }

            int added = (known.Count - recipesBefore) + (materials.Count - matsBefore) + (stations.Count - stationsBefore);
            app.Report("Discovered " + added + " entries (" + (known.Count - recipesBefore) + " recipes/pieces, "
                + (materials.Count - matsBefore) + " materials, " + (stations.Count - stationsBefore)
                + " stations). Re-open the craft UI or stand at a station to see them.");
        }
        internal void EquipPower(string name)
        {
            var p=app.RequirePlayer();
            if(!ObjectDB.instance.m_StatusEffects.Any(s=>s && s.name==name && name.StartsWith("GP_",StringComparison.Ordinal)))throw new ArgumentException("Choose a Guardian Power.");
            p.SetGuardianPower(name);p.m_guardianPowerCooldown=0;app.Report(L.T("ui.guardian_power_equipped"));
        }
        internal void TeleportToLocation(string name)
        {
            var p=app.RequirePlayer();
            if(!ZoneSystem.instance || string.IsNullOrWhiteSpace(name))throw new ArgumentException("Choose a world location.");
            if(ZoneSystem.instance.FindClosestLocation(name,p.transform.position,out var closest))
            {app.Features.Teleport(closest.m_position+Vector3.up*2);return;}
            pendingLocation="valheaven:"+Guid.NewGuid().ToString("N");pendingWorld=ZNet.instance;locationDeadline=Time.unscaledTime+12;
            Game.instance.DiscoverClosestLocation(name,p.transform.position,pendingLocation,(int)Minimap.PinType.Icon0,false,false);
            app.Report("Finding nearest "+name+".");
        }
        internal void DiscoverNamedLocations(params string[] names)
        {
            if (names == null || names.Length == 0) throw new ArgumentException("No location names provided.");
            var player = app.RequirePlayer();
            if (!ZoneSystem.instance || !Game.instance) throw new InvalidOperationException("Wait for the world to finish loading.");
            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in names)
            {
                string match = ResolveLocationName(new string[] { n });
                if (match == null) continue;
                string label = MenuView.LocationLabel(match);
                if (labels.Add(label)) Game.instance.DiscoverClosestLocation(match, player.transform.position, label, (int)Minimap.PinType.Icon3, false, false);
            }
        }
        internal void TeleportToNamedLocations(params string[] names)
        {
            if(names==null || names.Length==0)throw new ArgumentException("No location names provided.");
            var p=app.RequirePlayer();
            if(!ZoneSystem.instance)throw new InvalidOperationException("Wait for the world to finish loading.");
            string match = ResolveLocationName(names);
            if(match == null)throw new InvalidOperationException("None of those locations exist in this world's location list yet.");
            TeleportToLocation(match);
            app.Report("Teleporting toward "+match+".");
        }
        internal void DiscoverForgeOfPotential()
        {
            app.RequirePlayer();
            if (!ZoneSystem.instance || !Game.instance) throw new InvalidOperationException("Wait for the world to finish loading.");
            var fromList = ZoneSystem.instance.m_locations.Where(l => l != null && l.m_enable && !string.IsNullOrEmpty(l.m_prefabName)).Select(l => l.m_prefabName).Where(IsForgeLocationName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (fromList.Length == 0) throw new InvalidOperationException("The Forge of Potential is not in this world's location list yet.");
            DiscoverNamedLocations(fromList);
        }
        internal void TeleportToForgeOfPotential()
        {
            var p=app.RequirePlayer();
            if(!ZoneSystem.instance)throw new InvalidOperationException("Wait for the world to finish loading.");
            // Prefer location names that look like the refinement forge.
            var fromList = ZoneSystem.instance.m_locations
                .Where(l => l != null && l.m_enable && !string.IsNullOrEmpty(l.m_prefabName))
                .Select(l => l.m_prefabName)
                .Where(IsForgeLocationName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if(fromList.Length > 0)
            {
                TeleportToNamedLocations(fromList);
                return;
            }
            // Fallback: loaded upgrader stations in streamed areas.
            CraftingStation best = null;
            float bestDist = float.MaxValue;
            foreach(var station in UnityEngine.Object.FindObjectsByType<CraftingStation>(FindObjectsSortMode.None))
            {
                if(!station || !station.m_upgrader)continue;
                float d = Vector3.Distance(p.transform.position, station.transform.position);
                if(d >= bestDist)continue;
                bestDist = d; best = station;
            }
            if(best)
            {
                app.Features.Teleport(best.transform.position + Vector3.up * 2);
                app.Report(L.T("ui.teleported_to_a_loaded_forge"));
                return;
            }
            throw new InvalidOperationException("No Forge of Potential location is known yet. Explore Mountains or generate new terrain, then retry.");
        }
        private static bool IsForgeLocationName(string name)
        {
            if(string.IsNullOrEmpty(name))return false;
            return name.IndexOf("upgrad", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("potential", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("refinement", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static string ResolveLocationName(IEnumerable<string> candidates)
        {
            if(!ZoneSystem.instance)return null;
            var enabled = ZoneSystem.instance.m_locations
                .Where(l => l != null && l.m_enable && !string.IsNullOrEmpty(l.m_prefabName))
                .Select(l => l.m_prefabName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach(var want in candidates)
            {
                if(string.IsNullOrWhiteSpace(want))continue;
                var hit = enabled.FirstOrDefault(n => string.Equals(n, want, StringComparison.OrdinalIgnoreCase));
                if(hit != null)return hit;
            }
            foreach(var want in candidates)
            {
                if(string.IsNullOrWhiteSpace(want))continue;
                var hit = enabled.FirstOrDefault(n => n.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
                if(hit != null)return hit;
            }
            return null;
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
            var expansion = Plugin.Instance?.Expansion;
            if (expansion == null || !Player.m_localPlayer || Player.m_localPlayer.GetSkills()!=__instance || skillType==Skills.SkillType.None || factor<=0) return;
            float mult = Mathf.Clamp(expansion.SkillXpMult, 1f, 25f);
            // Honor legacy FastTraining if it was saved before the slider.
            if (expansion.FastTraining) mult = 25f;
            if (mult <= 1.001f) return;
            if (mult >= 24.5f)
            {
                __instance.GetSkillLevel(skillType);
                var skill=__instance.GetSkillList().FirstOrDefault(s=>s.m_info!=null && s.m_info.m_skill==skillType);
                if(skill!=null)factor=ExpansionFeatures.TrainingFactor(skill.m_level,skill.m_accumulator,skill.m_info.m_increseStep,Game.m_skillGainRate,factor);
                return;
            }
            factor *= mult;
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
