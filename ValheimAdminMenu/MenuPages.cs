using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;

namespace ValheimAdminMenu
{
    internal sealed partial class MenuView
    {
        private Skills.SkillType selectedSkill=Skills.SkillType.Swords;
        private string skillLevel="25", x="0",y="100",z="0",message="",alias="";
        private string powerName="", locationName="", foodName="";
        private string statusSearch="", statusName="", bookmarkName="", selectedBookmark="";
        private string blueprintName="", selectedBlueprint="", blueprintImport="";
        private bool weatherInstant=true;
        private Vector3? marker;
        private ZDOID playerDestination;
        private string playerDestinationName="Choose player";
        private string markerName="Choose marker";
        private static KeyValuePair<string,Action> Choice(string label,Action action)=>new KeyValuePair<string,Action>(label,action);
        private IEnumerable<KeyValuePair<string, Action>> LanguageChoices(SettingsData d)
        {
            foreach (string code in LocaleCodes.Preferences)
            {
                string c = code;
                yield return Choice(LocaleCodes.DisplayName(c), () =>
                {
                    d.language = c;
                    MenuLanguage.ApplyPreference(c);
                    rebuild = true;
                    app.Report(L.Tf("toast.menu_language", MenuLanguage.SettingsLabel(c)));
                });
            }
        }
        private static string PowerLabel(string name)
        {
            var effect=ObjectDB.instance.m_StatusEffects.FirstOrDefault(e=>e && e.name==name);
            if(!effect)return name;
            string loc=Localization.instance!=null?Localization.instance.Localize(effect.m_name):effect.m_name;
            if(string.IsNullOrEmpty(loc)||loc.StartsWith("??",StringComparison.Ordinal)||loc.StartsWith("$",StringComparison.Ordinal)) return name;
            return loc;
        }
        private static string StatusChoiceLabel(StatusEffect se)=>FeatureSuite.StatusLabel(se);
        private IEnumerable<KeyValuePair<string, Action>> StatusChoices()
        {
            if (!ObjectDB.instance || ObjectDB.instance.m_StatusEffects == null)
                return Enumerable.Empty<KeyValuePair<string, Action>>();
            string q = (statusSearch ?? "").Trim();
            return ObjectDB.instance.m_StatusEffects
                .Where(se => se && !string.IsNullOrEmpty(se.name))
                .Where(se => string.IsNullOrEmpty(q) || CatalogRules.Matches(se.name, StatusChoiceLabel(se), q))
                .OrderBy(StatusChoiceLabel, StringComparer.CurrentCultureIgnoreCase)
                .Select(se => { string n = se.name; return Choice(StatusChoiceLabel(se), () => statusName = n); });
        }
        private IEnumerable<KeyValuePair<string, Action>> BookmarkChoices()
        {
            return TeleportBookmarks.List()
                .OrderBy(e => e.name, StringComparer.CurrentCultureIgnoreCase)
                .Select(e => { string n = e.name; return Choice(n, () => selectedBookmark = n); });
        }
        private IEnumerable<KeyValuePair<string, Action>> BlueprintChoices()
        {
            return BlueprintStore.List()
                .Select(e =>
                {
                    string n = e.name;
                    string label = n + " (" + e.pieceCount + ")";
                    return Choice(label, () => selectedBlueprint = n);
                });
        }
        private IEnumerable<KeyValuePair<string, Action>> BlueprintImportChoices()
        {
            return BlueprintStore.ListExportFiles()
                .Select(f => Choice(f, () => blueprintImport = f));
        }
        private static string FoodLabel(string prefab)
        {
            if (string.IsNullOrEmpty(prefab) || !ObjectDB.instance) return prefab ?? "";
            var go = ObjectDB.instance.GetItemPrefab(prefab);
            var drop = go ? go.GetComponent<ItemDrop>() : null;
            var shared = drop?.m_itemData?.m_shared;
            if (shared == null || string.IsNullOrEmpty(shared.m_name)) return prefab;
            if (Localization.instance == null) return prefab;
            string loc = Localization.instance.Localize(shared.m_name);
            // Failed loc keys come back as "??key" from Valheim Localization.
            if (string.IsNullOrEmpty(loc) || loc.StartsWith("??", StringComparison.Ordinal)) return prefab;
            return loc;
        }
        private IEnumerable<KeyValuePair<string, Action>> FoodChoices()
        {
            if (!ObjectDB.instance) return Enumerable.Empty<KeyValuePair<string, Action>>();
            return ObjectDB.instance.m_items
                .Where(go =>
                {
                    if (!go) return false;
                    var drop = go.GetComponent<ItemDrop>();
                    var shared = drop?.m_itemData?.m_shared;
                    return shared != null && shared.m_food > 0f && !string.IsNullOrEmpty(go.name);
                })
                .Select(go => go.name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(FoodLabel, StringComparer.CurrentCultureIgnoreCase)
                .Select(name => Choice(FoodLabel(name), () => foodName = name));
        }

        private void CharacterPage(RectTransform page,int sub)
        {
            if(!RequireCharacter(page))return;
            var f=app.Features;var p=app.Phase;var e=app.Expansion;var d=app.Settings.Data;
            switch(sub)
            {
                case 0:
                    Section(page,"Damage & defense");
                    ToggleRow(page,"God mode",()=>f.WantGod || (Player.m_localPlayer && Player.m_localPlayer.InGodMode()),v=>Run(()=>f.God(v)),"Blocks all damage and keeps health full while enabled. Re-applies if another mod clears the vanilla flag.");
                    ToggleRow(page,"One-hit creatures",()=>p.OneHitKill||p.OneHitCreatures,v=>{p.OneHitCreatures=v;p.OneHitKill=v;},"Your attacks defeat creatures in one hit. Separate from structures.");
                    ToggleRow(page,"One-hit structures",()=>p.OneHitStructures,v=>p.OneHitStructures=v,"Your hits destroy buildings and other structures in one hit. Separate from creatures.");
                    SliderRow(page,"Outgoing damage",()=>p.OutgoingDamageMult,v=>p.OutgoingDamageMult=v,0,20,"Multiplies damage you deal. 1 is vanilla. Stacks with One-hit kill (one-hit still wins).");
                    SliderRow(page,"Incoming damage",()=>p.IncomingDamageMult,v=>p.IncomingDamageMult=v,0,5,"Multiplies damage you take. 0 blocks damage types that still reach ApplyDamage; God mode still fully blocks. Local player only.");
                    SliderRow(page,"Hit / push force",()=>p.HitPushMult,v=>p.HitPushMult=v,0,10,"Multiplies knockback / push force on your hits. 1 is vanilla.");
                    ToggleRow(page,"Stagger protection",()=>p.StaggerProtection,v=>p.StaggerProtection=v,"Blocks stagger buildup and stagger state on your character.");
                    Section(page,"Stamina & tools");
                    ToggleRow(page,"Unlimited stamina",()=>f.Stamina,v=>f.Stamina=v,"Keeps stamina full and blocks stamina drain.");
                    ToggleRow(page,"Unlimited eitr",()=>f.Eitr,v=>f.Eitr=v,"Keeps eitr full and blocks eitr drain.");
                    ToggleRow(page,"Instant mining & chopping",()=>e.InstantHarvest,v=>e.InstantHarvest=v,"Breaks ore, trees, and logs in one hit. Ignores hard-tree gates.");
                    ToggleRow(page,"No tool use delay",()=>p.NoToolDelay,v=>p.NoToolDelay=v,"Removes swing delay between tool and weapon uses.");
                    ToggleRow(page,"No special-attack cooldown",()=>p.NoSpecialCooldown,v=>p.NoSpecialCooldown=v,"Secondary / special attacks are ready immediately.");
                    Section(page,"Ranged & aim");
                    ToggleRow(page,"Draw bow immediately",()=>p.DrawBowImmediate,v=>p.DrawBowImmediate=v,"Bows reach full draw as soon as you start drawing. Release to fire.");
                    ToggleRow(page,"Preserve ammunition",()=>p.InfArrows,v=>p.InfArrows=v,"Attacks do not consume ammo or fishing bait (UseAmmo). You still need at least one matching item equipped or in inventory. Covers bows, crossbows, and other ammo types.");
                    SliderRow(page,"Projectile speed",()=>p.ProjectileSpeedMult,v=>p.ProjectileSpeedMult=v,1,10,"Multiplies your local projectile velocity (arrows, bolts, spears, magic). 1 is vanilla. Applied after aim assist / magic bullet so direction stays correct.");
                    ToggleRow(page,"Multishot",()=>p.MultiArrow,v=>p.MultiArrow=v,"Fires multiple projectiles per shot. Hits raise skill; extra shots drain durability unless Unlimited durability is on.");
                    SliderRow(page,"Arrow count",()=>p.ArrowCount,v=>p.ArrowCount=v,2,12,"Projectiles spawned per ranged attack when Multishot is on.",true);
                    SliderRow(page,"Arrow spread",()=>p.ArrowSpread,v=>p.ArrowSpread=v,0,45,"Extra aim cone in degrees added to each multishot volley.");
                    ToggleRow(page,"Pierce shots",()=>p.PierceShots,v=>p.PierceShots=v,"Your projectiles keep going through creatures and only stop on terrain. Local shots only.");
                    ToggleRow(page,"Bow aim assist",()=>p.BowAimAssist,v=>p.BowAimAssist=v,"While drawing or firing a bow/crossbow/spear, steers aim toward a target in your cone. While on, prediction and local projectiles use zero gravity so aim matches the shot. Local player only.");
                    ToggleRow(page,"Show predicted aim spot",()=>p.ShowAimPredict,v=>p.ShowAimPredict=v,"When Bow aim assist is on, draws a lightweight marker at the predicted lead / impact point.");
                    ToggleRow(page,"No arrow gravity",()=>p.NoArrowGravity,v=>p.NoArrowGravity=v,"Sets gravity to 0 on your local bow, crossbow, and spear projectiles. Also used without aim assist. Prefer this if you want flat arrows while assist is off.");
                    ToggleRow(page,"Magic bullet",()=>p.MagicBullet,v=>p.MagicBullet=v,"On fire, redirects your projectile velocity toward the predicted target. Stronger than aim assist; still local-only and separate from Pierce shots.");
                    SelectRow(page,"Aim target",()=>AimTargetLabel(p.BowAimTargetMode),()=>new[]{
                        Choice("Closest to crosshair",()=>p.BowAimTargetMode=0),
                        Choice("Highest health",()=>p.BowAimTargetMode=1),
                        Choice("Closest to player",()=>p.BowAimTargetMode=2)
                    });
                    SliderRow(page,"Aim assist strength",()=>p.BowAimStrength,v=>p.BowAimStrength=v,10,100,"How hard Bow aim assist blends toward the predicted aim. Magic bullet always fully redirects.");
                    SliderRow(page,"Aim assist cone",()=>p.BowAimAngle,v=>p.BowAimAngle=v,5,75,"Max degrees from crosshair to pick a target. Magic bullet uses a slightly wider cone.");
                    SliderRow(page,"Aim assist range",()=>p.BowAimRange,v=>p.BowAimRange=v,10,100,"Max target distance for aim assist and magic bullet.");
                    ToggleRow(page,"Aim at players",()=>p.BowAimTargetPlayers,v=>p.BowAimTargetPlayers=v,"Allow bow aim assist / magic bullet to lock onto other players. Off by default for PvE.");
                    Section(page,"Melee & block");
                    ToggleRow(page,"Melee multi-hit",()=>p.MeleeMultiHit,v=>p.MeleeMultiHit=v,"Repeats the melee swing resolution. Each repeat raises skill and drains durability unless Unlimited durability is on.");
                    SliderRow(page,"Melee hits per swing",()=>p.MeleeHits,v=>p.MeleeHits=v,2,10,"How many times one melee swing resolves when Melee multi-hit is on.",true);
                    ToggleRow(page,L.T("combat.melee_lock"),()=>p.MeleeTargetLock,v=>p.MeleeTargetLock=v,L.T("combat.melee_lock.help"));
                    ToggleRow(page,L.T("combat.melee_lock_players"),()=>p.MeleeLockPlayers,v=>p.MeleeLockPlayers=v,L.T("combat.melee_lock_players.help"));
                    ToggleRow(page,"Melee cleave",()=>p.MeleeCleave,v=>p.MeleeCleave=v,"Melee swings use the game multi-hit path so one swing can hit several targets.");
                    SliderRow(page,"Cleave angle bonus",()=>p.CleaveAngleBonus,v=>p.CleaveAngleBonus=v,0,90,"Extra degrees added to the melee hit cone when Melee cleave is on.");
                    ToggleRow(page,"Auto-parry while blocking",()=>p.AutoParry,v=>p.AutoParry=v,"While you hold block, timing stays in the perfect-block window.");
                    SliderRow(page,"Perfect-block window",()=>p.PerfectBlockWindow,v=>p.PerfectBlockWindow=v,1,8,"Multiplies the perfect-block timing window. 1 is vanilla.");
                    Section(page,"Fishing");
                    ToggleRow(page,"Unlimited bait",()=>p.UnlimitedBait,v=>p.UnlimitedBait=v,"Casting does not consume bait. You still need at least one bait. Infinite arrows also covers bait.");
                    ToggleRow(page,"Attract fish",()=>p.AttractFish,v=>p.AttractFish=v,"Nearby fish take interest in your float much more reliably.");
                    ToggleRow(page,"Always bite",()=>p.AlwaysBite,v=>p.AlwaysBite=v,"Any interested fish nibbles successfully, even with the wrong bait.");
                    ToggleRow(page,"Instant hook",()=>p.InstantHook,v=>p.InstantHook=v,"A successful nibble hooks the fish immediately.");
                    ToggleRow(page,"Instant reel",()=>p.InstantReel,v=>p.InstantReel=v,"Hooked fish are reeled in and caught automatically.");
                    ToggleRow(page,"No fish escape",()=>p.NoFishEscape,v=>p.NoFishEscape=v,"Hooked fish do not thrash-escape, and their stamina drain is removed.");
                    ToggleRow(page,"Unbreakable line",()=>p.UnbreakableLine,v=>p.UnbreakableLine=v,"Your fishing line will not snap from distance while you own the float.");
                    Section(page,"Skills");
                    SliderRow(page,"Skill XP multiplier",()=>e.SkillXpMult,v=>{e.SkillXpMult=v;e.FastTraining=v>=24.5f;},1,25,"1 is vanilla. Higher multiplies skill XP. At 25, each XP gain grants a full level (old Fast training).");
                    SelectRow(page,"Skill",()=>selectedSkill.ToString(),()=>Player.m_localPlayer.GetSkills().m_skills.OrderBy(s=>s.m_skill.ToString()).Where(s=>s.m_skill!=Skills.SkillType.None && s.m_skill!=Skills.SkillType.All)
                        .Select(s=>{var skill=s.m_skill;return Choice(skill.ToString(),()=>{selectedSkill=skill;});}));
                    var current=Label(page,"",15,Muted);Fixed(current.rectTransform,26);
                    Action refresh=()=>
                    {
                        if(!current || !Player.m_localPlayer)return;
                        var entry=Player.m_localPlayer.GetSkills().GetSkillList().FirstOrDefault(s=>s.m_info!=null && s.m_info.m_skill==selectedSkill);
                        current.text=L.Tf("character.current_level",entry==null?"0":entry.m_level.ToString("0.##",CultureInfo.InvariantCulture));
                    };refresh();refreshers.Add(refresh);
                    Field(page,"New level",skillLevel,v=>skillLevel=v,"An exact level from 0 to 100. Decimals are accepted.",8);
                    ActionRow(page,"Apply to selected skill","Set level",()=>f.SetSkill(selectedSkill,Number(skillLevel,"Level",0,100)),"Raises or lowers the skill to the entered level.");
                    DangerRow(page,"Apply to all skills","Set all","Sets every skill on this character to the entered level.",()=>f.SetAllSkills(Number(skillLevel,"Level",0,100)));
                    break;
                case 1:
                    Section(page,"Survival");
                    ToggleRow(page,"Ghost / stealth",()=>Player.m_localPlayer && Player.m_localPlayer.InGhostMode(),v=>Run(()=>f.Ghost(v)),"Stops creatures from detecting you.");
                    ToggleRow(page,"Always near fire",()=>p.AlwaysNearFire,v=>p.AlwaysNearFire=v,"Counts as standing by a fire for comfort and rest.");
                    ToggleRow(page, L.T("ui.bypass_dungeon_gates"), ()=>app.Settings.Data.bypassDungeonGates, v=>{app.Settings.Data.bypassDungeonGates=v;app.Settings.Save(false);}, L.T("ui.ignore_key_requirements_for_sunken"));
                    ToggleRow(page,"Lock rested",()=>p.LockRested,v=>p.LockRested=v,"Keeps the Rested status effect active and refreshes its timer. Works with Always near fire.");
                    ToggleRow(page,"Never freeze",()=>p.NeverFreeze,v=>p.NeverFreeze=v,"Clears freezing status while enabled.");
                    ToggleRow(page,"No fall damage",()=>p.NoFallDamage,v=>p.NoFallDamage=v,"Blocks fall HitType damage on your character.");
                    ToggleRow(page,"Noclip",()=>p.Noclip,v=>p.Noclip=v,"Disables your solid colliders so you pass through terrain and pieces. Distinct from Flight - combine both to move freely through the world. Restores colliders when off. Local only.");
                    ToggleRow(page,"Keep inventory on death",()=>p.KeepInventory,v=>p.KeepInventory=v,"Skips tombstone creation so your inventory stays with you.");
                    ToggleRow(page,"Keep food on death",()=>p.KeepFoodOnDeath,v=>p.KeepFoodOnDeath=v,"Restores active food slots after death. Pair with Keep inventory / Prevent skill loss.");
                    ToggleRow(page,"No bed restrictions",()=>p.NoBedRestrictions,v=>p.NoBedRestrictions=v,"Ignores fire and exposure checks when claiming a bed.");
                    Section(page,"Death recovery");
                    ActionRow(page,"Owned tombstone","Teleport",Release250.TeleportToTombstone,"Teleports to your nearest loaded owned tombstone.");
                    ActionRow(page,"Nearby tombstone","Reclaim",Release250.ReclaimTombstone,"Interacts / pulls inventory from your nearby owned tombstone.");
                    Section(page,"Skills protection");
                    ToggleRow(page,"Prevent skill loss",()=>p.PreventSkillLoss,v=>p.PreventSkillLoss=v,"Blocks skill drain on death (OnDeath / LowerAllSkills). Does not freeze XP gains.");
                    ToggleRow(page,"Freeze skills",()=>p.FreezeSkills,v=>p.FreezeSkills=v,"Blocks skill loss and XP gains (RaiseSkill + death drain). Use Freeze XP alone if you only want no gains.");
                    ToggleRow(page,"Freeze XP",()=>p.FreezeXp,v=>p.FreezeXp=v,"Blocks RaiseSkill XP only. Skill loss on death still happens unless Prevent skill loss or Freeze skills is on.");
                    Section(page,"Status effects");
                    Field(page,"Search",statusSearch,v=>statusSearch=v,"Filters the registered status-effect list.",48);
                    SelectRow(page,"Effect",()=>string.IsNullOrEmpty(statusName)?"Choose effect":statusName,()=>StatusChoices());
                    ActionRow(page,"Selected effect","Apply",()=>FeatureSuite.ApplyStatusEffect(statusName),"Applies the chosen status effect to you.");
                    ActionRow(page,"Selected effect","Remove",()=>FeatureSuite.RemoveStatusEffect(statusName),"Removes that effect if it is active on you.");
                    DangerRow(page,"All active effects","Clear","Removes every status effect currently on your character.",FeatureSuite.RemoveAllStatusEffects);
                    Section(page,"Recovery");
                    ActionRow(page,"Health","Restore",f.Heal,"Fully restores your health.");
                    break;
                case 2:
                    Section(page,"Food");
                    ToggleRow(page,"Unlimited food timers",()=>p.InfFood,v=>p.InfFood=v,"Keeps active food slots at full duration.");
                    ToggleRow(page,"Unlimited food buff",()=>p.InfFoodBuff,v=>p.InfFoodBuff=v,"Refreshes food buff duration while enabled.");
                    ToggleRow(page,"No food buff decrease",()=>p.NoFoodDecay,v=>p.NoFoodDecay=v,"Prevents food timers from ticking down.");
                    ToggleRow(page,"Always can eat",()=>p.AlwaysCanEat,v=>p.AlwaysCanEat=v,"Lets you eat even when food slots are occupied.");
                    ToggleRow(page,"No food cooldown",()=>p.NoFoodCooldown,v=>p.NoFoodCooldown=v,"Removes the delay before eating the same food again.");
                    Section(page,"Eat from menu");
                    SelectRow(page,"Food",()=>string.IsNullOrEmpty(foodName)?"Choose food":FoodLabel(foodName),()=>FoodChoices());
                    ActionRow(page,"Selected food","Eat",()=>e.EatFoodByName(foodName),"Eats from your inventory if you have it, otherwise adds one copy then eats.");
                    Note(page,"Cooking / fermenter / smelter timers are under Build > Stations. Plant growth and breeding are under Farming.");
                    break;
                case 3:
                    Section(page,"Movement");
                    ToggleRow(page,"Flight",()=>f.WantFly || (Player.m_localPlayer && Player.m_localPlayer.IsDebugFlying()),v=>Run(()=>f.Fly(v)),"Fly with movement keys. Jump rises; crouch descends. Falls back to local fly if another mod blocks debug fly.");
                    ToggleRow(page,"Noclip",()=>p.Noclip,v=>p.Noclip=v,"Pass through colliders (same toggle as Survival). Pair with Flight to move through the world freely.");
                    ToggleRow(page,"Super jump",()=>e.SuperJump,v=>e.SuperJump=v,"Increases jump strength. Works with infinite jumps.");
                    SliderRow(page,"Jump strength",()=>d.jumpMultiplier,v=>d.jumpMultiplier=v,1,8,"Multiplier applied to jump force.");
                    ToggleRow(page,"Infinite jumps",()=>p.InfiniteJumps,v=>p.InfiniteJumps=v,"Jump repeatedly without touching the ground.");
                    SliderRow(page,"Walk speed",()=>p.WalkSpeedMult,v=>p.WalkSpeedMult=v,.1f,10,"Multiplies normal jog and walk-toggle speed. 1 is vanilla. Local player only; skipped while debug flying.");
                    SliderRow(page,"Run speed",()=>p.RunSpeedMult,v=>p.RunSpeedMult=v,.1f,10,"Multiplies sprint / run speed. 1 is vanilla.");
                    SliderRow(page,"Swim speed",()=>p.SwimSpeedMult,v=>p.SwimSpeedMult=v,.1f,10,"Multiplies swim speed. 1 is vanilla.");
                    SliderRow(page,"Sneak speed",()=>p.SneakSpeedMult,v=>p.SneakSpeedMult=v,.1f,10,"Multiplies crouch / sneak speed. 1 is vanilla.");
                    ActionRow(page,"Movement speed multipliers","Reset",()=>{p.WalkSpeedMult=p.RunSpeedMult=p.SwimSpeedMult=p.SneakSpeedMult=1f;},"Resets walk, run, swim, and sneak multipliers to 1.");
                    ToggleRow(page,"Move underwater",()=>p.MoveUnderwater,v=>p.MoveUnderwater=v,"Reduces underwater penalties while swimming.");
                    ToggleRow(page,"No water stamina damage",()=>p.NoWaterStaminaDamage,v=>p.NoWaterStaminaDamage=v,"Avoids wet and related water stamina penalties.");
                    Section(page,L.T("ui.water_movement"));
                    ToggleRow(page,L.T("ui.walk_on_water"),()=>d.walkOnWater,v=>{d.walkOnWater=v;app.Settings.Save(false);},L.T("ui.makes_the_water_surface_solid"));
                    Section(page,"Sit / mount");
                    ToggleRow(page,L.T("character.flying_mount"),()=>p.FlyingMount,v=>p.FlyingMount=v,L.T("character.flying_mount.help"));
                    ToggleRow(page,L.T("character.mount_any"),()=>p.MountAnyCreature,v=>p.MountAnyCreature=v,L.T("character.mount_any.help"));
                    ToggleRow(page,L.T("character.mount_next"),()=>Release250.MountNextArmed,v=>{Release250.MountNextArmed=v;if(v)app.Report(L.T("character.mount_next.armed"));},L.T("character.mount_next.help"));
                    ActionRow(page,"Sit emote","Sit",Release250.SitAnywhere,"Starts the sit emote without a chair.");
                    ActionRow(page,"Nearest saddle","Mount",Release250.MountNearest,L.T("ui.tries_to_mount_the_closest"));
                    ActionRow(page,L.T("character.mount_look"),L.T("character.mount_look.action"),Release250.MountLookTarget,L.T("character.mount_look.help"));
                    ActionRow(page,L.T("character.dismount"),L.T("character.dismount.action"),Release250.Dismount,L.T("character.dismount.help"));
                    Section(page,"Boats & carts");
                    ToggleRow(page,"Boat speed boost",()=>p.BoatBoost,v=>p.BoatBoost=v,"Multiplies sail and paddle thrust on the ship you helm. Restored each physics step when you leave the wheel.");
                    SliderRow(page,"Boat speed multiplier",()=>p.BoatSpeedMult,v=>p.BoatSpeedMult=v,1,8,"How hard the boat boost pushes. 1 is vanilla.");
                    ToggleRow(page,"Boat stability",()=>p.BoatStable,v=>p.BoatStable=v,"Extra damping so ships tip and rock less while you helm.");
                    ToggleRow(page,"Boat fly",()=>p.BoatFly,v=>p.BoatFly=v,"While you helm: Jump up / crouch down, plus full sail/paddle forward-back and rudder turn in the air (drives rigidbody when above water disableLevel). Jump does not leave the wheel. Restores gravity when you leave the wheel or turn this off. Local helm only.");
                    ToggleRow(page,"Force following wind",()=>p.ForceWind,v=>p.ForceWind=v,"Locks wind to your facing direction so sails catch a following breeze.");
                    ToggleRow(page,"Boat infinite health",()=>p.BoatInfHealth,v=>p.BoatInfHealth=v,"Keeps the ship you are helming from taking structure damage.");
                    ToggleRow(page,"Easy cart hauling",()=>p.EasyCart,v=>p.EasyCart=v,"Lightens an attached cart and strengthens the hitch while you pull.");
                    SliderRow(page,"Cart ease",()=>p.CartEase,v=>p.CartEase=v,1,10,"Higher = lighter cart / stronger hitch. Only while Easy cart is on.");
                    ToggleRow(page,"Cart infinite health",()=>p.CartInfHealth,v=>p.CartInfHealth=v,"Keeps the cart you are hauling from taking structure damage.");
                    break;
                default:
                    Section(page,"Guardian power");
                    ActionRow(page,"Guardian Power","Activate",f.Power,"Activates your equipped Guardian Power.");
                    SelectRow(page,"Equip Guardian Power",()=>string.IsNullOrEmpty(powerName)?"Choose power":PowerLabel(powerName),()=>ObjectDB.instance.m_StatusEffects.Where(s=>s && s.name.StartsWith("GP_",StringComparison.Ordinal)).OrderBy(s=>s.name).Select(s=>{string name=s.name;return Choice(PowerLabel(name),()=>powerName=name);}));
                    ActionRow(page,"Selected power","Equip",()=>e.EquipPower(powerName),"Equips the chosen power and clears its cooldown.");
                    ToggleRow(page,"Instant guardian cooldown",()=>p.InstantGuardianCooldown,v=>p.InstantGuardianCooldown=v,"Keeps guardian power cooldown at zero.");
                    ToggleRow(page,"Hold guardian power",()=>p.HoldGuardianPower,v=>p.HoldGuardianPower=v,"Reapplies the equipped power until this toggle is turned off.");
                    break;
            }
        }

        private void BuildPageContent(RectTransform page,int sub)
        {
            if(!RequireCharacter(page))return;
            var f=app.Features;var p=app.Phase;
            switch(sub)
            {
                case 0:
                    Section(page,"Placement rules");
                    ToggleRow(page,"Free building",()=>Player.m_localPlayer && Player.m_localPlayer.PlacementCostDisabled,v=>Run(()=>f.NoCost(v)),"Place building pieces without spending materials.");
                    ToggleRow(page,"Build everywhere",()=>p.BuildEverywhere,v=>p.BuildEverywhere=v,"Ignores most placement location blocks.");
                    ToggleRow(page,"Ignore unstable support",()=>p.IgnoreUnstable,v=>p.IgnoreUnstable=v,"Allows placement when support would be unstable.");
                    ToggleRow(page,"Ignore player in the way",()=>p.IgnorePlayerBlock,v=>p.IgnorePlayerBlock=v,"Places even if a player occupies the ghost.");
                    ToggleRow(page,"Allow placement overlap",()=>p.AllowOverlap,v=>p.AllowOverlap=v,"Clears placement ghost blocks so pieces can overlap each other / invalid space checks. Complements Ignore player in the way.");
                    ToggleRow(page,"Ignore mystical force",()=>p.IgnoreMysticalForce,v=>p.IgnoreMysticalForce=v,"Bypasses area forces that block building.");
                    ToggleRow(page,"Extended build distance",()=>p.ExtendedBuildDistance,v=>p.ExtendedBuildDistance=v,"Multiplies max place distance while building.");
                    SliderRow(page,"Build distance multiplier",()=>p.BuildDistanceMult,v=>p.BuildDistanceMult=v,1,15,"How far you can place when Extended build distance is on. 1 is vanilla.");
                    Section(page,"Rotation & snap");
                    ToggleRow(page,"Fine rotation",()=>p.FineBuildRotation,v=>p.FineBuildRotation=v,"Uses 1-degree place rotation steps instead of the coarse vanilla angle.");
                    ToggleRow(page, L.T("build.free_rotation"), () => FreeBuildRotation.Enabled, v => FreeBuildRotation.Enabled = v, L.T("build.free_rotation.help"));
                    ToggleRow(page, L.T("build.universal_snap"), () => PlacementSnap.UniversalSnap, v => PlacementSnap.UniversalSnap = v, L.T("build.universal_snap.help"));
                    ToggleRow(page, L.T("build.drag_enabled"), () => DragBuilding.Enabled, v => DragBuilding.Enabled = v, L.T("build.drag_enabled.help"));
                    Section(page,"Stations for building");
                    ToggleRow(page,"Infinite workbench radius",()=>p.InfWorkbenchRadius,v=>p.InfWorkbenchRadius=v,"Extends crafting station build range.");
                    ToggleRow(page,"Workbench without roof",()=>p.WorkbenchNoRoof,v=>p.WorkbenchNoRoof=v,"Uses workbenches that are not covered.");
                    ToggleRow(page,"Station level 10",()=>p.StationLevel10,v=>p.StationLevel10=v,"Treats nearby crafting stations as level 10.");
                    break;
                case 1:
                    Section(page,"Crafting");
                    ToggleRow(page,"Free crafting & upgrading",()=>p.FreeCraft,v=>p.FreeCraft=v,"Craft and upgrade without materials (including Forge of Potential idols). Does not discover recipes - use Unlock below for that.");
                    ToggleRow(page,"Craft from nearby chests",()=>p.CraftFromChests,v=>p.CraftFromChests=v,"When Free crafting is off, HaveRequirements and material consumption also use accessible nearby containers you own/can open. Host/co-op: only chests this client can claim. Free crafting already skips costs.");
                    SliderRow(page,"Chest craft range (m)",()=>p.CraftChestRadius,v=>p.CraftChestRadius=v,5,40,"Radius for craft-from-chests material checks and consumption. Typical station range is ~10-20 m.");
                    ToggleRow(page,"Instant crafting",()=>f.FastCraft,v=>f.FastCraft=v,"Skips the crafting and upgrade timer.");
                    DangerRow(page,"Discover every recipe","Unlock","Writes all recipes, materials, stations, and build pieces to this character. Separate from Free crafting. Re-open craft UI or stand at a station to see them.",app.Expansion.UnlockRecipes);
                    Section(page,"Forge of Potential");
                    ToggleRow(page,"Always succeed refinement",()=>p.ForgeAlwaysSucceed,v=>p.ForgeAlwaysSucceed=v,"Refinement rolls always succeed.");
                    ToggleRow(page,"Never break on fail",()=>p.ForgeNeverBreak,v=>p.ForgeNeverBreak=v,"Removes break chance. Without Always succeed, a failed roll can still drop a refinement level.");
                    break;
                case 2:
                    Section(page,"Structure");
                    ToggleRow(page,"Indestructible buildings",()=>p.IndestructibleBuildings,v=>p.IndestructibleBuildings=v,"Blocks hit and environmental wear damage on structures.");
                    ToggleRow(page,"No rain damage",()=>p.NoRainDamage,v=>p.NoRainDamage=v,"Stops weather wet-wear on structures.");
                    ToggleRow(page,"No underwater building damage",()=>p.NoUnderwaterBuildingDamage,v=>p.NoUnderwaterBuildingDamage=v,"Stops water wet-wear on structures.");
                    Section(page,"Nearby objects");
                    SliderRow(page,"Radius (m)",()=>app.World.Radius,v=>app.World.Radius=v,1,200,"Range for repair and destroy actions.");
                    ActionRow(page,"Repair nearby","Repair",p.RepairNearby,"Repairs structures within the radius.");
                    DangerRow(page,"Destroy nearby","Destroy","Permanently destroys structures within the radius. There is no undo.",p.DestroyNearby);
                    break;
                case 3:
                    Section(page,"Stations");
                    ToggleRow(page,"Infinite station fuel",()=>p.InfStationFuel,v=>p.InfStationFuel=v,"Keeps smelter, kiln, and fireplace fuel topped up.");
                    ToggleRow(page,"Fireplace stays dry",()=>p.FireplaceNoWet,v=>p.FireplaceNoWet=v,"Prevents fireplaces from being extinguished by wetness where supported.");
                    ToggleRow(page,"Station slots expanded",()=>p.StationSlots999,v=>p.StationSlots999=v,"Keeps smelter and kiln queues filled.");
                    ToggleRow(page,"Stations always active",()=>p.StationAlwaysOn,v=>p.StationAlwaysOn=v,"Keeps owned smelters processing while enabled.");
                    Section(page,"Production timers");
                    Note(page,"Speeds owned cooking stations, fermenters, and smelters. Instant plant growth lives under Farming. Pair with Character > Food for eat-from-menu.");
                    SliderRow(page,"Production speed",()=>p.ProductionSpeedMult,v=>p.ProductionSpeedMult=v,1,25,"Divides cook / ferment / smelt / grow times on stations you own. 1 is vanilla.");
                    ToggleRow(page,"Instant cooking",()=>p.InstantCook,v=>p.InstantCook=v,"Owned cooking stations finish almost immediately.");
                    ToggleRow(page,"Instant fermenting",()=>p.InstantFerment,v=>p.InstantFerment=v,"Owned fermenters finish almost immediately.");
                    ToggleRow(page,"Instant smelting",()=>p.InstantSmelt,v=>p.InstantSmelt=v,"Owned smelters / kilns finish almost immediately.");
                    Note(page,"Plant tools (instant grow, force grow, tame, breed) are under the Farming tab.");
                    break;
                default:
                    if (sub == 5) { BuildToolsPage(page); break; }
                    Section(page,"Carry & loot");
                      ToggleRow(page,L.T("ui.force_trophy_drops"),()=>p.ForceTrophy,v=>p.ForceTrophy=v,L.T("ui.forces_a_creature_to_always"));
                      SliderRow(page,L.T("ui.forced_trophy_amount"),()=>p.ForceTrophyCount,v=>p.ForceTrophyCount=Mathf.RoundToInt(v),1,10,L.T("ui.how_many_trophies_to_force"),true);
                    SliderRow(page,"Carry weight multiplier",()=>p.CarryMultiplier,v=>p.CarryMultiplier=v,.1f,20,"Multiplies your max carry weight. 1 is vanilla.");
                    ToggleRow(page,"Unlimited carry weight",()=>f.Weight,v=>f.Weight=v,"Sets carry capacity extremely high.");
                    ToggleRow(page,"No encumber",()=>p.NoEncumber,v=>p.NoEncumber=v,"Never encumbered; auto-pickup keeps working when overweight.");
                    ToggleRow(page,"Item magnet",()=>p.ItemMagnet,v=>p.ItemMagnet=v,"Pulls nearby ground items toward you.");
                    SliderRow(page,"Magnet radius (m)",()=>p.MagnetRadius,v=>p.MagnetRadius=v,1,100,"Range for the item magnet.");
                    SliderRow(page,"Stack pickup multiplier",()=>p.StackPickupMultiplier,v=>p.StackPickupMultiplier=v,1,20,"Multiplies stack size when picking up ground loot, capped by max stack.");
                    ToggleRow(page,"Infinite stack splitting",()=>p.InfStackSplit,v=>p.InfStackSplit=v,"When you split or drag part of a stack, the source stack keeps its count (duplicate).");
                    Section(page,"Inventory capacity");
                    Note(page,L.T("build.inventory.note_override"));
                    ToggleRow(page,L.T("build.inv_override"),()=>InventoryCapacity.OverrideBagSize,v=>InventoryCapacity.OverrideBagSize=v,L.T("build.inv_override.help"));
                    SliderRow(page,"Player inventory rows",()=>InventoryCapacity.PlayerRows,v=>{InventoryCapacity.PlayerRows=Mathf.RoundToInt(v);},InventoryCapacity.MinRows,InventoryCapacity.MaxRows,L.T("build.player_rows.help_override"),true);
                    SliderRow(page,"Player extra columns",()=>InventoryCapacity.PlayerExtraCols,v=>{InventoryCapacity.PlayerExtraCols=Mathf.RoundToInt(v);},0,InventoryCapacity.MaxExtraCols,L.T("build.player_extra_cols.help_override"),true);
                    SliderRow(page,"Chest bonus rows",()=>InventoryCapacity.ChestBonusRows,v=>{InventoryCapacity.ChestBonusRows=Mathf.RoundToInt(v);},0,InventoryCapacity.MaxChestBonusRows,"Extra rows added to chests when you open them. Chest width stays normal.",true);
                    SliderRow(page,"Stack size multiplier",()=>InventoryCapacity.StackMult,v=>{InventoryCapacity.StackMult=v;InventoryCapacity.ApplyStackSizes();},1,InventoryCapacity.MaxStackMult,"Multiplies max stack size for stackable items. 1 is vanilla. Applies immediately.");
                    SliderRow(page,"Stack size override",()=>InventoryCapacity.StackOverride,v=>{InventoryCapacity.StackOverride=Mathf.RoundToInt(v);InventoryCapacity.ApplyStackSizes();},0,InventoryCapacity.MaxStackOverride,"When above 0, all stackables use this max instead of the multiplier. 0 = off.",true);
                    ActionRow(page,L.T("build.capacity_apply"),L.T("build.capacity_apply.action"),()=>InventoryCapacity.ApplyAll(true),L.Tf("build.capacity_apply.help_override",InventoryCapacity.StatusLine()));
                    Note(page,InventoryCapacity.StatusLine());
                    ToggleRow(page,"Unrestricted portals",()=>f.PortalItems,v=>f.PortalItems=v,"Take restricted items through portals.");
                    ToggleRow(page,"Extended interaction reach",()=>f.DistanceEnabled,v=>f.DistanceEnabled=v,"Use the reach distance set below.");
                    SliderRow(page,"Interaction reach (m)",()=>f.Distance,v=>f.Distance=v,1,50,"Distance for interacting with objects.");
                    Section(page,"Nearby chests");
                    Note(page,"Moves stacks between your inventory and chests you can open in range. Prefers existing stacks, then empty slots. Skips private or ward-blocked chests.");
                    SliderRow(page,"Chest radius (m)",()=>ChestInventory.Radius,v=>ChestInventory.Radius=v,1,50,"Range for deposit and withdraw.");
                    ToggleRow(page,"Deposit materials only",()=>ChestInventory.DepositMaterialsOnly,v=>ChestInventory.DepositMaterialsOnly=v,"When depositing, only move materials (wood, ore, etc.).");
                    ToggleRow(page,"Skip equipped on deposit",()=>ChestInventory.DepositSkipEquipped,v=>ChestInventory.DepositSkipEquipped=v,"Leaves currently equipped gear in your inventory.");
                    Field(page,"Withdraw name filter",ChestInventory.WithdrawSearch,v=>ChestInventory.WithdrawSearch=v,"Optional. Withdraw only items whose name contains this text. Empty = all.");
                    ActionRow(page,"Deposit to nearby chests","Deposit",()=>ChestInventory.DepositNearby(app),"Moves inventory stacks into nearby accessible chests.");
                    ActionRow(page,"Withdraw from nearby chests","Withdraw",()=>ChestInventory.WithdrawNearby(app),"Pulls stacks from nearby accessible chests into your inventory.");
                    Section(page,"Sort / junk / auto-pickup");
                    ActionRow(page,"Player inventory","Sort & merge",Release250.SortPlayerInventory,"Sorts by type/name and merges compatible stacks.");
                    ActionRow(page,"Nearby chests","Sort & merge",Release250.SortNearbyChests,"Sorts and merges stacks in accessible nearby chests.");
                    ActionRow(page,"Junk to nearby chests","Deposit junk",Release250.DepositJunk,"Deposits default junk list plus your extras, minus blacklist.");
                    Note(page,L.Tf("build.junk_defaults_note",Release250.JunkDefaultsText()));
                    Field(page,"Junk extras (comma IDs)",Release250.JunkExtra,v=>Release250.JunkExtra=v,"Extra item names treated as junk (comma-separated).",200);
                    Field(page,"Junk blacklist",Release250.JunkBlacklist,v=>Release250.JunkBlacklist=v,"Names removed from the junk set.",200);
                    ToggleRow(page,"Auto-pickup filter",()=>p.AutoPickupFilter,v=>p.AutoPickupFilter=v,"When on, ground auto-pickup and magnet honor whitelist/blacklist.");
                    Field(page,"Pickup whitelist",Release250.PickupWhitelist,v=>Release250.PickupWhitelist=v,"If non-empty, only these items auto-pickup. Comma-separated.",200);
                    Field(page,"Pickup blacklist",Release250.PickupBlacklist,v=>Release250.PickupBlacklist=v,"These items never auto-pickup when filter is on.",200);
                    break;
            }
        }

        private void WorldPage(RectTransform page,int sub)
        {
            if(!RequireCharacter(page))return;
            var p=app.Phase;var d=app.Settings.Data;var e=app.Expansion;
            switch(sub)
            {
                case 0:
                    Section(page,"Environment");
                    ToggleRow(page,"Always daytime",()=>d.alwaysDay,v=>{
                        d.alwaysDay=v;
                        if(v && e!=null)
                        {
                            // Forced weather uses m_debugEnv; noon lock fights storm lighting - drop weather ownership.
                            e.ClearWeather(notify:false);
                        }
                    },"Holds daylight at noon on this machine. World clock still advances. Turn off before weather that needs night lighting. Cleared automatically when you apply weather.");
                    ToggleRow(page,"Disable mist",()=>p.DisableMist,v=>p.DisableMist=v,"Hides mist particles while enabled.");
                    SliderRow(page,"Pickaxe radius",()=>p.PickaxeRadius,v=>p.PickaxeRadius=v,.5f,5,"Multiplies melee attack range for mining swings.");
                    SliderRow(page,"Explore radius",()=>p.ExploreRadius,v=>p.ExploreRadius=v,.1f,20,"Multiplies minimap exploration radius.");
                    Section(page,"Weather");
                    Note(page,"Host only. Applies on this machine; other players keep their own weather. Force following wind is under Character > Movement.");
                    SelectRow(page,"Preset",()=>string.IsNullOrEmpty(e.SelectedWeather)?"Choose weather":e.SelectedWeather,()=>
                        ExpansionFeatures.WeatherPresets.Select(name=>Choice(name,()=>e.SelectedWeather=name))
                            .Concat(e.EnvironmentNames().Where(n=>!ExpansionFeatures.WeatherPresets.Any(p=>string.Equals(p,n,StringComparison.OrdinalIgnoreCase))).Select(n=>Choice(n,()=>e.SelectedWeather=n))));
                    SliderRow(page,"Force duration (s)",()=>e.WeatherDuration,v=>e.WeatherDuration=v,0,1800,"0 keeps the weather until Clear. Otherwise clears after this many real-time seconds.");
                    ToggleRow(page,"Instant switch",()=>weatherInstant,v=>weatherInstant=v,"Switches weather immediately instead of blending.");
                    ActionRow(page,"Apply weather","Set",()=>e.ApplyWeather(e.SelectedWeather,e.WeatherDuration,weatherInstant),"Applies the selected weather. Turns off Always daytime so lighting matches.");
                    ActionRow(page,"Clear weather","Clear",()=>e.ClearWeather(true),"Removes the weather override.");
                    Section(page,"Map");
                    DangerRow(page,"Reveal the entire map","Reveal","Permanently reveals this world's map for your character. Previous exploration cannot be restored.",app.Features.Explore);
                    ToggleRow(page,L.T("ui.force_enable_map"),()=>app.Settings.Data.forceEnableMap,v=>{app.Settings.Data.forceEnableMap=v;app.Settings.Save(false);},L.T("ui.overrides_world_settings_and_mods"));
                    ActionRow(page,L.T("ui.all_boss_altars"),L.T("ui.mark_nearest"),()=>app.Expansion.DiscoverNamedLocations("Eikthyr","GDKing","Bonemass","Dragonqueen","GoblinKing","mistlands_dvergrboss","location_dvergrboss","fader","ashlands_fader","location_dnbossroomnew","location_dnbossroom","dnbossroomnew","dnbossroom","morkhalla","location_morkhalla"),L.T("ui.marks_the_nearest_altar_for"));
                    ActionRow(page,L.T("ui.all_traders"),L.T("ui.mark_nearest"),()=>app.Expansion.DiscoverNamedLocations("Vendor_BlackForest","Hildir_camp","BogWitch_Camp"),L.T("ui.marks_haldor_hildir_and_the"));
                    ActionRow(page,"Forge of Potential",L.T("ui.mark_nearest"),()=>app.Expansion.DiscoverForgeOfPotential(),L.T("ui.marks_the_nearest_generated_forge"));
                    Note(page,"Minimap zoom lives under Camera.");
                    Section(page,"Messages");
                    Field(page,"Message",message,v=>message=v,"Text to send.",240);
                    Field(page,"Display name",alias,v=>alias=v,"Name displayed beside your chat message.",48);
                    ActionRow(page,"Center-screen announcement","Send",()=>app.Network.Send("event",-1,text:message),"Shows the message in yellow at the center of the screen.");
                    ActionRow(page,"Chat message","Send",()=>app.Network.Send("chat",-1,text:message,alias:alias),"Sends the message with the entered display name.");
                    break;
                case 1:
                    Section(page,"Nearby creatures");
                    SliderRow(page,"Radius (m)",()=>app.World.Radius,v=>app.World.Radius=v,1,200,"Range for nearby creature and cleanup actions.");
                    ToggleRow(page,"Freeze AI (nearby)",()=>p.FreezeAiNearby,v=>p.FreezeAiNearby=v,"Pauses BaseAI updates for untamed creatures within the radius. Does not retame. Local session.");
                    ToggleRow(page,"Freeze AI (global)",()=>p.FreezeAiGlobal,v=>p.FreezeAiGlobal=v,"Pauses BaseAI for all loaded untamed creatures on this client. Stronger than nearby.");
                    DangerRow(page,"Ground drops","Remove","Permanently deletes loose items within the radius.",app.World.DeleteDrops);
                    DangerRow(page,"Your tombstones","Remove","Deletes your nearby tombstones and all items inside them.",app.World.RemoveTombstones);
                    DangerRow(page,"Creatures","Kill","Kills creatures within the radius, including tamed animals.",app.World.KillEntities);
                    break;
                case 2:
                    TerrainPage(page);
                    break;
            }
        }

        private void TeleportPage(RectTransform page)
        {
            if(!RequireCharacter(page))return;
            var p=app.Phase;
            Section(page,"Quick travel");
            ActionRow(page,"Crosshair aim point","Teleport",FeatureSuite.TeleportToCrosshair,"Raycasts from the camera and teleports to the hit point (or a short distance ahead if nothing is hit).");
            ToggleRow(page,"TP to map ping",()=>p.TpToPing,v=>p.TpToPing=v,"When you send a map ping (Chat.SendPing), teleport there. Local player only.");
            ToggleRow(page,"Disable portal fade",()=>p.DisablePortalFade,v=>p.DisablePortalFade=v,"While teleporting, forces Hud loading-screen alpha to 0 so the world stays visible. Best-effort; some fade paths may still flash.");
            Coordinates(page,false);MarkerSelector(page,false);
            Section(page,"Named bookmarks");
            Note(page,"Saved per world in Valheaven.bookmarks.json beside the game (or AppData fallback).");
            Field(page,"Bookmark name",bookmarkName,v=>bookmarkName=v,"Name for the bookmark at your current position.",48);
            ActionRow(page,"Current position","Save bookmark",()=>TeleportBookmarks.Add(bookmarkName,app.RequirePlayer().transform.position),"Creates or updates a bookmark for this world.");
            SelectRow(page,"Bookmark",()=>string.IsNullOrEmpty(selectedBookmark)?"Choose bookmark":selectedBookmark,()=>BookmarkChoices());
            ActionRow(page,"Selected bookmark","Teleport",()=>TeleportBookmarks.Teleport(selectedBookmark),"Teleports to the saved coordinates.");
            DangerRow(page,"Selected bookmark","Delete","Removes the bookmark from this world's list.",()=>TeleportBookmarks.Remove(selectedBookmark));
            Section(page,"Player destination");
            SelectRow(page,"Visible player",()=>playerDestinationName,()=>ZNet.instance.GetPlayerList().Where(pl=>pl.m_publicPosition).Select(pl=>{var id=pl.m_characterID;var name=pl.m_name;return Choice(name,()=>{playerDestination=id;playerDestinationName=name;});}));
            ActionRow(page,"Travel to player","Teleport",()=>{
                var found=ZNet.instance.GetPlayerList().FirstOrDefault(pl=>pl.m_characterID==playerDestination && pl.m_publicPosition);
                if(!found.m_publicPosition)throw new ArgumentException("Choose a player sharing their map position.");
                app.Features.Teleport(found.m_position+Vector3.up*2);
            },"Teleports you to the selected player's current map position.");
            Section(page,L.T("ui.world_locations"));
            SelectRow(page,"Location",()=>string.IsNullOrEmpty(locationName)?"Choose location":LocationLabel(locationName),()=>ZoneSystem.instance.m_locations.Where(l=>l.m_enable && !string.IsNullOrEmpty(l.m_prefabName)).Select(l=>l.m_prefabName).Distinct().OrderBy(n=>LocationLabel(n)).Select(n=>Choice(LocationLabel(n),()=>locationName=n)));
            ActionRow(page,"Nearest matching location","Teleport",()=>app.Expansion.TeleportToLocation(locationName),"Finds the nearest generated location. Search for an altar, trader, or forge by its location name.");
            Section(page,"End-game shortcuts");
            ActionRow(page,"Forge of Potential","Teleport",()=>app.Expansion.TeleportToForgeOfPotential(),"Finds the nearest Forge of Potential (refinement station) in generated world locations.");
            ActionRow(page,"Deep North boss room","Teleport",()=>app.Expansion.TeleportToNamedLocations("location_dnbossroomnew","location_dnbossroom","dnbossroomnew","dnbossroom","morkhalla","location_morkhalla"),"Nearest Deep North boss / Morkhalla-style location name the world knows.");
            ActionRow(page,"Fader altar","Teleport",()=>app.Expansion.TeleportToNamedLocations("fader","ashlands_fader"),"Nearest Fader sacrificial altar.");
            ActionRow(page,"The Queen","Teleport",()=>app.Expansion.TeleportToNamedLocations("mistlands_dvergrboss","location_dvergrboss"),"Nearest Queen / Mistlands dvergr boss location.");
        }

        private void CameraPage(RectTransform page)
        {
            var p=app.Phase;
            Section(page,"View");
            ToggleRow(page,"Hide HUD",()=>p.HideHud,v=>p.HideHud=v,"Hides the game HUD while enabled. The menu stays available.");
            ToggleRow(page,L.T("ui.fullbright"),()=>app.Settings.Data.fullbright,v=>{app.Settings.Data.fullbright=v;app.Settings.Save(false);},L.T("ui.locally_lights_up_dark_areas"));
            if(app.Settings.Data.fullbright) SliderRow(page,L.T("ui.fullbright_intensity"),()=>app.Settings.Data.fullbrightIntensity,v=>{app.Settings.Data.fullbrightIntensity=v;app.Settings.Save(false);},0.1f,2.0f,L.T("ui.intensity_of_the_local_light"));
            SliderRow(page,"Field of view scale",()=>p.FovScale,v=>p.FovScale=v,.5f,2,"Scales the camera field of view. Takes effect immediately.");
            SliderRow(page,"Max camera distance",()=>p.CameraDistance,v=>p.CameraDistance=v,.5f,5,"Scales third-person zoom range. Current zoom keeps its relative position.");
            SliderRow(page,"Max boat camera distance",()=>p.BoatCameraDistance,v=>p.BoatCameraDistance=v,.5f,5,"Scales camera distance while sailing.");
            Section(page,"Freecam");
            ToggleRow(page,"Freecam (free-fly)",()=>p.Freecam||(GameCamera.instance&&GameCamera.InFreeFly()),v=>Release250.SetFreecam(v),"Detaches the camera using vanilla GameCamera free-fly. Independent of character Flight.");
            ToggleRow(page,L.T("camera.freecam_build"),()=>p.FreecamBuild,v=>p.FreecamBuild=v,L.T("camera.freecam_build.help"));
            Section(page,"Photo mode");
            ToggleRow(page,"Photo mode",()=>p.PhotoMode,v=>Release250.SetPhotoMode(v),"Hides HUD chrome; optional freeze. Distinct from freecam but can be combined.");
            ToggleRow(page,"Photo freeze world",()=>p.PhotoFreeze,v=>{p.PhotoFreeze=v;if(p.PhotoMode&&v)Time.timeScale=0.0001f;else if(!v&&Mathf.Abs(Time.timeScale-1f)>.001f)Time.timeScale=1f;},"Near-pauses time while Photo mode is on. Restores on exit.");
            Section(page,"Minimap");
            ToggleRow(page,"Minimap zoom control",()=>p.MinimapZoomControl,v=>p.MinimapZoomControl=v,"Expands m_minZoom/m_maxZoom and continuously drives m_largeZoom + m_smallZoom from the slider.");
            SliderRow(page,"Minimap zoom",()=>Release250.MinimapZoom,v=>Release250.MinimapZoom=v,.5f,2.5f,"0.5 = closest in, 2.5 = farthest out (gentler curve / tighter max than 2.5.3).");
        }

        private void Coordinates(RectTransform page,bool remote)
        {
            Section(page,"Coordinates");
            var fx=Field(page,"X ? East / west",x,v=>x=v,"East / west coordinate.",16);
            var fy=Field(page,"Y ? Altitude",y,v=>y=v,"Height above the world origin.",16);
            var fz=Field(page,"Z ? North / south",z,v=>z=v,"North / south coordinate.",16);
            ActionRow(page,"Use current position","Copy",()=>
            {
                var player=app.RequirePlayer().transform.position;
                x=player.x.ToString("0.##",CultureInfo.InvariantCulture);y=player.y.ToString("0.##",CultureInfo.InvariantCulture);z=player.z.ToString("0.##",CultureInfo.InvariantCulture);
                if(fx)fx.SetTextWithoutNotify(x);if(fy)fy.SetTextWithoutNotify(y);if(fz)fz.SetTextWithoutNotify(z);
            },"Copies your position into the coordinate fields.");
            ActionRow(page,remote?"Move selected player":"Move to coordinates","Teleport",()=>
            {
                var pos=new Vector3(Number(x,"X",-20000,20000),Number(y,"Y",-10000,10000),Number(z,"Z",-20000,20000));
                app.Features.Teleport(pos);
            },"Moves to the entered coordinates. Check the altitude before teleporting.");
        }
        private void MarkerSelector(RectTransform page,bool remote)
        {
            Section(page,"Map markers");
            SelectRow(page,"Destination",()=>markerName,()=>app.World.Pins().Select(pin=>
            {var pos=pin.m_pos+Vector3.up*2;var name=string.IsNullOrEmpty(pin.m_name)?pin.m_pos.ToString("0"):Rules.Text(pin.m_name,60);return Choice(name,()=>{marker=pos;markerName=name;});}));
            ActionRow(page,"Move to marker","Teleport",()=>
            {
                if(!marker.HasValue)throw new ArgumentException("Choose a map marker first.");
                app.Features.Teleport(marker.Value);
            },"Teleports to the chosen map marker.");
        }
        private void TerrainPage(RectTransform page)
        {
            if(!RequireCharacter(page))return;
            var w=app.World;
            Section(page,"Brush");
            SliderRow(page,"Radius (m)",()=>w.BrushRadius,v=>w.BrushRadius=v,1,50,"Size of the terrain brush. Large radii only affect loaded heightmaps you own.");
            SliderRow(page,"Feather edge (m)",()=>w.BrushFeather,v=>w.BrushFeather=v,0,12,"0 is a hard edge. Higher blends flatten, raise, lower, and fill back into the surrounding ground. Same slope as blueprint paste.");
            SliderRow(page,"Height adjustment (m)",()=>w.BrushAmount,v=>w.BrushAmount=v,.05f,8,"Raise/lower delta. Precision mode applies 25% of this value.");
            SliderRow(page,"Paint strength",()=>w.PaintStrength,v=>w.PaintStrength=v,.1f,1,"How strongly paint layers are applied.");
            ToggleRow(page,"Square brush",()=>w.Square,v=>w.Square=v,"Switches from a circular brush to a square brush.");
            ToggleRow(page,"Precision",()=>w.Precision,v=>w.Precision=v,"Applies 25% of the height adjustment and softer raise edges for fine work.");
            ToggleRow(page,"Brush at look hit",()=>w.BrushAtLook,v=>w.BrushAtLook=v,"Centers the brush on the surface under the crosshair instead of your feet (still uses player Y for Flatten to player height).");
            Section(page,"Presets");
            ActionRow(page,"Fine detail","2 m",()=>w.ApplyBrushPreset(2,.25f),"Radius 2 m, amount 0.25 m.");
            ActionRow(page,"Building pad","8 m",()=>w.ApplyBrushPreset(8,1),"Radius 8 m, amount 1 m.");
            ActionRow(page,"Yard","20 m",()=>w.ApplyBrushPreset(20,1.5f),"Radius 20 m, amount 1.5 m.");
            ActionRow(page,"Wide reshape","40 m",()=>w.ApplyBrushPreset(40,2),"Radius 40 m, amount 2 m. Use carefully near builds.");
            Section(page,"Shape terrain");
            DangerRow(page,"Flatten at brush","Flatten","Levels terrain in the brush to the brush-center height (feet or look hit).",()=>w.Terrain(w.BrushAtLook?"levellook":"level"));
            DangerRow(page,"Flatten to player height","Player Y","Levels the brush area (feet or look XZ) to your feet height - good for pads.",()=>w.Terrain("levelplayer"));
            DangerRow(page,"Raise terrain","Raise","Raises terrain in the brush. Buildings can be affected.",()=>w.Terrain("raise"));
            DangerRow(page,"Lower terrain","Lower","Lowers terrain in the brush. Buildings can be affected.",()=>w.Terrain("lower"));
            DangerRow(page,"Smooth terrain","Smooth","Smooths height differences in the brush.",()=>w.Terrain("smooth"));
            DangerRow(page,"Fill dips","Fill","Levels toward brush height while softly raising dips - useful for hole fill.",()=>w.Terrain("fillholes"));
            DangerRow(page,"Reset terrain","Reset","Removes height and paint changes inside the brush (and nearby player terrain modifiers).",()=>w.Terrain("reset"));
            Section(page,"Paint");
            SelectRow(page,"Paint layer",()=>WorldTools.PaintModeLabel(w.PaintMode),()=>new[]{
                Choice("Dirt",()=>w.PaintMode=0),
                Choice("Cultivate",()=>w.PaintMode=1),
                Choice("Paved",()=>w.PaintMode=2),
                Choice("Grass / Vegetation (Reset)",()=>w.PaintMode=3),
                Choice("Clear vegetation",()=>w.PaintMode=4)
            });
            DangerRow(page,"Paint terrain","Paint","Applies the selected ground paint in the brush. Persists in the world save.",()=>w.Terrain("paint"));
            Section(page,"History");
            ActionRow(page,"Last brush stroke","Undo",()=>w.UndoTerrain(),"Restores height/paint arrays from before the last Valheaven terrain action. One step only.");
            ActionRow(page,L.T("world.level_last_paste"),L.T("world.level_last_paste.action"),()=>w.LevelLastPaste(false),L.T("world.level_last_paste.help"));
            ActionRow(page,L.T("world.feather_last_paste"),L.T("world.feather_last_paste.action"),()=>w.LevelLastPaste(true),L.T("world.feather_last_paste.help"));
            Note(page,"Terrain edits need ownership of loaded compilers (singleplayer / host). Undo is session-only and replaces the previous snapshot each stroke.");
        }
        internal static string LocationLabel(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name ?? "";
            if (TryLocationDisplayName(name, out var display)) return display;
            return name;
        }
        private static string AimTargetLabel(int mode)
        {
            switch (mode)
            {
                case 1: return "Highest health";
                case 2: return "Closest to player";
                default: return "Closest to crosshair";
            }
        }
        private static bool TryLocationDisplayName(string prefab, out string display)
        {
            display = null;
            if (prefab.IndexOf("upgrad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prefab.IndexOf("potential", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prefab.IndexOf("refinement", StringComparison.OrdinalIgnoreCase) >= 0)
            { display = "Forge of Potential"; return true; }
            // Loc keys wrong/generic for these teleports.
            switch (prefab.ToLowerInvariant())
            {
                case "eikthyr": case "eikthyrnir": display = "Eikthyr"; return true;
                case "gdking": display = "The Elder"; return true;
                case "bonemass": display = "Bonemass"; return true;
                case "dragonqueen": display = "Moder"; return true;
                case "goblinking": display = "Yagluth"; return true;
                case "mistlands_dvergrboss": case "location_dvergrboss": display = "The Queen"; return true;
                case "fader": case "ashlands_fader": display = "Fader"; return true;
                case "location_dnbossroomnew": case "location_dnbossroom": case "dnbossroomnew":
                case "dnbossroom": case "morkhalla": case "location_morkhalla": display = "Deep North Boss"; return true;
                case "vendor_blackforest": display = "Haldor"; return true;
                case "hildir_camp": display = "Hildir"; return true;
                case "bogwitch_camp": display = "The Bog Witch"; return true;
            }
            foreach (var key in LocationLocKeys(prefab))
            {
                if (!TryLocalizeKey(key, out display)) continue;
                return true;
            }
            return false;
        }
        private static IEnumerable<string> LocationLocKeys(string prefab)
        {
            string n = prefab.Trim();
            string lower = n.ToLowerInvariant();
            yield return n;
            yield return "location_" + n;
            yield return "piece_" + n;
            yield return "npc_" + n;
            if (lower.StartsWith("location_", StringComparison.Ordinal))
            {
                string stripped = n.Substring("location_".Length);
                yield return stripped;
                yield return "location_" + stripped;
                yield return "piece_" + stripped;
            }
            if (lower.StartsWith("piece_", StringComparison.Ordinal))
                yield return n.Substring("piece_".Length);
            switch (lower)
            {
                case "vendor_blackforest": yield return "npc_haldor"; break;
                case "hildir_camp": yield return "npc_hildir"; break;
                case "bogwitch_camp": yield return "npc_bogwitch"; break;
                case "upgradestation": case "upgrade_station": case "forgeofpotential": case "forge_of_potential":
                    yield return "piece_upgradestation"; break;
                case "mistlands_dvergrboss":
                    yield return "location_dvergrboss"; break;
            }
        }
        private static bool TryLocalizeKey(string key, out string text)
        {
            text = null;
            if (Localization.instance == null || string.IsNullOrWhiteSpace(key)) return false;
            string dollar = key[0] == '$' ? key : "$" + key;
            string localized = Localization.instance.Localize(dollar);
            if (string.IsNullOrWhiteSpace(localized)) return false;
            if (localized == dollar || localized == key) return false;
            if (localized.Length > 0 && localized[0] == '$') return false;
            string bare = dollar.Substring(1);
            if (string.Equals(localized, bare, StringComparison.OrdinalIgnoreCase)) return false;
            if (localized.StartsWith("??", StringComparison.Ordinal)) return false;
            // Skip lore/topic leftovers from odd key hits.
            if (localized.StartsWith("Lore:", StringComparison.OrdinalIgnoreCase)) return false;
            text = localized.Trim();
            return text.Length > 0;
        }
        private void OverlayPage(RectTransform page)
        {
            var d=app.Settings.Data;var o=app.Overlay;
            ToggleRow(page,"Enable ESP",()=>o.Enabled,v=>o.Enabled=v,"Shows labels over nearby objects.");
            Section(page,"Show labels for");
            string[] names={"Players","Creatures","Pickables","Ore deposits","Ground drops", "Locations (POI)", "Tombstones"};
            for(int i=0;i<names.Length;i++){int category=i;ToggleRow(page,names[i],()=>o.Categories[category],v=>o.Categories[category]=v,L.Tf("esp.category.help",names[i].ToLowerInvariant()));}
            Section(page,"Containers");
            ToggleRow(page,"Containers",()=>o.Categories[(int)Overlay.Kind.Container],v=>o.Categories[(int)Overlay.Kind.Container]=v,"Labels every Container: chests, crates, reinforced chests, ship holds, carts, and the rest. Tombstones stay on their own toggle.");
            ToggleRow(page,"Show item count",()=>d.espContainerCount,v=>d.espContainerCount=v,"Adds the number of stacks beside the container name. Hidden while List contents is on.");
            ToggleRow(page,"List contents",()=>d.espContainerItems,v=>d.espContainerItems=v,"Draws item names and stacks under the container name and distance.");
            SliderRow(page,"Content lines",()=>d.espContainerLines,v=>d.espContainerLines=Mathf.RoundToInt(v),1,64,"How many item lines to draw. A chest never lists more lines than its own slot count.",true);
            Note(page,"Hide individual container types under ESP > Filters > Containers.");
            int chest=(int)Overlay.Kind.Container;
            SliderRow(page,"Container red",()=>d.colors[chest].r,v=>{var color=d.colors[chest];color.r=v;d.colors[chest]=color;},0,1,"Red intensity for container labels.");
            SliderRow(page,"Container green",()=>d.colors[chest].g,v=>{var color=d.colors[chest];color.g=v;d.colors[chest]=color;},0,1,"Green intensity for container labels.");
            SliderRow(page,"Container blue",()=>d.colors[chest].b,v=>{var color=d.colors[chest];color.b=v;d.colors[chest]=color;},0,1,"Blue intensity for container labels.");
            Section(page,"Range & density");
            SliderRow(page,"Detection radius (m)",()=>o.Radius,v=>o.Radius=v,10,500,"Maximum detection distance.",true);
            SliderRow(page,"Maximum labels",()=>o.MaxLabels,v=>o.MaxLabels=Mathf.RoundToInt(v),10,200,"Limits label count. Nearby objects take priority.",true);
            ToggleRow(page,"Avoid overlaps",()=>d.espAvoidOverlap,v=>d.espAvoidOverlap=v,"Hides labels that overlap another label.");
            SliderRow(page,"Label spacing (px)",()=>d.espSpacing,v=>d.espSpacing=v,0,20,"Extra space between labels.");
            Section(page,"Appearance");
            SliderRow(page,"Text size (px)",()=>d.espFont,v=>d.espFont=Mathf.RoundToInt(v),8,24,"Size of the label text.",true);
            ToggleRow(page,"Show distance",()=>d.espDistance,v=>d.espDistance=v,"Adds the distance beside each name.");
            ToggleRow(page,"Text outline",()=>d.espOutline,v=>d.espOutline=v,"Adds a thin dark outline to improve readability.");
            ToggleRow(page,"Label background",()=>d.espBackground,v=>d.espBackground=v,"Adds a background fitted to each label.");
            SliderRow(page,"Background opacity",()=>d.espOpacity,v=>d.espOpacity=v,0,1,"Opacity of the label background.");
            SliderRow(page,"Vertical offset (px)",()=>d.espOffset,v=>d.espOffset=v,-60,60,"Moves labels above or below objects.");
            ToggleRow(page,"Hide while menu is open",()=>d.espHideInMenu,v=>d.espHideInMenu=v,"Hides ESP while you use the menu.");
            SliderRow(page,"Refresh interval (s)",()=>d.espRefresh,v=>d.espRefresh=v,.25f,5,"Time between searches for nearby objects.");
            for(int i=0;i<names.Length;i++)
            {
                int c=i;Section(page,names[i]+" color");
                SliderRow(page,"Red",()=>d.colors[c].r,v=>{var color=d.colors[c];color.r=v;d.colors[c]=color;},0,1,"Red intensity.");
                SliderRow(page,"Green",()=>d.colors[c].g,v=>{var color=d.colors[c];color.g=v;d.colors[c]=color;},0,1,"Green intensity.");
                SliderRow(page,"Blue",()=>d.colors[c].b,v=>{var color=d.colors[c];color.b=v;d.colors[c]=color;},0,1,"Blue intensity.");
            }
            Section(page,"Ore markers");
            ToggleRow(page,"Pin nearby deposits",()=>o.AutoPin,v=>o.AutoPin=v,"Adds map markers for nearby ore deposits.");
            ActionRow(page,"Deposit markers","Clear",o.RemovePins,"Removes ore markers created by this menu.");
        }
        private void HotkeysPage(RectTransform page)
        {
            HotkeyRegistry.Ensure();
            Section(page,L.T("hotkeys.section.menu"));
            Note(page,L.T("hotkeys.menu.note"));
            var menuRow=Row(page,46);var menuTitle=Label(menuRow,L.T("hotkeys.open_menu"),17);Stretch(menuTitle.rectTransform,0,0,280,0);
            var menuKey=Label(menuRow,app.Hotkey.ToString(),17,Gold);At(menuKey.rectTransform,290,0,140,42);refreshers.Add(()=>{if(menuKey)menuKey.text=app.Hotkey.ToString();});
            var menuBind=ActionButton(menuRow,L.T("hotkeys.change_key"),()=>{BindingFeatureId=null;BindingKey=true;bindAfterFrame=Time.frameCount+1;bindText.text=L.T("hotkeys.press_a_key");},L.T("hotkeys.change_key.help"));
            var menuRect=(RectTransform)menuBind.transform;menuRect.anchorMin=menuRect.anchorMax=new Vector2(1,.5f);menuRect.pivot=new Vector2(1,.5f);menuRect.sizeDelta=new Vector2(150,34);bindText=menuBind.GetComponentInChildren<TextMeshProUGUI>();
            Section(page,L.T("hotkeys.section.feature_binds"));
            foreach(var feature in HotkeyRegistry.All)
            {
                var f=feature;
                var row=Row(page,46);
                var title=Label(row,f.Label+(f.Kind==HotkeyKind.Toggle?L.T("hotkeys.toggle_suffix"):L.T("hotkeys.action_suffix")),16);Stretch(title.rectTransform,0,0,300,0);
                var keyLabel=Label(row,f.Key==KeyCode.None?L.T("hotkeys.unbound"):f.Key.ToString(),15,Gold);At(keyLabel.rectTransform,300,0,120,42);
                refreshers.Add(()=>{if(keyLabel)keyLabel.text=f.Key==KeyCode.None?L.T("hotkeys.unbound"):f.Key.ToString();});
                var clear=ActionButton(row,L.T("hotkeys.clear"),()=>{HotkeyRegistry.Clear(f.Id);app.Report(L.Tf("toast.hotkey_unbound", f.Label));if(app.Settings.Data.autoSave)app.Settings.Save(false);},L.T("hotkeys.clear.help"));
                var clearRect=(RectTransform)clear.transform;clearRect.anchorMin=clearRect.anchorMax=new Vector2(1,.5f);clearRect.pivot=new Vector2(1,.5f);clearRect.sizeDelta=new Vector2(72,34);clearRect.anchoredPosition=new Vector2(0,0);
                var bind=ActionButton(row,L.T("hotkeys.bind"),()=>{BindingFeatureId=f.Id;BindingKey=true;bindAfterFrame=Time.frameCount+1;if(bindText)bindText.text=L.T("hotkeys.press_a_key");app.Report(L.Tf("toast.hotkey_press_for", f.Label));},L.T("hotkeys.bind.help"));
                var bindRect=(RectTransform)bind.transform;bindRect.anchorMin=bindRect.anchorMax=new Vector2(1,.5f);bindRect.pivot=new Vector2(1,.5f);bindRect.sizeDelta=new Vector2(72,34);bindRect.anchoredPosition=new Vector2(-80,0);
            }
        }
        private void SettingsPage(RectTransform page)
        {
            var d=app.Settings.Data;
            Section(page,L.T("settings.section.controls"));
            Note(page,L.T("settings.controls.note"));
            var row=Row(page,46);var title=Label(row,L.T("settings.open_menu"),17);Stretch(title.rectTransform,0,0,320,0);
            var current=Label(row,app.Hotkey.ToString(),17,Gold);At(current.rectTransform,350,0,175,42);refreshers.Add(()=>{if(current)current.text=app.Hotkey.ToString();});
            var button=ActionButton(row,L.T("settings.change_key"),()=>{BindingFeatureId=null;BindingKey=true;bindAfterFrame=Time.frameCount+1;bindText.text=L.T("settings.press_a_key");},L.T("settings.change_key.help"));
            var rect=(RectTransform)button.transform;rect.anchorMin=rect.anchorMax=new Vector2(1,.5f);rect.pivot=new Vector2(1,.5f);rect.sizeDelta=new Vector2(210,34);bindText=button.GetComponentInChildren<TextMeshProUGUI>();
            Section(page,L.T("settings.section.interface"));
            SelectRow(page,L.T("settings.language"),()=>MenuLanguage.SettingsLabel(d.language),()=>LanguageChoices(d));
            Note(page,L.T("settings.language.note"));
            if (LocaleCodes.UsesCyrillic(MenuLanguage.EffectiveCode))
                Note(page,L.T("settings.language.font_preview"));
            SelectRow(page,L.T("settings.menu_layout"),()=>d.menuSectionFolds?L.T("settings.menu_layout.folds"):L.T("settings.menu_layout.full"),()=>new[]{
                Choice(L.T("settings.menu_layout.folds"),()=>{d.menuSectionFolds=true;rebuild=true;}),
                Choice(L.T("settings.menu_layout.full"),()=>{d.menuSectionFolds=false;rebuild=true;})
            });
            Note(page,L.T("settings.menu_layout.help"));
            SliderRow(page,L.T("settings.menu_scale"),()=>app.UiScale,v=>app.UiScale=v,.7f,1.6f,L.T("settings.menu_scale.help"));
            ToggleRow(page,L.T("settings.tooltips"),()=>d.tooltips,v=>{d.tooltips=v;ClearHover();},L.T("settings.tooltips.help"));
            ToggleRow(page,L.T("settings.block_inputs"),()=>d.blockGameInputs,v=>d.blockGameInputs=v,L.T("settings.block_inputs.help"));
            ActionRow(page,L.T("settings.window_position"),L.T("settings.window_position.action"),()=>position=new Vector2(-1,-1),L.T("settings.window_position.help"));
            Section(page,L.T("settings.section.saved"));
            ToggleRow(page,L.T("settings.save_on_close"),()=>d.autoSave,v=>d.autoSave=v,L.T("settings.save_on_close.help"));
            ToggleRow(page,L.T("settings.remember_character"),()=>d.rememberPlayer,v=>d.rememberPlayer=v,L.T("settings.remember_character.help"));
            ActionRow(page,L.T("settings.current_settings"),L.T("settings.current_settings.action"),()=>app.Settings.Save(),L.T("settings.current_settings.help"));
            ActionRow(page,L.T("settings.saved_settings"),L.T("settings.saved_settings.action"),()=>{app.Settings.Load();if(app.Settings.Data.rememberPlayer)app.Settings.ApplyCharacterToggles();rebuild=true;},L.T("settings.saved_settings.help"));
            DangerRow(page,L.T("settings.defaults"),L.T("settings.defaults.action"),L.T("settings.defaults.warning"),()=>{app.Settings.Reset();rebuild=true;});
            Section(page,L.T("settings.section.locales"));
            ActionRow(page,L.T("settings.export_locales"),L.T("settings.export_locales.action"),()=>{
                try { app.Report(L.Tf("toast.locales_exported", L.ExportForCommunity())); }
                catch (Exception ex) { app.Report(ex.Message); }
            },L.T("settings.export_locales.help"));
            ActionRow(page,L.T("settings.reload_locales"),L.T("settings.reload_locales.action"),()=>{
                L.Reload(); MenuLanguage.RefreshEffective(notify:false); rebuild=true;
                app.Report(L.T("toast.locales_reloaded"));
            },L.T("settings.reload_locales.help"));
            Section(page,L.T("settings.section.achievements"));
            DangerRow(page,L.T("settings.cheat_flag"),L.T("settings.cheat_flag.action"),L.T("settings.cheat_flag.warning"),()=>app.ClearCharacterCheatFlag());
            Section(page,L.T("settings.section.credits"));
            Note(page,"Valheaven version " + Plugin.ProductVersion);
            Note(page,L.T("settings.credits.note"));
        }
                private void EventsPage(RectTransform page)
        {
            var p = app.Phase;
            Section(page, L.T("ui.raid_controller"));
            ToggleRow(page, L.T("ui.never_get_raided"), () => p.DisableRaids, v => p.DisableRaids = v, L.T("ui.prevents_random_events_base_raids"));
            
            Section(page, L.T("ui.active_event"));
            if (RandEventSystem.instance != null && RandEventSystem.HaveActiveEvent())
            {
                var active = RandEventSystem.instance.GetCurrentRandomEvent();
                ActionRow(page, Localization.instance.Localize(active.m_name), L.T("ui.stop"), () => {
                    RandEventSystem.instance.ResetRandomEvent();
                });
            }
            else
            {
                Note(page, L.T("ui.no_active_event"));
            }
            
            Section(page, L.T("ui.trigger_specific_event"));
            if (RandEventSystem.instance != null)
            {
                var evts = RandEventSystem.instance.m_events;
                if (evts != null)
                {
                    foreach (var evt in evts)
                    {
                        if (evt == null || string.IsNullOrEmpty(evt.m_name)) continue;
                        ActionRow(page, Localization.instance.Localize(evt.m_name), L.T("ui.trigger"), () => {
                            RandEventSystem.instance.SetRandomEventByName(evt.m_name, Player.m_localPlayer.transform.position);
                        });
                    }
                }
            }
            else
            {
                Note(page, L.T("ui.join_a_world_to_see"));
            }
        }
    }
}
