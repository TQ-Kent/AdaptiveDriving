using System.Collections.Generic;
using F1Racing.Track;
using F1Racing.Vehicle;
using UnityEngine;

namespace F1Racing.UI
{
    /// <summary>
    /// North-up circuit minimap in the bottom-left corner: track outline, start line
    /// and both cars. Press N to toggle. Created by GameModeManager.
    /// </summary>
    public class F1MiniMap : MonoBehaviour
    {
        public F1TrackGenerator track;
        public int size = 220;          // on-screen pixels
        public int margin = 16;
        public KeyCode toggleKey = KeyCode.N;
        public bool visible = true;

        private const int TexSize = 512;
        private Texture2D _trackTex, _dot, _panel;
        private Rect _worldRect;        // x/z bounds, square
        private F1CarController[] _cars = new F1CarController[0];
        private float _nextCarScan;
        private GUIStyle _label;

        /// <summary>Created by GameModeManager on every scene load.</summary>
        public static F1MiniMap Create(F1TrackGenerator track)
        {
            var existing = FindAnyObjectByType<F1MiniMap>();
            if (existing != null) return existing;
            var map = new GameObject("F1MiniMap").AddComponent<F1MiniMap>();
            map.track = track;
            return map;
        }

        private void Start()
        {
            if (track == null) track = FindAnyObjectByType<F1TrackGenerator>();
            BuildTextures();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) visible = !visible;
            if (Time.unscaledTime >= _nextCarScan)
            {
                _cars = FindObjectsByType<F1CarController>();
                _nextCarScan = Time.unscaledTime + 2f;
            }
        }

        private void BuildTextures()
        {
            _dot = MakeDisc(32);
            _panel = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            _panel.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.08f, 0.72f));
            _panel.Apply();

            if (track == null) return;
            if (track.Spline == null) track.RebuildSplineOnly();
            IReadOnlyList<Vector3> pts = track.Waypoints;
            if (pts == null || pts.Count < 2) return;

            // Square world bounds with padding so the map keeps its aspect.
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vector3 p in pts)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            float span = Mathf.Max(maxX - minX, maxZ - minZ) + 60f;
            Vector2 c = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
            _worldRect = new Rect(c.x - span * 0.5f, c.y - span * 0.5f, span, span);

            var px = new Color32[TexSize * TexSize];
            float pxPerMetre = TexSize / span;
            float trackHalfWidthPx = Mathf.Max(2f, 8f * pxPerMetre);  // 16 m wide circuit
            Stroke(px, pts, trackHalfWidthPx + 2.5f, new Color32(15, 15, 18, 255));
            Stroke(px, pts, trackHalfWidthPx, new Color32(200, 205, 215, 255));

            // Start/finish marker.
            if (track.Spline != null)
            {
                var s = track.Spline.GetSampleAtDistance(F1TrackGenerator.StartLineDistance);
                Vector2 a = ToTex(s.Position - s.Right * 10f), b = ToTex(s.Position + s.Right * 10f);
                Line(px, a, b, 2.2f, new Color32(255, 70, 60, 255));
            }

            _trackTex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
            _trackTex.SetPixels32(px);
            _trackTex.Apply();
        }

        private Vector2 ToTex(Vector3 world) => new Vector2(
            (world.x - _worldRect.xMin) / _worldRect.width * TexSize,
            (world.z - _worldRect.yMin) / _worldRect.height * TexSize);

        private void Stroke(Color32[] px, IReadOnlyList<Vector3> pts, float radius, Color32 col)
        {
            for (int i = 0; i < pts.Count; i++)
                Line(px, ToTex(pts[i]), ToTex(pts[(i + 1) % pts.Count]), radius, col);
        }

        private static void Line(Color32[] px, Vector2 a, Vector2 b, float r, Color32 col)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.75f));
            for (int k = 0; k <= steps; k++) Disc(px, Vector2.Lerp(a, b, k / (float)steps), r, col);
        }

        private static void Disc(Color32[] px, Vector2 c, float r, Color32 col)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(c.x - r - 1)), x1 = Mathf.Min(TexSize - 1, Mathf.CeilToInt(c.x + r + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(c.y - r - 1)), y1 = Mathf.Min(TexSize - 1, Mathf.CeilToInt(c.y + r + 1));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                float a = Mathf.Clamp01(r + 0.5f - d);
                if (a <= 0f) continue;
                int i = y * TexSize + x;
                px[i] = Color32.Lerp(px[i], col, a);
            }
        }

        private static Texture2D MakeDisc(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            float r = n * 0.5f - 1f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f));
                t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f)));
            }
            t.Apply();
            return t;
        }

        private void OnGUI()
        {
            if (!visible || _trackTex == null) return;
            Rect area = new Rect(margin, Screen.height - margin - size, size, size);
            GUI.DrawTexture(area, _panel);
            GUI.DrawTexture(area, _trackTex);

            Color prev = GUI.color;
            foreach (F1CarController car in _cars)
            {
                if (car == null) continue;
                GUI.color = Color.black;
                DrawMarker(area, car.transform.position, 12f);
                GUI.color = CarColor(car);
                DrawMarker(area, car.transform.position, 9f);
            }
            GUI.color = prev;

            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(area.x + 6, area.y + 2, size, 18), "MAP  [N]", _label);
        }

        private void DrawMarker(Rect area, Vector3 world, float diameter)
        {
            float u = (world.x - _worldRect.xMin) / _worldRect.width;
            float v = (world.z - _worldRect.yMin) / _worldRect.height;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return;
            float x = area.x + u * area.width, y = area.yMax - v * area.height; // GUI y grows downward
            GUI.DrawTexture(new Rect(x - diameter * 0.5f, y - diameter * 0.5f, diameter, diameter), _dot);
        }

        private static Color CarColor(F1CarController car)
        {
            string n = car.carName ?? car.name;
            if (n.Contains("Red")) return new Color(1f, 0.2f, 0.15f);
            if (n.Contains("Blue")) return new Color(0.2f, 0.55f, 1f);
            return Color.white;
        }
    }
}
