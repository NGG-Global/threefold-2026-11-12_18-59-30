"""Compares two exported Mage models: skeleton, mesh, materials and every animation they share.

Run headless with Blender 4.x after rebuilding the model, to show that a rebuild changed only
what it meant to change:

    blender -b --factory-startup --python Tools/blender/compare_mage.py -- <before.fbx> <after.fbx>

It reports the bones, vertices, triangles and materials of both files, the animations added or
removed, and for every animation in both files the largest difference in any bone's position over
all frames (millimetres). It exits with code 1 if the skeleton or mesh differ, or if a shared
animation moved a bone by more than TOLERANCE_MM.
"""

import sys

import bpy

TOLERANCE_MM = 1.0


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    armature = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    triangles = sum(len(p.vertices) - 2 for m in meshes for p in m.data.polygons)
    vertices = sum(len(m.data.vertices) for m in meshes)
    materials = sorted({s.material.name for m in meshes for s in m.material_slots if s.material})
    bones = [b.name for b in armature.data.bones]

    scene = bpy.context.scene
    world = armature.matrix_world
    animations = {}
    for action in bpy.data.actions:
        name = action.name.split("|")[-1]
        armature.animation_data.action = action
        first, last = (int(round(f)) for f in action.frame_range)
        frames = []
        for frame in range(first, last + 1):
            scene.frame_set(frame)
            frames.append([(world @ armature.pose.bones[b].head).copy() for b in bones])
        animations[name] = frames

    return {"bones": bones, "vertices": vertices, "triangles": triangles, "materials": materials,
            "animations": animations}


def main():
    before_path, after_path = sys.argv[sys.argv.index("--") + 1:][:2]
    before = load(before_path)
    after = load(after_path)
    failed = False

    for key in ("bones", "vertices", "triangles", "materials"):
        same = before[key] == after[key]
        shown = len(before[key]) if key == "bones" else before[key]
        shown_after = len(after[key]) if key == "bones" else after[key]
        print(f"[compare] {key}: {shown} -> {shown_after} {'same' if same else 'DIFFERENT'}")
        failed |= not same

    added = sorted(set(after["animations"]) - set(before["animations"]))
    removed = sorted(set(before["animations"]) - set(after["animations"]))
    print(f"[compare] animations added ({len(added)}): {added}")
    print(f"[compare] animations removed ({len(removed)}): {removed}")
    failed |= bool(removed)

    for name in sorted(set(before["animations"]) & set(after["animations"])):
        a, b = before["animations"][name], after["animations"][name]
        if len(a) != len(b):
            print(f"[compare] {name}: frame count {len(a)} -> {len(b)} DIFFERENT")
            failed = True
            continue
        worst = max((pa - pb).length for fa, fb in zip(a, b) for pa, pb in zip(fa, fb)) * 1000.0
        verdict = "same" if worst <= TOLERANCE_MM else "DIFFERENT"
        print(f"[compare] {name}: {len(a)} frames, largest bone difference {worst:.3f} mm {verdict}")
        failed |= worst > TOLERANCE_MM

    print("[compare] RESULT: " + ("DIFFERENT" if failed else "only additions"))
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
