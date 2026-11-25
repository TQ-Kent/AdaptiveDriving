using System.Collections.Generic;
using UnityEngine;

namespace F1Racing.Track
{
    public enum TrackFlagState
    {
        Green,
        Yellow,
        Red
    }

    /// <summary>
    /// Controls all trackside LED marshal flag panels (Green/Yellow/Red)
    /// and the Start/Finish gantry lights; RaceManager drives both.
    /// </summary>
    public class TrackFlagController : MonoBehaviour
    {
        [Header("Current Flag State")]
        public TrackFlagState currentFlag = TrackFlagState.Green;

        [Header("LED Flag Panels")]
        public List<Renderer> flagPanelRenderers = new List<Renderer>();

        [Header("Start/Finish Gantry Lights")]
        public List<Renderer> startLightPodRenderers = new List<Renderer>();

        [Header("Emissive Materials")]
        public Material greenFlagMat;
        public Material yellowFlagMat;
        public Material redFlagMat;
        public Material startLightOnMat;
        public Material startLightOffMat;

        private void Start()
        {
            SetFlagState(currentFlag);
            SetStartLights(0);
        }

        [ContextMenu("Set Green Flag")]
        public void SetGreenFlag() => SetFlagState(TrackFlagState.Green);

        [ContextMenu("Set Yellow Flag")]
        public void SetYellowFlag() => SetFlagState(TrackFlagState.Yellow);

        [ContextMenu("Set Red Flag")]
        public void SetRedFlag() => SetFlagState(TrackFlagState.Red);

        public void SetFlagState(TrackFlagState state)
        {
            currentFlag = state;
            Material targetMat = state switch
            {
                TrackFlagState.Green => greenFlagMat,
                TrackFlagState.Yellow => yellowFlagMat,
                TrackFlagState.Red => redFlagMat,
                _ => greenFlagMat
            };

            if (targetMat == null) return;

            for (int i = 0; i < flagPanelRenderers.Count; i++)
            {
                if (flagPanelRenderers[i] != null)
                    flagPanelRenderers[i].sharedMaterial = targetMat;
            }
        }

        /// <summary>Lights the first <paramref name="lit"/> gantry pods (0 = lights out). Driven by RaceManager's countdown.</summary>
        public void SetStartLights(int lit)
        {
            if (startLightOffMat == null || startLightOnMat == null) return;
            for (int i = 0; i < startLightPodRenderers.Count; i++)
                if (startLightPodRenderers[i] != null)
                    startLightPodRenderers[i].sharedMaterial = i < lit ? startLightOnMat : startLightOffMat;
        }
    }
}
