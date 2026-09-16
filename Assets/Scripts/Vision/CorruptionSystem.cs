using UnityEngine;
using OverTheRainbow.Core;

namespace OverTheRainbow.Vision
{
    /// <summary>
    /// 視界の侵食ゲージ（企画書 4.1 リスク：視界の侵食 / 7. パラメータ表）。
    /// 遮眼子を当てている間は溜まり、外すと侵食より遅い速度で回復する。
    /// 限界（1）に達したらゲームオーバー。
    /// 一度外したら、侵食が 0 に回復しきるまで次の遮眼子を受け付けない。
    /// </summary>
    public class CorruptionSystem : MonoBehaviour
    {
        [Tooltip("未設定なら同じ GameObject から探す")]
        [SerializeField] private EyeCoverInput eyeCover;

        [Tooltip("遮眼子中に 1 秒で進む量（0〜1）。0.1 で約 10 秒で限界")]
        [SerializeField, Min(0f)] private float corruptionPerSecond = 0.3f;
        [Tooltip("外している間に 1 秒で回復する量（0〜1）。侵食の半分以下に")]
        [SerializeField, Min(0f)] private float recoveryPerSecond = 0.5f;

        /// <summary>侵食の進み具合。0 = なし、1 = 限界。</summary>
        public float Corruption { get; private set; }

        private void Awake()
        {
            if (eyeCover == null) eyeCover = GetComponent<EyeCoverInput>();
            if (eyeCover == null) eyeCover = FindObjectOfType<EyeCoverInput>();
        }

        private void OnEnable()
        {
            if (eyeCover != null) eyeCover.CoverEnded += OnCoverEnded;
        }

        private void OnDisable()
        {
            if (eyeCover != null) eyeCover.CoverEnded -= OnCoverEnded;
        }

        private void OnCoverEnded()
        {
            if (Corruption > 0f) eyeCover.IsCoverLocked = true;
        }

        private void Update()
        {
            // ゲームオーバー後に回復してしまうと、赤く染まった画面が引いていくので止める
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.State != GameState.Playing && gm.State != GameState.Countdown) return;

            bool covered = eyeCover != null && eyeCover.IsEyeCovered;
            float delta = covered ? corruptionPerSecond : -recoveryPerSecond;
            Corruption = Mathf.Clamp01(Corruption + delta * Time.deltaTime);

            if (Corruption <= 0f && eyeCover != null) eyeCover.IsCoverLocked = false;

            if (Corruption >= 1f && gm != null)
            {
                gm.TriggerGameOver(GameOverReason.Corruption);
            }
        }
    }
}
