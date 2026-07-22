// Pass-through fullscreen quad vertex shader
// Input: POSITION0 (NDC [-1,1]) + TEXCOORD0 (UV [0,1])
// Output: SV_Position + TEXCOORD0

struct VS_INPUT
{
    float3 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;
    output.Position = float4(input.Position, 1.0);
    output.TexCoord = input.TexCoord;
    return output;
}
