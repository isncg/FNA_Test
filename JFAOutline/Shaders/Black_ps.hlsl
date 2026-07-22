// Black pixel shader for depth-only occluder rendering.
// Outputs black but writes depth — used to make non-outline objects
// occlude outline objects in the VisibleMask depth buffer.

float4 PSMain(
    float3 worldNormal   : TEXCOORD0,
    float2 texCoord      : TEXCOORD1,
    float3 worldPosition : TEXCOORD2
) : SV_Target0
{
    return float4(0.0, 0.0, 0.0, 1.0);
}
