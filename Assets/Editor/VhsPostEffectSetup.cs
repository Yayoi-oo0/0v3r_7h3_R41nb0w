using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering.Universal; // RenderObjects はまだ Experimental 名前空間にいる
using OverTheRainbow.UI;
using OverTheRainbow.Vision;

namespace OverTheRainbow.EditorTools
{
    /// <summary>
    /// VHS 風ポストエフェクトを Universal Renderer に登録し、日時表示をシーンに置く。
    /// 何度実行しても、既にあるものは作り直さずに再利用する。
    /// </summary>
    public static class VhsPostEffectSetup
    {
        private const string ShaderPath = "Assets/Shaders/VhsPostEffect.shader";
        private const string RendererDataPath = "Assets/Settings/URP-Default_Renderer.asset";
        private const string FontPath = "Assets/Fonts/VCR_OSD_MONO_1.001.ttf";
        private const string DateObjectName = "VhsDate";
        private const string OverlayPassName = "VhsOverlayUI";
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("0v3r 7h3 R41nb0w/VHS エフェクトをセットアップ", false, 23)]
        public static void SetupMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[VhsPostEffectSetup] VHS エフェクトをセットアップしました。強さは "
                    + RendererDataPath + " の Vhs Post Effect で調整できます。");
        }

        /// <summary>今開いているシーンに日時表示を置き、Renderer に劣化エフェクトを登録する。</summary>
        public static void ApplyToOpenScene()
        {
            int overlayLayer = EnsureOverlayLayer();
            EnsureRendererFeatures(overlayLayer);
            EnsureDateDisplay();
            ApplyFontToHud();
        }

        /// <summary>
        /// Renderer に劣化エフェクトと、日時表示だけを描くパスを（なければ）追加する。
        /// 日時表示は通常の半透明描画から外し、ぼかしの後に描き直すことで、ぼけずに読める状態を保つ。
        /// </summary>
        private static void EnsureRendererFeatures(int overlayLayer)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);
            if (rendererData == null)
            {
                Debug.LogError("[VhsPostEffectSetup] " + RendererDataPath + " が見つかりません。先に URP をセットアップしてください。");
                return;
            }

            var vhs = EnsureFeature<VhsRenderFeature>(rendererData, "VhsPostEffect");

            // シェーダはマテリアル経由で参照されないので、ここで持たせておかないとビルドで剥がれる
            var featureSo = new SerializedObject(vhs);
            featureSo.FindProperty("shader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            featureSo.ApplyModifiedPropertiesWithoutUndo();

            if (overlayLayer >= 0)
            {
                int overlayMask = 1 << overlayLayer;

                var overlay = EnsureFeature<RenderObjects>(rendererData, OverlayPassName);
                overlay.settings.passTag = OverlayPassName;
                // ぼかし（-3）→ ここ（-2）→ 劣化（-1）→ URP のポストプロセス（±0）。
                // ポストプロセスより後ろに置くと HUD が消える（VhsRenderFeature のコメント参照）
                overlay.settings.Event = RenderPassEvent.BeforeRenderingPostProcessing - 2;
                overlay.settings.filterSettings.RenderQueueType = RenderQueueType.Transparent;
                overlay.settings.filterSettings.LayerMask = overlayMask;
                overlay.Create();
                EditorUtility.SetDirty(overlay);

                // カメラのカリングからは外さない。外すと描画対象そのものが消え、上のパスでも描けなくなる
                rendererData.transparentLayerMask = rendererData.transparentLayerMask & ~overlayMask;
            }

            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Renderer の Feature 一覧に T を足す。既にあればそれを返す。</summary>
        private static T EnsureFeature<T>(UniversalRendererData rendererData, string name)
            where T : ScriptableRendererFeature
        {
            var feature = rendererData.rendererFeatures.Find(f => f is T) as T;
            if (feature != null) return feature;

            feature = ScriptableObject.CreateInstance<T>();
            feature.name = name;
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
            return feature;
        }

        /// <summary>日時表示用のレイヤーを確保する。取れなければ -1。</summary>
        private static int EnsureOverlayLayer()
        {
            int existing = LayerMask.NameToLayer(VhsDateDisplay.OverlayLayerName);
            if (existing >= 0) return existing;

            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (asset.Length == 0) return -1;

            var tagManager = new SerializedObject(asset[0]);
            var layers = tagManager.FindProperty("layers");

            // 0〜7 は Unity の組み込みレイヤーなので触らない
            for (int i = 8; i < layers.arraySize; i++)
            {
                var layer = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(layer.stringValue)) continue;

                layer.stringValue = VhsDateDisplay.OverlayLayerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }

            Debug.LogError("[VhsPostEffectSetup] レイヤーの空きがありません。日時表示にも視界ブラーが掛かります。");
            return -1;
        }

        /// <summary>HUD の文字も同じ VCR OSD Mono に揃える。</summary>
        private static void ApplyFontToHud()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) return;

            foreach (var hud in Object.FindObjectsOfType<HudController>())
            {
                var so = new SerializedObject(hud);
                so.FindProperty("font").objectReferenceValue = font;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(hud);
            }
        }

        /// <summary>日時表示を置き、VCR OSD Mono を割り当てる。</summary>
        private static void EnsureDateDisplay()
        {
            var go = GameObject.Find(DateObjectName);
            if (go == null) go = new GameObject(DateObjectName);

            var display = go.GetComponent<VhsDateDisplay>();
            if (display == null) display = go.AddComponent<VhsDateDisplay>();

            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogError("[VhsPostEffectSetup] " + FontPath + " が見つかりません。日時表示の文字が出ません。");
            }

            var so = new SerializedObject(display);
            so.FindProperty("font").objectReferenceValue = font;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(go);
        }
    }
}
