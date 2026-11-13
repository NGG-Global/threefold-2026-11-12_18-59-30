#!/usr/bin/env bash
# Compiles every Threefold assembly with the C# compiler against the real Unity assemblies,
# without running the Unity Editor and without a Unity licence. Then runs the tests that do
# not need the Unity engine.
#
# What it proves:   the code compiles for the Editor and with Android player defines, and the
#                   project's shaders compile against the real URP shader library with a
#                   stand-in compiler (see Tools/ci/shader_check.py for what that covers).
# What it does not: that anything works at runtime. Tests that create GameObjects, the setup
#                   commands and the game itself only run inside Unity.
#
# How it works: it downloads the Unity Editor archive for the version in
# ProjectSettings/ProjectVersion.txt from Unity's public download server and extracts only the
# managed reference assemblies and the bundled package sources. Nothing from the archive is
# executed. The UI, Input System and AI Navigation packages are compiled from their own source.
# The render pipeline members in use are represented by Tools/ci/RenderPipelineStubs.cs.
#
# Requirements: bash, curl, tar with xz, python3, and the .NET 8 SDK (`dotnet`). The shader
# check also needs glslangValidator (Ubuntu: apt-get install glslang-tools); without it that
# step is skipped with a notice.
# First run downloads about 4 GB (streamed, about 450 MB kept). Later runs reuse the cache.
#
# Usage: Tools/ci/compile_check.sh            cache in ~/.cache/threefold-compile
#        THREEFOLD_COMPILE_CACHE=/path Tools/ci/compile_check.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CACHE="${THREEFOLD_COMPILE_CACHE:-$HOME/.cache/threefold-compile}"
cd "$ROOT"

command -v dotnet >/dev/null || { echo "The .NET SDK (dotnet) is required."; exit 2; }
CSC_DLL="$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll "$(dirname "$(readlink -f "$(command -v dotnet)")")"/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | head -1)"
[ -n "$CSC_DLL" ] || { echo "The C# compiler was not found inside the .NET SDK."; exit 2; }
CSC="dotnet $CSC_DLL"

VERSION="$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt)"
REVISION="$(sed -n 's/^m_EditorVersionWithRevision: .*(\(.*\))/\1/p' ProjectSettings/ProjectVersion.txt)"
INPUT_VERSION="$(python3 -c "import json;print(json.load(open('Packages/packages-lock.json'))['dependencies']['com.unity.inputsystem']['version'])")"
NAV_VERSION="$(python3 -c "import json;print(json.load(open('Packages/packages-lock.json'))['dependencies']['com.unity.ai.navigation']['version'])")"
echo "Unity $VERSION ($REVISION), Input System $INPUT_VERSION, AI Navigation $NAV_VERSION"

UNITY="$CACHE/unity-$VERSION"
DATA="$UNITY/Editor/Data"
BUILTIN="$DATA/Resources/PackageManager/BuiltInPackages"
INPUT="$CACHE/inputsystem-$INPUT_VERSION"
NAV="$CACHE/ainavigation-$NAV_VERSION"
OUT="$CACHE/out"
mkdir -p "$UNITY" "$OUT"

# The render pipeline packages are only needed for their shader library (the shader check).
# A cache made before they were added is downloaded again once.
if [ ! -f "$UNITY/.complete" ] || [ ! -d "$BUILTIN/com.unity.render-pipelines.universal/ShaderLibrary" ]; then
  echo "Downloading and extracting Unity reference assemblies and shader library (about 4 GB streamed)..."
  ( cd "$UNITY" && curl -sS -L "https://download.unity3d.com/download_unity/$REVISION/LinuxEditorInstaller/Unity.tar.xz" \
      | tar -xJ --wildcards \
          'Editor/Data/Managed/UnityEngine/*.dll' \
          'Editor/Data/NetStandard/*' \
          'Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.ugui/*' \
          'Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.ext.nunit/*' \
          'Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.universal/*' \
          'Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.core/*' \
          'Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.universal-config/*' )
  touch "$UNITY/.complete"
fi

if [ ! -d "$INPUT/package" ]; then
  echo "Downloading Input System $INPUT_VERSION source..."
  mkdir -p "$INPUT"
  curl -sS -L "https://packages.unity.com/com.unity.inputsystem/-/com.unity.inputsystem-$INPUT_VERSION.tgz" | tar -xz -C "$INPUT"
fi

if [ ! -d "$NAV/package" ]; then
  echo "Downloading AI Navigation $NAV_VERSION source..."
  mkdir -p "$NAV"
  curl -sS -L "https://packages.unity.com/com.unity.ai.navigation/-/com.unity.ai.navigation-$NAV_VERSION.tgz" | tar -xz -C "$NAV"
fi

REFS="-r:$DATA/NetStandard/ref/2.1.0/netstandard.dll"
for d in "$DATA"/Managed/UnityEngine/*.dll; do REFS="$REFS -r:$d"; done
PLAYER_REFS="-r:$DATA/NetStandard/ref/2.1.0/netstandard.dll"
for d in "$DATA"/Managed/UnityEngine/UnityEngine*.dll; do PLAYER_REFS="$PLAYER_REFS -r:$d"; done
SHIMS=""
for d in "$DATA"/NetStandard/compat/2.1.0/shims/netfx/*.dll; do SHIMS="$SHIMS -r:$d"; done
NUNIT_DIR="$BUILTIN/com.unity.ext.nunit/net472/unity-custom"

NEWER=""
for v in 5_3 5_4 5_5 5_6 2017_1 2017_2 2017_3 2017_4 2018_1 2018_2 2018_3 2018_4 2019_1 2019_2 2019_3 2019_4 \
         2020_1 2020_2 2020_3 2021_1 2021_2 2021_3 2022_1 2022_2 2022_3 2023_1 2023_2 2023_3 \
         6000_0 6000_1 6000_2 6000_3 6000_4 6000_5 6000_6; do NEWER="$NEWER;UNITY_${v}_OR_NEWER"; done
BASE_DEF="DEBUG;TRACE;UNITY_6000;UNITY_INCLUDE_TESTS;ENABLE_INPUT_SYSTEM;NET_STANDARD;NET_STANDARD_2_1;CSHARP_7_3_OR_NEWER;DEVELOPMENT_BUILD$NEWER"
EDITOR_DEF="UNITY_EDITOR;UNITY_EDITOR_64;UNITY_EDITOR_WIN;UNITY_STANDALONE;UNITY_STANDALONE_WIN;$BASE_DEF"
PLAYER_DEF="UNITY_ANDROID;ENABLE_IL2CPP;$BASE_DEF"
UI_DEF="PACKAGE_PHYSICS;PACKAGE_PHYSICS2D;PACKAGE_TILEMAP;PACKAGE_ANIMATION;PACKAGE_UITOOLKIT"
INPUT_DEF="UNITY_INPUT_SYSTEM_ENABLE_XR;UNITY_INPUT_SYSTEM_ENABLE_PHYSICS;UNITY_INPUT_SYSTEM_ENABLE_PHYSICS2D;UNITY_INPUT_SYSTEM_ENABLE_UI;UNITY_INPUT_SYSTEM_PLATFORM_SCROLL_DELTA;UNITY_INPUT_SYSTEM_INPUT_MODULE_SCROLL_DELTA;UNITY_INPUT_SYSTEM_INPUT_MODULE_NAVIGATION_DEVICE_TYPE;UNITY_INPUT_SYSTEM_SENDPOINTERHOVERTOPARENT;UNITY_INPUT_SYSTEM_ENABLE_ANALYTICS;UNITY_INPUT_SYSTEM_PLATFORM_POLLING_FREQUENCY;UNITY_INPUTSYSTEM_SUPPORTS_MOUSE_SCRIPT_EVENTS;UNITY_INPUTSYSTEM_SUPPORTS_FOCUS_EVENTS"
COMMON="-nologo -noconfig -nostdlib+ -target:library -langversion:9.0 -unsafe+ -debug- -deterministic+"

FAILED=0

# compile <label> <output> <warning level> <defines> <response file> <references...>
compile() {
  local label="$1" out="$2" warn="$3" defines="$4" rsp="$5"; shift 5
  local log="$OUT/$(basename "$out").log"
  if $CSC $COMMON -warn:"$warn" "-define:$defines" "$@" -out:"$out" "@$rsp" > "$log" 2>&1; then
    local warnings; warnings="$(grep -c 'warning CS' "$log" || true)"
    echo "OK    $label ($(wc -l < "$rsp") files, $warnings warning(s))"
    if [ "$warn" != "0" ] && [ "$warnings" != "0" ]; then grep 'warning CS' "$log" | sed "s#$ROOT/##" | sort -u | head -20; fi
  else
    FAILED=1
    echo "FAIL  $label: $(grep -c 'error CS' "$log" || true) error(s)"
    grep 'error CS' "$log" | sed "s#$ROOT/##" | sort -u | head -60
  fi
}

# Unity packages, compiled from their own source (warnings are Unity's, so they are not shown).
find "$BUILTIN/com.unity.ugui/Runtime/UGUI" -name '*.cs' > "$OUT/ui.rsp"
compile "UnityEngine.UI (Unity package)" "$OUT/UnityEngine.UI.dll" 0 "$EDITOR_DEF;$UI_DEF" "$OUT/ui.rsp" $REFS
find "$BUILTIN/com.unity.ugui/Editor/UGUI" -name '*.cs' > "$OUT/uieditor.rsp"
compile "UnityEditor.UI (Unity package)" "$OUT/UnityEditor.UI.dll" 0 "$EDITOR_DEF;$UI_DEF" "$OUT/uieditor.rsp" $REFS -r:"$OUT/UnityEngine.UI.dll"

# The Input System's main assembly only; its nested assemblies are left out.
python3 - "$INPUT/package/InputSystem" "$OUT/input.rsp" <<'PY'
import os, sys
root, out = sys.argv[1], sys.argv[2]
nested = [d for d, _, files in os.walk(root) if d != root and any(f.endswith('.asmdef') for f in files)]
paths = [os.path.join(d, f) for d, _, files in os.walk(root)
         if not any(d == n or d.startswith(n + os.sep) for n in nested)
         for f in files if f.endswith('.cs')]
open(out, 'w').write('\n'.join(paths) + '\n')
PY
compile "Unity.InputSystem (Unity package)" "$OUT/Unity.InputSystem.dll" 0 "$EDITOR_DEF;$INPUT_DEF" "$OUT/input.rsp" $REFS -r:"$OUT/UnityEngine.UI.dll" -r:"$OUT/UnityEditor.UI.dll"

# The AI Navigation runtime assembly (NavMeshSurface), used by the Editor setup commands.
find "$NAV/package/Runtime" -name '*.cs' > "$OUT/navigation.rsp"
compile "Unity.AI.Navigation (Unity package)" "$OUT/Unity.AI.Navigation.dll" 0 "$EDITOR_DEF;NMC_CAN_ACCESS_TERRAIN" "$OUT/navigation.rsp" $REFS

echo "$ROOT/Tools/ci/RenderPipelineStubs.cs" > "$OUT/stubs.rsp"
compile "Render pipeline stand-ins" "$OUT/RenderPipelineStubs.dll" 0 "$EDITOR_DEF" "$OUT/stubs.rsp" $REFS

# Threefold.
PKG="-r:$OUT/UnityEngine.UI.dll -r:$OUT/Unity.InputSystem.dll"
find "$ROOT/Assets/Threefold/Scripts/Runtime" -name '*.cs' > "$OUT/runtime.rsp"
find "$ROOT/Assets/Threefold/Scripts/Editor" -name '*.cs' > "$OUT/editor.rsp"
find "$ROOT/Assets/Threefold/Tests/Editor" -name '*.cs' > "$OUT/tests.rsp"

compile "Threefold.Runtime (Editor)" "$OUT/Threefold.Runtime.dll" 4 "$EDITOR_DEF" "$OUT/runtime.rsp" $REFS $PKG -r:"$OUT/RenderPipelineStubs.dll"
compile "Threefold.Runtime (Android player defines, no Editor references)" "$OUT/Threefold.Runtime.Player.dll" 4 "$PLAYER_DEF" "$OUT/runtime.rsp" $PLAYER_REFS $PKG -r:"$OUT/RenderPipelineStubs.dll"
compile "Threefold.Editor" "$OUT/Threefold.Editor.dll" 4 "$EDITOR_DEF" "$OUT/editor.rsp" $REFS $PKG -r:"$OUT/Threefold.Runtime.dll" -r:"$OUT/RenderPipelineStubs.dll" -r:"$OUT/Unity.AI.Navigation.dll"
compile "Threefold.Tests.Editor" "$OUT/Threefold.Tests.Editor.dll" 4 "$EDITOR_DEF" "$OUT/tests.rsp" $REFS $SHIMS -r:"$NUNIT_DIR/nunit.framework.dll" -r:"$OUT/Threefold.Runtime.dll" -r:"$OUT/Threefold.Editor.dll" -r:"$OUT/Unity.AI.Navigation.dll"

if [ "$FAILED" != "0" ]; then
  echo; echo "COMPILE CHECK FAILED"; exit 1
fi

echo; echo "Running the tests that do not need the Unity engine..."
RUNNER="$CACHE/testrunner"
rm -rf "$RUNNER"; mkdir -p "$RUNNER"; cp "$ROOT/Tools/ci/testrunner/Program.cs" "$RUNNER/"
# The project file is written here because the repository's Unity .gitignore excludes *.csproj.
cat > "$RUNNER/runner.csproj" <<'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
CSPROJ
( cd "$RUNNER" && dotnet build -c Release -v q --nologo > build.log 2>&1 ) || { cat "$RUNNER/build.log"; exit 1; }
dotnet "$RUNNER/bin/Release/net8.0/runner.dll" "$OUT/Threefold.Tests.Editor.dll" "$OUT" "$DATA/Managed/UnityEngine" "$NUNIT_DIR" "$DATA/NetStandard/compat/2.1.0/shims/netfx" > "$OUT/tests.log" 2>&1 || FAILED=1
grep -E '^(FAIL|TOTAL)' "$OUT/tests.log" || true

if [ "$FAILED" != "0" ]; then echo; echo "TESTS FAILED (see $OUT/tests.log)"; exit 1; fi

echo; echo "Compiling the shaders against the URP shader library..."
if command -v glslangValidator >/dev/null; then
  python3 "$ROOT/Tools/ci/shader_check.py" --urp "$BUILTIN" || { echo; echo "SHADER CHECK FAILED"; exit 1; }
else
  echo "SKIPPED: glslangValidator is not installed (apt-get install glslang-tools). Shaders were NOT checked."
fi

echo; echo "COMPILE CHECK PASSED"
