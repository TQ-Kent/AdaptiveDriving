using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace F1Racing.Race
{
    /// <summary>
    /// Local debug log for checking AI behaviour after a race: Logs/race_log.txt (events) and Logs/ai_telemetry.csv
    /// (AI samples). Lines are buffered and written by <see cref="Flush"/> from Update, never from the physics step.
    /// Overwritten at each race; local only, never uploaded.
    /// </summary>
    public sealed class RaceLog
    {
        public const string TelemetryHeader = "t,car,lap,lap_dist_m,speed_ms,target_ms,throttle,brake,steer,offset_m,side,gap_s,lat_m,aim_m,slip_deg,event";
        /// <summary>Human driving at 20 Hz, for finding patterns the AI can learn (Python/tools/analyze_player.py).</summary>
        public const string PlayerInputHeader = "t,car,lap,lap_dist_m,lateral_m,speed_ms,steer,throttle,brake,handbrake,drs,gear,yaw_rate_dps,slip_deg,on_track";
        private readonly StringBuilder _events = new StringBuilder(), _telemetry = new StringBuilder(), _input = new StringBuilder();
        private readonly string _eventsPath, _telemetryPath, _inputPath;

        public RaceLog()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
            _eventsPath = Path.Combine(dir, "race_log.txt");
            _telemetryPath = Path.Combine(dir, "ai_telemetry.csv");
            _inputPath = Path.Combine(dir, "player_input.csv");
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(_eventsPath, "");
                File.WriteAllText(_telemetryPath, TelemetryHeader + "\n");
                File.WriteAllText(_inputPath, PlayerInputHeader + "\n");
            }
            catch (IOException e) { Debug.LogWarning("[RaceLog] cannot write logs: " + e.Message); }
        }

        public void Event(string line)
        {
            _events.Append(Time.timeAsDouble.ToString("F2", CultureInfo.InvariantCulture)).Append("  ").Append(line).Append('\n');
            Debug.Log("[Race] " + line);
        }

        /// <summary>One AI sample. lat_m: car right of the centre line (m); aim_m: where it meant to be (racing line + lane offset). Invariant culture so the CSV parses the same on every machine.</summary>
        public void Telemetry(string car, int lap, float lapDist, float speed, float target, float throttle, float brake,
                              float steer, float offset, float side, float gapS, string evt, float latM = float.NaN, float aimM = float.NaN, float slipDeg = float.NaN)
        {
            var c = CultureInfo.InvariantCulture;
            _telemetry.Append(Time.timeAsDouble.ToString("F2", c)).Append(',').Append(car).Append(',').Append(lap).Append(',')
                .Append(lapDist.ToString("F1", c)).Append(',').Append(speed.ToString("F2", c)).Append(',')
                .Append(float.IsInfinity(target) ? "" : target.ToString("F2", c)).Append(',').Append(throttle.ToString("F2", c)).Append(',')
                .Append(brake.ToString("F2", c)).Append(',').Append(steer.ToString("F2", c)).Append(',')
                .Append(offset.ToString("F2", c)).Append(',').Append(side.ToString("F0", c)).Append(',')
                .Append(float.IsInfinity(gapS) ? "" : gapS.ToString("F2", c)).Append(',')
                .Append(float.IsNaN(latM) ? "" : latM.ToString("F2", c)).Append(',').Append(float.IsNaN(aimM) ? "" : aimM.ToString("F2", c)).Append(',')
                .Append(float.IsNaN(slipDeg) ? "" : slipDeg.ToString("F1", c)).Append(',').Append(evt).Append('\n');
        }

        /// <summary>One human sample. lateral_m: + = right of the centre line; slip_deg: angle between heading and travel (drift).</summary>
        public void PlayerInput(string car, int lap, float lapDist, float lateral, float speed, float steer, float throttle, float brake,
                                bool handbrake, bool drs, int gear, float yawRateDps, float slipDeg, bool onTrack)
        {
            var c = CultureInfo.InvariantCulture;
            _input.Append(Time.timeAsDouble.ToString("F3", c)).Append(',').Append(car).Append(',').Append(lap).Append(',')
                .Append(lapDist.ToString("F1", c)).Append(',').Append(lateral.ToString("F2", c)).Append(',')
                .Append(speed.ToString("F2", c)).Append(',').Append(steer.ToString("F2", c)).Append(',')
                .Append(throttle.ToString("F2", c)).Append(',').Append(brake.ToString("F2", c)).Append(',')
                .Append(handbrake ? 1 : 0).Append(',').Append(drs ? 1 : 0).Append(',').Append(gear).Append(',')
                .Append(yawRateDps.ToString("F1", c)).Append(',').Append(slipDeg.ToString("F1", c)).Append(',')
                .Append(onTrack ? 1 : 0).Append('\n');
        }

        public void Flush()
        {
            try
            {
                if (_events.Length > 0) File.AppendAllText(_eventsPath, _events.ToString());
                if (_telemetry.Length > 0) File.AppendAllText(_telemetryPath, _telemetry.ToString());
                if (_input.Length > 0) File.AppendAllText(_inputPath, _input.ToString());
            }
            catch (IOException e) { Debug.LogWarning("[RaceLog] write failed: " + e.Message); }
            _events.Clear();
            _telemetry.Clear();
            _input.Clear();
        }
    }
}
