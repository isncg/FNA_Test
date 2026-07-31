#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

FEB_BUILDER="../../FNA/tools/feb_builder.py"

echo "=== Building SceneRenderer FEBs ==="
for feb_json in Shaders/*.feb.json; do
    name=$(basename "$feb_json" .feb.json)
    echo "  $name..."
    python3 "$FEB_BUILDER" "$feb_json"
done

echo ""
echo "=== Building SceneRenderer C# ==="
dotnet build SceneRenderer.csproj -c Release

echo ""
echo "=== Setting up native library symlinks ==="
OUTDIR="bin/Release/net10.0"
FNA3D_BUILD="../../FNA/lib/FNA3D/build"
if [ -f "$FNA3D_BUILD/libFNA3D.so.27.0.0" ]; then
    ln -sf "$FNA3D_BUILD/libFNA3D.so.27.0.0" "$OUTDIR/libFNA3D.so"
    ln -sf "$FNA3D_BUILD/libFNA3D.so.27.0.0" "$OUTDIR/libFNA3D.so.0"
    echo "  Symlinks created in $OUTDIR"
fi

echo ""
echo "=== Done ==="
echo "Run: dotnet run --project SceneRenderer.csproj"
echo "Headless: FNA_TEST_HEADLESS=1 dotnet run --project SceneRenderer.csproj"
