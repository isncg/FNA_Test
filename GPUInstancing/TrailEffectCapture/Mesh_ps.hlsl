float4 PSMain(
    float4 svPos    : SV_POSITION,
    float3 worldPos : TEXCOORD0,
    float3 normal   : TEXCOORD1
) : SV_TARGET0
{
    return float4(1, 0, 0, 1);
}
