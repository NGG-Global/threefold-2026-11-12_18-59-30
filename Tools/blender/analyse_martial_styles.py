"""Measures the martial-style clips in the built Mage model, so the moments of every attack slot and the
claims in Docs/MartialStyles.md come from the motion itself.

Run headless with Blender 4.x, on the exported model (what Unity imports):

    blender -b --factory-startup --python Tools/blender/analyse_martial_styles.py -- \
        Assets/Threefold/Art/Characters/Mage/Mage.fbx <output .json> [<folder for comparison sheets>]

For every attack clip it reports, at 30 frames per second:

- the striking effector's fastest frame and its full reach (furthest forward) within 0.2 s after it;
- when the strike has settled (the effector stays within 5 cm of where the clip leaves it);
- hip and shoulder rotation about the vertical over the clip (degrees), and the largest twist between them;
- how far the hips sink below standing and travel along the ground (largest, and at the end).

For every stance: the height of the hips, the distance between the ankles, and the hands' height.

For every combo it reports the pose gap at each joint, the largest distance of a hand, foot or the head
(relative to the hips) between the pose the previous action leaves and the frame the next one starts on:
stance to first strike, strike to strike (from the frame where the previous strike's recovery ends), and the
last strike back to the stance. Unity cross-fades across these gaps (CombatAnimationSlot.blendInSeconds);
the numbers say how far the blend has to carry the pose.

With a folder, it renders one sheet per discipline for review, written as <folder>/<Discipline>.png: the stance,
then each strike at its wind-up (four frames before contact), contact, and follow-through (six frames after), left to
right and top to bottom, viewed from the front-left. Played-mirrored strikes are shown as authored (the other hand).
"""

import json
import math
import os
import sys

import bpy
from mathutils import Vector

FPS = 30.0
SETTLE_METRES = 0.05
REACH_WINDOW_FRAMES = 6

# Per attack clip: the striking effector(s); how contact is found ("reach": the first frame at full
# extension, within 1.5 cm of the furthest reach in the 0.2 s after the fastest frame, for thrusts and palms;
# "speed": the fastest frame, for swings); the frames the strike is searched in (a clip can go on into a
# faster movement that is not the strike, such as the sinking arc after the Rising Palm); the frame where an
# unwanted second movement starts, if any (recovery must end before it); whether the slot plays it mirrored;
# and its scope as the slots in CombatAnimationDefaults.cs give it (upper: only the arms, head and spine are
# shown, so the pose gaps count only the hands and the head).
ATTACKS = {
    "Arcane_RisingPalm": {"effectors": ["hand_l"], "contact": "reach", "search": (1, 17), "scope": "upper"},
    "Arcane_CirclingPalm": {"effectors": ["hand_l"], "contact": "reach", "search": (1, 17), "mirrored": True,
                            "scope": "upper"},
    "Arcane_Lance": {"effectors": ["hand_l"], "contact": "reach", "search": (1, 20), "scope": "upper"},
    "Flame_Punch": {"effectors": ["hand_r"], "contact": "reach", "scope": "upper"},
    "Flame_Hook": {"effectors": ["hand_r"], "contact": "speed", "scope": "full"},
    "Flame_Flare": {"effectors": ["hand_r"], "contact": "speed", "scope": "full"},
    "Aegis_ForcePalm": {"effectors": ["hand_r"], "contact": "reach", "scope": "full"},
    "Aegis_Forearm": {"effectors": ["hand_l"], "contact": "reach", "scope": "upper"},
    "Aegis_Slam": {"effectors": ["hand_r", "hand_l"], "contact": "speed", "scope": "full"},
}

REACH_TOLERANCE_METRES = 0.015

# Upper-body slots show only these; the legs come from the stance or the locomotion underneath.
UPPER_POINTS = ("hand_l", "hand_r", "Head")

STANCES = {"Arcane": "Arcane_Stance_Loop", "Flame": "Flame_Stance_Loop", "Aegis": "Aegis_Stance_Loop"}

COMBOS = {
    "Arcane": ["Arcane_RisingPalm", "Arcane_CirclingPalm", "Arcane_Lance"],
    "Flame": ["Flame_Punch", "Flame_Hook", "Flame_Flare"],
    "Aegis": ["Aegis_ForcePalm", "Aegis_Forearm", "Aegis_Slam"],
}

POSE_POINTS = ("hand_l", "hand_r", "foot_l", "foot_r", "Head")


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    armature = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    actions = {a.name.split("|")[-1]: a for a in bpy.data.actions}
    return armature, actions


class Frames:
    """World positions of the bones that matter, for every frame of a clip."""

    def __init__(self, armature, action, forward, mirrored=False):
        self.mirrored = mirrored
        scene = bpy.context.scene
        armature.animation_data.action = action
        first, last = (int(round(f)) for f in action.frame_range)
        world = armature.matrix_world
        names = set(POSE_POINTS) | {"pelvis", "spine_03", "thigh_l", "thigh_r", "upperarm_l", "upperarm_r"}
        self.rows = []
        for frame in range(first, last + 1):
            scene.frame_set(frame)
            self.rows.append({n: (world @ armature.pose.bones[n].head).copy() for n in names})
        self.forward = forward
        self.up = Vector((0.0, 0.0, 1.0))
        self.right = self.forward.cross(self.up).normalized()

    def __len__(self):
        return len(self.rows)

    def local(self, index, name, from_hips=False):
        """
        A point relative to the hips' ground point (or to the hips themselves), in the character's frame
        (right, forward, up). For a clip played mirrored, the point is where the character shows it: the other
        side's bone, reflected.
        """
        if self.mirrored:
            name = name[:-2] + ("_r" if name.endswith("_l") else "_l") if name.endswith(("_l", "_r")) else name
        row = self.rows[index]
        pelvis = row["pelvis"]
        origin = pelvis.copy() if from_hips else Vector((pelvis.x, pelvis.y, 0.0))
        offset = row[name] - origin
        result = Vector((offset.dot(self.right), offset.dot(self.forward), offset.z))
        if self.mirrored:
            result.x = -result.x
        return result

    def speed(self, index, name):
        if index == 0:
            return 0.0
        return (self.rows[index][name] - self.rows[index - 1][name]).length * FPS

    def reach(self, index, name):
        return (self.rows[index][name] - self.rows[index]["spine_03"]).dot(self.forward)

    def yaw(self, index, left, right):
        axis = self.rows[index][left] - self.rows[index][right]
        facing = axis.cross(self.up)
        return math.degrees(math.atan2(facing.dot(self.right), facing.dot(self.forward)))


def unwrap(values):
    out = [values[0]]
    for v in values[1:]:
        delta = (v - out[-1] + 180.0) % 360.0 - 180.0
        out.append(out[-1] + delta)
    return out


def analyse_attack(frames, spec, standing_height):
    effectors = spec["effectors"]
    count = len(frames)
    main = effectors[0]
    speeds = [max(frames.speed(i, e) for e in effectors) for i in range(count)]
    first, last = spec.get("search", (1, count))
    searched = range(first - 1, min(count, last))
    fastest = max(searched, key=lambda i: speeds[i])
    contact = fastest
    if spec["contact"] == "reach":
        window = range(fastest, min(count, fastest + REACH_WINDOW_FRAMES + 1))
        furthest = max(frames.reach(i, main) for i in window)
        contact = next(i for i in window if frames.reach(i, main) >= furthest - REACH_TOLERANCE_METRES)
    final = frames.rows[-1][main]
    settled = count - 1
    for i in range(count - 1, contact - 1, -1):
        if (frames.rows[i][main] - final).length > SETTLE_METRES:
            break
        settled = i
    if spec.get("second_movement"):
        settled = min(settled, spec["second_movement"] - 1)

    hips = unwrap([frames.yaw(i, "thigh_l", "thigh_r") for i in range(count)])
    shoulders = unwrap([frames.yaw(i, "upperarm_l", "upperarm_r") for i in range(count)])
    twist = [s - h for s, h in zip(shoulders, hips)]
    pelvis = [r["pelvis"] for r in frames.rows]
    start = Vector((pelvis[0].x, pelvis[0].y, 0.0))
    travel = [(Vector((p.x, p.y, 0.0)) - start).length for p in pelvis]
    span = max(1, count - 1)

    def norm(frame_index):
        return round(frame_index / span, 3)

    return {
        "frames": count,
        "seconds": round((count - 1) / FPS, 3),
        "fastest": {"frame": fastest + 1, "normalized": norm(fastest), "metres_per_second": round(speeds[fastest], 1)},
        "contact": {"frame": contact + 1, "normalized": norm(contact)},
        "settled": {"frame": settled + 1, "normalized": norm(settled)},
        "windup_seconds": round(contact / FPS, 3),
        "hip_rotation_degrees": round(max(hips) - min(hips), 1),
        "shoulder_rotation_degrees": round(max(shoulders) - min(shoulders), 1),
        "largest_twist_degrees": round(max(abs(t - twist[0]) for t in twist), 1),
        "hip_sink_metres": round(standing_height - min(p.z for p in pelvis), 3),
        "largest_travel_metres": round(max(travel), 3),
        "end_travel_metres": round(travel[-1], 3),
    }


def analyse_stance(frames, standing_height):
    heights = [r["pelvis"].z for r in frames.rows]
    width = [(r["foot_l"] - r["foot_r"]).length for r in frames.rows]
    hands = [(r["hand_l"].z + r["hand_r"].z) / 2.0 for r in frames.rows]
    return {
        "frames": len(frames),
        "hip_height_metres": round(sum(heights) / len(heights), 3),
        "hips_below_standing_metres": round(standing_height - sum(heights) / len(heights), 3),
        "ankle_distance_metres": round(sum(width) / len(width), 3),
        "hand_height_metres": round(sum(hands) / len(hands), 3),
        "loop_gap_metres": round(max((frames.local(0, n) - frames.local(len(frames) - 1, n)).length for n in POSE_POINTS), 4),
    }


def pose_gap(a, index_a, b, index_b, points=POSE_POINTS, from_hips=False):
    return max((a.local(index_a, n, from_hips) - b.local(index_b, n, from_hips)).length for n in points)


def joint(previous, previous_index, step, spec, contact_frame, previous_upper=False):
    """
    The gap into a strike at its first frame, and the frame before contact with the smallest gap. When either
    side is an upper-body strike, only the hands and head are compared, from the hips, because in the game the
    legs under an upper-body strike come from the stance.
    """
    upper = spec["scope"] == "upper" or previous_upper
    points = UPPER_POINTS if upper else POSE_POINTS

    def gap(i):
        return pose_gap(previous, previous_index, step, i, points, upper)

    latest = max(0, contact_frame - 1 - 3)  # keep at least three frames of wind-up
    best = min(range(0, latest + 1), key=gap)
    return {"first_frame_metres": round(gap(0), 3), "best_entry_frame": best + 1,
            "best_entry_normalized": round(best / max(1, len(step) - 1), 3), "best_entry_metres": round(gap(best), 3)}


def render_sheets(armature, actions, report, folder):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 16
    scene.cycles.use_denoising = False
    scene.render.resolution_x = 300
    scene.render.resolution_y = 360
    world = bpy.data.worlds.new("Sheet")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.62, 0.66, 0.71, 1.0)
    scene.world = world
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    scene.collection.objects.link(sun)
    camera = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    camera.data.lens = 40
    scene.collection.objects.link(camera)
    scene.camera = camera
    bpy.ops.mesh.primitive_plane_add(size=8)
    os.makedirs(folder, exist_ok=True)
    forward = Vector(report["forward"])
    side = forward.cross(Vector((0, 0, 1))).normalized()
    view = (forward * 0.75 - side * 0.66).normalized()
    shots = []
    for discipline, combo in COMBOS.items():
        picks = [(STANCES[discipline], 1, "stance")]
        for clip in combo:
            data = report["attacks"][clip]
            contact = data["contact"]["frame"]
            picks.append((clip, max(1, contact - 4), "wind-up"))
            picks.append((clip, contact, "contact"))
            picks.append((clip, min(data["frames"], contact + 6), "follow-through"))
        for clip, frame, label in picks:
            armature.animation_data.action = actions[clip]
            scene.frame_set(frame)
            pelvis = armature.matrix_world @ armature.pose.bones["pelvis"].head
            target = Vector((pelvis.x, pelvis.y, 0.9))
            camera.location = target + view * 3.6 + Vector((0, 0, 0.35))
            camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
            path = os.path.join(folder, f"{clip}__{frame:03d}.png")
            scene.render.filepath = path
            bpy.ops.render.render(write_still=True)
            shots.append({"discipline": discipline, "clip": clip, "frame": frame, "label": label, "path": path})
    compose_sheets(shots, folder)


def compose_sheets(shots, folder, columns=5):
    """Tiles each discipline's frames into one image, then deletes the single frames."""
    import numpy

    for discipline in COMBOS:
        mine = [s for s in shots if s["discipline"] == discipline]
        tiles = []
        for shot in mine:
            image = bpy.data.images.load(shot["path"])
            width, height = image.size
            pixels = numpy.array(image.pixels[:], dtype=numpy.float32).reshape(height, width, 4)
            tiles.append(pixels)
            bpy.data.images.remove(image)
        height, width = tiles[0].shape[:2]
        rows = (len(tiles) + columns - 1) // columns
        sheet = numpy.ones((rows * height, columns * width, 4), dtype=numpy.float32)
        for index, tile in enumerate(tiles):
            row, column = divmod(index, columns)
            # Blender's pixel rows run from the bottom up.
            top = (rows - 1 - row) * height
            sheet[top:top + height, column * width:(column + 1) * width] = tile
        out = bpy.data.images.new(f"{discipline}Sheet", columns * width, rows * height, alpha=True)
        out.pixels = sheet.ravel()
        out.filepath_raw = os.path.join(folder, f"{discipline}.png")
        out.file_format = "PNG"
        out.save()
        for shot in mine:
            os.remove(shot["path"])


def main():
    arguments = sys.argv[sys.argv.index("--") + 1:]
    if len(arguments) < 2:
        raise SystemExit("Usage: analyse_martial_styles.py <Mage.fbx> <out.json> [sheets folder]")
    model, output = arguments[:2]
    sheets = arguments[2] if len(arguments) > 2 else None

    armature, actions = load(model)

    # The character's forward is a world axis (the model faces along Y); which way along it is read from the
    # idle pose, ankles towards toes. The pose itself is not used for the direction: the idle stands a little
    # turned, and mirroring is about the character's own axes.
    scene = bpy.context.scene
    armature.animation_data.action = actions["Idle"]
    scene.frame_set(1)
    bones = armature.pose.bones
    world = armature.matrix_world
    toes = (world @ bones["ball_l"].head + world @ bones["ball_r"].head) / 2.0
    ankles = (world @ bones["foot_l"].head + world @ bones["foot_r"].head) / 2.0
    forward = Vector((0.0, 1.0 if (toes - ankles).y >= 0.0 else -1.0, 0.0))
    standing = (world @ bones["pelvis"].head).z

    report = {"forward": list(forward), "standing_hip_height_metres": round(standing, 3), "attacks": {}, "stances": {},
              "transitions": {}}
    cache = {}

    def frames_of(name, mirrored=False):
        key = (name, mirrored)
        if key not in cache:
            cache[key] = Frames(armature, actions[name], forward, mirrored)
        return cache[key]

    for name, spec in ATTACKS.items():
        report["attacks"][name] = analyse_attack(frames_of(name, spec.get("mirrored", False)), spec, standing)
    for discipline, name in STANCES.items():
        report["stances"][discipline] = analyse_stance(frames_of(name), standing)

    for discipline, combo in COMBOS.items():
        stance = frames_of(STANCES[discipline])
        steps = [frames_of(n, ATTACKS[n].get("mirrored", False)) for n in combo]
        joints = []
        for i, name in enumerate(combo):
            contact = report["attacks"][name]["contact"]["frame"]
            joints.append(dict(joint(stance, 0, steps[i], ATTACKS[name], contact), **{"from": "stance", "to": name}))
            if i > 0:
                # The next strike starts when the rules end the previous one's recovery, which its slot maps to
                # the clip's last frame.
                joints.append(dict(joint(steps[i - 1], len(steps[i - 1]) - 1, steps[i], ATTACKS[name], contact,
                                         ATTACKS[combo[i - 1]]["scope"] == "upper"),
                                   **{"from": combo[i - 1], "to": name}))
        last = steps[-1]
        upper = ATTACKS[combo[-1]]["scope"] == "upper"
        joints.append({"from": combo[-1], "to": "stance",
                       "first_frame_metres": round(pose_gap(last, len(last) - 1, stance, 0,
                                                            UPPER_POINTS if upper else POSE_POINTS, upper), 3)})
        report["transitions"][discipline] = joints

    with open(output, "w") as handle:
        json.dump(report, handle, indent=2, sort_keys=True)

    print("[styles] stance: hips below standing / ankles apart / hands at")
    for discipline, data in report["stances"].items():
        print(f"[styles]   {discipline}: {data['hips_below_standing_metres']} m / {data['ankle_distance_metres']} m / "
              f"{data['hand_height_metres']} m, loop gap {data['loop_gap_metres']} m")
    for name, data in report["attacks"].items():
        print(f"[styles] {name}: {data['frames']} frames, fastest {data['fastest']}, contact {data['contact']}, settled "
              f"{data['settled']}, hips {data['hip_rotation_degrees']} deg, shoulders {data['shoulder_rotation_degrees']} deg, "
              f"sink {data['hip_sink_metres']} m, travel {data['largest_travel_metres']} m (end {data['end_travel_metres']} m)")
    for discipline, joints in report["transitions"].items():
        for j in joints:
            best = (f", best entry frame {j['best_entry_frame']} ({j['best_entry_normalized']}): {j['best_entry_metres']} m"
                    if "best_entry_frame" in j else "")
            print(f"[styles] {discipline} {j['from']} -> {j['to']}: first frame {j['first_frame_metres']} m{best}")

    if sheets:
        render_sheets(armature, actions, report, sheets)
        print(f"[styles] sheets in {sheets}")
    print(f"[styles] wrote {output}")


if __name__ == "__main__":
    main()
