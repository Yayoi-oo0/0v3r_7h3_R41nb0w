using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OverTheRainbow.Vision
{
    /// <summary>
    /// 画面全体に VHS 風の劣化（色にじみ・色収差・走査線・ノイズ）を掛ける。
    /// 劣化は最後に乗るべきなので、ブラーや侵食ビネットより後ろで描く。
    /// </summary>
    [DisallowMultipleRendererFeature("Vhs Post Effect")]
    public class VhsRenderFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("色にじみ。色が横方向へ尾を引く")]
            [Range(0f, 1f)] public float bleeding = 0.8f;
            [Tooltip("色収差。輪郭に色の縁が出る")]
            [Range(0f, 1f)] public float fringing = 1f;
            [Tooltip("走査線の濃さ")]
            [Range(0f, 1f)] public float scanline = 0.125f;
            [Tooltip("走査線の本数")]
            [Range(64f, 1024f)] public float scanlineCount = 480f;
            [Tooltip("ブロックノイズ。帯状のずれが上から下へ流れる")]
            [Range(0f, 1f)] public float blockNoise = 0.5f;
            [Tooltip("テープノイズ。チリチリしたざらつき")]
            [Range(0f, 1f)] public float tapeNoise = 0.15f;

            [Header("色味")]
            [Range(0f, 1f)] public float saturation = 0.75f;
            [Range(1f, 2f)] public float contrast = 1.15f;
            [Range(0.5f, 1.5f)] public float gain = 0.95f;
        }

        [SerializeField] private Settings settings = new Settings();
        [SerializeField, HideInInspector] private Shader shader;

        private Material material;
        private VhsPass pass;

        public override void Create()
        {
            if (shader == null) shader = Shader.Find("Hidden/OverTheRainbow/VhsPostEffect");
            if (shader == null) return;

            material = CoreUtils.CreateEngineMaterial(shader);
            pass = new VhsPass(settings, material)
            {
                // ぼかし（-3）→ 日時表示（-2）→ 劣化（-1）→ URP のポストプロセス（±0）の順。
                // ポストプロセスより後ろ（+1 など）に置くと、URP はポストプロセスの結果を画面へ直接書き出してしまい、
                // その後ろで描いたせいで ScreenSpaceOverlay の HUD が丸ごと消えた
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing - 1
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null) return;
            if (renderingData.cameraData.cameraType != CameraType.Game) return;

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

        private class VhsPass : ScriptableRenderPass
        {
            private static readonly int ParamsId = Shader.PropertyToID("_VhsParams");
            private static readonly int NoiseParamsId = Shader.PropertyToID("_VhsNoiseParams");
            private static readonly int GradeId = Shader.PropertyToID("_VhsGrade");

            private readonly Settings settings;
            private readonly Material material;
            private readonly ProfilingSampler sampler = new ProfilingSampler("VHS Post Effect");

            private RTHandle target;
            private RTHandle temp;

            public VhsPass(Settings settings, Material material)
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
                RenderingUtils.ReAllocateIfNeeded(ref temp, desc, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "_VhsTemp");
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (material == null || target == null || temp == null) return;

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, sampler))
                {
                    // ずれ幅は画面に対する割合。解像度が変わってもにじみ方が揃う
                    cmd.SetGlobalVector(ParamsId, new Vector4(
                        0.04f * settings.bleeding,
                        0.0025f * settings.fringing,
                        settings.scanline,
                        settings.scanlineCount));
                    cmd.SetGlobalVector(NoiseParamsId, new Vector4(
                        settings.blockNoise, settings.tapeNoise, Time.time, 0f));
                    cmd.SetGlobalVector(GradeId, new Vector4(
                        settings.saturation, settings.contrast, settings.gain, 0f));

                    // 画面を読みながら同じ画面へは書けないので、一度退避してから戻す
                    Blitter.BlitCameraTexture(cmd, target, temp,
                        RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, 0);
                    Blitter.BlitCameraTexture(cmd, temp, target);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                temp?.Release();
                temp = null;
            }
        }
    }
}
