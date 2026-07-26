// AsteroidField pixel shader — pass-through vertex-lit color.

float4 PSMain(float4 color : COLOR0) : SV_TARGET0
{
    return color;
}
