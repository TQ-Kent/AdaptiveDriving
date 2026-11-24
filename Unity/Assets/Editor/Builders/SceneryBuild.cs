using System.Collections.Generic;
using F1Racing.Track;
using F1Racing.Vehicle;
using UnityEngine;

namespace F1Racing.EditorTools
{
    /// <summary>
    /// Editor-only: builds trackside scenery (covered grandstands, Jumbotrons, fan village with
    /// Ferris wheel, Parkland / Twilight Marina perimeter) under a <see cref="CircuitSceneryPopulator"/>.
    /// </summary>
    internal class SceneryBuild
    {
        private readonly CircuitSceneryPopulator p;
        private SceneryBuild(CircuitSceneryPopulator populator) { p = populator; }

        // Same names as the populator's fields so the moved build code reads unchanged.
        private Transform transform => p.transform;
        private CircuitTheme circuitTheme => p.circuitTheme;
        private F1TrackGenerator trackGenerator => p.trackGenerator;
        private int randomSeed => p.randomSeed;
        private Vector2Int jumbotronResolution => p.jumbotronResolution;
        private float jumbotronRefreshFps => p.jumbotronRefreshFps;
        private int parklandTreeCount => p.parklandTreeCount;
        private int parklandHillCount => p.parklandHillCount;
        private int marinaFloodlightCount => p.marinaFloodlightCount;
        private int marinaPalmCount => p.marinaPalmCount;
        private int marinaSkyscraperCount => p.marinaSkyscraperCount;
        private int marinaYachtCount => p.marinaYachtCount;

        private RenderTexture _jumbotronRT;
        private Material _jumbotronScreenMat;

        public static void Build(CircuitSceneryPopulator populator)
        {
            if (populator.trackGenerator == null)
                populator.trackGenerator = Object.FindAnyObjectByType<F1TrackGenerator>();
            new SceneryBuild(populator).BuildCircuitScenery();
        }

        private void BuildCircuitScenery()
        {
            BuildUtil.ClearChildren(transform);

            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");

            SetupJumbotronBroadcast(urpUnlit);

            // 1. Covered Grandstands & Jumbotrons
            Transform grandstandGroup = CreateGroup("01_Grandstands_And_Jumbotrons");
            BuildGrandstandsAndJumbotrons(grandstandGroup, urpLit);

            // 2. Fan Village, Food Court & Animated Ferris Wheel
            Transform fanGroup = CreateGroup("02_FanVillage_And_FerrisWheel");
            BuildFanVillage(fanGroup, urpLit);

            // 3. Configurable Perimeter Theme
            Transform themeGroup = CreateGroup($"03_Perimeter_{circuitTheme}");
            if (circuitTheme == CircuitTheme.Parkland)
                BuildParklandTheme(themeGroup, urpLit);
            else
                BuildTwilightMarinaTheme(themeGroup, urpLit);
        }

        private void SetupJumbotronBroadcast(Shader urpUnlit)
        {
            _jumbotronRT = new RenderTexture(jumbotronResolution.x, jumbotronResolution.y, 16, RenderTextureFormat.Default)
            {
                name = "F1_Jumbotron_RT"
            };
            _jumbotronRT.Create();

            _jumbotronScreenMat = new Material(urpUnlit) { name = "Jumbotron_Screen_Mat" };
            _jumbotronScreenMat.SetTexture("_BaseMap", _jumbotronRT);

            GameObject camObj = new GameObject("Jumbotron_BroadcastCamera");
            camObj.transform.SetParent(transform, false);
            Camera cam = camObj.AddComponent<Camera>();
            cam.targetTexture = _jumbotronRT;
            cam.fieldOfView = 48f;
            cam.farClipPlane = 450f;

            JumbotronBroadcastCamera broadcast = camObj.AddComponent<JumbotronBroadcastCamera>();
            broadcast.refreshRateFps = jumbotronRefreshFps;
            F1CarController car1 = Object.FindAnyObjectByType<F1CarController>();
            if (car1 != null) broadcast.targetCar = car1.transform;
        }

        private void BuildGrandstandsAndJumbotrons(Transform parent, Shader urpLit)
        {
            Material concreteMat = CreateMat("Scenery_Concrete_Mat", urpLit, new Color(0.68f, 0.70f, 0.72f), 0.18f, 0f);
            Material steelMat = CreateMat("Scenery_Steel_Mat", urpLit, new Color(0.18f, 0.20f, 0.23f), 0.45f, 0.75f);
            Material roofMat = CreateMat("Scenery_Roof_Mat", urpLit, new Color(0.90f, 0.91f, 0.93f), 0.35f, 0.1f);
            Material seatRedMat = CreateMat("Scenery_SeatRed_Mat", urpLit, new Color(0.82f, 0.12f, 0.12f), 0.3f, 0f);
            Material seatBlueMat = CreateMat("Scenery_SeatBlue_Mat", urpLit, new Color(0.12f, 0.34f, 0.82f), 0.3f, 0f);

            // Main Grandstand opposite Pit Complex (-X side of main straight), plus 2 corner grandstands
            Vector3[] standPositions =
            {
                new Vector3(-24f, 0f, 15f),
                new Vector3(115f, 7.5f, 325f),
                new Vector3(382f, 8.5f, 10f)
            };
            float[] standYaw = { 90f, 180f, -90f };
            float[] standLengths = { 56f, 40f, 40f };

            // Matching Jumbotrons placed across the track from each grandstand
            Vector3[] tvPositions =
            {
                new Vector3(11.5f, 0f, 15f),
                new Vector3(115f, 8.0f, 272f),
                new Vector3(332f, 9.0f, 10f)
            };
            float[] tvYaw = { -90f, 0f, 90f };

            for (int i = 0; i < standPositions.Length; i++)
            {
                BuildCoveredGrandstand(parent, $"Covered_Grandstand_{i + 1}", standPositions[i], standYaw[i],
                    standLengths[i], concreteMat, steelMat, roofMat, (i % 2 == 0) ? seatRedMat : seatBlueMat);

                BuildJumbotron(parent, $"Jumbotron_TV_{i + 1}", tvPositions[i], tvYaw[i], steelMat);
            }
        }

        private void BuildCoveredGrandstand(
            Transform parent, string name, Vector3 pos, float yaw, float length,
            Material concreteMat, Material steelMat, Material roofMat, Material seatMat)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            var lodRenderers = new List<Renderer>();

            // 5 Tiered Seating Rows
            for (int tier = 0; tier < 5; tier++)
            {
                float y = 0.8f + tier * 1.15f;
                float z = -tier * 1.9f;
                GameObject row = CreateBox(root.transform, $"Tier_{tier + 1}", new Vector3(0f, y * 0.5f, z), new Vector3(length, y, 1.9f), concreteMat, true);
                GameObject seats = CreateBox(root.transform, $"Seats_{tier + 1}", new Vector3(0f, y + 0.15f, z), new Vector3(length - 1f, 0.25f, 1.5f), seatMat, false);
                lodRenderers.Add(row.GetComponent<Renderer>());
                lodRenderers.Add(seats.GetComponent<Renderer>());
            }

            // Left & Right Staircases
            for (int side = -1; side <= 1; side += 2)
            {
                float sx = side * (length * 0.5f + 1.4f);
                for (int step = 0; step < 6; step++)
                {
                    GameObject st = CreateBox(root.transform, $"Stair_{side}_{step}",
                        new Vector3(sx, 0.45f + step * 0.9f, -step * 1.55f),
                        new Vector3(2.2f, 0.45f, 1.6f), concreteMat, true);
                    lodRenderers.Add(st.GetComponent<Renderer>());
                }
            }

            // Support Columns + Angled Canopy Roof
            for (int c = -2; c <= 2; c++)
            {
                float cx = c * (length * 0.22f);
                GameObject col = CreateBox(root.transform, $"RoofPillar_{c + 3}", new Vector3(cx, 5.2f, -8.5f), new Vector3(0.45f, 10.4f, 0.45f), steelMat, false);
                lodRenderers.Add(col.GetComponent<Renderer>());
            }

            GameObject roof = CreateBox(root.transform, "Angled_Roof", new Vector3(0f, 10.6f, -4.0f), new Vector3(length + 2f, 0.35f, 13.5f), roofMat, false);
            roof.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            lodRenderers.Add(roof.GetComponent<Renderer>());

            // LODGroup for 60+ FPS culling at extreme distance
            LODGroup lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.03f, lodRenderers.ToArray()) });
        }

        private void BuildJumbotron(Transform parent, string name, Vector3 pos, float yaw, Material steelMat)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            CreateBox(root.transform, "Support_Pillar_L", new Vector3(-4.2f, 4.5f, 0f), new Vector3(0.55f, 9f, 0.55f), steelMat, true);
            CreateBox(root.transform, "Support_Pillar_R", new Vector3(4.2f, 4.5f, 0f), new Vector3(0.55f, 9f, 0.55f), steelMat, true);
            CreateBox(root.transform, "Screen_Frame", new Vector3(0f, 8.2f, 0f), new Vector3(11.6f, 6.6f, 0.45f), steelMat, false);

            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "LED_Broadcast_Screen";
            screen.transform.SetParent(root.transform, false);
            screen.transform.localPosition = new Vector3(0f, 8.2f, -0.26f);
            screen.transform.localScale = new Vector3(10.8f, 6.0f, 1f);
            screen.GetComponent<Renderer>().sharedMaterial = _jumbotronScreenMat;
            Object.DestroyImmediate(screen.GetComponent<Collider>());
        }

        private void BuildFanVillage(Transform parent, Shader urpLit)
        {
            Vector3 plazaCenter = new Vector3(-58f, 0f, 15f);
            Material plazaMat = CreateMat("FanVillage_Plaza_Mat", urpLit, new Color(0.26f, 0.27f, 0.29f), 0.2f, 0f);
            Material whiteMat = CreateMat("FanVillage_White_Mat", urpLit, new Color(0.94f, 0.94f, 0.94f), 0.35f, 0f);
            Material steelMat = CreateMat("FanVillage_Steel_Mat", urpLit, new Color(0.22f, 0.24f, 0.26f), 0.4f, 0.7f);
            Material tireMat = CreateMat("FanVillage_Tire_Mat", urpLit, new Color(0.08f, 0.08f, 0.08f), 0.15f, 0f);

            // Paved Plaza
            CreateBox(parent, "FanVillage_Plaza", plazaCenter + Vector3.down * 0.04f, new Vector3(44f, 0.1f, 68f), plazaMat, true);

            Color[] truckColors = {
                new Color(0.92f, 0.22f, 0.14f),
                new Color(0.14f, 0.56f, 0.90f),
                new Color(0.96f, 0.72f, 0.10f),
                new Color(0.16f, 0.74f, 0.42f)
            };

            // 4 Food Trucks
            for (int i = 0; i < 4; i++)
            {
                Material truckMat = CreateMat($"FoodTruck_{i + 1}_Mat", urpLit, truckColors[i], 0.4f, 0.1f);
                GameObject truck = new GameObject($"FoodTruck_{i + 1}");
                truck.transform.SetParent(parent, false);
                truck.transform.position = plazaCenter + new Vector3(-8f, 0f, -20f + i * 13f);
                CreateBox(truck.transform, "Body", new Vector3(0f, 1.45f, 0f), new Vector3(2.6f, 2.5f, 6.2f), truckMat, true);
                CreateBox(truck.transform, "Service_Awning", new Vector3(1.8f, 2.2f, 0f), new Vector3(1.3f, 0.1f, 4.0f), whiteMat, false);
                CreateCylinder(truck.transform, "Wheel_F", new Vector3(0f, 0.42f, 2.0f), new Vector3(0.84f, 1.4f, 0.84f), tireMat).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                CreateCylinder(truck.transform, "Wheel_R", new Vector3(0f, 0.42f, -2.0f), new Vector3(0.84f, 1.4f, 0.84f), tireMat).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

            // 8 Outdoor Dining Tables + Umbrellas
            for (int i = 0; i < 8; i++)
            {
                int row = i / 4;
                int col = i % 4;
                Vector3 tPos = plazaCenter + new Vector3(3f + row * 5.5f, 0f, -15f + col * 10f);
                Material umbMat = CreateMat($"FanUmbrella_{i % 4}_Mat", urpLit, truckColors[i % 4], 0.25f, 0f);

                GameObject tbl = new GameObject($"DiningTable_{i + 1}");
                tbl.transform.SetParent(parent, false);
                tbl.transform.position = tPos;
                CreateCylinder(tbl.transform, "Stand", new Vector3(0f, 1.15f, 0f), new Vector3(0.1f, 1.15f, 0.1f), steelMat);
                CreateCylinder(tbl.transform, "TableTop", new Vector3(0f, 0.82f, 0f), new Vector3(1.4f, 0.06f, 1.4f), whiteMat);
                CreateCylinder(tbl.transform, "Umbrella", new Vector3(0f, 2.35f, 0f), new Vector3(2.4f, 0.12f, 2.4f), umbMat);
            }

            // 4 Merchandise Tents
            for (int i = 0; i < 4; i++)
            {
                Material tentMat = CreateMat($"MerchTent_{i + 1}_Mat", urpLit, truckColors[(i + 1) % 4], 0.25f, 0f);
                GameObject tent = new GameObject($"Merchandise_Tent_{i + 1}");
                tent.transform.SetParent(parent, false);
                tent.transform.position = plazaCenter + new Vector3(15f, 0f, -18f + i * 12f);
                CreateBox(tent.transform, "Canopy", new Vector3(0f, 2.8f, 0f), new Vector3(4.5f, 0.45f, 4.5f), tentMat, false);
                CreateBox(tent.transform, "Counter", new Vector3(-1.2f, 0.55f, 0f), new Vector3(0.8f, 1.1f, 3.6f), whiteMat, true);
                CreateBox(tent.transform, "Post_FL", new Vector3(-2.0f, 1.4f, 2.0f), new Vector3(0.15f, 2.8f, 0.15f), steelMat, false);
                CreateBox(tent.transform, "Post_FR", new Vector3(-2.0f, 1.4f, -2.0f), new Vector3(0.15f, 2.8f, 0.15f), steelMat, false);
                CreateBox(tent.transform, "Post_BL", new Vector3(2.0f, 1.4f, 2.0f), new Vector3(0.15f, 2.8f, 0.15f), steelMat, false);
                CreateBox(tent.transform, "Post_BR", new Vector3(2.0f, 1.4f, -2.0f), new Vector3(0.15f, 2.8f, 0.15f), steelMat, false);
            }

            // Animated Ferris Wheel Landmark
            BuildFerrisWheel(parent, plazaCenter + new Vector3(-24f, 0f, 8f), steelMat, whiteMat, truckColors[0], urpLit);
        }

        private void BuildFerrisWheel(Transform parent, Vector3 pos, Material steelMat, Material whiteMat, Color accentColor, Shader urpLit)
        {
            GameObject fwRoot = new GameObject("Landmark_FerrisWheel");
            fwRoot.transform.SetParent(parent, false);
            fwRoot.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 90f, 0f));

            FerrisWheelRotator rotator = fwRoot.AddComponent<FerrisWheelRotator>();
            float hubHeight = 20f;
            float radius = 15.5f;

            // A-Frame Support Legs
            GameObject legL = CreateBox(fwRoot.transform, "Leg_L", new Vector3(-4.8f, hubHeight * 0.5f, -1.6f), new Vector3(0.75f, hubHeight * 1.15f, 0.75f), steelMat, true);
            legL.transform.localRotation = Quaternion.Euler(0f, 0f, -14f);
            GameObject legR = CreateBox(fwRoot.transform, "Leg_R", new Vector3(4.8f, hubHeight * 0.5f, -1.6f), new Vector3(0.75f, hubHeight * 1.15f, 0.75f), steelMat, true);
            legR.transform.localRotation = Quaternion.Euler(0f, 0f, 14f);

            GameObject hub = new GameObject("Wheel_Hub");
            hub.transform.SetParent(fwRoot.transform, false);
            hub.transform.localPosition = new Vector3(0f, hubHeight, 0f);
            rotator.wheelHub = hub.transform;

            Material cabinMat = CreateMat("Ferris_Cabin_Mat", urpLit, accentColor, 0.4f, 0.2f);
            int spokeCount = 12;
            for (int i = 0; i < spokeCount; i++)
            {
                float angle = i * (360f / spokeCount);
                float rad = angle * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);

                GameObject spoke = CreateBox(hub.transform, $"Spoke_{i}", dir * (radius * 0.5f), new Vector3(radius, 0.22f, 0.22f), whiteMat, false);
                spoke.transform.localRotation = Quaternion.Euler(0f, 0f, angle);

                GameObject gondola = new GameObject($"Gondola_{i}");
                gondola.transform.SetParent(hub.transform, false);
                gondola.transform.localPosition = dir * radius;
                CreateBox(gondola.transform, "Cabin", new Vector3(0f, -1.1f, 0f), new Vector3(2.0f, 2.0f, 2.2f), cabinMat, false);
                rotator.gondolas.Add(gondola.transform);
            }
        }

        private void BuildParklandTheme(Transform parent, Shader urpLit)
        {
            Material hillMat = CreateMat("Parkland_Hill_Mat", urpLit, new Color(0.16f, 0.38f, 0.14f), 0.08f, 0f);
            Material trunkMat = CreateMat("Parkland_Trunk_Mat", urpLit, new Color(0.32f, 0.20f, 0.11f), 0.1f, 0f);
            Material pineMat = CreateMat("Parkland_Pine_Mat", urpLit, new Color(0.09f, 0.30f, 0.12f), 0.1f, 0f);
            Material helipadMat = CreateMat("Parkland_Helipad_Mat", urpLit, new Color(0.34f, 0.36f, 0.38f), 0.2f, 0f);
            Material yellowMat = CreateMat("Parkland_Yellow_Mat", urpLit, new Color(0.95f, 0.80f, 0.08f), 0.3f, 0f);

            System.Random rng = new System.Random(randomSeed);

            // Grassy Hills around outer perimeter
            for (int i = 0; i < parklandHillCount; i++)
            {
                float angle = i * (Mathf.PI * 2f / parklandHillCount);
                Vector3 pos = new Vector3(160f + Mathf.Cos(angle) * 290f, -2.5f, 20f + Mathf.Sin(angle) * 290f);
                GameObject hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hill.name = $"Grassy_Hill_{i + 1}";
                hill.transform.SetParent(parent, false);
                hill.transform.position = pos;
                hill.transform.localScale = new Vector3(95f + (float)rng.NextDouble() * 45f, 22f + (float)rng.NextDouble() * 14f, 95f + (float)rng.NextDouble() * 45f);
                hill.GetComponent<Renderer>().sharedMaterial = hillMat;
            }

            // Low-Poly Pine Trees with LODGroup
            for (int i = 0; i < parklandTreeCount; i++)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float dist = 210f + (float)(rng.NextDouble() * 110f);
                Vector3 pos = new Vector3(160f + Mathf.Cos(angle) * dist, -0.35f, 20f + Mathf.Sin(angle) * dist);

                GameObject tree = new GameObject($"Parkland_Pine_{i + 1}");
                tree.transform.SetParent(parent, false);
                tree.transform.position = pos;

                GameObject trunk = CreateCylinder(tree.transform, "Trunk", new Vector3(0f, 1.8f, 0f), new Vector3(0.5f, 1.8f, 0.5f), trunkMat);
                GameObject tier1 = CreateBox(tree.transform, "Foliage_Base", new Vector3(0f, 4.2f, 0f), new Vector3(3.2f, 2.6f, 3.2f), pineMat, false);
                GameObject tier2 = CreateBox(tree.transform, "Foliage_Top", new Vector3(0f, 6.2f, 0f), new Vector3(1.9f, 2.2f, 1.9f), pineMat, false);

                LODGroup lod = tree.AddComponent<LODGroup>();
                lod.SetLODs(new[]
                {
                    new LOD(0.06f, new[] { trunk.GetComponent<Renderer>(), tier1.GetComponent<Renderer>(), tier2.GetComponent<Renderer>() }),
                    new LOD(0.015f, new[] { tier1.GetComponent<Renderer>() })
                });
            }

            // Helipad near Pit Complex / Paddock
            GameObject pad = new GameObject("Circuit_Helipad");
            pad.transform.SetParent(parent, false);
            pad.transform.position = new Vector3(92f, 0.02f, -35f);
            CreateCylinder(pad.transform, "Concrete_Pad", Vector3.zero, new Vector3(16f, 0.12f, 16f), helipadMat);
            CreateBox(pad.transform, "H_Left", new Vector3(-2.0f, 0.15f, 0f), new Vector3(0.8f, 0.04f, 6.0f), yellowMat, false);
            CreateBox(pad.transform, "H_Right", new Vector3(2.0f, 0.15f, 0f), new Vector3(0.8f, 0.04f, 6.0f), yellowMat, false);
            CreateBox(pad.transform, "H_Cross", new Vector3(0f, 0.15f, 0f), new Vector3(4.0f, 0.04f, 0.8f), yellowMat, false);
        }

        private void BuildTwilightMarinaTheme(Transform parent, Shader urpLit)
        {
            Material steelMat = CreateMat("Marina_Steel_Mat", urpLit, new Color(0.22f, 0.24f, 0.28f), 0.4f, 0.7f);
            Material waterMat = CreateMat("Marina_Water_Mat", urpLit, new Color(0.04f, 0.16f, 0.28f), 0.92f, 0.2f);
            Material yachtWhiteMat = CreateMat("Marina_YachtWhite_Mat", urpLit, new Color(0.95f, 0.95f, 0.96f), 0.6f, 0.1f);
            Material palmTrunkMat = CreateMat("Marina_PalmTrunk_Mat", urpLit, new Color(0.42f, 0.30f, 0.18f), 0.1f, 0f);
            Material palmLeafMat = CreateMat("Marina_PalmLeaf_Mat", urpLit, new Color(0.14f, 0.45f, 0.16f), 0.2f, 0f);
            Material skyscraperMat = CreateMat("Marina_Skyscraper_Mat", urpLit, new Color(0.08f, 0.10f, 0.14f), 0.85f, 0.4f);

            Material floodGlowMat = CreateMat("Marina_FloodGlow_Mat", urpLit, Color.white, 0.2f, 0f);
            floodGlowMat.EnableKeyword("_EMISSION");
            floodGlowMat.SetColor("_EmissionColor", new Color(1.0f, 0.96f, 0.88f) * 4.5f);

            Material windowGlowMat = CreateMat("Marina_WindowGlow_Mat", urpLit, new Color(0.2f, 0.7f, 1.0f), 0.4f, 0f);
            windowGlowMat.EnableKeyword("_EMISSION");
            windowGlowMat.SetColor("_EmissionColor", new Color(0.35f, 0.82f, 1.0f) * 2.5f);

            // 1. Stadium Floodlight Towers around circuit
            if (trackGenerator != null && trackGenerator.Spline != null)
            {
                float step = trackGenerator.Spline.TotalLength / Mathf.Max(1, marinaFloodlightCount);
                for (int i = 0; i < marinaFloodlightCount; i++)
                {
                    var s = trackGenerator.Spline.GetSampleAtDistance(i * step);
                    GameObject tower = new GameObject($"Floodlight_Tower_{i + 1}");
                    tower.transform.SetParent(parent, false);
                    tower.transform.position = s.Position - s.Right * 15f;
                    tower.transform.rotation = Quaternion.LookRotation(s.Right, Vector3.up);

                    CreateBox(tower.transform, "Mast", new Vector3(0f, 11f, 0f), new Vector3(0.65f, 22f, 0.65f), steelMat, true);
                    CreateBox(tower.transform, "Lamp_Head", new Vector3(0f, 22f, 0.6f), new Vector3(3.8f, 1.4f, 0.4f), floodGlowMat, false);

                    // Cast crisp spot light onto track (every 2nd tower uses actual Light to preserve 60+ FPS)
                    if (i % 2 == 0)
                    {
                        GameObject lightObj = new GameObject("SpotLight");
                        lightObj.transform.SetParent(tower.transform, false);
                        lightObj.transform.localPosition = new Vector3(0f, 21.8f, 1.0f);
                        lightObj.transform.localRotation = Quaternion.Euler(48f, 0f, 0f);
                        Light spot = lightObj.AddComponent<Light>();
                        spot.type = LightType.Spot;
                        spot.range = 65f;
                        spot.spotAngle = 78f;
                        spot.intensity = 1800f;
                        spot.shadows = LightShadows.None;
                    }
                }
            }

            // 2. Palm Trees
            System.Random rng = new System.Random(randomSeed + 10);
            for (int i = 0; i < marinaPalmCount; i++)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float dist = 195f + (float)(rng.NextDouble() * 85f);
                Vector3 pos = new Vector3(160f + Mathf.Cos(angle) * dist, -0.35f, 20f + Mathf.Sin(angle) * dist);

                GameObject palm = new GameObject($"Palm_Tree_{i + 1}");
                palm.transform.SetParent(parent, false);
                palm.transform.position = pos;
                CreateCylinder(palm.transform, "Trunk", new Vector3(0f, 3.5f, 0f), new Vector3(0.38f, 3.5f, 0.38f), palmTrunkMat);
                for (int f = 0; f < 5; f++)
                {
                    GameObject frond = CreateBox(palm.transform, $"Frond_{f}", new Vector3(0f, 6.8f, 0f), new Vector3(3.6f, 0.12f, 0.7f), palmLeafMat, false);
                    frond.transform.localRotation = Quaternion.Euler(14f, f * 72f, 0f);
                }
            }

            // 3. Glowing Background City Skyscrapers
            for (int i = 0; i < marinaSkyscraperCount; i++)
            {
                float angle = i * (Mathf.PI * 2f / marinaSkyscraperCount);
                Vector3 pos = new Vector3(160f + Mathf.Cos(angle) * 340f, 0f, 20f + Mathf.Sin(angle) * 340f);
                float h = 55f + (float)rng.NextDouble() * 95f;

                GameObject bldg = new GameObject($"Skyscraper_{i + 1}");
                bldg.transform.SetParent(parent, false);
                bldg.transform.position = pos;
                CreateBox(bldg.transform, "Tower", new Vector3(0f, h * 0.5f, 0f), new Vector3(22f, h, 22f), skyscraperMat, false);
                CreateBox(bldg.transform, "Crown_Glow", new Vector3(0f, h - 1.5f, 0f), new Vector3(22.4f, 1.4f, 22.4f), windowGlowMat, false);
            }

            // 4. Waterfront Marina Plane & Low-Poly Luxury Yachts
            GameObject marinaWater = GameObject.CreatePrimitive(PrimitiveType.Plane);
            marinaWater.name = "Marina_Waterfront";
            marinaWater.transform.SetParent(parent, false);
            marinaWater.transform.position = new Vector3(180f, -0.45f, -310f);
            marinaWater.transform.localScale = new Vector3(45f, 1f, 22f);
            marinaWater.GetComponent<Renderer>().sharedMaterial = waterMat;

            for (int i = 0; i < marinaYachtCount; i++)
            {
                GameObject yacht = new GameObject($"Luxury_Yacht_{i + 1}");
                yacht.transform.SetParent(parent, false);
                yacht.transform.position = new Vector3(70f + i * 36f, 0.2f, -275f - (i % 2) * 18f);
                yacht.transform.rotation = Quaternion.Euler(0f, 20f + i * 8f, 0f);

                CreateBox(yacht.transform, "Hull", new Vector3(0f, 0.6f, 0f), new Vector3(4.2f, 1.4f, 14f), yachtWhiteMat, false);
                CreateBox(yacht.transform, "Cabin", new Vector3(0f, 1.9f, -1.2f), new Vector3(3.2f, 1.3f, 7.5f), skyscraperMat, false);
                CreateBox(yacht.transform, "Upper_Deck", new Vector3(0f, 2.8f, -1.5f), new Vector3(2.8f, 0.4f, 5.0f), yachtWhiteMat, false);
            }
        }

        private Transform CreateGroup(string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private static GameObject CreateBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat, bool keepCollider)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = pos;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = mat;
            if (!keepCollider) Object.DestroyImmediate(box.GetComponent<Collider>());
            return box;
        }

        private static GameObject CreateCylinder(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = name;
            cyl.transform.SetParent(parent, false);
            cyl.transform.localPosition = pos;
            cyl.transform.localScale = scale;
            cyl.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(cyl.GetComponent<Collider>());
            return cyl;
        }

        private static Material CreateMat(string name, Shader shader, Color color, float smoothness, float metallic)
        {
            Material mat = new Material(shader) { name = name, enableInstancing = true };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            return mat;
        }

    }
}
