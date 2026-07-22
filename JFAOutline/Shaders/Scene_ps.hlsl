// Diffuse-lit scene with procedural checkerboard (no texture binding needed).
// This isolates whether the issue is texture binding or parameter passing.

float4 DiffuseColor : register(c4);
float4 LightDir     : register(c5);
float4 AmbientColor : register(c6);

static const float3 LightCol = float3(1.0, 0.95, 0.85); // warm light

float4 PSMain_Scene(
    float3 worldNormal   : TEXCOORD0,
    float2 texCoord      : TEXCOORD1,
    float3 worldPosition : TEXCOORD2
) : SV_Target0
{
    float3 N = normalize(worldNormal);
    float3 L = normalize(LightDir.xyz);

    // Procedural checkerboard from texcoord (no texture sample)
    float2 uv = texCoord * 8.0;
    int2 cell = int2(floor(uv));
    float pattern = ((cell.x + cell.y) & 1) == 0 ? 1.0 : 0.4;

    // Diffuse Lambertian
    float NdotL = saturate(dot(N, L));

    // Ambient + diffuse + pattern
    float3 baseColor = pattern * DiffuseColor.rgb;
    float3 ambient = AmbientColor.rgb * baseColor;
    float3 diffuse = NdotL * LightCol * baseColor;
    float3 lit = ambient + diffuse;

    return float4(lit, 1.0);
}
