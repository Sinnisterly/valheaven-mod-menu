using System;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ValheimAdminMenu
{
    /// <summary>
    /// The build bar has a fixed set of tab buttons (Hud.m_pieceCategoryTabs); categories past the last one had no
    /// button (Q/E still reached them). With more categories than buttons, the first and last buttons page the row
    /// (also mouse wheel over the row) and the middle ones show a window that follows the selection.
    /// </summary>
    internal static class BuildTabPager
    {
        private static int offset, lastSelected = -1, lastCount = -1;
        internal static bool Active { get; private set; }

        internal static void Layout(Hud hud, Player player)
        {
            var tabs = hud.m_pieceCategoryTabs;
            Active = false;
            if (tabs == null || tabs.Length < 3 || !player.InPlaceMode()) return;
            player.GetBuildSelection(out _, out _, out _, out var category, out var table);
            if (!table || table.m_categories == null) return;
            int count = table.m_categories.Count, slots = tabs.Length - 2;
            if (count <= tabs.Length) { offset = 0; return; }
            Active = true;
            if (count != lastCount)
            {
                lastCount = count;
                AppLog.Debug("Build", "Tab paging on: " + count + " categories, " + tabs.Length + " buttons");
            }
            int selected = table.m_categories.IndexOf(category);
            if (selected != lastSelected)
            {
                lastSelected = selected;
                if (selected >= 0 && selected < offset) offset = selected;
                else if (selected >= offset + slots) offset = selected - slots + 1;
            }
            offset = Mathf.Clamp(offset, 0, count - slots);
            int after = count - offset - slots;
            var loc = Localization.instance;
            for (int i = 0; i < tabs.Length; i++)
            {
                string text;
                bool highlight = false;
                if (i == 0) text = offset > 0 ? "<noparse><<</noparse> <color=yellow>" + offset + "</color>" : "<color=#777777><noparse><<</noparse></color>";
                else if (i == tabs.Length - 1) text = after > 0 ? "<color=yellow>" + after + "</color> <noparse>>></noparse>" : "<color=#777777><noparse>>></noparse></color>";
                else
                {
                    int index = offset + i - 1;
                    string label = loc != null ? loc.Localize(table.m_categoryLabels[index]) : table.m_categoryLabels[index];
                    text = label + " [<color=yellow>" + player.GetAvailableBuildPiecesInCategory(table.m_categories[index]) + "</color>]";
                    highlight = table.m_categories[index] == category;
                }
                SetTab(tabs[i], text, highlight);
            }
        }

        // Same as Hud.UpdateBuild: the "Selected" child carries the highlighted label.
        private static void SetTab(GameObject tab, string text, bool highlight)
        {
            if (!tab) return;
            tab.SetActive(true);
            var selected = tab.transform.Find("Selected");
            if (selected) selected.gameObject.SetActive(highlight);
            var label = highlight && selected ? selected.GetComponentInChildren<TMP_Text>() : tab.GetComponentInChildren<TMP_Text>();
            if (label) label.text = text;
        }

        internal static void Page(int direction, Hud hud)
        {
            int slots = hud.m_pieceCategoryTabs.Length - 2;
            offset = Mathf.Max(0, offset + direction * slots);
        }

        // Returns false when the click was handled here.
        internal static bool Click(Hud hud, GameObject tab)
        {
            if (!Active || !Player.m_localPlayer) return true;
            var tabs = hud.m_pieceCategoryTabs;
            int i = Array.IndexOf(tabs, tab);
            if (i < 0) return true;
            if (i == 0) Page(-1, hud);
            else if (i == tabs.Length - 1) Page(1, hud);
            else
            {
                Player.m_localPlayer.SetBuildCategory(offset + i - 1);
                lastSelected = offset + i - 1;
            }
            return false;
        }

        // Mouse wheel over the tab row pages it (the wheel otherwise rotates the ghost).
        internal static void Wheel(Hud hud)
        {
            if (!Active || hud.m_pieceCategoryTabs.Length == 0) return;
            float wheel = ZInput.GetMouseScrollWheel();
            if (Mathf.Abs(wheel) < .01f) return;
            var row = hud.m_pieceCategoryTabs[0].transform.parent as RectTransform;
            if (!row || !RectTransformUtility.RectangleContainsScreenPoint(row, Input.mousePosition, null)) return;
            Page(wheel > 0f ? -1 : 1, hud);
        }
    }

    [HarmonyPatch(typeof(Hud), "UpdateBuild")]
    internal static class BuildTabPagerPatch
    {
        private static void Postfix(Hud __instance, Player player)
        {
            try
            {
                if (!player || !__instance.m_buildHud || !__instance.m_buildHud.activeInHierarchy) return;
                BuildTabPager.Layout(__instance, player);
                BuildTabPager.Wheel(__instance);
            }
            catch (Exception caught) { AppLog.Swallow(caught); }
        }
    }

    [HarmonyPatch(typeof(Hud), "OnLeftClickCategory")]
    internal static class BuildTabPagerClickPatch
    {
        private static bool Prefix(Hud __instance, UIInputHandler ih)
        {
            try { return ih == null || BuildTabPager.Click(__instance, ih.gameObject); }
            catch (Exception caught) { AppLog.Swallow(caught); return true; }
        }
    }
}
