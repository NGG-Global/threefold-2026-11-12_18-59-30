#!/usr/bin/env python3
"""Compiles the project's URP shaders outside Unity, to catch syntax and type errors early.

Unity's own shader compiler only runs inside the Editor. This script is a stand-in: for every
pass of every .shader file under Assets/Threefold/Shaders it takes the HLSL of the pass,
preprocesses it against the real URP and Core shader libraries of the project's Unity version,
and compiles the vertex and fragment functions with glslang's HLSL front end.

What it proves:   the HLSL is syntactically valid, every identifier and include resolves against
                  the real library of this URP version, and types match, for each listed set of
                  keywords.
What it does not: that Unity's compiler accepts it (Unity uses different compilers per platform),
                  that it works with mobile half precision (see below), or how anything looks.

Three adaptations are needed, because glslang is not the compiler the library is written for:

  * glslang treats 'half' and 'min16float' as 'float'. The library defines many functions once
    for float and once for half, which then collide. After preprocessing, later definitions with
    an identical signature are removed. Because of this the check runs with desktop precision.
  * '#include_with_pragmas' is a Unity directive; it is read as '#include'. '#pragma' lines are
    ignored by glslang, so keyword sets are supplied by this script instead (see VARIANTS).
  * An empty ';' left inside a constant buffer by a macro is removed; glslang rejects it there.

Requirements: glslangValidator (Ubuntu package 'glslang-tools') and the library sources, which
Tools/ci/compile_check.sh extracts into its cache. That script also runs this one.

Usage: python3 Tools/ci/shader_check.py [--urp /path/to/BuiltInPackages] [--keep] [shader files]
Exit code 0 means every pass compiled for every variant.
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SHADER_DIR = os.path.join(ROOT, "Assets", "Threefold", "Shaders")
DEFAULT_CACHE = os.environ.get("THREEFOLD_COMPILE_CACHE", os.path.expanduser("~/.cache/threefold-compile"))
PACKAGES = (
    "com.unity.render-pipelines.universal",
    "com.unity.render-pipelines.core",
    "com.unity.render-pipelines.universal-config",
)

# What Unity's compiler defines before the shader's own code.
PREAMBLE = """#define UNITY_VERSION 60000060
#define SHADER_API_VULKAN 1
#define SHADER_TARGET 45
#define INTRINSIC_WAVEREADFIRSTLANE 1
"""

# Keyword sets each pass is compiled with. Passes only see the keywords they declare in a pragma.
VARIANTS = [
    [],
    ["_MAIN_LIGHT_SHADOWS", "FOG_LINEAR"],
    ["_MAIN_LIGHT_SHADOWS", "_SHADOWS_SOFT", "_SHADOWS_SOFT_LOW", "FOG_LINEAR", "_ADDITIONAL_LIGHTS"],
    ["_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT", "FOG_EXP2", "_ADDITIONAL_LIGHTS_VERTEX"],
    ["_MAIN_LIGHT_SHADOWS_SCREEN", "FOG_EXP", "_CLUSTER_LIGHT_LOOP"],
    ["_MAIN_LIGHT_SHADOWS", "_CLUSTER_LIGHT_LOOP", "_SHADOWS_SOFT", "_SHADOWS_SOFT_MEDIUM", "INSTANCING_ON"],
    ["_MAIN_LIGHT_SHADOWS", "FOG_LINEAR", "_PAINT_VARIATION", "_HIGHLIGHT", "_ALPHATEST_ON"],
    ["_MAIN_LIGHT_SHADOWS", "FOG_LINEAR", "_OUTLINE_SMOOTH_NORMALS", "_ADDITIONAL_LIGHTS", "_PAINT_VARIATION"],
    ["_CASTING_PUNCTUAL_LIGHT_SHADOW", "_ALPHATEST_ON", "INSTANCING_ON"],
]

TYPE_ALIASES = re.compile(r"\b(?:min16float|half)(?=[1-4]?(?:x[1-4])?\b)")
QUALIFIERS = {"in", "out", "inout", "const", "uniform", "nointerpolation", "noperspective", "centroid", "linear",
              "sample", "static", "inline", "precise"}


def default_library() -> str:
    """The shader library inside the cache of Tools/ci/compile_check.sh, for the project's Unity version."""
    version = ""
    try:
        with open(os.path.join(ROOT, "ProjectSettings", "ProjectVersion.txt"), encoding="utf-8") as handle:
            match = re.search(r"^m_EditorVersion: (\S+)", handle.read(), re.M)
            version = match.group(1) if match else ""
    except OSError:
        pass
    return os.path.join(DEFAULT_CACHE, "unity-" + version, "Editor", "Data", "Resources", "PackageManager",
                        "BuiltInPackages")


def fail(message: str) -> None:
    print(message)
    sys.exit(2)


def find_passes(shader_text: str):
    """Yields (pass name, HLSL text) for every HLSLPROGRAM block, with shared HLSLINCLUDE blocks in front."""
    shared = "\n".join(m.group(1) for m in re.finditer(r"HLSLINCLUDE(.*?)ENDHLSL", shader_text, re.S))
    position = 0
    while True:
        start = shader_text.find("HLSLPROGRAM", position)
        if start < 0:
            return
        end = shader_text.find("ENDHLSL", start)
        if end < 0:
            fail("HLSLPROGRAM without ENDHLSL")
        before = shader_text[:start]
        names = re.findall(r'Name\s+"([^"]+)"', before[before.rfind("Pass"):])
        yield (names[-1] if names else "(unnamed)"), shared + "\n" + shader_text[start + len("HLSLPROGRAM"):end]
        position = end


def declared_keywords(hlsl: str, include_dirs) -> set:
    """Keywords the pass declares, including those from files pulled in with #include_with_pragmas."""
    keywords = set()
    texts = [hlsl]
    for match in re.finditer(r'#include_with_pragmas\s+"([^"]+)"', hlsl):
        for directory in include_dirs:
            path = os.path.join(directory, match.group(1))
            if os.path.exists(path):
                texts.append(open(path, encoding="utf-8", errors="replace").read())
                break
    for text in texts:
        for line in re.findall(r"#pragma\s+(?:multi_compile\w*|shader_feature\w*|dynamic_branch\w*)\s+([^\n]*)", text):
            keywords.update(word for word in line.split() if word != "_" and not word.startswith("//"))
        if "multi_compile_fog" in text:
            keywords.update(["FOG_LINEAR", "FOG_EXP", "FOG_EXP2"])
        if "multi_compile_instancing" in text:
            keywords.add("INSTANCING_ON")
    return keywords


def entry_points(hlsl: str):
    """The vertex and fragment function names, or None if the pass declares them inside an include."""
    vertex = re.search(r"#pragma\s+vertex\s+(\w+)", hlsl)
    fragment = re.search(r"#pragma\s+fragment\s+(\w+)", hlsl)
    if not vertex or not fragment:
        return None
    return vertex.group(1), fragment.group(1)


def split_parameters(text: str):
    parts, depth, current = [], 0, ""
    for char in text:
        if char in "([{":
            depth += 1
        elif char in ")]}":
            depth -= 1
        if char == "," and depth == 0:
            parts.append(current)
            current = ""
        else:
            current += char
    if current.strip():
        parts.append(current)
    return parts


def signature_key(header: str):
    """'float3 Foo(in float3 a, half b = 1)' gives ('Foo', ('in float3', 'float')), or None if not a function."""
    open_paren = header.find("(")
    if open_paren < 0:
        return None
    depth, close_paren = 0, -1
    for index in range(open_paren, len(header)):
        if header[index] == "(":
            depth += 1
        elif header[index] == ")":
            depth -= 1
            if depth == 0:
                close_paren = index
                break
    if close_paren < 0:
        return None
    before = header[:open_paren].split()
    after = header[close_paren + 1:].strip()
    if len(before) < 2 or "=" in header[:open_paren] or (after and not after.startswith(":")):
        return None
    if before[-2] in ("struct", "cbuffer", "return", "else") or before[-1] in ("if", "for", "while", "switch"):
        return None

    types = []
    for parameter in split_parameters(header[open_paren + 1:close_paren]):
        parameter = parameter.split("=")[0].split(":")[0].strip()
        if not parameter or parameter == "void":
            continue
        words = parameter.replace("[", " [").split()
        qualifiers = [w for w in words if w in QUALIFIERS and w not in ("in", "const")]
        rest = [w for w in words if w not in QUALIFIERS]
        if not rest:
            return None
        array = "".join(w for w in rest[2:] if w.startswith("["))
        types.append(" ".join(qualifiers + [rest[0] + array]))
    return before[-1], tuple(types)


def remove_empty_statements(text: str) -> str:
    """
    Blanks out a ';' that directly follows another ';' or a '{'. Macros that expand to nothing, or
    that end in ';' themselves, leave such empty declarations inside constant buffers, which
    glslang rejects. Anything inside parentheses, such as 'for (;;)', is left alone.
    """
    output = list(text)
    paren_depth = 0
    previous = ""
    in_directive = False
    for index, char in enumerate(text):
        if char == "\n":
            in_directive = False
            continue
        if in_directive or char in " \t\r":
            continue
        if char == "#" and previous in ("", ";", "{", "}", ")") and text[text.rfind("\n", 0, index) + 1:index].strip() == "":
            in_directive = True
            continue
        if char == "(":
            paren_depth += 1
        elif char == ")":
            paren_depth -= 1
        elif char == ";" and paren_depth == 0 and previous in (";", "{"):
            output[index] = " "
            continue
        previous = char
    return "".join(output)


def remove_duplicate_functions(text: str):
    """Blanks out function definitions whose name and parameter types repeat an earlier definition."""
    text = remove_empty_statements(TYPE_ALIASES.sub("float", text))
    seen, removed = set(), 0
    output = list(text)
    depth, statement_start, index, length = 0, 0, 0, len(text)

    while index < length:
        char = text[index]
        if char == "{":
            if depth == 0:
                header = text[statement_start:index]
                # Skip preprocessor lines that -E leaves in the output.
                header_code = "\n".join(l for l in header.split("\n") if not l.lstrip().startswith("#"))
                key = signature_key(" ".join(header_code.split()))
                body_end, inner = index, 0
                for cursor in range(index, length):
                    if text[cursor] == "{":
                        inner += 1
                    elif text[cursor] == "}":
                        inner -= 1
                        if inner == 0:
                            body_end = cursor
                            break
                if key is not None:
                    if key in seen:
                        removed += 1
                        for cursor in range(statement_start, body_end + 1):
                            in_directive = False
                            if output[cursor] != "\n":
                                line_start = text.rfind("\n", 0, cursor) + 1
                                in_directive = text[line_start:cursor + 1].lstrip().startswith("#")
                                if not in_directive:
                                    output[cursor] = " "
                    else:
                        seen.add(key)
                    index = body_end + 1
                    statement_start = index
                    continue
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                statement_start = index + 1
        elif char == ";" and depth == 0:
            statement_start = index + 1
        index += 1

    return "".join(output), removed


def run(command, cwd):
    """Returns (exit code, standard output, standard error)."""
    result = subprocess.run(command, cwd=cwd, capture_output=True, text=True, check=False)
    return result.returncode, result.stdout, result.stderr


def error_lines(output: str, work: str):
    lines = []
    for line in output.splitlines():
        if line.startswith("ERROR:") and "compilation errors" not in line:
            lines.append(line.replace(work + "/", "").replace("./", ""))
    return lines


def measure_stage(work, fixed_name, stage, entry):
    """Size of one compiled stage: instructions and texture samples after dead code is removed.

    The shader is compiled to SPIR-V, optimised with spirv-opt -O (which removes the library functions
    the shader never calls) and disassembled. The counts are a proxy for cost, made with a different
    compiler from Unity's: use them to compare two versions of the shader, not as a measure of a phone.
    Returns None if the SPIR-V tools are missing or fail.
    """
    if shutil.which("spirv-opt") is None or shutil.which("spirv-dis") is None:
        return None
    raw, optimised = fixed_name + ".spv", fixed_name + ".opt.spv"
    code, _, _ = run(["glslangValidator", "-D", "-S", stage, "-e", entry, "--target-env", "vulkan1.1",
                      "-V", "-o", raw, fixed_name], work)
    if code != 0:
        return None
    code, _, _ = run(["spirv-opt", "-O", raw, "-o", optimised], work)
    if code != 0:
        return None
    code, listing, _ = run(["spirv-dis", "--no-header", optimised], work)
    if code != 0:
        return None
    body = [line.strip() for line in listing.splitlines() if line.strip()]
    declarations = ("OpName", "OpMemberName", "OpDecorate", "OpMemberDecorate", "OpType", "OpConstant", "OpVariable",
                    "OpCapability", "OpExtInstImport", "OpMemoryModel", "OpEntryPoint", "OpExecutionMode", "OpSource",
                    "OpLabel", "OpFunction", "OpFunctionEnd", "OpReturn", "OpUndef")
    operations = [line for line in body if not any((" " + d) in (" " + line.split("= ")[-1]) and line.split("= ")[-1].startswith(d)
                                                   for d in declarations)]
    return {
        "instructions": len(operations),
        "samples": sum(1 for line in operations if "OpImageSample" in line),
        "branches": sum(1 for line in operations if "OpBranchConditional" in line or "OpSwitch" in line),
    }


def compile_stage(work, source_name, stage, entry, defines, measure=False):
    base = ["glslangValidator", "-D", "-S", stage, "-I.", "-I" + SHADER_DIR]
    for define in defines:
        base.append("-D" + define + "=1")
    base.append("-DSHADER_STAGE_" + ("VERTEX" if stage == "vert" else "FRAGMENT") + "=1")

    code, preprocessed, messages = run(base + ["-E", source_name], work)
    problems = error_lines(messages + "\n" + preprocessed, work)
    if code != 0 or problems:
        return problems or [(messages or preprocessed).strip()[-400:]], 0
    # The first line of -E output is the file name.
    if preprocessed.startswith(source_name):
        preprocessed = preprocessed[len(source_name):]

    fixed, removed = remove_duplicate_functions(preprocessed)
    fixed_name = source_name + "." + stage + ".pre.hlsl"
    with open(os.path.join(work, fixed_name), "w", encoding="utf-8") as handle:
        handle.write(fixed)

    code, output, messages = run(["glslangValidator", "-D", "-S", stage, "-e", entry, "--target-env", "vulkan1.1",
                                  "-V", "-o", os.devnull, fixed_name], work)
    output += "\n" + messages
    problems = error_lines(output, work)
    if code != 0 and not problems:
        problems = [output.strip()[-400:]]
    if "Entry point not found" in output:
        problems.append(f"entry point '{entry}' not found")
    if measure:
        return problems, (measure_stage(work, fixed_name, stage, entry) if not problems else None)
    return problems, removed


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--urp", default=default_library(),
                        help="folder holding the URP and Core packages of the project's Unity version")
    parser.add_argument("--keep", action="store_true", help="keep the working folder for inspection")
    parser.add_argument("--measure", action="store_true",
                        help="also print the size of every compiled stage (instructions, texture samples, branches "
                             "after spirv-opt -O; needs the package 'spirv-tools'). A proxy for comparing versions")
    parser.add_argument("files", nargs="*", help="shader files to check (default: every project shader)")
    arguments = parser.parse_args()

    if shutil.which("glslangValidator") is None:
        fail("glslangValidator was not found. Install it with: apt-get install glslang-tools")
    for package in PACKAGES:
        if not os.path.isdir(os.path.join(arguments.urp, package)):
            fail(f"The shader library '{package}' was not found in {arguments.urp}.\n"
                 "Run Tools/ci/compile_check.sh once; it extracts the library into its cache.")

    shaders = arguments.files or sorted(
        os.path.join(folder, name)
        for folder, _, names in os.walk(SHADER_DIR) for name in names if name.endswith(".shader"))
    if not shaders:
        print("No shaders to check.")
        return 0

    work = tempfile.mkdtemp(prefix="threefold-shadercheck-")
    os.makedirs(os.path.join(work, "Packages"))
    for package in PACKAGES:
        os.symlink(os.path.join(arguments.urp, package), os.path.join(work, "Packages", package))

    failures = 0
    compiled = 0
    try:
        for shader in shaders:
            text = open(shader, encoding="utf-8").read()
            label = os.path.relpath(shader, ROOT) if os.path.abspath(shader).startswith(ROOT) else os.path.basename(shader)
            for pass_index, (pass_name, hlsl) in enumerate(find_passes(text)):
                declared = declared_keywords(hlsl, [work, SHADER_DIR])
                entries = entry_points(hlsl)
                if entries is None:
                    print(f"SKIP  {label}  pass '{pass_name}': its vertex and fragment functions are not named in the pass")
                    continue
                vertex, fragment = entries
                failures_before = failures
                body = hlsl.replace("#include_with_pragmas", "#include")

                done = set()
                for variant in VARIANTS:
                    active = tuple(sorted(k for k in variant if k in declared))
                    if active in done:
                        continue
                    done.add(active)

                    source_name = f"pass{pass_index}_{len(done)}.hlsl"
                    with open(os.path.join(work, source_name), "w", encoding="utf-8") as handle:
                        handle.write(PREAMBLE + body)

                    for stage, entry in (("vert", vertex), ("frag", fragment)):
                        problems, size = compile_stage(work, source_name, stage, entry, active, arguments.measure)
                        compiled += 1
                        if arguments.measure and not problems:
                            keywords = " ".join(active) or "no keywords"
                            if size is None:
                                print(f"SIZE  {pass_name:<13} {stage}  not measured (spirv-tools missing or failed)  [{keywords}]")
                            else:
                                print(f"SIZE  {pass_name:<13} {stage}  {size['instructions']:>5} instructions  "
                                      f"{size['samples']:>2} samples  {size['branches']:>2} branches  [{keywords}]")
                        if problems:
                            failures += 1
                            print(f"FAIL  {label}  pass '{pass_name}'  {stage}  [{' '.join(active) or 'no keywords'}]")
                            for problem in problems[:12]:
                                print("      " + problem)

                if failures == failures_before:
                    print(f"OK    {label}  pass '{pass_name}'  ({len(done)} keyword set(s))")
    finally:
        if arguments.keep:
            print("Working folder kept: " + work)
        else:
            shutil.rmtree(work, ignore_errors=True)

    print()
    if failures:
        print(f"SHADER CHECK FAILED: {failures} of {compiled} compilations had errors.")
        return 1
    print(f"SHADER CHECK PASSED: {compiled} compilations (vertex and fragment, per pass and keyword set).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
