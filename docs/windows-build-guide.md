# FNA + FNA3D_HLSL Windows 编译与调试指南

本文档提供在 Windows 操作系统上从零搭建、编译、运行和调试 FNA + FNA3D_HLSL 完整技术栈的详细步骤。

---

## 1. 项目概述与架构

### 1.1 仓库关系

```
FNA_Test/               ← 测试验证仓库（当前）
../FNA/                  ← FNA C# 库（XNA 4.0 重新实现），分支: hlsl
../FNA/lib/FNA3D/        ← FNA3D_HLSL 子模块：HLSL → DXC → SPIR-V 管线（C + CMake）
```

### 1.2 着色器管线

```
HLSL 源码 (.hlsl)
  → DXC -spirv -T vs_6_0 / -T ps_6_0 / -T cs_6_0
    → SPIR-V 二进制 (.spv)
      → feb_builder.py（读取 .feb.json 清单）
        → .feb 二进制（FNA3D Effect Binary）
          → FNA3D_CreateEffect()（运行时，FNA3D_HLSL）
            → SDL_GPU 渲染（Vulkan）
```

### 1.3 关键约束

- **仅 Vulkan**：FNA3D_HLSL 仅请求 SPIR-V 着色器格式，不存在 D3D11/D3D12/OpenGL/Metal 后端
- **HLSL 顶点约定**：严格遵守 C1–C5 约定（详见 `CLAUDE.md`）
- **无常量缓冲区更新 API**：FNA3D_HLSL 尚未实现 uniform/constant buffer 运行时更新
- **Color 格式**：`FNA3D_VERTEXELEMENTFORMAT_COLOR` 使用 BGRA 字节序（XNA 约定）

### 1.4 原生库加载机制

FNA 通过 `FNADllMap.cs` 模块初始化器解析 `app.config`（运行时文件名为 `FNA.dll.config`）中的 `<dllmap>` 条目，映射逻辑库名到平台相关的 DLL 文件名：

| 逻辑名 | Windows 目标 | Linux 目标 |
|--------|-------------|-----------|
| `SDL3` | `SDL3.dll` | `libSDL3.so.0` |
| `FNA3D` | `FNA3D.dll` | `libFNA3D.so.0` |
| `FAudio` | `FAudio.dll` | `libFAudio.so.0` |
| `dav1dfile` | `dav1dfile.dll` | `libdav1dfile.so.0` |

`NativeLibrary.SetDllImportResolver` 回调会在所有 `[DllImport]` 调用前生效，自动完成名称映射和加载。**确保 `FNA.dll.config` 与 `FNA.dll` 在同一目录下。**

---

## 2. 环境准备

### 2.1 必需组件

| 组件 | 版本要求 | 安装方式 |
|------|----------|----------|
| **.NET SDK** | 10.0 | https://dotnet.microsoft.com/download |
| **Visual Studio 2022** | 17.x | 安装时勾选 "Desktop development with C++" 工作负载 |
| **CMake** | 3.10+ | https://cmake.org/download/ （安装时选择 "Add CMake to system PATH"） |
| **Ninja** | 任意 | https://github.com/ninja-build/ninja/releases （下载 `ninja-win.zip`，解压 `ninja.exe` 到 PATH 目录） |
| **SDL3** | 3.2.0+ | https://github.com/libsdl-org/SDL/releases （下载 `SDL3-devel-<version>-VC-x64.zip`） |
| **DXC** | 1.x | 推荐随 [Vulkan SDK](https://vulkan.lunarg.com/) 安装（同时获得 Vulkan 调试层） |
| **Python** | 3.8+ | https://www.python.org/downloads/ （安装时勾选 "Add Python to PATH"） |
| **Git** | 任意 | https://git-scm.com/download/win |
| **Vulkan Runtime** | 1.3+ | GPU 厂商驱动自带 Vulkan 支持；[Vulkan SDK](https://vulkan.lunarg.com/) 提供额外的验证层 |

### 2.2 验证安装

打开命令提示符（`cmd.exe`），逐条验证：

```bat
dotnet --version
cmake --version
ninja --version
python --version
git --version
dxc --version
```

所有命令都应正常输出版本号。若提示"不是内部或外部命令"，检查该工具的 PATH 配置。

> **注意**：如果系统只安装了 Python 启动器（`py.exe`）而没有 `python.exe`，请使用 `py --version` 验证；后续脚本和命令中的 `python` 可替换为 `py`。

### 2.3 Python ply 模块

```bat
python -m pip install ply
```

如果系统只有 `py.exe` 启动器，则改为：

```bat
py -m pip install ply
```

### 2.4 SDL3 配置

#### 方案 A：官方预编译包（推荐）

1. 从 SDL GitHub Releases 下载 `SDL3-devel-<version>-VC-x64.zip`
2. 解压到固定目录，例如 `C:\SDL3`
3. 解压后的目录结构：
   ```
   C:\SDL3\
   ├── include\SDL3\        ← 头文件
   ├── lib\x64\SDL3.dll.lib ← 导入库
   └── lib\x64\SDL3.dll     ← 运行时 DLL（需要复制到测试输出目录）
   ```

#### 方案 B：源码 / MinGW 构建

如果你已经从源码构建了 SDL3（例如使用 MinGW），目录结构可能如下：

```
D:\dev\SDL3\
├── include\SDL3\           ← 头文件
└── build\
    ├── SDL3.dll            ← 运行时 DLL
    ├── libSDL3.dll.a       ← MinGW 导入库
    └── SDL3Config.cmake    ← CMake 包配置
```

此时可通过环境变量指定 SDL3 根目录，无需放到 `C:\SDL3`：

```bat
set SDL3_DIR=D:\dev\SDL3
```

> **注意**：必须选择 `x64` 版本。FNA_Test 所有项目面向 `AnyCPU`（实际运行取决于 `dotnet.exe` 的架构），现代 .NET 在 64 位 Windows 上默认为 x64。

### 2.5 可选：IDE 安装

| IDE | 用途 | 安装 |
|-----|------|------|
| **Visual Studio 2022** | 完整的 C# + C 混合调试 | 安装时勾选 ".NET desktop development" 和 "Desktop development with C++" |
| **VS Code** | 轻量级编辑器 | 安装 C# Dev Kit、CMake Tools、C/C++ 扩展 |
| **JetBrains Rider** | .NET 专用 IDE | https://www.jetbrains.com/rider/ |

---

## 3. 克隆与子模块初始化

```bat
git clone --recurse-submodules <仓库地址> FNA
cd FNA
git checkout hlsl
git submodule update --init --recursive
```

验证子模块状态：

```bat
git submodule status
```

应显示 `lib/FNA3D` 子模块已检出（无 `-` 前缀）。

---

## 4. 构建 FNA3D（C 原生库）

FNA3D 是 C 库，通过 CMake 构建。Windows 上推荐使用 Ninja generator（与 Linux 一致）。

### 4.1 配置

#### 使用官方预编译 SDL3（显式路径）

```bat
cd FNA\lib\FNA3D

cmake -B build -G Ninja . -DCMAKE_BUILD_TYPE=Release ^
  -DSDL3_INCLUDE_DIRS=C:/SDL3/include ^
  -DSDL3_LIBRARIES=C:/SDL3/lib/x64/SDL3.dll.lib
```

**参数说明**：

| 参数 | 说明 |
|------|------|
| `-G Ninja` | 使用 Ninja 构建系统 |
| `-DCMAKE_BUILD_TYPE=Release` | Release 模式优化 |
| `-DSDL3_INCLUDE_DIRS` | SDL3 头文件路径（使用正斜杠 `/`） |
| `-DSDL3_LIBRARIES` | SDL3 导入库完整路径 |

#### 使用源码 / MinGW 构建的 SDL3（CMake 包）

如果 SDL3 是通过 CMake 从源码构建的（例如 `D:\dev\SDL3\build` 目录下已有 `SDL3Config.cmake`），可以直接让 CMake 通过包配置来定位 SDL3：

```bat
cd FNA\lib\FNA3D

cmake -B build -G Ninja . -DCMAKE_BUILD_TYPE=Release ^
  -DSDL3_DIR=D:/dev/SDL3/build
```

> **提示**：MinGW 生成的导入库 `libSDL3.dll.a` 可以像 `SDL3.dll.lib` 一样直接传给 `SDL3_LIBRARIES`；使用 `SDL3_DIR` 方式时 CMake 会自动处理。

### 4.2 构建

```bat
ninja -C build
```

产出：`build\FNA3D.dll`

> **Windows 特有命名**：CMakeLists.txt 中设置了 `CMAKE_SHARED_LIBRARY_PREFIX=""`，因此输出 `FNA3D.dll` 而非 `libFNA3D.dll`。

### 4.3 首次构建建议：跳过 ImGui

如果只需快速验证基础功能，可禁用 Dear ImGui 集成（跳过 Python/Git 依赖）：

```bat
cmake -B build -G Ninja . -DCMAKE_BUILD_TYPE=Release ^
  -DSDL3_INCLUDE_DIRS=C:/SDL3/include ^
  -DSDL3_LIBRARIES=C:/SDL3/lib/x64/SDL3.dll.lib ^
  -DFNA3D_IMGUI=OFF
ninja -C build
```

后续需要 ImGui 时重新启用（默认 `ON`）并重新配置构建。

### 4.4 Debug 构建（调试用）

```bat
cmake -B build -G Ninja . -DCMAKE_BUILD_TYPE=Debug ^
  -DSDL3_INCLUDE_DIRS=C:/SDL3/include ^
  -DSDL3_LIBRARIES=C:/SDL3/lib/x64/SDL3.dll.lib
ninja -C build
```

产出 `FNA3D.dll` + `FNA3D.pdb`（调试符号），用于原生代码断点调试。

### 4.5 替代方案：Visual Studio Generator

如果未安装 Ninja，可用 Visual Studio 自带的 MSBuild generator：

```bat
cmake -B build -G "Visual Studio 17 2022" .
cmake --build build --config Release
```

### 4.6 编译器说明

- **MSVC**：不添加 `-std=gnu99` 等 GNU C 标志；使用 MSVC 默认 C 编译行为
- **MinGW**：CMake 会自动添加 `-static-libgcc`，避免运行时依赖单独的 libgcc DLL
- **C++ 组件 (ImGui)**：Dear ImGui 源文件使用 C++17 编译（仅当 `FNA3D_IMGUI=ON` 时）

---

## 5. 构建 FNA（C# 库）

```bat
cd FNA
dotnet build FNA.Core.csproj
```

产出：`bin\Debug\net10.0\FNA.dll`

### 5.1 关键配置文件

`FNA.dll.config`（即 `app.config`）必须与 `FNA.dll` 位于同一目录，否则 dllmap 映射不会生效，`DllImport` 解析将失败。

Dotnet build 会自动将 `app.config` 复制到输出目录并重命名为 `FNA.dll.config`。

### 5.2 可选依赖

`app.config` 还映射了 FAudio 和 dav1dfile：

| 库 | 用途 | 当前需求 |
|----|------|----------|
| `FAudio.dll` | 音频播放 | 当前测试不涉及音频，可暂不安装 |
| `dav1dfile.dll` | AV1 视频解码 | 当前测试不涉及视频，可暂不安装 |

---

## 6. 构建 FEB 着色器资源

FEB（FNA3D Effect Binary）是 FNA3D 的着色器格式。构建需要 `dxc.exe` 在 PATH 上。

### 6.1 确保 dxc 可用

```bat
where dxc
:: 应输出 dxc.exe 的完整路径
```

### 6.2 构建股票特效 FEB

```bat
cd FNA\src\Graphics\Effect\StockEffects\FEB

for %f in (BasicEffect AlphaTestEffect DualTextureEffect SkinnedEffect SpriteEffect EnvironmentMapEffect) do (
    python ..\..\..\..\..\tools\feb_builder.py %f.feb.json
)

:: 复制到 FXB 目录
copy *.feb ..\FXB\
rename ..\FXB\*.feb *.fxb
```

> **注意**：`feb_builder.py` 路径为 `../FNA/tools/feb_builder.py`，`dxc.exe` 需在 PATH 上。如果系统只有 `py.exe`，将上面的 `python` 替换为 `py`。

### 6.3 构建测试项目 FEB

```bat
cd FNA_Test

:: SDF 字体着色器
cd SDFFontTest\Shaders
python ..\..\..\FNA\tools\feb_builder.py SDFText.feb.json
cd ..\..

:: StorageBuffer 着色器
cd StorageBuffer\AsteroidField\Shaders
python ..\..\..\..\FNA\tools\feb_builder.py AsteroidField.feb.json
cd ..\..

:: SceneRenderer 着色器（全部 .feb.json 清单）
cd SceneRenderer\Shaders
for %f in (*.feb.json) do python ..\..\..\FNA\tools\feb_builder.py %f
cd ..\..
```

> **提示**：如果系统只有 `py.exe`，将命令中的 `python` 替换为 `py`。

---

## 7. IDE 配置详解

### 7.1 Visual Studio 2022

Visual Studio 2022 提供最完整的 C# + 原生 C 混合调试体验，是 Windows 平台的首选 IDE。

#### 7.1.1 打开项目

- **方法 A**（推荐）：启动 VS 2022 → "Open a local folder" → 选择 `FNA_Test` 目录
- **方法 B**：直接在 FNA_Test 目录下运行 `start FNA_Test.sln`（如果存在），或通过 "File → Open → Folder"

VS 自动识别：
- **CMake 项目**（`../FNA/lib/FNA3D/CMakeLists.txt`）→ 显示在 Solution Explorer 的 "CMake Targets View"
- **.NET 项目**（所有 `.csproj`）→ 显示在 "Solution Explorer" 中

#### 7.1.2 配置 CMake（FNA3D 构建）

1. 在 Solution Explorer 顶部的下拉菜单切换到 "CMake Targets View"
2. 右键 `CMakeLists.txt` → "CMake Settings"
3. 在 `CMakeSettings.json` 中添加 SDL3 变量：

```json
{
  "configurations": [
    {
      "name": "x64-Debug",
      "generator": "Ninja",
      "configurationType": "Debug",
      "buildRoot": "${projectDir}\\build",
      "installRoot": "${projectDir}\\install",
      "cmakeCommandArgs": "",
      "variables": [
        {
          "name": "SDL3_INCLUDE_DIRS",
          "value": "C:/SDL3/include",
          "type": "PATH"
        },
        {
          "name": "SDL3_LIBRARIES",
          "value": "C:/SDL3/lib/x64/SDL3.dll.lib",
          "type": "FILEPATH"
        }
      ]
    },
    {
      "name": "x64-Release",
      "generator": "Ninja",
      "configurationType": "Release",
      "buildRoot": "${projectDir}\\build",
      "variables": [
        {
          "name": "SDL3_INCLUDE_DIRS",
          "value": "C:/SDL3/include",
          "type": "PATH"
        },
        {
          "name": "SDL3_LIBRARIES",
          "value": "C:/SDL3/lib/x64/SDL3.dll.lib",
          "type": "FILEPATH"
        }
      ]
    }
  ]
}
```

4. 菜单栏 "Build → Build All" 或右键 `FNA3D` 目标 → "Build"

#### 7.1.3 C# 项目调试

1. 在 Solution Explorer 中右键某个测试 `.csproj`（如 `StockEffect\BasicEffect\BasicEffect.csproj`）
2. 选择 "Set as Startup Item"
3. 在 `launchSettings.json` 中配置（置于项目 `Properties\` 目录）：

```json
{
  "profiles": {
    "BasicEffect (Debug)": {
      "commandName": "Project",
      "commandLineArgs": "",
      "nativeDebugging": true,
      "environmentVariables": {
        "VK_LAYER_KHRONOS_validation": "1"
      }
    },
    "BasicEffect (Headless)": {
      "commandName": "Project",
      "commandLineArgs": "--headless",
      "nativeDebugging": false,
      "environmentVariables": {
        "FNA_TEST_HEADLESS": "1"
      }
    }
  }
}
```

4. 按 `F5` 启动调试

#### 7.1.4 混合调试（C# + 原生 C）

VS 2022 支持在同一调试会话中同时调试 C# 托管代码和 C 原生代码：

1. **前置条件**：FNA3D 必须以 `Debug` 配置构建（生成 `FNA3D.pdb`）
2. 在 `launchSettings.json` 的对应 profile 中设置 `"nativeDebugging": true`
3. 在 C 源文件（如 `FNA3D_Driver_SDL.c`、`FNA3D_Effect.c`）中设置断点
4. 按 `F5` 启动 — C# 和 C 断点将在同一会话中命中
5. 调试时可以在 "Call Stack" 窗口中看到托管→原生的完整调用链

> **提示**：断点能在原生代码中生效的前提是 `.pdb` 文件与 `.dll` 在同一目录，且源码路径可访问（本地开发自然满足）。

#### 7.1.5 推荐 launchSettings.json 位置

在测试项目的目录下创建 `Properties\launchSettings.json`。例如 `StockEffect\BasicEffect\Properties\launchSettings.json`。

---

### 7.2 VS Code

#### 7.2.1 必需扩展

在 VS Code 中安装以下扩展（`Ctrl+Shift+X`）：

| 扩展 | 发布者 | 用途 |
|------|--------|------|
| C# Dev Kit | Microsoft | C# 语言服务、调试、测试 |
| CMake Tools | Microsoft | CMake 项目配置和构建 |
| C/C++ | Microsoft | 原生语言支持、调试 |

#### 7.2.2 工作区配置

在 `FNA_Test\.vscode\settings.json` 中配置 CMake 变量：

```json
{
  "cmake.configureSettings": {
    "SDL3_INCLUDE_DIRS": "C:/SDL3/include",
    "SDL3_LIBRARIES": "C:/SDL3/lib/x64/SDL3.dll.lib"
  },
  "cmake.sourceDirectory": "../FNA/lib/FNA3D",
  "cmake.buildDirectory": "${workspaceFolder}/../FNA/lib/FNA3D/build",
  "cmake.generator": "Ninja"
}
```

#### 7.2.3 构建 FNA3D

1. `Ctrl+Shift+P` → "CMake: Configure"
2. 选择 kit：`Visual Studio Community 2022 Release - amd64`
3. `Ctrl+Shift+P` → "CMake: Build"

#### 7.2.4 C# 调试配置

在 `.vscode\launch.json` 中：

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Debug BasicEffect",
      "type": "coreclr",
      "request": "launch",
      "program": "${workspaceFolder}/StockEffect/BasicEffect/bin/Debug/net10.0/BasicEffect.dll",
      "cwd": "${workspaceFolder}/StockEffect/BasicEffect",
      "args": [],
      "env": {
        "VK_LAYER_KHRONOS_validation": "1"
      }
    },
    {
      "name": "Run BasicEffect (Headless)",
      "type": "coreclr",
      "request": "launch",
      "program": "${workspaceFolder}/StockEffect/BasicEffect/bin/Debug/net10.0/BasicEffect.dll",
      "cwd": "${workspaceFolder}/StockEffect/BasicEffect",
      "args": ["--headless"],
      "env": {
        "FNA_TEST_HEADLESS": "1"
      }
    }
  ]
}
```

#### 7.2.5 tasks.json（构建快捷方式）

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "build FNA3D",
      "type": "shell",
      "command": "cmake",
      "args": ["--build", "../FNA/lib/FNA3D/build", "--config", "Release"],
      "group": "build",
      "problemMatcher": []
    },
    {
      "label": "build FNA",
      "type": "shell",
      "command": "dotnet",
      "args": ["build", "../FNA/FNA.Core.csproj"],
      "group": "build",
      "problemMatcher": "$msCompile"
    },
    {
      "label": "build current test",
      "type": "shell",
      "command": "dotnet",
      "args": ["build", "${relativeFileDirname}"],
      "group": "build",
      "problemMatcher": "$msCompile"
    },
    {
      "label": "full build",
      "dependsOn": ["build FNA3D", "build FNA"],
      "group": "build"
    }
  ]
}
```

#### 7.2.6 混合调试（C# + 原生 C）

VS Code 的 C# 和 C++ 混合调试不如 Visual Studio 流畅。两种方案：

**方案 A：分别 Debug**
1. 先用 C# 配置启动，调试托管代码
2. 需要调试原生代码时，用 C++ 配置附加到 `dotnet.exe` 进程

**方案 B：配置 sourceFileMap**
在 `launch.json` 中为 C++ 配置添加 `"sourceFileMap"`，将编译时的源码路径映射到本地：

```json
{
  "name": "Attach C++ to dotnet",
  "type": "cppvsdbg",
  "request": "attach",
  "processId": "${command:pickProcess}",
  "sourceFileMap": {
    "/home/user/FNA/lib/FNA3D/src": "${workspaceFolder}/../FNA/lib/FNA3D/src"
  }
}
```

> **建议**：优先使用 Visual Studio 2022 进行混合调试。VS Code 适合纯 C# 开发和轻量编辑场景。

---

### 7.3 JetBrains Rider

#### 7.3.1 打开项目

1. 启动 Rider → "Open" → 选择 `FNA_Test` 文件夹
2. Rider 自动识别所有 `.csproj` 文件并构建 Solution 视图
3. 通过 `<ProjectReference>` 自动解析并加载 `../FNA/FNA.Core.csproj` 源码

#### 7.3.2 C# 调试

1. 在 Solution Explorer 中右键某个测试项目
2. "Debug" → 选择目标框架 (`net10.0`)
3. Rider 内置 .NET 调试器自动启动

配置 Run/Debug Configuration：

```
Run → Edit Configurations → 添加 ".NET Project"
  - Project: StockEffect/BasicEffect/BasicEffect.csproj
  - Program arguments: --headless
  - Environment variables: VK_LAYER_KHRONOS_validation=1
```

#### 7.3.3 原生 C 调试

Rider 支持附加到运行中的进程进行原生调试：

1. **前置条件**：FNA3D 以 `Debug` 或 `RelWithDebInfo` 配置构建（生成 `.pdb`）
2. 启动测试程序（带断点等待或 `Console.ReadLine()`）
3. Rider 菜单：Run → "Attach to Process" → 选择 `dotnet.exe`
4. 在 Attach 对话框中选择 "Native (.NET Core)" 作为调试器类型
5. 在 C 源文件中设置断点

**符号路径配置**：

Settings → Build, Execution, Deployment → Native Debugging → Symbol Servers / Paths，添加 FNA3D 构建目录。

#### 7.3.4 CMake 集成

Rider 的 CMake 支持不如 CLion 强大。推荐工作流：

1. **终端构建 FNA3D**：通过 Rider 内置终端（`Alt+F12`）运行 cmake/ninja 命令
2. **Rider 中调试 C#**：FNA3D.dll 构建完成后，在 Rider 中 F5 启动 C# 测试
3. 需要原生调试时使用 "Attach to Process"

> **提示**：Rider + 终端手动构建的组合在不需要频繁修改 C 源码的场景下非常高效。如果需要深度调试原生代码，建议切换到 Visual Studio 2022。

---

## 8. 运行测试

### 8.1 关键准备：复制 DLL

测试项目通过 `<ProjectReference>` 引用 FNA，但**不会自动复制原生 DLL**。每个测试的运行时目录 `<test>\bin\Debug\net10.0\` 需要 `FNA3D.dll` 和 `SDL3.dll`。

**手动复制（单个测试）**：

```bat
copy FNA\lib\FNA3D\build\FNA3D.dll StockEffect\BasicEffect\bin\Debug\net10.0\
copy C:\SDL3\lib\x64\SDL3.dll StockEffect\BasicEffect\bin\Debug\net10.0\
```

> **对比 Linux**：Linux 上通过 `ln -sf` 创建符号链接，Windows 上需要直接复制 DLL。

### 8.2 命令行运行单个测试

```bat
dotnet run --project StockEffect\BasicEffect\BasicEffect.csproj
```

### 8.3 无头模式（自动化断言）

```bat
dotnet run --project StockEffect\BasicEffect\BasicEffect.csproj -- --headless
```

预期输出含 `RESULT:.*PASS`。退出码为 0 表示通过，非 0 表示失败。

### 8.4 从 IDE 运行

- **Visual Studio**：右键测试项目 → "Debug" / "Start Without Debugging"
- **VS Code**：选择对应的 launch configuration → `F5`
- **Rider**：选择 Run/Debug Configuration → 点击运行

### 8.5 从 IDE 运行无头模式

在 IDE 的运行配置中设置命令行参数 `--headless`，或设置环境变量 `FNA_TEST_HEADLESS=1`。

### 8.6 环境变量

| 变量 | 用途 |
|------|------|
| `VK_LAYER_KHRONOS_validation=1` | 启用 Vulkan 验证层 |
| `FNA_TEST_HEADLESS=1` | 强制无头模式 |
| `SDL_GPU_DEBUG=1` | SDL_GPU 调试输出 |

---

## 9. 调试技术与工具

### 9.1 C# 托管代码调试

在 IDE 中对 C# 源码设置断点即可：
- Effect 类（`BasicEffect.cs`、`SpriteEffect.cs` 等）
- 测试 `Program.cs`
- `TestHarness.cs` 断言逻辑

### 9.2 C 原生代码调试（FNA3D.dll）

**前置条件**：
1. FNA3D 必须以 `Debug` 配置构建（生成 `FNA3D.pdb`）
2. `.pdb` 文件与 `FNA3D.dll` 在同一目录
3. 测试程序运行时加载的是 Debug 版本的 DLL

**Visual Studio 2022**（推荐）：
- 在 `launchSettings.json` 中启用 `"nativeDebugging": true`
- 在 C 源文件中设断点 → F5 → 同时命中 C# 和 C 断点

**VS Code**：
- 使用 "cppvsdbg" 配置附加到 `dotnet.exe` 进程
- 或配置 `"sourceFileMap"` 映射源码路径

### 9.3 Vulkan 验证层

1. 安装 [Vulkan SDK](https://vulkan.lunarg.com/)
2. 设置环境变量：`VK_LAYER_KHRONOS_validation=1`
3. SDK 安装后默认包含验证层（`VkLayer_khronos_validation.json` 在 `VK_LAYER_PATH` 中）

验证输出会显示 Vulkan API 的误用和性能警告。在 VS 中，输出会显示在 Output 窗口。

### 9.4 SDL_GPU 调试模式

修改 FNA3D 调用处，传入 `debugMode=1`：

```c
FNA3D_CreateDeviceResult result = FNA3D_CreateDevice(&params, 1);  // debugMode=1
```

enable 后 SDL_GPU 会输出额外的诊断信息。

### 9.5 RenderDoc（图形调试器）

RenderDoc 支持 Windows Vulkan 捕获：

1. 下载安装 [RenderDoc](https://renderdoc.org/)
2. 启动 RenderDoc
3. "Launch Application" 设置：
   - Executable: `C:\Program Files\dotnet\dotnet.exe`（或 `where dotnet` 的结果）
   - Arguments: `run --project D:\path\to\FNA_Test\StockEffect\BasicEffect\BasicEffect.csproj`
   - Working Directory: `D:\path\to\FNA_Test`
4. 点击 "Launch" → 在 RenderDoc 中按 `F12` 捕获帧
5. 查看 Draw Call、Pipeline State、Shader 调试

### 9.6 FNA3D_HookLogFunctions（捕获原生日志）

FNA3D C 库通过以下函数输出日志：
- `FNA3D_LogInfo(const char *fmt, ...)`
- `FNA3D_LogWarn(const char *fmt, ...)`
- `FNA3D_LogError(const char *fmt, ...)`

可以注册回调将这些日志输出到 IDE 的 Output/Debug 窗口：

```c
// 在 FNA3D 初始化时注册
FNA3D_HookLogFunctions(my_log_callback);
```

在 C# 侧可以通过 P/Invoke 注册托管回调，将日志输出到 `Debug.WriteLine` 或 `Console.WriteLine`。

---

## 10. 常见问题排查

### 10.1 `DllNotFoundException: Unable to load DLL 'FNA3D'`

**原因**：`FNA3D.dll` 不在测试程序的输出目录中，也不在系统 PATH 中。

**解决**：
```bat
copy FNA\lib\FNA3D\build\FNA3D.dll <test>\bin\Debug\net10.0\
```

或检查 `FNA.dll.config` 是否在 `FNA.dll` 同目录下。

### 10.2 `DllNotFoundException: Unable to load DLL 'SDL3'`

**原因**：`SDL3.dll` 缺失。

**解决**：
```bat
copy C:\SDL3\lib\x64\SDL3.dll <test>\bin\Debug\net10.0\
```

### 10.3 设备创建失败 / 黑屏

**原因**：系统没有 Vulkan 驱动或 ICD。

**排查**：
1. 确认 GPU 支持 Vulkan（NVIDIA GTX 600+、AMD GCN 1+、Intel Skylake+）
2. 安装最新 GPU 驱动
3. 安装 Vulkan SDK（提供 `vulkaninfo.exe` 工具验证）
4. 运行 `vulkaninfo` 确认有可用的 Vulkan 设备

### 10.4 `dxc` 未找到

**错误**：运行 `feb_builder.py` 时报错找不到 `dxc`。

**解决**：
1. 安装 Vulkan SDK（推荐，包含 `dxc.exe` 和 Vulkan 工具）
2. 将 SDK 的 `Bin` 目录加入系统 PATH
3. 验证：`dxc --version`

> 也可以从 Windows SDK 或 GitHub 上的 [DirectXShaderCompiler](https://github.com/microsoft/DirectXShaderCompiler) releases 获取独立版本。

### 10.5 "Failed to apply imgui patch"

**原因**：Git 未安装或子模块未初始化。

**解决**：
1. 安装 Git for Windows
2. 运行 `git submodule update --init --recursive`
3. 或临时禁用 ImGui：`-DFNA3D_IMGUI=OFF`

### 10.6 Python `ply` 模块缺失

**错误**：`ModuleNotFoundError: No module named 'ply'`

**解决**：
```bat
pip install ply
```

### 10.7 CMake 找不到 SDL3

**错误**：CMake 配置时提示找不到 SDL3。

**解决**：显式指定路径变量，不要依赖 `find_package`：
```bat
cmake -B build -G Ninja . ^
  -DSDL3_INCLUDE_DIRS=C:/SDL3/include ^
  -DSDL3_LIBRARIES=C:/SDL3/lib/x64/SDL3.dll.lib
```

### 10.8 MSVC 链接错误

**错误**：`LNK1112: module machine type 'x86' conflicts with target machine type 'x64'`

**解决**：
1. 确保 SDL3 下载的是 `VC-x64` 版本（非 x86）
2. 确保 CMake 选择的是 x64 工具链（Ninja 默认跟随 VS 的 x64 命令提示符环境）
3. 从 "x64 Native Tools Command Prompt for VS 2022" 中运行构建命令

### 10.9 FEB 加载失败

**错误**：`FNA3D_CreateEffect` 返回 NULL 或测试渲染结果不正确。

**排查**：
1. 检查 `dxc` 版本是否兼容（推荐 Vulkan SDK 1.10+ 附带的版本）
2. 确认 HLSL 源码语法正确（无未定义的变量或错误的寄存器绑定）
3. 检查 `.feb.json` 清单中的入口点名称和着色器文件路径

### 10.10 "SPIR-V not supported"

**错误**：运行时报错 SPIR-V 着色器格式不被支持。

**原因**：FNA3D_HLSL 仅支持 Vulkan 后端，不支持 D3D。确认系统有可用的 Vulkan ICD。

---

## 11. 自动化测试脚本（`run_tests.bat`）

仓库根目录提供了 `run_tests.bat` 脚本，一键完成完整的构建和测试流程：

```bat
run_tests.bat
```

脚本会自动检测 Python（优先 `python.exe`，回退到 `py.exe`），并支持通过环境变量 `SDL3_DIR` 指定非默认的 SDL3 路径。当前环境示例：

```bat
set SDL3_DIR=D:\dev\SDL3
run_tests.bat --headless
```

### 11.1 脚本流程

1. 构建 FNA3D（C 库）
2. 构建所有 FEB 着色器资源
3. 构建 FNA（C# 库）
4. 复制 DLL 到所有测试输出目录
5. 运行全部测试（~58 个），输出 PASS/FAIL 统计

### 11.2 参数

| 参数 | 说明 |
|------|------|
| `--skip-fna3d` | 跳过 FNA3D 构建 |
| `--skip-feb` | 跳过 FEB 构建 |
| `--skip-fna` | 跳过 FNA 构建 |
| `--headless` | 仅无头模式运行 |
| `--help` | 显示帮助 |

### 11.3 示例

```bat
:: 完整构建和测试
run_tests.bat

:: 跳过所有构建，仅运行测试（假设已手动构建）
run_tests.bat --skip-fna3d --skip-feb --skip-fna

:: 仅运行无头测试
run_tests.bat --headless

:: 使用自定义 SDL3 目录并跳过构建
set SDL3_DIR=D:\dev\SDL3
run_tests.bat --skip-fna3d --skip-feb --skip-fna --headless
```

---

## 12. 附录：快速参考

### 12.1 关键文件路径

| 用途 | 路径 |
|------|------|
| FNA3D 源码 | `FNA\lib\FNA3D\` |
| FNA3D DLL 输出 | `FNA\lib\FNA3D\build\FNA3D.dll` |
| FNA 源码 | `FNA\src\` |
| FNA DLL 输出 | `FNA\bin\Debug\net10.0\FNA.dll` |
| 股票特效 HLSL | `FNA\src\Graphics\Effect\StockEffects\FEB\` |
| FEB 构建工具 | `FNA\tools\feb_builder.py` |
| 测试项目 | `FNA_Test\StockEffect\` `FNA_Test\ComputeShaderEffect\` 等 |
| DllMap 配置 | `FNA\app.config`（运行时 `FNA.dll.config`） |

### 12.2 环境变量速查

| 变量 | 值 | 效果 |
|------|-----|------|
| `VK_LAYER_KHRONOS_validation` | `1` | 启用 Vulkan 验证层 |
| `FNA_TEST_HEADLESS` | `1` | 强制无头模式 |
| `SDL_GPU_DEBUG` | `1` | SDL_GPU 调试输出 |

### 12.3 CMake 参数速查

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `BUILD_SHARED_LIBS` | `ON` | 构建动态库 |
| `FNA3D_IMGUI` | `ON` | 编译 Dear ImGui 集成（需要 Python3 + Git） |
| `FNA3D_IMGUIZMO` | `ON` | 编译 ImGuizmo 扩展（需要 `FNA3D_IMGUI=ON`） |
| `CMAKE_BUILD_TYPE` | `Release` | 可选: `Debug`, `Release`, `RelWithDebInfo` |

### 12.4 FNA .csproj 变体

| 文件 | 目标框架 | 用途 |
|------|----------|------|
| `FNA.Core.csproj` | `net10.0` | 现代 .NET（测试项目使用） |
| `FNA.NetStandard.csproj` | `netstandard2.0` | .NET Standard 兼容 |
| `FNA.NetFramework.csproj` | `net4.0` | .NET Framework 兼容 |

---

## 参考链接

- [FNA 官方文档](https://fna-xna.github.io/docs/)
- [SDL 官方仓库](https://github.com/libsdl-org/SDL)
- [Vulkan SDK](https://vulkan.lunarg.com/)
- [DirectXShaderCompiler](https://github.com/microsoft/DirectXShaderCompiler)
- [RenderDoc](https://renderdoc.org/)
- [FNA3D_HLSL 架构说明](../FNA/lib/FNA3D/CLAUDE.md)
