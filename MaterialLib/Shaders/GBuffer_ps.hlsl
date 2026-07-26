// GBuffer_ps.hlsl — G-Buffer pixel shader.
// Outputs view-space normal (RGB) and linear view-space depth (A)
// to a HalfVector4 render target.  No encoding needed — FP16 preserves sign.

struct PS_INPUT
{
    float4 PositionCS : SV_POSITION;
    float3 ViewNormal : TEXCOORD0;
    float  ViewDepth  : TEXCOORD1;
};

float4 PSMain(PS_INPUT input) : SV_TARGET0
{
    float3 N = normalize(input.ViewNormal);
    return float4(N, input.ViewDepth);
}
