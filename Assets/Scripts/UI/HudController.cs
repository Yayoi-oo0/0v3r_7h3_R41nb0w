using UnityEngine;
using UnityEngine.UI;
using OverTheRainbow.Core;
using OverTheRainbow.Player;
using OverTheRainbow.Stage;
using OverTheRainbow.Vision;

namespace OverTheRainbow.UI
{
    /// <summary>
    /// Phase 1 用の最小 HUD。Canvas と Text をランタイムで組み立てるので、
    /// シーン側の手作業は不要。Phase 5 で作り込んだ UI に差し替える前提。
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [SerializeField] private EyeCoverInput eyeCover;
        [SerializeField] private DistanceTracker distanceTracker;
        [SerializeField] private DarknessChaser darkness;
        [Tooltip("操作説明を画面下部に出す（体験会では切る）")]
        [SerializeField] private bool showDebugHelp = true;

        private Text distanceText;
        private Text visionText;
        private Text centerText;
        private Text helpText;

        private void Awake()
        {
            BuildCanvas();
        }

        /// <summary>
        /// 参照は Start で探す。DistanceTracker / DarknessChaser は他のスクリプトの Awake で
        /// 自動追加されることがあり、Awake の実行順によってはまだ存在しないため。
        /// </summary>
        private void Start()
        {
            if (eyeCover == null) eyeCover = FindObjectOfType<EyeCoverInput>();
            if (distanceTracker == null) distanceTracker = FindObjectOfType<DistanceTracker>();
            if (darkness == null) darkness = FindObjectOfType<DarknessChaser>();
        }

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;

            distanceText.text = FormatDistance();
            if (darkness != null)
            {
                distanceText.text += "\n闇まで  " + Mathf.Max(0, Mathf.FloorToInt(darkness.DistanceToPlayer)) + " m";
            }

            bool covered = eyeCover != null && eyeCover.IsEyeCovered;
            visionText.text = covered ? "視界：クリア（移動ロック）" : "視界：ぼやけ";
            visionText.color = covered ? new Color(1f, 0.95f, 0.6f) : new Color(1f, 1f, 1f, 0.6f);

            centerText.text = BuildCenterText(gm);
            helpText.enabled = showDebugHelp;
        }

        private string BuildCenterText(GameManager gm)
        {
            switch (gm.State)
            {
                case GameState.Countdown:
                    return Mathf.CeilToInt(gm.CountdownRemaining).ToString();

                case GameState.GameOver:
                    return "GAME OVER\n" + ReasonLabel(gm.LastGameOverReason) + "\n\n" + BuildResultText();

                default:
                    return string.Empty;
            }
        }

        /// <summary>リザルト（到達距離）。</summary>
        private string BuildResultText()
        {
            return "到達距離  " + FormatDistance() + "\n\nR でリトライ";
        }

        private string FormatDistance()
        {
            float distance = distanceTracker != null ? distanceTracker.Distance : 0f;
            return Mathf.FloorToInt(distance) + " m";
        }

        private static string ReasonLabel(GameOverReason reason)
        {
            switch (reason)
            {
                case GameOverReason.Fell: return "穴に落ちた";
                case GameOverReason.Darkness: return "闇に飲まれた";
                case GameOverReason.Corruption: return "視界が侵食された";
                default: return string.Empty;
            }
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("HUD_Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            Font font = LoadBuiltinFont();

            distanceText = CreateText(canvasGo.transform, font, "Distance", 44, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -40f), new Vector2(600f, 120f));

            visionText = CreateText(canvasGo.transform, font, "Vision", 36, TextAnchor.UpperRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(700f, 140f));

            centerText = CreateText(canvasGo.transform, font, "Center", 72, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 600f));

            helpText = CreateText(canvasGo.transform, font, "Help", 28, TextAnchor.LowerCenter,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(1400f, 80f));
            helpText.color = new Color(1f, 1f, 1f, 0.45f);
            helpText.text = "WASD 移動　マウス 視点　遮眼子を目に当てる（SPACE で代用）　F1 トラッカー状態　R リトライ　ESC ポーズ";
        }

        private static Text CreateText(Transform parent, Font font, string name, int size, TextAnchor anchor,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size2D)
        {
            var go = new GameObject("Text_" + name, typeof(Text));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(anchorMin.x, anchorMin.y);
            rect.anchoredPosition = position;
            rect.sizeDelta = size2D;

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Unity 2022 以降は LegacyRuntime.ttf。古い名前もフォールバックで試す。</summary>
        private static Font LoadBuiltinFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }
    }
}
