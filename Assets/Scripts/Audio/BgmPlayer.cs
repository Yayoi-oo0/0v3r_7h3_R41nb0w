using UnityEngine;
using OverTheRainbow.Core;

namespace OverTheRainbow.Audio
{
    /// <summary>
    /// BGM を鳴らし続け、ゲームオーバーで短くフェードアウトさせる（企画書 6.4 AudioManager）。
    ///
    /// 再生開始は AudioSource の Play On Awake に任せる。ここが受け持つのは
    /// 「ゲームオーバーで音を引く」ところだけ。
    /// リトライはシーンの再読み込みなので、次の再生は Play On Awake で頭から始まる。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class BgmPlayer : MonoBehaviour
    {
        [SerializeField] private AudioSource source;
        [Tooltip("ゲームオーバーで音を引くまでの秒数")]
        [SerializeField, Min(0f)] private float fadeOutSeconds = 0.8f;
        [Tooltip("通常時の音量")]
        [SerializeField, Range(0f, 1f)] private float volume = 0.5f;

        private GameState lastState = GameState.Title;
        private bool fading;
        private float fadeTimer;
        private float fadeFrom;

        private void Awake()
        {
            if (source == null) source = GetComponent<AudioSource>();
            if (source == null) return;

            source.loop = true;      // 途中で切れると無音になるので、設定漏れを防ぐ
            source.spatialBlend = 0f; // 2D。プレイヤーの向きで音量が変わらないように
            source.volume = volume;
        }

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.State != lastState)
            {
                lastState = gm.State;
                if (lastState == GameState.GameOver) BeginFadeOut();
            }

            AdvanceFade();
        }

        private void BeginFadeOut()
        {
            if (source == null || !source.isPlaying) return;

            fading = true;
            fadeTimer = 0f;
            fadeFrom = source.volume;
        }

        private void AdvanceFade()
        {
            if (!fading || source == null) return;

            // ポーズ中は timeScale が 0 になるので、実時間で進める
            fadeTimer += Time.unscaledDeltaTime;
            float t = fadeOutSeconds > 0f ? Mathf.Clamp01(fadeTimer / fadeOutSeconds) : 1f;
            source.volume = Mathf.Lerp(fadeFrom, 0f, t);

            if (t < 1f) return;

            source.Stop();
            fading = false;
        }
    }
}
