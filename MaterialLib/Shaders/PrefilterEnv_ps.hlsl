// PrefilterEnv_ps.hlsl — GGX prefiltered environment map convolution.
//
// Generates one mip level of the prefiltered env map for split-sum IBL.
// Input:  EnvMap at t0 (equirectangular HDR, level 0)
// Output: prefiltered color for the given roughness at this output texel.
//
// Each output texel UV → reflection direction R.
// Hemisphere sampling with GGX importance sampling around R.

Texture2D    EnvMap     : register(t0);
SamplerState EnvSampler : register(s0);

float  MipRoughness : register(c0); // target roughness for this mip level

#define PBR_PI 3.14159265358979323846

float2 DirToEquirect(float3 dir)
{
    float u = 0.5 + atan2(dir.z, dir.x) / (2.0 * PBR_PI);
    float v = 0.5 - asin(clamp(dir.y, -1.0, 1.0)) / PBR_PI;
    return float2(u, v);
}

float3 SampleEnvMap(float3 dir)
{
    float2 uv = DirToEquirect(dir);
    return EnvMap.SampleLevel(EnvSampler, uv, 0).rgb;
}

// ── Quasi-Monte Carlo helpers ─────────────────────────────────────────────

float RadicalInverse_VdC(uint bits)
{
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    return float(bits) * 2.3283064365386963e-10;
}

float2 Hammersley(uint i, uint N)
{
    return float2(float(i) / float(N), RadicalInverse_VdC(i));
}

float3 ImportanceSampleGGX(float2 Xi, float roughness, float3 N)
{
    float a = roughness * roughness;
    float phi = 2.0 * PBR_PI * Xi.x;
    float cosTheta = sqrt((1.0 - Xi.y) / (1.0 + (a * a - 1.0) * Xi.y));
    float sinTheta = sqrt(max(1.0 - cosTheta * cosTheta, 0.0));

    float3 H = float3(cos(phi) * sinTheta, cosTheta, sin(phi) * sinTheta);

    float3 up = abs(N.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 T = normalize(cross(up, N));
    float3 B = cross(N, T);
    return H.x * T + H.y * N + H.z * B;
}

// ── Main ──────────────────────────────────────────────────────────────────

float4 PSMain(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET0
{
    // Output UV → reflection direction R (inverse of DirToEquirect)
    // DirToEquirect: u = 0.5 + atan2(z,x)/(2π), v = 0.5 - asin(y)/π
    // Inverse: φ = (u - 0.5)·2π, θ = v·π
    float phi   = uv.x * 2.0 * PBR_PI - PBR_PI;
    float theta = uv.y * PBR_PI;
    float3 R = float3(sin(theta) * cos(phi), cos(theta), sin(theta) * sin(phi));

    if (MipRoughness < 0.01)
    {
        // Sharp mirror — no filtering needed
        return float4(SampleEnvMap(R), 1.0);
    }

    float3 N = R;
    float3 V = R;

    float3 prefilteredColor = float3(0, 0, 0);
    float  totalWeight = 0.0;

    const uint SAMPLE_COUNT = 256u;

    for (uint i = 0u; i < SAMPLE_COUNT; i++)
    {
        float2 Xi = Hammersley(i, SAMPLE_COUNT);
        float3 H = ImportanceSampleGGX(Xi, MipRoughness, N);
        float3 L = 2.0 * dot(V, H) * H - V;

        float NdotL = saturate(dot(N, L));
        if (NdotL > 0.0)
        {
            prefilteredColor += SampleEnvMap(L) * NdotL;
            totalWeight += NdotL;
        }
    }

    prefilteredColor /= max(totalWeight, 0.001);
    return float4(prefilteredColor, 1.0);
}
