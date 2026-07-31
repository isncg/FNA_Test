// GBuffer_ps.hlsl — Deferred G-Buffer pixel shader.
//
// Outputs 3 MRTs:
//   SV_TARGET0 (Color RGBA8):     RGB = albedo, A = baked AO
//   SV_TARGET1 (HalfVector4 FP16): RGB = world-space normal, A = perceptual roughness
//   SV_TARGET2 (HalfVector4 FP16): R = metallic, G = linear view depth, BA = motion vectors
//
// Material textures: AlbedoMap (t0), NormalMap (t1), ORMMap (t2: R=AO, G=Roughness, B=Metallic)

Texture2D    AlbedoMap    : register(t0);
SamplerState AlbedoSampler : register(s0);
Texture2D    NormalMap     : register(t1);
SamplerState NormalSampler : register(s1);
Texture2D    ORMMap        : register(t2);
SamplerState ORMSampler    : register(s2);

float3 AlbedoTint    : register(c12);
float  MetallicScale  : register(c13);
float  RoughnessScale : register(c14);
float4x4 PrevViewProj : register(c15);

struct PS_INPUT
{
    float4 PositionCS  : SV_POSITION;
    float3 WorldPos    : TEXCOORD0;
    float3 WorldNormal : TEXCOORD1;
    float2 TexCoord    : TEXCOORD2;
    float  ViewDepth   : TEXCOORD3; // clip.w = -viewSpace.z (linear depth)
};

struct PS_OUTPUT
{
    float4 AlbedoAO      : SV_TARGET0; // RGB=albedo, A=baked AO
    float4 NormalRough   : SV_TARGET1; // RGB=world normal, A=roughness
    float4 MetalDepthMV  : SV_TARGET2; // R=metallic, G=linear view depth, BA=motion vectors
};

PS_OUTPUT PSMain(PS_INPUT input)
{
    PS_OUTPUT o;

    // Sample material textures
    float3 albedo = AlbedoMap.Sample(AlbedoSampler, input.TexCoord).rgb * AlbedoTint;
    float3 normalTex = NormalMap.Sample(NormalSampler, input.TexCoord).rgb;
    float3 orm = ORMMap.Sample(ORMSampler, input.TexCoord).rgb;

    // Decode tangent-space normal from [0,1] to [-1,1]
    float3 tNormal = normalTex * 2.0 - 1.0;

    // Build tangent frame from world normal
    float3 N = normalize(input.WorldNormal);
    float3 up = abs(N.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
    float3 T = normalize(cross(up, N));
    float3 B = cross(N, T); // right-handed TBN

    float3 worldN = normalize(tNormal.x * T + tNormal.y * B + tNormal.z * N);

    // Material parameters
    float roughness = clamp(orm.g * RoughnessScale, 0.04, 1.0);
    float metallic  = clamp(orm.b * MetallicScale, 0.0, 1.0);
    float bakedAO   = orm.r;

    // Linear view-space depth (positive = distance from camera plane).
    // Passed as TEXCOORD from VS (= clip.w = -viewSpace.z), NOT read from
    // SV_POSITION.w which is 1/clip.w in Vulkan SPIR-V.
    float linearDepth = input.ViewDepth;

    // Motion vectors: screen-space UV delta from previous frame.
    // Use explicit dot products instead of mul(float4, float4x4) to avoid
    // a Vulkan SPIR-V issue with OpMatrixTimesVector on this driver.
    float4 worldPos4 = float4(input.WorldPos, 1.0);
    float4 prevClip;
    prevClip.x = dot(worldPos4, PrevViewProj[0]);
    prevClip.y = dot(worldPos4, PrevViewProj[1]);
    prevClip.z = dot(worldPos4, PrevViewProj[2]);
    prevClip.w = dot(worldPos4, PrevViewProj[3]);
    float2 currentUV = input.TexCoord;
    float2 prevUV;
    prevUV.x = (prevClip.x / max(abs(prevClip.w), 1e-6)) * 0.5 + 0.5;
    prevUV.y = (prevClip.y / max(abs(prevClip.w), 1e-6)) * -0.5 + 0.5;
    float2 motion = currentUV - prevUV;

    // Pack world normal to [0,1] for storage in UNORM-friendly range
    o.AlbedoAO     = float4(albedo, bakedAO);
    o.NormalRough  = float4(worldN * 0.5 + 0.5, roughness);
    o.MetalDepthMV = float4(metallic, linearDepth, motion);

    return o;
}
