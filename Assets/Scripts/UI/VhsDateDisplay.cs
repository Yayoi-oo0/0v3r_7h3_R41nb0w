using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace OverTheRainbow.UI
{
    /// <summary>
    /// VHS の OSD 風に現在時刻と日付を出す。
    /// Canvas は Overlay ではなくカメラ空間に置く。Overlay はカメラの描画より後なので、
    /// VhsRenderFeature の劣化が乗らず、文字だけ妙に綺麗に浮いてしまう。
    ///
    /// ただし視界ブラーまで掛かると読めなくなるので、専用レイヤーに逃がして
    /// ぼかしの後に描く。レイヤーの除外と描画パスの追加は VhsPostEffectSetup が行う。
    /// </summary>
    public class VhsDateDisplay : MonoBehaviour
    {
        /// <summary>このレイヤーだけ、通常の半透明描画から外してぼかしの後に描く。</summary>
        public const string OverlayLayerName = "VhsOverlay";

        [Tooltip("VCR OSD Mono。Assets/Fonts のものをセットアップ時に割り当てる")]
        [SerializeField] private Font font;
        [Tooltip("小さく描いてから引き伸ばすと、粗いピクセル感が出る")]
        [SerializeField] private int fontSize = 28;
        [Range(1f, 5f)] [SerializeField] private float pixelScale = 2.5f;
        [Range(0f, 1f)] [SerializeField] private float alpha = 0.8f;
        [Tooltip("画面左下からの余白")]
        [SerializeField] private Vector2 margin = new Vector2(60f, 60f);

        private Text text;
        private int shownMinute = -1;

        // Canvas に渡すカメラが要るので、シーンのカメラが揃う Start で組み立てる
        private void Start()
        {
            BuildCanvas();
        }

        private void Update()
        {
            DateTime now = DateTime.Now;

            // 表示は分までなので、分が変わったときだけ文字列を作り直す
            if (now.Minute == shownMinute) return;
            shownMinute = now.Minute;

            text.text = Format(now);
        }

        /// <summary>「PM  09:41 / SEP. 18 2026」の 2 行。月名は OS の言語に関わらず英語で出す。</summary>
        private static string Format(DateTime now)
        {
            var culture = CultureInfo.InvariantCulture;
            string ampm = now.Hour < 12 ? "AM" : "PM";
            string time = now.ToString("hh:mm", culture);
            string month = now.ToString("MMM", culture).ToUpperInvariant();

            return ampm + "  " + time + Environment.NewLine
                 + month + ". " + now.Day + " " + now.Year;
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("VhsDate_Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            // 壁際に寄っても手前の壁に隠れないよう、ニアクリップのすぐ先に置く
            canvas.planeDistance = 0.1f;
            // HUD より奥。侵食の警告表示などに隠れてよい
            canvas.sortingOrder = -1;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var go = new GameObject("Text_VhsDate", typeof(Text));
            go.transform.SetParent(canvasGo.transform, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = margin;
            rect.sizeDelta = new Vector2(600f, 200f);
            // 文字を大きくするのではなく引き伸ばす。ドットが目立ち、ブラウン管の粗さに近づく
            rect.localScale = Vector3.one * pixelScale;

            text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.lineSpacing = 1.1f;
            text.alignment = TextAnchor.LowerLeft;
            text.color = new Color(1f, 1f, 1f, alpha);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            ApplyOverlayLayer(canvasGo);

            if (font == null)
            {
                Debug.LogWarning("[VhsDateDisplay] フォントが未設定です。メニューの「VHS エフェクトをセットアップ」を実行してください。");
            }
        }

        /// <summary>レイヤーが無ければ通常描画のまま。読めはするので、警告だけ出して続ける。</summary>
        private static void ApplyOverlayLayer(GameObject canvasGo)
        {
            int layer = LayerMask.NameToLayer(OverlayLayerName);
            if (layer < 0)
            {
                Debug.LogWarning("[VhsDateDisplay] レイヤー " + OverlayLayerName + " がありません。"
                               + "日時表示にも視界ブラーが掛かります。メニューの「VHS エフェクトをセットアップ」を実行してください。");
                return;
            }

            foreach (var t in canvasGo.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
            }
        }
    }
}
