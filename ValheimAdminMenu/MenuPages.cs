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
        private string powerName="", locationName="";
        private static KeyValuePair<string,Action> Choice(string label,Action action)=>new KeyValuePair<string,Action>(label,action);
        private void CharacterPage(RectTransform page)
        {
            if(!RequireCharacter(page))return;
            var f=app.Features;
            Section(page,"Abilities");
            ToggleRow(page,"God mode",()=>Player.m_localPlayer && Player.m_localPlayer.InGodMode(),v=>Run(()=>f.God(v)),"Prevents damage to your character.");
            ToggleRow(page,"Unlimited stamina",()=>f.Stamina,v=>f.Stamina=v,"Keeps stamina full while running, jumping, and attacking.");
            ToggleRow(page,"Flight",()=>Player.m_localPlayer && Player.m_localPlayer.IsDebugFlying(),v=>Run(()=>f.Fly(v)),"Fly with movement keys. Jump rises; crouch descends.");
            ToggleRow(page,"Ghost mode",()=>Player.m_localPlayer && Player.m_localPlayer.InGhostMode(),v=>Run(()=>f.Ghost(v)),"Stops creatures from detecting you.");
            ToggleRow(page,"Super jump",()=>app.Expansion.SuperJump,v=>app.Expansion.SuperJump=v,"Increases jump strength.");
            SliderRow(page,"Jump strength",()=>app.Settings.Data.jumpMultiplier,v=>app.Settings.Data.jumpMultiplier=v,1,8,"Multiplier applied to jump force.");
            ToggleRow(page,"Instant mining & chopping",()=>app.Expansion.InstantHarvest,v=>app.Expansion.InstantHarvest=v,"Breaks struck ore sections, trees, and logs with one hit.");
            Section(page,"Building & inventory");
            ToggleRow(page,"Free building",()=>Player.m_localPlayer && Player.m_localPlayer.PlacementCostDisabled,v=>Run(()=>f.NoCost(v)),"Place building pieces without spending materials.");
            ToggleRow(page,"Unlimited carry weight",()=>f.Weight,v=>f.Weight=v,"Removes the carry-weight limit. Inventory slots stay the same.");
            ToggleRow(page,"Instant crafting",()=>f.FastCraft,v=>f.FastCraft=v,"Skips the crafting timer.");
            ToggleRow(page,"Unrestricted portals",()=>f.PortalItems,v=>f.PortalItems=v,"Take restricted items through portals.");
            ToggleRow(page,"Extended interaction reach",()=>f.DistanceEnabled,v=>f.DistanceEnabled=v,"Use the reach distance set below.");
            SliderRow(page,"Interaction reach (m)",()=>f.Distance,v=>f.Distance=v,1,50,"Distance for interacting with objects.");
            DangerRow(page,"Discover every recipe","Unlock","Permanently discovers all enabled recipes and building pieces. Crafting still uses its required station and materials.",app.Expansion.UnlockRecipes);
            Section(page,"Recovery");
            ActionRow(page,"Health","Restore",f.Heal,"Fully restores your health.");
            ActionRow(page,"Guardian Power","Activate",f.Power,"Activates your equipped Guardian Power.");
            SelectRow(page,"Equip Guardian Power",()=>string.IsNullOrEmpty(powerName)?"Choose power":PowerLabel(powerName),()=>ObjectDB.instance.m_StatusEffects.Where(e=>e && e.name.StartsWith("GP_",StringComparison.Ordinal)).OrderBy(e=>e.name).Select(e=>{string name=e.name;return Choice(PowerLabel(name),()=>powerName=name);}));
            ActionRow(page,"Selected power","Equip",()=>app.Expansion.EquipPower(powerName),"Equips the chosen power and clears its cooldown.");
            Section(page,"Skills");
            ToggleRow(page,"Fast training",()=>app.Expansion.FastTraining,v=>app.Expansion.FastTraining=v,"Earn one skill level whenever that skill gains XP, up to level 100.");
            SelectRow(page,"Skill",()=>selectedSkill.ToString(),()=>Player.m_localPlayer.GetSkills().m_skills.OrderBy(d=>d.m_skill.ToString()).Where(d=>d.m_skill!=Skills.SkillType.None && d.m_skill!=Skills.SkillType.All)
                .Select(d=>{var skill=d.m_skill;return Choice(skill.ToString(),()=>{selectedSkill=skill;});}));
            var current=Label(page,"",15,Muted);Fixed(current.rectTransform,26);
            Action refresh=()=>
            {
                if(!current || !Player.m_localPlayer)return;
                var entry=Player.m_localPlayer.GetSkills().GetSkillList().FirstOrDefault(s=>s.m_info!=null && s.m_info.m_skill==selectedSkill);
                current.text="Current level: "+(entry==null?"0":entry.m_level.ToString("0.##",CultureInfo.InvariantCulture));
            };refresh();refreshers.Add(refresh);
            Field(page,"New level",skillLevel,v=>skillLevel=v,"An exact level from 0 to 100. Decimals are accepted.",8);
            ActionRow(page,"Apply to selected skill","Set level",()=>f.SetSkill(selectedSkill,Number(skillLevel,"Level",0,100)),"Raises or lowers the skill to the entered level.");
            
        }
        private void Coordinates(RectTransform page,bool remote)
        {
            Section(page,"Coordinates");
            var fx=Field(page,"X · East / west",x,v=>x=v,"East / west coordinate.",16);
            var fy=Field(page,"Y · Altitude",y,v=>y=v,"Height above the world origin.",16);
            var fz=Field(page,"Z · North / south",z,v=>z=v,"North / south coordinate.",16);
            ActionRow(page,"Use current position","Copy",()=>
            {
                var p=app.RequirePlayer().transform.position;
                x=p.x.ToString("0.##",CultureInfo.InvariantCulture);y=p.y.ToString("0.##",CultureInfo.InvariantCulture);z=p.z.ToString("0.##",CultureInfo.InvariantCulture);
                if(fx)fx.SetTextWithoutNotify(x);if(fy)fy.SetTextWithoutNotify(y);if(fz)fz.SetTextWithoutNotify(z);
            },"Copies your position into the coordinate fields.");
            ActionRow(page,remote?"Move selected player":"Move to coordinates","Teleport",()=>
            {
                var pos=new Vector3(Number(x,"X",-20000,20000),Number(y,"Y",-10000,10000),Number(z,"Z",-20000,20000));
                app.Features.Teleport(pos);
            },"Moves to the entered coordinates. Check the altitude before teleporting.");
        }
        private Vector3? marker;
        private ZDOID playerDestination;
        private string playerDestinationName="Choose player";
        private static string PowerLabel(string name){var effect=ObjectDB.instance.m_StatusEffects.FirstOrDefault(e=>e && e.name==name);return effect?Localization.instance.Localize(effect.m_name):name;}
        private string markerName="Choose marker";
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
            Section(page,"Brush");
            SliderRow(page,"Radius (m)",()=>app.World.BrushRadius,v=>app.World.BrushRadius=v,1,20,"Size of the terrain brush around your feet.");
            SliderRow(page,"Height adjustment (m)",()=>app.World.BrushAmount,v=>app.World.BrushAmount=v,.1f,8,"Amount to raise or lower the terrain.");
            ToggleRow(page,"Square brush",()=>app.World.Square,v=>app.World.Square=v,"Switches from a circular brush to a square brush.");
            Section(page,"Shape terrain");
            foreach(var op in new[]{"level","raise","lower","smooth","reset"})
            {
                string operation=op;string label=char.ToUpperInvariant(op[0])+op.Substring(1);
                DangerRow(page,label+" terrain",label,op=="reset"?"Removes terrain changes inside the brush. There is no undo.":label+"s terrain around your feet. Buildings can be affected. There is no undo.",()=>app.World.Terrain(operation));
            }
        }
        private static string LocationLabel(string name)
        {
            string friendly;
            switch(name.ToLowerInvariant())
            {
                case "eikthyrnir":friendly="Eikthyr altar";break;
                case "gdking":friendly="The Elder altar";break;
                case "bonemass":friendly="Bonemass altar";break;
                case "dragonqueen":friendly="Moder altar";break;
                case "goblinking":friendly="Yagluth altar";break;
                case "mistlands_dvergrboss":friendly="The Queen";break;
                case "fader":case "ashlands_fader":friendly="Fader altar";break;
                case "vendor_blackforest":friendly="Haldor merchant";break;
                case "hildir_camp":friendly="Hildir merchant";break;
                case "bogwitch_camp":friendly="Bog Witch merchant";break;
                default:return name;
            }
            return friendly+" · "+name;
        }
        private void WorldPage(RectTransform page)
        {
            if(!RequireCharacter(page))return;
            Section(page,"Daylight");
            ToggleRow(page,"Always daytime",()=>app.Settings.Data.alwaysDay,v=>app.Settings.Data.alwaysDay=v,"Holds daylight at noon. World time continues.");
            Coordinates(page,false);MarkerSelector(page,false);
            Section(page,"Player destination");
            SelectRow(page,"Visible player",()=>playerDestinationName,()=>ZNet.instance.GetPlayerList().Where(p=>p.m_publicPosition).Select(p=>{var id=p.m_characterID;var name=p.m_name;return Choice(name,()=>{playerDestination=id;playerDestinationName=name;});}));
            ActionRow(page,"Travel to player","Teleport",()=>{
                var found=ZNet.instance.GetPlayerList().FirstOrDefault(p=>p.m_characterID==playerDestination && p.m_publicPosition);
                if(!found.m_publicPosition)throw new ArgumentException("Choose a player sharing their map position.");
                app.Features.Teleport(found.m_position+Vector3.up*2);
            },"Teleports you to the selected player's current map position.");
            Section(page,"Boss altars & world locations");
            SelectRow(page,"Location",()=>string.IsNullOrEmpty(locationName)?"Choose location":LocationLabel(locationName),()=>ZoneSystem.instance.m_locations.Where(l=>l.m_enable && !string.IsNullOrEmpty(l.m_prefabName)).Select(l=>l.m_prefabName).Distinct().OrderBy(n=>n).Select(n=>Choice(LocationLabel(n),()=>locationName=n)));
            ActionRow(page,"Nearest matching location","Teleport",()=>app.Expansion.TeleportToLocation(locationName),"Finds the nearest generated location. Search for an altar or trader by its location name.");
            Section(page,"Nearby objects");
            SliderRow(page,"Radius (m)",()=>app.World.Radius,v=>app.World.Radius=v,1,200,"Range for nearby creature and cleanup actions.");
            ActionRow(page,"Tame creatures","Tame",app.World.Tame,"Tames eligible creatures within the radius.");
            DangerRow(page,"Ground drops","Remove","Permanently deletes loose items within the radius.",app.World.DeleteDrops);
            DangerRow(page,"Your tombstones","Remove","Deletes your nearby tombstones and all items inside them.",app.World.RemoveTombstones);
            DangerRow(page,"Creatures","Kill","Kills creatures within the radius, including tamed animals.",app.World.KillEntities);
            Section(page,"Map");
            DangerRow(page,"Reveal the entire map","Reveal","Permanently reveals this world's map for your character. Previous exploration cannot be restored.",app.Features.Explore);
            Section(page,"Messages");
            Field(page,"Message",message,v=>message=v,"Text to send.",240);
            Field(page,"Display name",alias,v=>alias=v,"Name displayed beside your chat message.",48);
            ActionRow(page,"Center-screen announcement","Send",()=>app.Network.Send("event",-1,text:message),"Shows the message in yellow at the center of the screen.");
            ActionRow(page,"Chat message","Send",()=>app.Network.Send("chat",-1,text:message,alias:alias),"Sends the message with the entered display name.");
        }
        private void OverlayPage(RectTransform page)
        {
            var d=app.Settings.Data;var o=app.Overlay;
            ToggleRow(page,"Enable ESP",()=>o.Enabled,v=>o.Enabled=v,"Shows labels over nearby objects.");
            Section(page,"Show labels for");
            string[] names={"Players","Creatures","Pickables","Ore deposits","Ground drops"};
            for(int i=0;i<names.Length;i++){int category=i;ToggleRow(page,names[i],()=>o.Categories[category],v=>o.Categories[category]=v,"Shows labels for "+names[i].ToLowerInvariant()+".");}
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
        private void SettingsPage(RectTransform page)
        {
            var d=app.Settings.Data;
            Section(page,"Controls");
            var row=Row(page,46);var title=Label(row,"Open menu",17);Stretch(title.rectTransform,0,0,320,0);
            var current=Label(row,app.Hotkey.Value.ToString(),17,Gold);At(current.rectTransform,350,0,175,42);refreshers.Add(()=>{if(current)current.text=app.Hotkey.Value.ToString();});
            var button=ActionButton(row,"Change key",()=>{BindingKey=true;bindAfterFrame=Time.frameCount+1;bindText.text="Press a key…";},"Click, then press the new menu key. Escape cancels.");
            var rect=(RectTransform)button.transform;rect.anchorMin=rect.anchorMax=new Vector2(1,.5f);rect.pivot=new Vector2(1,.5f);rect.sizeDelta=new Vector2(210,34);bindText=button.GetComponentInChildren<TextMeshProUGUI>();
            Section(page,"Interface");
            SliderRow(page,"Menu scale",()=>app.UiScale.Value,v=>app.UiScale.Value=v,.7f,1.6f,"Changes menu size after you release the slider.");
            ToggleRow(page,"Tooltips",()=>d.tooltips,v=>{d.tooltips=v;ClearHover();},"Shows a short description when you rest the pointer on a control.");
            ActionRow(page,"Window position","Center",()=>position=new Vector2(-1,-1),"Centers the menu on the screen.");
            Section(page,"Saved preferences");
            ToggleRow(page,"Save on close",()=>d.autoSave,v=>d.autoSave=v,"Saves your settings when you close the menu.");
            ToggleRow(page,"Remember character toggles",()=>d.rememberPlayer,v=>d.rememberPlayer=v,"Restores your saved abilities when you enter a world.");
            ActionRow(page,"Current settings","Save",()=>app.Settings.Save(),"Saves menu, ESP, and feature settings.");
            ActionRow(page,"Saved settings","Reload",()=>{app.Settings.Load();rebuild=true;},"Replaces current settings with your last save.");
            DangerRow(page,"Default settings","Reset","Resets menu preferences and returns the open key to F8. World and character changes are kept.",()=>{app.Settings.Reset();rebuild=true;});
        }
    }
}

