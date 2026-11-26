using UnityEngine;

namespace F1Racing.Race
{
    /// <summary>One DRS zone: a detection line, then an activation stretch (spline distances, m).</summary>
    public readonly struct DrsZone
    {
        public readonly float DetectionM, StartM, EndM;
        public DrsZone(float detectionM, float startM, float endM) { DetectionM = detectionM; StartM = startM; EndM = endM; }
    }

    /// <summary>
    /// Simplified F1 sporting rules (FIA 2025-26, cut down for a short single race). Pure helpers, checked in
    /// Editor/SeamSelfCheck. RaceManager applies them identically to human and AI cars. Values are game choices, not FIA text:
    /// - Track limits: all four wheels past the white line is a strike; 3rd strike = black-and-white flag, 4th and
    ///   every later one = +5 s. A lap with a strike cannot be a best or fastest lap.
    /// - DRS: only inside a zone, from the car's 2nd lap, when it crossed the zone's detection line under 1.0 s
    ///   behind the car in front (any car, lapped or not). Braking closes it (F1CarController).
    /// - Start: moving more than 0.5 m before lights out is a jump start, +5 s.
    /// - Points 25-18-15-12-10-8-6-4-2-1 for P1-P10; fastest lap is shown, with no point (2025 rule).
    /// </summary>
    public static class RaceRules
    {
        public const float TrackLimitSlackM = 1.0f;  // car centre this far past the line: all four wheels are off
        public const int BlackWhiteFlagStrike = 3;
        public const float TrackLimitPenaltyS = 5f;
        public const float DrsGapS = 1.0f;
        public const int DrsFromLap = 2;
        public const float JumpStartM = 0.5f, JumpStartPenaltyS = 5f;
        public const float BlueFlagGapS = 1.0f;
        public const float YellowStoppedMs = 4f, YellowWarnAheadM = 250f;
        private static readonly int[] PointsTable = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };

        /// <summary>Monza-style zones on the scaled track: the Serraglio run to Ascari, and the main straight.</summary>
        public static readonly DrsZone[] Zones =
        {
            new DrsZone(1338f, 1458f, 1780f),
            new DrsZone(5f, 113f, 470f),
        };

        // Slipstream: a car up to 1 s behind another and within 2 m sideways of it gets less drag (F1CarController.slipstreamDragCut),
        // full at 0 s and fading to none at 1 s. Off on lap 1 until 200 m past the start line. Provisional values.
        public const float SlipstreamGapS = 1f, SlipstreamLateralM = 2f, SlipstreamOffAfterStartM = 200f;

        /// <summary>0..1 tow from a car <paramref name="gapS"/> seconds ahead and <paramref name="lateralM"/> to the side.</summary>
        public static float SlipstreamFactor(float gapS, float lateralM) =>
            float.IsFinite(gapS) && gapS > 0f && gapS < SlipstreamGapS && Mathf.Abs(lateralM) < SlipstreamLateralM ? 1f - gapS / SlipstreamGapS : 0f;

        public static bool PastTrackLimit(float lateralM, float trackHalfWidthM) => Mathf.Abs(lateralM) > trackHalfWidthM + TrackLimitSlackM;

        /// <summary>Time penalty (s) for strike number <paramref name="strike"/> (1-based).</summary>
        public static float PenaltyForStrike(int strike) => strike > BlackWhiteFlagStrike ? TrackLimitPenaltyS : 0f;

        /// <summary><paramref name="lap"/> is the 1-based lap the car is on; gap is to the last car over the detection line.</summary>
        public static bool DrsEligible(int lap, float gapToCarAheadS) =>
            lap >= DrsFromLap && float.IsFinite(gapToCarAheadS) && gapToCarAheadS >= 0f && gapToCarAheadS < DrsGapS;

        /// <summary>True when the car moved forward over <paramref name="markM"/> between two spline distances on a loop.</summary>
        public static bool Crossed(float prevM, float curM, float markM, float loopM)
        {
            float moved = Mathf.Repeat(curM - prevM, loopM);
            if (moved <= 0f || moved > loopM * 0.25f) return false; // standing still, reversing or a reset jump
            return Mathf.Repeat(markM - prevM, loopM) <= moved;
        }

        public static bool InZone(DrsZone z, float splineM, float loopM) =>
            Mathf.Repeat(splineM - z.StartM, loopM) <= Mathf.Repeat(z.EndM - z.StartM, loopM);

        public static int PointsFor(int position) => position >= 1 && position <= PointsTable.Length ? PointsTable[position - 1] : 0;
    }

    /// <summary>Counts each excursion past the track limit once: a new strike only after the car is back inside the line.</summary>
    public struct TrackLimitMonitor
    {
        private bool _off;

        /// <summary>Returns true on the step a new excursion starts.</summary>
        public bool Step(float lateralM, float trackHalfWidthM)
        {
            if (!_off && RaceRules.PastTrackLimit(lateralM, trackHalfWidthM)) return _off = true;
            if (_off && Mathf.Abs(lateralM) <= trackHalfWidthM) _off = false;
            return false;
        }
    }
}
