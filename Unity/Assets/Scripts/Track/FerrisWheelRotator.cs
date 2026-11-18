using System.Collections.Generic;
using UnityEngine;

namespace F1Racing.Track
{
    /// <summary>
    /// Smoothly rotates the Ferris Wheel hub while keeping passenger gondolas upright.
    /// </summary>
    public class FerrisWheelRotator : MonoBehaviour
    {
        public Transform wheelHub;
        public float degreesPerSecond = 8f;
        public List<Transform> gondolas = new List<Transform>();

        private void Update()
        {
            if (wheelHub == null) return;

            wheelHub.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime, Space.Self);

            // Counter-rotate gondolas so cabins stay level with the horizon
            for (int i = 0; i < gondolas.Count; i++)
            {
                if (gondolas[i] != null)
                {
                    gondolas[i].rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                }
            }
        }
    }
}
