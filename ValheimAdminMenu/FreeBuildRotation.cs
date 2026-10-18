using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimAdminMenu
{
    internal static class FreeBuildRotation
    {
        internal static bool Enabled;
        internal static int SnapDivisions = 45;
        internal static bool LocalFrame;

        private static Quaternion hammerRot = Quaternion.identity;
        private static string activeAxis = "Y";
        private static GameObject axisRoot;
        private static LineRenderer axisX, axisY, axisZ;

        internal static float StepDegrees => 180f / Mathf.Clamp(SnapDivisions, 2, 256);
        internal static string ActiveAxis => activeAxis;
        internal static Vector3 HammerEuler => hammerRot.eulerAngles;

        internal static void ResetHammer()
        {
            hammerRot = Quaternion.identity;
            activeAxis = "Y";
        }

        internal static void TickHammer(Player player)
        {
            if (!Enabled || !player || player != Player.m_localPlayer
                || (Plugin.Instance != null && Plugin.Instance.Open)
                || !player.InPlaceMode()
                || Hud.IsPieceSelectionVisible())
            {
                HideAxes();
                return;
            }

            var ghost = Reflection.Get<GameObject>(player, "m_placementGhost");
            if (!ghost || !ghost.activeInHierarchy)
            {
                HideAxes();
                return;
            }

            float scroll = ZInput.GetMouseScrollWheel();
            if (Mathf.Abs(scroll) > 0.01f)
            {
                Vector3 axis = RotationAxis();
                float sign = Mathf.Sign(scroll);
                if (LocalFrame) hammerRot = ghost.transform.rotation * Quaternion.AngleAxis(sign * StepDegrees, axis);
                else hammerRot = Quaternion.AngleAxis(sign * StepDegrees, axis) * hammerRot;
            }

            if (ZInput.GetKeyDown(KeyCode.V))
                ResetAxis();

            ghost.transform.rotation = hammerRot;
            PlacementSnap.AlignGhost(ghost);
            ShowAxes(ghost.transform.position, hammerRot);
        }

        internal static void TickPaste()
        {
            if (!Release250.PastePreviewActive)
            {
                if (!Enabled) HideAxes();
                return;
            }
            if (Plugin.Instance != null && Plugin.Instance.Open) return;
            if (Console.IsVisible() || TextInput.IsVisible() || StoreGui.IsVisible() || InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen())
                return;

            if (Input.GetMouseButtonDown(1) || ZInput.GetButtonDown("SecondaryAttack"))
            {
                Release250.CancelPastePreview();
                return;
            }

            ShowAxes(Release250.PasteWorldOrigin(), Release250.PasteRotation());

            float scroll = ZInput.GetMouseScrollWheel();
            if (Mathf.Abs(scroll) < 0.01f) return;
            float delta = Mathf.Sign(scroll) * StepDegrees;
            if (ZInput.GetKey(KeyCode.LeftControl) || ZInput.GetKey(KeyCode.RightControl))
            {
                Release250.PasteHeightOffset = Mathf.Clamp(Release250.PasteHeightOffset + Mathf.Sign(scroll) * 0.25f, -40f, 80f);
                return;
            }
            if (ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift))
            {
                activeAxis = "X";
                Release250.PastePitch = NormalizeAngle(Release250.PastePitch + delta);
            }
            else if (ZInput.GetKey(KeyCode.LeftAlt) || ZInput.GetKey(KeyCode.RightAlt))
            {
                activeAxis = "Z";
                Release250.PasteRoll = NormalizeAngle(Release250.PasteRoll + delta);
            }
            else
            {
                activeAxis = "Y";
                Release250.PasteYaw = NormalizeAngle(Release250.PasteYaw + delta);
            }
        }

        private static Vector3 RotationAxis()
        {
            if (ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift))
            {
                activeAxis = "X";
                return Vector3.right;
            }
            if (ZInput.GetKey(KeyCode.LeftAlt) || ZInput.GetKey(KeyCode.RightAlt))
            {
                activeAxis = "Z";
                return Vector3.forward;
            }
            activeAxis = "Y";
            return Vector3.up;
        }

        private static void ResetAxis()
        {
            var e = hammerRot.eulerAngles;
            if (activeAxis == "X") e.x = 0f;
            else if (activeAxis == "Z") e.z = 0f;
            else e.y = 0f;
            hammerRot = Quaternion.Euler(e);
        }

        private static float NormalizeAngle(float a)
        {
            while (a > 180f) a -= 360f;
            while (a < -180f) a += 360f;
            return a;
        }

        private static void ShowAxes(Vector3 pos, Quaternion rot)
        {
            EnsureAxes();
            if (!axisRoot) return;
            axisRoot.SetActive(true);
            axisRoot.transform.position = pos;
            axisRoot.transform.rotation = rot;
            float len = 2.2f;
            SetLine(axisX, Vector3.zero, Vector3.right * len, new Color(1f, .2f, .2f, 1f));
            SetLine(axisY, Vector3.zero, Vector3.up * len, new Color(.2f, 1f, .35f, 1f));
            SetLine(axisZ, Vector3.zero, Vector3.forward * len, new Color(.25f, .55f, 1f, 1f));
        }

        private static void HideAxes()
        {
            if (axisRoot) axisRoot.SetActive(false);
        }

        private static void EnsureAxes()
        {
            if (axisRoot) return;
            axisRoot = new GameObject("ValheavenGizmoAxes");
            UnityEngine.Object.DontDestroyOnLoad(axisRoot);
            axisX = MakeLine("X");
            axisY = MakeLine("Y");
            axisZ = MakeLine("Z");
            axisRoot.SetActive(false);
        }

        private static LineRenderer MakeLine(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(axisRoot.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = false;
            lr.startWidth = 0.08f;
            lr.endWidth = 0.045f;
            lr.widthMultiplier = 1f;
            lr.numCapVertices = 4;
            lr.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sortingOrder = 32767;
            return lr;
        }

        private static void SetLine(LineRenderer lr, Vector3 a, Vector3 b, Color c)
        {
            if (!lr) return;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.startColor = lr.endColor = c;
        }

        [HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
        private static class SetupGhostPatch
        {
            private static void Postfix(Player __instance)
            {
                if (!Enabled || __instance != Player.m_localPlayer) return;
                ResetHammer();
            }
        }

        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        private static class GhostRotPatch
        {
            private static void Postfix(Player __instance)
            {
                if (!Enabled || __instance != Player.m_localPlayer) return;
                if (DragBuilding.Active) return;
                var ghost = Reflection.Get<GameObject>(__instance, "m_placementGhost");
                if (!ghost || !ghost.activeInHierarchy)
                {
                    HideAxes();
                    return;
                }
                ghost.transform.rotation = hammerRot;
                PlacementSnap.AlignGhost(ghost);
            }
        }
    }
}
