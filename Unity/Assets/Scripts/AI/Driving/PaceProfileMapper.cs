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
        [Range(0f, 1f)] public float aggression;      // how early it commits to an overtake
        public Color livery;                          // paint colour (visual only)

        public OpponentIdentity(string driverName, float skillOffset, float aggression, Color livery)
        {
            this.livery = livery;
            this.driverName = driverName;
            this.skillOffset = skillOffset;
            this.aggression = aggression;
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
        // brakeDecel: race 1 telemetry measured ~18 m/s^2 braking straight but ~15 m/s^2 braking while turning, so Fastest plans 16.
        public static readonly PaceSettings Slowest = new PaceSettings { gripUtilization = 0.8f, brakeDecel = 11f, throttleDemand = 0.9f };
        public static readonly PaceSettings Fastest = new PaceSettings { gripUtilization = 1.7f, brakeDecel = 16f, throttleDemand = 1f };

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
