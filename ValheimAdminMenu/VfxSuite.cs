using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ValheimAdminMenu
{
    /// <summary>VFX browser / spawn / attach. Syncs when prefab has ZNetView; else local-only.</summary>
    internal static class VfxSuite
    {
        // Place: 0 ground, 1 crosshair, 2 player root
        internal const int PlaceGround = 0, PlaceCrosshair = 1, PlacePlayer = 2;
        // Attach: 0 none, 1-7 body, 8 weapon, 9 feet-while-moving
        internal const int AttachNone = 0, AttachRightHand = 1, AttachLeftHand = 2, AttachBothHands = 3,
            AttachFeet = 4, AttachChest = 5, AttachHead = 6, AttachBack = 7, AttachWeapon = 8, AttachFeetMoving = 9;

        internal static string SelectedId = "";
        internal static float Duration = 12f; // 0 = until cleared
        internal static int PlaceMode = PlaceGround;
        internal static int AttachMode = AttachNone;
        /// <summary>False (default): strip damage/AoE/status so effects are visual-only.</summary>
        internal static bool DoEffect = false;
        /// <summary>Scale on effect root (reapplied on loop respawn).</summary>
        internal static float Scale = 1f;
        /// <summary>One-shot re-trigger rate + particle simulationSpeed.</summary>
        internal static float ReplaySpeed = 1f;

        private static readonly List<VfxAnchor> Anchors = new List<VfxAnchor>();
        private static string weaponId = "";
        private static string feetMovingId = "";
        private static string bodyAttachId = "";
        private static int bodyAttachMode;
        private static float bodyExpireAt = -1f;
        private static float weaponExpireAt = -1f;
        private static float feetExpireAt = -1f;
        private static VisEquipment cachedVis;
        private static Player cachedVisPlayer;
        private static Transform cachedWeaponParent;
        private static float nextWeaponResolve;
        private static readonly Dictionary<int, Transform> boneCache = new Dictionary<int, Transform>();
        private static int boneCachePlayerId;

        internal static string WeaponId => weaponId;
        internal static string FeetMovingId => feetMovingId;
        internal static string BodyAttachId => bodyAttachId;

        internal static void LoadPrefs(string weapon, string feet, string selected)
        {
            weaponId = weapon ?? "";
            feetMovingId = feet ?? "";
            SelectedId = selected ?? "";
            if (!string.IsNullOrEmpty(weaponId)) AttachMode = AttachWeapon;
            else if (!string.IsNullOrEmpty(feetMovingId)) AttachMode = AttachFeetMoving;
        }

        internal static void Tick(Player player)
        {
            Sweep();
            if (!player || player.IsDead())
            {
                ClearWeapon();
                ClearFeetMoving();
                ClearBodyAttaches();
                cachedVis = null;
                cachedVisPlayer = null;
                cachedWeaponParent = null;
                return;
            }
            var phase = Plugin.Instance.Phase;
            bool anyAttach = phase.WeaponVfx || phase.FeetTrailVfx
                || (!string.IsNullOrEmpty(bodyAttachId) && bodyAttachMode >= AttachRightHand && bodyAttachMode <= AttachBack);
            if (!anyAttach && Anchors.Count == 0) return;
            if (phase.WeaponVfx) MaintainWeapon(player); else ClearWeapon();
            if (phase.FeetTrailVfx) MaintainFeetMoving(player); else ClearFeetMoving();
            if (!string.IsNullOrEmpty(bodyAttachId) && bodyAttachMode >= AttachRightHand && bodyAttachMode <= AttachBack)
                MaintainBody(player);
        }

        internal static void ResetSession()
        {
            ClearAll(notify: false);
            weaponId = feetMovingId = bodyAttachId = "";
            cachedVis = null;
            cachedVisPlayer = null;
            cachedWeaponParent = null;
            boneCache.Clear();
            boneCachePlayerId = 0;
        }

        internal static List<KeyValuePair<string, string>> Enumerate()
        {
            var list = new List<KeyValuePair<string, string>>();
            if (!ZNetScene.instance) return list;
            foreach (var prefab in ZNetScene.instance.m_prefabs)
            {
                if (!prefab || !IsEffectPrefab(prefab)) continue;
                list.Add(new KeyValuePair<string, string>(prefab.name, prefab.name));
            }
            list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));
            return list;
        }

        /// <summary>Particle / TimedDestruction / FX name hints; excludes items, pieces, creatures.</summary>
        internal static bool IsEffectPrefab(GameObject prefab)
        {
            if (!prefab) return false;
            if (prefab.GetComponent<Character>() || prefab.GetComponent<Player>() || prefab.GetComponent<ItemDrop>()
                || prefab.GetComponent<Piece>() || prefab.GetComponent<WearNTear>() || prefab.GetComponent<Container>()
                || prefab.GetComponent<Ship>() || prefab.GetComponent<Fish>() || prefab.GetComponent<TerrainOp>()
                || prefab.GetComponent<Projectile>() || prefab.GetComponent<Humanoid>()) return false;
            string lower = (prefab.name ?? "").ToLowerInvariant();
            bool nameHint = lower.Contains("fx") || lower.Contains("vfx") || lower.Contains("smoke") || lower.Contains("mist")
                || lower.Contains("fire") || lower.Contains("flame") || lower.Contains("spark") || lower.Contains("status")
                || lower.Contains("effect") || lower.Contains("aura") || lower.Contains("blast") || lower.Contains("burst")
                || lower.Contains("glow") || lower.Contains("ember") || lower.Contains("steam") || lower.Contains("poison")
                || lower.Contains("magic") || lower.Contains("lightning") || lower.Contains("ice") || lower.Contains("blood");
            bool particles = prefab.GetComponentInChildren<ParticleSystem>(true) != null;
            bool timed = prefab.GetComponent<TimedDestruction>() != null;
            bool trail = prefab.GetComponentInChildren<TrailRenderer>(true) != null;
            bool line = prefab.GetComponentInChildren<LineRenderer>(true) != null;
            if (particles || trail || line) return true;
            return nameHint || timed;
        }

        internal static string PlaceLabel(int mode)
        {
            switch (mode)
            {
                case PlaceCrosshair: return "At crosshair";
                case PlacePlayer: return "At player";
                default: return "On ground";
            }
        }

        internal static string AttachLabel(int mode)
        {
            switch (mode)
            {
                case AttachRightHand: return "Right hand";
                case AttachLeftHand: return "Left hand";
                case AttachBothHands: return "Both hands";
                case AttachFeet: return "Feet";
                case AttachChest: return "Chest";
                case AttachHead: return "Head";
                case AttachBack: return "Back";
                case AttachWeapon: return "Equipped weapon";
                case AttachFeetMoving: return "Feet while moving";
                default: return "World spawn (no attach)";
            }
        }

        private static float ExpireFromDuration() =>
            Duration > 0.01f ? Time.time + Mathf.Clamp(Duration, 0.25f, 3600f) : -1f;

        internal static void SpawnOrAttachSelected()
        {
            if (string.IsNullOrWhiteSpace(SelectedId)) throw new ArgumentException("Choose an effect from the browser.");
            var player = Plugin.Instance.RequirePlayer();
            Plugin.Instance.RequireWorld();
            var prefab = ZNetScene.instance.GetPrefab(SelectedId);
            if (!prefab || !IsEffectPrefab(prefab)) throw new ArgumentException("That prefab is not treated as a visual effect.");

            if (AttachMode == AttachNone)
            {
                SpawnWorld(player, prefab);
                return;
            }
            if (AttachMode == AttachWeapon)
            {
                Transform parent = ResolveWeaponParent(player, requireItem: true);
                if (!parent)
                    throw new InvalidOperationException("No equipped weapon transform found. Equip a right/left-hand item first.");
                weaponId = SelectedId;
                weaponExpireAt = ExpireFromDuration();
                Plugin.Instance.Phase.WeaponVfx = true;
                ClearWeapon();
                MaintainWeapon(player);
                ReportAttach(SelectedId, "equipped weapon", PrefabCanNetwork(SelectedId));
                return;
            }
            if (AttachMode == AttachFeetMoving)
            {
                Transform parent = ResolveFeetParent(player);
                if (!parent)
                    throw new InvalidOperationException("Could not resolve feet / player transform for attach.");
                feetMovingId = SelectedId;
                feetExpireAt = ExpireFromDuration();
                Plugin.Instance.Phase.FeetTrailVfx = true;
                ClearFeetMoving();
                MaintainFeetMoving(player);
                ReportAttach(SelectedId, "feet while moving", PrefabCanNetwork(SelectedId));
                return;
            }
            var parents = ResolveBodyParents(player, AttachMode);
            if (parents.Count == 0)
                throw new InvalidOperationException("Could not resolve attach target for " + AttachLabel(AttachMode) + ".");
            bodyAttachId = SelectedId;
            bodyAttachMode = AttachMode;
            bodyExpireAt = ExpireFromDuration();
            ClearBodyAttaches();
            MaintainBody(player);
            ReportAttach(SelectedId, AttachLabel(AttachMode), PrefabCanNetwork(SelectedId));
        }

        private static void ReportAttach(string id, string where, bool synced)
        {
            string life = Duration <= 0.01f ? "until cleared" : Duration.ToString("0.#") + "s";
            Plugin.Instance.Report("Attached " + id + " to " + where + " ("
                + (synced ? "networked follow" : "local-only") + ", " + life
                + ", scale " + Scale.ToString("0.##") + "x, replay " + ReplaySpeed.ToString("0.##") + "x"
                + (DoEffect ? ", gameplay on" : ", visual only") + ").");
        }

        internal static bool PrefabCanNetwork(string prefabId)
        {
            if (string.IsNullOrEmpty(prefabId) || !ZNetScene.instance) return false;
            var prefab = ZNetScene.instance.GetPrefab(prefabId);
            return prefab && prefab.GetComponent<ZNetView>();
        }

        internal static float ClampReplay(float s) => Mathf.Clamp(s, .25f, 4f);

        private static void SpawnWorld(Player player, GameObject prefab)
        {
            Vector3 pos = PlacePoint(player);
            float expire = ExpireFromDuration();
            // Keep ZNetView so peers can see networked effects.
            CreateAnchor(prefab.name, null, pos, Quaternion.identity, expire, stripNetwork: false, attached: false, onlyWhileMoving: false, null);
            string life = Duration <= 0.01f ? "until cleared" : Duration.ToString("0.#") + "s";
            bool synced = prefab.GetComponent<ZNetView>() != null;
            Plugin.Instance.Report("Spawned " + prefab.name + " (" + PlaceLabel(PlaceMode) + ", " + life
                + ", scale " + Scale.ToString("0.##") + "x, replay " + ReplaySpeed.ToString("0.##") + "x, "
                + (DoEffect ? "gameplay on" : "visual only") + ", "
                + (synced ? "networked" : "local-only") + ").");
        }

        private static Vector3 PlacePoint(Player player)
        {
            int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle", "character");
            if (PlaceMode == PlaceCrosshair)
            {
                var cam = GameCamera.instance ? GameCamera.instance.transform : Camera.main?.transform;
                if (cam)
                {
                    if (Physics.Raycast(cam.position, cam.forward, out var hit, 120f, mask, QueryTriggerInteraction.Ignore))
                        return hit.point;
                    return cam.position + cam.forward * 8f;
                }
            }
            if (PlaceMode == PlacePlayer)
                return player.transform.position + Vector3.up * 1.1f;
            if (Physics.Raycast(player.transform.position + Vector3.up * 0.5f, Vector3.down, out var ground, 4f, mask, QueryTriggerInteraction.Ignore))
                return ground.point + Vector3.up * 0.05f;
            return player.transform.position;
        }

        internal static void ClearLast()
        {
            Sweep();
            var world = Anchors.Where(a => a && a.IsWorldSpawn).ToList();
            if (world.Count == 0) { Plugin.Instance.Report(L.T("ui.no_world_vfx_to_clear")); return; }
            var last = world[world.Count - 1];
            Anchors.Remove(last);
            if (last) DestroyAnchor(last);
            Plugin.Instance.Report(L.T("ui.cleared_last_world_vfx"));
        }

        internal static void ClearAll(bool notify = true)
        {
            ClearWeapon();
            ClearFeetMoving();
            ClearBodyAttaches();
            foreach (var a in Anchors) if (a) DestroyAnchor(a);
            Anchors.Clear();
            weaponId = feetMovingId = bodyAttachId = "";
            bodyExpireAt = weaponExpireAt = feetExpireAt = -1f;
            if (Plugin.Instance != null)
            {
                Plugin.Instance.Phase.WeaponVfx = false;
                Plugin.Instance.Phase.FeetTrailVfx = false;
            }
            if (notify && Plugin.Instance) Plugin.Instance.Report(L.T("ui.cleared_all_vfx_world_attachments"));
        }

        private static void MaintainWeapon(Player player)
        {
            if (string.IsNullOrEmpty(weaponId)) return;
            if (weaponExpireAt > 0f && Time.time >= weaponExpireAt)
            {
                ClearWeapon();
                weaponId = "";
                weaponExpireAt = -1f;
                if (Plugin.Instance != null) Plugin.Instance.Phase.WeaponVfx = false;
                return;
            }
            Transform parent = ResolveWeaponParent(player, requireItem: false);
            var existing = Anchors.FirstOrDefault(a => a && a.Kind == VfxAnchor.AnchorKind.Weapon);
            if (!parent)
            {
                // No live item yet: park so we do not leave an orphan at a stale world point.
                if (existing) existing.ParkUntilFollow();
                return;
            }
            if (!existing)
            {
                bool net = PrefabCanNetwork(weaponId);
                CreateAnchor(weaponId, parent, parent.position, parent.rotation, weaponExpireAt,
                    stripNetwork: !net, attached: true, onlyWhileMoving: false, null, VfxAnchor.AnchorKind.Weapon, networkedFollow: net);
            }
            else
            {
                existing.gameObject.SetActive(true);
                if (existing.Follow != parent) existing.BindFollow(parent);
                existing.ExpireAt = weaponExpireAt;
                existing.VisualScale = ClampScale(Scale);
                existing.ReplayRate = ClampReplay(ReplaySpeed);
                existing.AllowGameplay = DoEffect;
            }
        }

        /// <summary>Prefer live VisEquipment item. requireItem=true skips hand-bone fallback so we never spawn on empty hand.</summary>
        private static Transform ResolveWeaponParent(Player player, bool requireItem)
        {
            if (!requireItem && cachedWeaponParent && Time.unscaledTime < nextWeaponResolve)
                return cachedWeaponParent;

            var vis = GetVis(player);
            Transform found = null;
            if (vis != null)
            {
                var rightItem = TryGetGameObject(vis, "m_rightItemInstance");
                if (rightItem) found = rightItem.transform;
                else
                {
                    var leftItem = TryGetGameObject(vis, "m_leftItemInstance");
                    if (leftItem) found = leftItem.transform;
                    else if (!requireItem)
                    {
                        var right = TryGetTransform(vis, "m_rightHand");
                        if (right) found = right;
                        else
                        {
                            var left = TryGetTransform(vis, "m_leftHand");
                            if (left) found = left;
                        }
                    }
                }
            }
            if (!found && !requireItem)
            {
                found = FindBone(player, "RightHand", "Hand_R", "r_hand")
                    ?? FindBone(player, "LeftHand", "Hand_L", "l_hand");
            }
            cachedWeaponParent = found;
            nextWeaponResolve = Time.unscaledTime + .25f;
            return found;
        }

        private static VisEquipment GetVis(Player player)
        {
            if (!player) return null;
            if (cachedVisPlayer == player && cachedVis) return cachedVis;
            cachedVisPlayer = player;
            cachedVis = player.GetComponentInChildren<VisEquipment>(true);
            return cachedVis;
        }

        private static void MaintainFeetMoving(Player player)
        {
            if (string.IsNullOrEmpty(feetMovingId)) return;
            if (feetExpireAt > 0f && Time.time >= feetExpireAt)
            {
                ClearFeetMoving();
                feetMovingId = "";
                feetExpireAt = -1f;
                if (Plugin.Instance != null) Plugin.Instance.Phase.FeetTrailVfx = false;
                return;
            }
            Transform parent = ResolveFeetParent(player);
            var existing = Anchors.FirstOrDefault(a => a && a.Kind == VfxAnchor.AnchorKind.FeetMoving);
            if (!parent)
            {
                if (existing) existing.ParkUntilFollow();
                return;
            }
            if (!existing)
            {
                bool net = PrefabCanNetwork(feetMovingId);
                CreateAnchor(feetMovingId, parent, parent.position, Quaternion.identity, feetExpireAt,
                    stripNetwork: !net, attached: true, onlyWhileMoving: true, player, VfxAnchor.AnchorKind.FeetMoving, networkedFollow: net);
            }
            else
            {
                if (existing.Follow != parent) existing.BindFollow(parent);
                existing.MovePlayer = player;
                existing.OnlyWhileMoving = true;
                existing.ExpireAt = feetExpireAt;
                existing.VisualScale = ClampScale(Scale);
                existing.ReplayRate = ClampReplay(ReplaySpeed);
                existing.AllowGameplay = DoEffect;
            }
        }

        private static Transform ResolveFeetParent(Player player)
        {
            var rf = FindBone(player, "RightFoot", "Foot_R", "r_foot");
            if (rf) return rf;
            return player ? player.transform : null;
        }

        private static void MaintainBody(Player player)
        {
            if (string.IsNullOrEmpty(bodyAttachId)) return;
            if (bodyExpireAt > 0f && Time.time >= bodyExpireAt)
            {
                ClearBodyAttaches();
                bodyAttachId = "";
                bodyExpireAt = -1f;
                return;
            }
            var parents = ResolveBodyParents(player, bodyAttachMode);
            if (parents.Count == 0)
            {
                foreach (var a in Anchors.Where(x => x && x.Kind == VfxAnchor.AnchorKind.Body))
                    a.ParkUntilFollow();
                return;
            }
            var bodyAnchors = Anchors.Where(a => a && a.Kind == VfxAnchor.AnchorKind.Body).ToList();
            if (bodyAnchors.Count != parents.Count)
            {
                ClearBodyAttaches();
                bool net = PrefabCanNetwork(bodyAttachId);
                foreach (var parent in parents)
                {
                    CreateAnchor(bodyAttachId, parent, parent.position, parent.rotation, bodyExpireAt,
                        stripNetwork: !net, attached: true, onlyWhileMoving: false, null, VfxAnchor.AnchorKind.Body, networkedFollow: net);
                }
            }
            else
            {
                for (int i = 0; i < parents.Count; i++)
                {
                    if (bodyAnchors[i].Follow != parents[i]) bodyAnchors[i].BindFollow(parents[i]);
                    bodyAnchors[i].ExpireAt = bodyExpireAt;
                    bodyAnchors[i].VisualScale = ClampScale(Scale);
                    bodyAnchors[i].ReplayRate = ClampReplay(ReplaySpeed);
                    bodyAnchors[i].AllowGameplay = DoEffect;
                }
            }
        }

        private static List<Transform> ResolveBodyParents(Player player, int mode)
        {
            var list = new List<Transform>();
            var vis = GetVis(player);
            Transform right = vis ? TryGetTransform(vis, "m_rightHand") : null;
            Transform left = vis ? TryGetTransform(vis, "m_leftHand") : null;
            switch (mode)
            {
                case AttachRightHand:
                    list.Add(right ? right : FindBone(player, "RightHand", "Hand_R", "r_hand") ?? player.transform);
                    break;
                case AttachLeftHand:
                    list.Add(left ? left : FindBone(player, "LeftHand", "Hand_L", "l_hand") ?? player.transform);
                    break;
                case AttachBothHands:
                    list.Add(right ? right : FindBone(player, "RightHand", "Hand_R") ?? player.transform);
                    list.Add(left ? left : FindBone(player, "LeftHand", "Hand_L") ?? player.transform);
                    break;
                case AttachFeet:
                    var rf = FindBone(player, "RightFoot", "Foot_R", "r_foot");
                    var lf = FindBone(player, "LeftFoot", "Foot_L", "l_foot");
                    if (rf) list.Add(rf);
                    if (lf) list.Add(lf);
                    if (list.Count == 0) list.Add(player.transform);
                    break;
                case AttachChest:
                    list.Add(FindBone(player, "Spine2", "Spine1", "Chest", "Spine") ?? player.transform);
                    break;
                case AttachHead:
                    var helmet = vis ? TryGetTransform(vis, "m_helmet") : null;
                    list.Add(helmet ? helmet : FindBone(player, "Head", "head") ?? player.transform);
                    break;
                case AttachBack:
                    var back = vis
                        ? (TryGetTransform(vis, "m_backMelee")
                           ?? TryGetTransform(vis, "m_backTwohandedMelee")
                           ?? TryGetTransform(vis, "m_backBow")
                           ?? TryGetTransform(vis, "m_backTool")
                           ?? TryGetTransform(vis, "m_backShield"))
                        : null;
                    list.Add(back ? back : FindBone(player, "Spine2", "Spine1", "Back", "Spine") ?? player.transform);
                    break;
            }
            // Dedupe without LINQ alloc
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!list[i]) { list.RemoveAt(i); continue; }
                for (int j = 0; j < i; j++)
                    if (list[j] == list[i]) { list.RemoveAt(i); break; }
            }
            return list;
        }

        private static Transform FindBone(Player player, params string[] names)
        {
            if (!player) return null;
            int pid = player.GetInstanceID();
            if (boneCachePlayerId != pid) { boneCache.Clear(); boneCachePlayerId = pid; }
            int key = names[0].GetHashCode();
            for (int i = 1; i < names.Length; i++) key = unchecked(key * 397 ^ names[i].GetHashCode());
            if (boneCache.TryGetValue(key, out var cached) && cached) return cached;

            var anim = player.GetComponentInChildren<Animator>(true);
            if (anim && anim.isHuman)
            {
                foreach (var n in names)
                {
                    if (string.Equals(n, "Head", StringComparison.OrdinalIgnoreCase))
                    { var t = anim.GetBoneTransform(HumanBodyBones.Head); if (t) { boneCache[key] = t; return t; } }
                    if (n.IndexOf("RightHand", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Hand_R", StringComparison.OrdinalIgnoreCase) >= 0)
                    { var t = anim.GetBoneTransform(HumanBodyBones.RightHand); if (t) { boneCache[key] = t; return t; } }
                    if (n.IndexOf("LeftHand", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Hand_L", StringComparison.OrdinalIgnoreCase) >= 0)
                    { var t = anim.GetBoneTransform(HumanBodyBones.LeftHand); if (t) { boneCache[key] = t; return t; } }
                    if (n.IndexOf("RightFoot", StringComparison.OrdinalIgnoreCase) >= 0)
                    { var t = anim.GetBoneTransform(HumanBodyBones.RightFoot); if (t) { boneCache[key] = t; return t; } }
                    if (n.IndexOf("LeftFoot", StringComparison.OrdinalIgnoreCase) >= 0)
                    { var t = anim.GetBoneTransform(HumanBodyBones.LeftFoot); if (t) { boneCache[key] = t; return t; } }
                    if (n.IndexOf("Chest", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Spine", StringComparison.OrdinalIgnoreCase) >= 0)
                    { var t = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.Spine); if (t) { boneCache[key] = t; return t; } }
                }
            }
            foreach (var t in player.GetComponentsInChildren<Transform>(true))
            {
                if (!t) continue;
                foreach (var n in names)
                    if (t.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                    { boneCache[key] = t; return t; }
            }
            return null;
        }

        private static VfxAnchor CreateAnchor(string prefabId, Transform follow, Vector3 pos, Quaternion rot, float expireAt,
            bool stripNetwork, bool attached, bool onlyWhileMoving, Player movePlayer, VfxAnchor.AnchorKind kind = VfxAnchor.AnchorKind.World,
            bool networkedFollow = false)
        {
            if (attached && !follow)
                throw new InvalidOperationException("Attach spawn refused: no live target transform.");
            var go = new GameObject("ValheavenVfxAnchor");
            // Networked: world space + ZDO pose each frame. Local: parent to bone/weapon.
            if (follow && !networkedFollow)
            {
                go.transform.SetParent(follow, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }
            else
            {
                go.transform.position = follow ? follow.position : pos;
                go.transform.rotation = follow ? follow.rotation : rot;
            }
            var anchor = go.AddComponent<VfxAnchor>();
            anchor.PrefabId = prefabId;
            anchor.Follow = follow;
            anchor.ExpireAt = expireAt;
            anchor.VisualScale = ClampScale(Scale);
            anchor.ReplayRate = ClampReplay(ReplaySpeed);
            anchor.AllowGameplay = DoEffect;
            anchor.StripNetwork = stripNetwork;
            anchor.NetworkedFollow = networkedFollow;
            anchor.AttachedToPlayer = attached;
            anchor.OnlyWhileMoving = onlyWhileMoving;
            anchor.MovePlayer = movePlayer;
            anchor.Kind = kind;
            anchor.WorldFixed = follow == null && !networkedFollow;
            anchor.EnsureInstance(force: true);
            Anchors.Add(anchor);
            return anchor;
        }

        internal static float ClampScale(float s) => Mathf.Clamp(s, .25f, 5f);

        private static Transform TryGetTransform(object obj, string field)
        {
            try { return Reflection.Get<Transform>(obj, field); }
            catch { return null; }
        }

        private static GameObject TryGetGameObject(object obj, string field)
        {
            try { return Reflection.Get<GameObject>(obj, field); }
            catch { return null; }
        }

        private static void DestroyAnchor(VfxAnchor a)
        {
            if (!a) return;
            a.HardKillInstance();
            Object.Destroy(a.gameObject);
        }

        private static void ClearWeapon()
        {
            cachedWeaponParent = null;
            nextWeaponResolve = 0f;
            for (int i = Anchors.Count - 1; i >= 0; i--)
            {
                var a = Anchors[i];
                if (!a || a.Kind != VfxAnchor.AnchorKind.Weapon) continue;
                Anchors.RemoveAt(i);
                DestroyAnchor(a);
            }
        }

        private static void ClearFeetMoving()
        {
            for (int i = Anchors.Count - 1; i >= 0; i--)
            {
                var a = Anchors[i];
                if (!a || a.Kind != VfxAnchor.AnchorKind.FeetMoving) continue;
                Anchors.RemoveAt(i);
                DestroyAnchor(a);
            }
        }

        private static void ClearBodyAttaches()
        {
            for (int i = Anchors.Count - 1; i >= 0; i--)
            {
                var a = Anchors[i];
                if (!a || a.Kind != VfxAnchor.AnchorKind.Body) continue;
                Anchors.RemoveAt(i);
                DestroyAnchor(a);
            }
        }

        private static void Sweep()
        {
            for (int i = Anchors.Count - 1; i >= 0; i--)
                if (!Anchors[i]) Anchors.RemoveAt(i);
        }

        /// <summary>Parents to live target (local) or world-follows + ZDO sync (networked).</summary>
        internal sealed class VfxAnchor : MonoBehaviour
        {
            internal enum AnchorKind { World, Body, Weapon, FeetMoving }

            internal string PrefabId;
            internal Transform Follow;
            internal bool WorldFixed;
            internal float ExpireAt = -1f;
            internal float VisualScale = 1f;
            internal float ReplayRate = 1f;
            internal bool AllowGameplay;
            internal bool StripNetwork = true;
            internal bool NetworkedFollow;
            internal bool AttachedToPlayer;
            internal bool OnlyWhileMoving;
            internal Player MovePlayer;
            internal AnchorKind Kind = AnchorKind.World;

            private GameObject instance;
            private float nextReplayAt;
            private bool lastAllowGameplay = true;
            private float lastScale = -1f;
            private float lastReplay = -1f;
            private bool parked;
            private ParticleSystem[] cachedSystems;
            private bool particleSpaceApplied;
            private float appliedReplay = -1f;
            private float nextZdoSync;

            internal bool IsWorldSpawn => Kind == AnchorKind.World;

            internal void BindFollow(Transform follow)
            {
                Follow = follow;
                parked = false;
                if (NetworkedFollow)
                {
                    transform.SetParent(null, true);
                    if (follow)
                    {
                        transform.position = follow.position;
                        transform.rotation = follow.rotation;
                    }
                    return;
                }
                if (follow && transform.parent != follow)
                    transform.SetParent(follow, false);
                if (follow)
                {
                    transform.localPosition = Vector3.zero;
                    transform.localRotation = Quaternion.identity;
                    transform.localScale = Vector3.one;
                }
            }

            /// <summary>Kill visuals until Follow returns; do not leave particles at a stale world point.</summary>
            internal void ParkUntilFollow()
            {
                parked = true;
                HardKillInstance();
                transform.SetParent(null, false);
                gameObject.SetActive(false);
            }

            private void LateUpdate()
            {
                if (ExpireAt > 0f && Time.time >= ExpireAt)
                {
                    HardKillInstance();
                    Destroy(gameObject);
                    return;
                }
                if (parked)
                {
                    if (Follow)
                    {
                        parked = false;
                        gameObject.SetActive(true);
                        BindFollow(Follow);
                    }
                    else return;
                }
                if (AttachedToPlayer && !Follow)
                {
                    ParkUntilFollow();
                    return;
                }
                if (OnlyWhileMoving)
                {
                    bool moving = MovePlayer && (MovePlayer.GetVelocity().magnitude > .35f || MovePlayer.IsRunning());
                    if (!moving)
                    {
                        if (instance) instance.SetActive(false);
                        return;
                    }
                }
                if (Follow)
                {
                    if (NetworkedFollow)
                    {
                        SyncNetworkedPose(Follow.position, Follow.rotation);
                    }
                    else if (transform.parent != Follow)
                    {
                        transform.SetParent(Follow, false);
                        transform.localPosition = Vector3.zero;
                        transform.localRotation = Quaternion.identity;
                    }
                    // Local parented attaches already follow the bone.
                }
                EnsureInstance(force: false);
                if (instance)
                {
                    if (!NetworkedFollow && instance.transform.parent != transform)
                        instance.transform.SetParent(transform, false);
                    if (NetworkedFollow)
                    {
                        instance.transform.position = transform.position;
                        instance.transform.rotation = transform.rotation;
                        ApplyScale(instance.transform, VisualScale);
                    }
                    else if (Mathf.Abs(lastScale - VisualScale) > .001f)
                        SnapLocal(instance.transform, VisualScale);
                    // Replay/particle space: once, or when rate changes.
                    if (Mathf.Abs(appliedReplay - ReplayRate) > .001f)
                    {
                        ApplyReplaySpeedCached(ReplayRate);
                        appliedReplay = ReplayRate;
                    }
                    instance.SetActive(true);
                    if (NeedsReplayCached() && Time.time >= nextReplayAt)
                        EnsureInstance(force: true);
                }
            }

            private void SyncNetworkedPose(Vector3 pos, Quaternion rot)
            {
                transform.SetParent(null, true);
                transform.position = pos;
                transform.rotation = rot;
                if (!instance) return;
                instance.transform.position = pos;
                instance.transform.rotation = rot;
                if (Time.unscaledTime < nextZdoSync) return;
                nextZdoSync = Time.unscaledTime + .1f;
                var view = instance.GetComponent<ZNetView>();
                if (!view || !view.IsValid()) return;
                if (!view.IsOwner())
                {
                    try { view.ClaimOwnership(); } catch { }
                }
                if (!view.IsOwner()) return;
                var zdo = view.GetZDO();
                if (zdo == null) return;
                zdo.SetPosition(pos);
                zdo.SetRotation(rot);
                try
                {
                    view.SetLocalScale(Vector3.one * ClampScale(VisualScale));
                    Reflection.Method(typeof(ZNetView), "SyncScale").Invoke(view, null);
                }
                catch { }
            }

            private void OnDestroy()
            {
                HardKillInstance();
            }

            internal void HardKillInstance()
            {
                if (!instance) { instance = null; cachedSystems = null; particleSpaceApplied = false; appliedReplay = -1f; return; }
                ClearParticles(instance);
                Object.Destroy(instance);
                instance = null;
                cachedSystems = null;
                particleSpaceApplied = false;
                appliedReplay = -1f;
            }

            internal void EnsureInstance(bool force)
            {
                if (string.IsNullOrEmpty(PrefabId) || !ZNetScene.instance) return;
                if (AttachedToPlayer && !Follow) return;
                bool settingsChanged = lastAllowGameplay != AllowGameplay
                    || Mathf.Abs(lastScale - VisualScale) > .001f
                    || Mathf.Abs(lastReplay - ReplayRate) > .001f;
                if (!force && instance && !settingsChanged) return;
                HardKillInstance();
                var prefab = ZNetScene.instance.GetPrefab(PrefabId);
                if (!prefab) return;
                if (NetworkedFollow)
                {
                    Vector3 pos = Follow ? Follow.position : transform.position;
                    Quaternion rot = Follow ? Follow.rotation : transform.rotation;
                    instance = InstantiateEffect(prefab, null, StripNetwork, AllowGameplay, AttachedToPlayer, VisualScale, ReplayRate);
                    if (instance)
                    {
                        instance.transform.position = pos;
                        instance.transform.rotation = rot;
                        ApplyScale(instance.transform, VisualScale);
                    }
                }
                else
                {
                    instance = InstantiateEffect(prefab, transform, StripNetwork, AllowGameplay, AttachedToPlayer, VisualScale, ReplayRate);
                }
                CacheSystems(instance);
                if (AttachedToPlayer && !NetworkedFollow && instance && !particleSpaceApplied)
                {
                    ForceAttachedParticleSpace(instance);
                    particleSpaceApplied = true;
                }
                lastAllowGameplay = AllowGameplay;
                lastScale = VisualScale;
                lastReplay = ReplayRate;
                appliedReplay = ReplayRate;
                nextReplayAt = Time.time + EstimateLoopDelayCached(ReplayRate);
            }

            private void CacheSystems(GameObject go)
            {
                cachedSystems = go ? go.GetComponentsInChildren<ParticleSystem>(true) : null;
            }

            private float EstimateLoopDelayCached(float replay)
            {
                float delay = .12f;
                if (cachedSystems == null || cachedSystems.Length == 0) return delay / Mathf.Max(.25f, replay);
                for (int i = 0; i < cachedSystems.Length; i++)
                {
                    var ps = cachedSystems[i];
                    if (!ps) continue;
                    var main = ps.main;
                    float d = main.duration + main.startLifetime.constantMax;
                    if (d > delay) delay = Mathf.Min(d, 8f);
                }
                return Mathf.Clamp(delay * .85f / Mathf.Max(.25f, replay), .04f, 6f);
            }

            private bool NeedsReplayCached()
            {
                if (!instance) return true;
                if (cachedSystems == null || cachedSystems.Length == 0) return false;
                for (int i = 0; i < cachedSystems.Length; i++)
                {
                    var ps = cachedSystems[i];
                    if (!ps) continue;
                    if (ps.IsAlive(true) || ps.isPlaying) return false;
                }
                return true;
            }

            private void ApplyReplaySpeedCached(float replay)
            {
                if (cachedSystems == null) return;
                float rate = ClampReplay(replay);
                for (int i = 0; i < cachedSystems.Length; i++)
                {
                    var ps = cachedSystems[i];
                    if (!ps) continue;
                    var main = ps.main;
                    main.simulationSpeed = rate;
                }
            }
        }

        internal static GameObject InstantiateEffect(GameObject prefab, Transform parent, bool stripNetwork, bool allowGameplay, bool attached, float scale, float replay = 1f)
        {
            bool prior = ZNetView.m_forceDisableInit;
            if (stripNetwork) ZNetView.m_forceDisableInit = true;
            try
            {
                var obj = parent ? Object.Instantiate(prefab, parent) : Object.Instantiate(prefab);
                if (parent) SnapLocal(obj.transform, scale);
                else ApplyScale(obj.transform, scale);
                SanitizeEffect(obj, stripNetwork, allowGameplay, attached && stripNetwork);
                // Local attaches: Local particle space. Networked stays world-synced.
                if (attached && stripNetwork) ForceAttachedParticleSpace(obj);
                ApplyReplaySpeed(obj, replay);
                return obj;
            }
            finally
            {
                ZNetView.m_forceDisableInit = prior;
            }
        }

        private static void SnapLocal(Transform t, float scale)
        {
            if (!t) return;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            ApplyScale(t, scale);
        }

        private static void ApplyScale(Transform t, float scale)
        {
            if (!t) return;
            t.localScale = Vector3.one * ClampScale(scale);
        }

        internal static void ApplyReplaySpeed(GameObject obj, float replay)
        {
            if (!obj) return;
            float rate = ClampReplay(replay);
            foreach (var ps in obj.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!ps) continue;
                var main = ps.main;
                main.simulationSpeed = rate;
            }
        }

        /// <summary>World-space sim left glowing orbs at spawn points. Force Local + Hierarchy scale on bone/weapon attaches.</summary>
        internal static void ForceAttachedParticleSpace(GameObject obj)
        {
            if (!obj) return;
            foreach (var ps in obj.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!ps) continue;
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.stopAction = ParticleSystemStopAction.None;
                // Custom space can still pin to a stale transform.
                if (main.simulationSpace == ParticleSystemSimulationSpace.Custom)
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
            }
            foreach (var trail in obj.GetComponentsInChildren<TrailRenderer>(true))
            {
                if (!trail) continue;
                // Short trails so rebind does not paint the world.
                if (trail.time > .35f) trail.time = .35f;
            }
        }

        private static void ClearParticles(GameObject obj)
        {
            if (!obj) return;
            foreach (var ps in obj.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!ps) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Clear(true);
            }
            foreach (var trail in obj.GetComponentsInChildren<TrailRenderer>(true))
            {
                if (!trail) continue;
                trail.Clear();
            }
        }

        internal static void SanitizeEffect(GameObject obj, bool stripNetwork, bool allowGameplay, bool attached)
        {
            if (!obj) return;
            KillTimedDestruction(obj);
            if (stripNetwork)
            {
                foreach (var view in obj.GetComponentsInChildren<ZNetView>(true))
                {
                    if (!view) continue;
                    view.enabled = false;
                    try { Reflection.Set(view, "m_ghost", true); } catch { }
                }
            }
            if (!allowGameplay)
                StripGameplay(obj);
            foreach (var ps in obj.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!ps) continue;
                var main = ps.main;
                main.stopAction = ParticleSystemStopAction.None;
                if (attached)
                {
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
                if (!allowGameplay)
                {
                    var collision = ps.collision;
                    if (collision.enabled)
                    {
                        collision.sendCollisionMessages = false;
                        collision.collidesWith = 0;
                    }
                    var trigger = ps.trigger;
                    if (trigger.enabled) trigger.enabled = false;
                }
            }
        }

        private static void KillTimedDestruction(GameObject obj)
        {
            foreach (var timed in obj.GetComponentsInChildren<TimedDestruction>(true))
            {
                if (!timed) continue;
                timed.enabled = false;
                timed.m_triggerOnAwake = false;
                timed.m_timeout = 999999f;
                Object.Destroy(timed);
            }
        }

        /// <summary>Disable damage/poison/status/AoE/attack colliders for visual-only mode.</summary>
        internal static void StripGameplay(GameObject obj)
        {
            if (!obj) return;
            foreach (var aoe in obj.GetComponentsInChildren<Aoe>(true))
            {
                if (!aoe) continue;
                aoe.enabled = false;
                Object.Destroy(aoe);
            }
            foreach (var area in obj.GetComponentsInChildren<EffectArea>(true))
            {
                if (!area) continue;
                area.enabled = false;
                Object.Destroy(area);
            }
            foreach (var projectile in obj.GetComponentsInChildren<Projectile>(true))
            {
                if (!projectile) continue;
                projectile.enabled = false;
                Object.Destroy(projectile);
            }
            foreach (var character in obj.GetComponentsInChildren<Character>(true))
            {
                if (!character) continue;
                character.enabled = false;
            }
            foreach (var col in obj.GetComponentsInChildren<Collider>(true))
            {
                if (!col) continue;
                col.enabled = false;
            }
            foreach (var rb in obj.GetComponentsInChildren<Rigidbody>(true))
            {
                if (!rb) continue;
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }
            foreach (var behaviour in obj.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!behaviour) continue;
                string n = behaviour.GetType().Name;
                if (n.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Poison", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Hit", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.Equals("SEMan", StringComparison.OrdinalIgnoreCase)
                    || n.IndexOf("AreaEffect", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    behaviour.enabled = false;
                }
            }
        }
    }
}
