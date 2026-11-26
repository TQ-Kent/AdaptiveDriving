using F1Racing.AI.Driving;
using F1Racing.Race;
using F1Racing.Track;
using F1Racing.Vehicle;
using UnityEngine;

namespace F1Racing.AI
{
    /// <summary>
    /// Driving-assist style line painted on the track along the AI's racing line: green = full throttle,
    /// yellow = braking zone ahead, red = braking. Colours come from one AI's speed plan. Visual only: no collider,
    /// nothing reads it. Toggle with L or the menu button (RaceSettings.ShowRacingLine).
    /// shortcut: colours are from the first AI's plan at race start; refresh on its corner learning if they drift visibly.
    /// </summary>
    public class RacingLineView : MonoBehaviour
    {
        private const float DashEveryM = 6f, DashLengthM = 3f, HalfWidthM = 0.45f, LiftM = 0.04f;
        private const float BrakingNowM = 10f, BrakingSoonM = 50f, SlowerMs = 0.5f;
        private static readonly Color Green = new Color(0.1f, 0.9f, 0.2f), Yellow = new Color(1f, 0.85f, 0.1f), Red = new Color(0.95f, 0.1f, 0.1f);

        private MeshRenderer _renderer;

        public static RacingLineView Create(F1TrackGenerator track, AIDriver driver)
        {
            var view = new GameObject("AI_RacingLine").AddComponent<RacingLineView>();
            view.Build(track.Spline, driver);
            return view;
        }

        private void Build(CatmullRomSpline spline, AIDriver driver)
        {
            int dashes = Mathf.FloorToInt(spline.TotalLength / DashEveryM);
            var verts = new Vector3[dashes * 4];
            var colors = new Color[dashes * 4];
            var tris = new int[dashes * 6];
            for (int i = 0; i < dashes; i++)
            {
                float d0 = i * DashEveryM, d1 = d0 + DashLengthM;
                Color c = ColourAt(driver, d0);
                Vector3 a = PointAt(spline, driver.Line, d0), b = PointAt(spline, driver.Line, d1);
                Vector3 side = Vector3.Cross(Vector3.up, (b - a).normalized) * HalfWidthM;
                int v = i * 4;
                verts[v] = a - side; verts[v + 1] = a + side; verts[v + 2] = b + side; verts[v + 3] = b - side;
                for (int k = 0; k < 4; k++) colors[v + k] = c;
                int t = i * 6;
                tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                tris[t + 3] = v; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
            }
            var mesh = new Mesh { name = "AI_RacingLine", vertices = verts, colors = colors, triangles = tris };
            mesh.RecalculateBounds();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            Shader shader = Shader.Find("Sprites/Default"); // unlit, uses vertex colours
            _renderer.sharedMaterial = new Material(shader) { name = "AI_RacingLine_Mat" };
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.enabled = RaceSettings.ShowRacingLine;
        }

        private static Vector3 PointAt(CatmullRomSpline spline, RacingLine line, float d)
        {
            var s = spline.GetSampleAtDistance(d);
            return s.Position + s.Right * line.OffsetAt(d) + Vector3.up * LiftM;
        }

        /// <summary>Red while the plan is slowing down here, yellow when it slows within 50 m, else green.</summary>
        private static Color ColourAt(AIDriver driver, float d)
        {
            float v = driver.TargetSpeedAt(d);
            if (driver.TargetSpeedAt(d + BrakingNowM) < v - SlowerMs) return Red;
            return driver.TargetSpeedAt(d + BrakingSoonM) < v - SlowerMs ? Yellow : Green;
        }

        private void Update()
        {
            if (GameInput.Down(KeyCode.L)) RaceSettings.ShowRacingLine = !RaceSettings.ShowRacingLine;
            _renderer.enabled = RaceSettings.ShowRacingLine;
        }
    }
}
