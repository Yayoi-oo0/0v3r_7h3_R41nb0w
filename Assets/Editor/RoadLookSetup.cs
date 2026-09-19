using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OverTheRainbow.Stage;

namespace OverTheRainbow.EditorTools
{
    /// <summary>
    /// コースの床に道路テクスチャ（KajamansRoads）を割り当てる。
    /// マテリアルは StageBuilder がランタイムで切り分けて作るので、ここでは絵を渡すだけ。
    /// </summary>
    public static class RoadLookSetup
    {
        private const string TextureFolder = "Assets/KajamansRoads/Textures/";
        private const string RoadTexturePath = TextureFolder + "Road_2lane_dark02.png";
        private const string RoadNormalPath = TextureFolder + "Road_2lane_dark02_n.png";
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("0v3r 7h3 R41nb0w/道路の見た目をセットアップ", false, 24)]
        public static void SetupMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[RoadLookSetup] 道路テクスチャを割り当てました。");
        }

        /// <summary>今開いているシーンの StageBuilder に道路テクスチャを渡す。</summary>
        public static void ApplyToOpenScene()
        {
            var builder = Object.FindObjectOfType<StageBuilder>();
            if (builder == null)
            {
                Debug.LogError("[RoadLookSetup] StageBuilder がシーンにありません。");
                return;
            }

            Apply(builder);
        }

        /// <summary>シーン生成の途中からも呼べるよう、対象を受け取る形も用意しておく。</summary>
        public static void Apply(StageBuilder builder)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(RoadTexturePath);
            var normal = AssetDatabase.LoadAssetAtPath<Texture>(RoadNormalPath);
            if (texture == null)
            {
                Debug.LogError("[RoadLookSetup] " + RoadTexturePath + " が見つかりません。床は白ボックスのままになります。");
                return;
            }

            var so = new SerializedObject(builder);
            so.FindProperty("roadTexture").objectReferenceValue = texture;
            so.FindProperty("roadNormalMap").objectReferenceValue = normal;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(builder);
        }
    }
}
