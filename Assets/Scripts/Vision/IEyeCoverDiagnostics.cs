using UnityEngine;

namespace OverTheRainbow.Vision
{
    /// <summary>
    /// キャリブレーション画面に出すための、入力元の内部状態。
    /// MediaPipe 版と UDP 版のどちらでも同じ表示ができるように切り出してある。
    /// </summary>
    public interface IEyeCoverDiagnostics
    {
        /// <summary>顔を検出できているか。</summary>
        bool FaceFound { get; }

        /// <summary>マーカーが画面に占める面積比。</summary>
        float MarkerRatio { get; }

        /// <summary>マーカーから近い方の目までの距離（両目間隔に対する比）。未検出なら -1。</summary>
        float EyeDistance { get; }

        /// <summary>覆っている目（"L" / "R" / ""）。</summary>
        string CoveredEye { get; }

        /// <summary>接続・起動状態の1行説明。</summary>
        string StatusLine { get; }

        /// <summary>キャリブレーション画面に映すカメラ画像。無ければ null。</summary>
        Texture PreviewTexture { get; }

        /// <summary>
        /// 今写っている一番鮮やかな色にマーカー色を合わせる。
        /// 合わせられない入力元（判定を外部プロセスに任せている場合など）は false を返す。
        /// </summary>
        bool TryCalibrateColor();
    }
}
