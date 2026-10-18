using System;
using System.IO;
using UnityEngine;

namespace ValheimAdminMenu
{
    /// <summary>Logs to Valheaven.log and the Unity console.</summary>
    internal static class AppLog
    {
        private static readonly object Gate = new object();
        private static bool headerWritten;

        internal static void Info(string message) => Write("INFO", message);
        internal static void Warning(string message) => Write("WARN", message);
        internal static void Error(string message) => Write("ERROR", message);
        internal static void Error(Exception ex) => Write("ERROR", ex?.ToString() ?? "null exception");

        private static void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message;
            try { Debug.Log("[Valheaven] " + message); } catch { /* headless */ }
            try
            {
                lock (Gate)
                {
                    string path = AppPaths.LogPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                    if (!headerWritten)
                    {
                        File.AppendAllText(path, "--- Valheaven log ---" + Environment.NewLine);
                        headerWritten = true;
                    }
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch { /* best-effort */ }
        }
    }
}
