// PbrMaterial_ps.hlsl — PBR Material Pixel Shader
// Cook-Torrance BRDF (GGX NDF, Smith geometry, Schlick Fresnel, Disney diffuse)
// with texture sampling (albedo, normal, ORM), equirectangular env map IBL,
// and directional shadow mapping (3x3 PCF).
//
// Texture bindings:
//   t0/s0: AlbedoMap
//   t1/s1: NormalMap (OpenGL convention)
//   t2/s2: ORMMap (R=AO, G=Roughness, B=Metallic)
//   t3/s3: EnvMap (equirectangular HDR, 2D, specular IBL)
//   t4/s4: ShadowMap (R32F depth, manual PCF)
//   t5/s5: IrradianceMap (pre-convolved diffuse IBL, low-res)
//   t6/s6: BrdfLut (split-sum BRDF integration LUT, RG16F)

Texture2D    AlbedoMap : register(t0);
SamplerState AlbedoSampler : register(s0);
Texture2D    NormalMap  : register(t1);
SamplerState NormalSampler : register(s1);
Texture2D    ORMMap     : register(t2);
SamplerState ORMSampler : register(s2);
Texture2D    EnvMap     : register(t3);
SamplerState EnvSampler : register(s3);
Texture2D    ShadowMap  : register(t4);
SamplerState ShadowSampler : register(s4);
Texture2D    IrradianceMap    : register(t5);
SamplerState IrradianceSampler : register(s5);
Texture2D    BrdfLut          : register(t6);
SamplerState BrdfSampler      : register(s6);
Texture2D    SSAOTex          : register(t7);
SamplerState SSAOSampler      : register(s7);

// Constants — register(cN) matches FEB parameter layout
float3   EyePosition    : register(c11);
float3   LightDirection : register(c12);
float3   LightColor     : register(c13);
float3   AlbedoTint     : register(c14);
float    MetallicScale  : register(c15);
float    RoughnessScale : register(c16);
float4x4 LightViewProj  : register(c17);
float     UseEnvOnly     : register(c21);
float     EnvIntensity   : register(c22);

#define PBR_PI 3.14159265358979323846

// ── BRDF ────────────────────────────────────────────────────────────────────

float GGX_D(float NdotH, float roughness)
{
    float a  = roughness * roughness;
    float a2 = a * a;
    float d  = (NdotH * NdotH) * (a2 - 1.0) + 1.0;
    return a2 / (PBR_PI * d * d);
}

float Smith_G1(float NdotX, float roughness)
{
    float k = (roughness + 1.0) * (roughness + 1.0) / 8.0;
    return NdotX / (NdotX * (1.0 - k) + k);
}
float Smith_G(float NdotL, float NdotV, float roughness)
{
    return Smith_G1(NdotL, roughness) * Smith_G1(NdotV, roughness);
}

float3 Schlick_F(float HdotV, float3 F0)
{
    return F0 + (1.0 - F0) * pow(max(1.0 - HdotV, 0.0), 5.0);
}

float3 DisneyDiffuse(float3 albedo, float NdotL, float NdotV, float LdotH,
                     float roughness, float metallic)
{
    float Fd90 = 0.5 + 2.0 * roughness * LdotH * LdotH;
    float3 diff = albedo / PBR_PI
        * lerp(1.0, Fd90, pow(max(1.0 - NdotL, 0.0), 5.0))
        * lerp(1.0, Fd90, pow(max(1.0 - NdotV, 0.0), 5.0))
        * (1.0 - metallic);
    return diff;
}

// ── IBL ─────────────────────────────────────────────────────────────────────

float2 DirToEquirect(float3 dir)
{
    float u = 0.5 + atan2(dir.z, dir.x) / (2.0 * PBR_PI);
    float v = 0.5 - asin(clamp(dir.y, -1.0, 1.0)) / PBR_PI;
    return float2(u, v);
}

float3 SampleEnvMap(float3 dir, float lod)
{
    float2 uv = DirToEquirect(dir);
    return EnvMap.SampleLevel(EnvSampler, uv, lod).rgb;
}

// ── Shadow Map ───────────────────────────────────────────────────────────────

float SampleShadow(float3 worldPos, float NdotL)
{
    float4 lightClipPos = mul(float4(worldPos, 1.0), LightViewProj);
    float3 shadowCoord = lightClipPos.xyz / lightClipPos.w;

    // Outside the shadow frustum → fully lit
    if (shadowCoord.x < -1.0 || shadowCoord.x > 1.0 ||
        shadowCoord.y < -1.0 || shadowCoord.y > 1.0)
        return 1.0;

    // Transform from NDC [-1,1] to UV [0,1]
    // FNA3D_HLSL renders with D3D viewport convention (y_ndc=+1→top, y_ndc=-1→bottom),
    // which flips Y relative to the Vulkan-native NDC→UV mapping from clip-space divide.
    float2 shadowUV = float2(shadowCoord.x * 0.5 + 0.5, shadowCoord.y * -0.5 + 0.5);
    float depth = shadowCoord.z; // Vulkan NDC [0,1]

    // 3×3 PCF
    uint w, h;
    ShadowMap.GetDimensions(w, h);
    float2 texelSize = float2(1.0 / w, 1.0 / h);

    float shadow = 0.0;
    if (NdotL > 0)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            [unroll]
            for (int y = -1; y <= 1; y++)
            {
                float2 uv = shadowUV + float2(x, y) * texelSize * 1.5;
                float sampledDepth = ShadowMap.SampleLevel(ShadowSampler, uv, 0).r;
                shadow += depth > sampledDepth ? 0.0 : 1.0;
            }
        }
    }

    return shadow / 9.0;
}

// ── Main ────────────────────────────────────────────────────────────────────

float4 PSMain(
    float4 screenPos   : SV_POSITION,
    float3 worldPos    : TEXCOORD0,
    float3 worldNormal : TEXCOORD1,
    float2 texCoord    : TEXCOORD2
) : SV_TARGET0
{
    // Sample textures
    float3 albedo    = AlbedoMap.Sample(AlbedoSampler, texCoord).rgb * AlbedoTint;
    float3 normalTex = NormalMap.Sample(NormalSampler, texCoord).rgb;
    float3 orm       = ORMMap.Sample(ORMSampler, texCoord).rgb;

    // SSAO: sample screen-space ambient occlusion
    uint ssaoW, ssaoH;
    SSAOTex.GetDimensions(ssaoW, ssaoH);
    float2 screenUV = screenPos.xy / float2(ssaoW, ssaoH);
    float ssao = SSAOTex.SampleLevel(SSAOSampler, screenUV, 0).r;
    float ao = orm.r * ssao;
    float roughness = clamp(orm.g * RoughnessScale, 0.04, 1.0);
    float metallic  = clamp(orm.b * MetallicScale, 0.0, 1.0);

    // Decode tangent-space normal from [0,1] to [-1,1]
    float3 tNormal = normalTex * 2.0 - 1.0;

    // Build tangent frame from world normal (simple method)
    float3 N = normalize(worldNormal);
    float3 up = abs(N.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
    float3 T = normalize(cross(up, N));
    float3 B = cross(T, N);  // right-handed TBN: B points along +V direction

    float3 worldN = normalize(tNormal.x * T + tNormal.y * B + tNormal.z * N);

    // Lighting vectors
    float3 L = normalize(LightDirection);
    float3 V = normalize(EyePosition - worldPos);

    float NdotLRaw = dot(worldN, L);
    float NdotL = max(NdotLRaw, 0.0);
    float NdotV = max(dot(worldN, V), 0.0);

    // Fresnel F0
    float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);

    // ── Direct light (directional + shadow) ──────────────────────────────────
    float3 directLight = float3(0, 0, 0);
    float  shadow = 1.0;

    if (UseEnvOnly <= 0.5 && NdotL > 0.0)
    {
        float3 H = normalize(L + V);
        float NdotH = max(dot(worldN, H), 0.0);
        float HdotV = max(dot(H, V), 0.0);
        float LdotH = max(dot(L, H), 0.0);

        float D = GGX_D(NdotH, roughness);
        float G = Smith_G(NdotL, NdotV, roughness);
        float3 F = Schlick_F(HdotV, F0);

        float3 specular = (D * F * G) / max(4.0 * NdotV, 0.001);
        float3 diffuse  = DisneyDiffuse(albedo, NdotL, NdotV, LdotH, roughness, metallic);

        shadow = SampleShadow(worldPos, NdotLRaw);
        directLight = (diffuse * NdotL + specular) * LightColor * shadow;
    }

    // ── Indirect light (IBL from environment map) ────────────────────────────
    // Split-sum approximation (Unreal Engine 4, Karis 2013).
    //
    // Diffuse  = IrradianceMap(N) * albedo * (1 - F) * (1 - metallic)
    // Specular = PrefilteredEnvMap(R, roughness) * (F0 * BrdfLut.x + BrdfLut.y)
    //
    // PrefilteredEnvMap: GGX-convolved mip chain (offline GPU precompute)
    // BrdfLut: 2D LUT (NdotV × roughness) integrating Fresnel over GGX lobe
    //
    // metallic=0 → matte surface (96% diffuse, 4% specular at normal)
    // metallic=1 → mirror surface (100% specular, zero diffuse)
    float3 R = reflect(-V, worldN);

    // Energy-conserving Fresnel partition for diffuse
    float3 F_ibl = Schlick_F(NdotV, F0);
    float3 kD = (1.0 - F_ibl) * (1.0 - metallic);

    // Specular: prefiltered env map at roughness-dependent mip level × BRDF LUT
    uint envW, envH, mipCount;
    EnvMap.GetDimensions(0, envW, envH, mipCount);
    float  maxMip = float(mipCount - 1);
    float  mipLevel = roughness * maxMip;
    float3 prefilteredColor = SampleEnvMap(R, mipLevel);
    float2 brdf = BrdfLut.Sample(BrdfSampler, float2(NdotV, roughness)).rg;

    float3 indirectLight;
    if (UseEnvOnly > 0.5)
    {
        float3 diffuseIBL  = IrradianceMap.Sample(IrradianceSampler, DirToEquirect(worldN)).rgb * albedo * kD;
        float3 specularIBL = prefilteredColor * (F0 * brdf.r + brdf.g);
        indirectLight = (diffuseIBL + specularIBL) * EnvIntensity * ao;
    }
    else
    {
        float3 diffuseIBL  = IrradianceMap.Sample(IrradianceSampler, DirToEquirect(worldN)).rgb * albedo * kD * 0.2;
        float3 specularIBL = prefilteredColor * (F0 * brdf.r + brdf.g);
        indirectLight = (diffuseIBL + specularIBL * 0.4) * ao;
        indirectLight *= lerp(0.3, 1.0, shadow);
    }

    // Combine
    float3 color = directLight + indirectLight;

    // ACES filmic tonemap (Narkowicz fit) — preserves specular contrast
    // better than Reinhard, matching Unreal's approach.
    // Formula: f(x) = x*(2.51*x + 0.03) / (x*(2.43*x + 0.59) + 0.14)
    color *= 1.2; // exposure (HDR env values typically 0.1-5, lift to visible range)
    float3 a = color * (2.51 * color + 0.03);
    float3 b = color * (2.43 * color + 0.59) + 0.14;
    color = saturate(a / b);

    // Gamma
    color = pow(max(color, 0.0), 1.0 / 2.2);

    return float4(color, 1.0);
}
