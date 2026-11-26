using System.Collections.Generic;
using F1Racing.Vehicle;
using UnityEngine;

namespace F1Racing.AI.Driving
{
    /// <summary>D05: pure-pursuit steering toward a look-ahead point in the car's local frame.</summary>
    public static class PathFollower
    {
        /// <summary>Normalized steer [-1, 1] (positive = right) for a target at <paramref name="localTarget"/>.</summary>
        public static float Steer(Vector3 localTarget, float wheelbase, float maxSteerDeg) =>
            Mathf.Clamp(SteerDemand(localTarget, wheelbase, maxSteerDeg), -1f, 1f);

        /// <summary>Unclamped steer: |value| &gt; 1 means the path needs more lock than the car has at this speed (understeer).</summary>
        public static float SteerDemand(Vector3 localTarget, float wheelbase, float maxSteerDeg)
        {
            float ld = localTarget.magnitude;
            if (ld < 0.1f || maxSteerDeg <= 0f) return 0f;
            float alpha = Mathf.Atan2(localTarget.x, localTarget.z);
            float wheelDeg = Mathf.Atan(2f * wheelbase * Mathf.Sin(alpha) / ld) * Mathf.Rad2Deg;
            return wheelDeg / maxSteerDeg;
        }
    }

    /// <summary>
    /// D06 helper: per racing-line point, the share of this car's own corner limit it plans to drive (1 = the physics plan).
    /// Lowered where it understeered, crashed or ran wide, so the braking pass moves braking earlier; raised back toward 1
    /// every lap, so a corner is tried faster again. Saved per track (TrackLearning). A share, not m/s: a slower driver keeps
    /// its slower plan. Race 23: absolute m/s caps made all 22 drivers equally fast in 158 of 497 points and never rose.
    /// Driver skill only; not difficulty, not physics.
    /// </summary>
    public sealed class CornerMemory
    {
        public const float MinScale = 0.6f;
        private const int ApproachBins = 8, PastBins = 2; // with 5 m bins: 40 m before to 10 m after the trouble spot
        private readonly float[] _scale;
        private readonly float _length;

        public IReadOnlyList<float> Scale => _scale;

        public CornerMemory(int bins, float loopLength)
        {
            _scale = new float[bins];
            for (int i = 0; i < bins; i++) _scale[i] = 1f;
            _length = loopLength;
        }

        /// <summary>Raise every point by <paramref name="amount"/> toward 1 (once a lap). True if anything changed.</summary>
        public bool Recover(float amount)
        {
            bool changed = false;
            for (int i = 0; i < _scale.Length; i++)
                if (_scale[i] < 1f) { _scale[i] = Mathf.Min(1f, _scale[i] + amount); changed = true; }
            return changed;
        }

        /// <summary>Plan at most <paramref name="share"/> of the corner limit from 40 m before to 10 m after <paramref name="distance"/>. True if anything changed.</summary>
        public bool CapAt(float distance, float share)
        {
            if (!float.IsFinite(share)) return false;
            share = Mathf.Max(MinScale, share);
            return Lower(distance, s => Mathf.Min(s, share));
        }

        /// <summary>Lower the plan around <paramref name="distance"/> by <paramref name="amount"/> (floored at MinScale). True if anything changed.</summary>
        public bool Learn(float distance, float amount) => Lower(distance, s => Mathf.Max(MinScale, s - amount));

        /// <summary>Start from shares learned in earlier races (TrackLearning). Ignored when the bin count differs.</summary>
        public void Preload(IReadOnlyList<float> saved)
        {
            if (saved == null || saved.Count != _scale.Length) return;
            for (int i = 0; i < _scale.Length; i++)
                if (saved[i] > 0f && saved[i] < _scale[i]) _scale[i] = Mathf.Max(MinScale, saved[i]);
        }

        private bool Lower(float distance, System.Func<float, float> rule)
        {
            int n = _scale.Length, b = (int)(Mathf.Repeat(distance, _length) / _length * n) % n;
            bool changed = false;
            for (int j = b - ApproachBins; j <= b + PastBins; j++)
            {
                int k = ((j % n) + n) % n;
                float s = rule(_scale[k]);
                if (s < _scale[k]) { _scale[k] = s; changed = true; }
            }
            return changed;
        }
    }

    /// <summary>D06: throttle/brake toward the target speed.</summary>
    public static class PaceController
    {
        // Provisional gains, tune in play.
        public static void Command(float speed, float targetSpeed, PaceSettings pace, out float throttle, out float brake)
        {
            float err = targetSpeed - speed;
            if (err >= 0f)
            {
                throttle = pace.throttleDemand * Mathf.Clamp01(0.3f + 0.35f * err);
                brake = 0f;
            }
            else if (err > -1.5f)
            {
                throttle = 0.15f * pace.throttleDemand; // small overspeed: lift, don't brake
                brake = 0f;
            }
            else
            {
                throttle = 0f;
                brake = Mathf.Clamp01(-err * 0.35f);
            }
            if (speed < 3f) brake = 0f; // brake held at low speed means reverse in the shared car
        }
    }

    /// <summary>
    /// D04 interface (architecture v1.2 §2). The tactical layer only picks a lane (-1 left, 0 line, +1 right); D05/D06 drive
    /// it and D08 can override it. Later variants (lookahead, world-model planner) plug in here beside the scripted baseline.
    /// </summary>
    public interface ITacticalDriver
    {
        /// <summary>At the start: stay in the grid lane until <paramref name="until"/>, even with a car ahead in it.</summary>
        void HoldLane(float side, float until);
        /// <summary>Corner attack: take <paramref name="side"/> until <paramref name="until"/> while that lane stays free.</summary>
        void Commit(float side, float until);
        /// <summary>Drop any held or committed lane (a dive backed out or ended).</summary>
        void Release();
        float Decide(float now, float gapAheadM, bool leftFree, bool rightFree, float preferredSide, float currentSide, float aggression);
    }

    /// <summary>D04 scripted baseline (v1.2 ScriptedTacticalDriver): follow or overtake, with hysteresis so the decision doesn't flicker.</summary>
    public sealed class ScriptedTacticalDriver : ITacticalDriver
    {
        private const float HoldSeconds = 2f;
        private bool _overtaking, _startHold;
        private float _side, _holdUntil;

        public void Commit(float side, float until)
        {
            _overtaking = true;
            _startHold = false;
            _side = side;
            _holdUntil = until;
        }

        public void Release() => _overtaking = _startHold = false;

        public void HoldLane(float side, float until)
        {
            _overtaking = _startHold = true;
            _side = side;
            _holdUntil = until;
        }

        /// <summary>
        /// Returns the lane to drive in: -1 left, 0 centre, +1 right. A lane is free when no nearby car is in it.
        /// With a car close ahead it moves to a free lane (the preferred one first), or stays in
        /// <paramref name="currentSide"/> and follows when both are blocked.
        /// </summary>
        public float Decide(float now, float gapAheadM, bool leftFree, bool rightFree, float preferredSide, float currentSide, float aggression)
        {
            float engage = Mathf.Lerp(15f, 30f, aggression);
            if (_overtaking)
            {
                bool sideFree = _side < 0f ? leftFree : rightFree;
                bool holding = now < _holdUntil;
                if ((holding && (_startHold || sideFree)) || (sideFree && gapAheadM < engage + 10f)) return _side;
                _overtaking = _startHold = false; // pass done, or the lane closed
            }
            if (gapAheadM >= engage) return 0f;

            float side = preferredSide < 0f && leftFree ? -1f
                       : preferredSide > 0f && rightFree ? 1f
                       : rightFree ? 1f
                       : leftFree ? -1f
                       : 0f;
            if (side == 0f) return currentSide; // both lanes blocked: follow where we are
            _overtaking = true;
            _side = side;
            _holdUntil = now + HoldSeconds;
            return side;
        }
    }

    /// <summary>
    /// D07: resets the car through the same <see cref="F1CarController.ResetToTrack"/> the player uses.
    /// shortcut: resets to the nearest centreline point even if another car is there; add a clear-spot search if it happens in play.
    /// </summary>
    public sealed class RecoveryController
    {
        private const float ProgressStepM = 3f, StuckS = 3f, BlockedStuckS = 10f;
        private float _stuck, _wrongWay, _offTrack, _flipped, _bestProgress = float.NegativeInfinity;

        /// <summary>
        /// True when the car must be reset; <paramref name="reason"/> says why. Stuck means no race progress
        /// (<paramref name="raceProgressM"/>) for 3 s, so a car circling or pinned at speed counts too. A car waiting
        /// behind traffic (<paramref name="blocked"/>) gets 10 s, so it is reset only when the car ahead is itself stuck.
        /// </summary>
        public bool Tick(float dt, float raceProgressM, float headingDot, float lateralAbsM, float halfWidthM, float upY, bool blocked, out string reason)
        {
            if (raceProgressM > _bestProgress + ProgressStepM) { _bestProgress = raceProgressM; _stuck = 0f; }
            else _stuck += dt;
            _wrongWay = headingDot < -0.17f ? _wrongWay + dt : 0f; // more than ~100 degrees off the track direction
            _offTrack = lateralAbsM > halfWidthM + 1.5f ? _offTrack + dt : 0f;
            _flipped = upY < 0.3f ? _flipped + dt : 0f;
            reason = _stuck >= (blocked ? BlockedStuckS : StuckS) ? "stuck" : _wrongWay >= 1.5f ? "wrong_way" : _offTrack >= 2f ? "off_track" : _flipped >= 1.5f ? "flipped" : null;
            if (reason == null) return false;
            _stuck = _wrongWay = _offTrack = _flipped = 0f;
            _bestProgress = float.NegativeInfinity; // the reset moves the car; measure progress from where it lands
            return true;
        }
    }

    /// <summary>D08: final command restriction for a car directly ahead. Reduces risk; does not guarantee no contact.</summary>
    public static class DrivingSafetyFilter
    {
        /// <summary>Time gap (s) and closing speed (m/s) to the nearest car ahead; gap is +inf when the way is clear.</summary>
        public static void Sense(Rigidbody self, RaycastHit[] buffer, float speed, out float timeGapS, out float closingMs)
        {
            timeGapS = float.PositiveInfinity;
            closingMs = 0f;
            Vector3 fwd = self.rotation * Vector3.forward;
            float range = Mathf.Max(10f, speed * 1.5f);
            int hits = Physics.SphereCastNonAlloc(self.position + Vector3.up * 0.5f + fwd * 2.8f, 0.9f, fwd,
                                                  buffer, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < hits; i++)
            {
                Rigidbody other = buffer[i].rigidbody;
                if (other == null || other == self || buffer[i].distance >= best || other.GetComponent<F1CarController>() == null) continue;
                best = buffer[i].distance;
                closingMs = speed - Vector3.Dot(other.linearVelocity, fwd);
            }
            if (best < float.MaxValue) timeGapS = best / Mathf.Max(speed, 1f);
        }

        /// <summary>Race 17: 21 cars reached the first chicane 0.06-0.15 s apart and piled up when the leaders braked. Keep a
        /// minimum gap even when not closing: lift below it, brake up to half below half of it. Provisional value.</summary>
        public const float MinHeadwayS = 0.3f;
        /// <summary>Race 29: with closing handled by time to contact, the 0.3 s floor held followers at exactly 0.30-0.33 s, short of
        /// the tow that brings a car alongside. After the start the floor is this. Provisional.</summary>
        public const float RacingHeadwayS = 0.15f;
        /// <summary>Time to contact (s) at the current closing speed: lift below the first, brake below the second. Provisional.</summary>
        public const float LiftContactS = 1.5f, BrakeContactS = 0.75f;

        public static VehicleCommand Apply(VehicleCommand cmd, float speed, float timeGapS, float closingMs, float minHeadwayS = MinHeadwayS)
        {
            if (timeGapS < minHeadwayS && speed > 3f)
            {
                cmd.Drs = false;
                cmd.Throttle = 0f;
                cmd.Brake = Mathf.Max(cmd.Brake, Mathf.Clamp01(1f - 2f * timeGapS / minHeadwayS) * 0.5f);
            }
            // Race 28: lifting and shutting DRS on any closing within 0.8 s held followers 0.4-0.9 s back, out of the tow and
            // DRS they had earned (Max behind Kimi all race). Act on time to contact instead.
            if (closingMs <= 0f) return cmd;
            float contactS = timeGapS * Mathf.Max(speed, 1f) / closingMs;
            if (contactS > LiftContactS) return cmd;
            cmd.Drs = false;
            cmd.Throttle = Mathf.Min(cmd.Throttle, 0.2f);
            if (contactS < BrakeContactS && speed > 3f)
            {
                cmd.Throttle = 0f;
                cmd.Brake = Mathf.Max(cmd.Brake, Mathf.Clamp01(closingMs * 0.25f));
            }
            return cmd;
        }
    }
}
