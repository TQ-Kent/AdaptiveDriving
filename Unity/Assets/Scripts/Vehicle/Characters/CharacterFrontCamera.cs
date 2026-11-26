using UnityEngine;

namespace F1Racing.Vehicle.Characters
{
    /// <summary>
    /// Camera placed in front of the model, looking back at her face.
    /// Press F to cycle: picture-in-picture (bottom right) -> fullscreen -> off.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CharacterFrontCamera : MonoBehaviour
    {
        public enum Mode { PictureInPicture, Fullscreen, Off }

        public Transform carRoot;
        public Transform lookTarget;               // head bone
        public Vector3 offset = new Vector3(0f, 1.25f, 2.2f); // car space: up, ahead
        public float lookUp = 0f;                  // extra aim height above lookTarget (metres)
        public Mode mode = Mode.PictureInPicture;
        public KeyCode toggleKey = KeyCode.F;

        private static readonly Rect PipRect = new Rect(0.70f, 0.08f, 0.28f, 0.32f);
        private Camera _cam;
        private GUIStyle _label;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.fieldOfView = 38f;
            _cam.nearClipPlane = 0.05f;
            _cam.depth = 10f;
            Apply();
        }

        private void Update()
        {
            if (GameInput.Down(toggleKey))
            {
                mode = (Mode)(((int)mode + 1) % 3);
                Apply();
            }
        }

        private void Apply()
        {
            _cam.enabled = mode != Mode.Off;
            _cam.rect = mode == Mode.Fullscreen ? new Rect(0f, 0f, 1f, 1f) : PipRect;
        }

        private void LateUpdate()
        {
            if (carRoot == null) return;
            // Rigid follow: the car can do 90 m/s, so any positional lag would drop the camera behind her.
            transform.position = carRoot.TransformPoint(offset);
            Vector3 target = lookTarget != null
                ? lookTarget.position + carRoot.up * lookUp
                : carRoot.TransformPoint(new Vector3(0f, 0.5f, 0f));
            transform.rotation = Quaternion.LookRotation(target - transform.position, carRoot.up);
        }

        private void OnGUI()
        {
            if (mode != Mode.PictureInPicture) return;
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            float x = PipRect.x * Screen.width;
            float y = (1f - PipRect.y - PipRect.height) * Screen.height - 20f;
            GUI.Label(new Rect(x, y, 260f, 20f), "Front cam  [F] cycle", _label);
        }
    }
}
