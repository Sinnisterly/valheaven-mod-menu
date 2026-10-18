using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimAdminMenu
{
    internal static class PlacementSnap
    {
        internal static bool UniversalSnap = true;
        private const float SearchRadius = 4.5f;
        private const float MaxPairDistance = 1.75f;
        private static readonly List<Piece> nearby = new List<Piece>();
        private static readonly List<Transform> ghostSnaps = new List<Transform>();
        private static readonly List<Transform> worldSnaps = new List<Transform>();
        private static readonly Collider[] overlap = new Collider[64];

        /// <summary>Snap ghost snap-points onto nearby world pieces. Safe to call while UniversalSnap is off (drag start).</summary>
        internal static void AlignGhost(GameObject ghost)
        {
            if (!ghost || !ghost.activeInHierarchy) return;
            if (DragBuilding.Active) return;
            var piece = ghost.GetComponent<Piece>();
            if (!piece) return;

            ghostSnaps.Clear();
            try { piece.GetSnapPoints(ghostSnaps); } catch { return; }
            if (ghostSnaps.Count == 0) return;

            CollectNearbyPieces(ghost.transform.position, SearchRadius);
            if (nearby.Count == 0) return;

            float best = MaxPairDistance * MaxPairDistance;
            Vector3 bestDelta = Vector3.zero;
            bool found = false;

            for (int g = 0; g < ghostSnaps.Count; g++)
            {
                var gs = ghostSnaps[g];
                if (!gs) continue;
                Vector3 gp = gs.position;
                for (int i = 0; i < nearby.Count; i++)
                {
                    var other = nearby[i];
                    if (!other || other == piece) continue;
                    if (other.transform.IsChildOf(ghost.transform)) continue;
                    worldSnaps.Clear();
                    try { other.GetSnapPoints(worldSnaps); } catch { continue; }
                    for (int w = 0; w < worldSnaps.Count; w++)
                    {
                        var ws = worldSnaps[w];
                        if (!ws) continue;
                        float sqr = (ws.position - gp).sqrMagnitude;
                        if (sqr >= best) continue;
                        best = sqr;
                        bestDelta = ws.position - gp;
                        found = true;
                    }
                }
            }

            if (found && bestDelta.sqrMagnitude > 0.000001f)
                ghost.transform.position += bestDelta;
        }

        private static void CollectNearbyPieces(Vector3 center, float radius)
        {
            nearby.Clear();
            int mask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small");
            int n = Physics.OverlapSphereNonAlloc(center, radius, overlap, mask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var col = overlap[i];
                if (!col) continue;
                var piece = col.GetComponentInParent<Piece>();
                if (!piece || piece.GetComponent<ItemDrop>() || piece.GetComponent<Character>()) continue;
                if (piece.gameObject && piece.gameObject.name.IndexOf("ValheavenDragGhost", StringComparison.Ordinal) >= 0) continue;
                if (!nearby.Contains(piece)) nearby.Add(piece);
            }
        }

        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        private static class GhostSnapPatch
        {
            private static void Postfix(Player __instance)
            {
                if (!UniversalSnap || __instance != Player.m_localPlayer) return;
                if (DragBuilding.Active) return;
                if (FreeBuildRotation.Enabled) return;
                var ghost = Reflection.Get<GameObject>(__instance, "m_placementGhost");
                if (ghost && ghost.activeInHierarchy) AlignGhost(ghost);
            }
        }
    }
}
