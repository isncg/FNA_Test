// Trail vertex shader — instanced replay from storage buffer ring.
// Each instance reads cube positions from one ring slot and transforms
// with ViewProj. Color fades red→blue with age.
//
// SDL3 graphics pipeline layout (see feb_builder.py):
//   Set 0 = vertex read-resources: samplers bind 0, SRV/UAV at higher bindings
//   Set 1 = vertex uniform buffers (register(cN) → globals)
// register(t1) instead of t0 — t0/bind 0 is taken by the forced-sampler slot.

StructuredBuffer<float4> TrailData : register(t1);

float4x4 ViewProj    : register(c0);
float VertexCount    : register(c4);
float RingHead       : register(c5);
float MaxRingSize    : register(c6);

struct VS_INPUT
{
    float3 DummyPos : POSITION0;
    float3 DummyNrm : NORMAL0;
    float2 DummyUV  : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
    float3 Junk     : TEXCOORD0;
};

VS_OUTPUT VSMain(VS_INPUT input, uint vertID : SV_VertexID, uint instanceID : SV_InstanceID)
{
    // Ring buffer index: (RingHead - 1 - instanceID + MaxRingSize) % MaxRingSize
    int vertCount = int(VertexCount);
    int ringSz = int(MaxRingSize);
    int head = int(RingHead);
    int slot = (head - 1 - int(instanceID) + ringSz) % ringSz;
    int bufferIdx = slot * vertCount + int(vertID);

    float3 worldPos = TrailData[bufferIdx].xyz;

    // Age-based color: red/orange (new) → blue (old), fading alpha
    float age = (float)instanceID / MaxRingSize;
    float alpha = 1.0 - age;
    float3 color = lerp(float3(1.0, 0.3, 0.1), float3(0.2, 0.3, 1.0), age);

    VS_OUTPUT output;
    output.Position = mul(float4(worldPos, 1.0), ViewProj);
    output.Color = float4(color, alpha);
    // Prevent DXC from optimizing vertex inputs & unused uniforms
    float junkVal = input.DummyPos.x + input.DummyNrm.x + input.DummyUV.x
        + VertexCount + RingHead + MaxRingSize;
    output.Junk = float3(junkVal, junkVal, junkVal);
    return output;
}
