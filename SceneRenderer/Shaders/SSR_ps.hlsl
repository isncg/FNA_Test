// SSR_ps.hlsl — Screen-Space Reflections via linear ray marching.
//
// Reads GBuffer:
//   t0/s0: GBufferRT0 (albedo.RGB, bakedAO.A)
//   t1/s1: GBufferRT1 (worldNormal.RGB*0.5+0.5, roughness.A)
//   t2/s2: GBufferRT2 (metallic.R, linearDepth.G)
//   t3/s3: SceneHistory (previous frame's lit HDR, after the skybox pass)
//
// Algorithm:
//   1. Reconstruct view-space position from depth + inverse projection
//   2. Compute view-space reflection direction R = reflect(V, N)
//   3. Linear ray march along R in view space
//   4. At each step, project to screen UV, compare signed depth difference
//   5. Detect surface crossing (sign change of depthDiff), then binary-search
//      refinement (5 iters) for a precise intersection — catches the first
//      surface the ray crosses regardless of its camera-facing thickness
//   6. Reproject the refined hit into the previous frame and sample the lit
//      history (UE5-style), apply roughness blur (cone-tracing approx)
//   7. On miss: return coverage 0 (fallback to IBL in lighting pass)
//
// Output (premultiplied, UE ReflectionApplyPS convention):
//   rgb = reflectedColor * coverage, a = coverage, where coverage is the
//   UE-style roughness mask saturate(roughness * RoughnessMaskMul + 2).
//   The lighting pass composites SSR.rgb + EnvSpecular * (1 - a), so the SSR
//   coverage occludes the environment specular along the reflection direction.

Texture2D    GBufferRT0      : register(t0);
SamplerState GBuffer0Sampler : register(s0);
Texture2D    GBufferRT1      : register(t1);
SamplerState GBuffer1Sampler : register(s1);
Texture2D    GBufferRT2      : register(t2);
SamplerState GBuffer2Sampler : register(s2);
Texture2D    SceneHistory    : register(t3);
SamplerState HistorySampler  : register(s3);

float4x4 ViewProj       : register(c0);
float4x4 InvViewProj    : register(c4);
float3   EyePosition    : register(c8);
float4   SSRParams      : register(c11); // x=maxSteps, y=stepSize, z=maxRoughness, w=roughnessMaskMul
float4x4 Projection     : register(c12);
float4x4 PrevViewProj   : register(c16); // reproject hits into the previous frame

#define SSR_MAX_STEPS      int(SSRParams.x)
#define SSR_STEP_SIZE      SSRParams.y
#define SSR_MAX_ROUGHNESS  SSRParams.z
#define SSR_ROUGHNESS_MASK_MUL  SSRParams.w

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

    // Crossing-based hit detection: track the signed depth difference between
    // the ray and the scene surface.  A sign change (prevDiff <= 0 -> diff > 0)
    // means the ray crossed a surface between consecutive steps.  This replaces
    // the old thickness-window test (depthDiff in (0, 0.5)) which compared the
    // ray against the camera-facing GBuffer depth: for objects reflected from
    // below (teapot in a floor), the ray approaches the UNDERSIDE while the
    // GBuffer stores the TOP surface.  The thickness window rejected the true
    // underside crossing (depthDiff ~ object height > 0.5) and only registered
    // a hit near the top — shifting each object's reflection by its own height
    // and producing depth-dependent "layering" that worsened for parts closer
    // to the floor.  Crossing detection catches the first intersection (the
    // underside) correctly regardless of object thickness.
    float prevDiff = -1.0; // assume the jittered start is in front of surfaces

    for (int i = 0; i < steps; i++)
    {
        float3 prevPos = rayPos;
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

        // Skip sky / uninitialized depth
        if (sampleDepth <= 0.0 || sampleDepth >= 1000.0)
        {
            prevDiff = -1.0;
            continue;
        }

        float depthDiff = rayPos.z - sampleDepth;

        // Crossing: previous step was in front (or at) the surface, this step
        // is behind.  The small epsilon rejects grazing self-intersections.
        if (prevDiff <= 0.0 && depthDiff > 0.001)
        {
            // ── Binary search refinement ──────────────────────────────────
            // Narrow the crossing between prevPos (in front) and rayPos
            // (behind).  5 iterations reduce the step-size uncertainty to
            // ~3%, giving a precise intersection for history reprojection.
            float3 hi = rayPos;   // behind surface
            float3 lo = prevPos;  // in front of surface
            [unroll]
            for (int r = 0; r < 5; r++)
            {
                float3 mid = (lo + hi) * 0.5;
                float4 mp;
                mp.x = mid.x * Projection._11;
                mp.y = mid.y * Projection._22;
                mp.z = mid.z * Projection._33 + Projection._43;
                mp.w = mid.z;
                float2 midUV = float2(
                    (mp.x / mp.w) * 0.5 + 0.5,
                    (1.0 - mp.y / mp.w) * 0.5);
                float midDepth = GBufferRT2.Sample(GBuffer2Sampler, midUV).g;
                if (mid.z > midDepth)
                    hi = mid;
                else
                    lo = mid;
            }
            rayPos = hi;  // precise intersection point
            // Coverage = occlusion mask for the environment specular (UE-style).
            // A confirmed hit is a reliable occluder of the infinitely-far
            // environment. UE shapes the confidence with a roughness mask
            // (ScreenSpaceReflections.usf, GetRoughnessFade):
            //     fade = saturate(roughness * RoughnessMaskMul + 2)
            // which (with the default negative mask) holds full confidence for
            // smooth and medium lobes and only fades the rough ones — the rough
            // lobe blur is left to the 3x3 history cone-trace above, not to the
            // confidence. This replaces the earlier linear (1 - roughness), which
            // attenuated medium-roughness reflections too aggressively. No
            // distance fade and no screen-edge fade weight the coverage — those
            // collapsed it for the near-floor teapot reflection (which projects to
            // the bottom border), leaking the HDRI window on top of the
            // reflection. Off-screen rays are handled by the march bounds check
            // above (no hit -> coverage 0 -> environment fallback), as in UE.
            float coverage = saturate(roughness * SSR_ROUGHNESS_MASK_MUL + 2.0);

            // UE5-style temporal reflection: sample the previous frame's lit
            // HDR scene (direct light + IBL + sky already included) instead of
            // the flat GBuffer albedo. Reconstruct the hit's world position,
            // reproject it into the previous frame, and sample the history.
            float3 hitViewRH = float3(rayPos.x, rayPos.y, -rayPos.z);
            float4 hitWorldH = mul(float4(hitViewRH, 1.0), invView);
            float3 hitWorld = hitWorldH.xyz / hitWorldH.w;

            float4 prevClip = mul(float4(hitWorld, 1.0), PrevViewProj);
            float2 prevUV;
            prevUV.x = (prevClip.x / prevClip.w) * 0.5 + 0.5;
            prevUV.y = (1.0 - prevClip.y / prevClip.w) * 0.5;

            float3 hitColor;
            if (prevUV.x < 0.0 || prevUV.x > 1.0 || prevUV.y < 0.0 || prevUV.y > 1.0)
            {
                // Disoccluded / off-screen in the previous frame: degrade to the
                // flat albedo rather than smearing clamped history edge texels.
                hitColor = GBufferRT0.Sample(GBuffer0Sampler, sampleUV).rgb;
            }
            else
            {
                // Roughness blur approximation (cone-tracing) in history space
                float blurRadius = roughness * 2.0;
                float2 texelSize = float2(1.0 / rtW, 1.0 / rtH);
                hitColor = float3(0, 0, 0);
                [unroll]
                for (int bx = -1; bx <= 1; bx++)
                {
                    [unroll]
                    for (int by = -1; by <= 1; by++)
                    {
                        hitColor += SceneHistory.Sample(HistorySampler,
                            prevUV + float2(bx, by) * texelSize * blurRadius).rgb;
                    }
                }
                hitColor /= 9.0;
            }

            // Premultiplied output: rgb = reflectedColor * coverage, a = coverage.
            // The lighting pass composites with the "over" operator
            // (SSR.rgb + Env * (1 - a)), matching UE's ReflectionApplyPS.
            reflectionColor = float4(hitColor * coverage, coverage);
            break;
        }

        prevDiff = depthDiff;
    }

    return reflectionColor;
}
