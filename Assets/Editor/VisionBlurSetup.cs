using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using OverTheRainbow.Vision;

namespace OverTheRainbow.EditorTools
{
    /// <summary>
    /// 視界ブラーを組み込む。ぼかしは VisionBlurRenderFeature が描くので、
    /// Renderer への登録と、濃さを動かす VisionBlurController の配置を行う。
    /// 何度実行しても、既にあるものは作り直さずに再利用する。
    /// </summary>
    public static class VisionBlurSetup
    {
        private const string ShaderPath = "Assets/Shaders/VisionBlur.shader";
        private const string RendererDataPath = "Assets/Settings/URP-Default_Renderer.asset";
        private const string ObjectName = "VisionBlur";
        private const string LegacyObjectName = "VisionBlurVolume"; // Depth of Field を使っていた頃の名前
        private const string LegacyProfilePath = "Assets/Settings/VisionBlurProfile.asset";
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("0v3r 7h3 R41nb0w/視界ブラーをセットアップ", false, 21)]
        public static void SetupMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[VisionBlurSetup] 視界ブラーをセットアップしました。ぼけの強さは "
                    + RendererDataPath + " の Vision Blur で調整できます。");
        }

        /// <summary>今開いているシーンにコントローラを置き、Renderer にブラーを登録する。</summary>
        public static void ApplyToOpenScene()
        {
            UrpSetup.EnsurePostProcessData();
            EnsureRendererFeature();

            foreach (var cam in Object.FindObjectsOfType<Camera>())
            {
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                EditorUtility.SetDirty(cam.gameObject);
            }

            var go = GameObject.Find(ObjectName);
            if (go == null) go = GameObject.Find(LegacyObjectName);
            if (go == null) go = new GameObject(ObjectName);
            go.name = ObjectName;

            // Depth of Field 時代の Volume が残っていると、弱いぼけが二重に掛かる
            var legacyVolume = go.GetComponent<Volume>();
            if (legacyVolume != null)
            {
                Object.DestroyImmediate(legacyVolume);
                Debug.Log("[VisionBlurSetup] 旧 Depth of Field 用の Volume を外しました。"
                        + LegacyProfilePath + " は未使用になったので削除して構いません。");
            }

            var controller = go.GetComponent<VisionBlurController>();
            if (controller == null) controller = go.AddComponent<VisionBlurController>();

            var so = new SerializedObject(controller);
            so.FindProperty("eyeCover").objectReferenceValue = Object.FindObjectOfType<EyeCoverInput>();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(go);
        }

        /// <summary>Universal Renderer に VisionBlurRenderFeature を（なければ）追加する。</summary>
        private static void EnsureRendererFeature()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);
            if (rendererData == null)
            {
                Debug.LogError("[VisionBlurSetup] " + RendererDataPath + " が見つかりません。先に URP をセットアップしてください。");
                return;
            }

            var feature = rendererData.rendererFeatures.Find(f => f is VisionBlurRenderFeature) as VisionBlurRenderFeature;
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<VisionBlurRenderFeature>();
                feature.name = "VisionBlur";
                // Renderer のサブアセットにしないと参照が保存されない
                AssetDatabase.AddObjectToAsset(feature, rendererData);

                var rendererSo = new SerializedObject(rendererData);
                var features = rendererSo.FindProperty("m_RendererFeatures");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;

                // インスペクタ側はこの ID で Feature を引く。合っていないと表示が崩れる
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                var map = rendererSo.FindProperty("m_RendererFeatureMap");
                map.arraySize = features.arraySize;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;

                rendererSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // シェーダはマテリアル経由で参照されないので、ここで持たせておかないとビルドで剥がれる
            var featureSo = new SerializedObject(feature);
            featureSo.FindProperty("shader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            featureSo.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
        }
    }
}
