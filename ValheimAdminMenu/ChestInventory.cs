using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimAdminMenu
{
    /// <summary>Nearby chest deposit/withdraw/craft materials. Local player, access-gated.</summary>
    internal static class ChestInventory
    {
        internal static float Radius = 15f;
        internal static bool DepositMaterialsOnly;
        internal static bool DepositSkipEquipped = true;
        internal static string WithdrawSearch = "";

        private const float CacheSeconds = 0.4f;
        private static readonly List<Container> cachedChests = new List<Container>();
        private static readonly Dictionary<string, int> cachedCounts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private static float cacheUntil;
        private static Vector3 cachePos;
        private static float cacheRadius = -1f;
        private static long cachePlayerId = -1;
        private static int cacheFrame = -1;

        internal static void InvalidateCraftCache()
        {
            cacheUntil = 0f;
            cacheFrame = -1;
            cachedChests.Clear();
            cachedCounts.Clear();
        }

        internal static List<Container> NearbyAccessible(Player player, float radius)
        {
            EnsureChestList(player, radius, claimForWrite: false);
            return new List<Container>(cachedChests);
        }

        /// <summary>Read-only access: no ClaimOwnership spam. Used for craft UI checks.</summary>
        private static bool CanRead(Container container, long playerId)
        {
            if (!container) return false;
            if (container.m_checkGuardStone && !PrivateArea.CheckAccess(container.transform.position, 0f, true, false))
                return false;
            try
            {
                var allowed = (bool)Reflection.Method(typeof(Container), "CheckAccess", typeof(long))
                    .Invoke(container, new object[] { playerId });
                if (!allowed) return false;
            }
            catch (Exception caught) { AppLog.Swallow(caught); return false; }
            var view = container.GetComponent<ZNetView>();
            return view && view.IsValid();
        }

        /// <summary>Write path (deposit/withdraw/consume): claim so inventory edits stick.</summary>
        internal static bool CanUse(Container container, long playerId)
        {
            if (!CanRead(container, playerId)) return false;
            var view = container.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) return false;
            if (!view.IsOwner()) view.ClaimOwnership();
            return view.IsOwner();
        }

        private static void EnsureChestList(Player player, float radius, bool claimForWrite)
        {
            if (!player)
            {
                InvalidateCraftCache();
                return;
            }
            radius = Mathf.Clamp(radius, 1f, 50f);
            long id = player.GetPlayerID();
            Vector3 pos = player.transform.position;
            // Same-frame reuse is free; short TTL covers craft UI hammering HaveRequirementItems.
            bool fresh = Time.frameCount == cacheFrame
                || (Time.unscaledTime < cacheUntil
                    && cachePlayerId == id
                    && Mathf.Abs(cacheRadius - radius) < 0.05f
                    && (pos - cachePos).sqrMagnitude < 2.25f);
            if (fresh && !claimForWrite)
                return;

            cachedChests.Clear();
            cachedCounts.Clear();
            cacheUntil = Time.unscaledTime + CacheSeconds;
            cachePos = pos;
            cacheRadius = radius;
            cachePlayerId = id;
            cacheFrame = Time.frameCount;

            float r2 = radius * radius;
            Container[] all;
            try { all = WorldTools.Loaded<Container>(); }
            catch (Exception caught) { AppLog.Swallow(caught); return; }
            if (all == null) return;

            for (int i = 0; i < all.Length; i++)
            {
                var container = all[i];
                if (!container) continue;
                if ((container.transform.position - pos).sqrMagnitude > r2) continue;
                if (claimForWrite)
                {
                    if (!CanUse(container, id)) continue;
                }
                else if (!CanRead(container, id)) continue;

                // One optional claim when first building a readable cache (co-op chests).
                if (!claimForWrite)
                {
                    try
                    {
                        var view = container.GetComponent<ZNetView>();
                        if (view && view.IsValid() && !view.IsOwner() && container.GetInventory() == null)
                            view.ClaimOwnership();
                    }
                    catch (Exception caught) { AppLog.Swallow(caught); }
                }

                cachedChests.Add(container);
                Inventory inv = null;
                try { inv = container.GetInventory(); } catch (Exception caught) { AppLog.Swallow(caught); }
                if (inv == null) continue;
                try
                {
                    var items = inv.GetAllItems();
                    if (items == null) continue;
                    for (int j = 0; j < items.Count; j++)
                    {
                        var item = items[j];
                        if (item?.m_shared == null || item.m_stack <= 0) continue;
                        string name = item.m_shared.m_name;
                        if (string.IsNullOrEmpty(name)) continue;
                        int have;
                        cachedCounts.TryGetValue(name, out have);
                        cachedCounts[name] = have + item.m_stack;
                    }
                }
                catch (Exception caught) { AppLog.Swallow(caught); }
            }
        }

        private static void EnsureWriteChests(Player player, float radius)
        {
            // Rebuild with claim so edits work; still only once per cache window.
            cacheUntil = 0f;
            EnsureChestList(player, radius, claimForWrite: true);
        }

        private static void Touch(Container container)
        {
            try
            {
                var inv = container.GetInventory();
                if (inv != null) Reflection.Method(typeof(Inventory), "Changed", typeof(bool), typeof(bool))
                    .Invoke(inv, new object[] { true, false });
            }
            catch (Exception caught) { AppLog.Swallow(caught); }
        }

        internal static int CountNearby(Player player, string sharedName, int quality, float radius)
        {
            if (string.IsNullOrEmpty(sharedName) || !player) return 0;
            // quality -1: use aggregated cache (craft checks). Specific quality: scan inventories once.
            if (quality < 0)
            {
                EnsureChestList(player, radius, claimForWrite: false);
                int total;
                return cachedCounts.TryGetValue(sharedName, out total) ? total : 0;
            }
            int sum = 0;
            EnsureChestList(player, radius, claimForWrite: false);
            for (int i = 0; i < cachedChests.Count; i++)
            {
                try
                {
                    var inv = cachedChests[i].GetInventory();
                    if (inv != null) sum += inv.CountItems(sharedName, quality, true);
                }
                catch (Exception caught) { AppLog.Swallow(caught); }
            }
            return sum;
        }

        internal static int RemoveNearby(Player player, string sharedName, int amount, int quality, float radius)
        {
            if (amount <= 0) return 0;
            EnsureWriteChests(player, radius);
            int left = amount;
            for (int i = 0; i < cachedChests.Count; i++)
            {
                if (left <= 0) break;
                var container = cachedChests[i];
                try
                {
                    var inv = container.GetInventory();
                    if (inv == null) continue;
                    int before = inv.CountItems(sharedName, quality, true);
                    if (before <= 0) continue;
                    int take = Math.Min(left, before);
                    inv.RemoveItem(sharedName, take, quality, true);
                    int after = inv.CountItems(sharedName, quality, true);
                    int removed = Math.Max(0, before - after);
                    left -= removed;
                    Touch(container);
                }
                catch (Exception caught) { AppLog.Swallow(caught); }
            }
            InvalidateCraftCache();
            return amount - left;
        }

        internal static void DepositNearby(Plugin app)
        {
            var player = app.RequirePlayer();
            app.RequireWorld();
            float radius = Mathf.Clamp(Radius, 1, 50);
            EnsureWriteChests(player, radius);
            if (cachedChests.Count == 0)
            {
                app.Report("No accessible nearby chests within " + radius.ToString("0.#") + " m.");
                return;
            }
            var chests = new List<Container>(cachedChests);
            var inv = player.GetInventory();
            var items = inv.GetAllItems().ToList();
            int moved = 0, stacks = 0, skipped = 0;
            foreach (var item in items)
            {
                if (item?.m_shared == null) continue;
                if (DepositSkipEquipped && player.IsItemEquiped(item)) { skipped++; continue; }
                if (DepositMaterialsOnly && item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Material) { skipped++; continue; }
                int stack = item.m_stack;
                if (TryMoveToChests(inv, item, chests, preferExisting: true) || TryMoveToChests(inv, item, chests, preferExisting: false))
                {
                    moved += stack;
                    stacks++;
                }
            }
            InvalidateCraftCache();
            app.Report($"Deposited {moved} items ({stacks} stacks) into {chests.Count} chest(s) within {radius:0.#} m"
                + (skipped > 0 ? $"; skipped {skipped}." : "."));
        }

        internal static void WithdrawNearby(Plugin app)
        {
            var player = app.RequirePlayer();
            app.RequireWorld();
            float radius = Mathf.Clamp(Radius, 1, 50);
            EnsureWriteChests(player, radius);
            if (cachedChests.Count == 0)
            {
                app.Report("No accessible nearby chests within " + radius.ToString("0.#") + " m.");
                return;
            }
            var dest = player.GetInventory();
            string filter = (WithdrawSearch ?? "").Trim();
            int moved = 0, stacks = 0;
            for (int c = 0; c < cachedChests.Count; c++)
            {
                var container = cachedChests[c];
                Inventory src;
                try { src = container.GetInventory(); } catch (Exception caught) { AppLog.Swallow(caught); continue; }
                if (src == null) continue;
                foreach (var item in src.GetAllItems().ToList())
                {
                    if (item?.m_shared == null) continue;
                    if (!MatchesFilter(item, filter)) continue;
                    if (!dest.CanAddItem(item, item.m_stack)) continue;
                    int stack = item.m_stack;
                    dest.MoveItemToThis(src, item);
                    moved += stack;
                    stacks++;
                }
                Touch(container);
            }
            InvalidateCraftCache();
            app.Report($"Withdrew {moved} items ({stacks} stacks) from {cachedChests.Count} chest(s) within {radius:0.#} m"
                + (!string.IsNullOrEmpty(filter) ? $" matching \"{Rules.Text(filter)}\"." : "."));
        }

        private static bool MatchesFilter(ItemDrop.ItemData item, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            string shared = item.m_shared.m_name ?? "";
            string prefab = item.m_dropPrefab ? item.m_dropPrefab.name : "";
            string localized = Localization.instance != null ? Localization.instance.Localize(shared) : shared;
            return shared.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || prefab.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || localized.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryMoveToChests(Inventory from, ItemDrop.ItemData item, List<Container> chests, bool preferExisting)
        {
            if (item == null || item.m_stack <= 0) return false;
            foreach (var container in chests)
            {
                Inventory to;
                try { to = container.GetInventory(); } catch (Exception caught) { AppLog.Swallow(caught); continue; }
                if (to == null) continue;
                if (preferExisting)
                {
                    bool hasStack = to.CountItems(item.m_shared.m_name, item.m_quality, true) > 0
                        || to.FindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel) > 0;
                    if (!hasStack) continue;
                }
                if (!to.CanAddItem(item, item.m_stack)) continue;
                to.MoveItemToThis(from, item);
                Touch(container);
                return true;
            }
            return false;
        }

        internal static bool HaveRecipeMaterials(Player player, Recipe recipe, int qualityLevel, int amount)
        {
            if (!player || recipe?.m_resources == null) return false;
            float radius = CraftRadius();
            EnsureChestList(player, radius, claimForWrite: false);
            var station = player.GetCurrentCraftingStation();
            foreach (var req in recipe.m_resources)
            {
                if (!IncludeRequirement(req, station)) continue;
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                int need = req.GetAmount(qualityLevel) * Math.Max(1, amount);
                if (need <= 0) continue;
                int chestHave;
                cachedCounts.TryGetValue(name, out chestHave);
                int have = CountQuality(player.GetInventory(), name) + chestHave;
                if (recipe.m_requireOnlyOneIngredient)
                {
                    if (have >= need) return true;
                    continue;
                }
                if (have < need) return false;
            }
            return !recipe.m_requireOnlyOneIngredient;
        }

        internal static bool HavePieceMaterials(Player player, Piece piece)
        {
            if (!player || piece?.m_resources == null) return false;
            float radius = CraftRadius();
            EnsureChestList(player, radius, claimForWrite: false);
            foreach (var req in piece.m_resources)
            {
                if (req?.m_resItem?.m_itemData?.m_shared == null || req.m_amount <= 0) continue;
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                int chestHave;
                cachedCounts.TryGetValue(name, out chestHave);
                int have = player.GetInventory().CountItems(name, -1, true) + chestHave;
                if (have < req.m_amount) return false;
            }
            return true;
        }

        internal static void ConsumeCraftMaterials(Player player, Piece.Requirement[] requirements, int qualityLevel, int itemQuality, int multiplier)
        {
            if (!player || requirements == null) return;
            float radius = CraftRadius();
            var station = player.GetCurrentCraftingStation();
            var inv = player.GetInventory();
            int fromChests = 0;
            foreach (var req in requirements)
            {
                if (!IncludeRequirement(req, station)) continue;
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                int need = req.GetAmount(qualityLevel) * Math.Max(1, multiplier);
                if (need <= 0) continue;
                int inBag = inv.CountItems(name, itemQuality, true);
                int takeBag = Math.Min(need, inBag);
                if (takeBag > 0) inv.RemoveItem(name, takeBag, itemQuality, true);
                int remain = need - takeBag;
                if (remain > 0) fromChests += RemoveNearby(player, name, remain, itemQuality, radius);
            }
            if (fromChests > 0 && Plugin.Instance)
                Plugin.Instance.Report("Craft used " + fromChests + " material(s) from nearby chests.");
        }

        private static bool IncludeRequirement(Piece.Requirement req, CraftingStation station)
        {
            if (req?.m_resItem?.m_itemData?.m_shared == null) return false;
            if (station && station.m_upgrader != req.m_upgraderResource) return false;
            if (!station && req.m_upgraderResource) return false;
            return true;
        }

        private static int CountQuality(Inventory inv, string name)
        {
            if (inv == null) return 0;
            int best = 0;
            for (int q = 1; q < 20; q++)
            {
                int c = inv.CountItems(name, q, true);
                if (c > best) best = c;
            }
            int any = inv.CountItems(name, -1, true);
            return Math.Max(best, any);
        }

        private static float CraftRadius()
        {
            var phase = Plugin.Instance?.Phase;
            float r = phase != null ? phase.CraftChestRadius : Radius;
            return Mathf.Clamp(r, 1, 50);
        }
    }
}
