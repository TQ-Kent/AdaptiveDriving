using System.Collections.Generic;
using F1Racing.Track;
using UnityEngine;
using TrackPhysicsMaterial = UnityEngine.PhysicsMaterial;

namespace F1Racing.EditorTools
{
    /// <summary>
    /// Editor-only: builds the track meshes (asphalt, shoulders, kerbs, barriers, gantry, grandstands)
    /// under an <see cref="F1TrackGenerator"/>. Output is baked into the scene.
    /// </summary>
    internal class TrackMeshBuild
    {
        private readonly F1TrackGenerator g;
        private TrackMeshBuild(F1TrackGenerator generator) { g = generator; }

        // Same names as the generator's fields so the moved build code reads unchanged.
        private Transform transform => g.transform;
        private CatmullRomSpline _spline => g.Spline;
        private float trackWidth => g.trackWidth;
        private float barrierOffset => g.barrierOffset;
        private float barrierHeight => g.barrierHeight;
        private float barrierThickness => g.barrierThickness;
        private float kerbWidth => g.kerbWidth;
        private float kerbHeight => g.kerbHeight;
        private float curvatureKerbThreshold => g.curvatureKerbThreshold;
        private const float StartLineDistance = F1TrackGenerator.StartLineDistance;

        public static void Build(F1TrackGenerator generator) => new TrackMeshBuild(generator).BuildTrack();

        private void BuildTrack()
        {
            BuildUtil.ClearChildren(transform);

            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            TrackPhysicsMaterial asphaltPhysMat = new TrackPhysicsMaterial("F1_Asphalt_PhysMat")
            {
                dynamicFriction = g.asphaltDynamicFriction,
                staticFriction = g.asphaltStaticFriction,
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
            var startSample = _spline.GetSampleAtDistance(StartLineDistance);
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
            Object.DestroyImmediate(finishLine.GetComponent<Collider>());

            Material checkMat = new Material(urpLit) { name = "Checkered_Mat" };
            checkMat.SetTexture("_BaseMap", GenerateCheckerTexture());
            finishLine.GetComponent<Renderer>().sharedMaterial = checkMat;

            // 5 F1 Starting Lights (the countdown itself is run by TrackFlagController)
            Material startLightOnMat = new Material(urpLit) { name = "StartLight_On" };
            startLightOnMat.SetColor("_BaseColor", Color.red);
            startLightOnMat.EnableKeyword("_EMISSION");
            startLightOnMat.SetColor("_EmissionColor", Color.red * 4f);

            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * 1.4f;
                GameObject pod = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pod.name = $"StartLight_{i + 1}";
                pod.transform.SetParent(gantryRoot.transform, false);
                pod.transform.localPosition = new Vector3(x, gantryHeight - 0.85f, -0.55f);
                pod.transform.localScale = Vector3.one * 0.55f;
                Object.DestroyImmediate(pod.GetComponent<Collider>());

                pod.GetComponent<Renderer>().sharedMaterial = startLightOnMat;
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

    }
}
