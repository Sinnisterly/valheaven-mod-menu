using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Splatform;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ValheimAdminMenu
{
    /// <summary>2.5.x helpers: spawn dest, VFX, copy/scale, loot, farming, camera, map/TP.</summary>
    internal static class Release250
    {
        internal const int SpawnInventory = 0, SpawnChest = 1, SpawnGround = 2;

        private static readonly string[] DefaultJunk =
        {
            "Stone", "Wood", "Resin", "Flint", "LeatherScraps", "BoneFragments",
            "TrophyDeer", "TrophyBoar", "TrophyNeck", "Entrails", "Ooze", "Guck",
            "Coal", "WitheredBone", "GreydwarfEye", "SurtlingCore"
        };

        /// <summary>Legacy multi-corner list; prefer CornerA / CornerB.</summary>
        internal static readonly List<Vector3> CopyCorners = new List<Vector3>();
        internal static Vector3? CornerA;
        internal static Vector3? CornerB;
        internal static bool CopyUseFeet;
        /// <summary>Also copy Location / WearNTear / registered ZNetView prefabs (ruins, houses).</summary>
        internal static bool IncludeWorldStructures = true;
        internal static readonly List<CopiedPiece> Clipboard = new List<CopiedPiece>();
        /// <summary>Hard cap; big ruins can be thousands of pieces.</summary>
        internal const int MaxClipboardPieces = 25000;
        internal static float PieceScale = 1f;
        internal static float RepairHammerRadius = 8f;
        internal static float VfxDuration
        {
            get => VfxSuite.Duration;
            set => VfxSuite.Duration = value;
        }
        internal static int VfxPlaceMode
        {
            get => VfxSuite.PlaceMode;
            set => VfxSuite.PlaceMode = value;
        }
        internal static string VfxSelectedId
        {
            get => VfxSuite.SelectedId;
            set => VfxSuite.SelectedId = value ?? "";
        }
        internal static string JunkExtra = "";
        internal static string JunkBlacklist = "";
        internal static string PickupWhitelist = "";
        internal static string PickupBlacklist = "";
        internal static float AutoFeedRadius = 20f;
        internal static float FarmHealRadius = 30f;
        internal static float MinimapZoom
        {
            get => minimapZoomValue;
            set => minimapZoomValue = Mathf.Clamp(value, .5f, 2.5f);
        }
        internal static Vector3 VfxCustomOffset = new Vector3(0, 1, 2);

        internal static bool PastePreviewActive;
        internal static bool PasteLocked;
        internal static bool AutoPastePreview = true;
        internal static bool PasteSnapToGround = true;
        internal static bool PasteAnchorSnap;
        internal static bool ApplyTerrainProfile;
        internal static float PasteHeightOffset;
        internal static float PasteYaw;
        internal static float PastePitch;
        internal static float PasteRoll;
        internal static float PasteNudgeX;
        internal static float PasteNudgeZ;
        internal static bool PasteLevelGround;
        internal static bool PasteLevelPerPiece;
        internal static bool LevelOnPlace;
        internal static float PasteFeather = 6f;
        internal static BlueprintTerrainDto ClipboardTerrain;
        internal static int HudPiecesInTerrain;

        private static float minimapZoomValue = 1f;
        private static float originalMinZoom = -1, originalMaxZoom = -1;
        private static float lastAppliedMinimapZoom = float.NaN;
        private static float nextMinimapMaintain;
        private static bool minimapRangeExpanded;
        private static float lastPasteH = float.NaN, lastPasteYaw, lastPastePitch, lastPasteRoll, lastPasteNx, lastPasteNz;
        private static Vector3 lastPasteOrigin;
        private static Vector3 pasteOrigin;
        private static float nextTerrainHudSample;
        private static bool photoOwnedHud;
        private static readonly List<GameObject> PasteGhosts = new List<GameObject>();
        private static readonly Dictionary<int, Material> GhostMatCache = new Dictionary<int, Material>();
        private static bool buildCamOwned;
        private static Rigidbody buildBody;
        private static bool buildPrevGravity = true;
        internal static bool MountNextArmed;
        private static Character mountNextCandidate;
        private static float mountNextSince;
        private static Character looseMount;
        private static Rigidbody looseBody;
        private static Rigidbody loosePlayerBody;
        private static bool looseBodyOwned;
        private static bool loosePrevGravity = true;
        private static bool loosePlayerPrevGravity = true;
        private static bool loosePlayerWasKinematic;
        private static Vector3 looseWish;
        private static bool looseRun, looseUp, looseDown;
        private static bool looseAiDisabled;
        private static MonsterAI looseAi;
        internal static bool AllowDismount;
        internal static bool MountBypass;
        private static GUIStyle copyMarkerStyle;
        private static float nextAutoFeedAt;
        private static float nextRepairAoeAt;

        internal sealed class CopiedPiece
        {
            internal string Prefab;
            internal Vector3 LocalPos;
            internal Quaternion LocalRot;
            internal Vector3 Scale;
        }

        internal static void Tick()
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null) return;
            var player = Player.m_localPlayer;
            if (!player || player.IsDead() || player.IsTeleporting())
            {
                ClearTransientBuildState(player);
                VfxSuite.Tick(null);
                return;
            }
            VfxSuite.Tick(player);
            if (phase.AutoFeedTames && Time.unscaledTime >= nextAutoFeedAt)
            {
                nextAutoFeedAt = Time.unscaledTime + .75f;
                AutoFeed(player);
            }
            if (phase.RepairHammerAoe && IsUsingHammer(player) && Time.unscaledTime >= nextRepairAoeAt)
            {
                nextRepairAoeAt = Time.unscaledTime + .45f;
                RepairAoe(player, RepairHammerRadius, silent: true);
            }
            ApplyMinimapZoom(phase);
            if (PastePreviewActive)
            {
                TickPasteAim(player);
                SyncPasteGhosts();
                FreeBuildRotation.TickPaste();
            }
            else if (PasteGhosts.Count > 0) DestroyPasteGhosts();
            FreeBuildRotation.TickHammer(player);
            DragBuilding.Tick(player);
            TickFreecamBuild(player);
            TickFlyingMount(player);
            TickLooseMount(player);
            TickMountNext(player);
        }

        /// <summary>Drop paste/drag/freecam-build/mount float state (death, portal, scene reset).</summary>
        internal static void ClearTransientBuildState(Player player)
        {
            if (PastePreviewActive) CancelPastePreview(silent: true);
            else DestroyPasteGhosts();
            DragBuilding.Cancel();
            FreeBuildRotation.HideAxesPublic();
            RestoreFreecamBuild(player);
            DetachLoose(player);
            RestoreFlyingBody();
            EnsurePlayerPhysics(player);
        }

        internal static void ResetSession()
        {
            CopyCorners.Clear();
            CornerA = CornerB = null;
            VfxSuite.ResetSession();
            photoOwnedHud = false;
            RestoreMinimapZoomRange();
            ClearTransientBuildState(Player.m_localPlayer);
        }

        /// <summary>Hammer/build tool only — not hoe or cultivator.</summary>
        internal static bool IsBuildHammer(Player player)
        {
            if (!player) return false;
            var shared = Reflection.Get<ItemDrop.ItemData>(player, "m_rightItem")?.m_shared;
            if (shared == null || shared.m_buildPieces == null) return false;
            string name = shared.m_name ?? "";
            if (name.IndexOf("cultivator", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (name.IndexOf("hoe", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }

        // Item spawn destinations
        internal static void Give(string id,int amount,int quality,int mode,string crafter=null,bool noCrafter=false,ItemEdits edits=null)
        {
            var app=Plugin.Instance;var player=app.RequirePlayer();
            var prefab=ObjectDB.instance.GetItemPrefab(id);
            if(!WorldTools.IsInventoryItem(prefab))throw new ArgumentException("Select an inventory item.");
            amount=Mathf.Clamp(amount,1,9999);quality=Mathf.Clamp(quality,1,ItemEditor.MaxQuality);
            if(mode==SpawnGround) { SpawnPresets.Drop(prefab,GroundSpawnPoint(player),amount,quality,crafter,noCrafter,edits);app.Report("Dropped "+amount+" "+id+".");return; }
            if(mode==SpawnChest) {
                var chests=ChestInventory.NearbyAccessible(player,ChestInventory.Radius);
                if(chests.Count==0)throw new InvalidOperationException("No accessible nearby chests.");
                int added=0;
                foreach(var chest in chests) { added+=SpawnPresets.Add(chest.GetInventory(),prefab,amount-added,quality,crafter,noCrafter,edits);if(added==amount)break; }
                app.Report("Added "+added+"/"+amount+" "+id+" to nearby chests.");return;
            }
            app.World.Give(id,amount,quality,crafter,noCrafter,edits);
        }

        private static Vector3 GroundSpawnPoint(Player player)
        {
            var cam = GameCamera.instance ? GameCamera.instance.transform : Camera.main?.transform;
            if (cam)
            {
                int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
                if (Physics.Raycast(cam.position, cam.forward, out var hit, 12f, mask, QueryTriggerInteraction.Ignore))
                    return hit.point + hit.normal * .05f;
            }
            return player.transform.position + player.transform.forward * 1.2f;
        }

        // VFX (delegates to VfxSuite)
        internal static List<KeyValuePair<string, string>> EnumerateVfx() => VfxSuite.Enumerate();
        internal static bool LooksLikeVfx(GameObject prefab) => VfxSuite.IsEffectPrefab(prefab);
        internal static void SpawnSelectedVfx() => VfxSuite.SpawnOrAttachSelected();
        internal static void LoadVfxPrefs(string weaponId, string feetId, string selectedId) => VfxSuite.LoadPrefs(weaponId, feetId, selectedId);
        internal static string WeaponVfxId => VfxSuite.WeaponId;
        internal static string FeetVfxId => VfxSuite.FeetMovingId;
        internal static void AttachWeaponVfx(string id)
        {
            VfxSuite.SelectedId = id ?? "";
            VfxSuite.AttachMode = VfxSuite.AttachWeapon;
            VfxSuite.SpawnOrAttachSelected();
        }
        internal static void AttachFeetVfx(string id)
        {
            VfxSuite.SelectedId = id ?? "";
            VfxSuite.AttachMode = VfxSuite.AttachFeetMoving;
            VfxSuite.SpawnOrAttachSelected();
        }

        // Build copy / paste / scale
        internal static void SetCornerA()
        {
            var player = Plugin.Instance.RequirePlayer();
            CornerA = PreciseCornerPoint(player);
            SyncCopyCornersList();
            Plugin.Instance.Report("Corner A set at " + CornerA.Value.ToString("0.##") + ".");
        }

        internal static void SetCornerB()
        {
            var player = Plugin.Instance.RequirePlayer();
            CornerB = PreciseCornerPoint(player);
            SyncCopyCornersList();
            Plugin.Instance.Report("Corner B set at " + CornerB.Value.ToString("0.##") + ".");
        }

        /// <summary>Legacy: fill next unset corner, then cycle B.</summary>
        internal static void MarkCopyCorner()
        {
            if (!CornerA.HasValue) SetCornerA();
            else SetCornerB();
        }

        internal static void ClearCopyCorners()
        {
            CornerA = CornerB = null;
            CopyCorners.Clear();
            Plugin.Instance.Report(L.T("ui.copy_corners_cleared"));
        }

        private static void SyncCopyCornersList()
        {
            CopyCorners.Clear();
            if (CornerA.HasValue) CopyCorners.Add(CornerA.Value);
            if (CornerB.HasValue) CopyCorners.Add(CornerB.Value);
        }

        internal static bool TryGetCopyBounds(out Bounds bounds)
        {
            bounds = default;
            if (!CornerA.HasValue || !CornerB.HasValue) return false;
            bounds = BoundsFromCorners(new List<Vector3> { CornerA.Value, CornerB.Value });
            return true;
        }

        internal static void CopyStructureVolume()
        {
            Plugin.Instance.RequireWorld();
            if (!TryGetCopyBounds(out Bounds bounds))
                throw new InvalidOperationException("Set Corner A and Corner B first (look-hit or feet).");
            CancelPastePreview(silent: true);
            Clipboard.Clear();
            var collected = CollectCopyCandidates(bounds, out int considered, out int skippedUnregistered, out int worldHits);
            if (collected.Count > MaxClipboardPieces)
            {
                collected.RemoveRange(MaxClipboardPieces, collected.Count - MaxClipboardPieces);
                Plugin.Instance.Report("Clipboard truncated to " + MaxClipboardPieces + " pieces (selection was larger).");
            }
            Clipboard.AddRange(collected);
            ClipboardTerrain = null;
            string worldNote = IncludeWorldStructures
                ? " (world structures ON, " + worldHits + " location/ruin hits)"
                : " (player Piece-only mode)";
            string skipNote = skippedUnregistered > 0 ? ", skipped " + skippedUnregistered + " unregistered prefabs" : "";
            Plugin.Instance.Report("Copied " + Clipboard.Count + " pieces (checked " + considered + skipNote + ") from "
                + bounds.size.ToString("0.#") + " volume" + worldNote + ".");
            if (Clipboard.Count >= 400)
                Plugin.Instance.Report(L.T("ui.large_clipboard_paste_can_hitch"));
            MaybeAutoPastePreview();
        }

        /// <summary>Expand A/B so overlapping structure bounds near the volume are fully enclosed.</summary>
        internal static void GrowCopyToOverlapping()
        {
            Plugin.Instance.RequireWorld();
            if (!TryGetCopyBounds(out Bounds bounds))
                throw new InvalidOperationException("Set Corner A and Corner B first.");
            int guard = 0;
            bool grew;
            do
            {
                grew = false;
                foreach (var go in EnumerateStructureRoots())
                {
                    if (!go) continue;
                    Bounds pb = ObjectWorldBounds(go);
                    if (!bounds.Intersects(pb)) continue;
                    Vector3 before = bounds.size;
                    bounds.Encapsulate(pb.min);
                    bounds.Encapsulate(pb.max);
                    if ((bounds.size - before).sqrMagnitude > .0001f) grew = true;
                }
                guard++;
            } while (grew && guard < 8);
            CornerA = bounds.min;
            CornerB = bounds.max;
            SyncCopyCornersList();
            var preview = CollectCopyCandidates(bounds, out _, out _, out _);
            Plugin.Instance.Report("Grew selection to " + bounds.size.ToString("0.#") + ", ~" + preview.Count + " pieces would copy"
                + (IncludeWorldStructures ? " (incl. world structures)." : "."));
        }

        /// <summary>Snap A/B to nearest loaded Location under crosshair or within 40 m.</summary>
        internal static void FitSelectionToNearbyLocation()
        {
            Plugin.Instance.RequireWorld();
            var player = Plugin.Instance.RequirePlayer();
            Location loc = null;
            var cam = GameCamera.instance ? GameCamera.instance.transform : Camera.main?.transform;
            if (cam && Physics.Raycast(cam.position, cam.forward, out var hit, 120f,
                    LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle"),
                    QueryTriggerInteraction.Ignore))
            {
                loc = hit.collider.GetComponentInParent<Location>();
            }
            if (!loc)
            {
                float best = 40f * 40f;
                Vector3 from = player.transform.position;
                foreach (var candidate in Object.FindObjectsByType<Location>(FindObjectsSortMode.None))
                {
                    if (!candidate) continue;
                    float d = (candidate.transform.position - from).sqrMagnitude;
                    if (d < best) { best = d; loc = candidate; }
                }
            }
            if (!loc) throw new InvalidOperationException("No Location (ruin/house/dungeon exterior) nearby. Aim at one or move closer.");
            Bounds b = ObjectWorldBounds(loc.gameObject);
            b.Expand(new Vector3(1.5f, 2f, 1.5f));
            CornerA = b.min;
            CornerB = b.max;
            SyncCopyCornersList();
            IncludeWorldStructures = true;
            Plugin.Instance.Report("Selection fitted to Location \"" + loc.name.Replace("(Clone)", "") + "\" "
                + b.size.ToString("0.#") + ". World structures enabled. Copy next (huge locations can hitch).");
        }

        private static List<CopiedPiece> CollectCopyCandidates(Bounds bounds, out int considered, out int skippedUnregistered, out int worldHits)
        {
            int consideredLocal = 0;
            int skippedLocal = 0;
            int worldLocal = 0;
            var results = new List<CopiedPiece>(128);
            var seen = new HashSet<int>();

            void TryAdd(GameObject go, bool fromWorld)
            {
                if (!go) return;
                if (!seen.Add(go.GetInstanceID())) return;
                if (IsExcludedCopyRoot(go)) return;
                consideredLocal++;
                Bounds pb = ObjectWorldBounds(go);
                if (!bounds.Intersects(pb)) return;
                string prefab = ResolvePasteablePrefab(go);
                if (string.IsNullOrEmpty(prefab))
                {
                    skippedLocal++;
                    return;
                }
                if (fromWorld) worldLocal++;
                results.Add(new CopiedPiece
                {
                    Prefab = prefab,
                    LocalPos = go.transform.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z),
                    LocalRot = go.transform.rotation,
                    Scale = go.transform.localScale
                });
            }

            foreach (var piece in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (!piece) continue;
                var nv = piece.GetComponent<ZNetView>();
                bool worldish = piece.GetComponentInParent<Location>() || piece.GetComponentInParent<LocationProxy>();
                if (nv && nv.IsValid())
                {
                    TryAdd(piece.gameObject, worldish);
                    continue;
                }
                // Static location pieces with no live ZDO still copy if the prefab is registered (paste makes a new ZDO).
                if (IncludeWorldStructures && worldish)
                    TryAdd(piece.gameObject, true);
            }

            if (!IncludeWorldStructures)
            {
                considered = consideredLocal;
                skippedUnregistered = skippedLocal;
                worldHits = worldLocal;
                return results;
            }

            foreach (var wear in Object.FindObjectsByType<WearNTear>(FindObjectsSortMode.None))
            {
                if (!wear || wear.GetComponent<Piece>()) continue;
                var nv = wear.GetComponent<ZNetView>();
                bool worldish = wear.GetComponentInParent<Location>() || wear.GetComponentInParent<LocationProxy>();
                if (nv && nv.IsValid()) TryAdd(wear.gameObject, true);
                else if (worldish) TryAdd(wear.gameObject, true);
            }

            foreach (var view in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if (!view || !view.IsValid()) continue;
                if (view.GetComponent<Piece>() || view.GetComponent<WearNTear>()) continue;
                if (!view.GetComponentInParent<Location>() && !view.GetComponentInParent<LocationProxy>()) continue;
                TryAdd(view.gameObject, true);
            }

            foreach (var loc in Object.FindObjectsByType<Location>(FindObjectsSortMode.None))
            {
                if (!loc) continue;
                Bounds lb = ObjectWorldBounds(loc.gameObject);
                if (!bounds.Intersects(lb)) continue;
                var renderers = loc.GetComponentsInChildren<Renderer>(true);
                if (renderers == null) continue;
                foreach (var r in renderers)
                {
                    if (!r) continue;
                    var go = r.gameObject;
                    var ownedPiece = go.GetComponentInParent<Piece>();
                    if (ownedPiece && ownedPiece.GetComponent<ZNetView>() is ZNetView ov && ov.IsValid()) continue;
                    var ownedWear = go.GetComponentInParent<WearNTear>();
                    if (ownedWear && ownedWear.GetComponent<ZNetView>() is ZNetView wv && wv.IsValid()) continue;
                    Transform t = go.transform;
                    for (int depth = 0; depth < 6 && t && t != loc.transform; depth++, t = t.parent)
                    {
                        string prefab = Utils.GetPrefabName(t.gameObject);
                        var registered = ZNetScene.instance ? ZNetScene.instance.GetPrefab(prefab) : null;
                        if (!registered) continue;
                        if (!registered.GetComponent<Piece>() && !registered.GetComponent<WearNTear>()) continue;
                        TryAdd(t.gameObject, true);
                        break;
                    }
                }
            }

            considered = consideredLocal;
            skippedUnregistered = skippedLocal;
            worldHits = worldLocal;
            return results;
        }

        private static IEnumerable<GameObject> EnumerateStructureRoots()
        {
            foreach (var piece in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (!piece) continue;
                var nv = piece.GetComponent<ZNetView>();
                if (nv && nv.IsValid()) { yield return piece.gameObject; continue; }
                if (IncludeWorldStructures && (piece.GetComponentInParent<Location>() || piece.GetComponentInParent<LocationProxy>()))
                    yield return piece.gameObject;
            }
            if (!IncludeWorldStructures) yield break;
            foreach (var wear in Object.FindObjectsByType<WearNTear>(FindObjectsSortMode.None))
            {
                if (!wear || wear.GetComponent<Piece>()) continue;
                var nv = wear.GetComponent<ZNetView>();
                if ((nv && nv.IsValid()) || wear.GetComponentInParent<Location>() || wear.GetComponentInParent<LocationProxy>())
                    yield return wear.gameObject;
            }
        }

        private static bool IsExcludedCopyRoot(GameObject go)
        {
            if (!go) return true;
            if (go.GetComponent<Player>()) return true;
            if (go.GetComponent<Character>()) return true;
            if (go.GetComponent<ItemDrop>() && !go.GetComponent<Piece>()) return true;
            if (go.GetComponent<TombStone>()) return true;
            if (go.GetComponent<LocationProxy>()) return true;
            if (go.GetComponent<DungeonGenerator>()) return true;
            if (go.GetComponent<Location>() && !go.GetComponent<Piece>() && !go.GetComponent<WearNTear>()) return true;
            if (go.GetComponent<TerrainModifier>() && !go.GetComponent<Piece>()) return true;
            if (go.name != null && go.name.StartsWith("ValheavenPasteGhost_", StringComparison.Ordinal)) return true;
            return false;
        }

        private static string ResolvePasteablePrefab(GameObject go)
        {
            if (!go || !ZNetScene.instance) return null;
            string name = Utils.GetPrefabName(go);
            if (string.IsNullOrEmpty(name)) return null;
            if (!ZNetScene.instance.GetPrefab(name)) return null;
            return name;
        }

        private static Bounds ObjectWorldBounds(GameObject go)
        {
            if (!go) return new Bounds(Vector3.zero, Vector3.one);
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers != null && renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    if (renderers[i]) b.Encapsulate(renderers[i].bounds);
                return b;
            }
            var cols = go.GetComponentsInChildren<Collider>();
            if (cols != null && cols.Length > 0)
            {
                Bounds b = cols[0].bounds;
                for (int i = 1; i < cols.Length; i++)
                    if (cols[i]) b.Encapsulate(cols[i].bounds);
                return b;
            }
            return new Bounds(go.transform.position, Vector3.one * .75f);
        }

        private static Bounds PieceWorldBounds(Piece piece) => ObjectWorldBounds(piece ? piece.gameObject : null);

        internal static void PasteStructure(bool atCrosshair)
        {
            Plugin.Instance.RequireWorld();
            if (Clipboard.Count == 0) throw new InvalidOperationException("Clipboard is empty. Copy a volume first.");
            var player = Plugin.Instance.RequirePlayer();
            Vector3 origin = atCrosshair ? CrosshairPoint(player) : player.transform.position;
            if (PasteSnapToGround) origin = SeatOriginOnGround(origin, Quaternion.identity);
            CommitPasteAt(origin, Quaternion.identity);
        }

        internal static void MaybeAutoPastePreview()
        {
            if (!AutoPastePreview || Clipboard.Count == 0) return;
            if (!Plugin.Instance || !ZNetScene.instance) return;
            try { BeginPastePreview(true); }
            catch { }
        }

        internal static void BeginPastePreview(bool atCrosshair)
        {
            Plugin.Instance.RequireWorld();
            if (Clipboard.Count == 0) throw new InvalidOperationException("Clipboard is empty. Copy a volume first.");
            var player = Plugin.Instance.RequirePlayer();
            DestroyPasteGhosts();
            PasteHeightOffset = PasteYaw = PastePitch = PasteRoll = 0f;
            PasteNudgeX = PasteNudgeZ = 0f;
            PasteLocked = false;
            pasteOrigin = atCrosshair ? AimPasteOrigin(player) : player.transform.position;
            if (PasteSnapToGround && !PasteAnchorSnap) pasteOrigin = SeatOriginOnGround(pasteOrigin, PasteRotation());
            PastePreviewActive = true;
            RebuildPasteGhosts();
            Plugin.Instance.Report("Paste preview active. LMB place, P lock, RMB cancel.");
        }

        private static int pasteLockFrame = -1;

        internal static void TogglePasteLock()
        {
            if (!PastePreviewActive) return;
            if (pasteLockFrame == Time.frameCount) return;
            pasteLockFrame = Time.frameCount;
            PasteLocked = !PasteLocked;
            if (Plugin.Instance) Plugin.Instance.Report(PasteLocked ? "Paste preview locked." : "Paste preview following aim.");
        }

        internal static void ConfirmPaste()
        {
            Plugin.Instance.RequireWorld();
            if (!PastePreviewActive) throw new InvalidOperationException("No paste preview active. Start preview first.");
            if (Clipboard.Count == 0) throw new InvalidOperationException("Clipboard is empty.");
            Vector3 origin = PasteWorldOrigin();
            var rot = PasteRotation();
            var pad = BuildPad(origin, PasteYaw, PasteFeather);
            var supports = PasteLevelPerPiece ? BuildSupportPads(PasteFeather) : null;
            var terrain = ClipboardTerrain;
            bool applyTerrain = ApplyTerrainProfile && terrain != null;
            CancelPastePreview(silent: true);
            if (pad.HasValue)
                Plugin.Instance.World.RememberPad(pad.Value);
            if (applyTerrain)
                Plugin.Instance.World.ApplyTerrainProfile(terrain, origin, rot);
            else if (PasteLevelGround)
            {
                if (supports != null && supports.Count > 0)
                {
                    Plugin.Instance.World.RememberSupports(supports);
                    Plugin.Instance.World.ApplySupports(supports);
                }
                else if (pad.HasValue)
                {
                    Plugin.Instance.World.ClearSupports();
                    Plugin.Instance.World.ApplyPad(pad.Value);
                }
            }
            CommitPasteAt(origin, rot);
        }

        internal static void CancelPastePreview(bool silent = false)
        {
            DestroyPasteGhosts();
            PastePreviewActive = false;
            PasteLocked = false;
            PasteHeightOffset = PasteYaw = PastePitch = PasteRoll = PasteNudgeX = PasteNudgeZ = 0f;
            lastPasteH = float.NaN;
            HudPiecesInTerrain = 0;
            if (!silent && Plugin.Instance) Plugin.Instance.Report(L.T("ui.paste_preview_cancelled"));
        }

        internal static Quaternion PasteRotation() => Quaternion.Euler(PastePitch, PasteYaw, PasteRoll);

        internal static Vector3 PasteWorldOrigin()
        {
            var rot = PasteRotation();
            return pasteOrigin + Vector3.up * PasteHeightOffset + rot * new Vector3(PasteNudgeX, 0f, PasteNudgeZ);
        }

        private static void TickPasteAim(Player player)
        {
            if (!PastePreviewActive) return;
            if (Plugin.Instance != null && Plugin.Instance.Open) return;
            if (Console.IsVisible() || ChatHasFocus() || TextInput.IsVisible() || StoreGui.IsVisible() || InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen())
                return;

            if (Input.GetKeyDown(PasteLockHotkey()))
                TogglePasteLock();

            if (!PasteLocked)
            {
                pasteOrigin = AimPasteOrigin(player);
                if (PasteSnapToGround && !PasteAnchorSnap)
                    pasteOrigin = SeatOriginOnGround(pasteOrigin, PasteRotation());
            }

            if (Time.unscaledTime >= nextTerrainHudSample)
            {
                nextTerrainHudSample = Time.unscaledTime + 0.35f;
                HudPiecesInTerrain = CountPiecesInTerrain();
            }

            if (!ZInput.GetButtonDown("Attack") && !Input.GetMouseButtonDown(0)) return;
            try { ConfirmPaste(); }
            catch (Exception ex)
            {
                if (Plugin.Instance) Plugin.Instance.Report(ex.GetBaseException().Message);
            }
        }

        private static bool ChatHasFocus()
        {
            try { return Chat.instance && Chat.instance.HasFocus(); }
            catch { return false; }
        }

        private static KeyCode PasteLockHotkey()
        {
            try
            {
                var feature = HotkeyRegistry.Find("pasteLockPreview");
                if (feature != null && feature.Key != KeyCode.None && Rules.ValidHotkey(feature.Key))
                    return feature.Key;
            }
            catch { }
            return KeyCode.P;
        }

        private static Vector3 AimPasteOrigin(Player player)
        {
            if (PasteAnchorSnap && TryAnchorSnapOrigin(player, out Vector3 snapped))
                return snapped;
            return CrosshairPoint(player);
        }

        private static Vector3 SeatOriginOnGround(Vector3 origin, Quaternion rot)
        {
            float lowest = float.MaxValue;
            bool any = false;
            for (int i = 0; i < Clipboard.Count; i++)
            {
                Vector3 world = origin + rot * Clipboard[i].LocalPos;
                if (world.y < lowest) lowest = world.y;
                any = true;
            }
            if (!any) lowest = origin.y;
            float ground = SampleGroundY(origin);
            float delta = ground - lowest;
            if (Mathf.Abs(delta) < 0.001f) return origin;
            return origin + Vector3.up * delta;
        }

        private static float SampleGroundY(Vector3 pos)
        {
            try
            {
                if (ZoneSystem.instance)
                {
                    float h = ZoneSystem.instance.GetGroundHeight(pos);
                    if (!float.IsNaN(h) && !float.IsInfinity(h)) return h;
                }
            }
            catch { }
            if (Physics.Raycast(pos + Vector3.up * 80f, Vector3.down, out var hit, 200f,
                LayerMask.GetMask("terrain", "Default", "static_solid"), QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return pos.y;
        }

        private static bool TryAnchorSnapOrigin(Player player, out Vector3 origin)
        {
            origin = default;
            if (!TryGetLookPiece(player, out var worldPiece, out _)) return false;
            var worldSnaps = new List<Transform>();
            try { worldPiece.GetSnapPoints(worldSnaps); } catch { return false; }
            if (worldSnaps.Count == 0) return false;

            var rot = PasteRotation();
            float best = float.MaxValue;
            Vector3 bestOrigin = CrosshairPoint(player);
            Vector3 hit = CrosshairPoint(player);

            for (int w = 0; w < worldSnaps.Count; w++)
            {
                var wt = worldSnaps[w];
                if (!wt) continue;
                float distHit = (wt.position - hit).sqrMagnitude;
                if (distHit > 25f) continue;

                int limit = Mathf.Min(Clipboard.Count, 48);
                for (int i = 0; i < limit; i++)
                {
                    var entry = Clipboard[i];
                    var prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(entry.Prefab) : null;
                    if (!prefab) continue;
                    var piece = prefab.GetComponent<Piece>();
                    if (!piece) continue;
                    var localSnaps = new List<Transform>();
                    try { piece.GetSnapPoints(localSnaps); } catch { continue; }
                    for (int s = 0; s < localSnaps.Count; s++)
                    {
                        var st = localSnaps[s];
                        if (!st) continue;
                        Vector3 localSnap = st.position - prefab.transform.position;
                        Vector3 candidate = wt.position - rot * (entry.LocalPos + localSnap);
                        float score = (candidate - hit).sqrMagnitude;
                        if (score < best)
                        {
                            best = score;
                            bestOrigin = candidate;
                        }
                    }
                }
            }
            if (best >= float.MaxValue * 0.5f) return false;
            origin = bestOrigin;
            return true;
        }

        private static int CountPiecesInTerrain()
        {
            if (!PastePreviewActive || Clipboard.Count == 0) return 0;
            Vector3 origin = PasteWorldOrigin();
            var rot = PasteRotation();
            int n = 0;
            int step = Clipboard.Count > 400 ? 4 : 1;
            for (int i = 0; i < Clipboard.Count; i += step)
            {
                Vector3 world = origin + rot * Clipboard[i].LocalPos;
                float ground = SampleGroundY(world);
                if (world.y < ground - 0.15f) n++;
            }
            if (step > 1) n = Mathf.RoundToInt(n * (float)step);
            return Mathf.Min(n, Clipboard.Count);
        }

        private static void SyncPasteGhosts()
        {
            if (!PastePreviewActive)
            {
                if (PasteGhosts.Count > 0) DestroyPasteGhosts();
                return;
            }
            if (Clipboard.Count == 0) { CancelPastePreview(silent: true); return; }
            if (PasteGhosts.Count != Clipboard.Count)
            {
                RebuildPasteGhosts();
                RememberPasteGhostState();
                return;
            }
            Vector3 origin = PasteWorldOrigin();
            bool dirty = float.IsNaN(lastPasteH)
                || Mathf.Abs(lastPasteH - PasteHeightOffset) > .001f
                || Mathf.Abs(lastPasteYaw - PasteYaw) > .01f
                || Mathf.Abs(lastPastePitch - PastePitch) > .01f
                || Mathf.Abs(lastPasteRoll - PasteRoll) > .01f
                || Mathf.Abs(lastPasteNx - PasteNudgeX) > .001f
                || Mathf.Abs(lastPasteNz - PasteNudgeZ) > .001f
                || (origin - lastPasteOrigin).sqrMagnitude > .0001f;
            if (!dirty) return;
            UpdatePasteGhostTransforms();
            RememberPasteGhostState();
        }

        private static void RememberPasteGhostState()
        {
            lastPasteH = PasteHeightOffset;
            lastPasteYaw = PasteYaw;
            lastPastePitch = PastePitch;
            lastPasteRoll = PasteRoll;
            lastPasteNx = PasteNudgeX;
            lastPasteNz = PasteNudgeZ;
            lastPasteOrigin = PasteWorldOrigin();
        }

        private static void RebuildPasteGhosts()
        {
            DestroyPasteGhosts();
            if (!PastePreviewActive || Clipboard.Count == 0 || !ZNetScene.instance) return;
            bool prior = ZNetView.m_forceDisableInit;
            ZNetView.m_forceDisableInit = true;
            try
            {
                Vector3 origin = PasteWorldOrigin();
                var rot = PasteRotation();
                foreach (var entry in Clipboard)
                {
                    var prefab = ZNetScene.instance.GetPrefab(entry.Prefab);
                    if (!prefab) { PasteGhosts.Add(null); continue; }
                    Vector3 worldPos = origin + rot * entry.LocalPos;
                    Quaternion worldRot = rot * entry.LocalRot;
                    var ghost = Object.Instantiate(prefab, worldPos, worldRot);
                    ghost.name = "ValheavenPasteGhost_" + entry.Prefab;
                    ghost.transform.localScale = entry.Scale;
                    ApplyPasteGhostLook(ghost);
                    PasteGhosts.Add(ghost);
                }
            }
            finally { ZNetView.m_forceDisableInit = prior; }
        }

        private static void UpdatePasteGhostTransforms()
        {
            Vector3 origin = PasteWorldOrigin();
            var rot = PasteRotation();
            for (int i = 0; i < Clipboard.Count && i < PasteGhosts.Count; i++)
            {
                var ghost = PasteGhosts[i];
                if (!ghost) continue;
                var entry = Clipboard[i];
                ghost.transform.position = origin + rot * entry.LocalPos;
                ghost.transform.rotation = rot * entry.LocalRot;
                ghost.transform.localScale = entry.Scale;
            }
        }

        private static void DestroyPasteGhosts()
        {
            for (int i = 0; i < PasteGhosts.Count; i++)
            {
                var g = PasteGhosts[i];
                if (g) Object.Destroy(g);
            }
            PasteGhosts.Clear();
            ClearGhostMaterials();
        }

        private static void ClearGhostMaterials()
        {
            foreach (var mat in GhostMatCache.Values)
                if (mat) Object.Destroy(mat);
            GhostMatCache.Clear();
        }

        private static Material GhostCopy(Material source)
        {
            if (!source) return null;
            int id = source.GetInstanceID();
            if (GhostMatCache.TryGetValue(id, out var cached) && cached) return cached;
            Texture albedo = null;
            if (source.HasProperty("_MainTex")) albedo = source.GetTexture("_MainTex");
            if (!albedo && source.HasProperty("_BaseMap")) albedo = source.GetTexture("_BaseMap");
            if (!albedo && source.HasProperty("_Albedo")) albedo = source.GetTexture("_Albedo");
            var shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (albedo && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", albedo);
            Color tint = Color.white;
            if (source.HasProperty("_Color")) tint = source.GetColor("_Color");
            else if (source.HasProperty("_BaseColor")) tint = source.GetColor("_BaseColor");
            tint = Color.Lerp(tint, new Color(.78f, .9f, 1f, 1f), .15f);
            tint.a = .55f;
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Mode"))
            {
                mat.SetFloat("_Mode", 3f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 1);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
            GhostMatCache[id] = mat;
            return mat;
        }

        private static void ApplyPasteGhostLook(GameObject go)
        {
            if (!go) return;
            foreach (var view in go.GetComponentsInChildren<ZNetView>(true))
            {
                if (!view) continue;
                view.enabled = false;
                try { Reflection.Set(view, "m_ghost", true); } catch { }
            }
            foreach (var wnt in go.GetComponentsInChildren<WearNTear>(true))
                if (wnt) wnt.enabled = false;
            foreach (var piece in go.GetComponentsInChildren<Piece>(true))
                if (piece) piece.enabled = false;
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                if (col) col.enabled = false;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
            {
                if (!rb) continue;
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                if (ps) ps.gameObject.SetActive(false);
            foreach (var light in go.GetComponentsInChildren<Light>(true))
                if (light) light.enabled = false;
            foreach (var audio in go.GetComponentsInChildren<AudioSource>(true))
                if (audio) audio.enabled = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!r || r is ParticleSystemRenderer) continue;
                var shared = r.sharedMaterials;
                if (shared == null || shared.Length == 0) continue;
                var mats = new Material[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                    mats[i] = GhostCopy(shared[i]) ?? shared[i];
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private static WorldTools.TerrainPad? BuildPad(Vector3 origin, float yawDegrees, float feather)
        {
            if (Clipboard.Count == 0) return null;
            var yaw = Quaternion.Euler(0f, yawDegrees, 0f);
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, minY = float.MaxValue;
            bool any = false;
            for (int i = 0; i < PasteGhosts.Count; i++)
            {
                var ghost = PasteGhosts[i];
                if (!ghost) continue;
                Vector3 pivot = ghost.transform.position;
                float pieceBottom = float.MaxValue;
                bool mesh = false;
                foreach (var renderer in ghost.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer || !renderer.enabled || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter && !filter.sharedMesh) continue;
                    Bounds b = renderer.bounds;
                    if (b.size.sqrMagnitude < 0.0001f || b.extents.y > 20f) continue;
                    if (Mathf.Abs(b.center.x - pivot.x) > 6f || Mathf.Abs(b.center.z - pivot.z) > 6f) continue;
                    // Skip bounds way below the piece (was digging the pad under the preview).
                    if (b.min.y < pivot.y - 1.25f) continue;
                    if (!mesh || b.min.y < pieceBottom) pieceBottom = b.min.y;
                    mesh = true;
                    if (b.min.x < minX) minX = b.min.x;
                    if (b.max.x > maxX) maxX = b.max.x;
                    if (b.min.z < minZ) minZ = b.min.z;
                    if (b.max.z > maxZ) maxZ = b.max.z;
                }
                if (!mesh)
                {
                    pieceBottom = pivot.y;
                    if (pivot.x < minX) minX = pivot.x;
                    if (pivot.x > maxX) maxX = pivot.x;
                    if (pivot.z < minZ) minZ = pivot.z;
                    if (pivot.z > maxZ) maxZ = pivot.z;
                }
                if (pieceBottom < minY) minY = pieceBottom;
                any = true;
            }
            if (!any)
            {
                foreach (var entry in Clipboard)
                {
                    Vector3 world = origin + yaw * entry.LocalPos;
                    if (world.x < minX) minX = world.x;
                    if (world.x > maxX) maxX = world.x;
                    if (world.z < minZ) minZ = world.z;
                    if (world.z > maxZ) maxZ = world.z;
                    if (world.y < minY) minY = world.y;
                }
            }
            return new WorldTools.TerrainPad
            {
                MinX = minX - 1f,
                MaxX = maxX + 1f,
                MinZ = minZ - 1f,
                MaxZ = maxZ + 1f,
                TargetY = minY,
                Feather = Mathf.Clamp(feather, 0f, 12f)
            };
        }

        private static List<WorldTools.TerrainPad> BuildSupportPads(float feather)
        {
            var pads = new List<WorldTools.TerrainPad>();
            for (int i = 0; i < PasteGhosts.Count; i++)
            {
                if (TrySupportPad(PasteGhosts[i], feather, out var pad)) pads.Add(pad);
            }
            return pads;
        }

        internal static void LevelUnderPlaced(GameObject piece)
        {
            if (!TrySupportPad(piece, PasteFeather, out var pad)) return;
            Plugin.Instance.World.ApplySupports(new List<WorldTools.TerrainPad> { pad });
        }

        private static bool TrySupportPad(GameObject go, float feather, out WorldTools.TerrainPad pad)
        {
            pad = default;
            if (!go) return false;
            Vector3 pivot = go.transform.position;
            float bottom = float.MaxValue;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            bool mesh = false;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer || !renderer.enabled || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter && !filter.sharedMesh) continue;
                Bounds b = renderer.bounds;
                if (b.size.sqrMagnitude < 0.0001f || b.extents.y > 40f || b.extents.x > 40f || b.extents.z > 40f) continue;
                if (Mathf.Abs(b.center.x - pivot.x) > 8f || Mathf.Abs(b.center.z - pivot.z) > 8f) continue;
                if (Mathf.Abs(b.center.y - pivot.y) > 30f) continue;
                mesh = true;
                if (b.min.y < bottom) bottom = b.min.y;
                if (b.min.x < minX) minX = b.min.x;
                if (b.max.x > maxX) maxX = b.max.x;
                if (b.min.z < minZ) minZ = b.min.z;
                if (b.max.z > maxZ) maxZ = b.max.z;
            }
            if (!mesh)
            {
                bottom = pivot.y;
                minX = pivot.x - 0.5f;
                maxX = pivot.x + 0.5f;
                minZ = pivot.z - 0.5f;
                maxZ = pivot.z + 0.5f;
            }
            pad = new WorldTools.TerrainPad
            {
                MinX = minX - 0.75f,
                MaxX = maxX + 0.75f,
                MinZ = minZ - 0.75f,
                MaxZ = maxZ + 0.75f,
                TargetY = bottom,
                Feather = Mathf.Clamp(feather, 0f, 12f)
            };
            return true;
        }

        private static void CommitPasteAt(Vector3 origin, Quaternion rot)
        {
            var player = Plugin.Instance.RequirePlayer();
            long playerId = player.GetPlayerID();
            PlatformUserID platformUser = default;
            try { platformUser = PlatformManager.DistributionPlatform.LocalUser.PlatformUserID; } catch { }
            string profileName = "";
            try { profileName = Game.instance?.GetPlayerProfile()?.GetName() ?? ""; } catch { }

            int n = 0, skipped = 0;
            var placedScratch = new List<IPlaced>(4);
            foreach (var entry in Clipboard)
            {
                var prefab = ZNetScene.instance.GetPrefab(entry.Prefab);
                if (!prefab) { skipped++; continue; }
                Vector3 worldPos = origin + rot * entry.LocalPos;
                Quaternion worldRot = rot * entry.LocalRot;

                // Same as PlacePiece: Instantiate of a ZNetView prefab creates the ZDO.
                TerrainModifier.SetTriggerOnPlaced(true);
                var obj = Object.Instantiate(prefab, worldPos, worldRot);
                TerrainModifier.SetTriggerOnPlaced(false);

                var view = obj.GetComponent<ZNetView>();
                if (view && view.IsValid())
                {
                    if (!view.IsOwner()) view.ClaimOwnership();
                    view.SetLocalScale(entry.Scale);
                    try { Reflection.Method(typeof(ZNetView), "SyncScale").Invoke(view, null); } catch { }
                }
                else obj.transform.localScale = entry.Scale;

                var piece = obj.GetComponent<Piece>();
                if (piece)
                {
                    // Sets ZDOVars.s_creator for IsPlacedByPlayer / remove-own-piece.
                    try { piece.SetCreator(playerId, platformUser); } catch { }
                }

                try { obj.GetComponent<PrivateArea>()?.Setup(profileName); } catch { }
                // Recalc support now (skips 30s post-Awake grace).
                try { obj.GetComponent<WearNTear>()?.OnPlaced(); } catch { }
                try { obj.GetComponent<ItemDrop>()?.MakePiece(sendRPC: true); } catch { }

                placedScratch.Clear();
                obj.GetComponents(placedScratch);
                foreach (var placed in placedScratch)
                {
                    try { placed.OnPlaced(); } catch { }
                }

                n++;
            }
            Plugin.Instance.Report("Pasted " + n + " pieces" + (skipped > 0 ? " (" + skipped + " prefabs missing)." : "."));
        }

        /// <summary>Camera forward hit for paste. Ignores CopyUseFeet.</summary>
        private static Vector3 CrosshairPoint(Player player)
        {
            var cam = GameCamera.instance ? GameCamera.instance.transform : Camera.main?.transform;
            if (cam)
            {
                int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle", "item");
                if (Physics.Raycast(cam.position, cam.forward, out var hit, 250f, mask, QueryTriggerInteraction.Ignore))
                    return hit.point;
                return cam.position + cam.forward * 12f;
            }
            return player.transform.position;
        }

        internal static void ScaleLookPiece(float scale)
        {
            Plugin.Instance.RequireWorld();
            var player = Plugin.Instance.RequirePlayer();
            scale = Mathf.Clamp(scale, .25f, 4f);
            PieceScale = scale;
            if (!TryGetLookPiece(player, out var piece, out var view))
                throw new InvalidOperationException("Aim at an owned building piece.");
            if (!view.IsOwner()) view.ClaimOwnership();
            if (!view.IsOwner()) throw new InvalidOperationException("Could not claim ownership of that piece.");
            view.SetLocalScale(Vector3.one * scale);
            try { Reflection.Method(typeof(ZNetView), "SyncScale").Invoke(view, null); } catch { }
            Plugin.Instance.Report("Scaled " + piece.name + " to " + scale.ToString("0.##") + "x.");
        }

        private static bool TryGetLookPiece(Player player, out Piece piece, out ZNetView view)
        {
            piece = null; view = null;
            var cam = GameCamera.instance ? GameCamera.instance.transform : Camera.main?.transform;
            if (!cam) return false;
            int mask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "vehicle");
            if (!Physics.Raycast(cam.position, cam.forward, out var hit, 40f, mask, QueryTriggerInteraction.Ignore)) return false;
            piece = hit.collider.GetComponentInParent<Piece>();
            if (!piece || piece.GetComponent<ItemDrop>() || piece.GetComponent<Character>()) return false;
            view = piece.GetComponent<ZNetView>();
            return view && view.IsValid();
        }

        private static Bounds BoundsFromCorners(List<Vector3> corners)
        {
            var b = new Bounds(corners[0], Vector3.zero);
            for (int i = 1; i < corners.Count; i++) b.Encapsulate(corners[i]);
            if (b.size.y < .5f) b.Expand(new Vector3(0, 4f, 0));
            // Inflate so walls just outside A-B still intersect.
            b.Expand(1.25f);
            return b;
        }

        // Snap to piece / WearNTear bounds when looking at buildables.
        private static Vector3 PreciseCornerPoint(Player player)
        {
            if (CopyUseFeet) return player.transform.position;
            var cam = GameCamera.instance ? GameCamera.instance.transform : Camera.main?.transform;
            if (cam)
            {
                int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle", "item");
                if (Physics.Raycast(cam.position, cam.forward, out var hit, 200f, mask, QueryTriggerInteraction.Ignore))
                {
                    var piece = hit.collider ? hit.collider.GetComponentInParent<Piece>() : null;
                    if (piece && !IsExcludedCopyRoot(piece.gameObject))
                        return NearestBoundsCorner(PieceWorldBounds(piece), hit.point);
                    var wear = hit.collider ? hit.collider.GetComponentInParent<WearNTear>() : null;
                    if (wear && !IsExcludedCopyRoot(wear.gameObject))
                        return NearestBoundsCorner(ObjectWorldBounds(wear.gameObject), hit.point);
                    return hit.point;
                }
                return cam.position + cam.forward * 12f;
            }
            return player.transform.position;
        }

        private static Vector3 NearestBoundsCorner(Bounds b, Vector3 point)
        {
            Vector3 min = b.min, max = b.max;
            Vector3 best = min;
            float bestDist = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                float d = (c - point).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        internal static void DrawCopyVolume()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!CornerA.HasValue && !CornerB.HasValue && !PastePreviewActive) return;
            var cam = Camera.main;
            if (!cam) return;
            Color prior = GUI.color;
            try
            {
                if (CornerA.HasValue || CornerB.HasValue)
                {
                    if (CornerA.HasValue) DrawWorldMarker(cam, CornerA.Value, new Color(.2f, .85f, .35f, .95f), "A");
                    if (CornerB.HasValue) DrawWorldMarker(cam, CornerB.Value, new Color(.95f, .55f, .15f, .95f), "B");
                    if (TryGetCopyBounds(out Bounds bounds))
                        DrawBoundsWire(cam, bounds, new Color(.35f, .85f, 1f, .9f));
                }
                DrawPastePreview(cam);
            }
            finally { GUI.color = prior; }
        }

        private static void DrawPastePreview(Camera cam)
        {
            if (!PastePreviewActive || Clipboard.Count == 0) return;
            // Ghosts already show the pieces; just mark the origin.
            DrawWorldMarker(cam, PasteWorldOrigin(), new Color(1f, .4f, .15f, 1f), "P");
        }

        private static void DrawWorldMarker(Camera cam, Vector3 world, Color color, string label)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0) return;
            float x = sp.x, y = Screen.height - sp.y;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x - 4, y - 4, 8, 8), Texture2D.whiteTexture);
            if (copyMarkerStyle == null)
            {
                copyMarkerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }
            copyMarkerStyle.normal.textColor = color;
            GUI.Label(new Rect(x - 20, y - 22, 40, 18), label, copyMarkerStyle);
        }

        private static void DrawBoundsWire(Camera cam, Bounds b, Color color)
        {
            Vector3 min = b.min, max = b.max;
            Vector3[] c =
            {
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z)
            };
            int[,] edges =
            {
                {0,1},{1,2},{2,3},{3,0},
                {4,5},{5,6},{6,7},{7,4},
                {0,4},{1,5},{2,6},{3,7}
            };
            GUI.color = color;
            for (int i = 0; i < edges.GetLength(0); i++)
                DrawScreenLine(cam, c[edges[i, 0]], c[edges[i, 1]]);
        }

        private static void DrawScreenLine(Camera cam, Vector3 a, Vector3 b)
        {
            Vector3 sa = cam.WorldToScreenPoint(a);
            Vector3 sb = cam.WorldToScreenPoint(b);
            if (sa.z <= 0 || sb.z <= 0) return;
            Vector2 p0 = new Vector2(sa.x, Screen.height - sa.y);
            Vector2 p1 = new Vector2(sb.x, Screen.height - sb.y);
            Vector2 d = p1 - p0;
            float len = d.magnitude;
            if (len < 1f) return;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, p0);
            GUI.DrawTexture(new Rect(p0.x, p0.y - 1f, len, 2f), Texture2D.whiteTexture);
            GUI.matrix = matrix;
        }

        // Loot / junk / auto-pickup
        internal static HashSet<string> ResolveJunkSet()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var j in DefaultJunk) set.Add(j);
            foreach (var part in SplitList(JunkExtra)) set.Add(part);
            foreach (var part in SplitList(JunkBlacklist)) set.Remove(part);
            return set;
        }

        internal static IEnumerable<string> SplitList(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) yield break;
            foreach (var part in raw.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = part.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        internal static void SortNearbyChests()
        {
            var player = Plugin.Instance.RequirePlayer();
            int chests = 0, stacks = 0;
            foreach (var container in ChestInventory.NearbyAccessible(player, ChestInventory.Radius))
            {
                Inventory inv;
                try { inv = container.GetInventory(); } catch { continue; }
                if (inv == null) continue;
                stacks += MergeStacks(inv);
                SortInventory(inv);
                chests++;
                try { Reflection.Method(typeof(Inventory), "Changed", typeof(bool), typeof(bool)).Invoke(inv, new object[] { true, false }); } catch { }
            }
            Plugin.Instance.Report("Sorted/merged " + chests + " chest(s); collapsed " + stacks + " stack gap(s).");
        }

        internal static void SortPlayerInventory()
        {
            var inv = Plugin.Instance.RequirePlayer().GetInventory();
            int merged = MergeStacks(inv);
            SortInventory(inv);
            try { Reflection.Method(typeof(Inventory), "Changed", typeof(bool), typeof(bool)).Invoke(inv, new object[] { true, false }); } catch { }
            Plugin.Instance.Report("Inventory sorted; merged " + merged + " stack gap(s).");
        }

        private static int MergeStacks(Inventory inv)
        {
            int merges = 0;
            var items = inv.GetAllItems().ToList();
            for (int i = 0; i < items.Count; i++)
            {
                var a = items[i];
                if (a?.m_shared == null || a.m_stack <= 0) continue;
                for (int j = i + 1; j < items.Count; j++)
                {
                    var b = items[j];
                    if (b?.m_shared == null || b.m_stack <= 0) continue;
                    if (a.m_shared.m_name != b.m_shared.m_name || a.m_quality != b.m_quality || a.m_worldLevel != b.m_worldLevel) continue;
                    if (!ItemStackIdentity.Matches(a, b)) continue;
                    int room = a.m_shared.m_maxStackSize - a.m_stack;
                    if (room <= 0) break;
                    int move = Math.Min(room, b.m_stack);
                    a.m_stack += move;
                    b.m_stack -= move;
                    merges++;
                    if (b.m_stack <= 0) inv.RemoveItem(b);
                }
            }
            return merges;
        }

        private static void SortInventory(Inventory inv)
        {
            var items = inv.GetAllItems().OrderBy(i => i?.m_shared?.m_itemType.ToString() ?? "")
                .ThenBy(i => Localization.instance != null ? Localization.instance.Localize(i.m_shared.m_name) : i.m_shared.m_name)
                .ThenBy(i => i.m_quality)
                .ToList();
            foreach (var old in inv.GetAllItems().ToList()) inv.RemoveItem(old);
            foreach (var item in items)
            {
                if (item == null || item.m_stack <= 0) continue;
                inv.AddItem(item);
            }
        }

        internal static void DepositJunk()
        {
            var player = Plugin.Instance.RequirePlayer();
            var junk = ResolveJunkSet();
            var chests = ChestInventory.NearbyAccessible(player, ChestInventory.Radius);
            if (chests.Count == 0) throw new InvalidOperationException("No accessible nearby chests.");
            var inv = player.GetInventory();
            int moved = 0, stacks = 0;
            foreach (var item in inv.GetAllItems().ToList())
            {
                if (item?.m_shared == null) continue;
                if (ChestInventory.DepositSkipEquipped && player.IsItemEquiped(item)) continue;
                string prefab = ObjectDB.instance?.GetItemPrefab(item.m_shared.m_name)?.name ?? item.m_dropPrefab?.name ?? "";
                string shared = item.m_shared.m_name ?? "";
                if (!junk.Contains(prefab) && !junk.Contains(shared.TrimStart('$'))) continue;
                int stack = item.m_stack;
                if (TryMoveItemToChests(inv, item, chests))
                {
                    moved += stack;
                    stacks++;
                }
            }
            Plugin.Instance.Report($"Deposited junk: {moved} items ({stacks} stacks).");
        }

        private static bool TryMoveItemToChests(Inventory from, ItemDrop.ItemData item, List<Container> chests)
        {
            foreach (var preferExisting in new[] { true, false })
            {
                foreach (var container in chests)
                {
                    Inventory to;
                    try { to = container.GetInventory(); } catch { continue; }
                    if (to == null) continue;
                    if (preferExisting && to.CountItems(item.m_shared.m_name, item.m_quality, true) <= 0) continue;
                    if (!to.CanAddItem(item)) continue;
                    to.MoveItemToThis(from, item);
                    return true;
                }
            }
            return false;
        }

        internal static bool AllowAutoPickup(ItemDrop drop)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || !phase.AutoPickupFilter || drop?.m_itemData?.m_shared == null) return true;
            string prefab = drop.name.Replace("(Clone)", "");
            string shared = drop.m_itemData.m_shared.m_name ?? "";
            string localized = Localization.instance != null ? Localization.instance.Localize(shared) : shared;
            var white = new HashSet<string>(SplitList(PickupWhitelist), StringComparer.OrdinalIgnoreCase);
            var black = new HashSet<string>(SplitList(PickupBlacklist), StringComparer.OrdinalIgnoreCase);
            if (black.Count > 0 && (black.Contains(prefab) || black.Contains(shared) || black.Contains(localized))) return false;
            if (white.Count > 0) return white.Contains(prefab) || white.Contains(shared) || white.Contains(localized);
            return true;
        }

        // Combat helpers
        internal static void RepairAoe(Player player, float radius, bool silent)
        {
            float r = Mathf.Clamp(radius, 1, 40);
            int n = 0;
            Vector3 center = player.transform.position;
            foreach (var wear in Object.FindObjectsByType<WearNTear>(FindObjectsSortMode.None))
            {
                if (!wear || (wear.transform.position - center).sqrMagnitude > r * r) continue;
                var view = wear.GetComponent<ZNetView>();
                if (view && view.IsValid() && !view.IsOwner()) view.ClaimOwnership();
                wear.Repair();
                n++;
            }
            if (!silent && Time.frameCount % 30 == 0)
                Plugin.Instance.Report("Hammer AoE repaired " + n + " pieces.");
        }

        private static bool IsUsingHammer(Player player)
        {
            var item = player.GetCurrentWeapon();
            return item?.m_shared != null && item.m_shared.m_name != null
                && item.m_shared.m_name.IndexOf("hammer", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Farming
        /// <summary>Relax biome/placement so out-of-biome PlantEverything crops can grow. Skips decorative markers.</summary>
        internal static void PreparePlantForForcedGrowth(Plant plant)
        {
            if (!plant) return;
            if (PlantEverything.Decorative(plant)) return;
            plant.m_needCultivatedGround = false;
            plant.m_destroyIfCantGrow = false;
            plant.m_growRadius = 0f;
            plant.m_tolerateCold = true;
            plant.m_tolerateHeat = true;
            plant.m_biome = Heightmap.Biome.All;
            try { Reflection.Set(plant, "m_status", Plant.Status.Healthy); } catch { }
        }

        internal static void GrowNearbyPlants()
        {
            Plugin.Instance.RequireWorld();
            var center = Plugin.Instance.RequirePlayer().transform.position;
            float r = Plugin.Instance.World.Radius;
            int n = 0, skipped = 0;
            foreach (var plant in Object.FindObjectsByType<Plant>(FindObjectsSortMode.None))
            {
                if (!plant || (plant.transform.position - center).sqrMagnitude > r * r) continue;
                if (PlantEverything.Decorative(plant)) { skipped++; continue; }
                if (plant.m_grownPrefabs == null || plant.m_grownPrefabs.Length == 0) { skipped++; continue; }
                var view = plant.GetComponent<ZNetView>();
                if (view && view.IsValid() && !view.IsOwner()) view.ClaimOwnership();
                PreparePlantForForcedGrowth(plant);
                try
                {
                    if (plant.Grow() != null) n++;
                    else skipped++;
                }
                catch { skipped++; }
            }
            Plugin.Instance.Report(skipped > 0
                ? $"Forced grow on {n} plants ({skipped} skipped)."
                : $"Forced grow on {n} plants.");
        }

        internal static void TameNearby()
        {
            Plugin.Instance.World.Tame();
        }

        internal static void HealTames()
        {
            Plugin.Instance.RequireWorld();
            var center = Plugin.Instance.RequirePlayer().transform.position;
            float r = Mathf.Clamp(FarmHealRadius, 1, 100);
            int n = 0;
            foreach (var character in Character.GetAllCharacters())
            {
                if (!character || character is Player) continue;
                if ((character.transform.position - center).sqrMagnitude > r * r) continue;
                var tame = character.GetComponent<Tameable>();
                if (tame == null || !tame.IsTamed()) continue;
                character.SetHealth(character.GetMaxHealth());
                n++;
            }
            Plugin.Instance.Report("Healed " + n + " tamed creatures.");
        }

        internal static void SpeedBreeding()
        {
            Plugin.Instance.RequireWorld();
            var center = Plugin.Instance.RequirePlayer().transform.position;
            float r = Mathf.Clamp(AutoFeedRadius, 1, 100);
            int n = 0;
            foreach (var proc in Object.FindObjectsByType<Procreation>(FindObjectsSortMode.None))
            {
                if (!proc || (proc.transform.position - center).sqrMagnitude > r * r) continue;
                var view = proc.GetComponent<ZNetView>();
                if (view && view.IsValid() && !view.IsOwner()) view.ClaimOwnership();
                try
                {
                    bool pregnant = false;
                    try { pregnant = (bool)Reflection.Method(typeof(Procreation), "IsPregnant").Invoke(proc, null); } catch { }
                    if (!pregnant)
                    {
                        try { Reflection.Method(typeof(Procreation), "MakePregnant").Invoke(proc, null); n++; }
                        catch
                        {
                            try { Reflection.Method(typeof(Procreation), "Procreate").Invoke(proc, null); n++; } catch { }
                        }
                    }
                }
                catch { }
            }
            Plugin.Instance.Report("Marked " + n + " nearby creatures pregnant (owned Procreation components).");
        }

        private static void AutoFeed(Player player)
        {
            float r = Mathf.Clamp(AutoFeedRadius, 1, 100);
            foreach (var tame in Object.FindObjectsByType<Tameable>(FindObjectsSortMode.None))
            {
                if (!tame || !tame.IsTamed()) continue;
                if ((tame.transform.position - player.transform.position).sqrMagnitude > r * r) continue;
                bool hungry = true;
                try { hungry = (bool)Reflection.Method(typeof(Tameable), "IsHungry").Invoke(tame, null); } catch { }
                if (!hungry) continue;
                try { Reflection.Method(typeof(Tameable), "ResetFeedingTimer").Invoke(tame, null); } catch { }
            }
        }

        // Camera / photo
        internal static void SetFreecam(bool on)
        {
            if (!GameCamera.instance) throw new InvalidOperationException("Camera is not ready.");
            bool current = GameCamera.InFreeFly();
            if (current != on)
            {
                try { Reflection.Method(typeof(GameCamera), "ToggleFreeFly").Invoke(GameCamera.instance, null); }
                catch { GameCamera.instance.ToggleFreeFly(); }
            }
            Plugin.Instance.Phase.Freecam = GameCamera.InFreeFly();
            Plugin.Instance.Report("Freecam: " + (Plugin.Instance.Phase.Freecam ? "ON" : L.T("ui.off")) + " (vanilla free-fly; independent of Flight).");
        }

        internal static void SetPhotoMode(bool on)
        {
            var phase = Plugin.Instance.Phase;
            phase.PhotoMode = on;
            if (on)
            {
                phase.HideHud = true;
                if (phase.PhotoFreeze) Time.timeScale = 0.0001f;
                photoOwnedHud = true;
            }
            else
            {
                if (photoOwnedHud) { phase.HideHud = false; photoOwnedHud = false; }
                if (Mathf.Abs(Time.timeScale - 1f) > .001f) Time.timeScale = 1f;
            }
            Plugin.Instance.Report("Photo mode: " + (on ? "ON" : L.T("ui.off")) + ".");
        }

        private static void ApplyMinimapZoom(PhaseFeatures phase)
        {
            if (!Minimap.instance)
            {
                originalMinZoom = originalMaxZoom = -1;
                lastAppliedMinimapZoom = float.NaN;
                minimapRangeExpanded = false;
                return;
            }
            if (!phase.MinimapZoomControl)
            {
                RestoreMinimapZoomRange();
                lastAppliedMinimapZoom = float.NaN;
                minimapRangeExpanded = false;
                return;
            }
            float z = Mathf.Clamp(minimapZoomValue, .5f, 2.5f);
            bool largeMapOpen = false;
            try { largeMapOpen = Minimap.instance.m_mode == Minimap.MapMode.Large; } catch { largeMapOpen = true; }
            bool dirty = float.IsNaN(lastAppliedMinimapZoom) || Mathf.Abs(lastAppliedMinimapZoom - z) > .001f || !minimapRangeExpanded;
            // Corner map: only on slider change. Large map: soft maintain ~6 Hz.
            if (!dirty && !largeMapOpen) return;
            if (!dirty && Time.unscaledTime < nextMinimapMaintain) return;

            if (originalMinZoom < 0)
            {
                originalMinZoom = Minimap.instance.m_minZoom;
                originalMaxZoom = Minimap.instance.m_maxZoom;
            }
            // Tighter than 2.5.3; zoom-out no longer pulls extremely far.
            float minZ = Mathf.Max(0.001f, originalMinZoom / 2f);
            float maxZ = originalMaxZoom * 1.75f;
            if (minZ > maxZ) { float t = minZ; minZ = maxZ; maxZ = t; }
            Minimap.instance.m_minZoom = minZ;
            Minimap.instance.m_maxZoom = maxZ;
            minimapRangeExpanded = true;
            // Square the slider so high values ease toward max.
            float tNorm = Mathf.InverseLerp(.5f, 2.5f, z);
            float eased = tNorm * tNorm;
            float target = Mathf.Lerp(minZ, maxZ, eased);
            target = Mathf.Clamp(target, minZ, maxZ);
            try
            {
                Minimap.instance.LargeZoom = target;
                Minimap.instance.SmallZoom = target;
            }
            catch
            {
                try { Reflection.Set(Minimap.instance, "m_largeZoom", target); } catch { }
                try { Reflection.Set(Minimap.instance, "m_smallZoom", target); } catch { }
            }
            // 1.0.12: fields private, properties clamp to m_min/m_max. Set via reflection too.
            try { Reflection.Set(Minimap.instance, "m_largeZoom", target); } catch { }
            try { Reflection.Set(Minimap.instance, "m_smallZoom", target); } catch { }
            lastAppliedMinimapZoom = z;
            nextMinimapMaintain = Time.unscaledTime + (largeMapOpen ? .15f : 1f);
        }

        private static void RestoreMinimapZoomRange()
        {
            if (originalMinZoom < 0 || !Minimap.instance) { originalMinZoom = originalMaxZoom = -1; return; }
            Minimap.instance.m_minZoom = originalMinZoom;
            Minimap.instance.m_maxZoom = originalMaxZoom;
            originalMinZoom = originalMaxZoom = -1;
        }

        // Death recovery
        internal static void TeleportToTombstone()
        {
            Plugin.Instance.RequireWorld();
            var player = Plugin.Instance.RequirePlayer();
            TombStone best = null;
            float bestDist = float.MaxValue;
            foreach (var grave in Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None))
            {
                if (!grave) continue;
                var view = grave.GetComponent<ZNetView>();
                if (!view || !view.IsValid()) continue;
                if (view.GetZDO().GetLong(ZDOVars.s_owner, 0) != player.GetPlayerID()) continue;
                float d = (grave.transform.position - player.transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = grave; }
            }
            if (!best) throw new InvalidOperationException("No owned tombstone found in loaded areas.");
            Plugin.Instance.Features.Teleport(best.transform.position + Vector3.up);
        }

        internal static void ReclaimTombstone()
        {
            Plugin.Instance.RequireWorld();
            var player = Plugin.Instance.RequirePlayer();
            int n = 0;
            foreach (var grave in Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None))
            {
                if (!grave) continue;
                var view = grave.GetComponent<ZNetView>();
                if (!view || !view.IsValid()) continue;
                if (view.GetZDO().GetLong(ZDOVars.s_owner, 0) != player.GetPlayerID()) continue;
                if ((grave.transform.position - player.transform.position).sqrMagnitude > 20f * 20f) continue;
                try
                {
                    Reflection.Method(typeof(TombStone), "Interact", typeof(Humanoid), typeof(bool), typeof(bool))
                        .Invoke(grave, new object[] { player, false, false });
                    n++;
                }
                catch
                {
                    // Fallback: pull container contents
                    var container = grave.GetComponent<Container>();
                    if (container == null) continue;
                    Inventory src;
                    try { src = container.GetInventory(); } catch { continue; }
                    if (src == null) continue;
                    foreach (var item in src.GetAllItems().ToList())
                    {
                        if (item == null) continue;
                        player.GetInventory().MoveItemToThis(src, item);
                    }
                    n++;
                }
            }
            Plugin.Instance.Report(n > 0 ? "Reclaimed " + n + " nearby tombstone(s)." : L.T("ui.no_nearby_owned_tombstone_to"));
        }

        // Sit / mount
        internal static void SitAnywhere()
        {
            var player = Plugin.Instance.RequirePlayer();
            try
            {
                var method = typeof(Character).GetMethod("StartEmote", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(string), typeof(bool) }, null)
                    ?? typeof(Player).GetMethod("StartEmote", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(string), typeof(bool) }, null);
                if (method == null) throw new MissingMethodException("StartEmote");
                method.Invoke(player, new object[] { "sit", false });
                Plugin.Instance.Report(L.T("ui.sitting_emote"));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Sit failed: " + ex.GetBaseException().Message);
            }
        }

        internal static void MountNearest()
        {
            var player = Plugin.Instance.RequirePlayer();
            Sadle best = null;
            float bestDist = float.MaxValue;
            foreach (var sadle in Object.FindObjectsByType<Sadle>(FindObjectsSortMode.None))
            {
                if (!sadle) continue;
                float d = (sadle.transform.position - player.transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = sadle; }
            }
            if (!best || bestDist > 40f * 40f) throw new InvalidOperationException("No mount saddle within 40 m.");
            MountBypass = Plugin.Instance.Phase.MountAnyCreature;
            try
            {
                Reflection.Method(typeof(Sadle), "Interact", typeof(Humanoid), typeof(bool), typeof(bool))
                    .Invoke(best, new object[] { player, false, false });
                Plugin.Instance.Report(L.T("ui.tried_to_mount_nearest_saddle"));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Mount failed: " + ex.GetBaseException().Message);
            }
            finally { MountBypass = false; }
        }

        internal static void MountLookTarget()
        {
            var player = Plugin.Instance.RequirePlayer();
            if (!TryLookCreature(player, out var character))
                throw new InvalidOperationException("Look at a living creature.");
            MountCharacter(player, character);
        }

        private static void TickMountNext(Player player)
        {
            if (!MountNextArmed || Plugin.Instance.Open) return;
            if (!TryLookCreature(player, out var character))
            {
                mountNextCandidate = null;
                return;
            }
            if (character == looseMount)
            {
                MountNextArmed = false;
                mountNextCandidate = null;
                return;
            }
            if (character != mountNextCandidate)
            {
                mountNextCandidate = character;
                mountNextSince = Time.unscaledTime;
                return;
            }
            if (Time.unscaledTime - mountNextSince < 0.2f) return;
            MountNextArmed = false;
            mountNextCandidate = null;
            try { MountCharacter(player, character); }
            catch (Exception ex) { Plugin.Instance.Report(ex.GetBaseException().Message); }
        }

        private static bool TryLookCreature(Player player, out Character character)
        {
            character = null;
            var cam = GameCamera.instance ? GameCamera.instance.transform : (player.m_eye ? player.m_eye : player.transform);
            int mask = LayerMask.GetMask("character", "character_trigger", "Default", "character_noenv");
            if (mask == 0) mask = ~0;
            if (!Physics.Raycast(cam.position, cam.forward, out var hit, 40f, mask, QueryTriggerInteraction.Collide))
                return false;
            character = hit.collider.GetComponentInParent<Character>();
            return character && character != player && !character.IsDead() && !(character is Player);
        }

        private static void MountCharacter(Player player, Character character)
        {
            var saddle = character.GetComponentInChildren<Sadle>();
            if (saddle)
            {
                MountBypass = Plugin.Instance.Phase.MountAnyCreature;
                try
                {
                    Reflection.Method(typeof(Sadle), "Interact", typeof(Humanoid), typeof(bool), typeof(bool))
                        .Invoke(saddle, new object[] { player, false, false });
                    Plugin.Instance.Report("Tried to mount " + character.m_name + ".");
                }
                finally { MountBypass = false; }
                return;
            }
            if (Plugin.Instance.Phase.MountAnyCreature != true)
                throw new InvalidOperationException("That creature has no saddle. Turn on Mount any creature.");
            AttachLoose(player, character);
            Plugin.Instance.Report("Mounted " + character.m_name + ".");
        }

        internal static void Dismount()
        {
            var player = Plugin.Instance.RequirePlayer();
            AllowDismount = true;
            try
            {
                var saddle = CurrentSaddle(player);
                if (saddle)
                {
                    try
                    {
                        Reflection.Method(typeof(Sadle), "RPC_ReleaseControl", typeof(long), typeof(long))
                            .Invoke(saddle, new object[] { player.GetPlayerID(), player.GetPlayerID() });
                    }
                    catch { }
                }
                DetachLoose(player);
                Plugin.Instance.Report("Dismounted.");
            }
            finally { AllowDismount = false; }
        }

        private static void TickFreecamBuild(Player player)
        {
            var phase = Plugin.Instance.Phase;
            if (!player || player.IsDead() || player.IsTeleporting())
            {
                RestoreFreecamBuild(player);
                EnsurePlayerPhysics(player);
                DragBuilding.Cancel();
                return;
            }
            if (!phase.FreecamBuild)
            {
                if (buildCamOwned) RestoreFreecamBuild(player);
                else EnsurePlayerPhysics(player);
                return;
            }
            if (GameCamera.instance && !GameCamera.InFreeFly())
            {
                try { SetFreecam(true); } catch { }
            }
            var cam = GameCamera.instance ? GameCamera.instance.transform : null;
            if (!cam) return;
            if (!buildCamOwned)
            {
                buildCamOwned = true;
                buildBody = player.GetComponent<Rigidbody>();
                if (buildBody)
                {
                    buildPrevGravity = buildBody.useGravity;
                    buildBody.useGravity = false;
                }
            }
            StickFreecam(player, cam.position);
        }

        internal static void StickFreecam(Player player, Vector3 pos)
        {
            if (!player || Plugin.Instance?.Phase.FreecamBuild != true) return;
            if (player.IsTeleporting()) return;
            player.transform.position = pos;
            if (!buildBody) buildBody = player.GetComponent<Rigidbody>();
            if (buildBody)
            {
                buildBody.useGravity = false;
                buildBody.position = pos;
                buildBody.linearVelocity = Vector3.zero;
                buildBody.angularVelocity = Vector3.zero;
            }
            try { Reflection.Set(player, "m_maxAirAltitude", pos.y); } catch { }
        }

        private static void RestoreFreecamBuild(Player player)
        {
            if (!buildCamOwned) return;
            buildCamOwned = false;
            if (buildBody)
            {
                buildBody.useGravity = true;
                buildBody = null;
            }
            else if (player)
            {
                var body = player.GetComponent<Rigidbody>();
                if (body) body.useGravity = true;
            }
            ResetAirAltitude(player);
        }

        /// <summary>Clear leftover no-gravity / freefall state after freecam-build, portals, or mount detach.</summary>
        internal static void EnsurePlayerPhysics(Player player)
        {
            if (!player) return;
            var phase = Plugin.Instance?.Phase;
            bool keepFloat = phase != null && (phase.FreecamBuild || phase.FlyingMount || player.IsDebugFlying());
            if (keepFloat) return;
            var body = player.GetComponent<Rigidbody>();
            if (body && !body.useGravity)
            {
                body.useGravity = true;
                body.linearVelocity = new Vector3(body.linearVelocity.x, Mathf.Min(0f, body.linearVelocity.y), body.linearVelocity.z);
            }
            ResetAirAltitude(player);
        }

        private static void ResetAirAltitude(Player player)
        {
            if (!player) return;
            try
            {
                float y = player.transform.position.y;
                Reflection.Set(player, "m_maxAirAltitude", y);
                Reflection.Set(player, "m_baseAirAltitude", y);
            }
            catch
            {
                try { Reflection.Set(player, "m_maxAirAltitude", player.transform.position.y); } catch { }
            }
        }

        private static void TickFlyingMount(Player player)
        {
            if (Plugin.Instance.Phase.FlyingMount != true)
            {
                RestoreFlyingBody();
                return;
            }
            if (!player || player.IsTeleporting() || player.IsDead())
            {
                RestoreFlyingBody();
                EnsurePlayerPhysics(player);
                return;
            }
            var saddle = CurrentSaddle(player);
            if (!saddle) return;
            bool local = false;
            try { local = (bool)Reflection.Method(typeof(Sadle), "IsLocalUser").Invoke(saddle, null); }
            catch { return; }
            if (!local) return;
            Character mount = null;
            try { mount = Reflection.Get<Character>(saddle, "m_character"); } catch { mount = saddle.GetComponentInParent<Character>(); }
            var body = mount ? mount.GetComponent<Rigidbody>() : null;
            if (!body) return;
            if (looseBody != body)
            {
                RestoreFlyingBody();
                looseBody = body;
                loosePrevGravity = body.useGravity;
                looseBodyOwned = true;
            }
            DriveFly(body, player, hover: true, driveHorizontal: false);
        }

        private static void RestoreFlyingBody()
        {
            if (!looseBodyOwned || !looseBody) { looseBodyOwned = false; looseBody = null; return; }
            looseBody.useGravity = true;
            looseBodyOwned = false;
            looseBody = null;
        }

        private static void DriveFly(Rigidbody body, Player player, bool hover, bool driveHorizontal)
        {
            body.useGravity = false;
            Vector3 vel = body.linearVelocity;
            bool up = ZInput.GetButton("Jump");
            bool down = ZInput.GetButton("Crouch");
            vel.y = up ? 10f : (down ? -10f : (hover ? 0f : vel.y));
            if (driveHorizontal)
            {
                Vector3 wish = FlatWish(player);
                if (wish.sqrMagnitude > 0.01f)
                {
                    wish.Normalize();
                    vel.x = wish.x * 8f;
                    vel.z = wish.z * 8f;
                }
                else if (hover)
                {
                    vel.x = 0f;
                    vel.z = 0f;
                }
            }
            body.linearVelocity = vel;
            try { Reflection.Set(player, "m_maxAirAltitude", player.transform.position.y); } catch { }
        }

        private static Vector3 FlatWish(Player player)
        {
            Vector3 fwd = player.GetLookDir();
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = player.transform.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 wish = Vector3.zero;
            if (ZInput.GetButton("Forward")) wish += fwd;
            if (ZInput.GetButton("Backward")) wish -= fwd;
            if (ZInput.GetButton("Left")) wish -= right;
            if (ZInput.GetButton("Right")) wish += right;
            return wish;
        }

        private static Sadle CurrentSaddle(Player player)
        {
            if (!player) return null;
            try
            {
                var doodad = Reflection.Get<object>(player, "m_doodadController");
                return doodad as Sadle;
            }
            catch { return null; }
        }

        private static void AttachLoose(Player player, Character mount)
        {
            DetachLoose(player);
            looseMount = mount;
            var view = mount.GetComponent<ZNetView>();
            if (view && view.IsValid() && !view.IsOwner()) view.ClaimOwnership();
            looseAi = mount.GetComponent<MonsterAI>();
            if (looseAi && looseAi.enabled) { looseAi.enabled = false; looseAiDisabled = true; }
            loosePlayerBody = player.GetComponent<Rigidbody>();
            if (loosePlayerBody)
            {
                loosePlayerPrevGravity = loosePlayerBody.useGravity;
                loosePlayerWasKinematic = loosePlayerBody.isKinematic;
                loosePlayerBody.useGravity = false;
                loosePlayerBody.isKinematic = true;
            }
            IgnoreSeat(player, mount, true);
        }

        private static void TickLooseMount(Player player)
        {
            if (!looseMount)
                return;
            if (!player || player.IsTeleporting() || player.IsDead() || looseMount.IsDead())
            {
                DetachLoose(player);
                EnsurePlayerPhysics(player);
                return;
            }
            Vector3 seat = looseMount.transform.TransformPoint(new Vector3(0f, 1.15f, 0.15f));
            player.transform.position = seat;
            player.transform.rotation = Quaternion.Euler(0f, looseMount.transform.eulerAngles.y, 0f);
            if (loosePlayerBody)
            {
                loosePlayerBody.useGravity = false;
                loosePlayerBody.position = seat;
                loosePlayerBody.linearVelocity = Vector3.zero;
            }
            var body = looseMount.GetComponent<Rigidbody>();
            if (!body) return;
            bool fly = Plugin.Instance.Phase.FlyingMount;
            DriveLoose(looseMount, player, fly);
            if (!fly && body.useGravity == false) body.useGravity = true;
            try { Reflection.Set(player, "m_maxAirAltitude", player.transform.position.y); } catch { }
        }

        private static void DriveLoose(Character mount, Player player, bool fly)
        {
            looseWish = FlatWish(player);
            looseRun = ZInput.GetButton("Run");
            looseUp = ZInput.GetButton("Jump");
            looseDown = ZInput.GetButton("Crouch");
            bool moving = looseWish.sqrMagnitude > 0.01f;
            if (moving) looseWish.Normalize();
            mount.SetMoveDir(moving ? looseWish : Vector3.zero);
            mount.SetRun(moving && looseRun);
            if (moving) mount.SetLookDir(looseWish);
            if (fly)
            {
                var body = mount.GetComponent<Rigidbody>();
                if (body) body.useGravity = false;
            }
        }

        internal static void AfterMountPhysics(Character mount)
        {
            if (mount != looseMount) return;
            var body = mount.GetComponent<Rigidbody>();
            var player = Player.m_localPlayer;
            if (!body || !player) return;
            bool fly = Plugin.Instance != null && Plugin.Instance.Phase.FlyingMount;
            body.useGravity = !fly;
            Vector3 wish = looseWish;
            bool moving = wish.sqrMagnitude > 0.01f;
            if (moving) wish.Normalize();
            float speed = moving && looseRun ? mount.m_runSpeed : mount.m_speed;
            if (speed < 2f) speed = 6f;
            Vector3 vel = body.linearVelocity;
            if (moving)
            {
                vel.x = wish.x * speed;
                vel.z = wish.z * speed;
                var face = Quaternion.LookRotation(wish);
                mount.transform.rotation = face;
                body.rotation = face;
                body.angularVelocity = Vector3.zero;
            }
            else
            {
                vel.x = 0f;
                vel.z = 0f;
                body.angularVelocity = Vector3.zero;
            }
            if (fly)
                vel.y = looseUp ? 10f : (looseDown ? -10f : 0f);
            body.linearVelocity = vel;
            Vector3 seat = mount.transform.TransformPoint(new Vector3(0f, 1.15f, 0.15f));
            player.transform.position = seat;
            player.transform.rotation = Quaternion.Euler(0f, mount.transform.eulerAngles.y, 0f);
            if (loosePlayerBody)
            {
                loosePlayerBody.position = seat;
                loosePlayerBody.linearVelocity = Vector3.zero;
                loosePlayerBody.angularVelocity = Vector3.zero;
            }
        }

        private static void IgnoreSeat(Player player, Character mount, bool ignore)
        {
            if (!player || !mount) return;
            var playerCols = player.GetComponentsInChildren<Collider>();
            var mountCols = mount.GetComponentsInChildren<Collider>();
            for (int i = 0; i < playerCols.Length; i++)
            {
                var a = playerCols[i];
                if (!a) continue;
                for (int j = 0; j < mountCols.Length; j++)
                {
                    var b = mountCols[j];
                    if (!b || a == b) continue;
                    Physics.IgnoreCollision(a, b, ignore);
                }
            }
        }

        private static void DetachLoose(Player player)
        {
            if (looseAiDisabled && looseAi) looseAi.enabled = true;
            looseAiDisabled = false;
            looseAi = null;
            if (loosePlayerBody)
            {
                loosePlayerBody.useGravity = true;
                loosePlayerBody.isKinematic = loosePlayerWasKinematic;
                loosePlayerBody = null;
            }
            if (player && looseMount) IgnoreSeat(player, looseMount, false);
            looseMount = null;
            MountNextArmed = false;
            mountNextCandidate = null;
            EnsurePlayerPhysics(player);
        }

        internal static string SpawnModeLabel(int mode)
        {
            switch (mode)
            {
                case SpawnChest: return "Nearby chest";
                case SpawnGround: return "On ground";
                default: return "Inventory";
            }
        }

        internal static string JunkDefaultsText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < DefaultJunk.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(DefaultJunk[i]);
            }
            return sb.ToString();
        }
    }

    // Patches
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
    internal static class OneHitStructuresPatch
    {
        private static void Prefix(WearNTear __instance, HitData hit)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || !phase.OneHitStructures || hit == null) return;
            if (hit.GetAttacker() != Player.m_localPlayer) return;
            float hp = __instance.m_health;
            try
            {
                var view = __instance.GetComponent<ZNetView>();
                if (view && view.IsValid())
                    hp = view.GetZDO().GetFloat(ZDOVars.s_health, __instance.m_health);
            }
            catch { }
            hit.m_damage.m_damage = Mathf.Max(hit.m_damage.m_damage, hp + 1f);
            hit.m_damage.m_chop = Mathf.Max(hit.m_damage.m_chop, hp + 1f);
            hit.m_damage.m_pickaxe = Mathf.Max(hit.m_damage.m_pickaxe, hp + 1f);
        }
    }

    [HarmonyPatch(typeof(ItemDrop), "CanPickup")]
    internal static class AutoPickupFilterPatch
    {
        private static void Postfix(ItemDrop __instance, ref bool __result)
        {
            if (!__result) return;
            if (!Release250.AllowAutoPickup(__instance)) __result = false;
        }
    }

    [HarmonyPatch(typeof(Chat), nameof(Chat.SendPing))]
    internal static class TpToPingPatch
    {
        private static float lastPingTeleport;
        private static void Postfix(Vector3 position)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || !phase.TpToPing) return;
            if (!Player.m_localPlayer || Player.m_localPlayer.IsDead() || Player.m_localPlayer.IsTeleporting()) return;
            if (Time.unscaledTime - lastPingTeleport < .4f) return;
            lastPingTeleport = Time.unscaledTime;
            Plugin.Instance.Features.Teleport(position + Vector3.up);
        }
    }

    [HarmonyPatch(typeof(Hud), "UpdateBlackScreen")]
    internal static class PortalFadeSkipPatch
    {
        private static bool Prefix()
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null || !phase.DisablePortalFade) return true;
            var player = Player.m_localPlayer;
            if (!player || !player.IsTeleporting()) return true;
            try
            {
                if (Hud.instance && Hud.instance.m_loadingScreen)
                    Hud.instance.m_loadingScreen.alpha = 0f;
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class FreecamBuildInputPatch
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (__result || __instance != Player.m_localPlayer) return;
            if (Plugin.Instance?.Phase.FreecamBuild != true || !GameCamera.InFreeFly()) return;
            if (__instance.IsDead() || __instance.InCutscene() || __instance.IsTeleporting()) return;
            if (Chat.instance && Chat.instance.HasFocus()) return;
            if (Console.IsVisible() || TextInput.IsVisible() || StoreGui.IsVisible() || InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen()) return;
            if (TextViewer.instance && TextViewer.instance.IsVisible()) return;
            try { if (PlayerCustomizaton.IsBarberGuiVisible()) return; } catch { }
            try { if (Hud.instance && Hud.instance.m_buildUi != null && Hud.instance.m_buildUi.SearchFieldFocused) return; } catch { }
            __result = true;
        }
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateFreeFly", typeof(float))]
    internal static class FreecamSyncPatch
    {
        private static bool Prefix()
        {
            // Piece list needs the cursor; free-fly would keep turning the camera under it.
            if (Plugin.Instance?.Phase.FreecamBuild != true || !GameCamera.InFreeFly()) return true;
            try { return !Hud.IsPieceSelectionVisible(); }
            catch { return true; }
        }

        private static void Postfix(GameCamera __instance)
        {
            var phase = Plugin.Instance?.Phase;
            if (phase == null) return;
            bool free = GameCamera.InFreeFly();
            if (phase.Freecam != free) phase.Freecam = free;
            // Attack/SecondaryAttack are free-fly focus locks; a build click would pin the camera.
            if (!phase.FreecamBuild || !free) return;
            try
            {
                Reflection.Set(__instance, "m_freeFlyTarget", null);
                Reflection.Set(__instance, "m_freeFlyLockon", null);
                float yaw = Reflection.Get<float>(__instance, "m_freeFlyYaw");
                float pitch = Reflection.Get<float>(__instance, "m_freeFlyPitch");
                __instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(pitch, 0f, 0f);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class KeepFoodOnDeathPatch
    {
        private static List<object> savedFoods;

        private static void Prefix(Player __instance)
        {
            savedFoods = null;
            if (Plugin.Instance?.Phase.KeepFoodOnDeath != true || __instance != Player.m_localPlayer) return;
            try
            {
                var foods = Reflection.Get<System.Collections.IList>(__instance, "m_foods");
                if (foods == null) return;
                savedFoods = new List<object>();
                foreach (var entry in foods) savedFoods.Add(entry);
            }
            catch { savedFoods = null; }
        }

        private static void Postfix(Player __instance)
        {
            if (savedFoods == null || __instance != Player.m_localPlayer) return;
            try
            {
                var foods = Reflection.Get<System.Collections.IList>(__instance, "m_foods");
                if (foods == null) return;
                foods.Clear();
                foreach (var entry in savedFoods) foods.Add(entry);
            }
            catch { }
            savedFoods = null;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
    internal static class LooseMountSteerPatch
    {
        private static void Postfix(Character __instance)
        {
            Release250.AfterMountPhysics(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class LevelOnPlacePatch
    {
        private static void Postfix(Player __instance, bool __result)
        {
            if (!__result || __instance != Player.m_localPlayer || !Release250.LevelOnPlace) return;
            if (!HoldingBuildHammer(__instance)) return;
            // TryPlacePiece gets the catalog prefab, not the build site. Use the ghost.
            GameObject ghost = null;
            try { ghost = Reflection.Get<GameObject>(__instance, "m_placementGhost"); } catch { ghost = null; }
            if (!ghost) return;
            if (ghost.GetComponent<Plant>() || ghost.GetComponentInChildren<Plant>(true)) return;
            try { Release250.LevelUnderPlaced(ghost); }
            catch (Exception ex) { Plugin.Instance?.Report(ex.Message); }
        }

        private static bool HoldingBuildHammer(Player player) => Release250.IsBuildHammer(player);
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class FreecamBuildStickPatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer || Plugin.Instance == null) return;
            if (Plugin.Instance.Phase.FreecamBuild && GameCamera.instance)
                Release250.StickFreecam(__instance, GameCamera.instance.transform.position);
        }
    }

    [HarmonyPatch(typeof(Sadle), "Interact")]
    internal static class MountAnySaddlePatch
    {
        private static void Prefix(Sadle __instance)
        {
            if (!Release250.MountBypass && Plugin.Instance?.Phase.MountAnyCreature != true) return;
            if (!Release250.MountBypass) return;
            try
            {
                var tame = Reflection.Get<Tameable>(__instance, "m_tambable");
                if (tame && !tame.IsTamed())
                    Reflection.Method(typeof(Tameable), "Tame").Invoke(tame, null);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Sadle), "RPC_ReleaseControl")]
    internal static class FlyingMountKeepSeatPatch
    {
        private static bool Prefix(Sadle __instance)
        {
            if (Release250.AllowDismount) return true;
            if (Plugin.Instance?.Phase.FlyingMount != true) return true;
            try
            {
                bool local = (bool)Reflection.Method(typeof(Sadle), "IsLocalUser").Invoke(__instance, null);
                return !local;
            }
            catch { return true; }
        }
    }
}
