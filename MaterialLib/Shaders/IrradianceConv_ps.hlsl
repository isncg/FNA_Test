// IrradianceConv_ps.hlsl — Hemisphere convolution for diffuse IBL irradiance map.
// One-shot GPU precompute: for each output texel, integrate the environment map
// over the hemisphere around N with cosine (Lambert) weighting.
//
// Input:  env map at t0 (equirectangular HDR)
// Output: irradiance map (equirectangular, same orientation)

Texture2D    EnvMap     : register(t0);
SamplerState EnvSampler : register(s0);

#define PBR_PI 3.14159265358979323846

float2 DirToEquirect(float3 dir)
{
    float u = 0.5 + atan2(dir.z, dir.x) / (2.0 * PBR_PI);
    float v = 0.5 - asin(clamp(dir.y, -1.0, 1.0)) / PBR_PI;
    return float2(u, v);
}

float4 PSMain(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET0
{
    // Output UV → normal direction N (inverse of DirToEquirect)
    // DirToEquirect: u = 0.5 + atan2(z,x)/(2π), v = 0.5 - asin(y)/π
    // Inverse: φ = (u - 0.5)·2π, θ = v·π
    float phi   = uv.x * 2.0 * PBR_PI - PBR_PI;
    float theta = uv.y * PBR_PI;
    float3 N = float3(sin(theta) * cos(phi), cos(theta), sin(theta) * sin(phi));

    // Tangent frame for N
    float3 up = abs(N.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
    float3 T = normalize(cross(up, N));
    float3 B = cross(N, T);

    // Stratified hemisphere integration: 64 phi × 16 theta = 1024 samples/pixel.
    // Weight = cos(θ_local) · sin(θ_local) — cosine-weighted solid angle.
    float3 irradiance = float3(0, 0, 0);
    float  totalWeight = 0.0;

    const uint N_PHI   = 64;
    const uint N_THETA = 16;

    for (uint ti = 0; ti < N_THETA; ti++)
    {
        float theta_s = (float(ti) + 0.5) / float(N_THETA) * (0.5 * PBR_PI);
        float cosT = cos(theta_s);
        float sinT = sin(theta_s);
        float weight = cosT * sinT; // cos-weighted solid-angle differential

        for (uint pi = 0; pi < N_PHI; pi++)
        {
            float phi_s = (float(pi) + 0.5) / float(N_PHI) * (2.0 * PBR_PI);

            // Local direction in TBN space
            float3 localDir = float3(
                sinT * cos(phi_s),
                cosT,
                sinT * sin(phi_s));

            // Rotate to world space
            float3 worldDir = localDir.x * T + localDir.y * N + localDir.z * B;

            float2 envUV = DirToEquirect(worldDir);
            irradiance += EnvMap.SampleLevel(EnvSampler, envUV, 0).rgb * weight;
            totalWeight += weight;
        }
    }

    irradiance /= totalWeight;
    return float4(irradiance, 1.0);
}
