using System.Collections.Generic;
using F1Racing.Track;
using UnityEngine;

namespace F1Racing.AI.Driving
{
    /// <summary>
    /// D05 data: a smooth racing line, stored as a lateral offset from the centre line every <see cref="SpacingM"/>.
    /// Built once per track: it minimises the bending energy (sum of curvature^2 * ds) of the line inside the
    /// usable width, by trying smooth Gaussian bumps from wide to narrow (single-point moves only make kinks).
    /// Wider radius means the same grip gives a higher corner speed, which is how the player is faster than a
    /// centre-line driver. Same physics for everyone; this only changes where the AI aims.
    /// </summary>
    public sealed class RacingLine
    {
        public const float SpacingM = 5f;
        private static readonly int[] Scales = { 12, 6, 3, 1 };
        private const int PassesPerScale = 25;

        private readonly float[] _offset, _distance, _curvature;
        private readonly float _length;

        /// <summary>Spline distance of each line point.</summary>
        public IReadOnlyList<float> Distances => _distance;
        /// <summary>Signed curvature (1/m) of the line itself, same sign rule as the spline (positive turns right).</summary>
        public IReadOnlyList<float> Curvatures => _curvature;

        private RacingLine(float[] distance, float[] offset, float[] curvature, float length)
        {
            _distance = distance;
            _offset = offset;
            _curvature = curvature;
            _length = length;
        }

        public static RacingLine Build(CatmullRomSpline spline, float maxOffsetM)
        {
            float length = spline.TotalLength;
            int m = Mathf.Max(8, Mathf.RoundToInt(length / SpacingM));
            var centre = new Vector2[m];
            var right = new Vector2[m];
            var distance = new float[m];
            for (int i = 0; i < m; i++)
            {
                distance[i] = i * length / m;
                var s = spline.GetSampleAtDistance(distance[i]);
                centre[i] = new Vector2(s.Position.x, s.Position.z);
                right[i] = new Vector2(s.Right.x, s.Right.z).normalized;
            }
            float[] offset = Optimize(centre, right, maxOffsetM);
            return new RacingLine(distance, offset, CurvaturesOf(Points(centre, right, offset)), length);
        }

        /// <summary>Lateral offset (m, + = toward Right) of the line at <paramref name="splineDistance"/>, linearly interpolated.</summary>
        public float OffsetAt(float splineDistance)
        {
            float f = Mathf.Repeat(splineDistance, _length) / _length * _offset.Length;
            int i = (int)f % _offset.Length;
            return Mathf.Lerp(_offset[i], _offset[(i + 1) % _offset.Length], f - (int)f);
        }

        /// <summary>Pure core (checked in SeamSelfCheck): offsets within +-<paramref name="maxOffsetM"/> that lower the line's bending energy.</summary>
        public static float[] Optimize(Vector2[] centre, Vector2[] right, float maxOffsetM)
        {
            int m = centre.Length;
            var o = new float[m];
            foreach (int s in Scales)
            {
                int half = 2 * s;
                if (2 * half + 3 > m) continue; // window would wrap onto itself
                var bump = new float[2 * half + 1];
                for (int k = 0; k < bump.Length; k++) bump[k] = Mathf.Exp(-Mathf.Pow((k - half) / (float)s, 2f));
                var old = new float[bump.Length];
                var best = new float[bump.Length];
                float step = 0.5f;
                for (int pass = 0; pass < PassesPerScale; pass++)
                {
                    for (int i = 0; i < m; i++)
                    {
                        int lo = i - half - 1, hi = i + half + 1;
                        for (int k = 0; k < bump.Length; k++) old[k] = o[Wrap(i + k - half, m)];
                        float bestE = Energy(centre, right, o, lo, hi);
                        bool improved = false;
                        for (int sign = 1; sign >= -1; sign -= 2)
                        {
                            for (int k = 0; k < bump.Length; k++)
                                o[Wrap(i + k - half, m)] = Mathf.Clamp(old[k] + sign * step * bump[k], -maxOffsetM, maxOffsetM);
                            float e = Energy(centre, right, o, lo, hi);
                            if (e < bestE - 1e-9f)
                            {
                                bestE = e;
                                improved = true;
                                for (int k = 0; k < bump.Length; k++) best[k] = o[Wrap(i + k - half, m)];
                            }
                        }
                        float[] keep = improved ? best : old;
                        for (int k = 0; k < bump.Length; k++) o[Wrap(i + k - half, m)] = keep[k];
                    }
                    step = Mathf.Max(0.05f, step * 0.85f);
                }
            }
            return o;
        }

        public static Vector2[] Points(Vector2[] centre, Vector2[] right, float[] offset)
        {
            var p = new Vector2[centre.Length];
            for (int i = 0; i < p.Length; i++) p[i] = centre[i] + right[i] * offset[i];
            return p;
        }

        public static float[] CurvaturesOf(Vector2[] p)
        {
            int m = p.Length;
            var k = new float[m];
            for (int i = 0; i < m; i++) k[i] = Curvature(p[Wrap(i - 1, m)], p[i], p[Wrap(i + 1, m)], out _);
            return k;
        }

        private static float Energy(Vector2[] centre, Vector2[] right, float[] o, int lo, int hi)
        {
            int m = centre.Length;
            float e = 0f;
            for (int j = lo; j <= hi; j++)
            {
                int a = Wrap(j - 1, m), b = Wrap(j, m), c = Wrap(j + 1, m);
                float k = Curvature(centre[a] + right[a] * o[a], centre[b] + right[b] * o[b], centre[c] + right[c] * o[c], out float ds);
                e += k * k * ds;
            }
            return e;
        }

        /// <summary>Signed curvature at b, Vector2 = (x, z). Matches Vector3.SignedAngle about up: positive turns toward +Right.</summary>
        private static float Curvature(Vector2 a, Vector2 b, Vector2 c, out float ds)
        {
            Vector2 u = b - a, v = c - b;
            ds = Mathf.Max(0.01f, (u.magnitude + v.magnitude) * 0.5f);
            return Mathf.Atan2(u.y * v.x - u.x * v.y, Vector2.Dot(u, v)) / ds;
        }

        private static int Wrap(int i, int m) => ((i % m) + m) % m;
    }
}
