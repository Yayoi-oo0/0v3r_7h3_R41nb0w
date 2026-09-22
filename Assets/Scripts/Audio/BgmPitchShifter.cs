using UnityEngine;
using OverTheRainbow.Stage;

namespace OverTheRainbow.Audio
{
    /// <summary>
    /// 闇が迫ってきたら BGM のピッチだけを下げ、気持ち悪さを出す。
    ///
    /// AudioSource.pitch は再生速度ごと変えてしまう（曲が早送り／スロー再生になる）ので使えない。
    /// ここでは音声出力を捕まえて、遅延線を二本ずらしながら読むやり方でピッチシフトする。
    /// 読み出し位置を書き込みより遅く進めると音は下がり、位置が一周する手前で
    /// もう一本へ等電力クロスフェードして繋ぎ目を隠す。速度（曲の進行）は変わらない。
    ///
    /// 揺らぎや滲みが多少出るが、狙いが「気持ち悪さ」なのでむしろ具合がいい。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class BgmPitchShifter : MonoBehaviour
    {
        [Header("効かせ方")]
        [Tooltip("未設定ならシーンから探す")]
        [SerializeField] private DarknessChaser darkness;
        [Tooltip("闇がここまで近づいたら効き始める m")]
        [SerializeField, Min(0f)] private float triggerDistance = 3f;
        [Tooltip("ここまで近づいたら効果が最大 m")]
        [SerializeField, Min(0f)] private float fullEffectDistance = 0.5f;
        [Tooltip("最大時のピッチ倍率。0.94 で半音弱ぶん低い")]
        [SerializeField, Range(0.5f, 1f)] private float minPitchRatio = 0.94f;
        [Tooltip("効き始め／戻りの滑らかさ（秒）")]
        [SerializeField, Min(0.01f)] private float responseSeconds = 0.35f;

        /// <summary>繋ぎ目を隠す窓の長さ（サンプル）。長いほど滲み、短いほど揺れる。</summary>
        private const int Window = 4096;
        private const int BufferSize = Window * 2;

        // 音声スレッドとやり取りする値。読み書きが1つずつなので volatile で足りる
        private volatile float targetRatio = 1f;
        private volatile float targetWet;

        private float[][] buffers;
        private int writeIndex;
        private float delay;
        private float ratio = 1f;
        private float wet;
        private float smoothing = 0.001f;

        private void Awake()
        {
            AllocateBuffers(2);

            int sampleRate = AudioSettings.outputSampleRate;
            if (sampleRate <= 0) sampleRate = 48000;
            // 1 サンプルあたりの寄せ幅。responseSeconds でおおよそ目標に届く
            smoothing = 1f - Mathf.Exp(-1f / (responseSeconds * sampleRate));
        }

        private void Start()
        {
            if (darkness == null) darkness = FindObjectOfType<DarknessChaser>();
        }

        private void Update()
        {
            if (darkness == null) return;

            // 3m から効き始め、0.5m で最大。範囲外は素通し
            float t = 1f - Mathf.InverseLerp(fullEffectDistance, triggerDistance, darkness.DistanceToPlayer);
            t = Mathf.Clamp01(t);

            targetRatio = Mathf.Lerp(1f, minPitchRatio, t);
            // 原音と混ざると唸りが出るので、効き始めたら早めに全部を差し替える
            targetWet = Mathf.Clamp01(t * 3f);
        }

        /// <summary>
        /// 音声スレッドから呼ばれる。Unity の API を触らないこと、確保をしないことが条件。
        /// </summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (buffers == null || buffers.Length < channels) AllocateBuffers(channels);

            float ratioTarget = targetRatio;
            float wetTarget = targetWet;

            for (int i = 0; i < data.Length; i += channels)
            {
                ratio += (ratioTarget - ratio) * smoothing;
                wet += (wetTarget - wet) * smoothing;

                // 書き込みより遅く読み進めるほど音が下がる
                delay += 1f - ratio;
                if (delay >= Window) delay -= Window;
                else if (delay < 0f) delay += Window;

                // 一周する手前で音量が 0 になる窓。二本合わせて常に等電力
                float phase = delay / Window;
                float gainA = Mathf.Sin(Mathf.PI * phase);
                float gainB = Mathf.Cos(Mathf.PI * phase);
                float delayB = delay + Window * 0.5f;
                if (delayB >= Window) delayB -= Window;

                for (int channel = 0; channel < channels; channel++)
                {
                    float[] buffer = buffers[channel];
                    float dry = data[i + channel];
                    buffer[writeIndex] = dry;

                    if (wet > 0.0001f)
                    {
                        float shifted = ReadTap(buffer, delay) * gainA + ReadTap(buffer, delayB) * gainB;
                        data[i + channel] = Mathf.Lerp(dry, shifted, wet);
                    }
                }

                writeIndex++;
                if (writeIndex >= BufferSize) writeIndex = 0;
            }
        }

        /// <summary>書き込み位置から d サンプル前を、線形補間で読む。</summary>
        private float ReadTap(float[] buffer, float d)
        {
            float position = writeIndex - d;
            if (position < 0f) position += BufferSize;

            int index = (int)position;
            float fraction = position - index;
            int next = index + 1;
            if (next >= BufferSize) next = 0;

            return buffer[index] * (1f - fraction) + buffer[next] * fraction;
        }

        private void AllocateBuffers(int channels)
        {
            buffers = new float[Mathf.Max(2, channels)][];
            for (int i = 0; i < buffers.Length; i++) buffers[i] = new float[BufferSize];
            writeIndex = 0;
        }
    }
}
