"""Checks the Mage's Wayfarer variant against Mage.fbx, whose animation clips it plays in Unity.

    blender -b --factory-startup --python Tools/blender/check_mage_wayfarer.py -- <Mage.fbx> <MageWayfarer.fbx>

It exits with code 1 unless:

- every bone of Mage.fbx is in the variant with the same parent and the same rest position (0.01 mm),
  so Unity builds the same Humanoid from both and the clips need no adaptation;
- the variant's extra bones are only the expected ones ("sec_" and "face_"), each below a library bone
  or another extra bone, and each moves some vertices;
- the variant carries no animation of its own;
- every vertex is weighted (the build script itself refuses a generated part with more than two bones
  per vertex, the Mobile quality level's limit; the library's body keeps its own weights);
- every texture coordinate lies inside the atlas, clear of the tile borders (no colour bleeding);
- the mesh stays within the triangle budget and has the two expected materials.
"""

import sys

import bpy

ALLOWED_PREFIXES = ("sec_", "face_")
EXPECTED_MATERIALS = ["MAGE_Crystal", "MAGE_Paint"]
TRIANGLE_BUDGET = 20000
TILES_PER_SIDE = 4
CLEAR_BORDER = 0.05     # share of a tile; the build keeps 0.07


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    armature = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    world = armature.matrix_world
    bones = {b.name: (b.parent.name if b.parent else None, (world @ b.head_local).copy()) for b in armature.data.bones}
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    return armature, bones, meshes, len(bpy.data.actions)


def main():
    mage_path, variant_path = sys.argv[sys.argv.index("--") + 1:][:2]
    _, library, _, _ = load(mage_path)
    _, bones, meshes, actions = load(variant_path)
    problems = []

    for name, (parent, head) in library.items():
        if name not in bones:
            problems.append(f"bone '{name}' is missing")
            continue
        other_parent, other_head = bones[name]
        if other_parent != parent:
            problems.append(f"bone '{name}' has parent '{other_parent}', not '{parent}'")
        if (other_head - head).length > 1e-5:
            problems.append(f"bone '{name}' rests {(other_head - head).length * 1000:.3f} mm away")

    extra = sorted(set(bones) - set(library))
    for name in extra:
        if not name.startswith(ALLOWED_PREFIXES):
            problems.append(f"unexpected extra bone '{name}'")
    for name, (parent, _) in library.items():
        if parent in extra:
            problems.append(f"library bone '{name}' hangs below the extra bone '{parent}'")

    if actions:
        problems.append(f"the variant holds {actions} animation(s); Mage.fbx is the only source of clips")

    if len(meshes) != 1:
        problems.append(f"{len(meshes)} meshes, expected one")
    mesh_object = meshes[0]
    mesh = mesh_object.data
    triangles = sum(len(p.vertices) - 2 for p in mesh.polygons)
    materials = sorted(s.material.name for s in mesh_object.material_slots if s.material)
    if triangles > TRIANGLE_BUDGET:
        problems.append(f"{triangles} triangles, over the budget of {TRIANGLE_BUDGET}")
    if materials != EXPECTED_MATERIALS:
        problems.append(f"materials {materials}, expected {EXPECTED_MATERIALS}")

    names = {g.index: g.name for g in mesh_object.vertex_groups}
    used = set()
    most = 0
    for vertex in mesh.vertices:
        groups = [g for g in vertex.groups if g.weight > 0.01]
        most = max(most, len(groups))
        used.update(names[g.group] for g in groups)
        if not groups:
            problems.append(f"vertex {vertex.index} follows no bone")
            break
    for name in extra:
        if name not in used:
            problems.append(f"extra bone '{name}' moves nothing")

    paint_index = [s.material.name for s in mesh_object.material_slots].index("MAGE_Paint")
    layer = mesh.uv_layers.active
    outside = 0
    for polygon in mesh.polygons:
        if polygon.material_index != paint_index:
            continue
        for loop_index in polygon.loop_indices:
            u, v = layer.data[loop_index].uv
            if not (0.0 <= u <= 1.0 and 0.0 <= v <= 1.0):
                outside += 1
                continue
            for value in (u, v):
                within = (value * TILES_PER_SIDE) % 1.0
                if value < 1.0 and (within < CLEAR_BORDER or within > 1.0 - CLEAR_BORDER):
                    outside += 1
                    break
    if outside:
        problems.append(f"{outside} texture coordinate(s) outside the clear part of their tile")

    print(f"[check] bones: {len(library)} library bones matched, extra: {extra}")
    print(f"[check] mesh: {len(mesh.vertices)} vertices, {triangles} triangles, materials {materials}, "
          f"at most {most} bones per vertex")
    for problem in problems:
        print(f"[check] PROBLEM: {problem}")
    print("[check] RESULT: " + ("FAILED" if problems else "the variant fits Mage.fbx's skeleton"))
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
