# SceneRenderer vs UE5 — 实现差异清单

> 生成时间：2026-08-01
> 状态：记录待办，后续逐项处理

---

## 1. Shadow System（差距最大）

| 项目 | SceneRenderer 现状 | UE5 做法 |
|------|-------------------|----------|
| 方案 | 单方向光、固定 2048² R32F | Virtual Shadow Maps (VSM) + Cascaded Shadow Maps |
| 过滤 | 3×3 固定 PCF | 自适应 PCF / PCSS / Ray-traced shadows |
| 光源覆盖 | 仅 `SunLight` 一盏 (`ShadowMapPass.cs`) | 所有 CastShadow 光源（点光/聚光/面光） |
| 投影范围 | 硬编码 `halfSize=10`, 场景中心 `(0, 0.5, 0)` | 动态 cascade 分割、自适应分辨率、page-based caching |
| Bias | 单一 `DepthBias = 0.02` | Per-object bias、slope-scaled bias、normal offset bias |

**涉及文件**: `Passes/ShadowMapPass.cs`, `Shaders/DeferredLighting_ps.hlsl` (SampleShadow)

---

## 2. GBuffer Layout & Precision

| 项目 | SceneRenderer 现状 | UE5 做法 |
|------|-------------------|----------|
| MRT 数量 | 3 (RGBA8 + 2×FP16) | 4–5 (含 Custom Data、Clear Coat 等) |
| Albedo 精度 | 8-bit UNORM — 暗部精度不足 | 编码后存储（如 `sqrt` 或 dither） |
| 法线存储 | `N * 0.5 + 0.5` 全 3 通道 FP16 | Octahedron 编码（2 通道即可） |
| 附加数据 | 无 | Subsurface profile、Clear coat、Custom data、Shading model ID |

**修复建议**: 最简方案 — 存储 `sqrt(albedo)` 并在 lighting pass 平方还原。

**涉及文件**: `Shaders/GBuffer_ps.hlsl` (L84), `Shaders/DeferredLighting_ps.hlsl`

---

## 3. Tangent Frame（已知缺陷）

**现状** (`GBuffer_ps.hlsl` L51-54):
```hlsl
float3 T = normalize(cross(up, N));  // 任意方向，不跟 UV 对齐
```
- 法线贴图细节随表面朝向旋转
- 地面 (`N ∥ up`) 处 frame 翻转

**UE5**: 导入时用 MikkTSpace 计算切线，通过 Vertex Factory 传递完整 TBN。

**修复选项**:
1. 顶点格式加 Tangent（需改 Mesh/GeometryGen/GBuffer VS+PS）
2. PS 中用 `ddx/ddy` 推导 cotangent frame（无需改顶点格式，但有 quad 依赖）

**涉及文件**: `Shaders/GBuffer_ps.hlsl`, `Core/Mesh.cs`, `Common/GeometryGen.cs`

---

## 4. Lighting Architecture

| 项目 | SceneRenderer 现状 | UE5 做法 |
|------|-------------------|----------|
| 光源上限 | 16 盏 CPU 视锥剔除 (`LightCuller.cs`) | 数千盏，GPU tiled/clustered 剔除 |
| 传递方式 | Constant buffer 数组 `float4 LightData[64]` | Structured Buffer / GPU Scene |
| 全局光照 | 静态 Irradiance Map（HDRI 卷积） | Lumen (SDF + Screen-space GI + Radiance Cache) |
| 间接高光 | Prefiltered EnvMap + 解析 BRDF approx | Lumen Reflections / Screen Traces / RT Reflections |
| 衰减模型 | `(1 - d²/r²)^falloff` | 物理反平方衰减 + smooth windowing |

**近期可行改进**: 利用已有 Compute 基础设施 (Phase 4) 实现 tiled light culling。

**涉及文件**: `Core/LightCuller.cs`, `Shaders/DeferredLighting_ps.hlsl`

---

## 5. SSR

| 项目 | SceneRenderer 现状 | UE5 做法 |
|------|-------------------|----------|
| 步进方式 | 等步长线性 march (64 步 × 0.5) | Hi-Z 层次化 march |
| 粗糙度模糊 | 3×3 固定 box | GGX importance sampling cone trace |
| 时域稳定 | 单帧 history，无累积 | 多帧时域累积 + 邻域 clamp (anti-ghosting) |
| 回退 | 直接 albedo 或 IBL | Lumen Reflections 无缝回退 |
| 步进精度 | `depthDiff < 0.5` 固定阈值 | 自适应 thickness 比较 |

**涉及文件**: `Shaders/SSR_ps.hlsl`, `Passes/SSRPass.cs`

---

## 6. SSAO

| 项目 | SceneRenderer 现状 | UE5 做法 |
|------|-------------------|----------|
| 算法 | 32 半球样本 + IGN 噪声 | GTAO (Ground Truth AO) + Distance Field AO |
| 时域 | 无 | 时域累积 + 运动向量重投影 |
| 分辨率 | 全分辨率 | 默认半分辨率 + 双边上采样 |
| 多尺度 | 单半径 | 多尺度组合 |

**涉及文件**: `Shaders/SSAO_ps.hlsl`, `Passes/SSAOPass.cs`, `Passes/BlurAOPass.cs`

---

## 7. Anti-Aliasing & Temporal（完全缺失）

**现状**: 无任何抗锯齿。GBuffer 已写 motion vectors (`GBuffer_ps.hlsl` L71-81)，但后续无 pass 消费。

**UE5**: TSR / TAA / FXAA / MSAA，全管线依赖 motion vectors。

**修复建议**: 先加 TAA（消费已有 motion vectors），再考虑 TSR 上采样。

**涉及文件**: 需新建 `Passes/TAAPass.cs` + `Shaders/TAA_ps.hlsl`

---

## 8. Post-Processing Stack

| SceneRenderer 已有 | UE5 额外有 |
|---|---|
| SSAO, SSR, Bloom, Tonemap | DOF, Motion Blur, Lens Flare, Chromatic Aberration |
| 手动 Exposure uniform | Film Grain, Vignette, Color Grading (LUT) |
| ACES (Narkowicz) + 固定 gamma 2.2 | Eye Adaptation (histogram auto-exposure) |

**涉及文件**: `Shaders/Tonemap_ps.hlsl`, `Passes/TonemapPass.cs`

---

## 9. Texture Pipeline

| 项目 | SceneRenderer 现状 | UE5 做法 |
|------|-------------------|----------|
| 源格式 | JPEG (4:2:0 chroma subsampling) | PNG/TGA/EXR → 自动压缩 |
| GPU 格式 | RGBA8 (无压缩) | BC1/BC3/BC4/BC5/BC7 (per-channel) |
| Mip 生成 | CPU box-filter (`TextureLoader.cs`) | GPU 硬件 mip / 离线 mipmap |
| 流式加载 | 无 | Virtual Texturing + streaming |

**修复建议**: 重新获取 lossless 源纹理 → 离线压缩为 BC5 (normal) / BC7 (albedo) / BC4 (mask)。

**涉及文件**: `Core/TextureLoader.cs`, `assets/materials/`

---

## 10. Rendering Infrastructure

| 项目 | SceneRenderer 现状 | UE5 做法 |
|------|-------------------|----------|
| Draw call | 逐对象 DrawPrimitives，无合批 | GPU Scene + Indirect Draw + Auto-Instancing |
| 遮挡剔除 | CPU 视锥剔除 (per-object sphere) | GPU Hi-Z Occlusion + Two-phase |
| 透明物体 | 不支持 | 前向 translucency + 排序 + 独立深度 |
| 材质系统 | 固定参数 (AlbedoTint, MetallicScale, RoughnessScale) | Material Graph + 编译变体 |
| Compute 使用 | 无（管线内） | Light culling, Hi-Z, Lumen, Nanite, SSR 等 |

**涉及文件**: `Core/SceneRenderer.cs`, `Core/Scene.cs`, `Passes/GBufferPass.cs`

---

## 已对齐的部分（无需修改）

- Shared Depth Buffer — GBuffer → DeferredLighting → Skybox 共享 D24S8 + PreserveContents
- SSR 时域反射 — 采样上一帧 lit HDR history（UE5-style）
- SSR Stencil 遮罩 — per-object ReceivesSSR 写 stencil
- PBR BRDF — Cook-Torrance GGX + Smith G + Schlick F + Disney Diffuse + Split-sum IBL
- sRGB 色彩空间管理 — albedo 用 ColorSrgbEXT，normal/mask 保持 linear
- Bloom — Karis 2013 tent filter pyramid
- Skybox 硬件深度测试 — VS z=1.0 + LessEqual

---

## 优先级排序

| 优先级 | 改进项 | 理由 |
|--------|--------|------|
| P0 | Tangent frame 修复 | 视觉影响最直接，法线贴图当前是错的 |
| P1 | TAA (消费已有 motion vectors) | 零抗锯齿画面锯齿严重 |
| P1 | Hi-Z SSR | 等步长效率低、远距反射质量差 |
| P2 | Cascaded / 多光源 Shadow | 单光固定范围远不够生产级 |
| P2 | GPU Light Culling (Compute) | 已有 compute 基础设施，可落地 |
| P2 | GBuffer albedo 精度 | sqrt 编码一行改动 |
| P3 | 纹理压缩 (BC5/BC7) | 消除 JPEG chroma 损害 + 节省带宽 |
| P3 | GTAO 替换 SSAO | 质量提升但工作量大 |
| P3 | Auto-exposure | 改善 HDR 工作流 |
| P4 | 透明物体渲染 | 当前场景无透明需求 |
| P4 | GPU-driven rendering | 场景规模小时不急 |
