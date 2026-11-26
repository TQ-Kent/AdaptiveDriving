using System;
using UnityEngine;

namespace F1Racing.AI.Driving
{
    /// <summary>D02: stable opponent identity. The skill offset and style never change during a season (spec B4).</summary>
    [Serializable]
    public struct OpponentIdentity
    {
        public string driverName;
        [Range(-0.2f, 0.2f)] public float skillOffset; // delta_i
        [Range(0f, 1f)] public float aggression;      // how early it commits to an overtake; lowers the risk weight
        public Color livery;                          // paint colour (visual only)
        // v1.2 D04 style (stable): weights of the lookahead planner's scores, never physics or difficulty.
        [Range(0f, 1f)] public float lateBraking;     // how much later than the plan it brakes when attacking (within the shared brake limit)
        [Range(0f, 1f)] public float defending;       // how readily it covers the inside against a car behind
        [Range(0f, 1f)] public float patience;        // predicted gain (s) it needs before it attacks

        public OpponentIdentity(string driverName, float skillOffset, float aggression, Color livery,
                                float lateBraking = 0.5f, float defending = 0.5f, float patience = 0.5f)
        {
            this.livery = livery;
            this.driverName = driverName;
            this.skillOffset = skillOffset;
            this.aggression = aggression;
            this.lateBraking = lateBraking;
            this.defending = defending;
            this.patience = patience;
        }
    }

    /// <summary>Controller settings that set an AI's pace. Driving targets only, never car physics.</summary>
    [Serializable]
    public struct PaceSettings
    {
        public float gripUtilization; // multiple of the car's lateral grip model used to plan corner speed
        public float brakeDecel;      // m/s^2 assumed when planning braking points
        public float throttleDemand;  // 0..1 cap on throttle
    }

    /// <summary>D03: maps normalized difficulty to pace settings (spec B4).</summary>
    public static class PaceProfileMapper
    {
        // Provisional, unvalidated endpoints. Utilization above 1 is allowed: lateralGripG is the steering-assist
        // estimate, below the tyres' real peak. Check overshoot in Logs/ai_telemetry.csv (speed > target, off_track resets). Measure the real difficulty -> lap-time curve with
        // Tools > AI > Run Pace Sweep; a linear step in d is not a linear step in lap time.
        // gripUtilization 1.7: race 2 the player cornered ~1.4-2x faster than Hard AI; the tyres peak well above lateralGripG (1.6 g).
        // brakeDecel: race 23 sustained full brake measured 31-44 m/s^2 at 20-60 m/s (n=16, no clear rise with speed), so Fastest
        // plans 28 (~85%, the share race 1 found for braking while turning). Was 16. Slowest keeps the old 0.69 ratio. Re-measure.
        public static readonly PaceSettings Slowest = new PaceSettings { gripUtilization = 0.8f, brakeDecel = 19f, throttleDemand = 0.9f };
        public static readonly PaceSettings Fastest = new PaceSettings { gripUtilization = 1.7f, brakeDecel = 28f, throttleDemand = 1f };

        /// <summary>
        /// The first <paramref name="count"/> identities with offsets relative to the best of them (best = 0, the rest below).
        /// Spec B4: race 23 clamped d_session + delta_i at 1, so at Expert 11 of 22 drivers had equal pace and no reason to pass.
        /// The best driver in the race runs at the session difficulty. Shifts the difficulty-to-pace mapping (M02, not yet calibrated).
        /// </summary>
        public static OpponentIdentity[] RelativeToBest(OpponentIdentity[] roster, int count)
        {
            count = Mathf.Clamp(count, 0, roster.Length);
            float top = float.NegativeInfinity;
            for (int i = 0; i < count; i++) top = Mathf.Max(top, roster[i].skillOffset);
            var result = new OpponentIdentity[count];
            for (int i = 0; i < count; i++) { result[i] = roster[i]; result[i].skillOffset -= top; }
            return result;
        }

        /// <summary>d_i = clamp(d_session + delta_i, 0, 1); non-finite input maps to the slowest setting.</summary>
        public static float Effective(float sessionDifficulty, OpponentIdentity id)
        {
            float d = sessionDifficulty + id.skillOffset;
            return float.IsFinite(d) ? Mathf.Clamp01(d) : 0f;
        }

        public static PaceSettings Map(float sessionDifficulty, OpponentIdentity id)
        {
            float t = Effective(sessionDifficulty, id);
            return new PaceSettings
            {
                gripUtilization = Mathf.Lerp(Slowest.gripUtilization, Fastest.gripUtilization, t),
                brakeDecel = Mathf.Lerp(Slowest.brakeDecel, Fastest.brakeDecel, t),
                throttleDemand = Mathf.Lerp(Slowest.throttleDemand, Fastest.throttleDemand, t),
            };
        }
    }
}
