// GBuffer_vs.hlsl — Deferred G-Buffer vertex shader.
//
// Vertex declaration: Position + Normal + TexCoord (PNT, matching
// FNA's VertexPositionNormalTexture format).
//
// Per C1-C5 conventions:
//   C1: VS_INPUT field order = Position, Normal, TexCoord (sequential locations)
//   C2: Only attributes the PNT layout provides — no superset
//   C4: Layout matches FNA's VertexPositionNormalTexture IVertexType
//   C5: float3 Position / float3 Normal / float2 TexCoord match Vector3/Vector2

float4x4 WorldViewProj       : register(c0);
float4x4 World               : register(c4);
float4x4 WorldInverseTranspose : register(c8);

struct VS_INPUT
{
    float3 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 PositionCS     : SV_POSITION;
    float3 WorldPos       : TEXCOORD0;
    float3 WorldNormal    : TEXCOORD1;
    float2 TexCoord       : TEXCOORD2;
    float  ViewDepth      : TEXCOORD3; // clip.w = -viewSpace.z (linear depth)
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;

    float4 localPos  = float4(input.Position, 1.0);

    output.PositionCS  = mul(localPos, WorldViewProj);
    output.WorldPos    = mul(localPos, World).xyz;
    output.WorldNormal = normalize(mul(float4(input.Normal, 0.0), WorldInverseTranspose).xyz);
    output.TexCoord    = input.TexCoord;
    output.ViewDepth   = output.PositionCS.w; // clip.w in VS = -viewSpace.z

    return output;
}
