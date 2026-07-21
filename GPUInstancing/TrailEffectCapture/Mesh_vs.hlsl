// Procedural wave mesh vertex shader.
// Applies wave deformation and outputs face-color based on model-space normal.
// (Capture moved to CPU side since SDL3 GPU API doesn't support vertex storage writes.)

float4x4 WorldViewProj : register(c0);
float4x4 World         : register(c6);
float Time            : register(c4);
float Amplitude       : register(c5);

struct VS_INPUT
{
    float3 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float3 WorldPos : TEXCOORD0;
    float3 Normal   : TEXCOORD1;
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    float3 p = input.Position;

    // Procedural wave deformation
    float wave = sin(p.x * 3.0 + Time) * cos(p.z * 3.0 + Time * 0.7) * Amplitude;
    p.y += wave;

    float4 worldPos = mul(float4(p, 1.0), World);

    VS_OUTPUT output;
    output.Position = mul(float4(p, 1.0), WorldViewProj);
    output.WorldPos = p;
    output.Normal = input.Normal;
    return output;
}
