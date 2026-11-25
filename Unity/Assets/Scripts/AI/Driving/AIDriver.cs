using System;
using System.Collections.Generic;
using F1Racing.Race;
using F1Racing.Track;
using F1Racing.Vehicle;
using UnityEngine;

namespace F1Racing.AI.Driving
{
    /// <summary>One completed AI lap. Only clean laps are pace evidence; flagged laps are kept, not dropped (spec 4.4).</summary>
    [Serializable]
    public struct LapObservation
    {
        public string observationId;
        public string carId;
        public int lapIndex;
        public float lapTimeS;
        public float difficulty;      // session difficulty in effect (before the identity offset)
        public bool mixedDifficulty;  // difficulty changed during the lap
        public bool incident;         // recovery reset during the lap
        public bool standingStart;    // first lap from the grid

        public bool IsClean => !mixedDifficulty && !incident && !standingStart;
    }

    /// <summary>
    /// D01: conventional AI driver for one car. Every physics tick: recovery, then tactics offset (10 Hz),
    /// path follower, pace controller, and the safety filter last, then writes <see cref="F1CarController.Command"/>.
    /// It drives the same car physics as the player; difficulty only changes <see cref="PaceSettings"/>.
    /// </summary>
    [RequireComponent(typeof(F1CarController), typeof(TrackProgress))]
    public class AIDriver : MonoBehaviour
    {
        private const float TacticsIntervalS = 0.1f, OvertakeOffsetM = 3.5f, LaneEdgeMarginM = 2f, HighSpeedClearancePerMs = 0.02f, OffsetSlewMs = 2f, BrakePreviewS = 0.3f;
        // LookBehindM 15: race 1 had a car move over into a lane a faster car was closing in from 7-8 m behind.
        private const float LaneHalfWidthM = 2.5f, LookBehindM = 15f, LookAheadM = 40f, TelemetryIntervalS = 0.25f, StartLaneHoldS = 3f;
        private const float SlowCornerMs = 30f, MinCapSpeedMs = 8f;
        // Understeer (race 4: braking at full lock dumped speed to 7-19 m/s where the player carried 30-40 by lifting):
        // lift at any excess; brake lightly (max 0.3) only past a 0.15 dead band; learn after 0.5 s past the dead band.
        // Each clean-air lap every trim recovers a little, so the AI keeps probing back toward the plan.
        private const float UndersteerReleaseDemand = 0.9f, SteerLockMargin = 0.85f;
        /// <summary>Track limits (same rule as the player): the racing line stays this far inside the white line.</summary>
        public const float LineEdgeMarginM = 2f; // race 9: 1.5 m left too little slack, 439 strikes
        private const float WhiteLineGuardM = 0.5f, GuardPullM = 2f;
        private bool _wasAtLimit;
        private const float UndersteerDeadBand = 0.15f, UndersteerBrakeGain = 1f, UndersteerMaxBrake = 0.3f, UndersteerLearnS = 0.5f;
        private const float LearnIntervalS = 0.5f, UndersteerTrim = 0.01f, CrashTrim = 0.04f, LapRecovery = 0.01f;

        public OpponentIdentity Identity { get; private set; }
        /// <summary>Session difficulty 0..1 currently applied (before the identity offset).</summary>
        public float Difficulty { get; private set; }
        public PaceSettings Pace { get; private set; }
        public event Action<LapObservation> LapObserved;
        /// <summary>Optional debug log (set by the RaceManager); null disables telemetry rows.</summary>
        public RaceLog Log { get; set; }
        /// <summary>After the chequered flag: drive on at reduced throttle.</summary>
        public bool CoolDown { get; set; }
        /// <summary>Ghost race: cars pass through each other, so no car-ahead braking and no overtaking lanes; stay on the line.</summary>
        public bool Ghost { get; set; }
        /// <summary>Race rules: a car a lap up is close behind. Move off the racing line and ease off a little to let it by.</summary>
        public bool BlueFlag { get; set; }

        private F1CarController _car;
        private Rigidbody _rb;
        private TrackProgress _progress;
        private F1TrackGenerator _track;
        private IReadOnlyList<TrackProgress> _field;
        private SpeedProfile _profile;
        private RacingLine _line;
        private float[] _steerCaps;
        private CornerMemory _corners;
        private float _understeerS, _nextLearn;
        private readonly TacticalDriver _tactics = new TacticalDriver();
        private readonly RecoveryController _recovery = new RecoveryController();
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private float _offset, _targetSide, _nextTactics, _nextTelemetry;
        private string _pendingEvent = "";
        private double _lapStart;
        private bool _standingStart = true, _lapIncident, _lapMixed, _lifting;

        public void Init(OpponentIdentity identity, F1TrackGenerator track, IReadOnlyList<TrackProgress> field, float difficulty, RacingLine line)
        {
            Identity = identity;
            _track = track;
            _field = field;
            _line = line;
            _corners = new CornerMemory(line.Distances.Count, track.Spline.TotalLength);
            _car = GetComponent<F1CarController>();
            _rb = GetComponent<Rigidbody>();
            _progress = GetComponent<TrackProgress>();
            _car.ExternalControl = true;
            _progress.LapCompleted += OnLapCompleted;
            if (!SetDifficulty(difficulty)) SetDifficulty(0.5f);
            _lapMixed = false;
            _lapStart = Time.timeAsDouble;
        }

        /// <summary>
        /// The adaptation write path. Only the DifficultyConfigApplier (after FairnessGovernor approval) may call
        /// this; until those exist, AIGrid sets the labelled default and the pace sweep sets calibration levels.
        /// Changes driving targets only, never Rigidbody, engine, grip or rules. Rejects non-finite or out-of-range values.
        /// </summary>
        public bool SetDifficulty(float sessionDifficulty)
        {
            if (!float.IsFinite(sessionDifficulty) || sessionDifficulty < 0f || sessionDifficulty > 1f) return false;
            Difficulty = sessionDifficulty;
            Pace = PaceProfileMapper.Map(sessionDifficulty, Identity);
            _profile = BuildProfile(Pace);
            _lapMixed = true;
            return true;
        }

        /// <summary>Lights out: start the lap clock and keep the grid lane for a few seconds so the field doesn't merge at once.</summary>
        public void BeginRace()
        {
            var here = _track.Spline.Samples[_track.Spline.FindNearestSampleIndex(_rb.position)];
            float lateral = Vector3.Dot(_rb.position - here.Position, here.Right);
            float side = Mathf.Abs(lateral) > 1f ? Mathf.Sign(lateral) : 0f;
            _offset = lateral - _line.OffsetAt(here.Distance); // overtake offset is measured from the racing line
            _targetSide = side;
            _tactics.HoldLane(side, Time.time + StartLaneHoldS);
            _nextTactics = Time.time + StartLaneHoldS;
            _lapStart = Time.timeAsDouble;
            _standingStart = true;
            _lapIncident = false;
        }

        /// <summary>
        /// Highest speed (m/s) at each line point where following the aimed path needs at most 85% of the car's
        /// steering lock at that speed (the same speed-sensitive lock the player has). Race 7: plans of 78-98 m/s on
        /// a fast right-hander needed full lock, so the cars ran wide into the barrier. Path fixed per race, so cached.
        /// </summary>
        private float[] SteeringSpeedCaps(float vMax)
        {
            if (_steerCaps != null) return _steerCaps;
            int n = _line.Distances.Count;
            IReadOnlyList<float> k = _line.Curvatures;
            _steerCaps = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f; // mean |k| over +-3 points (~35 m): smooths sample-level curvature noise
                for (int j = -3; j <= 3; j++) sum += Mathf.Abs(k[(i + j + n) % n]);
                float needDeg = Mathf.Atan(_car.Wheelbase * sum / 7f) * Mathf.Rad2Deg;
                float v = vMax;
                while (v > MinCapSpeedMs && needDeg > SteerLockMargin * _car.MaxSteerDegAt(v)) v -= 1f;
                _steerCaps[i] = v;
            }
            return _steerCaps;
        }

        private SpeedProfile BuildProfile(PaceSettings pace)
        {
            float vMax = _car.TopSpeedMs(true);
            float[] caps = SteeringSpeedCaps(vMax);
            // Corner speeds come from the racing line's curvature (wider radius than the centre line), indexed by spline distance.
            // Same lateral grip model the car's steering limit uses (F1CarController.ApplySteering), scaled by utilization.
            float g = Physics.gravity.magnitude;
            float mu = _car.lateralGripG * pace.gripUtilization;
            return SpeedProfile.Build(_line.Distances, _line.Curvatures, _track.Spline.TotalLength, vMax,
                                      mu * g, mu * _car.downforceCoefficient / _rb.mass, pace.brakeDecel, _corners.Trim, caps);
        }

        private void FixedUpdate()
        {
            if (_profile == null) return;
            float dt = Time.fixedDeltaTime;
            CatmullRomSpline spline = _track.Spline;
            var here = spline.Samples[spline.FindNearestSampleIndex(_rb.position)];
            Vector3 fwd = _rb.rotation * Vector3.forward;
            float speed = Vector3.Dot(_rb.linearVelocity, fwd);
            float lateral = Vector3.Dot(_rb.position - here.Position, here.Right);
            // Track limits apply to the AI as to the player: lanes stay inside the white line. The run-off is only for
            // recovering from a mistake, and only a car at the barrier is reset as off track.
            float trackHalf = _track.trackWidth * 0.5f;
            float barrierHalf = trackHalf + _track.barrierOffset;

            // Sense first: a car waiting behind traffic is blocked, not stuck (no false reset).
            float gapS = float.PositiveInfinity, closing = 0f;
            if (!Ghost) DrivingSafetyFilter.Sense(_rb, _hits, speed, out gapS, out closing);
            float upY = (_rb.rotation * Vector3.up).y;
            if (_recovery.Tick(dt, _progress.RaceProgress, Vector3.Dot(fwd, here.Tangent), Mathf.Abs(lateral), barrierHalf - 1.5f, upY, gapS < 1f, out string reason))
            {
                _car.ResetToTrack();
                _lapIncident = true;
                _nextLearn = 0f; // a crash always counts, even right after an understeer lesson
                if (gapS >= 1f) Learn(here.Distance, CrashTrim); // crashed in clear road: this corner was taken too fast
                _offset = _targetSide = 0f;
                _pendingEvent += "reset:" + reason + " ";
                Log?.Event($"{Identity.driverName} reset ({reason}) at lap {_progress.Laps + 1}, {_progress.LapDistance:F0} m");
                return;
            }

            if (Time.time >= _nextTactics)
            {
                _nextTactics = Time.time + TacticsIntervalS;
                float side = Ghost ? 0f : DecideSide();
                if (side != _targetSide) _pendingEvent += "side:" + side.ToString("F0") + " ";
                _targetSide = side;
            }
            float maxOffset = trackHalf - LaneEdgeMarginM;
            float laneTarget = InsideOfSlowCorner(here.Distance, speed, _targetSide) ? 0f : _targetSide * OvertakeOffsetM;
            if (BlueFlag) laneTarget = (_line.OffsetAt(here.Distance) > 0f ? -1f : 1f) * OvertakeOffsetM; // the side away from the line
            _offset = Mathf.Clamp(Mathf.MoveTowards(_offset, laneTarget, OffsetSlewMs * dt), -maxOffset, maxOffset);

            float lookAhead = Mathf.Clamp(0.5f * speed, 8f, 45f);
            var aim = spline.GetSampleAtDistance(here.Distance + lookAhead);
            // Race 6: cars ran wide at ~80 m/s on fast bends. Keep 0.02 m extra per m/s from the edge (80 m/s -> 1.6 m more).
            float aimLimit = Mathf.Max(0f, maxOffset - HighSpeedClearancePerMs * speed);
            float aimLateral = Mathf.Clamp(_line.OffsetAt(aim.Distance) + _offset, -aimLimit, aimLimit);
            // Track limits guard: near the white line and still drifting out (either side: cut apex or wide exit),
            // aim back inside, lift, and remember the corner so next lap is slower there. Same limits as the player.
            float outSide = Mathf.Sign(lateral);
            bool atLimit = Mathf.Abs(lateral) > trackHalf - WhiteLineGuardM && Vector3.Dot(_rb.linearVelocity, here.Right) * outSide > 0f;
            if (atLimit) aimLateral = outSide * Mathf.Min(outSide * aimLateral, Mathf.Abs(lateral) - GuardPullM);
            Vector3 local = Quaternion.Inverse(_rb.rotation) * (aim.Position + aim.Right * aimLateral - _rb.position);
            float demand = PathFollower.SteerDemand(local, _car.Wheelbase, _car.CurrentMaxSteerDeg);
            float steer = Mathf.Clamp(demand, -1f, 1f);

            float target = Mathf.Min(_profile.TargetAt(here.Distance), _profile.TargetAt(here.Distance + speed * BrakePreviewS));
            PaceController.Command(speed, target, Pace, out float throttle, out float brake);
            // Understeer: the line needs more lock than the car has at this speed, so it is running wide toward the wall.
            // Lift and brake now (weight onto the front tyres), and remember the corner so next lap brakes before it.
            // Race 7: the lift ended as soon as demand dipped under 1, the car went back to full throttle at 0.99 lock and
            // ran into the barrier. Hysteresis: once lifting, stay off the throttle until demand is back under 0.9.
            float excess = Mathf.Abs(demand) - 1f;
            if (excess > 0f && speed > 8f) _lifting = true;
            else if (Mathf.Abs(demand) < UndersteerReleaseDemand || speed <= 8f) _lifting = false;
            if (_lifting) throttle = 0f; // lift first, like the player does
            if (excess > 0f && speed > 8f)
            {
                float hard = excess - UndersteerDeadBand;
                if (hard > 0f)
                {
                    brake = Mathf.Max(brake, Mathf.Min(UndersteerMaxBrake, hard * UndersteerBrakeGain));
                    _understeerS += dt;
                    if (_understeerS >= UndersteerLearnS) Learn(here.Distance, UndersteerTrim);
                }
            }
            else _understeerS = 0f;
            if (atLimit && speed > 8f)
            {
                throttle = 0f;
                if (!_wasAtLimit) _pendingEvent += "limit ";
                Learn(here.Distance, UndersteerTrim);
            }
            _wasAtLimit = atLimit;
            if (CoolDown) throttle = Mathf.Min(throttle, 0.5f);
            if (BlueFlag) throttle = Mathf.Min(throttle, 0.85f);
            var cmd = new VehicleCommand
            {
                Steer = steer,
                Throttle = throttle,
                Brake = brake,
                Drs = !CoolDown && throttle >= Pace.throttleDemand && Mathf.Abs(steer) < 0.1f, // same DRS rule as the player's car
            };

            // Safety runs last: it overrides tactics and pace.
            VehicleCommand final = DrivingSafetyFilter.Apply(cmd, speed, gapS, closing);
            _car.Command = final;

            if (Log != null && Time.time >= _nextTelemetry)
            {
                _nextTelemetry = Time.time + TelemetryIntervalS;
                Log.Telemetry(Identity.driverName, _progress.Laps + 1, _progress.LapDistance, speed, target,
                              final.Throttle, final.Brake, final.Steer, _offset, _targetSide, gapS, _pendingEvent.Trim());
                _pendingEvent = "";
            }
        }

        private void Learn(float distance, float amount)
        {
            if (Time.time < _nextLearn || !_corners.Learn(distance, amount)) return;
            _nextLearn = Time.time + LearnIntervalS;
            _profile = BuildProfile(Pace); // ~300 points, cheap
            _pendingEvent += $"learn:{Mathf.Repeat(distance - F1TrackGenerator.StartLineDistance, _track.Spline.TotalLength):F0} ";
        }

        /// <summary>
        /// The speed plan is for the centre line; the inside lane of a slow corner is much tighter (race 1: the car on it
        /// reached full lock and went off). True when <paramref name="side"/> is the inside of a slow corner within the
        /// next ~2 s, i.e. while there is still time to slew back to the centre. The outside lane stays allowed.
        /// </summary>
        private bool InsideOfSlowCorner(float distance, float speed, float side)
        {
            if (side == 0f) return false;
            for (float t = 0.5f; t <= 2f; t += 0.5f)
            {
                float d = distance + Mathf.Max(10f, speed * t);
                // Positive curvature turns toward +Right (SignedAngle about up), so the inside lane has the curvature's sign.
                if (_profile.TargetAt(d) < SlowCornerMs && side * _track.Spline.GetSampleAtDistance(d).SignedCurvature > 0f) return true;
            }
            return false;
        }

        /// <summary>
        /// Lane choice from every car near us: the nearest car ahead sets the gap and the preferred (opposite) side;
        /// any car alongside or ahead within range blocks the lane it is in.
        /// </summary>
        private float DecideSide()
        {
            CatmullRomSpline spline = _track.Spline;
            float length = spline.TotalLength;
            float bestGap = float.MaxValue, preferred = 0f;
            bool leftFree = true, rightFree = true;
            foreach (TrackProgress other in _field)
            {
                if (other == null || other == _progress || !other.isActiveAndEnabled) continue;
                float gap = Mathf.Repeat(other.LapDistance - _progress.LapDistance, length);
                if (gap > length * 0.5f) gap -= length; // signed: negative = behind us
                if (gap < -LookBehindM || gap > LookAheadM) continue;
                var s = spline.GetSampleAtDistance(other.LapDistance + F1TrackGenerator.StartLineDistance);
                float lat = Vector3.Dot(other.transform.position - s.Position, s.Right) - _line.OffsetAt(s.Distance); // lanes sit either side of the line
                if (Mathf.Abs(lat + OvertakeOffsetM) < LaneHalfWidthM) leftFree = false;
                if (Mathf.Abs(lat - OvertakeOffsetM) < LaneHalfWidthM) rightFree = false;
                if (gap > 0f && gap < bestGap)
                {
                    bestGap = gap;
                    preferred = lat > 0f ? -1f : 1f;
                }
            }
            float current = Mathf.Abs(_offset) > OvertakeOffsetM * 0.5f ? Mathf.Sign(_offset) : 0f;
            return _tactics.Decide(Time.time, bestGap, leftFree, rightFree, preferred, current, Identity.aggression);
        }

        // shortcut: lap time uses TrackProgress's frame-time crossing (+-1 frame); move to the fixed-step RaceTimingService when it exists.
        private void OnLapCompleted(TrackProgress p)
        {
            if (_corners.Recover(LapRecovery)) _profile = BuildProfile(Pace);
            double now = Time.timeAsDouble;
            var obs = new LapObservation
            {
                observationId = $"{Identity.driverName}:{p.Laps}",
                carId = Identity.driverName,
                lapIndex = p.Laps,
                lapTimeS = (float)(now - _lapStart),
                difficulty = Difficulty,
                mixedDifficulty = _lapMixed,
                incident = _lapIncident,
                standingStart = _standingStart,
            };
            _lapStart = now;
            _lapMixed = _lapIncident = _standingStart = false;
            Log?.Event($"{obs.carId} lap {obs.lapIndex}: {obs.lapTimeS:F3}s clean={obs.IsClean} incident={obs.incident} d={obs.difficulty:F2}");
            LapObserved?.Invoke(obs);
        }

        private void OnDestroy()
        {
            if (_progress != null) _progress.LapCompleted -= OnLapCompleted;
        }
    }
}
