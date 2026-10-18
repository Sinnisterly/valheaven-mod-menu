using UnityEngine;

namespace ValheimAdminMenu
{
    internal static class WaterControls
    {
        private static GameObject walkPlatform;
        private static PhysicsMaterial walkMaterial;

        internal static void Tick()
        {
            bool enabled = Plugin.Instance && Plugin.Instance.Settings.Data.walkOnWater;
            if (!enabled || !Player.m_localPlayer)
            {
                DestroyWalkPlatform();
                return;
            }
            if (!walkPlatform)
            {
                walkPlatform = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(walkPlatform.GetComponent<MeshRenderer>());
                walkPlatform.name = "VAM_WalkOnWaterPlatform";
                walkPlatform.layer = 0;
                walkPlatform.transform.localScale = new Vector3(5f, .5f, 5f);
                walkMaterial = new PhysicsMaterial("VAM_WalkOnWaterMaterial")
                {
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    staticFriction = 0,
                    dynamicFriction = 0
                };
                walkPlatform.GetComponent<BoxCollider>().sharedMaterial = walkMaterial;
            }

            var pos = Player.m_localPlayer.transform.position;
            pos.y = (ZoneSystem.instance ? ZoneSystem.instance.m_waterLevel : 30f) - .25f;
            walkPlatform.transform.position = pos;
        }

        private static void DestroyWalkPlatform()
        {
            if (walkPlatform) Object.Destroy(walkPlatform);
            if (walkMaterial) Object.Destroy(walkMaterial);
            walkPlatform = null;
            walkMaterial = null;
        }

        internal static void Clear() => DestroyWalkPlatform();
    }
}
