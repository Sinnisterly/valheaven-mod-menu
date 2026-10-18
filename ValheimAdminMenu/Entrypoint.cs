using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Valheaven
{
    /// <summary>Native version.dll calls this from a worker thread after Mono is up. We only schedule a main-thread gate here.</summary>
    public static class Entrypoint
    {
        private static bool started;
        private static bool scheduled;
        private static Harmony bootstrap;

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
                host.AddComponent<ValheimAdminMenu.Plugin>();
                started = true;
            }
            catch (Exception)
            {
                // Native host logs invoke-time failures; don't rethrow into Unity's scene callback.
            }
            finally
            {
                try { bootstrap?.UnpatchAll(bootstrap.Id); } catch { /* ignore */ }
                bootstrap = null;
            }
        }
    }
}
