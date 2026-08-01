#!/bin/bash
# run_tests.sh — Build and run all FNA_Test projects in headless mode.
# Usage: ./run_tests.sh
# For CI/headless environments: VK_LAYER_KHRONOS_validation=1 ./run_tests.sh
# NOTE: no `set -e` — test failures are handled explicitly via return codes.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
FNA_DIR="$SCRIPT_DIR/../FNA"
FEB_BUILDER="$SCRIPT_DIR/../FNA/tools/feb_builder.py"
FEB_SRC="$FNA_DIR/src/Graphics/Effect/StockEffects/FEB"
FNA3D_BUILD="$FNA_DIR/lib/FNA3D/build"

# ─── Step 1: Rebuild FNA3D (submodule) ──────────────────────────────────────
echo "=== Building FNA3D (submodule) ==="
ninja -C "$FNA3D_BUILD" 2>&1 | tail -1

# ─── Step 2: Rebuild stock FEBs ──────────────────────────────────────
echo "=== Rebuilding stock FEBs ==="
cd "$FEB_SRC"
for manifest in BasicEffect AlphaTestEffect DualTextureEffect SkinnedEffect SpriteEffect EnvironmentMapEffect; do
    echo -n "  ${manifest}... "
    python3 "$FEB_BUILDER" "${manifest}.feb.json" 2>&1 | head -1
done

# ─── Step 3: Build FNA ──────────────────────────────────────────────
echo "=== Building FNA ==="
dotnet build "$FNA_DIR/FNA.Core.csproj" 2>&1 | tail -1

# ─── Step 3.5: Build SDF font FEB and atlases ───────────────────────
echo "=== Building SDF font shader ==="
(cd "$SCRIPT_DIR/SDFFontTest/Shaders" && python3 "$FEB_BUILDER" SDFText.feb.json) 2>&1 | head -1

echo "=== Building StorageBuffer shader ==="
(cd "$SCRIPT_DIR/StorageBuffer/AsteroidField/Shaders" && python3 "$FEB_BUILDER" AsteroidField.feb.json) 2>&1 | head -1

echo "=== Building DepthSampling shader ==="
(cd "$SCRIPT_DIR/DepthSampling/Shaders" && python3 "$FEB_BUILDER" DepthQuad.feb.json) 2>&1 | head -1

echo "=== Building DepthTexture shaders ==="
(cd "$SCRIPT_DIR/DepthTexture/Shaders" && python3 "$FEB_BUILDER" DepthFill.feb.json && python3 "$FEB_BUILDER" DepthView.feb.json) 2>&1 | head -2

echo "=== Building SharedDepth shader ==="
(cd "$SCRIPT_DIR/SharedDepth/Shaders" && python3 "$FEB_BUILDER" Geometry.feb.json) 2>&1 | head -1

echo "=== Building ComputeDispatch shader ==="
(cd "$SCRIPT_DIR/ComputeDispatch/Shaders" && python3 "$FEB_BUILDER" Doubler.feb.json) 2>&1 | head -1

echo "=== Building SceneRenderer FEBs ==="
for feb_json in "$SCRIPT_DIR/SceneRenderer/Shaders"/*.feb.json; do
    (cd "$SCRIPT_DIR/SceneRenderer/Shaders" && python3 "$FEB_BUILDER" "$(basename "$feb_json")") 2>&1 | head -1
done

echo "=== Generating SDF font atlases (if needed) ==="
if [ ! -f "$SCRIPT_DIR/SDFFontTest/Fonts/en_atlas.png" ]; then
    echo "  Building English SDF atlas..."
    python3 "$SCRIPT_DIR/tools/sdf_font_builder.py" \
        /usr/share/fonts/liberation/LiberationSans-Regular.ttf \
        "$SCRIPT_DIR/tools/charset_en_ascii.txt" \
        160 2048 "$SCRIPT_DIR/SDFFontTest/Fonts/" en \
        --pxpadding 2
fi
if [ ! -f "$SCRIPT_DIR/SDFFontTest/Fonts/cn_atlas.png" ]; then
    echo "  Building CJK SDF atlas (GB2312 + Hangul, 9,795 glyphs)..."
    # Extract SC variant from Noto Sans CJK TTC (msdf-atlas-gen doesn't support .ttc)
    CJK_OTF=/tmp/NotoSansCJK_SC.otf
    if [ ! -f "$CJK_OTF" ]; then
        python3 -c "
from fontTools.ttLib import TTCollection
ttc = TTCollection('/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc')
ttc[2].save('$CJK_OTF')  # index 2 = SC
print('Extracted SC variant to $CJK_OTF')
"
    fi
    python3 "$SCRIPT_DIR/tools/sdf_font_builder.py" \
        "$CJK_OTF" \
        "$SCRIPT_DIR/tools/charset_cjk_common.txt" \
        34 4096 "$SCRIPT_DIR/SDFFontTest/Fonts/" cn \
        --pxrange 5 --pxpadding 2
fi

# ─── Step 4: Build and run all test projects ─────────────────────────
cd "$SCRIPT_DIR"
PASS=0
FAIL=0
FAILED_TESTS=""
VALIDATION_FAILS=""

# Known validation failures (technical debt registry — remove entries as fixed)
KNOWN_VALIDATION_FAILURES=(
    "StockEffect/BasicEffect"   # VUID-07904, see REQ-effect-hlsl-vertex-convention.md
)

in_known_failures() {
    local name="$1"
    for entry in "${KNOWN_VALIDATION_FAILURES[@]}"; do
        [ "$entry" = "$name" ] && return 0
    done
    return 1
}

# Two-level log verdict: PASS / PASS(warn) / FAIL(validation) / FAIL
# Usage: check_test_log <logfile> <display-name>
# Returns: 0=pass, 1=fail, 2=fail(validation)
check_test_log() {
    local log="$1" name="$2"
    if ! grep -q "RESULT:.*PASS" "$log"; then
        return 1
    fi
    if grep -qE "VUID-|Validation Error|Assertion failure at SDL_GPU" "$log"; then
        if in_known_failures "$name"; then
            echo "  => PASS(warn)"
            return 0
        fi
        echo "  => FAIL(validation):"
        grep -E "VUID-|Validation Error" "$log" | head -1 | sed 's/^/     /'
        return 2
    fi
    echo "  => PASS"
    return 0
}

test_proj() {
    local cat="$1" proj="$2"
    local path="$cat/$proj/$proj.csproj"
    local outdir="$cat/$proj/bin/Debug/net10.0"
    local dispname="$cat/$proj"

    echo "=== $dispname ==="
    dotnet build "$path" --nologo -clp:NoSummary 2>&1 | tail -1
    ln -sf "$FNA3D_BUILD/libFNA3D.so.27.0.0" "$outdir/libFNA3D.so"
    ln -sf "$FNA3D_BUILD/libFNA3D.so.27.0.0" "$outdir/libFNA3D.so.0"

    local log; log=$(mktemp)
    dotnet run --no-build --project "$path" -- --headless > "$log" 2>&1
    check_test_log "$log" "$dispname"
    local rc=$?
    rm -f "$log"
    return $rc
}

for proj in SpriteEffect BasicEffect AlphaTestEffect DualTextureEffect EnvironmentMapEffect BasicEffectMatrix SkinnedEffect; do
    test_proj "StockEffect" "$proj"; rc=$?
    if [ $rc -eq 0 ]; then PASS=$((PASS + 1)); else FAIL=$((FAIL + 1)); FAILED_TESTS="$FAILED_TESTS StockEffect/$proj"; [ $rc -eq 2 ] && VALIDATION_FAILS="$VALIDATION_FAILS StockEffect/$proj"; fi
done
for proj in ParticleFire; do
    test_proj "ComputeShaderEffect" "$proj"; rc=$?
    if [ $rc -eq 0 ]; then PASS=$((PASS + 1)); else FAIL=$((FAIL + 1)); FAILED_TESTS="$FAILED_TESTS ComputeShaderEffect/$proj"; [ $rc -eq 2 ] && VALIDATION_FAILS="$VALIDATION_FAILS ComputeShaderEffect/$proj"; fi
done
for proj in AsteroidField; do
    test_proj "StorageBuffer" "$proj"; rc=$?
    if [ $rc -eq 0 ]; then PASS=$((PASS + 1)); else FAIL=$((FAIL + 1)); FAILED_TESTS="$FAILED_TESTS StorageBuffer/$proj"; [ $rc -eq 2 ] && VALIDATION_FAILS="$VALIDATION_FAILS StorageBuffer/$proj"; fi
done
for proj in TrailEffect TrailEffectCapture; do
    test_proj "GPUInstancing" "$proj"; rc=$?
    if [ $rc -eq 0 ]; then PASS=$((PASS + 1)); else FAIL=$((FAIL + 1)); FAILED_TESTS="$FAILED_TESTS GPUInstancing/$proj"; [ $rc -eq 2 ] && VALIDATION_FAILS="$VALIDATION_FAILS GPUInstancing/$proj"; fi
done
for proj in JFAOutline SDFFontTest DepthSampling DepthTexture SharedDepth; do
    test_proj "." "$proj"; rc=$?
    if [ $rc -eq 0 ]; then PASS=$((PASS + 1)); else FAIL=$((FAIL + 1)); FAILED_TESTS="$FAILED_TESTS $proj"; [ $rc -eq 2 ] && VALIDATION_FAILS="$VALIDATION_FAILS $proj"; fi
done
for proj in ComputeDispatch; do
    test_proj "." "$proj"; rc=$?
    if [ $rc -eq 0 ]; then PASS=$((PASS + 1)); else FAIL=$((FAIL + 1)); FAILED_TESTS="$FAILED_TESTS $proj"; [ $rc -eq 2 ] && VALIDATION_FAILS="$VALIDATION_FAILS $proj"; fi
done

# SceneRenderer (deferred PBR pipeline)
test_proj "." "SceneRenderer"; rc=$?
if [ $rc -eq 0 ]; then PASS=$((PASS + 1)); else FAIL=$((FAIL + 1)); FAILED_TESTS="$FAILED_TESTS SceneRenderer"; [ $rc -eq 2 ] && VALIDATION_FAILS="$VALIDATION_FAILS SceneRenderer"; fi

# RTS tests (FNA_RTS Phase 1)
for proj in Camera2D PrimitiveLines IsometricTiles ScreenToWorld DepthSorting RectSelection; do
    test_proj "RTS" "$proj"; rc=$?
    if [ $rc -eq 0 ]; then PASS=$((PASS + 1)); else FAIL=$((FAIL + 1)); FAILED_TESTS="$FAILED_TESTS RTS/$proj"; [ $rc -eq 2 ] && VALIDATION_FAILS="$VALIDATION_FAILS RTS/$proj"; fi
done

# GUI panel tests (all 38, G01–G38)
echo "=== GuiDemo/Panel (G01–G38) ==="
dotnet build "GuiDemo/Panel/Panel.csproj" --nologo -clp:NoSummary 2>&1 | tail -1
PANEL_OUTDIR="GuiDemo/Panel/bin/Debug/net10.0"
ln -sf "$FNA3D_BUILD/libFNA3D.so.27.0.0" "$PANEL_OUTDIR/libFNA3D.so"
ln -sf "$FNA3D_BUILD/libFNA3D.so.27.0.0" "$PANEL_OUTDIR/libFNA3D.so.0"
GUI_PASS=0
GUI_FAIL=0
for t in $(seq -w 1 38); do
    test_name="G$t"
    gui_log=$(mktemp)
    dotnet run --no-build --project "GuiDemo/Panel/Panel.csproj" -- --headless --test "$test_name" > "$gui_log" 2>&1
    check_test_log "$gui_log" "GuiDemo/Panel/$test_name"
    rc=$?
    rm -f "$gui_log"
    if [ $rc -eq 0 ]; then
        GUI_PASS=$((GUI_PASS + 1))
    else
        GUI_FAIL=$((GUI_FAIL + 1))
        FAILED_TESTS="$FAILED_TESTS GuiDemo/Panel/$test_name"
        [ $rc -eq 2 ] && VALIDATION_FAILS="$VALIDATION_FAILS GuiDemo/Panel/$test_name"
    fi
done
echo "  GuiDemo/Panel: $GUI_PASS passed, $GUI_FAIL failed"
if [ $GUI_PASS -gt 0 ]; then PASS=$((PASS + GUI_PASS)); fi
if [ $GUI_FAIL -gt 0 ]; then FAIL=$((FAIL + GUI_FAIL)); fi

# ─── Step 5: Summary ─────────────────────────────────────────────────
echo ""
echo "========================================"
echo "  Results: $PASS passed, $FAIL failed"
if [ -n "$FAILED_TESTS" ]; then
    echo "  Failed:$FAILED_TESTS"
fi
if [ -n "$VALIDATION_FAILS" ]; then
    echo "  Validation failures:$VALIDATION_FAILS"
fi
echo "========================================"

if [ "$FAIL" -gt 0 ]; then
    exit 1
fi
exit 0
