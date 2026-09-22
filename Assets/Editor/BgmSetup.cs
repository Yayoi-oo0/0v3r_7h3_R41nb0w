using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OverTheRainbow.Audio;
using OverTheRainbow.Core;

namespace OverTheRainbow.EditorTools
{
    /// <summary>
    /// BGM を鳴らす AudioSource と BgmPlayer を GameSystems に用意する。
    /// 再生は Play On Awake / Loop、終わり方だけ BgmPlayer が面倒を見る。
    /// </summary>
    public static class BgmSetup
    {
        private const string ClipPath = "Assets/Sounds/OverTheRainbow.mp3";
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("0v3r 7h3 R41nb0w/BGM をセットアップ", false, 30)]
        public static void SetupMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[BgmSetup] BGM を設定しました。音量とフェードの長さは GameSystems の BgmPlayer で調整できます。");
        }

        public static void ApplyToOpenScene()
        {
            var manager = Object.FindObjectOfType<GameManager>();
            if (manager == null)
            {
                Debug.LogError("[BgmSetup] GameManager が見つかりません。先に Main シーンを生成してください。");
                return;
            }

            Apply(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
        }

        public static void Apply(GameObject host)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
            if (clip == null)
            {
                Debug.LogError("[BgmSetup] " + ClipPath + " が見つかりません。");
                return;
            }

            var source = host.GetComponent<AudioSource>();
            if (source == null) source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.playOnAwake = true;
            source.loop = true;
            source.spatialBlend = 0f;

            if (host.GetComponent<BgmPlayer>() == null) host.AddComponent<BgmPlayer>();
            // 闇が迫ったときのピッチ下げ。AudioSource と同じ GameObject にある必要がある
            if (host.GetComponent<BgmPitchShifter>() == null) host.AddComponent<BgmPitchShifter>();
        }
    }
}
