// Mask rendering: outputs solid white for silhouette capture.
// Must match VS_OUTPUT signature from SceneMask_vs.hlsl (C2 compliance).

float4 PSMain_Mask(
    float3 worldNormal   : TEXCOORD0,
    float2 texCoord      : TEXCOORD1,
    float3 worldPosition : TEXCOORD2
) : SV_Target0
{
    return float4(1.0, 1.0, 1.0, 1.0);
}
