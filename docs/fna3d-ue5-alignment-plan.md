# FNA3D UE5 对齐开发计划

## 概述

目标：修改 FNA3D_HLSL，使其支持 Unreal Engine 5 风格的延迟渲染管线。每一项变更都配有独立的测试程序。

> **状态说明**：Phase 0（基线对齐）、Phase 1（可采样深度）、Phase 2（深度纹理包装）、Phase 3（共享深度）已完成；Phase 4 尚未开始。引用行号对应 `../FNA/lib/FNA3D/src/FNA3D_Driver_SDL.c` 当前版本。
>
> **Phase 0（基线对齐）已于 2026-07-31 完成**，详见下文。完成 Phase 0 后，FNA3D 子模块应位于 `c821adb`（`storage buffer api`）及其后提交。

## Phase 0: 基线对齐（已完成）

### 发现的问题

在准备 Phase 1 时发现 `../FNA/lib/FNA3D` 子模块的源码与 C# 层及预编译 `FNA3D.dll` 不一致：

| 项目 | 状态 |
|------|------|
| `../FNA/lib/FNA3D` 当前 HEAD | `453b1dd`（`FEB v2: 52-byte shader entries, add COMPUTE stage support`） |
| `../FNA` 父仓库记录的子模块提交 | `c821adb`（`storage buffer api`） |
| 各测试目录中的 `FNA3D.dll` | `2319794` 字节，来自 `453b1dd`，**未导出** `FNA3D_GenStorageBuffer` 等 StorageBuffer API |
| `../FNA/src/Graphics/FNA3D.cs` | 已包含 `FNA3D_GenStorageBuffer` / `SetStorageBufferData` / `SetVertexStorageBuffers` 等 P/Invoke |
| `StorageBuffer/AsteroidField` | 已包含使用 `StructuredBuffer` / `RWStructuredBuffer` 的顶点着色器测试 |

后果：
1. 若按 `453b1dd` 源码重新编译 FNA3D，会丢失 StorageBuffer 支持，导致 `AsteroidField` 等现有测试在 `EntryPointNotFoundException` 中失败。
2. `StorageBuffer/AsteroidField/Shaders/AsteroidField.feb` 在工作目录中被重建为 `4694` 字节，与提交版本 `4674` 字节不兼容，导致 `FNA3D_CreateEffect` 内访问冲突（`0xC0000005`）。

### 已执行的修复

1. **子模块回正**：在 `../FNA/lib/FNA3D` 执行 `git checkout c821adb`，使源码与父仓库记录及预编译 DLL 的能力对齐。
2. **重新编译 FNA3D**：
   ```bash
   cd ../FNA/lib/FNA3D
   cmake --build build --clean-first
   ```
   生成 `build/FNA3D.dll`（`2322343` 字节），`objdump -p` 确认已导出：
   - `FNA3D_GenStorageBuffer`
   - `FNA3D_AddDisposeStorageBuffer`
   - `FNA3D_SetStorageBufferData`
   - `FNA3D_GetStorageBufferData`
   - `FNA3D_SetVertexStorageBuffers`
3. **恢复 FEB 文件**：将 `StorageBuffer/AsteroidField/Shaders/AsteroidField.feb` 恢复为 `499222a` 提交版本（`4674` 字节），并重新嵌入到 `AsteroidField.dll`。
4. **保留 imgui depth-clip 补丁**：`thirdparty/imgui/backends/imgui_impl_sdlgpu3.cpp` 中的 `enable_depth_clip = true` 本地修改已保留；`patches/0001-sdlgpu3-enable-depth-clip.patch` 仍有空白字符差异，不影响功能。

### 验证结果

```bash
cd ../FNA_Test/StorageBuffer/AsteroidField/bin/Debug/net10.0
cp ../../../../../FNA/lib/FNA3D/build/FNA3D.dll .
timeout 5 ./AsteroidField.exe
```

输出：
```
Validation layers enabled, expect debug level performance!
SDL_GPU Driver: Vulkan
Vulkan Device: NVIDIA GeForce RTX 2060
Vulkan Driver: NVIDIA 581.42
Vulkan Conformance: 1.4.1.3
[AsteroidField] Effect loaded: 1 techniques, 5 params
[AsteroidField] 512 instances ready.
```

验证通过，无崩溃。

### 后续注意事项

- 所有测试目录 `bin/Debug/net10.0/FNA3D.dll` 仍是旧版 `453b1dd` 预编译 DLL；使用 `run_tests.bat`/`run_tests.sh` 时会从 `../FNA/lib/FNA3D/build/FNA3D.dll` 重新复制，因此无需逐个手动替换。
- 在继续 Phase 1 前，确保 `../FNA/lib/FNA3D` 始终位于 `c821adb` 或之后；若子模块被意外切回 `453b1dd`，StorageBuffer 测试会再次失败。

---

## 架构背景

### FNA3D 类型层次

```
FNA3D_Texture* ────── SDLGPU_TextureHandle* ────── SDL_GPUTexture*
  (可采样纹理)         (内部包装)                   (GPU 资源)

FNA3D_Renderbuffer* ─ SDLGPU_Renderbuffer ───────── SDL_GPUTexture*
  (深度/颜色附件)       (内部包装)                   (GPU 资源，不同用途标志)
```

**关键约束**：`FNA3D_Renderbuffer` 和 `FNA3D_Texture` 是独立的类型层次。深度渲染缓冲区只创建时带有 `SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET`，不能作为着色器输入采样。

### 关键源文件

| 层级 | 文件 |
|------|------|
| FNA3D 公共 API | `../FNA/lib/FNA3D/include/FNA3D.h` |
| 驱动程序函数表（`struct FNA3D_Device`） | `../FNA/lib/FNA3D/src/FNA3D_Driver.h` |
| SDL_GPU 驱动 | `../FNA/lib/FNA3D/src/FNA3D_Driver_SDL.c` |
| 效果系统 | `../FNA/lib/FNA3D/src/FNA3D_Effect.c` / `.h` |
| C# P/Invoke | `../FNA/src/Graphics/FNA3D.cs` |
| C# RenderTarget2D | `../FNA/src/Graphics/RenderTarget2D.cs` |
| C# Texture2D | `../FNA/src/Graphics/Texture2D.cs` |
| C# IRenderTarget | `../FNA/src/Graphics/IRenderTarget.cs` |
| C# GraphicsDevice | `../FNA/src/Graphics/GraphicsDevice.cs` |
| 测试工具 | `../FNA_Test/Common/TestHarness.cs` |

---

## Phase 1: 可采样的深度缓冲区（已完成）

### 目标
使深度模板缓冲区可以作为着色器纹理采样（SSAO、软阴影、屏幕空间反射等需要；与像素着色器输出 `SV_Depth` 不同）。

### 实际实现（2026-07-31）

与原计划的区别：未直接硬编码 `| SAMPLER`，而是新增内联辅助函数 `SDLGPU_INTERNAL_GetDepthUsageFlags(renderer, format, sampleCount)`，在创建时动态决定用法标志：

- 仅当 `sampleCount == 1` 且 `SDL_GPUTextureSupportsFormat(DEPTH_STENCIL_TARGET | SAMPLER)` 为真时返回 `DEPTH_STENCIL_TARGET | SAMPLER`；
- 否则回退纯 `DEPTH_STENCIL_TARGET`（SDL_GPU 禁止 MSAA 纹理带 SAMPLER 用法；个别 GPU 不支持可采样深度格式）；
- 该函数直接查询设备而不依赖 `supportsD24*` 标志，因此在 FauxBackbuffer（早于能力检测初始化）中也能正确工作。

修改点（`../FNA/lib/FNA3D/src/FNA3D_Driver_SDL.c`）：

| 位置 | 修改 |
|------|------|
| `XNAToSDL_DepthFormat` 之后 | 新增 `SDLGPU_INTERNAL_GetDepthUsageFlags` 辅助函数 |
| `SDLGPU_GenDepthStencilRenderbuffer` | usage 改为调用辅助函数 |
| `SDLGPU_INTERNAL_CreateFauxBackbuffer` 深度分支 | usage 改为调用辅助函数 |
| `supportsD24` / `supportsD24S8` 能力查询 | 加入 `SAMPLER`，使格式选择偏向可采样格式（不可采样时自动回退 D32_FLOAT 系） |

#### 1.2 能力检测（已包含在辅助函数中）

辅助函数每次创建时查询 `SDL_GPUTextureSupportsFormat`，不支持可采样深度的 GPU 自动回退，无需额外处理。

### 测试程序: `DepthSampling/`（已创建）

**路径**: `../FNA_Test/DepthSampling/`

**实际测试内容**（深度*采样*需 Phase 2 的纹理包装，本阶段验证新用法标志不破坏深度测试）：
1. 近处红色方块（z=0.25）+ 远处绿色方块（z=0.75），同心布局（Y 对称，断言不受 Y-flip 影响）
2. 路径 A：渲染到背板（验证 FauxBackbuffer 深度路径）
3. 路径 B：渲染到 D24S8 `RenderTarget2D`（验证 `GenDepthStencilRenderbuffer` 路径）
4. 断言：中心=红（深度测试拒绝了绿）、外环=绿、角落=清除色

**验证结果**：
- headless 运行 `RESULT: DepthSampling PASS`
- Vulkan 验证层开启下无新增验证错误
- 回归：BasicEffect / DepthSorting 通过；AsteroidField 的随机失败为既有 flaky（未定种 `Random`）；JFAOutline / SceneRenderer 的 effect 加载失败为既有问题，根因已查清并修复（见末尾“FEB param 布局遗留问题”）
- RenderDoc 确认 `vkCreateImage` 带 SAMPLED_BIT（待人工抽查）

**实际文件**：
```
DepthSampling/
  DepthSampling.csproj
  Program.cs                     # 双路径深度测试 + headless 断言
  Shaders/DepthQuad_vs.hlsl      # clip-space 直通（PC 布局，C1-C5）
  Shaders/DepthQuad_ps.hlsl      # 顶点色输出
  Shaders/DepthQuad.feb.json
```

已注册到 `run_tests.sh` / `run_tests.bat`。

---

## Phase 2: 深度缓冲区作为 C# Texture2D（已完成）

### 目标
将深度渲染缓冲区包装为 `Texture2D`，使其可以绑定到着色器的纹理槽位（如 `GraphicsDevice.Textures[0] = depthTexture`）。

### 实际实现（2026-07-31）

#### C 层

- **`FNA3D.h`**：新增 `FNA3DAPI FNA3D_Texture* FNA3D_GetDepthStencilTexture(FNA3D_Device*, FNA3D_Renderbuffer*)`（公共 API 第一参数是 `FNA3D_Device*`，非原计划的 `FNA3D_Renderer*`）。
- **`FNA3D.c`**：调度器，带 NULL 检查。
- **`FNA3D_Driver.h`**：`FNA3D_Device` 函数表新增 `GetDepthStencilTexture` 条目 + `ASSIGN_DRIVER_FUNC`。
- **`FNA3D_Driver_SDL.c`**：
  - `SDLGPU_TextureHandle` 新增 `uint8_t ownsTexture` 字段；`CreateTextureWithHandle` 置 1，`FreeTextureHandle` 仅在 `ownsTexture` 时释放底层 `SDL_GPUTexture`——解决了计划中标注的双重释放 FIXME。
  - `SDLGPU_GetDepthStencilTexture`：校验 renderbuffer 带 SAMPLER 用法（MSAA/不支持采样的格式返回 NULL 并报错），否则分配别名句柄（`ownsTexture = 0`）。
  - 生命周期约定：包装纹理必须在 renderbuffer 之前 dispose。

#### C# 层

- **`FNA3D.cs`**：`FNA3D_GetDepthStencilTexture` P/Invoke。
- **`Texture2D.cs`**：新增 internal 包装构造函数 `Texture2D(GraphicsDevice, width, height, SurfaceFormat, IntPtr existingTexture)`，不创建 GPU 资源；`SetData`/`GetData` 对包装纹理不支持（仅着色器采样）。
- **`RenderTarget2D.cs`**：
  - `DepthStencilTexture` 属性懒创建（`SurfaceFormat.Single` 仅为占位标记）；不可用时返回 null。
  - `Dispose` 中先释放包装纹理再释放 renderbuffer，符合生命周期约定。

### 测试程序: `DepthTexture/`（已创建）

**路径**: `../FNA_Test/DepthTexture/`

**实际测试内容**：
1. D24S8 `RenderTarget2D`，Pass 1 写入 z=0.25 的方块（x,y ∈ [-0.5,0.5]，Y 对称）
2. `rt.DepthStencilTexture` 获取深度纹理
3. Pass 2 全屏四边形采样 `t0`（PointClamp），把原始深度写为灰度到背板
4. 断言：中心 ≈ 0.25（灰度 64）、方块外 = 1.0（白）、角落 = 1.0

**验证结果**：
- headless 运行 `RESULT: DepthTexture PASS`（首次运行即通过）
- Vulkan 验证层开启下无验证错误（含深度采样的 layout 转换，由 SDL_GPU 自动处理）
- 回归：DepthSampling / BasicEffect 仍 PASS
- RenderDoc 确认深度纹理作为 `t0` 绑定（待人工抽查）

**实际文件**：
```
DepthTexture/
  DepthTexture.csproj
  Program.cs                    # 双 pass：深度写入 + 深度采样可视化
  Shaders/DepthFill_vs.hlsl     # clip-space 直通（PC 布局）
  Shaders/DepthFill_ps.hlsl
  Shaders/DepthView_vs.hlsl     # 全屏四边形（PT 布局）
  Shaders/DepthView_ps.hlsl     # Sample(depthTex, uv).r → 灰度
  Shaders/DepthFill.feb.json
  Shaders/DepthView.feb.json
```

已注册到 `run_tests.sh` / `run_tests.bat`。

**已知限制**：
- MSAA 深度缓冲区不可包装（Phase 1 回退为无 SAMPLER 用法，属性返回 null）。
- 同一帧内“写深度 → 采样深度”需先 `SetRenderTarget` 切走（结束 render pass），与普通 RT 采样约束一致。

---

## Phase 3: 共享深度缓冲区（已完成）

### 目标
允许不同的 `RenderTarget2D` 共享同一个深度缓冲区。这是 UE5 延迟渲染的核心——所有通道重用同一个深度缓冲区。

### 实际实现（2026-07-31）

**重要发现：无需修改 C 驱动与 `GraphicsDevice`。** 现有 `GraphicsDevice.SetRenderTargets` 已从 `renderTargets[0]` 的 `IRenderTarget.DepthStencilBuffer` 取深度，驱动侧 `SDLGPU_SetRenderTargets` 直接使用 `renderbuffer->textureHandle`；不清除时用 `SDL_GPU_LOADOP_LOAD` 且 `cycle = false`，深度内容天然跳过通道保留。因此只需让 `RenderTarget2D` 持有外部深度句柄，共享即生效——**也就绕开了原计划 3.3 担心的 `SetRenderTargets` 重载歧义问题**。

#### 3.1 新类: DepthStencilBuffer

**文件**: `../FNA/src/Graphics/DepthStencilBuffer.cs`（新建，已注册到 `FNA.Core.csproj`）

继承 `GraphicsResource`（而非计划中的 `IDisposable`），与 `StorageBuffer` 一致：

```csharp
public class DepthStencilBuffer : GraphicsResource
{
    internal IntPtr buffer;      // FNA3D_Renderbuffer*
    public int Width { get; }
    public int Height { get; }
    public DepthFormat Format { get; }
    public int MultiSampleCount { get; }

    public DepthStencilBuffer(GraphicsDevice, int width, int height, DepthFormat format);
    public DepthStencilBuffer(GraphicsDevice, int width, int height, DepthFormat format,
        int preferredMultiSampleCount);
    public Texture2D GetTexture();   // 复用 Phase 2 API，句柄由本类拥有
}
```

- `DepthFormat.None` 抛 `ArgumentException`；
- `Dispose` 先释放 `GetTexture()` 的包装纹理再释放 renderbuffer。

#### 3.2 RenderTarget2D 修改

新增两个接收外部深度的构造函数：

```csharp
// usage 默认 PreserveContents（原因见下文“关键约束”）
public RenderTarget2D(GraphicsDevice, int width, int height, bool mipMap,
    SurfaceFormat preferredFormat, DepthStencilBuffer depthStencilBuffer);

public RenderTarget2D(GraphicsDevice, int width, int height, bool mipMap,
    SurfaceFormat preferredFormat, DepthStencilBuffer depthStencilBuffer,
    RenderTargetUsage usage);
```

- 新增私有字段 `DepthStencilBuffer externalDepth`（而非计划中的 `bool _isExternalDepth`，保留引用才能委派 `GetTexture()`）；
- `DepthStencilFormat` / `MultiSampleCount` 从共享缓冲区继承（颜色附件的 sample count 必须与深度一致）；
- 构造时校验共享缓冲区尺寸不小于 RT；
- `DepthStencilTexture` 属性在外部深度时委派给 `externalDepth.GetTexture()`，避免产生多个包装句柄；
- `Dispose` **不**释放外部深度。

#### 3.3 GraphicsDevice：无需修改

原计划的 `SetRenderTargets(DepthStencilBuffer, params ...)` 重载已取消：深度由 RT 自身携带，无需新 API，也不存在重载歧义风险。

### 关键约束（实现中发现）

**共享深度的 RT 必须用 `RenderTargetUsage.PreserveContents`**。默认的 `DiscardContents` 会让 `SetRenderTargets` 在每次绑定时执行 `Clear(Target | DepthBuffer | Stencil)`，直接抹掉共享深度。因此新构造函数默认 `PreserveContents`，后续通道应只 `Clear(ClearOptions.Target, ...)` 单独清颜色。

### 测试程序: `SharedDepth/`（已创建）

**路径**: `../FNA_Test/SharedDepth/`

**实际测试内容**：
1. 创建 `DepthStencilBuffer`（D24S8, 256×256）+ RT1、RT2 两个颜色目标共享它
2. Pass 1 → RT1：清颜色+深度，绘近处方块（z=0.25）
3. Pass 2 → RT2：**只清颜色**，绘全屏“天空”（z=0.75）并开启深度测试
4. 断言：RT2 中心 = RT2 清除色（天空被共享深度拒绝）、RT2 外环/角落 = 天空色、RT1 中心 = 几何体色；兼验 `sharedDepth.GetTexture() != null`（Phase 2 互通）

**负对照（`--no-share`）**：该开关让 RT2 使用自己的深度缓冲区，此时测试**必须失败**，用以证明断言真的在检验共享而非空过。实测：共享时 PASS，`--no-share` 时 2 处失败（外环与角落未绘出天空）。

**验证结果**：
- headless 运行 `RESULT: SharedDepth PASS`；Vulkan 验证层无错误
- 回归：DepthSampling / DepthTexture / BasicEffect / DepthSorting / AsteroidField / JFAOutline / SDFFontTest / TrailEffect / TrailEffectCapture / ParticleFire 均 PASS（JFAOutline 与 SDFFontTest 经 FEB 修复后恢复，见末尾章节）
- RenderDoc 验证两个通道使用同一 `VkImageView`（待人工抽查）

**实际文件**（天空与几何体共用一个 shader，比计划少一组）：
```
SharedDepth/
  SharedDepth.csproj
  Program.cs                   # 双 RT 共享深度 + --no-share 负对照
  Shaders/Geometry_vs.hlsl     # clip-space 直通（PC 布局）
  Shaders/Geometry_ps.hlsl     # 顶点色输出
  Shaders/Geometry.feb.json
```

已注册到 `run_tests.sh` / `run_tests.bat`。

---

## Phase 4: 计算着色器支持

### 目标
添加 `DispatchCompute` 支持，用于 GPU 端分块光照剔除、Hi-Z 生成、SSAO 等。

> **前置依赖（已由 Phase 0 完成）**：C# 层已有 `StorageBuffer` 类（`FNA/src/Graphics/Vertices/StorageBuffer.cs`）及对应 P/Invoke；`FNA3D.h` 公共 API 与 `FNA3D_Driver_SDL.c` 在 `c821adb` 已加入 `GenStorageBuffer` / `SetStorageBufferData` / `GetStorageBufferData` / `SetVertexStorageBuffers` 等实现。Phase 4 只需在此基础上扩展计算管线与计算通道资源绑定。

### 变更

#### 4.1 FNA3D_Driver.h — 函数表条目

> 以下函数表条目中，`DispatchCompute` 为 Phase 4 新增；存储缓冲区相关条目已在 Phase 0（`c821adb`）就位，此处列出以确认基线。

```c
/* 计算调度（Phase 4 新增） */
void (*DispatchCompute)(
    FNA3D_Renderer *driverData,
    FNA3D_Effect *effect,
    FNA3D_EffectPass *pass,
    uint32_t threadGroupCountX,
    uint32_t threadGroupCountY,
    uint32_t threadGroupCountZ
);

/* 存储缓冲区（RWStructuredBuffer 等）—— 已在 c821adb 实现 */
FNA3D_Buffer* (*GenStorageBuffer)(
    FNA3D_Renderer *driverData,
    int32_t sizeInBytes,
    uint8_t vertexWrite,
    uint8_t vertexRead
);
void (*AddDisposeStorageBuffer)(
    FNA3D_Renderer *driverData,
    FNA3D_Buffer *buffer
);
void (*SetStorageBufferData)(
    FNA3D_Renderer *driverData,
    FNA3D_Buffer *buffer,
    int32_t offsetInBytes,
    void* data,
    int32_t dataLength
);
void (*GetStorageBufferData)(
    FNA3D_Renderer *driverData,
    FNA3D_Buffer *buffer,
    int32_t offsetInBytes,
    void* data,
    int32_t dataLength
);
void (*SetVertexStorageBuffers)(
    FNA3D_Renderer *driverData,
    FNA3D_Buffer **buffers,
    int32_t firstSlot,
    int32_t numBuffers,
    uint8_t writable
);
```

#### 4.2 FNA3D_Driver_SDL.c — 实现

> 当前 `SDLGPU_Effect` 只有 `vertexShaders` / `pixelShaders` 数组，没有计算管线字段；实现本阶段需要先扩展该结构体（例如增加 `SDL_GPUComputePipeline **computePipelines`）。

**在 `SDLGPU_CreateEffect` 中** (约第 3950 行):
添加计算着色器创建。注意 `FNA3D_Effect` 中所有着色器都存放在同一个 `shaders` 数组里，通过 `stage == FNA3D_SHADERSTAGE_COMPUTE` 识别：
```c
for (i = 0; i < effect->shaderCount; i++)
{
    FNA3D_EffectShader *csInfo = &effect->shaders[i];
    if (csInfo->stage != FNA3D_SHADERSTAGE_COMPUTE)
    {
        continue;
    }
    SDL_GPUComputePipelineCreateInfo createInfo = {0};
    createInfo.code = csInfo->spirvData;
    createInfo.code_size = csInfo->spirvSize;
    createInfo.entrypoint = csInfo->entryPoint;
    createInfo.num_samplers = SDL_max(csInfo->samplerCount, 1);
    createInfo.num_readonly_storage_buffers = csInfo->readonlyStorageBufferCount;
    createInfo.num_readonly_storage_textures = csInfo->readonlyStorageTextureCount;
    createInfo.num_readwrite_storage_buffers = csInfo->readwriteStorageBufferCount;
    createInfo.num_readwrite_storage_textures = csInfo->readwriteStorageTextureCount;
    gpuEffect->computePipelines[i] = SDL_CreateGPUComputePipeline(
        renderer->device, &createInfo);
}
```

**新函数 `SDLGPU_DispatchCompute`**:
```c
static void SDLGPU_DispatchCompute(
    FNA3D_Renderer *driverData,
    FNA3D_Effect *effect,
    FNA3D_EffectPass *pass,
    uint32_t tgX, uint32_t tgY, uint32_t tgZ
) {
    SDLGPU_Renderer *renderer = (SDLGPU_Renderer*) driverData;
    SDLGPU_Effect *gpuEffect = (SDLGPU_Effect*) effect->driverData;
    FNA3D_EffectShader *cs = &effect->shaders[pass->computeShaderIndex];

    SDL_GPUComputePass *computePass = SDL_BeginGPUComputePass(
        renderer->renderCommandBuffer, NULL, 0, NULL, 0);

    SDL_BindGPUComputePipeline(computePass,
        gpuEffect->computePipelines[pass->computeShaderIndex]);

    SDL_PushGPUComputeUniformData(renderer->renderCommandBuffer, 0,
        gpuEffect->uniformData, gpuEffect->uniformDataSize);

    // 绑定存储缓冲区和纹理...

    SDL_DispatchGPUCompute(computePass, tgX, tgY, tgZ);
    SDL_EndGPUComputePass(computePass);
}
```

#### 4.3 FNA3D.h — 公共 API

```c
FNA3DAPI void FNA3D_DispatchCompute(
    FNA3D_Renderer *device,
    FNA3D_Effect *effect,
    FNA3D_EffectPass *pass,
    uint32_t threadGroupCountX,
    uint32_t threadGroupCountY,
    uint32_t threadGroupCountZ
);
```

#### 4.4 FNA3D.cs — P/Invoke

```csharp
[DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
public static extern void FNA3D_DispatchCompute(
    IntPtr device, IntPtr effect, IntPtr pass,
    uint tgX, uint tgY, uint tgZ);
```

#### 4.5 GraphicsDevice.cs — C# 方法

```csharp
public void DispatchCompute(Effect effect, EffectPass pass,
    int threadGroupCountX, int threadGroupCountY, int threadGroupCountZ)
{
    FNA3D.FNA3D_DispatchCompute(GLDevice,
        effect.Internals.EffectPtr, pass.Internals.PassPtr,
        (uint)threadGroupCountX, (uint)threadGroupCountY, (uint)threadGroupCountZ);
}
```

### 测试程序: `ComputeDispatch/`（待创建）

**路径**: `../FNA_Test/ComputeDispatch/`（当前不存在）

> **注意**：现有 `ComputeShaderEffect/ParticleFire/` 目录名容易混淆，但它使用顶点着色器里的 GPU Instancing 实现粒子动画，**不是**计算着色器示例。

**核心逻辑**:
1. 加载包含计算着色器 (.hlsl → .spv → .feb) 的 FEB
2. 创建存储缓冲区 (RWStructuredBuffer<float>)
3. 调度计算着色器（写入缓冲区）
4. 回读存储缓冲区，验证值

**计算着色器** (`test_cs.hlsl`):
```hlsl
RWStructuredBuffer<float> Output : register(u0);

[numthreads(64, 1, 1)]
void CSMain(uint3 tid : SV_DispatchThreadID)
{
    Output[tid.x] = (float)tid.x * 2.0;
}
```

**验证**:
- 回读缓冲区，检查 `Output[i] == i * 2.0f` 是否成立
- headless 通过/失败断言

**文件**:
```
ComputeDispatch/
  ComputeDispatch.csproj
  Program.cs
  Shaders/test_cs.hlsl
  Shaders/test_cs.feb.json
```

---

## 依赖关系

```
Phase 0 (基线对齐：StorageBuffer C 实现 + FEB 修复)  【已完成】
    ↓
Phase 1 (可采样深度)  【已完成】
    ↓
Phase 2 (深度纹理包装) 【已完成】←── 依赖 Phase 1 的 SAMPLER 标志
    ↓
Phase 3 (共享深度缓冲区) 【已完成】←── 依赖 Phase 2 的纹理包装

Phase 4 (计算着色器) ←── 依赖 Phase 0 的 StorageBuffer；可与 Phase 1-3 并行
```

## 构建和测试

### 构建单个测试
> `ComputeDispatch/` 待 Phase 4 实施时创建；`DepthSampling/`、`DepthTexture/`、`SharedDepth/` 已存在。

```bash
cd ../FNA_Test/DepthSampling
dotnet build
```

### 运行所有 FNA3D 测试
```bash
# 待实现：将新测试添加到 run_tests.sh
cd ../FNA_Test
./run_tests.sh
```

### FNA3D 库构建
每次修改 FNA3D C 代码后：
```bash
cd ../FNA/lib/FNA3D
cmake -B build -G Ninja . -DCMAKE_BUILD_TYPE=Release
ninja -C build
```

## 验证清单

| Phase | 状态 | 测试程序 | 验证内容 | RenderDoc 检查 |
|-------|------|---------|---------|---------------|
| 1 | 完成 | DepthSampling | 深度缓冲区有 SAMPLER 用法；双路径深度测试正常 | `vkCreateImage` 的 usage 标志 |
| 2 | 完成 | DepthTexture | DepthStencilTexture 返回有效纹理；采样值匹配写入深度 | 片段着色器描述符集绑定 |
| 3 | 完成 | SharedDepth | 两个通道使用同一深度缓冲区；`--no-share` 负对照失败 | 两个 `vkCmdBeginRenderPass` 使用同一 `VkImageView` |
| 4 | 计划 | ComputeDispatch | Compute 输出匹配预期 | `vkCmdDispatch` 和存储缓冲区内容 |

## 回滚到 SceneRenderer 修复（未来重构方向）

> 当前 `SceneRenderer/DESIGN.md` 明确约束 **No compute shaders**，且 SkyboxPass 是 additive 写入 `_hdrSceneRT`，并未使用共享深度缓冲区的硬件深度测试。完成 Phase 1-3 后，可再考虑将 SceneRenderer 的 Skybox 改为 UE5 风格的硬件深度测试：

1. 创建共享 `DepthStencilBuffer`
2. GBuffer 通道使用此深度缓冲区渲染
3. DepthFill 通道将深度写入 HDR RT（也使用同一共享深度）
4. DeferredLighting 渲染到 HDR RT（深度测试关闭）
5. Skybox 渲染到 HDR RT，启用 `DepthBufferFunction.LessEqual` 深度测试

> **注意**：当前 SceneRenderer 除 effect 加载外尚有其他既有缺陷（见下节），上述重构应在那些问题解决后再进行。

---

## FEB param 布局遗留问题（2026-07-31 排查并修复）

与 UE5 对齐无关，但阻碍了 Phase 1-3 的完整回归，故记录于此。

### 症状

- `JFAOutline`：`FNA3D_CreateEffect` 内 native 访问冲突（`0xC0000005`）
- `SceneRenderer`：C# `Effect.INTERNAL_parseEffect` 抛 `IndexOutOfRangeException`

两者均在 Phase 0 基线 DLL 下同样失败，确认与 Phase 1-3 无关。

### 根因

`FNA/tools/feb_builder.py` 的提交 `4c5f015`（FEB v2 升级）将 **param 条目从 88 字节改为 84 字节**，但未重建已有的 FEB。C 解析器按 `84 + annotationCount * 40` 逐个顺序读取，遇到 88 字节条目会累计错位 4 字节，字符串偏移变成垃圾值。

> 注意：shader 条目（52 字节）和 header version（=2）都是新的，只有 param 条目遗留，因此仅看版本号会误判为正常。判定方法：`(tech_off - param_off) / paramCount`，84 为正常，88 为遗留。

当时仓库内 **29 个 FEB 为 88 字节**（涉及 JFAOutline、SceneRenderer、MaterialLib、SDFFontTest、Gui、ParticleEffect、GPUInstancing）；零参数的 FEB 与已重建的 AsteroidField 不受影响。

### 连带修复的工具链 bug

`feb_builder.py` 两处文本 `open()` 未指定编码，中文 Windows 默认 GBK，遇到 HLSL / manifest 中的非 ASCII 字符（如 `—`、`’`）即 `UnicodeDecodeError` —— 这正是这些 FEB 一直无法在 Windows 上重建的原因。已加 `encoding="utf-8"`。

### 结果

重建全部 29 个 FEB 后格式检查无遗留：

| 测试 | 修复前 | 修复后 |
|------|--------|--------|
| JFAOutline | native 访问冲突 | ✅ PASS |
| SDFFontTest | — | ✅ PASS |
| TrailEffect / TrailEffectCapture / ParticleFire | — | ✅ PASS（无回归） |
| SceneRenderer | effect 解析越界 | ⚠️ 越过解析，但崩在其他位置 |

### SceneRenderer 剩余问题（未修复，已确认非本项目引入）

SceneRenderer 现在能完成 effect 加载与 IBL 预计算，但在 draw 调用中 native 崩溃。已取得的证据：

- Phase 0 基线 DLL 下同样崩溃（位置为 SkyboxPass），确认为既有问题
- 崩溃点在同一 DLL 多次运行间漂移（ShadowMapPass / DeferredLightingPass），而 SceneRenderer 代码无随机数 → 指向内存/生命周期问题
- 零 Vulkan 验证层报错 → 在到达 Vulkan 之前就挂了，像是空/悬垂的 SDL_GPU 句柄
- 已排除：管线创建失败（无 `Failed to create graphics pipeline` 日志）、uniform 越界（native `SetEffectParamValueByHandle` 会按 `FNA3D_GetParamSize` 截断并告警）、资源池稳态回收（仅尺寸变化时释放）
- **领先假设**：已 `Dispose` 的纹理仍留在驱动的 `fragmentTextureSamplerBindings` 中（`SDLGPU_VerifySampler` 存的是裸 `SDL_GPUTexture*`，而 `SDL_ReleaseGPUTexture` 是立即释放），下次 draw 解引用已释放指针。需 native 调试器或在 `VerifySampler` / `FreeTextureHandle` 加日志才能定论。

### 另一个独立缺陷：数组型 effect 参数未支持

`SceneRenderer/Shaders/DeferredLighting.feb.json` 为 `LightData` 声明了 `"count": 64`，但 **FEB 格式、feb_builder、C/C# 解析器三方都没有 count 字段**，`FNA3D_EffectParam.currentValue` 也只有 16 个 float。native 写入会静默截断到 16 字节，即 `LightData[64]` 实际只有第一个 float4 生效 —— 不是崩溃原因（有截断保护），但意味着多光源照明目前不正确，属独立待办。
