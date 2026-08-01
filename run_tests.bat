@echo off
REM run_tests.bat — Build and run all FNA_Test projects on Windows.
REM Usage: run_tests.bat [--skip-fna3d] [--skip-feb] [--skip-fna] [--headless] [--help]
REM
REM Requires: CMake, Ninja, .NET 10 SDK, Python 3 (or py launcher), DXC, Git
REM SDL3 expected at C:\SDL3 (override via SDL3_DIR environment variable)
REM Supports both MSVC-prebuilt SDL3 (VC-x64) and source/MinGW SDL3 builds

setlocal enabledelayedexpansion

REM ─── Defaults ──────────────────────────────────────────────────────
set SKIP_FNA3D=0
set SKIP_FEB=0
set SKIP_FNA=0
set HEADLESS_ONLY=0

REM ─── Parse arguments ───────────────────────────────────────────────
:parse_args
if "%~1"=="" goto :args_done
if /i "%~1"=="--skip-fna3d"   set SKIP_FNA3D=1
if /i "%~1"=="--skip-feb"     set SKIP_FEB=1
if /i "%~1"=="--skip-fna"     set SKIP_FNA=1
if /i "%~1"=="--headless"     set HEADLESS_ONLY=1
if /i "%~1"=="--help"         goto :usage
shift
goto :parse_args

:usage
echo Usage: run_tests.bat [OPTIONS]
echo.
echo Options:
echo   --skip-fna3d    Skip FNA3D (C library) build
echo   --skip-feb      Skip FEB shader asset build
echo   --skip-fna      Skip FNA (C# library) build
echo   --headless      Only run tests in headless mode (no window)
echo   --help          Show this help message
echo.
echo SDL3 path: (default C:\SDL3, set SDL3_DIR to override)
exit /b 0

:args_done

REM ─── Paths ─────────────────────────────────────────────────────────
set SCRIPT_DIR=%~dp0
set FNA_DIR=%SCRIPT_DIR%..\FNA
set FEB_BUILDER=%FNA_DIR%\tools\feb_builder.py
set FEB_SRC=%FNA_DIR%\src\Graphics\Effect\StockEffects\HLSL_DXC
set FEB_DST=%FNA_DIR%\src\Graphics\Effect\StockEffects\FXB
set FNA3D_BUILD=%FNA_DIR%\lib\FNA3D\build

REM Python command (prefer %PYTHON%, then python.exe, then py launcher)
if not "%PYTHON%"=="" (
    set PYTHON_CMD=%PYTHON%
) else (
    where python >nul 2>&1
    if !ERRORLEVEL! equ 0 (
        set PYTHON_CMD=python
    ) else (
        where py >nul 2>&1
        if !ERRORLEVEL! equ 0 (
            set PYTHON_CMD=py
        ) else (
            echo [WARNING] Neither python.exe nor py.exe found. FEB build will fail.
            set PYTHON_CMD=python
        )
    )
)

REM SDL3 path (default C:\SDL3, override via environment variable)
if "%SDL3_DIR%"=="" set SDL3_DIR=C:\SDL3

REM Try the official prebuilt VC-x64 layout first
set SDL3_INC=%SDL3_DIR%\include
set SDL3_LIB=%SDL3_DIR%\lib\x64\SDL3.dll.lib
set SDL3_DLL=%SDL3_DIR%\lib\x64\SDL3.dll
set SDL3_CMAKE_MODE=EXPLICIT

REM Fall back to a source/MinGW build layout (e.g. D:\dev\SDL3 with build\)
if not exist "%SDL3_LIB%" (
    if exist "%SDL3_DIR%\build\libSDL3.dll.a" (
        set SDL3_LIB=%SDL3_DIR%\build\libSDL3.dll.a
        set SDL3_DLL=%SDL3_DIR%\build\SDL3.dll
        set SDL3_CMAKE_MODE=CONFIG
    )
)

REM DXC on PATH check
where dxc >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [WARNING] dxc.exe not found on PATH. FEB build will fail.
    echo           Install Vulkan SDK or add dxc.exe to PATH.
)

REM ─── Summary ───────────────────────────────────────────────────────
set PASS=0
set FAIL=0
set FAILED_TESTS=

echo ============================================
echo   FNA_Test Windows Test Runner
echo ============================================
echo.
echo   Script dir:  %SCRIPT_DIR%
echo   FNA dir:     %FNA_DIR%
echo   FNA3D build: %FNA3D_BUILD%
echo   SDL3 dir:    %SDL3_DIR%
echo.

REM ════════════════════════════════════════════════════════════════════
REM Step 1: Build FNA3D (C library)
REM ════════════════════════════════════════════════════════════════════
if %SKIP_FNA3D% equ 1 (
    echo [SKIP] Step 1: Build FNA3D
    goto :step1_done
)

echo === Step 1: Build FNA3D (C library) ===

REM Check if SDL3 directory exists
if "%SDL3_CMAKE_MODE%"=="EXPLICIT" (
    if not exist "%SDL3_INC%\SDL3\SDL.h" (
        echo [ERROR] SDL3 headers not found at %SDL3_INC%\SDL3
        echo         Download SDL3-devel-VC-x64.zip from https://github.com/libsdl-org/SDL/releases
        echo         and extract to C:\SDL3, or set SDL3_DIR environment variable.
        exit /b 1
    )
) else (
    if not exist "%SDL3_DIR%\build\SDL3Config.cmake" (
        echo [ERROR] SDL3 CMake config not found at %SDL3_DIR%\build\SDL3Config.cmake
        echo         Build SDL3 from source first, or set SDL3_DIR to a prebuilt SDL3 root.
        exit /b 1
    )
)

REM Configure if build directory doesn't exist
if not exist "%FNA3D_BUILD%\build.ninja" (
    echo   Configuring CMake...
    if "%SDL3_CMAKE_MODE%"=="EXPLICIT" (
        cmake -B "%FNA3D_BUILD%" -G Ninja "%FNA_DIR%\lib\FNA3D" ^
            -DCMAKE_BUILD_TYPE=Release ^
            -DSDL3_INCLUDE_DIRS=%SDL3_INC:\=/% ^
            -DSDL3_LIBRARIES=%SDL3_LIB:\=/%
    ) else (
        cmake -B "%FNA3D_BUILD%" -G Ninja "%FNA_DIR%\lib\FNA3D" ^
            -DCMAKE_BUILD_TYPE=Release ^
            -DSDL3_DIR=%SDL3_DIR:\=/%/build
    )
    if !ERRORLEVEL! neq 0 (
        echo [ERROR] CMake configure failed.
        exit /b 1
    )
)

echo   Building...
ninja -C "%FNA3D_BUILD%"
if %ERRORLEVEL% neq 0 (
    echo [ERROR] FNA3D build failed.
    exit /b 1
)
echo   Done: %FNA3D_BUILD%\FNA3D.dll

:step1_done
echo.

REM ════════════════════════════════════════════════════════════════════
REM Step 2: Build stock FEBs
REM ════════════════════════════════════════════════════════════════════
if %SKIP_FEB% equ 1 (
    echo [SKIP] Step 2: Build stock FEBs
    goto :step2_done
)

echo === Step 2: Build stock FEBs ===

if not exist "%FEB_SRC%" (
    echo [WARN] Stock effect HLSL source not found: %FEB_SRC%
    goto :step2_done
)

pushd "%FEB_SRC%"
for %%e in (BasicEffect AlphaTestEffect DualTextureEffect SkinnedEffect SpriteEffect EnvironmentMapEffect) do (
    echo   Building %%e...
    %PYTHON_CMD% "%FEB_BUILDER%" "%%e.feb.json" >nul 2>&1
    if !ERRORLEVEL! neq 0 (
        echo   [WARN] %%e FEB build failed
    ) else (
        echo   Done: %%e.feb
    )
)

REM Copy to FXB directory
if not exist "%FEB_DST%" mkdir "%FEB_DST%"
for %%f in (*.feb) do (
    copy /y "%%f" "%FEB_DST%\%%~nf.fxb" >nul
)
echo   Copied .feb -^> .fxb to FXB/
popd

:step2_done
echo.

REM ════════════════════════════════════════════════════════════════════
REM Step 3: Build FNA (C# library)
REM ════════════════════════════════════════════════════════════════════
if %SKIP_FNA% equ 1 (
    echo [SKIP] Step 3: Build FNA
    goto :step3_done
)

echo === Step 3: Build FNA (C# library) ===
dotnet build "%FNA_DIR%\FNA.Core.csproj" -nologo -clp:NoSummary
if %ERRORLEVEL% neq 0 (
    echo [ERROR] FNA build failed.
    exit /b 1
)
echo   Done.

:step3_done
echo.

REM ════════════════════════════════════════════════════════════════════
REM Step 3.5: Build additional FEBs (SDFFont, StorageBuffer, SceneRenderer)
REM ════════════════════════════════════════════════════════════════════
if %SKIP_FEB% equ 1 (
    echo [SKIP] Step 3.5: Build additional FEBs
    goto :step35_done
)

echo === Step 3.5: Build additional FEBs ===

REM SDF font shader
if exist "%SCRIPT_DIR%SDFFontTest\Shaders\SDFText.feb.json" (
    echo   Building SDF text shader FEB...
    pushd "%SCRIPT_DIR%SDFFontTest\Shaders"
    %PYTHON_CMD% "%FEB_BUILDER%" SDFText.feb.json >nul 2>&1
    if !ERRORLEVEL! neq 0 echo   [WARN] SDFText FEB build failed
    popd
)

REM StorageBuffer shader
if exist "%SCRIPT_DIR%StorageBuffer\AsteroidField\Shaders\AsteroidField.feb.json" (
    echo   Building StorageBuffer shader FEB...
    pushd "%SCRIPT_DIR%StorageBuffer\AsteroidField\Shaders"
    %PYTHON_CMD% "%FEB_BUILDER%" AsteroidField.feb.json >nul 2>&1
    if !ERRORLEVEL! neq 0 echo   [WARN] AsteroidField FEB build failed
    popd
)

REM DepthSampling shader (FNA3D UE5 alignment Phase 1)
if exist "%SCRIPT_DIR%DepthSampling\Shaders\DepthQuad.feb.json" (
    echo   Building DepthSampling shader FEB...
    pushd "%SCRIPT_DIR%DepthSampling\Shaders"
    %PYTHON_CMD% "%FEB_BUILDER%" DepthQuad.feb.json >nul 2>&1
    if !ERRORLEVEL! neq 0 echo   [WARN] DepthQuad FEB build failed
    popd
)

REM DepthTexture shaders (FNA3D UE5 alignment Phase 2)
if exist "%SCRIPT_DIR%DepthTexture\Shaders\DepthFill.feb.json" (
    echo   Building DepthTexture shader FEBs...
    pushd "%SCRIPT_DIR%DepthTexture\Shaders"
    %PYTHON_CMD% "%FEB_BUILDER%" DepthFill.feb.json >nul 2>&1
    if !ERRORLEVEL! neq 0 echo   [WARN] DepthFill FEB build failed
    %PYTHON_CMD% "%FEB_BUILDER%" DepthView.feb.json >nul 2>&1
    if !ERRORLEVEL! neq 0 echo   [WARN] DepthView FEB build failed
    popd
)

REM SharedDepth shader (FNA3D UE5 alignment Phase 3)
if exist "%SCRIPT_DIR%SharedDepth\Shaders\Geometry.feb.json" (
    echo   Building SharedDepth shader FEB...
    pushd "%SCRIPT_DIR%SharedDepth\Shaders"
    %PYTHON_CMD% "%FEB_BUILDER%" Geometry.feb.json >nul 2>&1
    if !ERRORLEVEL! neq 0 echo   [WARN] Geometry FEB build failed
    popd
)

REM ComputeDispatch shader (FNA3D UE5 alignment Phase 4)
if exist "%SCRIPT_DIR%ComputeDispatch\Shaders\Doubler.feb.json" (
    echo   Building ComputeDispatch shader FEB...
    pushd "%SCRIPT_DIR%ComputeDispatch\Shaders"
    %PYTHON_CMD% "%FEB_BUILDER%" Doubler.feb.json >nul 2>&1
    if !ERRORLEVEL! neq 0 echo   [WARN] Doubler FEB build failed
    popd
)

REM SceneRenderer shaders
if exist "%SCRIPT_DIR%SceneRenderer\Shaders\" (
    echo   Building SceneRenderer FEBs...
    pushd "%SCRIPT_DIR%SceneRenderer\Shaders"
    for %%f in (*.feb.json) do (
        %PYTHON_CMD% "%FEB_BUILDER%" "%%f" >nul 2>&1
        if !ERRORLEVEL! neq 0 echo   [WARN] %%f FEB build failed
    )
    popd
)

REM SDF font atlases - check if tools are available
if exist "%SCRIPT_DIR%tools\sdf_font_builder.py" (
    if not exist "%SCRIPT_DIR%SDFFontTest\Fonts\en_atlas.png" (
        echo   [INFO] SDF font atlas not found.
        echo          On Windows, SDF font atlases must be generated manually
        echo          or copied from a Linux build.
        echo          Required fonts: LiberationSans-Regular.ttf (en),
        echo          NotoSansCJK-Regular.ttc (cn).
        echo          See docs/windows-build-guide.md for details.
    )
)

:step35_done
echo.

REM ════════════════════════════════════════════════════════════════════
REM Step 4: Build and run all test projects
REM ════════════════════════════════════════════════════════════════════
echo === Step 4: Build and run tests ===
echo.

REM Helper: build, copy DLLs, and run one test
REM Usage: CALL :run_test <category> <project> [--extra-args]
REM   category="" for top-level projects
goto :run_tests_start

:run_test
set CAT=%~1
set PROJ=%~2
set EXTRA=%~3

if "%CAT%"=="" (
    set PROJPATH=%PROJ%\%PROJ%.csproj
    set OUTDIR=%PROJ%\bin\Debug\net10.0
    set DISPNAME=%PROJ%
) else (
    set PROJPATH=%CAT%\%PROJ%\%PROJ%.csproj
    set OUTDIR=%CAT%\%PROJ%\bin\Debug\net10.0
    set DISPNAME=%CAT%/%PROJ%
)

echo --- !DISPNAME! ---

REM Build
dotnet build "!PROJPATH!" --nologo -clp:NoSummary >nul 2>&1
if !ERRORLEVEL! neq 0 (
    echo   =^> BUILD FAIL
    set /a FAIL+=1
    if "!FAILED_TESTS!"=="" (set "FAILED_TESTS=!DISPNAME!(build)") else (set "FAILED_TESTS=!FAILED_TESTS! !DISPNAME!(build)")
    exit /b 1
)

REM Copy DLLs
if exist "%FNA3D_BUILD%\FNA3D.dll" (
    xcopy /d /y /q "%FNA3D_BUILD%\FNA3D.dll" "!OUTDIR!\" >nul 2>&1
)
if exist "%SDL3_DLL%" (
    xcopy /d /y /q "%SDL3_DLL%" "!OUTDIR!\" >nul 2>&1
)

REM Run (single invocation, pipe to findstr)
REM NOTE: Use `dotnet <dll>` directly instead of `dotnet run`. `dotnet run`
REM launches the app in a way that fails to resolve native SDL3.dll/FNA3D.dll
REM from the output dir (DllNotFoundException), while `dotnet <dll>` resolves
REM them correctly from the app base directory.
if "%HEADLESS_ONLY%"=="1" set EXTRA=--headless
dotnet "%OUTDIR%\%PROJ%.dll" %EXTRA% 2>&1 | findstr "RESULT:.*PASS" >nul
if !ERRORLEVEL! equ 0 (
    echo   =^> PASS
    set /a PASS+=1
    exit /b 0
) else (
    echo   =^> FAIL
    set /a FAIL+=1
    if "!FAILED_TESTS!"=="" (set FAILED_TESTS=!DISPNAME!) else (set FAILED_TESTS=!FAILED_TESTS! !DISPNAME!)
    exit /b 1
)

:run_tests_start

REM ─── StockEffect tests ─────────────────────────────────────────────
for %%p in (SpriteEffect BasicEffect AlphaTestEffect DualTextureEffect EnvironmentMapEffect BasicEffectMatrix SkinnedEffect) do (
    call :run_test StockEffect %%p
)

REM ─── ComputeShaderEffect tests ─────────────────────────────────────
call :run_test ComputeShaderEffect ParticleFire

REM ─── StorageBuffer tests ───────────────────────────────────────────
call :run_test StorageBuffer AsteroidField

REM ─── GPUInstancing tests ───────────────────────────────────────────
call :run_test GPUInstancing TrailEffect
call :run_test GPUInstancing TrailEffectCapture

REM ─── Top-level tests ───────────────────────────────────────────────
call :run_test "" JFAOutline
call :run_test "" SDFFontTest
call :run_test "" DepthSampling
call :run_test "" DepthTexture
call :run_test "" SharedDepth
call :run_test "" ComputeDispatch

REM ─── SceneRenderer (deferred PBR pipeline) ─────────────────────────
call :run_test "" SceneRenderer

REM ─── RTS tests ─────────────────────────────────────────────────────
for %%p in (Camera2D PrimitiveLines IsometricTiles ScreenToWorld DepthSorting RectSelection) do (
    call :run_test RTS %%p
)

REM ─── GUI panel tests (G01–G38) ─────────────────────────────────────
echo --- GuiDemo/Panel (G01-G38) ---
set PANEL_PROJ=GuiDemo\Panel\Panel.csproj
set PANEL_OUTDIR=GuiDemo\Panel\bin\Debug\net10.0

dotnet build "%PANEL_PROJ%" --nologo -clp:NoSummary >nul 2>&1
if !ERRORLEVEL! neq 0 (
    echo   =^> BUILD FAIL
    set /a FAIL+=38
    goto :gui_done
)

REM Copy DLLs
if exist "%FNA3D_BUILD%\FNA3D.dll" (
    xcopy /d /y /q "%FNA3D_BUILD%\FNA3D.dll" "%PANEL_OUTDIR%\" >nul 2>&1
)
if exist "%SDL3_DLL%" (
    xcopy /d /y /q "%SDL3_DLL%" "%PANEL_OUTDIR%\" >nul 2>&1
)

set GUI_PASS=0
set GUI_FAIL=0
for /L %%i in (1,1,38) do (
    set NUM=0%%i
    set NUM=!NUM:~-2!
    set TEST_NAME=G!NUM!

    dotnet "%PANEL_OUTDIR%\Panel.dll" --headless --test !TEST_NAME! 2>&1 | findstr "RESULT:.*PASS" >nul
    if !ERRORLEVEL! equ 0 (
        set /a GUI_PASS+=1
    ) else (
        set /a GUI_FAIL+=1
        set FAILED_TESTS=!FAILED_TESTS! GuiDemo/Panel/!TEST_NAME!
        echo   !TEST_NAME! =^> FAIL
    )
)
echo   GuiDemo/Panel: !GUI_PASS! passed, !GUI_FAIL! failed
set /a PASS+=%GUI_PASS%
set /a FAIL+=%GUI_FAIL%

:gui_done
echo.

REM ════════════════════════════════════════════════════════════════════
REM Summary
REM ════════════════════════════════════════════════════════════════════
echo ============================================
echo   Results: %PASS% passed, %FAIL% failed
if not "%FAILED_TESTS%"=="" (
    echo   Failed:%FAILED_TESTS%
)
echo ============================================

if %FAIL% gtr 0 exit /b 1
exit /b 0
