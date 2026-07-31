// Tonemap_ps.hlsl — ACES filmic tonemap + gamma correction.
// Reads the HDR scene RT and optional bloom RT, outputs LDR to backbuffer.

Texture2D    HdrSceneRT  : register(t0);
SamplerState SceneSampler : register(s0);
Texture2D    BloomRT     : register(t1);
SamplerState BloomSampler : register(s1);

float Exposure       : register(c0); // default 1.0
float BloomIntensity : register(c1); // default 0.3

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD0;
};

// ACES filmic tonemap (Narkowicz fit)
// Formula: f(x) = x*(2.51*x + 0.03) / (x*(2.43*x + 0.59) + 0.14)
float3 ACESFilm(float3 x)
{
    float3 a = x * (2.51 * x + 0.03);
    float3 b = x * (2.43 * x + 0.59) + 0.14;
    return saturate(a / b);
}

float4 PSMain(PS_INPUT input) : SV_TARGET0
{
    uint w, h;
    HdrSceneRT.GetDimensions(w, h);
    float2 uv = input.Position.xy / float2(w, h);

    float3 hdrColor = HdrSceneRT.Sample(SceneSampler, uv).rgb;
    float3 bloom = BloomRT.Sample(BloomSampler, uv).rgb;

    // Apply exposure and add bloom
    float3 color = hdrColor * Exposure + bloom * BloomIntensity;

    // ACES tonemap
    color = ACESFilm(color);

    // Gamma correction
    color = pow(max(color, 0.0), 1.0 / 2.2);

    return float4(color, 1.0);
}
