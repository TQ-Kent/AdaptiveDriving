using System.IO;
using F1Racing.CameraSystem;
using F1Racing.Track;
using F1Racing.UI;
using F1Racing.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace F1Racing.Editor
{
    /// <summary>
    /// Editor utility that assembles the complete playable F1 racing scene:
    /// track, 2 F1 cars, chase/split-screen cameras, lighting, and HUD.
    /// </summary>
    public static class F1SceneBuilder
    {
        private const string SceneFolder = "Assets/Scenes";
        private const string ScenePath = "Assets/Scenes/F1CircuitScene.unity";

        [MenuItem("F1 Racing/Build Complete F1 Scene")]
        public static void BuildCompleteF1Scene()
        {
            EnsureURPPipelineAsset();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Configure Directional Light for crisp outdoor race day lighting
            Light sun = Object.FindFirstObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
                sun.intensity = 1.6f;
                sun.shadows = LightShadows.Soft;
                if (sun.GetComponent<UniversalAdditionalLightData>() == null)
                    sun.gameObject.AddComponent<UniversalAdditionalLightData>();
            }

            // 1. Track Generator
            GameObject trackObj = new GameObject("F1_Circuit");
            F1TrackGenerator trackGen = trackObj.AddComponent<F1TrackGenerator>();
            trackGen.BuildTrack();

            // 1b. Trackside Environment, Safety Barriers, Marshal Posts & Nature
            GameObject envObj = new GameObject("Track_Environment_Props");
            TrackEnvironmentRoot envRoot = envObj.AddComponent<TrackEnvironmentRoot>();
            envRoot.trackGenerator = trackGen;
            TrackEnvironmentBuilder.BuildAllProps(envRoot);

            // 1c. Multi-Story Pit Complex, Pit Lane, Podium & Backstage Paddock
            PitComplexBuilderEditor.BuildPitComplexFromMenu();

            // 2. Spawn 2 F1 Cars on staggered starting grid
            trackGen.GetStartingGridSlot(0, out Vector3 p1Pos, out Quaternion p1Rot);
            F1CarController car1 = F1ProceduralCarBuilder.BuildF1Car(
                "F1_Car_1_Red",
                new Color(0.84f, 0.08f, 0.08f),
                Color.white,
                ControlMode.Player1_WASD,
                false,
                trackGen,
                p1Pos,
                p1Rot);

            trackGen.GetStartingGridSlot(1, out Vector3 p2Pos, out Quaternion p2Rot);
            F1CarController car2 = F1ProceduralCarBuilder.BuildF1Car(
                "F1_Car_2_Blue",
                new Color(0.08f, 0.36f, 0.88f),
                new Color(1f, 0.55f, 0.05f),
                ControlMode.Player2_Arrows,
                true,
                trackGen,
                p2Pos,
                p2Rot);

            // 3. Primary Chase Camera + Secondary Split-Screen Camera
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                GameObject camObj = new GameObject("Main Camera");
                camObj.tag = "MainCamera";
                mainCam = camObj.AddComponent<Camera>();
                camObj.AddComponent<AudioListener>();
            }

            if (mainCam.GetComponent<UniversalAdditionalCameraData>() == null)
                mainCam.gameObject.AddComponent<UniversalAdditionalCameraData>();

            GameObject splitCamObj = new GameObject("Secondary_SplitCamera");
            Camera splitCam = splitCamObj.AddComponent<Camera>();
            splitCamObj.AddComponent<UniversalAdditionalCameraData>();
            splitCam.depth = 1f;
            F1ChaseCamera splitChase = splitCamObj.AddComponent<F1ChaseCamera>();
            splitChase.isSecondaryCamera = true;
            splitChase.car1Target = car1;
            splitChase.car2Target = car2;
            splitCamObj.SetActive(false);

            F1ChaseCamera mainChase = mainCam.gameObject.AddComponent<F1ChaseCamera>();
            mainChase.isSecondaryCamera = false;
            mainChase.car1Target = car1;
            mainChase.car2Target = car2;
            mainChase.secondarySplitCamera = splitCam;

            // 4. HUD
            GameObject hudObj = new GameObject("F1_RaceHUD");
            F1RaceHUD hud = hudObj.AddComponent<F1RaceHUD>();
            hud.car1 = car1;
            hud.car2 = car2;
            hud.mainChaseCamera = mainChase;

            // 5. Save Scene
            if (!Directory.Exists(SceneFolder))
                Directory.CreateDirectory(SceneFolder);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.Refresh();

            Debug.Log($"[F1SceneBuilder] Built and saved playable F1 scene at: {ScenePath}");
        }

        public static void BuildSceneBatchmode()
        {
            BuildCompleteF1Scene();
        }

        [MenuItem("F1 Racing/Fix Pink Materials (Setup URP Asset)")]
        public static void EnsureURPPipelineAsset()
        {
            const string settingsFolder = "Assets/Settings";
            const string rendererPath = "Assets/Settings/F1_URP_Renderer.asset";
            const string pipelinePath = "Assets/Settings/F1_URP_Asset.asset";

            if (!Directory.Exists(settingsFolder))
                Directory.CreateDirectory(settingsFolder);

            UniversalRenderPipelineAsset urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (urpAsset == null)
            {
                UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
                if (rendererData == null)
                {
                    rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(rendererData, rendererPath);
                }

                urpAsset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urpAsset, pipelinePath);
                AssetDatabase.SaveAssets();
            }

            GraphicsSettings.defaultRenderPipeline = urpAsset;

            int originalLevel = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urpAsset;
            }
            QualitySettings.SetQualityLevel(originalLevel, false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[F1SceneBuilder] Configured URP Pipeline Asset in GraphicsSettings & QualitySettings.");
        }
    }
}
