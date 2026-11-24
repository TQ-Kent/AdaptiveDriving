using F1Racing.Vehicle;
using UnityEngine;

namespace F1Racing.CameraSystem
{
    /// <summary>
    /// Smooth damped F1 chase camera with speed-based dynamic FOV scaling,
    /// C-key target toggle between Car 1 and Car 2, and V-key Split-Screen mode.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class F1ChaseCamera : MonoBehaviour
    {
        [Header("Targets")]
        public F1CarController car1Target;
        public F1CarController car2Target;
        public Camera secondarySplitCamera;
        public bool isSecondaryCamera = false;

        [Header("Follow Offsets")]
        public Vector3 followOffset = new Vector3(0f, 2.35f, -6.2f);
        public Vector3 lookAheadOffset = new Vector3(0f, 0.95f, 6.5f);
        public float positionDamping = 10f;
        public float rotationDamping = 9f;

        [Header("Dynamic FOV")]
        public float minFov = 60f;
        public float maxFov = 82f;
        public float drsBonusFov = 5f;
        public float topSpeedForFov = 325f;

        public bool IsSplitScreen { get; private set; }
        public F1CarController ActiveTarget { get; private set; }

        private Camera _cam;
        private int _activeCarIndex = 0;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            ActiveTarget = isSecondaryCamera ? car2Target : car1Target;
            ApplySplitScreenLayout(false);
        }

        private void Update()
        {
            if (isSecondaryCamera) return;

            if (Input.GetKeyDown(KeyCode.V))
            {
                IsSplitScreen = !IsSplitScreen;
                ApplySplitScreenLayout(IsSplitScreen);
            }

            if (!IsSplitScreen && Input.GetKeyDown(KeyCode.C))
            {
                _activeCarIndex = (_activeCarIndex + 1) % 2;
                ActiveTarget = (_activeCarIndex == 0) ? car1Target : car2Target;
            }
        }

        private void LateUpdate()
        {
            F1CarController target = isSecondaryCamera ? car2Target : ActiveTarget;
            if (target == null) return;

            Vector3 desiredPos = target.transform.TransformPoint(followOffset);
            transform.position = Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-positionDamping * Time.deltaTime));

            Vector3 lookTarget = target.transform.TransformPoint(lookAheadOffset);
            Quaternion desiredRot = Quaternion.LookRotation((lookTarget - transform.position).normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, 1f - Mathf.Exp(-rotationDamping * Time.deltaTime));

            float speedNorm = Mathf.Clamp01(target.CurrentSpeedKmh / topSpeedForFov);
            float targetFov = Mathf.Lerp(minFov, maxFov, speedNorm) + (target.IsDrsActive ? drsBonusFov : 0f);
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, targetFov, Time.deltaTime * 6f);
        }

        private void ApplySplitScreenLayout(bool split)
        {
            if (isSecondaryCamera) return;

            if (split && secondarySplitCamera != null)
            {
                _activeCarIndex = 0;
                ActiveTarget = car1Target;
                _cam.rect = new Rect(0f, 0.5f, 1f, 0.5f);
                secondarySplitCamera.gameObject.SetActive(true);
                secondarySplitCamera.rect = new Rect(0f, 0f, 1f, 0.5f);
            }
            else
            {
                _cam.rect = new Rect(0f, 0f, 1f, 1f);
                if (secondarySplitCamera != null)
                {
                    secondarySplitCamera.gameObject.SetActive(false);
                }
            }
        }
    }
}
