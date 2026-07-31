// Geometry pixel shader - flat vertex color.

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
};

float4 PSMain(PS_INPUT input) : SV_TARGET0
{
    return input.Color;
}
