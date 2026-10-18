using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ValheimAdminMenu
{
    internal sealed class WorldTools
    {
        private readonly Plugin app;
        internal float Radius = 30, BrushRadius = 5, BrushAmount = 1;
        internal bool Square;
        internal WorldTools(Plugin app) { this.app = app; }
        internal static T[] Loaded<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsSortMode.None);
        internal static bool Nearby(Component obj, Vector3 center, float radius) => obj && (obj.transform.position - center).sqrMagnitude <= radius * radius;
        private static bool Own(GameObject obj)
        {
            var view = obj.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) return false;
            if (!view.IsOwner()) view.ClaimOwnership();
            return view.IsOwner();
        }
        internal static bool IsInventoryItem(GameObject prefab)
        {
            if (!prefab || prefab.GetComponent<Character>() || prefab.GetComponent<Piece>() || prefab.GetComponent<Container>() || prefab.GetComponent<TerrainOp>() || prefab.GetComponent<Projectile>()) return false;
            var drop = prefab.GetComponent<ItemDrop>();
            return drop && drop.m_itemData != null && drop.m_itemData.m_shared != null
                && !string.IsNullOrWhiteSpace(drop.m_itemData.m_shared.m_name)
                && drop.m_itemData.m_shared.m_maxStackSize > 0
                && drop.m_itemData.m_shared.m_icons != null && drop.m_itemData.m_shared.m_icons.Length > 0;
        }
        internal void Give(string id, int amount, int quality)
        {
            var player = app.RequirePlayer();
            var prefab = ObjectDB.instance.GetItemPrefab(id);
            var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
            if (!IsInventoryItem(prefab)) throw new ArgumentException("Select an inventory item, not a character, structure, or drop effect.");
            amount = Mathf.Clamp(amount, 1, 9999);
            quality = Mathf.Clamp(quality, 1, Math.Max(1, drop.m_itemData.m_shared.m_maxQuality));
            int added = 0;
            while (added < amount)
            {
                var item = drop.m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_stack = Math.Min(amount - added, Math.Max(1, item.m_shared.m_maxStackSize));
                item.m_quality = quality; item.m_variant = 0;
                item.m_durability = item.GetMaxDurability();
                item.m_crafterID = player.GetPlayerID(); item.m_crafterName = player.GetPlayerName();
                item.m_worldLevel = (byte)Game.m_worldLevel; item.m_cheated = true; item.m_pickedUp = true;
                if (!player.GetInventory().CanAddItem(item, item.m_stack)) break;
                int before = player.GetInventory().CountItems(item.m_shared.m_name, quality);
                bool complete = player.GetInventory().AddItem(item);
                added += player.GetInventory().CountItems(item.m_shared.m_name, quality) - before;
                if (!complete) break;
            }
            app.Report($"Added {added}/{amount} {id}, quality {quality}." + (added < amount ? " Inventory capacity reached." : ""));
        }
        internal void Spawn(string id, int count, int level)
        {
            app.RequireWorld();
            var prefab = ZNetScene.instance.GetPrefab(id);
            if (!prefab || !prefab.GetComponent<ZNetView>() || prefab.GetComponent<Player>()) throw new ArgumentException("Select a registered network prefab other than Player.");
            var player = app.RequirePlayer(); count = Mathf.Clamp(count, 1, 20); level = Mathf.Clamp(level, 1, 10);
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = player.transform.position + player.transform.forward * 4 + player.transform.right * ((i % 5) - 2) * 1.5f + Vector3.up;
                var obj = Object.Instantiate(prefab, pos, Quaternion.identity);
                var character = obj.GetComponent<Character>(); if (character) character.SetLevel(level);
                var item = obj.GetComponent<ItemDrop>(); if (item) item.m_itemData.m_cheated = true;
                spawned++;
            }
            app.Report($"Spawned {spawned} {id} in front of the local player.");
        }
        internal void DeleteDrops()
        {
            app.RequireWorld(); var pos = app.RequirePlayer().transform.position; int removed = 0, skipped = 0;
            foreach (var item in Loaded<ItemDrop>().Where(x => Nearby(x, pos, Radius)).Take(2000))
            {
                if (Own(item.gameObject)) { ZNetScene.instance.Destroy(item.gameObject); removed++; } else skipped++;
            }
            app.Report($"Removed {removed} loaded ground drops within {Radius:0} m; skipped {skipped} without ownership.");
        }
        internal void RemoveTombstones()
        {
            app.RequireWorld(); var player = app.RequirePlayer(); int removed = 0;
            foreach (var grave in Loaded<TombStone>().Where(x => Nearby(x, player.transform.position, Radius)).Take(200))
            {
                var view = grave.GetComponent<ZNetView>();
                if (!view || !view.IsValid() || view.GetZDO().GetLong(ZDOVars.s_owner, 0) != player.GetPlayerID()) continue;
                if (Own(grave.gameObject)) { ZNetScene.instance.Destroy(grave.gameObject); removed++; }
            }
            app.Report($"Removed {removed} of your loaded tombstones within {Radius:0} m, including their contents.");
        }
        internal void KillEntities()
        {
            app.RequireWorld(); var center = app.RequirePlayer().transform.position; int count = 0;
            foreach (var character in Character.GetAllCharacters().ToArray())
            {
                if (!character || character is Player || character.IsDead() || !Nearby(character, center, Radius)) continue;
                if (!Own(character.gameObject)) continue;
                character.SetHealth(0); Reflection.Method(typeof(Character), "CheckDeath").Invoke(character, null); count++;
                if (count >= 1000) break;
            }
            app.Report($"Killed {count} loaded non-player characters within {Radius:0} m, including tamed creatures.");
        }
        internal void Tame()
        {
            app.RequireWorld(); var center = app.RequirePlayer().transform.position; int count = 0;
            foreach (var character in Character.GetAllCharacters().ToArray())
            {
                if (!character || character is Player || character.IsDead() || character.IsTamed() || !Nearby(character, center, Radius)) continue;
                var tameable = character.GetComponent<Tameable>();
                if (!tameable || !Own(character.gameObject)) continue;
                Reflection.Method(typeof(Tameable), "Tame").Invoke(tameable, null);
                if (character.IsTamed()) count++;
            }
            app.Report($"Tamed {count} eligible loaded creatures within {Radius:0} m.");
        }
        internal void Terrain(string operation)
        {
            app.RequireWorld();
            Vector3 center = app.RequirePlayer().transform.position;
            float radius = Mathf.Clamp(BrushRadius, 1, 20), amount = Mathf.Clamp(BrushAmount, 0.1f, 8);
            var maps = new List<Heightmap>(); Heightmap.FindHeightmap(center, radius, maps);
            if (maps.Count == 0) throw new InvalidOperationException("No loaded terrain at the player position.");
            if (operation == "reset")
            {
                foreach (var legacy in TerrainModifier.GetAllInstances().ToArray())
                {
                    if (!legacy || !legacy.m_playerModifiction) continue;
                    Vector3 d = legacy.transform.position - center;
                    bool inside = Square ? Mathf.Abs(d.x) <= radius && Mathf.Abs(d.z) <= radius : d.x * d.x + d.z * d.z <= radius * radius;
                    if (inside && Own(legacy.gameObject)) ZNetScene.instance.Destroy(legacy.gameObject);
                }
            }
            int count = 0;
            foreach (var map in maps)
            {
                var compiler = map.GetAndCreateTerrainCompiler();
                if (!compiler || !Own(compiler.gameObject)) continue;
                if (operation == "reset") ResetCompiler(compiler, map, center, radius);
                else
                {
                    var settings = new TerrainOp.Settings
                    {
                        m_paintCleared = false, m_square = Square, m_level = operation == "level",
                        m_levelRadius = radius, m_levelOffset = 0,
                        m_raise = operation == "raise" || operation == "lower", m_raiseRadius = radius,
                        m_raiseDelta = operation == "lower" ? -amount : amount, m_raisePower = 0,
                        m_smooth = operation == "smooth", m_smoothRadius = radius, m_smoothPower = 3
                    };
                    if (!settings.m_level && !settings.m_raise && !settings.m_smooth) throw new ArgumentException("Unknown terrain operation.");
                    Reflection.Method(typeof(TerrainComp), "DoOperation", typeof(Vector3), typeof(Vector3), typeof(TerrainOp.Settings)).Invoke(compiler, new object[] { center, Vector3.zero, settings });
                }
                count++;
            }
            app.Report($"Terrain {operation}: updated {count}/{maps.Count} loaded terrain tiles, brush {radius:0.#} m. Changes persist in the world save.");
        }
        private void ResetCompiler(TerrainComp comp, Heightmap map, Vector3 center, float radius)
        {
            var changed = Reflection.Get<bool[]>(comp, "m_modifiedHeight");
            var level = Reflection.Get<float[]>(comp, "m_levelDelta"); var smooth = Reflection.Get<float[]>(comp, "m_smoothDelta");
            var paint = Reflection.Get<bool[]>(comp, "m_modifiedPaint");
            int pitch = map.m_width + 1;
            bool InBrush(Vector3 pos) => Square ? Mathf.Abs(pos.x - center.x) <= radius && Mathf.Abs(pos.z - center.z) <= radius : new Vector2(pos.x - center.x, pos.z - center.z).sqrMagnitude <= radius * radius;
            for (int y = 0; y < pitch; y++) for (int x = 0; x < pitch; x++)
            {
                int index = y * pitch + x;
                Vector3 pos = map.transform.position + new Vector3((x - map.m_width / 2) * map.m_scale, 0, (y - map.m_width / 2) * map.m_scale);
                if (InBrush(pos)) { changed[index] = false; level[index] = 0; smooth[index] = 0; }
                if (index < paint.Length && InBrush(map.VertexMaskToWorld(x, y))) paint[index] = false;
            }
            Reflection.Set(comp, "m_lastOpPoint", center); Reflection.Set(comp, "m_lastOpRadius", radius);
            Reflection.Method(typeof(TerrainComp), "Save", typeof(bool)).Invoke(comp, new object[] { false });
            map.Poke(1);
            if (ClutterSystem.instance) ClutterSystem.instance.ResetGrass(center, radius);
        }
        internal List<Minimap.PinData> Pins()
        {
            if (!Minimap.instance) return new List<Minimap.PinData>();
            return Reflection.Get<List<Minimap.PinData>>(Minimap.instance, "m_pins").Where(p => p.m_save).ToList();
        }
    }
}
