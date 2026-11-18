using System.Collections.Generic;
using UnityEngine;

namespace F1Racing.Vehicle.Characters
{
    /// <summary>
    /// Replaces the red car's visuals with PMX character models (speed-driven run cycle +
    /// front camera on F). Press M to cycle models; the last entry is the original car.
    /// Physics, colliders and the controller are untouched. Models live in Resources/Characters/.
    /// </summary>
    public class CharacterModelSwitcher : MonoBehaviour
    {
        /// <summary>Called by GameModeManager in Fun mode.</summary>
        public static void Attach(GameObject car)
        {
            if (car == null || car.GetComponent<CharacterModelSwitcher>() != null) return;
            var front = new GameObject("CharacterFrontCamera", typeof(Camera)).AddComponent<CharacterFrontCamera>();
            front.carRoot = car.transform;
            car.AddComponent<CharacterModelSwitcher>().frontCamera = front;
        }

        [System.Serializable]
        public struct Entry
        {
            public string displayName;
            public string folder;    // under Resources/, with trailing slash
            public string pmxAsset;  // TextAsset name (file is <name>.bytes)
        }

        public Entry[] models =
        {
            new Entry { displayName = "Daitaku Helios", folder = "Characters/DaitakuHelios/", pmxAsset = "DaitakuHelios_pmx" },
            new Entry { displayName = "Gold Ship",      folder = "Characters/GoldShip/",      pmxAsset = "GoldShip_pmx" },
        };

        public KeyCode switchKey = KeyCode.M;
        public float targetHeight = 2.0f; // metres, ears included
        public CharacterFrontCamera frontCamera;

        private readonly Dictionary<int, PmxModel> _built = new Dictionary<int, PmxModel>();
        private readonly List<Renderer> _carRenderers = new List<Renderer>();
        private int _index = -1;
        private float _toastUntil;
        private GUIStyle _toastStyle;

        /// <summary>The currently shown character model, or null when the original car is shown.</summary>
        public PmxModel CurrentModel =>
            _index >= 0 && _index < models.Length && _built.TryGetValue(_index, out PmxModel m) ? m : null;

        // Cycle length = models + the original car.
        private int OptionCount => models.Length + 1;

        private void Start()
        {
            foreach (string part in new[] { "Chassis_Body", "WheelMeshes" })
            {
                Transform t = transform.Find(part);
                if (t != null) _carRenderers.AddRange(t.GetComponentsInChildren<Renderer>(true));
            }
            Select(0);
        }

        private void Update()
        {
            if (Input.GetKeyDown(switchKey)) Select((_index + 1) % OptionCount);
        }

        private void Select(int index)
        {
            if (_built.TryGetValue(_index, out PmxModel previous)) previous.Root.SetActive(false);

            PmxModel model = index < models.Length ? GetOrBuild(index) : null;
            if (index < models.Length && model == null)
            {
                // Failed to load: skip to the next option (eventually the original car).
                _index = index;
                Select((index + 1) % OptionCount);
                return;
            }

            _index = index;
            foreach (Renderer r in _carRenderers) if (r != null) r.enabled = model == null;

            if (model != null)
            {
                model.Root.SetActive(true);
                if (frontCamera != null)
                {
                    frontCamera.lookTarget = model.FaceAnchor;
                    float faceY = model.FaceAnchor != null
                        ? transform.InverseTransformPoint(model.FaceAnchor.position).y
                        : targetHeight * 0.6f;
                    frontCamera.offset = new Vector3(0f, faceY, 2.2f);
                }
            }
            else if (frontCamera != null)
            {
                frontCamera.lookTarget = null;
                frontCamera.offset = new Vector3(0f, 1.0f, 3.4f);
            }
            _toastUntil = Time.unscaledTime + 2f;
        }

        private PmxModel GetOrBuild(int index)
        {
            if (_built.TryGetValue(index, out PmxModel cached)) return cached;
            Entry e = models[index];

            TextAsset pmx = Resources.Load<TextAsset>(e.folder + e.pmxAsset);
            if (pmx == null)
            {
                Debug.LogWarning($"[CharacterModelSwitcher] PMX not found at Resources/{e.folder}{e.pmxAsset}");
                return null;
            }

            PmxModel model;
            try { model = PmxRuntimeLoader.Load(pmx.bytes, "Character_" + e.displayName.Replace(" ", ""), e.folder); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CharacterModelSwitcher] Failed to load {e.displayName}: {ex}");
                return null;
            }

            // MMD models face -Z; the car drives along +Z. Feet on the ground (~ wheel bottom).
            float scale = targetHeight / Mathf.Max(model.Height, 0.001f);
            Transform root = model.Root.transform;
            root.SetParent(transform, false);
            root.localRotation = Quaternion.Euler(0f, 180f, 0f);
            root.localScale = Vector3.one * scale;
            root.localPosition = new Vector3(0f, -0.02f - model.MinY * scale, 0f);

            var anim = model.Root.AddComponent<CharacterRunAnimator>();
            anim.car = GetComponent<F1CarController>();
            anim.Bind(model);

            _built[index] = model;
            return model;
        }

        private void OnGUI()
        {
            if (Time.unscaledTime > _toastUntil) return;
            _toastStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 18, fontStyle = FontStyle.Bold };
            string name = _index < models.Length ? models[_index].displayName : "Original car";
            GUI.Box(new Rect(Screen.width * 0.5f - 160f, 120f, 320f, 36f), $"Model: {name}   [{switchKey}]", _toastStyle);
        }
    }
}
