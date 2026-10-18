using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace ValheimAdminMenu
{
    [Serializable]
    internal sealed class SettingsData
    {
        public int version = 2, hotkey = (int)KeyCode.F8, espFont = 11, espMax = 70;
        public bool alwaysDay,fastTraining,superJump,infiniteDurability,instantHarvest;
        public float jumpMultiplier=2;
        public bool autoSave = true, rememberPlayer, tooltips = true;
        public float scale = 1, windowX = -1, windowY = -1;
        public bool esp, autoPin, espDistance = true, espBackground, espOutline = true, espAvoidOverlap = true, espHideInMenu = true;
        public float espRadius = 150, espOpacity = .3f, espSpacing = 3, espOffset = 0, espRefresh = 1;
        public bool[] categories = { true, true, false, true, false };
        public Color[] colors = { new Color(.65f,.87f,1), new Color(1,.62f,.49f), new Color(.65f,.9f,.66f), new Color(1,.82f,.43f), new Color(.83f,.74f,1) };
        public float worldRadius = 30, brushRadius = 5, brushAmount = 1, interactionDistance = 10;
        public bool square, god, ghost, fly, noCost, stamina, weight, fastCraft, portal, interaction;
        internal void Validate()
        {
            if (!Rules.ValidHotkey((KeyCode)hotkey)) hotkey = (int)KeyCode.F8;
            jumpMultiplier=Clamp(jumpMultiplier,1,8,2);
            scale = Clamp(scale,.7f,1.6f,1); windowX = Clamp(windowX,-1,10000,-1); windowY = Clamp(windowY,-1,10000,-1);
            espFont = Mathf.Clamp(espFont,8,24); espMax = Mathf.Clamp(espMax,10,200);
            espRadius = Clamp(espRadius,10,500,150); espOpacity = Clamp(espOpacity,0,1,.3f);
            espSpacing = Clamp(espSpacing,0,20,3); espOffset = Clamp(espOffset,-60,60,0); espRefresh = Clamp(espRefresh,.25f,5,1);
            worldRadius = Clamp(worldRadius,1,200,30); brushRadius = Clamp(brushRadius,1,20,5); brushAmount = Clamp(brushAmount,.1f,8,1);
            interactionDistance = Clamp(interactionDistance,1,50,10);
            if (categories == null || categories.Length != 5) categories = new SettingsData().categories;
            if (colors == null || colors.Length != 5) colors = new SettingsData().colors;
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(Clamp(colors[i].r,0,1,1),Clamp(colors[i].g,0,1,1),Clamp(colors[i].b,0,1,1),1);
        }
        private static float Clamp(float v,float min,float max,float fallback) => Rules.Finite(v) ? Mathf.Clamp(v,min,max) : fallback;
    }
    internal sealed class SavedSettings
    {
        private readonly Plugin app;
        internal SettingsData Data = new SettingsData();
        internal bool PendingPlayer = true;
        internal string PathName => Path.Combine(Paths.ConfigPath,"Valheaven.settings.json");
        internal SavedSettings(Plugin app) { this.app = app; }
        internal void Load()
        {
            var loaded = new SettingsData { version=1, hotkey = (int)app.Hotkey.Value, scale = app.UiScale.Value };
            if (File.Exists(PathName)) JsonUtility.FromJsonOverwrite(File.ReadAllText(PathName),loaded);
            if(loaded.version<2){loaded.version=2;loaded.windowX=loaded.windowY=-1;}
            loaded.Validate(); Data = loaded; Apply(); app.Report("Valheaven settings loaded.");
        }
        internal void Apply()
        {
            Data.Validate(); app.Hotkey.Value = (KeyCode)Data.hotkey; app.UiScale.Value = Data.scale;
            app.Menu.SetPosition(Data.windowX,Data.windowY);
            app.Overlay.Enabled = Data.esp; app.Overlay.AutoPin = Data.autoPin; app.Overlay.Radius = Data.espRadius;
            app.Overlay.MaxLabels = Data.espMax; app.Overlay.Categories = (bool[])Data.categories.Clone();
            app.World.Radius = Data.worldRadius; app.World.BrushRadius = Data.brushRadius; app.World.BrushAmount = Data.brushAmount; app.World.Square = Data.square;
            app.Features.Distance = Data.interactionDistance; PendingPlayer = true; app.Overlay.Clear();
        }
        internal void ApplyPlayer()
        {
            var p = Player.m_localPlayer;
            if (!PendingPlayer || !p || p.IsDead() || p.IsTeleporting()) return;
            PendingPlayer = false;
            if (!Data.rememberPlayer) return;
            var f = app.Features;
            app.Expansion.FastTraining=Data.fastTraining;app.Expansion.SuperJump=Data.superJump;app.Expansion.Durability=Data.infiniteDurability;app.Expansion.InstantHarvest=Data.instantHarvest;
            f.God(Data.god); f.Ghost(Data.ghost); f.Fly(Data.fly); f.NoCost(Data.noCost);
            f.Stamina = Data.stamina; f.Weight = Data.weight; f.FastCraft = Data.fastCraft; f.PortalItems = Data.portal;
            f.DistanceEnabled = Data.interaction; f.Distance = Data.interactionDistance;
        }
        internal void Save(bool notify = true)
        {
            Data.hotkey = (int)app.Hotkey.Value; Data.scale = app.UiScale.Value;
            var pos = app.Menu.Position; Data.windowX = pos.x; Data.windowY = pos.y;
            var o = app.Overlay; Data.esp = o.Enabled; Data.autoPin = o.AutoPin; Data.espRadius = o.Radius; Data.espMax = o.MaxLabels; Data.categories = (bool[])o.Categories.Clone();
            Data.worldRadius = app.World.Radius; Data.brushRadius = app.World.BrushRadius; Data.brushAmount = app.World.BrushAmount; Data.square = app.World.Square;
            Data.interactionDistance = app.Features.Distance;
            if (Player.m_localPlayer)
            {
                var p = Player.m_localPlayer; var f = app.Features;
                Data.fastTraining=app.Expansion.FastTraining;Data.superJump=app.Expansion.SuperJump;Data.infiniteDurability=app.Expansion.Durability;Data.instantHarvest=app.Expansion.InstantHarvest;
                Data.god = p.InGodMode(); Data.ghost = p.InGhostMode(); Data.fly = p.IsDebugFlying(); Data.noCost = p.PlacementCostDisabled;
                Data.stamina = f.Stamina; Data.weight = f.Weight; Data.fastCraft = f.FastCraft; Data.portal = f.PortalItems; Data.interaction = f.DistanceEnabled;
            }
            Data.Validate();
            Directory.CreateDirectory(Paths.ConfigPath);
            string temp = PathName + ".tmp";
            File.WriteAllText(temp,JsonUtility.ToJson(Data,true));
            if (File.Exists(PathName)) File.Replace(temp,PathName,PathName + ".bak"); else File.Move(temp,PathName);
            app.Config.Save(); if (notify) app.Report("Settings saved.");
        }
        internal void Reset() { Data = new SettingsData(); Apply(); app.Report("Defaults restored. Save settings to keep them."); }
    }
}
