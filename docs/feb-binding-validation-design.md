# 设计文档：FEB 描述符绑定错误预防体系

| | |
|---|---|
| 状态 | 待实施 |
| 日期 | 2026-07-31 |
| 背景 | FNA3D UE5 对齐（Phase 0-4）期间两次踩中描述符绑定错误 |
| 关联文档 | `fna3d-ue5-alignment-plan.md`、`HLSL-FEB-DEVELOPMENT-GUIDE.md`、`REQ-effect-hlsl-vertex-convention.md` |

---

## 1. 背景与问题模型

### 1.1 已发生的两起事故

| 事故 | 根因 | 症状 | 暴露方式 |
|------|------|------|---------|
| FEB param 布局遗留（Phase 0/3 排查） | feb_builder `4c5f015` 将 param 条目 88→84 字节，旧 FEB 未重建；header version 仍为 2，无法从版本号看出 | `FNA3D_CreateEffect` native 访问冲突（JFAOutline）；C# 解析越界（SceneRenderer） | 运行时崩溃，且崩溃点随损坏数据漂移 |
| RWStructuredBuffer 误判只读（Phase 4） | `reflect_spirv` 用 `NonReadable` decoration 区分读写，但 DXC 对 `RWStructuredBuffer` **不发** `NonWritable`/`NonReadable` | 计算管线布局缺 set 1 绑定：VUID-07988 验证错误 + `SDL_GPU_CheckComputeBindings` 断言 | 验证层报错，但测试脚本未消费，人工才发现 |

### 1.2 根因模型：三份布局描述必须逐字节一致

```
① HLSL register(t/s/u/b#) 声明          （开发者手写）
        │ dxc -fvk-bind-register（feb_builder 分配 set/binding）
        ▼
② FEB 内的资源计数                       （feb_builder 手写 reflect_spirv 反射）
   num_samplers / num_*_storage_buffers / threadcount 等
        │ SDL_CreateGPU*Pipeline(createInfo)
        ▼
③ SDL_GPU 生成的管线布局 ↔ SPIR-V 实际布局  （Vulkan 驱动比对）
```

任何一处漂移都只在 **③ 运行时**暴露，且常表现为随机崩溃/静默错误而非明确报错。本设计的目标：把暴露点尽量前移（构建期 > 测试判定期 > 运行期），并消除脆弱环节。

---

## 2. 措施总览（按优先级）

| # | 措施 | 拦截阶段 | 改动面 | 优先级 |
|---|------|---------|--------|--------|
| M1 | 测试脚本消费验证层输出 | 测试判定期 | `run_tests.sh` / `run_tests.bat` | P0 |
| M2 | feb_builder 构建期自洽断言 | FEB 构建期 | `FNA/tools/feb_builder.py` | P0 |
| M3 | spirv-cross 替换手写反射 | FEB 构建期 | `feb_builder.py` reflect 部分 | P1 |
| M4 | FEB 条目尺寸守卫 | Effect 加载期 | `FNA3D_Effect.c` + C# `Effect` | P1 |
| M5 | 描述符集约定单一事实源 | 开发期 | 文档 + feb_builder 常量 | P2 |

---

## 3. 措施详细设计

### M1：测试脚本消费验证层输出（P0）

**现状**：`run_tests.sh` 第 104 行只 `grep -q "RESULT:.*PASS"`。验证层已开启（`Validation layers enabled` 出现在每个测试输出中），但 VUID 错误行被完全忽略——产生验证错误但未崩溃的用例照样报 PASS。

**设计**：

- `test_proj()` 改为先捕获完整输出到临时文件，再做两级判定：
  1. 无 `RESULT:.*PASS` → FAIL（现状不变）；
  2. 输出含 `VUID-` / `Validation Error` / `Assertion failure at SDL_GPU` → 即使有 PASS 也判 **FAIL(validation)**，并打印首条错误行。
- `run_tests.bat` 的 `:run_test` 做同样改造（`findstr` 两次）。
- GuiDemo 的 G01-G38 循环同样适用。

**已知豁免**：BasicEffect 存在文档已记录的 VUID-07904（`REQ-effect-hlsl-vertex-convention.md` 待实施项）。两种处理任选：
- a) 豁免清单：脚本内维护 `KNOWN_VALIDATION_FAILURES="StockEffect/BasicEffect"`，命中时降级为 WARN；
- b) 先实施 REQ 文档的 C1-C5 修复再启用 M1。
建议 a)，避免 M1 被无限期阻塞；豁免清单本身就是技术债清单。

**验收**：
- 人为在任一 FEB 中把 `num_samplers` 改错 → 对应测试判 FAIL(validation)；
- BasicEffect 在豁免清单内仍 PASS(warn)；其余测试不受影响。

### M2：feb_builder 构建期自洽断言（P0）

**现状**：`compile_hlsl_to_spirv` 里 `scan_hlsl_registers` 扫出 HLSL 的 t/s/u 寄存器集合并据此分配 `-fvk-bind-register`；`reflect_spirv` 独立地从 SPIR-V 反射计数。两者没有交叉校验——Phase 4 的误判本可在这里拦住。

**设计**：在 `build_feb` 中每个 shader entry 完成反射后，断言"扫描 ↔ 反射"自洽，失败即 `sys.exit(1)`（FEB 不落盘）：

| 阶段 | 断言 |
|------|------|
| compute | `readwriteStorageBufferCount + readwriteStorageTextureCount == len(ui)`（HLSL 的 u# 数） |
| compute | `readonlyStorageBufferCount == len(ti)`（HLSL 的 t# 中非纹理部分；若无法区分，放宽为 `roBuf + samplers 相关 ≥ len(ti)` 并 WARN） |
| compute | `threadCountX*Y*Z > 0`（缺 `[numthreads]` 的入口直接拒绝） |
| graphics | `uniforms ≤ 1`（当前驱动只推 slot 0，多于 1 个 cbuffer 属于未支持用法） |
| 全部 | 反射到的每个资源的 descriptor set 必须落在该 stage 的合法集合内（compute: {0,1,2}，vertex: {0,1}，pixel: {2,3}） |

错误信息必须包含：shader 文件、entry、期望值 vs 实际值、修复提示（如"RW 资源请用 register(uN)"）。

**验收**：
- 构造一个把 `RWStructuredBuffer` 写成 `register(t0)` 的着色器 → 构建失败并指出原因；
- 现有全部 `.feb.json` 重建通过（无回归）。

### M3：spirv-cross 替换手写反射（P1）

**现状**：`reflect_spirv` 约 100 行手写 SPIR-V 字码遍历，是 Phase 4 bug 的直接来源。环境中已有 `spirv-cross`（`~/.local/spirv-cross/bin`，2021-01 版，已验证 `--reflect` 可用）。

**已验证的输出能力**（`spirv-cross <file.spv> --reflect`，注意文件参数在 `--reflect` 之前）：
- `entryPoints[].workgroup_size` → threadCount；
- `ssbos[]` 每项含 `set` / `binding` / 类型名（`type.RWStructuredBuffer.float` / `type.StructuredBuffer.*`）；
- `ubos[]`、`separate_images[]`、`separate_samplers[]` 分类清晰。

**设计**：
- `reflect_spirv` 改为：写临时 `.spv` → 调 `spirv-cross --reflect` → 解析 JSON；
- 读写/只读判别规则保持 Phase 4 的修法：**按描述符集**（compute set 1 = RW），类型名仅作二次校验（`type.RWStructuredBuffer.*` 应当出现在 set 1，否则报错）——不依赖 `NonWritable`；
- 保留手写解析作为 fallback（`spirv-cross` 不在 PATH 时 WARN 降级），保证 CI 环境不强依赖新工具；
- M2 的断言继续生效，双保险。

**风险**：spirv-cross 版本较旧（2021），对新 SPIR-V 特性支持有限；当前着色器均为 SM6.0 基础特性，风险低。升级 spirv-cross 属环境事务，不在本设计范围。

### M4：FEB 条目尺寸守卫（P1）

**现状**：C 解析器（`FNA3D_Effect.c`）信任 header 偏移顺序读取；条目尺寸漂移（如 88→84）会静默错位。判定式已在排查中验证：`(tech_off - param_off) / paramCount`。

**设计**：`FNA3D_LoadEffect` 解析 header 后、读 param 前增加守卫：

```c
/* param 条目 84 字节 + 每 annotation 40 字节；无 annotation 时区域应为 84*nc。
 * 带 annotation 时无法用除法精确校验，改为顺序读完后校验游标 == 区域尺寸。 */
if (paramCount > 0 && annotation 总数 == 0 && (techOffset - paramOffset) % 84 != 0) {
    FNA3D_LogError("FEB param entries are not 84 bytes — stale FEB, rebuild with current feb_builder");
    return 0;  /* CreateEffect 失败而非越界 */
}
/* 更通用：param 循环结束后 assert(paramReadOff == techOffset - paramOffset) */
```

- 通用校验点选**读完后核对游标**（`paramReadOff == 区域尺寸`），能覆盖任何形式的条目漂移，不只 88→84；
- pass（24B）与 shader（52B）区域同样加除法校验（无变长部分，可精确整除）；
- C# `Effect.INTERNAL_parseEffect` 加同样的游标核对，报 `InvalidOperationException("stale FEB")` 而非越界。
- 顺带把 FEB_VERSION 升到 3 的方案**被否决**：版本号无法防"格式没变但工具演进导致的布局差"，游标核对是更本质的守卫；且升版本要求全量重建+双端同步，代价大。

**验收**：把任一 git 历史里的 88 字节旧 FEB 喂给新解析器 → 得到明确的 "stale FEB" 错误而非崩溃。

### M5：描述符集约定单一事实源（P2）

**现状**：set 布局知识散落两处——feb_builder 的绑定分配代码（`gs,ss` 变量与 compute 分支）和 SDL_GPU 文档约定（驱动侧隐含）。Phase 4 的修复注释里第三次重复了这份知识。

**设计**：
- 在 `HLSL-FEB-DEVELOPMENT-GUIDE.md` 增加"描述符集布局"一节，作为唯一权威表：

| stage | set 0 | set 1 | set 2 | set 3 |
|-------|-------|-------|-------|-------|
| vertex | 采样纹理+只读存储 | uniform | — | — |
| pixel | — | — | 采样纹理+只读存储 | uniform |
| compute | 采样纹理+只读存储(t#) | 读写存储(u#) | uniform(b#) | — |

- feb_builder 中把散落的 `"1","0"` / `"3","2"` / `"2","0"` 魔法数替换为顶部命名常量（`SET_VS_RES, SET_VS_UBO, ...`），注释指向指南章节；
- M2/M3 的校验引用同一组常量。

---

## 4. 实施顺序与依赖

```
M1（脚本判定）──────────── 独立，先做，立即生效
M2（构建期断言）─────────── 独立，先做
M3（spirv-cross 反射）──── 依赖 M2 先就位（断言兜底），建议随后
M4（加载期守卫）─────────── 独立，C 改动需重编 FNA3D + 回归
M5（单一事实源）─────────── 随 M3 一起做最顺
```

全部措施都不改变 FEB 二进制格式与运行时行为，对已通过的测试零影响（除 M1 会暴露 BasicEffect 的既有验证错误，用豁免清单处理）。

## 5. 非目标

- 不修 BasicEffect 的 VUID-07904（属 `REQ-effect-hlsl-vertex-convention.md` 范围）；
- 不修 SceneRenderer 的 draw 崩溃与数组参数（`LightData[64]`）缺 count 字段问题（已在 alignment-plan 文档记录为独立待办）；
- 不引入运行时 SPIR-V 反射（驱动仍完全信任 FEB 元数据——守卫的职责是让不可信的 FEB 无法生成/加载，而非在运行时兜底）。
