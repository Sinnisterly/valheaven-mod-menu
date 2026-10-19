using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Splatform;
using UnityEngine;

namespace ValheimAdminMenu
{
    /// <summary>Proxy Piece that places the real ZNetScene prefab (Better Creative style).</summary>
    internal sealed class CreativeRecipe : MonoBehaviour
    {
        public GameObject Prefab;
        public bool ClipEverything;
        /// <summary>World location (ZoneSystem) instead of a prefab; see CreativeLocations.</summary>
        public string LocationName;
    }

    /// <summary>Injects ZNetScene prefabs into the Hammer table as free-build proxies (BetterCreative-style, no Jotunn).</summary>
    internal static class CreativeHammer
    {
        private const int PageSize = 90;
        private static readonly HashSet<string> IgnoredNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Player", "Valkyrie", "HelmetOdin", "CapeOdin", "CastleKit_pot03", "Ravens",
            "TERRAIN_TEST", "PlaceMarker", "Circle_section"
        };
        private static readonly HashSet<string> ClipAlways = new HashSet<string>(StringComparer.Ordinal)
        {
            "horizontal_web", "tunnel_web", "dragoneggcup", "SmokeBall", "HeathRockPillar",
            "MountainGraveStone01", "Rock_7", "demister_ball"
        };
        private static readonly HashSet<string> ClipNever = new HashSet<string>(StringComparer.Ordinal)
        {
            "Pickable_SurtlingCoreStand"
        };

        private static PieceTable table;
        private static GameObject root;
        private static readonly List<GameObject> recipes = new List<GameObject>();
        private static List<Piece.PieceCategory> originalCategories;
        private static List<string> originalLabels;
        private static readonly Dictionary<string, Piece.PieceCategory> categories = new Dictionary<string, Piece.PieceCategory>();
        private static readonly Dictionary<string, int> pageCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private static HashSet<string> existingPieceNames;
        private static float nextTry;
        private static int nextCategoryId = (int)Piece.PieceCategory.Max;
        private static readonly Queue<(Piece piece, GameObject visual)> pendingIcons = new Queue<(Piece, GameObject)>();
        private static readonly Dictionary<string, Sprite> renderedIcons = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly List<Texture2D> renderedTextures = new List<Texture2D>();
        private const int IconsPerFrame = 10;
        private const int IconSize = 64;

        internal static bool Enabled => Plugin.Instance && Plugin.Instance.Settings.Data.hammerAllPrefabs;
        internal static int RecipeCount => recipes.Count;

        internal static bool UsingHammer(Player player) =>
            player && player == Player.m_localPlayer && player.GetInventory().GetAllItems()
                .Any(i => i.m_equipped && i.m_dropPrefab && Utils.GetPrefabName(i.m_dropPrefab) == "Hammer");

        internal static void Tick()
        {
            if (!Enabled) { if (root) Clear(); return; }
            if (root && table)
            {
                DrainIconQueue();
                return;
            }
            if (Time.realtimeSinceStartup < nextTry) return;
            nextTry = Time.realtimeSinceStartup + 2f;
            if (!ObjectDB.instance || !ZNetScene.instance) return;
            var hammer = ObjectDB.instance.GetItemPrefab("Hammer");
            if (!hammer) return;
            table = hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
            if (!table) return;

            root = new GameObject("Valheaven creative hammer recipes");
            root.SetActive(false);
            originalCategories = new List<Piece.PieceCategory>(table.m_categories);
            originalLabels = new List<string>(table.m_categoryLabels);
            categories.Clear();
            pageCounts.Clear();
            pendingIcons.Clear();
            nextCategoryId = (int)Piece.PieceCategory.Max;
            existingPieceNames = CollectExistingPieceNames();

            var icon = hammer.GetComponent<ItemDrop>().m_itemData.GetIcon();
            var list = ZNetScene.instance.m_prefabs
                .Where(p => p && !p.transform.parent && !ShouldIgnore(p))
                .GroupBy(p => p.name)
                .Select(g => g.First())
                .OrderBy(p => p.name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var prefab in list)
                AddRecipe(prefab, icon);
            var dungeons = list.Where(p => p.GetComponent<DungeonGenerator>()).Select(p => p.name + " (" + p.GetComponent<DungeonGenerator>().m_algorithm + ")").ToList();
            int locations = AddLocationRecipes(icon);
            AppLog.Info("Creative", "Catalog: " + recipes.Count + " recipes from " + list.Count + " prefabs and " + locations + " world locations; "
                + dungeons.Count + " dungeon generators: " + string.Join(", ", dungeons));

            // Vanilla only makes 9 category buckets; custom ids start at Max. Without growing, UpdateAvailable throws and hammer sticks in repair.
            EnsureTableCapacity(table, nextCategoryId);

            if (Player.m_localPlayer)
                Reflection.Method(typeof(Player), "UpdateAvailablePiecesList").Invoke(Player.m_localPlayer, null);
        }

        /// <summary>Vanilla only makes 9 category buckets; we grow the arrays.</summary>
        internal static void EnsureTableCapacity(PieceTable pt, int categoryCount)
        {
            if (!pt || categoryCount <= 9) return;
            var field = AccessTools.Field(typeof(PieceTable), "m_availablePiecesByCategory");
            if (field == null) return;
            if (!(field.GetValue(pt) is List<List<Piece>> lists)) return;
            while (lists.Count < categoryCount)
                lists.Add(new List<Piece>());

            if (pt.m_selectedPiece == null || pt.m_selectedPiece.Length < categoryCount)
            {
                var next = new Vector2Int[categoryCount];
                if (pt.m_selectedPiece != null)
                    Array.Copy(pt.m_selectedPiece, next, Math.Min(pt.m_selectedPiece.Length, next.Length));
                pt.m_selectedPiece = next;
            }
            if (pt.m_lastSelectedPiece == null || pt.m_lastSelectedPiece.Length < categoryCount)
            {
                var next = new Vector2Int[categoryCount];
                if (pt.m_lastSelectedPiece != null)
                    Array.Copy(pt.m_lastSelectedPiece, next, Math.Min(pt.m_lastSelectedPiece.Length, next.Length));
                pt.m_lastSelectedPiece = next;
            }
        }

        internal static void Refresh()
        {
            Clear();
            nextTry = 0;
            Tick();
            Plugin.Instance.Report("Hammer catalog refreshed: " + recipes.Count + " prefabs.");
        }

        internal static void Clear()
        {
            if (table)
            {
                foreach (var recipe in recipes) table.m_pieces.Remove(recipe);
                if (originalCategories != null)
                {
                    table.m_categories.Clear();
                    table.m_categories.AddRange(originalCategories);
                }
                if (originalLabels != null)
                {
                    table.m_categoryLabels.Clear();
                    table.m_categoryLabels.AddRange(originalLabels);
                }
            }
            recipes.Clear();
            categories.Clear();
            pageCounts.Clear();
            pendingIcons.Clear();
            existingPieceNames = null;
            ClearRenderedIcons();
            if (root) UnityEngine.Object.Destroy(root);
            root = null;
            table = null;
            if (Player.m_localPlayer)
                Reflection.Method(typeof(Player), "UpdateAvailablePiecesList").Invoke(Player.m_localPlayer, null);
        }

        private static void ClearRenderedIcons()
        {
            foreach (var tex in renderedTextures)
                if (tex) UnityEngine.Object.Destroy(tex);
            renderedTextures.Clear();
            foreach (var sprite in renderedIcons.Values)
                if (sprite) UnityEngine.Object.Destroy(sprite);
            renderedIcons.Clear();
        }

        internal static bool IsOurTable(PieceTable value) => Enabled && table && table == value;

        internal static void Place(Player player, CreativeRecipe recipe, Vector3 pos, Quaternion rot)
        {
            if (!Enabled || !UsingHammer(player))
                throw new InvalidOperationException("Enable Hammer all prefabs and equip the hammer.");
            if (!PrivateArea.CheckAccess(pos, 0, true, true))
                throw new InvalidOperationException("Placement is blocked by a ward.");
            if (recipe && !string.IsNullOrEmpty(recipe.LocationName))
            {
                CreativeLocations.Place(player, recipe.LocationName, pos, rot);
                return;
            }
            if (!recipe || !recipe.Prefab)
                throw new InvalidOperationException("Missing prefab.");

            // Live ZNetScene entry so Characters/spawners get a proper prefab hash.
            GameObject source = recipe.Prefab;
            string prefabName = Utils.GetPrefabName(recipe.Prefab);
            if (ZNetScene.instance)
            {
                var named = ZNetScene.instance.GetPrefab(prefabName);
                if (named) source = named;
            }

            // Creatures: snap to ground or they fall through and look dead.
            bool isCreature = source.GetComponent<Character>() || source.GetComponentInChildren<Character>(true);
            if (isCreature)
                pos = SnapToGround(pos, 2f);

            // Yaw-only for creatures; full ghost rot for cliffs/props.
            if (isCreature)
                rot = Quaternion.Euler(0f, rot.eulerAngles.y, 0f);

            var obj = UnityEngine.Object.Instantiate(source, pos, rot);
            // A few ZNetScene prefabs are stored inactive; the clone would sit hidden with no ZDO.
            if (!obj.activeSelf) obj.SetActive(true);
            EnsureSingleZNetView(obj);

            var view = obj.GetComponent<ZNetView>() ?? obj.GetComponentInChildren<ZNetView>(true);
            if (view)
            {
                view.m_persistent = true;
                if (view.IsValid())
                {
                    if (!view.IsOwner()) view.ClaimOwnership();
                    var zdo = view.GetZDO();
                    if (zdo != null)
                    {
                        zdo.Persistent = true;
                        try { zdo.Set(ZDOVars.s_spawnPoint, obj.transform.position); } catch (Exception caught) { AppLog.Swallow(caught); }
                        try
                        {
                            if (zdo.GetLong(ZDOVars.s_spawnTime, 0L) == 0L && ZNet.instance != null)
                                zdo.Set(ZDOVars.s_spawnTime, ZNet.instance.GetTime().Ticks);
                        }
                        catch (Exception caught) { AppLog.Swallow(caught); }
                    }
                }
                else
                    AppLog.Warning("Creative place: ZDO not ready for " + source.name);
            }
            // Some cliffs/props have no ZNetView; still place locally.

            var piece = obj.GetComponent<Piece>() ?? obj.AddComponent<Piece>();
            ConfigurePlacedPiece(piece, recipe.ClipEverything || WantsClipEverything(source));
            if (view && view.IsValid())
            {
                PlatformUserID platformUser = default;
                try { platformUser = PlatformManager.DistributionPlatform.LocalUser.PlatformUserID; } catch (Exception caught) { AppLog.Swallow(caught); }
                try { piece.SetCreator(player.GetPlayerID(), platformUser); } catch (Exception caught) { AppLog.Swallow(caught); }
            }
            obj.GetComponent<WearNTear>()?.OnPlaced();

            try
            {
                var placed = obj.GetComponentsInChildren<IPlaced>(true);
                if (placed != null)
                    foreach (var p in placed) p?.OnPlaced();
            }
            catch (Exception caught) { AppLog.Swallow(caught); }

            ApplyCreatureBrain(obj);
            var spawner = obj.GetComponent<CreatureSpawner>();
            if (spawner) KeepPlacedSpawner(spawner, prefabName);
            var dungeon = obj.GetComponent<DungeonGenerator>();
            if (dungeon) CreativeDungeon.Build(dungeon, prefabName, rot);
            // Horizontal: loading is by zone, so a dungeon 5000 m up is still "here".
            var offset = obj.transform.position - player.transform.position;
            offset.y = 0f;
            float away = offset.magnitude;
            AppLog.Info("Creative placed " + prefabName + " at " + obj.transform.position.ToString("F1") + (away > 60f ? " (" + away.ToString("F0") + " m from you)" : "") + ".");
            // ZNetScene unloads instances outside the active zones (~100+ m); the ZDO stays and loads when you get there.
            if (away > 120f) AppLog.Warning("Creative", prefabName + " landed " + away.ToString("F0") + " m away, outside the loaded area; it unloads now and reappears there when you get close.");
            int id = obj.GetInstanceID();
            lock (Recent) { if (Recent.Count > 64) Recent.Clear(); Recent[id] = prefabName; }
            if (Plugin.Instance) Plugin.Instance.StartCoroutine(VerifyPlaced(obj, id, prefabName, DescribeScripts(obj), away > 120f));
        }

        // Just-placed objects, so CreativeDestroyTracePatch can name whatever destroys one in its first seconds.
        private static readonly Dictionary<int, string> Recent = new Dictionary<int, string>();
        internal static bool TryRecent(GameObject go, out string prefabName)
        {
            prefabName = null;
            lock (Recent) return Recent.Count > 0 && go && Recent.TryGetValue(go.GetInstanceID(), out prefabName);
        }

        /// <summary>
        /// Vanilla CreatureSpawner deletes itself on its first tick when the Location around it lists its spawn group in
        /// m_blockSpawnGroups (dungeon/tower variants). A placed spawner is deliberate: pre-mark the location as checked
        /// (level overrides still apply). Placement and, for creator-stamped spawners after reload, CreativeSpawnerKeepPatch.
        /// </summary>
        internal static void KeepPlacedSpawner(CreatureSpawner spawner, string prefabName)
        {
            try
            {
                var loc = Location.GetLocation(spawner.transform.position);
                Reflection.Set(spawner, "m_location", loc);
                Reflection.Set(spawner, "m_checkedLocation", true);
                bool blocked = loc && loc.m_blockSpawnGroups != null && loc.m_blockSpawnGroups.Contains(spawner.m_spawnGroupID);
                if (blocked)
                    AppLog.Info("Creative", "Kept " + prefabName + ": location " + Utils.GetPrefabName(loc.gameObject) + " blocks spawn group " + spawner.m_spawnGroupID + ", which makes vanilla delete the spawner.");
                else
                    AppLog.Debug("Creative", prefabName + " spawner: group " + spawner.m_spawnGroupID + ", location " + (loc ? Utils.GetPrefabName(loc.gameObject) : "none") + ", interval " + spawner.m_spawnInterval + "s, day " + spawner.m_spawnAtDay + ", night " + spawner.m_spawnAtNight);
            }
            catch (Exception caught) { AppLog.Swallow(caught); }
        }

        /// <summary>Report placements that vanish or show nothing, with their scripts, so failing prefabs can be named.</summary>
        private static IEnumerator VerifyPlaced(GameObject obj, int id, string prefabName, string scripts, bool far)
        {
            yield return new WaitForSecondsRealtime(2f);
            lock (Recent) Recent.Remove(id);
            string problem = null;
            if (!obj) problem = far ? "unloaded (placed outside the loaded area)" : "removed itself right after placing (no ZNetScene.Destroy trace above = direct Object.Destroy)";
            else if (!obj.activeInHierarchy) problem = "was hidden right after placing";
            // Generators: camps spawn their visible pieces as separate network objects, not children.
            else if (!obj.GetComponent<DungeonGenerator>() && !HasVisibleGeometry(obj, includeParticles: true)) problem = "placed but has nothing visible";
            if (problem == null) yield break;
            AppLog.Warning("Creative " + prefabName + " " + problem + " (scripts: " + scripts + ").");
            Plugin.Instance?.Report(prefabName + " " + problem + ". Details in Valheaven.log.");
        }

        private static string DescribeScripts(GameObject obj)
        {
            var names = new List<string>();
            foreach (var behaviour in obj.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!behaviour) continue;
                string n = behaviour.GetType().Name;
                if (!names.Contains(n)) names.Add(n);
                if (names.Count >= 14) break;
            }
            return names.Count == 0 ? "none" : string.Join(", ", names);
        }

        /// <summary>Enabled, active, non-empty renderers (particles optional; vanilla ghosts hide them).</summary>
        internal static bool HasVisibleGeometry(GameObject go, bool includeParticles)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r is ParticleSystemRenderer && !includeParticles) continue;
                if (r.bounds.size.sqrMagnitude > 0.0001f) return true;
            }
            return false;
        }

        // Ghost proxies keep only renderers; meshes a script would switch on, or particle-only props, render nothing.
        private static readonly HashSet<string> markerLogged = new HashSet<string>(StringComparer.Ordinal);
        private static Material markerMaterial;

        internal static void EnsureGhostVisible(GameObject ghost)
        {
            if (!ghost || HasVisibleGeometry(ghost, includeParticles: false)) return;
            if (ActivateHiddenVisual(ghost) && HasVisibleGeometry(ghost, includeParticles: false)) return;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "VHCreativeMarker";
            var col = marker.GetComponent<Collider>();
            if (col) UnityEngine.Object.DestroyImmediate(col);
            marker.layer = ghost.layer;
            marker.transform.SetParent(ghost.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one * 0.8f;
            var mr = marker.GetComponent<MeshRenderer>();
            if (mr)
            {
                if (!markerMaterial)
                {
                    var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                    if (shader) markerMaterial = new Material(shader) { color = new Color(.35f, .85f, 1f, .45f) };
                }
                if (markerMaterial) mr.sharedMaterial = markerMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            string key = ghost.name;
            if (markerLogged.Add(key))
                AppLog.Info("Creative ghost " + key + " has no visible mesh (particles/scripted visuals); showing a marker cube.");
        }

        /// <summary>
        /// UpdatePlacementGhost offsets the ghost by its nearest enabled solid collider, starting from world origin. With none
        /// (spawner visuals borrow creature colliders, which SetupPlacementGhost disables by layer) the piece lands at
        /// 2x the aim point + 50 m up, outside the loaded area, and unloads at once. Give such ghosts a box from render bounds.
        /// </summary>
        internal static void EnsureGhostCollider(GameObject ghost)
        {
            if (!ghost) return;
            foreach (var c in ghost.GetComponentsInChildren<Collider>())
                if (c && c.enabled && !c.isTrigger && (!(c is MeshCollider mesh) || mesh.convex)) return;
            var box = ghost.AddComponent<BoxCollider>();
            var bounds = EncapsulateRenderBounds(ghost);
            var s = ghost.transform.lossyScale;
            if (bounds.size.sqrMagnitude > 0.0001f && Mathf.Abs(s.x * s.y * s.z) > 1e-6f)
            {
                box.center = ghost.transform.InverseTransformPoint(bounds.center);
                box.size = new Vector3(bounds.size.x / Mathf.Abs(s.x), bounds.size.y / Mathf.Abs(s.y), bounds.size.z / Mathf.Abs(s.z));
            }
            else { box.center = new Vector3(0f, .25f, 0f); box.size = Vector3.one * .5f; }
            AppLog.Debug("Creative", ghost.name + " ghost had no solid collider; added box " + box.size.ToString("F2") + " @ " + box.center.ToString("F2"));
        }

        /// <summary>Turn on the first inactive child that carries a mesh (variants a script would normally pick).</summary>
        private static bool ActivateHiddenVisual(GameObject ghost)
        {
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
            {
                if (!r || r is ParticleSystemRenderer || r.gameObject.activeInHierarchy) continue;
                if (r is MeshRenderer && (!r.GetComponent<MeshFilter>() || !r.GetComponent<MeshFilter>().sharedMesh)) continue;
                for (var t = r.transform; t && t != ghost.transform; t = t.parent)
                    if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                return true;
            }
            return false;
        }

        private static Vector3 SnapToGround(Vector3 pos, float lift)
        {
            int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out var hit, 120f, mask, QueryTriggerInteraction.Ignore))
                return hit.point;
            if (ZoneSystem.instance != null)
            {
                float y = ZoneSystem.instance.GetGroundHeight(pos);
                if (y > pos.y - 40f && y < pos.y + 40f)
                    return new Vector3(pos.x, y, pos.z);
            }
            return pos + Vector3.up * lift;
        }

        private static void EnsureSingleZNetView(GameObject obj)
        {
            var views = obj.GetComponents<ZNetView>();
            for (int i = 1; i < views.Length; i++)
                UnityEngine.Object.DestroyImmediate(views[i]);
        }

        /// <summary>Full AI = hunt/fight. Off = disable BaseAI, idle anims (statue mode).</summary>
        private static void ApplyCreatureBrain(GameObject obj)
        {
            var character = obj.GetComponent<Character>() ?? obj.GetComponentInChildren<Character>(true);
            if (!character) return;

            bool fullAi = Plugin.Instance == null || Plugin.Instance.Settings.Data.hammerCreatureFullAi;
            // Ghosts/night or raid spawns walk off and despawn in daylight or outside an event, so they seem to never spawn.
            foreach (var monster in obj.GetComponentsInChildren<MonsterAI>(true))
            {
                if (!monster) continue;
                try { monster.SetDespawnInDay(false); } catch (Exception caught) { AppLog.Swallow(caught); }
                try { monster.SetEventCreature(false); } catch (Exception caught) { AppLog.Swallow(caught); }
            }
            var brains = obj.GetComponentsInChildren<BaseAI>(true);
            foreach (var ai in brains)
            {
                if (!ai) continue;
                ai.enabled = fullAi;
                if (fullAi)
                {
                    try { ai.ResetPatrolPoint(); } catch (Exception caught) { AppLog.Swallow(caught); }
                }
            }

            if (fullAi) return;

            // Freeze motion; keep Animator for idle loops.
            var body = character.GetComponent<Rigidbody>();
            if (body)
            {
                try { body.linearVelocity = Vector3.zero; } catch (Exception caught) { AppLog.Swallow(caught); }
                try { body.angularVelocity = Vector3.zero; } catch (Exception caught) { AppLog.Swallow(caught); }
                body.constraints = RigidbodyConstraints.FreezeAll;
            }
            try { character.SetHealth(character.GetMaxHealth()); } catch (Exception caught) { AppLog.Swallow(caught); }
        }

        /// <summary>NPC/spawner colliders fail CheckPlacementGhostVSPlayers even when the marker is green.</summary>
        internal static void ForceGhostValid(Player player)
        {
            if (!player) return;
            try { Reflection.Set(player, "m_placementStatus", 0); } catch (Exception caught) { AppLog.Swallow(caught); }
            try
            {
                var ghost = Reflection.Get<GameObject>(player, "m_placementGhost");
                var piece = ghost ? ghost.GetComponent<Piece>() : null;
                if (piece) piece.SetInvalidPlacementHeightlight(false);
            }
            catch (Exception caught) { AppLog.Swallow(caught); }
        }

        private static void AddRecipe(GameObject prefab, Sprite fallback)
        {
            bool prior = ZNetView.m_forceDisableInit;
            ZNetView.m_forceDisableInit = true;
            try
            {
                var proxy = UnityEngine.Object.Instantiate(prefab, root.transform, false);
                proxy.name = "VHCreative_" + prefab.name;
                if (!PrepareGhostProxy(proxy, prefab))
                {
                    // A surviving Character/AI in the ghost leaks into Character.s_characters and breaks AI + placement.
                    AppLog.Warning("Creative hammer skipped " + prefab.name + ": gameplay scripts could not be stripped.");
                    UnityEngine.Object.DestroyImmediate(proxy);
                    return;
                }

                var piece = proxy.GetComponent<Piece>() ?? proxy.AddComponent<Piece>();
                bool clip = WantsClipEverything(prefab);
                ConfigurePlacedPiece(piece, clip);
                piece.m_category = CategoryFor(prefab);
                piece.m_name = FriendlyName(prefab);
                if (Localization.instance != null)
                    piece.m_name = Localization.instance.Localize(piece.m_name);
                piece.m_description = prefab.name;
                piece.m_icon = ResolveIcon(prefab, fallback);
                piece.m_resources = new Piece.Requirement[0];
                piece.m_craftingStation = null;
                piece.m_dlc = "";

                if (piece.m_icon == fallback || !piece.m_icon)
                    pendingIcons.Enqueue((piece, VisualSubject(prefab)));

                var marker = proxy.AddComponent<CreativeRecipe>();
                marker.Prefab = prefab;
                marker.ClipEverything = clip;
                proxy.SetActive(true);
                recipes.Add(proxy);
                table.m_pieces.Add(proxy);
            }
            finally { ZNetView.m_forceDisableInit = prior; }
        }

        /// <summary>
        /// World locations (troll caves, burial chambers, frost caves, camps, altars...) are soft-referenced location
        /// prefabs, not ZNetScene prefabs. One "Locations" recipe each; the ghost is a footprint disc of the exterior radius.
        /// </summary>
        private static int AddLocationRecipes(Sprite icon)
        {
            var zs = ZoneSystem.instance;
            if (zs == null || zs.m_locations == null || zs.m_locations.Count == 0)
            {
                AppLog.Warning("Creative", "World locations not ready; Hammer catalog > Refresh adds them later.");
                return 0;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int added = 0;
            foreach (var zl in zs.m_locations.OrderBy(l => l?.m_prefab.Name ?? "", StringComparer.OrdinalIgnoreCase))
            {
                if (zl == null || !zl.m_enable || !zl.m_prefab.IsValid) continue;
                string name = zl.m_prefab.Name;
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                try
                {
                    var proxy = new GameObject("VHCreative_Loc_" + name);
                    proxy.transform.SetParent(root.transform, false);
                    CreativeLocations.AddFootprint(proxy, Mathf.Max(zl.m_exteriorRadius, zl.m_interiorRadius));
                    var piece = proxy.AddComponent<Piece>();
                    ConfigurePlacedPiece(piece, true);
                    piece.m_category = CategoryFor("Locations");
                    piece.m_name = CreativeLocations.DisplayName(name);
                    piece.m_description = name + (zl.m_interiorRadius > 0f ? " (has an interior)" : "") + ", radius " + zl.m_exteriorRadius.ToString("F0") + " m";
                    piece.m_icon = icon;
                    piece.m_resources = new Piece.Requirement[0];
                    piece.m_craftingStation = null;
                    piece.m_dlc = "";
                    var marker = proxy.AddComponent<CreativeRecipe>();
                    marker.LocationName = name;
                    marker.ClipEverything = true;
                    recipes.Add(proxy);
                    table.m_pieces.Add(proxy);
                    added++;
                }
                catch (Exception caught) { AppLog.Swallow(caught); }
            }
            return added;
        }

        /// <summary>Keep meshes/colliders for the ghost; strip gameplay. Meshless spawners get a child from the creature prefab.</summary>
        private static bool PrepareGhostProxy(GameObject proxy, GameObject sourcePrefab)
        {
            if (!StripToVisual(proxy)) return false;

            if (!proxy.GetComponentInChildren<Renderer>(true))
            {
                var visual = VisualSubject(sourcePrefab);
                if (visual && visual != sourcePrefab)
                {
                    bool prior = ZNetView.m_forceDisableInit;
                    ZNetView.m_forceDisableInit = true;
                    try
                    {
                        var child = UnityEngine.Object.Instantiate(visual, proxy.transform, false);
                        child.name = "VHCreativeVisual";
                        // Piece on the visual would be a second ghost piece; only the proxy root keeps one.
                        foreach (var extra in child.GetComponentsInChildren<Piece>(true))
                            if (extra) UnityEngine.Object.DestroyImmediate(extra);
                        if (!StripToVisual(child)) return false;
                        child.transform.localPosition = Vector3.zero;
                        child.transform.localRotation = Quaternion.identity;
                    }
                    finally { ZNetView.m_forceDisableInit = prior; }
                }
            }

            if (!proxy.GetComponentInChildren<Collider>(true))
            {
                var box = proxy.AddComponent<BoxCollider>();
                var bounds = EncapsulateRenderBounds(proxy);
                if (bounds.size.sqrMagnitude > 0.0001f)
                {
                    box.center = proxy.transform.InverseTransformPoint(bounds.center);
                    box.size = bounds.size;
                }
                else box.size = Vector3.one;
            }
            return true;
        }

        /// <summary>
        /// Strip everything but visuals/colliders. Reverse order + repeat passes so dependents (CharacterDrop) go before
        /// what they require (Character); one forward pass left live Humanoids in ghosts. False if any script survives.
        /// </summary>
        internal static bool StripToVisual(GameObject go)
        {
            for (int pass = 0; pass < 8; pass++)
            {
                var components = go.GetComponentsInChildren<Component>(true);
                bool removed = false;
                for (int i = components.Length - 1; i >= 0; i--)
                {
                    var component = components[i];
                    if (!component || KeepForVisual(component)) continue;
                    try { UnityEngine.Object.DestroyImmediate(component); }
                    catch (Exception caught) { AppLog.Swallow(caught); }
                    if (!component) removed = true;
                }
                if (!removed) break;
            }
            foreach (var component in go.GetComponentsInChildren<Component>(true))
                if (component && !KeepForVisual(component)) return false;
            return true;
        }

        // Animator stays so skinned meshes still draw in the ghost.
        private static bool KeepForVisual(Component c) =>
            c is Transform || c is Renderer || c is MeshFilter || c is LODGroup || c is Collider || c is Animator
            || c is Piece || c is CreativeRecipe;

        private static GameObject VisualSubject(GameObject prefab)
        {
            if (!prefab) return prefab;
            var spawner = prefab.GetComponent<CreatureSpawner>();
            if (spawner && spawner.m_creaturePrefab) return spawner.m_creaturePrefab;
            var area = prefab.GetComponent<SpawnArea>();
            if (area?.m_prefabs != null && area.m_prefabs.Count > 0 && area.m_prefabs[0].m_prefab)
                return area.m_prefabs[0].m_prefab;
            var pick = prefab.GetComponent<Pickable>();
            if (pick && pick.m_itemPrefab) return pick.m_itemPrefab;
            var random = prefab.GetComponent<PickableItem>();
            if (random != null)
            {
                try
                {
                    var field = AccessTools.Field(typeof(PickableItem), "m_randomItemPrefabs");
                    if (field?.GetValue(random) is Array arr && arr.Length > 0)
                    {
                        var entry = arr.GetValue(0);
                        var itemField = entry?.GetType().GetField("m_itemPrefab");
                        if (itemField?.GetValue(entry) is ItemDrop drop && drop)
                            return drop.gameObject;
                    }
                }
                catch (Exception caught) { AppLog.Swallow(caught); }
            }
            return prefab;
        }

        private static void DrainIconQueue()
        {
            int n = 0;
            while (n < IconsPerFrame && pendingIcons.Count > 0)
            {
                var (piece, visual) = pendingIcons.Dequeue();
                n++;
                if (!piece || !visual) continue;
                try
                {
                    var icon = RenderPrefabIcon(visual);
                    if (icon) piece.m_icon = icon;
                }
                catch (Exception ex) { AppLog.Warning("Creative icon render failed (" + visual.name + "): " + ex.Message); }
            }
        }

        private static Sprite RenderPrefabIcon(GameObject prefab)
        {
            if (!prefab) return null;
            string key = prefab.name;
            if (renderedIcons.TryGetValue(key, out var cached) && cached) return cached;

            bool prior = ZNetView.m_forceDisableInit;
            ZNetView.m_forceDisableInit = true;
            GameObject holder = null;
            GameObject instance = null;
            GameObject camObject = null;
            RenderTexture target = null;
            try
            {
                // Inactive parent: no Awake runs (dungeons, spawners, characters) before scripts are stripped.
                holder = new GameObject("VHCreativeIconHolder");
                holder.SetActive(false);
                instance = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
                instance.name = "VHCreativeIcon_" + prefab.name;
                if (!StripToVisual(instance)) return null;
                foreach (var extra in instance.GetComponentsInChildren<Piece>(true))
                    if (extra) UnityEngine.Object.DestroyImmediate(extra);
                foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                    if (col) col.enabled = false;
                instance.transform.SetParent(null, false);
                instance.transform.position = new Vector3(0f, -8000f, 0f);
                if (!instance.activeSelf) instance.SetActive(true);

                var bounds = EncapsulateRenderBounds(instance);
                if (bounds.size.sqrMagnitude < 0.0001f)
                    return null;

                camObject = new GameObject("VHCreativeIconCamera");
                var camera = camObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.orthographic = true;
                camera.nearClipPlane = 0.01f;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.cullingMask = ~0;
                camera.enabled = false;

                float radius = Mathf.Max(bounds.extents.magnitude, 0.15f);
                camera.orthographicSize = radius * 1.15f;
                camera.farClipPlane = radius * 8f + 10f;
                // Iso-ish angle (same idea as Jotunn RenderManager).
                var offset = new Vector3(0.65f, 0.75f, -0.65f).normalized * (radius * 2.4f);
                camObject.transform.position = bounds.center + offset;
                camObject.transform.LookAt(bounds.center, Vector3.up);

                target = new RenderTexture(IconSize, IconSize, 16, RenderTextureFormat.ARGB32);
                target.Create();
                camera.targetTexture = target;
                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
                RenderTexture.active = previous;
                camera.targetTexture = null;

                var pixels = tex.GetPixels32();
                int opaque = 0;
                for (int i = 0; i < pixels.Length; i++)
                    if (pixels[i].a > 8) opaque++;
                if (opaque < Mathf.Max(8, pixels.Length / 200))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                tex.Apply(false, true);

                var sprite = Sprite.Create(tex, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f), 100f);
                renderedTextures.Add(tex);
                renderedIcons[key] = sprite;
                return sprite;
            }
            finally
            {
                if (target)
                {
                    target.Release();
                    UnityEngine.Object.Destroy(target);
                }
                if (camObject) UnityEngine.Object.Destroy(camObject);
                if (instance) UnityEngine.Object.Destroy(instance);
                if (holder) UnityEngine.Object.Destroy(holder);
                ZNetView.m_forceDisableInit = prior;
            }
        }

        private static Bounds EncapsulateRenderBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in renderers)
            {
                if (!r || !r.enabled) continue;
                if (r is ParticleSystemRenderer) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }

        /// <summary>
        /// Place adds a Piece at runtime, which is not saved: after a reload, creative objects whose prefab has none
        /// (dungeons and their portals, spawners, creatures, cliffs) were invisible to hammer remove. Re-add it.
        /// </summary>
        internal static void RestorePiece(GameObject go)
        {
            var piece = go.AddComponent<Piece>();
            ConfigurePlacedPiece(piece, false);
            piece.m_name = FriendlyName(go);
            AppLog.Debug("Creative", "Restored removable Piece on " + Utils.GetPrefabName(go) + " for hammer remove");
        }

        private static void ConfigurePlacedPiece(Piece piece, bool clipEverything)
        {
            piece.enabled = true;
            piece.m_enabled = true;
            piece.m_canBeRemoved = true;
            piece.m_clipEverything = clipEverything;
            piece.m_groundPiece = false;
            piece.m_groundOnly = false;
            piece.m_noInWater = false;
            piece.m_notOnWood = false;
            piece.m_notOnTiltingSurface = false;
            piece.m_notOnFloor = false;
            piece.m_allowedInDungeons = true;
            piece.m_onlyInTeleportArea = false;
            piece.m_inCeilingOnly = false;
            piece.m_cultivatedGroundOnly = false;
            piece.m_onlyInBiome = Heightmap.Biome.None;
            piece.m_allowRotatedOverlap = true;
            piece.m_spaceRequirement = 0;
            piece.m_noClipping = false;
            piece.m_vegetationGroundOnly = false;
        }

        private static bool WantsClipEverything(GameObject prefab)
        {
            if (ClipNever.Contains(prefab.name)) return false;
            if (ClipAlways.Contains(prefab.name)) return true;
            if (HasAny(prefab, typeof(Character), typeof(Pickable), typeof(PickableItem), typeof(RandomFlyingBird), typeof(Fish), typeof(TombStone))
                || prefab.GetComponent("Odin"))
                return true;
            // Big cliffs/rocks often fail vanilla collision placement.
            string n = prefab.name ?? "";
            if (ContainsAny(n, "cliff", "Cliff", "rock", "Rock", "ashland", "Ashland", "ruin", "Ruin", "stone", "Stone"))
                return true;
            return HasAny(prefab, typeof(Destructible), typeof(MineRock));
        }

        private static Piece.PieceCategory CategoryFor(GameObject prefab) => CategoryFor(BaseCategory(prefab));

        private static Piece.PieceCategory CategoryFor(string baseName)
        {
            if (!pageCounts.TryGetValue(baseName, out int count)) count = 0;
            // BetterCreative: Furniture/Building already fill page 1, so overflow starts at page 2.
            if ((baseName == "Furniture" || baseName == "Building") && count == 0)
                count = PageSize;
            int page = count / PageSize + 1;
            pageCounts[baseName] = count + 1;
            string label = page > 1 ? baseName + " " + page : baseName;
            if (categories.TryGetValue(label, out var existing)) return existing;
            // GetSelectedCategory treats Max (and All) as "unset" and jumps to the first tab, so a category with id Max
            // bounced every selection in it back to Repair.
            while (table.m_categories.Contains((Piece.PieceCategory)nextCategoryId)
                   || nextCategoryId == (int)Piece.PieceCategory.Max
                   || nextCategoryId == (int)Piece.PieceCategory.All)
                nextCategoryId++;
            var cat = (Piece.PieceCategory)nextCategoryId++;
            categories[label] = cat;
            table.m_categories.Add(cat);
            table.m_categoryLabels.Add(label);
            return cat;
        }

        private static string BaseCategory(GameObject prefab)
        {
            var destructible = prefab.GetComponent<Destructible>();
            if (prefab.GetComponent<DungeonGenerator>()) return "Dungeons";
            if (HasAny(prefab, typeof(Pickable), typeof(PickableItem))) return "Pickable";
            if (HasAny(prefab, typeof(Humanoid), typeof(Character), typeof(Leviathan), typeof(RandomFlyingBird), typeof(Fish), typeof(Trader))
                || prefab.GetComponent("Odin") || prefab.GetComponent("Valkyrie"))
                return "NPCs";
            if (HasAny(prefab, typeof(CreatureSpawner), typeof(SpawnArea)) || prefab.GetComponent("TriggerSpawner"))
                return "Spawners";
            string n = prefab.name;
            if (ContainsAny(n, "Bush", "Root", "root", "shrub", "stubbe", "vines", "SwampTree")
                || HasAny(prefab, typeof(TreeBase), typeof(TreeLog))
                || (destructible != null && (int)destructible.m_destructibleType == 2))
                return "Vegetation";
            if (HasAny(prefab, typeof(ArmorStand), typeof(Container), typeof(Fireplace))
                || ContainsAny(n, "groundtorch", "brazier", "cloth_hanging", "banner", "table", "chair", "sign", "bed"))
                return "Furniture";
            if (HasAny(prefab, typeof(WearNTear), typeof(Door))) return "Building";
            if (HasAny(prefab, typeof(Destructible), typeof(MineRock))) return "Destructible";
            if (prefab.GetComponent<ZNetView>()) return "Other";
            return "Extended";
        }

        private static bool ShouldIgnore(GameObject prefab)
        {
            if (!prefab) return true;
            string name = prefab.name;
            if (name.StartsWith("VHPlant_", StringComparison.Ordinal) || name.StartsWith("VHCreative_", StringComparison.Ordinal))
                return true;
            if (!HasVisualOrSpawner(prefab)) return true;
            if (HasComponentByName(prefab, "Projectile", "TimedDestruction", "Ragdoll", "LocationProxy", "Gibber",
                    "MineRock5", "FishingFloat", "Smoke", "TeleportAbility"))
                return true;
            if (prefab.GetComponent<ItemDrop>() && !prefab.GetComponent<Fish>()) return true;
            if (prefab.GetComponent("Aoe") && !HasAny(prefab, typeof(Collider), typeof(MeshCollider), typeof(WearNTear)))
                return true;
            if (name.StartsWith("vfx_", StringComparison.Ordinal) || name.StartsWith("sfx_", StringComparison.Ordinal)
                || name.StartsWith("fx_", StringComparison.Ordinal) || name.StartsWith("_", StringComparison.Ordinal)
                || name.EndsWith("_aoe", StringComparison.OrdinalIgnoreCase))
                return true;
            if (IgnoredNames.Contains(name)) return true;
            // Skip prefabs already on a piece table (leave vanilla hammer pieces alone).
            return existingPieceNames != null && existingPieceNames.Contains(name);
        }

        private static HashSet<string> CollectExistingPieceNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pt in Resources.FindObjectsOfTypeAll<PieceTable>())
            {
                if (!pt || pt.m_pieces == null) continue;
                foreach (var go in pt.m_pieces)
                {
                    if (!go) continue;
                    string n = go.name;
                    if (n.StartsWith("VHPlant_", StringComparison.Ordinal) || n.StartsWith("VHCreative_", StringComparison.Ordinal))
                        continue;
                    names.Add(n);
                }
            }
            return names;
        }

        private static bool HasVisualOrSpawner(GameObject prefab) =>
            prefab.GetComponentInChildren<Collider>(true)
            || prefab.GetComponentInChildren<Renderer>(true)
            || prefab.GetComponent<CreatureSpawner>()
            || prefab.GetComponent<SpawnArea>()
            // Bare generators (most DG_ prefabs: no mesh until rooms are built); see CreativeDungeon.
            || prefab.GetComponent<DungeonGenerator>();

        private static bool HasAny(GameObject go, params Type[] types)
        {
            foreach (var t in types)
                if (go.GetComponent(t) || go.GetComponentInChildren(t, true)) return true;
            return false;
        }

        private static bool HasComponentByName(GameObject go, params string[] names)
        {
            foreach (var n in names)
            {
                if (go.GetComponent(n)) return true;
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                    if (c && string.Equals(c.GetType().Name, n, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool ContainsAny(string value, params string[] parts)
        {
            foreach (var p in parts)
                if (value.IndexOf(p, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        internal static string FriendlyName(GameObject prefab)
        {
            var hover = prefab.GetComponent<HoverText>();
            if (hover && !string.IsNullOrWhiteSpace(hover.m_text)) return hover.m_text;
            var item = prefab.GetComponent<ItemDrop>();
            if (item) return item.m_itemData.m_shared.m_name;
            var character = prefab.GetComponent<Character>();
            if (character && !string.IsNullOrWhiteSpace(character.m_name)) return character.m_name;
            var rune = prefab.GetComponent<RuneStone>();
            if (rune && !string.IsNullOrWhiteSpace(rune.m_name)) return rune.m_name;
            var stand = prefab.GetComponent<ItemStand>();
            if (stand && !string.IsNullOrWhiteSpace(stand.m_name)) return stand.m_name;
            var mine = prefab.GetComponent<MineRock>();
            if (mine && !string.IsNullOrWhiteSpace(mine.m_name)) return mine.m_name;
            var pick = prefab.GetComponent<Pickable>();
            if (pick && pick.m_itemPrefab) return FriendlyName(pick.m_itemPrefab);
            var spawner = prefab.GetComponent<CreatureSpawner>();
            if (spawner && spawner.m_creaturePrefab) return FriendlyName(spawner.m_creaturePrefab);
            var area = prefab.GetComponent<SpawnArea>();
            if (area && area.m_prefabs != null && area.m_prefabs.Count > 0 && area.m_prefabs[0].m_prefab)
                return FriendlyName(area.m_prefabs[0].m_prefab);
            var piece = prefab.GetComponent<Piece>();
            if (piece && !string.IsNullOrWhiteSpace(piece.m_name)) return piece.m_name;
            return NaturalCatalogRules.FriendlyPrefabName(prefab.name);
        }

        private static Sprite ResolveIcon(GameObject prefab, Sprite fallback)
        {
            var piece = prefab.GetComponent<Piece>();
            if (piece && piece.m_icon) return piece.m_icon;
            var item = prefab.GetComponent<ItemDrop>();
            if (item)
            {
                var icon = item.m_itemData.GetIcon();
                if (icon) return icon;
            }
            var pick = prefab.GetComponent<Pickable>();
            if (pick && pick.m_itemPrefab)
            {
                var drop = pick.m_itemPrefab.GetComponent<ItemDrop>();
                var icon = drop?.m_itemData.GetIcon();
                if (icon) return icon;
            }
            var visual = VisualSubject(prefab);
            if (visual && visual != prefab)
            {
                var drop = visual.GetComponent<ItemDrop>();
                var icon = drop?.m_itemData.GetIcon();
                if (icon) return icon;
                var visualPiece = visual.GetComponent<Piece>();
                if (visualPiece && visualPiece.m_icon) return visualPiece.m_icon;
            }
            return fallback;
        }
    }

    [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
    internal static class CreativeRecipeListPatch
    {
        private static void Prefix(PieceTable __instance, ref bool noPlacementCost)
        {
            if (!CreativeHammer.IsOurTable(__instance)) return;
            noPlacementCost = true;
            // Re-grow every refresh; UpdateAvailable assumes at least 9 buckets.
            int needed = 9;
            foreach (var go in __instance.m_pieces)
            {
                var piece = go ? go.GetComponent<Piece>() : null;
                if (!piece) continue;
                int id = (int)piece.m_category;
                if (id >= 0 && id != (int)Piece.PieceCategory.All && id + 1 > needed)
                    needed = id + 1;
            }
            CreativeHammer.EnsureTableCapacity(__instance, needed);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), new[] { typeof(Piece), typeof(Player.RequirementMode) })]
    internal static class CreativeRequirementsPatch
    {
        private static void Postfix(Player __instance, Piece piece, Player.RequirementMode mode, ref bool __result)
        {
            if (!CreativeHammer.Enabled || !CreativeHammer.UsingHammer(__instance) || !piece || !piece.GetComponent<CreativeRecipe>())
                return;
            // Leave IsKnown alone or UpdateKnownRecipesList spams "$msg_newpiece" for every prefab.
            // noPlacementCost still lists them in the build menu.
            if (mode == Player.RequirementMode.IsKnown) return;
            __result = true;
        }
    }

    /// <summary>Blank 15x6 grid cells return null ("$hud_nothingtobuild"); snap to first piece in category.</summary>
    [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.GetSelectedPiece))]
    internal static class CreativeSelectedPiecePatch
    {
        private static void Postfix(PieceTable __instance, ref Piece __result)
        {
            if (__result || !CreativeHammer.IsOurTable(__instance)) return;
            try
            {
                var list = __instance.GetPiecesInSelectedCategory();
                if (list != null && list.Count > 0)
                {
                    __result = list[0];
                    __instance.SetSelected(Vector2Int.zero);
                }
            }
            catch (Exception caught) { AppLog.Swallow(caught); }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class CreativeTryPlacePatch
    {
        private static bool Prefix(Player __instance, Piece piece, ref bool __result)
        {
            var recipe = piece ? piece.GetComponent<CreativeRecipe>() : null;
            if (!recipe || !CreativeHammer.Enabled || !CreativeHammer.UsingHammer(__instance)) return true;

            // Refresh ghost, then clear BlockedbyPlayer/Invalid from oversized NPC/spawner/cliff colliders.
            try { Reflection.Method(typeof(Player), "UpdatePlacementGhost", typeof(bool)).Invoke(__instance, new object[] { true }); }
            catch (Exception caught) { AppLog.Swallow(caught); }
            CreativeHammer.ForceGhostValid(__instance);

            var ghost = Reflection.Get<GameObject>(__instance, "m_placementGhost");
            if (!ghost)
            {
                // Selection ok but ghost failed; place at look-hit or feet.
                Vector3 pos = __instance.transform.position + __instance.transform.forward * 2f;
                Quaternion rot = Quaternion.Euler(0f, __instance.transform.eulerAngles.y, 0f);
                try
                {
                    var cam = GameCamera.instance ? GameCamera.instance.transform : Camera.main?.transform;
                    if (cam && Physics.Raycast(cam.position, cam.forward, out var hit, 50f,
                            LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle"),
                            QueryTriggerInteraction.Ignore))
                        pos = hit.point;
                    CreativeHammer.Place(__instance, recipe, pos, rot);
                    __result = true;
                }
                catch (Exception ex)
                {
                    AppLog.Error("Creative", "Place at look-hit failed (" + (recipe && recipe.Prefab ? recipe.Prefab.name : "?") + ")", ex);
                    Plugin.Instance.Report(ex.Message);
                    __result = false;
                }
                return false;
            }

            try
            {
                CreativeHammer.Place(__instance, recipe, ghost.transform.position, ghost.transform.rotation);
                try
                {
                    var right = Reflection.Get<ItemDrop.ItemData>(__instance, "m_rightItem");
                    if (right != null)
                    {
                        __instance.FaceLookDirection();
                        var anim = Reflection.Get<ZSyncAnimation>(__instance, "m_zanim");
                        anim?.SetTrigger(right.m_shared.m_attack.m_attackAnimation);
                    }
                }
                catch (Exception caught) { AppLog.Swallow(caught); }
                __result = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Creative", "Place at ghost failed (" + (recipe && recipe.Prefab ? recipe.Prefab.name : "?") + ")", ex);
                Plugin.Instance.Report(ex.Message);
                __result = false;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "PlacePiece")]
    internal static class CreativePlacementPatch
    {
        private static bool Prefix(Player __instance, Piece piece, Vector3 pos, Quaternion rot)
        {
            var recipe = piece ? piece.GetComponent<CreativeRecipe>() : null;
            if (!recipe) return true;
            try { CreativeHammer.Place(__instance, recipe, pos, rot); }
            catch (Exception ex) { AppLog.Error("Creative", "PlacePiece redirect failed (" + (recipe.Prefab ? recipe.Prefab.name : "?") + ")", ex); Plugin.Instance.Report(ex.Message); }
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
    internal static class CreativeGhostVisiblePatch
    {
        private static void Postfix(Player __instance)
        {
            if (!CreativeHammer.Enabled || __instance != Player.m_localPlayer) return;
            try
            {
                var ghost = Reflection.Get<GameObject>(__instance, "m_placementGhost");
                if (ghost && ghost.GetComponent<CreativeRecipe>()) { CreativeHammer.EnsureGhostVisible(ghost); CreativeHammer.EnsureGhostCollider(ghost); }
            }
            catch (Exception ex) { AppLog.Warning("Creative ghost preview failed: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class CreativePlacementGhostPatch
    {
        private static void Postfix(Player __instance)
        {
            if (!CreativeHammer.Enabled || !CreativeHammer.UsingHammer(__instance)) return;
            var ghost = Reflection.Get<GameObject>(__instance, "m_placementGhost");
            if (!ghost || !ghost.GetComponent<CreativeRecipe>()) return;
            CreativeHammer.ForceGhostValid(__instance);
        }
    }

    /// <summary>
    /// Before vanilla's hover pick (which feeds RemovePiece and UpdatePlacement's hoveringPiece): give the aimed
    /// creator-stamped object back its Piece (see RestorePiece). In RemovePiece itself, hoveringPiece stayed null and
    /// UpdatePlacement threw after the remove.
    /// </summary>
    [HarmonyPatch(typeof(Player), "UpdateWearNTearHover")]
    internal static class CreativeRemoveRestorePatch
    {
        private static int mask;
        private static void Prefix(Player __instance)
        {
            if (!CreativeHammer.Enabled || __instance != Player.m_localPlayer || !CreativeHammer.UsingHammer(__instance)) return;
            try
            {
                var cam = GameCamera.instance;
                if (!cam) return;
                // Same ray as vanilla (m_removeRayMask).
                if (mask == 0) mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle");
                if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 50f, mask)) return;
                if (hit.collider.GetComponentInParent<Piece>()) return;
                var view = hit.collider.GetComponentInParent<ZNetView>();
                var zdo = view && view.IsValid() ? view.GetZDO() : null;
                if (zdo == null || zdo.GetLong(ZDOVars.s_creator, 0L) == 0L) return;
                CreativeHammer.RestorePiece(view.gameObject);
            }
            catch (Exception caught) { AppLog.Swallow(caught); }
        }
    }

    /// <summary>Creator-stamped spawners (placed with the creative hammer) skip vanilla's location block after a reload too.</summary>
    [HarmonyPatch(typeof(CreatureSpawner), "UpdateSpawner")]
    internal static class CreativeSpawnerKeepPatch
    {
        private static void Prefix(CreatureSpawner __instance)
        {
            try
            {
                if (Reflection.Get<bool>(__instance, "m_checkedLocation")) return;
                var view = __instance.GetComponent<ZNetView>();
                var zdo = view && view.IsValid() ? view.GetZDO() : null;
                if (zdo == null || !view.IsOwner() || zdo.GetLong(ZDOVars.s_creator, 0L) == 0L) return;
                CreativeHammer.KeepPlacedSpawner(__instance, Utils.GetPrefabName(__instance.gameObject));
            }
            catch (Exception caught) { AppLog.Swallow(caught); }
        }
    }

    /// <summary>Names the caller when something destroys a just-placed creative object.</summary>
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy))]
    internal static class CreativeDestroyTracePatch
    {
        private static void Prefix(GameObject go)
        {
            if (!CreativeHammer.TryRecent(go, out string name)) return;
            AppLog.Warning("Creative", name + " destroyed through ZNetScene.Destroy right after placing, by:" + AppLog.Callers(1, 6));
        }
    }
}
