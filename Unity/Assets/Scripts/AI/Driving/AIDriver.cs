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
        public const float LineEdgeMarginM = 2.5f; // race 9: 1.5 m left too little slack; race 11: 2-3 m overshoot in fast S-bends
        private const float WhiteLineGuardM = 1.5f, GuardPullM = 1.5f, GuardOffPathM = 1f;
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
        /// <summary>The racing line this car aims along (shared by every AI car).</summary>
        public RacingLine Line => _line;
        /// <summary>Planned speed (m/s) at a spline distance; 0 before the profile exists.</summary>
        public float TargetSpeedAt(float splineDistance) => _profile != null ? _profile.TargetAt(splineDistance) : 0f;

        private F1CarController _car;
        private Rigidbody _rb;
        private TrackProgress _progress;
        private F1TrackGenerator _track;
        private IReadOnlyList<TrackProgress> _field;
        private SpeedProfile _profile; // the racing line's plan (= _plans[1])
        private RacingLine _line;
        private CornerMemory _corners;
        // Lanes by index side + 1 (-1 left, 0 the line, +1 right), each with its own curvature, so a car in the inside lane plans
        // that lane's tighter corner speed. Late plans brake later (attacks). Geometry and steering caps are fixed per race.
        private readonly IReadOnlyList<float>[] _laneCurv = new IReadOnlyList<float>[3];
        private readonly float[][] _laneStretch = new float[3][], _laneSteerCaps = new float[3][];
        private float _steerLockShare = -1f; // lock share the cached steering caps were built with
        private readonly SpeedProfile[] _plans = new SpeedProfile[3], _latePlans = new SpeedProfile[3];
        // Attack braking: up to 16% later than the plan by style (Max 0.9 -> x1.14 = 32 m/s^2 at Expert, race 23 measured 31-44).
        private const float LateBrakeMax = 0.16f;
        // Provisional: race 12 paths showed ~90 m/s^2 achieved at 80-90 m/s against 99 planned; medium speed matched the plan.
        private const float AeroPlanFactor = 0.85f;
        private float _diveUntil, _diveSide;
        private bool _sBend, _mergeBlocked;
        private float _sideLockedUntil;
        private const float MinSideDwellS = 1f; // provisional
        // Race 31: lanes are 3.5 m apart but block within 2.5 m, so the car being attacked (mostly 0-2.5 m off the line) also
        // blocked the lane beside it: 741 of 1400 "no room" rejections had no other car near. For that car only, a lane is
        // blocked when its centre is within about a car width (provisional: ~2 m car + 0.2 m).
        private const float TargetLaneHalfWidthM = 2.2f;
        private const float MergeClearM = 8f;
        private const float ExitLimitRiseMs = 5f; // provisional
        private const float LapOneCalmM = 700f; // race distance: no attacks or defences until the pack is past the first corners // a car on the line less than this far ahead blocks a merge onto it
        private const float CarLengthM = 5f;
        // Race 15: about 70% of the strikes at the 1700 m S-bend came from cars changing lane or diving in it (lane rows
        // missed the path by 4.2 m on average against 2.7 m on the line). In a fast S-bend the car stays on the racing line.
        // Provisional: curvature both ways above 1/250 m within the next 2 s, from 45 m/s.
        private const float SBendMinSpeedMs = 45f, SBendLookS = 2f, SBendCurvature = 0.004f;
        private const float DiveHoldS = 3f;
        private const float LimitCapScale = 0.96f, CapMinPlanShare = 0.6f, DiveCornerAheadM = 150f, DiveDropMs = 8f, DiveClosingMs = 1f;
        private float _understeerS, _nextLearn;
        // v1.2 D04: the scripted baseline; other tactical variants plug in behind the same interface.
        private readonly ITacticalDriver _tactics = new ScriptedTacticalDriver();
        // v1.2 encounter log for corner dives: target, start, back-out reason, outcome, time the safety filter overrode.
        private TrackProgress _diveTarget;
        private int _encounters;
        private float _diveOverrideS;
        private float _declinedExitM = float.MaxValue; // corner of the last logged declined attack (not NaN: NaN compares false)
        private bool _bypassDwell; // a back-out or a new committed maneuver changes lane at once, without the dwell
        private string _encounterWhat;
        // v1.2 D04 variant: the lookahead planner, or null for the scripted baseline (menu: AI tactics).
        private LookaheadTacticalDriver _planner;
        private readonly RecoveryController _recovery = new RecoveryController();
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private float _offset, _targetSide, _nextTactics, _nextTelemetry;
        private string _pendingEvent = "";
        private double _lapStart;
        private bool _standingStart = true, _lapIncident, _lapMixed, _lifting;

        public void Init(OpponentIdentity identity, F1TrackGenerator track, IReadOnlyList<TrackProgress> field, float difficulty, RacingLine line)
        {
            Identity = identity;
            _planner = RaceSettings.LookaheadTactics ? new LookaheadTacticalDriver(identity) : null;
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
        private float[] SteeringSpeedCaps(float vMax, int lane, float lockShare)
        {
            if (_laneSteerCaps[lane] != null && _steerLockShare == lockShare) return _laneSteerCaps[lane];
            IReadOnlyList<float> k = _laneCurv[lane];
            int n = k.Count;
            var caps = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f; // mean |k| over +-3 points (~35 m): smooths sample-level curvature noise
                for (int j = -3; j <= 3; j++) sum += Mathf.Abs(k[(i + j + n) % n]);
                float needDeg = Mathf.Atan(_car.Wheelbase * sum / 7f) * Mathf.Rad2Deg;
                float v = vMax;
                while (v > MinCapSpeedMs && needDeg > lockShare * _car.MaxSteerDegAt(v)) v -= 1f;
                caps[i] = v;
            }
            return _laneSteerCaps[lane] = caps;
        }

        private void EnsureLanes()
        {
            if (_laneCurv[1] != null) return;
            float maxOffset = _track.trackWidth * 0.5f - LaneEdgeMarginM;
            _laneCurv[0] = _line.LaneCurvatures(-OvertakeOffsetM, maxOffset, out _laneStretch[0]);
            _laneCurv[2] = _line.LaneCurvatures(OvertakeOffsetM, maxOffset, out _laneStretch[2]);
            _line.LaneCurvatures(0f, maxOffset, out _laneStretch[1]); // the line's own length; its curvature stays unsmoothed
            _laneCurv[1] = _line.Curvatures;
        }

        /// <summary>Lane index (0 left, 1 line, 2 right) of an offset from the racing line.</summary>
        private static int LaneOf(float offsetM) => Mathf.Abs(offsetM) > OvertakeOffsetM * 0.5f ? (offsetM < 0f ? 0 : 2) : 1;

        private SpeedProfile BuildProfile(PaceSettings pace)
        {
            EnsureLanes();
            float vMax = _car.TopSpeedMs(true);
            // Corner speeds come from each lane's curvature (the racing line has a wider radius than the centre line), indexed by spline distance.
            // Same lateral grip model the car's steering limit uses (F1CarController.ApplySteering), scaled by utilization.
            float g = Physics.gravity.magnitude;
            float mu = _car.lateralGripG * pace.gripUtilization;
            float aero = mu * _car.downforceCoefficient / _rb.mass * AeroPlanFactor;
            float late = pace.brakeDecel * (1f + LateBrakeMax * Identity.lateBraking);
            // Race 35: the shared lock cap made long corners (Lesmo, Ascari, Parabolica) the same speed for every skill, so
            // laps spread only 0.5 s over d_i 0.86-1.00. A driver dares the same share of the lock as of the grip (controller
            // setting only; the car's lock is unchanged). Top driver keeps SteerLockMargin.
            float lockShare = SteerLockMargin * Mathf.Min(1f, pace.gripUtilization / PaceProfileMapper.Fastest.gripUtilization);
            for (int lane = 0; lane < 3; lane++)
            {
                float[] caps = SteeringSpeedCaps(vMax, lane, lockShare);
                _plans[lane] = SpeedProfile.Build(_line.Distances, _laneCurv[lane], _track.Spline.TotalLength, vMax,
                                                  mu * g, aero, pace.brakeDecel, _corners.Scale, caps);
                _latePlans[lane] = SpeedProfile.Build(_line.Distances, _laneCurv[lane], _track.Spline.TotalLength, vMax,
                                                      mu * g, aero, late, _corners.Scale, caps);
            }
            _steerLockShare = lockShare;
            return _plans[1];
        }

        /// <summary>Planned speed (m/s) at a spline distance in the lane this car is in now (lookahead forecasts of other cars).</summary>
        public float LanePlanAt(float splineDistance) => _profile != null ? _plans[LaneOf(_offset)].TargetAt(splineDistance) : 0f;

        /// <summary>Learned share of the corner limit per line point (1 = none), saved per track by TrackLearning.</summary>
        public IReadOnlyList<float> LearnedScale => _corners.Scale;

        /// <summary>Start from shares learned in earlier races on this track.</summary>
        public void PreloadScale(IReadOnlyList<float> scale)
        {
            _corners.Preload(scale);
            _profile = BuildProfile(Pace);
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
                if (_diveTarget != null) EndDive("reset");
                _offset = _targetSide = 0f;
                _pendingEvent += "reset:" + reason + " ";
                Log?.Event($"{Identity.driverName} reset ({reason}) at lap {_progress.Laps + 1}, {_progress.LapDistance:F0} m");
                return;
            }

            if (Time.time >= _nextTactics)
            {
                _nextTactics = Time.time + TacticsIntervalS;
                bool sBend = InFastSBend(here.Distance, speed);
                if (sBend && !_sBend) _pendingEvent += "sbend ";
                _sBend = sBend;
                float side = _planner != null ? DecideLookahead(speed) : DecideSide(speed); // ghost too: lanes and dives are visible racecraft
                // Race 18: in traffic lanes opened and closed every tick; 430 of 912 lane changes reversed within 1 s, weaving
                // cars into each other at T1, Roggia and Lesmo. Keep a chosen lane at least MinSideDwellS (a dive still commits).
                bool dove = Time.time < _diveUntil && side == _diveSide && side != _targetSide;
                bool backOut = _bypassDwell; // the v1.2 abort path must not wait for the dwell
                _bypassDwell = false;
                if (side != _targetSide && (dove || backOut || Time.time >= _sideLockedUntil))
                {
                    _pendingEvent += "side:" + side.ToString("F0") + " ";
                    _targetSide = side;
                    _sideLockedUntil = Time.time + MinSideDwellS;
                }
            }
            float maxOffset = trackHalf - LaneEdgeMarginM;
            bool diving = !_sBend && Time.time < _diveUntil && _targetSide == _diveSide;
            // The lookahead plans each lane at its own corner speed, so it may keep the inside of a slow corner (to defend or attack).
            float laneTarget = _sBend || (_planner == null && !diving && InsideOfSlowCorner(here.Distance, speed, _targetSide)) ? 0f : _targetSide * OvertakeOffsetM;
            if (BlueFlag) laneTarget = (_line.OffsetAt(here.Distance) > 0f ? -1f : 1f) * OvertakeOffsetM; // the side away from the line
            // Merging back onto the line beside a car slightly ahead on it: the car behind gives way (holds its lane, lifts).
            bool yielding = !Ghost && _mergeBlocked && Mathf.Abs(laneTarget) < Mathf.Abs(_offset) - 1f;
            if (yielding) laneTarget = _offset;
            _offset = Mathf.Clamp(Mathf.MoveTowards(_offset, laneTarget, OffsetSlewMs * dt), -maxOffset, maxOffset);

            // Track limits guard (same white line as the player): only when the car is already off its own path toward the
            // edge, close to the line. Race 11: predicting from raw sideways speed fired whenever the line itself swept across
            // the track in fast S-bends (30-77% of the time); lifting and halving look-ahead at 75 m/s spun cars into the wall.
            float outSide = Mathf.Sign(lateral);
            float pathHere = Mathf.Clamp(_line.OffsetAt(here.Distance) + _offset, -maxOffset, maxOffset);
            bool atLimit = AtTrackLimit(lateral, pathHere, Vector3.Dot(_rb.linearVelocity, here.Right), speed, trackHalf);

            // Race 26: a 0.35 s look-ahead in fast S-bends left the Ascari path error unchanged (~5 m) and saturated steering more; reverted.
            float lookAhead = Mathf.Clamp(0.5f * speed, 8f, 45f);
            var aim = spline.GetSampleAtDistance(here.Distance + lookAhead);
            // Race 6: cars ran wide at ~80 m/s on fast bends. Keep 0.02 m extra per m/s from the edge (80 m/s -> 1.6 m more).
            float aimLimit = Mathf.Max(0f, maxOffset - HighSpeedClearancePerMs * speed);
            float aimLateral = Mathf.Clamp(_line.OffsetAt(aim.Distance) + _offset, -aimLimit, aimLimit);
            if (atLimit) aimLateral = outSide * Mathf.Min(outSide * aimLateral, Mathf.Abs(lateral) - GuardPullM);
            // Slip angle, telemetry only. Race 14: aiming from the travel direction instead of the nose lagged and made cars weave.
            float slipDeg = speed > 10f ? Vector3.SignedAngle(Vector3.ProjectOnPlane(transform.forward, Vector3.up),
                                                              Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up), Vector3.up) : 0f;
            Vector3 local = Quaternion.Inverse(_rb.rotation) * (aim.Position + aim.Right * aimLateral - _rb.position);
            float demand = PathFollower.SteerDemand(local, _car.Wheelbase, _car.CurrentMaxSteerDeg);
            float steer = Mathf.Clamp(demand, -1f, 1f);

            // The lane we are in and the lane we are moving to both bind (race 1: the inside lane of a slow corner is tighter).
            SpeedProfile[] plans = diving ? _latePlans : _plans; // diving: brake later on the inside
            SpeedProfile inLane = plans[LaneOf(_offset)], toLane = plans[LaneOf(laneTarget)];
            float preview = here.Distance + speed * BrakePreviewS;
            float target = Mathf.Min(Mathf.Min(inLane.TargetAt(here.Distance), inLane.TargetAt(preview)), toLane.TargetAt(preview));
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
            if (atLimit)
            {
                throttle = 0f;
                if (!_wasAtLimit) _pendingEvent += "limit ";
                // Next lap: slower than the speed that went off here. Only near the plan: a car already slowed by the
                // excursion would teach a crawling cap.
                // Race 19: caps learned in traffic (pushed wide, in a lane, diving) held the leader in clean air at 33 m/s
                // through the Parabolica. A cap is for the racing line, so learn it only on the line in clear road.
                // Race 30: wide on the Parabolica exit capped the exit itself, which slowed the exit but not the apex that set
                // the wide line, so it ratcheted to the floor (16 caps; 61 m/s onto the main straight). On an exit trim the apex.
                if (speed > CapMinPlanShare * target && Mathf.Abs(_offset) < 0.5f && !diving && gapS >= 1f)
                {
                    // Race 35: once the exit was capped the plan there was flat, so a "plan rises ahead" test failed and the cap
                    // ratcheted again; race 36: on the near-straight exit the limit is already top speed, so "limit rises" failed too.
                    // Exit = the apex behind (slowest plan within 120 m) has a clearly lower corner limit than here.
                    float apex = ApexBehind(here.Distance);
                    if (_profile.LimitAt(apex) < _profile.LimitAt(here.Distance) - ExitLimitRiseMs) Learn(apex, UndersteerTrim);
                    else LearnCap(here.Distance, speed * LimitCapScale / _profile.LimitAt(here.Distance));
                }
            }
            _wasAtLimit = atLimit;
            if (yielding) throttle = 0f;
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
            VehicleCommand final = DrivingSafetyFilter.Apply(cmd, speed, gapS, closing,
                _progress.Laps == 0 ? DrivingSafetyFilter.MinHeadwayS : DrivingSafetyFilter.RacingHeadwayS); // race 17: wide on lap 1
            _car.Command = final;
            if (_diveTarget != null && (final.Throttle < cmd.Throttle || final.Brake > cmd.Brake)) _diveOverrideS += dt;

            if (Log != null && Time.time >= _nextTelemetry)
            {
                _nextTelemetry = Time.time + TelemetryIntervalS;
                Log.Telemetry(Identity.driverName, _progress.Laps + 1, _progress.LapDistance, speed, target,
                              final.Throttle, final.Brake, final.Steer, _offset, _targetSide, gapS, _pendingEvent.Trim(),
                              lateral, pathHere, slipDeg);
                _pendingEvent = "";
            }
        }

        /// <summary>
        /// Track-limit guard (checked in SeamSelfCheck): near the white line, more than GuardOffPathM off its own path toward
        /// that edge, and still drifting outward. Race 11: predicting from raw sideways speed fired on every line sweep;
        /// race 12: without the outward check a car steering back from the run-off kept the throttle cut.
        /// </summary>
        public static bool AtTrackLimit(float lateralM, float pathM, float lateralVelMs, float speedMs, float trackHalfM)
        {
            float outSide = Mathf.Sign(lateralM);
            return speedMs > 8f && Mathf.Abs(lateralM) > trackHalfM - WhiteLineGuardM
                && (lateralM - pathM) * outSide > GuardOffPathM && lateralVelMs * outSide > 0f;
        }

        private void LearnCap(float distance, float share)
        {
            if (Time.time < _nextLearn || !_corners.CapAt(distance, share)) return;
            _nextLearn = Time.time + LearnIntervalS;
            _profile = BuildProfile(Pace);
            _pendingEvent += $"cap:{Mathf.Repeat(distance - F1TrackGenerator.StartLineDistance, _track.Spline.TotalLength):F0}@{share:F2} ";
        }

        /// <summary>Spline distance of the slowest planned point in the 120 m behind <paramref name="distance"/> (the corner's apex).</summary>
        private float ApexBehind(float distance)
        {
            float apex = distance, slowest = float.MaxValue;
            for (float d = 0f; d <= 120f; d += 5f)
            {
                float v = _profile.TargetAt(distance - d);
                if (v < slowest) { slowest = v; apex = distance - d; }
            }
            return apex;
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

        /// <summary>What one tactical tick sees of the cars around (shared by both D04 variants).</summary>
        private struct FieldScan
        {
            public float BestGap, AheadSpeed, AheadLat, GapBehind, BehindSpeed;
            public TrackProgress Ahead, Behind;
            public bool LeftFree, LineFree, RightFree;    // to start using a lane: no car in it from 15 m behind to 40 m ahead
            public bool LeftClear, LineClear, RightClear; // to keep a lane: no car in it alongside or ahead (one following us is fine)
            public bool LineMergeFree;                    // to move back onto the line: no car on it from 15 m behind to 8 m ahead, nor a slower one we would reach within LiftContactS
        }

        /// <summary>
        /// Every car near us: the nearest car ahead sets the gap and the preferred (opposite) side; any car alongside or ahead
        /// within range blocks the lane it is in; the nearest car behind (within the defence range) is kept for the lookahead.
        /// </summary>
        private FieldScan ScanField(float speed, float targetHalfWidthM = LaneHalfWidthM)
        {
            CatmullRomSpline spline = _track.Spline;
            float length = spline.TotalLength;
            float SignedGap(TrackProgress o)
            {
                float g = Mathf.Repeat(o.LapDistance - _progress.LapDistance, length);
                return g > length * 0.5f ? g - length : g; // negative = behind us
            }
            TrackProgress target = null; // nearest car ahead: the one an attack would pass
            float targetGap = LookAheadM;
            foreach (TrackProgress other in _field)
            {
                if (other == null || other == _progress || !other.isActiveAndEnabled) continue;
                float g = SignedGap(other);
                if (g > 0f && g < targetGap) { targetGap = g; target = other; }
            }
            var f = new FieldScan { BestGap = float.MaxValue, AheadSpeed = speed, GapBehind = float.MaxValue, LeftFree = true, LineFree = true, RightFree = true,
                                    LeftClear = true, LineClear = true, RightClear = true, LineMergeFree = true };
            float maxOffset = _track.trackWidth * 0.5f - LaneEdgeMarginM;
            float lookBehind = Mathf.Max(LookBehindM, LookaheadTacticalDriver.DefendRangeM);
            _mergeBlocked = false;
            foreach (TrackProgress other in _field)
            {
                if (other == null || other == _progress || !other.isActiveAndEnabled) continue;
                float gap = SignedGap(other);
                if (gap < -lookBehind || gap > LookAheadM) continue;
                if (gap < 0f && -gap < f.GapBehind)
                {
                    f.GapBehind = -gap;
                    f.Behind = other;
                    var brb = other.GetComponent<Rigidbody>();
                    f.BehindSpeed = brb != null ? brb.linearVelocity.magnitude : speed;
                }
                if (gap < -LookBehindM) continue; // only for the defence: too far back to block a lane
                var s = spline.GetSampleAtDistance(other.LapDistance + F1TrackGenerator.StartLineDistance);
                float lineM = _line.OffsetAt(s.Distance);
                float lat = Vector3.Dot(other.transform.position - s.Position, s.Right) - lineM; // lanes sit either side of the line
                // Race 16: with the line near the edge the lane is clamped onto the track, i.e. onto the car on the line. Check
                // the lane where the car would really drive; 15 cars swept into the other grid column 3 s after the start.
                float leftLane = Mathf.Clamp(lineM - OvertakeOffsetM, -maxOffset, maxOffset) - lineM;
                float rightLane = Mathf.Clamp(lineM + OvertakeOffsetM, -maxOffset, maxOffset) - lineM;
                float half = other == target ? targetHalfWidthM : LaneHalfWidthM;
                bool inLeft = Mathf.Abs(lat - leftLane) < half, inRight = Mathf.Abs(lat - rightLane) < half, onLine = Mathf.Abs(lat) < half;
                bool alongsideOrAhead = gap > -CarLengthM;
                if (inLeft) { f.LeftFree = false; if (alongsideOrAhead) f.LeftClear = false; }
                if (inRight) { f.RightFree = false; if (alongsideOrAhead) f.RightClear = false; }
                if (onLine) { f.LineFree = false; if (alongsideOrAhead) f.LineClear = false; }
                if (Mathf.Abs(lat) < LaneHalfWidthM)
                {
                    // Race 32: Max merged back onto the line 12 m behind a car 12 m/s slower (contact in 1 s) and the forward
                    // sensor only saw it once in its lane, 0.6 s out. A slower car ahead on the line blocks the merge by time.
                    bool closingFast = false;
                    if (gap > 0f && gap >= MergeClearM)
                    {
                        var orb = other.GetComponent<Rigidbody>();
                        float closing = speed - (orb != null ? orb.linearVelocity.magnitude : speed);
                        closingFast = closing > 0f && gap < closing * DrivingSafetyFilter.LiftContactS;
                    }
                    if (gap < MergeClearM || closingFast) f.LineMergeFree = false; // merging: every car, wide
                    // Race 33: a car alongside on the line (nose just behind ours) blocks too; S-bends force the merge and Charles moved
                    // into Lando side by side at 358 m on lap 1, starting a 19-reset pile-up.
                    if (gap > -CarLengthM && (gap < MergeClearM || closingFast)) _mergeBlocked = true;
                }
                if (gap > 0f && gap < f.BestGap)
                {
                    f.BestGap = gap;
                    f.Ahead = other;
                    f.AheadLat = lat;
                    var orb = other.GetComponent<Rigidbody>();
                    f.AheadSpeed = orb != null ? orb.linearVelocity.magnitude : speed;
                }
            }
            return f;
        }

        /// <summary>D04 scripted baseline: lane choice plus the corner dive with its back-out path.</summary>
        private float DecideSide(float speed)
        {
            FieldScan f = ScanField(speed);
            float bestGap = f.BestGap, aheadSpeed = f.AheadSpeed, preferred = f.AheadLat > 0f ? -1f : 1f;
            bool leftFree = f.LeftFree, rightFree = f.RightFree;
            TrackProgress ahead = f.Ahead;

            // v1.2: a late inside attack needs an abort path. Back out to the line when the inside closes, a fast S-bend
            // starts, or the target pulls away; the merge yield then drops the car in behind. A finished dive logs its outcome.
            if (_diveTarget != null && Time.time >= _diveUntil) EndDive(null);
            else if (_diveTarget != null)
            {
                string why = !(_diveSide < 0f ? leftFree : rightFree) ? "inside closed"
                           : _sBend ? "fast S-bend"
                           // Race 21: "not faster" backed out 110 of 181 dives within a tick (1 pass). Only a target pulling away counts.
                           : ahead == _diveTarget && aheadSpeed > speed + DiveClosingMs ? "target pulling away"
                           : null;
                if (why != null)
                {
                    EndDive(why);
                    return 0f;
                }
            }

            // Corner overtake: closing on a car before a braking zone -> dive down the inside of that corner and brake a bit later.
            // Race 22: 177 of 194 dives started 20-40 m back closing ~3 m/s, i.e. ~9 s from alongside in a 3 s dive (2 passes).
            if (!_sBend && Time.time >= _diveUntil && bestGap < (speed - aheadSpeed) * DiveHoldS && speed > aheadSpeed + DiveClosingMs)
            {
                float inside = NextCorner(_progress.LapDistance + F1TrackGenerator.StartLineDistance, DiveCornerAheadM, out _, out _);
                // Race 20: a dive from the opposite lane crossed 7 m of track through the pack and was hit; dive only from the line or the same side.
                bool crossing = _offset * inside < -OvertakeOffsetM * 0.5f;
                if (inside != 0f && !crossing && (inside < 0f ? leftFree : rightFree))
                {
                    _diveSide = inside;
                    _diveUntil = Time.time + DiveHoldS;
                    _tactics.Commit(inside, _diveUntil);
                    StartEncounter("dive " + (inside < 0f ? "left" : "right"), ahead, bestGap, speed - aheadSpeed, null);
                    return inside;
                }
            }
            float current = Mathf.Abs(_offset) > OvertakeOffsetM * 0.5f ? Mathf.Sign(_offset) : 0f;
            return _tactics.Decide(Time.time, bestGap, leftFree, rightFree, preferred, current, Identity.aggression);
        }

        /// <summary>
        /// D04 lookahead variant (v1.2 phase 2-3): builds the duel context and forecasts, lets the planner pick a maneuver and
        /// executes it with the same lane/late-braking machinery as the dive (late plan, learning off, encounter log).
        /// </summary>
        private float DecideLookahead(float speed)
        {
            // Race 29: PassStraight moves 5 m apart in the packed start (66-120 m) flipped 7 cars. Hold lanes until the field
            // strings out, the same start zone that has no slipstream.
            if (_progress.RaceProgress < RaceRules.SlipstreamOffAfterStartM) return LaneOf(_offset) - 1;
            FieldScan f = ScanField(speed, TargetLaneHalfWidthM);
            float length = _track.Spline.TotalLength;
            float here = _progress.LapDistance + F1TrackGenerator.StartLineDistance;
            float range = Mathf.Max(DiveCornerAheadM, speed * 3f);
            float cornerSide = NextCorner(here, range, out float brakeIn, out float exitM);
            // Race 30: at the 1050 m apex no braking zone lies ahead, so it read as a straight and started PassStraight mid-corner.
            bool inCorner = false;
            for (float ahead = 0f; ahead <= 20f && cornerSide == 0f; ahead += 5f)
                if (Mathf.Abs(_track.Spline.GetSampleAtDistance(here + ahead).SignedCurvature) > 0.005f) inCorner = true;
            TrackProgress aheadCar = f.Ahead;
            AIDriver aheadAi = aheadCar != null ? aheadCar.GetComponent<AIDriver>() : null;
            int aheadLane = LaneOf(f.AheadLat);
            Func<float, float> aheadPlan = aheadAi != null ? aheadAi.LanePlanAt : _plans[aheadLane].TargetAt; // a human: our plan for its lane
            // Race 33: straight passes forecast -0.37 s on average because the forecast ignored the tow and DRS, the only way
            // past on this track. Our drag share now (same factors the physics applies to every car).
            // shortcut: assumes the tow lasts the whole forecast (it ends on pulling out), so gains are overstated; tune need if passes get reckless.
            float selfDrag = (_car.DrsAllowed ? 1f - _car.drsDragReduction : 1f) * (1f - _car.slipstreamDragCut * Mathf.Clamp01(_car.SlipstreamFactor));
            float aheadM = here + f.BestGap, behindM = here - f.GapBehind, aheadV = f.AheadSpeed, behindV = f.BehindSpeed, v0 = speed;
            var ctx = new TacticalContext
            {
                Now = Time.time,
                SpeedMs = speed,
                SplineM = Mathf.Repeat(here, length),
                LoopM = length,
                CurrentSide = LaneOf(_offset) - 1,
                BrakeDecelMs2 = Pace.brakeDecel,
                Lap = _progress.Laps,
                // Races 31-34: 9-19 resets, all on lap 1 between the first chicane (394 m) and the straight at 660 m. Provisional.
                Calm = _progress.RaceProgress < LapOneCalmM,
                LeftFree = f.LeftFree,
                LineFree = f.LineFree,
                RightFree = f.RightFree,
                LeftClear = f.LeftClear,
                LineClear = f.LineClear,
                RightClear = f.RightClear,
                LineMergeFree = f.LineMergeFree,
                SBend = _sBend,
                InCorner = inCorner,
                HasAhead = aheadCar != null,
                GapAheadM = f.BestGap,
                AheadSpeedMs = f.AheadSpeed,
                AheadSide = aheadLane - 1,
                HasBehind = f.Behind != null,
                GapBehindM = f.GapBehind,
                BehindSpeedMs = f.BehindSpeed,
                CornerSide = cornerSide,
                BrakeInM = brakeIn,
                CornerExitM = Mathf.Repeat(exitM, length),
                SelfTime = (side, late, to) => Forecast((late ? _latePlans : _plans)[(int)side + 1].TargetAt, (int)side + 1, here, v0, to, selfDrag),
                AheadTime = to => Forecast(aheadPlan, aheadLane, aheadM, aheadV, to),
                BehindTime = (side, late, to) => Forecast((late ? _latePlans : _plans)[(int)side + 1].TargetAt, (int)side + 1, behindM, behindV, to),
            };
            TacticalDecision d = _planner.Decide(ctx);
            // Race 29: Max sat 0.3 s behind Kimi with no encounter logged. Log a declined attack once per corner when close.
            if (!d.Started && d.Reason != null && d.Reason.Contains("need") && f.BestGap < speed * 0.6f && Mathf.Abs(DeclineKey(cornerSide, exitM, here) - _declinedExitM) > 30f) // the exit point moves in 5 m steps as we drive (race 31: 2084 lines)
            {
                _declinedExitM = DeclineKey(cornerSide, exitM, here);
                Log?.Event($"declined {Identity.driverName} on {NameOf(aheadCar)} at lap {_progress.Laps + 1}, {_progress.LapDistance:F0} m, " +
                           $"gap {f.BestGap:F0} m, closing {speed - f.AheadSpeed:F1} m/s [{d.Reason}]");
            }
            if (d.Started)
            {
                _bypassDwell = true;
                bool defend = d.Maneuver == Maneuver.DefendInside;
                StartEncounter(d.Maneuver + (d.Side < 0f ? " left" : d.Side > 0f ? " right" : " line"), defend ? f.Behind : aheadCar,
                               defend ? -f.GapBehind : f.BestGap, defend ? f.BehindSpeed - speed : speed - f.AheadSpeed, d.Reason);
                _diveSide = d.Side;
                _diveUntil = d.Late ? Time.time + LookaheadTacticalDriver.MaxCommitS : 0f; // late plan + no learning while attacking
            }
            else if (d.Maneuver == Maneuver.BackOut)
            {
                if (_diveTarget != null) EndDive(d.Reason);
                _diveUntil = 0f;
            }
            else if (d.Ended && _diveTarget != null) EndDive(null);
            return d.Side;
        }

        // A corner by its exit; a straight by its 200 m stretch (its "exit" is wherever the car is).
        private static float DeclineKey(float cornerSide, float exitM, float hereM) => cornerSide != 0f ? exitM : -1f - Mathf.Floor(hereM / 200f) * 200f;

        private float Forecast(Func<float, float> plan, int lane, float fromM, float speedMs, float toM, float dragScale = 1f)
        {
            float[] stretch = _laneStretch[lane];
            float length = _track.Spline.TotalLength;
            return TacticalWorldModel.TimeTo(plan, v => _car.FullThrottleAccelAt(v, dragScale) * Pace.throttleDemand,
                d => stretch[(int)(Mathf.Repeat(d, length) / length * stretch.Length) % stretch.Length], fromM, speedMs, toM, length);
        }

        /// <summary>Opens an encounter (v1.2 log): who, against whom, where, the gap and closing speed, and the planner's candidates.</summary>
        private void StartEncounter(string what, TrackProgress other, float gapM, float closingMs, string candidates)
        {
            _pendingEvent += what.Replace(' ', ':') + " ";
            _diveTarget = other;
            _diveOverrideS = 0f;
            _encounterWhat = what;
            _encounters++;
            Log?.Event($"encounter {Identity.driverName}#{_encounters}: {what} on {NameOf(other)} at lap {_progress.Laps + 1}, " +
                       $"{_progress.LapDistance:F0} m, gap {gapM:F0} m, closing {closingMs:F1} m/s" + (candidates != null ? $" [{candidates}]" : ""));
        }

        /// <summary>Closes the current encounter: backs out (reason given) or ends normally, and logs the outcome.</summary>
        private void EndDive(string backOutReason)
        {
            bool defence = _encounterWhat != null && _encounterWhat.StartsWith(nameof(Maneuver.DefendInside));
            bool ahead = _diveTarget != null && _progress.RaceProgress > _diveTarget.RaceProgress;
            string outcome = _diveTarget == null || !_diveTarget.isActiveAndEnabled ? "target gone"
                           : defence ? (ahead ? "kept ahead of" : "passed by")
                           : ahead ? "passed" : "still behind";
            Log?.Event($"encounter {Identity.driverName}#{_encounters}: {(backOutReason != null ? "back out (" + backOutReason + ")" : _encounterWhat + " ended")}, " +
                       $"{outcome} {NameOf(_diveTarget)}, safety override {_diveOverrideS:F1} s");
            if (backOutReason != null)
            {
                _pendingEvent += "backout ";
                _bypassDwell = true;
                _tactics.Release();
                _planner?.Release();
            }
            _diveTarget = null;
            _diveUntil = 0f;
        }

        private static string NameOf(TrackProgress p) =>
            p == null ? "?" : p.TryGetComponent(out AIDriver d) ? d.Identity.driverName : p.name;

        /// <summary>True when the track bends both ways within the next SBendLookS at speed (a fast S-bend or chicane).</summary>
        private bool InFastSBend(float splineM, float speed)
        {
            if (speed < SBendMinSpeedMs) return false;
            float kMax = 0f, kMin = 0f;
            for (float d = 0f; d <= speed * SBendLookS; d += 5f)
            {
                float kc = _track.Spline.GetSampleAtDistance(splineM + d).SignedCurvature;
                kMax = Mathf.Max(kMax, kc);
                kMin = Mathf.Min(kMin, kc);
            }
            return kMax > SBendCurvature && kMin < -SBendCurvature;
        }

        /// <summary>
        /// Inside of the next corner (-1 left, +1 right) when its braking zone starts within <paramref name="rangeM"/>, else 0.
        /// Braking zone: the plan drops by more than DiveDropMs; the corner side is the curvature sign just past it.
        /// <paramref name="brakeInM"/>: metres to the braking zone; <paramref name="exitM"/>: spline metres 40 m past the
        /// corner's slowest point (the lookahead's common forecast point).
        /// </summary>
        private float NextCorner(float splineM, float rangeM, out float brakeInM, out float exitM)
        {
            float now = _profile.TargetAt(splineM);
            brakeInM = 0f;
            exitM = splineM;
            for (float d = 10f; d <= rangeM; d += 10f)
            {
                if (_profile.TargetAt(splineM + d) > now - DiveDropMs) continue;
                float k = 0f, slowest = float.MaxValue, apex = d; // strongest curvature in the 80 m after the braking point: that is the corner
                for (float c = d; c <= d + 80f; c += 5f)
                {
                    float kc = _track.Spline.GetSampleAtDistance(splineM + c).SignedCurvature;
                    if (Mathf.Abs(kc) > Mathf.Abs(k)) k = kc;
                }
                for (float c = d; c <= d + 200f; c += 5f)
                {
                    float v = _profile.TargetAt(splineM + c);
                    if (v < slowest) { slowest = v; apex = c; }
                }
                brakeInM = d - 10f;
                exitM = splineM + apex + 40f;
                return Mathf.Abs(k) > 0.005f ? Mathf.Sign(k) : 0f; // positive curvature turns right: inside is +Right
            }
            return 0f;
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
