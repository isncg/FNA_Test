// ParticleEffect vertex shader — GPU-instanced billboarded fire particles.
//
// Geometry buffer (slot 0, freq=0): 4 vertices with float2 CornerXY:
//   (-1,-1), (1,-1), (1,1), (-1,1)  — unit quad corners
// Instance buffer (slot 1, freq=1): N vertices with BirthData0 + BirthData1
//
// VS_INPUT field order MUST match the combined vertex declarations:
//   Location 0: CornerXY   (Vector2) — geometry buffer, slot 0, freq=0
//   Location 1: BirthData0 (Vector4) — instance buffer, slot 1, freq=1
//   Location 2: BirthData1 (Vector4) — instance buffer, slot 1, freq=1

float4x4 WorldViewProj : register(c0);
float    ElapsedTime   : register(c4);
float4   CameraRight   : register(c5);  // float3 right, padded to float4
float4   CameraUp      : register(c6);  // float3 up, padded to float4

struct VS_INPUT
{
    float2 CornerXY   : TEXCOORD0;  // quad corner: (-1,-1), (1,-1), (1,1), (-1,1)
    float4 BirthData0 : TEXCOORD1;  // x=spawnRadius, y=spawnAngle, z=lifetime, w=speed
    float4 BirthData1 : TEXCOORD2;  // x=size, y=seed (phase offset), zw=unused
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
    float4 Color    : COLOR0;
};

float4 ComputeFireColor(float age)
{
    // Gradient stops: 0.0=white → 0.15=yellow → 0.4=orange → 0.7=red → 1.0=transparent
    float t0 = saturate(age / 0.15);
    float t1 = saturate((age - 0.15) / 0.25);
    float t2 = saturate((age - 0.40) / 0.30);
    float t3 = saturate((age - 0.70) / 0.30);

    float4 c0 = float4(1.0, 1.0, 1.0, 1.0);       // white-hot core
    float4 c1 = float4(1.0, 1.0, 0.2, 1.0);        // bright yellow
    float4 c2 = float4(1.0, 0.5, 0.0, 1.0);        // orange
    float4 c3 = float4(0.8, 0.0, 0.0, 1.0);        // deep red
    float4 c4 = float4(0.0, 0.0, 0.0, 0.0);        // fade to nothing

    return lerp(lerp(lerp(lerp(c0, c1, t0), c2, t1), c3, t2), c4, t3);
}

VS_OUTPUT VSMain(VS_INPUT input, uint instanceID : SV_InstanceID)
{
    // ── Decode birth state ──────────────────────────────────────────────
    float spawnRadius = input.BirthData0.x;
    float spawnAngle  = input.BirthData0.y;
    float lifetime    = max(input.BirthData0.z, 0.01);
    float speed       = input.BirthData0.w;
    float size        = input.BirthData1.x;
    float seed        = input.BirthData1.y;

    // ── Analytic lifecycle ──────────────────────────────────────────────
    // Phase offset de-synchronizes particles so they're at different stages
    float particleTime = fmod(ElapsedTime + seed * lifetime, lifetime);
    float age = particleTime / lifetime;

    // ── Current world position ──────────────────────────────────────────
    // Circular spawn area at the fire base (y=0), rise upward
    float3 currentPos = float3(
        cos(spawnAngle) * spawnRadius,
        speed * particleTime,
        sin(spawnAngle) * spawnRadius
    );

    // ── Current size: shrink as particle rises and ages ─────────────────
    float currentSize = lerp(size, size * 0.05, age);

    // ── Fire color ──────────────────────────────────────────────────────
    float4 color = ComputeFireColor(age);

    // ── Billboard expansion ─────────────────────────────────────────────
    float3 worldPos = currentPos
        + CameraRight.xyz * input.CornerXY.x * currentSize * 0.5
        + CameraUp.xyz    * input.CornerXY.y * currentSize * 0.5;

    // ── Output ──────────────────────────────────────────────────────────
    VS_OUTPUT output;
    output.Position = mul(float4(worldPos, 1.0), WorldViewProj);
    output.TexCoord = input.CornerXY * 0.5 + 0.5;  // remap (-1..1) → (0..1)
    output.Color = color;
    return output;
}
