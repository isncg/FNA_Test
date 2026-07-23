#!/bin/bash
# Build SDF text FEB from HLSL sources
set -e
cd "$(dirname "$0")"
FEB_BUILDER="$(readlink -f ../../../FNA/tools/feb_builder.py)"

for manifest in *.feb.json; do
    echo "Building $manifest..."
    python3 "$FEB_BUILDER" "$manifest"
done
echo "All FEBs built successfully."
