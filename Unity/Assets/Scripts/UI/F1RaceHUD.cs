using F1Racing.CameraSystem;
using F1Racing.Vehicle;
using UnityEngine;

namespace F1Racing.UI
{
    /// <summary>
    /// Lightweight runtime telemetry HUD for both F1 cars and camera/AI mode controls.
    /// </summary>
    public class F1RaceHUD : MonoBehaviour
    {
        public F1CarController car1;
        public F1CarController car2;
        public F1ChaseCamera mainChaseCamera;

        private GUIStyle _panelStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _bigSpeedStyle;

        private void InitStyles()
        {
            if (_panelStyle != null) return;

            Texture2D bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.09f, 0.78f));
            bgTex.Apply();

            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = bgTex }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.white }
            };

            _bigSpeedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
        }

        private void OnGUI()
        {
            InitStyles();

            if (car1 != null)
                DrawCarTelemetry(new Rect(16, 16, 250, 95), car1, "P1 (WASD + L-Shift DRS)");

            if (car2 != null)
            {
                string modeLabel = car2.controlMode == ControlMode.AI_Waypoint
                    ? "AI Mode (T = Arrows)"
                    : "P2 (Arrows + R-Shift DRS)";
                DrawCarTelemetry(new Rect(Screen.width - 266, 16, 250, 95), car2, modeLabel);
            }

            string camText = (mainChaseCamera != null && mainChaseCamera.IsSplitScreen)
                ? "Split-Screen ON (V)"
                : $"Cam: {(mainChaseCamera != null && mainChaseCamera.ActiveTarget != null ? mainChaseCamera.ActiveTarget.carName : "Car 1")} (C = Switch, V = Split)";

            GUI.Box(new Rect(Screen.width * 0.5f - 230, Screen.height - 42, 460, 28), "", _panelStyle);
            GUI.Label(new Rect(Screen.width * 0.5f - 220, Screen.height - 38, 440, 22),
                $"{camText}  |  T: Car 2 AI  |  R: Reset P1", _labelStyle);
        }

        private void DrawCarTelemetry(Rect rect, F1CarController car, string subtitle)
        {
            GUI.Box(rect, "", _panelStyle);
            GUI.Label(new Rect(rect.x + 10, rect.y + 6, rect.width - 20, 20), $"{car.carName} - {subtitle}", _labelStyle);
            GUI.Label(new Rect(rect.x + 10, rect.y + 28, 130, 32), $"{Mathf.RoundToInt(car.CurrentSpeedKmh)} km/h", _bigSpeedStyle);
            GUI.Label(new Rect(rect.x + 145, rect.y + 34, 95, 24), $"G{car.CurrentGear} | {(int)car.CurrentRpm}", _labelStyle);

            string drsStatus = car.IsDrsActive ? "DRS OPEN" : (car.IsBraking ? "BRAKE" : "DRS READY");
            GUI.Label(new Rect(rect.x + 10, rect.y + 64, 220, 22), drsStatus, _labelStyle);
        }
    }
}
