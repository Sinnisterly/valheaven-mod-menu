using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ValheimAdminMenu
{
    /// <summary>
    /// Hold-place on walls/floors/beams/roofs only. The click always places through vanilla; holding past a short delay
    /// (or aiming along the row, or up/down for walls and pillars) stretches a snap-stepped line from that first piece.
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
        // Arm/hold older than this with Attack released means Tick stalled; never let it block vanilla placing.
        private const float StaleAfter = 1f;

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
        private static float lastTickAt;
        private static Vector3 startPos;
        private static Quaternion startRot;
        private static Vector3 stepDir;
        private static float stepLen;
        private static int lastCount = -1;
        private static int lastSign = 1;
        // Up to two line axes: snap-stepped horizontal and (walls/pillars) vertical. Index 0 = horizontal.
        private static readonly Vector3[] axisDir = new Vector3[2];
        private static readonly float[] axisLen = new float[2];
        private static readonly bool[] axisOn = new bool[2];
        private static int axis = -1;
        private static int lastAxis = -1;

        internal static void Tick(Player player)
        {
            lastTickAt = Time.unscaledTime;
            if (!player || player != Player.m_localPlayer)
            {
                Cancel("player gone");
                return;
            }
            if (player.IsTeleporting() || player.IsDead())
            {
                Cancel("teleport/death");
                return;
            }
            if (!Enabled)
            {
                Cancel("disabled");
                return;
            }
            if (Plugin.Instance != null && Plugin.Instance.Open)
            {
                Cancel("menu open");
                return;
            }
            if (Release250.PastePreviewActive) { Cancel("paste preview"); return; }
            if (!player.InPlaceMode() || !Release250.IsBuildHammer(player)) { Cancel("left build mode"); return; }

            bool attackUp = Input.GetMouseButtonUp(0) || ZInput.GetButtonUp("Attack");
            bool attackHeld = Input.GetMouseButton(0) || ZInput.GetButton("Attack");

            if (armed && !holding)
            {
                // Quick click: vanilla already placed the piece; nothing more to do.
                if (attackUp || !attackHeld)
                {
                    Cancel();
                    return;
                }
                float held = Time.unscaledTime - armedAt;
                bool moved = false;
                for (int a = 0; a < axisOn.Length; a++)
                    if (axisOn[a] && Mathf.Abs(AxisParam(player, a)) >= axisLen[a] * MoveStartFrac) moved = true;
                if (held >= HoldDelay || moved)
                    BeginFromArm();
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
                Cancel("right-click/Esc");
                return;
            }
            if (!attackHeld)
            {
                Commit(player);
                return;
            }
            UpdateDrag(player);
        }

        /// <summary>After vanilla placed <paramref name="piece"/>: remember its pose so a held click can extend a line.</summary>
        private static void TryArm(Player player, Piece piece)
        {
            if (!Enabled || !player || !piece || holding || armed) return;
            if (Release250.PastePreviewActive) return;
            if (!Release250.IsBuildHammer(player)) return;
            if (!IsDragSupported(piece)) return;
            var ghost = Reflection.Get<GameObject>(player, "m_placementGhost");
            if (!ghost || !ghost.activeInHierarchy) return;
            if (!TryBuildAxes(ghost)) return;
            var prefab = ResolvePrefab(piece);
            if (!prefab) return;

            dragPiece = piece;
            dragPrefab = prefab;
            startPos = ghost.transform.position;
            startRot = ghost.transform.rotation;
            armed = true;
            armedAt = Time.unscaledTime;
            lastCount = -1;
            lastSign = 1;
            axis = axisOn[0] ? 0 : 1;
            lastAxis = -1;
            stepDir = axisDir[axis];
            stepLen = axisLen[axis];
        }

        private static void BeginFromArm()
        {
            armed = false;
            if (!dragPrefab || !dragPiece) { Cancel(); return; }
            holding = true;
            Active = true;
            AppLog.Debug("Drag", "Drag start: " + dragPrefab.name + " at " + startPos.ToString("F1"));
            RebuildLine(1, 1);
        }

        private static void UpdateDrag(Player player)
        {
            if (!dragPiece || axis < 0) { Cancel(); return; }

            // Pick the axis the aim actually runs along: whichever line would get more pieces. Sticky so it doesn't flicker.
            var counts = new int[2];
            var signs = new int[2];
            for (int a = 0; a < axisOn.Length; a++)
            {
                if (!axisOn[a]) continue;
                float t = AxisParam(player, a);
                signs[a] = t >= 0f ? 1 : -1;
                counts[a] = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(t) / axisLen[a]) + 1, 1, MaxPieces);
            }
            int other = 1 - axis;
            if (axisOn[other] && (counts[other] >= counts[axis] + 2 || (counts[axis] <= 1 && counts[other] > 1)))
                axis = other;
            stepDir = axisDir[axis];
            stepLen = axisLen[axis];
            int count = counts[axis];
            int sign = signs[axis];
            if (count == lastCount && sign == lastSign && axis == lastAxis) return;
            lastAxis = axis;
            RebuildLine(count, sign);
        }

        /// <summary>
        /// Signed distance along axis <paramref name="a"/> from the start piece to where the camera ray passes closest.
        /// Works when aiming at open sky (vertical); falls back to the ground hit when the ray runs parallel to the axis.
        /// </summary>
        private static float AxisParam(Player player, int a)
        {
            Vector3 u = axisDir[a];
            var cam = GameCamera.instance ? GameCamera.instance.transform : Camera.main?.transform;
            if (!cam) return 0f;
            Vector3 o = cam.position, r = cam.forward;
            Vector3 w0 = o - startPos;
            float b = Vector3.Dot(r, u), d = Vector3.Dot(r, w0), e = Vector3.Dot(u, w0);
            float denom = 1f - b * b;
            float t;
            if (denom < 0.02f)
                t = Vector3.Dot(AimPoint(player, startPos) - startPos, u);
            else
            {
                float s = (b * e - d) / denom;
                // Closest point behind the camera means the ray leaves the line; use the camera's own projection.
                t = s < 0f ? e : (e - b * d) / denom;
            }
            float limit = axisLen[a] * MaxPieces;
            return Mathf.Clamp(t, -limit, limit);
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

        /// <summary>Index 0 is the piece vanilla already placed; ghosts and commits cover 1..count-1.</summary>
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
                if (i > 0) SpawnGhost(pos, startRot);
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
            var piece = dragPiece;
            int total = positions.Count;
            int placed = 0;
            string stopped = null;
            for (int i = 1; i < total && piece; i++)
            {
                stopped = PlaceLinePiece(player, piece, positions[i], rotations[i]);
                if (stopped != null) break;
                placed++;
            }
            AppLog.Debug("Drag", "Commit " + (dragPrefab ? dragPrefab.name : "?") + ": placed " + (placed + 1) + "/" + Math.Max(1, total) + (stopped != null ? ", stopped: " + stopped : ""));
            Cancel();
            if (!Plugin.Instance) return;
            string msg = "Drag placed " + (placed + 1) + " pieces.";
            if (stopped != null) msg += " Stopped: " + stopped;
            Plugin.Instance.Report(msg);
        }

        /// <summary>Same rules as a vanilla click (ward, cost); null on success, else the reason it stopped.</summary>
        private static string PlaceLinePiece(Player player, Piece piece, Vector3 pos, Quaternion rot)
        {
            try
            {
                if (!PrivateArea.CheckAccess(pos, 0f, false, false)) return "ward";
                bool noCost = player.NoCostCheat();
                if (!noCost && !player.HaveRequirements(piece, Player.RequirementMode.CanBuild)) return "missing materials";
                player.PlacePiece(piece, pos, rot, false);
                if (!noCost && ZoneSystem.instance && !ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey()))
                    player.ConsumeResources(piece.m_resources, 0);
                return null;
            }
            catch (Exception ex)
            {
                AppLog.Error("Drag", "Drag place failed", ex);
                return ex.Message;
            }
        }

        internal static void Cancel() => Cancel(null);
        /// <summary><paramref name="why"/> is logged at Debug only when a hold/arm was live, so per-frame callers stay quiet.</summary>
        private static void Cancel(string why)
        {
            if (why != null && (holding || armed)) AppLog.Debug("Drag", "Drag cancelled: " + why + (holding ? " (holding " + positions.Count + " pieces)" : " (armed)"));
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
            axis = -1;
            lastAxis = -1;
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
            try { piece.GetSnapPoints(snapScratch); } catch (Exception caught) { AppLog.Swallow(caught); return false; }
            return snapScratch.Count >= 2;
        }

        /// <summary>Horizontal step from the longest flat snap pair; vertical step from a stacked pair (wall/pillar edge).</summary>
        private static bool TryBuildAxes(GameObject ghost)
        {
            axisOn[0] = TryBuildStep(ghost, out axisDir[0], out axisLen[0]);
            axisOn[1] = TryVerticalStep(ghost, out axisLen[1]);
            axisDir[1] = Vector3.up;
            return axisOn[0] || axisOn[1];
        }

        private static bool TryVerticalStep(GameObject ghost, out float length)
        {
            length = 0f;
            var piece = ghost ? ghost.GetComponent<Piece>() : null;
            if (!piece) return false;
            snapScratch.Clear();
            try { piece.GetSnapPoints(snapScratch); } catch (Exception caught) { AppLog.Swallow(caught); return false; }
            for (int i = 0; i < snapScratch.Count; i++)
            {
                if (!snapScratch[i]) continue;
                for (int j = i + 1; j < snapScratch.Count; j++)
                {
                    if (!snapScratch[j]) continue;
                    Vector3 d = snapScratch[j].position - snapScratch[i].position;
                    float vert = Mathf.Abs(d.y);
                    d.y = 0f;
                    // Straight up an edge only; sloped pairs (roofs) would stack crooked.
                    if (vert < 0.4f || vert > 12f || d.magnitude > vert * 0.35f) continue;
                    if (vert > length) length = vert;
                }
            }
            return length > 0.01f;
        }

        private static bool TryBuildStep(GameObject ghost, out Vector3 dir, out float length)
        {
            dir = Vector3.right;
            length = 0f;
            if (!ghost) return false;
            var piece = ghost.GetComponent<Piece>();
            if (!piece) return false;
            snapScratch.Clear();
            try { piece.GetSnapPoints(snapScratch); } catch (Exception caught) { AppLog.Swallow(caught); return false; }
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
            // A row of world locations would stack their interiors; one at a time.
            if (recipe && !string.IsNullOrEmpty(recipe.LocationName)) return null;
            if (recipe && recipe.Prefab) return recipe.Prefab;
            string name = piece.gameObject.name.Replace("(Clone)", "").Trim();
            return ZNetScene.instance.GetPrefab(name) ?? piece.gameObject;
        }

        private static bool Stale()
        {
            if (!armed && !holding) return false;
            bool attackHeld = Input.GetMouseButton(0) || ZInput.GetButton("Attack");
            // Tick stopped running (Release250.Tick threw) or the button is long released.
            if (Time.unscaledTime - lastTickAt > StaleAfter) return true;
            return !attackHeld && Time.unscaledTime - armedAt > StaleAfter;
        }

        [HarmonyPatch(typeof(Player), "TryPlacePiece")]
        private static class TryPlacePatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Player __instance, ref bool __result)
            {
                if (__instance != Player.m_localPlayer) return true;
                if (Stale()) Cancel();
                if (!Enabled) return true;
                // While a line is stretched, the release commits it; don't let vanilla drop extra pieces.
                if (holding)
                {
                    __result = false;
                    return false;
                }
                // A fresh press replaces any pending arm.
                if (armed) Cancel();
                return true;
            }

            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Player __instance, Piece piece, bool __result)
            {
                if (!__result || !Enabled || __instance != Player.m_localPlayer || !piece) return;
                if (!Input.GetMouseButton(0) && !ZInput.GetButton("Attack")) return;
                TryArm(__instance, piece);
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
