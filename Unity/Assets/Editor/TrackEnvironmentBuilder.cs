using System.Collections.Generic;
using System.IO;
using F1Racing.Track;
using UnityEditor;
using UnityEngine;

#if UNITY_6000_0_OR_NEWER
using TrackPhysicsMaterial = UnityEngine.PhysicsMaterial;
#else
using TrackPhysicsMaterial = UnityEngine.PhysicMaterial;
#endif

namespace F1Racing.Editor
{
    /// <summary>
    /// Custom Inspector & procedural builder for F1 trackside safety infrastructure,
    /// run-off areas, gravel traps, catch fencing, Tecpro barriers, brake boards,
    /// marshal LED flag posts, start/finish gantry, and nature (grass + trees with colliders).
    /// </summary>
    [CustomEditor(typeof(TrackEnvironmentRoot))]
    public class TrackEnvironmentBuilder : UnityEditor.Editor
    {
        private const string MatFolder = "Assets/Materials/TrackEnv";
        private const string TexFolder = "Assets/Textures/TrackEnv";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            TrackEnvironmentRoot root = (TrackEnvironmentRoot)target;

            GUI.backgroundColor = new Color(0.25f, 0.85f, 0.45f);
            if (GUILayout.Button("Build Track Props", GUILayout.Height(36)))
            {
                BuildAllProps(root);
            }
            GUI.backgroundColor = Color.white;
        }

        [MenuItem("F1 Racing/Build Track Props")]
        public static void BuildTrackPropsFromMenu()
        {
            TrackEnvironmentRoot root = Object.FindFirstObjectByType<TrackEnvironmentRoot>();
            if (root == null)
            {
                GameObject go = new GameObject("Track_Environment_Props");
                root = go.AddComponent<TrackEnvironmentRoot>();
                root.trackGenerator = Object.FindFirstObjectByType<F1TrackGenerator>();
            }
            BuildAllProps(root);
            Selection.activeGameObject = root.gameObject;
        }

        public static void BuildAllProps(TrackEnvironmentRoot root)
        {
            if (root == null) return;

            CatmullRomSpline spline = ResolveSpline(root);
            if (spline == null || spline.Samples.Count < 8)
            {
                Debug.LogError("[TrackEnvironmentBuilder] Valid F1TrackGenerator or at least 4 customWaypointTransforms required.");
                return;
            }

            EnsureAssetFolders();
            ClearChildren(root.transform);

            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            // Setup Flag Controller on root
            TrackFlagController flagController = root.GetComponent<TrackFlagController>();
            if (flagController == null) flagController = root.gameObject.AddComponent<TrackFlagController>();
            flagController.flagPanelRenderers.Clear();
            flagController.startLightPodRenderers.Clear();

            // 1. Ground & Nature (Grass + Trees with Colliders)
            Transform natureGroup = CreateGroup(root.transform, "01_Ground_And_Nature");
            BuildGrassGround(natureGroup, urpLit);
            BuildTreesAroundCircuit(natureGroup, spline, root, urpLit);

            // 2. Run-off Areas & Gravel Traps
            Transform runoffGroup = CreateGroup(root.transform, "02_Runoff_And_Gravel");
            BuildRunoffsAndGravel(runoffGroup, spline, root, urpLit);

            // 3. Concrete Barriers & Catch Fencing
            Transform barrierGroup = CreateGroup(root.transform, "03_Barriers_And_Fencing");
            BuildBarriersAndCatchFencing(barrierGroup, spline, root, urpLit);

            // 4. Tecpro / Tire Barrier Blocks on Sharp Corners
            Transform tecproGroup = CreateGroup(root.transform, "04_Tecpro_Barriers");
            BuildTecproBlocks(tecproGroup, spline, root, urpLit);

            // 5. Brake Distance Boards (300m, 200m, 100m)
            Transform brakeGroup = CreateGroup(root.transform, "05_Brake_Boards");
            BuildBrakeDistanceBoards(brakeGroup, spline, root, urpLit);

            // 6. Marshal Posts & LED Flag Panels every 250m
            Transform marshalGroup = CreateGroup(root.transform, "06_Marshal_Posts");
            BuildMarshalPosts(marshalGroup, spline, root, urpLit, flagController);

            // 7. Start/Finish Truss Gantry & 5 Starting Lights at Index 0
            Transform gantryGroup = CreateGroup(root.transform, "07_Start_Gantry");
            BuildStartGantryAtIndex0(gantryGroup, spline, root, urpLit, flagController);

            EditorUtility.SetDirty(root.gameObject);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TrackEnvironmentBuilder] Built all F1 trackside props, safety barriers, and nature.");
        }

        private static CatmullRomSpline ResolveSpline(TrackEnvironmentRoot root)
        {
            if (root.trackGenerator == null)
                root.trackGenerator = Object.FindFirstObjectByType<F1TrackGenerator>();

            if (root.trackGenerator != null)
            {
                if (root.trackGenerator.Spline == null)
                    root.trackGenerator.RebuildSplineOnly();
                return root.trackGenerator.Spline;
            }

            if (root.customWaypointTransforms != null && root.customWaypointTransforms.Count >= 4)
            {
                var pts = new List<Vector3>();
                foreach (var t in root.customWaypointTransforms)
                {
                    if (t != null) pts.Add(t.position);
                }
                if (pts.Count >= 4) return new CatmullRomSpline(pts, 24);
            }

            return null;
        }

        private static void BuildGrassGround(Transform parent, Shader urpLit)
        {
            Texture2D grassTex = SaveTextureAsset("Grass_Tex.png", GenerateGrassTexture());
            Material grassMat = CreateOrLoadMaterial("Ground_Grass_Mat.mat", urpLit, new Color(0.20f, 0.42f, 0.16f), 0.08f, 0f, true);
            grassMat.SetTexture("_BaseMap", grassTex);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Nature_Grass_Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.position = new Vector3(160f, -0.85f, 20f);
            ground.transform.localScale = new Vector3(1400f, 1.0f, 1400f);
            ground.GetComponent<Renderer>().sharedMaterial = grassMat;

            // Ensure solid BoxCollider for off-track driving
            BoxCollider col = ground.GetComponent<BoxCollider>();
            if (col == null) col = ground.AddComponent<BoxCollider>();
            col.material = new TrackPhysicsMaterial("Grass_PhysMat")
            {
                dynamicFriction = 0.75f,
                staticFriction = 0.85f
            };
        }

        private static void BuildTreesAroundCircuit(Transform parent, CatmullRomSpline spline, TrackEnvironmentRoot root, Shader urpLit)
        {
            Material trunkMat = CreateOrLoadMaterial("Tree_Trunk_Mat.mat", urpLit, new Color(0.32f, 0.21f, 0.12f), 0.1f, 0f, true);
            Material foliageMatA = CreateOrLoadMaterial("Tree_Foliage_A_Mat.mat", urpLit, new Color(0.11f, 0.34f, 0.12f), 0.12f, 0f, true);
            Material foliageMatB = CreateOrLoadMaterial("Tree_Foliage_B_Mat.mat", urpLit, new Color(0.16f, 0.42f, 0.15f), 0.12f, 0f, true);

            var samples = spline.Samples;
            Vector3 center = Vector3.zero;
            for (int i = 0; i < samples.Count; i++) center += samples[i].Position;
            center /= samples.Count;

            System.Random rng = new System.Random(root.randomSeed);
            float minClearSq = root.minTreeClearanceFromTrack * root.minTreeClearanceFromTrack;

            int placed = 0;
            int attempts = 0;
            while (placed < root.treeCount && attempts < root.treeCount * 12)
            {
                attempts++;
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float dist = (float)(rng.NextDouble() * root.forestSpreadRadius);
                Vector3 candidate = new Vector3(
                    center.x + Mathf.Cos(angle) * dist * 1.25f,
                    -0.35f,
                    center.z + Mathf.Sin(angle) * dist * 1.25f
                );

                // Check clearance from all track samples
                bool tooClose = false;
                for (int s = 0; s < samples.Count; s += 2)
                {
                    Vector3 diff = candidate - samples[s].Position;
                    diff.y = 0f;
                    if (diff.sqrMagnitude < minClearSq)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                float scale = 0.85f + (float)rng.NextDouble() * 0.75f;
                GameObject tree = new GameObject($"Tree_{placed + 1:000}");
                tree.transform.SetParent(parent, false);
                tree.transform.position = candidate;
                tree.transform.localScale = Vector3.one * scale;

                // Trunk with CapsuleCollider
                GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                trunk.name = "Trunk";
                trunk.transform.SetParent(tree.transform, false);
                trunk.transform.localPosition = new Vector3(0f, 2.0f, 0f);
                trunk.transform.localScale = new Vector3(0.55f, 2.0f, 0.55f);
                trunk.GetComponent<Renderer>().sharedMaterial = trunkMat;

                // Layered foliage spheres (no collider needed on upper leaves)
                Material leafMat = (placed % 2 == 0) ? foliageMatA : foliageMatB;
                CreateFoliageTier(tree.transform, "Canopy_Lower", new Vector3(0f, 4.4f, 0f), new Vector3(3.4f, 2.4f, 3.4f), leafMat);
                CreateFoliageTier(tree.transform, "Canopy_Upper", new Vector3(0f, 6.1f, 0f), new Vector3(2.3f, 2.1f, 2.3f), leafMat);

                placed++;
            }
        }

        private static void CreateFoliageTier(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            canopy.name = name;
            canopy.transform.SetParent(parent, false);
            canopy.transform.localPosition = pos;
            canopy.transform.localScale = scale;
            canopy.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(canopy.GetComponent<Collider>());
        }

        private static void BuildRunoffsAndGravel(Transform parent, CatmullRomSpline spline, TrackEnvironmentRoot root, Shader urpLit)
        {
            Texture2D runoffTex = SaveTextureAsset("Runoff_Stripes_Tex.png", GenerateRunoffStripeTexture());
            Material runoffMat = CreateOrLoadMaterial("Runoff_Painted_Mat.mat", urpLit, Color.white, 0.25f, 0f, true);
            runoffMat.SetTexture("_BaseMap", runoffTex);

            Texture2D gravelTex = SaveTextureAsset("Gravel_Trap_Tex.png", GenerateGravelTexture());
            Material gravelMat = CreateOrLoadMaterial("Gravel_Trap_Mat.mat", urpLit, Color.white, 0.05f, 0f, true);
            gravelMat.SetTexture("_BaseMap", gravelTex);

            TrackPhysicsMaterial gravelPhys = new TrackPhysicsMaterial("Gravel_PhysMat")
            {
                dynamicFriction = 1.6f,
                staticFriction = 1.7f,
                bounciness = 0f
            };

            var samples = spline.Samples;
            int n = samples.Count;
            float halfTrack = root.trackWidth * 0.5f;

            var runoffVerts = new List<Vector3>();
            var runoffUvs = new List<Vector2>();
            var runoffTris = new List<int>();

            var gravelVerts = new List<Vector3>();
            var gravelUvs = new List<Vector2>();
            var gravelTris = new List<int>();

            for (int i = 0; i < n; i++)
            {
                var s0 = samples[i];
                if (Mathf.Abs(s0.SignedCurvature) < root.sharpCornerCurvatureThreshold) continue;

                var s1 = samples[(i + 1) % n];
                // Outer side of the turn
                float outerSign = s0.SignedCurvature > 0f ? -1f : 1f;

                float asphaltEnd = halfTrack + 0.2f;
                float paintedEnd = asphaltEnd + root.runOffWidth * 0.5f;
                float gravelEnd = asphaltEnd + root.runOffWidth;

                AddStripQuad(runoffVerts, runoffUvs, runoffTris, s0, s1, outerSign, asphaltEnd, paintedEnd, i);
                AddStripQuad(gravelVerts, gravelUvs, gravelTris, s0, s1, outerSign, paintedEnd, gravelEnd, i);
            }

            CreateMeshObject(parent, "Runoff_PaintedAsphalt", runoffVerts, runoffUvs, runoffTris, runoffMat, null);
            CreateMeshObject(parent, "Runoff_GravelTraps", gravelVerts, gravelUvs, gravelTris, gravelMat, gravelPhys);
        }

        private static void AddStripQuad(
            List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            CatmullRomSpline.SamplePoint s0, CatmullRomSpline.SamplePoint s1,
            float sideSign, float innerDist, float outerDist, int idxSeed)
        {
            int baseIdx = verts.Count;
            Vector3 p0In = s0.Position + s0.Right * (sideSign * innerDist) - Vector3.up * 0.02f;
            Vector3 p0Out = s0.Position + s0.Right * (sideSign * outerDist) - Vector3.up * 0.03f;
            Vector3 p1In = s1.Position + s1.Right * (sideSign * innerDist) - Vector3.up * 0.02f;
            Vector3 p1Out = s1.Position + s1.Right * (sideSign * outerDist) - Vector3.up * 0.03f;

            verts.Add(p0In); verts.Add(p0Out); verts.Add(p1In); verts.Add(p1Out);
            uvs.Add(new Vector2(0f, idxSeed * 0.4f));
            uvs.Add(new Vector2(1f, idxSeed * 0.4f));
            uvs.Add(new Vector2(0f, (idxSeed + 1) * 0.4f));
            uvs.Add(new Vector2(1f, (idxSeed + 1) * 0.4f));

            if (sideSign > 0f)
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

        private static void BuildBarriersAndCatchFencing(Transform parent, CatmullRomSpline spline, TrackEnvironmentRoot root, Shader urpLit)
        {
            Material concreteMat = CreateOrLoadMaterial("Barrier_Concrete_Mat.mat", urpLit, new Color(0.75f, 0.77f, 0.79f), 0.18f, 0f, true);
            Material fencePostMat = CreateOrLoadMaterial("Fence_Post_Mat.mat", urpLit, new Color(0.28f, 0.30f, 0.34f), 0.4f, 0.75f, true);

            Texture2D fenceTex = SaveTextureAsset("CatchFence_Tex.png", GenerateChainLinkTexture());
            Material fencePanelMat = CreateOrLoadMaterial("CatchFence_Panel_Mat.mat", urpLit, new Color(0.65f, 0.68f, 0.72f, 0.45f), 0.3f, 0.5f, true);
            fencePanelMat.SetTexture("_BaseMap", fenceTex);
            fencePanelMat.SetFloat("_Surface", 1f); // Transparent in URP
            fencePanelMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            fencePanelMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            fencePanelMat.SetInt("_ZWrite", 0);
            fencePanelMat.renderQueue = 3000;

            float wallOffset = root.trackWidth * 0.5f + root.barrierOffsetFromEdge;
            BuildContinuousWall(parent, "Concrete_Wall_Left", spline, -wallOffset, 0.95f, 0.5f, concreteMat);
            BuildContinuousWall(parent, "Concrete_Wall_Right", spline, wallOffset, 0.95f, 0.5f, concreteMat);

            // Modular catch fence posts & panels along both barriers
            float postSpacing = 12f;
            float dist = 0f;
            int postIdx = 0;
            while (dist < spline.TotalLength - postSpacing)
            {
                var s0 = spline.GetSampleAtDistance(dist);
                var s1 = spline.GetSampleAtDistance(dist + postSpacing);

                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 p0 = s0.Position + s0.Right * (side * (wallOffset + 0.25f)) + Vector3.up * 0.95f;
                    Vector3 p1 = s1.Position + s1.Right * (side * (wallOffset + 0.25f)) + Vector3.up * 0.95f;

                    GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    post.name = $"FencePost_{(side < 0 ? "L" : "R")}_{postIdx}";
                    post.transform.SetParent(parent, false);
                    post.transform.position = p0 + Vector3.up * (root.catchFenceHeight * 0.5f);
                    post.transform.localScale = new Vector3(0.12f, root.catchFenceHeight, 0.12f);
                    post.GetComponent<Renderer>().sharedMaterial = fencePostMat;
                    Object.DestroyImmediate(post.GetComponent<Collider>());

                    Vector3 mid = (p0 + p1) * 0.5f + Vector3.up * (root.catchFenceHeight * 0.5f);
                    float segLen = Vector3.Distance(p0, p1);
                    GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    panel.name = $"FencePanel_{(side < 0 ? "L" : "R")}_{postIdx}";
                    panel.transform.SetParent(parent, false);
                    panel.transform.position = mid;
                    panel.transform.rotation = Quaternion.LookRotation((p1 - p0).normalized, Vector3.up);
                    panel.transform.localScale = new Vector3(0.04f, root.catchFenceHeight, segLen);
                    panel.GetComponent<Renderer>().sharedMaterial = fencePanelMat;
                    Object.DestroyImmediate(panel.GetComponent<Collider>());
                }

                postIdx++;
                dist += postSpacing;
            }
        }

        private static void BuildContinuousWall(Transform parent, string name, CatmullRomSpline spline, float lateralOffset, float height, float thickness, Material mat)
        {
            var samples = spline.Samples;
            int n = samples.Count;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            float sign = Mathf.Sign(lateralOffset);
            float inner = Mathf.Abs(lateralOffset);
            float outer = inner + thickness;

            for (int i = 0; i < n; i++)
            {
                var s0 = samples[i];
                var s1 = samples[(i + 1) % n];

                Vector3 b0In = s0.Position + s0.Right * (sign * inner);
                Vector3 b0Out = s0.Position + s0.Right * (sign * outer);
                Vector3 t0In = b0In + Vector3.up * height;
                Vector3 t0Out = b0Out + Vector3.up * height;

                Vector3 b1In = s1.Position + s1.Right * (sign * inner);
                Vector3 b1Out = s1.Position + s1.Right * (sign * outer);
                Vector3 t1In = b1In + Vector3.up * height;
                Vector3 t1Out = b1Out + Vector3.up * height;

                AddQuad(verts, uvs, tris, b0In, t0In, t1In, b1In, sign > 0f);
                AddQuad(verts, uvs, tris, t0In, t0Out, t1Out, t1In, sign > 0f);
                AddQuad(verts, uvs, tris, t0Out, b0Out, b1Out, t1Out, sign > 0f);
            }

            CreateMeshObject(parent, name, verts, uvs, tris, mat, null);
        }

        private static void BuildTecproBlocks(Transform parent, CatmullRomSpline spline, TrackEnvironmentRoot root, Shader urpLit)
        {
            Material redTecpro = CreateOrLoadMaterial("Tecpro_Red_Mat.mat", urpLit, new Color(0.86f, 0.1f, 0.1f), 0.25f, 0f, true);
            Material whiteTecpro = CreateOrLoadMaterial("Tecpro_White_Mat.mat", urpLit, new Color(0.94f, 0.94f, 0.94f), 0.25f, 0f, true);

            float wallOffset = root.trackWidth * 0.5f + root.barrierOffsetFromEdge - 0.65f;
            float step = 3.2f;
            float dist = 0f;
            int idx = 0;

            while (dist < spline.TotalLength)
            {
                var s = spline.GetSampleAtDistance(dist);
                if (Mathf.Abs(s.SignedCurvature) >= root.sharpCornerCurvatureThreshold)
                {
                    float outerSign = s.SignedCurvature > 0f ? -1f : 1f;
                    GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    block.name = $"Tecpro_Block_{idx}";
                    block.transform.SetParent(parent, false);
                    block.transform.position = s.Position + s.Right * (outerSign * wallOffset) + Vector3.up * 0.55f;
                    block.transform.rotation = Quaternion.LookRotation(s.Tangent, Vector3.up);
                    block.transform.localScale = new Vector3(0.85f, 1.1f, 2.6f);
                    block.GetComponent<Renderer>().sharedMaterial = (idx % 2 == 0) ? redTecpro : whiteTecpro;
                    idx++;
                }
                dist += step;
            }
        }

        private static void BuildBrakeDistanceBoards(Transform parent, CatmullRomSpline spline, TrackEnvironmentRoot root, Shader urpLit)
        {
            var samples = spline.Samples;
            int n = samples.Count;
            float rightOffset = root.trackWidth * 0.5f + root.barrierOffsetFromEdge - 0.4f;

            Material board300 = CreateBrakeBoardMaterial("BrakeBoard_300.mat", "300", urpLit);
            Material board200 = CreateBrakeBoardMaterial("BrakeBoard_200.mat", "200", urpLit);
            Material board100 = CreateBrakeBoardMaterial("BrakeBoard_100.mat", "100", urpLit);
            Material postMat = CreateOrLoadMaterial("BrakeBoard_Post_Mat.mat", urpLit, new Color(0.2f, 0.2f, 0.22f), 0.3f, 0.6f, true);

            int zoneIdx = 0;
            bool inSharpZone = false;

            for (int i = 0; i < n; i++)
            {
                bool isSharp = Mathf.Abs(samples[i].SignedCurvature) >= root.sharpCornerCurvatureThreshold;
                if (isSharp && !inSharpZone)
                {
                    inSharpZone = true;
                    float entryDist = samples[i].Distance;
                    // Place 300m, 200m, 100m boards (scaled to circuit proportions: 75m, 50m, 25m ahead of apex)
                    SpawnBrakeBoard(parent, spline, entryDist - 75f, rightOffset, $"Zone{zoneIdx}_300m", board300, postMat);
                    SpawnBrakeBoard(parent, spline, entryDist - 50f, rightOffset, $"Zone{zoneIdx}_200m", board200, postMat);
                    SpawnBrakeBoard(parent, spline, entryDist - 25f, rightOffset, $"Zone{zoneIdx}_100m", board100, postMat);
                    zoneIdx++;
                }
                else if (!isSharp)
                {
                    inSharpZone = false;
                }
            }
        }

        private static void SpawnBrakeBoard(Transform parent, CatmullRomSpline spline, float dist, float rightOffset, string label, Material boardMat, Material postMat)
        {
            var s = spline.GetSampleAtDistance(dist);
            GameObject root = new GameObject($"BrakeBoard_{label}");
            root.transform.SetParent(parent, false);
            root.transform.position = s.Position + s.Right * rightOffset;
            root.transform.rotation = Quaternion.LookRotation(s.Tangent, Vector3.up);

            GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Board";
            board.transform.SetParent(root.transform, false);
            board.transform.localPosition = new Vector3(0f, 1.55f, 0f);
            board.transform.localScale = new Vector3(1.4f, 0.9f, 0.12f);
            board.GetComponent<Renderer>().sharedMaterial = boardMat;
            Object.DestroyImmediate(board.GetComponent<Collider>());

            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Post";
            post.transform.SetParent(root.transform, false);
            post.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            post.transform.localScale = new Vector3(0.12f, 1.3f, 0.12f);
            post.GetComponent<Renderer>().sharedMaterial = postMat;
            Object.DestroyImmediate(post.GetComponent<Collider>());
        }

        private static void BuildMarshalPosts(Transform parent, CatmullRomSpline spline, TrackEnvironmentRoot root, Shader urpLit, TrackFlagController flagCtrl)
        {
            Material hutMat = CreateOrLoadMaterial("Marshal_Hut_Mat.mat", urpLit, new Color(0.88f, 0.88f, 0.9f), 0.2f, 0f, true);
            Material poleMat = CreateOrLoadMaterial("Marshal_Pole_Mat.mat", urpLit, new Color(0.25f, 0.25f, 0.28f), 0.3f, 0.7f, true);

            flagCtrl.greenFlagMat = CreateEmissiveFlagMaterial("Flag_Green_Mat.mat", new Color(0.05f, 0.95f, 0.15f), urpLit);
            flagCtrl.yellowFlagMat = CreateEmissiveFlagMaterial("Flag_Yellow_Mat.mat", new Color(1.0f, 0.82f, 0.05f), urpLit);
            flagCtrl.redFlagMat = CreateEmissiveFlagMaterial("Flag_Red_Mat.mat", new Color(1.0f, 0.08f, 0.05f), urpLit);

            float offset = root.trackWidth * 0.5f + root.barrierOffsetFromEdge + 1.8f;
            int postCount = Mathf.Max(1, Mathf.FloorToInt(spline.TotalLength / root.marshalPostIntervalMeters));

            for (int i = 0; i < postCount; i++)
            {
                float d = i * root.marshalPostIntervalMeters + 40f;
                var s = spline.GetSampleAtDistance(d);
                float side = (i % 2 == 0) ? 1f : -1f;

                GameObject postRoot = new GameObject($"MarshalPost_{i + 1:00}");
                postRoot.transform.SetParent(parent, false);
                postRoot.transform.position = s.Position + s.Right * (side * offset);
                postRoot.transform.rotation = Quaternion.LookRotation(s.Tangent, Vector3.up);

                // Small Marshal Hut
                GameObject hut = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hut.name = "Marshal_Hut";
                hut.transform.SetParent(postRoot.transform, false);
                hut.transform.localPosition = new Vector3(0f, 1.1f, 0f);
                hut.transform.localScale = new Vector3(2.0f, 2.2f, 2.0f);
                hut.GetComponent<Renderer>().sharedMaterial = hutMat;

                // LED Pole + Emissive Panel
                GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pole.name = "LED_Pole";
                pole.transform.SetParent(postRoot.transform, false);
                pole.transform.localPosition = new Vector3(-side * 1.1f, 2.2f, 0f);
                pole.transform.localScale = new Vector3(0.14f, 2.2f, 0.14f);
                pole.GetComponent<Renderer>().sharedMaterial = poleMat;
                Object.DestroyImmediate(pole.GetComponent<Collider>());

                GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = "LED_Flag_Panel";
                panel.transform.SetParent(postRoot.transform, false);
                panel.transform.localPosition = new Vector3(-side * 1.1f, 4.1f, 0f);
                panel.transform.localScale = new Vector3(1.1f, 0.85f, 0.16f);
                Renderer r = panel.GetComponent<Renderer>();
                r.sharedMaterial = flagCtrl.greenFlagMat;
                Object.DestroyImmediate(panel.GetComponent<Collider>());

                flagCtrl.flagPanelRenderers.Add(r);
            }
        }

        private static void BuildStartGantryAtIndex0(Transform parent, CatmullRomSpline spline, TrackEnvironmentRoot root, Shader urpLit, TrackFlagController flagCtrl)
        {
            var s0 = spline.Samples[0];
            GameObject gantry = new GameObject("StartFinish_Truss_Gantry");
            gantry.transform.SetParent(parent, false);
            gantry.transform.position = s0.Position;
            gantry.transform.rotation = Quaternion.LookRotation(s0.Tangent, Vector3.up);

            Material steelMat = CreateOrLoadMaterial("Gantry_Truss_Mat.mat", urpLit, new Color(0.22f, 0.24f, 0.28f), 0.45f, 0.8f, true);
            flagCtrl.startLightOnMat = CreateEmissiveFlagMaterial("Gantry_Light_On.mat", new Color(1.0f, 0.05f, 0.05f), urpLit);
            flagCtrl.startLightOffMat = CreateOrLoadMaterial("Gantry_Light_Off.mat", urpLit, new Color(0.14f, 0.02f, 0.02f), 0.2f, 0f, true);

            float span = root.trackWidth + root.barrierOffsetFromEdge * 2f;
            float h = 6.5f;

            CreateTrussBox(gantry.transform, "Pillar_L", new Vector3(-span * 0.5f, h * 0.5f, 0f), new Vector3(0.75f, h, 0.75f), steelMat);
            CreateTrussBox(gantry.transform, "Pillar_R", new Vector3(span * 0.5f, h * 0.5f, 0f), new Vector3(0.75f, h, 0.75f), steelMat);
            CreateTrussBox(gantry.transform, "Overhead_Truss", new Vector3(0f, h, 0f), new Vector3(span + 0.8f, 1.1f, 1.1f), steelMat);

            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * 1.45f;
                GameObject pod = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pod.name = $"StartLight_Pod_{i + 1}";
                pod.transform.SetParent(gantry.transform, false);
                pod.transform.localPosition = new Vector3(x, h - 0.85f, -0.6f);
                pod.transform.localScale = Vector3.one * 0.55f;
                Object.DestroyImmediate(pod.GetComponent<Collider>());

                Renderer r = pod.GetComponent<Renderer>();
                r.sharedMaterial = flagCtrl.startLightOffMat;
                flagCtrl.startLightPodRenderers.Add(r);
            }
        }

        private static void CreateTrussBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = pos;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static void CreateMeshObject(
            Transform parent, string name,
            List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Material mat, TrackPhysicsMaterial physMat)
        {
            if (verts.Count == 0) return;

            Mesh mesh = new Mesh
            {
                name = name + "_Mesh",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;

            MeshCollider mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            if (physMat != null) mc.sharedMaterial = physMat;
        }

        private static void AddQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool flip)
        {
            int idx = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1));
            uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));

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

        private static Material CreateOrLoadMaterial(string fileName, Shader shader, Color baseColor, float smoothness, float metallic, bool instanced)
        {
            string path = $"{MatFolder}/{fileName}";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            mat.enableInstancing = instanced;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateEmissiveFlagMaterial(string fileName, Color color, Shader shader)
        {
            Material mat = CreateOrLoadMaterial(fileName, shader, color * 0.4f, 0.4f, 0f, true);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 3.5f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateBrakeBoardMaterial(string fileName, string digits, Shader shader)
        {
            Texture2D tex = SaveTextureAsset($"Brake_{digits}.png", GenerateBrakeBoardTexture(digits));
            Material mat = CreateOrLoadMaterial(fileName, shader, Color.white, 0.25f, 0f, true);
            mat.SetTexture("_BaseMap", tex);
            return mat;
        }

        private static Texture2D SaveTextureAsset(string fileName, Texture2D tex)
        {
            string path = $"{TexFolder}/{fileName}";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D GenerateGrassTexture()
        {
            Texture2D tex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.18f, y * 0.18f) * 0.10f - 0.05f;
                    tex.SetPixel(x, y, new Color(0.16f + n, 0.36f + n, 0.12f + n * 0.5f));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateRunoffStripeTexture()
        {
            Texture2D tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Color blue = new Color(0.10f, 0.32f, 0.78f);
            Color red = new Color(0.82f, 0.12f, 0.12f);
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    bool stripe = ((x + y) / 16) % 2 == 0;
                    tex.SetPixel(x, y, stripe ? blue : red);
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateGravelTexture()
        {
            Texture2D tex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            System.Random rng = new System.Random(101);
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    float v = (float)rng.NextDouble() * 0.18f - 0.09f;
                    tex.SetPixel(x, y, new Color(0.68f + v, 0.62f + v, 0.50f + v));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateChainLinkTexture()
        {
            Texture2D tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    bool wire = (x % 8 == y % 8) || ((x + y) % 8 == 0);
                    tex.SetPixel(x, y, wire ? new Color(0.7f, 0.72f, 0.75f, 0.9f) : new Color(0f, 0f, 0f, 0.05f));
                }
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateBrakeBoardTexture(string digits)
        {
            Texture2D tex = new Texture2D(96, 64, TextureFormat.RGBA32, false);
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 96; x++)
                {
                    bool border = x < 4 || x >= 92 || y < 4 || y >= 60;
                    tex.SetPixel(x, y, border ? Color.red : Color.white);
                }
            }

            // Simple 3x5 pixel-font renderer for "300", "200", "100"
            DrawDigit(tex, digits[0], 18, 22, 5, Color.black);
            DrawDigit(tex, digits[1], 40, 22, 5, Color.black);
            DrawDigit(tex, digits[2], 62, 22, 5, Color.black);
            tex.Apply();
            return tex;
        }

        private static void DrawDigit(Texture2D tex, char ch, int ox, int oy, int scale, Color col)
        {
            string[] map = ch switch
            {
                '3' => new[] { "111", "001", "111", "001", "111" },
                '2' => new[] { "111", "001", "111", "100", "111" },
                '1' => new[] { "010", "110", "010", "010", "111" },
                _ => new[] { "111", "101", "101", "101", "111" }
            };

            for (int row = 0; row < 5; row++)
            {
                for (int c = 0; c < 3; c++)
                {
                    if (map[4 - row][c] != '1') continue;
                    for (int dy = 0; dy < scale; dy++)
                        for (int dx = 0; dx < scale; dx++)
                            tex.SetPixel(ox + c * scale + dx, oy + row * scale + dy, col);
                }
            }
        }

        private static Transform CreateGroup(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        private static void EnsureAssetFolders()
        {
            if (!Directory.Exists(MatFolder)) Directory.CreateDirectory(MatFolder);
            if (!Directory.Exists(TexFolder)) Directory.CreateDirectory(TexFolder);
        }
    }
}
