using HarmonyLib;
using UnityEngine;

namespace ValheimAdminMenu
{
    internal static class ViewControls
    {
        private static Light fullbrightLight;

        internal static void Tick()
        {
            if (!Plugin.Instance) return;

            bool active = Plugin.Instance.Settings.Data.fullbright;
            if (!active)
            {
                if (fullbrightLight) Object.Destroy(fullbrightLight.gameObject);
                fullbrightLight = null;
                return;
            }

            if (!GameCamera.instance) return;

            if (!fullbrightLight)
            {
                var go = new GameObject("VAM_Fullbright");
                go.transform.SetParent(GameCamera.instance.transform, false);
                fullbrightLight = go.AddComponent<Light>();
                fullbrightLight.type = LightType.Directional;
                fullbrightLight.shadows = LightShadows.None;
                fullbrightLight.cullingMask = -1;
            }

            fullbrightLight.intensity = Plugin.Instance.Settings.Data.fullbrightIntensity;
        }

        internal static void Clear()
        {
            if (fullbrightLight) Object.Destroy(fullbrightLight.gameObject);
            fullbrightLight = null;
        }
    }
}
