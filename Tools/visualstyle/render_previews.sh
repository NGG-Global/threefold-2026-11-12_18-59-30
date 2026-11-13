#!/usr/bin/env bash
# Renders the preview images of the visual style without Unity (see Program.cs for what they
# show and what they do not). The images use the same texture generator and the same lighting
# arithmetic as the game, taken from the assemblies that Tools/ci/compile_check.sh builds.
#
# Usage: Tools/visualstyle/render_previews.sh [output folder] [--all-textures]
#        The default output folder is Docs/VisualStyle/images.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CACHE="${THREEFOLD_COMPILE_CACHE:-$HOME/.cache/threefold-compile}"
OUT="$CACHE/out"
OUTPUT="${1:-$ROOT/Docs/VisualStyle/images}"

[ -f "$OUT/Threefold.Editor.dll" ] || { echo "Run Tools/ci/compile_check.sh first; it builds the assemblies this tool reads."; exit 2; }
ENGINE="$(ls -d "$CACHE"/unity-*/Editor/Data/Managed/UnityEngine | head -1)"

WORK="$CACHE/visualstyle-preview"
rm -rf "$WORK"; mkdir -p "$WORK"; cp "$ROOT/Tools/visualstyle/Program.cs" "$WORK/"
# The project file is written here because the repository's Unity .gitignore excludes *.csproj.
cat > "$WORK/preview.csproj" <<CSPROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Threefold.Editor"><HintPath>$OUT/Threefold.Editor.dll</HintPath></Reference>
    <Reference Include="Threefold.Runtime"><HintPath>$OUT/Threefold.Runtime.dll</HintPath></Reference>
    <Reference Include="UnityEngine.CoreModule"><HintPath>$ENGINE/UnityEngine.CoreModule.dll</HintPath></Reference>
  </ItemGroup>
</Project>
CSPROJ
( cd "$WORK" && dotnet build -c Release -v q --nologo > build.log 2>&1 ) || { cat "$WORK/build.log"; exit 1; }
dotnet "$WORK/bin/Release/net8.0/preview.dll" "$OUTPUT" "${@:2}"
