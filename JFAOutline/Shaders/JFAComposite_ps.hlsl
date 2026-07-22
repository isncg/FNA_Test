// JFA Composite: Scene + outline + X-ray occlusion fill.

Texture2D<float4> SceneTexture   : register(t0);
SamplerState      SceneSampler   : register(s0);
Texture2D<float2> JFATexture     : register(t1);
SamplerState      JFASampler     : register(s1);
Texture2D<float4> FullMaskTex    : register(t2);
SamplerState      FullMaskSamp   : register(s2);
Texture2D<float4> VisibleMaskTex : register(t3);
SamplerState      VisibleMaskSamp: register(s3);

float  OutlineWidth : register(c0);
float4 OutlineColor : register(c1);
float4 ScreenSize   : register(c2); // (w, h, 1/w, 1/h)
float4 XRayColor    : register(c3); // (r, g, b, alpha)
float  XRayEnable   : register(c4); // 1.0 = on, 0.0 = off

float4 PSMain(float4 svPos : SV_Position, float2 texCoord : TEXCOORD0) : SV_Target0
{
    float4 sceneColor = SceneTexture.Sample(SceneSampler, texCoord);

    int3 loadCoord = int3(svPos.xy, 0);

    // Point-sample JFA result
    float2 seedUV = JFATexture.Load(loadCoord).xy;

    if (seedUV.x < 0.0)
        return sceneColor; // no mask object nearby

    // Distance to nearest mask edge in pixels
    float dist = length((texCoord - seedUV) * ScreenSize.xy);

    // Sample masks (point samples for exact comparison)
    float fullMask    = FullMaskTex.Load(loadCoord).r;
    float visibleMask = VisibleMaskTex.Load(loadCoord).r;

    // Outline band (drawn around the FULL silhouette)
    if (dist > 0.0 && dist <= OutlineWidth)
    {
        float t = saturate(dist / max(OutlineWidth, 0.001));
        return lerp(OutlineColor, sceneColor, t);
    }

    // Inside object silhouette
    if (fullMask > 0.5)
    {
        if (visibleMask > 0.5)
        {
            // Visible surface → show scene color
            return sceneColor;
        }
        else if (XRayEnable > 0.5)
        {
            // Occluded interior → show X-ray color with alpha blend
            float3 xray = XRayColor.rgb * XRayColor.a + sceneColor.rgb * (1.0 - XRayColor.a);
            return float4(xray, 1.0);
        }
    }

    return sceneColor;
}
