// Skybox_ps.hlsl — Equirectangular skybox pixel shader.
// The VS emits z = 1.0 (far plane), so the hardware depth test against the
// shared depth buffer rejects sky pixels wherever the GBuffer pass wrote
// geometry. No manual depth sampling or discard needed.

Texture2D    EnvMap       : register(t0);
SamplerState EnvSampler   : register(s0);

#define PBR_PI 3.14159265358979323846

float2 DirToEquirect(float3 dir)
{
    float u = 0.5 + atan2(dir.z, dir.x) / (2.0 * PBR_PI);
    float v = 0.5 - asin(clamp(dir.y, -1.0, 1.0)) / PBR_PI;
    return float2(u, v);
}

float4 PSMain(float4 pos : SV_POSITION, float3 viewDir : TEXCOORD0) : SV_TARGET0
{
    float3 dir = normalize(viewDir);
    float2 uv = DirToEquirect(dir);
    float3 color = EnvMap.Sample(EnvSampler, uv).rgb;
    return float4(color, 1.0);
}
