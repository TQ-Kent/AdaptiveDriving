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
        public float trackWidth = 16f;
        public float barrierOffset = 2.5f;
        public float barrierHeight = 1.1f;
        public float barrierThickness = 0.6f;
        public float kerbWidth = 1.4f;
        public float kerbHeight = 0.08f;
        public float curvatureKerbThreshold = 0.0085f;
        public int subdivisionsPerSegment = 24;

        [Header("Physics Surface (used by the editor builder)")]
        public float asphaltDynamicFriction = 1.15f;
        public float asphaltStaticFriction = 1.25f;

        /// <summary>Spline distance (m) of the start/finish line; the grid sits just behind it.</summary>
        public const float StartLineDistance = 140f;

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

        /// <summary>Closed loop: start straight, uphill S-bend, hairpin, downhill chicane, parabolica.</summary>
        private static List<Vector3> ControlPoints() => new List<Vector3>
        {
            // Main Start/Finish Straight (heading +Z)
            new Vector3(0f,    0.0f, -140f),
            new Vector3(0f,    0.0f,    0f),
            new Vector3(0f,    0.0f,  150f),

            // Turn 1-2-3: Sweeping uphill right-left-right S-bend
            new Vector3(38f,   3.5f,  255f),
            new Vector3(115f,  8.0f,  295f),
            new Vector3(185f, 11.5f,  250f),

            // Elevated Back Straight
            new Vector3(260f, 12.0f,  195f),
            new Vector3(320f, 11.0f,  110f),

            // Tight Hairpin Turn (180-degree right-hander)
            new Vector3(355f,  9.0f,   20f),
            new Vector3(335f,  7.5f,  -45f),
            new Vector3(270f,  6.5f,  -20f),

            // Downhill Run into Fast Chicane
            new Vector3(215f,  4.0f,  -75f),
            new Vector3(185f,  2.0f, -150f),
            new Vector3(135f,  1.0f, -135f),
            new Vector3(105f,  0.5f, -205f),

            // Final Sweeping Parabolica onto Main Straight
            new Vector3(45f,   0.0f, -235f),
        };
    }
}
