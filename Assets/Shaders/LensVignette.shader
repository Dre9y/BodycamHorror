// What the lens housing does to the finished picture: it goes soft towards the edges, as cheap wide
// glass does, and darkens into a rounded frame. Drawn over the image by a full-screen renderer
// feature that hands it the frame as _BlitTexture (Fetch Color Buffer on); LensLook feeds it the numbers.
Shader "BodycamHorror/Lens Vignette"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Lens Vignette"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _LensOn;
            float4 _LensFrame;  // xy: inset from the screen edge, z: corner radius, w: feather
            float4 _LensShade;  // xy: inset from the screen edge, z: feather, w: strength
            float4 _LensBlur;   // x: blur radius at the very corner (share of screen height), y: where it starts (0 centre .. 1 corner)

            // negative inside the box, positive outside
            float RoundedBox(float2 p, float2 halfSize, float radius)
            {
                radius = min(radius, min(halfSize.x, halfSize.y));
                float2 q = abs(p) - halfSize + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            float Edge(float distance, float feather)
            {
                return smoothstep(-max(feather, 0.001), 0.0, distance);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                if (_LensOn < 0.5) return float4(color, 1.0);

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 screen = float2(aspect, 1.0);
                float2 p = (uv - 0.5) * 2.0 * screen;

                // soft towards the edges: a small disc of taps that grows with distance from the centre
                float away = length(p) / length(screen);
                float blur = _LensBlur.x * smoothstep(_LensBlur.y, 1.0, away);
                if (blur > 0.0001)
                {
                    float turn = InterleavedGradientNoise(input.positionCS.xy, 0.0) * 6.2831853;
                    float3 sum = color;
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = turn + i * 2.3999632;            // golden angle
                        float radius = blur * sqrt((i + 0.5) / 8.0);
                        float2 offset = float2(cos(angle), sin(angle)) * radius;
                        sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(offset.x / aspect, offset.y)).rgb;
                    }
                    color = sum / 9.0;
                }

                float frame = Edge(RoundedBox(p, screen - _LensFrame.xy, _LensFrame.z), _LensFrame.w);
                float shade = Edge(RoundedBox(p, screen - _LensShade.xy, _LensFrame.z * 2.0), _LensShade.z) * _LensShade.w;
                float dark = 1.0 - (1.0 - frame) * (1.0 - shade);

                // the long dark gradient bands without a little noise
                dark += (InterleavedGradientNoise(input.positionCS.xy, 0.0) - 0.5) / 128.0;
                return float4(color * (1.0 - saturate(dark)), 1.0);
            }
            ENDHLSL
        }
    }
}
