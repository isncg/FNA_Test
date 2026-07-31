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
