// Trail vertex shader — reads captured world-space positions from
// a storage buffer and renders trail instances via GPU instancing.

float4x4 ViewProj : register(c0);
StructuredBuffer<float3> DeformedRing : register(t0);
uint VertexCount : register(c4);

struct VS_INPUT
{
    float3 DummyPos : POSITION0;    // from geometry buffer (ignored)
    float4 Color    : TEXCOORD0;    // per-instance trail gradient
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
};

VS_OUTPUT VSMain(VS_INPUT input, uint vertID : SV_VertexID, uint instID : SV_InstanceID)
{
    uint idx = instID * VertexCount + vertID;
    float3 worldPos = DeformedRing[idx];

    VS_OUTPUT output;
    output.Position = mul(float4(worldPos, 1.0), ViewProj);
    output.Color = input.Color;
    return output;
}
