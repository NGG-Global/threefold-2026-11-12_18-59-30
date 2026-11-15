#!/usr/bin/env python3
"""Reads the third-party VFX packs straight from their Unity YAML and reports what each prefab
contains, so effects can be chosen and adapted from facts rather than from their names.

For every prefab under the pack folders it reports: particle systems, the sum of their particle
limits, simulation spaces, render modes, enabled collision / lights / trails / sub-emitter
modules, Light and AudioSource components, nested prefabs, and every material with the shader
it uses (Shader Graph, hand-written shader, Unity built-in shader, or missing). For each
material it also lists the textures and their import size for the default platform and for
Android, read from the texture's .meta file.

Nothing is modified. The output is Markdown on stdout (or --json for machine use).
Usage: python3 Tools/vfx/analyse_vfx_packs.py > Docs/MagicVfx/PackInventory.md
"""
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PACKS = [
    ("Free Slash VFX", "Assets/Free Slash VFX"),
    ("Hovl Studio Magic Effects FREE", "Assets/Hovl Studio/Magic effects pack"),
    ("Vefects Free Fire VFX URP", "Assets/Vefects/Free Fire VFX URP"),
]

# Unity's built-in resources: shaders referenced by fileID with this guid.
BUILTIN_EXTRA = "0000000000000000f000000000000000"
BUILTIN_SHADERS = {
    "200": "Particles/Standard Unlit (Built-in)",  # fileID values seen in older packs
    "203": "Legacy Shaders/Particles/Alpha Blended (Built-in)",
    "210": "Particles/Standard Surface (Built-in)",
    "211": "Particles/Standard Unlit (Built-in)",
    "10720": "Default-Line (Built-in)",
    "10721": "Default-ParticleSystem (Built-in)",
}
RENDER_MODES = {"0": "billboard", "1": "stretched", "2": "horizontal", "3": "vertical", "4": "mesh", "5": "none"}
SIM_SPACES = {"0": "local", "1": "world", "2": "custom"}

DOC_HEADER = re.compile(r"^--- !u!(\d+) &(-?\d+)", re.M)
GUID_REF = re.compile(r"guid: ([0-9a-f]{32})")


def build_guid_index():
    index = {}
    for base, _, files in os.walk(os.path.join(ROOT, "Assets")):
        for name in files:
            if not name.endswith(".meta"):
                continue
            path = os.path.join(base, name)
            try:
                with open(path, encoding="utf-8", errors="replace") as handle:
                    head = handle.read(400)
            except OSError:
                continue
            match = re.search(r"^guid: ([0-9a-f]{32})", head, re.M)
            if match:
                index[match.group(1)] = os.path.relpath(path[:-5], ROOT)
    return index


def split_documents(text):
    documents = []
    headers = list(DOC_HEADER.finditer(text))
    for i, header in enumerate(headers):
        end = headers[i + 1].start() if i + 1 < len(headers) else len(text)
        documents.append((int(header.group(1)), text[header.end():end]))
    return documents


def module_enabled(body, module):
    match = re.search(r"^  " + module + r":\n(?:    .*\n)*?    enabled: (\d)", body, re.M)
    return match is not None and match.group(1) == "1"


def first(pattern, body, default=None):
    match = re.search(pattern, body, re.M)
    return match.group(1) if match else default


def texture_info(path):
    meta = os.path.join(ROOT, path + ".meta")
    info = {"path": path, "lfs_pointer": False}
    try:
        with open(os.path.join(ROOT, path), "rb") as handle:
            info["lfs_pointer"] = handle.read(40).startswith(b"version https://git-lfs")
    except OSError:
        info["missing_file"] = True
    try:
        with open(meta, encoding="utf-8", errors="replace") as handle:
            text = handle.read()
    except OSError:
        return info
    # Each platform block: buildTarget, maxTextureSize, ..., overridden. The default platform's
    # value is what every platform without an override imports at.
    platforms = {}
    for name, size, overridden in re.findall(
            r"buildTarget: (\w+)\n    maxTextureSize: (\d+)\n(?:    .*\n)*?    overridden: (\d)", text):
        platforms[name] = (int(size), overridden == "1")
    if "DefaultTexturePlatform" in platforms:
        info["max_default"] = platforms["DefaultTexturePlatform"][0]
    else:
        legacy = first(r"^  maxTextureSize: (\d+)", text)
        info["max_default"] = int(legacy) if legacy else None
    if platforms.get("Android", (0, False))[1]:
        info["max_android"] = platforms["Android"][0]
    return info


def material_info(guid, index, cache):
    if guid in cache:
        return cache[guid]
    path = index.get(guid)
    info = {"guid": guid, "path": path}
    if path is None:
        info["missing"] = True
        cache[guid] = info
        return info
    try:
        with open(os.path.join(ROOT, path), encoding="utf-8", errors="replace") as handle:
            text = handle.read()
    except OSError:
        info["unreadable"] = True
        cache[guid] = info
        return info
    shader = re.search(r"m_Shader: \{fileID: (-?\d+), guid: ([0-9a-f]{32})", text)
    if shader:
        file_id, shader_guid = shader.groups()
        if shader_guid == BUILTIN_EXTRA:
            info["shader"] = BUILTIN_SHADERS.get(file_id, "Built-in shader fileID " + file_id)
            info["shader_kind"] = "built-in"
        else:
            shader_path = index.get(shader_guid)
            info["shader"] = shader_path or ("missing shader " + shader_guid)
            if shader_path is None:
                info["shader_kind"] = "missing"
            elif shader_path.endswith(".shadergraph"):
                info["shader_kind"] = "shader graph"
            else:
                info["shader_kind"] = "shader"
    else:
        info["shader_kind"] = "none"
    # Colour properties brighter than 1 are HDR intensities.
    hdr = []
    for name, r, g, b in re.findall(r"- (_\w+): \{r: ([-\d.e]+), g: ([-\d.e]+), b: ([-\d.e]+)", text):
        if not re.search(r"col|tint|emission", name, re.I):
            continue  # vectors such as noise scales are not colours
        peak = max(float(r), float(g), float(b))
        if peak > 1.2:
            hdr.append((name, round(peak, 2)))
    info["hdr_colours"] = hdr
    textures = []
    for tex_guid in re.findall(r"m_Texture: \{fileID: \d+, guid: ([0-9a-f]{32})", text):
        tex_path = index.get(tex_guid)
        textures.append(texture_info(tex_path) if tex_path else {"guid": tex_guid, "missing": True})
    info["textures"] = textures
    cache[guid] = info
    return info


def analyse_prefab(path, index, material_cache):
    with open(os.path.join(ROOT, path), encoding="utf-8", errors="replace") as handle:
        text = handle.read()
    result = {
        "path": path, "particle_systems": 0, "max_particles": 0, "spaces": {}, "render_modes": {},
        "collision": 0, "lights_module": 0, "trails_module": 0, "sub_emitters": 0, "looping": 0,
        "lights": 0, "audio": 0, "trail_renderers": 0, "mesh_renderers": 0, "nested_prefabs": [],
        "materials": [], "longest_seconds": 0.0,
    }
    material_guids = []
    for class_id, body in split_documents(text):
        if class_id == 198:  # ParticleSystem
            result["particle_systems"] += 1
            result["max_particles"] += int(first(r"^    maxNumParticles: (\d+)", body, "0"))
            space = SIM_SPACES.get(first(r"^  moveWithTransform: (\d)", body, "0"), "?")
            result["spaces"][space] = result["spaces"].get(space, 0) + 1
            result["looping"] += first(r"^  looping: (\d)", body, "0") == "1"
            result["longest_seconds"] = max(result["longest_seconds"], float(first(r"^  lengthInSec: ([\d.]+)", body, "0")))
            result["collision"] += module_enabled(body, "CollisionModule")
            result["lights_module"] += module_enabled(body, "LightsModule")
            result["trails_module"] += module_enabled(body, "TrailModule")
            result["sub_emitters"] += module_enabled(body, "SubModule")
        elif class_id == 199:  # ParticleSystemRenderer
            if first(r"^  m_Enabled: (\d)", body, "1") == "1":
                mode = RENDER_MODES.get(first(r"^  m_RenderMode: (\d)", body, "0"), "?")
                result["render_modes"][mode] = result["render_modes"].get(mode, 0) + 1
            material_guids += re.findall(r"^  - \{fileID: 2100000, guid: ([0-9a-f]{32})", body, re.M)
        elif class_id in (23, 96, 120):  # MeshRenderer, TrailRenderer, LineRenderer
            if class_id == 23:
                result["mesh_renderers"] += 1
            else:
                result["trail_renderers"] += 1
            material_guids += re.findall(r"^  - \{fileID: 2100000, guid: ([0-9a-f]{32})", body, re.M)
        elif class_id == 108:
            result["lights"] += 1
        elif class_id == 82:
            result["audio"] += 1
        elif class_id == 1001:
            source = first(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})", body)
            if source:
                result["nested_prefabs"].append(index.get(source, "missing " + source))
            # Overrides of a nested prefab can assign materials too.
            material_guids += re.findall(r"objectReference: \{fileID: 2100000, guid: ([0-9a-f]{32})", body)
    seen = set()
    for guid in material_guids:
        if guid in seen:
            continue
        seen.add(guid)
        result["materials"].append(material_info(guid, index, material_cache))
    return result


def analyse():
    index = build_guid_index()
    cache = {}
    packs = []
    for title, folder in PACKS:
        prefabs = []
        for base, _, files in os.walk(os.path.join(ROOT, folder)):
            for name in sorted(files):
                if name.endswith(".prefab"):
                    prefabs.append(analyse_prefab(os.path.relpath(os.path.join(base, name), ROOT), index, cache))
        prefabs.sort(key=lambda p: p["path"])
        packs.append({"title": title, "folder": folder, "present": os.path.isdir(os.path.join(ROOT, folder)),
                      "prefabs": prefabs})
    return packs


def summarise_shaders(prefab):
    kinds = []
    for material in prefab["materials"]:
        if material.get("missing"):
            kinds.append("**missing material**")
        else:
            kinds.append(material.get("shader_kind", "?"))
    return ", ".join(sorted(set(kinds))) or "none"


def markdown(packs):
    lines = ["# Third-party VFX pack inventory", "",
             "Generated by `Tools/vfx/analyse_vfx_packs.py` from the prefab, material and texture YAML in this",
             "repository. It reads files only; nothing here was seen running in Unity. Texture pixel sizes are",
             "import limits from the `.meta` files: the image files themselves are Git LFS objects.", ""]
    for pack in packs:
        lines.append(f"## {pack['title']}")
        lines.append("")
        if not pack["present"]:
            lines += [f"`{pack['folder']}` is not in this checkout.", ""]
            continue
        lines += ["| Prefab | Systems | Σ max particles | Space | Render | Collision | Lights | Trails | Sub-emitters | Light / Audio | Nested | Shaders |",
                  "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
        for p in pack["prefabs"]:
            name = os.path.relpath(p["path"], pack["folder"])
            spaces = " ".join(f"{k} {v}" for k, v in sorted(p["spaces"].items())) or "-"
            modes = " ".join(f"{k} {v}" for k, v in sorted(p["render_modes"].items())) or "-"
            nested = len(p["nested_prefabs"])
            lines.append(f"| `{name}` | {p['particle_systems']} | {p['max_particles']} | {spaces} | {modes} | "
                         f"{p['collision']} | {p['lights_module']} | {p['trails_module']} | {p['sub_emitters']} | "
                         f"{p['lights']} / {p['audio']} | {nested} | {summarise_shaders(p)} |")
        lines.append("")
        materials = {}
        for p in pack["prefabs"]:
            for m in p["materials"]:
                materials[m["guid"]] = m
        lines += ["Materials used by these prefabs:", "",
                  "| Material | Shader | HDR colours (> 1.2) | Textures (default / Android import limit) |",
                  "| --- | --- | --- | --- |"]
        for m in sorted(materials.values(), key=lambda m: m.get("path") or "~"):
            if m.get("missing"):
                lines.append(f"| missing `{m['guid']}` | - | - | - |")
                continue
            hdr = ", ".join(f"{n} {v}" for n, v in m.get("hdr_colours", [])) or "-"
            textures = []
            listed = set()
            for t in m.get("textures", []):
                key = t.get("path") or t.get("guid")
                if key in listed:
                    continue
                listed.add(key)
                if t.get("missing"):
                    textures.append("missing texture")
                    continue
                android = t.get("max_android")
                default = t.get("max_default")
                textures.append(f"{os.path.basename(t['path'])} {default if default else '?'} / "
                                f"{android if android else 'no override'}")
            shader = m.get("shader", "?")
            lines.append(f"| `{os.path.basename(m['path'])}` | {shader} | {hdr} | {'; '.join(textures) or '-'} |")
        lines.append("")
    return "\n".join(lines) + "\n"


if __name__ == "__main__":
    result = analyse()
    if "--json" in sys.argv:
        json.dump(result, sys.stdout, indent=1)
    else:
        sys.stdout.write(markdown(result))
