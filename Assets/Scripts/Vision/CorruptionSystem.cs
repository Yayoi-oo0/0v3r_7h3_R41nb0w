using UnityEngine;

namespace OverTheRainbow.Vision
{
    /// <summary>
    /// 視界の侵食ゲージ（企画書 4.1 リスク：視界の侵食 / 7. パラメータ表）。
    /// 遮眼子を当てている間は溜まり、外すと侵食より遅い速度で回復する。
    /// </summary>
    public class CorruptionSystem : MonoBehaviour
    {
        [Tooltip("未設定なら同じ GameObject から探す")]
        [SerializeField] private EyeCoverInput eyeCover;

        [Tooltip("遮眼子中に 1 秒で進む量（0〜1）。0.1 で約 10 秒で限界")]
        [SerializeField, Min(0f)] private float corruptionPerSecond = 0.1f;
        [Tooltip("外している間に 1 秒で回復する量（0〜1）。侵食の半分以下に")]
        [SerializeField, Min(0f)] private float recoveryPerSecond = 0.04f;

        /// <summary>侵食の進み具合。0 = なし、1 = 限界。</summary>
        public float Corruption { get; private set; }

        private void Awake()
        {
            if (eyeCover == null) eyeCover = GetComponent<EyeCoverInput>();
            if (eyeCover == null) eyeCover = FindObjectOfType<EyeCoverInput>();
        }

        private void Update()
        {
            bool covered = eyeCover != null && eyeCover.IsEyeCovered;
            float delta = covered ? corruptionPerSecond : -recoveryPerSecond;
            Corruption = Mathf.Clamp01(Corruption + delta * Time.deltaTime);
        }
    }
}
