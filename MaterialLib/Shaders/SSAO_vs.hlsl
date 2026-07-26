// SSAO_vs.hlsl — Fullscreen triangle VS for SSAO pass.
// Passes through UV [0,1] for sampling the G-Buffer.

struct VS_INPUT
{
    float3 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD0;
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;
    output.Position = float4(input.Position, 1.0);
    output.UV       = input.TexCoord;
    return output;
}
