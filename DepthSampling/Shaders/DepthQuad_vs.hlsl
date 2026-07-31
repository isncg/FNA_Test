// DepthSampling vertex shader - clip-space passthrough.
// Positions are pre-baked in clip space (x,y in [-1,1], z in [0,1]).
//
// VS_INPUT: Position + Color (PC layout, C1-C5 compliance)

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
    output.Position = float4(input.Position, 1.0);
    output.Color = input.Color;
    return output;
}
