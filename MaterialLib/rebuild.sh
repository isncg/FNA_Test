#!/bin/bash
set -e
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
FEB_BUILDER="$SCRIPT_DIR/../FNA/tools/feb_builder.py"

python3 "$FEB_BUILDER" ./Shaders/PbrMaterial.feb.json -o ./Shaders/PbrMaterial.feb
dotnet build MaterialLib.csproj
dotnet run MaterialLib.csproj
