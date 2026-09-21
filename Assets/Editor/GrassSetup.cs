using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OverTheRainbow.Stage;

namespace OverTheRainbow.EditorTools
{
    /// <summary>
    /// 道の脇に平原（芝生）を敷く。視力検査のあの絵——地平線まで続く道と、その両脇の草——を狙う。
    ///
    /// 芝のテクスチャは手持ちが無いので、繰り返しても継ぎ目が出ないノイズから作る。
    /// 併せて、平原が地平線まで届くようカメラの描画距離を伸ばし、視界を塞ぐ脇の壁を見えなくする
    /// （当たり判定は残るので、道から落ちることはない）。
    /// </summary>
    public static class GrassSetup
    {
        private const string TextureDir = "Assets/Textures";
        private const string TexturePath = TextureDir + "/Grass.png";
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const int TextureSize = 512;

        /// <summary>平原の端まで描けるだけの距離。既定の 300m では地平線の手前で切れる。</summary>
        private const float CameraFarClip = 1200f;

        [MenuItem("0v3r 7h3 R41nb0w/道の脇に平原を敷く", false, 28)]
        public static void SetupMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[GrassSetup] 平原を敷きました。芝の色や幅は Stage の StageBuilder（平原）で調整できます。");
        }

        /// <summary>今開いているシーンの StageBuilder とカメラに設定する。</summary>
        public static void ApplyToOpenScene()
        {
            foreach (var builder in Object.FindObjectsOfType<StageBuilder>())
            {
                Apply(builder);
                EditorUtility.SetDirty(builder);
            }

            foreach (var cam in Object.FindObjectsOfType<Camera>())
            {
                if (cam.farClipPlane >= CameraFarClip) continue;
                cam.farClipPlane = CameraFarClip;
                EditorUtility.SetDirty(cam);
            }
        }

        /// <summary>StageBuilder の平原まわりの設定を入れる。</summary>
        public static void Apply(StageBuilder builder)
        {
            var so = new SerializedObject(builder);
            so.FindProperty("buildGrass").boolValue = true;
            so.FindProperty("grassTexture").objectReferenceValue = EnsureTexture();
            so.FindProperty("buildFarRoad").boolValue = true;
            // 平原が見えるように壁を隠す。当たり判定は残るので落ちない
            so.FindProperty("showSideWalls").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Texture2D EnsureTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (existing != null) return existing;

            Directory.CreateDirectory(TextureDir);
            Texture2D generated = GenerateGrass(TextureSize);
            File.WriteAllBytes(TexturePath, generated.EncodeToPNG());
            Object.DestroyImmediate(generated);

            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.wrapMode = TextureWrapMode.Repeat;   // 繰り返して貼るので必須
            importer.filterMode = FilterMode.Trilinear;   // 遠景でちらつかせない
            importer.anisoLevel = 8;                      // 浅い角度で見るので効く
            importer.maxTextureSize = TextureSize;
            importer.SaveAndReimport();

            Debug.Log("[GrassSetup] " + TexturePath + " を生成しました。");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        /// <summary>
        /// 繰り返しても継ぎ目の出ない芝テクスチャを作る。
        /// 格子の乱数を周期的に補間した値ノイズを数オクターブ重ね、緑の濃淡に変換する。
        /// 最後に 1 画素ごとの細かいばらつきを足すと、近くで見たときの草っぽさが出る。
        /// </summary>
        private static Texture2D GenerateGrass(int size)
        {
            var rng = new System.Random(20260921);
            var field = new float[size * size];
            float amplitude = 1f;
            float totalAmplitude = 0f;

            foreach (int frequency in new[] { 4, 8, 16, 48 })
            {
                var grid = new float[frequency * frequency];
                for (int i = 0; i < grid.Length; i++) grid[i] = (float)rng.NextDouble();

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        field[y * size + x] += amplitude * SampleGrid(grid, frequency, x / (float)size, y / (float)size);
                    }
                }

                totalAmplitude += amplitude;
                amplitude *= 0.55f;
            }

            var dark = new Color(0.20f, 0.30f, 0.11f);
            var light = new Color(0.56f, 0.68f, 0.29f);

            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                float n = Mathf.Clamp01(field[i] / totalAmplitude);
                Color color = Color.Lerp(dark, light, n);

                // 一様に混ぜず、明るいところほど強く散らす（日の当たった草先のちらつき）
                float speckle = ((float)rng.NextDouble() - 0.5f) * 0.10f * (0.4f + n);
                color.r = Mathf.Clamp01(color.r + speckle);
                color.g = Mathf.Clamp01(color.g + speckle * 1.2f);
                color.b = Mathf.Clamp01(color.b + speckle * 0.6f);
                pixels[i] = color;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>格子を周期的に（端が繋がるように）補間して取り出す。</summary>
        private static float SampleGrid(float[] grid, int frequency, float u, float v)
        {
            float fx = u * frequency;
            float fy = v * frequency;
            int x0 = Mathf.FloorToInt(fx);
            int y0 = Mathf.FloorToInt(fy);
            int x1 = (x0 + 1) % frequency;
            int y1 = (y0 + 1) % frequency;
            x0 %= frequency;
            y0 %= frequency;

            float tx = Smooth(fx - Mathf.Floor(fx));
            float ty = Smooth(fy - Mathf.Floor(fy));

            float top = Mathf.Lerp(grid[y0 * frequency + x0], grid[y0 * frequency + x1], tx);
            float bottom = Mathf.Lerp(grid[y1 * frequency + x0], grid[y1 * frequency + x1], tx);
            return Mathf.Lerp(top, bottom, ty);
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
