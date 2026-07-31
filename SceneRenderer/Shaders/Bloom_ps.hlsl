// Bloom_ps.hlsl — Bloom post-processing (bright extract + downsample + upsample).
//
// Controlled by ShaderIndex uniform:
//   ShaderIndex == 0: Bright extract (threshold filter)
//   ShaderIndex == 1: Downsample (13-tap tent filter)
//   ShaderIndex == 2: Upsample (combine with previous mip)

Texture2D    InputTex   : register(t0);
SamplerState InputSampler : register(s0);

float  BloomThreshold : register(c0);
float2 TexelSize      : register(c1);
float  ShaderIndex    : register(c2);
float  BloomIntensity : register(c3);

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD0;
};

// Luminance from sRGB primaries
float Luminance(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

float4 PSMain(PS_INPUT input) : SV_TARGET0
{
    int mode = int(ShaderIndex + 0.5);

    if (mode == 0)
    {
        // Bright extract
        float3 color = InputTex.Sample(InputSampler, input.UV).rgb;
        float brightness = Luminance(color);
        float3 bright = color * smoothstep(BloomThreshold, BloomThreshold * 2.0, brightness);
        return float4(bright, 1.0);
    }
    else if (mode == 1)
    {
        // Downsample: 13-tap tent filter (Karis 2013)
        // Sample a 4x4 block using bilinear filtering trick:
        // Take center sample + offsets at 3x3 pattern with custom weights
        float3 color = float3(0, 0, 0);

        // 3x3 grid using bilinear offsets (each sample covers 2x2 texels)
        float2 uv0 = input.UV + float2(-1.0, -1.0) * TexelSize;
        float2 uv1 = input.UV + float2( 1.0, -1.0) * TexelSize;
        float2 uv2 = input.UV + float2(-1.0,  1.0) * TexelSize;
        float2 uv3 = input.UV + float2( 1.0,  1.0) * TexelSize;

        color += InputTex.Sample(InputSampler, uv0).rgb * 0.25;
        color += InputTex.Sample(InputSampler, uv1).rgb * 0.25;
        color += InputTex.Sample(InputSampler, uv2).rgb * 0.25;
        color += InputTex.Sample(InputSampler, uv3).rgb * 0.25;

        return float4(color, 1.0);
    }
    else
    {
        // Upsample: combine current mip with upsampled smaller mip
        // (simple bilinear upsample + blend)
        float3 color = InputTex.Sample(InputSampler, input.UV).rgb;
        return float4(color * BloomIntensity, 1.0);
    }
}
