// A flashlight's beam as a soft cone of light in the air, for the item screen (UI.ItemShowcase): an open cone mesh,
// additive, both sides. Bright down its middle and fading to its edges (the more the surface is seen face-on, the more
// air the eye looks through), fading along its length by the vertex alpha. No depth write, no lighting.
Shader "BodycamHorror/Showcase Beam"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.94, 0.82, 1)
        _Strength ("Strength", Float) = 0.2
        _EdgeSoftness ("Edge softness", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Strength;
                half _EdgeSoftness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; half4 color : COLOR; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(positionWS);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half facing = abs(dot(normalize(i.normalWS), normalize(i.viewWS)));
                half soft = pow(facing, _EdgeSoftness);
                return half4(_Color.rgb * i.color.rgb * (i.color.a * soft * _Strength), 1);
            }
            ENDHLSL
        }
    }
}
