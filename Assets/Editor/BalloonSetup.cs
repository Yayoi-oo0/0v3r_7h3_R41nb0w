using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OverTheRainbow.Stage;

namespace OverTheRainbow.EditorTools
{
    /// <summary>
    /// 道の先の赤い気球（企画書 4.2）をプレイヤーの子として配置する。
    /// OBJ には .mtl が同梱されていないので、色テクスチャを貼った URP マテリアルを作って割り当てる。
    /// 何度実行しても、既にあるものは作り直さずに再利用する。
    /// </summary>
    public static class BalloonSetup
    {
        private const string ModelPath = "Assets/viva-balloon/source/Viva Balloon.OBJ";
        private const string TexturePath = "Assets/viva-balloon/textures/Viva_Balloon_Colors_Mat1.jpg";
        private const string MaterialPath = "Assets/viva-balloon/VivaBalloon.mat";
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string ObjectName = "VivaBalloon";

        /// <summary>気球の見かけの大きさ（高さ約 21m 相当）。</summary>
        private const float ModelScale = 30f;

        [MenuItem("0v3r 7h3 R41nb0w/道の先に赤い気球を配置", false, 27)]
        public static void SetupMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[BalloonSetup] 赤い気球を配置しました。距離や高さは Player/" + ObjectName
                    + " の BalloonMarker（Offset）で調整できます。");
        }

        /// <summary>今開いているシーンのプレイヤーに気球をぶら下げる。</summary>
        public static void ApplyToOpenScene()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Debug.LogError("[BalloonSetup] Player が見つかりません。先に Main シーンを生成してください。");
                return;
            }

            Apply(player.transform);
            EditorUtility.SetDirty(player);
        }

        /// <summary>プレイヤーの下に気球一式を用意する。</summary>
        public static void Apply(Transform player)
        {
            Transform existing = player.Find(ObjectName);
            GameObject balloon = existing != null ? existing.gameObject : new GameObject(ObjectName);
            balloon.transform.SetParent(player, false);

            var marker = balloon.GetComponent<BalloonMarker>();
            if (marker == null) balloon.AddComponent<BalloonMarker>();

            if (balloon.GetComponentInChildren<MeshRenderer>() == null)
            {
                GameObject model = InstantiateModel(balloon.transform);
                if (model == null) return;
                CenterOn(balloon.transform, model.transform);
            }
        }

        private static GameObject InstantiateModel(Transform parent)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null)
            {
                Debug.LogError("[BalloonSetup] " + ModelPath + " が見つかりません。");
                return null;
            }

            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * ModelScale;

            Material material = EnsureMaterial();
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                // OBJ 側は 37 個のマテリアルに分かれているが、テクスチャは1枚のアトラス。
                // 全スロットに同じマテリアルを割り当てる
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;

                // 180m 先の遠景なので影は不要。影のカスケードを無駄に使わせない
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return model;
        }

        /// <summary>モデルの原点は中心からずれているので、見た目の中心を親に合わせる。</summary>
        private static void CenterOn(Transform anchor, Transform model)
        {
            var renderers = model.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            model.position -= bounds.center - anchor.position;
        }

        private static Material EnsureMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader) { name = "VivaBalloon" };

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (texture != null) material.SetTexture("_BaseMap", texture);
            else Debug.LogWarning("[BalloonSetup] " + TexturePath + " が見つかりません。無地になります。");

            // 布と籠なので、てかりは抑える
            material.SetFloat("_Smoothness", 0.15f);

            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }
    }
}
