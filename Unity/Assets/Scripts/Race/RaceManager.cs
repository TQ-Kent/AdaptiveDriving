using System.Collections.Generic;
using System.Text;
using F1Racing.AI.Driving;
using F1Racing.Track;
using F1Racing.Vehicle;
using UnityEngine;

namespace F1Racing.Race
{
    public enum RacePhase { Menu, Countdown, Racing, Finished }

    /// <summary>Menu choices. Static so they survive the scene reload that restarts a race.</summary>
    public static class RaceSettings
    {
        public const int Laps = 3;
        public static readonly string[] DifficultyNames = { "Easy", "Normal", "Hard", "Expert" };
        public static readonly float[] DifficultyValues = { 0.25f, 0.5f, 0.8f, 1f };

        public static int DifficultyIndex = 3; // default test setup: 22 AI cars, Expert, collisions on, no human car
        public static int Opponents = MaxOpponents;
        public const int MaxOpponents = 22;
        /// <summary>Off: no human car, an AI-only race to watch (click a tower name to switch car).</summary>
        public static bool HumanPlayer = false;
        /// <summary>Show the AI racing line painted on the track (L key or menu). Visual only.</summary>
        public static bool ShowRacingLine;
        /// <summary>Restart: reload straight into the countdown with the same settings.</summary>
        public static bool SkipMenu;
        /// <summary>Ghost cars: no car-to-car collisions (all cars, human and AI alike). Locked at the start like difficulty.</summary>
        public static bool Ghost = false;
        /// <summary>v1.2 D04 variant for every AI: the lookahead planner, or off for the scripted baseline (to compare). Set at grid build.</summary>
        public static bool LookaheadTactics = true;
        /// <summary>Unnamed physics layer the cars are moved to so their mutual collisions can be switched off.</summary>
        public const int CarLayer = 8;

        public static float Difficulty => DifficultyValues[Mathf.Clamp(DifficultyIndex, 0, DifficultyValues.Length - 1)];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            DifficultyIndex = 3;
            Opponents = MaxOpponents;
            HumanPlayer = false;
            ShowRacingLine = false;
            SkipMenu = false;
            Ghost = false;
        }
    }

    /// <summary>One car in the race and its timing record.</summary>
    public sealed class RaceEntry
    {
        public F1CarController Car;
        public TrackProgress Progress;
        public AIDriver Driver;  // null for a human
        public bool IsHuman => Driver == null;
        public string Name => Car.carName;

        public readonly List<float> MarkerTimes = new List<float>(); // race time at each timing marker passed
        public readonly List<float> LapTimes = new List<float>();
        public readonly List<bool> LapValid = new List<bool>();      // false: the lap had a track-limit strike
        public float LapStartTime;
        public bool Finished;
        public float FinishTime;
        public int FinishLaps;

        // Rules state (RaceRules), the same for human and AI.
        public int Strikes;
        public float PenaltyS;
        public bool CurrentLapValid = true;
        public bool JumpStart;
        public bool BlueFlag, YellowAhead, DrsEligible;
        public float ReactionS;            // AI only: delay after lights out before it moves
        public string Message = "";        // latest steward note for the HUD
        public float MessageTime = -99f;
        internal TrackLimitMonitor Limits;
        internal Vector3 GridPosition;
        internal float SplineM = float.NaN, Lateral, Speed;

        /// <summary>Classified race time: finishing time plus time penalties.</summary>
        public float TotalTime => FinishTime + PenaltyS;

        /// <summary>Best lap without a track-limit strike (NaN if none).</summary>
        public float BestLap
        {
            get
            {
                float best = float.NaN;
                for (int i = 0; i < LapTimes.Count; i++)
                    if (LapValid[i] && (float.IsNaN(best) || LapTimes[i] < best)) best = LapTimes[i];
                return best;
            }
        }

        /// <summary>Best lap including deleted ones (NaN if none): shown, marked, when there is no valid lap.</summary>
        public float BestAnyLap
        {
            get
            {
                float best = float.NaN;
                foreach (float t in LapTimes) if (float.IsNaN(best) || t < best) best = t;
                return best;
            }
        }
    }

    /// <summary>Pure ordering and gap rules, checked in Editor/SeamSelfCheck.</summary>
    public static class RaceStandings
    {
        public const int MarkersPerLap = 20;

        /// <summary>Higher is further ahead. A finished car freezes at the line, so a car still on the lead lap passes a lapped finisher.</summary>
        public static float SortKey(bool finished, int finishLaps, float lapLength, float raceProgress) =>
            finished ? finishLaps * lapLength : raceProgress;

        /// <summary>Time gap (s) at the last timing marker both cars have passed; NaN before the first marker.</summary>
        public static float GapSeconds(IReadOnlyList<float> leaderTimes, IReadOnlyList<float> carTimes)
        {
            int k = carTimes.Count - 1;
            if (k < 0 || k >= leaderTimes.Count) return float.NaN;
            return carTimes[k] - leaderTimes[k];
        }

        public static int LapsBehind(IReadOnlyList<float> leaderTimes, IReadOnlyList<float> carTimes) =>
            Mathf.Max(0, (leaderTimes.Count - carTimes.Count) / MarkersPerLap);

        public static string FormatTime(float s) =>
            float.IsNaN(s) ? "--:--.---" : $"{(int)(s / 60f)}:{s % 60f:00.000}";
    }

    /// <summary>
    /// Race flow: Menu -> 5-light Countdown -> Racing (3 laps) -> Finished. Holds every car with the handbrake
    /// until lights out, then hands control back. Owns no physics and never changes AI difficulty (locked at grid build).
    /// shortcut: marker and lap times use frame time (+-1 frame); move to the fixed-step RaceTimingService when it exists.
    /// </summary>
    public class RaceManager : MonoBehaviour
    {
        private const float FlushIntervalS = 2f, FinishTimeoutS = 60f;

        public RacePhase Phase { get; private set; } = RacePhase.Menu;
        public IReadOnlyList<RaceEntry> Standings => _order;
        public RaceEntry Player { get; private set; }
        public float RaceTime => Phase == RacePhase.Racing || Phase == RacePhase.Finished ? (float)(Time.timeAsDouble - _startTime) : 0f;
        public bool FlagOut { get; private set; }
        /// <summary>Red lights shown during the countdown (0..5).</summary>
        public int LightsOn { get; private set; }
        public float LapLength => _lapLength;
        /// <summary>Car with the fastest valid lap so far (null if none).</summary>
        public RaceEntry FastestLap
        {
            get
            {
                RaceEntry best = null;
                foreach (RaceEntry e in _entries)
                    if (!float.IsNaN(e.BestLap) && (best == null || e.BestLap < best.BestLap)) best = e;
                return best;
            }
        }

        private readonly List<RaceEntry> _entries = new List<RaceEntry>();
        private readonly List<RaceEntry> _order = new List<RaceEntry>();
        private RaceLog _log;
        private double _startTime, _countdownStart, _lightsOutAt;
        private float _flagTime, _nextFlush, _lapLength, _nextHumanSample, _nextInputSample;
        private string _lastOrder = "", _pendingOrder;
        private float _pendingOrderAt;
        private const float OrderSettleS = 1f;
        private readonly Dictionary<RaceEntry, int> _prevPos = new Dictionary<RaceEntry, int>();
        private TrackFlagController _flags;
        private TrackFlagState _flagShown = TrackFlagState.Green;
        private int _lightsShown = -1;
        // Per DRS zone: race time and car of the last crossing of its detection line.
        private readonly float[] _detectTime = new float[RaceRules.Zones.Length];
        private readonly RaceEntry[] _detectCar = new RaceEntry[RaceRules.Zones.Length];

        public static RaceManager Create(IEnumerable<F1CarController> cars, float lapLength)
        {
            var rm = new GameObject("RaceManager").AddComponent<RaceManager>();
            rm._lapLength = lapLength;
            rm._flags = FindAnyObjectByType<TrackFlagController>();
            foreach (F1CarController car in cars)
            {
                var e = new RaceEntry { Car = car, Progress = car.GetComponent<TrackProgress>(), Driver = car.GetComponent<AIDriver>() };
                car.DrsAllowed = false; // only RaceRules opens DRS during a race
                if (e.Driver != null) e.ReactionS = ReactionTime(car.carName);
                rm._entries.Add(e);
                if (e.IsHuman && car.controlMode == ControlMode.Player1_WASD) rm.Player = e;
                e.Progress.LapCompleted += p => rm.OnLap(e);
            }
            rm._order.AddRange(rm._entries);
            foreach (RaceEntry e in rm._entries) // AIGrid places the AI in slots 1..n; humans take slot 0 (and 1 in 2P)
                if (e.IsHuman) PlaceOnGrid(e.Car, e.Car.controlMode == ControlMode.Player1_WASD ? 0 : 1);
            rm.Hold();
            if (RaceSettings.SkipMenu) rm.StartCountdown();
            return rm;
        }

        /// <summary>Menu "Start race": begin the light sequence.</summary>
        public void StartCountdown()
        {
            if (Phase != RacePhase.Menu) return; // idempotent: a double click starts one countdown
            RaceSettings.SkipMenu = false;
            Phase = RacePhase.Countdown;
            _countdownStart = Time.timeAsDouble;
            _lightsOutAt = _countdownStart + 5.0 + Random.Range(0.4f, 1.4f);

            _log = new RaceLog();
            _log.Event($"race settings locked: laps={RaceSettings.Laps} difficulty={RaceSettings.DifficultyNames[RaceSettings.DifficultyIndex]} " +
                       $"({RaceSettings.Difficulty:F2}, manual) cars={_entries.Count} ghost={RaceSettings.Ghost} " +
                       $"tactics={(RaceSettings.LookaheadTactics ? "lookahead" : "scripted")}");
            ApplyGhost(RaceSettings.Ghost);
            string spec = _entries[0].Car.PhysicsSpecKey();
            bool sameSpec = _entries.TrueForAll(e => e.Car.PhysicsSpecKey() == spec);
            _log.Event($"car spec identical for all cars: {(sameSpec ? "yes" : "NO")} ({spec})");
            foreach (RaceEntry e in _entries)
                if (!e.IsHuman)
                    _log.Event($"  {e.Name}: d_i={PaceProfileMapper.Effective(e.Driver.Difficulty, e.Driver.Identity):F2} " +
                               $"grip={e.Driver.Pace.gripUtilization:F2} brake={e.Driver.Pace.brakeDecel:F1} throttle={e.Driver.Pace.throttleDemand:F2} reaction={e.ReactionS:F2}s");

            // Humans sit on the grid under their own control from here: moving before lights out is a jump start.
            foreach (RaceEntry e in _entries)
            {
                e.GridPosition = e.Car.transform.position;
                if (!e.IsHuman) continue;
                e.Car.Command = default;
                e.Car.ExternalControl = false;
            }
        }

        /// <summary>A fixed per-driver start reaction, 0.20-0.35 s (about a human's), so AI starts are repeatable.</summary>
        private static float ReactionTime(string name)
        {
            int h = 17;
            foreach (char c in name) h = h * 31 + c;
            return 0.2f + 0.15f * ((h & 0x7fffffff) % 1000) / 1000f;
        }

        /// <summary>Same rule for every car: all on one layer, and that layer either collides with itself or not.</summary>
        private void ApplyGhost(bool ghost)
        {
            foreach (RaceEntry e in _entries)
            {
                foreach (Transform t in e.Car.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = RaceSettings.CarLayer;
                if (e.Driver != null) e.Driver.Ghost = ghost;
            }
            Physics.IgnoreLayerCollision(RaceSettings.CarLayer, RaceSettings.CarLayer, ghost); // global setting: set both ways every race
        }

        private static void PlaceOnGrid(F1CarController car, int slot)
        {
            if (car.trackReference == null) return;
            car.trackReference.GetStartingGridSlot(slot, out Vector3 pos, out Quaternion rot);
            var rb = car.GetComponent<Rigidbody>();
            rb.linearVelocity = rb.angularVelocity = Vector3.zero;
            rb.position = pos;
            rb.rotation = rot;
            car.transform.SetPositionAndRotation(pos, rot);
        }

        private void Hold()
        {
            foreach (RaceEntry e in _entries)
            {
                if (e.Driver != null) e.Driver.enabled = false;
                e.Car.ExternalControl = true;
                e.Car.Command = new VehicleCommand { Handbrake = true };
            }
        }

        private void LightsOut()
        {
            Phase = RacePhase.Racing;
            _startTime = Time.timeAsDouble;
            foreach (RaceEntry e in _entries)
            {
                e.LapStartTime = 0f;
                if (e.Driver != null) e.Driver.Log = _log; // released after its reaction time (ReleaseAI)
            }
            _log.Event("lights out");
        }

        private void ReleaseAI()
        {
            foreach (RaceEntry e in _entries)
                if (e.Driver != null && !e.Driver.enabled && !e.Finished && RaceTime >= e.ReactionS)
                {
                    e.Driver.enabled = true;
                    e.Driver.BeginRace();
                }
        }

        private void CheckJumpStarts()
        {
            foreach (RaceEntry e in _entries)
            {
                if (!e.IsHuman || e.JumpStart) continue;
                Vector3 moved = e.Car.transform.position - e.GridPosition;
                moved.y = 0f;
                if (moved.magnitude <= RaceRules.JumpStartM) continue;
                e.JumpStart = true;
                e.PenaltyS += RaceRules.JumpStartPenaltyS;
                Note(e, $"JUMP START: +{RaceRules.JumpStartPenaltyS:F0} s penalty");
            }
        }

        private void Note(RaceEntry e, string message)
        {
            e.Message = message;
            e.MessageTime = Time.time;
            _log?.Event($"steward: {e.Name} {message}");
        }

        /// <summary>Track limits, DRS, blue and yellow flags for every running car (RaceRules; same for human and AI).</summary>
        private void ApplyRules()
        {
            float now = RaceTime;
            CatmullRomSpline spline = null;
            foreach (RaceEntry e in _entries)
            {
                spline ??= e.Progress.track.Spline;
                float prevM = e.SplineM;
                e.SplineM = Mathf.Repeat(e.Progress.LapDistance + F1TrackGenerator.StartLineDistance, _lapLength);
                var rb = e.Car.GetComponent<Rigidbody>();
                var here = spline.Samples[spline.FindNearestSampleIndex(rb.position)];
                e.Lateral = Vector3.Dot(rb.position - here.Position, here.Right);
                e.Speed = rb.linearVelocity.magnitude;
                if (e.Finished) { e.Car.DrsAllowed = false; continue; }

                // Track limits: one strike per excursion; the lap is no longer valid for best/fastest lap.
                if (e.Limits.Step(e.Lateral, e.Progress.track.trackWidth * 0.5f))
                {
                    e.Strikes++;
                    e.CurrentLapValid = false;
                    float pen = RaceRules.PenaltyForStrike(e.Strikes);
                    e.PenaltyS += pen;
                    Note(e, pen > 0f ? $"Track limits strike {e.Strikes}: +{pen:F0} s"
                          : e.Strikes == RaceRules.BlackWhiteFlagStrike ? $"Track limits strike {e.Strikes}: BLACK AND WHITE FLAG, next one is +{RaceRules.TrackLimitPenaltyS:F0} s"
                          : $"Track limits strike {e.Strikes} (lap time deleted)");
                    // Where and which side, for tuning: inside = cut the corner, outside = ran wide.
                    _log?.Event($"  {e.Name} off at lap {e.Progress.Laps + 1}, {e.Progress.LapDistance:F0} m, " +
                                $"{(e.Lateral * here.SignedCurvature > 0f ? "inside" : "outside")} ({e.Lateral:F1} m, R {1f / Mathf.Max(1e-4f, Mathf.Abs(here.SignedCurvature)):F0} m, {e.Speed * 3.6f:F0} km/h)");
                }

                // DRS: eligibility is decided at the detection line, used in the zone after it.
                bool allowed = false;
                for (int z = 0; z < RaceRules.Zones.Length; z++)
                {
                    DrsZone zone = RaceRules.Zones[z];
                    if (!float.IsNaN(prevM) && RaceRules.Crossed(prevM, e.SplineM, zone.DetectionM, _lapLength))
                    {
                        float gap = _detectCar[z] != null && _detectCar[z] != e ? now - _detectTime[z] : float.PositiveInfinity;
                        bool eligible = RaceRules.DrsEligible(e.Progress.Laps + 1, gap);
                        if (eligible && !e.DrsEligible) _log.Event($"{e.Name} DRS enabled (zone {z + 1}, {gap:F2} s behind)");
                        e.DrsEligible = eligible;
                        _detectTime[z] = now;
                        _detectCar[z] = e;
                    }
                    if (RaceRules.InZone(zone, e.SplineM, _lapLength)) allowed = e.DrsEligible;
                }
                e.Car.DrsAllowed = allowed;
            }

            // Blue flag: a car a lap or more up is within 1 s behind on the road. Yellow: a stopped car up to 250 m ahead.
            bool anyYellow = false;
            foreach (RaceEntry e in _entries)
            {
                bool blue = false, yellow = false;
                float slip = 0f;
                foreach (RaceEntry o in _entries)
                {
                    if (o == e || o.Finished || float.IsNaN(o.SplineM) || float.IsNaN(e.SplineM)) continue;
                    float behind = Mathf.Repeat(e.SplineM - o.SplineM, _lapLength); // o is this far behind e on track
                    // Slipstream (same rule for every car): o is ahead of e on the road, within 1 s and roughly in line.
                    slip = Mathf.Max(slip, RaceRules.SlipstreamFactor((_lapLength - behind) / Mathf.Max(e.Speed, 10f), Mathf.Abs(o.Lateral - e.Lateral)));
                    if (o.Progress.RaceProgress - e.Progress.RaceProgress > _lapLength * 0.5f && behind < Mathf.Max(o.Speed, 10f) * RaceRules.BlueFlagGapS)
                        blue = true;
                    bool stopped = o.Speed < RaceRules.YellowStoppedMs && now > 8f;
                    if (stopped && _lapLength - behind < RaceRules.YellowWarnAheadM) yellow = true;
                    anyYellow |= stopped;
                }
                if (!e.Finished)
                {
                    if (blue && !e.BlueFlag) Note(e, "BLUE FLAG: let the leaders through");
                    if (yellow && !e.YellowAhead && e.IsHuman) Note(e, "YELLOW FLAG: stopped car ahead");
                }
                e.BlueFlag = blue && !e.Finished;
                e.YellowAhead = yellow && !e.Finished;
                if (e.Driver != null) e.Driver.BlueFlag = e.BlueFlag;
                bool startZone = e.Progress.Laps == 0 && (e.Progress.LapDistance < RaceRules.SlipstreamOffAfterStartM || e.Progress.LapDistance > _lapLength - RaceRules.SlipstreamOffAfterStartM); // grid: behind the line
                e.Car.SlipstreamFactor = e.Finished || startZone ? 0f : slip; // no tow while the grid is still bunched up
            }
            SetTrackFlag(anyYellow ? TrackFlagState.Yellow : TrackFlagState.Green);
        }

        private void SetTrackFlag(TrackFlagState state)
        {
            if (_flags == null || state == _flagShown) return;
            _flagShown = state;
            _flags.SetFlagState(state);
            _log?.Event($"track flag: {state}");
        }

        private void Update()
        {
            if (Phase == RacePhase.Countdown)
            {
                double t = Time.timeAsDouble;
                LightsOn = Mathf.Clamp((int)(t - _countdownStart) + 1, 0, 5);
                CheckJumpStarts();
                if (t >= _lightsOutAt)
                {
                    LightsOn = 0;
                    LightsOut();
                }
            }
            if (_flags != null && LightsOn != _lightsShown)
            {
                _lightsShown = LightsOn;
                _flags.SetStartLights(LightsOn);
            }
            if (Phase == RacePhase.Racing)
            {
                ReleaseAI();
                ApplyRules();
                LogHumans();
                RecordMarkers();
                SortStandings();
                if (FlagOut && (AllFinished() || RaceTime - _flagTime > FinishTimeoutS)) EndRace();
            }
            if (_log != null && Time.unscaledTime >= _nextFlush)
            {
                _nextFlush = Time.unscaledTime + FlushIntervalS;
                _log.Flush();
            }
        }

        /// <summary>
        /// Human rows: 4 Hz in the AI telemetry (to trace contact against AI rows) and 20 Hz in player_input.csv
        /// (line, controls, yaw and slip) for diagnosis. Local file only.
        /// </summary>
        private void LogHumans()
        {
            if (Time.time >= _nextInputSample)
            {
                _nextInputSample = Time.time + 0.05f;
                foreach (RaceEntry e in _entries)
                    if (e.IsHuman && !e.Finished) RecordInput(e);
            }
            if (Time.time < _nextHumanSample) return;
            _nextHumanSample = Time.time + 0.25f;
            foreach (RaceEntry e in _entries)
            {
                if (!e.IsHuman || e.Finished) continue;
                var rb = e.Car.GetComponent<Rigidbody>();
                float speed = Vector3.Dot(rb.linearVelocity, rb.rotation * Vector3.forward);
                VehicleCommand c = e.Car.Command;
                _log.Telemetry(e.Name, e.Progress.Laps + 1, e.Progress.LapDistance, speed, float.PositiveInfinity,
                               c.Throttle, c.Brake, c.Steer, 0f, 0f, float.PositiveInfinity, "human");
            }
        }

        private void RecordInput(RaceEntry e)
        {
            var rb = e.Car.GetComponent<Rigidbody>();
            CatmullRomSpline spline = e.Progress.track.Spline;
            var here = spline.Samples[spline.FindNearestSampleIndex(rb.position)];
            float lateral = Vector3.Dot(rb.position - here.Position, here.Right);
            Vector3 fwd = rb.rotation * Vector3.forward, v = rb.linearVelocity;
            Vector3 flatV = new Vector3(v.x, 0f, v.z);
            float slip = flatV.magnitude > 3f ? Vector3.SignedAngle(new Vector3(fwd.x, 0f, fwd.z), flatV, Vector3.up) : 0f;
            VehicleCommand c = e.Car.Command;
            _log.PlayerInput(e.Name, e.Progress.Laps + 1, e.Progress.LapDistance, lateral, Vector3.Dot(v, fwd), c.Steer, c.Throttle, c.Brake,
                             c.Handbrake, c.Drs, e.Car.CurrentGear, rb.angularVelocity.y * Mathf.Rad2Deg, slip,
                             Mathf.Abs(lateral) <= e.Progress.track.trackWidth * 0.5f);
        }

        private void RecordMarkers()
        {
            if (_lapLength <= 0f) return;
            float step = _lapLength / RaceStandings.MarkersPerLap, now = RaceTime;
            foreach (RaceEntry e in _entries)
            {
                if (e.Finished) continue;
                int marker = Mathf.FloorToInt(e.Progress.RaceProgress / step);
                while (e.MarkerTimes.Count <= marker) e.MarkerTimes.Add(now);
            }
        }

        private void SortStandings()
        {
            _order.Sort((a, b) =>
            {
                float ka = RaceStandings.SortKey(a.Finished, a.FinishLaps, _lapLength, a.Progress.RaceProgress);
                float kb = RaceStandings.SortKey(b.Finished, b.FinishLaps, _lapLength, b.Progress.RaceProgress);
                if (ka != kb) return kb.CompareTo(ka);
                if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
                return a.TotalTime.CompareTo(b.TotalTime); // time penalties count in the classification
            });
            var sb = new StringBuilder();
            foreach (RaceEntry e in _order) sb.Append(e.Name).Append(' ');
            string order = sb.ToString();
            // Race 13: side-by-side cars swapped order every frame (8681 "passes" lines). Log an order only once it holds 1 s.
            if (order == _lastOrder) _pendingOrder = null;
            else if (order != _pendingOrder) { _pendingOrder = order; _pendingOrderAt = Time.time; }
            if (order != _lastOrder && (_lastOrder.Length == 0 || Time.time - _pendingOrderAt >= OrderSettleS))
            {
                if (_lastOrder.Length > 0)
                {
                    _log.Event("order: " + order);
                    // Overtakes, for tuning the corner dives: who passed whom, and where.
                    for (int i = 0; i + 1 < _order.Count; i++)
                    {
                        RaceEntry a = _order[i], b = _order[i + 1];
                        if (_prevPos.TryGetValue(a, out int pa) && _prevPos.TryGetValue(b, out int pb) && pa > pb && !a.Finished)
                            _log.Event($"{a.Name} passes {b.Name} at lap {a.Progress.Laps + 1}, {a.Progress.LapDistance:F0} m, {a.Speed * 3.6f:F0} km/h");
                    }
                }
                _lastOrder = order;
                for (int i = 0; i < _order.Count; i++) _prevPos[_order[i]] = i;
            }
        }

        private void OnLap(RaceEntry e)
        {
            if (Phase != RacePhase.Racing || e.Finished) return;
            float now = RaceTime;
            e.LapTimes.Add(now - e.LapStartTime);
            e.LapValid.Add(e.CurrentLapValid);
            e.LapStartTime = now;
            _log.Event($"{e.Name} completed lap {e.Progress.Laps}: {RaceStandings.FormatTime(e.LapTimes[e.LapTimes.Count - 1])}" +
                       (e.CurrentLapValid ? "" : " (deleted: track limits)"));
            e.CurrentLapValid = true;

            if (!FlagOut && e.Progress.Laps >= RaceSettings.Laps)
            {
                FlagOut = true;
                _flagTime = now;
                _log.Event($"chequered flag: {e.Name} wins in {RaceStandings.FormatTime(now)}");
            }
            if (FlagOut) Finish(e, now); // F1 rule: after the flag every car finishes at its next line crossing
        }

        private void Finish(RaceEntry e, float now)
        {
            e.Finished = true;
            e.FinishTime = now;
            e.Car.DrsAllowed = false;
            e.FinishLaps = e.Progress.Laps;
            if (e.Driver != null) e.Driver.CoolDown = true;
            _log.Event($"{e.Name} finished: {e.FinishLaps} laps, {RaceStandings.FormatTime(now)}");
        }

        private bool AllFinished()
        {
            foreach (RaceEntry e in _entries) if (!e.Finished) return false;
            return true;
        }

        private void EndRace()
        {
            SortStandings();
            Phase = RacePhase.Finished;
            _log.Event("results:");
            for (int i = 0; i < _order.Count; i++)
            {
                RaceEntry e = _order[i];
                _log.Event($"  P{i + 1} {e.Name,-10} {ResultText(e),-12} pts {RaceRules.PointsFor(i + 1),2} pen {e.PenaltyS:F0}s strikes {e.Strikes} " +
                           $"best {BestLapText(e)}{(e == FastestLap ? " FASTEST" : "")} laps {string.Join(" ", e.LapTimes.ConvertAll(t => t.ToString("F2")))}");
            }

            // Keep what the AI learned about this track (median over drivers) for the next race.
            var caps = new List<IReadOnlyList<float>>();
            foreach (RaceEntry e in _entries) if (e.Driver != null) caps.Add(e.Driver.LearnedScale);
            if (caps.Count > 0)
            {
                F1Racing.Track.F1TrackGenerator track = _entries[0].Progress.track;
                string key = TrackLearning.Key(track.Spline.TotalLength, F1Racing.Track.F1TrackGenerator.StartLineDistance, _entries[0].Car.PhysicsSpecKey());
                _log.Event($"track learning: saved {TrackLearning.Save(key, caps)} capped line points");
            }
            _log.Flush();
        }

        /// <summary>Best valid lap; with none, the best deleted lap marked "(del)" (it never counts for fastest lap).</summary>
        public static string BestLapText(RaceEntry e) =>
            !float.IsNaN(e.BestLap) ? RaceStandings.FormatTime(e.BestLap)
            : !float.IsNaN(e.BestAnyLap) ? RaceStandings.FormatTime(e.BestAnyLap) + " (del)" : RaceStandings.FormatTime(float.NaN);

        /// <summary>Tower/results text: "Leader", "+1.234" (at the line), "+1.2" (running, at the last marker) or "+1 lap".</summary>
        public string GapText(RaceEntry e)
        {
            RaceEntry leader = _order[0];
            if (e == leader) return "Leader";
            if (e.Finished && leader.Finished && e.FinishLaps == leader.FinishLaps) return $"+{e.TotalTime - leader.TotalTime:F3}";
            int lapsDown = e.Finished ? leader.FinishLaps - e.FinishLaps : RaceStandings.LapsBehind(leader.MarkerTimes, e.MarkerTimes);
            if (lapsDown > 0) return $"+{lapsDown} lap{(lapsDown > 1 ? "s" : "")}";
            float gap = RaceStandings.GapSeconds(leader.MarkerTimes, e.MarkerTimes);
            return float.IsNaN(gap) ? "--" : $"+{gap:F1}";
        }

        public string ResultText(RaceEntry e) =>
            e == _order[0] && e.Finished ? RaceStandings.FormatTime(e.TotalTime) : GapText(e);

        public int PositionOf(RaceEntry e) => _order.IndexOf(e) + 1;

        private void OnDestroy() => _log?.Flush();
    }
}
