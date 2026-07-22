# FNA3D 图形后端迁移方案：SDL_GPU → Vulkan

> 目标：将 FNA3D_HLSL 的底层图形后端从 **SDL_GPU** 替换为**直接调用 Vulkan API**，
> 在**不改动 `FNA3D.h` 公共 ABI**、**不重新构建现有 FEB 着色器包**的前提下，
> 实现与现有 SDL_GPU 后端功能等价的原生 Vulkan 驱动。

本文档面向实现者，给出**决策完整**的分阶段实施方案：读者无需再做架构决策，
按阶段推进即可。文中所有相对路径以 `../FNA/lib/FNA3D/` 为基准（FNA3D_HLSL 子模块根目录）。

---

## 目录

1. [背景与目标](#1-背景与目标)
2. [现状架构分析](#2-现状架构分析)
3. [总体迁移策略与关键决策](#3-总体迁移策略与关键决策)
4. [SDL_GPU → Vulkan 概念映射](#4-sdl_gpu--vulkan-概念映射)
5. [描述符集与绑定模型（兼容性核心）](#5-描述符集与绑定模型兼容性核心)
6. [新驱动模块分解](#6-新驱动模块分解)
7. [分阶段实施计划](#7-分阶段实施计划)
8. [构建系统改动](#8-构建系统改动)
9. [Dear ImGui 集成](#9-dear-imgui-集成)
10. [外部互操作（SysRenderer）](#10-外部互操作sysrenderer)
11. [风险与缓解](#11-风险与缓解)
12. [测试与验收计划](#12-测试与验收计划)
13. [里程碑与工作量估算](#13-里程碑与工作量估算)

---

## 1. 背景与目标

### 1.1 现状

FNA3D_HLSL 当前只有一个后端驱动 [FNA3D_Driver_SDL.c](../FNA/lib/FNA3D/src/FNA3D_Driver_SDL.c)（约 4900 行），
它通过 **SDL3 的 GPU 子系统（SDL_GPU）** 完成所有渲染。SDL_GPU 自身是一层跨平台抽象，
其内部在桌面平台上默认调用 **Vulkan**（也可走 D3D12/Metal）。

因此 FNA3D_HLSL 的实际调用链是：

```
FNA (C#)  →  FNA3D 公共 API  →  FNA3D_Driver_SDL.c  →  SDL_GPU  →  Vulkan 驱动
```

### 1.2 目标

去掉中间的 SDL_GPU 抽象层，新增一个直接面向 Vulkan 的驱动：

```
FNA (C#)  →  FNA3D 公共 API  →  FNA3D_Driver_Vulkan.c  →  Vulkan 驱动
                                        │
                                        └─ SDL3 仅用于窗口/表面创建 (SDL_Vulkan_*)
```

### 1.3 硬性约束（成功标准）

- **C1｜公共 ABI 不变**：不修改 [FNA3D.h](../FNA/lib/FNA3D/include/FNA3D.h) 中的任何导出函数签名，
  C# 层（`../FNA/`）零改动即可运行。
- **C2｜FEB 零重建**：现有 `.feb` 二进制（内含 DXC 编译的 SPIR-V）**无需重新编译**。
  这是最关键的约束，其可行性依据见 [§5](#5-描述符集与绑定模型兼容性核心)。
- **C3｜功能等价**：现有全部测试程序（StockEffect、ComputeShaderEffect、GPUInstancing、
  test_sprite）在新后端下渲染结果与 SDL_GPU 后端一致。
- **C4｜可切换**：通过编译宏 / 运行时环境变量在两个后端间切换，便于对比与回退。

---

## 2. 现状架构分析

### 2.1 三层结构

| 层 | 文件 | 职责 |
|---|---|---|
| 调度层 | [FNA3D.c](../FNA/lib/FNA3D/src/FNA3D.c) | 维护 `drivers[]` 数组，选择驱动，转发公共 API 到驱动 vtable |
| 驱动接口 | [FNA3D_Driver.h](../FNA/lib/FNA3D/src/FNA3D_Driver.h) | 定义 `FNA3D_Device` 函数指针表（约 90 个入口）+ `ASSIGN_DRIVER` 宏 + `FNA3D_Driver` 注册结构 |
| 后端实现 | [FNA3D_Driver_SDL.c](../FNA/lib/FNA3D/src/FNA3D_Driver_SDL.c) | 用 SDL_GPU 实现全部 vtable 入口 |
| 后端无关 | `FNA3D_Effect.c`、`FNA3D_PipelineCache.c`、`FNA3D_Image.c` | FEB 解析、管线哈希、图像加载，不依赖具体后端 API |

### 2.2 驱动注册机制（关键：这是新驱动的挂载点）

[FNA3D.c](../FNA/lib/FNA3D/src/FNA3D.c#L43-L46)：

```c
static const FNA3D_Driver *drivers[] = {
	&SDLGPUDriver,
	NULL
};
```

- `FNA3D_PrepareWindowAttributes()` 遍历 `drivers[]`，第一个 `PrepareWindowAttributes()` 返回真的驱动被选中。
- `FNA3D_CreateDevice()` 调用被选中驱动的 `CreateDevice()`，后者用 `ASSIGN_DRIVER(SDLGPU)` 宏
  填充整张 `FNA3D_Device` 函数指针表。

新驱动只需：定义一个 `FNA3D_Driver VulkanDriver = { "Vulkan", Vulkan_PrepareWindowAttributes, Vulkan_CreateDevice }`，
并把 `&VulkanDriver` 加入 `drivers[]`。**调度层与 C# 层完全不感知后端差异。**

### 2.3 需要实现的 vtable 入口（按功能分组）

来自 [FNA3D_Driver.h](../FNA/lib/FNA3D/src/FNA3D_Driver.h#L261-L724)，全部约 90 个函数，分组如下：

- **生命周期**：`DestroyDevice`
- **呈现**：`SwapBuffers`
- **绘制**：`Clear`、`DrawIndexedPrimitives`、`DrawInstancedPrimitives`、`DrawPrimitives`
- **可变渲染状态**：`SetViewport`、`SetScissorRect`、`Get/SetBlendFactor`、`Get/SetMultiSampleMask`、`Get/SetReferenceStencil`
- **不可变渲染状态**：`SetBlendState`、`SetDepthStencilState`、`ApplyRasterizerState`、`VerifySampler`、`VerifyVertexSampler`、`ApplyVertexBufferBindings`
- **渲染目标**：`SetRenderTargets`、`ResolveTarget`
- **后备缓冲**：`ResetBackbuffer`、`ReadBackbuffer`、`GetBackbufferSize/SurfaceFormat/DepthFormat/MultiSampleCount`
- **纹理**：`CreateTexture2D/3D/Cube`、`AddDisposeTexture`、`SetTextureData2D/3D/Cube/YUV`、`GetTextureData2D/3D/Cube`
- **RenderBuffer**：`GenColorRenderbuffer`、`GenDepthStencilRenderbuffer`、`AddDisposeRenderbuffer`
- **顶点/索引缓冲**：`GenVertexBuffer`、`GenIndexBuffer`、`Set/GetVertexBufferData`、`Set/GetIndexBufferData`、`AddDispose*`
- **Effect**：`CreateEffect`、`CloneEffect`、`AddDisposeEffect`、`SetEffectTechnique`、`ApplyEffect`、`Begin/EndPassRestore`、`SetEffectParamValue(ByHandle)`
- **查询**：`CreateQuery`、`AddDisposeQuery`、`QueryBegin/End/Complete/PixelCount`
- **能力查询**：`SupportsDXT1/S3TC/BC7/HardwareInstancing/NoOverwrite/SRGBRenderTargets`、`GetMaxTextureSlots`、`GetMaxMultiSampleCount`
- **调试**：`SetStringMarker`、`SetTextureName`
- **互操作**：`GetSysRenderer`、`CreateSysTexture`
- **ImGui**：`ImGuiInit`、`ImGuiNewFrame`、`ImGuiProcessEvent`、`ImGuiShutdown`

> 说明：`Effect*` 系列的**解析**已在后端无关的 `FNA3D_Effect.c` 完成，
> 驱动只需消费 `FNA3D_EffectShader.spirvData`（原始 SPIR-V）来创建 `VkShaderModule` 与管线。

### 2.4 SDL_GPU 使用面（需要被 Vulkan 替代的 API 集合）

对 [FNA3D_Driver_SDL.c](../FNA/lib/FNA3D/src/FNA3D_Driver_SDL.c) 全量扫描后，SDL_GPU 的使用可归为 11 类，
每类对应的 Vulkan 替代见 [§4](#4-sdl_gpu--vulkan-概念映射)。核心对象与调用包括：

- 设备/交换链：`SDL_CreateGPUDeviceWithProperties`、`SDL_ClaimWindowForGPUDevice`、
  `SDL_SetGPUSwapchainParameters`、`SDL_WaitAndAcquireGPUSwapchainTexture`、`SDL_GetGPUSwapchainTextureFormat`
- 命令与同步：`SDL_AcquireGPUCommandBuffer`、`SDL_SubmitGPUCommandBuffer(AndAcquireFence)`、
  `SDL_WaitForGPUFences`、`SDL_WaitForGPUIdle`、`SDL_ReleaseGPUFence`、`SDL_CancelGPUCommandBuffer`
- Pass：`SDL_Begin/EndGPURenderPass`、`SDL_Begin/EndGPUCopyPass`
- 管线/着色器：`SDL_CreateGPUShader`、`SDL_CreateGPUGraphicsPipeline`、`SDL_BindGPUGraphicsPipeline`
- 资源：`SDL_CreateGPUBuffer/Texture/Sampler/TransferBuffer` 及对应 `Release`
- 传输：`SDL_Map/UnmapGPUTransferBuffer`、`SDL_UploadToGPU*`、`SDL_DownloadFromGPU*`
- 绑定：`SDL_BindGPUVertexBuffers/IndexBuffer`、`SDL_BindGPUVertex/FragmentSamplers`
- Uniform：`SDL_PushGPUVertexUniformData`、`SDL_PushGPUFragmentUniformData`
- 绘制：`SDL_DrawGPUPrimitives`、`SDL_DrawGPUIndexedPrimitives`
- 动态状态：`SDL_SetGPUViewport/Scissor/BlendConstants/StencilReference`
- 其他：`SDL_BlitGPUTexture`、`SDL_GenerateMipmapsForGPUTexture`、格式/采样能力查询

---

## 3. 总体迁移策略与关键决策

### 决策 1：以「新增并行驱动」而非「原地替换」的方式实现

- 新建 [FNA3D_Driver_Vulkan.c](../FNA/lib/FNA3D/src/FNA3D_Driver_Vulkan.c)，
  与现有 SDL 驱动**并存**；用编译宏 `FNA3D_DRIVER_VULKAN` 控制编译。
- `drivers[]` 中把 Vulkan 驱动排在 SDL 之前（或用环境变量 `FNA3D_FORCE_DRIVER` 选择）。
- 好处：随时可回退对照，满足约束 C4；迁移期两后端可同时构建做差异测试。

### 决策 2：SDL3 保留，仅用于窗口与表面

- 继续依赖 SDL3，但只用 `SDL_Vulkan_CreateSurface`、`SDL_Vulkan_GetInstanceExtensions`、
  `SDL_Vulkan_GetDrawableSize` 等窗口相关 API，不再用 `SDL_GPU*`。
- 好处：FNA 的窗口/事件系统无需改动；`PrepareWindowAttributes` 只需返回 `SDL_WINDOW_VULKAN`。

### 决策 3：用 Vulkan Memory Allocator (VMA) 管理显存

- Vulkan 原生显存管理（`vkAllocateMemory` + 内存类型选择 + 子分配）繁琐易错。
- 引入 [VMA](https://github.com/GPUOpen-LibrariesAndSDKs/VulkanMemoryAllocator)（单头文件，作为 thirdparty 引入），
  统一处理缓冲/纹理/暂存缓冲的分配、对齐与内存类型选择。
- VMA 是 C++ 头文件；将其封装在一个独立的 `FNA3D_Vulkan_vma.cpp` TU 中，对 C 驱动暴露 C 接口。

### 决策 4：复用 SDL_GPU 的描述符集布局约定，实现 FEB 零重建（约束 C2 的技术根基）

FEB 中的 SPIR-V 是由 `feb_builder.py` 通过 DXC 的 `-fvk-bind-*` 标志、
**按 SDL_GPU 的固定描述符集布局**编译的（见 [HLSL-FEB-DEVELOPMENT-GUIDE.md](../FNA_Test/docs/HLSL-FEB-DEVELOPMENT-GUIDE.md) §2）。
只要新 Vulkan 后端**逐字复刻**这套 set/binding 布局来创建 `VkDescriptorSetLayout` 与 `VkPipelineLayout`，
即可直接加载现有 SPIR-V，无需改动 FEB 或工具链。详见 [§5](#5-描述符集与绑定模型兼容性核心)。

### 决策 5：渲染通道采用「传统 VkRenderPass + VkFramebuffer」

- 可选方案：Dynamic Rendering（`VK_KHR_dynamic_rendering`）代码更简，但需 Vulkan 1.3 / 扩展。
- **决策：先用传统 `VkRenderPass` + `VkFramebuffer`**，对 render pass 与 framebuffer 做哈希缓存
  （复用 [FNA3D_PipelineCache.c](../FNA/lib/FNA3D/src/FNA3D_PipelineCache.c) 的哈希基础设施）。
- 理由：兼容面最广（Vulkan 1.0 + 常见扩展），且能精确复刻 SDL_GPU 的 load/store/resolve 语义。
- 后续可作为优化项切换到 Dynamic Rendering。

### 决策 6：目标 Vulkan 版本与扩展基线

- **基线：Vulkan 1.1**（`VK_API_VERSION_1_1`），设备扩展：`VK_KHR_swapchain`。
- 可选扩展（存在则启用）：`VK_EXT_debug_utils`（调试标签/对象命名）、
  `VK_KHR_portability_subset`（MoltenVK/macOS）。
- 理由：1.1 覆盖率极高，且提供我们需要的多数功能；
  Uniform 采用动态 UBO（见 §5.3），无需 push descriptor。

---

## 4. SDL_GPU → Vulkan 概念映射

| SDL_GPU 概念 | Vulkan 对应 | 备注 |
|---|---|---|
| `SDL_GPUDevice` | `VkInstance` + `VkPhysicalDevice` + `VkDevice` + 队列 + VMA allocator | 单一图形队列（含 transfer 能力）即可满足 XNA 模型 |
| 交换链（Claim Window） | `VkSurfaceKHR`（`SDL_Vulkan_CreateSurface`）+ `VkSwapchainKHR` | 重建逻辑对应 `ResetBackbuffer` |
| `SDL_GPUCommandBuffer` | `VkCommandBuffer`（来自每帧 `VkCommandPool`） | 区分 render / upload 两条命令流 |
| Copy Pass | `vkCmdCopyBuffer* / vkCmdCopyImage*` + 屏障 | 无独立对象，直接记录到命令缓冲 |
| Render Pass | `VkRenderPass` + `VkFramebuffer` + `vkCmdBeginRenderPass` | 哈希缓存；load/store 映射见 §7 |
| `SDL_GPUGraphicsPipeline` | `VkPipeline`(graphics) + `VkPipelineLayout` | 复用现有管线哈希键 |
| `SDL_GPUShader`（SPIR-V） | `VkShaderModule` | SPIR-V 直接可用，**无需交叉编译** |
| `SDL_GPUBuffer` | `VkBuffer` + VMA 分配（DEVICE_LOCAL） | usage: VERTEX/INDEX/STORAGE/TRANSFER_DST |
| `SDL_GPUTransferBuffer` | `VkBuffer`（HOST_VISIBLE\|HOST_COHERENT）作暂存 | 环形分配，对应上传/回读 |
| `SDL_GPUTexture` | `VkImage` + `VkImageView` + VMA 分配 | 2D/3D/Cube/RT/DS |
| `SDL_GPUSampler` | `VkSampler` | 哈希缓存（复用 `SamplerStateHashArray`） |
| Push Uniform Data | 动态 UBO 环形缓冲 + `vkCmdBindDescriptorSets(dynamicOffset)` | 见 §5.3 |
| Bind Vertex/Index/Samplers | `vkCmdBindVertexBuffers/IndexBuffer` + 描述符集更新 | |
| `SDL_GPUFence` | `VkFence` | 帧同步（MAX_FRAMES_IN_FLIGHT = 3） |
| 交换链呈现 | `vkAcquireNextImageKHR` + `vkQueuePresentKHR` + `VkSemaphore` | |
| Blit / Mipmap | `vkCmdBlitImage`（循环生成 mip） | 需 `VK_IMAGE_USAGE_TRANSFER_SRC/DST` |
| 遮挡查询 | `VkQueryPool`(OCCLUSION) | 对应 `QueryPixelCount` |
| 能力/格式查询 | `vkGetPhysicalDeviceFormatProperties` / `...ImageFormatProperties` | 对应 `Supports*` / `GetMaxMultiSampleCount` |
| 调试标签 | `VK_EXT_debug_utils`（`vkCmdInsertDebugUtilsLabelEXT` / `vkSetDebugUtilsObjectNameEXT`） | 对应 `SetStringMarker` / `SetTextureName` |

---

## 5. 描述符集与绑定模型（兼容性核心）

> 本节是整个迁移可行性的技术根基。**新 Vulkan 后端必须逐字复刻**下述布局，
> 才能直接加载现有 FEB 中的 SPIR-V。依据：[HLSL-FEB-DEVELOPMENT-GUIDE.md](../FNA_Test/docs/HLSL-FEB-DEVELOPMENT-GUIDE.md) §2。

### 5.1 图形管线：4 个描述符集

| Set | 阶段 | 内容 | 绑定顺序 |
|---|---|---|---|
| **Set 0** | 顶点着色器 | 采样器(Combined Image Sampler) → 存储纹理 → 存储缓冲区 | Binding 0..S-1, S.., .. |
| **Set 1** | 顶点着色器 | Uniform Buffer（**Dynamic**） | Binding 0..U-1 |
| **Set 2** | 像素着色器 | 采样器 → 存储纹理 → 存储缓冲区 | Binding 0..S-1, .. |
| **Set 3** | 像素着色器 | Uniform Buffer（**Dynamic**） | Binding 0..U-1 |

`VkPipelineLayout` 必须以 **Set 0..3** 顺序包含上述 4 个 `VkDescriptorSetLayout`。

### 5.2 计算管线：3 个描述符集

| Set | 内容 |
|---|---|
| **Set 0** | 采样纹理 / 只读存储纹理 / 只读存储缓冲区 |
| **Set 1** | 读写存储缓冲区 / 读写存储纹理（`register(uN)`） |
| **Set 2** | Uniform Buffer（Dynamic） |

### 5.3 Uniform 推送 → 动态 UBO 环形缓冲

SDL_GPU 的 `SDL_PushGPUVertexUniformData` / `...FragmentUniformData` 语义是：
把一小块 uniform 数据压入一个内部环形缓冲，并在 draw 时以动态偏移绑定。Vulkan 复刻方案：

1. 每帧维护一个大的 HOST_VISIBLE UBO 环形缓冲（如 1 MiB，按 `minUniformBufferOffsetAlignment` 对齐）。
2. `ApplyEffect` 时，把 VS/PS 的 `$Globals` 数据 `memcpy` 进环形缓冲，记录本次 `dynamicOffset`。
3. Set 1 / Set 3 使用 `VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC`，
   draw 前 `vkCmdBindDescriptorSets` 传入对应 `dynamicOffset`。
4. 环形缓冲写满或帧结束时随帧 fence 回收。

### 5.4 强制采样器槽位

SDL_GPU 约定：即使着色器无纹理，Set 0 / Set 2 也至少有 1 个采样器绑定
（对应 SDL 驱动里的 `num_samplers = SDL_max(vs->samplerCount, 1)`）。
Vulkan 后端在构造 Set 0 / Set 2 布局时同样按 `max(samplerCount, 1)` 处理，
并用 dummy texture + dummy sampler 填充未使用槽位（SDL 驱动已有 `dummyTexture2D/3D/Cube` + `dummySampler`，逻辑照搬）。

### 5.5 资源计数来源

每个 `FNA3D_EffectShader` 已携带
（见 [FNA3D_Effect.h](../FNA/lib/FNA3D/src/FNA3D_Effect.h#L162-L178)）：
`samplerCount`、`uniformBufferCount`、`readonly/readwriteStorageBufferCount`、
`readonly/readwriteStorageTextureCount`、`threadCountX/Y/Z`。
这些由 `feb_builder.py` 反射 SPIR-V 得到，**足以驱动 `VkDescriptorSetLayout` 的构造**，
无需在运行时再做 SPIR-V 反射。

---

## 6. 新驱动模块分解

建议按职责拆分为若干内部子模块（可在同一 TU 内分区，或拆多文件）：

| 子模块 | 职责 | 对应 SDL 驱动区域 |
|---|---|---|
| `Vulkan_Instance` | 实例/校验层/物理设备选择/逻辑设备/队列 | `CreateDevice` 前半 |
| `Vulkan_Swapchain` | Surface、交换链、重建、呈现同步 | Claim Window + faux backbuffer |
| `Vulkan_Memory` (VMA 封装) | 缓冲/纹理/暂存分配 | `SDL_CreateGPUBuffer/Texture/TransferBuffer` |
| `Vulkan_Command` | 命令池、每帧命令缓冲、fence、上传/渲染双流 | `AcquireGPUCommandBuffer` 等 |
| `Vulkan_RenderPass` | render pass / framebuffer 哈希缓存 | `Begin/EndGPURenderPass` |
| `Vulkan_Pipeline` | graphics/compute 管线 + 布局 + shader module 缓存 | `CreateGPUGraphicsPipeline` |
| `Vulkan_Descriptor` | 描述符池/集分配、动态 UBO 环形缓冲 | 绑定 + Push uniform |
| `Vulkan_Texture` | 纹理创建/上传/回读/mipmap/YUV | `SetTextureData*` / `GetTextureData*` |
| `Vulkan_Buffer` | 顶点/索引/存储缓冲 + set/get | `Set/GetVertexBufferData` 等 |
| `Vulkan_State` | blend/depth/stencil/raster/sampler → 管线状态与动态状态 | 各 `SetXxxState` |
| `Vulkan_Effect` | 消费 SPIR-V → shader module + 参数 → UBO | `CreateEffect` / `ApplyEffect` |
| `Vulkan_Query` | 遮挡查询 | `Query*` |
| `Vulkan_ImGui` | ImGui Vulkan 后端桥接 | `ImGui*` |

数据结构：定义 `VULKAN_Renderer`（对标 `SDLGPU_Renderer`，见
[FNA3D_Driver_SDL.c](../FNA/lib/FNA3D/src/FNA3D_Driver_SDL.c#L545-L691)），
以及 `VulkanTexture`、`VulkanBuffer`、`VulkanEffect`（放入 `FNA3D_Effect.driverData`）、
`VulkanRenderbuffer`、`VulkanQuery` 等句柄结构。

---

## 7. 分阶段实施计划

> 每阶段结束都应有可运行/可验证的产物。建议每阶段用一个最小测试程序 gate。

### 阶段 0：脚手架与设备创建（可创建设备、清屏）

- 新增 [FNA3D_Driver_Vulkan.c](../FNA/lib/FNA3D/src/FNA3D_Driver_Vulkan.c)，
  定义 `VulkanDriver` 与 `Vulkan_PrepareWindowAttributes`（返回 `SDL_WINDOW_VULKAN`）。
- 在 [FNA3D.c](../FNA/lib/FNA3D/src/FNA3D.c#L43-L46) 的 `drivers[]` 注册 `&VulkanDriver`（置于 SDL 之前，宏保护）。
- 用 `ASSIGN_DRIVER(VULKAN)` 先把所有入口指向 stub（打印 `not implemented`）。
- 实现：VkInstance（+ 校验层 debug 模式）、物理设备选择、逻辑设备、图形队列、VMA 初始化。
- 实现 Surface（`SDL_Vulkan_CreateSurface`）+ 交换链创建 + `VkRenderPass`/`VkFramebuffer` 生成。
- 实现 `Clear` + `SwapBuffers`（acquire → begin pass → clear → end → submit → present）。
- **验收**：窗口显示为清屏颜色，帧循环稳定（`test_sprite` 改造为仅 GraphicsDevice.Clear）。

### 阶段 1：缓冲与纹理资源

- 顶点/索引缓冲：`GenVertexBuffer`、`GenIndexBuffer`、`Set/GetVertexBufferData`、`Set/GetIndexBufferData`。
  `SetDataOptions`（NONE/DISCARD/NOOVERWRITE）映射为「按帧 fence 决定是否 cycle 到新分配」，
  照搬 SDL 驱动的 cycle 语义。
- 暂存/传输：HOST_VISIBLE 环形暂存缓冲 + `vkCmdCopyBuffer`；回读用 DOWNLOAD 暂存 + fence 等待。
- 纹理：`CreateTexture2D/3D/Cube`、`SetTextureData2D/3D/Cube`、`GetTextureData2D/3D/Cube`；
  `vkCmdCopyBufferToImage` / `CopyImageToBuffer` + 布局屏障。
- 格式映射表：把 `XNAToSDL_SurfaceFormat`（[SDL 驱动 L111-L140](../FNA/lib/FNA3D/src/FNA3D_Driver_SDL.c#L111-L140)）
  重写为 `XNAToVK_SurfaceFormat`（`VkFormat`）；深度格式、采样数、比较/混合/模板/寻址/过滤同理。
- **验收**：能创建并回读一张纹理数据（`GetTextureData` == `SetTextureData`）。

### 阶段 2：着色器、管线与首个三角形

- `CreateEffect`：遍历 `FNA3D_Effect.shaders`，用 SPIR-V 创建 `VkShaderModule`；
  按 §5 的资源计数为每个 shader 构造/查找 Set 0..3（或计算 Set 0..2）`VkDescriptorSetLayout`。
- `Vulkan_Pipeline`：按管线哈希键（复用 `GraphicsPipelineHash`）创建/缓存 `VkPipeline` + `VkPipelineLayout`。
  顶点输入布局来自 `ApplyVertexBufferBindings`（`vertexDescriptions` / `vertexAttributes`）。
- `ApplyVertexBufferBindings`、`SetBlendState`、`SetDepthStencilState`、`ApplyRasterizerState`、
  `SetViewport`、`SetScissorRect`：填充管线状态与动态状态（viewport/scissor/blendConstants/stencilRef 用 `vkCmdSetX` 动态化）。
- `DrawPrimitives` / `DrawIndexedPrimitives` / `DrawInstancedPrimitives`。
- **验收**：`StockEffect/SpriteEffect` 与最简顶点色三角形正确渲染。

### 阶段 3：Uniform、纹理采样与完整 Effect 应用

- 动态 UBO 环形缓冲（§5.3）：`ApplyEffect` 写入 VS/PS `$Globals`，draw 时绑定动态偏移。
- `VerifySampler` / `VerifyVertexSampler`：创建/缓存 `VkSampler`，更新 Set 0 / Set 2 描述符集。
- dummy 资源填充（§5.4）。
- `SetEffectTechnique` / `SetEffectParamValue(ByHandle)` / `Begin/EndPassRestore`。
- **验收**：`BasicEffect`（4 technique）、`AlphaTestEffect`、`DualTextureEffect`、
  `SkinnedEffect`、`EnvironmentMapEffect` 全部通过。

### 阶段 4：渲染目标、MSAA 与后备缓冲管理

- `SetRenderTargets`（多 RT + 深度/模板）、`ResolveTarget`（MSAA resolve → `vkCmdResolveImage` 或 render pass resolve attachment）。
- `GenColorRenderbuffer` / `GenDepthStencilRenderbuffer` / `AddDisposeRenderbuffer`。
- faux backbuffer：把渲染结果 blit 到交换链图像（对应 SDL 的 `SDL_BlitGPUTexture`）；`ResetBackbuffer` 重建交换链。
- `ReadBackbuffer`、`GetBackbufferSize/SurfaceFormat/DepthFormat/MultiSampleCount`。
- mipmap 生成（`vkCmdBlitImage` 链）。
- **验收**：离屏渲染 + RT 采样 + MSAA 的测试用例通过。

### 阶段 5：计算着色器与存储缓冲

- 计算管线（Set 0..2，§5.2）、`Dispatch`（经 Effect/compute pass 路径）。
- 存储缓冲（`StructuredBuffer` / `RWStructuredBuffer`）+ 计算↔图形屏障。
- **验收**：`ComputeShaderEffect/ParticleFire`、`GPUInstancing/TrailEffect(Capture)` 通过。

### 阶段 6：查询、能力查询、调试、YUV、互操作

- `Query*`（`VkQueryPool` OCCLUSION）。
- `Supports*` / `GetMaxTextureSlots` / `GetMaxMultiSampleCount`（`vkGetPhysicalDevice*`）。
- `SetStringMarker` / `SetTextureName`（`VK_EXT_debug_utils`）。
- `SetTextureDataYUV`（对应 YUV→RGBA 上传路径）。
- `GetSysRenderer` / `CreateSysTexture`（§10）。
- **验收**：查询与调试工具（RenderDoc）验证无校验层错误。

### 阶段 7：ImGui、清理与优化

- ImGui Vulkan 后端桥接（§9）。
- 校验层零错误、显存无泄漏（VMA 统计）、fence/信号量正确回收。
- 可选优化：Dynamic Rendering、pipeline cache 落盘、descriptor 批量更新。
- **验收**：`run_tests.sh` 全量通过；与 SDL_GPU 后端逐程序目视/像素比对一致。

---

## 8. 构建系统改动

对 [CMakeLists.txt](../FNA/lib/FNA3D/CMakeLists.txt) 的改动：

1. **编译宏**：新增 `FNA3D_DRIVER_VULKAN` 选项，默认与 `FNA3D_DRIVER_SDL` 并存：
   ```cmake
   option(FNA3D_DRIVER_VULKAN "Build the native Vulkan backend" ON)
   if(FNA3D_DRIVER_VULKAN)
       add_definitions(-DFNA3D_DRIVER_VULKAN)
   endif()
   ```
2. **源文件**：把 `src/FNA3D_Driver_Vulkan.c` 与 VMA 封装 `src/FNA3D_Vulkan_vma.cpp` 加入 `add_library`。
3. **Vulkan 依赖**：
   ```cmake
   find_package(Vulkan REQUIRED)
   target_link_libraries(FNA3D PUBLIC Vulkan::Vulkan)
   ```
   或用 volk 动态加载入口（可作为可选项，避免硬链接 `vulkan-1`）。
4. **VMA**：作为 thirdparty 单头文件引入，`FNA3D_Vulkan_vma.cpp` 编译为 C++17 TU（沿用 ImGui 已启用的 `enable_language(CXX)` 模式）。
5. **SDL3**：保留现有 SDL3 链接（窗口/表面）；无需 SDL_GPU 特性开关。
6. **保持 C 语言隔离**：VMA/ImGui 的 C++ TU 与 C 驱动通过 C 接口交互，沿用现有 `-std=gnu99` 仅作用于 C 源的写法。

> 版本号：本次改动不改 `LIB_VERSION`（保持 ABI 兼容）。

---

## 9. Dear ImGui 集成

现状：`CMakeLists.txt` 编译 `imgui_impl_sdlgpu3.cpp` + `imgui_impl_sdl3.cpp`，
胶水在 `src/FNA3D_ImGui.cpp`（对 SDL 驱动暴露 `FNA3D_INTERNAL_ImGui*`，见
[SDL 驱动 L43-L57](../FNA/lib/FNA3D/src/FNA3D_Driver_SDL.c#L43-L57)）。

Vulkan 后端方案：

- 改用 `imgui_impl_vulkan.cpp`（imgui 自带，见 `thirdparty/imgui/backends/imgui_impl_vulkan.h`）+ 保留 `imgui_impl_sdl3.cpp`（事件）。
- `FNA3D_ImGui.cpp` 增加一套 Vulkan 变体的 `FNA3D_INTERNAL_ImGuiInit/NewFrame/Render/Shutdown`，
  按当前后端分派（编译宏或运行时判断）。ImGui Vulkan init 需要传入 instance/physicalDevice/device/queue/
  descriptor pool/render pass，这些从 `VULKAN_Renderer` 提供。
- `Vulkan_SwapBuffers` 在结束前调用 `FNA3D_INTERNAL_ImGuiRender(commandBuffer, ...)`，
  语义对标 SDL 驱动在 swap 时渲染 ImGui。

> ImGui 为可选（`FNA3D_IMGUI`）；两后端的 ImGui 桥接互斥编译，避免符号冲突。

---

## 10. 外部互操作（SysRenderer）

[FNA3D_SysRenderer.h](../FNA/lib/FNA3D/include/FNA3D_SysRenderer.h) 当前只定义了
`FNA3D_RENDERER_TYPE_SDL_GPU_EXT`。为 Vulkan 后端：

- 新增枚举 `FNA3D_RENDERER_TYPE_VULKAN_EXT`（追加值，不改动已有值，保持 ABI）。
- 定义 Vulkan 版本的 `renderer` union 布局（`VkInstance`/`VkPhysicalDevice`/`VkDevice`/`VkQueue`/queueFamilyIndex 等），
  以及 `texture` union（`VkImage`/`VkImageView`/`VkFormat`）。填充在 64 字节 filler 内。
- `Vulkan_GetSysRenderer` / `Vulkan_CreateSysTexture` 实现相应导出。

> 该扩展默认不参与主流程，属于低优先级（阶段 6）。

---

## 11. 风险与缓解

| 风险 | 影响 | 缓解 |
|---|---|---|
| 描述符集布局与 SDL_GPU 有细微差异 | FEB 读到零/垃圾数据，渲染错误 | 严格按 §5 复刻；用 `spirv-dis` 核对若干 FEB 的 set/binding；先跑 §12 的最小 shader 验证 |
| 动态 UBO 对齐/环形回绕 bug | 偶发闪烁/错帧 | 按 `minUniformBufferOffsetAlignment` 对齐；环形缓冲随帧 fence 回收；写满则扩容 |
| 资源生命周期（删除仍在飞行中的资源） | 校验层报错/崩溃 | 延迟销毁队列：资源入队，等其所属帧 fence 完成后再 `vkDestroy*`（对标 SDL cycle 语义） |
| 交换链重建（窗口缩放/最小化） | 呈现失败 `VK_ERROR_OUT_OF_DATE_KHR` | acquire/present 返回码统一处理，触发 `ResetBackbuffer` 路径 |
| MoltenVK/macOS 差异 | 无法运行 | 启用 `VK_KHR_portability_subset`；baseVertex 等特性按 SDL 驱动既有 FIXME 处理 |
| VMA 的 C++ 与 C 驱动混编 | 构建复杂 | 严格 C 接口边界；沿用现有 ImGui 的 C/C++ 混编方案 |
| 校验层性能/噪声 | 调试效率 | 仅 debug 模式启用校验层与 debug_utils |

---

## 12. 测试与验收计划

### 12.1 分层验证

1. **最小 shader 布局验证**（阶段 2 前置）：取一个已知 FEB（如 `ParticleEffect.feb`），
   用 `spirv-dis` 打印其 `DescriptorSet`/`Binding`，与 §5 表对照，确认布局一致。
2. **单元级**：纹理/缓冲 round-trip（Set==Get）、离屏 RT 回读像素。
3. **集成级**：逐个运行现有测试工程（见下）。
4. **对照级**：同一程序分别用 SDL_GPU 与 Vulkan 后端运行，截帧像素比对。

### 12.2 现有测试工程覆盖矩阵

| 测试工程 | 覆盖能力 | 阶段 |
|---|---|---|
| `test_sprite` | 清屏、SpriteBatch、基础纹理 | 0–3 |
| `StockEffect/SpriteEffect` | 单 technique、纹理采样 | 2–3 |
| `StockEffect/BasicEffect(Matrix)` | 4 technique、uniform、光照分支 | 3 |
| `StockEffect/AlphaTest/DualTexture/Skinned/EnvironmentMap` | 多布局、多纹理、蒙皮 | 3 |
| `ComputeShaderEffect/ParticleFire` | 计算着色器、存储缓冲、GPU 实例化 | 5 |
| `GPUInstancing/TrailEffect(Capture)` | 实例化、存储缓冲、capture | 5 |

### 12.3 自动化

- 复用 [run_tests.sh](../FNA_Test/run_tests.sh)：新增 `FNA3D_FORCE_DRIVER=Vulkan` 环境变量分支，
  对全部程序跑一遍构建+运行；SDL_GPU 后端作为对照基线。
- 校验层零错误、VMA 无泄漏纳入验收门槛。

---

## 13. 里程碑与工作量估算

| 里程碑 | 内容 | 相对工作量 |
|---|---|---|
| M0 | 阶段 0：设备/交换链/清屏 | 中 |
| M1 | 阶段 1：缓冲/纹理资源与传输 | 大 |
| M2 | 阶段 2–3：管线/着色器/Uniform/采样（首个 Effect 全通） | 大 |
| M3 | 阶段 4：RT/MSAA/后备缓冲 | 中 |
| M4 | 阶段 5：计算/存储缓冲 | 中 |
| M5 | 阶段 6–7：查询/调试/ImGui/互操作/优化，全量测试 | 中 |

> 关键路径是 M1→M2：资源与描述符模型一旦稳定，其余入口多为「填表 + 命令记录」。
> 建议在 M0 完成后立即做 §12.1 的 shader 布局验证，尽早暴露 §5 复刻偏差。

---

## 附：实现顺序速查（Checklist）

- [ ] 新建 `FNA3D_Driver_Vulkan.c` + `VulkanDriver` 注册进 `drivers[]`（宏保护）
- [ ] CMake 加 `FNA3D_DRIVER_VULKAN`、`find_package(Vulkan)`、VMA TU
- [ ] Instance/Device/Queue/VMA/Surface/Swapchain/RenderPass/Framebuffer
- [ ] Clear + SwapBuffers（清屏跑通）
- [ ] 顶点/索引缓冲 + 暂存传输 + 纹理创建/上传/回读 + 格式映射表
- [ ] ShaderModule + 描述符集布局（严格复刻 §5）+ 管线缓存 + 顶点输入
- [ ] Draw* + 动态状态（viewport/scissor/blend/stencil）
- [ ] 动态 UBO 环形缓冲 + VerifySampler + dummy 资源
- [ ] SetRenderTargets/ResolveTarget/Renderbuffer/faux backbuffer/ReadBackbuffer/mipmap
- [ ] 计算管线 + 存储缓冲 + 屏障
- [ ] Query/Supports*/调试标签/YUV/SysRenderer
- [ ] ImGui Vulkan 桥接
- [ ] 延迟销毁队列 + 校验层零错误 + VMA 无泄漏
- [ ] run_tests.sh 全量通过 + 与 SDL_GPU 后端像素比对
