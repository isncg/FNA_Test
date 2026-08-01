# 设计文档：FEB 描述符绑定错误预防体系（三道不变量）

| | |
|---|---|
| 状态 | 已审阅，可实施 |
| 日期 | 2026-08-01（覆写自 2026-07-31 版） |
| 背景 | FNA3D UE5 对齐（Phase 0-4）期间两次踩中描述符绑定错误 |
| 关联文档 | `fna3d-ue5-alignment-plan.md`、`HLSL-FEB-DEVELOPMENT-GUIDE.md`、`REQ-effect-hlsl-vertex-convention.md` |
| 修订说明 | 以**三条可机器检验的不变量**组织，措施 M1–M5' 全部归位于不变量之下 |

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

任何一处漂移都只在 **③ 运行时**暴露，且常表现为随机崩溃/静默错误而非明确报错。

### 1.3 设计哲学：三道不变量

把整个预防体系压缩成三条**可机器检验的不变量**，每条由"最早能拦截的层"负责。任何措施如果不服务于其中一条，就不该做。

| # | 不变量 | 责任层 | 失败时的用户体验 |
|---|---|---|---|
| **I1** | HLSL 写错就编译不过 | feb_builder 构建期 | 红字 + 文件:行号 + 修复提示，FEB 不落盘 |
| **I2** | FEB 陈旧就加载不过 | `FNA3D_Effect.c` / C# `Effect` 加载期 | 明确 "stale FEB" 错误，CreateEffect 返回失败而非崩溃 |
| **I3** | 验证层报错就测试不过 | `run_tests.sh` / `run_tests.bat` | FAIL(validation) + 首条 VUID 行 |

三条不变量**互相不替代**：

- **I1** 拦"开发者写错"——对应根因模型 ① 的输入端；
- **I2** 拦"工具演进 + 旧产物"——对应事故 1，与 register 无关，是字节布局漂移；
- **I3** 拦"前两道都漏掉的运行期症状"——对应事故 2 的暴露方式（验证层有报错但没人看）。

设计文档里两起事故恰好分别落在 I2 和 I3 的盲区——这就是 Phase 1 必须先做 M1/M4 的依据。

### 1.4 措施代号速查

本文用 M1–M5' 作为实施措施的短代号，完整定义见各章节：

| 代号 | 一句话定义 | 归属不变量 | 详见 |
|---|---|---|---|
| **M1** | 测试脚本消费 Vulkan 验证层输出，VUID 错误即判 FAIL | I3 | §4 |
| **M2** | feb_builder 构建期自洽断言（A1–A6），失败即拒绝落盘 | I1 | §2.3 |
| **M3** | 用 spirv-cross 替换手写 SPIR-V 反射，消除 decoration 误判 | I1 | §2.3 |
| **M4** | FEB 加载期游标核对，字节布局漂移即拒绝加载 | I2 | §3 |
| **M5'** | 描述符集布局 + 参数校验的单一事实源（派生层） | I1 | §2.2 |

---

## 2. I1：HLSL 写错就编译不过

### 2.1 三层结构

```
派生层（消除）─── 能让开发者写不出的错，就不要靠抓
   │
断言层（拒绝）─── 派生覆盖不到的（显式覆盖、存量），构建期 sys.exit(1)
   │
转义层（兼容）─── 显式 register 标注 = "我清楚自己在做什么"，工具只校验不改写
```

### 2.2 派生层（M5'）

| 派生项 | 事实源 | 工具动作 |
|---|---|---|
| 资源寄存器**类别**（t/s/u/b） | HLSL 类型名 | `RWStructuredBuffer/RWTexture* → u#`，`StructuredBuffer/Texture*/SamplerState → t#/s#`，`cbuffer/cN → b#` |
| 资源寄存器**编号** | 工具规则 | 图形阶段只读存储从 t1 起（避开强制采样器槽位），u# 自动偏移 `max(1, max(t∪s)+1)` |
| `parameters[]` register/size 校验 | SPIR-V 反射 `$Globals` 布局（成员偏移 + 总大小） | 反射得出 cbuffer 总字节数与成员偏移序列，与 JSON 声明的 register+type 推算的布局交叉核对；**名称与默认值仍由 JSON 提供**（DXC 默认 strip 反射信息，SPIR-V 中成员名不可靠） |
| set/binding | feb_builder 命名常量 | `SET_VS_RES=0, SET_VS_UBO=1, SET_PS_RES=2, SET_PS_UBO=3, SET_CS_RO=0, SET_CS_RW=1, SET_CS_UBO=2` |

**关键约束**：

- 派生发生在内存中，磁盘上的 `.hlsl` 与 `.feb.json` 始终是开发者原文；dxc 接收的是临时改写产物；
- 描述符集布局表（`HLSL-FEB-DEVELOPMENT-GUIDE.md` 新增"描述符集布局"节）作为唯一权威表，feb_builder 常量与 M2/M3 校验都引用同一组常量；
- 原 `feb_builder` 中散落的 `"1","0"` / `"3","2"` / `"2","0"` 魔法数全部替换为命名常量，注释指向指南章节。

### 2.3 断言层（M2 + M3 双保险）

每个 shader entry 反射完成后，逐条断言，任一失败即 `sys.exit(1)`、FEB 不落盘：

| ID | 断言 | 拦截子类 |
|---|---|---|
| A1 | 类型 ↔ 寄存器类别一致（显式覆盖时）：`RWStructuredBuffer/RWTexture*` 必须 `u#`，`StructuredBuffer/Texture*/SamplerState` 必须 `t#/s#` | 类别用错 |
| A2 | 图形阶段 `StructuredBuffer` 不应占据 t0（与强制采样器 binding 0 冲突）；**依赖 M5' 类型感知扫描**——在此之前降为 WARN（当前 `scan_hlsl_registers` 只提取编号，无法区分 Texture2D 与 StructuredBuffer） | 编号撞车 |
| A3 | 派生参数表 ↔ `$Globals` 反射布局逐字节自洽（name hash + register + size） | HLSL ↔ JSON 漂移、MATRIX 占位算错 |
| A4 | HLSL 文本扫描的 u# 数 == 反射 `readwriteStorageBufferCount + readwriteStorageTextureCount` | **事故 2 的直接拦截器** |
| A5 | 反射到的每个资源 set ∈ 该 stage 合法集合（compute {0,1,2}，VS {0,1}，PS {2,3}） | 描述符集知识错 |
| A6 | compute entry 必须有 `[numthreads]`；graphics entry `uniforms ≤ 1`（当前驱动只推 slot 0） | 杂项 |

**反射端（M3）**：用 `spirv-cross --reflect` 替换约 100 行手写 `reflect_spirv`（事故 2 的直接来源）。

- 查找顺序：`PATH` → `SPIRV_CROSS` 环境变量指定的路径 → `~/.local/spirv-cross/bin/spirv-cross`（Linux 默认安装位置）；调用方式 `spirv-cross <file.spv> --reflect`（文件参数在 `--reflect` 之前），已验证 2021-01 版可用；
- 输出能力：`entryPoints[].workgroup_size` → threadCount；`ssbos[]` 含 `set`/`binding`/类型名（`type.RWStructuredBuffer.*` vs `type.StructuredBuffer.*`）；`ubos[]`、`separate_images[]`、`separate_samplers[]` 分类清晰；
- 读写判别**按描述符集**（compute set 1 = RW），类型名仅作二次校验（`type.RWStructuredBuffer.*` 应出现在 set 1，否则报错）——**不依赖 `NonWritable`/`NonReadable`**，这是 Phase 4 修法固化为代码；
- 保留手写解析作 fallback：`spirv-cross` 不在 PATH 时 WARN 降级，CI 环境不强依赖新工具；
- 风险：spirv-cross 版本较旧（2021），对新 SPIR-V 特性支持有限；当前着色器均为 SM6.0 基础特性，风险低。升级属环境事务，不在本设计范围。

### 2.4 转义层（兼容存量与调试）

- 默认自动生成 register；显式 `: register(u0)` 标注 = 工具只校验、不改写；
- 显式覆盖路径必须有至少一个测试用例，防止转义层退化；
- 存量 `.feb.json` 不强制迁移：保留显式 register 即按转义路径走，新建着色器推荐省略 register。

### 2.5 错误信息规范（强制）

每条断言失败必须输出四要素，否则该断言视为不合格：

```
[FEB-E203] register category mismatch
  file:   Shaders/Particle_cs.hlsl:14
  found:  RWStructuredBuffer<float4> Output : register(t0)
  expect: RWStructuredBuffer must use register(uN)
  fix:    change t0 → u0 (auto-assigned if register omitted)
```

错误码（`FEB-Exxx`）建一张表收进 `HLSL-FEB-DEVELOPMENT-GUIDE.md` §8，开发者第一次踩中即可自助。

### 2.6 I1 验收

- 构造反例覆盖 A1–A6 各一条 → 全部构建失败、错误信息含四要素；
- 仓库现存所有 `.feb.json` 重建零回归（diff 仅时间戳）；
- 显式 register 覆盖路径有至少一个测试用例。

---

## 3. I2：FEB 陈旧就加载不过

### 3.1 核心机制：游标核对（M4）

**不升 FEB_VERSION**——版本号防不住"格式没变但工具演进导致的布局差"（事故 1 的 header version 仍为 2 即是反例）；且升版本要求全量重建 + 双端同步，代价大。改为**结构性自校验**。

C 解析器（`FNA3D_Effect.c`）信任 header 偏移顺序读取的现状改为：解析 header 后、读 param 前增加守卫，**通用校验点选"读完后核对游标"**——能覆盖任何形式的条目漂移，不只 88→84：

```c
/* param 循环结束后核对游标 == 区域尺寸 */
if (paramReadOff != techOffset - paramOffset) {
    FNA3D_LogError("stale FEB: param region size mismatch "
                   "(read %u, expect %u) — rebuild with current feb_builder",
                   paramReadOff, techOffset - paramOffset);
    return 0;  /* CreateEffect 失败而非越界 */
}
/* pass（24B）与 shader（52B）区域无变长部分，可精确整除校验 */
```

带 annotation 的 param 区域无法用除法精确校验（每 annotation 40 字节变长），改用上述顺序读完后核对游标的通用路径。

FEB header 原有 3 个 reserved uint32（`pack("<16I", ..., total, 0, 0, 0)` 的末三位），builder fingerprint 占用其一，header 尺寸不变、既有偏移不动。

C# `Effect.INTERNAL_parseEffect` **无需独立核对**——它通过 P/Invoke 查询 C 层（`FNA3D_GetEffectParam` 等），不直接解析 FEB 二进制。游标核对在 C 一处生效，C# 端自动受益；若 C 层返回失败，C# 的 `FNA3D_CreateEffect` P/Invoke 得到 NULL，抛 `InvalidOperationException("stale FEB")`。

### 3.2 辅助机制

| 机制 | 作用 | 强度 |
|---|---|---|
| **builder fingerprint**：FEB header 末尾追加 `feb_builder` git short SHA（不升 version 号） | 加载时与运行时记录的 expected SHA 比对，不一致 → WARN（不 FAIL，避免 CI 误伤） | 软提示 |
| **参数表 ABI 摘要**：FEB 落盘时记录 `(nameHash, register, size)` 序列的 CRC | C# 端按名取参数失败时，错误信息列出当前 FEB 实际参数表 + 摘要 | 错误信息增强 |
| **SPIR-V magic 校验**：每个 shader blob 加载前验 `0x07230203` | 拦住 FEB 截断/损坏 | 硬拒绝 |

### 3.3 I2 验收

- 从 git 历史挖出 88 字节旧 param FEB → 加载得到明确 "stale FEB" 错误，**进程不崩**；
- 手工篡改任一区域的 size 字段 → 加载失败；
- 当前所有 FEB 加载零回归。

---

## 4. I3：验证层报错就测试不过

### 4.1 现状

`run_tests.sh` 第 104 行只 `grep -q "RESULT:.*PASS"`。验证层已开启（`Validation layers enabled` 出现在每个测试输出中），但 VUID 错误行被完全忽略——产生验证错误但未崩溃的用例照样报 PASS。事故 2 的 VUID-07988 就是这样被人工而非脚本发现的。

### 4.2 脚本改造（M1）

`run_tests.sh::test_proj()` 与 `run_tests.bat:::run_test` 同构改造（`findstr` 两次）：

```bash
# ─── test_proj() 内部改造 ───
test_proj() {
    local cat="$1" proj="$2"
    # ... build/ln 不变 ...

    # 1. 完整捕获输出到临时文件（不再 pipe 给 grep）
    local log; log=$(mktemp)
    dotnet run --no-build --project "$path" -- --headless > "$log" 2>&1

    # 2. 两级判定
    if ! grep -q "RESULT:.*PASS" "$log"; then
        rm -f "$log"; return 1                  # 现状：FAIL
    fi
    if grep -qE "VUID-|Validation Error|Assertion failure at SDL_GPU" "$log"; then
        if in_known_failures "$cat/$proj"; then
            echo "  => PASS(warn)"; rm -f "$log"; return 0
        fi
        echo "  => FAIL(validation):"
        grep -E "VUID-|Validation Error" "$log" | head -1 | sed 's/^/     /'
        rm -f "$log"; return 2                  # 新增：FAIL(validation)
    fi
    echo "  => PASS"; rm -f "$log"; return 0
}

# ─── 调用方区分 return 1 vs 2 ───
test_proj "$cat" "$proj"
rc=$?
if [ $rc -eq 0 ]; then
    PASS=$((PASS + 1))
else
    FAIL=$((FAIL + 1))
    FAILED_TESTS="$FAILED_TESTS $cat/$proj"
    [ $rc -eq 2 ] && VALIDATION_FAILS="$VALIDATION_FAILS $cat/$proj"
fi
```

**GuiDemo G01–G38**：现有代码是 inline 循环（不经 `test_proj()`），需同样改为先捕获到临时文件再两级判定。建议抽出 `run_headless_check()` 公共函数，`test_proj()` 与 GUI 循环共用。

**run_tests.bat**：`:run_test` 用 `exit /b N` 代替 `return N`；调用方当前不检查 errorlevel（`call :run_test ...` 后直接继续），需在每个 `call` 后加 `if !ERRORLEVEL! equ 2 set /a VALIDATION_FAILS+=1`。`findstr` 模式：`findstr /R "VUID- Validation.Error Assertion.failure" "%LOG%"`。

### 4.3 豁免清单（技术债显式化）

```bash
KNOWN_VALIDATION_FAILURES=(
    "StockEffect/BasicEffect"   # VUID-07904，REQ-effect-hlsl-vertex-convention.md 待实施
)
```

清单本身就是技术债台账——每修一项就删一行，PR 里看得见。**不允许**用 `2>/dev/null` 或 `|| true` 这种方式静默验证层。

之所以选豁免清单而非"先修 BasicEffect 再启用 M1"：避免 M1 被无限期阻塞；BasicEffect 的 VUID-07904 修复属 REQ 文档 C1–C5 范围，与本设计正交。

### 4.4 I3 验收

- 人为把任一 FEB 的 `num_samplers` 改错 → 对应测试 exit code 2、输出含首条 VUID；
- BasicEffect 在豁免清单内 → exit code 0、输出 `PASS(warn)`；
- 其余测试不受影响。

---

## 5. 实施顺序与依赖

```
Phase 1（一周内，零工具大改）
  M1  脚本消费验证层      ── I3 立即生效，事故 2 类故障当天就能拦住
  M4  加载期游标核对       ── I2 立即生效，事故 1 类故障当天就能拦住
       （C 改动需重编 FNA3D + 全量回归一次）

Phase 2（两周，feb_builder 集中改造）
  M2  构建期断言 A1–A6    ── I1 拒绝层就位
  M5' 派生层              ── I1 消除层就位（与 M2 同 PR，断言保护派生）
       依赖：M2 先于 M5' 合入，避免派生 bug 直接污染产物

Phase 3（一周，反射端替换）
  M3  spirv-cross 反射    ── I1 反射可靠性，与 A4 形成双保险
       依赖：M2 已就位（断言兜底，spirv-cross 不在 PATH 时降级）
```

**关键约束**：Phase 1 必须先做。理由是 M1/M4 改动小、收益快、且为后续 feb_builder 改造提供"安全网"——没有 I2/I3 兜底就动 I1，等于在没护栏的路上换轮胎。

措施与不变量映射（完整定义见 §1.4）：

| 措施 | 归属不变量 | 阶段 |
|---|---|---|
| M1 测试脚本消费验证层 | I3 | Phase 1 |
| M4 FEB 加载期游标核对 | I2 | Phase 1 |
| M2 feb_builder 构建期断言 | I1（拒绝层） | Phase 2 |
| M5' 描述符集 + parameters 单一事实源 | I1（消除层） | Phase 2 |
| M3 spirv-cross 替换手写反射 | I1（反射可靠性） | Phase 3 |

全部措施都不改变 FEB 二进制格式的**既有偏移与运行时行为**。builder fingerprint 占用 header 已有的 3 个 reserved uint32 之一（`feb_builder.py` 第 201 行 `pack("<16I", ..., total, 0, 0, 0)` 末三位），header 尺寸 64B 不变，旧解析器忽略该字段。对已通过的测试零影响（除 M1 会暴露 BasicEffect 的既有验证错误，用豁免清单处理）。

---

## 6. 非目标

- 不修 BasicEffect 的 VUID-07904（属 `REQ-effect-hlsl-vertex-convention.md` 范围）；
- 不修 SceneRenderer 的 draw 崩溃与数组参数（`LightData[64]`）缺 count 字段问题（已在 alignment-plan 文档记录为独立待办；I1 派生层会顺带让数组参数 count 不再可能漏，属附带收益，不列为目标）；
- 不引入运行时 SPIR-V 反射（驱动仍完全信任 FEB 元数据——I2 的职责是让不可信的 FEB 加载不了，而非在运行时兜底）；
- 不升 FEB_VERSION（游标核对 + builder fingerprint 是更本质的守卫，详见 §3.1）。

---

## 7. 完成态判定

三条不变量各自有"一句话验收"：

- **I1**：随便写一个错误的 HLSL，`feb_builder` 必然红字拒绝，错误信息能让人不查文档就改对；
- **I2**：随便从 git 历史挖一个旧 FEB，运行时必然报 "stale FEB"，进程不崩；
- **I3**：随便构造一个验证错误，`run_tests` 必然 exit 非零，首条 VUID 出现在输出里。

三条同时成立时，本设计文档关闭，`HLSL-FEB-DEVELOPMENT-GUIDE.md` §8 故障表中被 I1 覆盖的行（类别用错、编号撞车、HLSL ↔ JSON 漂移、MATRIX 占位、描述符集选错）删除——**文档变薄是体系生效的最直观证据**。
