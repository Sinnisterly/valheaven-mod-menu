using System;
using System.Collections.Generic;
using HarmonyLib;
using Splatform;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ValheimAdminMenu
{
    /// <summary>
    /// Hold-place on walls/floors/beams/roofs only. Quick click places normally;
    /// hold past a short delay (or drag along the snap axis) to stretch a snap-stepped line.
    /// </summary>
    internal static class DragBuilding
    {
        // Off by default: when on, only walls/floors/beams/roofs arm; click still places.
        internal static bool Enabled;
        internal static int MaxPieces = 500;
        internal static bool Active;
        internal static int PreviewCount => positions.Count;

        private const float HoldDelay = 0.2f;
        private const float MoveStartFrac = 0.45f;

        private static readonly List<Vector3> positions = new List<Vector3>();
        private static readonly List<Quaternion> rotations = new List<Quaternion>();
        private static readonly List<GameObject> ghosts = new List<GameObject>();
        private static readonly List<Transform> snapScratch = new List<Transform>();
        private static readonly RaycastHit[] rayHits = new RaycastHit[16];

        private static Piece dragPiece;
        private static GameObject dragPrefab;
        private static bool holding;
        private static bool armed;
        private static float armedAt;
        private static Vector3 startPos;
        private static Quaternion startRot;
        private static Vector3 stepDir;
        private static float stepLen;
        private static int lastCount = -1;
        private static int lastSign = 1;

        internal static void Tick(Player player)
        {
            if (!player || player != Player.m_localPlayer)
            {
                Cancel();
                return;
            }
            if (player.IsTeleporting() || player.IsDead())
            {
                Cancel();
                return;
            }
            if (!Enabled)
            {
                Cancel();
                return;
            }
            if (Plugin.Instance != null && Plugin.Instance.Open)
            {
                Cancel();
                return;
            }
            if (Release250.PastePreviewActive) { Cancel(); return; }
            if (!player.InPlaceMode() || !Release250.IsBuildHammer(player)) { Cancel(); return; }

            bool attackUp = Input.GetMouseButtonUp(0) || ZInput.GetButtonUp("Attack");
            bool attackHeld = Input.GetMouseButton(0) || ZInput.GetButton("Attack");

            if (armed && !holding)
            {
                if (attackUp || !attackHeld)
                {
                    PlaceArmedSingle(player);
                    ClearArm();
                    return;
                }
                float held = Time.unscaledTime - armedAt;
                Vector3 tip = AimPoint(player, startPos);
                float along = Mathf.Abs(Vector3.Dot(tip - startPos, stepDir));
                if (held >= HoldDelay || along >= stepLen * MoveStartFrac)
                {
                    BeginFromArm();
                    ClearArm();
                }
                return;
            }

            if (!holding) return;

            if (attackUp)
            {
                Commit(player);
                return;
            }
            if (Input.GetMouseButtonDown(1) || ZInput.GetButtonDown("SecondaryAttack") || Input.GetKeyDown(KeyCode.Escape))
            {
                Cancel();
                return;
            }
            if (!attackHeld)
            {
                Commit(player);
                return;
            }
            UpdateDrag(player);
        }

        /// <summary>Called from TryPlacePiece: arm drag instead of placing when piece is drag-eligible.</summary>
        private static bool TryArm(Player player, Piece piece)
        {
            if (!Enabled || !player || !piece || holding || armed) return false;
            if (Release250.PastePreviewActive) return false;
            if (!Release250.IsBuildHammer(player)) return false;
            if (!IsDragSupported(piece)) return false;
            var ghost = Reflection.Get<GameObject>(player, "m_placementGhost");
            if (!ghost || !ghost.activeInHierarchy) return false;

            PlacementSnap.AlignGhost(ghost);
            if (!TryBuildStep(ghost, out stepDir, out stepLen)) return false;
            var prefab = ResolvePrefab(piece);
            if (!prefab) return false;

            dragPiece = piece;
            dragPrefab = prefab;
            startPos = ghost.transform.position;
            startRot = ghost.transform.rotation;
            armed = true;
            armedAt = Time.unscaledTime;
            lastCount = -1;
            lastSign = 1;
            return true;
        }

        private static void BeginFromArm()
        {
            if (!dragPrefab || !dragPiece) return;
            holding = true;
            Active = true;
            RebuildLine(1, 1);
        }

        private static void ClearArm()
        {
            armed = false;
            armedAt = 0f;
            if (!holding)
            {
                dragPiece = null;
                dragPrefab = null;
            }
        }

        private static void PlaceArmedSingle(Player player)
        {
            if (!player || !dragPiece || !dragPrefab) return;
            Vector3 pos = startPos;
            Quaternion rot = startRot;
            var ghost = Reflection.Get<GameObject>(player, "m_placementGhost");
            if (ghost && ghost.activeInHierarchy)
            {
                pos = ghost.transform.position;
                rot = ghost.transform.rotation;
            }
            try
            {
                var recipe = dragPiece.GetComponent<CreativeRecipe>();
                if (recipe && CreativeHammer.Enabled && CreativeHammer.UsingHammer(player))
                    CreativeHammer.Place(player, recipe, pos, rot);
                else
                {
                    TerrainModifier.SetTriggerOnPlaced(true);
                    var obj = Object.Instantiate(dragPrefab, pos, rot);
                    TerrainModifier.SetTriggerOnPlaced(false);
                    var view = obj.GetComponent<ZNetView>();
                    if (view && view.IsValid() && !view.IsOwner()) view.ClaimOwnership();
                    var piece = obj.GetComponent<Piece>();
                    if (piece)
                    {
                        try
                        {
                            long id = player.GetPlayerID();
                            PlatformUserID platform = default;
                            try { platform = PlatformManager.DistributionPlatform.LocalUser.PlatformUserID; } catch { }
                            piece.SetCreator(id, platform);
                        }
                        catch { }
                    }
                    try { obj.GetComponent<WearNTear>()?.OnPlaced(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Plugin.Instance?.Report(ex.Message);
            }
        }

        private static void UpdateDrag(Player player)
        {
            if (!dragPiece || stepLen < 0.01f) { Cancel(); return; }

            Vector3 tip = AimPoint(player, startPos);
            Vector3 delta = tip - startPos;
            float along = Vector3.Dot(delta, stepDir);
            int sign = along >= 0f ? 1 : -1;
            float dist = Mathf.Abs(along);
            int count = Mathf.Clamp(Mathf.RoundToInt(dist / stepLen) + 1, 1, MaxPieces);
            if (count == lastCount && sign == lastSign) return;
            RebuildLine(count, sign);
        }

        private static Vector3 AimPoint(Player player, Vector3 fallback)
        {
            var cam = GameCamera.instance ? GameCamera.instance.transform : Camera.main?.transform;
            if (!cam) return fallback;
            int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, rayHits, 80f, mask, QueryTriggerInteraction.Ignore);
            if (n <= 0) return fallback;
            float best = float.MaxValue;
            Vector3 hit = fallback;
            for (int i = 0; i < n; i++)
            {
                var h = rayHits[i];
                if (!h.collider) continue;
                if (h.collider.GetComponentInParent<Player>() == player) continue;
                string name = h.collider.transform.root ? h.collider.transform.root.name : "";
                if (name.IndexOf("ValheavenDragGhost", StringComparison.Ordinal) >= 0) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    hit = h.point;
                }
            }
            return best < float.MaxValue ? hit : fallback;
        }

        private static void RebuildLine(int count, int sign)
        {
            lastCount = count;
            lastSign = sign;
            ClearGhosts();
            positions.Clear();
            rotations.Clear();
            Vector3 step = stepDir * stepLen * sign;
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = startPos + step * i;
                positions.Add(pos);
                rotations.Add(startRot);
                SpawnGhost(pos, startRot);
            }

            var player = Player.m_localPlayer;
            if (!player) return;
            var ghost = Reflection.Get<GameObject>(player, "m_placementGhost");
            if (ghost && ghost.activeInHierarchy && positions.Count > 0)
            {
                ghost.transform.position = positions[positions.Count - 1];
                ghost.transform.rotation = startRot;
            }
        }

        private static void Commit(Player player)
        {
            if (!holding) return;
            holding = false;
            Active = false;
            if (positions.Count == 0 || !dragPrefab)
            {
                ClearGhosts();
                dragPiece = null;
                dragPrefab = null;
                return;
            }

            int placed = 0;
            for (int i = 0; i < positions.Count; i++)
            {
                try
                {
                    TerrainModifier.SetTriggerOnPlaced(true);
                    var obj = Object.Instantiate(dragPrefab, positions[i], rotations[i]);
                    TerrainModifier.SetTriggerOnPlaced(false);
                    var view = obj.GetComponent<ZNetView>();
                    if (view && view.IsValid() && !view.IsOwner()) view.ClaimOwnership();
                    var piece = obj.GetComponent<Piece>();
                    if (piece)
                    {
                        try
                        {
                            long id = player.GetPlayerID();
                            PlatformUserID platform = default;
                            try { platform = PlatformManager.DistributionPlatform.LocalUser.PlatformUserID; } catch { }
                            piece.SetCreator(id, platform);
                        }
                        catch { }
                    }
                    try { obj.GetComponent<WearNTear>()?.OnPlaced(); } catch { }
                    placed++;
                }
                catch { }
            }
            ClearGhosts();
            dragPiece = null;
            dragPrefab = null;
            positions.Clear();
            rotations.Clear();
            lastCount = -1;
            if (Plugin.Instance) Plugin.Instance.Report("Drag placed " + placed + " pieces.");
        }

        internal static void Cancel()
        {
            holding = false;
            Active = false;
            armed = false;
            armedAt = 0f;
            ClearGhosts();
            dragPiece = null;
            dragPrefab = null;
            positions.Clear();
            rotations.Clear();
            lastCount = -1;
            lastSign = 1;
        }

        private static void SpawnGhost(Vector3 pos, Quaternion rot)
        {
            if (!dragPrefab) return;
            bool prior = ZNetView.m_forceDisableInit;
            ZNetView.m_forceDisableInit = true;
            try
            {
                var g = Object.Instantiate(dragPrefab, pos, rot);
                g.name = "ValheavenDragGhost";
                foreach (var col in g.GetComponentsInChildren<Collider>(true))
                    if (col) col.enabled = false;
                foreach (var view in g.GetComponentsInChildren<ZNetView>(true))
                    if (view) view.enabled = false;
                ghosts.Add(g);
            }
            finally { ZNetView.m_forceDisableInit = prior; }
        }

        private static void ClearGhosts()
        {
            for (int i = 0; i < ghosts.Count; i++)
                if (ghosts[i]) Object.Destroy(ghosts[i]);
            ghosts.Clear();
        }

        private static bool IsDragSupported(Piece piece)
        {
            if (!piece) return false;
            // Never drag creative proxies / plants / terrain tools by snap-count alone.
            if (piece.GetComponent<CreativeRecipe>()) return false;
            if (piece.GetComponent<Plant>()) return false;
            if (piece.GetComponent<TerrainModifier>() || piece.GetComponentInChildren<TerrainModifier>(true)) return false;

            string n = (piece.name ?? "") + " " + (piece.m_name ?? "");
            bool named =
                n.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("beam", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("roof", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("pillar", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!named) return false;

            snapScratch.Clear();
            try { piece.GetSnapPoints(snapScratch); } catch { return false; }
            return snapScratch.Count >= 2;
        }

        private static bool TryBuildStep(GameObject ghost, out Vector3 dir, out float length)
        {
            dir = Vector3.right;
            length = 0f;
            if (!ghost) return false;
            var piece = ghost.GetComponent<Piece>();
            if (!piece) return false;
            snapScratch.Clear();
            try { piece.GetSnapPoints(snapScratch); } catch { return false; }
            if (snapScratch.Count < 2) return false;

            Vector3 right = ghost.transform.right;
            Vector3 forward = ghost.transform.forward;
            right.y = 0f;
            forward.y = 0f;
            if (right.sqrMagnitude > 0.0001f) right.Normalize();
            if (forward.sqrMagnitude > 0.0001f) forward.Normalize();

            float best = 0f;
            Vector3 bestDelta = Vector3.zero;
            for (int i = 0; i < snapScratch.Count; i++)
            {
                if (!snapScratch[i]) continue;
                for (int j = i + 1; j < snapScratch.Count; j++)
                {
                    if (!snapScratch[j]) continue;
                    Vector3 d = snapScratch[j].position - snapScratch[i].position;
                    float vert = Mathf.Abs(d.y);
                    d.y = 0f;
                    float mag = d.magnitude;
                    if (mag < 0.4f || mag > 12f) continue;
                    if (vert > mag * 0.55f) continue;
                    float axis = Mathf.Max(
                        Mathf.Abs(Vector3.Dot(d.normalized, right)),
                        Mathf.Abs(Vector3.Dot(d.normalized, forward)));
                    float score = mag * (0.65f + 0.35f * axis);
                    if (score > best)
                    {
                        best = score;
                        bestDelta = d;
                    }
                }
            }
            if (bestDelta.sqrMagnitude < 0.16f) return false;
            length = bestDelta.magnitude;
            dir = bestDelta / length;
            return length > 0.01f;
        }

        private static GameObject ResolvePrefab(Piece piece)
        {
            if (!piece || !ZNetScene.instance) return null;
            var recipe = piece.GetComponent<CreativeRecipe>();
            if (recipe && recipe.Prefab) return recipe.Prefab;
            string name = piece.gameObject.name.Replace("(Clone)", "").Trim();
            return ZNetScene.instance.GetPrefab(name) ?? piece.gameObject;
        }

        [HarmonyPatch(typeof(Player), "TryPlacePiece")]
        private static class TryPlacePatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Player __instance, Piece piece, ref bool __result)
            {
                if (!Enabled || __instance != Player.m_localPlayer || !piece) return true;
                if (holding || armed)
                {
                    __result = false;
                    return false;
                }
                if (!Input.GetMouseButton(0) && !ZInput.GetButton("Attack")) return true;
                if (!IsDragSupported(piece)) return true;
                if (TryArm(__instance, piece))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        private static class GhostHoldPatch
        {
            private static void Postfix(Player __instance)
            {
                if (!holding || __instance != Player.m_localPlayer) return;
                var ghost = Reflection.Get<GameObject>(__instance, "m_placementGhost");
                if (!ghost || positions.Count == 0) return;
                ghost.transform.position = positions[positions.Count - 1];
                ghost.transform.rotation = startRot;
            }
        }
    }
}
