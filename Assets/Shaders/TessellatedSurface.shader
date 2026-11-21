// A lit URP surface whose geometry is tessellated near the camera and pushed out by a height map: real relief on the
// basement's brick walls and floor instead of flat boxes with a normal map. Optional second layer (rust over concrete)
// height-blended by a tiling blend map that also carries grime and where water stands; below the local water level the
// surface goes flat and glassy (and the geometry is levelled to it). Damp can rise up from the floor.
// Meshes: vertex colour R weights the displacement (0 at a face's border so neighbouring slabs keep meeting) when
// "Use Vertex Weight" is on; they want triangles no bigger than ~0.5 m so the tessellation has something to work on.
Shader "BodycamHorror/Tessellated Surface"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor][HDR] _BaseColor("Colour (above 1 brightens a dark scan)", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1
        [NoScaleOffset] _MaskMap("Mask (R metal, G occlusion, A smoothness)", 2D) = "white" {}
        _Smoothness("Smoothness", Range(0, 1)) = 0.8
        _OcclusionStrength("Occlusion", Range(0, 1)) = 1
        [NoScaleOffset] _HeightMap("Height", 2D) = "gray" {}

        [Header(Displacement)]
        _Displacement("Displacement (m)", Float) = 0.03
        _HeightBias("Height Bias (0..1 stays put)", Range(0, 1)) = 0
        [Toggle] _UseVertexWeight("Use Vertex Weight (colour R)", Float) = 0
        _TessMax("Max Tessellation", Range(1, 64)) = 32
        _TessEdgeNear("Edge Length Near (m)", Float) = 0.03
        _TessEdgeFar("Edge Length Far (m)", Float) = 0.6
        _TessNear("Near Distance (m)", Float) = 2
        _TessFar("Far Distance (m)", Float) = 14

        [Header(Second Layer)]
        [Toggle(_LAYER2)] _Layer2("Second Layer", Float) = 0
        [NoScaleOffset] _BaseMap2("Albedo 2", 2D) = "white" {}
        [HDR] _BaseColor2("Colour 2", Color) = (1, 1, 1, 1)
        [NoScaleOffset][Normal] _BumpMap2("Normal Map 2", 2D) = "bump" {}
        _BumpScale2("Normal Scale 2", Float) = 1
        [NoScaleOffset] _MaskMap2("Mask 2", 2D) = "white" {}
        _Smoothness2("Smoothness 2", Range(0, 1)) = 0.8
        [NoScaleOffset] _HeightMap2("Height 2", 2D) = "gray" {}
        _Layer2Scale("Layer 2 Tiling (per UV unit)", Float) = 1
        [NoScaleOffset] _BlendMap("Blend Map (R layer 2, G grime, B flooding)", 2D) = "black" {}
        _BlendScale("Blend Map Tiling (per UV unit)", Float) = 0.1
        _BlendContrast("Blend Contrast", Float) = 4
        _Layer2Amount("Layer 2 Amount", Range(0, 2)) = 1
        _GrimeColor("Grime Colour", Color) = (0.35, 0.3, 0.25, 1)
        _GrimeAmount("Grime Amount", Range(0, 2)) = 1
        _WaterColor("Water Tint", Color) = (0.35, 0.36, 0.33, 1)
        _WaterLevel("Water Level (height 0..1)", Range(0, 1)) = 0
        _FloodDepth("Flood Depth (blend B adds)", Range(0, 1)) = 0.6
        _WaterSmoothness("Water Smoothness", Range(0, 1)) = 0.95

        [Header(Anti Tiling)]
        [Toggle(_ANTITILE)] _AntiTile("Break Up Tiling", Float) = 0
        _AntiTileScale("Patch Frequency (per UV unit)", Float) = 0.25
        _MacroVariation("Tone Variation", Range(0, 0.5)) = 0.2

        [Header(Damp)]
        _DampHeight("Damp Height (m, 0 off)", Float) = 0
        _DampDarken("Damp Darkening", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.6
            #pragma require tessellation tessHW
            #pragma vertex TessVertex
            #pragma hull TessHull
            #pragma domain TessDomain
            #pragma fragment Frag

            #pragma shader_feature_local _LAYER2
            #pragma shader_feature_local _ANTITILE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "TessellatedSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"

            struct VaryingsLit
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };
            #define TESS_OUTPUT VaryingsLit

            VaryingsLit FinishVertex(TessPoint v)
            {
                VaryingsLit o;
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = nrm.normalWS;
                o.tangentWS = float4(nrm.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }
            #include "TessellatedSurfaceDomain.hlsl"

            void Frag(VaryingsLit i, out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                SurfaceSample s = EvaluateSurface(i.uv, i.positionWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                float3 normalWS = normalize(i.normalWS);
                float3 bitangent = i.tangentWS.w * cross(normalWS, i.tangentWS.xyz);
                half3x3 tbn = half3x3(i.tangentWS.xyz, bitangent, normalWS);
                inputData.tangentToWorld = tbn;
                inputData.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(s.normalTS, tbn));
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(i.positionWS, 1.0), i.fogFactor);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = s.albedo;
                surface.metallic = s.metallic;
                surface.smoothness = s.smoothness;
                surface.normalTS = s.normalTS;
                surface.occlusion = s.occlusion;
                surface.alpha = 1.0h;
            #if defined(_DBUFFER)
                ApplyDecalToSurfaceData(i.positionCS, surface, inputData);
            #endif
                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                outColor = half4(color.rgb, 1.0h);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.6
            #pragma require tessellation tessHW
            #pragma vertex TessVertex
            #pragma hull TessHull
            #pragma domain TessDomain
            #pragma fragment FragShadow
            #pragma shader_feature_local _LAYER2
            #pragma shader_feature_local _ANTITILE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "TessellatedSurface.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct VaryingsShadow { float4 positionCS : SV_POSITION; };
            #define TESS_OUTPUT VaryingsShadow

            VaryingsShadow FinishVertex(TessPoint v)
            {
                VaryingsShadow o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                o.positionCS = ApplyShadowClamping(positionCS);
                return o;
            }
            #include "TessellatedSurfaceDomain.hlsl"

            half4 FragShadow(VaryingsShadow i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.6
            #pragma require tessellation tessHW
            #pragma vertex TessVertex
            #pragma hull TessHull
            #pragma domain TessDomain
            #pragma fragment FragDepth
            #pragma shader_feature_local _LAYER2
            #pragma shader_feature_local _ANTITILE

            #include "TessellatedSurface.hlsl"

            struct VaryingsDepth { float4 positionCS : SV_POSITION; };
            #define TESS_OUTPUT VaryingsDepth

            VaryingsDepth FinishVertex(TessPoint v)
            {
                VaryingsDepth o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            #include "TessellatedSurfaceDomain.hlsl"

            half FragDepth(VaryingsDepth i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.6
            #pragma require tessellation tessHW
            #pragma vertex TessVertex
            #pragma hull TessHull
            #pragma domain TessDomain
            #pragma fragment FragDepthNormals
            #pragma shader_feature_local _LAYER2
            #pragma shader_feature_local _ANTITILE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "TessellatedSurface.hlsl"

            struct VaryingsNormals
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };
            #define TESS_OUTPUT VaryingsNormals

            VaryingsNormals FinishVertex(TessPoint v)
            {
                VaryingsNormals o;
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = nrm.normalWS;
                o.tangentWS = float4(nrm.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = v.uv;
                return o;
            }
            #include "TessellatedSurfaceDomain.hlsl"

            void FragDepthNormals(VaryingsNormals i, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                SurfaceSample s = EvaluateSurface(i.uv, i.positionWS);
                float3 normalWS = normalize(i.normalWS);
                float3 bitangent = i.tangentWS.w * cross(normalWS, i.tangentWS.xyz);
                normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(s.normalTS, half3x3(i.tangentWS.xyz, bitangent, normalWS)));
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(normalWS);
                outNormalWS = half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
            #else
                outNormalWS = half4(normalWS, 0.0);
            #endif
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
