// Procedural wave mesh vertex shader.
// Demonstrates general vertex deformation (no bones) and captures
// world-space output positions to a storage buffer for trail replay.
//
// The trail vertex shader reads these captured positions to render
// ghost instances via GPU instancing.

float4x4 WorldViewProj : register(c0);
float Time            : register(c4);
float Amplitude       : register(c5);
float3 LightDir       : register(c16);

RWStructuredBuffer<float3> CaptureBuffer : register(u0);

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

VS_OUTPUT VSMain(VS_INPUT input, uint vertID : SV_VertexID)
{
    float3 p = input.Position;

    // Procedural wave deformation (not bone-specific)
    float wave = sin(p.x * 3.0 + Time) * cos(p.z * 3.0 + Time * 0.7) * Amplitude;
    p.y += wave;

    // Capture world-space position for trail replay
    CaptureBuffer[vertID] = p;

    VS_OUTPUT output;
    output.Position = mul(float4(p, 1.0), WorldViewProj);
    output.WorldPos = p;
    output.Normal = input.Normal;
    return output;
}
