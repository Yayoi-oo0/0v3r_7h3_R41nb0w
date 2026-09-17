// 視界ぼやけ用のフルスクリーンブラー（企画書 4.1 / 6.5）。
// URP 標準の Depth of Field はぼけ半径が 14px で頭打ちになるため、自前でぼかす。
Shader "Hidden/OverTheRainbow/VisionBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // xy = 参照するテクスチャの 1 テクセル分の UV、z = サンプル間隔（テクセル）、w = 合成時の濃さ
            float4 _VisionBlurParams;
            #define TexelSize _VisionBlurParams.xy
            #define Spread    _VisionBlurParams.z
            #define Amount    _VisionBlurParams.w

            // 4 点をバイリニアで拾う Kawase 風のぼかし。間隔を広げながら繰り返すと半径が伸びる。
            half4 SampleBlurred(float2 uv)
            {
                float2 o = TexelSize * Spread;
                half4 c  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-o.x, -o.y));
                c       += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( o.x, -o.y));
                c       += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-o.x,  o.y));
                c       += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( o.x,  o.y));
                return c * 0.25;
            }
        ENDHLSL

        // 0: 縮小とぼかしの両方に使う
        Pass
        {
            Name "VisionBlurDownsampleAndBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return SampleBlurred(input.texcoord);
            }
            ENDHLSL
        }

        // 1: カメラの絵に濃さぶんだけ重ねる。アルファブレンドなので画面を読み直さずに済む
        Pass
        {
            Name "VisionBlurComposite"
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(SampleBlurred(input.texcoord).rgb, Amount);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
