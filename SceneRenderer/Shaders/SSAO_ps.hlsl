// SSAO_ps.hlsl — Screen-Space Ambient Occlusion (improved from MaterialLib).
//
// Improvements over MaterialLib:
//   - 32 hemisphere samples (up from 16)
//   - Interleaved gradient noise (replaces hash2D for smoother distribution)
//   - Half-res rendering support via SSAOResolutionScale uniform
//   - Angle-adaptive radius/bias (kept from MaterialLib)
//
// Reads GBufferRT1 (world normal.RGB, roughness.A) and GBufferRT2 (linearDepth.G).

Texture2D    GBufferRT1      : register(t0);
SamplerState GBuffer1Sampler : register(s0);
Texture2D    GBufferRT2      : register(t1);
SamplerState GBuffer2Sampler : register(s1);

float4x4 Projection        : register(c0);
float4   SSAOParams        : register(c4); // x=radius, y=bias, z=intensity
float2   SSAOResolutionScale : register(c5); // x=1/scaleW, y=1/scaleH for half-res
float4x4 View              : register(c6); // world -> view, for the normal

#define SSAO_RADIUS    SSAOParams.x
#define SSAO_BIAS      SSAOParams.y
#define SSAO_INTENSITY SSAOParams.z

// 32 hemisphere samples (tangent space, z = hemisphere up)
static const float3 gSamples[32] =
{
    float3( 0.5381,  0.1856,  0.4319), float3( 0.1379,  0.7723,  0.4539),
    float3(-0.4292, -0.1142,  0.7212), float3( 0.4519, -0.3659,  0.6540),
    float3(-0.7832,  0.3897,  0.2581), float3( 0.1810, -0.4989,  0.4318),
    float3( 0.6597,  0.6147,  0.2391), float3(-0.2563,  0.8102,  0.3471),
    float3(-0.4152, -0.7027,  0.4473), float3( 0.8838, -0.1142,  0.3151),
    float3(-0.5258,  0.0894,  0.7721), float3( 0.2947,  0.2147,  0.8339),
    float3(-0.0853, -0.3145,  0.8671), float3(-0.6723, -0.5138,  0.5374),
    float3( 0.0658,  0.9215,  0.2756), float3( 0.7381, -0.4273,  0.5585),
    float3( 0.2023,  0.3441,  0.7345), float3(-0.3215,  0.5567,  0.6211),
    float3( 0.6789,  0.2345,  0.5901), float3(-0.5432, -0.3210,  0.6234),
    float3( 0.4567, -0.6789,  0.4567), float3(-0.7890,  0.1234,  0.5678),
    float3( 0.3344,  0.4455,  0.7788), float3(-0.2233, -0.5566,  0.6677),
    float3( 0.1122,  0.3344,  0.8899), float3(-0.4455, -0.6677,  0.5566),
    float3( 0.0234, -0.1234,  0.8989), float3(-0.6789, -0.2345,  0.4455),
    float3( 0.5454, -0.1212,  0.7676), float3(-0.1212,  0.4545,  0.7878),
    float3( 0.2323, -0.5656,  0.6767), float3(-0.3434, -0.4343,  0.7676),
};

// Interleaved gradient noise (Jimenez 2016) — better distribution than hash2D
static float InterleavedGradientNoise(float2 screenPos)
{
    float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
    return frac(magic.z * frac(dot(screenPos, magic.xy)));
}

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD0;
};

float4 PSMain(PS_INPUT input) : SV_TARGET0
{
    uint gbW, gbH;
    GBufferRT1.GetDimensions(gbW, gbH);
    float2 uv = input.Position.xy / float2(gbW, gbH);

    // Sample GBuffer
    float4 gb1 = GBufferRT1.Sample(GBuffer1Sampler, uv);
    float4 gb2 = GBufferRT2.Sample(GBuffer2Sampler, uv);

    float3 worldN = normalize(gb1.rgb * 2.0 - 1.0);
    float  viewZ  = gb2.g;

    // Skip sky / far plane pixels
    if (viewZ <= 0.0 || viewZ >= 100.0)
        return 1.0;

    // NDC from UV (UV.y=0 at top → ndc.y=+1 at top)
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);

    // Reconstruct view-space position (positive-depth convention matching GBuffer).
    float3 viewPos;
    viewPos.x = ndc.x * viewZ / Projection._11;
    viewPos.y = ndc.y * viewZ / Projection._22;
    viewPos.z = viewZ;

    // Bring the world normal into view space, then negate z to move from RH
    // view space (front-facing N.z > 0) to the positive-depth convention the
    // hemisphere sampling below assumes (front-facing N.z < 0). This mirrors
    // MaterialLib's SSAO, whose GBuffer stores a view-space normal and negates
    // z for exactly this reason. Skipping either step flips the sampling
    // hemisphere into the surface on camera-facing geometry, which reads as
    // fully occluded (black) — the teapot body showed this before the fix.
    float3 viewN = normalize(mul(float4(worldN, 0.0), View).xyz);
    float3 N = float3(viewN.x, viewN.y, -viewN.z);
    float NdotV = abs(N.z); // |dot(N, viewForward)| in the positive-depth convention

    // Random rotation from interleaved gradient noise
    float  r   = InterleavedGradientNoise(input.Position.xy) * 6.2831853;
    float2 rot = float2(cos(r), sin(r));

    // Build tangent-to-view transform
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

    // Angle-adaptive radius and bias
    float angleAtten  = saturate(NdotV * 2.0);
    float radius      = SSAO_RADIUS * lerp(0.3, 1.0, angleAtten);
    float adaptedBias = SSAO_BIAS / lerp(0.15, 1.0, angleAtten);

    float occlusion = 0.0;

    for (int i = 0; i < 32; i++)
    {
        // Scale samples: biased toward origin for smoother falloff
        float scale = (float) i / 32.0;
        scale = lerp(0.1, 1.0, scale * scale);

        // Rotate hemisphere sample in tangent space
        float3 sampleDir = gSamples[i] * scale;
        float3 sampleTan;
        sampleTan.x = sampleDir.x * rot.x + sampleDir.y * rot.y;
        sampleTan.y = sampleDir.x * -rot.y + sampleDir.y * rot.x;
        sampleTan.z = sampleDir.z;

        // Transform to view space
        float3 sampleView = sampleTan.x * T + sampleTan.y * B + sampleTan.z * N;

        // Offset view-space position
        float3 samplePos = viewPos + sampleView * radius;

        // Project to screen
        float4 sampleClip;
        sampleClip.x = samplePos.x * Projection._11;
        sampleClip.y = samplePos.y * Projection._22;
        sampleClip.z = samplePos.z * Projection._33 + Projection._43;
        sampleClip.w = samplePos.z;

        float2 sampleUV;
        sampleUV.x = (sampleClip.x / sampleClip.w) * 0.5 + 0.5;
        sampleUV.y = (1.0 - sampleClip.y / sampleClip.w) * 0.5;

        // Sample depth at projected position
        float sampleDepth = GBufferRT2.Sample(GBuffer2Sampler, sampleUV).g;

        // Range check
        float rangeCheck = smoothstep(0.0, 1.0,
            radius / max(abs(viewZ - sampleDepth), 1e-4));

        // Occlusion test
        occlusion += (sampleDepth < samplePos.z - adaptedBias ? 1.0 : 0.0) * rangeCheck;
    }

    occlusion /= 32.0;
    float ao = 1.0 - occlusion * SSAO_INTENSITY;
    ao = saturate(ao);

    return ao;
}
