using System.Collections.Generic;
using UnityEngine;

namespace F1Racing.Track
{
    /// <summary>
    /// Procedural multi-story Formula 1 Pit Complex, Pit Lane, Podium Overhang,
    /// and Backstage Paddock generator.
    /// Drop onto a GameObject or use F1 Racing -> Build Pit Complex.
    /// </summary>
    public class PitComplexBuilder : MonoBehaviour
    {
        [Header("Layout & Alignment")]
        public bool autoAlignToMainStraight = true;
        public bool placeOnRightSideOfStraight = true;
        public float lateralOffsetFromTrackCenter = 11.2f;
        public float longitudinalOffsetZ = -45f;

        [Header("Customizable Dimensions")]
        [Range(2, 16)] public int numberOfGarages = 10;
        public float garageBayWidth = 12f;
        public float garageDepth = 16f;
        public float garageHeight = 4.6f;
        public float vipFloorHeight = 4.2f;
        public float pitLaneWidth = 12f;
        public float paddockDepth = 38f;

        [Header("Team Accent Liveries")]
        public Color[] teamAccentColors = new Color[]
        {
            new Color(0.86f, 0.08f, 0.08f), // Red
            new Color(0.08f, 0.36f, 0.88f), // Blue
            new Color(1.00f, 0.50f, 0.05f), // Papaya
            new Color(0.75f, 0.78f, 0.82f), // Silver
            new Color(0.05f, 0.42f, 0.25f), // Racing Green
            new Color(0.04f, 0.72f, 0.68f), // Teal
            new Color(0.92f, 0.30f, 0.58f), // Pink
            new Color(0.10f, 0.16f, 0.38f), // Navy
            new Color(0.95f, 0.78f, 0.08f), // Yellow
            new Color(0.45f, 0.08f, 0.16f)  // Maroon
        };

        public float TotalBuildingLength => numberOfGarages * garageBayWidth;

        [ContextMenu("Build Pit Complex")]
        public void BuildPitComplex()
        {
            ClearChildren();

            if (autoAlignToMainStraight)
            {
                float side = placeOnRightSideOfStraight ? 1f : -1f;
                transform.position = new Vector3(side * lateralOffsetFromTrackCenter, 0f, longitudinalOffsetZ);
                transform.rotation = placeOnRightSideOfStraight
                    ? Quaternion.identity
                    : Quaternion.Euler(0f, 180f, 0f);
            }

            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            Material concreteMat = CreateMat("Pit_Concrete_Mat", urpLit, new Color(0.74f, 0.75f, 0.77f), 0.18f, 0f);
            Material darkSteelMat = CreateMat("Pit_DarkSteel_Mat", urpLit, new Color(0.16f, 0.18f, 0.21f), 0.45f, 0.75f);
            Material asphaltMat = CreateMat("Pit_Asphalt_Mat", urpLit, new Color(0.14f, 0.145f, 0.155f), 0.24f, 0f);
            Material whiteMat = CreateMat("Pit_White_Mat", urpLit, new Color(0.94f, 0.94f, 0.94f), 0.35f, 0f);
            Material tireMat = CreateMat("Pit_Tire_Mat", urpLit, new Color(0.07f, 0.07f, 0.07f), 0.15f, 0f);
            Material glassMat = CreateGlassMat("Pit_TintedGlass_Mat", urpLit, new Color(0.12f, 0.24f, 0.34f, 0.48f));

            Material ceilingGlowMat = CreateEmissiveMat("Pit_CeilingGlow_Mat", urpLit, Color.white, 2.8f);
            Material warmVipGlowMat = CreateEmissiveMat("Pit_WarmVIP_Mat", urpLit, new Color(1.0f, 0.76f, 0.42f), 2.2f);
            Material monitorGlowMat = CreateEmissiveMat("Pit_MonitorGlow_Mat", urpLit, new Color(0.15f, 0.72f, 1.0f), 2.5f);

            float len = TotalBuildingLength;

            // 1. Pit Wall, Pit Lane, Pit Boxes, Gantry Booms & Command Stand
            Transform pitLaneGroup = CreateGroup("01_PitLane_And_CommandWall");
            BuildPitLaneAndWall(pitLaneGroup, len, asphaltMat, concreteMat, darkSteelMat, whiteMat, monitorGlowMat, urpLit);

            // 2. Ground Floor Team Garages
            Transform garageGroup = CreateGroup("02_GroundFloor_Garages");
            BuildGroundFloorGarages(garageGroup, len, concreteMat, darkSteelMat, whiteMat, tireMat, ceilingGlowMat, urpLit);

            // 3. Second & Third Floor VIP Paddock Club
            Transform vipGroup = CreateGroup("03_VIP_PaddockClub_Floors2And3");
            BuildVipHospitalityFloors(vipGroup, len, concreteMat, darkSteelMat, whiteMat, glassMat, warmVipGlowMat, urpLit);

            // 4. Podium Overhang & Parc Ferme
            Transform podiumGroup = CreateGroup("04_Podium_Overhang_And_ParcFerme");
            BuildPodiumAndParcFerme(podiumGroup, len, concreteMat, darkSteelMat, whiteMat, asphaltMat, urpLit);

            // 5. Backstage Paddock Plaza, Motorhomes & Semi-Trucks
            Transform paddockGroup = CreateGroup("05_Backstage_Paddock");
            BuildBackstagePaddock(paddockGroup, len, asphaltMat, whiteMat, darkSteelMat, glassMat, tireMat, urpLit);
        }

        private void BuildPitLaneAndWall(
            Transform parent, float len,
            Material asphaltMat, Material concreteMat, Material steelMat, Material whiteMat, Material monitorGlowMat, Shader urpLit)
        {
            // Pit Road Asphalt (+X from 0 to pitLaneWidth)
            GameObject pitRoad = CreateBox(parent, "PitLane_Asphalt",
                new Vector3(pitLaneWidth * 0.5f, -0.02f, len * 0.5f),
                new Vector3(pitLaneWidth, 0.08f, len + 36f), asphaltMat, keepCollider: true);

            // Low Concrete Pit Wall separating main straight (x=0) from pit lane
            CreateBox(parent, "PitWall_Concrete",
                new Vector3(0.25f, 0.55f, len * 0.5f),
                new Vector3(0.5f, 1.1f, len + 18f), concreteMat, keepCollider: true);

            // Pit Wall Command Stand (covered engineering desks + glowing monitors)
            GameObject standRoot = CreateGroup("PitWall_CommandStand", parent).gameObject;
            float standLen = Mathf.Min(len * 0.85f, numberOfGarages * 9f);
            float standStartZ = (len - standLen) * 0.5f;
            CreateBox(standRoot.transform, "CommandStand_Base",
                new Vector3(1.1f, 0.45f, len * 0.5f),
                new Vector3(1.4f, 0.9f, standLen), steelMat, keepCollider: true);
            CreateBox(standRoot.transform, "CommandStand_Canopy",
                new Vector3(1.1f, 2.75f, len * 0.5f),
                new Vector3(1.9f, 0.14f, standLen + 1f), whiteMat, keepCollider: false);

            int monitorCount = numberOfGarages * 2;
            for (int i = 0; i < monitorCount; i++)
            {
                float z = standStartZ + (i + 0.5f) * (standLen / monitorCount);
                CreateBox(standRoot.transform, $"Command_Monitor_{i + 1}",
                    new Vector3(1.05f, 1.45f, z),
                    new Vector3(0.08f, 0.42f, 0.72f), monitorGlowMat, keepCollider: false);
            }

            // 10 Painted Team Pit Boxes + Overhead Pit Gantry Booms
            for (int i = 0; i < numberOfGarages; i++)
            {
                Color teamCol = GetTeamColor(i);
                Material teamMat = CreateMat($"PitBox_Team_{i + 1}_Mat", urpLit, teamCol, 0.4f, 0.1f);
                float bayCenterZ = (i + 0.5f) * garageBayWidth;

                // Painted Pit Box Border & Stop Pad
                CreateBox(parent, $"PitBox_{i + 1:00}",
                    new Vector3(pitLaneWidth * 0.45f, 0.03f, bayCenterZ),
                    new Vector3(3.6f, 0.02f, 6.2f), teamMat, keepCollider: false);

                // Overhead Pit Gantry Boom extending from garage roof over pit box
                float boomX = pitLaneWidth - 2.2f;
                CreateBox(parent, $"PitBoom_Arm_{i + 1:00}",
                    new Vector3(boomX, garageHeight - 0.3f, bayCenterZ),
                    new Vector3(5.2f, 0.22f, 0.24f), steelMat, keepCollider: false);
                CreateBox(parent, $"PitBoom_Light_{i + 1:00}",
                    new Vector3(boomX - 1.8f, garageHeight - 0.42f, bayCenterZ),
                    new Vector3(1.1f, 0.08f, 0.32f), teamMat, keepCollider: false);
            }
        }

        private void BuildGroundFloorGarages(
            Transform parent, float len,
            Material concreteMat, Material steelMat, Material whiteMat, Material tireMat, Material ceilingGlowMat, Shader urpLit)
        {
            float frontX = pitLaneWidth;
            float centerX = frontX + garageDepth * 0.5f;

            // Garage Floor Slab & Back Wall
            CreateBox(parent, "Garage_FloorSlab",
                new Vector3(centerX, 0.0f, len * 0.5f),
                new Vector3(garageDepth, 0.12f, len), concreteMat, keepCollider: true);

            CreateBox(parent, "Garage_BackWall",
                new Vector3(frontX + garageDepth - 0.2f, garageHeight * 0.5f, len * 0.5f),
                new Vector3(0.4f, garageHeight, len), concreteMat, keepCollider: true);

            // End Caps
            CreateBox(parent, "Garage_Wall_Start",
                new Vector3(centerX, garageHeight * 0.5f, 0.2f),
                new Vector3(garageDepth, garageHeight, 0.4f), concreteMat, keepCollider: true);
            CreateBox(parent, "Garage_Wall_End",
                new Vector3(centerX, garageHeight * 0.5f, len - 0.2f),
                new Vector3(garageDepth, garageHeight, 0.4f), concreteMat, keepCollider: true);

            // Modular Bays
            for (int i = 0; i < numberOfGarages; i++)
            {
                float bayZ0 = i * garageBayWidth;
                float bayCenterZ = bayZ0 + garageBayWidth * 0.5f;
                Color teamCol = GetTeamColor(i);
                Material teamMat = CreateMat($"Garage_Team_{i + 1}_Mat", urpLit, teamCol, 0.45f, 0.2f);

                GameObject bay = CreateGroup($"Garage_Bay_{i + 1:00}", parent).gameObject;

                // Divider Wall between bays
                if (i > 0)
                {
                    CreateBox(bay.transform, "Divider_Wall",
                        new Vector3(centerX, garageHeight * 0.5f, bayZ0),
                        new Vector3(garageDepth - 0.4f, garageHeight, 0.35f), concreteMat, keepCollider: true);
                }

                // Front Fascia Beam + Team Accent Banner + Rolled-up Shutter
                CreateBox(bay.transform, "Front_Header",
                    new Vector3(frontX + 0.2f, garageHeight - 0.55f, bayCenterZ),
                    new Vector3(0.4f, 1.1f, garageBayWidth - 0.4f), teamMat, keepCollider: false);

                GameObject shutter = CreateCylinder(bay.transform, "Rolled_Shutter",
                    new Vector3(frontX + 0.45f, garageHeight - 1.15f, bayCenterZ),
                    new Vector3(0.45f, (garageBayWidth - 1.2f) * 0.5f, 0.45f), steelMat);
                shutter.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                // Glowing Ceiling Light Panels
                CreateBox(bay.transform, "Ceiling_Light_A",
                    new Vector3(frontX + garageDepth * 0.35f, garageHeight - 0.15f, bayCenterZ),
                    new Vector3(2.2f, 0.08f, garageBayWidth * 0.65f), ceilingGlowMat, keepCollider: false);
                CreateBox(bay.transform, "Ceiling_Light_B",
                    new Vector3(frontX + garageDepth * 0.72f, garageHeight - 0.15f, bayCenterZ),
                    new Vector3(2.2f, 0.08f, garageBayWidth * 0.65f), ceilingGlowMat, keepCollider: false);

                // Tire Rack Prop inside back-left of each garage
                GameObject rack = CreateGroup("Tire_Rack", bay.transform).gameObject;
                rack.transform.localPosition = new Vector3(frontX + garageDepth - 2.2f, 0f, bayCenterZ - garageBayWidth * 0.3f);
                CreateBox(rack.transform, "Rack_Frame", new Vector3(0f, 0.9f, 0f), new Vector3(0.9f, 1.8f, 2.8f), steelMat, false);
                for (int t = 0; t < 4; t++)
                {
                    GameObject tire = CreateCylinder(rack.transform, $"SpareTire_{t}",
                        new Vector3(0f, 0.55f, -0.9f + t * 0.6f),
                        new Vector3(0.68f, 0.22f, 0.68f), tireMat);
                    tire.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
            }
        }

        private void BuildVipHospitalityFloors(
            Transform parent, float len,
            Material concreteMat, Material steelMat, Material whiteMat, Material glassMat, Material warmVipGlowMat, Shader urpLit)
        {
            float frontX = pitLaneWidth;
            float centerX = frontX + garageDepth * 0.5f;

            for (int floor = 1; floor <= 2; floor++)
            {
                float baseY = garageHeight + (floor - 1) * vipFloorHeight;
                Transform floorGroup = CreateGroup($"VIP_Floor_{floor + 1}", parent);

                // Floor Slab + Roof Slab (top floor)
                CreateBox(floorGroup, "Floor_Slab",
                    new Vector3(centerX, baseY, len * 0.5f),
                    new Vector3(garageDepth + 0.6f, 0.3f, len + 0.6f), concreteMat, keepCollider: true);

                if (floor == 2)
                {
                    CreateBox(floorGroup, "Roof_Slab",
                        new Vector3(centerX, baseY + vipFloorHeight, len * 0.5f),
                        new Vector3(garageDepth + 1.4f, 0.35f, len + 1.4f), whiteMat, keepCollider: true);
                }

                // Back & End Walls
                CreateBox(floorGroup, "Back_Wall",
                    new Vector3(frontX + garageDepth - 0.2f, baseY + vipFloorHeight * 0.5f, len * 0.5f),
                    new Vector3(0.35f, vipFloorHeight, len), whiteMat, keepCollider: false);

                // Floor-to-ceiling Tinted Glass Windows facing pit lane
                CreateBox(floorGroup, "Front_TintedGlass",
                    new Vector3(frontX + 0.15f, baseY + vipFloorHeight * 0.5f, len * 0.5f),
                    new Vector3(0.12f, vipFloorHeight - 0.4f, len - 0.8f), glassMat, keepCollider: false);

                // Warm Interior Restaurant Lighting Strips
                CreateBox(floorGroup, "Interior_WarmLighting",
                    new Vector3(frontX + 3.2f, baseY + vipFloorHeight - 0.35f, len * 0.5f),
                    new Vector3(2.4f, 0.1f, len - 4f), warmVipGlowMat, keepCollider: false);

                // Cantilevered Outdoor Viewing Balcony + Glass Railing
                float balconyWidth = 3.2f;
                float balconyX = frontX - balconyWidth * 0.5f;
                CreateBox(floorGroup, "Cantilever_Balcony",
                    new Vector3(balconyX, baseY + 0.05f, len * 0.5f),
                    new Vector3(balconyWidth, 0.24f, len), concreteMat, keepCollider: true);

                CreateBox(floorGroup, "Balcony_GlassRailing",
                    new Vector3(frontX - balconyWidth + 0.08f, baseY + 0.65f, len * 0.5f),
                    new Vector3(0.08f, 1.05f, len), glassMat, keepCollider: false);

                // Patio Tables & Umbrellas on Balcony
                int tableCount = numberOfGarages;
                for (int t = 0; t < tableCount; t++)
                {
                    float tz = (t + 0.5f) * (len / tableCount);
                    CreatePatioTableAndUmbrella(floorGroup, $"VIP_PatioTable_{t + 1}",
                        new Vector3(balconyX, baseY + 0.18f, tz), whiteMat, steelMat, GetTeamColor(t), urpLit);
                }
            }
        }

        private void CreatePatioTableAndUmbrella(Transform parent, string name, Vector3 pos, Material whiteMat, Material steelMat, Color accent, Shader urpLit)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;

            CreateCylinder(root.transform, "Table_Pole", new Vector3(0f, 0.4f, 0f), new Vector3(0.1f, 0.4f, 0.1f), steelMat);
            CreateCylinder(root.transform, "Table_Top", new Vector3(0f, 0.8f, 0f), new Vector3(1.1f, 0.05f, 1.1f), whiteMat);

            CreateCylinder(root.transform, "Umbrella_Pole", new Vector3(0f, 1.45f, 0f), new Vector3(0.06f, 0.7f, 0.06f), steelMat);
            Material umbMat = CreateMat($"Umbrella_{name}_Mat", urpLit, accent, 0.2f, 0f);
            CreateCylinder(root.transform, "Umbrella_Canopy", new Vector3(0f, 2.2f, 0f), new Vector3(2.1f, 0.12f, 2.1f), umbMat);
        }

        private void BuildPodiumAndParcFerme(
            Transform parent, float len,
            Material concreteMat, Material steelMat, Material whiteMat, Material asphaltMat, Shader urpLit)
        {
            float podiumZ = len * 0.18f;
            float overhangX = pitLaneWidth - 4.2f;
            float podiumY = garageHeight + 0.2f;

            // Elevated Podium Deck jutting out from 2nd floor over pit lane
            CreateBox(parent, "Podium_Deck",
                new Vector3(overhangX, podiumY, podiumZ),
                new Vector3(7.5f, 0.35f, 10.5f), concreteMat, keepCollider: true);

            // Sponsor Backdrop Board
            Material sponsorMat = CreateMat("Podium_Sponsor_Mat", urpLit, new Color(0.12f, 0.14f, 0.22f), 0.3f, 0.1f);
            CreateBox(parent, "Podium_SponsorBackdrop",
                new Vector3(overhangX + 2.8f, podiumY + 1.8f, podiumZ),
                new Vector3(0.25f, 3.2f, 8.8f), sponsorMat, keepCollider: false);

            // 1st / 2nd / 3rd Place Steps
            CreateBox(parent, "Step_P1", new Vector3(overhangX - 1.2f, podiumY + 0.55f, podiumZ), new Vector3(1.4f, 0.75f, 1.6f), whiteMat, false);
            CreateBox(parent, "Step_P2", new Vector3(overhangX - 1.2f, podiumY + 0.42f, podiumZ - 1.7f), new Vector3(1.4f, 0.50f, 1.5f), whiteMat, false);
            CreateBox(parent, "Step_P3", new Vector3(overhangX - 1.2f, podiumY + 0.32f, podiumZ + 1.7f), new Vector3(1.4f, 0.30f, 1.5f), whiteMat, false);

            // Stairs leading up from 2nd floor edge
            for (int s = 0; s < 4; s++)
            {
                CreateBox(parent, $"Podium_Stair_{s + 1}",
                    new Vector3(overhangX + 3.2f - s * 0.4f, podiumY + 0.15f + s * 0.12f, podiumZ + 4.0f),
                    new Vector3(0.45f, 0.18f, 1.6f), steelMat, false);
            }

            // Fenced Parc Ferme Parking Zone directly below the podium
            float pfWidth = 7.2f;
            float pfLen = 11.5f;
            CreateBox(parent, "ParcFerme_Asphalt",
                new Vector3(overhangX, 0.02f, podiumZ),
                new Vector3(pfWidth, 0.04f, pfLen), asphaltMat, keepCollider: true);

            CreateBox(parent, "ParcFerme_Fence_Front",
                new Vector3(overhangX - pfWidth * 0.5f, 0.55f, podiumZ),
                new Vector3(0.12f, 1.0f, pfLen), steelMat, keepCollider: true);
            CreateBox(parent, "ParcFerme_Fence_Left",
                new Vector3(overhangX, 0.55f, podiumZ - pfLen * 0.5f),
                new Vector3(pfWidth, 1.0f, 0.12f), steelMat, keepCollider: true);
            CreateBox(parent, "ParcFerme_Fence_Right",
                new Vector3(overhangX, 0.55f, podiumZ + pfLen * 0.5f),
                new Vector3(pfWidth, 1.0f, 0.12f), steelMat, keepCollider: true);
        }

        private void BuildBackstagePaddock(
            Transform parent, float len,
            Material asphaltMat, Material whiteMat, Material steelMat, Material glassMat, Material tireMat, Shader urpLit)
        {
            float paddockStartX = pitLaneWidth + garageDepth;
            float paddockCenterX = paddockStartX + paddockDepth * 0.5f;

            // Backstage Paddock Asphalt Plaza
            CreateBox(parent, "Paddock_Asphalt_Plaza",
                new Vector3(paddockCenterX, -0.02f, len * 0.5f),
                new Vector3(paddockDepth, 0.08f, len + 16f), asphaltMat, keepCollider: true);

            // Row 1: 2-Story Team Motorhomes
            int count = Mathf.Min(numberOfGarages, 10);
            for (int i = 0; i < count; i++)
            {
                float z = (i + 0.5f) * (len / count);
                Color teamCol = GetTeamColor(i);
                Material teamMat = CreateMat($"Paddock_Team_{i + 1}_Mat", urpLit, teamCol, 0.45f, 0.25f);

                // Sleek 2-story Motorhome block
                GameObject mh = CreateGroup($"Team_Motorhome_{i + 1:00}", parent).gameObject;
                mh.transform.localPosition = new Vector3(paddockStartX + 10.5f, 0f, z);
                CreateBox(mh.transform, "Lower_Unit", new Vector3(0f, 1.75f, 0f), new Vector3(9.5f, 3.5f, 7.8f), whiteMat, true);
                CreateBox(mh.transform, "Upper_VIP_Deck", new Vector3(0f, 4.9f, 0f), new Vector3(9.0f, 2.8f, 7.4f), glassMat, true);
                CreateBox(mh.transform, "Team_Trim", new Vector3(-4.6f, 3.5f, 0f), new Vector3(0.35f, 0.55f, 7.9f), teamMat, false);

                // Row 2: Logistics Semi-Truck Trailer
                GameObject truck = CreateGroup($"Team_SemiTruck_{i + 1:00}", parent).gameObject;
                truck.transform.localPosition = new Vector3(paddockStartX + 27.5f, 0f, z);
                CreateBox(truck.transform, "Trailer", new Vector3(1.5f, 2.0f, 0f), new Vector3(11.5f, 3.2f, 2.7f), teamMat, true);
                CreateBox(truck.transform, "Cab", new Vector3(-5.8f, 1.55f, 0f), new Vector3(2.8f, 2.7f, 2.5f), whiteMat, true);
                for (int w = 0; w < 3; w++)
                {
                    float wx = -5.5f + w * 4.5f;
                    CreateCylinder(truck.transform, $"Wheel_L_{w}", new Vector3(wx, 0.48f, -1.25f), new Vector3(0.95f, 0.2f, 0.95f), tireMat)
                        .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    CreateCylinder(truck.transform, $"Wheel_R_{w}", new Vector3(wx, 0.48f, 1.25f), new Vector3(0.95f, 0.2f, 0.95f), tireMat)
                        .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
            }
        }

        private Color GetTeamColor(int index)
        {
            if (teamAccentColors == null || teamAccentColors.Length == 0) return Color.red;
            return teamAccentColors[index % teamAccentColors.Length];
        }

        private Transform CreateGroup(string name, Transform customParent = null)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(customParent == null ? transform : customParent, false);
            return go.transform;
        }

        private static GameObject CreateBox(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat, bool keepCollider)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPos;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = mat;
            if (!keepCollider) F1TrackGenerator.DestroyImmediateSafe(box.GetComponent<Collider>());
            return box;
        }

        private static GameObject CreateCylinder(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = name;
            cyl.transform.SetParent(parent, false);
            cyl.transform.localPosition = localPos;
            cyl.transform.localScale = scale;
            cyl.GetComponent<Renderer>().sharedMaterial = mat;
            F1TrackGenerator.DestroyImmediateSafe(cyl.GetComponent<Collider>());
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

        private static Material CreateEmissiveMat(string name, Shader shader, Color color, float intensity)
        {
            Material mat = CreateMat(name, shader, color * 0.4f, 0.3f, 0f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * intensity);
            return mat;
        }

        private static Material CreateGlassMat(string name, Shader shader, Color color)
        {
            Material mat = CreateMat(name, shader, color, 0.88f, 0.15f);
            mat.SetFloat("_Surface", 1f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            return mat;
        }

        private void ClearChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                F1TrackGenerator.DestroyImmediateSafe(transform.GetChild(i).gameObject);
        }

    }
}
