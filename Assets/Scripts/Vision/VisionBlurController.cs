using UnityEngine;

namespace OverTheRainbow.Vision
{
    /// <summary>
    /// 通常時は視界をぼかし、遮眼子中はぼかしを 0 にする（企画書 4.1 / 6.5）。
    ///
    /// ぼかし自体は VisionBlurRenderFeature（URP-Default_Renderer に登録）が描く。
    /// ここは 0 〜 1 の濃さを渡すだけ。ぼけの強さの調整は Renderer Feature 側の設定で行う。
    /// </summary>
    public class VisionBlurController : MonoBehaviour
    {
        [Tooltip("未設定ならシーンから探す")]
        [SerializeField] private EyeCoverInput eyeCover;
        [Tooltip("遮眼子を外しているときのぼかしの濃さ。1 で Renderer Feature の設定どおりの最大ぼけ")]
        [SerializeField, Range(0f, 1f)] private float blurAmount = 1f;
        [Tooltip("ぼやけ ↔ クリアの切り替えにかける秒数（企画書 6.5：0.2〜0.3 秒）")]
        [SerializeField, Min(0f)] private float transitionSeconds = 0.25f;

        private float blur = 1f; // 0 = クリア, 1 = 最大ぼけ

        private void Awake()
        {
            if (eyeCover == null) eyeCover = FindObjectOfType<EyeCoverInput>();
        }

        private void OnEnable() => Apply();

        private void Update()
        {
            bool covered = eyeCover != null && eyeCover.IsEyeCovered;
            float target = covered ? 0f : 1f;

            blur = transitionSeconds > 0f
                ? Mathf.MoveTowards(blur, target, Time.deltaTime / transitionSeconds)
                : target;
            Apply();
        }

        // 消し忘れると次のシーンやエディタ復帰時にぼけたままになる
        private void OnDisable() => VisionBlurRenderFeature.Intensity = 0f;

        private void Apply() => VisionBlurRenderFeature.Intensity = blur * blurAmount;
    }
}
