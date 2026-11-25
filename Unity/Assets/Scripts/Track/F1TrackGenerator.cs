using System.Collections.Generic;
using UnityEngine;

namespace F1Racing.Track
{
    /// <summary>
    /// Runtime description of the circuit: its centre-line spline, start line and grid slots.
    /// The meshes under this object are baked in the editor (Inspector "Build Track" button).
    /// </summary>
    public class F1TrackGenerator : MonoBehaviour
    {
        [Header("Track Dimensions (used by the editor builder)")]
        public float trackWidth = 18f;
        [Tooltip("Run-off between the track edge (white line) and the barrier, in metres.")]
        public float barrierOffset = 14f;
        public float barrierHeight = 1.1f;
        public float barrierThickness = 0.6f;
        public float kerbWidth = 1.4f;
        public float kerbHeight = 0.08f;
        public float curvatureKerbThreshold = 0.0085f;
        public int subdivisionsPerSegment = 12;

        [Header("Physics Surface (used by the editor builder)")]
        public float asphaltDynamicFriction = 1.15f;
        public float asphaltStaticFriction = 1.25f;

        /// <summary>Spline distance (m) of the start/finish line; the grid sits just behind it.</summary>
        // 170: a 23-car grid (6 m per slot) still ends on the straight, which starts ~20 m after the Parabolica exit.
        public const float StartLineDistance = 170f;

        private CatmullRomSpline _spline;
        private List<Vector3> _waypoints;

        public CatmullRomSpline Spline => _spline ??= new CatmullRomSpline(ControlPoints(), subdivisionsPerSegment);

        public IReadOnlyList<Vector3> Waypoints
        {
            get
            {
                if (_waypoints != null) return _waypoints;
                _waypoints = new List<Vector3>(Spline.Samples.Count);
                foreach (var s in Spline.Samples) _waypoints.Add(s.Position);
                return _waypoints;
            }
        }

        /// <summary>Staggered grid position <paramref name="slotIndex"/> behind the start/finish line.</summary>
        public void GetStartingGridSlot(int slotIndex, out Vector3 position, out Quaternion rotation)
        {
            float rowBackOffset = 10f + slotIndex * 6f;
            float lateralOffset = (slotIndex % 2 == 0) ? -3.2f : 3.2f;

            var sample = Spline.GetSampleAtDistance(StartLineDistance - rowBackOffset);
            position = sample.Position + sample.Right * lateralOffset + Vector3.up * 0.65f;
            rotation = Quaternion.LookRotation(sample.Tangent, Vector3.up);
        }

        /// <summary>
        /// Monza-style closed loop (clockwise), traced from Italy_Circuit.avif and scaled to ~2.5 km (45% of 5.793 km):
        /// main straight (heading +Z) -> Rettifilo chicane (T1-2) -> Curva Grande (T3) -> Roggia (T4-5) -> Lesmo 1-2
        /// (T6-7) -> Serraglio straight -> Ascari (T8-10) -> back straight -> Parabolica (T11). Point 0 is the Parabolica
        /// exit. Generated with an even ~30 m spacing; every corner keeps a centre-line radius of at least 16 m.
        /// </summary>
        private static List<Vector3> ControlPoints() => new List<Vector3>
        {
            new Vector3(0.0f, 0f, 0.0f),
            new Vector3(-4.4f, 0f, 29.8f),
            new Vector3(-7.2f, 0f, 59.8f),
            new Vector3(-8.7f, 0f, 89.8f),
            new Vector3(-9.3f, 0f, 120.0f),
            new Vector3(-9.6f, 0f, 150.1f),
            new Vector3(-9.9f, 0f, 180.2f),
            new Vector3(-10.1f, 0f, 210.3f),
            new Vector3(-10.2f, 0f, 240.4f),
            new Vector3(-10.2f, 0f, 270.6f),
            new Vector3(-10.2f, 0f, 300.7f),
            new Vector3(-10.1f, 0f, 330.8f),
            new Vector3(-10.0f, 0f, 360.9f),
            new Vector3(-9.9f, 0f, 391.0f),
            new Vector3(-9.7f, 0f, 421.2f),
            new Vector3(-9.5f, 0f, 451.3f),
            new Vector3(-9.5f, 0f, 481.4f),
            new Vector3(-6.6f, 0f, 510.7f),
            new Vector3(5.0f, 0f, 536.5f),
            new Vector3(17.8f, 0f, 560.0f),
            new Vector3(12.9f, 0f, 587.1f),
            new Vector3(1.4f, 0f, 614.4f),
            new Vector3(-6.3f, 0f, 643.3f),
            new Vector3(-8.1f, 0f, 673.4f),
            new Vector3(-6.7f, 0f, 703.4f),
            new Vector3(0.3f, 0f, 732.7f),
            new Vector3(13.0f, 0f, 759.9f),
            new Vector3(32.9f, 0f, 782.3f),
            new Vector3(59.0f, 0f, 797.1f),
            new Vector3(87.5f, 0f, 806.8f),
            new Vector3(117.1f, 0f, 812.3f),
            new Vector3(146.9f, 0f, 816.9f),
            new Vector3(176.7f, 0f, 821.3f),
            new Vector3(206.5f, 0f, 825.7f),
            new Vector3(236.3f, 0f, 829.8f),
            new Vector3(265.0f, 0f, 835.5f),
            new Vector3(285.2f, 0f, 855.1f),
            new Vector3(313.1f, 0f, 864.2f),
            new Vector3(342.3f, 0f, 870.5f),
            new Vector3(370.2f, 0f, 881.8f),
            new Vector3(397.9f, 0f, 893.7f),
            new Vector3(425.7f, 0f, 904.7f),
            new Vector3(453.2f, 0f, 896.1f),
            new Vector3(463.1f, 0f, 868.1f),
            new Vector3(467.8f, 0f, 838.3f),
            new Vector3(471.9f, 0f, 808.5f),
            new Vector3(473.1f, 0f, 778.5f),
            new Vector3(453.0f, 0f, 757.2f),
            new Vector3(428.1f, 0f, 740.3f),
            new Vector3(403.9f, 0f, 722.4f),
            new Vector3(379.9f, 0f, 704.2f),
            new Vector3(355.7f, 0f, 686.2f),
            new Vector3(331.9f, 0f, 667.8f),
            new Vector3(309.6f, 0f, 647.6f),
            new Vector3(288.6f, 0f, 626.0f),
            new Vector3(268.1f, 0f, 603.9f),
            new Vector3(248.2f, 0f, 581.3f),
            new Vector3(227.8f, 0f, 559.1f),
            new Vector3(206.9f, 0f, 537.5f),
            new Vector3(185.4f, 0f, 516.3f),
            new Vector3(163.2f, 0f, 496.0f),
            new Vector3(141.7f, 0f, 474.9f),
            new Vector3(135.9f, 0f, 446.6f),
            new Vector3(132.6f, 0f, 417.6f),
            new Vector3(112.0f, 0f, 395.9f),
            new Vector3(107.1f, 0f, 366.9f),
            new Vector3(106.9f, 0f, 336.8f),
            new Vector3(106.6f, 0f, 306.7f),
            new Vector3(106.0f, 0f, 276.5f),
            new Vector3(105.3f, 0f, 246.4f),
            new Vector3(104.7f, 0f, 216.3f),
            new Vector3(104.2f, 0f, 186.2f),
            new Vector3(103.7f, 0f, 156.1f),
            new Vector3(103.3f, 0f, 126.0f),
            new Vector3(102.9f, 0f, 95.8f),
            new Vector3(102.4f, 0f, 65.7f),
            new Vector3(101.9f, 0f, 35.6f),
            new Vector3(101.4f, 0f, 5.5f),
            new Vector3(99.8f, 0f, -24.6f),
            new Vector3(86.0f, 0f, -49.9f),
            new Vector3(58.4f, 0f, -60.1f),
            new Vector3(30.1f, 0f, -50.6f),
            new Vector3(10.6f, 0f, -28.1f),
        };
    }
}
