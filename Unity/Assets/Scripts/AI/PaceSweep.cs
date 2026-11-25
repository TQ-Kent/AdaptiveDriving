using System.Globalization;
using System.IO;
using System.Text;
using F1Racing.AI.Driving;
using UnityEngine;

namespace F1Racing.AI
{
    /// <summary>
    /// Calibration run for the AI pace model (M02): one AI car alone, several clean laps at each difficulty,
    /// written to Python/calibration/ai_pace_sweep.csv. Start it with Tools > AI > Run Pace Sweep.
    /// Every lap is written with its flags; only clean laps count toward a level.
    /// </summary>
    public class PaceSweep : MonoBehaviour
    {
        public const string RequestKey = "F1Racing.AI.PaceSweepRequested";
        private static readonly float[] Levels = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        public int cleanLapsPerLevel = 3;
        [Tooltip("Move on after this many laps at one level even without enough clean ones (e.g. it crashes every lap).")]
        public int maxLapsPerLevel = 10;
        // shortcut: lap timing is per frame, so higher time scales widen the timing error; keep this modest.
        public float timeScale = 3f;

        private AIDriver _driver;
        private int _level, _cleanLaps, _lapsAtLevel;
        private bool _written;
        private readonly StringBuilder _csv = new StringBuilder(
            "observation_id,car_id,lap_index,lap_time_s,difficulty,mixed_difficulty,incident,standing_start,clean\n");

        public static string OutputPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Python", "calibration", "ai_pace_sweep.csv"));

        public static bool ConsumeRequest()
        {
#if UNITY_EDITOR
            bool requested = UnityEditor.SessionState.GetBool(RequestKey, false);
            UnityEditor.SessionState.EraseBool(RequestKey);
            return requested;
#else
            return false;
#endif
        }

        public void Begin(AIDriver driver)
        {
            _driver = driver;
            _driver.LapObserved += OnLap;
            _driver.SetDifficulty(Levels[0]);
            Time.timeScale = timeScale;
            Debug.Log($"[PaceSweep] started: {Levels.Length} levels x {cleanLapsPerLevel} clean laps at {timeScale}x");
        }

        private void OnLap(LapObservation o)
        {
            _csv.Append(o.observationId).Append(',').Append(o.carId).Append(',').Append(o.lapIndex).Append(',')
                .Append(o.lapTimeS.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append(o.difficulty.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append(o.mixedDifficulty).Append(',').Append(o.incident).Append(',')
                .Append(o.standingStart).Append(',').Append(o.IsClean).Append('\n');
            Debug.Log($"[PaceSweep] d={o.difficulty:F2} lap {o.lapIndex}: {o.lapTimeS:F2}s clean={o.IsClean}");

            if (o.IsClean) _cleanLaps++;
            if (_cleanLaps < cleanLapsPerLevel && ++_lapsAtLevel < maxLapsPerLevel) return;
            if (_cleanLaps < cleanLapsPerLevel) Debug.LogWarning($"[PaceSweep] d={o.difficulty:F2}: only {_cleanLaps} clean laps in {maxLapsPerLevel}");
            _cleanLaps = _lapsAtLevel = 0;
            if (++_level < Levels.Length) _driver.SetDifficulty(Levels[_level]); // next lap is flagged mixed
            else Finish();
        }

        private void Finish()
        {
            _driver.LapObserved -= OnLap;
            Write("done");
            Time.timeScale = 1f;
            enabled = false;
        }

        private void Write(string status)
        {
            _written = true;
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            File.WriteAllText(OutputPath, _csv.ToString());
            Debug.Log($"[PaceSweep] {status}, wrote {OutputPath}");
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            if (!_written && _driver != null) Write("stopped early, partial data"); // keep the laps already recorded
        }
    }
}
