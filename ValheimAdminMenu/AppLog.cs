using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace ValheimAdminMenu
{
    /// <summary>
    /// Valheaven.log (+ Unity console). Lines are "time [LEVEL] [Tag] message". Debug lines only when <see cref="Verbose"/>.
    /// Hot paths: guard with <see cref="Due"/> / <see cref="Verbose"/> before building strings.
    /// </summary>
    internal static class AppLog
    {
        private const long RotateBytes = 4L << 20;
        private const int FirstRepeatMs = 10000, MaxRepeatMs = 600000, Frames = 5;
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Limit> keyed = new Dictionary<string, Limit>(StringComparer.Ordinal);
        private static readonly Dictionary<(string, int), Limit> sites = new Dictionary<(string, int), Limit>();
        private static StreamWriter writer;
        private static int retryAt;
        private static bool hooked;
        [ThreadStatic] private static bool writing;
        /// <summary>Debug level. Settings > Diagnostics, persisted as verboseLog; off by default.</summary>
        internal static volatile bool Verbose;

        private sealed class Limit { internal int hits, reported, nextAt, interval = FirstRepeatMs; }

        internal static void Info(string message) => Write("INFO", null, message);
        internal static void Info(string tag, string message) => Write("INFO", tag, message);
        internal static void Warning(string message) => Write("WARN", null, message);
        internal static void Warning(string tag, string message) => Write("WARN", tag, message);
        internal static void Error(string message) => Write("ERROR", null, message);
        internal static void Error(string tag, string message) => Write("ERROR", tag, message);
        internal static void Error(Exception ex) => Write("ERROR", null, ex?.ToString() ?? "null exception");
        internal static void Error(string tag, string context, Exception ex) => Write("ERROR", tag, context + ": " + Describe(ex));
        internal static void Debug(string tag, string message) { if (Verbose) Write("DEBUG", tag, message); }

        /// <summary>For catch blocks that must not throw. First hit per call site logs type/message/frames; repeats back off 10s..10min.</summary>
        internal static void Swallow(Exception ex, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            int repeats;
            lock (Gate)
            {
                if (!sites.TryGetValue((file, line), out var limit)) sites[(file, line)] = limit = new Limit();
                if (!Tick(limit, out repeats)) return;
            }
            string tag = TagOf(file);
            Write("WARN", tag, "Swallowed in " + member + " (" + tag + ".cs:" + line + ")"
                + (repeats > 0 ? " x" + (repeats + 1) + " since last report" : "") + ": " + Describe(ex));
        }

        /// <summary>Rate limit by key (first, then backed-off). Call before building the message.</summary>
        internal static bool Due(string key)
        {
            lock (Gate)
            {
                if (!keyed.TryGetValue(key, out var limit)) keyed[key] = limit = new Limit();
                return Tick(limit, out _);
            }
        }
        /// <summary>True only the first time per key this session.</summary>
        internal static bool Once(string key)
        {
            lock (Gate)
            {
                if (keyed.ContainsKey(key)) return false;
                keyed[key] = new Limit { hits = 1, reported = 1, nextAt = int.MaxValue };
                return true;
            }
        }
        internal static void WarnLimited(string key, string tag, string message) { if (Due(key)) Write("WARN", tag, message); }

        private static bool Tick(Limit limit, out int repeats)
        {
            int now = Environment.TickCount;
            limit.hits++;
            repeats = limit.hits - limit.reported - 1;
            if (limit.reported > 0 && unchecked(now - limit.nextAt) < 0) return false;
            limit.reported = limit.hits;
            if (limit.hits > 1) limit.interval = Math.Min(MaxRepeatMs, limit.interval * 2);
            limit.nextAt = unchecked(now + limit.interval);
            return true;
        }

        /// <summary>Current call stack as "Type.Method" frames (skip = frames above the caller to drop).</summary>
        internal static string Callers(int skip, int max)
        {
            var sb = new StringBuilder(256);
            var frames = new System.Diagnostics.StackTrace(skip + 1, false).GetFrames();
            if (frames == null) return "";
            for (int i = 0, n = 0; i < frames.Length && n < max; i++)
            {
                var m = frames[i].GetMethod();
                if (m == null) continue;
                sb.Append("\n    at ").Append(m.DeclaringType != null ? m.DeclaringType.Name + "." : "").Append(m.Name);
                n++;
            }
            return sb.ToString();
        }

        /// <summary>"Type: message (via Outer)" + top stack frames.</summary>
        internal static string Describe(Exception ex)
        {
            if (ex == null) return "null exception";
            var root = ex.GetBaseException() ?? ex;
            var sb = new StringBuilder(256);
            sb.Append(root.GetType().Name).Append(": ").Append(root.Message);
            if (!ReferenceEquals(root, ex)) sb.Append(" (via ").Append(ex.GetType().Name).Append(')');
            AppendFrames(sb, root.StackTrace ?? ex.StackTrace, Frames);
            return sb.ToString();
        }
        private static void AppendFrames(StringBuilder sb, string stack, int max)
        {
            if (string.IsNullOrEmpty(stack)) return;
            int start = 0, n = 0;
            while (start < stack.Length && n < max)
            {
                int end = stack.IndexOf('\n', start);
                if (end < 0) end = stack.Length;
                string frame = stack.Substring(start, end - start).Trim();
                if (frame.Length > 0) { sb.Append("\n    ").Append(frame.StartsWith("at ", StringComparison.Ordinal) ? frame : "at " + frame); n++; }
                start = end + 1;
            }
        }
        private static string TagOf(string file)
        {
            if (string.IsNullOrEmpty(file)) return "?";
            int slash = Math.Max(file.LastIndexOf('\\'), file.LastIndexOf('/'));
            int dot = file.LastIndexOf('.');
            if (dot <= slash) dot = file.Length;
            return file.Substring(slash + 1, dot - slash - 1);
        }

        /// <summary>Mirror Unity errors/exceptions (incl. ones thrown inside Harmony patches or vanilla code we affect) into Valheaven.log.</summary>
        internal static void HookUnity()
        {
            if (hooked) return;
            hooked = true;
            Application.logMessageReceivedThreaded += OnUnityLog;
        }
        internal static void UnhookUnity()
        {
            if (!hooked) return;
            hooked = false;
            Application.logMessageReceivedThreaded -= OnUnityLog;
        }
        private static void OnUnityLog(string condition, string stack, LogType type)
        {
            if (writing || (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)) return;
            try
            {
                condition = condition ?? "";
                int nl = condition.IndexOf('\n');
                string head = nl >= 0 ? condition.Substring(0, nl) : condition;
                if (head.Length > 200) head = head.Substring(0, 200);
                string first = "";
                if (!string.IsNullOrEmpty(stack)) { int e = stack.IndexOf('\n'); first = e >= 0 ? stack.Substring(0, e) : stack; }
                int repeats;
                lock (Gate)
                {
                    string key = "unity|" + head + "|" + first;
                    if (!keyed.TryGetValue(key, out var limit)) keyed[key] = limit = new Limit();
                    if (!Tick(limit, out repeats)) return;
                }
                bool ours = stack != null && (stack.IndexOf("ValheimAdminMenu", StringComparison.Ordinal) >= 0 || stack.IndexOf("DMD<", StringComparison.Ordinal) >= 0);
                var sb = new StringBuilder(256);
                sb.Append(type).Append(ours ? " (Valheaven/patched frame)" : "").Append(repeats > 0 ? " x" + (repeats + 1) + " since last report" : "").Append(": ").Append(head);
                AppendFrames(sb, stack, 8);
                Write(type == LogType.Exception ? "ERROR" : "WARN", "Unity", sb.ToString(), mirror: false);
            }
            catch { /* never throw into Unity's logger */ }
        }

        private static void Write(string level, string tag, string message, bool mirror = true)
        {
            if (writing) return;
            writing = true;
            try
            {
                string body = tag == null ? message : "[" + tag + "] " + message;
                // DEBUG stays out of Player.log (Debug.Log captures a stack per call).
                if (mirror && level != "DEBUG") { try { UnityEngine.Debug.Log("[Valheaven] " + body); } catch { /* headless */ } }
                lock (Gate)
                {
                    if (!Open()) return;
                    writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + body);
                }
            }
            catch { /* best-effort */ }
            finally { writing = false; }
        }
        // One appending writer per session; rotates a >4 MB log to Valheaven.prev.log at open.
        private static bool Open()
        {
            if (writer != null) return true;
            if (retryAt != 0 && unchecked(Environment.TickCount - retryAt) < 0) return false;
            try
            {
                string path = AppPaths.LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                var info = new FileInfo(path);
                if (info.Exists && info.Length > RotateBytes)
                {
                    string prev = Path.Combine(info.DirectoryName ?? ".", "Valheaven.prev.log");
                    File.Copy(path, prev, true);
                    File.Delete(path);
                }
                var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                writer.WriteLine();
                writer.WriteLine("=== Valheaven session " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
                return true;
            }
            catch
            {
                writer = null;
                retryAt = unchecked(Environment.TickCount + 10000);
                if (retryAt == 0) retryAt = 1;
                return false;
            }
        }
        internal static void Flush() { lock (Gate) { try { writer?.Flush(); } catch { } } }
    }
}
