using UnityEngine;
using OverTheRainbow.Vision;

namespace OverTheRainbow.UI
{
    /// <summary>
    /// 遮眼子トラッカーの接続状態を出す簡易オーバーレイ（企画書 5. キャリブレーション相当）。
    /// 体験会で「動いていないことに気付けない」のが一番困るので、
    /// 未接続とキーボード代用の2つは常時小さく出し、詳細は F1 で開く。
    /// IMGUI なのは HUD の Canvas を汚さずに抜き差しできるため。Phase 5 で本 UI に差し替える前提。
    /// </summary>
    public class EyeCoverStatusOverlay : MonoBehaviour
    {
        [SerializeField] private EyeCoverInput eyeCover;
        [Tooltip("macOS では F1〜F12 が輝度・音量に取られるので、既定は Tab")]
        [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
        [Tooltip("詳細パネルを開いている間、このキーでマーカー色を拾い直す")]
        [SerializeField] private KeyCode calibrateKey = KeyCode.C;
        [Tooltip("詳細パネルを最初から開いておく（設営中に便利）")]
        [SerializeField] private bool showDetailsOnStart;

        private IEyeCoverDiagnostics diagnostics;
        private bool showDetails;
        private string calibrateMessage = string.Empty;
        private float calibrateMessageUntil;
        private GUIStyle boxStyle;
        private GUIStyle labelStyle;

        private void Start()
        {
            if (eyeCover == null) eyeCover = FindObjectOfType<EyeCoverInput>();
            foreach (var behaviour in FindObjectsOfType<MonoBehaviour>())
            {
                if (behaviour is IEyeCoverDiagnostics found)
                {
                    diagnostics = found;
                    break;
                }
            }
            showDetails = showDetailsOnStart;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) showDetails = !showDetails;

            // 会場の照明に合わせる。遮眼子をカメラに向けて押す
            if (showDetails && Input.GetKeyDown(calibrateKey) && diagnostics != null)
            {
                calibrateMessage = diagnostics.TryCalibrateColor()
                    ? "マーカー色を拾いました"
                    : "色を拾えませんでした（マーカーをカメラに近づけてください）";
                calibrateMessageUntil = Time.realtimeSinceStartup + 3f;
            }
        }

        private void OnGUI()
        {
            if (eyeCover == null) return;
            EnsureStyles();

            if (eyeCover.IsUsingFallback) DrawFallbackWarning();
            if (showDetails) DrawDetails();
        }

        /// <summary>トラッカーが死んでいるのに気付かず遊ばせてしまうのを防ぐ。</summary>
        private void DrawFallbackWarning()
        {
            var rect = new Rect(Screen.width * 0.5f - 320f, 12f, 640f, 30f);
            var prev = GUI.color;
            GUI.color = new Color(1f, 0.55f, 0.2f, 0.9f);
            GUI.Label(rect, "遮眼子トラッカー未接続 — SPACE キーで代用中（" + toggleKey + " で詳細）", labelStyle);
            GUI.color = prev;
        }

        private void DrawDetails()
        {
            var rect = new Rect(12f, Screen.height - 232f, 420f, 220f);
            GUI.Box(rect, GUIContent.none, boxStyle);
            GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 12f, rect.width - 28f, rect.height - 24f));

            GUILayout.Label("遮眼子トラッカー（" + toggleKey + " で閉じる）", labelStyle);
            GUILayout.Space(6f);
            GUILayout.Label("入力元　" + eyeCover.SourceName, labelStyle);

            if (diagnostics == null)
            {
                GUILayout.Label("検出コンポーネントがシーンにありません", labelStyle);
                GUILayout.Label("メニュー「0v3r 7h3 R41nb0w / 遮眼子トラッカーをセットアップ」", labelStyle);
            }
            else
            {
                bool live = eyeCover.IsSourceAvailable;
                GUILayout.Label("状態　　" + diagnostics.StatusLine,
                    Colored(live ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.5f, 0.4f)));
                GUILayout.Label("顔　　　" + (diagnostics.FaceFound ? "検出" : "未検出 — カメラに顔を入れてください"),
                    Colored(diagnostics.FaceFound ? Color.white : new Color(1f, 0.6f, 0.3f)));
                GUILayout.Label("マーカー面積　" + diagnostics.MarkerRatio.ToString("F4"), labelStyle);
                GUILayout.Label("目までの距離　"
                    + (diagnostics.EyeDistance < 0f ? "—" : diagnostics.EyeDistance.ToString("F2")), labelStyle);
            }

            GUILayout.Space(6f);
            bool covered = eyeCover.IsEyeCovered;
            string eye = diagnostics != null && !string.IsNullOrEmpty(diagnostics.CoveredEye)
                ? "（" + (diagnostics.CoveredEye == "L" ? "左目" : "右目") + "）"
                : string.Empty;
            GUILayout.Label("判定　　" + (covered ? "遮眼子を当てている" + eye : "当てていない"),
                Colored(covered ? new Color(1f, 0.95f, 0.6f) : Color.white));

            GUILayout.Space(4f);
            if (diagnostics != null && diagnostics.PreviewTexture != null)
            {
                GUILayout.Label("C　この色を拾う（遮眼子をカメラに向けて）", labelStyle);
            }
            if (Time.realtimeSinceStartup < calibrateMessageUntil)
            {
                GUILayout.Label(calibrateMessage, Colored(new Color(1f, 0.95f, 0.6f)));
            }

            GUILayout.EndArea();

            DrawPreview(rect);
        }

        /// <summary>詳細パネルの上にカメラ画像を出す。設営時にカメラの向きを合わせるため。</summary>
        private void DrawPreview(Rect panel)
        {
            Texture preview = diagnostics != null ? diagnostics.PreviewTexture : null;
            if (preview == null || preview.width <= 16) return;

            float width = 240f;
            float height = width * preview.height / preview.width;
            var rect = new Rect(panel.x, panel.y - height - 8f, width, height);
            GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
        }

        private GUIStyle Colored(Color color)
        {
            var style = new GUIStyle(labelStyle);
            style.normal.textColor = color;
            return style;
        }

        private void EnsureStyles()
        {
            if (labelStyle != null) return;

            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            labelStyle.normal.textColor = Color.white;

            boxStyle = new GUIStyle(GUI.skin.box);
        }
    }
}
