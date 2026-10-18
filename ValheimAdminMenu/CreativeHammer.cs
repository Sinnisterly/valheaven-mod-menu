using System;
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
                        try { zdo.Set(ZDOVars.s_spawnPoint, obj.transform.position); } catch { }
                        try
                        {
                            if (zdo.GetLong(ZDOVars.s_spawnTime, 0L) == 0L && ZNet.instance != null)
                                zdo.Set(ZDOVars.s_spawnTime, ZNet.instance.GetTime().Ticks);
                        }
                        catch { }
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
                try { platformUser = PlatformManager.DistributionPlatform.LocalUser.PlatformUserID; } catch { }
                try { piece.SetCreator(player.GetPlayerID(), platformUser); } catch { }
            }
            obj.GetComponent<WearNTear>()?.OnPlaced();

            try
            {
                var placed = obj.GetComponentsInChildren<IPlaced>(true);
                if (placed != null)
                    foreach (var p in placed) p?.OnPlaced();
            }
            catch { }

            ApplyCreatureBrain(obj);
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
            var brains = obj.GetComponentsInChildren<BaseAI>(true);
            foreach (var ai in brains)
            {
                if (!ai) continue;
                ai.enabled = fullAi;
                if (fullAi)
                {
                    try { ai.ResetPatrolPoint(); } catch { }
                }
            }

            if (fullAi) return;

            // Freeze motion; keep Animator for idle loops.
            var body = character.GetComponent<Rigidbody>();
            if (body)
            {
                try { body.linearVelocity = Vector3.zero; } catch { }
                try { body.angularVelocity = Vector3.zero; } catch { }
                body.constraints = RigidbodyConstraints.FreezeAll;
            }
            try { character.SetHealth(character.GetMaxHealth()); } catch { }
        }

        /// <summary>NPC/spawner colliders fail CheckPlacementGhostVSPlayers even when the marker is green.</summary>
        internal static void ForceGhostValid(Player player)
        {
            if (!player) return;
            try { Reflection.Set(player, "m_placementStatus", 0); } catch { }
            try
            {
                var ghost = Reflection.Get<GameObject>(player, "m_placementGhost");
                var piece = ghost ? ghost.GetComponent<Piece>() : null;
                if (piece) piece.SetInvalidPlacementHeightlight(false);
            }
            catch { }
        }

        private static void AddRecipe(GameObject prefab, Sprite fallback)
        {
            bool prior = ZNetView.m_forceDisableInit;
            ZNetView.m_forceDisableInit = true;
            try
            {
                var proxy = UnityEngine.Object.Instantiate(prefab, root.transform, false);
                proxy.name = "VHCreative_" + prefab.name;
                PrepareGhostProxy(proxy, prefab);

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

        /// <summary>Keep meshes/colliders for the ghost; strip gameplay. Meshless spawners get a child from the creature prefab.</summary>
        private static void PrepareGhostProxy(GameObject proxy, GameObject sourcePrefab)
        {
            foreach (var component in proxy.GetComponentsInChildren<Component>(true))
            {
                if (!component || component is Transform || component is Piece) continue;
                if (component is Renderer || component is MeshFilter || component is LODGroup || component is Collider)
                    continue;
                if (component is Rigidbody || component is WearNTear) continue;
                // Animator stays so skinned meshes still draw in the ghost.
                if (component is Animator) continue;
                try { UnityEngine.Object.DestroyImmediate(component); }
                catch { }
            }

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
                        foreach (var component in child.GetComponentsInChildren<Component>(true))
                        {
                            if (!component || component is Transform) continue;
                            if (component is Renderer || component is MeshFilter || component is LODGroup || component is Collider || component is Animator)
                                continue;
                            try { UnityEngine.Object.DestroyImmediate(component); }
                            catch { }
                        }
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
        }

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
                catch { }
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
            GameObject instance = null;
            GameObject camObject = null;
            RenderTexture target = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab);
                instance.name = "VHCreativeIcon_" + prefab.name;
                instance.transform.position = new Vector3(0f, -8000f, 0f);
                foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!behaviour) continue;
                    // Animator is Behaviour, not MonoBehaviour; leave it for skinned meshes.
                    UnityEngine.Object.DestroyImmediate(behaviour);
                }

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

        private static Piece.PieceCategory CategoryFor(GameObject prefab)
        {
            string baseName = BaseCategory(prefab);
            if (!pageCounts.TryGetValue(baseName, out int count)) count = 0;
            // BetterCreative: Furniture/Building already fill page 1, so overflow starts at page 2.
            if ((baseName == "Furniture" || baseName == "Building") && count == 0)
                count = PageSize;
            int page = count / PageSize + 1;
            pageCounts[baseName] = count + 1;
            string label = page > 1 ? baseName + " " + page : baseName;
            if (categories.TryGetValue(label, out var existing)) return existing;
            while (table.m_categories.Contains((Piece.PieceCategory)nextCategoryId)
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
                || name.EndsWith("_aoe", StringComparison.Ordinal))
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
            || prefab.GetComponent<SpawnArea>();

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

        private static string FriendlyName(GameObject prefab)
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
            catch { }
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
            catch { }
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
                        Reflection.Method(typeof(Character), "FaceLookDirection").Invoke(__instance, null);
                        var anim = Reflection.Get<ZSyncAnimation>(__instance, "m_zanim");
                        anim?.SetTrigger(right.m_shared.m_attack.m_attackAnimation);
                    }
                }
                catch { }
                __result = true;
            }
            catch (Exception ex)
            {
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
            catch (Exception ex) { Plugin.Instance.Report(ex.Message); }
            return false;
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
}
