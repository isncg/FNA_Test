#!/bin/bash
# run_tests.sh — Build and run all FNA_Test projects in headless mode.
# Usage: ./run_tests.sh
# For CI/headless environments: VK_LAYER_KHRONOS_validation=1 ./run_tests.sh
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
FNA_DIR="$SCRIPT_DIR/../FNA"
FEB_BUILDER="$SCRIPT_DIR/../FNA/tools/feb_builder.py"
FEB_SRC="$FNA_DIR/src/Graphics/Effect/StockEffects/HLSL_DXC"
FEB_DST="$FNA_DIR/src/Graphics/Effect/StockEffects/FXB"
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
# Copy to FXB/
for f in *.feb; do
    base="${f%.feb}"
    cp "$f" "$FEB_DST/${base}.fxb"
done
echo "  Copied to FXB/"

# ─── Step 3: Build FNA ──────────────────────────────────────────────
echo "=== Building FNA ==="
dotnet build "$FNA_DIR/FNA.Core.csproj" 2>&1 | tail -1

# ─── Step 3.5: Build SDF font FEB and atlases ───────────────────────
echo "=== Building SDF font shader ==="
(cd "$SCRIPT_DIR/SDFFontTest/Shaders" && python3 "$FEB_BUILDER" SDFText.feb.json) 2>&1 | head -1

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

test_proj() {
    local cat="$1" proj="$2"
    local path="$cat/$proj/$proj.csproj"
    local outdir="$cat/$proj/bin/Debug/net10.0"

    echo "=== $cat/$proj ==="
    dotnet build "$path" --nologo -clp:NoSummary 2>&1 | tail -1
    ln -sf "$FNA3D_BUILD/libFNA3D.so.27.0.0" "$outdir/libFNA3D.so"
    ln -sf "$FNA3D_BUILD/libFNA3D.so.27.0.0" "$outdir/libFNA3D.so.0"

    if dotnet run --no-build --project "$path" -- --headless 2>&1 | grep -q "RESULT:.*PASS"; then
        echo "  => PASS"
        return 0
    else
        echo "  => FAIL"
        return 1
    fi
}

for proj in SpriteEffect BasicEffect AlphaTestEffect DualTextureEffect EnvironmentMapEffect BasicEffectMatrix SkinnedEffect; do
    if test_proj "StockEffect" "$proj"; then PASS=$((PASS + 1)); else FAIL=$((FAIL + 1)); FAILED_TESTS="$FAILED_TESTS StockEffect/$proj"; fi
done
for proj in ParticleFire; do
    if test_proj "ComputeShaderEffect" "$proj"; then PASS=$((PASS + 1)); else FAIL=$((FAIL + 1)); FAILED_TESTS="$FAILED_TESTS ComputeShaderEffect/$proj"; fi
done
for proj in TrailEffect TrailEffectCapture; do
    if test_proj "GPUInstancing" "$proj"; then PASS=$((PASS + 1)); else FAIL=$((FAIL + 1)); FAILED_TESTS="$FAILED_TESTS GPUInstancing/$proj"; fi
done
for proj in JFAOutline SDFFontTest; do
    if test_proj "." "$proj"; then PASS=$((PASS + 1)); else FAIL=$((FAIL + 1)); FAILED_TESTS="$FAILED_TESTS $proj"; fi
done

# ─── Step 5: Summary ─────────────────────────────────────────────────
echo ""
echo "========================================"
echo "  Results: $PASS passed, $FAIL failed"
if [ -n "$FAILED_TESTS" ]; then
    echo "  Failed:$FAILED_TESTS"
fi
echo "========================================"

if [ "$FAIL" -gt 0 ]; then
    exit 1
fi
exit 0
