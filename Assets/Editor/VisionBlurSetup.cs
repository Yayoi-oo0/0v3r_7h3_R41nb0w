using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using OverTheRainbow.Vision;

namespace OverTheRainbow.EditorTools
{
    /// <summary>
    /// 視界ブラー（Depth of Field）の Volume をシーンに組み込む。
    /// 何度実行しても、既にあるものは作り直さずに再利用する。
    /// </summary>
    public static class VisionBlurSetup
    {
        private const string ProfilePath = "Assets/Settings/VisionBlurProfile.asset";
        private const string VolumeObjectName = "VisionBlurVolume";
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("0v3r 7h3 R41nb0w/視界ブラーをセットアップ", false, 21)]
        public static void SetupMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[VisionBlurSetup] 視界ブラーをセットアップしました。");
        }

        /// <summary>今開いているシーンに Volume を置き、カメラのポストプロセスを有効にする。</summary>
        public static void ApplyToOpenScene()
        {
            UrpSetup.EnsurePostProcessData();
            VolumeProfile profile = EnsureProfile();

            foreach (var cam in Object.FindObjectsOfType<Camera>())
            {
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                EditorUtility.SetDirty(cam.gameObject);
            }

            var go = GameObject.Find(VolumeObjectName);
            if (go == null) go = new GameObject(VolumeObjectName);

            var volume = go.GetComponent<Volume>();
            if (volume == null) volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            var controller = go.GetComponent<VisionBlurController>();
            if (controller == null) controller = go.AddComponent<VisionBlurController>();

            var so = new SerializedObject(controller);
            so.FindProperty("eyeCover").objectReferenceValue = Object.FindObjectOfType<EyeCoverInput>();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(go);
        }

        /// <summary>Depth of Field を入れた Volume Profile を作る。</summary>
        private static VolumeProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile != null) return profile;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            // 実行中は VisionBlurController が焦点距離を動かす。ここの値はエディタで見たときの最大ぼけ（maxCoC = 1）。
            // 焦点距離はピント距離より短くすること。逆転すると全体が手前ボケ扱いになり、ぼけが弱くなる。
            var dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(0.1f);
            dof.focalLength.Override(9.51f);
            dof.aperture.Override(1f);

            // Volume Profile の中身はサブアセットとして保存しないと消える
            dof.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(dof, profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
