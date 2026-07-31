# FNA3D UE5 对齐开发计划

## 概述

目标：修改 FNA3D_HLSL，使其支持 Unreal Engine 5 风格的延迟渲染管线。每一项变更都配有独立的测试程序。

## 架构背景

### FNA3D 类型层次

```
FNA3D_Texture* ────── SDLGPU_TextureHandle* ────── SDL_GPUTexture*
  (可采样纹理)         (内部包装)                   (GPU 资源)

FNA3D_Renderbuffer* ─ SDLGPU_Renderbuffer ───────── SDLGPUTexture*
  (深度/颜色附件)       (内部包装)                   (GPU 资源，不同用途标志)
```

**关键约束**：`FNA3D_Renderbuffer` 和 `FNA3D_Texture` 是独立的类型层次。深度渲染缓冲区只创建时带有 `SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET`，不能作为着色器输入采样。

### 关键源文件

| 层级 | 文件 |
|------|------|
| FNA3D 公共 API | `../FNA/lib/FNA3D/include/FNA3D.h` |
| 驱动程序函数表 | `../FNA/lib/FNA3D/src/FNA3D_Driver.h` |
| SDL_GPU 驱动 | `../FNA/lib/FNA3D/src/FNA3D_Driver_SDL.c` |
| 效果系统 | `../FNA/lib/FNA3D/src/FNA3D_Effect.c` / `.h` |
| C# P/Invoke | `../FNA/src/Graphics/FNA3D.cs` |
| C# RenderTarget2D | `../FNA/src/Graphics/RenderTarget2D.cs` |
| C# Texture2D | `../FNA/src/Graphics/Texture2D.cs` |
| C# IRenderTarget | `../FNA/src/Graphics/IRenderTarget.cs` |
| C# GraphicsDevice | `../FNA/src/Graphics/GraphicsDevice.cs` |
| 测试工具 | `../FNA_Test/Common/TestHarness.cs` |

---

## Phase 1: 可采样的深度缓冲区

### 目标
使深度模板缓冲区可以作为着色器纹理采样（`SV_Depth` 填充、SSAO、软阴影等需要）。

### 变更

#### 1.1 FNA3D_Driver_SDL.c — 添加 SAMPLER 标志

**文件**: `../FNA/lib/FNA3D/src/FNA3D_Driver_SDL.c`

**位置 1** — `SDLGPU_GenDepthStencilRenderbuffer` (约第 2967-2977 行):
```c
// 当前：
SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET,

// 改为：
SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET | SDL_GPU_TEXTUREUSAGE_SAMPLER,
```

**位置 2** — `SDLGPU_INTERNAL_CreateFauxBackbuffer` (约第 2691-2701 行):
```c
// 当前：
SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET,

// 改为：
SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET | SDL_GPU_TEXTUREUSAGE_SAMPLER,
```

**位置 3** — 能力查询 (约第 4946-4957 行):
```c
// 更新 uses 标志以包含 SAMPLER：
SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET | SDL_GPU_TEXTUREUSAGE_SAMPLER
```

#### 1.2 能力检测

在驱动初始化时查询 `SDL_GPUTextureSupportsFormat` 是否需要更新。部分 GPU 可能不支持可采样的深度纹理（极少数情况）。

### 测试程序: `DepthSampling/`

**路径**: `../FNA_Test/DepthSampling/`

**核心逻辑**:
1. 创建一个带有 `DepthFormat.Depth24Stencil8` 的 `RenderTarget2D`
2. 渲染一个三角形（深度值 0.5）
3. 将深度缓冲区包装为纹理（当前不可用，需 Phase 2）
4. 在第二个全屏通道中对深度纹理进行采样
5. 回读并验证深度值

**验证**:
- 在 RenderDoc 中确认深度纹理具有 `SAMPLER` 用法
- 确认无 Vulkan 验证错误
- headless：验证深度纹理的像素值（需要 Phase 2 的纹理包装）

**文件**:
```
DepthSampling/
  DepthSampling.csproj
  Program.cs
  Shaders/DepthFill_vs.hlsl
  Shaders/DepthFill_ps.hlsl     # 输出 SV_Depth
  Shaders/DepthSample_vs.hlsl   # 全屏三角形
  Shaders/DepthSample_ps.hlsl   # 采样深度纹理，输出到颜色
  Shaders/DepthFill.feb.json
  Shaders/DepthSample.feb.json
```

---

## Phase 2: 深度缓冲区作为 C# Texture2D

### 目标
将深度渲染缓冲区包装为 `Texture2D`，使其可以绑定到着色器的纹理槽位（如 `GraphicsDevice.Textures[0] = depthTexture`）。

### 变更

#### 2.1 FNA3D.h — 新 API

```c
/* 从现有的深度渲染缓冲区创建一个可采样的纹理句柄。
 * 渲染缓冲区必须已使用 SAMPLER 用法创建（Phase 1）。
 * 返回的纹理共享相同的底层 GPU 资源。*/
FNA3DAPI FNA3D_Texture* FNA3D_GetDepthStencilTexture(
    FNA3D_Renderer *driverData,
    FNA3D_Renderbuffer *renderbuffer
);
```

#### 2.2 FNA3D_Driver_SDL.c — 实现

```c
static FNA3D_Texture* SDLGPU_GetDepthStencilTexture(
    FNA3D_Renderer *driverData,
    FNA3D_Renderbuffer *renderbuffer
) {
    SDLGPU_Renderbuffer *rb = (SDLGPU_Renderbuffer*) renderbuffer;
    SDLGPU_TextureHandle *handle = SDL_malloc(sizeof(SDLGPU_TextureHandle));
    SDL_memcpy(handle, rb->textureHandle, sizeof(SDLGPU_TextureHandle));
    // handle->boundAsRenderTarget 等字段需重置
    handle->boundAsRenderTarget = 0;
    return (FNA3D_Texture*) handle;
}
```

#### 2.3 FNA3D_Driver.h — 函数表条目

在 `FNA3D_RendererFunctions` 结构体中添加:
```c
FNA3D_Texture* (*GetDepthStencilTexture)(
    FNA3D_Renderer *driverData,
    FNA3D_Renderbuffer *renderbuffer
);
```

#### 2.4 FNA3D.cs — P/Invoke

```csharp
[DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]
public static extern IntPtr FNA3D_GetDepthStencilTexture(
    IntPtr device, IntPtr renderbuffer);
```

#### 2.5 RenderTarget2D.cs — 暴露属性

```csharp
private Texture2D? _depthStencilTexture;

public Texture2D DepthStencilTexture
{
    get
    {
        if (_depthStencilTexture == null && glDepthStencilBuffer != IntPtr.Zero)
        {
            IntPtr texPtr = FNA3D.FNA3D_GetDepthStencilTexture(
                GraphicsDevice.GLDevice, glDepthStencilBuffer);
            _depthStencilTexture = new Texture2D(
                GraphicsDevice, Width, Height, false,
                SurfaceFormat.Single, /* or special depth format */
                texPtr);
        }
        return _depthStencilTexture;
    }
}
```

**注意**: `Texture2D` 可能需要一个新的内部构造函数，接受预先存在的 `FNA3D_Texture*` 指针，避免重复创建。

### 测试程序: `DepthTexture/`

**路径**: `../FNA_Test/DepthTexture/`

**核心逻辑**:
1. 创建带有深度的 `RenderTarget2D`，渲染已知深度的三角形
2. 通过 `rt.DepthStencilTexture` 获取深度纹理
3. 渲染第二个全屏通道：对深度纹理进行采样，输出到颜色
4. 使用 `GetData` 回读颜色，验证深度值正确（在容差范围内）

**验证**:
- `AssertPixel` 检查深度值 ≈ 0.5（三角形）和 1.0（清除值）
- 在 RenderDoc 中确认深度纹理作为 `t0` 绑定

**文件**:
```
DepthTexture/
  DepthTexture.csproj
  Program.cs
  Shaders/DepthToColor_vs.hlsl
  Shaders/DepthToColor_ps.hlsl   # Sample(depthTex, uv).r → SV_TARGET0
  Shaders/DepthToColor.feb.json
```

---

## Phase 3: 共享深度缓冲区

### 目标
允许不同的 `RenderTarget2D` 共享同一个深度缓冲区。这是 UE5 延迟渲染的核心——所有通道重用同一个深度缓冲区。

### 变更

#### 3.1 新类: DepthStencilBuffer

**文件**: `../FNA/src/Graphics/DepthStencilBuffer.cs` (新建)

```csharp
public class DepthStencilBuffer : IDisposable
{
    internal IntPtr Handle; // FNA3D_Renderbuffer*
    public int Width, Height;
    public DepthFormat Format;
    
    public DepthStencilBuffer(GraphicsDevice device, int width, int height, DepthFormat format);
    public Texture2D GetTexture(); // 通过 Phase 2 API 包装为纹理
    public void Dispose();
}
```

#### 3.2 RenderTarget2D 修改

添加一个新的构造函数，接受外部深度缓冲区：

```csharp
public RenderTarget2D(GraphicsDevice device, int width, int height,
    bool mipMap, SurfaceFormat format, DepthStencilBuffer depthStencilBuffer)
{
    // 使用外部深度缓冲区
    this.glDepthStencilBuffer = depthStencilBuffer.Handle;
    this._isExternalDepth = true;
    // 正常创建颜色纹理...
}
```

#### 3.3 GraphicsDevice 修改

添加 `SetRenderTargets` 重载，接受外部深度：

```csharp
public void SetRenderTargets(DepthStencilBuffer depthBuffer, params RenderTargetBinding[] renderTargets)
{
    // 使用提供的深度缓冲区调用 FNA3D_SetRenderTargets
}
```

### 测试程序: `SharedDepth/`

**路径**: `../FNA_Test/SharedDepth/`

**核心逻辑**:
1. 创建 `DepthStencilBuffer` (D24S8)
2. 创建 `RT1`（颜色，使用共享深度）→ 渲染 3D 几何体
3. 创建 `RT2`（颜色，使用**相同深度**）→ 渲染天空盒/全屏通道，启用深度测试
4. 验证几何体区域被正确遮挡（天空只出现在空白区域）

**验证**:
- 几何体像素 ≠ 天空颜色（天空被深度测试正确拒绝）
- 空白像素 = 天空颜色
- 在 RenderDoc 中验证两个通道使用相同的深度附件

**文件**:
```
SharedDepth/
  SharedDepth.csproj
  Program.cs
  Shaders/Geometry_vs.hlsl
  Shaders/Geometry_ps.hlsl     # 简单颜色输出
  Shaders/Fullscreen_vs.hlsl
  Shaders/Fullscreen_ps.hlsl   # 天空颜色
  Shaders/Geometry.feb.json
  Shaders/Fullscreen.feb.json
```

---

## Phase 4: 计算着色器支持

### 目标
添加 `DispatchCompute` 支持，用于 GPU 端分块光照剔除、Hi-Z 生成、SSAO 等。

### 变更

#### 4.1 FNA3D_Driver.h — 新函数表条目

```c
void (*DispatchCompute)(
    FNA3D_Renderer *driverData,
    FNA3D_Effect *effect,
    FNA3D_EffectPass *pass,
    uint32_t threadGroupCountX,
    uint32_t threadGroupCountY,
    uint32_t threadGroupCountZ
);
```

#### 4.2 FNA3D_Driver_SDL.c — 实现

**在 `SDLGPU_CreateEffect` 中** (约第 3970 行):
添加计算着色器创建：
```c
for (i = 0; i < effect->computeShaderCount; i++)
{
    FNA3D_EffectShader *csInfo = &effect->computeShaders[i];
    SDL_GPUComputePipelineCreateInfo createInfo = {0};
    createInfo.code = csInfo->spirvData;
    createInfo.code_size = csInfo->spirvSize;
    createInfo.entrypoint = csInfo->entryPoint;
    createInfo.num_samplers = max(csInfo->samplerCount, 1);
    // ... 存储缓冲区/纹理计数 ...
    effect->sdlComputePipelines[i] = SDL_CreateGPUComputePipeline(
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
    SDLGPU_Effect *gpuEffect = (SDLGPU_Effect*) effect;
    
    SDL_GPUComputePass *computePass = SDL_BeginGPUComputePass(
        renderer->renderCommandBuffer, NULL, 0, NULL, 0);
    
    SDL_BindGPUComputePipeline(computePass,
        gpuEffect->sdlComputePipelines[pass->computeShaderIndex]);
    
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

### 测试程序: `ComputeDispatch/`

**路径**: `../FNA_Test/ComputeDispatch/`

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
Phase 1 (可采样深度)
    ↓
Phase 2 (深度纹理包装) ←── 依赖 Phase 1 的 SAMPLER 标志
    ↓
Phase 3 (共享深度缓冲区) ←── 依赖 Phase 2 的纹理包装
    
Phase 4 (计算着色器) ←── 独立，与其他阶段并行
```

## 构建和测试

### 构建单个测试
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

| Phase | 测试程序 | 验证内容 | RenderDoc 检查 |
|-------|---------|---------|---------------|
| 1 | DepthSampling | 深度缓冲区有 SAMPLER 用法 | `vkCreateImage` 的 usage 标志 |
| 2 | DepthTexture | DepthStencilTexture 返回有效纹理 | 片段着色器描述符集绑定 |
| 3 | SharedDepth | 两个通道使用同一深度缓冲区 | 两个 `vkCmdBeginRenderPass` 使用同一 `VkImageView` |
| 4 | ComputeDispatch | Compute 输出匹配预期 | `vkCmdDispatch` 和存储缓冲区内容 |

## 回滚到 SceneRenderer 修复

完成 Phase 1-3 后，SceneRenderer 的 Skybox 可以改为 UE5 风格的硬件深度测试：

1. 创建共享 `DepthStencilBuffer`
2. GBuffer 通道使用此深度缓冲区渲染
3. DepthFill 通道将深度写入 HDR RT（也使用同一共享深度）
4. DeferredLighting 渲染到 HDR RT（深度测试关闭）
5. Skybox 渲染到 HDR RT，启用 `DepthBufferFunction.LessEqual` 深度测试
