using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OverTheRainbow.Vision
{
    /// <summary>
    /// 侵食ゲージに合わせて、充血したような赤いビネットを画面端から広げる（企画書 6.5 侵食進行）。
    /// ブラー用とは別の Volume に置く。ブラー側は weight を 0 にするので、同じ Volume だとビネットも消えてしまう。
    /// </summary>
    [RequireComponent(typeof(Volume))]
    public class CorruptionVignetteController : MonoBehaviour
    {
        [Tooltip("未設定ならシーンから探す")]
        [SerializeField] private CorruptionSystem corruption;

        [SerializeField] private Color color = new Color(0.55f, 0.02f, 0.04f);
        [Tooltip("侵食 0 → 1 でのビネットの濃さ")]
        [SerializeField, Range(0f, 1f)] private float intensityAtMin = 0f;
        [SerializeField, Range(0f, 1f)] private float intensityAtMax = 0.75f;
        [Tooltip("侵食 0 → 1 でのぼかし幅。大きいほど画面中央まで染み込む")]
        [SerializeField, Range(0.01f, 1f)] private float smoothnessAtMin = 0.2f;
        [SerializeField, Range(0.01f, 1f)] private float smoothnessAtMax = 1f;

        private Vignette vignette;

        private void Awake()
        {
            if (corruption == null) corruption = FindObjectOfType<CorruptionSystem>();

            // volume.profile は実行時のコピーを返すので、アセット側は書き換わらない
            if (!GetComponent<Volume>().profile.TryGet(out vignette))
            {
                Debug.LogError("[CorruptionVignetteController] Volume Profile に Vignette がありません。", this);
                enabled = false;
                return;
            }
            vignette.color.Override(color);
            vignette.intensity.overrideState = true;
            vignette.smoothness.overrideState = true;
        }

        private void Update()
        {
            float t = corruption != null ? corruption.Corruption : 0f;
            vignette.intensity.value = Mathf.Lerp(intensityAtMin, intensityAtMax, t);
            vignette.smoothness.value = Mathf.Lerp(smoothnessAtMin, smoothnessAtMax, t);
        }
    }
}
