// A speck of dust in the air. It has no colour of its own: it shows only where a lamp or the
// flashlight reaches it, as bright as that light is there and shadowed like everything else, and
// it scatters the same in every direction (no facing term), so motes glint in a beam seen from
// any side. Additive, so dust in the dark costs nothing on screen.
Shader "BodycamHorror/Dust Mote"
{
    Properties
    {
        _Brightness ("Brightness", Float) = 1
        _Ceiling ("Brightest a mote can get", Float) = 1.6
        _Reach ("Distance they fade out by (m)", Float) = 7
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "Dust Mote"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Brightness;
                float _Ceiling;
                float _Reach;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 d = input.uv * 2.0 - 1.0;
                float disc = saturate(1.0 - dot(d, d));
                disc *= disc;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                float3 lit = 0.0;
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                #ifdef _LIGHT_LAYERS
                    if ((light.layerMask & 1u) != 0u)   // lights that reach the world, not the hands-only fills
                #endif
                    {
                        lit += light.color * (light.distanceAttenuation * light.shadowAttenuation);
                    }
                LIGHT_LOOP_END

                lit = min(lit * _Brightness, _Ceiling);
                // far ones would all be single bright pixels, ones right at the lens big blobs: both fade away
                float away = length(input.positionWS - _WorldSpaceCameraPos);
                lit *= saturate(1.0 - away / max(_Reach, 0.01)) * smoothstep(0.25, 0.8, away);
                return float4(lit * disc * input.color.rgb * input.color.a, 0.0);
            }
            ENDHLSL
        }
    }
}
