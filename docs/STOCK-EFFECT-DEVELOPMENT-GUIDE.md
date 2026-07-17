# FNA Stock Effect 开发指南

本文档总结 FNA_Test 仓库中的测试程序体系，供后续 FNA + FNA3D_HLSL 的 Effect 开发参考。

---

## 1. 测试程序一览

| 项目 | 对应 Effect | 演示内容 | 预期表现 |
|------|------------|---------|---------|
| `SpriteEffect/` | SpriteEffect | SpriteBatch 批处理渲染：旋转、缩放、颜色调制、Alpha 混合、5 种排序模式 | 5 个旋转缩放的红白棋盘格 Sprite |
| `BasicEffect/` | BasicEffect | 4 technique 切换：PNT(光照) / PT(纹理) / PC(顶点色) / PCT(顶点色+纹理)，含雾、3 档光照 | 旋转立方体，键盘切换效果 |
| `AlphaTestEffect/` | AlphaTestEffect | 8 种 Alpha 比较函数、可调 ReferenceAlpha、自动振荡、雾 | 全屏 Alpha 渐变四边形，clip 边界随参数移动 |
| `DualTextureEffect/` | DualTextureEffect | 双纹理混合（棋盘格 × 径向渐变），脉冲漫反射色，雾 | 全屏四边形，颜色脉冲变化 |
| `EnvironmentMapEffect/` | EnvironmentMapEffect | 立方体贴图反射、Fresnel 边缘增强、高光、雾 | 旋转球体，边缘彩色反射、正面棋盘格纹理 |
| `SkinnedEffect/` | SkinnedEffect | GPU 骨骼蒙皮（2 骨骼）、可调权重数、顶点/像素光照 | 弯曲圆柱体，骨骼动画 |
| `BasicEffectMatrix/` | BasicEffect（自动测试） | B1-B9 自动化像素断言，覆盖全部 technique × 顶点布局组合 | headless 运行，打印 PASS/FAIL |

---

## 2. 设计模式

每个测试程序结构相同，可作为新 Effect 测试的模板：

```
EffectName/
├── EffectName.csproj    # 引用 FNA.Core + Common/*.cs
└── Program.cs           # Game 子类
```

### 2.1 最小模板

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNA.Test;

public class MyDemo : Game
{
    private GraphicsDeviceManager graphics;

    public MyDemo()
    {
        graphics = new GraphicsDeviceManager(this) {
            PreferredBackBufferWidth = 800,
            PreferredBackBufferHeight = 600,
            SynchronizeWithVerticalRetrace = false
        };
    }

    protected override void LoadContent()
    {
        // 创建 Effect、顶点缓冲、纹理
    }

    protected override void Update(GameTime gt)
    {
        // 键盘输入（可选）
        if (Keyboard.GetState().IsKeyDown(Keys.Escape)) Exit();

        // headless 断言（不能省！）
        TestHarness.Tick(this, 3, () =>
        {
            var px = TestHarness.ReadBackbuffer(GraphicsDevice);
            int fails = 0;
            fails += TestHarness.AssertCoverage(px, Color.CornflowerBlue, 0.02f, "coverage");
            TestHarness.Report("MyDemo", fails);
        });
    }

    protected override void Draw(GameTime gt)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        // effect.CurrentTechnique.Passes[0].Apply();
        // GraphicsDevice.SetVertexBuffer(vb);
        // GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, n);
    }

    static void Main(string[] args)
    {
        TestHarness.ParseArgs(args);
        using var g = new MyDemo();
        g.Run();
    }
}
```

### 2.2 TestHarness 机制

- `--headless` 参数（或 `FNA_TEST_HEADLESS=1` 环境变量）启用自动断言模式
- `Tick(game, frame, action)` 在第 `frame` 帧执行 `action`（读像素、断言），然后 `Exit()`
- `ReadBackbuffer(device)` 通过 `GetBackBufferData<Color>()` 读整个帧缓冲
- `AssertCoverage(px, clearColor, minRatio)` 检查非背景像素占比
- `AssertPixel(px, w, x, y, expected, tol, label)` 检查指定坐标像素值（容差 ±3）

---

## 3. `run_tests.sh` 一键回归

```bash
./run_tests.sh
```

自动完成：编译 FNA3D_HLSL → 重建全部 FEB → 编译 FNA → 编译所有测试 → headless 运行 → 汇总结果。退出码非零表示有失败。

---

## 4. 顶点布局必须匹配（最容易出错）

### 4.1 严格约定 (C1-C5)

| 原则 | 说明 | 违反后果 |
|------|------|---------|
| **C1 顺序一致** | VS_INPUT 字段顺序 = 顶点声明元素顺序 | 属性值错位，颜色当法线读 |
| **C2 精确声明** | 只声明实际提供的属性，不声明超集 | Vulkan UB，validation 报错 |
| **C3 technique=布局** | 每种输入布局一个 technique | — |
| **C4 布局标准** | 以 FNA `IVertexType` 为参考 | — |
| **C5 数值类别** | HLSL float↔Vector/Color, uint↔Byte4 | 类型不匹配导致垃圾数据 |

### 4.2 常见错误

**错误 1：vertex buffer 与 technique 不匹配**

```csharp
// 错：PT technique 要求 {Position, TexCoord}，但传了 VPNT
effect.LightingEnabled = false;  // → 选 PT technique
effect.CurrentTechnique.Passes[0].Apply();
GraphicsDevice.SetVertexBuffer(cubeVpnt);  // {P,N,T} ← Normal 被当 TexCoord 读！
```

```
正确做法：根据 technique 选择正确的 vertex buffer
PT technique → VertexPositionTexture
PC technique → VertexPositionColor
PNT technique → VertexPositionNormalTexture
```

**错误 2：属性变更后第一次 Apply 用了旧 technique 的 pass**

`CurrentTechnique.Passes[0]` 在 `OnApply()` 之前取值，拿到的还是旧 technique。第一帧发错 globalPass → 错 shader。测试中要先 `Apply` 一次（Prime）再正式绘制。

**错误 3：PS 寄存器与 FEB manifest 不同步**

修改 PS 的 `register(cN)` 后必须更新 `.feb.json` 中对应参数的 `register` 值。反之亦然。验证方法：

```bash
# 对比 PS/V S寄存器与 FEB manifest
grep "register" Effect_ps.hlsl
python3 -c "import json; [print(p['name'], p['register']) for p in json.load(open('Effect.feb.json'))['parameters']]"
```

**错误 4：修改 shader 后忘记重建 FEB 并复制到 FXB/**

```
修改 .hlsl 或 .feb.json
  → python3 feb_builder.py Effect.feb.json
    → cp Effect.feb ../FXB/Effect.fxb
      → dotnet build FNA.Core.csproj
```

### 4.3 几何体绕序

Vulkan 后端的 front-face 约定要求三角形顶点为**顺时针**绕序（从外表面观察）。`GeometryGen` 中的 Cube/Sphere/SkinnedCylinder 已全部统一为此绕序。自行添加几何体时注意验证。

---

## 5. 多 Technique Effect 开发指南

### 5.1 HLSL 结构

```hlsl
// 同一文件，共享 cbuffer、VS_OUTPUT、辅助函数
// 每个 technique 一个 VS_INPUT + 一个 entry point

struct VS_INPUT_PNT { float4 P; float3 N; float2 T; };
struct VS_INPUT_PT  { float4 P; float2 T; };
// ...

VS_OUTPUT VSMain_PNT(VS_INPUT_PNT input) { ... }
VS_OUTPUT VSMain_PT(VS_INPUT_PT input)   { ... }
```

### 5.2 FEB Manifest 结构

```json
{
  "techniques": [
    { "name": "PNT", "passes": [{ "name": "P0",
        "vertexShader": {"source": "Effect_vs.hlsl", "entry": "VSMain_PNT"},
        "pixelShader":  {"source": "Effect_ps.hlsl", "entry": "PSMain"} }] },
    { "name": "PT",  "passes": [{ "...entry: VSMain_PT..." : "" }] }
  ],
  "parameters": [ ... ]
}
```

### 5.3 C# OnApply 模式

```csharp
// 缓存 technique 引用
techniquePNT = Techniques["PNT"];
techniquePT  = Techniques["PT"];

// OnApply 中选择
EffectTechnique target;
if (vertexColorEnabled)
    target = textureEnabled ? techniquePCT : techniquePC;
else
    target = lightingEnabled ? techniquePNT : techniquePT;

if (CurrentTechnique != target)
    CurrentTechnique = target;
```

注意 `ShaderIndex` 中不再设 vertexColor 位（+2），由 technique 选择接管。

---

## 6. 调试技巧

| 症状 | 可能原因 | 检查方法 |
|------|---------|---------|
| 窗口全黑 | 纹理/ShaderIndex/寄存器不匹配 | headless 测试 `AssertCoverage` 检查 |
| 纯色无纹理 | vertex buffer 与 technique 不匹配 | 确认 `SetVertexBuffer` 的类型与当前 technique 一致 |
| 看到内表面 | 三角形绕序反了 | 与 Cube 对比，Swap 后两顶点反转 |
| 只能看到一半 | 绕序不一致（有的面 CW 有的 CCW） | 逐面计算 `right×up` 是否等于 normal |
| Fog 无效果 | FogColor 默认黑色（不可见） | 设 `FogColor = Color.Red.ToVector3()`，缩小雾距 |
| 环境反射全黑 | `EnvironmentMapSpecular=(0,0,0)` 且 `FresnelFactor=1.0` | 调低 FresnelFactor 或开 Spec |
| 改参数无反应 | PS 寄存器与 FEB manifest 不一致 | 对比 grep 输出 |
| 修改 shader 不生效 | 没重建 FEB 或没复制到 FXB/ | 检查 `ls -la HLSL_DXC/*.feb FXB/*.fxb` 时间戳 |
