// The domain stage of BodycamHorror/Tessellated Surface: included by each pass after its FinishVertex.
[domain("tri")]
TESS_OUTPUT TessDomain(TessFactors f, OutputPatch<TessPoint, 3> p, float3 b : SV_DomainLocation)
{
    TessPoint v;
    v.positionOS = p[0].positionOS * b.x + p[1].positionOS * b.y + p[2].positionOS * b.z;
    v.normalOS = normalize(p[0].normalOS * b.x + p[1].normalOS * b.y + p[2].normalOS * b.z);
    v.tangentOS = p[0].tangentOS * b.x + p[1].tangentOS * b.y + p[2].tangentOS * b.z;
    v.tangentOS.xyz = SafeNormalize(v.tangentOS.xyz);   // meshes without tangents must not turn into NaNs
    v.uv = p[0].uv * b.x + p[1].uv * b.y + p[2].uv * b.z;
    v.color = p[0].color * b.x + p[1].color * b.y + p[2].color * b.z;
    float weight = lerp(1.0, v.color.r, _UseVertexWeight);   // 0 at a face's border: the slabs keep meeting
    v.positionOS.xyz += v.normalOS * (DisplacedHeight(v.uv) - _HeightBias) * _Displacement * weight;
    return FinishVertex(v);
}
