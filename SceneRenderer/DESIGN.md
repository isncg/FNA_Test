# SceneRenderer — Design Document

Production-quality deferred 3D rendering pipeline for FNA3D_HLSL.

## Architecture

```
Program.cs (Game subclass)
  ├── Scene (objects + lights + materials)
  ├── SceneCamera (orbit cam + frustum culling)
  └── SceneRendererEngine (orchestrator)
        ├── _sharedDepth (D24S8, shared by GBuffer RT0 + _hdrSceneRT)
        ├── ShadowMapPass    → _shadowMap (R32F, 2048x2048)
        ├── GBufferPass      → RT0 (Color RGBA8), RT1 (HalfVector4), RT2 (HalfVector4) + fills _sharedDepth
        ├── SSAOPass         → _ssaoRT (R32F) → BlurAOPass → _ssaoBlurRT (R32F)
        ├── SSRPass          → _ssrRT (HalfVector4)
        ├── DeferredLightingPass → _hdrSceneRT (HalfVector4, clears colour only)
        ├── SkyboxPass       → _hdrSceneRT (depth-tested against _sharedDepth)
        ├── BloomPass        → bloom chain (HalfVector4)
        └── TonemapPass      → backbuffer (ACES + gamma)
```

## Shared Depth Buffer (UE5-style)

`SceneRendererEngine` owns one `DepthStencilBuffer` that both the GBuffer's RT0
and the HDR scene target attach to (FNA takes an MRT set's depth from
`renderTargets[0]`). The GBuffer pass fills it; the Skybox pass then depth-tests
against it, so the sky is rejected wherever geometry exists.

Rules that keep this working:

- Both targets must use `RenderTargetUsage.PreserveContents`. The default
  `DiscardContents` makes every `SetRenderTargets` clear the shared depth.
- Only the GBuffer pass clears depth (its `Clear(Color)` covers
  Target|DepthBuffer|Stencil). DeferredLighting must clear **colour only**
  (`Clear(ClearOptions.Target, ...)`).
- The Skybox VS emits `z = 1.0`, so `CompareFunction.LessEqual` with depth
  writes disabled passes only where depth is still the cleared 1.0. Blending is
  `Opaque` — the sky replaces the lighting residue in empty pixels rather than
  adding to it.

This replaced the earlier approach where the Skybox sampled GBuffer RT2's linear
depth and called `discard`, blending additively.

## GBuffer Layout (3 MRTs + Depth)

| RT | Format | R | G | B | A |
|----|--------|---|---|---|---|
| RT0 | Color (RGBA8 UNORM) | Albedo R | Albedo G | Albedo B | Baked AO |
| RT1 | HalfVector4 (FP16) | World Normal X | World Normal Y | World Normal Z | Roughness |
| RT2 | HalfVector4 (FP16) | Metallic | Linear View Depth | (reserved) | (reserved) |

## Shader Inventory

| Shader | Type | Textures | Key Uniforms |
|--------|------|----------|-------------|
| GBuffer | VS+PS (PNT) | t0-2: Albedo/Normal/ORM | c0:WorldViewProj, c4:World, c8:WorldInvTranspose |
| DeferredLighting | FS triangle | t0-8: GBuffer×3, SSAO, SSR, Shadow, Irr, Prefilt, BrdfLut | c0:EyePos, c3:Ambient, c8:LightData[64] |
| SSAO | FS triangle | t0-1: GBuffer1, GBuffer2 | c0:Projection, c4:SSAOParams |
| BlurAO | FS triangle | t0-1: AORT, GBuffer2 | c0:TexelSize, c1:BlurSharpness |
| SSR | FS triangle | t0-2: GBuffer×3 | c0:ViewProj, c4:InvViewProj, c11:SSRParams |
| ShadowMap | VS+PS (PNT) | none | c0:WorldViewProj |
| Skybox | FS triangle | t0:EnvMap | c0:CameraForward, c3:FovParams |
| Bloom | FS triangle | t0:Input | c0:Threshold, c2:ShaderIndex |
| Tonemap | FS triangle | t0:HdrScene, t1:Bloom | c0:Exposure, c1:BloomIntensity |
| IrradianceConv | FS triangle | t0:EnvMap | none |
| BrdfLut | FS triangle | none | none |
| PrefilterEnv | FS triangle | t0:EnvMap | c0:MipRoughness |

## C# Class Design

**Scene data**: `Scene` (objects+lights container), `SceneObject` (transform+mesh+material), `Material` (PBR textures+tint), `Light` (DirectionalLight/PointLight/SpotLight), `Mesh` (vertex buffer+bounds).

**Passes**: Each implements `IRenderPass` with `Initialize`/`Resize`/`Execute`/`Dispose`. Passes communicate via `RenderContext` which holds shared RT references.

**Orchestrator**: `SceneRendererEngine` owns the pass list, the `ResourcePool` and the shared `DepthStencilBuffer`. `Render(scene, camera)` executes passes in order. The shared depth buffer is created before the passes initialize and injected into `GBufferPass.SharedDepth` / `DeferredLightingPass.SharedDepth`; on resize it is replaced *before* the passes rebuild their targets and the old one is disposed only afterwards, since those targets alias it.

## Constraints

- **No compute shaders**: all post-processing via fullscreen triangle (SV_VertexID). FNA3D does support `DispatchCompute` (see `ComputeDispatch/`), this pipeline just has no use for it yet.
- **MRT limit**: 4 targets (using 3 for GBuffer)
- **PS sampler limit**: 16 (DeferredLighting uses 9)
- **C1-C5 vertex conventions**: PNT layout matches VertexPositionNormalTexture
- **Vulkan-only**: D3D→Vulkan Y-flip conventions in SSAO, SSR, Skybox
- **Shared depth**: see the section above — `PreserveContents` on both targets, and only the GBuffer pass may clear depth

## Key Algorithms

- **PBR**: Cook-Torrance GGX, Smith geometry, Schlick Fresnel, Disney diffuse, split-sum IBL
- **SSR**: Linear ray marching in view space, roughness-adaptive steps, cone-trace blur
- **SSAO**: 32 hemisphere samples, interleaved gradient noise, angle-adaptive radius
- **Bloom**: Bright extract → 4-step downsample → 4-step upsample (Karis 2013 tent filter)
- **Shadows**: Single directional shadow map, 2048x2048 R32F, 3x3 PCF
- **Tonemap**: ACES filmic (Narkowitz fit) + gamma 2.2

## Texture Colour Space and Mips

`Texture2D.FromStream` always produces a single-level `SurfaceFormat.Color`
(linear UNORM) texture, which broke the pipeline in two ways until 2026-07-31:
sRGB-encoded albedo was consumed as if it were linear, and nothing had mips for
the anisotropic sampler to minify with. `Core/TextureLoader.cs` now handles this,
mirroring Unreal's per-texture sRGB flag:

| Map | Format | Rationale |
|-----|--------|-----------|
| albedo | `ColorSrgbEXT` | sRGB-encoded colour; the texture unit decodes to linear on sample, before filtering |
| normal | `Color` (linear) | tangent-space vectors, not colour |
| packed (ARM) | `Color` (linear) | scalar AO/Roughness/Metallic data |

The error being fixed was strongly non-uniform, which is why it read as "detail
looks wrong" rather than "too bright": treating an sRGB texel as linear
over-brightens texel 32 by **8.7x** and texel 128 by **2.3x**, while texel 255 is
exact — the texture's tonal range gets crushed.

Mips are built on the CPU (no GPU mip generation is exposed: `SDL_GenerateMipmaps`
needs `COLOR_TARGET` usage, which FNA3D only sets for render targets).
Downsampling happens in the space that suits the data — linear light for sRGB
colour, renormalised vectors for normal maps, plain averages for masks — since
box-filtering sRGB-encoded values directly would darken each level. Cost is
~2.3 s for 24 2048² textures (12 levels each), mostly JPEG decode.

## Known Issues

- **Material source format**: the maps are JPEG. Measured from the SOF markers,
  every `*_packed.jpg` and some `*_normal.jpg` use 2x2 chroma subsampling, so
  roughness (G), metallic (B) and the normal's Y/Z are effectively half
  resolution, plus DCT block artefacts on data that is not colour. Unreal uses
  BC5 for normals and BC4/BC7 for masks: block compressed, but per-channel and
  without chroma subsampling. Fixing this means re-fetching the textures in a
  lossless format and compressing properly.
- **Tangent frame is not UV-aligned**: the vertex format is PNT (`Position`,
  `Normal`, `TexCoord`) with **no tangents**, so `GBuffer_ps` fabricates one from
  the world normal (`T = normalize(cross(up, N))`). Normal-map detail is
  therefore rotated arbitrarily per surface orientation and the frame flips on
  the floor, where `N` is parallel to `up`. Unreal generates tangents at import
  (MikkTSpace) and passes `TangentToWorld` through the vertex factory, so its
  frame is UV-aligned by construction. Fix options: add tangents to the vertex
  format, or derive a cotangent frame from `ddx/ddy` of position and UV in the
  pixel shader.
- **GBuffer albedo precision**: RT0 is `SurfaceFormat.Color`, so linear albedo is
  stored in 8 bits, which under-serves the darks. Unreal encodes base colour
  before storing it in the GBuffer for this reason. A cheap fix is to store
  `sqrt(albedo)` and square it in the lighting pass.
- **Teapot mesh attributes**: the `.tris` source has neither normals nor
  texcoords, so `TeapotModel.Load` averages face normals across *all* vertices
  (an O(n²) pass over 10464 vertices) and generates UVs by cylindrical
  projection. That UV scheme interacts badly with the tangent issue above.
