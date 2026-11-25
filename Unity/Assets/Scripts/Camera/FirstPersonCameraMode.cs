using F1Racing.Vehicle;
using F1Racing.Vehicle.Characters;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1Racing.CameraSystem
{
    /// <summary>
    /// First-person view for the main camera (press P). Runs after <see cref="F1ChaseCamera"/>
    /// and overrides its pose while active. Eye point: the character's eyes when a Fun-mode
    /// model is shown (the model is hidden from this camera only, keeping its shadow and the
    /// front-camera view), otherwise the driver's helmet in the cockpit.
    /// Follows whichever car the chase camera is targeting (C key).
    /// </summary>
    [DefaultExecutionOrder(500)]
    [RequireComponent(typeof(Camera))]
    public class FirstPersonCameraMode : MonoBehaviour
    {
        public KeyCode toggleKey = KeyCode.P;
        public bool active;

        [Header("Cockpit (original car)")]
        public Vector3 cockpitEyeOffset = new Vector3(0f, 0.12f, 0.02f); // from the helmet centre, car space
        public Vector3 cockpitFallbackEye = new Vector3(0f, 0.68f, 0.05f);

        [Header("Character")]
        public float eyeForward = 0.05f;  // metres in front of the eye surface
        public float eyeUp = 0f;
        [Tooltip("Hide the character from this camera while in first person (keeps its shadow).")]
        public bool hideCharacter = true;

        [Header("Lens")]
        public float fov = 78f;
        public float nearClip = 0.03f;
        public float pitchDown = 3f;      // slight downward tilt so the road fills the view

        private Camera _cam;
        private F1ChaseCamera _chase;
        private float _defaultNear;
        private Renderer _hiddenRenderer;    // character renderer to hide while this camera renders
        private ShadowCastingMode _savedShadowMode;

        public static FirstPersonCameraMode AttachToMainChaseCamera()
        {
            foreach (F1ChaseCamera chase in FindObjectsByType<F1ChaseCamera>())
            {
                if (chase.isSecondaryCamera) continue;
                var existing = chase.GetComponent<FirstPersonCameraMode>();
                return existing != null ? existing : chase.gameObject.AddComponent<FirstPersonCameraMode>();
            }
            return null;
        }

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _chase = GetComponent<F1ChaseCamera>();
            _defaultNear = _cam.nearClipPlane;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) SetActive(!active);
        }

        public void SetActive(bool on)
        {
            if (active == on) return;
            active = on;
            _cam.nearClipPlane = active ? nearClip : _defaultNear;
            if (_chase != null) _chase.OnFirstPersonChanged(active);
        }

        private void LateUpdate()
        {
            _hiddenRenderer = null;
            if (!active) return;
            F1CarController car = _chase != null ? _chase.ActiveTarget : null;
            if (car == null) return;

            Transform t = car.transform;
            Vector3 eye;
            var switcher = car.GetComponent<CharacterModelSwitcher>();
            PmxModel model = switcher != null ? switcher.CurrentModel : null;
            Transform eyeAnchor = model != null ? (model.EyeAnchor != null ? model.EyeAnchor : model.FaceAnchor) : null;
            if (eyeAnchor != null)
            {
                eye = eyeAnchor.position + t.forward * eyeForward + t.up * eyeUp;
                if (hideCharacter) _hiddenRenderer = model.Renderer;
            }
            else if (car.driverHelmetMesh != null)
                eye = car.driverHelmetMesh.position + t.TransformVector(cockpitEyeOffset);
            else
                eye = t.TransformPoint(cockpitFallbackEye);

            transform.SetPositionAndRotation(eye, t.rotation * Quaternion.Euler(pitchDown, 0f, 0f));
            _cam.fieldOfView = fov;
        }

        // Hide the character only for this camera's render; other cameras (front cam, split screen) still see it.
        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _cam || _hiddenRenderer == null) return;
            _savedShadowMode = _hiddenRenderer.shadowCastingMode;
            _hiddenRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }

        private void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _cam || _hiddenRenderer == null) return;
            _hiddenRenderer.shadowCastingMode = _savedShadowMode;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            if (_cam != null) _cam.nearClipPlane = _defaultNear;
        }
    }
}
