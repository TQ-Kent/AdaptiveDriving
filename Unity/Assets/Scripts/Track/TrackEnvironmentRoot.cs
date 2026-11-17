using System.Collections.Generic;
using UnityEngine;

namespace F1Racing.Track
{
    /// <summary>
    /// Host component for procedural trackside safety, race infrastructure, and nature props.
    /// Uses F1TrackGenerator's closed-loop spline by default, or an optional list of waypoint Transforms.
    /// Select this GameObject and click "Build Track Props" in the Inspector.
    /// </summary>
    public class TrackEnvironmentRoot : MonoBehaviour
    {
        [Header("Track Source")]
        public F1TrackGenerator trackGenerator;
        [Tooltip("Optional fallback if TrackGenerator is not assigned.")]
        public List<Transform> customWaypointTransforms = new List<Transform>();

        [Header("Dimensions & Spacing")]
        public float trackWidth = 16f;
        public float runOffWidth = 9.5f;
        public float barrierOffsetFromEdge = 2.8f;
        public float catchFenceHeight = 2.4f;
        public float sharpCornerCurvatureThreshold = 0.011f;
        public float marshalPostIntervalMeters = 250f;

        [Header("Nature & Ground")]
        public int treeCount = 160;
        public float minTreeClearanceFromTrack = 22f;
        public float forestSpreadRadius = 260f;
        public int randomSeed = 42;
    }
}
