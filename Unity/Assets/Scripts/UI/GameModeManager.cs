using System.Collections.Generic;
using F1Racing.AI;
using F1Racing.AI.Driving;
using F1Racing.CameraSystem;
using F1Racing.Race;
using F1Racing.Track;
using F1Racing.Vehicle;
using F1Racing.Vehicle.Characters;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace F1Racing.UI
{
    /// <summary>
    /// Composition root, run on every race-scene load (RuntimeInitializeOnLoadMethod only fires once).
    /// Builds the AI grid, the race manager and its HUD. Reloading the scene is the full reset used by
    /// Restart and by the menu's mode toggles:
    ///  - Normal: original cars, minimap, first-person view (P).
    ///  - Fun:    + exploding box stacks, character model on the red car (M), front camera (F).
    /// </summary>
    public static class GameModeManager
    {
        public static bool FunMode { get; private set; }
        /// <summary>On: the second car and its clones are AI opponents. Off: local 2-player (arrow keys).</summary>
        public static bool AIOpponents { get; private set; } = true;
        /// <summary>The baked track meshes do not match the track spline (layout changed, not rebuilt yet).</summary>
        public static bool TrackMeshStale { get; private set; }

        private static bool _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Survives "Enter Play Mode" without a domain reload.
            FunMode = false;
            AIOpponents = true;
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
            var track = Object.FindAnyObjectByType<F1TrackGenerator>();
            if (track == null) return; // not a race scene

            Transform asphalt = track.transform.Find("Track_Asphalt");
            Mesh baked = asphalt != null ? asphalt.GetComponent<MeshFilter>()?.sharedMesh : null;
            Transform gantry = track.transform.Find("StartFinish_Gantry"); // moves when StartLineDistance changes
            TrackMeshStale = baked == null || baked.vertexCount != track.Spline.Samples.Count * 2 || gantry == null
                || Vector3.Distance(gantry.position, track.Spline.GetSampleAtDistance(F1TrackGenerator.StartLineDistance).Position) > 1f;
            if (TrackMeshStale)
                Debug.LogError("Track meshes do not match the track layout: run Tools > Track > Rebuild Monza Track in the editor.");

            bool sweep = PaceSweep.ConsumeRequest(); // always consumed, so a stale request can't fire later
            F1CarController player = null, second = null;
            foreach (F1CarController car in Object.FindObjectsByType<F1CarController>(FindObjectsInactive.Include))
                if (car.controlMode == ControlMode.Player1_WASD) player = car; else second = car;
            // The menu decides, not the saved scene: an AI-only race switches the player car off (2-player needs it on).
            if (player != null) player.gameObject.SetActive(RaceSettings.HumanPlayer || !AIOpponents);

            foreach (F1CarController car in Object.FindObjectsByType<F1CarController>())
                if (car.GetComponent<TrackProgress>() == null) car.gameObject.AddComponent<TrackProgress>().track = track;

            if ((AIOpponents || sweep) && second != null) // before the minimap so it sees the clones
            {
                AIGrid grid = AIGrid.Create(track, player, second, RaceSettings.Opponents, RaceSettings.Difficulty, sweep);
                if (!sweep) RacingLineView.Create(track, grid.Drivers[0]);
                if (player == null || !player.gameObject.activeSelf) FollowWithMainCamera(second);
            }

            F1MiniMap.Create(track);
            FirstPersonCameraMode.AttachToMainChaseCamera();

            if (!sweep) // the sweep drives at once, with no race around it
            {
                var cars = new List<F1CarController>(Object.FindObjectsByType<F1CarController>());
                RaceManager race = RaceManager.Create(cars, track.Spline.TotalLength);
                RaceHUD.Create(race);
            }

            if (FunMode)
            {
                PushableBoxField.Create(track);
                CharacterModelSwitcher.Attach(GameObject.Find("F1_Car_1_Red"));
            }
        }

        private static void FollowWithMainCamera(F1CarController car)
        {
            foreach (F1ChaseCamera chase in Object.FindObjectsByType<F1ChaseCamera>())
                if (!chase.isSecondaryCamera) chase.Follow(car);
        }

        public static void SwitchMode()
        {
            FunMode = !FunMode;
            ReloadScene();
        }

        public static void ToggleAIOpponents()
        {
            AIOpponents = !AIOpponents;
            ReloadScene();
        }

        public static void ReloadScene()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.buildIndex >= 0) SceneManager.LoadScene(active.buildIndex);
            else SceneManager.LoadScene(active.name);
        }
    }
}
