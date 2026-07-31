// SSR_ps.hlsl — Screen-Space Reflections via linear ray marching.
//
// Reads GBuffer:
//   t0/s0: GBufferRT0 (albedo.RGB, bakedAO.A)
//   t1/s1: GBufferRT1 (worldNormal.RGB*0.5+0.5, roughness.A)
//   t2/s2: GBufferRT2 (metallic.R, linearDepth.G)
//
// Algorithm:
//   1. Reconstruct view-space position from depth + inverse projection
//   2. Compute view-space reflection direction R = reflect(V, N)
//   3. Linear ray march along R in view space
//   4. At each step, project to screen UV, compare depth
//   5. On hit: sample albedo, apply Fresnel fade + roughness blur
//   6. On miss: return transparent (fallback to IBL in lighting pass)
//   7. Edge fade to avoid pop at screen borders

Texture2D    GBufferRT0      : register(t0);
SamplerState GBuffer0Sampler : register(s0);
Texture2D    GBufferRT1      : register(t1);
SamplerState GBuffer1Sampler : register(s1);
Texture2D    GBufferRT2      : register(t2);
SamplerState GBuffer2Sampler : register(s2);

float4x4 ViewProj       : register(c0);
float4x4 InvViewProj    : register(c4);
float3   EyePosition    : register(c8);
float4   SSRParams      : register(c11); // x=maxSteps, y=stepSize, z=maxRoughness, w=fadeDistance
float4x4 Projection     : register(c12);

#define SSR_MAX_STEPS      int(SSRParams.x)
#define SSR_STEP_SIZE      SSRParams.y
#define SSR_MAX_ROUGHNESS  SSRParams.z
#define SSR_FADE_DISTANCE  SSRParams.w

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD0;
};

float4 PSMain(PS_INPUT input) : SV_TARGET0
{
    uint rtW, rtH;
    GBufferRT0.GetDimensions(rtW, rtH);
    float2 uv = input.Position.xy / float2(rtW, rtH);

    // Sample GBuffer
    float4 gb0 = GBufferRT0.Sample(GBuffer0Sampler, uv);
    float4 gb1 = GBufferRT1.Sample(GBuffer1Sampler, uv);
    float4 gb2 = GBufferRT2.Sample(GBuffer2Sampler, uv);

    float3 albedo    = gb0.rgb;
    float3 worldN    = normalize(gb1.rgb * 2.0 - 1.0);
    float  roughness = gb1.a;
    float  metallic  = gb2.r;
    float  viewZ     = gb2.g;

    // Skip if too rough or sky
    if (roughness > SSR_MAX_ROUGHNESS || viewZ <= 0.0 || viewZ >= 1000.0)
        return float4(0, 0, 0, 0);

    // Reconstruct accurate world position from GBuffer view-space depth.
    // viewZ = SV_POSITION.w = -viewSpace.z (positive distance).
    //
    // Two view-space representations are needed:
    //   viewPos (+)    — positive Z, for ray-march depth comparisons against GBuffer depth
    //   viewPosWorld (-) — correct right-handed view-space Z for world-position reconstruction
    float2 ndc = float2(uv.x * 2.0 - 1.0, (1.0 - uv.y) * 2.0 - 1.0);
    float3 viewPos;
    viewPos.z = viewZ;
    viewPos.x = ndc.x * viewZ / Projection._11;
    viewPos.y = ndc.y * viewZ / Projection._22;

    // World position (uses correct view-space Z, negative in front of camera)
    float3 viewPosWorld = viewPos;
    viewPosWorld.z = -viewZ;
    float4x4 invView = mul(Projection, InvViewProj);
    float4 worldPosH = mul(float4(viewPosWorld, 1.0), invView);
    float3 worldPos = worldPosH.xyz / worldPosH.w;

    // View direction
    float3 V = normalize(EyePosition - worldPos);

    // Reflection direction
    float3 R = reflect(-V, worldN);

    // View-space ray marching (viewPos already reconstructed above)
    // Transform world-space R to view-space: undo projection scaling from ViewProj
    float3 viewR = mul(float4(R, 0.0), ViewProj).xyz;
    viewR.x /= Projection._11;
    viewR.y /= Projection._22;
    float3 viewDir = normalize(viewR);

    // Adapt step count based on roughness
    int steps = SSR_MAX_STEPS;
    float stepSize = SSR_STEP_SIZE;

    // Ray march
    float3 rayPos = viewPos;
    float3 rayStep = viewDir * stepSize;
    float4 reflectionColor = float4(0, 0, 0, 0);

    // Jitter start position to avoid self-intersection
    rayPos += rayStep * 0.5;

    for (int i = 0; i < steps; i++)
    {
        rayPos += rayStep;

        // Project view-space position to screen UV
        float4 projSample;
        projSample.x = rayPos.x * Projection._11;
        projSample.y = rayPos.y * Projection._22;
        projSample.z = rayPos.z * Projection._33 + Projection._43;
        projSample.w = rayPos.z;

        float2 sampleUV;
        sampleUV.x = (projSample.x / projSample.w) * 0.5 + 0.5;
        sampleUV.y = (1.0 - projSample.y / projSample.w) * 0.5;

        // Check bounds
        if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0)
            break;

        // Sample depth at projected position
        float sampleDepth = GBufferRT2.Sample(GBuffer2Sampler, sampleUV).g;

        // Hit check
        float depthDiff = rayPos.z - sampleDepth;
        if (depthDiff > 0.0 && depthDiff < 0.5)
        {
            // Hit: sample albedo
            float3 hitAlbedo = GBufferRT0.Sample(GBuffer0Sampler, sampleUV).rgb;

            // Fresnel fading based on step distance
            float fade = 1.0 - (float(i) / float(steps));
            fade = smoothstep(0.0, 1.0, fade);

            // Roughness blur approximation (cone-tracing)
            float blurRadius = roughness * 2.0;
            float2 texelSize = float2(1.0 / rtW, 1.0 / rtH);
            float3 blurredColor = float3(0, 0, 0);
            [unroll]
            for (int bx = -1; bx <= 1; bx++)
            {
                [unroll]
                for (int by = -1; by <= 1; by++)
                {
                    blurredColor += GBufferRT0.Sample(GBuffer0Sampler,
                        sampleUV + float2(bx, by) * texelSize * blurRadius).rgb;
                }
            }
            blurredColor /= 9.0;

            reflectionColor = float4(blurredColor * fade, fade);
            break;
        }
    }

    // Edge fade: reduce reflection near screen borders
    float edgeFadeX = smoothstep(0.0, 0.15, uv.x) * smoothstep(1.0, 0.85, uv.x);
    float edgeFadeY = smoothstep(0.0, 0.15, uv.y) * smoothstep(1.0, 0.85, uv.y);
    reflectionColor.rgb *= edgeFadeX * edgeFadeY;

    return reflectionColor;
}
