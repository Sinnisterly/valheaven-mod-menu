using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimAdminMenu
{
    /// <summary>
    /// DG_ prefabs placed with the creative hammer. Vanilla only runs DungeonGenerator.Generate from ZoneSystem when a
    /// location spawns, and the location supplies the entrance door and interior lighting. Here: interiors are built
    /// 5000 m up (like vanilla, so InInterior holds) at the edge of the placed zone, facing in so rooms can fill it,
    /// with a portal pair (ground at the aim point, inside the start room) and the matching location's EnvZone.
    /// Camps (CampGrid/CampRadial) build in place. Rooms persist through vanilla's s_roomData; portals through our keys.
    /// </summary>
    internal static class CreativeDungeon
    {
        private const float SkyLift = 5000f, Stack = 160f, EdgeInset = 26f;
        private static readonly int CreativeKey = "VH_dgCreative".GetStableHashCode();
        private static readonly int EntranceKey = "VH_dgEntrance".GetStableHashCode();
        private static readonly int EntranceYawKey = "VH_dgEntranceYaw".GetStableHashCode();
        private static readonly int ExitKey = "VH_dgExit".GetStableHashCode();
        private static readonly int ExitYawKey = "VH_dgExitYaw".GetStableHashCode();
        private static readonly Dictionary<string, Location> interiors = new Dictionary<string, Location>(StringComparer.Ordinal);

        internal static void Build(DungeonGenerator dg, string prefabName, Quaternion rot)
        {
            var view = dg.GetComponent<ZNetView>();
            var zdo = view && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null) { AppLog.Warning("Dungeon", prefabName + ": no ZDO, not generated."); return; }
            Vector3 aim = dg.transform.position;
            float yaw = rot.eulerAngles.y;
            var level = Quaternion.Euler(0f, yaw, 0f);
            // Bounds are the zone cube around transform.y - m_originalPosition.y; vanilla sets it for custom interiors only.
            dg.m_originalPosition = Vector3.zero;
            bool interior = dg.m_algorithm == DungeonGenerator.Algorithm.Dungeon;
            if (interior)
            {
                var zone = ZoneSystem.GetZone(aim);
                Vector3 center = ZoneSystem.GetZonePos(zone);
                // Rooms grow along the generator's forward from its entrance connection: start at the back edge, face in.
                Vector3 sky = center - level * Vector3.forward * EdgeInset;
                sky.y = SkyHeight(dg, zone, aim.y + SkyLift);
                dg.transform.SetPositionAndRotation(sky, level);
                zdo.SetPosition(sky);
                zdo.SetRotation(level);
            }
            else dg.transform.rotation = level;

            int rooms;
            // Vanilla seeds from world seed + position (same spot, same dungeon). Random per placement, and feed it to the
            // rooms' RandomSpawn/loot too: GetSeed() would otherwise replace it with the position seed (m_hasGeneratedSeed).
            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            dg.m_generatedSeed = seed;
            dg.m_addBaseSeedToRandomSpawn = true;
            try { Reflection.Set(dg, "m_hasGeneratedSeed", true); } catch (Exception caught) { AppLog.Swallow(caught); }
            try
            {
                dg.Generate(seed, ZoneSystem.SpawnMode.Full);
                rooms = dg.GetComponentsInChildren<Room>(true).Length;
            }
            catch (Exception ex)
            {
                AppLog.Error("Dungeon", prefabName + " generate failed", ex);
                Plugin.Instance?.Report(prefabName + ": dungeon generation failed. Details in Valheaven.log.");
                return;
            }
            if (!interior)
            {
                AppLog.Info("Dungeon", "Built " + prefabName + " (" + dg.m_algorithm + ") in place: " + rooms + " rooms, seed " + seed + ".");
                return;
            }

            Vector3 exit = ExitPoint(dg, out float exitYaw);
            zdo.Set(CreativeKey, true);
            zdo.Set(EntranceKey, aim);
            zdo.Set(EntranceYawKey, yaw);
            zdo.Set(ExitKey, exit);
            zdo.Set(ExitYawKey, exitYaw);
            Attach(dg, zdo);
            AppLog.Info("Dungeon", "Built " + prefabName + ": " + rooms + " rooms (min " + dg.m_minRooms + ", max " + dg.m_maxRooms + ", seed " + seed + ") at " + dg.transform.position.ToString("F0")
                + ", entrance " + aim.ToString("F1") + ", exit " + exit.ToString("F1") + ".");
            if (rooms <= 1) AppLog.Warning("Dungeon", prefabName + " placed only " + rooms + " room(s); its themes may have no rooms loaded.");
            Plugin.Instance?.Report(prefabName + ": " + rooms + " rooms built. Use the portal to enter.");
        }

        // Below other sky interiors in the same zone (vanilla locations or earlier placements) so rooms never overlap.
        private static float SkyHeight(DungeonGenerator self, Vector2s zone, float y)
        {
            var all = UnityEngine.Object.FindObjectsByType<DungeonGenerator>(FindObjectsSortMode.None);
            for (int tries = 0; tries < 24; tries++)
            {
                bool clash = false;
                foreach (var other in all)
                {
                    if (!other || other == self || other.transform.position.y < 3000f) continue;
                    if (!ZoneSystem.GetZone(other.transform.position).Equals(zone)) continue;
                    if (Mathf.Abs(other.transform.position.y - y) < 120f) { clash = true; break; }
                }
                if (!clash) return y;
                y += Stack;
            }
            return y;
        }

        // ZoneSystem.m_solidRayMask: UpdateTeleport's FindFloor must hit under the arrival point or it bounces you ("Blocked").
        private static int SolidMask => LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");

        /// <summary>
        /// A floor inside the dungeon (a Room's transform is its box centre). Entrance rooms first, then real rooms by
        /// distance from the entrance connection; end caps (wall plugs, size 0 on an axis) never: their "centre" is the
        /// wall, so the exit landed outside it. A point counts only with floor below and ceiling above (enclosed); an
        /// open room is the fallback. Not place order: rooms respawned from ZDO all read order 0.
        /// </summary>
        private static Vector3 ExitPoint(DungeonGenerator dg, out float yaw)
        {
            Vector3 origin = dg.transform.position;
            var rooms = new List<Room>(dg.GetComponentsInChildren<Room>(true));
            rooms.RemoveAll(r => !r || r.m_endCap || r.m_size.x == 0 || r.m_size.z == 0);
            rooms.Sort((a, b) =>
            {
                if (a.m_entrance != b.m_entrance) return a.m_entrance ? -1 : 1;
                return (a.transform.position - origin).sqrMagnitude.CompareTo((b.transform.position - origin).sqrMagnitude);
            });
            Physics.SyncTransforms();
            Vector3 open = default;
            Room openRoom = null;
            foreach (var room in rooms)
            {
                Vector3 c = room.transform.position;
                float half = Mathf.Max(1f, room.m_size.y * .5f);
                if (!Physics.Raycast(c + Vector3.up * .5f, Vector3.down, out var floor, half + 3f, SolidMask, QueryTriggerInteraction.Ignore))
                {
                    AppLog.Debug("Dungeon", "No floor under " + room.name + " centre " + c.ToString("F1"));
                    continue;
                }
                // Headroom for the player, and a roof within the room's height (outside a wall there is none).
                bool roof = Physics.Raycast(floor.point + Vector3.up * .3f, Vector3.up, out var ceiling, room.m_size.y + 4f, SolidMask, QueryTriggerInteraction.Ignore)
                    && ceiling.distance > 1.6f;
                if (!roof)
                {
                    if (!openRoom) { openRoom = room; open = floor.point; }
                    AppLog.Debug("Dungeon", "No ceiling over " + room.name + " floor " + floor.point.ToString("F1"));
                    continue;
                }
                yaw = YawFrom(origin, c, dg);
                AppLog.Debug("Dungeon", "Exit in " + room.name + (room.m_entrance ? " (entrance)" : "") + " size " + room.m_size + ", floor " + floor.point.ToString("F1") + " on " + floor.collider.name);
                return floor.point;
            }
            if (openRoom)
            {
                yaw = YawFrom(origin, openRoom.transform.position, dg);
                AppLog.Info("Dungeon", "Exit in open room " + openRoom.name + " (no ceiling found) at " + open.ToString("F1"));
                return open;
            }
            yaw = dg.transform.eulerAngles.y;
            AppLog.Warning("Dungeon", "No floor found in any of " + rooms.Count + " rooms of " + dg.name + "; the exit stays at the entrance connection.");
            return origin;
        }

        private static float YawFrom(Vector3 origin, Vector3 roomCentre, DungeonGenerator dg)
        {
            var dir = roomCentre - origin;
            dir.y = 0f;
            return dir.sqrMagnitude > .01f ? Quaternion.LookRotation(dir.normalized).eulerAngles.y : dg.transform.eulerAngles.y;
        }

        /// <summary>Before entering: if nothing is under the exit arrival point (older placement, rooms moved), recompute it.</summary>
        internal static void EnsureExit(Teleport entrance)
        {
            var exit = entrance.m_targetPoint;
            var dg = entrance.GetComponentInParent<DungeonGenerator>();
            if (!exit || !dg || ZoneSystem.instance == null) return;
            Vector3 arrival = exit.transform.position + exit.transform.forward - exit.transform.up;
            if (ZoneSystem.instance.FindFloor(arrival, out _)) return;
            if (dg.GetComponentsInChildren<Room>(true).Length == 0)
            {
                AppLog.Warning("Dungeon", dg.name + " rooms are not loaded yet; entering would bounce.");
                return;
            }
            Vector3 floor = ExitPoint(dg, out float yaw);
            Place(exit.transform, floor, yaw);
            if (IsCreative(dg, out var zdo) && zdo.IsOwner()) { zdo.Set(ExitKey, floor); zdo.Set(ExitYawKey, yaw); }
            AppLog.Info("Dungeon", "Moved " + dg.name + " exit from " + arrival.ToString("F1") + " (no floor) to " + floor.ToString("F1") + ".");
        }

        /// <summary>Portals + interior lighting. After Generate (it clears children) and from DungeonGenerator.Awake on reload.</summary>
        internal static void Attach(DungeonGenerator dg, ZDO zdo)
        {
            if (!dg || zdo == null || !zdo.GetBool(CreativeKey) || dg.transform.Find("VHDungeonEntrance")) return;
            try
            {
                string title = DisplayName(dg);
                var entrance = Portal(dg.transform, "VHDungeonEntrance", zdo.GetVec3(EntranceKey, dg.transform.position), zdo.GetFloat(EntranceYawKey), "$location_enter " + title, title);
                var exit = Portal(dg.transform, "VHDungeonExit", zdo.GetVec3(ExitKey, dg.transform.position), zdo.GetFloat(ExitYawKey), "$location_exit", title + "\n<size=60%>$location_exit</size>");
                entrance.m_targetPoint = exit;
                exit.m_targetPoint = entrance;
                Lighting(dg);
            }
            catch (Exception ex) { AppLog.Error("Dungeon", "Portals for " + dg.name + " failed", ex); }
        }

        private static string DisplayName(DungeonGenerator dg)
        {
            string name = Utils.GetPrefabName(dg.gameObject);
            var prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(name) : null;
            string friendly = prefab ? CreativeHammer.FriendlyName(prefab) : name;
            if (Localization.instance != null) friendly = Localization.instance.Localize(friendly);
            if (friendly.StartsWith("DG ", StringComparison.OrdinalIgnoreCase) || friendly.StartsWith("DG_", StringComparison.OrdinalIgnoreCase))
                friendly = friendly.Substring(3);
            return friendly.Replace('_', ' ').Trim();
        }

        // Teleport sends you to target + forward - up. Root sits 1 m up and 1 m behind the arrival floor point, so you land
        // on `floor` facing `yaw`, with the portal just behind you.
        private static Teleport Portal(Transform parent, string name, Vector3 floor, float yaw, string hover, string label)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Place(go.transform, floor, yaw);
            var visual = PortalVisual(go.transform);
            visual.transform.localPosition = new Vector3(0f, -1f, 0f);
            visual.transform.localRotation = Quaternion.identity;
            Label(go.transform, label);
            var tp = go.AddComponent<Teleport>();
            tp.m_hoverText = hover;
            return tp;
        }

        private static void Place(Transform portal, Vector3 floor, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            portal.SetPositionAndRotation(floor + Vector3.up - rot * Vector3.forward, rot);
        }

        // Floating name over the portal (portal_wood is ~3.5 m tall), turned to the camera.
        private static void Label(Transform portal, string text)
        {
            try
            {
                var go = new GameObject("VHDungeonLabel");
                go.transform.SetParent(portal, false);
                go.transform.localPosition = new Vector3(0f, 3.4f, 0f);
                var tmp = go.AddComponent<TMPro.TextMeshPro>();
                var font = MenuFonts.Resolve("en").Display;
                if (font) tmp.font = font;
                tmp.text = Localization.instance != null ? Localization.instance.Localize(text) : text;
                tmp.fontSize = 6f;
                tmp.alignment = TMPro.TextAlignmentOptions.Center;
                tmp.enableWordWrapping = false;
                tmp.color = new Color(1f, .86f, .45f);
                tmp.rectTransform.sizeDelta = new Vector2(10f, 2f);
                go.AddComponent<DungeonLabel>();
            }
            catch (Exception caught) { AppLog.Swallow(caught); }
        }

        private static GameObject PortalVisual(Transform parent)
        {
            var source = ZNetScene.instance ? ZNetScene.instance.GetPrefab("portal_wood") : null;
            if (source)
            {
                bool prior = ZNetView.m_forceDisableInit;
                ZNetView.m_forceDisableInit = true;
                var holder = new GameObject("VHDungeonHolder");
                holder.SetActive(false);
                try
                {
                    var vis = UnityEngine.Object.Instantiate(source, holder.transform, false);
                    if (CreativeHammer.StripToVisual(vis))
                    {
                        // No Piece of its own: hammer-removing the portal removes the dungeon (the DG's Piece).
                        foreach (var p in vis.GetComponentsInChildren<Piece>(true)) if (p) UnityEngine.Object.DestroyImmediate(p);
                        foreach (var c in vis.GetComponentsInChildren<Collider>(true)) if (c && c.isTrigger) UnityEngine.Object.DestroyImmediate(c);
                        vis.name = "VHDungeonPortalVisual";
                        vis.transform.SetParent(parent, false);
                        return vis;
                    }
                    UnityEngine.Object.DestroyImmediate(vis);
                }
                catch (Exception caught) { AppLog.Swallow(caught); }
                finally
                {
                    ZNetView.m_forceDisableInit = prior;
                    UnityEngine.Object.Destroy(holder);
                }
            }
            if (AppLog.Once("dungeon.portal.fallback")) AppLog.Warning("Dungeon", "portal_wood visual unavailable; using a plain frame.");
            var frame = new GameObject("VHDungeonPortalVisual");
            frame.transform.SetParent(parent, false);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(frame.transform, false);
            cube.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            cube.transform.localScale = new Vector3(2f, 3f, .3f);
            return frame;
        }

        // The location's interior EnvZone (dark crypt/cave weather and lighting) over the zone, like Location.Awake.
        private static void Lighting(DungeonGenerator dg)
        {
            var loc = InteriorFor(Utils.GetPrefabName(dg.gameObject), dg);
            if (!loc || !loc.m_interiorPrefab) return;
            Vector3 center = ZoneSystem.GetZonePos(ZoneSystem.GetZone(dg.transform.position));
            var env = UnityEngine.Object.Instantiate(loc.m_interiorPrefab, new Vector3(center.x, dg.transform.position.y, center.z), Quaternion.identity, dg.transform);
            env.name = "VHDungeonEnv";
            env.transform.localScale = new Vector3(64f, 150f, 64f);
            var zone = env.GetComponent<EnvZone>();
            if (zone) zone.m_environment = loc.m_interiorEnvironment;
        }

        /// <summary>Location prefab whose generator is this DG (else same themes). Loaded once per name; a match stays held.</summary>
        private static Location InteriorFor(string dgName, DungeonGenerator dg)
        {
            if (interiors.TryGetValue(dgName, out var known)) return known;
            Location exact = null, similar = null;
            var held = new List<SoftReferenceableAssets.SoftReference<GameObject>>();
            int checkedCount = 0;
            var zs = ZoneSystem.instance;
            if (zs != null && zs.m_locations != null)
            {
                foreach (var zl in zs.m_locations)
                {
                    if (exact) break;
                    if (zl == null || zl.m_interiorRadius <= 0f || !zl.m_prefab.IsValid) continue;
                    var prefab = zl.m_prefab;
                    try
                    {
                        prefab.Load();
                        checkedCount++;
                        var loc = prefab.Asset ? prefab.Asset.GetComponent<Location>() : null;
                        // m_generator is only set on custom-interior locations (Hildir, Dvergr); older crypts just nest it.
                        var gen = loc ? (loc.m_generator ? loc.m_generator : loc.GetComponentInChildren<DungeonGenerator>(true)) : null;
                        if (gen && loc.m_interiorPrefab && Utils.GetPrefabName(gen.gameObject) == dgName) { exact = loc; held.Add(prefab); continue; }
                        if (gen && loc.m_interiorPrefab && !similar && gen.m_themes == dg.m_themes) { similar = loc; held.Add(prefab); continue; }
                        prefab.Release();
                    }
                    catch (Exception caught) { AppLog.Swallow(caught); }
                }
            }
            var found = exact ? exact : similar;
            // Keep only the chosen prefab loaded.
            foreach (var h in held)
                if (!found || h.Asset != found.gameObject) { try { h.Release(); } catch (Exception caught) { AppLog.Swallow(caught); } }
            interiors[dgName] = found;
            AppLog.Info("Dungeon", dgName + " lighting: " + (found ? Utils.GetPrefabName(found.gameObject) + " (" + found.m_interiorEnvironment + (exact ? "" : ", same themes") + ")" : "none found")
                + ", checked " + checkedCount + " interior locations.");
            return found;
        }

        internal static bool IsCreative(DungeonGenerator dg, out ZDO zdo)
        {
            zdo = null;
            var view = dg ? dg.GetComponent<ZNetView>() : null;
            zdo = view && view.IsValid() ? view.GetZDO() : null;
            return zdo != null && zdo.GetBool(CreativeKey);
        }

        /// <summary>Removing a creative interior also removes what it spawned in its sky zone (chests, spawners, mobs).</summary>
        internal static void RemoveContents(DungeonGenerator dg)
        {
            Vector3 pos = dg.transform.position;
            if (pos.y < 3000f) return;
            Vector3 center = ZoneSystem.GetZonePos(ZoneSystem.GetZone(pos));
            var bounds = new Bounds(new Vector3(center.x, pos.y, center.z), new Vector3(64f, 80f, 64f));
            var own = dg.GetComponent<ZNetView>();
            var doomed = new List<ZNetView>();
            foreach (var view in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if (!view || view == own || !view.IsValid() || !bounds.Contains(view.transform.position)) continue;
                if (view.GetComponent<Player>() || view.GetComponent<TombStone>() || view.GetComponent<DungeonGenerator>()) continue;
                doomed.Add(view);
            }
            foreach (var view in doomed)
            {
                try { if (view && view.IsValid()) { view.ClaimOwnership(); ZNetScene.instance.Destroy(view.gameObject); } }
                catch (Exception caught) { AppLog.Swallow(caught); }
            }
            AppLog.Info("Dungeon", "Removed " + Utils.GetPrefabName(dg.gameObject) + " and " + doomed.Count + " objects it spawned.");
        }
    }

    internal sealed class DungeonLabel : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = GameCamera.instance ? GameCamera.instance.transform : null;
            if (cam) transform.rotation = Quaternion.LookRotation(transform.position - cam.position, Vector3.up);
        }
    }

    [HarmonyPatch(typeof(Teleport), nameof(Teleport.Interact))]
    internal static class CreativeDungeonEnterPatch
    {
        private static void Prefix(Teleport __instance, bool hold)
        {
            if (hold || !__instance || __instance.gameObject.name != "VHDungeonEntrance") return;
            try { CreativeDungeon.EnsureExit(__instance); }
            catch (Exception caught) { AppLog.Swallow(caught); }
        }
    }

    [HarmonyPatch(typeof(DungeonGenerator), "Awake")]
    internal static class CreativeDungeonLoadPatch
    {
        private static void Postfix(DungeonGenerator __instance)
        {
            try { if (CreativeDungeon.IsCreative(__instance, out var zdo)) CreativeDungeon.Attach(__instance, zdo); }
            catch (Exception caught) { AppLog.Swallow(caught); }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy))]
    internal static class CreativeDungeonRemovePatch
    {
        private static void Prefix(GameObject go)
        {
            try
            {
                var dg = go ? go.GetComponent<DungeonGenerator>() : null;
                if (dg && CreativeDungeon.IsCreative(dg, out _)) CreativeDungeon.RemoveContents(dg);
            }
            catch (Exception caught) { AppLog.Swallow(caught); }
        }
    }
}
