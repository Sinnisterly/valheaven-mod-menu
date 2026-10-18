using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using ValheimAdminMenu;

namespace Valheaven
{
    /// <summary>Native version.dll calls this from a worker thread after Mono is up. We only schedule a main-thread gate here.</summary>
    public static class Entrypoint
    {
        private static bool started;
        private static bool scheduled;
        private static Harmony bootstrap;
        private static int failures;

        /// <summary>Idempotent. Safe from a non-main Mono thread.</summary>
        public static void Start()
        {
            if (started || scheduled) return;
            scheduled = true;

            bootstrap = new Harmony("valheaven.bootstrap");
            MethodInfo gate =
                AccessTools.Method(typeof(SceneManager), "Internal_ActiveSceneChanged")
                ?? AccessTools.Method(typeof(SceneManager), "Internal_SceneLoaded");

            if (gate == null)
                throw new MissingMethodException("No Unity main-thread bootstrap gate found.");

            bootstrap.Patch(gate, postfix: new HarmonyMethod(typeof(Entrypoint), nameof(OnMainThreadReady)));
        }

        // Harmony postfix. Unity objects only on the main thread.
        private static void OnMainThreadReady()
        {
            if (started) return;
            try
            {
                // Plugin.Awake destroys a duplicate host.
                var host = new GameObject("ValheavenHost");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<Plugin>();
                started = true;
                AppLog.Info("Bootstrap host created on main thread (" + SystemInfo.graphicsDeviceType + ").");
            }
            catch (Exception ex)
            {
                failures++;
                // Don't rethrow into Unity's scene callback; keep the gate patched and retry next scene change.
                try { AppLog.Error("Bootstrap failed (attempt " + failures + "): " + ex); }
                catch { /* AppLog itself may not be writable yet */ }
                return;
            }

            try { bootstrap?.UnpatchAll(bootstrap.Id); } catch { /* ignore */ }
            bootstrap = null;
        }
    }
}
