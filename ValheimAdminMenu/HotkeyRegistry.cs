using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimAdminMenu
{
    internal enum HotkeyKind { Toggle, Action }

    internal sealed class HotkeyFeature
    {
        internal string Id;
        internal string LabelKey;
        internal string Label => L.T(LabelKey);
        internal HotkeyKind Kind;
        internal Func<bool> Get;
        internal Action<bool> Set;
        internal Action Run;
        internal KeyCode Key;
    }

    [Serializable]
    public sealed class HotkeyBindData
    {
        public string id;
        public int key;
    }

    /// <summary>Bindable toggles and actions for hotkeys.</summary>
    internal static class HotkeyRegistry
    {
        private static readonly List<HotkeyFeature> features = new List<HotkeyFeature>();
        private static bool built;
        private static int boundCount;

        internal static IReadOnlyList<HotkeyFeature> All
        {
            get { Ensure(); return features; }
        }

        internal static void Ensure()
        {
            if (built) return;
            var app = Plugin.Instance;
            if (app == null) return;
            built = true;

            Toggle("god", "hotkey.god",
                () => Player.m_localPlayer && Player.m_localPlayer.InGodMode(),
                v => Safe(() => app.Features.God(v)));
            Toggle("ghost", "hotkey.ghost",
                () => Player.m_localPlayer && Player.m_localPlayer.InGhostMode(),
                v => Safe(() => app.Features.Ghost(v)));
            Toggle("fly", "hotkey.fly",
                () => Player.m_localPlayer && Player.m_localPlayer.IsDebugFlying(),
                v => Safe(() => app.Features.Fly(v)));
            Toggle("noclip", "hotkey.noclip", () => app.Phase.Noclip, v => app.Phase.Noclip = v);
            Toggle("stamina", "hotkey.stamina", () => app.Features.Stamina, v => app.Features.Stamina = v);
            Toggle("eitr", "hotkey.eitr", () => app.Features.Eitr, v => app.Features.Eitr = v);
            Toggle("weight", "hotkey.weight", () => app.Features.Weight, v => app.Features.Weight = v);
            Toggle("noCost", "hotkey.noCost",
                () => Player.m_localPlayer && Player.m_localPlayer.PlacementCostDisabled,
                v => Safe(() => app.Features.NoCost(v)));
            Toggle("freeCraft", "hotkey.freeCraft", () => app.Phase.FreeCraft, v => app.Phase.FreeCraft = v);
            Toggle("fastCraft", "hotkey.fastCraft", () => app.Features.FastCraft, v => app.Features.FastCraft = v);
            Toggle("craftFromChests", "hotkey.craftFromChests", () => app.Phase.CraftFromChests, v => app.Phase.CraftFromChests = v);
            Toggle("itemMagnet", "hotkey.itemMagnet", () => app.Phase.ItemMagnet, v => app.Phase.ItemMagnet = v);
            Toggle("oneHitKill", "hotkey.oneHitKill", () => app.Phase.OneHitKill || app.Phase.OneHitCreatures, v => { app.Phase.OneHitCreatures = v; app.Phase.OneHitKill = v; });
            Toggle("oneHitStructures", "hotkey.oneHitStructures", () => app.Phase.OneHitStructures, v => app.Phase.OneHitStructures = v);
            Toggle("noFallDamage", "hotkey.noFallDamage", () => app.Phase.NoFallDamage, v => app.Phase.NoFallDamage = v);
            Toggle("boatFly", "hotkey.boatFly", () => app.Phase.BoatFly, v => app.Phase.BoatFly = v);
            Toggle("hideHud", "hotkey.hideHud", () => app.Phase.HideHud, v => app.Phase.HideHud = v);
            Toggle("freecam", "hotkey.freecam", () => app.Phase.Freecam || (GameCamera.instance&&GameCamera.InFreeFly()), v => Safe(() => Release250.SetFreecam(v)));
            Toggle("freecamBuild", "hotkey.freecamBuild", () => app.Phase.FreecamBuild, v => app.Phase.FreecamBuild = v);
            Toggle("meleeTargetLock", "hotkey.meleeTargetLock", () => app.Phase.MeleeTargetLock, v => app.Phase.MeleeTargetLock = v);
            Toggle("flyingMount", "hotkey.flyingMount", () => app.Phase.FlyingMount, v => app.Phase.FlyingMount = v);
            Toggle("mountAny", "hotkey.mountAny", () => app.Phase.MountAnyCreature, v => app.Phase.MountAnyCreature = v);
            Toggle("mountNext", "hotkey.mountNext", () => Release250.MountNextArmed, v => Release250.MountNextArmed = v);
            Toggle("photoMode", "hotkey.photoMode", () => app.Phase.PhotoMode, v => Safe(() => Release250.SetPhotoMode(v)));
            Toggle("tpToPing", "hotkey.tpToPing", () => app.Phase.TpToPing, v => app.Phase.TpToPing = v);
            Toggle("autoPickupFilter", "hotkey.autoPickupFilter", () => app.Phase.AutoPickupFilter, v => app.Phase.AutoPickupFilter = v);
            Toggle("repairHammerAoe", "hotkey.repairHammerAoe", () => app.Phase.RepairHammerAoe, v => app.Phase.RepairHammerAoe = v);
            Toggle("esp", "hotkey.esp", () => app.Overlay.Enabled, v => app.Overlay.Enabled = v);

            Action("heal", "hotkey.heal", () => Safe(() => app.Features.Heal()));
            Action("depositChests", "hotkey.depositChests", () => Safe(() => ChestInventory.DepositNearby(app)));
            Action("withdrawChests", "hotkey.withdrawChests", () => Safe(() => ChestInventory.WithdrawNearby(app)));
            Action("depositJunk", "hotkey.depositJunk", () => Safe(Release250.DepositJunk));
            Action("revealMap", "hotkey.revealMap", () => Safe(() => app.Features.Explore()));
            Action("repairNearby", "hotkey.repairNearby", () => Safe(() => app.Phase.RepairNearby()));
            Action("tpCrosshair", "hotkey.tpCrosshair", () => Safe(FeatureSuite.TeleportToCrosshair));
            Action("tpTombstone", "hotkey.tpTombstone", () => Safe(Release250.TeleportToTombstone));
            Action("sitAnywhere", "hotkey.sitAnywhere", () => Safe(Release250.SitAnywhere));
            Action("copyCornerA", "hotkey.copyCornerA", () => Safe(Release250.SetCornerA));
            Action("copyCornerB", "hotkey.copyCornerB", () => Safe(Release250.SetCornerB));
            Action("copyVolume", "hotkey.copyVolume", () => Safe(Release250.CopyStructureVolume));
            Action("pasteStructure", "hotkey.pasteStructure", () => Safe(() => Release250.PasteStructure(false)));
            Action("clearVfx", "hotkey.clearVfx", () => Safe(() => VfxSuite.ClearAll(true)));
            Action("forceGrow", "hotkey.forceGrow", () => Safe(Release250.GrowNearbyPlants));
        }

        private static void Toggle(string id, string labelKey, Func<bool> get, Action<bool> set)
        {
            features.Add(new HotkeyFeature { Id = id, LabelKey = labelKey, Kind = HotkeyKind.Toggle, Get = get, Set = set, Key = KeyCode.None });
        }

        private static void Action(string id, string labelKey, Action run)
        {
            features.Add(new HotkeyFeature { Id = id, LabelKey = labelKey, Kind = HotkeyKind.Action, Run = run, Key = KeyCode.None });
        }

        private static void Safe(Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                if (Plugin.Instance) Plugin.Instance.Report(ex.GetBaseException().Message);
            }
        }

        internal static HotkeyFeature Find(string id) => All.FirstOrDefault(f => f.Id == id);

        internal static string Bind(string id, KeyCode key, KeyCode menuKey)
        {
            Ensure();
            var feature = Find(id);
            if (feature == null) return L.T("toast.unknown_feature");
            if (!Rules.ValidHotkey(key)) return L.T("toast.invalid_key");
            if (key == menuKey) return L.T("toast.conflicts_menu");
            var clash = features.FirstOrDefault(f => f.Id != id && f.Key == key);
            if (clash != null) return L.Tf("toast.conflicts_feature", clash.Label);
            if (feature.Key == KeyCode.None && key != KeyCode.None) boundCount++;
            else if (feature.Key != KeyCode.None && key == KeyCode.None) boundCount = Math.Max(0, boundCount - 1);
            feature.Key = key;
            return null;
        }

        internal static void Clear(string id)
        {
            var feature = Find(id);
            if (feature == null) return;
            if (feature.Key != KeyCode.None) boundCount = Math.Max(0, boundCount - 1);
            feature.Key = KeyCode.None;
        }

        internal static void ApplyBinds(HotkeyBindData[] binds)
        {
            Ensure();
            foreach (var f in features) f.Key = KeyCode.None;
            boundCount = 0;
            if (binds == null) return;
            foreach (var bind in binds)
            {
                if (bind == null || string.IsNullOrEmpty(bind.id)) continue;
                var feature = Find(bind.id);
                if (feature == null) continue;
                var key = (KeyCode)bind.key;
                if (!Rules.ValidHotkey(key)) continue;
                feature.Key = key;
                boundCount++;
            }
        }

        internal static HotkeyBindData[] CaptureBinds()
        {
            Ensure();
            return features.Where(f => f.Key != KeyCode.None)
                .Select(f => new HotkeyBindData { id = f.Id, key = (int)f.Key })
                .ToArray();
        }

        internal static void Poll(Plugin app)
        {
            Ensure();
            if (boundCount <= 0) return;
            if (app.Menu != null && (app.Menu.BindingKey || app.Menu.EditingText)) return;
            for (int i = 0; i < features.Count; i++)
            {
                var feature = features[i];
                if (feature.Key == KeyCode.None || !Rules.ValidHotkey(feature.Key)) continue;
                if (feature.Key == app.Hotkey) continue;
                if (!Input.GetKeyDown(feature.Key)) continue;
                if (feature.Kind == HotkeyKind.Toggle && feature.Get != null && feature.Set != null)
                {
                    bool next = !feature.Get();
                    feature.Set(next);
                    app.Report(feature.Label + ": " + (next ? "ON" : L.T("ui.off")));
                }
                else if (feature.Kind == HotkeyKind.Action && feature.Run != null)
                {
                    app.Enqueue(feature.Run);
                }
            }
        }
    }
}
