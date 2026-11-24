using System.Collections;
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
    /// and runs the Start/Finish gantry 5-red-light countdown sequence.
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

            if (startLightPodRenderers.Count > 0)
            {
                StartCoroutine(RunStartCountdownCoroutine());
            }
        }

        private void Update()
        {
            // Quick keyboard shortcuts to test trackside LED flag states at runtime
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetFlagState(TrackFlagState.Green);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetFlagState(TrackFlagState.Yellow);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetFlagState(TrackFlagState.Red);
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

        /// <summary>
        /// F1 starting lights sequence: 3..2..1.. Lights Out.
        /// </summary>
        public IEnumerator RunStartCountdownCoroutine()
        {
            if (startLightOffMat == null || startLightOnMat == null) yield break;

            foreach (var pod in startLightPodRenderers)
            {
                if (pod != null) pod.sharedMaterial = startLightOffMat;
            }

            yield return new WaitForSeconds(1.0f);

            // Light up 5 red pods sequentially (3..2..1..)
            for (int i = 0; i < startLightPodRenderers.Count; i++)
            {
                if (startLightPodRenderers[i] != null)
                    startLightPodRenderers[i].sharedMaterial = startLightOnMat;
                yield return new WaitForSeconds(0.6f);
            }

            // Hold before Lights Out
            yield return new WaitForSeconds(1.0f);

            foreach (var pod in startLightPodRenderers)
            {
                if (pod != null) pod.sharedMaterial = startLightOffMat;
            }

            SetFlagState(TrackFlagState.Green);
        }
    }
}
