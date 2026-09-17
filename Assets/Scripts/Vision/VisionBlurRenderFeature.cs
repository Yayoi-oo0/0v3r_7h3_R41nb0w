using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OverTheRainbow.Vision
{
    /// <summary>
    /// 画面全体を一様にぼかす（企画書 4.1 / 6.5）。濃さは <see cref="Intensity"/> で VisionBlurController が動かす。
    ///
    /// URP の Depth of Field は使えない。Bokeh はぼけ半径を 14px（BokehDepthOfField の kRadiusInPixels）で、
    /// CoC を 1.0 で飽和させるので、焦点距離をいくら伸ばしてもそこで頭打ちになる。
    /// Gaussian は最大半径 2px とさらに弱い。そこで縮小 + 反復ぼかしで半径の上限をなくす。
    /// </summary>
    [DisallowMultipleRendererFeature("Vision Blur")]
    public class VisionBlurRenderFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("何段階 1/2 に縮めてからぼかすか。大きいほど強く・軽くなるが、粗くなる")]
            [Range(1, 4)] public int downSample = 2;
            [Tooltip("ぼかしの反復回数。増やすほど半径が伸びる")]
            [Range(1, 8)] public int iterations = 4;
            [Tooltip("1 回あたりのサンプル間隔（縮小後のテクセル）。増やすほど強いが、離しすぎると縞が出る")]
            [Range(0.5f, 4f)] public float spread = 1.5f;
        }

        /// <summary>0 = クリア, 1 = 最大ぼけ。VisionBlurController が毎フレーム書き込む。</summary>
        public static float Intensity { get; set; }

        [SerializeField] private Settings settings = new Settings();
        [SerializeField, HideInInspector] private Shader shader;

        private Material material;
        private VisionBlurPass pass;

        // シーンを抜けても static が残るので、再生開始時に必ずクリアへ戻す
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetIntensity() => Intensity = 0f;

        public override void Create()
        {
            if (shader == null) shader = Shader.Find("Hidden/OverTheRainbow/VisionBlur");
            if (shader == null) return;

            material = CoreUtils.CreateEngineMaterial(shader);
            pass = new VisionBlurPass(settings, material)
            {
                // ビネットなどのポストプロセスより前。ぼかしたあとの絵にビネットが乗る
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null) return;
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            if (Intensity <= 0f) return;

            renderer.EnqueuePass(pass);
        }

        // URP 13 以降、カメラのカラーターゲットはこのタイミングでしか取れない
        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (pass == null) return;
            if (renderingData.cameraData.cameraType != CameraType.Game) return;

            pass.SetTarget(renderer.cameraColorTargetHandle);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            pass = null;
            CoreUtils.Destroy(material);
            material = null;
        }

        private class VisionBlurPass : ScriptableRenderPass
        {
            private const int PassBlur = 0;
            private const int PassComposite = 1;
            private static readonly int ParamsId = Shader.PropertyToID("_VisionBlurParams");

            private readonly Settings settings;
            private readonly Material material;
            private readonly ProfilingSampler sampler = new ProfilingSampler("Vision Blur");
            private readonly RTHandle[] chain = new RTHandle[4];

            private RTHandle target;
            private RTHandle pong;
            private int levels;

            public VisionBlurPass(Settings settings, Material material)
            {
                this.settings = settings;
                this.material = material;
            }

            public void SetTarget(RTHandle cameraColor) => target = cameraColor;

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;

                levels = Mathf.Clamp(settings.downSample, 1, chain.Length);
                for (int i = 0; i < levels; i++)
                {
                    var levelDesc = desc;
                    levelDesc.width = Mathf.Max(1, desc.width >> (i + 1));
                    levelDesc.height = Mathf.Max(1, desc.height >> (i + 1));
                    RenderingUtils.ReAllocateIfNeeded(ref chain[i], levelDesc, FilterMode.Bilinear,
                        TextureWrapMode.Clamp, name: "_VisionBlurLevel" + i);

                    if (i == levels - 1)
                    {
                        RenderingUtils.ReAllocateIfNeeded(ref pong, levelDesc, FilterMode.Bilinear,
                            TextureWrapMode.Clamp, name: "_VisionBlurPong");
                    }
                }
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                float amount = Intensity;
                if (material == null || target == null || amount <= 0f) return;

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, sampler))
                {
                    // 一気に縮めると細かい模様がちらつくので 1/2 ずつ落とす
                    RTHandle source = target;
                    for (int i = 0; i < levels; i++)
                    {
                        SetParams(cmd, chain[i], 0.5f, 0f);
                        Blitter.BlitCameraTexture(cmd, source, chain[i],
                            RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, PassBlur);
                        source = chain[i];
                    }

                    // 間隔を広げながら往復させ、半径を伸ばす
                    RTHandle read = source;
                    RTHandle write = pong;
                    for (int i = 0; i < settings.iterations; i++)
                    {
                        SetParams(cmd, read, settings.spread * (i + 0.5f), 0f);
                        Blitter.BlitCameraTexture(cmd, read, write,
                            RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, PassBlur);
                        (read, write) = (write, read);
                    }

                    SetParams(cmd, read, settings.spread * 0.5f, amount);
                    Blitter.BlitCameraTexture(cmd, read, target,
                        RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, material, PassComposite);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            /// <summary>サンプル間隔は、読み出すテクスチャのテクセルを基準にする。</summary>
            private void SetParams(CommandBuffer cmd, RTHandle reference, float spread, float amount)
            {
                cmd.SetGlobalVector(ParamsId, new Vector4(
                    1f / reference.rt.width, 1f / reference.rt.height, spread, amount));
            }

            public void Dispose()
            {
                for (int i = 0; i < chain.Length; i++)
                {
                    chain[i]?.Release();
                    chain[i] = null;
                }
                pong?.Release();
                pong = null;
            }
        }
    }
}
