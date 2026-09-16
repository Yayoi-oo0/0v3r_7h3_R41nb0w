using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OverTheRainbow.Vision
{
    /// <summary>
    /// 通常時は Depth of Field（Bokeh）で視界をぼかし、遮眼子中はブラーを 0 にする（企画書 4.1 / 6.5）。
    ///
    /// Volume の weight で補間するとピント距離まで動き、ピントの合う範囲が輪のように広がってしまう。
    /// そこでピント距離は最短に固定し、焦点距離だけでぼけの大きさを変えて、画面全体を一様に切り替える。
    /// </summary>
    [RequireComponent(typeof(Volume))]
    public class VisionBlurController : MonoBehaviour
    {
        // URP の Bokeh は coc = (1 - P / 奥行き) * maxCoC、maxCoC = (f / 絞り) * F / (P - F)（F = f / 1000）。
        // P を最短の 0.1m にすると 1m 以遠で (1 - P / 奥行き) ≒ 1 になり、ぼけの大きさが maxCoC だけで決まる。
        private const float FocusDistance = 0.1f;
        private const float Aperture = 1f;

        [Tooltip("未設定ならシーンから探す")]
        [SerializeField] private EyeCoverInput eyeCover;
        [Tooltip("ぼやけ ↔ クリアの切り替えにかける秒数（企画書 6.5：0.2〜0.3 秒）")]
        [SerializeField, Min(0f)] private float transitionSeconds = 0.25f;

        private Volume volume;
        private DepthOfField dof;
        private float blur = 1f; // 0 = クリア, 1 = 最大ぼけ

        private void Awake()
        {
            volume = GetComponent<Volume>();
            if (eyeCover == null) eyeCover = FindObjectOfType<EyeCoverInput>();

            // volume.profile は実行時のコピーを返すので、アセット側は書き換わらない
            if (!volume.profile.TryGet(out dof))
            {
                Debug.LogError("[VisionBlurController] Volume Profile に Depth of Field がありません。", this);
                enabled = false;
                return;
            }
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(FocusDistance);
            dof.aperture.Override(Aperture);
            dof.focalLength.overrideState = true;

            Apply(blur);
        }

        private void Update()
        {
            bool covered = eyeCover != null && eyeCover.IsEyeCovered;
            float target = covered ? 0f : 1f;

            blur = transitionSeconds > 0f
                ? Mathf.MoveTowards(blur, target, Time.deltaTime / transitionSeconds)
                : target;
            Apply(blur);
        }

        private void Apply(float amount)
        {
            // weight は補間に使わず、ON / OFF だけ。0 のときは DoF 自体を止めて負荷をなくす
            volume.weight = amount > 0f ? 1f : 0f;
            dof.focalLength.value = FocalLengthForCoC(amount);
        }

        /// <summary>maxCoC が目標値になる焦点距離（mm）。maxCoC の式を f について解いたもの。</summary>
        private static float FocalLengthForCoC(float maxCoC)
        {
            // f^2 / (1000 * 絞り) = maxCoC * (P - f / 1000)  →  f^2 + k f - 1000 k P = 0（k = maxCoC * 絞り）
            float k = maxCoC * Aperture;
            return (-k + Mathf.Sqrt(k * k + 4000f * k * FocusDistance)) * 0.5f;
        }
    }
}
