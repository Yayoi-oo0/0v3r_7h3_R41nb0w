using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using OverTheRainbow.Vision;

namespace OverTheRainbow.EditorTools
{
    /// <summary>
    /// 侵食ゲージと赤いビネットの Volume をシーンに組み込む。
    /// 何度実行しても、既にあるものは作り直さずに再利用する。
    /// </summary>
    public static class CorruptionVignetteSetup
    {
        private const string ProfilePath = "Assets/Settings/CorruptionVignetteProfile.asset";
        private const string VolumeObjectName = "CorruptionVignetteVolume";
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("0v3r 7h3 R41nb0w/侵食ビネットをセットアップ", false, 22)]
        public static void SetupMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[CorruptionVignetteSetup] 侵食ビネットをセットアップしました。");
        }

        /// <summary>
        /// 今開いているシーンに CorruptionSystem と Volume を置く。
        /// カメラのポストプロセス有効化は VisionBlurSetup が行う。
        /// </summary>
        public static void ApplyToOpenScene()
        {
            var eyeCover = Object.FindObjectOfType<EyeCoverInput>();
            if (eyeCover == null)
            {
                Debug.LogError("[CorruptionVignetteSetup] EyeCoverInput がシーンにありません。");
                return;
            }

            // 侵食ゲージは入力と同じ GameSystems に置く
            var corruption = eyeCover.GetComponent<CorruptionSystem>();
            if (corruption == null) corruption = eyeCover.gameObject.AddComponent<CorruptionSystem>();
            SetReference(corruption, "eyeCover", eyeCover);

            var go = GameObject.Find(VolumeObjectName);
            if (go == null) go = new GameObject(VolumeObjectName);

            var volume = go.GetComponent<Volume>();
            if (volume == null) volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = EnsureProfile();

            var controller = go.GetComponent<CorruptionVignetteController>();
            if (controller == null) controller = go.AddComponent<CorruptionVignetteController>();
            SetReference(controller, "corruption", corruption);

            EditorUtility.SetDirty(eyeCover.gameObject);
            EditorUtility.SetDirty(go);
        }

        private static void SetReference(Object target, string propertyName, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(propertyName).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Vignette を入れた Volume Profile を作る。濃さとぼかし幅は実行中に Controller が動かす。</summary>
        private static VolumeProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile != null) return profile;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            var vignette = profile.Add<Vignette>(true);
            vignette.color.Override(new Color(0.55f, 0.02f, 0.04f));
            vignette.intensity.Override(0f);
            vignette.smoothness.Override(0.2f);

            // Volume Profile の中身はサブアセットとして保存しないと消える
            vignette.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(vignette, profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
