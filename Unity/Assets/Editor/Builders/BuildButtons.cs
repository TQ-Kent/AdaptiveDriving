using F1Racing.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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
