using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OverTheRainbow.UI;
using OverTheRainbow.Vision;

namespace OverTheRainbow.EditorTools
{
    /// <summary>
    /// 遮眼子の入力元を、キーボード仮実装から実機（MediaPipe Unity Plugin）へ切り替える。
    /// キーボードは消さず、トラッカー未接続時の予備として EyeCoverInput に繋いでおく
    /// （企画書 Phase 3「スペースキー実装はデバッグ用に残す」）。
    /// 何度実行しても、既にあるものは作り直さずに再利用する。
    /// </summary>
    public static class EyeCoverSetup
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("0v3r 7h3 R41nb0w/遮眼子トラッカーをセットアップ", false, 25)]
        public static void SetupMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[EyeCoverSetup] 遮眼子トラッカーを接続しました（MediaPipe Unity Plugin）。"
                    + "再生ボタンだけで動きます。カメラが使えないときは SPACE キーで代用できます（F1 で状態表示）。");
        }

        /// <summary>今開いているシーンの EyeCoverInput に、UDP 受信と予備のキーボードを繋ぐ。</summary>
        public static void ApplyToOpenScene()
        {
            var input = Object.FindObjectOfType<EyeCoverInput>();
            if (input == null)
            {
                Debug.LogError("[EyeCoverSetup] EyeCoverInput が見つかりません。先に Main シーンを生成してください。");
                return;
            }

            Apply(input.gameObject);
            EditorUtility.SetDirty(input.gameObject);
        }

        /// <summary>EyeCoverInput が乗っている GameObject に、入力元一式を揃える。</summary>
        public static void Apply(GameObject host)
        {
            var mediapipe = host.GetComponent<MediaPipeEyeCoverSource>();
            if (mediapipe == null) mediapipe = host.AddComponent<MediaPipeEyeCoverSource>();

            var keyboard = host.GetComponent<KeyboardEyeCoverSource>();
            if (keyboard == null) keyboard = host.AddComponent<KeyboardEyeCoverSource>();

            // Python 版が残っているとカメラを二重に掴んでしまうので外す
            // （スクリプト自体は残してあり、切り戻したくなったら手で付け直せる）
            RemoveIfPresent<UdpEyeCoverSource>(host);
            RemoveIfPresent<EyeTrackerLauncher>(host);

            var input = host.GetComponent<EyeCoverInput>();
            if (input != null)
            {
                // private なシリアライズフィールドなので SerializedObject 経由で設定する
                var so = new SerializedObject(input);
                so.FindProperty("sourceBehaviour").objectReferenceValue = mediapipe;
                so.FindProperty("fallbackSourceBehaviour").objectReferenceValue = keyboard;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var overlay = host.GetComponent<EyeCoverStatusOverlay>();
            if (overlay == null) overlay = host.AddComponent<EyeCoverStatusOverlay>();

            // F1 は macOS で輝度キーに取られる。古い既定値のままなら Tab に直す
            var overlaySo = new SerializedObject(overlay);
            var toggleKey = overlaySo.FindProperty("toggleKey");
            if (toggleKey.intValue == (int)KeyCode.F1)
            {
                toggleKey.intValue = (int)KeyCode.Tab;
                overlaySo.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void RemoveIfPresent<T>(GameObject host) where T : Component
        {
            var component = host.GetComponent<T>();
            if (component != null) Object.DestroyImmediate(component);
        }
    }
}
