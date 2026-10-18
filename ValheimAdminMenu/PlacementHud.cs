using UnityEngine;

namespace ValheimAdminMenu
{
    internal static class PlacementHud
    {
        internal static bool Enabled = true;
        internal static bool ShowRotation = true;
        internal static bool ShowAxisStep = true;
        internal static bool ShowPieceCount = true;
        internal static bool ShowLockState = true;
        internal static bool ShowGroundSnap = true;
        internal static bool ShowInTerrain = true;
        internal static bool ShowPrefabName = true;

        private static GUIStyle panelStyle;
        private static GUIStyle labelStyle;
        private static Texture2D panelTex;

        internal static void Draw()
        {
            if (!Enabled) return;
            var app = Plugin.Instance;
            if (app == null || app.Open) return;
            if (app.Phase != null && (app.Phase.HideHud || app.Phase.PhotoMode)) return;

            bool paste = Release250.PastePreviewActive;
            bool hammer = FreeBuildRotation.Enabled && Player.m_localPlayer && Player.m_localPlayer.InPlaceMode();
            bool drag = DragBuilding.Active;
            if (!paste && !hammer && !drag) return;

            EnsureStyles();
            float w = 220f * app.UiScale;
            float line = 16f * app.UiScale;
            int lines = 0;
            if (ShowRotation) lines++;
            if (ShowAxisStep) lines++;
            if (paste && ShowPieceCount) lines++;
            if (paste && ShowLockState) lines++;
            if (paste && ShowGroundSnap) lines++;
            if (paste && ShowInTerrain) lines++;
            if (hammer && ShowPrefabName) lines++;
            if (drag && ShowPieceCount) lines++;
            if (lines == 0) return;

            float h = 10f * app.UiScale + lines * line + 8f * app.UiScale;
            float pad = 12f * app.UiScale;
            float bottomLift = Screen.height * 0.25f;
            var rect = new Rect(Screen.width - w - pad, Screen.height - h - bottomLift, w, h);
            GUI.Box(rect, GUIContent.none, panelStyle);
            float y = rect.y + 6f * app.UiScale;
            float x = rect.x + 8f * app.UiScale;

            if (ShowRotation)
            {
                Vector3 e = paste
                    ? new Vector3(Release250.PastePitch, Release250.PasteYaw, Release250.PasteRoll)
                    : FreeBuildRotation.HammerEuler;
                DrawLine(x, y, string.Format("Rot  X {0:0.#}  Y {1:0.#}  Z {2:0.#}", Norm(e.x), Norm(e.y), Norm(e.z)));
                y += line;
            }
            if (ShowAxisStep)
            {
                DrawLine(x, y, "Axis " + FreeBuildRotation.ActiveAxis + "  step " + FreeBuildRotation.StepDegrees.ToString("0.##") + " deg");
                y += line;
            }
            if (paste && ShowPieceCount)
            {
                DrawLine(x, y, "Pieces " + Release250.Clipboard.Count);
                y += line;
            }
            if (paste && ShowLockState)
            {
                DrawLine(x, y, Release250.PasteLocked ? "LOCKED (P unlocks)" : "Following aim (P locks)");
                y += line;
            }
            if (paste && ShowGroundSnap)
            {
                string mode = Release250.PasteAnchorSnap ? "Anchor snap"
                    : Release250.PasteSnapToGround ? "Ground seat" : "Free aim";
                DrawLine(x, y, mode);
                y += line;
            }
            if (paste && ShowInTerrain)
            {
                DrawLine(x, y, "In terrain  " + Release250.HudPiecesInTerrain);
                y += line;
            }
            if (hammer && ShowPrefabName)
            {
                string name = SelectedPrefabName(Player.m_localPlayer);
                if (!string.IsNullOrEmpty(name))
                {
                    DrawLine(x, y, name);
                    y += line;
                }
            }
            if (drag && ShowPieceCount)
            {
                DrawLine(x, y, "Drag  " + DragBuilding.PreviewCount);
                y += line;
            }
        }

        private static void DrawLine(float x, float y, string text)
        {
            GUI.Label(new Rect(x, y, 200f * Plugin.Instance.UiScale, 18f * Plugin.Instance.UiScale), text, labelStyle);
        }

        private static float Norm(float a)
        {
            while (a > 180f) a -= 360f;
            while (a < -180f) a += 360f;
            return a;
        }

        private static string SelectedPrefabName(Player player)
        {
            try
            {
                var ghost = Reflection.Get<GameObject>(player, "m_placementGhost");
                if (!ghost) return "";
                var piece = ghost.GetComponent<Piece>();
                if (piece && !string.IsNullOrEmpty(piece.m_name)) return piece.m_name;
                return ghost.name.Replace("(Clone)", "").Trim();
            }
            catch { return ""; }
        }

        private static void EnsureStyles()
        {
            if (panelStyle != null) return;
            panelTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            panelTex.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.07f, 0.55f));
            panelTex.Apply();
            panelStyle = new GUIStyle(GUI.skin.box) { normal = { background = panelTex } };
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(12 * (Plugin.Instance ? Plugin.Instance.UiScale : 1f)),
                normal = { textColor = new Color(0.9f, 0.93f, 0.96f, 0.95f) },
                alignment = TextAnchor.MiddleLeft
            };
        }
    }
}
