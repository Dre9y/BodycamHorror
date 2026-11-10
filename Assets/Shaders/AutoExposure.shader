// The body camera's sensor gain: multiplies the frame, before any post-processing, by the exposure and
// white-balance gains AutoExposure worked out from how bright and what colour the scene is.
Shader "Hidden/BodycamHorror/Auto Exposure"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Auto Exposure"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _ExposureGain;   // rgb: gain per channel (exposure x white balance)

            float4 Frag(Varyings input) : SV_Target
            {
                float4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
                return float4(color.rgb * _ExposureGain.rgb, color.a);
            }
            ENDHLSL
        }
    }
}
