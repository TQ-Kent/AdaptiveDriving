using System.Collections.Generic;
using F1Racing.AI.Driving;
using F1Racing.AI.Integration;
using F1Racing.Track;
using F1Racing.Vehicle;
using UnityEngine;

namespace F1Racing.AI
{
    /// <summary>
    /// Composition root for AI opponents: turns the second car into an AI driver, clones it into the next
    /// grid slots, and gives each one a stable identity and livery. Created by GameModeManager on scene load.
    /// The difficulty is the player's manual menu choice, applied once here and locked for the race.
    /// Debug keys: F8 pings the Python AI service, F7 asks it for a (not applied) difficulty proposal.
    /// </summary>
    public class AIGrid : MonoBehaviour
    {
        public int pythonTimeoutMs = 250;

        // Fictional drivers. Offsets stay small and spread so drivers don't saturate at the same bound.
        // Then: aggression, livery, and the v1.2 style lateBraking, defending, patience (provisional, chosen for variety).
        public OpponentIdentity[] identities =
        {
            new OpponentIdentity("Max", 0.04f, 0.6f, new Color(0.15f, 0.45f, 1f), 0.9f, 0.8f, 0.2f),
            new OpponentIdentity("Kimi", -0.03f, 0.4f, new Color(0.1f, 0.75f, 0.3f), 0.6f, 0.4f, 0.5f),
            new OpponentIdentity("Lewis", 0f, 0.5f, new Color(1f, 0.5f, 0.05f), 0.7f, 0.6f, 0.6f),
            new OpponentIdentity("George", 0.06f, 0.7f, new Color(0.6f, 0.2f, 0.9f), 0.5f, 0.7f, 0.4f),
            new OpponentIdentity("Charles", -0.06f, 0.3f, new Color(1f, 0.85f, 0.1f), 0.8f, 0.3f, 0.3f),
            new OpponentIdentity("Lando", 0.02f, 0.5f, new Color(0.95f, 0.95f, 0.95f), 0.5f, 0.5f, 0.6f),
            new OpponentIdentity("Oscar", -0.02f, 0.6f, new Color(0.1f, 0.1f, 0.12f), 0.4f, 0.8f, 0.7f),
            new OpponentIdentity("Isack", 0.05f, 0.45f, new Color(0f, 0.8f, 0.8f), 0.7f, 0.4f, 0.3f),
            new OpponentIdentity("Liam", -0.05f, 0.55f, new Color(1f, 0.4f, 0.7f), 0.6f, 0.3f, 0.4f),
            new OpponentIdentity("Bearman", 0.01f, 0.35f, new Color(0.45f, 0.25f, 0.1f), 0.3f, 0.6f, 0.7f),
            new OpponentIdentity("Pierre", -0.01f, 0.65f, new Color(0.6f, 0.6f, 0.65f), 0.6f, 0.5f, 0.5f),
            new OpponentIdentity("Franco", 0.03f, 0.4f, new Color(0.05f, 0.2f, 0.5f), 0.8f, 0.2f, 0.2f),
            new OpponentIdentity("Fernando", -0.04f, 0.7f, new Color(0.55f, 0.85f, 0.2f), 0.7f, 0.9f, 0.5f),
            new OpponentIdentity("Carlos", 0.07f, 0.5f, new Color(0.7f, 0f, 0.15f), 0.5f, 0.7f, 0.6f),
            new OpponentIdentity("Alexander", -0.07f, 0.3f, new Color(0.9f, 0.7f, 0.5f), 0.3f, 0.5f, 0.8f),
            new OpponentIdentity("Lance", 0.0f, 0.6f, new Color(0f, 0.45f, 0.25f), 0.4f, 0.6f, 0.6f),
            new OpponentIdentity("Valtteri", 0.04f, 0.35f, new Color(0.35f, 0.75f, 1f), 0.3f, 0.4f, 0.8f),
            new OpponentIdentity("Rafael", -0.03f, 0.55f, new Color(1f, 0.3f, 0.2f), 0.7f, 0.3f, 0.3f),
            new OpponentIdentity("Nico", 0.06f, 0.45f, new Color(0.75f, 0.55f, 1f), 0.4f, 0.8f, 0.7f),
            new OpponentIdentity("Arvid", -0.06f, 0.65f, new Color(0.85f, 0.85f, 0.3f), 0.6f, 0.4f, 0.4f),
            new OpponentIdentity("Sergio", 0.02f, 0.4f, new Color(0.2f, 0.3f, 0.3f), 0.5f, 0.9f, 0.5f),
            new OpponentIdentity("Gabriel", -0.02f, 0.5f, new Color(1f, 0.6f, 0f), 0.8f, 0.3f, 0.4f),
        };

        private const int RecentLapCount = 20;
        private readonly List<AIDriver> _drivers = new List<AIDriver>();
        private readonly Queue<LapObservation> _recentLaps = new Queue<LapObservation>();

        public IReadOnlyList<AIDriver> Drivers => _drivers;

        public static AIGrid Create(F1TrackGenerator track, F1CarController player, F1CarController template,
                                    int opponents, float difficulty, bool sweep)
        {
            var grid = new GameObject("AIGrid").AddComponent<AIGrid>();
            grid.Setup(track, player, template, opponents, difficulty, sweep);
            return grid;
        }

        private void Setup(F1TrackGenerator track, F1CarController player, F1CarController template, int opponents, float difficulty, bool sweep)
        {
            int count = sweep ? 1 : Mathf.Clamp(opponents, 1, identities.Length);
            if (sweep && player != null) player.gameObject.SetActive(false); // the sweep times one car in clean air

            var field = new List<TrackProgress>();
            if (player != null && player.gameObject.activeSelf) field.Add(player.GetComponent<TrackProgress>());

            var cars = new List<F1CarController>();
            for (int i = 0; i < count; i++)
            {
                F1CarController car = i == 0 ? template : Instantiate(template);
                car.name = "AI_" + identities[i].driverName;
                car.carName = identities[i].driverName;
                CarLivery.Apply(car, identities[i].livery);
                track.GetStartingGridSlot(sweep ? 0 : i + 1, out Vector3 pos, out Quaternion rot);
                var rb = car.GetComponent<Rigidbody>();
                rb.position = pos;
                rb.rotation = rot;
                car.transform.SetPositionAndRotation(pos, rot);
                cars.Add(car);
                field.Add(car.GetComponent<TrackProgress>());
            }

            // One racing line per track, shared by every AI car (same usable width as AIDriver's lane clamp).
            RacingLine line = RacingLine.Build(track.Spline, track.trackWidth * 0.5f - AIDriver.LineEdgeMarginM); // inside the white lines (track limits)
            OpponentIdentity[] racing = PaceProfileMapper.RelativeToBest(identities, count);
            for (int i = 0; i < count; i++)
            {
                var driver = cars[i].gameObject.AddComponent<AIDriver>();
                driver.Init(racing[i], track, field, difficulty, line);
                driver.LapObserved += Remember;
                _drivers.Add(driver);
            }

            if (sweep) gameObject.AddComponent<PaceSweep>().Begin(_drivers[0]);
            else
            {
                float[] learned = TrackLearning.Load(TrackLearning.Key(track.Spline.TotalLength, F1TrackGenerator.StartLineDistance, template.PhysicsSpecKey()));
                int capped = 0;
                if (learned != null) foreach (float c in learned) if (c < 1f) capped++;
                foreach (AIDriver d in _drivers) d.PreloadScale(learned);
                Debug.Log($"[Race] track learning: loaded {capped} capped line points from {TrackLearning.FilePath}");
            }
        }

        private void Remember(LapObservation lap)
        {
            _recentLaps.Enqueue(lap);
            while (_recentLaps.Count > RecentLapCount) _recentLaps.Dequeue();
        }

        private void Update()
        {
            if (GameInput.Down(KeyCode.F8)) Probe(new PythonAIClient.Request { type = "ping" });
            if (GameInput.Down(KeyCode.F7) && _drivers.Count > 0)
                Probe(new PythonAIClient.Request
                {
                    type = "recommend_difficulty",
                    current = _drivers[0].Difficulty,
                    laps = _recentLaps.ToArray(),
                });
        }

        private async void Probe(PythonAIClient.Request request)
        {
            PythonAIClient.Reply reply = await PythonAIClient.Send(request, pythonTimeoutMs);
            // Logged only: nothing is applied until the FairnessGovernor and DifficultyConfigApplier exist.
            Debug.Log(reply == null
                ? $"[AI bridge] {request.type}: no valid reply (service down, timeout or invalid), keeping current setting"
                : $"[AI bridge] {request.type}: {JsonUtility.ToJson(reply)}");
        }
    }
}
