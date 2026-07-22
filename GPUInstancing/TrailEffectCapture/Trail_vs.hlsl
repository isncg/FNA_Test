// Trail vertex shader — simplified: world-space positions + colors pre-computed on CPU.
// Each vertex already contains its world-space Position and age-faded Color.
// Just transform by ViewProj and pass through.

float4x4 ViewProj : register(c0);

struct VS_INPUT
{
    float3 Position : POSITION0;
    float4 Color    : COLOR0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;
    output.Position = mul(float4(input.Position, 1.0), ViewProj);
    output.Color = input.Color;
    return output;
}
