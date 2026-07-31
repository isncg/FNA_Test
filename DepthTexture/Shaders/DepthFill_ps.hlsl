// DepthFill pixel shader - flat color output (depth comes from the
// rasterized position, no SV_Depth override).

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
};

float4 PSMain(PS_INPUT input) : SV_TARGET0
{
    return input.Color;
}
