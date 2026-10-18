using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ValheimAdminMenu.Validation")]

namespace ValheimAdminMenu
{
    [BepInPlugin(Id, "Valheaven Mod Menu", "2.1.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "local.valheim.adminmenu";
        internal static Plugin Instance;
        internal static bool Capturing => Instance && (Instance.Open || Time.frameCount <= Instance.blockThroughFrame) && Application.isFocused;
        internal readonly Queue<Action> Commands = new Queue<Action>();
        internal ConfigEntry<KeyCode> Hotkey;
        internal SavedSettings Settings;
        internal ConfigEntry<float> UiScale;
        internal bool Open;
        internal string Status = "Ready. F8 opens and closes the menu.";
        internal PlayerFeatures Features;
        internal ExpansionFeatures Expansion;
        internal ClientPlayers Network;
        internal WorldTools World;
        internal Overlay Overlay;
        internal MenuView Menu;
        private Harmony harmony;
        private CursorLockMode priorLock;
        private bool priorRequested;
        private Player lastPlayer;
        private bool compatible;
        private int blockThroughFrame = -1;
        private readonly List<BaseInputModule> blockedModules = new List<BaseInputModule>();

        private void Awake()
        {
            Instance = this;
            Hotkey = Config.Bind("Menu", "Hotkey", KeyCode.F8, "Open or close the menu.");
            UiScale = Config.Bind("Menu", "Scale", 1f, new ConfigDescription("Interface scale.", new AcceptableValueRange<float>(0.7f, 1.6f)));
            Config.SaveOnConfigSet = false;
            Features = new PlayerFeatures(this);
            Expansion = new ExpansionFeatures(this);
            World = new WorldTools(this);
            Network = new ClientPlayers(this);
            Overlay = new Overlay(this);
            Menu = new MenuView(this);
            Settings = new SavedSettings(this);
            try { Settings.Load(); } catch (Exception ex) { Logger.LogWarning("Settings load failed: " + ex.Message); Settings.Apply(); }
            compatible = global::Version.CurrentVersion.ToString() == "1.0.7";
            if (!compatible)
            {
                Logger.LogError("This build supports Valheim 1.0.7 only. Feature hooks are disabled on " + global::Version.CurrentVersion);
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
                Logger.LogInfo("Valheaven 2.1.0 ready for Valheim 1.0.7. Hotkey: " + Hotkey.Value);
            }
            catch (Exception ex)
            {
                compatible = false;
                harmony?.UnpatchSelf();
                Logger.LogError(ex);
                Status = "Compatibility check failed. See BepInEx log.";
            }
        }

        private void Update()
        {
            if (!compatible) return;
            try
            {
                bool capturingKey = Menu.BindingKey;
                Menu.PollKeybind();
                if (!capturingKey && !Menu.EditingText && Input.GetKeyDown(Hotkey.Value)) SetOpen(!Open);
                if (!capturingKey && Open && Input.GetKeyDown(KeyCode.Escape) && !Menu.DismissOverlay()) SetOpen(false);
                var player = Player.m_localPlayer;
                if (player != lastPlayer)
                {
                    SetOpen(false);
                    Features.Restore();
                    Expansion.Reset();
                    lastPlayer = player;
                    Settings.PendingPlayer = true;
                    Commands.Clear();
                    Overlay.Clear();
                }
                Features.Tick(player);
                Expansion.Tick();
                Settings.ApplyPlayer();
                if (Open) BlockNativeUI();
                Network.Tick();
                Overlay.Tick();
                Menu.Tick();
                for (int i = 0; i < 4 && Commands.Count > 0; i++)
                {
                    try { Commands.Dequeue()(); }
                    catch (Exception ex) { Report(ex.GetBaseException().Message); Logger.LogError(ex); }
                }
            }
            catch (Exception ex)
            {
                Report("Update failed; menu closed. " + ex.GetBaseException().Message);
                SetOpen(false);
                Logger.LogError(ex);
            }
        }

        private void OnGUI()
        {
            if (!compatible || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            try { Overlay.Draw(); }
            catch (ExitGUIException) { throw; }
            catch (Exception ex) { SetOpen(false); Overlay.Enabled = false; Logger.LogError(ex); Report("UI closed after a rendering error. See BepInEx log."); }
        }
        private void OnApplicationFocus(bool focused) { if (!focused) SetOpen(false); }
        private void SceneChanged(Scene a, Scene b)
        {
            SetOpen(false); Commands.Clear(); Features.Restore(); Expansion.Reset(); Overlay.Clear(); Menu.ResetScene();
        }
        internal void SetOpen(bool open)
        {
            if (Open == open) return;
            if (open)
            {
                priorLock = ZCursor.LockState;
                priorRequested = ZCursor.IsRequested;
                Open = true;
                Menu.Show();
                BlockNativeUI();
                ReleaseCursor();
            }
            else
            {
                if (Settings != null && Settings.Data.autoSave) { try { Settings.Save(false); } catch (Exception ex) { Logger.LogWarning("Settings save failed: " + ex.Message); } }
                Menu?.StopBinding();
                Open = false;
                Menu?.Hide();
                blockThroughFrame = Time.frameCount + 1;
                foreach (var module in blockedModules) if (module) module.enabled = true;
                blockedModules.Clear();
                Menu?.RestoreInput();
                Menu?.CancelConfirmation();
                ZCursor.LockState = priorLock;
                if (priorRequested) ZCursor.Show(); else ZCursor.Hide();
                if (GameCamera.instance) GameCamera.instance.UpdateMouseCapture();
            }
        }
        private void BlockNativeUI()
        {
            if (!EventSystem.current) return;
            foreach (var module in EventSystem.current.GetComponents<BaseInputModule>())
                if (module.enabled && module != Menu.InputModule) { if(!blockedModules.Contains(module))blockedModules.Add(module); module.enabled = false; }
        }
        internal static void ReleaseCursor() { ZCursor.LockState = CursorLockMode.None; ZCursor.Show(); Cursor.visible = true; }
        internal void Enqueue(Action action)
        {
            if (Commands.Count < 32) Commands.Enqueue(action);
            else Report("Command queue is full. Wait for pending actions.");
        }
        internal void Report(string message) { Status = message; Logger.LogInfo(message); }
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
        private void OnDisable() { SetOpen(false); Features?.Restore(); Expansion?.Reset(); Commands.Clear(); }
        private void OnDestroy()
        {
            SetOpen(false); Features?.Restore(); Expansion?.Reset(); Network?.Dispose(); Overlay?.Clear(); Menu?.Dispose();
            SceneManager.activeSceneChanged -= SceneChanged;
            harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }
    }

    internal static class Reflection
    {
        private static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>();
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
        internal static MethodInfo Method(Type type, string name, params Type[] args)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var method = current.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
                if (method != null) return method;
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
        }
    }

    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class PlayerInputPatch { private static void Postfix(ref bool __result) { if (Plugin.Capturing) __result = false; } }
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
        private static void Postfix(ref bool __result) { if (Plugin.Capturing) __result = false; }
    }
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseDelta))]
    internal static class MouseDeltaPatch { private static void Postfix(ref Vector2 __result) { if (Plugin.Capturing) __result = Vector2.zero; } }
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ScrollPatch { private static void Postfix(ref float __result) { if (Plugin.Capturing) __result = 0; } }
    [HarmonyPatch]
    internal static class StickPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "GetJoyRightStickX", "GetJoyRightStickY", "GetJoyLeftStickX", "GetJoyLeftStickY" }) yield return Reflection.Method(typeof(ZInput), name, typeof(bool));
        }
        private static void Postfix(ref float __result) { if (Plugin.Capturing) __result = 0; }
    }
}

