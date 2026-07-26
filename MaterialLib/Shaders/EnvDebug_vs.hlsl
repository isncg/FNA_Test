// EnvDebug_vs.hlsl — Fullscreen triangle vertex shader for env map debug.
// VB contains clip-space positions; pass through with SV_POSITION.z=0 (always visible).
// Single triangle covers the entire viewport (vertices: (-1,-1), (3,-1), (-1,3)).

struct VS_INPUT
{
    float3 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;
    output.Position = float4(input.Position, 1.0);
    output.TexCoord = input.TexCoord;
    return output;
}
