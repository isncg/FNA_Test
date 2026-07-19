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
