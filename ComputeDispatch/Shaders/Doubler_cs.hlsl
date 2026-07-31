// Compute shader: writes Output[i] = i * 2.0 for a 1D buffer.
// One read-write structured buffer at u0, 64 threads per group.

RWStructuredBuffer<float> Output : register(u0);

[numthreads(64, 1, 1)]
void CSMain(uint3 tid : SV_DispatchThreadID)
{
    Output[tid.x] = (float)tid.x * 2.0;
}
