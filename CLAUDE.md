# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is the test/validation repository for the **FNA HLSL fork** (`../FNA/`, branch `hlsl`). The goal is to verify FNA works correctly when its graphics backend uses **FNA3D_HLSL** (at `../FNA/lib/FNA3D/` as a git submodule), which replaces MojoShader with DXC — compiling HLSL source to SPIR-V and packing it into a custom FEB (FNA3D Effect Binary) for consumption by SDL_GPU at runtime.

## Repository Relationships

```
FNA_Test/               ← you are here (test harness)
../FNA/                 ← FNA C# library (XNA 4.0 reimplementation), branch: hlsl
../FNA/lib/FNA3D/       ← FNA3D_HLSL fork (submodule): HLSL→DXC→SPIR-V pipeline, C + CMake
```

## Key References

- **FNA3D_HLSL architecture and build**: `../FNA/lib/FNA3D/CLAUDE.md`
- **HLSL→FEB pipeline**: `../FNA/lib/FNA3D/README` and `../FNA3D_HLSL_Test/CLAUDE.md`
- **FNA official docs**: https://fna-xna.github.io/docs/ (example programs for testing)
- **FEB builder**: `../FNA/tools/feb_builder.py` (canonical copy; mirrors `../FNA3D_HLSL_Test/tools/feb_builder.py`)

## Build Commands (FNA3D_HLSL C library)

```bash
cd ../FNA/lib/FNA3D
cmake -B build -G Ninja . -DCMAKE_BUILD_TYPE=Release
ninja -C build
# Disable Dear ImGui if not needed:
cmake -B build -G Ninja . -DFNA3D_IMGUI=OFF
```

## Build Commands (FNA C# library)

```bash
# Build FNA
cd ../FNA && dotnet build FNA.Core.csproj   # → bin/Debug/net10.0/FNA.dll

# Build and run tests
cd ../FNA_Test
dotnet run --project StockEffect/SpriteEffect/SpriteEffect.csproj
```

FNA has several .csproj variants:
- `FNA.Core.csproj` — .NET Core / modern (targeting net10.0)
- `FNA.csproj` — full framework
- `FNA.NetFramework.csproj` — .NET Framework
- `FNA.NetStandard.csproj` — .NET Standard

## Test Structure

```
FNA_Test/
├── StockEffect/                   # Stock effect validation tests
│   ├── SpriteEffect/
│   ├── BasicEffect/
│   ├── AlphaTestEffect/
│   ├── DualTextureEffect/
│   ├── EnvironmentMapEffect/
│   ├── BasicEffectMatrix/
│   └── SkinnedEffect/
├── ComputeShaderEffect/           # Compute shader effect tests
│   └── ParticleFire/              # GPU-instanced fire particles
├── ParticleEffect/                # Shared particle FEB assets
├── Common/                        # Shared utilities (TextureGen, TestHarness, ImGui)
├── test_sprite/                   # Minimal SpriteBatch integration test
├── docs/                          # Design documentation
└── run_tests.sh                   # CI: build FNA3D → rebuild FEBs → build FNA → test all
```

## Native Library Loading

The test needs `libFNA3D.so.0` in the runtime directory. Symlinks point to the submodule build:
```
<test>/bin/Debug/net10.0/libFNA3D.so   → ../../../../../lib/FNA3D/build/libFNA3D.so.27.0.0
<test>/bin/Debug/net10.0/libFNA3D.so.0 → ../../../../../lib/FNA3D/build/libFNA3D.so.27.0.0
```

`run_tests.sh` manages these symlinks automatically.

## Shader Pipeline (HLSL → FEB)

```
HLSL source (.hlsl)
  → DXC -spirv -T vs_6_0 / -T ps_6_0 / -T cs_6_0
    → SPIR-V binary (.spv)
      → feb_builder.py (reads .feb.json manifest)
        → .feb binary (FNA3D Effect Binary)
          → FNA3D_CreateEffect() at runtime (FNA3D_HLSL)
            → SDL_GPU renders via Vulkan
```

## Stock Effects Migration Task

FNA embeds 6 stock effects as compiled `.fxb` resources:
- `AlphaTestEffect.fxb`
- `BasicEffect.fxb`
- `DualTextureEffect.fxb`
- `EnvironmentMapEffect.fxb`
- `SkinnedEffect.fxb`
- `SpriteEffect.fxb`

Plus 2 YUV-to-RGBA effects. These are in `../FNA/src/Graphics/Effect/StockEffects/`.

The migration task:
1. Determine which stock effects can be reimplemented in HLSL
2. Write HLSL source + `.feb.json` manifests for them
3. Build them into FEB binaries using `feb_builder.py`
4. Update the FNA C# effect classes (e.g., `BasicEffect.cs`) if the parameter binding changes
5. Remove `.fxb` files for effects that cannot be ported

## Testing Approach

Per the README: build and run test programs against the modified FNA+FNA3D_HLSL stack. The `run_tests.sh` script automates the full pipeline.

```bash
./run_tests.sh    # Full CI: build FNA3D → rebuild FEBs → build FNA → test all
```

## Constraints

### Strict Effect–HLSL Vertex Convention (C1–C5)

All HLSL/Effect development MUST follow these conventions:

- **C1 (Sequential Match)**: Each vertex shader entry point's `VS_INPUT` field order MUST equal its target FNA vertex declaration's element order. Both sides assign locations sequentially — zero mapping needed.
- **C2 (Exact Declaration)**: `VS_INPUT` MUST declare **only** the attributes the target layout actually provides — no superset declarations. The shader must not consume unprovided attributes (eliminates Vulkan UB; "validation zero error" is the verifiable test).
- **C3 (Technique = Input Signature)**: Effect flags that affect input signature (`VertexColorEnabled` / `TextureEnabled` / `LightingEnabled`) → one **technique per layout**, switched by the Effect class in `OnApply` via `CurrentTechnique`. Non-signature switches (`FogEnabled`, lighting sub-mode) stay as `ShaderIndex` uniform branches.
- **C4 (Layout Standard)**: Use the original FNA stock `IVertexType` element order as authoritative (`FNA/src/Graphics/Vertices/`):
  - `VertexPositionColor` (PC): Position, Color
  - `VertexPositionColorTexture` (PCT): Position, Color, TextureCoordinate
  - `VertexPositionNormalTexture` (PNT): Position, Normal, TextureCoordinate
  - `VertexPositionTexture` (PT): Position, TextureCoordinate
  - Custom layouts: Skinned = (Position, Normal, TextureCoordinate, BlendIndices, BlendWeight); DualTexture = (Position, TexCoord0, TexCoord1), with Color after Position when vertex color is present.
- **C5 (Numeric Category Match)**: `VS_INPUT` field numeric categories must match vertex format (float ↔ Vector*/Color, uint ↔ Byte4). `Color` format is BGRA byte order (XNA convention); the shader receives normalized RGBA `float4`.

### FNA3D_HLSL Constraints

- FNA3D_HLSL does not yet implement uniform/constant buffer update APIs. Shaders must work with default parameter values baked into the FEB, or be pass-through (NDC position + vertex color only).
- FNA3D_HLSL is Vulkan-only (SPIR-V shader format). No D3D11, OpenGL, or Metal backends exist.
- `FNA3D_VERTEXELEMENTFORMAT_COLOR` uses BGRA byte order (XNA convention).

### Effect Architecture (Post-Migration)

Each stock effect uses multi-technique FEBs:
- **BasicEffect**: 4 techniques (PNT, PT, PC, PCT) — vertex color and lighting handled by technique selection
- **AlphaTestEffect**: 2 techniques (PT, PCT)
- **DualTextureEffect**: 2 techniques (PTT, PCTT)
- **SkinnedEffect**: 1 technique (PNT+BlendIndices+BlendWeights), no Color input
- **EnvironmentMapEffect / SpriteEffect**: 1 technique each (no changes needed)
