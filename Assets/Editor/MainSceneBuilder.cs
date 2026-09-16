using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OverTheRainbow.Core;
using OverTheRainbow.Player;
using OverTheRainbow.Stage;
using OverTheRainbow.UI;
using OverTheRainbow.Vision;

namespace OverTheRainbow.EditorTools
{
    /// <summary>
    /// Phase 1 の Main シーンを丸ごと組み立てる。
    /// Inspector での手作業を挟まずに「再生 → 遊べる」状態を作るためのもの。
    /// </summary>
    public static class MainSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("0v3r 7h3 R41nb0w/Phase 1 Main シーンを生成", false, 0)]
        public static void BuildMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLighting();
            CreateStage();
            CreateSystems();
            CreatePlayer();
            VisionBlurSetup.ApplyToOpenScene(); // カメラと EyeCoverInput ができてから
            CorruptionVignetteSetup.ApplyToOpenScene();

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            RegisterInBuildSettings();

            // Debug.Log("[MainSceneBuilder] " + ScenePath + " を生成しました。再生ボタンで遊べます。");
            // EditorUtility.DisplayDialog(
            //     "0v3r 7h3 R41nb0w",
            //     "Main シーンを生成しました。\n\n再生して WASD で移動、マウスで視点、SPACE で遮眼子（仮）です。",
            //     "OK");
        }

        private static void CreateLighting()
        {
            var lightGo = new GameObject("Directional Light");
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.intensity = 1.3f;
            light.shadows = LightShadows.Soft;

            // 環境光は影の部分も照らすので、明るすぎると影が薄くなる。影がはっきり出る程度に抑える
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.48f, 0.48f, 0.52f);
            RenderSettings.ambientEquatorColor = new Color(0.40f, 0.39f, 0.37f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.20f);
        }

        private static void CreateStage()
        {
            var go = new GameObject("Stage");
            var builder = go.AddComponent<StageBuilder>();
            go.AddComponent<DarknessChaser>();

            var baseMat = AssetDatabase.LoadAssetAtPath<Material>(UrpSetup.StageBaseMaterialPath);
            if (baseMat != null) UrpSetup.AssignBaseMaterial(builder, baseMat);
        }

        private static void CreateSystems()
        {
            var go = new GameObject("GameSystems");
            go.AddComponent<GameManager>();

            // 遮眼子入力：Phase 1 はキーボード。Phase 3 で MediaPipe の Source に差し替える。
            go.AddComponent<KeyboardEyeCoverSource>();
            go.AddComponent<EyeCoverInput>();

            go.AddComponent<HudController>();
        }

        private static void CreatePlayer()
        {
            var player = new GameObject("Player");
            player.tag = "Player";
            // StageBuilder の PlayerSpawn と同じ位置（先頭セルの中央レーン、床上）
            player.transform.position = new Vector3(0f, 1f, 0f);

            var controller = player.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.35f;
            controller.center = Vector3.zero;
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.3f;

            player.AddComponent<PlayerController>();
            player.AddComponent<DistanceTracker>();

            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.65f, 0f); // 目線 1.65m 相当

            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.80f, 0.79f, 0.78f); // フォグと揃える

            camGo.AddComponent<AudioListener>();
        }

        private static void RegisterInBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == ScenePath)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
