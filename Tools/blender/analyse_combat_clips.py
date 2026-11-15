"""Measures the combat clips of the Universal Animation Library, so the timing of each animation
slot comes from the motion itself and not from a guess.

Run headless with Blender 4.x:

    blender -b --factory-startup --python Tools/blender/analyse_combat_clips.py -- \
        <UAL1_Standard.fbx> <UAL2_Standard.fbx> <output .json> [<folder for frames> [<clips to render, comma-separated>]]

For every clip in CLIPS it samples each frame and records, for both hands and both feet:

- reach: how far the effector is in front of the chest, along the character's facing (metres);
- speed: how fast the effector moves (metres per second).

The striking effector is the one with the highest peak speed. Two candidate contact moments are
reported, both as frames and as normalised time (0 at the first frame, 1 at the last):

- peak_speed: the frame where the striking effector moves fastest (best for wide swings);
- peak_reach: the frame where it is furthest forward, searched from the peak-speed frame onwards
  within a short window (best for punches and palm strikes, which connect at full extension).

The numbers are a measurement, not a decision: Assets/Threefold/Scripts/Runtime/Animation/
CombatAnimationDefaults.cs records which candidate each slot uses, and why. With a fourth
argument, eight evenly spaced frames of every clip (or of the clips named in a fifth argument) are
rendered (Cycles, CPU) for review. It also reports when each strike has settled (the striking
effector stays within 5 cm of where the clip leaves it), and how far apart the poses are where an
attack clip meets its recovery clip.
"""

import json
import math
import os
import sys

import bpy
from mathutils import Vector

# (library, clip in the library). The library is 1 or 2.
CLIPS = [
    (1, "Punch_Jab"), (1, "Punch_Cross"), (1, "Sword_Attack"), (1, "Sword_Idle"),
    (1, "Spell_Simple_Enter"), (1, "Spell_Simple_Idle_Loop"), (1, "Spell_Simple_Exit"), (1, "Spell_Simple_Shoot"),
    (1, "Hit_Chest"), (1, "Hit_Head"), (1, "Roll"), (1, "Death01"), (1, "Idle_Loop"),
    (2, "Melee_Hook"), (2, "Melee_Hook_Rec"),
    (2, "Sword_Regular_A"), (2, "Sword_Regular_A_Rec"), (2, "Sword_Regular_B"), (2, "Sword_Regular_B_Rec"),
    (2, "Sword_Regular_C"), (2, "Sword_Regular_Combo"), (2, "Sword_Heavy_Combo"), (2, "Sword_Block"), (2, "Sword_Dash"),
    (2, "Idle_Shield_Loop"), (2, "Idle_Shield_Break"), (2, "Shield_OneShot"), (2, "Shield_Dash"),
    (2, "Hit_Knockback"), (2, "NinjaJump_Start"), (2, "NinjaJump_Idle_Loop"), (2, "NinjaJump_Land"), (2, "OverhandThrow"),
    (1, "Push_Loop"), (1, "Interact"), (2, "LayToIdle"),
]

# Clip pairs meant to be played one after the other. The gap between the last pose of the first
# and the first pose of the second is measured, to know whether they can be joined into one clip.
PAIRS = [("Melee_Hook", "Melee_Hook_Rec"), ("Sword_Regular_A", "Sword_Regular_A_Rec"),
         ("Sword_Regular_B", "Sword_Regular_B_Rec")]

# A strike has settled once the striking effector stays this close to where the clip leaves it.
SETTLE_METRES = 0.05

EFFECTORS = ("hand_l", "hand_r", "foot_l", "foot_r")

# The library's characters face +Y in Blender world space once imported.
FORWARD = Vector((0.0, 1.0, 0.0))

# Window after the peak-speed frame in which full extension is looked for, in seconds.
REACH_WINDOW_SECONDS = 0.2


def import_library(path):
    before = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path)
    actions = {}
    for action in bpy.data.actions:
        if action not in before:
            actions[action.name.split("|")[-1].split(".")[0]] = action
    armature = [o for o in bpy.data.objects if o.type == "ARMATURE"][-1]
    return armature, actions


def sample(armature, action, fps):
    armature.animation_data.action = action
    first, last = (int(round(f)) for f in action.frame_range)
    scene = bpy.context.scene
    world = armature.matrix_world
    frames = []
    for frame in range(first, last + 1):
        scene.frame_set(frame)
        bones = armature.pose.bones
        chest = world @ bones["spine_03"].head
        pelvis = world @ bones["pelvis"].head
        points = {name: world @ bones[name].head for name in EFFECTORS}
        frames.append({"frame": frame, "chest": chest.copy(), "pelvis": pelvis.copy(), "points": points})

    result = {"first": first, "last": last, "frames": last - first + 1,
              "seconds": round((last - first) / fps, 4), "effectors": {}}

    for name in EFFECTORS:
        reach, speed = [], []
        for i, f in enumerate(frames):
            reach.append((f["points"][name] - f["chest"]).dot(FORWARD))
            if i == 0:
                speed.append(0.0)
            else:
                speed.append((f["points"][name] - frames[i - 1]["points"][name]).length * fps)
        result["effectors"][name] = {"reach": reach, "speed": speed}

    striker = max(EFFECTORS, key=lambda n: max(result["effectors"][n]["speed"]))
    speed = result["effectors"][striker]["speed"]
    reach = result["effectors"][striker]["reach"]
    peak_speed_index = max(range(len(speed)), key=lambda i: speed[i])
    window = max(1, int(round(REACH_WINDOW_SECONDS * fps)))
    search = range(peak_speed_index, min(len(reach), peak_speed_index + window + 1))
    peak_reach_index = max(search, key=lambda i: reach[i])

    span = max(1, last - first)
    points = [f["points"][striker] for f in frames]
    settle_index = len(points) - 1
    for i in range(len(points) - 1, peak_reach_index - 1, -1):
        if (points[i] - points[-1]).length > SETTLE_METRES:
            break
        settle_index = i

    pelvis_drop = frames[0]["pelvis"].z - min(f["pelvis"].z for f in frames)
    travel = (frames[-1]["pelvis"] - frames[0]["pelvis"]).length

    result.update({
        "striker": striker,
        "peak_speed": {"frame": first + peak_speed_index, "normalized": round(peak_speed_index / span, 3),
                       "metres_per_second": round(speed[peak_speed_index], 2)},
        "peak_reach": {"frame": first + peak_reach_index, "normalized": round(peak_reach_index / span, 3),
                       "metres": round(reach[peak_reach_index], 3)},
        "settled": {"frame": first + settle_index, "normalized": round(settle_index / span, 3)},
        "striker_reach_curve": [round(r, 3) for r in reach],
        "striker_speed_curve": [round(v, 2) for v in speed],
        "rest_reach_metres": round(reach[0], 3),
        "pelvis_drop_metres": round(pelvis_drop, 3),
        "pelvis_travel_metres": round(travel, 3),
    })
    for name in EFFECTORS:
        del result["effectors"][name]
    del result["effectors"]
    result["first_pose"] = {name: list(round(c, 4) for c in frames[0]["points"][name] - frames[0]["pelvis"]) for name in EFFECTORS}
    result["last_pose"] = {name: list(round(c, 4) for c in frames[-1]["points"][name] - frames[-1]["pelvis"]) for name in EFFECTORS}
    return result


def render_frames(armature, action, folder, label):
    scene = bpy.context.scene
    armature.animation_data.action = action
    first, last = (int(round(f)) for f in action.frame_range)
    picks = sorted({first + round((last - first) * i / 7) for i in range(8)})
    for frame in picks:
        scene.frame_set(frame)
        scene.render.filepath = os.path.join(folder, f"{label}__{frame:03d}.png")
        bpy.ops.render.render(write_still=True)


def setup_render():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 6
    scene.cycles.use_denoising = False
    scene.render.resolution_x = 220
    scene.render.resolution_y = 260
    world = bpy.data.worlds.new("Preview")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.6, 0.63, 0.68, 1)
    scene.world = world
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    scene.collection.objects.link(sun)
    camera = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    camera.data.lens = 40
    scene.collection.objects.link(camera)
    scene.camera = camera
    target = Vector((0, 0, 0.95))
    yaw = math.radians(40)
    camera.location = target + Vector((math.sin(yaw) * 4.6, math.cos(yaw) * 4.6, 0.6))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()


def main():
    arguments = sys.argv[sys.argv.index("--") + 1:]
    if len(arguments) < 3:
        raise SystemExit("Usage: analyse_combat_clips.py <UAL1_Standard.fbx> <UAL2_Standard.fbx> <out.json> [frames folder]")
    first_library, second_library, output = arguments[:3]
    frames_folder = arguments[3] if len(arguments) > 3 else None
    only = set(arguments[4].split(",")) if len(arguments) > 4 else None

    bpy.ops.wm.read_factory_settings(use_empty=True)
    armature_1, actions_1 = import_library(first_library)
    # The importer sets the scene's frame rate to the file's (30 for these libraries).
    fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base

    # The second library's rig is identical; its armature and meshes are hidden, its actions are
    # played on the first library's armature so both are measured in the same space.
    objects_before = set(bpy.data.objects)
    _, actions_2 = import_library(second_library)
    for obj in [o for o in bpy.data.objects if o not in objects_before]:
        bpy.data.objects.remove(obj, do_unlink=True)

    if frames_folder:
        os.makedirs(frames_folder, exist_ok=True)
        setup_render()

    report = {"fps": fps, "clips": {}}
    for library, name in CLIPS:
        actions = actions_1 if library == 1 else actions_2
        action = actions.get(name)
        if action is None:
            report["clips"][name] = {"library": library, "missing": True}
            print(f"[analyse] MISSING {name}")
            continue
        entry = sample(armature_1, action, fps)
        entry["library"] = library
        report["clips"][name] = entry
        print(f"[analyse] {name}: {entry['frames']} frames, {entry['seconds']} s, striker {entry['striker']}, "
              f"peak speed {entry['peak_speed']}, peak reach {entry['peak_reach']}")
        if frames_folder and (only is None or name in only):
            render_frames(armature_1, action, frames_folder, name)

    report["pairs"] = {}
    for a, b in PAIRS:
        if "last_pose" not in report["clips"].get(a, {}) or "first_pose" not in report["clips"].get(b, {}):
            continue
        gap = max((Vector(report["clips"][a]["last_pose"][n]) - Vector(report["clips"][b]["first_pose"][n])).length
                  for n in EFFECTORS)
        report["pairs"][f"{a}+{b}"] = {"largest_effector_gap_metres": round(gap, 3)}
        print(f"[analyse] pair {a} -> {b}: largest effector gap {gap:.3f} m")

    with open(output, "w") as handle:
        json.dump(report, handle, indent=2, sort_keys=True)
    print(f"[analyse] wrote {output}")


if __name__ == "__main__":
    main()
