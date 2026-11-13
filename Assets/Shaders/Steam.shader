// A puff of steam. Like the dust motes it is lit by whatever lamp or flashlight reaches it, so a jet
// in the dark is a dim grey shape and one in a beam is white; unlike them it is a soft, alpha-blended
// cloud that hides what is behind it. A little light of its own (_Ambient) keeps it from vanishing
// where there is no lamp at all: it kills, the player has to be able to make it out.
Shader "BodycamHorror/Steam"
{
    Properties
    {
        _Brightness ("Brightness under a light", Float) = 0.5
        _Ambient ("Brightness with no light", Float) = 0.035
        _Ceiling ("Brightest it can get", Float) = 1.2
        _Density ("Density", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "Steam"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
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
                float _Ambient;
                float _Ceiling;
                float _Density;
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
                // a soft ball, a little lumpy so a row of them does not read as discs
                float2 d = input.uv * 2.0 - 1.0;
                float r2 = dot(d, d);
                float lump = 0.85 + 0.15 * sin(d.x * 5.3 + d.y * 3.1) * sin(d.y * 4.7 - d.x * 2.3);
                float cloud = saturate(1.0 - r2);
                cloud = cloud * cloud * lump;

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

                lit = min(lit * _Brightness + _Ambient, _Ceiling);
                // right at the lens a puff would fill the whole screen: fade it out there
                float away = length(input.positionWS - _WorldSpaceCameraPos);
                float alpha = cloud * input.color.a * _Density * smoothstep(0.1, 0.6, away);
                return float4(lit * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
