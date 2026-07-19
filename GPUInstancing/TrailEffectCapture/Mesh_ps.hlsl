// Simple directional light pixel shader for the head mesh.

float3 LightDir : register(c16);   // normalized world-space light direction

float4 PSMain(
    float4 svPos    : SV_POSITION,
    float3 worldPos : TEXCOORD0,
    float3 normal   : TEXCOORD1
) : SV_TARGET0
{
    float3 N = normalize(normal);
    float NdotL = saturate(dot(N, normalize(LightDir)));
    float ambient = 0.15;
    float3 color = float3(0.3, 0.6, 1.0) * (ambient + NdotL * 0.85);
    return float4(color, 1.0);
}
