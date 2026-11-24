using UnityEngine;

namespace F1Racing.Track
{
    public enum CircuitTheme
    {
        Parkland,
        TwilightMarina
    }

    /// <summary>
    /// Settings for the trackside scenery. The scenery itself is baked in the editor
    /// (Inspector "Build Scenery" button, see Editor/Builders/SceneryBuild.cs).
    /// </summary>
    public class CircuitSceneryPopulator : MonoBehaviour
    {
        [Header("Theme & Track Reference")]
        public CircuitTheme circuitTheme = CircuitTheme.Parkland;
        public F1TrackGenerator trackGenerator;
        public int randomSeed = 77;

        [Header("Jumbotron Live Broadcast")]
        public Vector2Int jumbotronResolution = new Vector2Int(512, 288);
        public float jumbotronRefreshFps = 30f;

        [Header("Perimeter Density")]
        public int parklandTreeCount = 120;
        public int parklandHillCount = 14;
        public int marinaFloodlightCount = 12;
        public int marinaPalmCount = 65;
        public int marinaSkyscraperCount = 26;
        public int marinaYachtCount = 7;
    }
}
