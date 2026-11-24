using System.Collections.Generic;
using UnityEngine;

namespace F1Racing.Track
{
    /// <summary>
    /// Closed-loop centripetal Catmull-Rom spline with uniform arc-length lookup
    /// and curvature estimation for procedural F1 circuit generation.
    /// </summary>
    public class CatmullRomSpline
    {
        public struct SamplePoint
        {
            public Vector3 Position;
            public Vector3 Tangent;
            public Vector3 Right;
            public Vector3 Up;
            public float Distance;
            /// <summary>
            /// Signed curvature in radians/meter. Positive = turning right, Negative = turning left.
            /// </summary>
            public float SignedCurvature;
        }

        private readonly List<Vector3> _controlPoints;
        private readonly List<SamplePoint> _samples = new List<SamplePoint>();
        private float _totalLength;

        public IReadOnlyList<SamplePoint> Samples => _samples;
        public float TotalLength => _totalLength;

        public CatmullRomSpline(List<Vector3> controlPoints, int subdivisionsPerSegment = 24)
        {
            _controlPoints = new List<Vector3>(controlPoints);
            BuildLookupTable(subdivisionsPerSegment);
        }

        /// <summary>
        /// Evaluates a closed-loop Catmull-Rom spline at normalized parameter t in [0, 1).
        /// </summary>
        public Vector3 EvaluatePosition(float t)
        {
            int count = _controlPoints.Count;
            if (count < 4) return Vector3.zero;

            t = Mathf.Repeat(t, 1f) * count;
            int p1 = Mathf.FloorToInt(t) % count;
            int p0 = (p1 - 1 + count) % count;
            int p2 = (p1 + 1) % count;
            int p3 = (p1 + 2) % count;

            float localT = t - Mathf.Floor(t);
            return CatmullRom(_controlPoints[p0], _controlPoints[p1], _controlPoints[p2], _controlPoints[p3], localT);
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        private void BuildLookupTable(int subdivisionsPerSegment)
        {
            _samples.Clear();
            int count = _controlPoints.Count;
            if (count < 4) return;

            int totalSteps = count * subdivisionsPerSegment;
            var rawPositions = new Vector3[totalSteps];
            for (int i = 0; i < totalSteps; i++)
            {
                float t = (float)i / totalSteps;
                rawPositions[i] = EvaluatePosition(t);
            }

            float cumulativeDist = 0f;
            for (int i = 0; i < totalSteps; i++)
            {
                int prev = (i - 1 + totalSteps) % totalSteps;
                int next = (i + 1) % totalSteps;

                if (i > 0)
                    cumulativeDist += Vector3.Distance(rawPositions[i - 1], rawPositions[i]);

                Vector3 tangent = (rawPositions[next] - rawPositions[prev]).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
                Vector3 up = Vector3.Cross(tangent, right).normalized;

                // Estimate signed horizontal curvature
                Vector3 dPrev = (rawPositions[i] - rawPositions[prev]);
                Vector3 dNext = (rawPositions[next] - rawPositions[i]);
                float segLen = Mathf.Max(0.01f, (dPrev.magnitude + dNext.magnitude) * 0.5f);

                Vector3 flatPrev = new Vector3(dPrev.x, 0f, dPrev.z).normalized;
                Vector3 flatNext = new Vector3(dNext.x, 0f, dNext.z).normalized;
                float angle = Vector3.SignedAngle(flatPrev, flatNext, Vector3.up) * Mathf.Deg2Rad;
                float signedCurvature = angle / segLen;

                _samples.Add(new SamplePoint
                {
                    Position = rawPositions[i],
                    Tangent = tangent,
                    Right = right,
                    Up = up,
                    Distance = cumulativeDist,
                    SignedCurvature = signedCurvature
                });
            }

            _totalLength = cumulativeDist + Vector3.Distance(rawPositions[totalSteps - 1], rawPositions[0]);
        }

        /// <summary>
        /// Returns the sample closest to the given arc-length distance (wrapping around TotalLength).
        /// </summary>
        public SamplePoint GetSampleAtDistance(float distance)
        {
            if (_samples.Count == 0) return default;
            float wrapped = Mathf.Repeat(distance, _totalLength);

            int low = 0;
            int high = _samples.Count - 1;
            while (low < high)
            {
                int mid = (low + high + 1) >> 1;
                if (_samples[mid].Distance <= wrapped)
                    low = mid;
                else
                    high = mid - 1;
            }
            return _samples[low];
        }

        /// <summary>Index of the sample closest to a world position.</summary>
        // shortcut: brute force over every sample (~400), fine for a few cars; use a windowed search around the last index if car count grows.
        public int FindNearestSampleIndex(Vector3 position)
        {
            int best = 0;
            float bestSq = float.MaxValue;
            for (int i = 0; i < _samples.Count; i++)
            {
                float sq = (_samples[i].Position - position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }
    }
}
