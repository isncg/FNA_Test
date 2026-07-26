// BrdfLut_ps.hlsl — BRDF integration LUT for split-sum IBL.
//
// Output: RG texture where
//   R = F0 scale factor  (∫ D·G·(1−(1−cosθ)⁵) · cosθ/NdotV  dω)
//   G = additive bias     (∫ D·G·(1−cosθ)⁵ · cosθ/NdotV  dω)
//
// UV.x = NdotV (0→1), UV.y = roughness (0→1)
//
// Reference: "Real Shading in Unreal Engine 4" (Karis, 2013)

#define PBR_PI 3.14159265358979323846

// Hammersley low-discrepancy sequence
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

// GGX importance sampling: tangent-space half-vector
float3 ImportanceSampleGGX(float2 Xi, float roughness, float3 N)
{
    float a = roughness * roughness;
    float phi = 2.0 * PBR_PI * Xi.x;
    float cosTheta = sqrt((1.0 - Xi.y) / (1.0 + (a * a - 1.0) * Xi.y));
    float sinTheta = sqrt(max(1.0 - cosTheta * cosTheta, 0.0));

    float3 H = float3(cos(phi) * sinTheta, cosTheta, sin(phi) * sinTheta);

    // Rotate from tangent space (N = (0,0,1)) to world
    float3 up = abs(N.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 T = normalize(cross(up, N));
    float3 B = cross(N, T);
    return H.x * T + H.y * N + H.z * B;
}

// Smith geometry term for IBL (without Fresnel/k-factor)
float G_Smith_IBL(float NdotL, float NdotV, float roughness)
{
    float a = roughness * roughness;
    float k = a * a / 2.0;
    float G1L = NdotL / (NdotL * (1.0 - k) + k);
    float G1V = NdotV / (NdotV * (1.0 - k) + k);
    return G1L * G1V;
}

float4 PSMain(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET0
{
    float NdotV = uv.x;
    float roughness = uv.y;

    // Avoid singularities
    // NdotV = max(NdotV, 0.001);
    // roughness = max(roughness, 0.04);

    float3 V = float3(sqrt(max(1.0 - NdotV * NdotV, 0.0)), 0.0, NdotV);

    float A = 0.0;
    float B = 0.0;

    const uint SAMPLE_COUNT = 1024u;

    for (uint i = 0u; i < SAMPLE_COUNT; i++)
    {
        float2 Xi = Hammersley(i, SAMPLE_COUNT);
        float3 H = ImportanceSampleGGX(Xi, roughness, float3(0, 0, 1));
        float3 L = 2.0 * dot(V, H) * H - V;

        float NdotL = saturate(L.z);
        float NdotH = saturate(H.z);
        float VdotH = saturate(dot(V, H));

        if (NdotL > 0.0)
        {
            float G = G_Smith_IBL(NdotL, NdotV, roughness);
            float G_Vis = (G * VdotH) / max(NdotH * NdotV, 0.001);
            float Fc = pow(max(1.0 - VdotH, 0.0), 5.0);

            A += G_Vis * (1.0 - Fc);
            B += G_Vis * Fc;
        }
    }

    A /= float(SAMPLE_COUNT);
    B /= float(SAMPLE_COUNT);

    return float4(A, B, 0.0, 1.0);
}
