#!/usr/bin/env bash
# Renders pictures of the mobile control layout without Unity, from the same numbers the setup
# command uses (MobileControlsLayout in the compiled Threefold.Runtime assembly). They show where
# each control sits, its size and its touch area on a phone and a tablet. They are diagrams, not
# screenshots: Unity, the real icons and the game view are not involved.
#
# Usage: Tools/touchcontrols/render_layout.sh [output folder]
#        The default output folder is Docs/TouchControls/images.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CACHE="${THREEFOLD_COMPILE_CACHE:-$HOME/.cache/threefold-compile}"
OUT="$CACHE/out"
OUTPUT="${1:-$ROOT/Docs/TouchControls/images}"

[ -f "$OUT/Threefold.Runtime.dll" ] || { echo "Run Tools/ci/compile_check.sh first; it builds the assembly this tool reads."; exit 2; }
ENGINE="$(ls -d "$CACHE"/unity-*/Editor/Data/Managed/UnityEngine | head -1)"

WORK="$CACHE/touchcontrols-preview"
rm -rf "$WORK"; mkdir -p "$WORK"; cp "$ROOT/Tools/touchcontrols/Program.cs" "$WORK/"
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
    <Reference Include="Threefold.Runtime"><HintPath>$OUT/Threefold.Runtime.dll</HintPath></Reference>
    <Reference Include="UnityEngine.CoreModule"><HintPath>$ENGINE/UnityEngine.CoreModule.dll</HintPath></Reference>
  </ItemGroup>
</Project>
CSPROJ
( cd "$WORK" && dotnet build -c Release -v q --nologo > build.log 2>&1 ) || { cat "$WORK/build.log"; exit 1; }
dotnet "$WORK/bin/Release/net8.0/preview.dll" "$OUTPUT"
