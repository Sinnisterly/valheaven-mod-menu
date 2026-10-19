using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimAdminMenu
{
    /// <summary>
    /// World locations placed with the creative hammer (troll caves, burial chambers, frost caves, ...), through vanilla's
    /// own ZoneSystem.SpawnLocation (what the dev "location" command uses): exterior, interior 5000 m up with its real
    /// doors, generated dungeon, loot and spawners, and a LocationProxy that respawns the exterior after reloads.
    /// The proxy is creator-stamped and lists every network object the spawn made, so removing it removes them too.
    /// </summary>
    internal static class CreativeLocations
    {
        private static readonly int CreativeKey = "VH_locCreative".GetStableHashCode();
        private static readonly int SpawnedKey = "VH_locSpawned".GetStableHashCode();
        private static MethodInfo spawn;
        private static Material footprint;

        internal static string DisplayName(string name)
        {
            string friendly =
                name.StartsWith("TrollCave", StringComparison.Ordinal) ? "Troll cave"
                : name.StartsWith("SunkenCrypt", StringComparison.Ordinal) ? "Sunken crypt"
                : name.StartsWith("Crypt", StringComparison.Ordinal) ? "Burial chambers"
                : name.StartsWith("MountainCave", StringComparison.Ordinal) ? "Frost cave"
                : name.StartsWith("Mistlands_DvergrTownEntrance", StringComparison.Ordinal) ? "Infested mine"
                : NaturalCatalogRules.FriendlyPrefabName(name);
            return string.Equals(friendly, name, StringComparison.OrdinalIgnoreCase) ? name : friendly + " (" + name + ")";
        }

        /// <summary>Flat translucent disc of the location's radius: the ghost shows how much ground it takes.</summary>
        internal static void AddFootprint(GameObject proxy, float radius)
        {
            float r = Mathf.Clamp(radius, 3f, 40f);
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "VHLocationFootprint";
            var col = disc.GetComponent<Collider>();
            if (col) UnityEngine.Object.DestroyImmediate(col);
            disc.transform.SetParent(proxy.transform, false);
            disc.transform.localPosition = new Vector3(0f, .05f, 0f);
            disc.transform.localScale = new Vector3(r * 2f, .02f, r * 2f);
            var mr = disc.GetComponent<MeshRenderer>();
            if (!mr) return;
            if (!footprint)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                if (shader) footprint = new Material(shader) { color = new Color(.95f, .75f, .3f, .22f) };
            }
            if (footprint) mr.sharedMaterial = footprint;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        internal static void Place(Player player, string name, Vector3 pos, Quaternion rot)
        {
            var zs = ZoneSystem.instance;
            if (zs == null) throw new InvalidOperationException("World is not ready.");
            var zl = zs.m_locations.FirstOrDefault(l => l != null && l.m_prefab.IsValid && l.m_prefab.Name == name);
            if (zl == null) throw new InvalidOperationException("Unknown location " + name + ".");
            spawn = spawn ?? AccessTools.Method(typeof(ZoneSystem), "SpawnLocation", new[] { typeof(ZoneSystem.ZoneLocation), typeof(int),
                typeof(Vector3), typeof(Quaternion), typeof(ZoneSystem.SpawnMode), typeof(List<GameObject>), typeof(bool) });
            if (spawn == null) throw new InvalidOperationException("ZoneSystem.SpawnLocation is missing in this game version.");

            var level = Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
            Vector3 aim = pos;
            if (zl.m_interiorRadius > 0f)
            {
                // Like the dev command: the interior is built over this zone, so the location must fit inside it.
                float r = Mathf.Max(zl.m_exteriorRadius, zl.m_interiorRadius);
                Vector3 zc = ZoneSystem.GetZonePos(ZoneSystem.GetZone(pos));
                pos.x = r >= 32f ? zc.x : Mathf.Clamp(pos.x, zc.x - 32f + r, zc.x + 32f - r);
                pos.z = r >= 32f ? zc.z : Mathf.Clamp(pos.z, zc.z - 32f + r, zc.z + 32f - r);
                if ((pos - aim).sqrMagnitude > .01f) pos.y = zs.GetGroundHeight(pos);
                if (InteriorTaken(zc)) AppLog.Warning("Location", "This zone already has an interior up top; " + name + "'s may overlap it.");
            }

            var before = new HashSet<int>();
            foreach (var v in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None)) if (v) before.Add(v.GetInstanceID());
            int seed = UnityEngine.Random.Range(0, int.MaxValue);
            spawn.Invoke(zs, new object[] { zl, seed, pos, level, ZoneSystem.SpawnMode.Full, new List<GameObject>(), false });

            ZNetView proxy = null;
            var made = new List<ZDOID>();
            foreach (var v in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if (!v || before.Contains(v.GetInstanceID()) || !v.IsValid()) continue;
                if (!proxy && v.GetComponent<LocationProxy>()) { proxy = v; continue; }
                made.Add(v.GetZDO().m_uid);
            }
            if (!proxy)
            {
                AppLog.Warning("Location", "Placed " + name + " but found no LocationProxy; it will not reload or remove as one piece.");
                return;
            }
            var zdo = proxy.GetZDO();
            zdo.Set(CreativeKey, true);
            zdo.Set(ZDOVars.s_creator, player.GetPlayerID());
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(made.Count);
                foreach (var id in made) { w.Write(id.UserID); w.Write(id.ID); }
                zdo.Set(SpawnedKey, ms.ToArray());
            }
            float moved = Vector3.Distance(new Vector3(aim.x, 0f, aim.z), new Vector3(pos.x, 0f, pos.z));
            AppLog.Info("Location", "Placed " + name + " at " + pos.ToString("F1") + (moved > .1f ? " (moved " + moved.ToString("F0") + " m to fit its zone)" : "")
                + ", seed " + seed + ", " + made.Count + " network objects" + (zl.m_interiorRadius > 0f ? ", with interior" : "") + ".");
            Plugin.Instance?.Report("Placed " + DisplayName(name) + (moved > .1f ? ", moved " + moved.ToString("F0") + " m to fit its zone." : "."));
        }

        private static bool InteriorTaken(Vector3 zoneCenter)
        {
            var zone = ZoneSystem.GetZone(zoneCenter);
            foreach (var loc in UnityEngine.Object.FindObjectsByType<Location>(FindObjectsSortMode.None))
                if (loc && loc.m_hasInterior && ZoneSystem.GetZone(loc.transform.position).Equals(zone)) return true;
            foreach (var dg in UnityEngine.Object.FindObjectsByType<DungeonGenerator>(FindObjectsSortMode.None))
                if (dg && dg.transform.position.y > 3000f && ZoneSystem.GetZone(dg.transform.position).Equals(zone)) return true;
            return false;
        }

        internal static bool IsCreative(GameObject go, out ZDO zdo)
        {
            zdo = null;
            if (!go || !go.GetComponent<LocationProxy>()) return false;
            var view = go.GetComponent<ZNetView>();
            zdo = view && view.IsValid() ? view.GetZDO() : null;
            return zdo != null && zdo.GetBool(CreativeKey);
        }

        /// <summary>Everything the placement spawned (exterior pieces, interior, dungeon contents), loaded or not.</summary>
        internal static void RemoveSpawned(ZDO proxy)
        {
            if (!proxy.GetByteArray(SpawnedKey, out var data) || data == null) return;
            int removed = 0;
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    var id = new ZDOID(r.ReadInt64(), r.ReadUInt32());
                    try
                    {
                        var zdo = ZDOMan.instance.GetZDO(id);
                        if (zdo == null) continue;
                        var inst = ZNetScene.instance.FindInstance(zdo);
                        if (inst)
                        {
                            if (inst.GetComponent<Player>() || inst.GetComponent<TombStone>()) continue;
                            inst.ClaimOwnership();
                            ZNetScene.instance.Destroy(inst.gameObject);
                        }
                        else
                        {
                            zdo.SetOwner(ZDOMan.GetSessionID());
                            ZDOMan.instance.DestroyZDO(zdo);
                        }
                        removed++;
                    }
                    catch (Exception caught) { AppLog.Swallow(caught); }
                }
            }
            AppLog.Info("Location", "Removed a placed location and " + removed + " objects it spawned.");
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy))]
    internal static class CreativeLocationRemovePatch
    {
        private static void Prefix(GameObject go)
        {
            try { if (CreativeLocations.IsCreative(go, out var zdo)) CreativeLocations.RemoveSpawned(zdo); }
            catch (Exception caught) { AppLog.Swallow(caught); }
        }
    }
}
