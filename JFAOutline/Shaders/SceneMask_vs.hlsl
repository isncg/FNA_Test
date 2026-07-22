// Shared geometry vertex shader for Scene and Mask techniques.
// Input: VertexPositionNormalTexture (PNT) matching C4 convention.
// Output: SV_Position + world normal + texcoord + world position.

float4x4 WorldViewProj : register(c0);

struct VS_INPUT
{
    float3 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position      : SV_Position;
    float3 WorldNormal   : TEXCOORD0;
    float2 TexCoord      : TEXCOORD1;
    float3 WorldPosition : TEXCOORD2;
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;
    output.Position = mul(float4(input.Position, 1.0), WorldViewProj);
    output.WorldNormal = input.Normal;
    output.TexCoord = input.TexCoord;
    output.WorldPosition = input.Position;
    return output;
}
