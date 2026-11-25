using System.Collections.Generic;
using UnityEngine;

namespace F1Racing.AI.Driving
{
    /// <summary>
    /// D06 data: target speed along a closed loop (spec B3). Corner speed comes from the car's own grip
    /// model, a_lat(v) = cornerGrip + aeroGrip * v^2, so v = sqrt(cornerGrip / (|k| - aeroGrip)). A backward
    /// braking pass then makes every corner reachable at <c>brakeDecel</c>.
    /// </summary>
    public sealed class SpeedProfile
    {
        private readonly float[] _distance, _speed;
        private readonly float _length;

        public float LoopLength => _length;
        public IReadOnlyList<float> Speeds => _speed;
        public IReadOnlyList<float> Distances => _distance;

        private SpeedProfile(float[] distance, float[] speed, float length)
        {
            _distance = distance;
            _speed = speed;
            _length = length;
        }

        public static SpeedProfile Build(IReadOnlyList<float> distances, IReadOnlyList<float> curvatures, float loopLength,
                                         float vMax, float cornerGripMs2, float aeroGripPerV2, float brakeDecel,
                                         IReadOnlyList<float> cornerScale = null, IReadOnlyList<float> speedCap = null)
        {
            int n = distances.Count;
            var dist = new float[n];
            var v = new float[n];
            for (int i = 0; i < n; i++)
            {
                dist[i] = distances[i];
                // Worst curvature over +-2 samples: spline curvature is noisy at sample level.
                float k = 0f;
                for (int j = -2; j <= 2; j++) k = Mathf.Max(k, Mathf.Abs(curvatures[(i + j + n) % n]));
                float denom = k - aeroGripPerV2;
                v[i] = denom > 1e-6f ? Mathf.Min(vMax, Mathf.Sqrt(cornerGripMs2 / denom)) : vMax;
                if (speedCap != null) v[i] = Mathf.Min(v[i], speedCap[i]); // e.g. the steering-lock limit
                if (cornerScale != null) v[i] *= cornerScale[i]; // learned trims; the braking pass below moves braking earlier
            }
            return WithBraking(dist, v, loopLength, brakeDecel);
        }

        private static SpeedProfile WithBraking(float[] dist, float[] v, float loopLength, float brakeDecel)
        {
            int n = dist.Length;
            // Two backward passes around the loop so braking zones that cross the start line are covered.
            for (int pass = 0; pass < 2; pass++)
                for (int i = n - 1; i >= 0; i--)
                {
                    int next = (i + 1) % n;
                    float ds = next == 0 ? loopLength - dist[i] + dist[0] : dist[next] - dist[i];
                    v[i] = Mathf.Min(v[i], Mathf.Sqrt(v[next] * v[next] + 2f * brakeDecel * Mathf.Max(0f, ds)));
                }
            return new SpeedProfile(dist, v, loopLength);
        }

        /// <summary>Target speed (m/s) at spline distance <paramref name="distance"/>, wrapping the loop.</summary>
        public float TargetAt(float distance)
        {
            float d = Mathf.Repeat(distance, _length);
            int lo = 0, hi = _distance.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_distance[mid] <= d) lo = mid; else hi = mid - 1;
            }
            return _speed[lo];
        }
    }
}
