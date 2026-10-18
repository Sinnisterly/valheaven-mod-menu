using System;
using System.IO;
using UnityEngine;

namespace ValheimAdminMenu
{
    /// <summary>Writable sidecar paths next to the game (AppData fallback).</summary>
    internal static class AppPaths
    {
        private static string root;
        private static bool resolved;

        internal static string Root
        {
            get
            {
                if (!resolved) Resolve();
                return root;
            }
        }

        internal static string SettingsPath => Path.Combine(Root, "Valheaven.settings.json");
        internal static string LogPath => Path.Combine(Root, "Valheaven.log");
        internal static string BlueprintsPath => Path.Combine(Root, "Valheaven.blueprints.json");
        internal static string BlueprintsDir => Path.Combine(Root, "blueprints");
        /// <summary>Sidecar locale JSON (overrides embedded packs).</summary>
        internal static string LocalesDir => Path.Combine(Root, "locales");

        private static void Resolve()
        {
            resolved = true;
            string gameDir = null;
            try
            {
                if (!string.IsNullOrEmpty(Application.dataPath))
                    gameDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            }
            catch { /* ignore */ }

            if (!string.IsNullOrEmpty(gameDir) && TryEnsureWritable(gameDir))
            {
                root = gameDir;
                return;
            }

            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Valheaven");
            Directory.CreateDirectory(appData);
            root = appData;
        }

        private static bool TryEnsureWritable(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, "Valheaven.write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
