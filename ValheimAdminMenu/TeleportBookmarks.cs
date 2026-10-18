using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ValheimAdminMenu
{
    [Serializable]
    internal sealed class BookmarkStore
    {
        public int version = 1;
        public List<BookmarkWorld> worlds = new List<BookmarkWorld>();
    }

    [Serializable]
    internal sealed class BookmarkWorld
    {
        public string world = "";
        public List<BookmarkEntry> entries = new List<BookmarkEntry>();
    }

    [Serializable]
    internal sealed class BookmarkEntry
    {
        public string name = "";
        public float x, y, z;
    }

    /// <summary>Teleport bookmarks per world (sidecar JSON).</summary>
    internal static class TeleportBookmarks
    {
        private static BookmarkStore store;
        private static string PathName => System.IO.Path.Combine(AppPaths.Root, "Valheaven.bookmarks.json");

        internal static string CurrentWorld()
        {
            try
            {
                if (ZNet.instance != null)
                {
                    string name = ZNet.instance.GetWorldName();
                    if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
                }
            }
            catch { }
            return "default";
        }

        private static void Ensure()
        {
            if (store != null) return;
            store = new BookmarkStore();
            try
            {
                if (File.Exists(PathName))
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(PathName), store);
            }
            catch { store = new BookmarkStore(); }
            if (store.worlds == null) store.worlds = new List<BookmarkWorld>();
        }

        private static void Save()
        {
            Ensure();
            string directory = System.IO.Path.GetDirectoryName(PathName);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = PathName + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(store, true));
            if (File.Exists(PathName)) File.Replace(temp, PathName, PathName + ".bak");
            else File.Move(temp, PathName);
        }

        private static BookmarkWorld WorldBucket()
        {
            Ensure();
            string world = CurrentWorld();
            var bucket = store.worlds.FirstOrDefault(w => w != null && string.Equals(w.world, world, StringComparison.OrdinalIgnoreCase));
            if (bucket == null)
            {
                bucket = new BookmarkWorld { world = world, entries = new List<BookmarkEntry>() };
                store.worlds.Add(bucket);
            }
            if (bucket.entries == null) bucket.entries = new List<BookmarkEntry>();
            return bucket;
        }

        internal static IEnumerable<BookmarkEntry> List()
        {
            return WorldBucket().entries.Where(e => e != null && !string.IsNullOrWhiteSpace(e.name));
        }

        internal static void Add(string name, Vector3 pos)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a bookmark name.");
            if (!Rules.ValidPosition(pos)) throw new ArgumentException("Position is out of range.");
            name = Rules.Text(name, 48);
            var bucket = WorldBucket();
            var existing = bucket.entries.FirstOrDefault(e => e != null && string.Equals(e.name, name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new BookmarkEntry { name = name };
                bucket.entries.Add(existing);
            }
            existing.name = name;
            existing.x = pos.x; existing.y = pos.y; existing.z = pos.z;
            Save();
            Plugin.Instance.Report("Saved bookmark \"" + name + "\" for world " + CurrentWorld() + ".");
        }

        internal static void Remove(string name)
        {
            var bucket = WorldBucket();
            int n = bucket.entries.RemoveAll(e => e != null && string.Equals(e.name, name, StringComparison.OrdinalIgnoreCase));
            if (n <= 0) throw new ArgumentException("Bookmark not found.");
            Save();
            Plugin.Instance.Report("Removed bookmark \"" + name + "\".");
        }

        internal static void Teleport(string name)
        {
            var entry = List().FirstOrDefault(e => string.Equals(e.name, name, StringComparison.OrdinalIgnoreCase));
            if (entry == null) throw new ArgumentException("Choose a bookmark.");
            Plugin.Instance.Features.Teleport(new Vector3(entry.x, entry.y, entry.z));
        }
    }
}
