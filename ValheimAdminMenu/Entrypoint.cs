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
        private static float retryAt;

        /// <summary>Idempotent. Safe from a non-main Mono thread.</summary>
        public static void Start()
        {
            if (started || scheduled) return;
            scheduled = true;

            bootstrap = new Harmony("valheaven.bootstrap");
            MethodInfo gate =
                AccessTools.Method(typeof(SceneManager), "Internal_ActiveSceneChanged")
                ?? AccessTools.Method(typeof(SceneManager), "Internal_SceneLoaded");

            var ready = new HarmonyMethod(typeof(Entrypoint), nameof(OnMainThreadReady));
            int gates = 0;
            if (gate != null) { bootstrap.Patch(gate, postfix: ready); gates++; }

            // Scene change alone can miss: if the worker attaches after the last transition, nothing boots until the
            // next one and the hotkey looks dead. Per-frame menu/world updates are a fallback main-thread gate.
            foreach (var frame in new[]
                     {
                         AccessTools.Method(typeof(FejdStartup), "Update"),
                         AccessTools.Method(typeof(MonoUpdaters), "Update")
                     })
            {
                if (frame == null) continue;
                try { bootstrap.Patch(frame, postfix: ready); gates++; }
                catch (Exception ex) { try { AppLog.Warning("Bootstrap gate " + frame.DeclaringType?.Name + " failed: " + ex.Message); } catch (Exception caught) { AppLog.Swallow(caught); } }
            }

            if (gates == 0)
                throw new MissingMethodException("No Unity main-thread bootstrap gate found.");
        }

        // Harmony postfix. Unity objects only on the main thread.
        private static void OnMainThreadReady()
        {
            if (started) return;
            // Per-frame gates fire every frame; after a failure, retry about once a second rather than every frame.
            if (failures > 0 && Time.realtimeSinceStartup < retryAt) return;
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
                retryAt = Time.realtimeSinceStartup + 1f;
                // Don't rethrow into Unity's callback; keep the gates patched and retry.
                if (failures <= 5 || failures % 60 == 0)
                {
                    try { AppLog.Error("Bootstrap failed (attempt " + failures + "): " + ex); }
                    catch (Exception caught) { AppLog.Swallow(caught); /* AppLog itself may not be writable yet */ }
                }
                return;
            }

            try { bootstrap?.UnpatchAll(bootstrap.Id); } catch (Exception caught) { AppLog.Swallow(caught); /* ignore */ }
            bootstrap = null;
        }
    }
}
