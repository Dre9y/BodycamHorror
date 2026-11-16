// The lens's focus: sharp up to a distance, softer and softer beyond it. Drawn by a full-screen renderer
// feature before post-processing (so it blurs the scene as rendered, in HDR, before the wide-angle
// distortion and the grade), with the camera depth. LensLook feeds it the numbers.
// Each pixel gathers a disc of taps whose size follows its own distance, so the blur comes in smoothly
// along a wall and a far bright patch melts into its far surroundings with no hard boundary; taps from
// sharper (nearer) surfaces count less, so nothing in focus smears into the soft background.
// The same pass draws the double vision after a blow to the head (ConcussionEffect feeds it).
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
            float4 _LensDouble;  // double vision. x: split (share of screen height), y: tilt (rad), z: ghost weight (0 = off), w: near distance (m)
            float4 _LensDoubleB; // x: split share far away, y: ghost smear, z: share of the split taken by the real image

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

            // Focus blur of the scene around uv.
            float3 Focus(float2 uv, float2 positionCS)
            {
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                if (_LensFocus.w < 0.5) return color;

                float soft = Softness(uv);
                if (soft < 0.002) return color;

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float radius = _LensFocus.z * soft;
                float turn = InterleavedGradientNoise(positionCS, 0.0) * 6.2831853;
                float3 sum = color;
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
                return sum / weight;
            }

            // --- Double vision (ConcussionEffect). The split follows depth like the eyes' parallax: full for
            // things within the near distance, shrinking to a share of it far away.
            float DoubleScale(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                if (raw <= 0.0) return _LensDoubleB.x;
            #else
                if (raw >= 1.0) return _LensDoubleB.x;
            #endif
                float d = LinearEyeDepth(raw, _ZBufferParams);
                return lerp(_LensDoubleB.x, 1.0, saturate(_LensDouble.w / max(d, 0.01)));
            }

            // How far to shift at uv along offset: the larger of this pixel's split and that of whatever sits
            // a full split away, so a near thing's ghost spills out over the far wall beside it.
            float SplitAt(float2 uv, float2 offset)
            {
                return max(DoubleScale(uv), DoubleScale(uv + offset));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float alpha = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).a;
                if (_LensDouble.z < 0.001 || _LensDouble.x < 0.00001)
                    return float4(Focus(uv, input.positionCS.xy), alpha);

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 split = float2(cos(_LensDouble.y) / aspect, sin(_LensDouble.y)) * _LensDouble.x;

                // the real image eases one way, the ghost the other
                float2 mainOffset = -split * _LensDoubleB.z;
                float2 ghostOffset = split * (1.0 - _LensDoubleB.z);
                float3 seen = Focus(uv + mainOffset * SplitAt(uv, mainOffset), input.positionCS.xy);

                float2 g = ghostOffset * SplitAt(uv, ghostOffset);
                float2 smear = g * (0.35 * _LensDoubleB.y);
                float3 ghost = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + g).rgb * 0.4
                             + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + g - smear).rgb * 0.3
                             + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + g + smear).rgb * 0.3;

                return float4(lerp(seen, ghost, _LensDouble.z), alpha);
            }
            ENDHLSL
        }
    }
}
