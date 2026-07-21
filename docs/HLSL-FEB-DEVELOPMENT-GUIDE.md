# FNA3D_HLSL 着色器开发指南：描述符集、feb_builder 与 FEB 管线

本文档面向编写自定义 HLSL 着色器并在 FNA3D_HLSL 运行时加载的 FNA 开发者。它系统地解释了从 HLSL 源码到 GPU 的完整链路中最重要的一个环节——**SDL3 描述符集布局**——以及构建工具 `feb_builder.py` 的用法和原理。

无论你是第一次在 FNA3D_HLSL 上编写着色器，还是已经踩过描述符集不匹配的坑，本文档都旨在提供一份完整的参考。

---

## 目录

1. [背景：完整着色器管线](#1-背景完整着色器管线)
2. [SDL3 描述符集布局](#2-sdl3-描述符集布局)
   - [2.1 为什么需要了解描述符集](#21-为什么需要了解描述符集)
   - [2.2 图形管线的四集合布局](#22-图形管线的四集合布局)
   - [2.3 计算管线的三集合布局](#23-计算管线的三集合布局)
   - [2.4 强制采样器槽位](#24-强制采样器槽位)
3. [HLSL 资源注册：register 语法](#3-hlsl-资源注册register-语法)
   - [3.1 寄存器类型速查](#31-寄存器类型速查)
   - [3.2 隐式 $Globals 常量缓冲区](#32-隐式-globals-常量缓冲区)
   - [3.3 资源类型与寄存器的对应关系](#33-资源类型与寄存器的对应关系)
   - [3.4 存储缓冲区寄存器的绑定偏移](#34-存储缓冲区寄存器的绑定偏移)
4. [feb_builder.py 详解](#4-feb_builderpy-详解)
   - [4.1 概述](#41-概述)
   - [4.2 命令行用法](#42-命令行用法)
   - [4.3 编译流程](#43-编译流程)
   - [4.4 寄存器扫描](#44-寄存器扫描)
   - [4.5 DXC 标志生成规则](#45-dxc-标志生成规则)
   - [4.6 SPIR-V 反射](#46-spir-v-反射)
   - [4.7 FEB 二进制格式](#47-feb-二进制格式)
5. [.feb.json 清单文件参考](#5-febjson-清单文件参考)
   - [5.1 完整结构](#51-完整结构)
   - [5.2 参数类型](#52-参数类型)
   - [5.3 register 编号规则](#53-register-编号规则)
6. [完整示例](#6-完整示例)
   - [6.1 最简着色器（无纹理、仅顶点色）](#61-最简着色器无纹理仅顶点色)
   - [6.2 带纹理和 Uniform 的着色器](#62-带纹理和-uniform-的着色器)
   - [6.3 顶点着色器中的结构化缓冲区（存储缓冲区）](#63-顶点着色器中的结构化缓冲区存储缓冲区)
   - [6.4 计算着色器](#64-计算着色器)
   - [6.5 多 Technique 效果](#65-多-technique-效果)
7. [手动编译参考（DXC 标志速查）](#7-手动编译参考dxc-标志速查)
8. [故障排查](#8-故障排查)

---

## 1. 背景：完整着色器管线

在 FNA3D_HLSL 中，着色器从编写到 GPU 执行经历以下流程：

```
HLSL 源码 (.hlsl)
  │
  ▼
DXC（DirectX Shader Compiler）
  将 HLSL 编译为 SPIR-V 二进制。
  关键：通过命令行标志（-fvk-bind-globals, -fvk-bind-register）
  指定每个资源在 Vulkan 描述符集中的位置。
  │
  ▼
feb_builder.py
  读取 .hlsl 源码和 .feb.json 清单文件。
  调用 DXC 编译，反射 SPIR-V 获取资源计数。
  将所有着色器 + 元数据打包为一个 .feb 二进制文件。
  │
  ▼
FNA3D_CreateEffect()
  运行时解析 .feb 二进制。
  通过 SDL_GPU API 创建 GPU 着色器对象。
  │
  ▼
SDL_GPU（底层：Vulkan/D3D12/Metal）
  根据 SDL_GPUShaderCreateInfo 中的资源计数
  创建描述符集布局和管线布局。
  绑定资源，执行绘制。
```

**核心问题：** 这个链条中的每一步都对"资源放在哪个描述符集的哪个绑定位"有自己的假设。DXC 有默认值，SDL_GPU 有硬编码的布局约定，`feb_builder.py` 通过 DXC 标志让两者对齐。如果任何一步的假设不一致，着色器就会读到垃圾数据或零值。

---

## 2. SDL3 描述符集布局

### 2.1 为什么需要了解描述符集

现代 GPU API（Vulkan、D3D12、Metal）将着色器可访问的资源组织为**描述符集（Descriptor Set）**——一组绑定位的集合。着色器通过在源码中声明 `DescriptorSet N, Binding M` 来引用特定资源。

SDL3 的 GPU 子系统封装了这些底层 API，定义了一套**固定的描述符集布局**。所有通过 `SDL_GPU_SHADERFORMAT_SPIRV` 加载的 SPIR-V 着色器都必须遵循这套布局。**SDL_GPU 不会对你的 SPIR-V 做任何绑定位重映射**——着色器代码中的集合/绑定必须与 SDL_GPU 内部创建的管线布局完全一致。

这套布局定义在 SDL3 源码的 Vulkan 后端中（`src/gpu/vulkan/SDL_gpu_vulkan.c`），并由 SDL_shadercross 的 Metal 交叉编译路径显式校验（`src/SDL_shadercross.c`）。

### 2.2 图形管线的四集合布局

图形管线（顶点着色器 + 像素着色器）使用 **4 个描述符集**：

```
┌─────────────────────────────────────────────────────────┐
│  Set 0 — 顶点着色器 读取资源 (VS read-resources)          │
│  ├─ Binding 0..S-1:   Combined Image Samplers (samplers) │
│  ├─ Binding S..S+T-1: Sampled Images (storage textures)  │
│  └─ Binding S+T..:    Storage Buffers (StructuredBuffer) │
├─────────────────────────────────────────────────────────┤
│  Set 1 — 顶点着色器 Uniform Buffers (VS UBO)              │
│  └─ Binding 0..U-1:   Uniform Buffer Dynamic             │
├─────────────────────────────────────────────────────────┤
│  Set 2 — 像素着色器 读取资源 (PS read-resources)          │
│  ├─ Binding 0..S-1:   Combined Image Samplers            │
│  ├─ Binding S..S+T-1: Sampled Images (storage textures)  │
│  └─ Binding S+T..:    Storage Buffers                    │
├─────────────────────────────────────────────────────────┤
│  Set 3 — 像素着色器 Uniform Buffers (PS UBO)              │
│  └─ Binding 0..U-1:   Uniform Buffer Dynamic             │
└─────────────────────────────────────────────────────────┘
```

用表格表示：

| 资源类型 | 顶点着色器 (VS) | 像素着色器 (PS) |
|---|---|---|
| 采样纹理（Texture + Sampler） | **Set 0** | **Set 2** |
| 存储纹理（只读 Storage Texture） | **Set 0** | **Set 2** |
| **存储缓冲区（只读 StructuredBuffer）** | **Set 0** | **Set 2** |
| **Uniform Buffer（`register(cN)` 常量）** | **Set 1** | **Set 3** |

关键结论：

- **VS 和 PS 的读取资源在不同的 Set 中**。Set 0 只管 VS，Set 2 只管 PS。
- **UBO 独占一个 Set**，不与采样器/存储缓冲区共享。
- **同一 Set 内的绑定编号是连续的**，从 Binding 0 开始。

### 2.3 计算管线的三集合布局

计算管线只有 **3 个描述符集**：

| 资源类型 | 计算着色器 (CS) |
|---|---|
| 采样纹理、存储纹理（只读） | **Set 0** |
| 存储缓冲区（只读 / 读写） | **Set 1** |
| Uniform Buffer | **Set 2** |

计算着色器的 RW 资源（`RWStructuredBuffer`, `RWTexture2D` 等）通过 `register(uN)` 声明，放在 **Set 1**。

### 2.4 强制采样器槽位

SDL_GPU 要求**即使着色器完全不需要纹理，也必须至少声明 1 个采样器槽位**。这确保了描述符集被正确创建和绑定（空的描述符集会导致 Vulkan 验证层报 "descriptor set N not bound"）。

FNA3D 在创建着色器时会自动处理：

```c
// FNA3D_Driver_SDL.c: SDLGPU_CreateEffect()
createInfo.num_samplers = SDL_max(vs->samplerCount, 1);
```

**这意味着：** 在 Set 0（VS）和 Set 2（PS）中，Binding 0 永远被一个采样器槽位占用，**即使你的着色器没有声明任何纹理**。如果你在 VS 中使用存储缓冲区，它的绑定位应该从 **Binding 1** 开始。

---

## 3. HLSL 资源注册：`register` 语法

### 3.1 寄存器类型速查

HLSL 使用 `register` 语法声明资源在 Direct3D 寄存器空间中的位置：

| 语法 | 含义 | D3D 寄存器类型 |
|---|---|---|
| `register(cN)` | 常量缓冲区（Constant Buffer） | float4 常量寄存器 |
| `register(tN)` | 着色器资源视图（SRV） | 纹理 / StructuredBuffer / ByteAddressBuffer |
| `register(sN)` | 采样器（Sampler） | SamplerState |
| `register(uN)` | 无序访问视图（UAV） | RWStructuredBuffer / RWTexture2D |
| `register(bN)` | 显式常量缓冲区 | cbuffer（不常用） |

### 3.2 隐式 `$Globals` 常量缓冲区

当你使用 `register(cN)` 声明独立的 uniform 变量时：

```hlsl
float4x4 WorldViewProj : register(c0);
float     Time         : register(c4);
float4    Diffuse      : register(c5);
```

DXC 会将它们收集到一个**隐式常量缓冲区**中，命名为 `$Globals`。在生成的 SPIR-V 中，这个缓冲区被放置在 `register(b0)`，并默认映射到 **DescriptorSet 0, Binding 0**。

这就是为什么需要 `-fvk-bind-globals` 标志——将 `$Globals` 从 DXC 默认的 Set 0 移到 SDL_GPU 要求的 Set 1（VS）或 Set 3（PS）。

### 3.3 资源类型与寄存器的对应关系

| HLSL 声明 | 寄存器 | DXC 默认 Set | SDL_GPU 要求 Set (VS) | SDL_GPU 要求 Set (PS) |
|---|---|---|---|---|
| `float4x4 mat : register(c0)` | `c0` | Set 0 | **Set 1** | **Set 3** |
| `Texture2D tex : register(t0)` | `t0` | Set 0 | Set 0 | **Set 2** |
| `SamplerState samp : register(s0)` | `s0` | Set 0 | Set 0 | **Set 2** |
| `StructuredBuffer<T> buf : register(tN)` | `tN` | Set 0 | Set 0 | Set 2 |
| `RWStructuredBuffer<T> buf : register(uN)` | `uN` | Set 0 | Set 0 | Set 2 |
| `RWTexture2D<T> tex : register(uN)` | `uN` | Set 0 | Set 0 | Set 2 |

### 3.4 存储缓冲区寄存器的绑定偏移

由于 SDL_GPU 强制要求至少 1 个采样器槽位（见 [§2.4](#24-强制采样器槽位)），Set 0 和 Set 2 的 **Binding 0 永远被占用**。当你在顶点或像素着色器中使用 `StructuredBuffer`（`register(tN)`）或 `RWStructuredBuffer`（`register(uN)`）时，必须考虑这个偏移。

`feb_builder.py` 自动处理这个偏移：

- 对于 `t` 寄存器：直接使用寄存器编号作为绑定编号。如果编号与采样器冲突，建议**跳号使用**（例如用 `t1` 代替 `t0`）。
- 对于 `u` 寄存器：自动将绑定编号偏移到 `max(1, 最大采样器寄存器编号 + 1)` 之后。

**推荐写法：**

```hlsl
// 顶点着色器中的只读结构化缓冲区
// 使用 t1（而非 t0）避免与强制采样器槽位冲突
StructuredBuffer<float4> TrailData : register(t1);

// 读写结构化缓冲区（顶点/像素着色器中很少见）
RWStructuredBuffer<uint> Counter : register(u0);
// ↑ feb_builder 自动将其绑定偏移到 Binding 1
```

---

## 4. feb_builder.py 详解

### 4.1 概述

`feb_builder.py` 位于 `FNA/tools/feb_builder.py`，是 FEB（FNA3D Effect Binary）的构建工具。它的核心职责：

1. **调用 DXC** 将 HLSL 编译为 SPIR-V，并附加正确的描述符集修正标志
2. **反射 SPIR-V** 获取着色器资源计数（采样器数量、存储缓冲区数量等）
3. **打包**所有着色器 SPIR-V 二进制 + 元数据到单个 `.feb` 文件中

**依赖：** 需要系统已安装 `dxc`（DirectX Shader Compiler）命令行工具。

### 4.2 命令行用法

```bash
python3 tools/feb_builder.py <清单文件.json>
```

输出文件命名规则：

| 清单文件名 | 输出文件名 |
|---|---|
| `Trail.feb.json` | `Trail.feb` |
| `Effect.json` | `Effect.feb` |
| `whatever.json` | `whatever.feb` |

清单文件路径的目录部分会被用作 HLSL 源码路径的基准目录。

### 4.3 编译流程

`feb_builder.py` 的完整处理流程如下：

```
1. 读取 .feb.json 清单文件
   │
2. 遍历每个 Technique → Pass → Shader
   │
3. 对每个着色器：
   ├─ 3a. 扫描 HLSL 源码，提取 register(tN)、register(sN)、register(uN)
   │      → 得到 ti, si, ui 三个索引集合
   │
   ├─ 3b. 构建 DXC 命令行：
   │      基础标志：dxc -spirv -T <profile> -E <entry> <source> -Fo <output>
   │      全局常量绑定：-fvk-bind-globals 0 <globals_set>
   │      寄存器绑定：-fvk-bind-register <reg> 0 <binding> <set>
   │        （根据着色器阶段和寄存器类型自动选择正确的 set/binding）
   │
   ├─ 3c. 运行 DXC，生成 SPIR-V 二进制
   │
   ├─ 3d. 反射 SPIR-V 二进制，提取资源计数
   │      → samplerCount, uniformBufferCount,
   │        readonlyStorageBufferCount, readwriteStorageBufferCount
   │
   └─ 3e. 记录着色器元数据（入口名、SPIR-V 数据、资源计数）
   │
4. 将所有着色器 + 参数 + Technique/Pass 结构打包为 FEB 二进制
```

### 4.4 寄存器扫描

`scan_hlsl_registers()` 函数用正则表达式扫描 HLSL 源码：

```python
re.findall(r"register\(\s*([tsu])(\d+)", src)
```

它匹配 `register(t0)`, `register(s1)`, `register(u0)` 等模式，返回三类寄存器的索引集合。

**注意：** 注释中的 register 语句也会被匹配。扫描前会先移除 C 风格注释（`//` 和 `/* */`），但不会解析 `#if` 预处理指令。

### 4.5 DXC 标志生成规则

`feb_builder.py` 根据着色器阶段自动选择正确的描述符集，并生成对应的 DXC 标志。

#### 全局常量缓冲区（`$Globals`）

所有 `register(cN)` 变量共享一个 `$Globals` 缓冲区。通过 `-fvk-bind-globals` 标志设置其描述符集：

| 着色器阶段 | `$Globals` 目标集 | 生成的标志 |
|---|---|---|
| vertex | Set 1 | `-fvk-bind-globals 0 1` |
| pixel | Set 3 | `-fvk-bind-globals 0 3` |
| compute | Set 2 | `-fvk-bind-globals 0 2` |

#### 纹理和采样器寄存器（`t` / `s`）

`-fvk-bind-register` 标志的格式为：

```
-fvk-bind-register <类型-编号> <space> <binding> <set>
```

- `<类型-编号>`：如 `t0`, `s1`
- `<space>`：寄存器空间，始终为 `0`（D3D 默认空间）
- `<binding>`：目标绑定位。对于 `t`/`s` 寄存器，等于寄存器编号
- `<set>`：目标描述符集

| 着色器阶段 | `t`/`s` 目标集 |
|---|---|
| vertex | Set 0 (`ss="0"`) |
| pixel | Set 2 (`ss="2"`) |

示例——像素着色器中的 `Texture2D tex : register(t0)` 和 `SamplerState samp : register(s0)`：

```bash
-fvk-bind-register t0 0 0 2   # t0 → Set 2, Binding 0
-fvk-bind-register s0 0 0 2   # s0 → Set 2, Binding 0
```

#### UAV 寄存器（`u`）——图形阶段

对于顶点/像素着色器中的 `register(uN)`（`RWStructuredBuffer` 等），`feb_builder.py` 自动计算偏移以避免与采样器冲突：

```
u_base = max(1, max(ti ∪ si) + 1)
```

每个 `u` 寄存器从 `u_base` 开始顺序分配绑定编号。

#### UAV 寄存器（`u`）——计算阶段

计算着色器中的 `register(uN)` 放在 **Set 1**：

```bash
-fvk-bind-register u0 0 0 1   # u0 → Set 1, Binding 0
```

计算着色器中的 `t`/`s` 寄存器放在 **Set 0**。

### 4.6 SPIR-V 反射

编译出 SPIR-V 后，`reflect_spirv()` 函数解析其二进制结构，提取以下信息：

| 反射字段 | 含义 | 用途 |
|---|---|---|
| `samplers` | 采样器（Combined Image Sampler）数量 | 填充 `FNA3D_EffectShader.samplerCount` |
| `uniforms` | Uniform Buffer 数量 | 填充 `FNA3D_EffectShader.uniformBufferCount` |
| `readonlyStorageBufferCount` | 只读存储缓冲区数量 | `num_storage_buffers` 的一部分 |
| `readwriteStorageBufferCount` | 读写存储缓冲区数量 | `num_storage_buffers` 的另一部分 |
| `threadCountX/Y/Z` | 计算着色器线程组大小 | 用于 Dispatch |

反射逻辑按 SPIR-V 变量的**存储类**和**装饰**分类：

```python
StorageClass UniformConstant → 采样器 / 存储纹理
  ├─ NonWritable 且 !NonReadable → 只读存储纹理
  ├─ NonReadable 且 !NonWritable → 读写存储纹理
  └─ 其他 → 采样器

StorageClass StorageBuffer → 存储缓冲区
  ├─ NonReadable 且 !NonWritable → 读写存储缓冲区
  └─ 其他 → 只读存储缓冲区

StorageClass Uniform → Uniform Buffer 或存储缓冲区
  ├─ DescriptorSet == globals_set → Uniform Buffer
  ├─ NonReadable 且 !NonWritable → 读写存储缓冲区
  └─ 其他 → 只读存储缓冲区
```

### 4.7 FEB 二进制格式

FEB 文件是一个自包含的二进制包，结构如下：

```
┌──────────────────────────────────────┐
│  Header (64 bytes)                   │
│  ├─ Magic: "FNA\x46" (0x42414E46)    │
│  ├─ Version: 2                       │
│  ├─ 技术数、Pass 数、参数数、着色器数  │
│  └─ 各 Section 的偏移量               │
├──────────────────────────────────────┤
│  String Table（所有名称和语义字符串）   │
├──────────────────────────────────────┤
│  Parameters（每个参数 84+ 字节）       │
│  ├─ 名称、语义（字符串表偏移）         │
│  ├─ 类型、寄存器编号                  │
│  └─ 默认值（16 个 float）             │
├──────────────────────────────────────┤
│  Techniques（每个 16 字节）            │
├──────────────────────────────────────┤
│  Passes（每个 24 字节）               │
│  ├─ VS/PS/CS 着色器索引               │
├──────────────────────────────────────┤
│  Shader Entries（每个 52 字节）        │
│  ├─ 阶段、入口名、SPIR-V 偏移/大小     │
│  ├─ 采样器数、UBO 数                  │
│  └─ 线程组大小（仅计算着色器）         │
├──────────────────────────────────────┤
│  SPIR-V Blobs（原始二进制拼接）        │
└──────────────────────────────────────┘
```

---

## 5. .feb.json 清单文件参考

### 5.1 完整结构

```json
{
  "techniques": [
    {
      "name": "TechniqueName",
      "passes": [
        {
          "name": "P0",
          "vertexShader": {
            "source": "path/to/vs.hlsl",
            "entry": "VSMain"
          },
          "pixelShader": {
            "source": "path/to/ps.hlsl",
            "entry": "PSMain"
          },
          "computeShader": {
            "source": "path/to/cs.hlsl",
            "entry": "CSMain"
          }
        }
      ]
    }
  ],
  "parameters": [
    {
      "name": "WorldViewProj",
      "type": "MATRIX",
      "register": 0,
      "default": [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]
    }
  ]
}
```

每个 Pass 可以包含 vertexShader、pixelShader、computeShader 的任意组合。图形 Pass 通常有 VS+PS，计算 Pass 只有 CS。

**旧版清单格式：** 旧版 `.feb.json` 允许在 `vertexShader` / `pixelShader` 对象中手动声明 `"samplers": N` 和 `"uniforms": M` 字段。这些字段**现在已废弃**——`feb_builder.py` 会通过 SPIR-V 反射自动获取资源计数，清单中的值会被忽略。你仍然可以保留它们以保证与旧版工具的兼容性，但不再需要手动填写。

### 5.2 参数类型

| 类型 | 占用寄存器数 | C# 类型 | HLSL 声明示例 |
|---|---|---|---|
| `FLOAT` | 1 | `float` | `float Time : register(c0);` |
| `FLOAT2` | 1 | `Vector2` | `float2 Size : register(c1);` |
| `FLOAT3` | 1 | `Vector3` | `float3 Dir : register(c2);` |
| `FLOAT4` | 1 | `Vector4` / `Color` | `float4 Color : register(c3);` |
| `INT` | 1 | `int` | `int Mode : register(c4);` |
| `BOOL` | 1 | `bool` | `bool Enable : register(c5);` |
| `MATRIX` | 4 | `Matrix` | `float4x4 WVP : register(c0);` ← 占用 c0-c3 |
| `TEXTURE` / `TEXTURE2D` 等 | — | `Texture2D` | 通过 `register(tN)` 声明 |

**重要：**
- `MATRIX` 消耗 4 个连续的 `c` 寄存器（每个 `float4x4` 的行占用 1 个 `float4` 寄存器）。
- 在 `.feb.json` 的 `parameters` 中，`register` 是**起始**寄存器编号。
- 后续参数的 `register` 必须考虑前面 MATRIX 参数占用的空间。例如 `c0` 放了 MATRIX 后，下一个参数必须从 `c4` 开始。

### 5.3 register 编号规则

在 `.feb.json` 参数声明和 HLSL 源码之间保持寄存器编号的一致性至关重要。

```
验证方法：
# 对比 HLSL 和 FEB manifest 中的寄存器
grep "register" shader_vs.hlsl shader_ps.hlsl
python3 -c "import json; [print(p['name'], p['register']) for p in json.load(open('Effect.feb.json'))['parameters']]"
```

---

## 6. 完整示例

### 6.1 最简着色器（无纹理、仅顶点色）

**顶点着色器 (`simple_vs.hlsl`)：**

```hlsl
float4x4 WorldViewProj : register(c0);

struct VS_INPUT
{
    float4 Position : POSITION0;
    float4 Color    : COLOR0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;
    output.Position = mul(input.Position, WorldViewProj);
    output.Color = input.Color;
    return output;
}
```

**像素着色器 (`simple_ps.hlsl`)：**

```hlsl
float4 PSMain(float4 color : COLOR0) : SV_TARGET0
{
    return color;
}
```

**清单文件 (`simple.feb.json`)：**

```json
{
  "techniques": [
    {
      "name": "Simple",
      "passes": [
        {
          "name": "P0",
          "vertexShader": {"source": "simple_vs.hlsl", "entry": "VSMain"},
          "pixelShader":  {"source": "simple_ps.hlsl", "entry": "PSMain"}
        }
      ]
    }
  ],
  "parameters": [
    {
      "name": "WorldViewProj",
      "type": "MATRIX",
      "register": 0,
      "default": [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]
    }
  ]
}
```

**构建：**

```bash
python3 tools/feb_builder.py simple.feb.json
# → 输出 simple.feb
```

**描述符集分配情况：**

| 着色器 | 资源 | SPIR-V 中的位置 |
|---|---|---|
| VS | `$Globals` (WorldViewProj) | Set 1, Binding 0 |
| VS | 强制采样器 | Set 0, Binding 0 |
| PS | 无资源 | — |

### 6.2 带纹理和 Uniform 的着色器

**顶点着色器 (`tex_vs.hlsl`)：**

```hlsl
float4x4 WorldViewProj : register(c0);

struct VS_INPUT
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;
    output.Position = mul(input.Position, WorldViewProj);
    output.TexCoord = input.TexCoord;
    return output;
}
```

**像素着色器 (`tex_ps.hlsl`)：**

```hlsl
Texture2D<float4> Texture    : register(t0);
SamplerState     TexSampler  : register(s0);
float4           TintColor   : register(c0);

// ⚠️ 参数顺序必须与 VS_OUTPUT 字段顺序一致！
// VS_OUTPUT: SV_POSITION, TEXCOORD0
float4 PSMain(float2 texCoord : TEXCOORD0) : SV_TARGET0
{
    return Texture.Sample(TexSampler, texCoord) * TintColor;
}
```

**清单文件 (`tex.feb.json`)：**

```json
{
  "techniques": [
    {
      "name": "Textured",
      "passes": [
        {
          "name": "P0",
          "vertexShader": {"source": "tex_vs.hlsl", "entry": "VSMain"},
          "pixelShader":  {"source": "tex_ps.hlsl", "entry": "PSMain"}
        }
      ]
    }
  ],
  "parameters": [
    {
      "name": "WorldViewProj",
      "type": "MATRIX",
      "register": 0,
      "default": [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]
    },
    {
      "name": "TintColor",
      "type": "FLOAT4",
      "register": 0,
      "default": [1, 1, 1, 1]
    }
  ]
}
```

**描述符集分配情况：**

| 着色器 | 资源 | SPIR-V 中的位置 |
|---|---|---|
| VS | `$Globals` (WorldViewProj) | Set 1, Binding 0 |
| VS | 强制采样器 | Set 0, Binding 0 |
| PS | `Texture` + `TexSampler` | Set 2, Binding 0 |
| PS | `$Globals` (TintColor) | Set 3, Binding 0 |

**注意：** VS 的 `WorldViewProj` 在 `c0`，PS 的 `TintColor` 也在 `c0`。因为它们在不同的着色器阶段，共享寄存器编号不会有冲突——VS 的 `$Globals` 在 Set 1，PS 的 `$Globals` 在 Set 3，是完全独立的。

### 6.3 顶点着色器中的结构化缓冲区（存储缓冲区）

这是从 CPU 上传的 `StorageBuffer` 读取数据的完整示例（Trail Effect Capture 项目的简化版）。

**顶点着色器 (`storage_vs.hlsl`)：**

```hlsl
// 使用 t1（而非 t0）避免与强制采样器槽位冲突
StructuredBuffer<float4> VertexData : register(t1);

float4x4 ViewProj : register(c0);
float    Time     : register(c4);

struct VS_INPUT
{
    float3 DummyPos : POSITION0;
    float3 DummyNrm : NORMAL0;
    float2 DummyUV  : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
};

VS_OUTPUT VSMain(VS_INPUT input, uint vertID : SV_VertexID)
{
    float3 worldPos = VertexData[vertID].xyz;

    VS_OUTPUT output;
    output.Position = mul(float4(worldPos, 1.0), ViewProj);
    output.Color = float4(1, 0.5, 0, 1);

    // 防止 DXC 优化掉顶点输入和未使用的 uniform
    float junk = input.DummyPos.x + input.DummyNrm.x + input.DummyUV.x + Time;
    output.Color.g += junk * 0.0;

    return output;
}
```

**清单文件 (`storage.feb.json`)：**

```json
{
  "techniques": [
    {
      "name": "StorageTrail",
      "passes": [
        {
          "name": "P0",
          "vertexShader": {"source": "storage_vs.hlsl", "entry": "VSMain"},
          "pixelShader":  {"source": "storage_ps.hlsl", "entry": "PSMain"}
        }
      ]
    }
  ],
  "parameters": [
    {"name": "ViewProj", "type": "MATRIX", "register": 0,
     "default": [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]},
    {"name": "Time",     "type": "FLOAT",  "register": 4, "default": [0]}
  ]
}
```

**描述符集分配情况：**

| 着色器 | 资源 | SPIR-V 中的位置 |
|---|---|---|
| VS | 强制采样器 | Set 0, Binding 0 |
| VS | `VertexData` (StructuredBuffer) | **Set 0, Binding 1** |
| VS | `$Globals` (ViewProj, Time) | Set 1, Binding 0 |

**C# 端绑定：**

```csharp
// 创建存储缓冲区（vertexRead: true 是必需的）
var storageBuffer = new StorageBuffer(GraphicsDevice,
    sizeInBytes, vertexWrite: true, vertexRead: true);

// 上传数据
storageBuffer.SetData(0, data, 0, count);

// 在绘制前绑定 → 对应 Set 0 中的存储缓冲区槽位
GraphicsDevice.SetVertexStorageBuffers(0, storageBuffer);
```

**register 选择速查：**

| 场景 | 推荐寄存器 | 原因 |
|---|---|---|
| 第一个（唯一）存储缓冲区，无纹理 | `register(t1)` | `t0` 与强制采样器冲突 |
| 有 1 个纹理 + 1 个存储缓冲区 | `t0` 给纹理，`t1` 给存储缓冲区 | 纹理用低编号，存储缓冲区接在后面 |
| 读写存储缓冲区 | `register(u0)` | `feb_builder` 自动计算绑定偏移 |
| 多个存储缓冲区 | `t1`, `t2`, `t3` ... | 连续编号，从 1 开始 |

### 6.4 计算着色器

**计算着色器 (`particle_cs.hlsl`)：**

```hlsl
// 只读输入缓冲区
StructuredBuffer<float4> InputData  : register(t0);
// 读写输出缓冲区
RWStructuredBuffer<float4> OutputData : register(u0);
// Uniform 参数
float DeltaTime : register(c0);
float ParticleCount : register(c1);

[numthreads(64, 1, 1)]
void CSMain(uint3 dtid : SV_DispatchThreadID)
{
    uint idx = dtid.x;
    if (idx >= (uint)ParticleCount) return;

    float4 particle = InputData[idx];
    particle.y += DeltaTime * 0.5;
    OutputData[idx] = particle;
}
```

**清单文件 (`particle.feb.json`)：**

```json
{
  "techniques": [
    {
      "name": "ParticleUpdate",
      "passes": [
        {
          "name": "P0",
          "computeShader": {"source": "particle_cs.hlsl", "entry": "CSMain"}
        }
      ]
    }
  ],
  "parameters": [
    {"name": "DeltaTime",     "type": "FLOAT", "register": 0, "default": [0.016]},
    {"name": "ParticleCount", "type": "FLOAT", "register": 1, "default": [1024]}
  ]
}
```

**描述符集分配情况：**

| 着色器 | 资源 | SPIR-V 中的位置 |
|---|---|---|
| CS | `InputData` (t0, 只读) | Set 0, Binding 0 |
| CS | `OutputData` (u0, 读写) | Set 1, Binding 0 |
| CS | `$Globals` (DeltaTime, ParticleCount) | Set 2, Binding 0 |

### 6.5 多 Technique 效果

当一个 Effect 需要支持多种顶点布局时（如 `BasicEffect` 有 4 种技术），每种布局对应一个 Technique，每个 Technique 使用不同的顶点着色器入口。

**顶点着色器 (`effect_vs.hlsl`)：**

```hlsl
float4x4 WorldViewProj : register(c0);

// ── PNT 布局（Position + Normal + TexCoord）──
struct VS_INPUT_PNT
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float2 TexCoord : TEXCOORD0;
};

// ── PT 布局（Position + TexCoord）──
struct VS_INPUT_PT
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

VS_OUTPUT VSMain_PNT(VS_INPUT_PNT input)
{
    VS_OUTPUT output;
    output.Position = mul(input.Position, WorldViewProj);
    output.TexCoord = input.TexCoord;
    return output;
}

VS_OUTPUT VSMain_PT(VS_INPUT_PT input)
{
    VS_OUTPUT output;
    output.Position = mul(input.Position, WorldViewProj);
    output.TexCoord = input.TexCoord;
    return output;
}
```

**清单文件 (`effect.feb.json`)：**

```json
{
  "techniques": [
    {
      "name": "PNT",
      "passes": [{
        "name": "P0",
        "vertexShader": {"source": "effect_vs.hlsl", "entry": "VSMain_PNT"},
        "pixelShader":  {"source": "effect_ps.hlsl", "entry": "PSMain"}
      }]
    },
    {
      "name": "PT",
      "passes": [{
        "name": "P0",
        "vertexShader": {"source": "effect_vs.hlsl", "entry": "VSMain_PT"},
        "pixelShader":  {"source": "effect_ps.hlsl", "entry": "PSMain"}
      }]
    }
  ],
  "parameters": [
    {
      "name": "WorldViewProj",
      "type": "MATRIX",
      "register": 0,
      "default": [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]
    }
  ]
}
```

---

## 7. 手动编译参考（DXC 标志速查）

如果你需要在 `feb_builder.py` 之外手动使用 DXC 编译着色器（例如调试、或使用自定义构建系统），以下是可以直接复制的命令模板。

### DXC 标志格式

```bash
# 全局常量缓冲区（$Globals）
-fvk-bind-globals <binding> <set>

# 单个寄存器绑定
-fvk-bind-register <类型-编号> <space> <binding> <set>
```

### 顶点着色器

```bash
# 无纹理（仅有 uniform）
dxc -spirv -T vs_6_0 -E VSMain shader_vs.hlsl -Fo shader_vs.spv \
    -fvk-bind-globals 0 1

# 使用 StructuredBuffer（register t1, 避开强制采样器）
dxc -spirv -T vs_6_0 -E VSMain shader_vs.hlsl -Fo shader_vs.spv \
    -fvk-bind-globals 0 1 \
    -fvk-bind-register t1 0 1 0

# 使用 RWStructuredBuffer（register u0）
dxc -spirv -T vs_6_0 -E VSMain shader_vs.hlsl -Fo shader_vs.spv \
    -fvk-bind-globals 0 1 \
    -fvk-bind-register u0 0 1 0
```

### 像素着色器

```bash
# 无纹理
dxc -spirv -T ps_6_0 -E PSMain shader_ps.hlsl -Fo shader_ps.spv \
    -fvk-bind-globals 0 3

# 1 个纹理 + 1 个采样器
dxc -spirv -T ps_6_0 -E PSMain shader_ps.hlsl -Fo shader_ps.spv \
    -fvk-bind-globals 0 3 \
    -fvk-bind-register t0 0 0 2 \
    -fvk-bind-register s0 0 0 2

# N 个纹理 + N 个采样器
dxc -spirv -T ps_6_0 -E PSMain shader_ps.hlsl -Fo shader_ps.spv \
    -fvk-bind-globals 0 3 \
    -fvk-bind-register t0 0 0 2 \
    -fvk-bind-register s0 0 0 2 \
    -fvk-bind-register t1 0 1 2 \
    -fvk-bind-register s1 0 1 2
#   （以此类推...）
```

### 计算着色器

```bash
# 1 个只读缓冲区 + 1 个读写缓冲区 + uniform
dxc -spirv -T cs_6_0 -E CSMain shader_cs.hlsl -Fo shader_cs.spv \
    -fvk-bind-globals 0 2 \
    -fvk-bind-register t0 0 0 0 \
    -fvk-bind-register u0 0 0 1
```

### 完整的 Set/Binding 速查表

```
┌──────────────────────┬───────────────────┬─────────────────┬──────────────────┐
│ 资源类型              │ Vertex Shader     │ Pixel Shader    │ Compute Shader   │
├──────────────────────┼───────────────────┼─────────────────┼──────────────────┤
│ $Globals (register   │ Set 1, Binding 0  │ Set 3, Bind 0   │ Set 2, Binding 0 │
│   c0..cN)            │                   │                 │                  │
├──────────────────────┼───────────────────┼─────────────────┼──────────────────┤
│ Texture + Sampler    │ Set 0, Binding N  │ Set 2, Bind N   │ Set 0, Binding N │
│   (register tN, sN)  │                   │                 │                  │
├──────────────────────┼───────────────────┼─────────────────┼──────────────────┤
│ StructuredBuffer<T>  │ Set 0             │ Set 2           │ Set 0            │
│   (register tN)      │ bind ≥ 1*         │ bind ≥ 1*       │                  │
├──────────────────────┼───────────────────┼─────────────────┼──────────────────┤
│ RWStructuredBuffer   │ Set 0             │ Set 2           │ Set 1            │
│   (register uN)      │ auto-offset       │ auto-offset     │                  │
├──────────────────────┼───────────────────┼─────────────────┼──────────────────┤
│ RWTexture2D          │ Set 0             │ Set 2           │ Set 1            │
│   (register uN)      │ auto-offset       │ auto-offset     │                  │
└──────────────────────┴───────────────────┴─────────────────┴──────────────────┘
* bind ≥ 1: 因为 Binding 0 被强制采样器槽位占用
```

---

## 8. 故障排查

### 症状 → 原因 → 解决方法

| 症状 | 可能原因 | 检查方法 |
|---|---|---|
| **渲染全黑 / 位置完全错误** | `$Globals` 描述符集不匹配 | 检查着着色器是否有 `register(cN)` 变量，确认 `feb_builder.py` 生成了 `-fvk-bind-globals` 标志 |
| **纹理全黑** | 纹理寄存器未映射到正确的 Set | 确认 PS 中的 `register(tN)` 是否被 `feb_builder.py` 扫描到并生成了 `-fvk-bind-register` 标志 |
| **纹理颜色错位** | PS 参数声明顺序与 VS_OUTPUT 不一致 | 确认 PS 入口参数的声明顺序与 VS 输出结构体字段顺序完全一致 |
| **结构化缓冲区读到垃圾数据（全屏三角形、闪烁）** | 绑定编号与强制采样器冲突 | 使用 `register(t1)`（而非 `t0`）作为第一个存储缓冲区 |
| **结构化缓冲区读到全零** | 描述符集选错 / 未绑定位 | 确认 `SetVertexStorageBuffers` 的 `firstSlot` 与着色器中的绑定编号对齐 |
| **SDL_GPU 断言："Missing vertex storage buffer binding"** | SPIR-V 引用的描述符集与 SDL_GPU 管线布局不匹配 | 确认存储缓冲区使用正确 Set（VS=0, PS=2） |
| **修改着色器不生效** | 未重建 FEB | 重新运行 `python3 tools/feb_builder.py xxx.feb.json` |
| **修改参数不生效** | HLSL 寄存器编号与 `.feb.json` 不一致 | 对比 `grep register *.hlsl` 和清单文件中的 `register` 值 |
| **Vulkan 验证层："descriptor set N not bound"** | SDL_GPU 未绑定该描述符集 | 检查是否有资源声明但对应着色器阶段未使用（如 PS uniform 在 VS 中引用） |
| **DXC 编译错误："invalid register specification"** | 寄存器类型用错 | `StructuredBuffer` 用 `register(tN)`；`RWStructuredBuffer` 用 `register(uN)` |

### 调试工作流

当渲染结果异常时，推荐按以下顺序排查：

```
1. 确认着色器语法
   ├─ DXC 编译是否成功？（查看 feb_builder 输出）
   └─ 寄存器类型是否使用正确？

2. 确认 SPIR-V 描述符集
   ├─ 手动编译：dxc -spirv ... -Fo test.spv
   ├─ 反汇编：spirv-dis test.spv | grep -E "DescriptorSet|Binding"
   └─ 对照本文档第 7 节的速查表验证

3. 确认 CPU 端数据
   ├─ uniform 参数值是否正确？
   ├─ StorageBuffer.SetData 写入的数据是否正确？
   └─ 用 GetData 验证回读

4. 确认渲染管线状态
   ├─ BlendState, DepthStencilState 是否正确？
   ├─ 顶点缓冲与 Technique 是否匹配？
   └─ 纹理是否已加载并绑定？

5. 使用 GPU 调试工具
   └─ RenderDoc 截帧：检查 SPIR-V 反汇编、描述符集绑定状态、验证层错误
```

### 验证 SPIR-V 描述符集的快速脚本

```bash
#!/bin/bash
# verify_bindings.sh — 检查编译后 SPIR-V 的描述符集分配
SHADER=$1
ENTRY=$2
STAGE=${3:-vs}  # vs, ps, cs

case $STAGE in
  vs) PROFILE="vs_6_0"; GLOBALS_SET="1" ;;
  ps) PROFILE="ps_6_0"; GLOBALS_SET="3" ;;
  cs) PROFILE="cs_6_0"; GLOBALS_SET="2" ;;
esac

echo "=== $SHADER ($STAGE) ==="
dxc -spirv -T $PROFILE -E $ENTRY "$SHADER" -Fo /tmp/verify.spv \
    -fvk-bind-globals 0 $GLOBALS_SET 2>&1

echo "--- 描述符集分配 ---"
spirv-dis /tmp/verify.spv 2>/dev/null | grep -E "DescriptorSet|Binding" | sort

echo "--- 变量列表 ---"
spirv-dis /tmp/verify.spv 2>/dev/null | grep "OpVariable" | grep -v "Input\|Output\|BuiltIn"

echo ""
echo "预期："
echo "  $Globals → Set $GLOBALS_SET, Binding 0"
case $STAGE in
  vs) echo "  纹理/采样器/存储缓冲区 → Set 0" ;;
  ps) echo "  纹理/采样器/存储缓冲区 → Set 2" ;;
  cs) echo "  只读资源 → Set 0, 读写资源 → Set 1" ;;
esac
```

---

## 延伸阅读

- **SDL_GPU Uniform 绑定深度分析：** `../FNA3D_HLSL_Test/docs/sdl_gpu_uniform_binding.md` — 完整的调试过程复盘和描述符集发现历程
- **FNA3D_HLSL 着色器编写指南：** `../FNA3D_HLSL_Test/docs/hlsl_shader_authoring_guide.md` — Location 对齐和旧版清单字段说明
- **Stock Effect 开发指南：** `STOCK-EFFECT-DEVELOPMENT-GUIDE.md` — 多 Technique Effect 的 C# 端集成模式
- **HLSL Effect 顶点约定：** `REQ-effect-hlsl-vertex-convention.md` — C1-C5 顶点布局严格约定
- **FNA3D_HLSL 架构：** `../FNA/lib/FNA3D/CLAUDE.md` — FNA3D C 驱动的完整架构说明
- **DXC SPIR-V 文档：** [DXC Wiki: SPIR-V CodeGen](https://github.com/microsoft/DirectXShaderCompiler/blob/main/docs/SPIR-V.rst)
- **SDL_GPU API 参考：** [SDL3 GPU Category](https://wiki.libsdl.org/SDL3/CategoryGPU)
