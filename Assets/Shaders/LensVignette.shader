// What the lens housing does to the finished picture: a fisheye bend (strength from the Tab debug menu),
// it goes soft towards the edges, as cheap wide
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
            float4 _LensFisheye; // x: fisheye strength (0 off .. 1 full equidistant), y: tan of the camera's half vertical FOV
            float4 _DeathCam;    // the feed breaking up (DeathCam): x: glitch 0..1, y: blackout 0..1, z: stepped time (a new pattern per step), w: static 0..1

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // A digital feed failing: bands of lines torn sideways, macroblocks gone flat, smeared or pulled in
            // from elsewhere, the colour channels slipping apart, whole lines dropping out.
            float3 DeathGlitch(float2 uv, float2 pixel, float aspect)
            {
                float g = _DeathCam.x;
                float t = _DeathCam.z;

                // torn bands
                float bands = lerp(14.0, 70.0, Hash(float2(t, 1.7)));
                float band = floor(uv.y * bands);
                float rb = Hash(float2(band, t));
                if (rb < g * 0.55) uv.x += (Hash(float2(band, t + 3.1)) - 0.5) * g * 0.4;
                // fine sideways jitter of every line
                uv.x += (Hash(float2(floor(uv.y * _ScreenParams.y * 0.5), t + 5.3)) - 0.5) * g * 0.006;

                // macroblocks
                float cx = floor(lerp(10.0, 42.0, Hash(float2(t, 9.2))));
                float2 cells = float2(cx, max(1.0, floor(cx / aspect)));
                float2 cell = floor(uv * cells);
                float rc = Hash(cell + t * 1.37);
                bool corrupt = rc < g * g * 0.5;
                float mode = Hash(cell + t * 2.71);
                if (corrupt)
                {
                    if (mode < 0.35) uv += (float2(Hash(cell + t + 1.0), Hash(cell + t + 2.0)) - 0.5) * 0.35;   // pulled from elsewhere
                    else if (mode < 0.7) uv = (cell + 0.5) / cells;                                             // one flat colour
                    else uv.y = (cell.y + 0.5) / cells.y;                                                       // smeared down the block
                }

                // the channels slip apart
                float split = g * 0.016 * (0.3 + rb);
                float3 c;
                c.r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(split, 0.0)).r;
                c.g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).g;
                c.b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(split, 0.0)).b;

                if (corrupt)
                {
                    // fewer colours in a broken block (stepped in a gamma-like space: the dark scene must not all round to black)
                    c = sqrt(max(c, 0.0));
                    c = floor(c * 7.0 + 0.5) / 7.0;
                    c *= c;
                    float tint = Hash(cell + t * 4.13);
                    if (tint < 0.3) c *= float3(0.35, 1.25, 0.45);        // codec green
                    else if (tint < 0.45) c = c.brg * float3(1.2, 0.4, 1.2); // magenta
                }

                // whole lines dropping out (black or a flat grey)
                float row = floor(uv.y * 270.0);
                float rr = Hash(float2(row, t + 7.7));
                if (rr < g * 0.05) c = Hash(float2(row, t + 8.8)) < 0.6 ? 0.0 : 0.55;

                // static over everything
                float n = frac(sin(dot(pixel + frac(t * 0.6180339) * 113.0, float2(12.9898, 78.233))) * 43758.5453);
                c = lerp(c, n.xxx, saturate(_DeathCam.w));
                return c;
            }

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

            // Fisheye: the rectilinear picture remapped to an equidistant one (angle grows evenly with the
            // distance from the centre), blended in by strength. The corners stay where they are, so nothing
            // black comes in: the middle swells and the edges are squeezed, straight lines bow outwards.
            // p: point on screen, screen half-height = 1; returns the point to sample.
            float2 Fisheye(float2 p, float2 screen)
            {
                float strength = _LensFisheye.x;
                float tanHalf = _LensFisheye.y;
                float r = length(p);
                if (strength <= 0.0001 || r < 1e-5 || tanHalf <= 0.0) return p;
                float corner = length(screen);
                float cornerAngle = atan(corner * tanHalf);
                float angle = r / corner * cornerAngle;          // equidistant: angle in step with radius
                float source = tan(angle) / tanHalf;             // where that angle lies on the flat picture
                return p * (lerp(r, source, strength) / r);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                if (_LensOn < 0.5) return float4(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb, 1.0);

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 screen = float2(aspect, 1.0);
                float2 p = (uv - 0.5) * 2.0 * screen;
                // the glass bends the picture; the frame below stays square to the screen
                uv = Fisheye(p, screen) / (2.0 * screen) + 0.5;
                bool glitch = _DeathCam.x > 0.001 || _DeathCam.w > 0.001;
                float3 color = glitch ? DeathGlitch(uv, input.positionCS.xy, aspect)
                                      : SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // soft towards the edges: a small disc of taps that grows with distance from the centre
                float away = length(p) / length(screen);
                float blur = _LensBlur.x * smoothstep(_LensBlur.y, 1.0, away);
                if (blur > 0.0001 && !glitch)
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
                return float4(color * (1.0 - saturate(dark)) * (1.0 - saturate(_DeathCam.y)), 1.0);
            }
            ENDHLSL
        }
    }
}
