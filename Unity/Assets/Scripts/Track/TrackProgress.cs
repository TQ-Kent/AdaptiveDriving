using System;
using UnityEngine;

namespace F1Racing.Track
{
    /// <summary>
    /// Per-car lap progress along the circuit spline (spec A4: p = nL + s).
    /// Added to every car by GameModeManager. Checkpoints, sectors and lap validity build on this.
    /// </summary>
    public class TrackProgress : MonoBehaviour
    {
        public F1TrackGenerator track;

        public int Laps { get; private set; }
        /// <summary>s: metres travelled past the start/finish line, 0..L.</summary>
        public float LapDistance { get; private set; }
        /// <summary>p = nL + s.</summary>
        public float TotalProgress => Laps * LapLength + LapDistance;
        public float LapLength => track != null && track.Spline != null ? track.Spline.TotalLength : 0f;
        /// <summary>Race distance in metres: like <see cref="TotalProgress"/>, but a car still behind the line is negative.</summary>
        public float RaceProgress => LapCounter.RaceProgress(Laps, LapDistance, LapLength, _counter.PassedHalfway);

        public event Action<TrackProgress> LapCompleted;

        private LapCounter _counter;

        private void Update()
        {
            if (track == null) return;
            CatmullRomSpline spline = track.Spline;
            if (spline == null || spline.TotalLength <= 0f) return;

            float onSpline = spline.Samples[spline.FindNearestSampleIndex(transform.position)].Distance;
            LapDistance = Mathf.Repeat(onSpline - F1TrackGenerator.StartLineDistance, spline.TotalLength);
            if (_counter.Step(LapDistance, spline.TotalLength))
            {
                Laps++;
                LapCompleted?.Invoke(this);
            }
        }
    }

    /// <summary>
    /// Lap rule kept free of Unity types so it can be tested on its own: a lap counts when the car
    /// wraps forward across the line (end of lap -> start of lap) after passing the halfway point,
    /// so reversing over the line or the race-start crossing does not count.
    /// </summary>
    public struct LapCounter
    {
        private float _last;
        private bool _hasLast, _passedHalfway;

        public bool PassedHalfway => _passedHalfway;

        /// <summary>Before its first halfway point a car in the last part of the lap is behind the line, not a lap ahead.</summary>
        public static float RaceProgress(int laps, float lapDistance, float lapLength, bool passedHalfway) =>
            laps * lapLength + lapDistance - (!passedHalfway && lapDistance > lapLength * 0.6f ? lapLength : 0f);

        public bool Step(float lapDistance, float lapLength)
        {
            bool completed = false;
            if (_hasLast)
            {
                if (lapDistance > lapLength * 0.4f && lapDistance < lapLength * 0.6f) _passedHalfway = true;
                bool crossedForward = _last > lapLength * 0.8f && lapDistance < lapLength * 0.2f;
                if (crossedForward && _passedHalfway)
                {
                    _passedHalfway = false;
                    completed = true;
                }
            }
            _last = lapDistance;
            _hasLast = true;
            return completed;
        }
    }
}
