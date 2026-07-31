// DepthView pixel shader - samples the depth texture (t0) and writes the
// raw depth value as grayscale so the CPU can read it back as color.

Texture2D DepthTex : register(t0);
SamplerState DepthSampler : register(s0);

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

float4 PSMain(PS_INPUT input) : SV_TARGET0
{
    float depth = DepthTex.Sample(DepthSampler, input.TexCoord).r;
    return float4(depth, depth, depth, 1.0);
}
