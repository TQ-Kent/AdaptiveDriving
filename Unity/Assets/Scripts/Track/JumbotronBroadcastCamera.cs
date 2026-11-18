using F1Racing.Vehicle;
using UnityEngine;

namespace F1Racing.Track
{
    /// <summary>
    /// Broadcast camera that tracks the player's F1 car and renders live footage
    /// to a shared RenderTexture displayed on trackside Jumbotron LED screens.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class JumbotronBroadcastCamera : MonoBehaviour
    {
        public Transform targetCar;
        public Vector3 cameraOffset = new Vector3(4.5f, 3.2f, 9.5f);
        public float followDamping = 6f;
        [Tooltip("Throttled render rate for 60+ FPS performance.")]
        public float refreshRateFps = 30f;

        private Camera _cam;
        private float _timer;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (targetCar == null)
            {
                F1CarController car = FindFirstObjectByType<F1CarController>();
                if (car != null) targetCar = car.transform;
            }

            // Disable automatic per-frame rendering; we trigger Render() at refreshRateFps
            if (_cam.targetTexture != null)
            {
                _cam.enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (targetCar == null) return;

            Vector3 desiredPos = targetCar.TransformPoint(cameraOffset);
            transform.position = Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-followDamping * Time.deltaTime));
            transform.LookAt(targetCar.position + Vector3.up * 0.6f);

            if (_cam.targetTexture != null)
            {
                _timer += Time.deltaTime;
                float interval = 1f / Mathf.Max(5f, refreshRateFps);
                if (_timer >= interval)
                {
                    _timer -= interval;
                    _cam.Render();
                }
            }
        }
    }
}
