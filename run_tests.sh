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
FNA3D_BUILD="$SCRIPT_DIR/../FNA3D_HLSL/build"

# ─── Step 1: Rebuild FNA3D_HLSL ──────────────────────────────────────
echo "=== Building FNA3D_HLSL ==="
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

for proj in SpriteEffect BasicEffect AlphaTestEffect DualTextureEffect EnvironmentMapEffect BasicEffectMatrix SkinnedEffect; do
    echo "=== $proj ==="
    dotnet build "$proj/$proj.csproj" --nologo -clp:NoSummary 2>&1 | tail -1

    # Ensure libFNA3D symlink
    OUTDIR="$proj/bin/Debug/net10.0"
    if [ ! -L "$OUTDIR/libFNA3D.so" ]; then
        ln -sf "$FNA3D_BUILD/libFNA3D.so.27.0.0" "$OUTDIR/libFNA3D.so"
    fi

    # Run headless
    if dotnet run --no-build --project "$proj/$proj.csproj" -- --headless 2>&1 | grep -q "RESULT:.*PASS"; then
        echo "  => PASS"
        PASS=$((PASS + 1))
    else
        echo "  => FAIL"
        FAIL=$((FAIL + 1))
        FAILED_TESTS="$FAILED_TESTS $proj"
    fi
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
