using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ValheimAdminMenu.Validation")]

namespace ValheimAdminMenu
{
    /// <summary>Standalone Unity host (no BepInEx). Booted from <see cref="Valheaven.Entrypoint.Start"/>.</summary>
    public sealed class Plugin : MonoBehaviour
    {
        public const string Id = "local.valheim.adminmenu";
        public const string ProductVersion = "3.0.5";
        /// <summary>Steam 1.0.7+ (checked through 1.0.20). Reflection.Check is the real gate.</summary>
        private const int MinSupportedPatch = 7;
        internal static Plugin Instance;
        // While the menu is open, keep capturing even across brief DX11 exclusive-fullscreen focus blips.
        internal static bool Capturing => Instance && (Instance.Open || (Time.frameCount <= Instance.blockThroughFrame && Application.isFocused));
        internal static bool BlockingInput => Capturing && Instance.Settings != null && Instance.Settings.Data.blockGameInputs;
        internal readonly Queue<Action> Commands = new Queue<Action>();
        internal KeyCode Hotkey = KeyCode.F8;
        internal SavedSettings Settings;
        internal float UiScale = 1f;
        internal bool Open;
        internal string Status = "Ready. F8 opens and closes the menu.";
        internal PlayerFeatures Features;
        internal ExpansionFeatures Expansion;
        internal PhaseFeatures Phase;
        internal ClientPlayers Network;
        internal WorldTools World;
        internal Overlay Overlay;
        internal MenuView Menu;
        private Harmony harmony;
        private CursorLockMode priorLock;
        private bool priorRequested;
        private bool priorCursorVisible;
        private Player lastPlayer;
        private bool compatible;
        private int blockThroughFrame = -1;
        private bool exclusiveHinted;
        private readonly List<BaseInputModule> blockedModules = new List<BaseInputModule>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Features = new PlayerFeatures(this);
            Expansion = new ExpansionFeatures(this);
            Phase = new PhaseFeatures(this);
            World = new WorldTools(this);
            Network = new ClientPlayers(this);
            Overlay = new Overlay(this);
            Menu = new MenuView(this);
            Settings = new SavedSettings(this);
            try { Settings.Load(); } catch (Exception ex) { AppLog.Warning("Settings load failed: " + ex.Message); Settings.Apply(); }
            var game = global::Version.CurrentVersion;
            compatible = game.m_major == 1 && game.m_minor == 0 && game.m_patch >= MinSupportedPatch;
            if (!compatible)
            {
                AppLog.Error("This build supports Valheim 1.0." + MinSupportedPatch + "+. Feature hooks are disabled on " + game);
                Status = "Unsupported game version. Rebuild and verify signatures before enabling features.";
                return;
            }
            try
            {
                Reflection.Check();
                harmony = new Harmony(Id);
                harmony.PatchAll(typeof(Plugin).Assembly);
                SceneManager.activeSceneChanged += SceneChanged;
#if VALHEAVEN_UI_CHECK
                StartCoroutine(Menu.RuntimeCheck());
#endif
                AppLog.Info("Valheaven " + ProductVersion + " ready for Valheim " + game
                    + ". Hotkey: " + Hotkey
                    + ". GFX: " + SystemInfo.graphicsDeviceType
                    + " / " + Screen.fullScreenMode);
                if (Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen)
                    AppLog.Warning("Exclusive Fullscreen detected. If the menu doesn't appear, switch Valheim to Fullscreen Window (borderless).");
            }
            catch (Exception ex)
            {
                compatible = false;
                PlantEverything.Clear(); CreativeHammer.Clear(); ViewControls.Clear(); WaterControls.Clear(); MapControls.Clear();
                harmony?.UnpatchAll(Id);
                AppLog.Error(ex);
                Status = "Compatibility check failed. See Valheaven.log.";
            }
        }

        private void Update()
        {
            if (!compatible) return;
            try
            {
                bool capturingKey = Menu.BindingKey;
                Menu.PollKeybind();
                if (!capturingKey && !Menu.EditingText && Input.GetKeyDown(Hotkey)) SetOpen(!Open);
                if (!capturingKey && !Menu.EditingText) HotkeyRegistry.Poll(this);
                if (!capturingKey && Open && Input.GetKeyDown(KeyCode.Escape) && !Menu.DismissOverlay()) SetOpen(false);
                var player = Player.m_localPlayer;
                if (player != lastPlayer)
                {
                    SetOpen(false);
                    Settings.CaptureCharacterState = false;
                    Features.Restore();
                    Expansion.Reset();
                    Phase.Reset();
                    lastPlayer = player;
                    Settings.PendingPlayer = true;
                    Commands.Clear();
                    Overlay.Clear();
                }
                CharacterListGuard.Tick();
                Features.Tick(player);
                Expansion.Tick();
                Phase.Tick();
                PlantEverything.Tick();
                CreativeHammer.Tick();
                WaterControls.Tick();
                ViewControls.Tick();
                MapControls.Tick();
                Settings.ApplyPlayer();
                if (Open) BlockNativeUI();
                Network.Tick();
                Overlay.Tick();
                Menu.Tick();
                for (int i = 0; i < 4 && Commands.Count > 0; i++)
                {
                    try { Commands.Dequeue()(); }
                    catch (Exception ex) { Report(ex.GetBaseException().Message); AppLog.Error(ex); }
                }
            }
            catch (Exception ex)
            {
                Report("Update failed; menu closed. " + ex.GetBaseException().Message);
                SetOpen(false);
                AppLog.Error(ex);
            }
        }

        private void OnGUI()
        {
            if (!compatible || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            try { Overlay.Draw(); }
            catch (ExitGUIException) { throw; }
            catch (Exception ex) { SetOpen(false); Overlay.Enabled = false; AppLog.Error(ex); Report(L.T("ui.ui_closed_after_a_rendering")); }
            try { BowAim.DrawPredictMarker(); BowAim.DrawMeleeLock(); }
            catch (ExitGUIException) { throw; }
            catch (Exception ex) { AppLog.Warning("Aim marker draw failed: " + ex.Message); BowAim.ClearPredictMarker(); }
            try { Release250.DrawCopyVolume(); }
            catch (ExitGUIException) { throw; }
            catch (Exception ex) { AppLog.Warning("Copy volume draw failed: " + ex.Message); }
            try { PlacementHud.Draw(); }
            catch (ExitGUIException) { throw; }
            catch (Exception ex) { AppLog.Warning("Placement HUD draw failed: " + ex.Message); }
        }
        // DX11 exclusive fullscreen can blip focus when unlocking the cursor; closing here made F8 look dead.
        // Keep the menu open across focus changes and re-assert the cursor when focus returns.
        private void OnApplicationFocus(bool focused)
        {
            if (!focused || !Open) return;
            try { ReleaseCursor(); BlockNativeUI(); }
            catch (Exception ex) { AppLog.Warning("Focus restore failed: " + ex.Message); }
        }
        private void OnApplicationQuit()
        {
            if (Settings != null && Settings.Data.autoSave)
            {
                try { Settings.Save(false); }
                catch (Exception ex) { AppLog.Warning("Settings save failed: " + ex.Message); }
            }
        }
        private void SceneChanged(Scene a, Scene b)
        {
            SetOpen(false);
            if (Settings != null) { Settings.CaptureCharacterState = false; Settings.PendingPlayer = true; }
            Commands.Clear(); Features.Restore(); Expansion.Reset(); Phase.Reset(); Overlay.Clear(); Menu.ResetScene(); PlantEverything.Clear(); CreativeHammer.Clear(); ViewControls.Clear(); WaterControls.Clear(); MapControls.Clear();
        }
        internal void SetOpen(bool open)
        {
            if (Open == open) return;
            if (open)
            {
                priorLock = ZCursor.LockState;
                priorRequested = ZCursor.IsRequested;
                priorCursorVisible = Cursor.visible;
                try
                {
                    Menu.Show();
                    Open = true;
                    BlockNativeUI();
                    ReleaseCursor();
                    HintExclusiveFullscreen();
                }
                catch (Exception ex)
                {
                    Open = false;
                    try { Menu?.Hide(); } catch { /* ignore */ }
                    AppLog.Error(ex);
                    Report(L.T("ui.ui_closed_after_a_rendering"));
                }
            }
            else
            {
                if (Settings != null && Settings.Data.autoSave) { try { Settings.Save(false); } catch (Exception ex) { AppLog.Warning("Settings save failed: " + ex.Message); } }
                Menu?.StopBinding();
                Open = false;
                Menu?.Hide();
                blockThroughFrame = Time.frameCount + 1;
                foreach (var module in blockedModules) if (module) module.enabled = true;
                blockedModules.Clear();
                // Catch modules left disabled after a stale list / scene change.
                if (EventSystem.current)
                    foreach (var module in EventSystem.current.GetComponents<BaseInputModule>())
                        if (module) module.enabled = true;
                Menu?.RestoreInput();
                Menu?.CancelConfirmation();
                ZCursor.LockState = priorLock;
                if (priorRequested) ZCursor.Show(); else ZCursor.Hide();
                Cursor.visible = priorCursorVisible;
                if (GameCamera.instance) GameCamera.instance.UpdateMouseCapture();
            }
        }
        // The menu canvas may be the thing that's invisible, so say it through the game's own HUD once.
        private void HintExclusiveFullscreen()
        {
            if (exclusiveHinted || Screen.fullScreenMode != FullScreenMode.ExclusiveFullScreen) return;
            exclusiveHinted = true;
            try
            {
                if (MessageHud.instance)
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "Valheaven: if the menu is invisible, set Valheim to Fullscreen Window.");
            }
            catch { }
        }
        private void BlockNativeUI()
        {
            if (!EventSystem.current || Menu?.InputModule == null) return;
            foreach (var module in EventSystem.current.GetComponents<BaseInputModule>())
                if (module.enabled && module != Menu.InputModule) { if(!blockedModules.Contains(module))blockedModules.Add(module); module.enabled = false; }
        }
        internal static void ReleaseCursor() { ZCursor.LockState = CursorLockMode.None; ZCursor.Show(); Cursor.visible = true; }
        internal void Enqueue(Action action)
        {
            if (Commands.Count < 32) Commands.Enqueue(action);
            else Report(L.T("ui.command_queue_is_full_wait"));
        }
        internal void Report(string message) { Status = message; AppLog.Info(message); }
        internal Player RequirePlayer()
        {
            var player = Player.m_localPlayer;
            if (!player || player.IsDead() || player.IsTeleporting()) throw new InvalidOperationException("Wait for your character to finish spawning or teleporting.");
            return player;
        }
        internal void RequireWorld()
        {
            RequirePlayer();
            if (!ZNetScene.instance) throw new InvalidOperationException("Wait for the world to finish loading.");
        }
        private void OnDisable() { PlantEverything.Clear(); CreativeHammer.Clear(); ViewControls.Clear(); WaterControls.Clear(); MapControls.Clear(); SetOpen(false); Features?.Restore(); Expansion?.Reset(); Phase?.Reset(); Commands.Clear(); }
        private void OnDestroy()
        {
            SetOpen(false); Features?.Restore(); Expansion?.Reset(); Phase?.Reset(); Network?.Dispose(); Overlay?.Clear(); Menu?.Dispose();
            SceneManager.activeSceneChanged -= SceneChanged;
            PlantEverything.Clear(); CreativeHammer.Clear(); ViewControls.Clear(); WaterControls.Clear(); MapControls.Clear();
            harmony?.UnpatchAll(Id);
            if (Instance == this) Instance = null;
        }

        internal void ClearCharacterCheatFlag()
        {
            HealCheatFlags(notify: true);
        }
        /// <summary>Clears m_usedCheats and item m_cheated stamps (editor/load false positives).</summary>
        internal void HealCheatFlags(bool notify)
        {
            if (!Game.instance) { if (notify) throw new InvalidOperationException("Wait for the game to finish loading."); return; }
            var profile = Game.instance.GetPlayerProfile();
            if (profile == null) { if (notify) throw new InvalidOperationException("No character profile is loaded."); return; }
            profile.m_usedCheats = false;
            int cleared = 0;
            void ClearInv(Inventory inv)
            {
                if (inv == null) return;
                foreach (var item in inv.GetAllItems())
                {
                    if (item == null || !item.m_cheated) continue;
                    item.m_cheated = false;
                    cleared++;
                }
            }
            var player = Player.m_localPlayer;
            if (player) ClearInv(player.GetInventory());
            foreach (var container in WorldTools.Loaded<Container>())
            {
                try { ClearInv(container.GetInventory()); } catch { }
            }
            if (notify)
                Report("Cheat flags cleared (" + cleared + " inventory item" + (cleared == 1 ? "" : "s") + "). World modifiers are unchanged.");
            else if (cleared > 0)
                AppLog.Info("Auto-cleared character cheat flag and " + cleared + " cheated inventory item stamp(s).");
        }
    }

    internal static class Reflection
    {
        private static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>();
        private static readonly Dictionary<string, MethodInfo> Methods = new Dictionary<string, MethodInfo>();
        internal static FieldInfo Field(Type type, string name)
        {
            string key = type.FullName + "." + name;
            if (!Fields.TryGetValue(key, out var field))
            {
                for (var current = type; current != null && field == null; current = current.BaseType)
                    field = current.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                Fields[key] = field ?? throw new MissingFieldException(key);
            }
            return field;
        }
        internal static T Get<T>(object obj, string name) => (T)Field(obj.GetType(), name).GetValue(obj);
        internal static void Set(object obj, string name, object value) => Field(obj.GetType(), name).SetValue(obj, value);
        internal static T GetStatic<T>(Type type, string name) => (T)Field(type, name).GetValue(null);
        internal static MethodInfo Method(Type type, string name, params Type[] args)
        {
            args = args ?? Type.EmptyTypes;
            var keyBuilder = new System.Text.StringBuilder(type.FullName.Length + name.Length + 16 + args.Length * 24);
            keyBuilder.Append(type.FullName).Append('.').Append(name).Append('(');
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) keyBuilder.Append(',');
                keyBuilder.Append(args[i] != null ? args[i].FullName : "null");
            }
            keyBuilder.Append(')');
            string key = keyBuilder.ToString();
            if (Methods.TryGetValue(key, out var cached)) return cached;
            for (var current = type; current != null; current = current.BaseType)
            {
                var method = current.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
                if (method != null) { Methods[key] = method; return method; }
            }
            throw new MissingMethodException(type.Name, name);
        }
        internal static void Check()
        {
            Field(typeof(Player),"m_knownRecipes");Field(typeof(Player),"m_knownMaterial");
            Field(typeof(Attack),"m_character");Field(typeof(Attack),"m_weapon");
            Method(typeof(Inventory),"Changed",typeof(bool),typeof(bool));
            Method(typeof(EventSystem), "ChangeEventModule", typeof(BaseInputModule));
            Method(typeof(PlayerController), "TakeInput", typeof(bool));
            Method(typeof(InventoryGui), "UpdateRecipe", typeof(Player), typeof(float));
            Method(typeof(TerrainComp), "DoOperation", typeof(Vector3), typeof(Vector3), typeof(TerrainOp.Settings));
            Method(typeof(TerrainComp), "Save", typeof(bool));
            foreach (string field in new[] { "m_modifiedHeight", "m_levelDelta", "m_smoothDelta", "m_modifiedPaint", "m_paintMask", "m_lastOpPoint", "m_lastOpRadius" }) Field(typeof(TerrainComp), field);
            Field(typeof(Minimap), "m_pins"); Field(typeof(InventoryGui), "m_craftTimer");
            Method(typeof(Character), "CheckDeath");
            Method(typeof(Tameable), "Tame");
            Method(typeof(FishingFloat), "TryToHook");
            Method(typeof(FishingFloat), "ReturnBait");
            Method(typeof(FishingFloat), "GetOwner");
            Method(typeof(FishingFloat), "SetCatch", typeof(Fish));
            Method(typeof(Fish), "TestBate", typeof(FishingFloat));
            Method(typeof(Fish), "FindFloat", Type.EmptyTypes); // instance Fish.FindFloat(), not FishingFloat.FindFloat overloads
            Method(typeof(Fish), "Escape");
            Field(typeof(FishingFloat), "m_lineLength");
            Field(typeof(FishingFloat), "m_nibbleTime");
            Field(typeof(Fish), "m_fishingFloat");
            Method(typeof(Ship), "GetSpeedSetting");
            Method(typeof(Projectile), "Setup", typeof(Character), typeof(Vector3), typeof(float), typeof(HitData), typeof(ItemDrop.ItemData), typeof(ItemDrop.ItemData));
            Method(typeof(Skills), "OnDeath");
            Method(typeof(BaseAI), "UpdateAI", typeof(float));
            Method(typeof(Plant), "GetGrowTime");
            Method(typeof(Player), "UpdatePlacement", typeof(bool), typeof(float));
            Method(typeof(Character), "AddStaggerDamage", typeof(float), typeof(Vector3), typeof(HitData));
            Field(typeof(Character), "s_characters");
            Method(typeof(Player), "PlacePiece", typeof(Piece), typeof(Vector3), typeof(Quaternion), typeof(bool), typeof(bool));
        }
    }

    [HarmonyPatch(typeof(PlayerProfile), "get_s_bypassCheatChecks")]
    internal static class BypassCheatChecksPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (Plugin.Instance) __result = true;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class HealCheatsOnSpawnPatch
    {
        private static void Postfix(Player __instance)
        {
            if (!Plugin.Instance || __instance != Player.m_localPlayer) return;
            Plugin.Instance.HealCheatFlags(notify: false);
        }
    }
    [HarmonyPatch(typeof(Achievements), nameof(Achievements.CanGetAchievements), typeof(bool))]
    internal static class CanGetAchievementsPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (Plugin.Instance) __result = true;
        }
    }
    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class PlayerInputPatch { private static void Postfix(ref bool __result) { if (Plugin.BlockingInput) __result = false; } }
    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class CursorPatch
    {
        private static bool Prefix() { if (!Plugin.Capturing) return true; Plugin.ReleaseCursor(); return false; }
    }
    [HarmonyPatch]
    internal static class InputButtonsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "GetButton", "GetButtonDown", "GetButtonUp" }) yield return Reflection.Method(typeof(ZInput), name, typeof(string));
            foreach (string name in new[] { "GetKey", "GetKeyDown", "GetKeyUp" }) yield return Reflection.Method(typeof(ZInput), name, typeof(KeyCode), typeof(bool));
            foreach (string name in new[] { "GetMouseButton", "GetMouseButtonDown", "GetMouseButtonUp" }) yield return Reflection.Method(typeof(ZInput), name, typeof(int));
        }
        private static void Postfix(ref bool __result) { if (Plugin.BlockingInput) __result = false; }
    }
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseDelta))]
    internal static class MouseDeltaPatch { private static void Postfix(ref Vector2 __result) { if (Plugin.BlockingInput) __result = Vector2.zero; } }
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ScrollPatch { private static void Postfix(ref float __result) { if (Plugin.BlockingInput) __result = 0; } }
    [HarmonyPatch]
    internal static class StickPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "GetJoyRightStickX", "GetJoyRightStickY", "GetJoyLeftStickX", "GetJoyLeftStickY" })
            {
                MethodBase method;
                try { method = Reflection.Method(typeof(ZInput), name, typeof(bool)); }
                catch (MissingMethodException) { method = Reflection.Method(typeof(ZInput), name); }
                yield return method;
            }
        }
        private static void Postfix(ref float __result) { if (Plugin.BlockingInput) __result = 0; }
    }
}
