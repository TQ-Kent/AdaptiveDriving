using System.Collections.Generic;
using F1Racing.Vehicle;
using UnityEngine;

namespace F1Racing.Track
{
    /// <summary>
    /// Spawns pyramid stacks of physics boxes along the circuit. When a car touches a stack it
    /// explodes and the whole stack is blasted into the air. Every stack is rebuilt whenever any
    /// car completes a lap (or press B). Created by GameModeManager in Fun mode.
    /// </summary>
    public class PushableBoxField : MonoBehaviour
    {
        public F1TrackGenerator track;

        [Header("Placement")]
        [Tooltip("Distance between stacks along the racing line (m).")]
        public float stackSpacing = 110f;
        [Tooltip("Lateral offsets (m) cycled per stack; track is 16 m wide.")]
        public float[] lateralOffsets = { 0f, -3.5f, 3.5f };
        [Tooltip("Keep clear this far before / after the start line (m): the grid and the line.")]
        public Vector2 keepClearAroundStart = new Vector2(80f, 30f);

        [Header("Stacks")]
        public int baseRowCount = 5;    // pyramid: 5-4-3-2-1 = 15 boxes
        public float boxSize = 1.0f;
        public float boxMass = 35f;
        public KeyCode respawnKey = KeyCode.B;

        [Header("Box Visual")]
        [Tooltip("Show a character figure instead of the cube. The cube BoxCollider is always kept.")]
        public bool useFigureVisual = true;
        public string figurePmxResource = "Characters/GoldShip/GoldShip_pmx";
        public string figureTextureFolder = "Characters/GoldShip/";

        [Header("Explosion")]
        public float explosionVelocity = 20f;  // m/s at the blast centre
        public float explosionRadius = 9f;
        public float upwardsModifier = 1.2f;
        public float carryCarVelocity = 0.45f; // fraction of the car's velocity added to each box
        public float fxScale = 1f;

        private readonly List<List<Rigidbody>> _stacks = new List<List<Rigidbody>>();
        private readonly List<bool> _exploded = new List<bool>();
        private TrackProgress[] _progress = new TrackProgress[0];
        private Material[] _mats;
        private PhysicsMaterial _physMat;
        private bool _figureTried;
        private Mesh _figureMesh;           // baked once, shared by every box
        private Material[] _figureMats;
        private float _figureHeight, _figureMinY;
        private string _toast;
        private float _toastUntil;
        private GUIStyle _toastStyle;

        /// <summary>Created by GameModeManager in Fun mode.</summary>
        public static PushableBoxField Create(F1TrackGenerator track)
        {
            var existing = FindAnyObjectByType<PushableBoxField>();
            if (existing != null) return existing;
            var field = new GameObject("PushableBoxField").AddComponent<PushableBoxField>();
            field.track = track;
            return field;
        }

        private void Start()
        {
            if (track == null) track = FindAnyObjectByType<F1TrackGenerator>();
            _progress = FindObjectsByType<TrackProgress>();
            foreach (TrackProgress p in _progress) p.LapCompleted += OnLapCompleted;
            Spawn();
        }

        private void OnDestroy()
        {
            foreach (TrackProgress p in _progress) if (p != null) p.LapCompleted -= OnLapCompleted;
        }

        private void OnLapCompleted(TrackProgress p)
        {
            Spawn();
            var car = p.GetComponent<F1CarController>();
            ShowToast($"{(car != null ? car.carName : p.name)}  LAP {p.Laps} complete  -  boxes reset");
        }

        private void Update()
        {
            if (Input.GetKeyDown(respawnKey))
            {
                Spawn();
                ShowToast("Boxes reset");
            }
        }

        // ---------------- Spawning ----------------

        public void Spawn()
        {
            foreach (List<Rigidbody> stack in _stacks)
                foreach (Rigidbody rb in stack)
                    if (rb != null) Destroy(rb.gameObject);
            _stacks.Clear();
            _exploded.Clear();

            CatmullRomSpline spline = GetSpline();
            if (spline == null) return;

            EnsureMaterials();
            int s = 0;
            for (float dist = stackSpacing * 0.5f; dist < spline.TotalLength - 20f; dist += stackSpacing)
            {
                float fromStart = dist - F1TrackGenerator.StartLineDistance;
                if (fromStart > -keepClearAroundStart.x && fromStart < keepClearAroundStart.y) continue;

                var sample = spline.GetSampleAtDistance(dist);
                Vector3 fwd = sample.Tangent.normalized;
                Vector3 up = sample.Up.sqrMagnitude > 0.01f ? sample.Up.normalized : Vector3.up;
                Vector3 right = sample.Right.normalized;
                Vector3 centre = sample.Position + right * lateralOffsets[s % lateralOffsets.Length];

                var stack = new List<Rigidbody>();
                BuildPyramid(stack, centre, Quaternion.LookRotation(fwd, up), right, up, s);
                _stacks.Add(stack);
                _exploded.Add(false);
                s++;
            }
        }

        private void BuildPyramid(List<Rigidbody> stack, Vector3 centre, Quaternion rot, Vector3 right, Vector3 up, int stackIndex)
        {
            float gap = boxSize * 1.02f;
            for (int row = 0; row < baseRowCount; row++)
            {
                int count = baseRowCount - row;
                float y = boxSize * 0.5f + 0.02f + row * boxSize;
                for (int i = 0; i < count; i++)
                {
                    float x = (i - (count - 1) * 0.5f) * gap;
                    stack.Add(CreateBox(centre + right * x + up * y, rot, stackIndex,
                        $"Box_{stackIndex}_{row}_{i}", _mats[(stackIndex + row + i) % _mats.Length]));
                }
            }
        }

        private Rigidbody CreateBox(Vector3 pos, Quaternion rot, int stackIndex, string name, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, true);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one * boxSize;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.GetComponent<Collider>().sharedMaterial = _physMat;
            if (useFigureVisual && EnsureFigure()) AttachFigure(go);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = boxMass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.1f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var box = go.AddComponent<PushableBox>();
            box.field = this;
            box.stackIndex = stackIndex;
            return rb;
        }

        // ---------------- Figure visual ----------------

        /// <summary>Hides the cube mesh and adds the figure, scaled to fit the 1x1x1 box space, feet on the box bottom.</summary>
        private void AttachFigure(GameObject box)
        {
            box.GetComponent<MeshRenderer>().enabled = false;

            var vis = new GameObject("Figure");
            vis.transform.SetParent(box.transform, false);
            float s = 1f / Mathf.Max(_figureHeight, 0.001f);
            vis.transform.localScale = Vector3.one * s;
            vis.transform.localPosition = new Vector3(0f, -0.5f - _figureMinY * s, 0f);
            // MMD models face -Z; the box's +Z points along the racing direction, so she faces oncoming cars.
            vis.transform.localRotation = Quaternion.identity;
            vis.AddComponent<MeshFilter>().sharedMesh = _figureMesh;
            vis.AddComponent<MeshRenderer>().sharedMaterials = _figureMats;
        }

        /// <summary>Loads the PMX once and bakes it to a static mesh (arms lowered from the A-pose).</summary>
        private bool EnsureFigure()
        {
            if (_figureTried) return _figureMesh != null;
            _figureTried = true;

            TextAsset pmx = Resources.Load<TextAsset>(figurePmxResource);
            if (pmx == null)
            {
                Debug.LogWarning($"[PushableBoxField] Figure PMX not found at Resources/{figurePmxResource}; using cubes.");
                return false;
            }
            try
            {
                var model = Vehicle.Characters.PmxRuntimeLoader.Load(pmx.bytes, "BoxFigure_Bake", figureTextureFolder);
                Transform armL = model.Bone("左腕"), armR = model.Bone("右腕");
                if (armL != null) armL.localRotation = Quaternion.Euler(0f, 0f, -48f);
                if (armR != null) armR.localRotation = Quaternion.Euler(0f, 0f, 48f);

                var smr = model.Root.GetComponentInChildren<SkinnedMeshRenderer>();
                var mesh = new Mesh { name = "BoxFigure_Mesh" };
                smr.BakeMesh(mesh, true);
                mesh.RecalculateBounds();

                _figureMesh = mesh;
                _figureMats = smr.sharedMaterials;
                _figureHeight = model.Height;
                _figureMinY = model.MinY;
                Destroy(model.Root);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[PushableBoxField] Failed to build figure visual, using cubes: " + e);
                return false;
            }
        }

        // ---------------- Explosion ----------------

        public void OnStackHit(int stackIndex, Vector3 point, Vector3 carVelocity)
        {
            if (stackIndex < 0 || stackIndex >= _stacks.Count || _exploded[stackIndex]) return;
            _exploded[stackIndex] = true;

            BoxExplosionFx.Spawn(point, fxScale);

            // Blast from slightly below the contact so boxes go up and out.
            Vector3 blastOrigin = point - Vector3.up * 0.8f;
            foreach (Rigidbody rb in _stacks[stackIndex])
            {
                if (rb == null) continue;
                rb.AddExplosionForce(explosionVelocity, blastOrigin, explosionRadius, upwardsModifier, ForceMode.VelocityChange);
                rb.AddForce(carVelocity * carryCarVelocity, ForceMode.VelocityChange);
                rb.AddTorque(Random.insideUnitSphere * 12f, ForceMode.VelocityChange);
            }
        }

        // ---------------- Helpers ----------------

        private CatmullRomSpline GetSpline()
        {
            if (track == null) return null;
            CatmullRomSpline spline = track.Spline;
            return spline != null && spline.TotalLength > 0f ? spline : null;
        }

        private void ShowToast(string text)
        {
            _toast = text;
            _toastUntil = Time.unscaledTime + 2.5f;
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(_toast) || Time.unscaledTime > _toastUntil) return;
            _toastStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 18, fontStyle = FontStyle.Bold };
            GUI.Box(new Rect(Screen.width * 0.5f - 230f, 165f, 460f, 36f), _toast, _toastStyle);
        }

        private void EnsureMaterials()
        {
            if (_mats != null) return;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Color[] colors =
            {
                new Color(0.78f, 0.55f, 0.30f), // cardboard
                new Color(0.95f, 0.45f, 0.08f), // safety orange
                new Color(0.92f, 0.92f, 0.90f), // white
            };
            _mats = new Material[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                _mats[i] = new Material(lit) { name = "PushBox_Mat_" + i };
                _mats[i].SetColor("_BaseColor", colors[i]);
                _mats[i].SetFloat("_Smoothness", 0.25f);
            }
            _physMat = new PhysicsMaterial("PushBox_PhysMat")
            {
                dynamicFriction = 0.5f,
                staticFriction = 0.6f,
                bounciness = 0.15f,
            };
        }
    }
}
