using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using OverTheRainbow.Stage;

namespace OverTheRainbow.EditorTools
{
    /// <summary>
    /// ビルトイン → URP 移行の残り作業をまとめて行う。
    /// 何度実行しても、既にあるアセットは作り直さずに再利用する。
    /// </summary>
    public static class UrpSetup
    {
        private const string SettingsFolder = "Assets/Settings";
        private const string PipelineAssetPath = SettingsFolder + "/URP-Default.asset";
        private const string RendererDataPath = SettingsFolder + "/URP-Default_Renderer.asset";
        public const string StageBaseMaterialPath = SettingsFolder + "/Mat_StageBase.mat";
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("0v3r 7h3 R41nb0w/URP をセットアップ", false, 20)]
        public static void Setup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }

            UniversalRenderPipelineAsset pipeline = CreatePipelineAsset();
            EnsurePostProcessData();
            AssignPipeline(pipeline);
            Material baseMat = CreateStageBaseMaterial();
            AssignBaseMaterialInMainScene(baseMat);

            AssetDatabase.SaveAssets();
            Debug.Log("[UrpSetup] URP のセットアップが完了しました。");
        }

        /// <summary>手順2：URP Asset (with Universal Renderer) を作る。</summary>
        private static UniversalRenderPipelineAsset CreatePipelineAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (existing != null) return existing;

            // Renderer を先にアセット化しておかないと、Pipeline Asset からの参照が保存されない
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererDataPath);
            }

            var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            // 影をはっきり出す：描画距離を詰めて解像度を稼ぎ、バイアスを小さくして影が物体から浮かないようにする
            pipeline.shadowDistance = 35f;
            pipeline.shadowCascadeCount = 2;
            pipeline.shadowDepthBias = 0.5f;
            pipeline.shadowNormalBias = 0.3f;
            AssetDatabase.CreateAsset(pipeline, PipelineAssetPath);
            return pipeline;
        }

        /// <summary>
        /// スクリプトから作った Renderer は PostProcessData が空のことがあり、そのままだとポストプロセスが描画されない。
        /// </summary>
        public static void EnsurePostProcessData()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);
            if (rendererData == null || rendererData.postProcessData != null) return;

            rendererData.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                UniversalRenderPipelineAsset.packagePath + "/Runtime/Data/PostProcessData.asset");
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
        }

        /// <summary>手順3：Graphics と全 Quality レベルに設定する。</summary>
        private static void AssignPipeline(RenderPipelineAsset pipeline)
        {
            GraphicsSettings.defaultRenderPipeline = pipeline;

            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        /// <summary>
        /// 手順6（案B）：URP/Lit のマテリアルをアセットとして持つ。
        /// シーンから参照されるので、ビルド時にシェーダが削除されない。
        /// </summary>
        private static Material CreateStageBaseMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(StageBaseMaterialPath);
            if (existing != null) return existing;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, StageBaseMaterialPath);
            return mat;
        }

        private static void AssignBaseMaterialInMainScene(Material baseMat)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath) == null) return;

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            foreach (var builder in Object.FindObjectsOfType<StageBuilder>())
            {
                AssignBaseMaterial(builder, baseMat);
            }
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>StageBuilder の private なシリアライズフィールドに設定する。</summary>
        public static void AssignBaseMaterial(StageBuilder builder, Material baseMat)
        {
            var so = new SerializedObject(builder);
            so.FindProperty("baseMaterial").objectReferenceValue = baseMat;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
