# 需求文档：Effect 类与 HLSL 顶点属性严格一致约定（Strict Effect–HLSL Vertex Convention）

| | |
|---|---|
| 状态 | 待实施 |
| 日期 | 2026-07-17 |
| 实施方 | **FNA_Test（本项目，主导：测试与验证）**；FNA（HLSL / manifest / C# effect 类修改与 FEB 重建）；FNA3D_HLSL（唯一驱动修复，见第 4 节） |
| 关联问题 | 任务 #5：BasicEffect VertexPositionColor 路径顶点属性错位 |

---

## 0. 设计决策记录

曾评估两版替代方案并**否决**：

1. FEB 携带语义表（构建期 SPIR-V 反射 + 驱动按 (usage, usageIndex) 映射）——需修订 FEB 标准；
2. 运行时解析 SPIR-V 提取语义映射——引入运行时反射复杂度。

**最终决策**：Effect 类与着色器在顶点属性等各方面保持**严格一致的约定**，不引入任何声明对齐机制。作为 FNA 分支，当存在不一致时，**以原版 FNA/XNA 顶点类型为标准，修改 HLSL 的编写**。实际工程中 Effect 类与 HLSL 高度耦合，无需通用对齐规则。FEB 格式、feb_builder、驱动的 location 分配逻辑均**保持不变**。

---

## 1. 背景与问题

### 1.1 location 分配的两侧规则

- 驱动侧：`FNA3D_HLSL/src/FNA3D_Driver_SDL.c:1544-1557` 按应用顶点声明的**元素顺序**顺序分配 attribute location（0, 1, 2, …）。
- 着色器侧：DXC 按 HLSL VS_INPUT **字段声明顺序**分配 SPIR-V location。

两侧均为顺序分配 —— 只要「VS_INPUT 字段顺序 == 顶点声明元素顺序」即天然对齐。本约定将其确立为强制规约，取代任何映射机制。

### 1.2 已确认缺陷

1. **BasicEffect + VertexPositionColor 错位**：`BasicEffect_vs.hlsl:30-33` 的输入为超集 (POSITION, NORMAL, TEXCOORD, COLOR)。应用使用 `VertexPositionColor`（P, C）时，Color 数据绑到 location 1 被当作 Normal 读取，真正的 Color 输入（location 3）悬空。复现：`FNA_Test/BasicEffect` demo 按 V 键。
2. **读取未绑定属性（Vulkan UB）**：stock 着色器统一声明超集输入 + `ShaderIndex` uniform 动态分支（如 `BasicEffect_vs.hlsl:85-87`），顶点声明未提供的属性被着色器消费，Vulkan 规范下未定义行为，validation layer 下报错。
3. 同类潜在错位：AlphaTestEffect × VPCT、DualTextureEffect × 带顶点色布局等。

### 1.3 根源

XNA 原版 BasicEffect 以 **32 个着色器排列**应对不同输入布局与功能组合；本 fork 将其折叠为「单一超集 VS + uniform 分支」，丢失了「每种输入布局一个入口签名」这一必要维度。本需求恢复该维度（以 technique 为单位），uniform 分支仅保留给不影响输入签名的开关。

---

## 2. 约定（规约正文）

> 实施完成后，本节内容应同步摘录到 `FNA_Test/CLAUDE.md` 的 Constraints 节（替换现有单条 DXC location 说明），作为后续所有 HLSL/Effect 开发的强制规约。

- **C1（顺序一致）**：每个顶点着色器入口的 VS_INPUT 字段顺序**必须**等于其目标 FNA 顶点声明的元素顺序。两侧顺序分配天然对齐，驱动不做任何映射。
- **C2（精确声明）**：VS_INPUT **必须且只能**声明目标布局实际提供的属性——禁止超集声明。着色器不得消费声明未提供的属性（消除 UB；「validation 零 error」由此成为可验收项）。
- **C3（technique = 输入签名）**：影响输入签名的 Effect 标志（`VertexColorEnabled` / `TextureEnabled` / `LightingEnabled`）→ 每种布局一个 **technique**，由 Effect 类在 `OnApply` 中切换 `CurrentTechnique`；不影响签名的开关（`FogEnabled`、光照模式细分）保留 `ShaderIndex` uniform 分支。
- **C4（布局标准）**：以原版 FNA stock `IVertexType` 的元素顺序为标准（`FNA/src/Graphics/Vertices/`）：

  | 顶点类型 | 元素顺序 | 缩写 |
  |---|---|---|
  | VertexPositionColor | Position, Color | PC |
  | VertexPositionColorTexture | Position, Color, TextureCoordinate | PCT |
  | VertexPositionNormalTexture | Position, Normal, TextureCoordinate | PNT |
  | VertexPositionTexture | Position, TextureCoordinate | PT |

  无 stock 类型的布局由本文档规定标准顺序：Skinned = (Position, Normal, TextureCoordinate, BlendIndices, BlendWeight)；DualTexture = (Position, TexCoord0, TexCoord1)，带顶点色时 Color 紧随 Position（P, C, T0, T1），与 VPCT 的 Color 位置一致。
- **C5（数值类别一致）**：VS_INPUT 字段的数值类别必须与顶点格式一致（float↔Vector*/Color(UNORM)，uint↔Byte4）。`Color` 格式为 BGRA 字节序（XNA 约定），着色器中收到的是归一化 RGBA float4，无需额外处理。

### 机制可行性（已验证）

- `CurrentTechnique` setter 立即调用 `FNA3D_SetEffectTechnique`（`FNA/src/Graphics/Effect/Effect.cs:23-39`）；
- `EffectPass.Apply()` 的 technique 一致性 guard 在 `OnApply()` **之前**执行（`EffectPass.cs:62-72`），因此 OnApply 内切换 technique 合法；标准调用模式 `effect.CurrentTechnique.Passes[0].Apply()` 每帧重取 CurrentTechnique，后续帧 guard 自洽；
- 本方案所有 technique 均为单 pass，technique 相对 pass 索引恒 0。

---

## 3. 各 Effect 的 technique 矩阵与 HLSL 修改

实施位置：`FNA/src/Graphics/Effect/StockEffects/HLSL_DXC/`。同一 effect 的所有 VS 变体写在**同一 HLSL 文件的多个 entry point** 中（共享 cbuffer/寄存器布局与 VS_OUTPUT 结构，PS 不变、共用）。feb_builder 的 manifest 已支持 per-pass `entry` 字段与多 technique（`techniques[]` 数组 → FEB 的 passStart/passCount），**FEB 格式与 feb_builder 零改动**。

### 3.1 BasicEffect（4 techniques）

| technique 名 | VS 入口 | VS_INPUT | OnApply 选择条件 |
|---|---|---|---|
| `PNT` | `VSMain_PNT` | P(float4), N(float3), T(float2) | !vc && lit（纹理开关经 uniform） |
| `PT` | `VSMain_PT` | P(float4), T(float2) | !vc && !lit（纹理开关经 uniform） |
| `PC` | `VSMain_PC` | P(float4), C(float4) | vc && !tex |
| `PCT` | `VSMain_PCT` | P(float4), C(float4), T(float2) | vc && tex |

- `ShaderIndex` 公式（`BasicEffect.cs:459-515`）：**vertexColor 位（+2）废弃置 0**，HLSL 删除对应分支；fog 位（+1）、texture 位（+4）、光照位（+8/+16/+24）保留，各入口忽略与自身无关的位。
- 边界组合的规定行为：
  - **vc && lit**：4 种 stock 布局无 (P, N, C) 组合，**暂不支持**——OnApply 按 vc 路径选 PC/PCT 并将光照位清零（按无光照渲染），文档化；后续有需求时新增 PNC/PNCT 入口。
  - **!vc && !lit && !tex**：选 PT（布局仍须含 TexCoord，属性未消费…注意 C2——PT 入口声明了 T，布局必须提供 T）。位置-only 布局暂不支持（XNA 亦无 stock P-only 类型）。

### 3.2 AlphaTestEffect（2 techniques）

| technique | VS_INPUT | 条件 |
|---|---|---|
| `PT` | P, T | !vertexColorEnabled |
| `PCT` | P, C, T | vertexColorEnabled |

ShaderIndex（`AlphaTestEffect.cs:419-434`）：vc 位（+2）废弃置 0；!fog（+1）、isEqNe（+4）保留。

### 3.3 DualTextureEffect（2 techniques）

| technique | VS_INPUT | 条件 |
|---|---|---|
| `PTT` | P, T0, T1 | !vertexColorEnabled |
| `PCTT` | P, C, T0, T1 | vertexColorEnabled |

ShaderIndex（`DualTextureEffect.cs:305-317`）：vc 位（+2）废弃置 0；!fog（+1）保留。

### 3.4 SkinnedEffect（单 technique，两处修正）

- **删除 spurious 的 `Color` 输入**（XNA SkinnedEffect 无 VertexColorEnabled，该输入纯属超集残留，违反 C2）；HLSL 中 vertexColorEnabled 分支一并删除（其 ShaderIndex 公式本无 vc 位，`SkinnedEffect.cs:512-531` 不变）。
- **BlendIndices 按 XNA 标准 Byte4**：HLSL 声明改 `uint4 BlendIndices : BLENDINDICES0`（骨骼索引使用处显式转换）。输入签名固定 (P, N, T, BI, BW)。

### 3.5 EnvironmentMapEffect / SpriteEffect（无改动）

- EnvironmentMapEffect：VS_INPUT (P, N, T) 已与 VPNT 精确匹配，无超集输入。
- SpriteEffect：已按本约定修复（P, C, T 匹配 SpriteBatch 顶点布局）。

### 3.6 manifest 结构（示例：BasicEffect.feb.json）

```json
{
  "techniques": [
    { "name": "PNT", "passes": [ { "name": "P0",
        "vertexShader": {"source": "BasicEffect_vs.hlsl", "entry": "VSMain_PNT"},
        "pixelShader":  {"source": "BasicEffect_ps.hlsl", "entry": "PSMain"} } ] },
    { "name": "PT",  "passes": [ { "...同上, entry: VSMain_PT" : "" } ] },
    { "name": "PC",  "passes": [ ] },
    { "name": "PCT", "passes": [ ] }
  ],
  "parameters": [ "...现有参数不变..." ]
}
```

FEB 重建统一调用 `../FNA3D_HLSL_Test/tools/feb_builder.py`（工具单一真源，禁止复制副本）。

---

## 4. FNA3D_HLSL 驱动唯一修改：pass 索引 technique 相对化

**多 technique 的前置缺陷（调研确认，且已对照重写后的 Effect.cs 复核）**：C# 侧传入的是 technique 相对 pass 索引（`Effect.cs:295` `INTERNAL_applyEffect` 直传 `EffectPass` 构造时的技术内序号，见 `INTERNAL_parseEffect` 中 `Effect.cs:450/466`），而 `SDLGPU_ApplyEffect`（`FNA3D_Driver_SDL.c:4007` 附近）虽读取了 `effect->currentTechnique`，取着色器时却按**全局** pass 索引：`gpuEffect->vertexShaders[pass]`。单 technique 时二者恰好相等；多 technique 即错位。

修改需求：

- 以当前 technique 解析全局 pass 索引：`globalPass = (technique->passes - effectData->passes) + pass`（`FNA3D_EffectTechnique.passes` 指向全局 pass 数组内部，见 `FNA3D_Effect.h` 结构定义与 `FNA3D_Effect.c:233-249` 解析），边界检查改为 `pass < technique->passCount`；
- `SDLGPU_BeginPassRestore`（`FNA3D_Driver_SDL.c:4074-4098`，现硬编码 `vertexShaders[0]`）同步改为当前 technique 的 pass 0 对应的全局索引；
- FEB 格式、`FNA3D_Effect.c` 解析、`GenerateVertexInputInfo`、PipelineCache **零改动**。管线缓存已以 vertexShader 指针入键（`FNA3D_PipelineCache.c:172-242`），technique 切换自然产生不同管线，无别名风险；
- 该修复由 FNA_Test 的 C# 多 technique 测试端到端验证（6.2 B7）。

---

## 5. FNA C# 修改

### 5.1 stock effect 类（`FNA/src/Graphics/Effect/StockEffects/`）

- 构造/`CacheEffectParameters` 时按名缓存 technique 引用（如 `techniquePNT = Techniques["PNT"]`）；
- `OnApply()` 按第 3 节条件表计算目标 technique，**仅当与 CurrentTechnique 不同时**赋值（setter 会立即调 `FNA3D_SetEffectTechnique`）；
- ShaderIndex 公式按第 3 节调整（vc 位废弃置 0）；
- 涉及：`BasicEffect.cs`、`AlphaTestEffect.cs`、`DualTextureEffect.cs`（SkinnedEffect/EnvironmentMapEffect/SpriteEffect 无 technique 切换）；
- **与现有 textureBindings 机制正交**：`INTERNAL_applyEffect` 中 texture 类参数按 FEB register 自动绑定到 `GraphicsDevice.Textures[slot]` 的机制（`Effect.cs:311-319`，null 纹理跳过）保持不变。technique 切换发生在 OnApply（更早），纹理绑定时序不受影响；纹理开关（TextureEnabled）按 C3 仍走 ShaderIndex uniform 位，与该机制的「null 纹理 = 采样路径由 uniform 禁用」注释语义一致。

### 5.2 FNA_Test 顶点类型对齐

- `Common/GeometryGen.cs` 的 `SkinnedVertex`：BlendIndices 由 `VertexElementFormat.Vector4` 改为 **`Byte4`**，顶点数据同步改 byte 索引（当前仅用骨骼 0/3，直接编码即可）。

---

## 6. 严格测试需求（实施于 FNA_Test，本项目，核心交付）

测试为 C# 程序，沿用本仓库现有 per-effect demo 项目模式。像素回读走 XNA 标准 API `GraphicsDevice.GetBackBufferData<Color>()`（`FNA/src/Graphics/GraphicsDevice.cs:846`，底层 `FNA3D_ReadBackbuffer`，驱动已实现于 `FNA3D_Driver_SDL.c:3736`）。

### 6.1 测试 harness（新增 `Common/TestHarness.cs`）

```csharp
public static class TestHarness
{
    public static bool Headless;                     // --headless 参数或 FNA_TEST_HEADLESS=1
    public static void ParseArgs(string[] args);
    public static bool FrameLimit(Game game, int frames = 3);  // 渲染 3 帧后触发断言+Exit（跨越首帧惰性建管线）
    public static Color[] ReadBackbuffer(GraphicsDevice dev);
    public static int AssertPixel(Color[] px, int w, int x, int y,
        Color expected, int tol /*=3*/, string label);          // 失败打印 got/want 与坐标
    public static int AssertCoverage(Color[] px, Color clearColor,
        float minRatio, string label);                          // 非背景像素占比
    public static int Report(string testName, int failures);   // "RESULT: PASS|FAIL" → Environment.ExitCode
}
```

- headless 模式：第 3 帧 Draw 完成后回读、断言、退出（退出码 = 是否有失败）；非 headless 保持现有交互窗口（视觉检查能力不丢失）；
- 断言采样点取图元内部（远离边缘），容差默认 ±3（UNORM 舍入/插值）；期望值由与着色器一致的 CPU 镜像公式计算。

### 6.2 BasicEffect 矩阵测试（新建 `BasicEffectMatrix/` 项目，**首要验收项**）

每个子例：满屏或大面积 quad + 指定布局 + 指定 effect 状态 → 内部采样点断言。

| # | 子例 | 布局 × 状态 | 判定 |
|---|---|---|---|
| B1 | PNT 光照 | VPNT × lit | 朝光面亮、背光面按环境光，两采样点 |
| B2 | PNT 纹理 | VPNT × lit+tex（棋盘格） | 棋盘两色格采样正确 |
| B3 | PT 无光纹理 | VPT × !lit+tex | 棋盘采样正确 |
| B4 | PT 纯色 | VPT × !lit+!tex | == DiffuseColor |
| B5 | **PC 顶点色（原 bug 场景）** | VPC × vc | 顶点色正确；**BGRA 落位**：顶点写 R≠G≠B 的颜色，回读通道逐一比对 |
| B6 | PCT 顶点色+纹理 | VPCT × vc+tex | 棋盘 × 顶点色调制正确 |
| B7 | **technique 交替切换** | 同帧序列内 PC↔PNT 交替绘制多帧 | 两布局各自正确 —— 端到端覆盖第 4 节驱动 pass 索引修复与管线缓存 |
| B8 | fog 位仍生效 | 任一 technique × fog on | 雾色混合符合 CPU 镜像公式 |
| B9 | vc&&lit 降级 | VPC × vc+lit | 按无光照渲染（3.1 规定行为），不崩溃、validation 零 error |

### 6.3 其他 effect 测试

- **AlphaTestEffect**：现有 demo 增加 PCT（顶点色）子例：保留/丢弃区 + 顶点色调制断言；
- **DualTextureEffect**：增加 PCTT 子例：双纹理调制 × 顶点色断言；
- **SkinnedEffect**：`SkinnedVertex` 改 Byte4 后，弯曲圆柱顶部/底部采样点断言（受骨骼 1/骨骼 0 支配区）；
- **EnvironmentMapEffect**：覆盖率冒烟断言。

### 6.4 既有 6 个 demo 回归

全部接入 harness `--headless`：SpriteEffect / AlphaTest / DualTexture / Basic 用精确像素断言；EnvMap / Skinned 用覆盖率冒烟断言。非 headless 行为不变。

### 6.5 负路径演示用例（非阻断）

约定违背场景的**行为文档化**：如 VPC 布局 × 强制 PNT technique —— 预期渲染错误但进程不崩溃、validation 可复现报错。标记为 EXPECTED-MISMATCH，不计入通过判定；用途是给约定 C1/C2 提供直观反例。

### 6.6 一键脚本 `run_tests.sh`（FNA_Test 根目录）

1. 重建全部 stock FEB（`python3 ../FNA3D_HLSL_Test/tools/feb_builder.py <manifest>` 逐个）；
2. `dotnet build` FNA 与各测试项目；
3. 逐项目 `dotnet run --no-build -- --headless`，收集退出码；
4. 汇总打印，任一失败非零退出。CI/无显示环境使用 lavapipe ICD（脚本注释说明）。

### 6.7 通过标准

- `run_tests.sh` 全绿（所有测试退出码 0）；
- 全部 demo 在 `VK_LAYER_KHRONOS_validation` 下**零 error**（约定 C2 的可验收体现；当前超集输入方案做不到这一点）。

---

## 7. 实施顺序

1. **FNA3D_HLSL**：ApplyEffect / BeginPassRestore pass 索引修复（第 4 节）。单 technique FEB 下 `passStart 恒 0`，行为不变，可先行合入；
2. **FNA_Test**：TestHarness + 既有 demo 接入断言 —— 在旧着色器行为上先建立基线（此时 B5 类用例预期红灯，作为 bug 的自动化复现）；
3. **FNA**：HLSL 拆分入口 + manifest 多 technique + C# OnApply/ShaderIndex 调整 + `SkinnedVertex` 对齐 + FEB 重建；
4. **FNA_Test**：BasicEffectMatrix 及全部测试转绿；validation 零 error；更新 `CLAUDE.md` Constraints（第 2 节规约摘录）。

---

## 8. 风险与说明

| 风险/事项 | 说明与缓解 |
|---|---|
| OnApply 内切换 technique 的时序 | guard 先于 OnApply 执行（已验证）；用户代码若缓存 `EffectPass` 数组跨帧复用，guard 会在状态切换后抛异常——属 XNA 原有语义，文档注明 |
| vc && lit 组合 | 3.1 规定降级行为；后续按需新增 PNC/PNCT 入口即可，不破坏本约定 |
| ShaderIndex 位废弃 | 保留位布局仅置 0，HLSL 删除读取——避免移位造成两侧公式漂移 |
| feb_builder 相对路径依赖 | `run_tests.sh` 与 FNA 构建文档需注明依赖 `../FNA3D_HLSL_Test/tools/feb_builder.py`（单一真源） |
| GetBackBufferData 依赖驱动 ReadBackbuffer | 已确认实现（`FNA3D_Driver_SDL.c:3736`）；若个别格式路径有缺口，在阶段 2 建基线时即暴露 |

---

## 9. 验收标准清单

- [ ] FNA3D_HLSL：pass 索引 technique 相对化合入，现有单 technique demo 行为不变
- [ ] `Common/TestHarness.cs` 落地，既有 6 个 demo 接入 `--headless` 断言并建立基线
- [ ] BasicEffect：4 technique HLSL 入口 + manifest + OnApply 切换完成，FEB 重建
- [ ] AlphaTest / DualTexture：顶点色 technique 变体完成
- [ ] SkinnedEffect：Color 输入删除、BlendIndices uint4/Byte4 对齐（含 FNA_Test `SkinnedVertex`）
- [ ] BasicEffectMatrix B1–B9 全部 PASS（B5 = 原错位 bug 的回归项）
- [ ] 6.3/6.4 全部测试 PASS；`run_tests.sh` 一键全绿
- [ ] 全 demo 在 `VK_LAYER_KHRONOS_validation` 下零 error
- [ ] 约定 C1–C5 摘录进 `FNA_Test/CLAUDE.md` Constraints 节
