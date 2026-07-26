python ~/dev/FNA/tools/feb_builder.py ./Shaders/PbrMaterial.feb.json -o ./Shaders/PbrMaterial.feb
dotnet build MaterialLib.csproj
dotnet run MaterialLib.csproj
