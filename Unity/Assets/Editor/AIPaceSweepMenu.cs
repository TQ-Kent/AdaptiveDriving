using F1Racing.AI;
using UnityEditor;

/// <summary>Enters Play Mode in the open race scene and runs the AI pace calibration sweep.</summary>
public static class AIPaceSweepMenu
{
    [MenuItem("Tools/AI/Run Pace Sweep")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            UnityEngine.Debug.LogWarning("[PaceSweep] stop Play Mode first, then run the sweep.");
            return;
        }
        SessionState.SetBool(PaceSweep.RequestKey, true);
        EditorApplication.EnterPlaymode();
    }
}
