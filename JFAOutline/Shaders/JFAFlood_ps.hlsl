// JFA Flood: One propagation step.
// Samples 8 neighbors at stepSize distance, adopts closest valid seed.

Texture2D<float2> InputTexture : register(t0);
SamplerState InputSampler : register(s0);

float StepSize  : register(c0);
float4 RTInvSize : register(c1); // (1/w, 1/h, w, h)

// 8 neighbor directions at step distance (JFA standard kernel)
static const int2 kOffsets[8] = {
    int2(-1, -1), int2(0, -1), int2(1, -1),
    int2(-1,  0),              int2(1,  0),
    int2(-1,  1), int2(0,  1), int2(1,  1)
};

float2 PSMain(float4 svPos : SV_Position, float2 texCoord : TEXCOORD0) : SV_Target0
{
    int2 pixelCoord = int2(svPos.xy);
    int2 texSize = int2(RTInvSize.zw);
    int iStep = int(StepSize);

    // Read current best seed
    float2 currentSeed = InputTexture.Load(int3(pixelCoord, 0)).xy;
    bool currentValid = (currentSeed.x >= 0.0);
    float bestDist = 1e10;

    if (currentValid)
    {
        bestDist = length((texCoord - currentSeed) * RTInvSize.zw);
    }

    // Check 8 neighbors
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        int2 neighborCoord = pixelCoord + kOffsets[i] * iStep;
        neighborCoord = clamp(neighborCoord, int2(0, 0), texSize - 1);

        float2 neighborSeed = InputTexture.Load(int3(neighborCoord, 0)).xy;
        if (neighborSeed.x >= 0.0)
        {
            float d = length((texCoord - neighborSeed) * RTInvSize.zw);
            if (d < bestDist)
            {
                bestDist = d;
                currentSeed = neighborSeed;
            }
        }
    }

    return currentSeed;
}
