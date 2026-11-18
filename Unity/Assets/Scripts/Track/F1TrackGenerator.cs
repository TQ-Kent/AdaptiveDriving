using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_6000_0_OR_NEWER
using TrackPhysicsMaterial = UnityEngine.PhysicsMaterial;
#else
using TrackPhysicsMaterial = UnityEngine.PhysicMaterial;
#endif

namespace F1Racing.Track
{
    /// <summary>
    /// Procedural closed-loop Formula 1 circuit generator.
    /// Builds UV-mapped asphalt, apex red/white kerbs, safety barriers,
    /// start/finish gantry with 5 F1 starting lights, grandstands, and high-friction MeshColliders.
    /// </summary>
    [ExecuteAlways]
    public class F1TrackGenerator : MonoBehaviour
    {
        [Header("Track Dimensions")]
        [SerializeField] private float trackWidth = 16f;
        [SerializeField] private float barrierOffset = 2.5f;
        [SerializeField] private float barrierHeight = 1.1f;
        [SerializeField] private float barrierThickness = 0.6f;
        [SerializeField] private float kerbWidth = 1.4f;
        [SerializeField] private float kerbHeight = 0.08f;
        [SerializeField] private float curvatureKerbThreshold = 0.0085f;
        [SerializeField] private int subdivisionsPerSegment = 24;

        [Header("Physics Surface")]
        [SerializeField] private float asphaltDynamicFriction = 1.15f;
        [SerializeField] private float asphaltStaticFriction = 1.25f;

        [Header("Runtime")]
        [SerializeField] private bool generateOnAwakeIfEmpty = true;

        private CatmullRomSpline _spline;
        private readonly List<Vector3> _waypoints = new List<Vector3>();
        private readonly List<Renderer> _startLightRenderers = new List<Renderer>();
        private Material _startLightOnMat;
        private Material _startLightOffMat;

        public IReadOnlyList<Vector3> Waypoints => _waypoints;
        public CatmullRomSpline Spline => _spline;

        private void Awake()
        {
            if (Application.isPlaying && generateOnAwakeIfEmpty && transform.childCount == 0)
            {
                BuildTrack();
            }
            else if (Application.isPlaying)
            {
                RebuildSplineOnly();
            }
        }

        private void Start()
        {
            if (Application.isPlaying && _startLightRenderers.Count > 0)
            {
                StartCoroutine(RunF1StartLightsSequence());
            }
        }

        /// <summary>
        /// Defines a closed-loop F1 circuit with straights, uphill S-bends,
        /// a tight hairpin, a downhill braking zone, and a fast chicane.
        /// </summary>
        private List<Vector3> GetDefaultControlPoints()
        {
            return new List<Vector3>
            {
                // Main Start/Finish Straight (heading +Z)
                new Vector3(0f,    0.0f, -140f),
                new Vector3(0f,    0.0f,    0f),
                new Vector3(0f,    0.0f,  150f),

                // Turn 1-2-3: Sweeping uphill right-left-right S-bend
                new Vector3(38f,   3.5f,  255f),
                new Vector3(115f,  8.0f,  295f),
                new Vector3(185f, 11.5f,  250f),

                // Elevated Back Straight
                new Vector3(260f, 12.0f,  195f),
                new Vector3(320f, 11.0f,  110f),

                // Tight Hairpin Turn (180-degree right-hander)
                new Vector3(355f,  9.0f,   20f),
                new Vector3(335f,  7.5f,  -45f),
                new Vector3(270f,  6.5f,  -20f),

                // Downhill Run into Fast Chicane
                new Vector3(215f,  4.0f,  -75f),
                new Vector3(185f,  2.0f, -150f),
                new Vector3(135f,  1.0f, -135f),
                new Vector3(105f,  0.5f, -205f),

                // Final Sweeping Parabolica onto Main Straight
                new Vector3(45f,   0.0f, -235f),
            };
        }

        public void RebuildSplineOnly()
        {
            _spline = new CatmullRomSpline(GetDefaultControlPoints(), subdivisionsPerSegment);
            _waypoints.Clear();
            foreach (var sample in _spline.Samples)
            {
                _waypoints.Add(sample.Position);
            }
        }

        [ContextMenu("Build F1 Track")]
        public void BuildTrack()
        {
            ClearChildren();
            RebuildSplineOnly();

            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            TrackPhysicsMaterial asphaltPhysMat = new TrackPhysicsMaterial("F1_Asphalt_PhysMat")
            {
                dynamicFriction = asphaltDynamicFriction,
                staticFriction = asphaltStaticFriction,
                bounciness = 0.02f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };

            CreateGroundPlane(urpLit);
            CreateAsphaltMesh(urpLit, asphaltPhysMat);
            CreateShoulderStrips(urpLit, asphaltPhysMat);
            CreateApexKerbs(urpLit);
            CreateSafetyBarriers(urpLit);
            CreateStartFinishGantry(urpLit);
            CreateGrandstands(urpLit);
        }

        private void ClearChildren()
        {
            _startLightRenderers.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        private void CreateGroundPlane(Shader urpLit)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "InfieldGround";
            ground.transform.SetParent(transform, false);
            ground.transform.position = new Vector3(160f, -0.35f, 20f);
            ground.transform.localScale = new Vector3(120f, 1f, 120f);

            Material grassMat = new Material(urpLit) { name = "F1_Grass_Mat" };
            grassMat.SetColor("_BaseColor", new Color(0.14f, 0.28f, 0.12f));
            grassMat.SetFloat("_Smoothness", 0.08f);
            ground.GetComponent<Renderer>().sharedMaterial = grassMat;
        }

        private void CreateAsphaltMesh(Shader urpLit, TrackPhysicsMaterial physMat)
        {
            var samples = _spline.Samples;
            int n = samples.Count;

            Vector3[] vertices = new Vector3[n * 2];
            Vector2[] uvs = new Vector2[n * 2];
            int[] triangles = new int[n * 6];

            float halfWidth = trackWidth * 0.5f;

            for (int i = 0; i < n; i++)
            {
                var s = samples[i];
                vertices[i * 2] = s.Position - s.Right * halfWidth;
                vertices[i * 2 + 1] = s.Position + s.Right * halfWidth;

                float vCoord = s.Distance / 12f;
                uvs[i * 2] = new Vector2(0f, vCoord);
                uvs[i * 2 + 1] = new Vector2(1f, vCoord);

                int next = (i + 1) % n;
                int ti = i * 6;
                triangles[ti] = i * 2;
                triangles[ti + 1] = next * 2;
                triangles[ti + 2] = i * 2 + 1;

                triangles[ti + 3] = i * 2 + 1;
                triangles[ti + 4] = next * 2;
                triangles[ti + 5] = next * 2 + 1;
            }

            Mesh mesh = new Mesh
            {
                name = "F1_Asphalt_Mesh",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GameObject roadObj = new GameObject("Track_Asphalt");
            roadObj.transform.SetParent(transform, false);
            roadObj.layer = LayerMask.NameToLayer("Default");

            MeshFilter mf = roadObj.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = roadObj.AddComponent<MeshRenderer>();
            Material asphaltMat = new Material(urpLit) { name = "F1_Asphalt_Mat" };
            Texture2D asphaltTex = GenerateAsphaltTexture();
            asphaltMat.SetTexture("_BaseMap", asphaltTex);
            asphaltMat.SetFloat("_Smoothness", 0.25f);
            mr.sharedMaterial = asphaltMat;

            MeshCollider mc = roadObj.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.sharedMaterial = physMat;
        }

        private Texture2D GenerateAsphaltTexture()
        {
            int w = 256, h = 256;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            Color baseDark = new Color(0.13f, 0.135f, 0.145f);
            Color lineWhite = new Color(0.92f, 0.92f, 0.92f);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / w;
                    // Crisp white track boundary lines near left and right edges
                    if ((u > 0.02f && u < 0.045f) || (u > 0.955f && u < 0.98f))
                    {
                        tex.SetPixel(x, y, lineWhite);
                        continue;
                    }

                    // Subtle asphalt grain + rubbered-in racing groove
                    float noise = Mathf.PerlinNoise(x * 0.25f, y * 0.25f) * 0.04f - 0.02f;
                    float groove = Mathf.Exp(-Mathf.Pow((u - 0.5f) * 3.5f, 2f)) * -0.02f;
                    Color c = new Color(
                        Mathf.Clamp01(baseDark.r + noise + groove),
                        Mathf.Clamp01(baseDark.g + noise + groove),
                        Mathf.Clamp01(baseDark.b + noise + groove)
                    );
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return tex;
        }

        private void CreateShoulderStrips(Shader urpLit, TrackPhysicsMaterial physMat)
        {
            var samples = _spline.Samples;
            int n = samples.Count;
            float halfWidth = trackWidth * 0.5f;
            float innerOffset = halfWidth - 0.05f;
            float outerOffset = halfWidth + barrierOffset + 0.8f;

            var verts = new List<Vector3>(n * 4);
            var uvs = new List<Vector2>(n * 4);
            var tris = new List<int>(n * 12);

            for (int side = -1; side <= 1; side += 2)
            {
                int sideBase = verts.Count;
                for (int i = 0; i < n; i++)
                {
                    var s = samples[i];
                    verts.Add(s.Position + s.Right * (side * innerOffset) - Vector3.up * 0.015f);
                    verts.Add(s.Position + s.Right * (side * outerOffset) - Vector3.up * 0.015f);

                    float vCoord = s.Distance / 4.5f;
                    uvs.Add(new Vector2(0f, vCoord));
                    uvs.Add(new Vector2(1f, vCoord));
                }

                for (int i = 0; i < n; i++)
                {
                    int next = (i + 1) % n;
                    int a = sideBase + i * 2;
                    int b = sideBase + i * 2 + 1;
                    int c = sideBase + next * 2;
                    int d = sideBase + next * 2 + 1;

                    if (side > 0)
                    {
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
                    else
                    {
                        tris.Add(a); tris.Add(b); tris.Add(c);
                        tris.Add(b); tris.Add(d); tris.Add(c);
                    }
                }
            }

            Mesh shoulderMesh = new Mesh
            {
                name = "F1_ShoulderStrips_Mesh",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            shoulderMesh.SetVertices(verts);
            shoulderMesh.SetUVs(0, uvs);
            shoulderMesh.SetTriangles(tris, 0);
            shoulderMesh.RecalculateNormals();
            shoulderMesh.RecalculateBounds();

            GameObject shoulderObj = new GameObject("Track_Shoulder_Stripes");
            shoulderObj.transform.SetParent(transform, false);
            shoulderObj.AddComponent<MeshFilter>().sharedMesh = shoulderMesh;

            Material shoulderMat = new Material(urpLit) { name = "F1_Shoulder_Mat" };
            shoulderMat.SetTexture("_BaseMap", GenerateKerbTexture());
            shoulderMat.SetFloat("_Smoothness", 0.3f);
            shoulderObj.AddComponent<MeshRenderer>().sharedMaterial = shoulderMat;

            MeshCollider mc = shoulderObj.AddComponent<MeshCollider>();
            mc.sharedMesh = shoulderMesh;
            mc.sharedMaterial = physMat;
        }

        private void CreateApexKerbs(Shader urpLit)
        {
            var samples = _spline.Samples;
            int n = samples.Count;
            float halfWidth = trackWidth * 0.5f;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int i = 0; i < n; i++)
            {
                var s = samples[i];
                if (Mathf.Abs(s.SignedCurvature) < curvatureKerbThreshold) continue;

                int next = (i + 1) % n;
                var sNext = samples[next];

                // Place kerb on the inside apex of the turn
                float side = s.SignedCurvature > 0f ? 1f : -1f;

                Vector3 inner0 = s.Position + s.Right * (side * halfWidth);
                Vector3 outer0 = inner0 + s.Right * (side * kerbWidth) + Vector3.up * kerbHeight;
                Vector3 inner1 = sNext.Position + sNext.Right * (side * halfWidth);
                Vector3 outer1 = inner1 + sNext.Right * (side * kerbWidth) + Vector3.up * kerbHeight;

                int baseIdx = verts.Count;
                verts.Add(inner0 + Vector3.up * 0.01f);
                verts.Add(outer0);
                verts.Add(inner1 + Vector3.up * 0.01f);
                verts.Add(outer1);

                // Alternate UVs for red/white stripes
                float v0 = i * 0.5f;
                float v1 = (i + 1) * 0.5f;
                uvs.Add(new Vector2(0f, v0));
                uvs.Add(new Vector2(1f, v0));
                uvs.Add(new Vector2(0f, v1));
                uvs.Add(new Vector2(1f, v1));

                if (side > 0f)
                {
                    tris.Add(baseIdx); tris.Add(baseIdx + 2); tris.Add(baseIdx + 1);
                    tris.Add(baseIdx + 1); tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
                }
                else
                {
                    tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
                    tris.Add(baseIdx + 1); tris.Add(baseIdx + 3); tris.Add(baseIdx + 2);
                }
            }

            if (verts.Count == 0) return;

            Mesh kerbMesh = new Mesh { name = "F1_Kerbs_Mesh" };
            kerbMesh.SetVertices(verts);
            kerbMesh.SetUVs(0, uvs);
            kerbMesh.SetTriangles(tris, 0);
            kerbMesh.RecalculateNormals();

            GameObject kerbObj = new GameObject("Track_ApexKerbs");
            kerbObj.transform.SetParent(transform, false);
            kerbObj.AddComponent<MeshFilter>().sharedMesh = kerbMesh;

            Material kerbMat = new Material(urpLit) { name = "F1_Kerb_Mat" };
            kerbMat.SetTexture("_BaseMap", GenerateKerbTexture());
            kerbMat.SetFloat("_Smoothness", 0.35f);
            kerbObj.AddComponent<MeshRenderer>().sharedMaterial = kerbMat;

            MeshCollider kc = kerbObj.AddComponent<MeshCollider>();
            kc.sharedMesh = kerbMesh;
        }

        private Texture2D GenerateKerbTexture()
        {
            Texture2D tex = new Texture2D(4, 64, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };
            Color red = new Color(0.85f, 0.1f, 0.1f);
            Color white = new Color(0.95f, 0.95f, 0.95f);

            for (int y = 0; y < 64; y++)
            {
                Color c = y < 32 ? red : white;
                for (int x = 0; x < 4; x++)
                    tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        private void CreateSafetyBarriers(Shader urpLit)
        {
            CreateBarrierSide("Track_Barrier_Left", -1f, urpLit);
            CreateBarrierSide("Track_Barrier_Right", 1f, urpLit);
        }

        private void CreateBarrierSide(string objName, float sideSign, Shader urpLit)
        {
            var samples = _spline.Samples;
            int n = samples.Count;
            float innerOffset = trackWidth * 0.5f + barrierOffset;
            float outerOffset = innerOffset + barrierThickness;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            for (int i = 0; i < n; i++)
            {
                var s0 = samples[i];
                var s1 = samples[(i + 1) % n];

                Vector3 b0In = s0.Position + s0.Right * (sideSign * innerOffset);
                Vector3 b0Out = s0.Position + s0.Right * (sideSign * outerOffset);
                Vector3 t0In = b0In + Vector3.up * barrierHeight;
                Vector3 t0Out = b0Out + Vector3.up * barrierHeight;

                Vector3 b1In = s1.Position + s1.Right * (sideSign * innerOffset);
                Vector3 b1Out = s1.Position + s1.Right * (sideSign * outerOffset);
                Vector3 t1In = b1In + Vector3.up * barrierHeight;
                Vector3 t1Out = b1Out + Vector3.up * barrierHeight;

                // Inner wall face + top cap + outer wall face
                AddQuad(verts, tris, b0In, t0In, t1In, b1In, sideSign > 0f);
                AddQuad(verts, tris, t0In, t0Out, t1Out, t1In, sideSign > 0f);
                AddQuad(verts, tris, t0Out, b0Out, b1Out, t1Out, sideSign > 0f);
            }

            Mesh wallMesh = new Mesh
            {
                name = objName + "_Mesh",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            wallMesh.SetVertices(verts);
            wallMesh.SetTriangles(tris, 0);
            wallMesh.RecalculateNormals();

            GameObject wallObj = new GameObject(objName);
            wallObj.transform.SetParent(transform, false);
            wallObj.AddComponent<MeshFilter>().sharedMesh = wallMesh;

            Material wallMat = new Material(urpLit) { name = "F1_Barrier_Mat" };
            wallMat.SetColor("_BaseColor", new Color(0.76f, 0.78f, 0.8f));
            wallMat.SetFloat("_Smoothness", 0.2f);
            wallObj.AddComponent<MeshRenderer>().sharedMaterial = wallMat;

            MeshCollider mc = wallObj.AddComponent<MeshCollider>();
            mc.sharedMesh = wallMesh;
        }

        private static void AddQuad(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool flip)
        {
            int idx = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            if (!flip)
            {
                tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
                tris.Add(idx); tris.Add(idx + 2); tris.Add(idx + 3);
            }
            else
            {
                tris.Add(idx); tris.Add(idx + 2); tris.Add(idx + 1);
                tris.Add(idx); tris.Add(idx + 3); tris.Add(idx + 2);
            }
        }

        private void CreateStartFinishGantry(Shader urpLit)
        {
            var startSample = _spline.GetSampleAtDistance(140f);
            GameObject gantryRoot = new GameObject("StartFinish_Gantry");
            gantryRoot.transform.SetParent(transform, false);
            gantryRoot.transform.position = startSample.Position;
            gantryRoot.transform.rotation = Quaternion.LookRotation(startSample.Tangent, Vector3.up);

            Material pillarMat = new Material(urpLit) { name = "Gantry_Steel_Mat" };
            pillarMat.SetColor("_BaseColor", new Color(0.18f, 0.19f, 0.22f));
            pillarMat.SetFloat("_Metallic", 0.7f);

            float span = trackWidth + barrierOffset * 2f;
            float gantryHeight = 6.2f;

            // Left & Right Pillars
            CreateBox(gantryRoot.transform, "Pillar_Left", new Vector3(-span * 0.5f, gantryHeight * 0.5f, 0f), new Vector3(0.7f, gantryHeight, 0.7f), pillarMat);
            CreateBox(gantryRoot.transform, "Pillar_Right", new Vector3(span * 0.5f, gantryHeight * 0.5f, 0f), new Vector3(0.7f, gantryHeight, 0.7f), pillarMat);
            // Overhead Crossbar
            CreateBox(gantryRoot.transform, "Overhead_Beam", new Vector3(0f, gantryHeight, 0f), new Vector3(span + 0.7f, 1.2f, 1.0f), pillarMat);

            // Checkered Start/Finish Line Strip on Asphalt
            GameObject finishLine = GameObject.CreatePrimitive(PrimitiveType.Quad);
            finishLine.name = "StartFinish_Line";
            finishLine.transform.SetParent(gantryRoot.transform, false);
            finishLine.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            finishLine.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            finishLine.transform.localScale = new Vector3(trackWidth, 2.2f, 1f);
            DestroyImmediateSafe(finishLine.GetComponent<Collider>());

            Material checkMat = new Material(urpLit) { name = "Checkered_Mat" };
            checkMat.SetTexture("_BaseMap", GenerateCheckerTexture());
            finishLine.GetComponent<Renderer>().sharedMaterial = checkMat;

            // 5 F1 Starting Lights
            _startLightOnMat = new Material(urpLit) { name = "StartLight_On" };
            _startLightOnMat.SetColor("_BaseColor", Color.red);
            _startLightOnMat.EnableKeyword("_EMISSION");
            _startLightOnMat.SetColor("_EmissionColor", Color.red * 4f);

            _startLightOffMat = new Material(urpLit) { name = "StartLight_Off" };
            _startLightOffMat.SetColor("_BaseColor", new Color(0.12f, 0.02f, 0.02f));

            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * 1.4f;
                GameObject pod = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pod.name = $"StartLight_{i + 1}";
                pod.transform.SetParent(gantryRoot.transform, false);
                pod.transform.localPosition = new Vector3(x, gantryHeight - 0.85f, -0.55f);
                pod.transform.localScale = Vector3.one * 0.55f;
                DestroyImmediateSafe(pod.GetComponent<Collider>());

                Renderer r = pod.GetComponent<Renderer>();
                r.sharedMaterial = _startLightOnMat;
                _startLightRenderers.Add(r);
            }
        }

        private Texture2D GenerateCheckerTexture()
        {
            Texture2D tex = new Texture2D(16, 4, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    bool white = ((x + y) % 2) == 0;
                    tex.SetPixel(x, y, white ? Color.white : Color.black);
                }
            }
            tex.Apply();
            return tex;
        }

        private void CreateGrandstands(Shader urpLit)
        {
            Material standMat = new Material(urpLit) { name = "Grandstand_Mat" };
            standMat.SetColor("_BaseColor", new Color(0.35f, 0.37f, 0.42f));

            Material roofMat = new Material(urpLit) { name = "Grandstand_Roof_Mat" };
            roofMat.SetColor("_BaseColor", new Color(0.85f, 0.15f, 0.15f));

            float[] standDistances = { 90f, 180f, 680f };
            float[] standSides = { -1f, 1f, 1f };

            for (int s = 0; s < standDistances.Length; s++)
            {
                var sample = _spline.GetSampleAtDistance(standDistances[s]);
                GameObject stand = new GameObject($"Grandstand_{s + 1}");
                stand.transform.SetParent(transform, false);
                stand.transform.position = sample.Position + sample.Right * (standSides[s] * (trackWidth * 0.5f + 12f));
                stand.transform.rotation = Quaternion.LookRotation(sample.Tangent, Vector3.up);

                // 4 terraced seating tiers
                for (int tier = 0; tier < 4; tier++)
                {
                    float offsetX = standSides[s] * (tier * 2.2f);
                    float heightY = 1.0f + tier * 1.2f;
                    CreateBox(stand.transform, $"Tier_{tier}", new Vector3(offsetX, heightY * 0.5f, 0f), new Vector3(2.2f, heightY, 42f), standMat);
                }
                // Canopy roof
                CreateBox(stand.transform, "Roof", new Vector3(standSides[s] * 3.5f, 7.5f, 0f), new Vector3(9.5f, 0.4f, 43f), roofMat);
            }
        }

        private static void CreateBox(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPos;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private IEnumerator RunF1StartLightsSequence()
        {
            foreach (var r in _startLightRenderers)
                r.sharedMaterial = _startLightOffMat;

            yield return new WaitForSeconds(0.6f);

            for (int i = 0; i < _startLightRenderers.Count; i++)
            {
                _startLightRenderers[i].sharedMaterial = _startLightOnMat;
                yield return new WaitForSeconds(0.55f);
            }

            yield return new WaitForSeconds(0.9f);

            // Lights out and away we go!
            foreach (var r in _startLightRenderers)
                r.sharedMaterial = _startLightOffMat;
        }

        /// <summary>
        /// Computes staggered F1 starting grid positions just behind the Start/Finish line.
        /// </summary>
        public void GetStartingGridSlot(int slotIndex, out Vector3 position, out Quaternion rotation)
        {
            if (_spline == null) RebuildSplineOnly();

            float startLineDist = 140f;
            float rowBackOffset = 10f + slotIndex * 6f;
            float lateralOffset = (slotIndex % 2 == 0) ? -3.2f : 3.2f;

            var sample = _spline.GetSampleAtDistance(startLineDist - rowBackOffset);
            position = sample.Position + sample.Right * lateralOffset + Vector3.up * 0.65f;
            rotation = Quaternion.LookRotation(sample.Tangent, Vector3.up);
        }

        private static void DestroyImmediateSafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }
}
