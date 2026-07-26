// AsteroidField vertex shader — GPU-instanced orbiting debris chunks.
//
// Each instance (debris chunk) has its transform + color stored in a
// StructuredBuffer, indexed by SV_InstanceID. This is the modern GPU-driven
// rendering pattern: instance data lives in a storage buffer (not a vertex
// buffer), so it can hold arbitrary structs without vertex attribute limits.
//
// Additionally, the VS writes per-instance visibility flags to an
// RWStructuredBuffer, demonstrating the GPU→CPU feedback path used by
// GPU occlusion culling, particle state sync, and dynamic LOD systems.
//
// VS_INPUT: Position + Normal (exact match — C2 compliance)
//   C1: Sequential field order = vertex declaration element order
//   C2: Only the attributes the vertex layout provides
//   C5: float3 ↔ Vector3 numeric category match

float4x4 WorldViewProj : register(c0);
float4   LightDir      : register(c4);  // xyz=light direction, normalized
float4   AmbientColor  : register(c5);  // rgb=ambient term
float4   CameraPos     : register(c6);  // xyz=camera world position
float    ElapsedTime   : register(c7);

struct InstanceData
{
    float4 Position;   // xyz=orbit offset from origin, w=orbit speed
    float4 Rotation;   // rotation quaternion (xyzw)
    float4 Color;      // rgb=per-instance diffuse color, a=scale
};

StructuredBuffer<InstanceData> g_Instances : register(t1);
RWStructuredBuffer<uint> g_Visibility : register(u0);

struct VS_INPUT
{
    float3 Position : POSITION0;
    float3 Normal   : NORMAL0;
};

struct VS_OUTPUT
{
    float4 Position    : SV_POSITION;
    float4 Color       : COLOR0;
};

// Rotate vector v by unit quaternion q
float3 rotate_by_quaternion(float3 v, float4 q)
{
    return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
}

VS_OUTPUT VSMain(VS_INPUT input, uint instanceID : SV_InstanceID)
{
    InstanceData data = g_Instances[instanceID];

    // ── Orbit animation ────────────────────────────────────────────────────
    // Each instance orbits at its own radius and speed
    float3 orbitPos = data.Position.xyz;
    float  speed    = data.Position.w;
    float  scale    = data.Color.a;

    float angle = ElapsedTime * speed;
    float cosA = cos(angle);
    float sinA = sin(angle);

    // Rotate around Y axis (debris ring in XZ plane)
    float3 worldPos = float3(
        orbitPos.x * cosA - orbitPos.z * sinA,
        orbitPos.y,
        orbitPos.x * sinA + orbitPos.z * cosA
    );

    // ── Visibility feedback (GPU→CPU readback) ────────────────────────────
    // A simplified hemisphere check: is the instance on the camera-facing
    // side of the ring? In a real engine this would be frustum/occlusion
    // culling. We mark the instance visible if it's not behind the origin.
    float3 toCamera = CameraPos.xyz - worldPos;
    float3 toOrigin = normalize(-worldPos);
    float facing = dot(normalize(toCamera), toOrigin);
    uint visible = (facing > -0.3) ? 1 : 0;
    g_Visibility[instanceID] = visible;

    // ── Transform geometry ─────────────────────────────────────────────────
    float3 scaledPos = input.Position * scale;
    float3 rotatedPos = rotate_by_quaternion(scaledPos, data.Rotation);
    float3 finalPos = rotatedPos + worldPos;

    // ── Lambertian diffuse lighting ────────────────────────────────────────
    float3 N = normalize(rotate_by_quaternion(input.Normal, data.Rotation));
    float3 L = normalize(LightDir.xyz);
    float NdotL = saturate(dot(N, L));
    float3 litColor = data.Color.rgb * (AmbientColor.rgb + NdotL);

    VS_OUTPUT output;
    output.Position = mul(float4(finalPos, 1.0), WorldViewProj);
    output.Color = float4(litColor, 1.0);
    return output;
}
