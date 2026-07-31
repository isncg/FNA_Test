// BlurAO_ps.hlsl — Bilateral blur for SSAO de-noising.
// 5x5 cross-bilateral kernel: spatial Gaussian × depth edge-stopping.
// (Identical to MaterialLib version.)

Texture2D    AOTex          : register(t0);
SamplerState AOSampler      : register(s0);
Texture2D    GBufferRT2     : register(t1);
SamplerState GBuffer2Sampler: register(s1);

float2 TexelSize     : register(c0);
float  BlurSharpness : register(c1);

static const float kWeights[5] = { 0.06136, 0.24477, 0.38774, 0.24477, 0.06136 };

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD0;
};

float4 PSMain(PS_INPUT input) : SV_TARGET0
{
    uint aow, aoh;
    AOTex.GetDimensions(aow, aoh);
    float2 uv = input.Position.xy / float2(aow, aoh);

    float centerDepth = GBufferRT2.Sample(GBuffer2Sampler, uv).g;

    if (centerDepth <= 0.0 || centerDepth >= 100.0)
        return AOTex.Sample(AOSampler, uv);

    float sumAO = 0.0;
    float sumW  = 0.0;

    [unroll]
    for (int y = -2; y <= 2; y++)
    {
        [unroll]
        for (int x = -2; x <= 2; x++)
        {
            float2 sampleUV = uv + float2(x, y) * TexelSize;
            float sampleDepth = GBufferRT2.Sample(GBuffer2Sampler, sampleUV).g;
            float sampleAO = AOTex.Sample(AOSampler, sampleUV);

            float w = kWeights[x + 2] * kWeights[y + 2];
            float depthDiff = abs(centerDepth - sampleDepth);
            float depthW = exp(-depthDiff / max(BlurSharpness, 0.001));

            sumAO += sampleAO * w * depthW;
            sumW  += w * depthW;
        }
    }

    return sumAO / max(sumW, 0.0001);
}
