using F1Racing.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace F1Racing.EditorTools
{
    internal static class BuildUtil
    {
        /// <summary>Deletes every child of <paramref name="root"/> (editor, not play mode).</summary>
        public static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.GetChild(i).gameObject);
        }

        /// <summary>Track centre-line bounds on the ground plane: x = world x, y = world z.</summary>
        public static Rect TrackBounds(F1Racing.Track.F1TrackGenerator track)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var s in track.Spline.Samples)
            {
                minX = Mathf.Min(minX, s.Position.x); maxX = Mathf.Max(maxX, s.Position.x);
                minZ = Mathf.Min(minZ, s.Position.z); maxZ = Mathf.Max(maxZ, s.Position.z);
            }
            return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
        }

        /// <summary>
        /// Largest offset (m) from the centre line on <paramref name="side"/> (+1 right, -1 left) at each sample before an
        /// offset curve folds over itself: 85% of the tightest inside radius within +-25 m. Large on the outside of a corner.
        /// </summary>
        public static float[] SideOffsetLimit(F1Racing.Track.CatmullRomSpline spline, float side)
        {
            var samples = spline.Samples;
            int n = samples.Count;
            float step = spline.TotalLength / n;
            int window = Mathf.Max(1, Mathf.RoundToInt(25f / step));
            var limit = new float[n];
            for (int i = 0; i < n; i++)
            {
                float k = 0f;
                for (int j = -window; j <= window; j++) k = Mathf.Max(k, side * samples[(i + j + n) % n].SignedCurvature);
                limit[i] = k > 1e-4f ? 0.85f / k : 1e4f;
            }
            return limit;
        }

        /// <summary>
        /// A trackside spot at <paramref name="distance"/> along the spline, <paramref name="beyondBarrierM"/> outside the barrier
        /// on <paramref name="side"/>; <paramref name="facingTrack"/> points forward toward the track.
        /// </summary>
        public static Vector3 Trackside(F1Racing.Track.F1TrackGenerator track, float distance, float side, float beyondBarrierM, out Quaternion facingTrack)
        {
            var s = track.Spline.GetSampleAtDistance(distance);
            facingTrack = Quaternion.LookRotation(-side * s.Right, Vector3.up);
            return s.Position + s.Right * (side * (track.trackWidth * 0.5f + track.barrierOffset + track.barrierThickness + beyondBarrierM));
        }

        /// <summary>Draws the default inspector plus a rebuild button.</summary>
        public static void InspectorWithButton(Editor editor, string label, System.Action rebuild)
        {
            editor.DrawDefaultInspector();
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (!GUILayout.Button(label, GUILayout.Height(28))) return;
            }
            rebuild();
            EditorSceneManager.MarkSceneDirty(((Component)editor.target).gameObject.scene);
        }
    }

    /// <summary>
    /// One click after the track layout changes: rebuilds every generated part from the spline and moves the
    /// hand-placed props that belong to the old layout out of the way (disabled, not deleted, so nothing is lost).
    /// </summary>
    internal static class RebuildTrackMenu
    {
        private const string OldPropsRoot = "Track_Environment_Props";
        private const string MarshalGroup = "06_Marshal_Posts";
        // Outside of the braking zones / corners: T1, Roggia, Lesmo 1, Ascari, Parabolica (spline m, side +1 right / -1 left).
        private static readonly float[] MarshalDistances = { 480f, 1030f, 1230f, 1810f, 2330f };
        private static readonly float[] MarshalSides = { -1f, 1f, -1f, 1f, -1f };

        [MenuItem("Tools/Track/Rebuild Monza Track (track, scenery, pit)")]
        private static void Rebuild()
        {
            if (Application.isPlaying) { Debug.LogWarning("Stop Play Mode before rebuilding the track."); return; }
            var track = Object.FindAnyObjectByType<F1TrackGenerator>();
            if (track == null) { Debug.LogError("No F1TrackGenerator in the open scene."); return; }

            TrackMeshBuild.Build(track);
            var scenery = Object.FindAnyObjectByType<CircuitSceneryPopulator>();
            if (scenery != null) SceneryBuild.Build(scenery);
            var pit = Object.FindAnyObjectByType<PitComplexBuilder>();
            if (pit != null) PitBuild.Build(pit);

            int disabled = RetireOldLayoutProps(track);
            RelinkStartLights(track);

            Scene scene = track.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Track rebuilt: {track.Spline.TotalLength:F0} m, {track.Spline.Samples.Count} samples; " +
                      $"{disabled} old-layout prop groups disabled (re-enable under {OldPropsRoot} if wanted). Scene saved.");
        }

        /// <summary>The old props were hand-built around the previous layout: their barriers cross the new track.</summary>
        private static int RetireOldLayoutProps(F1TrackGenerator track)
        {
            GameObject root = GameObject.Find(OldPropsRoot);
            if (root == null) return 0;
            int disabled = 0;
            foreach (Transform group in root.transform)
            {
                if (group.name == MarshalGroup)
                {
                    // Keep the marshal posts (their LED flag panels show the track flag); move them to the new corners.
                    for (int i = 0; i < group.childCount && i < MarshalDistances.Length; i++)
                    {
                        Vector3 pos = BuildUtil.Trackside(track, MarshalDistances[i], MarshalSides[i], 2f, out Quaternion rot);
                        Undo.RecordObject(group.GetChild(i), "Move marshal post");
                        group.GetChild(i).SetPositionAndRotation(new Vector3(pos.x, 0f, pos.z), rot);
                    }
                    for (int i = MarshalDistances.Length; i < group.childCount; i++) group.GetChild(i).gameObject.SetActive(false);
                    continue;
                }
                if (!group.gameObject.activeSelf) continue;
                Undo.RecordObject(group.gameObject, "Disable old track props");
                group.gameObject.SetActive(false);
                disabled++;
            }
            return disabled;
        }

        /// <summary>RaceManager drives the start lights through TrackFlagController: point it at the rebuilt gantry's pods.</summary>
        private static void RelinkStartLights(F1TrackGenerator track)
        {
            var flags = Object.FindAnyObjectByType<TrackFlagController>();
            if (flags == null) return;
            Undo.RecordObject(flags, "Relink start lights");
            flags.startLightPodRenderers.Clear();
            for (int i = 1; i <= 5; i++)
            {
                Transform pod = FindDeep(track.transform, $"StartLight_{i}");
                if (pod != null) flags.startLightPodRenderers.Add(pod.GetComponent<Renderer>());
            }
            flags.flagPanelRenderers.RemoveAll(r => r == null || !r.gameObject.activeInHierarchy);
            EditorUtility.SetDirty(flags);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }

    [CustomEditor(typeof(F1TrackGenerator))]
    internal class F1TrackGeneratorEditor : Editor
    {
        public override void OnInspectorGUI() =>
            BuildUtil.InspectorWithButton(this, "Build Track", () => TrackMeshBuild.Build((F1TrackGenerator)target));
    }

    [CustomEditor(typeof(CircuitSceneryPopulator))]
    internal class CircuitSceneryPopulatorEditor : Editor
    {
        public override void OnInspectorGUI() =>
            BuildUtil.InspectorWithButton(this, "Build Scenery", () => SceneryBuild.Build((CircuitSceneryPopulator)target));
    }

    [CustomEditor(typeof(PitComplexBuilder))]
    internal class PitComplexBuilderEditor : Editor
    {
        public override void OnInspectorGUI() =>
            BuildUtil.InspectorWithButton(this, "Build Pit Complex", () => PitBuild.Build((PitComplexBuilder)target));
    }
}
