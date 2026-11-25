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
    /// D06 helper: corner speed trims this car learns during the race where it understeered or crashed. The trim
    /// lowers the planned corner speed, so the braking pass moves the braking point earlier ("brake before the corner").
    /// Driver skill only; not difficulty, not physics. Starts at 1 for every car each race.
    /// </summary>
    public sealed class CornerMemory
    {
        public const float MinTrim = 0.8f;
        private const int ApproachBins = 8, PastBins = 2; // with 5 m bins: 40 m before to 10 m after the trouble spot
        private readonly float[] _trim;
        private readonly float _length;

        public IReadOnlyList<float> Trim => _trim;

        public CornerMemory(int bins, float loopLength)
        {
            _trim = new float[bins];
            for (int i = 0; i < bins; i++) _trim[i] = 1f;
            _length = loopLength;
        }

        /// <summary>Raise every trim by <paramref name="amount"/> toward 1 (called once a lap). True if anything changed.</summary>
        public bool Recover(float amount)
        {
            bool changed = false;
            for (int i = 0; i < _trim.Length; i++)
                if (_trim[i] < 1f) { _trim[i] = Mathf.Min(1f, _trim[i] + amount); changed = true; }
            return changed;
        }

        /// <summary>Lower the corner speed around <paramref name="distance"/> by <paramref name="amount"/> (floored at MinTrim). True if anything changed.</summary>
        public bool Learn(float distance, float amount)
        {
            int n = _trim.Length, b = (int)(Mathf.Repeat(distance, _length) / _length * n) % n;
            bool changed = false;
            for (int j = b - ApproachBins; j <= b + PastBins; j++)
            {
                int k = ((j % n) + n) % n;
                float t = Mathf.Max(MinTrim, _trim[k] - amount);
                if (t < _trim[k]) { _trim[k] = t; changed = true; }
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

    /// <summary>D04: follow or overtake, with hysteresis so the decision doesn't flicker.</summary>
    public sealed class TacticalDriver
    {
        private const float HoldSeconds = 2f;
        private bool _overtaking, _startHold;
        private float _side, _holdUntil;

        /// <summary>At the start: stay in the grid lane until <paramref name="until"/>, even with a car ahead in it.</summary>
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

        public static VehicleCommand Apply(VehicleCommand cmd, float speed, float timeGapS, float closingMs)
        {
            if (closingMs <= 0f || timeGapS > 0.8f) return cmd;
            cmd.Drs = false;
            cmd.Throttle = Mathf.Min(cmd.Throttle, 0.2f);
            if (timeGapS < 0.5f && speed > 3f)
            {
                cmd.Throttle = 0f;
                cmd.Brake = Mathf.Max(cmd.Brake, Mathf.Clamp01(closingMs * 0.25f));
            }
            return cmd;
        }
    }
}
