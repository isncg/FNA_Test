# FNA 游戏内 GUI 系统设计与实现方案

> 目标：为运行在 **FNA + FNA3D_HLSL（Vulkan-only）** 之上的游戏，设计并实现一套
> **保留模式（Retained Mode）游戏内 GUI 系统**——覆盖主菜单、HUD、设置面板、
> 对话框等玩家可见界面，强调**皮肤化、布局、动画、本地化、手柄导航**。
> 渲染路径仅依赖 FNA 现有的 `SpriteBatch`，**不引入任何第三方 UI 框架**，
> 文本渲染采用 **SDF（有向距离场）** 方案（基于本仓库已验证的 SDFFontTest：msdf-atlas-gen 图集 + 自定义 SDF Effect），分辨率无关、可描边/加粗。
> 现有 Dear ImGui（`FNA3D_ImGui_*`）保留用于开发者/调试工具，与本系统分层共存。

本文档面向实现者，给出**决策完整**的方案：先横向对比成熟 GUI 方案并给出选型结论，
再给出可分阶段推进的架构与实施计划。文中相对路径以本仓库根 `FNA_Test/` 为基准，
文档内链接以 `docs/` 目录为相对基准（如 [`../Common/TestHarness.cs`](../Common/TestHarness.cs)）。

---

## 目录

1. [背景与目标](#1-背景与目标)
2. [成熟 GUI 方案对比与选型](#2-成熟-gui-方案对比与选型)
3. [总体架构](#3-总体架构)
4. [关键子系统设计](#4-关键子系统设计)
5. [与 FNA / FNA3D_HLSL 的集成约束](#5-与-fna--fna3d_hlsl-的集成约束)
6. [分阶段实施计划](#6-分阶段实施计划)
7. [测试与验收计划](#7-测试与验收计划)
8. [工程组织与目录结构](#8-工程组织与目录结构)
9. [Web UI 设计器（开发者工具，可选）](#9-web-ui-设计器开发者工具可选)
10. [风险与缓解](#10-风险与缓解)
11. [里程碑与工作量估算](#11-里程碑与工作量估算)
12. [实现顺序速查（Checklist）](#附实现顺序速查checklist)

---

## 1. 背景与目标

### 1.1 现状

- 渲染栈：FNA（XNA 4.0 重实现）→ FNA3D_HLSL → SDL_GPU → Vulkan。
- 已具备的绘制能力：`SpriteBatch`（内部使用 `SpriteEffect`）、`Texture2D`、
  `GraphicsDevice.Clear`、混合状态、`ScissorRectangle` 裁剪。参见
  [`../StockEffect/SpriteEffect/Program.cs`](../StockEffect/SpriteEffect/Program.cs)。
- 已有的 UI 能力仅限 **Dear ImGui**（通过 [`../Common/ImGuiBindings.cs`](../Common/ImGuiBindings.cs)
  的 P/Invoke 绑定 `FNA3D_ImGui_*`）。ImGui 适合调试面板，但**不适合玩家可见的、需皮肤化的游戏界面**。
- 无内容管线（Content Pipeline）：不使用 `.xnb`/`SpriteFont`。字体需在运行时从 TTF 光栅化。
- 测试基建：[`../Common/TestHarness.cs`](../Common/TestHarness.cs) 提供 headless 帧驱动与
  像素级断言（`ReadBackbuffer` / `AssertPixel` / `AssertCoverage` / `Report`），本系统的自动化测试将复用它。

### 1.2 目标（本方案要交付的能力）

- 保留模式控件树：面板、按钮、文本、图片、复选/单选、滑条、输入框、列表、滚动区、进度条、对话框等。
- 布局系统：Stack / Grid / Dock / 绝对定位 / 锚点 + 内外边距 + 测量-排布两遍式。
- 皮肤系统：主题（颜色/字体/度量）+ 9-slice 纹理皮肤 + 控件状态（normal/hover/pressed/disabled/focused）。
- 输入系统：鼠标/键盘/触摸/**手柄**统一路由，命中测试，事件冒泡/捕获，焦点与方向导航。
- 文本系统：**SDF 文本渲染**（图集 + 距离场着色器）、字体图集、UTF-8/CJK、富文本（颜色/换行/截断）、描边/加粗、**本地化**接管。
- 动画：属性补间（Tween）与控件状态过渡。
- 序列化（可选）：从 C# 代码构建界面为主，支持从 JSON 资源加载布局与皮肤。
- 与现有 ImGui 调试层**共存不冲突**。

### 1.3 硬性约束（成功标准）

- **C1｜受控的渲染路径**：图片/纯色/9-slice 走 `SpriteBatch`（纹理四边形 + 颜色调制 + 变换矩阵 + 裁剪）；
  **文本走已验证的 SDF Effect**（`SDFText.feb`，自定义顶点/索引批处理，见 SDFFontTest）。二者均为本后端已跑通的路径；
  SDF 所需的 uniform 更新（`MatrixTransform`/`Smoothing`/`OutlineColor` 等）已在 SDFFontTest 中验证可用。
- **C2｜零第三方 UI 框架依赖**：不集成 Myra/Gum/Nez UI 等库；**文本自研 SDF 方案**（运行时零第三方库；字体图集由离线工具 msdf-atlas-gen 预生成），以 `IFontProvider` 接口隔离，可替换为其它字体后端。
- **C3｜headless 可测**：GUI 逻辑（布局/命中/事件）在 `TestHarness.Headless` 下可运行并断言；渲染部分在有窗口时用像素断言验证。
- **C4｜与 ImGui 分层共存**：GUI 系统与 Dear ImGui 调试面板可同帧渲染，输入不互相吞噬（提供输入捕获协商）。
- **C5｜低 GC 压力**：稳态每帧不产生可观测的托管分配（布局缓存、绘制指令池化、字符串缓存）。

---

## 2. 成熟 GUI 方案对比与选型

### 2.1 两种基本架构范式

| 范式 | 代表 | 状态存放 | 优点 | 缺点 | 适配场景 |
|---|---|---|---|---|---|
| **即时模式 IMGUI** | Dear ImGui | 无（每帧由调用代码重建） | 代码即界面、无状态同步、迭代极快 | 皮肤化/动画/复杂布局弱、每帧重算、不利美术接管 | 开发者工具、调试面板 |
| **保留模式 RMGUI** | WPF、Myra、Gum、Nez UI | 控件对象树（持久） | 皮肤化强、布局/动画/数据绑定成熟、事件模型清晰 | 需状态同步、实现更重 | **玩家可见的游戏 UI** |

> 本项目目标为**游戏内玩家 UI**，选择**保留模式**范式；ImGui 继续承担开发者工具角色。

### 2.2 候选方案概览

| 方案 | 类型 | 语言/依赖 | 皮肤化 | 可视化编辑器 | 许可 | 在 FNA3D_HLSL(Vulkan) 上的适配风险 |
|---|---|---|---|---|---|---|
| **Dear ImGui**（现有） | 即时 | 原生 C++（已绑定） | 弱（主题色） | 无 | MIT | 已跑通，但不适合游戏 UI |
| **Myra** | 保留 | 纯 C#（MonoGame/FNA） | 强（MML/皮肤） | 有（MyraPad） | MIT | 依赖 SpriteBatch + 字体，理论可用；需验证其字体/渲染路径与本后端契合 |
| **Gum** | 保留 + 运行时 | 纯 C# + 独立编辑器 | 强 | 有（Gum 工具） | MIT | 运行时可用，但引入编辑器工作流与其布局模型 |
| **Nez UI**（scene2d.ui 移植） | 保留 | 纯 C#（依赖 Nez 框架） | 中（skin json） | 无 | MIT | 需引入 Nez 框架整体，耦合大 |
| **GeonBit.UI** | 保留 | 纯 C# | 中（图集皮肤） | 无 | MIT | 可用，但维护活跃度与定制自由度一般 |
| **Noesis GUI** | 保留（XAML） | 原生 + C# 绑定 | 极强（XAML/CSS） | 有（Blend） | 商业收费 | 需原生集成与自定义渲染后端，成本高 |
| **RmlUi** | 保留（HTML/CSS） | 原生 C++ | 极强（CSS） | 浏览器即所见 | MIT | 需为 Vulkan 写渲染后端 + C# 绑定，重 |
| **EmptyKeys** | 保留（XAML/MVVM） | 纯 C# | 中 | 部分 | MIT | 维护较少，较陈旧 |
| **自研（本方案）** | 保留 | 纯 C# + SpriteBatch + SDF 文本 | 完全自定义 | 无（可后续做） | 自有 | **无适配风险**：只用已跑通的 SpriteBatch + SDF Effect 路径 |

### 2.3 评估维度与决策矩阵

评分：★=弱 / ★★=中 / ★★★=强（针对**本项目约束**下的表现，而非库的绝对能力）。

| 维度 | Dear ImGui | Myra | Gum | 自研（本方案） |
|---|---|---|---|---|
| 游戏 UI 皮肤化 | ★ | ★★★ | ★★★ | ★★★（自定义） |
| 布局能力 | ★★ | ★★★ | ★★★ | ★★★ |
| 文本/本地化/富文本 | ★★ | ★★ | ★★ | ★★★（自研 SDF：分辨率无关、可描边/加粗） |
| 手柄/焦点导航 | ★ | ★★ | ★★ | ★★★（按需定制） |
| 数据绑定/MVVM | ★ | ★★ | ★★ | ★★（轻量自研） |
| **FNA3D_HLSL(Vulkan) 兼容确定性** | ★★★（已跑通） | ★★（需验证） | ★★（需验证） | **★★★（仅用 SpriteBatch）** |
| **零第三方 UI 依赖（约束 C2）** | — | ✗ | ✗ | **✓** |
| 定制自由度 | ★ | ★★ | ★★ | ★★★ |
| 与现有 ImGui/测试基建协同 | ★★★ | ★★ | ★★ | ★★★（复用 TestHarness） |
| 初期开发成本 | 低 | 低 | 低 | **高** |
| 长期维护可控性 | 中 | 中（外部演进） | 中 | **高（自有代码）** |

### 2.4 选型结论与理由

**结论：自研保留模式 GUI，建立在 `SpriteBatch`（图片/皮肤）+ SDF 文本渲染之上；Dear ImGui 保留为开发者工具层。**

理由：

1. **约束驱动**：约束 C1/C2 排除了所有第三方 UI 框架方案；FNA3D_HLSL 仅有 Vulkan 后端且自定义 uniform 更新未完备，
   而 `SpriteBatch`/`SpriteEffect` 已在测试中跑通——**仅走 SpriteBatch 是当前确定性最高的渲染路径**。
2. **可控性与契合度**：游戏 UI 需要与项目美术/本地化/手柄导航深度契合，自研可精确定制，避免外部库的布局/字体假设与本后端冲突。
3. **成本可接受**：保留模式的核心（控件树 + 两遍布局 + 事件路由 + 皮肤 + SpriteBatch 绘制）是成熟且有限的工程量，可分阶段交付。
4. **风险最低**：不承担「外部库能否在 Vulkan-only 后端跑通」的不确定性；文本采用本仓库 **SDFFontTest 已验证的 SDF 方案**（自定义 Effect + 图集），并以 `IFontProvider` 接口隔离，可替换其它字体后端。
5. **序列化格式**：界面资源采用与 WPF/XAML **相似但精简的 XML**（XAML-lite，见 §4.8），仅用 BCL `System.Xml`、零新增依赖；并为 Web UI 设计器（§9）提供数据基础。

> 备选保留意见：若后期项目优先级转为「尽快出界面、可接受外部依赖」，Myra 是最省力的替代（纯 C#、MIT、有可视化编辑器）；
> 届时可用本方案的 `IGuiRenderer` 抽象层做隔离评估，成本可控。

---

## 3. 总体架构

### 3.1 分层结构

```
┌─────────────────────────────────────────────────────────┐
│  应用层：游戏界面（Screen/View）+ 视图模型（可选 MVVM-lite）  │
├─────────────────────────────────────────────────────────┤
│  控件层：Widget 树（Panel/Button/Label/Slider/...）         │
├───────────────┬───────────────┬──────────────┬───────────┤
│  布局子系统     │  输入子系统      │  样式/皮肤      │  动画子系统  │
│  (Layout)     │  (Input)       │  (Theme/Skin)│  (Tween)  │
├───────────────┴───────────────┴──────────────┴───────────┤
│  文本子系统（SDF 距离场：图集/度量/富文本/描边/本地化）        │
├─────────────────────────────────────────────────────────┤
│  渲染抽象层：IGuiRenderer（绘制指令 → 批处理 → 裁剪 → 排序）    │
├─────────────────────────────────────────────────────────┤
│  渲染实现层：SpriteBatchGuiRenderer（唯一实现，走 SpriteBatch）│
├─────────────────────────────────────────────────────────┤
│  FNA / FNA3D_HLSL（GraphicsDevice / SpriteBatch / Texture2D）│
└─────────────────────────────────────────────────────────┘
   旁路：Dear ImGui（开发者工具，输入捕获协商，见 §5.4）
```

### 3.2 模块分解

| 模块 | 职责 | 关键类型 |
|---|---|---|
| `Gui.Core` | 系统入口、帧循环编排、根容器、屏幕栈 | `GuiSystem`、`GuiScreen`、`ScreenStack` |
| `Gui.Widgets` | 控件基类、可视元素抽象与内置控件 | `Widget`、`Graphic`（`Image`/`Text` 共同基类，§4.5.1）、`Panel`、`Button`、`Label`、`Slider`、`CheckBox`、`TextBox`、`ScrollView`、`ListView` |
| `Gui.Layout` | 测量-排布、布局容器 | `ILayout`、`StackLayout`、`GridLayout`、`DockLayout`、`Thickness`、`Alignment` |
| `Gui.Input` | 输入设备聚合、命中、事件路由、焦点/导航 | `InputRouter`、`GuiEvent`、`FocusManager`、`NavigationMap` |
| `Gui.Styling` | 主题、样式表、皮肤图集、9-slice | `Theme`、`Style`、`Skin`、`NineSlice`、`WidgetState` |
| `Gui.Text` | SDF 字体、图集、度量、富文本、描边、本地化 | `IFontProvider`、`SdfFont`、`SdfTextBatch`、`TextLayout`、`ILocalization` |
| `Gui.Render` | 渲染抽象与实现 | `IGuiRenderer`、`SpriteBatchGuiRenderer`、`GeometryBuffer`、`GraphicQuad`、`DrawCommand`、`Clip` |
| `Gui.Animation` | 补间与状态过渡 | `Tween`、`Easing`、`Transition` |
| `Gui.Binding`（可选） | 事件/命令/数据绑定（§4.9） | `Bindable<T>`、`IGuiCommand`、`BindingEngine`、`HandlerTable` |
| `Gui.Serialization`（可选） | XAML-lite XML 布局/皮肤加载、类型注册表、dev 热重载端点 | `XamlLiteLoader`、`TypeRegistry`、`SkinLoader`、`HotReloadServer` |

---

## 4. 关键子系统设计

### 4.1 渲染抽象层（IGuiRenderer）

将「控件如何画」与「用什么画」解耦，是本方案隔离 SpriteBatch 依赖、满足约束 C1/C2 的关键。

```csharp
public interface IGuiRenderer
{
    void Begin(Matrix transform);           // 每帧一次；SpriteBatch.Begin
    void PushClip(Rectangle rect);          // 裁剪栈（映射到 ScissorRectangle）
    void PopClip();
    void DrawRect(Rectangle r, Color c);    // 纯色矩形（1x1 白纹理拉伸）
    void DrawTexture(Texture2D tex, Rectangle dst, Rectangle? src, Color tint);
    void DrawNineSlice(NineSlice slice, Rectangle dst, Color tint);
    void DrawText(TextLayout text, Vector2 pos, Color color);
    void DrawGeometry(GeometryBuffer geometry, Color tint); // 统一图元：一批带纹理/颜色的四边形（Image/Text 共用，§4.5.1）
    void End();
}
```

实现要点（`SpriteBatchGuiRenderer`）：

- **纯色**：持有 1×1 白色 `Texture2D`，拉伸绘制实现填充/描边。
- **裁剪**：`PushClip` 时求交并设置 `GraphicsDevice.ScissorRectangle`；使用带
  `ScissorTestEnable=true` 的 `RasterizerState`。由于 `SpriteBatch` 状态在 `Begin` 时固定，
  **裁剪切换需 `End()` 当前批次 → 重设 `ScissorRectangle` → 以相同参数 `Begin()` 续批**（封装在渲染器内部）。
- **排序/层级**：GUI 采用**画家算法**（父后代顺序 = 提交顺序），`SpriteSortMode.Deferred`，
  不依赖 `layerDepth`；对话框/弹层通过「屏幕栈 + 独立批次」实现覆盖。
- **变换**：`Begin(transform)` 支持整体缩放（DPI/分辨率自适应，见 §5.2）。
- **批处理与 GC**：`DrawCommand` 结构体池化；SDF 文本由专用 `SdfTextBatch`（动态顶点/索引缓冲）批处理（§4.2）。
- **统一几何**：`DrawGeometry` 消费 `GeometryBuffer`（`GraphicQuad` 列表，池化）；`DrawNineSlice`/`DrawText` 视作其上的便捷封装，几何生成逻辑移入 `Graphic` 子类（§4.5.1）。
- **文本 = 独立 SDF 批次**：`DrawText` 不走 `SpriteBatch` 的 `SpriteEffect`，而进入专用 SDF 批次（自定义 `SDFText.feb` Effect + 动态顶点/索引缓冲，复刻 SDFFontTest 的 `SDFTextRenderer`）；与图片批次按提交顺序交错 flush，保持画家算法层级。

### 4.2 文本与字体（SDF 有向距离场）

文本采用 **SDF（Signed Distance Field）** 渲染，基础实现已在本仓库 SDFFontTest 中验证，
`Gui.Text` 在其上正式化。SDF 的核心优势：**单一图集缩放到任意字号仍清晰（分辨率无关）**，且天然支持**描边/加粗**。

- **字体资产（离线）**：用 msdf-atlas-gen 从 TTF 预生成 **单通道 SDF 图集（PNG）+ 度量 JSON**（`atlas/metrics/glyphs`）。运行时零第三方字体库（满足 C2）；图集生成是构建期离线步骤。
- **加载**：`SdfFont`（源自 [`SDFFont.cs`](../SDFFontTest/SDFFont.cs)）解析 JSON 得每字形的 `atlasBounds/planeBounds/advance/offset` 与归一化 UV，PNG → `Texture2D`；提供 `MeasureString`、`LineHeight/Ascender/Descender`。
- **批处理与着色**：`SdfTextBatch`（源自 [`SDFTextRenderer.cs`](../SDFFontTest/SDFTextRenderer.cs)）把字形累积为 `VertexPositionColorTexture` 四边形写入动态顶点/索引缓冲，用 **`SDFText.feb` Effect** 一次 `DrawIndexedPrimitives` 提交。
- **着色参数**（[`SDFText_ps.hlsl`](../SDFFontTest/Shaders/SDFText_ps.hlsl)，uniform 已验证可更新）：`Smoothing`（边缘抗锯齿）、`OutlineColor/OutlineWidth`（描边）、`Weight`（加粗/变细）；单通道 SDF 用 5-tap max 补偿凹角。
- **缩放**：绘制时按 `scale / SdfFont.FontSize` 缩放同一图集到目标字号——**任意字号无需重烘焙**。
- **富文本**：轻量标记（`[color=#RRGGBB]...[/color]`、换行、`…` 截断），由 `TextLayout` 解析为分段（颜色烘焙进顶点色）。
- **尺寸自适应重排**：按内容宽度断行（决定行数）、按高度截断/省略号，作为 `Text` 可视元素的几何生成输入（见 §4.5.1）。
- **本地化**：`ILocalization.Get(key)` 提供文案；控件文本支持 key 绑定，语言切换触发重新布局。CJK 需图集包含相应字形集。
- **度量缓存**：相同（字符串 × 字号 × 宽度约束）的测量结果缓存，避免每帧重算（约束 C5）。
- **隔离**：以 `IFontProvider` 接口隔离，SDF 为默认实现；如需可替换为位图字体/其它后端。小字号极端场景可改用多通道 MSDF 图集（同一管线）。

### 4.3 布局系统（两遍式）

布局系统采用与 WPF/XAML 同源的 **Measure → Arrange** 两遍模型，但精简为游戏场景所需的最小闭包。
本节给出**决策完整**的规格：实现者据此可无歧义地写出各容器算法与失效逻辑。

#### 4.3.1 尺寸模型与布局属性

每个 `Widget` 携带下列布局输入属性（均为值类型/基础类型，避免每帧分配）：

| 属性 | 类型 | 语义 | 缺省 |
|---|---|---|---|
| `Width` / `Height` | `float` | 显式逻辑像素尺寸；`NaN` 表示 **Auto**（由内容/对齐决定） | `NaN` |
| `MinWidth/MaxWidth`、`MinHeight/MaxHeight` | `float` | 尺寸夹取区间 | `0` / `+∞` |
| `Margin` | `Thickness`(l,t,r,b) | 外边距，参与父分配、不属于自身 `Bounds` 内容 | `0` |
| `Padding` | `Thickness` | 内边距，内容区 = 自身矩形内缩 `Padding`（容器语义） | `0` |
| `HorizontalAlignment` | `enum{Left,Center,Right,Stretch}` | 在父分配矩形内的水平对齐/拉伸 | `Stretch` |
| `VerticalAlignment` | `enum{Top,Center,Bottom,Stretch}` | 垂直对齐/拉伸 | `Stretch` |
| `Visibility` | `enum{Visible,Hidden,Collapsed}` | `Hidden` 占位不绘制；`Collapsed` 不占位不绘制 | `Visible` |

**尺寸决议规则**（单轴，水平轴示例，垂直轴对称）：

1. 若 `Width` 非 `NaN` → 期望内容宽 = `Width`（再经 Min/Max 夹取）。
2. 否则若 `HorizontalAlignment == Stretch` 且可用宽度有限 → 期望内容宽 = 可用宽度（填充）。
3. 否则（Auto 且非 Stretch，或可用宽度为 `+∞`）→ 期望内容宽 = **内容度量结果**（`OnMeasure` 返回）。

#### 4.3.2 Measure 契约（自底向上）

`Measure(Vector2 availableSize)` 计算并缓存 `DesiredSize`（**含自身 `Margin`**）。`availableSize` 的某一维可为
`float.PositiveInfinity`，表示该方向不受约束（如竖直 `StackLayout` 给子控件的高度、`ScrollView` 内容轴）。

```
Measure(available):
    if Visibility == Collapsed: DesiredSize = (0,0); return
    if !MeasureDirty && available ≈ _lastMeasureInput:
        return _cachedDesired                      # 度量缓存命中（约束 C5）
    inner = available − Margin − Padding            # 传给内容的可用空间
    inner = ClampToExplicitAndMinMax(inner)         # 显式 Width/Height 优先，再夹 Min/Max
    contentSize = OnMeasure(inner)                  # 容器/叶子各自实现，度量子控件
    desired = Clamp(contentSize + Padding, Min, Max, explicit=Width/Height)
    DesiredSize = desired + Margin
    _lastMeasureInput = available; _cachedDesired = DesiredSize; MeasureDirty = false
    return DesiredSize
```

- 叶子控件（`Label`/`Image`）的 `OnMeasure` 直接返回内容尺寸（文本度量见 §4.2、图片按源尺寸或显式尺寸）。
- 容器的 `OnMeasure` 按其算法（§4.3.4）对每个子控件调用 `child.Measure(...)`，再聚合。

#### 4.3.3 Arrange 契约（自顶向下）

`Arrange(Rectangle finalRect)` 将父分配的矩形落实为自身 `Bounds`，并递归排布子控件。

```
Arrange(finalRect):
    if Visibility == Collapsed: Bounds = empty; return
    inner = finalRect − Margin
    size  = ResolveArrangedSize(inner, DesiredSize − Margin, HAlign, VAlign)
            # Stretch → 取 inner 对应维；非 Stretch → 取 min(DesiredSize, inner) 后按对齐定位
    size  = ClampToExplicitAndMinMax(size)
    Bounds = AlignWithin(inner, size, HAlign, VAlign)   # 计算最终 x/y
    content = Bounds − Padding
    OnArrange(content)                                  # 容器在 content 内定位子控件
    ArrangeDirty = false
```

- **对齐 vs 拉伸**：`Stretch` 使该轴尺寸 = 可用尺寸；`Left/Top`、`Center`、`Right/Bottom` 使用 `DesiredSize` 并在可用空间内定位。
- 若父给的 `finalRect` 小于 `DesiredSize`，按「先夹到可用、再按对齐定位」处理，多余内容交由裁剪（§5.3）。

#### 4.3.4 内置容器与算法

| 容器 | 主要参数 | Measure 要点 | Arrange 要点 |
|---|---|---|---|
| `StackLayout` | `Orientation{H,V}`、`Spacing` | 主轴：累加子 `DesiredSize` + `Spacing`；交叉轴：取子最大值。主轴给子的可用量为 `+∞`（内容驱动），交叉轴透传 | 沿主轴依次偏移放置；交叉轴按子对齐/拉伸 |
| `GridLayout` | `RowDefinitions`/`ColumnDefinitions`（每条 `Fixed(px)`/`Auto`/`Star(weight)`）、单元 `Row/Column/RowSpan/ColumnSpan` | 见下述三步定轨算法 | 由轨道偏移求每个单元矩形（含跨度），对子 `Arrange(cellRect)` |
| `DockLayout` | 子的 `Dock{Left,Top,Right,Bottom}`、`LastChildFill` | 按停靠顺序从剩余空间逐个扣减度量；剩余给最后一个 | 同序分配矩形；`LastChildFill` 时末子占满剩余 |
| `WrapLayout`（可选） | `Orientation`、`Spacing` | 按行/列累积，超出主轴换行 | 逐行排布 |
| `AbsoluteLayout`/`Canvas` | 子的 `Anchor`(见 §4.3.5) + 偏移 | 用 `+∞` 度量子（内容尺寸） | 按锚点与偏移直接定位，不参与相互挤占 |

**GridLayout 三步定轨算法**（列方向示例，行同理）：

1. **Fixed 轨**：宽度 = 定义值。
2. **Auto 轨**：宽度 = 该轨内**非跨列**单元 `DesiredSize.X` 的最大值；跨列单元的溢出量在其跨越的 Auto 轨间按需补足。
3. **Star 轨**：`剩余宽 = 容器内容宽 − ΣFixed − ΣAuto`，按各 Star 权重比例分配（`剩余宽 × w_i / Σw`），逐像素取整并把舍入余量补给最后一条 Star 轨，避免累计误差。

> 若容器可用宽度为 `+∞`（例如被 Auto 父包裹），Star 轨退化为 `Auto`（按内容度量），以避免「无限 × 比例」的未定义结果。

#### 4.3.5 响应式锚点（Anchor）

为适配多分辨率，`AbsoluteLayout`/`Canvas` 的子控件支持 Unity `RectTransform` 风格锚点：

- `AnchorMin`/`AnchorMax` ∈ `[0,1]²`：相对父内容区的归一化锚框。
- `OffsetMin`/`OffsetMax`：锚框到子控件边的像素偏移。
- 当 `AnchorMin == AnchorMax` → 点锚（子按 `DesiredSize` 定位于该点）；当二者分离 → 边锚（子随父按比例拉伸）。
- 该机制与 §5.2 的虚拟分辨率缩放**正交**：锚点解决「同一逻辑分辨率下的相对布局」，缩放矩阵解决「逻辑→物理像素映射」。

#### 4.3.6 失效、缓存与每帧收敛

- **双脏标记**：`MeasureDirty` 与 `ArrangeDirty` 分离。
  - 影响尺寸的属性（`Width/Height/Margin/Padding/Min/Max/Visibility`、内容变更）→ `InvalidateMeasure()`：置自身脏并**向上冒泡**至各祖先（父的期望尺寸依赖子），遇到已脏节点即止。
  - 仅影响定位的属性（`Alignment`、`Anchor`/偏移）→ `InvalidateArrange()`：仅置自身脏，不必上冒。
- **度量缓存**：`Measure` 命中「未脏 且 `availableSize` 与上次相同」时直接返回缓存 `DesiredSize`（§4.3.2）。
- **每帧收敛**：`GuiSystem.Update` 末尾对根执行 `root.Measure(screenSize)` → `root.Arrange(screenRect)`；若整树无脏则整体跳过。布局在**渲染之前**完成，`Draw` 只读 `Bounds`。
- **稳态零分配**：轨道尺寸数组、子控件度量结果等使用可复用缓冲/结构体，避免每帧 GC（约束 C5）。

#### 4.3.7 坐标空间与边界情形

- **坐标空间**：布局全程使用**逻辑像素**（设计分辨率）；`Bounds` 为逻辑坐标。渲染经 §5.2 的缩放矩阵映射到后备缓冲；命中测试用逆矩阵把指针坐标变换回逻辑空间，保证输入/渲染/布局三者一致。
- **`Collapsed` vs `Hidden`**：`Collapsed` 度量返回 `(0,0)` 且不参与父分配；`Hidden` 正常参与布局但不绘制。
- **无限可用尺寸 + Stretch**：Stretch 在该轴退化为 Auto（内容尺寸），杜绝无限尺寸传播。
- **溢出**：内容大于分配矩形时不重排、由裁剪处理（`ScrollView`/`Panel` 可开启裁剪，见 §5.3）。
- **循环依赖防护**：Auto 父包裹 Star/Stretch 子的场景，一律以「无限度量取内容尺寸」打破环。

### 4.4 输入系统

- **设备聚合**：每帧采集 `Mouse`/`Keyboard`/`TouchPanel`/`GamePad`（FNA `Microsoft.Xna.Framework.Input`），归一为 `GuiInputSnapshot`（含 delta、按下/释放边沿）。
- **命中测试**：从根到叶按 `Bounds` + 裁剪矩形做点选，命中链用于事件路由。
- **事件模型**：`GuiEvent`（PointerEnter/Leave/Down/Up/Click、Drag、KeyDown、TextInput、Scroll、FocusGained/Lost）。
  路由采用 **捕获（capture，根→叶）+ 冒泡（bubble，叶→根）** 两阶段，事件可 `Handled` 截断。
- **焦点与手柄导航**：`FocusManager` 维护当前焦点控件；`NavigationMap` 根据几何位置或显式声明的
  上下左右邻接关系，处理手柄方向键/摇杆导航与「确认/返回」键；焦点控件绘制 focus 视觉态。
- **文本输入**：接入 FNA `TextInputEXT`（SDL 文本输入事件）供 `TextBox` 使用；处理 IME/组合输入（尽力而为）。
- **事件→逻辑**：`GuiEvent` 是交互绑定（§4.9）的底座——控件的 `Click`/`ValueChanged` 等语义事件在其之上分发给代码后置处理器、命令与双向数据绑定。

### 4.5 控件体系

`Widget` 基类：`Bounds`、`DesiredSize`、`Visible`、`Enabled`、`Parent/Children`、`Style`、
`State`（normal/hover/pressed/disabled/focused）、生命周期钩子（`OnMeasure`/`OnArrange`/`OnDraw`/`OnEvent`）。

首批内置控件（覆盖游戏 UI 常见需求）：

- 容器：`Panel`、`StackPanel`、`Grid`、`ScrollView`、`Window/Dialog`（可拖拽、模态）。
- 基础：`Label`/`Text`、`Image`（均派生自 `Graphic`，§4.5.1）、`Button`（图/文/图文）、`IconButton`。
- 输入：`CheckBox`、`RadioButton`（分组）、`Slider`、`TextBox`、`DropDown`。
- 展示：`ProgressBar`、`Tooltip`、`ListView`（虚拟化可后置）、`TabControl`。

#### 4.5.1 可视元素抽象：Graphic（Image / Text 的共同基类）

参照 UGUI 的 `Graphic`：UI 的两大可视元素 **Image 与 Text 统一抽象为「几何发射器」**——
各自把内容烘焙成一批带纹理/颜色的四边形（`GraphicQuad`）写入可复用的 `GeometryBuffer`，
再由 `IGuiRenderer.DrawGeometry`（§4.1）提交。二者共享同一套「**按尺寸重建几何**」的机制。

**共同基类**

```csharp
public abstract class Graphic : Widget
{
    public Color Color = Color.White;   // 整体着色：draw 时作 tint，不触发几何重建
    public void SetGeometryDirty();     // 顶点脏（对标 UGUI SetVerticesDirty）

    protected abstract Vector2 OnMeasure(Vector2 available);                    // 自然尺寸（内容驱动）
    protected abstract void OnRebuildGeometry(Rectangle content, GeometryBuffer buffer); // 按尺寸生成几何
    // OnArrange：content 尺寸变化 → SetGeometryDirty()
    // OnDraw：  脏则重建并缓存几何，再 DrawGeometry(buffer, Color)
}
```

**脏分级**（对标 UGUI 顶点脏 / 材质脏）：

- **几何脏**（内容 / 尺寸 / 换行等改变）→ 重建 `GeometryBuffer`。
- **着色脏**（`Color` / 整体透明度）→ 仅在 draw 时改 tint，**不重建几何**（低成本）。
- 富文本分段颜色、渐变属**几何**（颜色烘焙进 quad）。

**「按尺寸动态调整内容」的落点** = `OnRebuildGeometry(content, buffer)`，其中 `content = Bounds − Padding`。
`OnArrange` 检测到尺寸变化即标脏，下次 `OnDraw` 重建：

| 元素 | 尺寸自适应生成 |
|---|---|
| `Image` | 按 `content` 大小生成图元；`ImageType` 决定策略：`Simple`(1 quad) / `Sliced`(**九宫格：角固定、边单轴拉伸、心双轴拉伸，1..9 quad**) / `Tiled`(平铺重复) / `Filled`(按 `FillAmount` 径向/线性裁切) |
| `Text` | 按 `content.Width` **断行**得行数、按 `content.Height / 行高` 得**可见行数**并按 `Overflow`(Overflow/Truncate/Ellipsis) 处理；可选 `BestFit` 在 `[Min,Max]` 内自动缩放字号适配；按 `Align` 定位，生成可见字形 quad（引用 SDF 图集、经 SDF Effect 着色，字号缩放分辨率无关） |

- **九宫格随尺寸再生成**：`Sliced` 从 `Sprite.Border`(l,t,r,b) 切出 3×3；角固定尺寸、边沿对应轴拉伸、中心双轴拉伸；当 `content` 小于两角之和时按比例缩角。一张皮肤图即可适配任意按钮/面板尺寸。
- **文本随尺寸再排版**：宽度变 → 重新断行改变行数（列 = 每行可容字符数）；高度变 → 改变可见行数；`Ellipsis` 时末可见行结尾省略号；`BestFit` 用二分字号使文本恰好放入 `content`。

**与其它子系统衔接**：

- **Measure**：`OnMeasure` 返回自然尺寸（Image = 精灵原尺寸；Text = 给定宽度约束下的文本度量，§4.2），参与 §4.3 两遍布局。
- **渲染**：统一经 `IGuiRenderer.DrawGeometry`（§4.1）；`GeometryBuffer` 池化，稳态零分配（约束 C5）。
- **裁剪/遮罩**：需遮罩的 `Graphic`（对标 UGUI `MaskableGraphic`）经 §5.3 裁剪栈实现。
- **扩展**：新增可视类型（纯色 `RawImage`、渐变、进度条填充等）只需再派生 `Graphic` 并实现 `OnRebuildGeometry`，无需改渲染层。

### 4.6 样式与皮肤

- **Theme**：全局默认（调色板、默认字体与字号、控件默认度量）。
- **Style**：按控件类型/命名/显式赋值层叠；每个可视属性可分**状态**取值（normal/hover/pressed/disabled/focused）。
- **Skin**：一张或多张纹理图集 + 命名区域；`NineSlice` 描述九宫格边距，用于可拉伸的按钮/面板背景。
- **状态过渡**：状态切换时触发 §4.7 的过渡（如 hover 渐变）。
- 皮肤与主题**可从 JSON 加载**（§4.8 可选），代码内联定义为默认。

### 4.7 动画/过渡

- `Tween<T>`：对 float/Color/Vector2/Rectangle 的属性补间，含 `Easing` 曲线库。
- `Transition`：绑定到控件状态切换（如 pressed → normal 的颜色/缩放回弹）。
- 由 `GuiSystem.Update(dt)` 统一步进；完成回调支持链式序列。

### 4.8 序列化（XAML-lite XML，可选/后置阶段）

界面资源采用与 WPF/XAML **相似但精简**的 XML 方言（下称 **XAML-lite**）。保留模式控件树天然对应
XML 元素树，故 XML 比 JSON 更契合 UI 序列化。仅用 BCL `System.Xml`（`XmlReader`/`XDocument`），
**零新增依赖**（满足约束 C2）。

**映射规则**

| XAML-lite | 映射到本设计 |
|---|---|
| 元素 = 控件类型（`<Button>`） | `Gui.Widgets` 的控件类型（§4.5） |
| 嵌套 = 子控件；单内容控件用内容属性 | `Widget.Children` |
| Attribute = 属性（字符串经**类型转换器**解析） | §4.3.1 值类型：`float`/`Thickness`/`Color`/`enum`/`Alignment`/`GridLength(Auto/*/px)` |
| 附加属性 `Owner.Prop="..."`（`Grid.Row`、`Dock`） | §4.3.4 容器的单元/停靠参数 |
| `x:Name="..."` | 名称表 + 代码后置 `FindByName<T>()` |
| 事件 `Click="OnStartClicked"` | 绑定到后置对象方法（委托） |
| 标记扩展 `{loc key}`（后续 `{res key}`） | 本地化（§4.2）/ 资源引用 |

示例（可读性接近 XAML，去掉了依赖属性/模板/触发器等重型机制）：

```xml
<Screen xmlns="fna-gui">
  <DockLayout LastChildFill="true" Padding="12">
    <Label Dock="Top" Text="{loc menu.title}" FontSize="32" HorizontalAlignment="Center"/>
    <Grid VerticalAlignment="Center" HorizontalAlignment="Center">
      <Grid.ColumnDefinitions>
        <Column Width="Auto"/><Column Width="*"/>
      </Grid.ColumnDefinitions>
      <Button x:Name="StartButton" Grid.Row="0" Grid.Column="0"
              Text="{loc menu.start}" Click="OnStartClicked"/>
      <Slider Grid.Row="0" Grid.Column="1" Min="0" Max="100" Value="50"/>
    </Grid>
  </DockLayout>
</Screen>
```

**实现要点**

- **类型注册表**：`Register("Button", () => new Button())` 显式注册，**不依赖反射**（Native AOT/裁剪安全）；
  同时**导出 JSON Schema**（类型/属性/类型/默认值/附加属性/枚举取值）供 Web 设计器（§9）驱动，做到单一事实源。
- **类型转换器注册表**：`string → 目标类型`，集中管理。
- **Load-only（MVP）**：先只做加载；回写序列化（保留格式、省略默认值、往返一致性）复杂，留待可视化编辑器阶段。
- **简化边界**：不做 `DependencyProperty`、`xmlns` 程序集限定、`x:Class` 代码生成、`Triggers`/`ControlTemplate`；
  状态样式改用 §4.6 模型，完整 `{Binding}` MVVM 后置（MVP 可仅做单向只读绑定）。
- 皮肤/主题这类扁平数据可留 XML 或 JSON，二选一统一即可。

**定位**：以**代码构建为主**，XML 为**数据驱动/热更/美术协作**的增强项，非 MVP 必需。

### 4.9 交互绑定（事件 / 命令 / 数据绑定）

把静态控件树接到 C# 交互逻辑。**无论控件树由代码还是 XAML-lite（§4.8）构建，绑定 API 完全一致**——
XML 只是同一套对象的声明式外壳。分三层，按 MVP → 进阶落地：

| 层 | 解决什么 | 机制 | 定位 |
|---|---|---|---|
| **L1 代码后置** | 取控件引用 + 响应交互 | `x:Name` 注入 + 事件 | MVP 必备 |
| **L2 命令** | 解耦「按钮」与「动作」、驱动可用态 | `Command` → 命令表 | 菜单/手柄推荐 |
| **L3 数据绑定** | 模型状态 ↔ 控件属性 自动同步 | `Bindable<T>` + `DataContext` | MVVM-lite，可后置 |

#### 4.9.1 L1：代码后置（View 引用 + 事件）

- **取引用**：加载后按 `x:Name` 解析 `FindByName<Button>("StartButton")`，或自动注入同名字段（`BindNamedFields(this)`）。
- **事件**：代码显式挂接，或 XML 声明式（`Click="OnStartClicked"`）。
- **AOT 安全**：声明式事件名**不走反射**，而查**处理器登记表**（可由 source generator 生成）；开发期可退化为反射。

```csharp
protected override void OnLoaded()
{
    _startButton = FindByName<Button>("StartButton");
    _startButton.Click += OnStartClicked;          // 显式挂接
    RegisterHandler("OnStartClicked", OnStartClicked); // 供 XML 声明式 Click 解析（AOT 安全）
}
private void OnStartClicked(Widget sender, GuiEvent e) => Game.StartNewGame();
```

#### 4.9.2 L2：命令（Command）

按钮绑「命令」而非裸方法，命令自带 `CanExecute` → 自动驱动控件 `Enabled`；手柄「确认键」与鼠标点击共用触发路径。

```csharp
public interface IGuiCommand { bool CanExecute(); void Execute(); }
commands.Register("StartGame", canExecute: () => _canStart, execute: () => Game.StartNewGame());
```
```xml
<Button Text="{loc menu.start}" Command="StartGame"/>
```

#### 4.9.3 L3：数据绑定（MVVM-lite）

- **变更通知**：轻量 `Bindable<T>`（`Value` + `Changed` 事件），避免全量反射轮询。
- **建立绑定**：首选**显式 lambda 绑定**（零反射、AOT 安全、低 GC）；`{bind Path}` 语法糖经**属性访问器注册表**（可源生成）解析，保持 AOT 安全。
- **DataContext 沿树继承**：子默认继承父上下文，可局部覆盖（列表项各绑一条数据）。
- **单/双向**：单向 source→UI；双向经控件 `ValueChanged` 写回 `Bindable` 再通知其它监听者。

```csharp
Bind(volumeLabel, w => w.Text, vm.Volume, v => $"{v:P0}");   // 单向 source → UI
BindTwoWay(volumeSlider, s => s.Value, vm.Volume);          // 双向 UI ↔ source
screen.DataContext = vm;                                    // 整屏共享上下文
```
```xml
<Slider   Value="{bind Volume, mode=TwoWay}"/>
<CheckBox IsChecked="{bind Fullscreen, mode=TwoWay}"/>
<Label    Text="{bind Volume, format=P0}"/>
```

#### 4.9.4 与帧循环/生命周期的衔接

- **求值时机**：绑定在 `GuiSystem.Update` 内、**布局收敛之前**执行；仅重算被 `Bindable.Changed` 标脏的绑定（复用 §4.3.6 脏驱动），稳态零遍历、零 GC。
- **回写闭环**：控件事件（§4.4）→ 写回 `Bindable` → 通知其它绑定刷新。
- **生命周期**：绑定与 `GuiScreen` 同生命周期，`OnUnloaded` 统一退订，防泄漏；全在游戏线程，无并发。
- **落地顺序**：L1 随 §4.8 Loader（MVP）→ L2 命令 → L3 先显式 lambda、后 `{bind}` 语法糖。

---

## 5. 与 FNA / FNA3D_HLSL 的集成约束

### 5.1 帧循环编排

```csharp
// Game.Update
guiSystem.Update(gameTime);       // 采集输入 → 事件路由 → 绑定同步(§4.9) → 动画步进 → 收敛布局

// Game.Draw
GraphicsDevice.Clear(...);
world.Draw();                     // 游戏世界
guiSystem.Draw();                 // GUI（内部 SpriteBatch.Begin/End + 裁剪）
imguiDebug?.Draw();               // 可选：Dear ImGui 调试层（最上层）
```

`GuiSystem` 对 `TestHarness.Headless` 敏感：headless 下仍执行 `Update`（逻辑可测），
`Draw` 中的实际 `SpriteBatch` 调用在有 `GraphicsDevice` 时执行（供像素断言）。

> 绑定求值（§4.9）在 `Update` 内、**布局收敛之前**进行，且仅重算被 `Bindable.Changed` 标脏的绑定，稳态零遍历。

### 5.2 分辨率与 DPI 自适应

- 采用**虚拟分辨率**（设计分辨率，如 1920×1080）+ `Begin(transform)` 的缩放矩阵映射到实际后备缓冲，
  或按锚点做响应式布局。二者可组合。
- 缩放矩阵同时用于 GUI 命中测试的坐标反变换，保证输入与渲染坐标系一致。

### 5.3 裁剪与批次（SpriteBatch 图片批 + SDF 文本批）

- 裁剪切换会打断 `SpriteBatch` 批次（§4.1）。渲染器内部维护「当前裁剪矩形」，仅在变化时
  `End→设置 ScissorRectangle→Begin`，以最小化批次数量。SDF 文本批次同样受当前裁剪矩形约束（共享 `ScissorRectangle`）。
- 图片/纯色/9-slice 经 `SpriteBatch` 默认 `SpriteEffect`；**文本经自定义 `SDFText.feb` Effect**（uniform 更新已在 SDFFontTest 验证）。两类批次按提交顺序交错 flush，保证层级正确。

### 5.4 与 Dear ImGui 的输入捕获协商

- 每帧先让 GUI 系统判定是否「占用」鼠标/键盘（指针命中 GUI 或有焦点文本框）。
- 提供 `GuiSystem.WantsMouse` / `WantsKeyboard` 标志；调试层（ImGui）或游戏世界据此决定是否消费同一输入，避免双重响应。
- ImGui 始终绘制在最上层（开发者工具），其自身的 `WantCaptureMouse` 优先级最高。

---

## 6. 分阶段实施计划

> 每阶段结束都应有可运行/可验证产物。建议每阶段配一个最小 Demo 程序作为验收 gate（放入 `GuiDemo/`，见 §8）。
>
> **本计划以测试驱动（TDD）推进**：每阶段的实质是把 §7.5「用例阶梯」中对应层级的 `Gxx` 断言用例从红转绿（先写测试 → 最小实现 → 重构）。下列阶段与用例层级一一对应，简单用例先行、复杂用例是简单用例的组合。

### 阶段 0：渲染抽象与最小可视（画一个带皮肤的面板）
- 定义 `IGuiRenderer`，实现 `SpriteBatchGuiRenderer`（纯色矩形 + 纹理 + 9-slice + 裁剪栈）。
- 定义 `GuiSystem`、`Widget`、`Panel`，最简手动定位（无布局）。
- **验收**：窗口显示一个 9-slice 皮肤面板；headless 下 `AssertCoverage` 断言面板区域非清屏色。

### 阶段 1：文本子系统（在 SDFFontTest 基础上正式实现）
- 将 SDFFontTest 的 `SDFFont`/`SDFTextRenderer` 沉淀为 `Gui.Text`：`SdfFont`（图集+度量加载）、`SdfTextBatch`（批处理+SDF Effect），实现 `IFontProvider` 与 `Text`/`Label`。
- 富文本（颜色/换行/截断）、描边/加粗（复用 SDF `Smoothing/OutlineColor/OutlineWidth/Weight`）、度量缓存、`ILocalization`（内存字典）。
- **验收**：`Label` 正确渲染多字号/多语言文案，缩放清晰（分辨率无关）；像素断言文字区域覆盖率与描边；度量缓存命中（无每帧分配）。

### 阶段 2：布局系统
- 两遍式 `Measure/Arrange`；`StackLayout`/`GridLayout`/`DockLayout`/绝对定位；`Thickness`/`Alignment`/`Anchor`；脏标记增量重排。
- **验收**：headless 下对若干布局用例断言各控件 `Bounds`（纯逻辑测试，不依赖渲染）。

### 阶段 3：输入与事件
- 输入聚合、命中测试、捕获/冒泡事件路由、`FocusManager`；`Button`/`CheckBox`/`Slider` 交互。
- 与 ImGui 的输入捕获协商（§5.4）。
- **验收**：模拟指针序列驱动点击/拖拽，断言事件触发与控件状态（headless 逻辑测试）。

### 阶段 4：样式、皮肤与状态
- `Theme`/`Style`/`Skin`/`NineSlice`；控件五态样式；hover/pressed 视觉切换。
- **验收**：同一控件在不同状态下像素断言颜色/背景差异。

### 阶段 5：动画与手柄导航
- `Tween`/`Easing`/`Transition`；`NavigationMap` 手柄方向导航 + 确认/返回；focus 视觉态。
- **验收**：模拟手柄输入遍历焦点顺序断言；过渡动画在 N 帧后收敛到目标值。

### 阶段 6：进阶控件与滚动
- `ScrollView`（裁剪 + 滚动条）、`Window/Dialog`（模态/拖拽）、`TextBox`（`TextInputEXT`）、`DropDown`、`ListView`、`TabControl`、`Tooltip`。
- **验收**：滚动裁剪正确；模态对话框拦截下层输入；文本框可编辑。

### 阶段 7：序列化、优化与打磨（可选/收尾）
- XAML-lite XML 加载器（`XamlLiteLoader` + 类型/转换器/附加属性注册表，Load-only、AOT 安全）；类型注册表导出 JSON Schema；绘制指令与字符串缓存优化；稳态零 GC 验证。
- **验收**：从 XML 还原界面与代码构建一致；Schema 覆盖全部内置控件；`run_tests.sh` 全量通过；稳态帧无可观测托管分配。

> Web UI 设计器（§9）作为独立可选工具，可在阶段 7 之后并行推进，不阻塞运行时里程碑。

---

## 7. 测试与验收计划

### 7.1 分层验证

1. **逻辑单元（headless）**：布局 `Bounds`、命中链、事件路由、焦点导航——不依赖渲染，复用
   [`../Common/TestHarness.cs`](../Common/TestHarness.cs) 的帧驱动与 `Report`。
2. **渲染像素级**：有窗口时用 `ReadBackbuffer` + `AssertPixel`/`AssertCoverage` 断言关键区域颜色/覆盖率。
3. **交互仿真**：以可注入的 `GuiInputSnapshot` 序列驱动，避免真实设备依赖。
4. **对照/回归**：关键界面截帧留存，版本间比对。

### 7.2 Demo 与覆盖矩阵

| Demo | 覆盖能力 | 阶段 | 用例(§7.5) |
|---|---|---|---|
| `GuiDemo/Panel` | 渲染抽象、9-slice、裁剪、Graphic/Image | 0 | G01–G08 |
| `GuiDemo/Text` | SDF 文本、富文本、本地化、度量缓存 | 1 | G09–G13 |
| `GuiDemo/Layout` | Stack/Grid/Dock/锚点、脏重排 | 2 | G14–G20 |
| `GuiDemo/Widgets` | 按钮/复选/滑条、事件、焦点、绑定 | 3 | G21–G29 |
| `GuiDemo/Skin` | 主题/皮肤/状态样式、动画、手柄导航 | 4–5 | G30–G32 |
| `GuiDemo/Advanced` | 滚动/对话框/文本框 | 6 | G33–G35 |
| `GuiDemo/Menu` | 序列化、完整设置菜单端到端、零 GC | 7 | G36–G38 |

### 7.3 自动化

- 扩展 [`../run_tests.sh`](../run_tests.sh)：新增 GUI Demo 的 headless 构建+运行分支，纳入 CI。
- 门槛：headless 逻辑用例全过、关键像素断言全过、稳态零 GC（`GC.GetAllocatedBytesForCurrentThread` 抽样）。

### 7.4 TDD 工作流与测试基建

**红-绿-重构循环**：每个 `Gxx` 用例先写成一个可运行断言（红）→ 写最小实现使其通过（绿）→ 在保持全绿前提下重构。每个用例产出 `PASS/FAIL`（复用 [`../Common/TestHarness.cs`](../Common/TestHarness.cs) 的 `ParseArgs/Tick/Report`），纳入 CI 防回归。

**三类测试及其基建**：

- **纯逻辑（headless，无 `GraphicsDevice`）**：布局 `Bounds`、命中链、事件、绑定、脏标记计数。用 `RecordingRenderer`（`IGuiRenderer` 的记录实现：把 `DrawGeometry`/`DrawRect`/`PushClip` 记为可断言的调用列表）验证「画了什么」，无需真实像素。
- **像素级（有窗口）**：SDF 文本、9-slice、裁剪、皮肤状态。用 `ReadBackbuffer` + `AssertPixel`/`AssertCoverage`。
- **交互仿真**：注入 `GuiInputSnapshot` 序列（鼠标/键盘/手柄）驱动帧推进；`FakeClock` 提供确定性时间步进 Tween/动画。

**测试基建落位**：`GuiTestKit`（`RecordingRenderer`、`InputScript`、`FakeClock`、`Bounds`/几何断言助手）放在 `GuiDemo/` 共享，供各 Demo 的 headless 模式调用。

**可断言观测量**（除像素外）：`MeasureCount`/`ArrangeCount`/`GeometryRebuildCount`/`BindingEvalCount`，用于验证「增量重排」「几何不必要重建」「稳态零分配」等非视觉性质。

### 7.5 用例阶梯（G01–G38，从简单到完备）

> 类型：**逻辑**=headless 纯逻辑；**记录**=RecordingRenderer 几何断言；**像素**=ReadBackbuffer；**仿真**=输入/时钟注入。

| 用例 | 断言内容 | 类型 | 阶段 |
|---|---|---|---|
| G01 | 空系统仅清屏：无控件时全屏=清屏色 | 像素 | 0 |
| G02 | `DrawRect` 纯色矩形覆盖目标区、区外为清屏色 | 像素 | 0 |
| G03 | 单纹理 quad 的目标矩形/UV 正确 | 记录+像素 | 0 |
| G04 | 裁剪栈：clip 内保留、clip 外被裁 | 像素 | 0 |
| G05 | 9-slice 小/大尺寸：角像素固定、边中点按轴拉伸 | 像素 | 0 |
| G06 | `Graphic` 尺寸变→`GeometryRebuildCount+1`；尺寸不变不重建 | 记录 | 0–1 |
| G07 | 仅 `Color` 改变不触发几何重建（只改 tint） | 记录 | 0–1 |
| G08 | `Image` 四种 `ImageType` 的 quad 数与覆盖符合预期 | 记录+像素 | 1 |
| G09 | `SdfFont` 加载 + `MeasureString` 宽/高（含多行）断言 | 逻辑 | 1 |
| G10 | 单行文本渲染：覆盖率 + 基线/左边距定位 | 像素 | 1 |
| G11 | 同图集缩放 0.5×/2× 仍清晰（边缘覆盖率阈值） | 像素 | 1 |
| G12 | 描边（`OutlineWidth`）+ 加粗（`Weight`）像素差异 | 像素 | 1 |
| G13 | 宽度约束下断行行数正确；`Ellipsis` 末行省略号 | 逻辑+像素 | 1 |
| G14 | 单控件 `Margin`/`Padding`/对齐 → `Bounds` | 逻辑 | 2 |
| G15 | `StackLayout` 主轴累加+`Spacing`+交叉轴对齐 → `Bounds` | 逻辑 | 2 |
| G16 | `GridLayout` Fixed/Auto/Star 定轨 + span + 舍入补偿 → `Bounds` | 逻辑 | 2 |
| G17 | `DockLayout` 停靠 + `LastChildFill` → `Bounds` | 逻辑 | 2 |
| G18 | `Anchor` 点锚/边锚随父缩放 → `Bounds` | 逻辑 | 2 |
| G19 | 改一个子控件仅重排脏子树（`MeasureCount` 断言） | 逻辑 | 2 |
| G20 | `Collapsed` 不占位 / `Hidden` 占位不绘制 | 逻辑+像素 | 2 |
| G21 | 命中链：命中控件序列正确（裁剪外不命中） | 仿真 | 3 |
| G22 | 捕获→冒泡顺序 + `Handled` 截断 | 仿真 | 3 |
| G23 | `Button`：Down→Up 同控件触发 `Click`；移出取消 | 仿真 | 3 |
| G24 | `Slider` 拖拽改 `Value`；`CheckBox` toggle；焦点获得/失去 | 仿真 | 3 |
| G25 | Tab/方向键焦点遍历顺序 | 仿真 | 3 |
| G26 | `x:Name` 注入 + 声明式 `Click` 调用登记处理器 | 逻辑 | 3 |
| G27 | `Command.CanExecute` 驱动控件 `Enabled` | 逻辑 | 3 |
| G28 | 单向 source→UI 刷新；双向 UI→source 回写 | 逻辑 | 3 |
| G29 | `OnUnloaded` 退订后 `Changed` 不再影响 UI（无泄漏） | 逻辑 | 3 |
| G30 | 五态样式：hover/pressed 像素差异 | 仿真+像素 | 4 |
| G31 | `Tween` 在 N 帧（`FakeClock`）后达目标、缓动端点正确 | 逻辑 | 5 |
| G32 | 手柄方向导航遍历焦点 + 确认/返回 | 仿真 | 5 |
| G33 | `ScrollView` 滚动偏移 + 裁剪（可见/超出项） | 仿真+像素 | 6 |
| G34 | 模态 `Dialog` 拦截下层输入 | 仿真 | 6 |
| G35 | `TextBox` 编辑（注入 `TextInputEXT` 文本） | 仿真 | 6 |
| G36 | XAML-lite 加载树与代码构建等价（结构+`Bounds` 对比） | 逻辑 | 7 |
| G37 | 完整设置菜单端到端（截帧对照 + 交互路径） | 像素+仿真 | 7 |
| G38 | 稳态零 GC：连续帧 `AllocatedBytes` 增量 ≈ 0 | 逻辑 | 7 |

### 7.6 推进规则与门槛

- **层级即依赖顺序**：上一层全部 `Gxx` 绿方进入下一层；跨层依赖用测试替身解耦（如布局未完成时，Graphic 几何用例用固定 `Bounds` 注入）。
- **阶段完成的定义** = 该阶段对应的全部 `Gxx` 绿 + 既有用例无回归。
- **每个 `Gxx` 入 CI**（`run_tests.sh`）；任一变红阻断合并。
- **缺陷优先补最小失败用例**：出现问题时先写一个更小、可复现的失败用例（插入相应层级），再修复，永久留在回归集中。

---

## 8. 工程组织与目录结构

GUI 系统作为**可复用类库**，供各 Demo/游戏引用；测试 Demo 独立成程序。

```
FNA_Test/
├── Gui/                       # 新增：GUI 类库（引用 FNA.Core；文本自研 SDF，无第三方字体库）
│   ├── Gui.csproj
│   ├── Core/                  # GuiSystem, GuiScreen, ScreenStack
│   ├── Widgets/               # Widget 及内置控件
│   ├── Layout/                # 布局容器与两遍式
│   ├── Input/                 # 输入聚合、路由、焦点、导航
│   ├── Styling/               # Theme/Style/Skin/NineSlice
│   ├── Text/                  # IFontProvider / SdfFont / SdfTextBatch / TextLayout / 本地化（源自 SDFFontTest）
│   ├── Render/                # IGuiRenderer / SpriteBatchGuiRenderer / DrawCommand
│   ├── Animation/             # Tween/Easing/Transition
│   └── Serialization/         # (可选) XamlLiteLoader/TypeRegistry/SkinLoader + dev-only HotReloadServer
├── GuiDemo/                   # 新增：各阶段验收 Demo（每子目录一个可运行程序）
│   ├── Panel/  Text/  Layout/  Widgets/  Skin/  Menu/
├── Tools/
│   └── GuiDesigner/           # (可选) Web UI 设计器：前端 + 本地 dev server（见 §9），不随游戏发布
└── docs/FNA-GUI-SYSTEM-DESIGN.md   # 本文档
```

- 依赖方向：`GuiDemo/* → Gui → FNA.Core`（文本为自研 SDF，无第三方字体 NuGet；SDF 图集由离线 msdf-atlas-gen 预生成）。
- 与 `Common/` 的关系：GUI Demo 可复用 [`../Common/TestHarness.cs`](../Common/TestHarness.cs)、[`../Common/TextureGen.cs`](../Common/TextureGen.cs)；
  Dear ImGui（[`../Common/ImGuiBindings.cs`](../Common/ImGuiBindings.cs)）仅在需要调试层的 Demo 中引用，与 GUI 库解耦。

---

## 9. Web UI 设计器（开发者工具，可选）

> 目标：提供浏览器内的可视化界面编辑器，产出 §4.8 的 XAML-lite XML，并对接运行时做**所见即所得**预览。

### 9.1 可行性结论

**可行。** 设计器是**开发期外部工具**，不随游戏发布，因此**不受约束 C1/C2 限制**（可自由使用任意 Web 技术栈）。
它读写 XAML-lite XML（§4.8），与运行时通过「Schema 驱动 + 实时预览」两条链路对接。

### 9.2 架构

- **前端（浏览器）**：控件树视图 + 拖拽画布 + 属性检视器 + XML 源码**双向视图**（可视 ↔ 源码）。
- **Schema 驱动**：运行时从**类型注册表（§4.8）导出 JSON Schema**（控件类型、属性、类型、默认值、附加属性、枚举取值）；
  设计器据此**自动生成**调色板与属性编辑器。单一事实源 = 注册表，杜绝设计器与运行时漂移。
- **持久化**：设计器经本地 dev server 的文件接口读写磁盘上的 `.xml` 资源。

### 9.3 预览：两种模式

1. **实时链路（推荐，权威保真）**：游戏 **dev 版**内置一个**仅绑定 localhost 的 WebSocket/HTTP 服务**。
   设计器把当前 XAML-lite 文档推送给游戏 → 游戏 `XamlLiteLoader` **热重载**该屏 →
   用现有 `ReadBackbuffer`（[`../Common/TestHarness.cs`](../Common/TestHarness.cs) 已有）截帧回传 PNG →
   设计器显示。可选：转发鼠标/键盘事件到游戏做交互预览。
2. **近似预览（离线兜底）**：无游戏连接时，设计器用 DOM/Canvas 按**同一套布局规则（§4.3）**做近似渲染，
   仅供纯布局编辑。**注意**：近似预览会与真实引擎产生保真漂移，正式核对以实时链路为准。

### 9.4 与运行时的契约

- **Schema 导出**：复用 §4.8 的类型/转换器/附加属性注册表导出，不手写、不漂移。
- **热重载端点**：dev-only，release 构建编译剔除；仅绑定 localhost。
- **截帧协议**：`ReadBackbuffer` → PNG（或原始像素）→ WebSocket 二进制帧。

### 9.5 工程落位

- Web 前端 + 本地 dev server 位于 `Tools/GuiDesigner/`（独立工程，见 §8），**不随游戏发布**。
- 运行时侧的热重载/截帧端点位于 `Gui.Serialization` 的 **dev-only 子模块**。

> 定位：**可选增强工具**，与运行时里程碑解耦，可在阶段 7 之后并行推进。

---

## 10. 风险与缓解

| 风险 | 影响 | 缓解 |
|---|---|---|
| 单通道 SDF 在小字号/凹角处失真 | 文字发虚/缺角 | 5-tap max 补偿（已在 `SDFText_ps` 实现）；极端场景切多通道 MSDF（同管线） |
| SDF 图集需离线用 msdf-atlas-gen 生成 | 资产流程依赖工具 | 纳入构建期资产步骤；图集+度量随游戏发布；运行时零字体库依赖 |
| 裁剪频繁打断 SpriteBatch 批次 → 批次爆炸 | 帧率下降 | 仅裁剪矩形变化时切批；相邻同裁剪合并；必要时对深层滚动做区域烘焙 |
| `ScissorRectangle` 与虚拟分辨率缩放坐标不一致 | 裁剪错位 | 裁剪矩形统一在「后备缓冲像素坐标」计算，输入/绘制经同一缩放矩阵反变换 |
| 每帧托管分配（字符串/指令/事件）导致 GC 卡顿 | 掉帧 | 结构体化 `DrawCommand`/事件、度量与字符串缓存、对象池；CI 抽样断言零分配 |
| 手柄导航几何邻接判定不直观 | 导航跳错控件 | 支持显式声明上下左右邻接，几何推断仅作缺省 |
| 与 ImGui 输入双重响应 | 点击穿透/冲突 | `WantsMouse/WantsKeyboard` 协商，ImGui 捕获优先级最高（§5.4） |
| 文本输入 IME/组合输入 | CJK 输入不全 | 接入 `TextInputEXT`；IME 组合尽力而为，先保证基础输入 |
| 反射工厂在 Native AOT/裁剪下失效 | XML 加载崩溃 | 用显式类型/转换器注册表，不依赖反射 |
| XAML-lite 范围蔓延到完整 Binding/模板 | 复杂度失控 | 分阶段：先静态树 + `x:Name` + 附加属性，绑定/模板后置 |
| Web 设计器近似预览与引擎保真漂移 | 所见非所得 | 以实时链路（热重载 + 截帧）为权威，近似预览标注「仅布局」 |
| dev 热重载/截帧端点误入 release | 安全/体积风险 | 端点 dev-only 编译剔除、仅绑定 localhost |
| `{bind Path}`/事件名反射解析在 AOT 下失效 | 绑定/事件丢失 | 走属性访问器与处理器登记表（可源生成）；显式 lambda 绑定为首选路径（§4.9） |
| 数据绑定订阅未退订导致内存泄漏 | 屏幕切换后泄漏 | 绑定随 `GuiScreen` 生命周期，`OnUnloaded` 统一退订 |

---

## 11. 里程碑与工作量估算

| 里程碑 | 内容 | 相对工作量 |
|---|---|---|
| M0 | 阶段 0–1：渲染抽象 + 文本（首个带文字的皮肤面板） | 中 |
| M1 | 阶段 2–3：布局 + 输入事件（可交互控件） | 大 |
| M2 | 阶段 4–5：皮肤状态 + 动画 + 手柄导航 | 中 |
| M3 | 阶段 6：进阶控件与滚动/对话框/文本框 | 中 |
| M4 | 阶段 7：XAML-lite 序列化/优化/打磨，全量测试 | 中 |
| M5（可选） | §9：类型注册表导出 Schema + Web 设计器 + 实时预览链路 | 大 |

> 关键路径为 M1：布局与输入模型一旦稳定，后续控件多为「组合既有能力 + 皮肤化」。
> 建议 M0 完成后立即锁定 `IGuiRenderer` 与 `Widget` 基类 API，避免后期返工。

---

## 附：实现顺序速查（Checklist）

- [ ] 新建 `Gui/` 类库工程，引用 FNA.Core（文本自研 SDF，无第三方字体库）
- [ ] `IGuiRenderer` + `SpriteBatchGuiRenderer`（纯色/纹理/9-slice/裁剪栈 + `DrawGeometry`/`GeometryBuffer`）
- [ ] `Graphic` 可视元素基类（几何缓存 + 尺寸自适应重建）+ `Image`(9宫格/平铺/填充) + `Text`(断行/省略号/BestFit)
- [ ] `GuiSystem` + `Widget` + `Panel`（最小可视，headless 覆盖率断言）
- [ ] SDF 文本：`SdfFont`（图集/度量）+ `SdfTextBatch`（SDF Effect 批处理）+ `IFontProvider` + `Text`/`Label` + 富文本/描边/本地化/度量缓存（源自 SDFFontTest）
- [ ] 两遍式布局 `Measure/Arrange` + Stack/Grid/Dock/锚点 + 脏重排（Bounds 逻辑断言）
- [ ] 输入聚合 + 命中 + 捕获/冒泡事件 + `FocusManager` + ImGui 输入协商
- [ ] `Button/CheckBox/RadioButton/Slider`（交互 + 状态）
- [ ] `Theme/Style/Skin/NineSlice` + 控件五态样式
- [ ] `Tween/Easing/Transition` + `NavigationMap` 手柄导航 + focus 视觉态
- [ ] `ScrollView/Window/Dialog/TextBox(TextInputEXT)/DropDown/ListView/TabControl/Tooltip`
- [ ] （可选）XAML-lite XML `XamlLiteLoader` + 类型/转换器/附加属性注册表（Load-only、AOT 安全）
- [ ] 绘制指令/字符串缓存优化 + 稳态零 GC 验证
- [ ] `GuiDemo/*` 各阶段 Demo + 扩展 `run_tests.sh` 纳入 CI
- [ ] （可选）注册表导出 JSON Schema + dev-only 热重载/截帧端点 + `Tools/GuiDesigner/` Web 设计器（§9）
