using UnityEngine;

namespace F1Racing.Track
{
    /// <summary>
    /// Settings for the pit complex, pit lane, podium and paddock. The buildings are baked in the
    /// editor (Inspector "Build Pit Complex" button, see Editor/Builders/PitBuild.cs).
    /// </summary>
    public class PitComplexBuilder : MonoBehaviour
    {
        [Header("Layout & Alignment")]
        public bool autoAlignToMainStraight = true;
        [Tooltip("Off by default: the infield between the main and back straights is too narrow for the paddock.")]
        public bool placeOnRightSideOfStraight = false;
        [Tooltip("Gap (m) between the outside of the track barrier and the pit wall (with a track in the scene).")]
        public float lateralOffsetFromTrackCenter = 1f;
        [Tooltip("Along the main straight from the start line (m); negative = before it.")]
        public float longitudinalOffsetZ = -45f;

        [Header("Customizable Dimensions")]
        [Range(2, 16)] public int numberOfGarages = 10;
        public float garageBayWidth = 12f;
        public float garageDepth = 16f;
        public float garageHeight = 4.6f;
        public float vipFloorHeight = 4.2f;
        public float pitLaneWidth = 12f;
        public float paddockDepth = 38f;

        [Header("Team Accent Liveries")]
        public Color[] teamAccentColors =
        {
            new Color(0.86f, 0.08f, 0.08f), // Red
            new Color(0.08f, 0.36f, 0.88f), // Blue
            new Color(1.00f, 0.50f, 0.05f), // Papaya
            new Color(0.75f, 0.78f, 0.82f), // Silver
            new Color(0.05f, 0.42f, 0.25f), // Racing Green
            new Color(0.04f, 0.72f, 0.68f), // Teal
            new Color(0.92f, 0.30f, 0.58f), // Pink
            new Color(0.10f, 0.16f, 0.38f), // Navy
            new Color(0.95f, 0.78f, 0.08f), // Yellow
            new Color(0.45f, 0.08f, 0.16f)  // Maroon
        };
    }
}
