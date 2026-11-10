// Haze in the air around a lamp: the light a point or spot light scatters towards the camera on its
// way through dusty air. Drawn as a box around the light's range (LightHaze feeds it the numbers), marched
// along the view ray up to whatever the depth buffer says is in the way, and shadowed by the light's own
// shadow map, so a body under the lamp cuts a dark shaft into the glow.
Shader "BodycamHorror/Light Haze"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Light Haze"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Front   // back faces, so it still draws with the camera inside the box

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #define HAZE_STEPS 24

            float4 _HazePos;      // xyz: light position, w: range
            float4 _HazeDir;      // xyz: spot direction, w: how wispy the air is
            float4 _HazeCone;     // x: cos outer, y: cos inner (both below -1 = point light), z: shadows on, w: source size
            float4 _HazeColor;    // rgb: light colour x intensity x density

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation float lightIndex : TEXCOORD0;
            };

            float HazeHash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1) * 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float HazeNoise(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(HazeHash(i), HazeHash(i + float3(1, 0, 0)), f.x), lerp(HazeHash(i + float3(0, 1, 0)), HazeHash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(HazeHash(i + float3(0, 0, 1)), HazeHash(i + float3(1, 0, 1)), f.x), lerp(HazeHash(i + float3(0, 1, 1)), HazeHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            // slow smoke: thicker and thinner patches drifting through the air, 0..1
            float Wisps(float3 p)
            {
                float3 drift = float3(0.06, -0.035, 0.045) * _Time.y;
                return HazeNoise(p * 1.4 + drift) * 0.65 + HazeNoise(p * 3.3 - drift * 1.8) * 0.35;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                // which of the frame's lights this is, for its shadow map
                float index = -1.0;
            #if defined(_ADDITIONAL_LIGHT_SHADOWS) && !USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
                for (int i = 0; i < min(MAX_VISIBLE_LIGHTS, 128); i++)
                {
                    float3 d = _AdditionalLightsPosition[i].xyz - _HazePos.xyz;
                    if (_AdditionalLightsPosition[i].w > 0.5 && dot(d, d) < 0.0004) { index = i; break; }
                }
            #endif
                output.lightIndex = index;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                float rawDepth = SampleSceneDepth(uv);
            #if !UNITY_REVERSED_Z
                rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
            #endif
                float3 scenePos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 ro = _WorldSpaceCameraPos;
                float3 toScene = scenePos - ro;
                float sceneDist = length(toScene);
                float3 rd = toScene / max(sceneDist, 1e-4);

                // the stretch of the view ray inside the light's range
                float range = _HazePos.w;
                float3 oc = ro - _HazePos.xyz;
                float b = dot(oc, rd);
                float h2 = dot(oc, oc) - b * b;
                float disc = range * range - h2;
                if (disc <= 0.0) return 0.0;
                float s = sqrt(disc);
                float t0 = max(-b - s, 0.0);
                float t1 = min(-b + s, sceneDist);
                if (t1 <= t0) return 0.0;

                // equi-angular steps: bunched up where the ray passes the lamp, which is where 1/d^2 lives
                float h = sqrt(h2 + _HazeCone.w * _HazeCone.w);
                float a0 = atan((t0 + b) / h);
                float a1 = atan((t1 + b) / h);
                float jitter = InterleavedGradientNoise(input.positionCS.xy, (int)(_Time.y * 60.0) % 64);

                float sum = 0.0;
                for (int i = 0; i < HAZE_STEPS; i++)
                {
                    float a = lerp(a0, a1, (i + jitter) / HAZE_STEPS);
                    float t = -b + h * tan(a);
                    float3 p = ro + rd * t;
                    float3 v = p - _HazePos.xyz;
                    float d2 = dot(v, v);
                    float3 dir = v * rsqrt(max(d2, 1e-6));

                    float fade = saturate(1.0 - Sq(d2 / (range * range)));
                    float g = fade * fade * smoothstep(_HazeCone.x, _HazeCone.y, dot(dir, _HazeDir.xyz));
                #if defined(_ADDITIONAL_LIGHT_SHADOWS)
                    if (_HazeCone.z > 0.5 && input.lightIndex >= 0.0 && g > 0.0)
                        g *= AdditionalLightRealtimeShadow((int)input.lightIndex, p, -dir);
                #endif
                    if (_HazeDir.w > 0.0 && g > 0.0) g *= lerp(1.0, saturate(Wisps(p) * 2.2 - 0.35), _HazeDir.w);
                    sum += g;
                }

                float3 haze = _HazeColor.rgb * sum * ((a1 - a0) / (h * HAZE_STEPS));
                return float4(haze, 0.0);
            }
            ENDHLSL
        }
    }
}
