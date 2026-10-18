namespace ValheimAdminMenu
{
    internal static class MapControls
    {
        private static bool owned;
        private static bool originalNoMap;

        internal static void Tick()
        {
            bool enabled = Plugin.Instance && Plugin.Instance.Settings.Data.forceEnableMap;
            if (enabled && Game.instance)
            {
                if (!owned) { originalNoMap = Game.m_noMap; owned = true; }
                Game.m_noMap = false;
            }
            else Restore();
        }

        internal static void Clear() => Restore();

        private static void Restore()
        {
            if (!owned) return;
            Game.m_noMap = originalNoMap;
            owned = false;
        }
    }
}
