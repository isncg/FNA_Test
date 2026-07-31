// Skybox_ps.hlsl — Equirectangular skybox pixel shader.
// Outputs HDR color for additive blend into the HDR scene RT.
// Uses GBuffer depth to only render sky where no geometry exists.

Texture2D    EnvMap       : register(t0);
SamplerState EnvSampler   : register(s0);
Texture2D    GBufferRT2   : register(t1); // metallic.R, linearDepth.G
SamplerState GBuffer2Sampler : register(s1);

#define PBR_PI 3.14159265358979323846

float2 DirToEquirect(float3 dir)
{
    float u = 0.5 + atan2(dir.z, dir.x) / (2.0 * PBR_PI);
    float v = 0.5 - asin(clamp(dir.y, -1.0, 1.0)) / PBR_PI;
    return float2(u, v);
}

float4 PSMain(float4 pos : SV_POSITION, float3 viewDir : TEXCOORD0) : SV_TARGET0
{
    // Only render sky where no geometry (viewZ <= 0 or >= far plane)
    uint rtW, rtH;
    GBufferRT2.GetDimensions(rtW, rtH);
    float2 depthUV = pos.xy / float2(rtW, rtH);
    float viewZ = GBufferRT2.Sample(GBuffer2Sampler, depthUV).g;
    if (viewZ > 0.0 && viewZ < 1000.0)
        discard; // geometry pixel — don't add sky

    float3 dir = normalize(viewDir);
    float2 uv = DirToEquirect(dir);
    float3 color = EnvMap.Sample(EnvSampler, uv).rgb;
    return float4(color, 1.0);
}
