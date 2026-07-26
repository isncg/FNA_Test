// PbrMaterial_vs.hlsl — PBR Material Vertex Shader
// Transforms position/normal to world space, passes texcoords through.
// Conforms to C1–C5: sequential VS_INPUT matching FNA vertex declaration
// (Position, Normal, TextureCoordinate)

float4x4 WorldViewProj : register(c0);
float4x4 World         : register(c4);
float3x3 WorldInverseTranspose : register(c8);

struct VS_INPUT
{
    float3 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 PositionCS   : SV_POSITION;
    float3 WorldPos     : TEXCOORD0;
    float3 WorldNormal  : TEXCOORD1;
    float2 TexCoord     : TEXCOORD2;
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;

    float4 worldPos = mul(float4(input.Position, 1.0), World);
    output.WorldPos = worldPos.xyz;
    output.PositionCS = mul(float4(input.Position, 1.0), WorldViewProj);

    // Transform normal to world space
    output.WorldNormal = normalize(mul(input.Normal, WorldInverseTranspose));

    output.TexCoord = input.TexCoord;

    return output;
}
