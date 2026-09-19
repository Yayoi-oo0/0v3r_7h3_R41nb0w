// VHS 風の画面劣化。色は RGB のままでは扱わず、いったん YIQ（輝度 Y と色差 I/Q）へ移す。
// 実際のビデオ信号が輝度と色差を分けて送り、劣化が水平方向にだけ出るという性質を、そのまま再現するため。
Shader "Hidden/OverTheRainbow/VhsPostEffect"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "VhsPostEffect"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // x = 色にじみ幅(UV), y = 色収差幅(UV), z = 走査線の強さ, w = 走査線の本数
            float4 _VhsParams;
            // x = ブロックノイズの強さ, y = テープノイズの強さ, z = 時間
            float4 _VhsNoiseParams;
            // x = 彩度, y = コントラスト, z = ゲイン
            float4 _VhsGrade;

            #define BleedWidth  _VhsParams.x
            #define FringeWidth _VhsParams.y
            #define Scanline    _VhsParams.z
            #define ScanCount   _VhsParams.w
            #define BlockNoise  _VhsNoiseParams.x
            #define TapeNoise   _VhsNoiseParams.y
            #define NoiseTime   _VhsNoiseParams.z

            // にじみを何点で作るか。間隔ではなく点数を固定して、解像度が変わってもにじみ方を揃える
            #define BLEED_SAMPLES 12

            static const float3x3 RgbToYiq = float3x3(
                0.299,  0.587,  0.114,
                0.596, -0.274, -0.322,
                0.211, -0.523,  0.312);

            static const float3x3 YiqToRgb = float3x3(
                1.0,  0.956,  0.621,
                1.0, -0.272, -0.647,
                1.0, -1.106,  1.703);

            // YIQ は元々ガンマ空間の信号なので、リニアで描いていれば一度戻してから変換する
            float3 ToSignal(float3 rgb)
            {
            #ifndef UNITY_COLORSPACE_GAMMA
                rgb = LinearToSRGB(rgb);
            #endif
                return mul(RgbToYiq, rgb);
            }

            float3 ToDisplay(float3 rgb)
            {
            #ifndef UNITY_COLORSPACE_GAMMA
                rgb = SRGBToLinear(rgb);
            #endif
                return rgb;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            float3 SampleSignal(float2 uv)
            {
                return ToSignal(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // ブロックノイズ: 横長の帯が上から下へ流れ、その行だけ絵が横にずれる
                float band = floor(uv.y * 24.0 + NoiseTime * 6.0);
                float bandNoise = Hash(float2(band, floor(NoiseTime * 12.0)));
                float slip = step(0.93, bandNoise) * (bandNoise - 0.93) * 4.0 * BlockNoise;
                uv.x += slip;

                // 色収差: 色差だけを左右にずらし、輪郭に色の縁を出す
                float3 center = SampleSignal(uv);
                float3 left   = SampleSignal(uv - float2(FringeWidth, 0.0));
                float3 right  = SampleSignal(uv + float2(FringeWidth, 0.0));
                float3 yiq = float3(center.x, left.y, right.z);

                // 色にじみ: 色差を左方向にだけ平均する。走査は左から右へ進むので、後ろに尾を引く
                float3 bleed = 0.0;
                float weight = 0.0;
                for (int i = 0; i < BLEED_SAMPLES; i++)
                {
                    float t = i / (float)(BLEED_SAMPLES - 1);
                    float w = 1.0 - t;
                    bleed += SampleSignal(uv - float2(BleedWidth * t, 0.0)) * w;
                    weight += w;
                }
                yiq.yz = bleed.yz / weight;

                // 走査線: 輝度を横縞で落とす
                float scan = 1.0 - Scanline * (0.5 + 0.5 * sin(uv.y * ScanCount * TWO_PI));
                yiq.x *= scan;

                // テープノイズ: 輝度に細かいざらつきを乗せる
                float grain = Hash(uv * float2(640.0, 480.0) + NoiseTime * 60.0);
                yiq.x += (grain - 0.5) * TapeNoise;

                float3 rgb = saturate(mul(YiqToRgb, yiq));

                // くすんだ色合いに寄せる。彩度を落とし、コントラストを上げ、ゲインで沈める。
                // 信号と同じガンマ空間のまま行う。リニアで掛けると効き方が変わる
                float luma = dot(rgb, float3(0.299, 0.587, 0.114));
                rgb = lerp(luma.xxx, rgb, _VhsGrade.x);
                rgb = saturate((rgb - 0.5) * _VhsGrade.y + 0.5) * _VhsGrade.z;

                return half4(ToDisplay(rgb), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
