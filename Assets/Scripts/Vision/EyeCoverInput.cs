using System;
using UnityEngine;

namespace OverTheRainbow.Vision
{
    /// <summary>
    /// IEyeCoverSource の生の判定を平滑化し、ゲーム側に安定した IsEyeCovered を公開する。
    /// 企画書 6.3「ノイズ対策（必須）」に対応：
    ///   ・N フレーム連続一致でのみ状態遷移（チャタリング防止）
    ///   ・ON / OFF で必要フレーム数を変える（ヒステリシス）
    ///   ・Lost は「遮眼子なし」に安全側フォールバック
    /// Phase 1 ではキーボード入力なので平滑化はほぼ素通りするが、
    /// Phase 3 で MediaPipe に差し替えても呼び出し側を変えずに済むよう先に噛ませておく。
    /// </summary>
    public class EyeCoverInput : MonoBehaviour
    {
        [Header("入力元")]
        [Tooltip("未設定なら同じ GameObject から IEyeCoverSource を探す")]
        [SerializeField] private MonoBehaviour sourceBehaviour;
        [Tooltip("主入力が使用不能なとき（カメラ未接続、Python 未起動）に使う予備。通常はキーボード")]
        [SerializeField] private MonoBehaviour fallbackSourceBehaviour;

        [Header("平滑化（企画書 7. 状態遷移の連続フレーム数）")]
        [Tooltip("Open → Covered に必要な連続フレーム数")]
        [SerializeField, Min(1)] private int framesToCover = 5;
        [Tooltip("Covered → Open に必要な連続フレーム数。外すときは軽くして取り残され感を防ぐ")]
        [SerializeField, Min(1)] private int framesToUncover = 3;

        private IEyeCoverSource source;
        private IEyeCoverSource fallbackSource;
        private int agreeingFrames;
        private EyeState lastRaw = EyeState.Open;

        /// <summary>平滑化後の「片目を覆っているか」。ゲームロジックはこちらだけを見る。</summary>
        public bool IsEyeCovered { get; private set; }

        /// <summary>遮眼子を当てている合計秒数。</summary>
        public float TotalCoveredSeconds { get; private set; }

        /// <summary>入力元が使用不能（カメラ未接続など）。キャリブレーション画面の判定に使う。</summary>
        public bool IsSourceAvailable => source != null && source.IsAvailable;

        /// <summary>予備に落ちているか。体験会でトラッカーが落ちたことに気付くための表示用。</summary>
        public bool IsUsingFallback => !IsSourceAvailable && fallbackSource != null && fallbackSource.IsAvailable;

        /// <summary>今フレーム実際に読んでいる入力元。主入力が使えなければ予備。</summary>
        public IEyeCoverSource ActiveSource => IsSourceAvailable ? source : fallbackSource;

        public string SourceName
        {
            get
            {
                IEyeCoverSource active = ActiveSource;
                return active != null ? active.SourceName : "None";
            }
        }

        /// <summary>
        /// true の間は新しく遮眼子を当てても IsEyeCovered にならない（当てている最中の状態は変えない）。
        /// 侵食が回復しきるまで連続使用させないために CorruptionSystem が切り替える。
        /// </summary>
        public bool IsCoverLocked { get; set; }

        public event Action CoverBegan;
        public event Action CoverEnded;

        private void Awake()
        {
            source = sourceBehaviour as IEyeCoverSource;
            if (source == null) source = GetComponent<IEyeCoverSource>();
            if (source == null)
            {
                Debug.LogError("[EyeCoverInput] IEyeCoverSource が見つかりません。KeyboardEyeCoverSource を同じ GameObject に付けてください。", this);
            }

            fallbackSource = fallbackSourceBehaviour as IEyeCoverSource;
            if (fallbackSource == source) fallbackSource = null;
        }

        private void Update()
        {
            IEyeCoverSource active = ActiveSource;
            if (active == null) return;

            EyeState raw = active.IsAvailable ? active.ReadRaw() : EyeState.Lost;
            // Lost は安全側（遮眼子なし）に倒す。誤って視界クリアのまま固まるのを防ぐ。
            bool rawCovered = raw == EyeState.Covered;

            if (raw == lastRaw) agreeingFrames++;
            else agreeingFrames = 1;
            lastRaw = raw;

            int required = IsEyeCovered ? framesToUncover : framesToCover;
            bool blocked = rawCovered && IsCoverLocked;
            if (rawCovered != IsEyeCovered && agreeingFrames >= required && !blocked)
            {
                IsEyeCovered = rawCovered;
                if (IsEyeCovered) CoverBegan?.Invoke();
                else CoverEnded?.Invoke();
            }

            if (IsEyeCovered) TotalCoveredSeconds += Time.deltaTime;
        }
    }
}
