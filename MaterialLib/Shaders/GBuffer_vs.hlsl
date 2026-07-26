// GBuffer_vs.hlsl — G-Buffer vertex shader for SSAO.
// Outputs view-space normal and linear depth for the SSAO pass.
// Vertex declaration: Position + Normal + TexCoord (matches TeapotModel.Vertex).

float4x4 WorldViewProj : register(c0);
float4x4 WorldView     : register(c4); // model → view space

struct VS_INPUT
{
    float3 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 PositionCS  : SV_POSITION;
    float3 ViewNormal  : TEXCOORD0;
    float  ViewDepth   : TEXCOORD1;
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;

    float4 worldPos = float4(input.Position, 1.0);
    float4 viewPos  = mul(worldPos, WorldView);
    output.PositionCS = mul(worldPos, WorldViewProj);
    output.ViewNormal = mul(float4(input.Normal, 0.0), WorldView).xyz;
    // Store positive depth (FNA right-handed view space has -Z forward;
    // negate to match the positive-depth convention SSAO expects).
    output.ViewDepth = -viewPos.z;

    return output;
}
