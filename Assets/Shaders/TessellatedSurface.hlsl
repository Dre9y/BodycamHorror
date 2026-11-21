// Shared code for BodycamHorror/Tessellated Surface (see TessellatedSurface.shader).
// Each pass includes this, defines TESS_OUTPUT (its varyings) and TESS_OUTPUT FinishVertex(TessPoint v), then
// includes TessellatedSurfaceDomain.hlsl for the domain stage that displaces and hands over to FinishVertex.
#ifndef BODYCAM_TESSELLATED_SURFACE_INCLUDED
#define BODYCAM_TESSELLATED_SURFACE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_BaseMap);
// one inline sampler for every layer map: the domain stage reads only the heights, so per-texture samplers get stripped
SAMPLER(sampler_trilinear_repeat_aniso8);
#define sampler_BaseMap sampler_trilinear_repeat_aniso8
TEXTURE2D(_BumpMap);
TEXTURE2D(_MaskMap);
TEXTURE2D(_HeightMap);
TEXTURE2D(_BaseMap2);
TEXTURE2D(_BumpMap2);
TEXTURE2D(_MaskMap2);
TEXTURE2D(_HeightMap2);
TEXTURE2D(_BlendMap);
#define sampler_BlendMap sampler_trilinear_repeat_aniso8

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _BumpScale;
    half _Smoothness;
    half _OcclusionStrength;
    float _Displacement;
    float _HeightBias;
    float _UseVertexWeight;
    float _TessMax;
    float _TessEdgeNear;
    float _TessEdgeFar;
    float _TessNear;
    float _TessFar;
    half4 _BaseColor2;
    float _Layer2Scale;
    half _BumpScale2;
    half _Smoothness2;
    float _BlendScale;
    half _BlendContrast;
    half _Layer2Amount;
    half4 _GrimeColor;
    half _GrimeAmount;
    half4 _WaterColor;
    float _WaterLevel;
    float _FloodDepth;
    half _WaterSmoothness;
    float _DampHeight;
    half _DampDarken;
    float _AntiTileScale;
    half _MacroVariation;
CBUFFER_END

// ------------------------------------------------------------------ the surface

struct SurfaceSample
{
    half3 albedo;
    half3 normalTS;
    half metallic;
    half smoothness;
    half occlusion;
    float height;
};

float2 LayerAUV(float2 uv) { return uv * _BaseMap_ST.xy + _BaseMap_ST.zw; }

// how much of layer 2 shows: the blend map's red, sharpened, pushed by which layer stands higher (height blending)
float Layer2Weight(float hA, float hB, float r)
{
    return saturate((r * _Layer2Amount - 0.5) * _BlendContrast + 0.5 + (hB - hA) * 0.6);
}

// ------------------------------------------------------------------ breaking up the tiling
// A smooth noise cuts the surface into patches, each reading the texture at its own random offset; neighbouring
// patches hand over to each other with a height-aware blend (the higher brick wins, so the seam stays crisp instead of
// a smeary cross-fade). Offsets only, no rotation: tangent-space normals stay valid. Every map, and the displacement in
// the domain stage, goes through the same patches, so relief and picture agree.

float ATHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float2 ATOffset(float n) { return frac(sin(float2(n * 127.1 + 1.7, n * 311.7 + 9.3)) * 43758.5453); }

float ATNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = ATHash(i), b = ATHash(i + float2(1, 0)), c = ATHash(i + float2(0, 1)), d = ATHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

struct ATCoords { float2 uv0; float2 uv1; float t; };

ATCoords AntiTile(float2 uv, float seed)
{
    ATCoords c;
#if defined(_ANTITILE)
    float k = ATNoise(uv * _AntiTileScale + seed * 7.31) * 6.0;
    float i = floor(k), f = frac(k);
    c.uv0 = uv + ATOffset(i + seed * 13.0);
    c.uv1 = uv + ATOffset(i + 1.0 + seed * 13.0);
    c.t = smoothstep(0.2, 0.8, f);
#else
    c.uv0 = uv; c.uv1 = uv; c.t = 0.0;
#endif
    return c;
}

// the hand-over weight: 0 and 1 at the patch ends (so patches join without a step), height-sharpened in between
float ATWeight(ATCoords c, float h0, float h1)
{
    float w = saturate(c.t + (h1 - h0) * 3.0 * c.t * (1.0 - c.t));
    return w * w * (3.0 - 2.0 * w);
}

#define AT_GRAD(tex, c, dx, dy, w) lerp(SAMPLE_TEXTURE2D_GRAD(tex, sampler_BaseMap, c.uv0, dx, dy), SAMPLE_TEXTURE2D_GRAD(tex, sampler_BaseMap, c.uv1, dx, dy), w)
#define AT_LOD(tex, c, w) lerp(SAMPLE_TEXTURE2D_LOD(tex, sampler_BaseMap, c.uv0, 0), SAMPLE_TEXTURE2D_LOD(tex, sampler_BaseMap, c.uv1, 0), w)

float HeightLOD(TEXTURE2D_PARAM(tex, smp), float2 uv, float seed)
{
    ATCoords c = AntiTile(uv, seed);
    float h0 = SAMPLE_TEXTURE2D_LOD(tex, smp, c.uv0, 0).r;
    float h1 = SAMPLE_TEXTURE2D_LOD(tex, smp, c.uv1, 0).r;
    return lerp(h0, h1, ATWeight(c, h0, h1));
}

// the height the geometry is pushed to (0..1): the layers' blended height, raised to the water's level where flooded
float DisplacedHeight(float2 uv)
{
    float h = HeightLOD(TEXTURE2D_ARGS(_HeightMap, sampler_BaseMap), LayerAUV(uv), 0.0);
#if defined(_LAYER2)
    half4 blend = SAMPLE_TEXTURE2D_LOD(_BlendMap, sampler_BlendMap, uv * _BlendScale, 0);
    float h2 = HeightLOD(TEXTURE2D_ARGS(_HeightMap2, sampler_BaseMap), uv * _Layer2Scale, 1.0);
    h = lerp(h, h2, Layer2Weight(h, h2, blend.r));
    h = max(h, _WaterLevel + blend.b * _FloodDepth);
#endif
    return h;
}

SurfaceSample EvaluateSurface(float2 uv, float3 positionWS)
{
    float2 uvA = LayerAUV(uv);
    float2 dxA = ddx(uvA), dyA = ddy(uvA);
    ATCoords ca = AntiTile(uvA, 0.0);
    float ha0 = SAMPLE_TEXTURE2D_GRAD(_HeightMap, sampler_BaseMap, ca.uv0, dxA, dyA).r;
    float ha1 = SAMPLE_TEXTURE2D_GRAD(_HeightMap, sampler_BaseMap, ca.uv1, dxA, dyA).r;
    float wa = ATWeight(ca, ha0, ha1);
    half4 albedo = AT_GRAD(_BaseMap, ca, dxA, dyA, wa) * _BaseColor;
    half4 mask = AT_GRAD(_MaskMap, ca, dxA, dyA, wa);    // R metal, G occlusion, A smoothness
    SurfaceSample s;
    s.albedo = albedo.rgb;
    s.normalTS = UnpackNormalScale(AT_GRAD(_BumpMap, ca, dxA, dyA, wa), _BumpScale);
    s.metallic = mask.r;
    s.smoothness = mask.a * _Smoothness;
    s.occlusion = lerp(1.0h, mask.g, _OcclusionStrength);
    s.height = lerp(ha0, ha1, wa);

#if defined(_ANTITILE)
    // broad, slow variation in tone and hue over metres: stains, fading, odd batches of brick
    float macro = ATNoise(uvA * 0.23 + 31.7) * 0.65 + ATNoise(uvA * 0.071 + 5.3) * 0.35;
    float hue = ATNoise(uvA * 0.17 + 91.1);
    s.albedo *= lerp(1.0 - _MacroVariation, 1.0 + _MacroVariation * 0.5, macro);
    s.albedo *= lerp(half3(1.04, 0.98, 0.92), half3(0.94, 0.98, 1.04), hue * _MacroVariation * 2.0);
#endif

#if defined(_LAYER2)
    float2 uvB = uv * _Layer2Scale;
    float2 dxB = ddx(uvB), dyB = ddy(uvB);
    ATCoords cb = AntiTile(uvB, 1.0);
    float hb0 = SAMPLE_TEXTURE2D_GRAD(_HeightMap2, sampler_BaseMap, cb.uv0, dxB, dyB).r;
    float hb1 = SAMPLE_TEXTURE2D_GRAD(_HeightMap2, sampler_BaseMap, cb.uv1, dxB, dyB).r;
    float wb = ATWeight(cb, hb0, hb1);
    half4 blend = SAMPLE_TEXTURE2D(_BlendMap, sampler_BlendMap, uv * _BlendScale);   // R layer 2, G grime, B flooding
    half4 albedo2 = AT_GRAD(_BaseMap2, cb, dxB, dyB, wb) * _BaseColor2;
    half4 mask2 = AT_GRAD(_MaskMap2, cb, dxB, dyB, wb);
    half3 normal2 = UnpackNormalScale(AT_GRAD(_BumpMap2, cb, dxB, dyB, wb), _BumpScale2);
    float h2 = lerp(hb0, hb1, wb);
    half k = Layer2Weight(s.height, h2, blend.r);
    s.albedo = lerp(s.albedo, albedo2.rgb, k);
    s.metallic = lerp(s.metallic, mask2.r, k);
    s.smoothness = lerp(s.smoothness, mask2.a * _Smoothness2, k);
    s.occlusion = lerp(s.occlusion, mask2.g, k);
    s.normalTS = normalize(lerp(s.normalTS, normal2, k));
    s.height = lerp(s.height, h2, k);

    // grime: soot and mud darkening whole patches, dulling them
    half grime = saturate(blend.g * _GrimeAmount);
    s.albedo = lerp(s.albedo, s.albedo * _GrimeColor.rgb, grime);
    s.smoothness *= lerp(1.0h, 0.55h, grime);

    // standing water: below the local level a flat glassy film, a damp dark rim round it
    float level = _WaterLevel + blend.b * _FloodDepth;
    half damp = saturate((level + 0.12 - s.height) * 6.0);
    half water = saturate((level - s.height) * 50.0);
    s.albedo *= lerp(1.0h, 0.72h, damp);
    s.smoothness = lerp(s.smoothness, 0.8h, damp * 0.6h);
    // murky water: the floor seen through it, tinted towards the water's colour (not multiplied to black)
    s.albedo = lerp(s.albedo, lerp(s.albedo, _WaterColor.rgb, 0.45h), water);
    s.smoothness = lerp(s.smoothness, _WaterSmoothness, water);
    s.metallic = lerp(s.metallic, 0.0h, water);
    s.occlusion = lerp(s.occlusion, 1.0h, water);
    s.normalTS = normalize(lerp(s.normalTS, half3(0.0h, 0.0h, 1.0h), water));
#endif

    // damp rising up a wall from the floor
    if (_DampHeight > 0.001)
    {
        half rise = saturate(1.0 - positionWS.y / _DampHeight);
        rise *= rise;
        s.albedo *= lerp(1.0h, 1.0h - _DampDarken, rise);
        s.smoothness = lerp(s.smoothness, max(s.smoothness, 0.2h), rise);   // damp is dark, not glossy: brick soaks it up
    }
    return s;
}

// ------------------------------------------------------------------ tessellation

struct TessAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float4 color : COLOR;
};

struct TessPoint
{
    float4 positionOS : INTERNALTESSPOS;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float4 color : COLOR;
};

struct TessFactors
{
    float edge[3] : SV_TessFactor;
    float inside : SV_InsideTessFactor;
};

TessPoint TessVertex(TessAttributes v)
{
    TessPoint o;
    o.positionOS = v.positionOS;
    o.normalOS = v.normalOS;
    o.tangentOS = v.tangentOS;
    o.uv = v.uv;
    o.color = v.color;
    return o;
}

// edges are cut into pieces about _TessEdgeNear long close up, growing to _TessEdgeFar at _TessFar and beyond
float EdgeFactor(float3 a, float3 b)
{
    float d = distance((a + b) * 0.5, GetCameraPositionWS());
    float t = saturate((d - _TessNear) / max(_TessFar - _TessNear, 0.01));
    float target = lerp(_TessEdgeNear, _TessEdgeFar, t);
    return clamp(distance(a, b) / target, 1.0, _TessMax);
}

TessFactors PatchConstants(InputPatch<TessPoint, 3> p)
{
    float3 w0 = TransformObjectToWorld(p[0].positionOS.xyz);
    float3 w1 = TransformObjectToWorld(p[1].positionOS.xyz);
    float3 w2 = TransformObjectToWorld(p[2].positionOS.xyz);
    TessFactors f;
    f.edge[0] = EdgeFactor(w1, w2);
    f.edge[1] = EdgeFactor(w2, w0);
    f.edge[2] = EdgeFactor(w0, w1);
    f.inside = (f.edge[0] + f.edge[1] + f.edge[2]) / 3.0;
    return f;
}

[domain("tri")]
[partitioning("fractional_odd")]
[outputtopology("triangle_cw")]
[patchconstantfunc("PatchConstants")]
[outputcontrolpoints(3)]
TessPoint TessHull(InputPatch<TessPoint, 3> p, uint id : SV_OutputControlPointID)
{
    return p[id];
}

#endif
