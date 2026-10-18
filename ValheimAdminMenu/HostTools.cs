namespace ValheimAdminMenu
{
    /// <summary>Host authority checks.</summary>
    internal static class HostTools
    {
        internal static bool IsHost()
        {
            return ZNet.instance && ZNet.instance.IsServer();
        }
    }
}
