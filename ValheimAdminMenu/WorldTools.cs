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
        internal float Radius = 30, BrushRadius = 5, BrushAmount = 1, PaintStrength = 1, BrushFeather = 6f;
        internal bool Square, Precision, BrushAtLook;
        /// <summary>0 Dirt, 1 Cultivate, 2 Paved, 3 Reset, 4 Clear vegetation.</summary>
        internal int PaintMode;
        private TerrainUndoState undo;
        internal bool CanUndo => undo != null;
        internal WorldTools(Plugin app) { this.app = app; }
        private sealed class TerrainUndoState
        {
            internal readonly List<Tile> Tiles = new List<Tile>();
            internal Vector3 Center;
            internal float Radius;
            internal string Label;
            internal sealed class Tile
            {
                internal TerrainComp Comp;
                internal Heightmap Map;
                internal bool[] ModifiedHeight, ModifiedPaint;
                internal float[] LevelDelta, SmoothDelta;
                internal Color[] PaintMask;
            }
        }
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
                && drop.m_itemData.m_shared.m_maxStackSize > 0;
        }
        internal void Give(string id,int amount,int quality,string crafter=null,bool noCrafter=false,ItemEdits edits=null)
        {
            var player=app.RequirePlayer();var prefab=ObjectDB.instance.GetItemPrefab(id);
            if(!IsInventoryItem(prefab))throw new ArgumentException("Select an inventory item.");
            amount=Mathf.Clamp(amount,1,9999);
            int added=SpawnPresets.Add(player.GetInventory(),prefab,amount,quality,crafter,noCrafter,edits);
            app.Report("Added "+added+"/"+amount+" "+id+"."+(added<amount?L.T("ui.inventory_capacity_reached"):""));
        }
        internal void Spawn(string id, int count, int level)
        {
            app.RequireWorld();
            var prefab = ZNetScene.instance.GetPrefab(id);
            if (!prefab || !prefab.GetComponent<ZNetView>() || prefab.GetComponent<Player>()) throw new ArgumentException("Select a registered network prefab other than Player.");
            var player = app.RequirePlayer(); count = Mathf.Clamp(count, 1, 20); level = Mathf.Clamp(level, 1, 999);
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = player.transform.position + player.transform.forward * 4 + player.transform.right * ((i % 5) - 2) * 1.5f + Vector3.up;
                var obj = Object.Instantiate(prefab, pos, Quaternion.identity);
                var character = obj.GetComponent<Character>(); if (character) character.SetLevel(level);
                var item = obj.GetComponent<ItemDrop>(); if (item) item.m_itemData.m_cheated = false;
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
        internal void ApplyBrushPreset(float radius, float amount = -1)
        {
            BrushRadius = Mathf.Clamp(radius, 1, 50);
            if (amount > 0) BrushAmount = Mathf.Clamp(amount, 0.05f, 8);
            app.Report($"Brush preset: radius {BrushRadius:0.#} m" + (amount > 0 ? $", amount {BrushAmount:0.##} m." : "."));
        }
        internal void Terrain(string operation)
        {
            app.RequireWorld();
            var player = app.RequirePlayer();
            Vector3 center = ResolveBrushCenter(player, operation);
            float radius = Mathf.Clamp(BrushRadius, 1, 50);
            float amount = Mathf.Clamp(BrushAmount, 0.05f, 8);
            float feather = Mathf.Clamp(BrushFeather, 0f, 12f);
            if (Precision) amount *= 0.25f;
            bool shape = operation == "raise" || operation == "lower" || operation == "level" || operation == "levellook" || operation == "levelplayer" || operation == "fillholes";
            float search = radius + (shape && feather > 0.01f ? feather : 0f);
            var maps = new List<Heightmap>(); Heightmap.FindHeightmap(center, search, maps);
            if (maps.Count == 0) throw new InvalidOperationException("No loaded terrain in the brush area.");
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
            var owned = new List<(Heightmap map, TerrainComp comp)>();
            foreach (var map in maps)
            {
                var compiler = map.GetAndCreateTerrainCompiler();
                if (!compiler || !Own(compiler.gameObject)) continue;
                owned.Add((map, compiler));
            }
            if (owned.Count == 0) throw new InvalidOperationException("Could not claim ownership of terrain compilers in range (host/loaded tiles only).");
            CaptureUndo(owned, center, search, operation);
            int count = 0;
            foreach (var (map, compiler) in owned)
            {
                if (operation == "reset") ResetCompiler(compiler, map, center, radius, heights: true, paint: true);
                else if (operation == "paint") ApplyPaint(compiler, center, radius);
                else if (operation == "fillholes" && feather <= 0.01f)
                {
                    // Soft raise toward brush center height. Fills dips without a hard plateau.
                    var settings = ShapeSettings(operation: "raise", radius, amount: Math.Max(0.15f, amount), center);
                    settings.m_raisePower = 2f;
                    settings.m_level = true;
                    settings.m_levelRadius = radius;
                    settings.m_levelOffset = 0;
                    DoOp(compiler, center, settings);
                }
                else if (shape && feather > 0.01f)
                    BlendBrush(compiler, map, center, radius, feather, operation, amount);
                else
                {
                    var settings = ShapeSettings(operation, radius, amount, center);
                    if (!settings.m_level && !settings.m_raise && !settings.m_smooth && !settings.m_paintCleared)
                        throw new ArgumentException("Unknown terrain operation.");
                    DoOp(compiler, center, settings);
                }
                count++;
            }
            if (shape && feather > 0.01f && ClutterSystem.instance) ClutterSystem.instance.ResetGrass(center, search);
            string where = BrushAtLook && operation != "levelplayer" ? "at look hit" : "at feet";
            string edge = shape && feather > 0.01f ? $", feather {feather:0.#} m" : "";
            app.Report($"Terrain {operation}: {count}/{maps.Count} tiles, brush {radius:0.#} m {where}{edge}. Persists in world save.");
        }
        internal void UndoTerrain()
        {
            app.RequireWorld();
            if (undo == null) throw new InvalidOperationException("Nothing to undo. Only the last Valheaven brush stroke is stored.");
            int restored = 0;
            foreach (var tile in undo.Tiles)
            {
                if (!tile.Comp || !tile.Map || !Own(tile.Comp.gameObject)) continue;
                var changed = Reflection.Get<bool[]>(tile.Comp, "m_modifiedHeight");
                var level = Reflection.Get<float[]>(tile.Comp, "m_levelDelta");
                var smooth = Reflection.Get<float[]>(tile.Comp, "m_smoothDelta");
                var paint = Reflection.Get<bool[]>(tile.Comp, "m_modifiedPaint");
                var mask = Reflection.Get<Color[]>(tile.Comp, "m_paintMask");
                Array.Copy(tile.ModifiedHeight, changed, Math.Min(changed.Length, tile.ModifiedHeight.Length));
                Array.Copy(tile.LevelDelta, level, Math.Min(level.Length, tile.LevelDelta.Length));
                Array.Copy(tile.SmoothDelta, smooth, Math.Min(smooth.Length, tile.SmoothDelta.Length));
                Array.Copy(tile.ModifiedPaint, paint, Math.Min(paint.Length, tile.ModifiedPaint.Length));
                Array.Copy(tile.PaintMask, mask, Math.Min(mask.Length, tile.PaintMask.Length));
                Reflection.Set(tile.Comp, "m_lastOpPoint", undo.Center);
                Reflection.Set(tile.Comp, "m_lastOpRadius", undo.Radius);
                Reflection.Method(typeof(TerrainComp), "Save", typeof(bool)).Invoke(tile.Comp, new object[] { false });
                tile.Map.Poke(1);
                restored++;
            }
            if (ClutterSystem.instance) ClutterSystem.instance.ResetGrass(undo.Center, undo.Radius);
            string label = undo.Label;
            undo = null;
            app.Report($"Undid last terrain op ({label}): restored {restored} tile(s).");
        }
        private Vector3 ResolveBrushCenter(Player player, string operation)
        {
            if (operation == "levelplayer")
            {
                Vector3 hit = BrushAtLook ? RequireLookHit(player) : player.transform.position;
                return new Vector3(hit.x, player.transform.position.y, hit.z);
            }
            if (BrushAtLook || operation == "levellook") return RequireLookHit(player);
            return player.transform.position;
        }
        private static Vector3 RequireLookHit(Player player)
        {
            Transform eye = GameCamera.instance ? GameCamera.instance.transform : (player.m_eye ? player.m_eye : player.transform);
            int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            if (Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, 250f, mask)) return hit.point;
            throw new InvalidOperationException("No surface under the crosshair within 250 m.");
        }
        private TerrainOp.Settings ShapeSettings(string operation, float radius, float amount, Vector3 center)
        {
            bool level = operation == "level" || operation == "levellook" || operation == "levelplayer";
            bool raise = operation == "raise" || operation == "lower";
            bool smooth = operation == "smooth";
            return new TerrainOp.Settings
            {
                m_paintCleared = false,
                m_square = Square,
                m_level = level,
                m_levelRadius = radius,
                m_levelOffset = 0,
                m_raise = raise,
                m_raiseRadius = radius,
                m_raiseDelta = operation == "lower" ? -amount : amount,
                m_raisePower = Precision ? 2f : 0f,
                m_smooth = smooth,
                m_smoothRadius = radius,
                m_smoothPower = Precision ? 4f : 3f
            };
        }
        private void ApplyPaint(TerrainComp compiler, Vector3 center, float radius)
        {
            var paintType = PaintTypeFromMode(PaintMode);
            float strength = Mathf.Clamp(PaintStrength, 0.1f, 1f);
            var settings = new TerrainOp.Settings
            {
                m_paintCleared = true,
                m_paintType = paintType,
                m_paintRadius = radius,
                m_paintStrength = strength,
                m_paintHeightCheck = false,
                m_square = Square,
                m_level = false,
                m_raise = false,
                m_smooth = false
            };
            DoOp(compiler, center, settings);
        }
        private static TerrainModifier.PaintType PaintTypeFromMode(int mode)
        {
            switch (Mathf.Clamp(mode, 0, 4))
            {
                case 1: return TerrainModifier.PaintType.Cultivate;
                case 2: return TerrainModifier.PaintType.Paved;
                case 3: return TerrainModifier.PaintType.Reset;
                case 4: return TerrainModifier.PaintType.ClearVegetation;
                default: return TerrainModifier.PaintType.Dirt;
            }
        }
        internal static string PaintModeLabel(int mode)
        {
            switch (Mathf.Clamp(mode, 0, 4))
            {
                case 1: return "Cultivate";
                case 2: return "Paved";
                case 3: return "Grass / Vegetation (Reset)";
                case 4: return "Clear vegetation";
                default: return "Dirt";
            }
        }
        private static void DoOp(TerrainComp compiler, Vector3 center, TerrainOp.Settings settings)
        {
            Reflection.Method(typeof(TerrainComp), "DoOperation", typeof(Vector3), typeof(Vector3), typeof(TerrainOp.Settings))
                .Invoke(compiler, new object[] { center, Vector3.zero, settings });
        }
        private void CaptureUndo(List<(Heightmap map, TerrainComp comp)> owned, Vector3 center, float radius, string label)
        {
            var state = new TerrainUndoState { Center = center, Radius = radius, Label = label };
            foreach (var (map, comp) in owned)
            {
                var changed = Reflection.Get<bool[]>(comp, "m_modifiedHeight");
                var level = Reflection.Get<float[]>(comp, "m_levelDelta");
                var smooth = Reflection.Get<float[]>(comp, "m_smoothDelta");
                var paint = Reflection.Get<bool[]>(comp, "m_modifiedPaint");
                var mask = Reflection.Get<Color[]>(comp, "m_paintMask");
                state.Tiles.Add(new TerrainUndoState.Tile
                {
                    Comp = comp,
                    Map = map,
                    ModifiedHeight = (bool[])changed.Clone(),
                    LevelDelta = (float[])level.Clone(),
                    SmoothDelta = (float[])smooth.Clone(),
                    ModifiedPaint = (bool[])paint.Clone(),
                    PaintMask = (Color[])mask.Clone()
                });
            }
            undo = state;
        }
        private void ResetCompiler(TerrainComp comp, Heightmap map, Vector3 center, float radius, bool heights, bool paint)
        {
            var changed = Reflection.Get<bool[]>(comp, "m_modifiedHeight");
            var level = Reflection.Get<float[]>(comp, "m_levelDelta"); var smooth = Reflection.Get<float[]>(comp, "m_smoothDelta");
            var paintFlags = Reflection.Get<bool[]>(comp, "m_modifiedPaint");
            int pitch = map.m_width + 1;
            bool InBrush(Vector3 pos) => Square ? Mathf.Abs(pos.x - center.x) <= radius && Mathf.Abs(pos.z - center.z) <= radius : new Vector2(pos.x - center.x, pos.z - center.z).sqrMagnitude <= radius * radius;
            for (int y = 0; y < pitch; y++) for (int x = 0; x < pitch; x++)
            {
                int index = y * pitch + x;
                Vector3 pos = map.transform.position + new Vector3((x - map.m_width / 2) * map.m_scale, 0, (y - map.m_width / 2) * map.m_scale);
                if (heights && InBrush(pos)) { changed[index] = false; level[index] = 0; smooth[index] = 0; }
                if (paint && index < paintFlags.Length && InBrush(map.VertexMaskToWorld(x, y))) paintFlags[index] = false;
            }
            Reflection.Set(comp, "m_lastOpPoint", center); Reflection.Set(comp, "m_lastOpRadius", radius);
            Reflection.Method(typeof(TerrainComp), "Save", typeof(bool)).Invoke(comp, new object[] { false });
            map.Poke(1);
            if (ClutterSystem.instance) ClutterSystem.instance.ResetGrass(center, radius);
        }
        internal struct TerrainPad
        {
            public float MinX, MaxX, MinZ, MaxZ, TargetY, Feather;
        }
        internal bool HasPad;
        internal TerrainPad LastPad;
        internal bool LastPerPiece;
        private List<TerrainPad> lastSupports;

        internal void RememberPad(TerrainPad pad)
        {
            LastPad = pad;
            HasPad = true;
        }

        internal void RememberSupports(List<TerrainPad> pads)
        {
            lastSupports = pads;
            LastPerPiece = pads != null && pads.Count > 0;
            HasPad = LastPerPiece;
        }

        internal void ClearSupports()
        {
            lastSupports = null;
            LastPerPiece = false;
        }

        internal void LevelLastPaste(bool feather)
        {
            if (LastPerPiece && lastSupports != null && lastSupports.Count > 0)
            {
                float edge = feather ? Mathf.Clamp(Release250.PasteFeather, 0f, 12f) : 0f;
                var copy = new List<TerrainPad>(lastSupports.Count);
                for (int i = 0; i < lastSupports.Count; i++)
                {
                    var pad = lastSupports[i];
                    pad.Feather = edge;
                    copy.Add(pad);
                }
                ApplySupports(copy);
                return;
            }
            if (!HasPad) throw new InvalidOperationException("No paste footprint yet. Confirm a paste first.");
            var flat = LastPad;
            flat.Feather = feather ? Mathf.Clamp(Release250.PasteFeather, 0f, 12f) : 0f;
            ApplyPad(flat);
            app.Report(feather
                ? $"Feathered last paste pad ({flat.Feather:0.#} m edge)."
                : "Leveled last paste pad.");
        }

        internal void ApplyPad(TerrainPad pad)
        {
            app.RequireWorld();
            float feather = Mathf.Clamp(pad.Feather, 0f, 12f);
            Vector3 center = new Vector3((pad.MinX + pad.MaxX) * 0.5f, pad.TargetY, (pad.MinZ + pad.MaxZ) * 0.5f);
            float reach = Mathf.Max(pad.MaxX - pad.MinX, pad.MaxZ - pad.MinZ) * 0.5f + feather + 4f;
            var maps = new List<Heightmap>();
            Heightmap.FindHeightmap(center, reach, maps);
            if (maps.Count == 0) throw new InvalidOperationException("No loaded terrain under the paste.");
            var owned = new List<(Heightmap map, TerrainComp comp)>();
            foreach (var map in maps)
            {
                var compiler = map.GetAndCreateTerrainCompiler();
                if (!compiler || !Own(compiler.gameObject)) continue;
                owned.Add((map, compiler));
            }
            if (owned.Count == 0) throw new InvalidOperationException("Could not claim ownership of terrain compilers in range (host/loaded tiles only).");
            CaptureUndo(owned, center, reach, feather > 0.01f ? "paste-feather" : "paste-level");
            foreach (var (map, compiler) in owned)
                BlendPad(compiler, map, pad, feather, center, reach);
            if (ClutterSystem.instance) ClutterSystem.instance.ResetGrass(center, reach);
        }

        internal void ApplySupports(List<TerrainPad> pads)
        {
            app.RequireWorld();
            if (pads == null || pads.Count == 0) throw new InvalidOperationException("No piece footprints to level.");
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, feather = 0f;
            for (int i = 0; i < pads.Count; i++)
            {
                var pad = pads[i];
                if (pad.MinX < minX) minX = pad.MinX;
                if (pad.MaxX > maxX) maxX = pad.MaxX;
                if (pad.MinZ < minZ) minZ = pad.MinZ;
                if (pad.MaxZ > maxZ) maxZ = pad.MaxZ;
                if (pad.Feather > feather) feather = pad.Feather;
            }
            Vector3 center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            float reach = Mathf.Max(maxX - minX, maxZ - minZ) * 0.5f + feather + 4f;
            var maps = new List<Heightmap>();
            Heightmap.FindHeightmap(center, reach, maps);
            if (maps.Count == 0) throw new InvalidOperationException("No loaded terrain under the paste.");
            var owned = new List<(Heightmap map, TerrainComp comp)>();
            foreach (var map in maps)
            {
                var compiler = map.GetAndCreateTerrainCompiler();
                if (!compiler || !Own(compiler.gameObject)) continue;
                owned.Add((map, compiler));
            }
            if (owned.Count == 0) throw new InvalidOperationException("Could not claim ownership of terrain compilers in range (host/loaded tiles only).");
            var grid = new Dictionary<long, List<int>>();
            const float cell = 4f;
            for (int i = 0; i < pads.Count; i++)
            {
                var pad = pads[i];
                int x0 = Mathf.FloorToInt((pad.MinX - pad.Feather) / cell);
                int x1 = Mathf.FloorToInt((pad.MaxX + pad.Feather) / cell);
                int z0 = Mathf.FloorToInt((pad.MinZ - pad.Feather) / cell);
                int z1 = Mathf.FloorToInt((pad.MaxZ + pad.Feather) / cell);
                for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    long key = ((long)x << 32) | (uint)z;
                    if (!grid.TryGetValue(key, out var bucket))
                    {
                        bucket = new List<int>();
                        grid[key] = bucket;
                    }
                    bucket.Add(i);
                }
            }
            CaptureUndo(owned, center, reach, "paste-pieces");
            foreach (var (map, compiler) in owned)
                BlendSupports(compiler, map, pads, grid);
            if (ClutterSystem.instance) ClutterSystem.instance.ResetGrass(center, reach);
            app.Report($"Leveled ground under {pads.Count} pieces.");
        }

        private static void BlendSupports(TerrainComp comp, Heightmap map, List<TerrainPad> pads, Dictionary<long, List<int>> grid)
        {
            var changed = Reflection.Get<bool[]>(comp, "m_modifiedHeight");
            var level = Reflection.Get<float[]>(comp, "m_levelDelta");
            var smooth = Reflection.Get<float[]>(comp, "m_smoothDelta");
            int pitch = map.m_width + 1;
            const float cellSize = 4f;
            for (int y = 0; y < pitch; y++)
            {
                for (int x = 0; x < pitch; x++)
                {
                    int index = y * pitch + x;
                    if (index >= level.Length || index >= smooth.Length || index >= changed.Length) continue;
                    Vector3 pos = map.transform.position + new Vector3((x - map.m_width / 2f) * map.m_scale, 0f, (y - map.m_width / 2f) * map.m_scale);
                    int cx = Mathf.FloorToInt(pos.x / cellSize);
                    int cz = Mathf.FloorToInt(pos.z / cellSize);
                    long key = ((long)cx << 32) | (uint)cz;
                    if (!grid.TryGetValue(key, out var bucket)) continue;
                    bool inside = false;
                    float target = 0f;
                    float nearDist = float.MaxValue;
                    float nearY = 0f;
                    float nearFeather = 1f;
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var pad = pads[bucket[i]];
                        float dist = OutsideAabb(pos.x, pos.z, pad.MinX, pad.MaxX, pad.MinZ, pad.MaxZ);
                        if (dist <= 0.001f)
                        {
                            if (!inside || pad.TargetY < target) target = pad.TargetY;
                            inside = true;
                        }
                        else if (!inside && dist <= pad.Feather + 0.001f && dist < nearDist)
                        {
                            nearDist = dist;
                            nearY = pad.TargetY;
                            nearFeather = Mathf.Max(0.05f, pad.Feather);
                        }
                    }
                    float worldH = map.GetHeight(x, y) + map.transform.position.y;
                    float desired;
                    if (inside) desired = target;
                    else if (nearDist < float.MaxValue) desired = Mathf.Lerp(nearY, worldH, Mathf.SmoothStep(0f, 1f, nearDist / nearFeather));
                    else continue;
                    if (Mathf.Abs(desired - worldH) > 8.05f) continue;
                    float add = desired - worldH + smooth[index];
                    smooth[index] = 0f;
                    level[index] = Mathf.Clamp(level[index] + add, -8f, 8f);
                    changed[index] = true;
                }
            }
            Reflection.Set(comp, "m_lastOpPoint", map.transform.position);
            Reflection.Set(comp, "m_lastOpRadius", map.m_width * map.m_scale);
            Reflection.Method(typeof(TerrainComp), "Save", typeof(bool)).Invoke(comp, new object[] { false });
            map.Poke(1);
        }

        private void BlendPad(TerrainComp comp, Heightmap map, TerrainPad pad, float feather, Vector3 center, float radius)
        {
            var changed = Reflection.Get<bool[]>(comp, "m_modifiedHeight");
            var level = Reflection.Get<float[]>(comp, "m_levelDelta");
            var smooth = Reflection.Get<float[]>(comp, "m_smoothDelta");
            int pitch = map.m_width + 1;
            for (int y = 0; y < pitch; y++)
            {
                for (int x = 0; x < pitch; x++)
                {
                    int index = y * pitch + x;
                    if (index >= level.Length || index >= smooth.Length || index >= changed.Length) continue;
                    Vector3 pos = map.transform.position + new Vector3((x - map.m_width / 2f) * map.m_scale, 0f, (y - map.m_width / 2f) * map.m_scale);
                    float dist = OutsideAabb(pos.x, pos.z, pad.MinX, pad.MaxX, pad.MinZ, pad.MaxZ);
                    if (dist > feather + 0.001f) continue;
                    float worldH = map.GetHeight(x, y) + map.transform.position.y;
                    float t = feather <= 0.001f || dist <= 0f ? 0f : Mathf.SmoothStep(0f, 1f, dist / feather);
                    float desired = Mathf.Lerp(pad.TargetY, worldH, t);
                    float add = desired - worldH + smooth[index];
                    smooth[index] = 0f;
                    level[index] = Mathf.Clamp(level[index] + add, -8f, 8f);
                    changed[index] = true;
                }
            }
            Reflection.Set(comp, "m_lastOpPoint", center);
            Reflection.Set(comp, "m_lastOpRadius", radius);
            Reflection.Method(typeof(TerrainComp), "Save", typeof(bool)).Invoke(comp, new object[] { false });
            map.Poke(1);
        }

        private void BlendBrush(TerrainComp comp, Heightmap map, Vector3 center, float radius, float feather, string operation, float amount)
        {
            var changed = Reflection.Get<bool[]>(comp, "m_modifiedHeight");
            var level = Reflection.Get<float[]>(comp, "m_levelDelta");
            var smooth = Reflection.Get<float[]>(comp, "m_smoothDelta");
            if (changed == null || level == null || smooth == null) return;
            bool fill = operation == "fillholes";
            bool raise = operation == "raise" || operation == "lower" || fill;
            float delta = operation == "lower" ? -amount : amount;
            int pitch = map.m_width + 1;
            for (int y = 0; y < pitch; y++)
            {
                for (int x = 0; x < pitch; x++)
                {
                    int index = y * pitch + x;
                    if (index >= level.Length || index >= smooth.Length || index >= changed.Length) continue;
                    Vector3 pos = map.transform.position + new Vector3((x - map.m_width / 2f) * map.m_scale, 0f, (y - map.m_width / 2f) * map.m_scale);
                    float dist = OutsideBrush(pos, center, radius);
                    if (dist > feather + 0.001f) continue;
                    float worldH = map.GetHeight(x, y) + map.transform.position.y;
                    float t = dist <= 0f ? 0f : Mathf.SmoothStep(0f, 1f, dist / Mathf.Max(0.05f, feather));
                    float goal;
                    if (!raise) goal = center.y;
                    else if (fill)
                    {
                        if (worldH >= center.y - 0.02f) continue;
                        goal = Mathf.Min(center.y, worldH + amount);
                    }
                    else goal = worldH + delta;
                    float desired = Mathf.Lerp(goal, worldH, t);
                    if (Mathf.Abs(desired - worldH) > 8.05f) continue;
                    float add = desired - worldH + smooth[index];
                    smooth[index] = 0f;
                    level[index] = Mathf.Clamp(level[index] + add, -8f, 8f);
                    changed[index] = true;
                }
            }
            Reflection.Set(comp, "m_lastOpPoint", center);
            Reflection.Set(comp, "m_lastOpRadius", radius + feather);
            Reflection.Method(typeof(TerrainComp), "Save", typeof(bool)).Invoke(comp, new object[] { false });
            map.Poke(1);
        }

        private float OutsideBrush(Vector3 pos, Vector3 center, float radius)
        {
            if (Square) return OutsideAabb(pos.x, pos.z, center.x - radius, center.x + radius, center.z - radius, center.z + radius);
            float dx = pos.x - center.x, dz = pos.z - center.z;
            return Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - radius);
        }

        private static float OutsideAabb(float x, float z, float minX, float maxX, float minZ, float maxZ)
        {
            float dx = x < minX ? minX - x : (x > maxX ? x - maxX : 0f);
            float dz = z < minZ ? minZ - z : (z > maxZ ? z - maxZ : 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        internal void ApplyTerrainProfile(BlueprintTerrainDto terrain, Vector3 origin, Quaternion rotation)
        {
            app.RequireWorld();
            if (terrain == null || terrain.heights == null || terrain.width < 2)
                throw new InvalidOperationException("No terrain profile on the clipboard.");
            int width = terrain.width;
            if (terrain.heights.Length != width * width)
                throw new InvalidOperationException("Terrain profile size mismatch.");
            float spacing = Mathf.Clamp(terrain.spacing <= 0f ? 1f : terrain.spacing, 0.25f, 4f);
            float feather = 4f;
            float half = (width - 1) * spacing * 0.5f;
            Vector3 localCenter = new Vector3(terrain.originX, 0f, terrain.originZ);
            Vector3 worldCenter = origin + rotation * localCenter;
            float reach = half + feather + 8f;
            var maps = new List<Heightmap>();
            Heightmap.FindHeightmap(worldCenter, reach, maps);
            if (maps.Count == 0) throw new InvalidOperationException("No loaded terrain under the profile.");
            var owned = new List<(Heightmap map, TerrainComp comp)>();
            foreach (var map in maps)
            {
                var compiler = map.GetAndCreateTerrainCompiler();
                if (!compiler || !Own(compiler.gameObject)) continue;
                owned.Add((map, compiler));
            }
            if (owned.Count == 0)
                throw new InvalidOperationException("Could not claim ownership of terrain compilers in range.");
            CaptureUndo(owned, worldCenter, reach, "bbp-terrain");
            var inv = Quaternion.Inverse(rotation);
            foreach (var (map, compiler) in owned)
                BlendTerrainProfile(compiler, map, terrain, origin, inv, spacing, feather);
            if (ClutterSystem.instance) ClutterSystem.instance.ResetGrass(worldCenter, reach);
            app.Report("Applied blueprint terrain profile (" + width + "x" + width + ").");
        }

        private static void BlendTerrainProfile(
            TerrainComp comp, Heightmap map, BlueprintTerrainDto terrain,
            Vector3 origin, Quaternion invRot, float spacing, float feather)
        {
            var changed = Reflection.Get<bool[]>(comp, "m_modifiedHeight");
            var level = Reflection.Get<float[]>(comp, "m_levelDelta");
            var smooth = Reflection.Get<float[]>(comp, "m_smoothDelta");
            int pitch = map.m_width + 1;
            int width = terrain.width;
            float half = (width - 1) * spacing * 0.5f;
            float radius = terrain.radius > 0f ? terrain.radius : half;
            for (int y = 0; y < pitch; y++)
            {
                for (int x = 0; x < pitch; x++)
                {
                    int index = y * pitch + x;
                    if (index >= level.Length || index >= smooth.Length || index >= changed.Length) continue;
                    Vector3 pos = map.transform.position + new Vector3((x - map.m_width / 2f) * map.m_scale, 0f, (y - map.m_width / 2f) * map.m_scale);
                    Vector3 local = invRot * (pos - origin);
                    float lx = local.x - terrain.originX;
                    float lz = local.z - terrain.originZ;
                    float dist = Mathf.Sqrt(lx * lx + lz * lz);
                    if (dist > radius + feather) continue;
                    float gx = lx / spacing + (width - 1) * 0.5f;
                    float gz = lz / spacing + (width - 1) * 0.5f;
                    if (gx < -0.01f || gz < -0.01f || gx > width - 0.99f || gz > width - 0.99f) continue;
                    float targetRel = SampleGridBilinear(terrain.heights, width, gx, gz);
                    float worldH = map.GetHeight(x, y) + map.transform.position.y;
                    float desired = origin.y + targetRel;
                    float t = dist <= radius ? 0f : Mathf.SmoothStep(0f, 1f, (dist - radius) / Mathf.Max(0.05f, feather));
                    desired = Mathf.Lerp(desired, worldH, t);
                    if (Mathf.Abs(desired - worldH) > 8.05f) continue;
                    float add = desired - worldH + smooth[index];
                    smooth[index] = 0f;
                    level[index] = Mathf.Clamp(level[index] + add, -8f, 8f);
                    changed[index] = true;
                }
            }
            Reflection.Set(comp, "m_lastOpPoint", map.transform.position);
            Reflection.Set(comp, "m_lastOpRadius", map.m_width * map.m_scale);
            Reflection.Method(typeof(TerrainComp), "Save", typeof(bool)).Invoke(comp, new object[] { false });
            map.Poke(1);
        }

        private static float SampleGridBilinear(float[] heights, int width, float gx, float gz)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, width - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, width - 1);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int z1 = Mathf.Min(z0 + 1, width - 1);
            float tx = Mathf.Clamp01(gx - x0);
            float tz = Mathf.Clamp01(gz - z0);
            float a = heights[z0 * width + x0];
            float b = heights[z0 * width + x1];
            float c = heights[z1 * width + x0];
            float d = heights[z1 * width + x1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        internal List<Minimap.PinData> Pins()
        {
            if (!Minimap.instance) return new List<Minimap.PinData>();
            return Reflection.Get<List<Minimap.PinData>>(Minimap.instance, "m_pins").Where(p => p.m_save).ToList();
        }
    }
}
