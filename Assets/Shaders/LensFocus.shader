// The lens's focus: sharp up to a distance, softer and softer beyond it. Drawn by a full-screen renderer
// feature before post-processing (so it blurs the scene as rendered, in HDR, before the wide-angle
// distortion and the grade), with the camera depth. LensLook feeds it the numbers.
// Each pixel gathers a disc of taps whose size follows its own distance, so the blur comes in smoothly
// along a wall and a far bright patch melts into its far surroundings with no hard boundary; taps from
// sharper (nearer) surfaces count less, so nothing in focus smears into the soft background.
Shader "BodycamHorror/Lens Focus"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Lens Focus"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _LensFocus;   // x: sharp up to (m), y: fully soft from (m), z: blur radius at full softness (share of screen height), w: on

            #define TAPS 24

            float Softness(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                if (raw <= 0.0) return 1.0;          // the sky / nothing: as far as it gets
            #else
                if (raw >= 1.0) return 1.0;
            #endif
                float d = LinearEyeDepth(raw, _ZBufferParams);
                return smoothstep(_LensFocus.x, _LensFocus.y, d);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_LensFocus.w < 0.5) return color;

                float soft = Softness(uv);
                if (soft < 0.002) return color;

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float radius = _LensFocus.z * soft;
                float turn = InterleavedGradientNoise(input.positionCS.xy, 0.0) * 6.2831853;
                float3 sum = color.rgb;
                float weight = 1.0;
                for (int i = 0; i < TAPS; i++)
                {
                    float angle = turn + i * 2.3999632;            // golden angle: an even spiral of taps
                    float r = radius * sqrt((i + 0.5) / TAPS);
                    float2 tapUv = uv + float2(cos(angle) * r / aspect, sin(angle) * r);
                    float tapSoft = Softness(tapUv);
                    float w = saturate(tapSoft / soft);            // a sharper (nearer) surface does not smear in
                    sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, tapUv).rgb * w;
                    weight += w;
                }
                return float4(sum / weight, color.a);
            }
            ENDHLSL
        }
    }
}
