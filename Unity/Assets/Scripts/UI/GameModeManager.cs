using F1Racing.CameraSystem;
using F1Racing.Track;
using F1Racing.Vehicle;
using F1Racing.Vehicle.Characters;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace F1Racing.UI
{
    /// <summary>
    /// Normal / Fun mode switch. The on-screen button reloads the scene (a full game reset)
    /// and flips the mode:
    ///  - Normal: original cars, minimap, first-person view (P).
    ///  - Fun:    + exploding box stacks, character model on the red car (M), front camera (F).
    /// Sets everything up on every scene load, since RuntimeInitializeOnLoadMethod only fires once.
    /// </summary>
    public class GameModeManager : MonoBehaviour
    {
        public static bool FunMode { get; private set; }

        private static bool _subscribed;
        private GUIStyle _buttonStyle, _hintStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Survives "Enter Play Mode" without a domain reload.
            FunMode = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _subscribed = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Subscribe()
        {
            if (_subscribed) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            _subscribed = true;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var track = FindAnyObjectByType<F1TrackGenerator>();
            if (track == null) return; // not a race scene

            if (FindAnyObjectByType<GameModeManager>() == null)
                new GameObject("GameModeManager").AddComponent<GameModeManager>();

            foreach (F1CarController car in FindObjectsByType<F1CarController>())
                if (car.GetComponent<TrackProgress>() == null) car.gameObject.AddComponent<TrackProgress>().track = track;

            F1MiniMap.Create(track);
            FirstPersonCameraMode.AttachToMainChaseCamera();

            if (FunMode)
            {
                PushableBoxField.Create(track);
                CharacterModelSwitcher.Attach(GameObject.Find("F1_Car_1_Red"));
            }
        }

        public static void SwitchMode()
        {
            FunMode = !FunMode;
            Scene active = SceneManager.GetActiveScene();
            if (active.buildIndex >= 0) SceneManager.LoadScene(active.buildIndex);
            else SceneManager.LoadScene(active.name);
        }

        private void OnGUI()
        {
            _buttonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            _hintStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.UpperCenter };

            float w = 170f;
            Rect r = new Rect(Screen.width * 0.5f - w * 0.5f, 16f, w, 38f);
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = FunMode ? new Color(0.55f, 0.75f, 1f) : new Color(1f, 0.55f, 0.85f);
            if (GUI.Button(r, FunMode ? "Normal mode" : "Fun mode", _buttonStyle)) SwitchMode();
            GUI.backgroundColor = prev;

            string hint = FunMode
                ? "P first person · M model · F front cam · B boxes · N map"
                : "P first person · N map";
            GUI.Label(new Rect(r.x - 120f, r.yMax + 2f, w + 240f, 18f), hint, _hintStyle);
        }
    }
}
