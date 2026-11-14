#!/usr/bin/env python3
"""Repository checks for a Unity project that do not need Unity or a Unity licence.

These catch the mistakes that are easy to make when Unity assets are authored outside the
Editor, and that Unity would otherwise only reveal on the next import:

  * an asset or folder under Assets/ without a .meta file, or a .meta file without its asset
  * a .meta file without a GUID, or two .meta files sharing one GUID
  * a file that .gitattributes routes to Git LFS but that was committed as a regular file
  * a large binary committed outside Git LFS
  * invalid JSON in assembly definitions and the package manifest
  * unresolved merge conflict markers
  * a MonoBehaviour or ScriptableObject class (directly or through a base class) that is not
    in a file of the same name: Unity cannot link a component or asset to such a class, so
    assets created from it lose their script (found by the first Unity build of LM-01 to LM-07)

Only files tracked by Git are inspected, so ignored folders such as Library/ are never read.
Run from anywhere inside the repository:

    python3 Tools/ci/check_unity_repo.py

Exit code 0 means no errors. Warnings do not fail the run.
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
from collections import defaultdict

ASSET_ROOT = "Assets/"
LFS_POINTER_PREFIX = b"version https://git-lfs.github.com/spec/v1"
MAX_REGULAR_FILE_BYTES = 10 * 1024 * 1024
GUID_PATTERN = re.compile(rb"^guid:\s*([0-9a-f]{32})\s*$", re.MULTILINE)
CONFLICT_PATTERN = re.compile(rb"^(<<<<<<<|>>>>>>>) ", re.MULTILINE)
JSON_SUFFIXES = (".asmdef", ".asmref")
JSON_FILES = ("Packages/manifest.json",)
TEXT_SUFFIXES = (
    ".cs", ".unity", ".prefab", ".asset", ".mat", ".meta", ".json", ".asmdef", ".asmref",
    ".inputactions", ".md", ".yml", ".yaml", ".txt", ".shader", ".hlsl", ".uss", ".uxml",
)

errors: list[tuple[str, str]] = []
warnings: list[tuple[str, str]] = []


def git(*args: str, stdin: bytes | None = None) -> bytes:
    result = subprocess.run(["git", *args], input=stdin, capture_output=True, check=False)
    if result.returncode != 0:
        sys.exit(f"git {' '.join(args)} failed:\n{result.stderr.decode(errors='replace')}")
    return result.stdout


def error(path: str, message: str) -> None:
    errors.append((path, message))


def warning(path: str, message: str) -> None:
    warnings.append((path, message))


def is_hidden_from_unity(path: str) -> bool:
    """Unity does not import files or folders whose name starts with '.' or ends with '~'."""
    return any(part.startswith(".") or part.endswith("~") for part in path.split("/"))


def read_index() -> dict[str, str]:
    """Maps every tracked path to the id of its staged blob."""
    entries: dict[str, str] = {}
    for record in git("ls-files", "-s", "-z").split(b"\0"):
        if not record:
            continue
        meta, path = record.split(b"\t", 1)
        mode, blob, _stage = meta.split(b" ")
        if mode == b"160000":  # submodule
            continue
        entries[path.decode("utf-8", errors="surrogateescape")] = blob.decode()
    return entries


def read_blob_sizes(index: dict[str, str]) -> dict[str, int]:
    blobs = sorted(set(index.values()))
    output = git("cat-file", "--batch-check", stdin=("\n".join(blobs) + "\n").encode())
    sizes: dict[str, int] = {}
    for line in output.decode().splitlines():
        blob, _kind, size = line.split(" ")
        sizes[blob] = int(size)
    return sizes


def read_blob(blob: str) -> bytes:
    return git("cat-file", "blob", blob)


def read_lfs_paths(paths: list[str]) -> set[str]:
    """Paths that .gitattributes assigns to the Git LFS filter."""
    if not paths:
        return set()
    stdin = b"\0".join(p.encode("utf-8", errors="surrogateescape") for p in paths) + b"\0"
    fields = git("check-attr", "-z", "--stdin", "filter", stdin=stdin).split(b"\0")
    lfs: set[str] = set()
    for i in range(0, len(fields) - 2, 3):
        if fields[i + 2] == b"lfs":
            lfs.add(fields[i].decode("utf-8", errors="surrogateescape"))
    return lfs


def check_meta_pairs(index: dict[str, str]) -> None:
    asset_paths = [p for p in index if p.startswith(ASSET_ROOT) and not is_hidden_from_unity(p)]
    metas = {p for p in asset_paths if p.endswith(".meta")}
    files = {p for p in asset_paths if not p.endswith(".meta")}

    folders: set[str] = set()
    for path in files:
        parent = os.path.dirname(path)
        while parent and parent != ASSET_ROOT.rstrip("/"):
            folders.add(parent)
            parent = os.path.dirname(parent)

    for path in sorted(files):
        if path + ".meta" not in metas:
            error(path, "asset has no .meta file; Unity would generate one with a GUID that is not in Git")

    for folder in sorted(folders):
        if folder + ".meta" not in metas:
            error(folder, "folder has no .meta file")

    for meta in sorted(metas):
        target = meta[: -len(".meta")]
        if target not in files and target not in folders:
            error(meta, ".meta file has no matching asset or folder; Unity would delete it")


def check_guids(index: dict[str, str]) -> None:
    by_guid: dict[str, list[str]] = defaultdict(list)
    for path, blob in sorted(index.items()):
        if not path.endswith(".meta") or is_hidden_from_unity(path):
            continue
        match = GUID_PATTERN.search(read_blob(blob))
        if match is None:
            error(path, ".meta file has no valid 32-character GUID")
            continue
        by_guid[match.group(1).decode()].append(path)

    for guid, paths in sorted(by_guid.items()):
        if len(paths) > 1:
            for path in paths:
                others = ", ".join(p for p in paths if p != path)
                error(path, f"GUID {guid} is also used by {others}")


def check_lfs(index: dict[str, str], sizes: dict[str, int]) -> None:
    lfs_paths = read_lfs_paths(sorted(index))

    for path in sorted(lfs_paths):
        blob = index[path]
        is_pointer = sizes[blob] < 1024 and read_blob(blob).startswith(LFS_POINTER_PREFIX)
        if not is_pointer:
            error(path, "file type belongs in Git LFS but was committed as a regular file; "
                        "fix with: git add --renormalize -- <path>")

    for path, blob in sorted(index.items()):
        if path not in lfs_paths and sizes[blob] > MAX_REGULAR_FILE_BYTES:
            megabytes = sizes[blob] / (1024 * 1024)
            error(path, f"{megabytes:.1f} MB file is stored outside Git LFS; add its type to .gitattributes")


def check_json(index: dict[str, str]) -> None:
    for path, blob in sorted(index.items()):
        if not (path.endswith(JSON_SUFFIXES) or path in JSON_FILES):
            continue
        try:
            json.loads(read_blob(blob).decode("utf-8-sig"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            error(path, f"invalid JSON: {exc}")


def check_conflict_markers(index: dict[str, str], sizes: dict[str, int], lfs_paths: set[str]) -> None:
    for path, blob in sorted(index.items()):
        if path in lfs_paths or not path.endswith(TEXT_SUFFIXES) or sizes[blob] > MAX_REGULAR_FILE_BYTES:
            continue
        if CONFLICT_PATTERN.search(read_blob(blob)):
            error(path, "contains unresolved merge conflict markers")


CLASS_PATTERN = re.compile(
    r"^[ \t]*(?:\[[^\]\n]*\][ \t]*)*((?:(?:public|internal|private|protected|sealed|abstract|static|partial)[ \t]+)*)"
    r"class[ \t]+(\w+)(?:<[^>\n]*>)?[ \t]*(?::[ \t]*([\w.<>, \t]+))?",
    re.MULTILINE,
)
UNITY_SCRIPT_BASES = {"MonoBehaviour", "ScriptableObject", "StateMachineBehaviour", "EditorWindow", "Editor", "ScriptableSingleton"}


def check_script_file_names(index: dict[str, str], sizes: dict[str, int]) -> None:
    """A concrete class that Unity serializes as a script must be in a file of the same name."""
    classes: dict[str, list[tuple[str, bool, str | None]]] = defaultdict(list)
    for path, blob in sorted(index.items()):
        if not (path.startswith(ASSET_ROOT) and path.endswith(".cs")) or sizes[blob] > MAX_REGULAR_FILE_BYTES:
            continue
        source = re.sub(r"//[^\n]*", "", read_blob(blob).decode("utf-8-sig", errors="replace"))
        for match in CLASS_PATTERN.finditer(source):
            bases = [b.strip().split("<")[0].split(".")[-1] for b in (match.group(3) or "").split(",") if b.strip()]
            classes[match.group(2)].append((path, "abstract" in match.group(1).split(), bases[0] if bases else None))

    def is_unity_script(name: str, seen: frozenset[str] = frozenset()) -> bool:
        if name in UNITY_SCRIPT_BASES:
            return True
        if name in seen or name not in classes:
            return False
        return any(base is not None and is_unity_script(base, seen | {name}) for _, _, base in classes[name])

    for name, declarations in sorted(classes.items()):
        for path, abstract, base in declarations:
            # Test helpers are only created in memory by the tests and never saved, so they may share a file.
            if abstract or base is None or "/Tests/" in path or not is_unity_script(base):
                continue
            if os.path.splitext(os.path.basename(path))[0] != name:
                error(path, f"class {name} derives from a Unity script type but is not in {name}.cs; "
                            "Unity cannot link components or assets to it")


def check_project_layout(index: dict[str, str]) -> None:
    version_file = "ProjectSettings/ProjectVersion.txt"
    if version_file not in index:
        error(version_file, "missing; Unity and CI read the Editor version from this file")
    else:
        match = re.search(rb"^m_EditorVersion:\s*(\S+)", read_blob(index[version_file]), re.MULTILINE)
        if match is None:
            error(version_file, "does not declare m_EditorVersion")
        else:
            print(f"Unity Editor version: {match.group(1).decode()}")

    if "Packages/manifest.json" not in index:
        error("Packages/manifest.json", "missing")

    for path in index:
        top = path.split("/", 1)[0].lower()
        if top in ("library", "temp", "obj", "logs", "usersettings"):
            error(path, "generated Unity folder is tracked by Git; it must stay ignored")


def report() -> int:
    in_actions = os.environ.get("GITHUB_ACTIONS") == "true"

    for level, items in (("warning", warnings), ("error", errors)):
        for path, message in items:
            if in_actions:
                print(f"::{level} file={path}::{message}")
            else:
                print(f"{level.upper()}: {path}: {message}")

    print(f"\n{len(errors)} error(s), {len(warnings)} warning(s).")
    return 1 if errors else 0


def main() -> int:
    top_level = git("rev-parse", "--show-toplevel").decode().strip()
    os.chdir(top_level)

    index = read_index()
    sizes = read_blob_sizes(index)
    print(f"Checking {len(index)} tracked files.")

    check_project_layout(index)
    check_meta_pairs(index)
    check_guids(index)
    check_lfs(index, sizes)
    check_json(index)
    check_conflict_markers(index, sizes, read_lfs_paths(sorted(index)))
    check_script_file_names(index, sizes)
    return report()


if __name__ == "__main__":
    sys.exit(main())
