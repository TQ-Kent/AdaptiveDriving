using System;
using System.Text;
using UnityEngine;

namespace F1Racing.AI.Driving
{
    /// <summary>v1.2 D04c maneuvers. Lanes: -1 left, 0 racing line, +1 right (offsets from the line, as AIDriver drives them).</summary>
    public enum Maneuver { HoldLine, FollowTow, PassStraight, AttackInside, AttackOutside, Switchback, DefendInside, BackOut }

    /// <summary>
    /// v1.2 D04a: one tactical tick's track-relative snapshot. Observed state only: positions, speeds and lanes of the cars
    /// ahead and behind, never their inputs or plans' future. Forecast delegates come from D04d (the caller's world model).
    /// </summary>
    public struct TacticalContext
    {
        public float Now, SpeedMs, SplineM, LoopM, CurrentSide, BrakeDecelMs2;
        public int Lap;
        /// <summary>Free: no car in the lane from 15 m behind to 40 m ahead (to start a move). Clear: none alongside or ahead (to keep one).</summary>
        public bool LeftFree, LineFree, RightFree, LeftClear, LineClear, RightClear, SBend;
        public bool InCorner; // inside a corner with no braking zone ahead (at or past the apex): no new maneuver
        public bool Calm;     // opening lap through the first corners: single file, no new maneuver
        /// <summary>No car on the racing line from 15 m behind to 8 m ahead: safe to move back onto it.</summary>
        public bool LineMergeFree;
        public bool HasAhead;
        public float GapAheadM, AheadSpeedMs, AheadSide;
        public bool HasBehind;
        public float GapBehindM, BehindSpeedMs;
        /// <summary>Next braking zone in range: its inside (-1/+1, 0 = none), metres to its start, spline metres of the corner exit.</summary>
        public float CornerSide, BrakeInM, CornerExitM;
        /// <summary>Seconds for this car to reach a spline distance in a lane (late = attack braking); +inf if not within the horizon.</summary>
        public Func<float, bool, float, float> SelfTime;
        /// <summary>Seconds for the car ahead to reach a spline distance, keeping its lane (non-interactive).</summary>
        public Func<float, float> AheadTime;
        /// <summary>Seconds for the car behind to reach a spline distance in a lane, on this car's plans (same car spec).</summary>
        public Func<float, bool, float, float> BehindTime;
    }

    public struct TacticalDecision
    {
        public Maneuver Maneuver;
        public float Side;
        public bool Late;     // brake later than the plan (attack), within the shared brake limit
        public bool Started;  // a new committed maneuver this tick (log it, skip the lane dwell)
        public bool Ended;    // the committed maneuver finished normally this tick
        public string Reason; // candidates with forecast margins and rejections, or the abort reason
    }

    /// <summary>v1.2 D04d (simple track-relative motion): one car following a speed plan with the shared car's acceleration.</summary>
    public static class TacticalWorldModel
    {
        public const float StepS = 0.05f, HorizonS = 8f;

        /// <summary>
        /// Seconds to travel from <paramref name="fromM"/> to <paramref name="toM"/> (spline metres, wrapping <paramref name="loopM"/>)
        /// starting at <paramref name="speedMs"/>: speed rises at <paramref name="accel"/>(v) up to the plan and follows the plan down
        /// (the plan is braking-reachable). <paramref name="stretch"/>(d) = lane metres per spline metre. +inf if beyond the horizon.
        /// </summary>
        public static float TimeTo(Func<float, float> plan, Func<float, float> accel, Func<float, float> stretch,
                                   float fromM, float speedMs, float toM, float loopM)
        {
            float dist = Mathf.Repeat(toM - fromM, loopM), s = 0f, t = 0f, v = Mathf.Max(1f, speedMs);
            while (s < dist && t < HorizonS)
            {
                float d = fromM + s, target = plan(d);
                v = v < target ? Mathf.Min(target, v + Mathf.Max(0f, accel(v)) * StepS) : target;
                v = Mathf.Max(1f, v);
                float k = Mathf.Max(0.5f, stretch(d));
                s += v * StepS / k;
                t += StepS;
                if (s >= dist) return t - (s - dist) * k / v;
            }
            return s >= dist ? t : float.PositiveInfinity;
        }
    }

    /// <summary>
    /// v1.2 D04c/e: the lookahead tactical driver (non-interactive lookahead). Each tactical tick it generates lane maneuvers,
    /// forecasts both cars to a common point (the next corner's exit) on their speed plans, rejects infeasible ones (no room,
    /// crossing the track, a late inside attack with no way to back out, fast S-bend) and ranks the rest by predicted gain
    /// minus risk and switching cost, weighted by the driver's style. A committed maneuver is held until its corner exit or
    /// an abort condition. It only picks a lane and late braking; D05/D06 drive it and D08 can override it.
    /// Provisional constants, to be tuned from the encounter log.
    /// </summary>
    public sealed class LookaheadTacticalDriver
    {
        public const float EngageS = 1.2f, MinEngageM = 15f, TowGapS = 1f, SwitchCostS = 0.05f, LateRiskS = 0.08f;
        public const float NeedMinS = 0f, NeedMaxS = 0.3f, JitterS = 0.05f, AbortGapM = 5f, AbortDecelShare = 0.8f, AbortMarginS = 0.15f;
        public const float SwitchbackExtraM = 60f, DefendRangeM = 20f, MaxCommitS = 5f;

        private readonly float _aggression, _defending, _patience;
        private readonly int _seed;
        private Maneuver _committed = Maneuver.HoldLine;
        private float _side, _until, _horizonM, _defendedExitM = float.NaN;
        private bool _late;

        public Maneuver Committed => _committed;

        public LookaheadTacticalDriver(OpponentIdentity id)
        {
            _aggression = id.aggression;
            _defending = id.defending;
            _patience = id.patience;
            foreach (char c in id.driverName ?? "") _seed = _seed * 31 + c; // stable across runs (string.GetHashCode is not)
        }

        public void Release()
        {
            _committed = Maneuver.HoldLine;
            _late = false;
        }

        public TacticalDecision Decide(in TacticalContext c)
        {
            if (_committed != Maneuver.HoldLine) return Continue(c);
            if (c.Calm) return Hold(c, "lap-1 calm");
            if (c.SBend) return Hold(c, "fast S-bend");
            if (c.InCorner) return Hold(c, "in corner");
            if (c.HasAhead && c.GapAheadM < Mathf.Max(MinEngageM, c.SpeedMs * EngageS)) return Attack(c);
            if (c.HasBehind) return Defend(c);
            return Hold(c, null);
        }

        private TacticalDecision Continue(in TacticalContext c)
        {
            bool attack = _committed != Maneuver.DefendInside;
            string abort = c.SBend ? "fast S-bend"
                         : !Clear(c, _side) ? "lane closed"
                         : attack && !c.HasAhead ? "target gone"
                         : null;
            // Still before the braking zone and the forecast turned: give up while backing out is easy.
            if (abort == null && attack && c.BrakeInM > c.SpeedMs * 0.5f && c.HasAhead)
            {
                float margin = c.AheadTime(_horizonM) - c.SelfTime(_side, _late, _horizonM);
                if (margin < -AbortMarginS) abort = $"no gain ({margin:+0.00;-0.00} s)";
            }
            if (abort != null)
            {
                Release();
                return new TacticalDecision { Maneuver = Maneuver.BackOut, Side = c.LineMergeFree ? 0f : c.CurrentSide, Reason = abort };
            }
            if (c.Now >= _until || Passed(c, _horizonM))
            {
                Maneuver done = _committed;
                Release();
                return new TacticalDecision { Maneuver = done, Side = c.CurrentSide, Ended = true, Reason = "corner exit" };
            }
            return new TacticalDecision { Maneuver = _committed, Side = _side, Late = _late };
        }

        private TacticalDecision Attack(in TacticalContext c)
        {
            float exitM = c.CornerSide != 0f ? c.CornerExitM : c.SplineM + Mathf.Max(60f, c.SpeedMs * 3f);
            float tAhead = c.AheadTime(exitM);
            float need = Mathf.Lerp(NeedMinS, NeedMaxS, _patience) + Jitter(c, exitM);
            var why = new StringBuilder();
            Maneuver bestM = Maneuver.FollowTow;
            float bestScore = float.NegativeInfinity, bestSide = c.AheadSide, bestMargin = 0f, bestHorizon = exitM;
            bool bestLate = false;
            for (int i = -1; i <= 1; i++)
            {
                float side = i;
                if (side == c.AheadSide) continue; // its lane: following, not a maneuver
                bool late = false;
                float horizon = exitM;
                Maneuver m;
                if (c.CornerSide == 0f) m = Maneuver.PassStraight;
                else if (side == c.CornerSide) { m = Maneuver.AttackInside; late = true; }
                else if (c.AheadSide == c.CornerSide) { m = Maneuver.Switchback; horizon = exitM + SwitchbackExtraM; }
                else m = Maneuver.AttackOutside;

                string reject = Reject(c, side, late);
                float margin = 0f;
                if (reject == null)
                {
                    float tSelf = c.SelfTime(side, late, horizon);
                    float tOther = horizon == exitM ? tAhead : c.AheadTime(horizon);
                    margin = tOther - tSelf;
                    if (float.IsNaN(margin) || float.IsInfinity(tSelf)) reject = "no forecast";
                }
                if (reject != null) { why.Append($"{m}{Lane(side)} rejected ({reject}) | "); continue; }
                float score = margin - (side != c.CurrentSide ? SwitchCostS : 0f) - (late ? LateRiskS * (1f - _aggression) : 0f);
                why.Append($"{m}{Lane(side)} {margin:+0.00;-0.00} | ");
                if (score > bestScore) { bestScore = score; bestM = m; bestSide = side; bestLate = late; bestMargin = margin; bestHorizon = horizon; }
            }
            why.Append($"need {need:+0.00;-0.00}");
            if (bestM != Maneuver.FollowTow && bestMargin > need)
            {
                Commit(c, bestM, bestSide, bestLate, bestHorizon);
                return new TacticalDecision { Maneuver = bestM, Side = bestSide, Late = bestLate, Started = true, Reason = why.ToString() };
            }
            // No attack worth it: tuck in behind in its lane on a straight (slipstream), else keep the line.
            bool tow = c.CornerSide == 0f && c.GapAheadM < c.SpeedMs * TowGapS; // its lane is "taken" by the car we follow
            return tow ? new TacticalDecision { Maneuver = Maneuver.FollowTow, Side = c.AheadSide, Reason = why.ToString() } : Hold(c, why.ToString());
        }

        private TacticalDecision Defend(in TacticalContext c)
        {
            float inside = c.CornerSide;
            // One move per straight, and not under braking (F1 rule, and moving in the braking zone is what causes contact).
            if (inside == 0f || c.GapBehindM > DefendRangeM || c.BrakeInM < c.SpeedMs * 0.5f || c.CurrentSide == inside
                || (!float.IsNaN(_defendedExitM) && Gap(c, _defendedExitM, c.CornerExitM) < 30f)
                || !Free(c, inside))
                return Hold(c, null);
            float h = c.CornerExitM;
            float tLine = c.SelfTime(0f, false, h), tInside = c.SelfTime(inside, false, h);
            float threatHold = tLine - c.BehindTime(inside, true, h);       // > 0: it gets to the exit first down the inside
            float threatDefend = tInside - c.BehindTime(-inside, false, h); // its best other way once the inside is covered
            float value = threatHold - threatDefend - (tInside - tLine);
            float need = Mathf.Lerp(0.3f, 0f, _defending) + Jitter(c, h);
            string why = $"hold threat {threatHold:+0.00;-0.00} | defend threat {threatDefend:+0.00;-0.00} | cost {tInside - tLine:+0.00;-0.00} | need {need:+0.00;-0.00}";
            if (float.IsNaN(value) || threatHold < -0.1f || value <= need) return Hold(c, null);
            _defendedExitM = h;
            Commit(c, Maneuver.DefendInside, inside, false, h);
            return new TacticalDecision { Maneuver = Maneuver.DefendInside, Side = inside, Started = true, Reason = why };
        }

        private string Reject(in TacticalContext c, float side, bool late)
        {
            if (!Free(c, side)) return "no room";
            if (side * c.CurrentSide < 0f) return "crossing the track"; // race 20: a dive from the opposite lane was hit mid-track
            if (late && c.GapAheadM > AbortGapM)
            {
                // A late inside attack must be able to back out: brake to the target's speed before reaching it.
                // Gap used while braking to its speed is relative: (v - v_ahead)^2 / 2a, not the absolute v^2 - v_ahead^2.
                float closing = Mathf.Max(0f, c.SpeedMs - c.AheadSpeedMs);
                if (closing * closing / (2f * AbortDecelShare * c.BrakeDecelMs2) > c.GapAheadM - AbortGapM) return "no way to back out";
            }
            return null;
        }

        private void Commit(in TacticalContext c, Maneuver m, float side, bool late, float horizonM)
        {
            _committed = m;
            _side = side;
            _late = late;
            _horizonM = horizonM;
            _until = c.Now + MaxCommitS;
        }

        // Back to the line only when nobody is on it beside us (race 16: 15 cars swept across the grid onto the line at once).
        private TacticalDecision Hold(in TacticalContext c, string why) =>
            new TacticalDecision { Maneuver = Maneuver.HoldLine, Side = c.LineMergeFree ? 0f : c.CurrentSide, Reason = why };

        private static bool Free(in TacticalContext c, float side) => side < 0f ? c.LeftFree : side > 0f ? c.RightFree : c.LineFree;
        private static bool Clear(in TacticalContext c, float side) => side < 0f ? c.LeftClear : side > 0f ? c.RightClear : c.LineClear;

        private static float Gap(in TacticalContext c, float a, float b) => Mathf.Abs(Mathf.Repeat(b - a + c.LoopM * 0.5f, c.LoopM) - c.LoopM * 0.5f);

        /// <summary>True once the car is past <paramref name="splineM"/> (within half a lap, wrapping).</summary>
        private static bool Passed(in TacticalContext c, float splineM) => Mathf.Repeat(c.SplineM - splineM, c.LoopM) < c.LoopM * 0.5f;

        /// <summary>Stable per driver, corner and lap: the same situation is not always met with the same threshold (+-JitterS).</summary>
        private float Jitter(in TacticalContext c, float cornerM)
        {
            int h = _seed * 397 ^ (c.Lap * 7919) ^ Mathf.RoundToInt(Mathf.Repeat(cornerM, c.LoopM) / 50f) * 104729;
            h ^= h >> 13;
            h *= 1274126177;
            return ((h & 0xffff) / 65535f * 2f - 1f) * JitterS;
        }

        private static string Lane(float side) => side < 0f ? "(L)" : side > 0f ? "(R)" : "(line)";
    }
}
