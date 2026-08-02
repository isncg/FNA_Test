// DeferredLighting_ps.hlsl — Deferred PBR lighting (Cook-Torrance GGX + split-sum IBL).
//
// Reads GBuffer MRTs:
//   t0/s0: GBufferRT0 (albedo.RGB, bakedAO.A)
//   t1/s1: GBufferRT1 (worldNormal.RGB *0.5+0.5, roughness.A)
//   t2/s2: GBufferRT2 (metallic.R, linearDepth.G, motionVec.BA)
//
// Reads post-process inputs:
//   t3/s3: SSAOBlurRT (R32F)
//   t4/s4: SSRRT (HalfVector4 — reflection color)
//   t5/s5: ShadowMap (R32F)
//   t6/s6: IrradianceMap (diffuse IBL)
//   t7/s7: PrefilteredEnvMap (specular IBL, mip chain)
//
// Note: BRDF LUT replaced by analytical Karis approximation to stay within 16-sampler limit.

Texture2D    GBufferRT0        : register(t0);
SamplerState GBuffer0Sampler   : register(s0);
Texture2D    GBufferRT1        : register(t1);
SamplerState GBuffer1Sampler   : register(s1);
Texture2D    GBufferRT2        : register(t2);
SamplerState GBuffer2Sampler   : register(s2);
Texture2D    SSAOBlurRT        : register(t3);
SamplerState SSAOSampler       : register(s3);
Texture2D    SSRRT             : register(t4);
SamplerState SSRSampler        : register(s4);
Texture2D    ShadowMap         : register(t5);
SamplerState ShadowSampler     : register(s5);
Texture2D    IrradianceMap     : register(t6);
SamplerState IrradianceSampler : register(s6);
Texture2D    PrefilteredEnvMap : register(t7);
SamplerState PrefilteredSampler: register(s7);
float3   EyePosition     : register(c0);
float3   AmbientLight    : register(c3);
float    EnvIntensity    : register(c6);
float    NumActiveLights : register(c7);
// Light buffer: up to 16 lights, each 4 float4s (64 registers total)
// Light[i].Data0: (Type, Intensity, Range, CastsShadows)
// Light[i].Data1: (PosOrDir.x, PosOrDir.y, PosOrDir.z, InnerConeCos)
// Light[i].Data2: (Color*Intensity.r, Color*Intensity.g, Color*Intensity.b, OuterConeCos)
// Light[i].Data3: (SpotDir.x, SpotDir.y, SpotDir.z, Falloff)
float4 LightData[64]       : register(c8);
float4x4 InvViewProj       : register(c72);
float4x4 LightViewProj     : register(c76);
float4x4 Projection        : register(c80);

#define PBR_PI         3.14159265358979323846
#define LIGHT_DIRECTIONAL 0.0
#define LIGHT_POINT       1.0
#define LIGHT_SPOT        2.0

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

// ── Analytical BRDF LUT ─────────────────────────────────────────────────────
// Karis 2014 / Disney approximation — replaces the 2D BRDF LUT texture.
// Matches the split-sum LUT to within ~1.5% for most materials.

float2 EnvBRDFApprox(float NdotV, float roughness)
{
    float4 c0 = float4(-1.0, -0.0275, -0.572,  0.022);
    float4 c1 = float4( 1.0,  0.0425,  1.040, -0.040);
    float4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
    float b004 = min(r.z * r.z, exp2(-9.28 * NdotV)) * r.z + r.w;
    return float2(max(a004, 0.0), max(b004, 0.0));
}

// Off-specular peak reflection direction (UE ReflectionEnvironmentShared.usf,
// GetOffSpecularPeakReflectionDir). For a rough surface the dominant direction of
// the GGX importance-sampled lobe is not the mirror reflection but is shifted
// toward the normal; this blends R toward N by a roughness-dependent weight
// (a = roughness^2, weight = (1-a)(sqrt(1-a)+a): 1 at roughness 0 -> pure mirror,
// 0 at roughness 1 -> pure normal). Normalized because our equirectangular lookup
// (DirToEquirect) needs a unit direction for the elevation term, unlike UE's
// cubemap sampling which is direction-only.
float3 GetOffSpecularPeakReflectionDir(float3 normal, float3 reflectionVector, float roughness)
{
    float a = roughness * roughness;
    return normalize(lerp(normal, reflectionVector, (1.0 - a) * (sqrt(1.0 - a) + a)));
}

// ── IBL ─────────────────────────────────────────────────────────────────────

float2 DirToEquirect(float3 dir)
{
    float u = 0.5 + atan2(dir.z, dir.x) / (2.0 * PBR_PI);
    float v = 0.5 - asin(clamp(dir.y, -1.0, 1.0)) / PBR_PI;
    return float2(u, v);
}

float3 SampleEnvMap(Texture2D envMap, SamplerState envSampler, float3 dir, float lod)
{
    float2 uv = DirToEquirect(dir);
    return envMap.SampleLevel(envSampler, uv, lod).rgb;
}

// ── Shadow Map ───────────────────────────────────────────────────────────────

float SampleShadow(float3 worldPos, float3 lightDir, float4x4 lightVP, float NdotL)
{
    float4 lightClipPos = mul(float4(worldPos, 1.0), lightVP);
    float3 shadowCoord = lightClipPos.xyz / lightClipPos.w;

    if (shadowCoord.x < -1.0 || shadowCoord.x > 1.0 ||
        shadowCoord.y < -1.0 || shadowCoord.y > 1.0)
        return 1.0;

    float2 shadowUV = float2(shadowCoord.x * 0.5 + 0.5, shadowCoord.y * -0.5 + 0.5);
    float depth = shadowCoord.z;

    uint w, h;
    ShadowMap.GetDimensions(w, h);
    float2 texelSize = float2(1.0 / w, 1.0 / h);

    float shadow = 0.0;
    if (NdotL > 0.0)
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

// ── Position Reconstruction ─────────────────────────────────────────────────

float3 ReconstructWorldPos(float2 uv, float viewZ, float4x4 invViewProj, float4x4 proj)
{
    // Reconstruct view-space position from linear depth + NDC coordinates.
    // viewZ = clip.w = -viewSpace.z (positive distance from camera), passed
    // from the GBuffer via a TEXCOORD varying (NOT from SV_POSITION.w, which
    // is 1/clip.w in Vulkan SPIR-V).
    // Right-handed view space has negative Z in front of camera, so negate.
    float2 ndc = float2(uv.x * 2.0 - 1.0, (1.0 - uv.y) * 2.0 - 1.0);
    float3 viewPos;
    viewPos.x = ndc.x * viewZ / proj._11;
    viewPos.y = ndc.y * viewZ / proj._22;
    viewPos.z = -viewZ;

    // Derive inverse view matrix: InvView = Proj * InvViewProj
    float4x4 invView = mul(proj, invViewProj);

    // Transform view-space position to world-space
    float4 worldPos = mul(float4(viewPos, 1.0), invView);
    return worldPos.xyz / worldPos.w;
}

// ── Light Attenuation ───────────────────────────────────────────────────────

float DistanceAttenuation(float dist, float range, float falloff)
{
    // Smooth falloff: (1 - (d/r)^2)^falloff, clamped to [0,1]
    float att = 1.0 - (dist * dist) / (range * range);
    return pow(max(att, 0.0), falloff);
}

float SpotAttenuation(float3 L, float3 spotDir, float innerCos, float outerCos)
{
    float cosAngle = dot(-L, spotDir);
    return smoothstep(outerCos, innerCos, cosAngle);
}

// ── Main ────────────────────────────────────────────────────────────────────

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD0;
};

float4 PSMain(PS_INPUT input) : SV_TARGET0
{
    // Compute UV from SV_POSITION
    uint rtW, rtH;
    GBufferRT0.GetDimensions(rtW, rtH);
    float2 uv = input.Position.xy / float2(rtW, rtH);

    // Sample GBuffer
    float4 gb0 = GBufferRT0.Sample(GBuffer0Sampler, uv); // albedo.RGB, bakedAO.A
    float4 gb1 = GBufferRT1.Sample(GBuffer1Sampler, uv); // worldNormal.RGB*0.5+0.5, roughness.A
    float4 gb2 = GBufferRT2.Sample(GBuffer2Sampler, uv); // metallic.R, linearDepth.G

    float3 albedo    = gb0.rgb;
    float  bakedAO   = gb0.a;
    float3 worldN    = normalize(gb1.rgb * 2.0 - 1.0);
    float  roughness = gb1.a;
    float  metallic  = gb2.r;
    float  viewZ     = gb2.g;

    // Skip sky/no-geometry pixels
    if (viewZ <= 0.0 || viewZ >= 1000.0)
        discard;

    // Reconstruct world position from view-space depth + projection
    float3 worldPos = ReconstructWorldPos(uv, viewZ, InvViewProj, Projection);

    // View vector (world-space)
    float3 V = normalize(EyePosition - worldPos);
    float NdotV = max(dot(worldN, V), 0.0);

    // Sample post-process inputs
    float ssao = SSAOBlurRT.Sample(SSAOSampler, uv).r;
    float ao  = bakedAO * ssao;

    // UE-style specular occlusion (ReflectionEnvironmentShaders.usf, ReflectionApplyPS):
    // tightens the indirect-specular Fresnel term in creases and at grazing
    // angles, so occluded / edge-on surfaces reflect less instead of staying
    // bright. saturate((NoV + AO)^2 - 1 + AO).
    float specOcclusion = saturate((NdotV + ao) * (NdotV + ao) - 1.0 + ao);

    // SSR reflection
    float4 ssrColor = SSRRT.Sample(SSRSampler, uv);

    // Fresnel F0
    float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);

    // ── Accumulate lighting ─────────────────────────────────────────────────
    float3 totalColor = AmbientLight * albedo * ao;

    int numLights = int(clamp(NumActiveLights, 0.0, 16.0));

    for (int li = 0; li < numLights; li++)
    {
        int base = li * 4;
        float  lightType   = LightData[base + 0].x;
        float  intensity   = LightData[base + 0].y;
        // float  range       = LightData[base + 0].z; // used per-type below
        float  castsShadow = LightData[base + 0].w;
        float3 lightVec    = LightData[base + 1].xyz; // direction or position
        float  innerCos    = LightData[base + 1].w;
        float3 lightColor  = LightData[base + 2].rgb;
        float  outerCos    = LightData[base + 2].w;
        float3 spotDir     = float3(LightData[base + 3].x, LightData[base + 3].y, LightData[base + 3].z);
        float  falloff     = LightData[base + 3].w;

        float3 L;
        float  attenuation = 1.0;

        if (lightType <= LIGHT_DIRECTIONAL + 0.1)
        {
            // Directional light: lightVec = direction TO light
            L = normalize(lightVec);
            attenuation = 1.0;
        }
        else if (lightType <= LIGHT_POINT + 0.1)
        {
            // Point light: lightVec = position
            float3 toLight = lightVec - worldPos;
            float dist = length(toLight);
            L = toLight / max(dist, 0.001);
            float range = LightData[base + 0].z;
            attenuation = DistanceAttenuation(dist, range, falloff);
        }
        else if (lightType <= LIGHT_SPOT + 0.1)
        {
            // Spot light: lightVec = position
            float3 toLight = lightVec - worldPos;
            float dist = length(toLight);
            L = toLight / max(dist, 0.001);
            float range = LightData[base + 0].z;
            float distAtt = DistanceAttenuation(dist, range, falloff);
            float spotAtt = SpotAttenuation(L, spotDir, innerCos, outerCos);
            attenuation = distAtt * spotAtt;
        }
        else
        {
            continue; // unknown light type
        }

        float NdotL = max(dot(worldN, L), 0.0);

        if (NdotL <= 0.0 || attenuation <= 0.001)
            continue;

        float3 H = normalize(L + V);
        float NdotH = max(dot(worldN, H), 0.0);
        float HdotV = max(dot(H, V), 0.0);
        float LdotH = max(dot(L, H), 0.0);

        // Cook-Torrance BRDF
        float D = GGX_D(NdotH, roughness);
        float G = Smith_G(NdotL, NdotV, roughness);
        float3 F = Schlick_F(HdotV, F0);

        float3 specular = (D * F * G) / max(4.0 * NdotV, 0.001);
        float3 diffuse  = DisneyDiffuse(albedo, NdotL, NdotV,
                                         LdotH, roughness, metallic);

        // Shadow mapping (directional light with shadows enabled)
        float shadow = 1.0;
        if (lightType <= LIGHT_DIRECTIONAL + 0.1 && castsShadow > 0.5)
        {
            shadow = SampleShadow(worldPos, L, LightViewProj, NdotL);
        }

        // lightColor is already pre-multiplied with intensity by Light.Pack()
        float3 lightContrib = (diffuse * NdotL + specular) * lightColor * attenuation * shadow;
        totalColor += lightContrib;
    }

    // ── Indirect IBL ─────────────────────────────────────────────────────────
    float3 R = reflect(-V, worldN);
    // The environment lookup uses the off-specular peak direction (UE): a rough
    // surface reflects dominantly slightly toward the normal. SSR keeps the mirror
    // direction R (computed in the SSR pass), matching UE's split.
    float3 envR = GetOffSpecularPeakReflectionDir(worldN, R, roughness);

    float3 F_ibl = Schlick_F(NdotV, F0);
    float3 kD = (1.0 - F_ibl) * (1.0 - metallic);

    // Diffuse IBL
    float3 diffuseIBL = IrradianceMap.Sample(IrradianceSampler, DirToEquirect(worldN)).rgb
                       * albedo * kD;

    // Specular IBL
    uint envW, envH, mipCount;
    PrefilteredEnvMap.GetDimensions(0, envW, envH, mipCount);
    float  maxMip = float(mipCount - 1);
    float  mipLevel = roughness * maxMip;
    float3 prefilteredColor = SampleEnvMap(PrefilteredEnvMap, PrefilteredSampler, envR, mipLevel);
    float2 brdf = EnvBRDFApprox(NdotV, roughness);
    float3 envBRDF = (F0 * brdf.x + brdf.y) * specOcclusion;

    // UE-style SSR compositing (ReflectionEnvironmentShaders.usf, ReflectionApplyPS).
    // The SSR target is premultiplied: rgb = reflectedColor * coverage, a = coverage.
    // Composite with the premultiplied "over" operator so the SSR coverage OCCLUDES
    // the environment specular along the shared reflection direction R: a confident
    // hit (a -> 1, e.g. the teapot on a smooth floor) drives the HDRI specular toward
    // zero instead of stacking on top of it; a miss (a = 0) reveals the environment.
    // The Fresnel / split-sum term (envBRDF) is applied to the combined reflection
    // afterwards, so both sources are scaled by the surface reflectance, as UE does.
    float3 reflection = ssrColor.rgb + prefilteredColor * (1.0 - ssrColor.a);
    float3 specularIBL = reflection * envBRDF;

    float3 indirectLight = (diffuseIBL + specularIBL) * EnvIntensity * ao;
    totalColor += indirectLight;

    // Debug: when EnvIntensity slider is at exact 0, output albedo for diagnosis
    if (EnvIntensity < 0.001)
        return float4(albedo, 1.0);

    // HDR output — tonemap is done separately in TonemapPass
    return float4(totalColor, 1.0);
}
