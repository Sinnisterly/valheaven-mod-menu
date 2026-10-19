using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimAdminMenu
{
    /// <summary>Bag rows/cols, chest bonus rows, stack mult/override. No console, no m_cheated, no per-frame FindObjects.</summary>
    internal static class InventoryCapacity
    {
        internal const int DefaultRows = 4;
        internal const int DefaultCols = 8;
        internal const int MinRows = 4;
        internal const int MaxRows = 12;
        internal const int MaxExtraCols = 4;
        internal const int MaxChestBonusRows = 8;
        internal const float MaxStackMult = 50f;
        internal const int MaxStackOverride = 9999;

        /// <summary>False = trader bag upgrades own size; sliders ignored.</summary>
        internal static bool OverrideBagSize;
        /// <summary>Bag height while override is on (vanilla 4; SetInventorySize clamps to 9, we allow 12).</summary>
        internal static int PlayerRows = DefaultRows;
        /// <summary>Extra columns past vanilla width 8. Override only.</summary>
        internal static int PlayerExtraCols;
        /// <summary>Bonus rows on Container.m_height (awake / open / UpdateRows).</summary>
        internal static int ChestBonusRows;
        /// <summary>Multiplies SharedData.m_maxStackSize (1 = vanilla).</summary>
        internal static float StackMult = 1f;
        /// <summary>When &gt; 0, absolute max stack instead of orig * mult.</summary>
        internal static int StackOverride;

        private static readonly FieldInfo InvWidthField =
            AccessTools.Field(typeof(Inventory), "m_width");
        private static readonly Dictionary<ItemDrop.ItemData.SharedData, int> OriginalStacks =
            new Dictionary<ItemDrop.ItemData.SharedData, int>();
        private static readonly ConditionalWeakTable<Container, ChestBase> ChestBases =
            new ConditionalWeakTable<Container, ChestBase>();

        // Equipment and Quick Slots puts gear in hidden rows past the visible bag and resets
        // m_height every player update. Visible rows only change via Slots.SetBaseRows.
        // Looked up at apply time so a mod loaded later is still seen.
        private const int EqsHiddenRows = 3;
        private static MethodInfo eqsSetBaseRows;
        private static MethodInfo eqsUpdateSlots;
        private static PropertyInfo eqsExtraRows;
        private static PropertyInfo eqsVisibleRows;
        private static int applyDepth;

        private sealed class ChestBase
        {
            internal int Height;
        }

        private static void SetInventoryWidth(Inventory inv, int width)
        {
            if (inv == null || InvWidthField == null) return;
            if (inv.GetWidth() == width) return;
            InvWidthField.SetValue(inv, width);
        }

        internal static void Reset()
        {
            RestoreStackSizes();
        }

        /// <summary>Menu Apply: write current sliders (including back to 4x8).</summary>
        internal static void ApplyAll(bool notify = false)
        {
            ApplyStackSizes();
            if (OverrideBagSize)
                ApplyPlayerInventory();
            if (notify && Plugin.Instance)
                Plugin.Instance.Report(StatusLine());
        }

        internal static string StatusLine()
        {
            int chest = Mathf.Clamp(ChestBonusRows, 0, MaxChestBonusRows);
            string stacks = StackOverride > 0
                ? ("override " + StackOverride)
                : (Mathf.Abs(StackMult - 1f) < 0.01f ? "vanilla" : (StackMult.ToString("0.##") + "x"));
            if (!OverrideBagSize)
                return L.Tf("build.capacity_status_off", chest, stacks);
            int cols = DefaultCols + Mathf.Clamp(PlayerExtraCols, 0, MaxExtraCols);
            int rows = Mathf.Clamp(PlayerRows, MinRows, MaxRows);
            return L.Tf("build.capacity_status", rows, cols, chest, stacks);
        }

        internal static bool HasPlayerCustomSize => OverrideBagSize;

        internal static void ApplyPlayerInventory(bool force = false)
        {
            if (!OverrideBagSize || applyDepth > 0) return;
            var player = Player.m_localPlayer;
            if (!player || player.IsDead()) return;
            var inv = player.GetInventory();
            if (inv == null) return;

            applyDepth++;
            try
            {
                int rows = Mathf.Clamp(PlayerRows, MinRows, MaxRows);
                int cols = DefaultCols + Mathf.Clamp(PlayerExtraCols, 0, MaxExtraCols);
                int prevH = inv.GetHeight();
                int prevW = inv.GetWidth();
                bool eqs = EnsureEquipmentSlots();
                int oldVisible = eqs && prevH > EqsHiddenRows ? prevH - EqsHiddenRows : prevH;

                int extra = 0;
                if (eqs && eqsExtraRows != null)
                {
                    try { extra = Mathf.Max(0, (int)eqsExtraRows.GetValue(null, null)); }
                    catch (Exception caught) { AppLog.Swallow(caught); extra = 0; }
                }

                // EQS visible = BaseRows + ExtraRows. invrows stores vanilla base (max 9) so server load agrees with EQS.
                int baseRows = eqs ? Mathf.Clamp(rows - extra, 1, MaxRows) : rows;
                WriteInvRows(player, Mathf.Min(baseRows, 9));
                SetInventoryWidth(inv, cols);

                int visible = rows;
                if (eqs)
                {
                    try { eqsSetBaseRows.Invoke(null, new object[] { baseRows }); }
                    catch (Exception ex) { Plugin.Instance?.Report("Equipment slot rows were not updated. " + ex.Message); }
                    if (cols != prevW && eqsUpdateSlots != null)
                    {
                        try { eqsUpdateSlots.Invoke(null, new object[] { true }); }
                        catch (Exception caught) { AppLog.Swallow(caught); /* slot cells keep the previous width until the next row change */ }
                    }

                    if (eqsVisibleRows != null)
                    {
                        try { visible = (int)eqsVisibleRows.GetValue(null, null); }
                        catch (Exception caught) { AppLog.Swallow(caught); visible = baseRows + extra; }
                    }
                    else visible = baseRows + extra;
                }
                else if (prevH != rows)
                    inv.SetHeight(rows);

                var gui = InventoryGui.instance;
                if (gui != null && (force || visible != oldVisible || (!eqs && prevH != rows)))
                    gui.SetInventorySize(Mathf.Max(MinRows, visible));

                if (visible < oldVisible || cols < prevW)
                    player.DropInvalidItems();
            }
            finally
            {
                applyDepth--;
            }
        }

        private static bool EnsureEquipmentSlots()
        {
            if (eqsSetBaseRows != null) return true;
            var slots = AccessTools.TypeByName("EquipmentAndQuickSlots.Slots");
            if (slots == null) return false;
            eqsSetBaseRows = AccessTools.Method(slots, "SetBaseRows");
            eqsUpdateSlots = AccessTools.Method(slots, "UpdateSlotsGridPosition");
            eqsExtraRows = AccessTools.Property(slots, "ExtraRows");
            eqsVisibleRows = AccessTools.Property(slots, "VisibleRows");
            return eqsSetBaseRows != null;
        }

        /// <summary>Persists bag height for game/dedicated-server restore.</summary>
        private static void WriteInvRows(Player player, int rows)
        {
            string next = Mathf.Clamp(rows, 1, 9).ToString();
            try
            {
                if (player.TryGetUniqueKeyValue("invrows", out var current) && current == next) return;
                player.AddUniqueKeyValue("invrows", next);
            }
            catch (Exception caught) { AppLog.Swallow(caught); /* unique-key sync can fail before the player view exists */ }
        }

        internal static void EnsureChest(Container container)
        {
            if (container == null) return;
            int bonus = Mathf.Clamp(ChestBonusRows, 0, MaxChestBonusRows);
            if (bonus <= 0) return;
            if (!ChestBases.TryGetValue(container, out var box))
            {
                // First touch: m_height is still the prefab base (before our bonus).
                box = new ChestBase { Height = Math.Max(1, container.m_height) };
                ChestBases.Add(container, box);
            }

            int target = box.Height + bonus;
            if (container.m_height != target)
                container.m_height = target;

            var inv = container.GetInventory();
            if (inv == null) return;
            // Don't shrink below what UpdateRows already needed for existing items.
            int need = Math.Max(target, inv.GetHeight());
            if (inv.GetHeight() < need)
                inv.SetHeight(need);
        }

        internal static void ApplyStackSizes()
        {
            float mult = Mathf.Clamp(StackMult, 1f, MaxStackMult);
            int ov = Mathf.Clamp(StackOverride, 0, MaxStackOverride);
            if (Mathf.Abs(mult - 1f) < 0.01f && ov <= 0)
            {
                RestoreStackSizes();
                return;
            }

            if (!ObjectDB.instance || ObjectDB.instance.m_items == null) return;
            foreach (var go in ObjectDB.instance.m_items)
            {
                if (!go) continue;
                var drop = go.GetComponent<ItemDrop>();
                var shared = drop?.m_itemData?.m_shared;
                if (shared == null || shared.m_maxStackSize <= 1) continue;

                if (!OriginalStacks.ContainsKey(shared))
                    OriginalStacks[shared] = shared.m_maxStackSize;

                int orig = OriginalStacks[shared];
                int next = ov > 0
                    ? Mathf.Clamp(ov, 1, MaxStackOverride)
                    : Mathf.Clamp(Mathf.RoundToInt(orig * mult), 1, MaxStackOverride);
                if (shared.m_maxStackSize != next)
                    shared.m_maxStackSize = next;
            }
        }

        internal static void RestoreStackSizes()
        {
            if (OriginalStacks.Count == 0) return;
            foreach (var kv in OriginalStacks)
            {
                if (kv.Key != null)
                    kv.Key.m_maxStackSize = kv.Value;
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class InventoryCapacityOnSpawnPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player __instance)
        {
            if (!Plugin.Instance || __instance != Player.m_localPlayer) return;
            // After vanilla invrows / store upgrades, re-apply size + stacks.
            InventoryCapacity.ApplyStackSizes();
            if (InventoryCapacity.HasPlayerCustomSize)
                InventoryCapacity.ApplyPlayerInventory(force: true);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetInventorySize))]
    internal static class InventoryCapacitySetSizePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player __instance)
        {
            if (!Plugin.Instance || __instance != Player.m_localPlayer) return;
            if (InventoryCapacity.HasPlayerCustomSize)
                InventoryCapacity.ApplyPlayerInventory(force: true);
        }
    }

    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class InventoryCapacityContainerAwakePatch
    {
        private static void Prefix(Container __instance)
        {
            if (__instance == null || InventoryCapacity.ChestBonusRows <= 0) return;
            InventoryCapacity.EnsureChest(__instance);
        }

        private static void Postfix(Container __instance)
        {
            if (__instance == null || InventoryCapacity.ChestBonusRows <= 0) return;
            InventoryCapacity.EnsureChest(__instance);
        }
    }

    [HarmonyPatch(typeof(Container), "UpdateRows")]
    internal static class InventoryCapacityContainerRowsPatch
    {
        private static void Postfix(Container __instance)
        {
            if (__instance == null || InventoryCapacity.ChestBonusRows <= 0) return;
            InventoryCapacity.EnsureChest(__instance);
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class InventoryCapacityContainerInteractPatch
    {
        private static void Prefix(Container __instance)
        {
            if (__instance == null || InventoryCapacity.ChestBonusRows <= 0) return;
            InventoryCapacity.EnsureChest(__instance);
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    internal static class InventoryCapacityObjectDbPatch
    {
        private static void Postfix()
        {
            if (!Plugin.Instance) return;
            InventoryCapacity.ApplyStackSizes();
        }
    }
}
