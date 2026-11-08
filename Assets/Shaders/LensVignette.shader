// Darkens the picture towards a rounded frame, like looking through a lens housing. Drawn over the
// finished image by a full-screen renderer feature; LensLook feeds it the numbers.
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
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _LensOn;
            float4 _LensFrame;  // xy: inset from the screen edge, z: corner radius, w: feather
            float4 _LensShade;  // xy: inset from the screen edge, z: feather, w: strength

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
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 screen = float2(aspect, 1.0);
                float2 p = (input.texcoord - 0.5) * 2.0 * screen;

                float frame = Edge(RoundedBox(p, screen - _LensFrame.xy, _LensFrame.z), _LensFrame.w);
                float shade = Edge(RoundedBox(p, screen - _LensShade.xy, _LensFrame.z * 2.0), _LensShade.z) * _LensShade.w;
                float alpha = 1.0 - (1.0 - frame) * (1.0 - shade);

                // the long dark gradient bands without a little noise
                alpha += (InterleavedGradientNoise(input.positionCS.xy, 0.0) - 0.5) / 128.0;
                return float4(0.0, 0.0, 0.0, saturate(alpha) * _LensOn);
            }
            ENDHLSL
        }
    }
}
