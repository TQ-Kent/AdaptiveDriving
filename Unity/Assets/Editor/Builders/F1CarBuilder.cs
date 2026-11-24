using F1Racing.Track;
using UnityEngine;

namespace F1Racing.Vehicle
{
    /// <summary>
    /// Builds a complete, playable 3D Formula 1 car GameObject hierarchy with
    /// chassis, wings, DRS flap, cockpit steering wheel, driver helmet, 4 WheelColliders,
    /// visual wheels, skid trails, tire smoke particles, and engine AudioSource.
    /// </summary>
    public static class F1ProceduralCarBuilder
    {
        public static F1CarController BuildF1Car(
            string carName,
            Color primaryColor,
            Color accentColor,
            ControlMode controlMode,
            F1TrackGenerator trackRef,
            Vector3 position,
            Quaternion rotation)
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            Material bodyMat = new Material(urpLit) { name = carName + "_Body_Mat" };
            bodyMat.SetColor("_BaseColor", primaryColor);
            bodyMat.SetFloat("_Metallic", 0.35f);
            bodyMat.SetFloat("_Smoothness", 0.78f);

            Material carbonMat = new Material(urpLit) { name = carName + "_Carbon_Mat" };
            carbonMat.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.09f));
            carbonMat.SetFloat("_Smoothness", 0.35f);

            Material accentMat = new Material(urpLit) { name = carName + "_Accent_Mat" };
            accentMat.SetColor("_BaseColor", accentColor);
            accentMat.SetFloat("_Smoothness", 0.65f);

            Material tireMat = new Material(urpLit) { name = carName + "_Tire_Mat" };
            tireMat.SetColor("_BaseColor", new Color(0.06f, 0.06f, 0.06f));
            tireMat.SetFloat("_Smoothness", 0.15f);

            Material brakeLightMat = new Material(urpLit) { name = carName + "_BrakeLight_Mat" };
            brakeLightMat.SetColor("_BaseColor", new Color(0.3f, 0.02f, 0.02f));
            brakeLightMat.EnableKeyword("_EMISSION");

            GameObject root = new GameObject(carName);
            root.transform.SetPositionAndRotation(position, rotation);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 798f;

            F1CarController controller = root.AddComponent<F1CarController>();
            controller.carName = carName;
            controller.controlMode = controlMode;
            controller.trackReference = trackRef;

            // 1. Body & Collision Hierarchy
            GameObject bodyRoot = new GameObject("Chassis_Body");
            bodyRoot.transform.SetParent(root.transform, false);

            // Main monocoque tub
            CreatePart(bodyRoot.transform, "Monocoque", PrimitiveType.Cube, new Vector3(0f, 0.28f, -0.05f), new Vector3(0.78f, 0.38f, 3.2f), bodyMat, true);
            // Tapered nosecone
            CreatePart(bodyRoot.transform, "Nosecone", PrimitiveType.Cube, new Vector3(0f, 0.22f, 2.0f), new Vector3(0.42f, 0.22f, 1.35f), bodyMat, true);
            // Sidepods
            CreatePart(bodyRoot.transform, "Sidepod_Left", PrimitiveType.Cube, new Vector3(-0.62f, 0.26f, -0.25f), new Vector3(0.46f, 0.32f, 1.85f), bodyMat, true);
            CreatePart(bodyRoot.transform, "Sidepod_Right", PrimitiveType.Cube, new Vector3(0.62f, 0.26f, -0.25f), new Vector3(0.46f, 0.32f, 1.85f), bodyMat, true);
            // Floor & Diffuser
            CreatePart(bodyRoot.transform, "Floor", PrimitiveType.Cube, new Vector3(0f, 0.08f, -0.1f), new Vector3(1.55f, 0.05f, 3.6f), carbonMat, false);
            // Airbox / Engine Cover
            CreatePart(bodyRoot.transform, "EngineCover", PrimitiveType.Cube, new Vector3(0f, 0.62f, -0.75f), new Vector3(0.38f, 0.42f, 1.45f), accentMat, false);

            // Front Wing
            CreatePart(bodyRoot.transform, "FrontWing_Main", PrimitiveType.Cube, new Vector3(0f, 0.11f, 2.55f), new Vector3(1.95f, 0.06f, 0.48f), carbonMat, true);
            CreatePart(bodyRoot.transform, "FrontWing_Endplate_L", PrimitiveType.Cube, new Vector3(-0.98f, 0.2f, 2.55f), new Vector3(0.05f, 0.24f, 0.52f), accentMat, false);
            CreatePart(bodyRoot.transform, "FrontWing_Endplate_R", PrimitiveType.Cube, new Vector3(0.98f, 0.2f, 2.55f), new Vector3(0.05f, 0.24f, 0.52f), accentMat, false);

            // Rear Wing + DRS Flap
            CreatePart(bodyRoot.transform, "RearWing_Pylon", PrimitiveType.Cube, new Vector3(0f, 0.55f, -2.05f), new Vector3(0.12f, 0.55f, 0.22f), carbonMat, false);
            CreatePart(bodyRoot.transform, "RearWing_Endplate_L", PrimitiveType.Cube, new Vector3(-0.62f, 0.72f, -2.12f), new Vector3(0.05f, 0.45f, 0.55f), accentMat, false);
            CreatePart(bodyRoot.transform, "RearWing_Endplate_R", PrimitiveType.Cube, new Vector3(0.62f, 0.72f, -2.12f), new Vector3(0.05f, 0.45f, 0.55f), accentMat, false);
            CreatePart(bodyRoot.transform, "RearWing_MainPlane", PrimitiveType.Cube, new Vector3(0f, 0.76f, -2.05f), new Vector3(1.22f, 0.05f, 0.34f), carbonMat, false);

            GameObject drsPivot = new GameObject("DRS_Flap_Pivot");
            drsPivot.transform.SetParent(bodyRoot.transform, false);
            drsPivot.transform.localPosition = new Vector3(0f, 0.88f, -2.22f);
            CreatePart(drsPivot.transform, "DRS_UpperFlap", PrimitiveType.Cube, new Vector3(0f, 0.02f, 0.08f), new Vector3(1.18f, 0.04f, 0.22f), accentMat, false);
            controller.drsRearWingFlap = drsPivot.transform;

            // Rear Brake / Rain Light
            GameObject brakeLight = CreatePart(bodyRoot.transform, "Rear_BrakeLight", PrimitiveType.Cube, new Vector3(0f, 0.32f, -2.15f), new Vector3(0.14f, 0.12f, 0.06f), brakeLightMat, false);
            controller.brakeLightRenderer = brakeLight.GetComponent<Renderer>();

            // Cockpit, Halo, Steering Wheel & Driver Helmet
            CreatePart(bodyRoot.transform, "Halo_Left", PrimitiveType.Cube, new Vector3(-0.24f, 0.58f, 0.32f), new Vector3(0.05f, 0.05f, 0.75f), carbonMat, false);
            CreatePart(bodyRoot.transform, "Halo_Right", PrimitiveType.Cube, new Vector3(0.24f, 0.58f, 0.32f), new Vector3(0.05f, 0.05f, 0.75f), carbonMat, false);
            CreatePart(bodyRoot.transform, "Halo_Center", PrimitiveType.Cube, new Vector3(0f, 0.52f, 0.68f), new Vector3(0.06f, 0.18f, 0.06f), carbonMat, false);

            GameObject helmet = CreatePart(bodyRoot.transform, "Driver_Helmet", PrimitiveType.Sphere, new Vector3(0f, 0.56f, 0.05f), new Vector3(0.28f, 0.28f, 0.30f), accentMat, false);
            controller.driverHelmetMesh = helmet.transform;

            GameObject steerPivot = new GameObject("SteeringWheel_Pivot");
            steerPivot.transform.SetParent(bodyRoot.transform, false);
            steerPivot.transform.localPosition = new Vector3(0f, 0.45f, 0.48f);
            steerPivot.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
            CreatePart(steerPivot.transform, "SteeringWheel_Plate", PrimitiveType.Cube, Vector3.zero, new Vector3(0.28f, 0.16f, 0.04f), carbonMat, false);
            controller.steeringWheelMesh = steerPivot.transform;

            // 2. WheelColliders + Visual Meshes
            GameObject wcRoot = new GameObject("WheelColliders");
            wcRoot.transform.SetParent(root.transform, false);
            GameObject wmRoot = new GameObject("WheelMeshes");
            wmRoot.transform.SetParent(root.transform, false);

            var skidList = new System.Collections.Generic.List<TrailRenderer>();
            var smokeList = new System.Collections.Generic.List<ParticleSystem>();

            CreateWheel("FL", new Vector3(-0.88f, 0.34f, 1.55f), true, wcRoot.transform, wmRoot.transform, tireMat, accentMat,
                out controller.frontLeftCollider, out controller.frontLeftMesh, null, null);
            CreateWheel("FR", new Vector3(0.88f, 0.34f, 1.55f), true, wcRoot.transform, wmRoot.transform, tireMat, accentMat,
                out controller.frontRightCollider, out controller.frontRightMesh, null, null);
            CreateWheel("RL", new Vector3(-0.92f, 0.34f, -1.45f), false, wcRoot.transform, wmRoot.transform, tireMat, accentMat,
                out controller.rearLeftCollider, out controller.rearLeftMesh, skidList, smokeList);
            CreateWheel("RR", new Vector3(0.92f, 0.34f, -1.45f), false, wcRoot.transform, wmRoot.transform, tireMat, accentMat,
                out controller.rearRightCollider, out controller.rearRightMesh, skidList, smokeList);

            controller.skidTrails = skidList.ToArray();
            controller.tireSmokeParticles = smokeList.ToArray();

            // 3. Engine AudioSource
            AudioSource audio = root.AddComponent<AudioSource>();
            audio.spatialBlend = 0.85f;
            audio.minDistance = 6f;
            audio.maxDistance = 85f;
            controller.engineAudioSource = audio;

            return controller;
        }

        private static GameObject CreatePart(Transform parent, string name, PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;

            if (!keepCollider)
            {
                Collider col = go.GetComponent<Collider>();
                if (col != null)
                {
                    if (Application.isPlaying) Object.Destroy(col);
                    else Object.DestroyImmediate(col);
                }
            }
            return go;
        }

        private static void CreateWheel(
            string label,
            Vector3 localPos,
            bool isFront,
            Transform wcParent,
            Transform wmParent,
            Material tireMat,
            Material rimMat,
            out WheelCollider wheelCollider,
            out Transform wheelMeshRoot,
            System.Collections.Generic.List<TrailRenderer> skidList,
            System.Collections.Generic.List<ParticleSystem> smokeList)
        {
            GameObject wcObj = new GameObject($"WC_{label}");
            wcObj.transform.SetParent(wcParent, false);
            wcObj.transform.localPosition = localPos;
            wheelCollider = wcObj.AddComponent<WheelCollider>();
            F1CarController.ConfigureWheelColliderPhysics(wheelCollider, isFront);

            GameObject meshRoot = new GameObject($"Wheel_{label}");
            meshRoot.transform.SetParent(wmParent, false);
            meshRoot.transform.localPosition = localPos;
            wheelMeshRoot = meshRoot.transform;

            float width = isFront ? 0.32f : 0.40f;
            GameObject tire = CreatePart(meshRoot.transform, "Tire", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.72f, width * 0.5f, 0.72f), tireMat, false);
            tire.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            GameObject rim = CreatePart(meshRoot.transform, "Rim", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.42f, width * 0.52f, 0.42f), rimMat, false);
            rim.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            if (!isFront && skidList != null && smokeList != null)
            {
                // Skid TrailRenderer anchored just above asphalt under rear wheels
                GameObject skidObj = new GameObject($"SkidTrail_{label}");
                skidObj.transform.SetParent(wcObj.transform, false);
                skidObj.transform.localPosition = new Vector3(0f, -0.33f, 0f);
                TrailRenderer trail = skidObj.AddComponent<TrailRenderer>();
                trail.time = 6f;
                trail.startWidth = 0.34f;
                trail.endWidth = 0.34f;
                trail.emitting = false;
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                Material skidMat = new Material(unlit) { name = "Skid_Mat" };
                skidMat.color = new Color(0.05f, 0.05f, 0.05f, 0.85f);
                trail.sharedMaterial = skidMat;
                skidList.Add(trail);

                // Tire Smoke ParticleSystem
                GameObject smokeObj = new GameObject($"Smoke_{label}");
                smokeObj.transform.SetParent(wcObj.transform, false);
                smokeObj.transform.localPosition = new Vector3(0f, -0.30f, -0.2f);
                ParticleSystem ps = smokeObj.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.playOnAwake = false;
                main.startLifetime = 0.75f;
                main.startSpeed = 2.5f;
                main.startSize = 0.55f;
                main.startColor = new Color(0.85f, 0.85f, 0.85f, 0.45f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = ps.emission;
                emission.rateOverTime = 28f;
                smokeList.Add(ps);
            }
        }
    }
}
