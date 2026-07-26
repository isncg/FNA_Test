// SSAO_ps.hlsl — Screen-Space Ambient Occlusion.
//
// Reads a single packed G-Buffer texture (HalfVector4):
//   RGB = view-space normal  (no encoding — FP16 stores signed values)
//   A   = linear view-space depth (>0 in front of camera)
//
// 16 hemisphere samples with random rotation, range check, and smooth falloff.
//
// Reference: FNA3D_HLSL_Test ssao_ps.hlsl.  Differences from reference:
//   1. Packed G-Buffer (RGB=normal, A=depth) instead of MRT
//   2. UV computed from SV_POSITION (matches reference quad: UV.y=0 at top)
//   3. sampleTan.z negated — RH→pseudo-LH hemisphere flip
//   4. Angle-adaptive radius/bias to suppress grazing-angle artefacts

Texture2D<float4> GBufferTex  : register(t0);
SamplerState      GBufferSampler : register(s0);

float4x4 Projection : register(c0);
float4   SSAOParams : register(c4);

#define SSAO_RADIUS    SSAOParams.x
#define SSAO_BIAS      SSAOParams.y
#define SSAO_INTENSITY SSAOParams.z

/* 16 hemisphere samples (tangent space, z = hemisphere up) */
static const float3 gSamples[16] =
{
    float3( 0.5381,  0.1856,  0.4319),
    float3( 0.1379,  0.7723,  0.4539),
    float3(-0.4292, -0.1142,  0.7212),
    float3( 0.4519, -0.3659,  0.6540),
    float3(-0.7832,  0.3897,  0.2581),
    float3( 0.1810, -0.4989,  0.4318),
    float3( 0.6597,  0.6147,  0.2391),
    float3(-0.2563,  0.8102,  0.3471),
    float3(-0.4152, -0.7027,  0.4473),
    float3( 0.8838, -0.1142,  0.3151),
    float3(-0.5258,  0.0894,  0.7721),
    float3( 0.2947,  0.2147,  0.8339),
    float3(-0.0853, -0.3145,  0.8671),
    float3(-0.6723, -0.5138,  0.5374),
    float3( 0.0658,  0.9215,  0.2756),
    float3( 0.7381, -0.4273,  0.5585),
};

/* Simple hash for random rotation per pixel */
static float hash2D(float2 uv)
{
    return frac(sin(dot(uv, float2(127.1, 311.7))) * 43758.5453);
}

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD0; // unused — UV computed from SV_POSITION below
};

float4 PSMain(PS_INPUT input) : SV_TARGET0
{
    // Compute UV from SV_POSITION (D3D viewport: y=0 at top, y+ = down).
    // This matches the reference quad where UV.y=0 at the top of the screen.
    uint gbW, gbH;
    GBufferTex.GetDimensions(gbW, gbH);
    float2 uv = input.Position.xy / float2(gbW, gbH);

    /* Sample G-Buffer */
    float4 gbuffer = GBufferTex.Sample(GBufferSampler, uv);
    // GBuffer stores RH view-space normal where front-facing N.z > 0.
    // Reference works in LH space where front-facing N.z < 0.
    // Negate N.z so the TBN matches the reference exactly, giving the
    // correct hemisphere distribution (especially on tilted surfaces).
    float3 N       = normalize(float3(gbuffer.rg, -gbuffer.b));
    float  viewZ   = gbuffer.a;

    /* Skip sky / far plane pixels */
    if (viewZ <= 0.0 || viewZ >= 100.0)
        return 1.0;

    /* NDC from UV — reference formula (UV.y=0 at top → ndc.y=+1 at top) */
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);

    /* Reconstruct view-space position */
    float3 viewPos;
    viewPos.x = ndc.x * viewZ / Projection._11;
    viewPos.y = ndc.y * viewZ / Projection._22;
    viewPos.z = viewZ;

    /* Random rotation from hash */
    float  r   = hash2D(uv) * 6.2831853; /* 2*PI */
    float2 rot = float2(cos(r), sin(r));

    /* Build tangent-to-view transform.
       Reference uses abs(N.z) but that degenerates when N ≈ (0,1,0) (floor).
       Check abs(N.y) so the up vector (0,1,0) is only used when it isn't
       parallel to N — same pattern used in IrradianceConv / PBR tangent frames. */
    float3 T, B;
    if (abs(N.y) < 0.999)
    {
        T = normalize(cross(N, float3(0.0, 1.0, 0.0)));
    }
    else
    {
        T = normalize(cross(N, float3(1.0, 0.0, 0.0)));
    }
    B = cross(N, T);

    // At grazing angles depth changes rapidly → scale radius down, bias up.
    float angleAtten  = saturate(abs(N.z) * 2.0);
    float radius      = SSAO_RADIUS * lerp(0.3, 1.0, angleAtten);
    float adaptedBias = SSAO_BIAS / lerp(0.15, 1.0, angleAtten);

    float occlusion = 0.0;

    for (int i = 0; i < 16; i++)
    {
        /* Scale samples to fill the hemisphere volume,
           biased toward the origin for a smoother falloff */
        float scale = (float) i / 16.0;
        scale = lerp(0.1, 1.0, scale * scale);

        /* Rotate hemisphere sample in tangent space */
        float3 sampleDir = gSamples[i] * scale;
        float3 sampleTan;
        sampleTan.x = sampleDir.x * rot.x + sampleDir.y * rot.y;
        sampleTan.y = sampleDir.x * -rot.y + sampleDir.y * rot.x;

        sampleTan.z = sampleDir.z;  // N.z already negated to match LH convention

        /* Transform to view space */
        float3 sampleView = sampleTan.x * T + sampleTan.y * B + sampleTan.z * N;

        /* Offset view-space position by sample */
        float3 samplePos = viewPos + sampleView * radius;

        /* Project to screen — reference formulas (LH projection) */
        float4 sampleClip;
        sampleClip.x = samplePos.x * Projection._11;
        sampleClip.y = samplePos.y * Projection._22;
        sampleClip.z = samplePos.z * Projection._33 + Projection._43;
        sampleClip.w = samplePos.z;

        float2 sampleUV;
        sampleUV.x = (sampleClip.x / sampleClip.w) * 0.5 + 0.5;
        sampleUV.y = (1.0 - sampleClip.y / sampleClip.w) * 0.5;

        /* Sample depth at projected position */
        float sampleDepth = GBufferTex.Sample(GBufferSampler, sampleUV).a;

        /* Range check: fade out contributions from geometry far
           outside the sampling radius (avoids halos) */
        float rangeCheck = smoothstep(0.0, 1.0,
            radius / max(abs(viewZ - sampleDepth), 1e-4));

        /* Occlusion: visible geometry at the probe's pixel is
           closer to the camera than the probe itself */
        occlusion += (sampleDepth < samplePos.z - adaptedBias ? 1.0 : 0.0) * rangeCheck;
    }

    occlusion /= 16.0;
    float ao = 1.0 - occlusion * SSAO_INTENSITY;
    ao = saturate(ao);

    return ao;
}
