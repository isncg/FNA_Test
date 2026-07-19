// TrailEffect vertex shader — GPU-instanced colored cube trail.
//
// Geometry buffer (slot 0, freq=0): 36 float3 positions (unit cube)
// Instance buffer (slot 1, freq=1): N x { float4 pos, float4 rot, float4 color }
//
// VS_INPUT field order MUST match the combined vertex declarations:
//   Location 0: Position      (float3) — geometry buffer, slot 0, freq=0
//   Location 1: InstancePos   (float4) — instance buffer, slot 1, freq=1
//   Location 2: InstanceRot   (float4) — instance buffer, slot 1, freq=1
//   Location 3: InstanceColor (float4) — instance buffer, slot 1, freq=1

float4x4 ViewProj : register(c0);

struct VS_INPUT
{
    float3 Position      : POSITION0;
    float4 InstancePos   : TEXCOORD0;  // world position, w unused
    float4 InstanceRot   : TEXCOORD1;  // rotation quaternion (xyzw)
    float4 InstanceColor : TEXCOORD2;  // rgba color
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
};

// Rotate vector v by unit quaternion q.
// Equivalent to q * (0, v) * q^-1, optimized to the cross-product form.
float3 rotate_by_quaternion(float3 v, float4 q)
{
    return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
}

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;

    // Rotate cube vertex by instance quaternion
    float3 rotatedPos = rotate_by_quaternion(input.Position, input.InstanceRot);

    // Translate to world position
    float3 worldPos = rotatedPos + input.InstancePos.xyz;

    // Transform to clip space
    output.Position = mul(float4(worldPos, 1.0), ViewProj);
    output.Color = input.InstanceColor;

    return output;
}
