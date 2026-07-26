// BlurAO_ps.hlsl — Bilateral blur for SSAO de-noising.
//
// Blurs the noisy AO texture while preserving edges detected via
// depth discontinuities from the G-Buffer (A channel = view-space Z).
//
// A 5×5 cross-bilateral kernel: spatial Gaussian × depth Gaussian.

Texture2D<float> AOTex      : register(t0);
SamplerState     AOSampler  : register(s0);
Texture2D<float4> GBufferTex : register(t1);
SamplerState      GBufferSampler : register(s1);

float2  TexelSize  : register(c0); // (1/width, 1/height)
float   BlurSharpness : register(c1); // depth kernel width (smaller = sharper edges)

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD0;
};

// Spatial Gaussian weight (precomputed for radius 2 kernel)
static const float kWeights[5] = { 0.06136, 0.24477, 0.38774, 0.24477, 0.06136 };

float4 PSMain(PS_INPUT input) : SV_TARGET0
{
    // Compute UV from SV_POSITION (y=0 at top, matching SSAO pass convention)
    uint aow, aoh;
    AOTex.GetDimensions(aow, aoh);
    float2 uv = input.Position.xy / float2(aow, aoh);

    float centerDepth = GBufferTex.Sample(GBufferSampler, uv).a;

    // Skip sky pixels — no blur needed
    if (centerDepth <= 0.0 || centerDepth >= 100.0)
        return AOTex.Sample(AOSampler, uv);

    float sumAO   = 0.0;
    float sumW    = 0.0;

    [unroll]
    for (int y = -2; y <= 2; y++)
    {
        [unroll]
        for (int x = -2; x <= 2; x++)
        {
            float2 sampleUV = uv + float2(x, y) * TexelSize;
            float sampleDepth = GBufferTex.Sample(GBufferSampler, sampleUV).a;
            float sampleAO = AOTex.Sample(AOSampler, sampleUV);

            // Spatial weight
            float w = kWeights[x + 2] * kWeights[y + 2];

            // Depth weight: penalise large depth differences
            float depthDiff = abs(centerDepth - sampleDepth);
            float depthW = exp(-depthDiff / max(BlurSharpness, 0.001));

            sumAO += sampleAO * w * depthW;
            sumW  += w * depthW;
        }
    }

    float ao = sumAO / max(sumW, 0.0001);
    return ao;
}
