// ShadowMap_vs.hlsl — Depth-only vertex shader for directional shadow map.
// C1: VS_INPUT matches PNT vertex declaration (Position, Normal, TexCoord).

float4x4 WorldViewProj : register(c0);

struct VS_INPUT
{
    float3 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;
    output.Position = mul(float4(input.Position, 1.0), WorldViewProj);
    return output;
}
