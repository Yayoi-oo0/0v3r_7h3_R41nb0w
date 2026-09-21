using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OverTheRainbow.Stage;

namespace OverTheRainbow.EditorTools
{
    /// <summary>
    /// コース脇の白ボックスの壁を、柵のモデル（fence_single）に置き換える。
    /// 当たり判定は今までどおり見えない一枚板が持つので、落下防止の挙動は変わらない。
    /// </summary>
    public static class FenceSetup
    {
        private const string FencePath = "Assets/Snowed Fence/FBX/fence_single.FBX";
        private const string MaterialPath = "Assets/Snowed Fence/Materials/FenceWhite.mat";
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("0v3r 7h3 R41nb0w/道の脇に柵を立てる", false, 29)]
        public static void SetupMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[FenceSetup] 柵を立てました（白いマテリアルを新規作成して割り当て）。大きさや位置は Stage の StageBuilder（壁・柵）で調整できます。");
        }

        public static void ApplyToOpenScene()
        {
            foreach (var builder in Object.FindObjectsOfType<StageBuilder>())
            {
                Apply(builder);
                EditorUtility.SetDirty(builder);
            }
        }

        public static void Apply(StageBuilder builder)
        {
            var fence = AssetDatabase.LoadAssetAtPath<GameObject>(FencePath);
            if (fence == null)
            {
                Debug.LogError("[FenceSetup] " + FencePath + " が見つかりません。");
                return;
            }

            var so = new SerializedObject(builder);
            so.FindProperty("sideFencePrefab").objectReferenceValue = fence;
            so.FindProperty("fenceMaterial").objectReferenceValue = EnsureWhiteMaterial();
            so.FindProperty("buildSideWalls").boolValue = true; // 当たり判定は必要
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 白い柵用のマテリアルを用意する。
        /// 同梱の fence.mat は Built-in 用シェーダなので、URP ではピンクになる。
        /// 元アセットを書き換えると再インポートで戻るため、別アセットとして作る。
        /// </summary>
        private static Material EnsureWhiteMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "FenceWhite" };
            // 木目のテクスチャは貼らない。白い柵にしたいので、素の白で塗る
            material.SetColor("_BaseColor", new Color(0.94f, 0.94f, 0.92f));
            material.SetFloat("_Smoothness", 0.05f); // 塗装した木くらいの反射に抑える

            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[FenceSetup] " + MaterialPath + " を作成しました。");
            return material;
        }
    }
}
