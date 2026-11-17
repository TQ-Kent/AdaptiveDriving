using F1Racing.Track;
using UnityEditor;
using UnityEngine;

namespace F1Racing.Editor
{
    /// <summary>
    /// Custom Inspector & menu launcher for PitComplexBuilder.
    /// </summary>
    [CustomEditor(typeof(PitComplexBuilder))]
    public class PitComplexBuilderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            PitComplexBuilder builder = (PitComplexBuilder)target;

            GUI.backgroundColor = new Color(0.25f, 0.78f, 0.95f);
            if (GUILayout.Button("Build Pit Complex", GUILayout.Height(36)))
            {
                builder.BuildPitComplex();
                EditorUtility.SetDirty(builder.gameObject);
            }
            GUI.backgroundColor = Color.white;
        }

        [MenuItem("F1 Racing/Build Pit Complex")]
        public static void BuildPitComplexFromMenu()
        {
            PitComplexBuilder builder = Object.FindFirstObjectByType<PitComplexBuilder>();
            if (builder == null)
            {
                GameObject go = new GameObject("F1_Pit_Complex");
                builder = go.AddComponent<PitComplexBuilder>();
            }
            builder.BuildPitComplex();
            EditorUtility.SetDirty(builder.gameObject);
            Selection.activeGameObject = builder.gameObject;
        }
    }
}
