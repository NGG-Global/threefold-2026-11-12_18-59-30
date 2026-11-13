"""Builds the Threefold Mage character from the CC0 Universal Animation Library by Quaternius.

Run headless with Blender 4.x:

    blender -b --factory-startup --python Tools/blender/build_mage.py -- \
        <path to UAL1_Standard.fbx> <output .fbx> [<folder for preview renders>]

What it does, all from code so it can be repeated and changed:

1. Imports the library's rigged humanoid (65-bone rig, about 13,700 triangles) and its animations.
2. Dresses the body: each face of the body takes a clothing material according to the bone that
   moves it (tunic on the torso and upper arms, leather bracers, gloves, trousers, knee boots).
3. Adds clothing and gear as extra geometry bound to the skeleton: a hood, a shoulder mantle, a
   short back cloak, a belt with buckle and pouches, a clasp with a glowing crystal and a rune band
   on the left wrist.
4. Keeps only the animations the game uses and gives them plain names.
5. Exports one FBX containing mesh, skeleton and animations, ready for Unity's Humanoid import.
6. Optionally renders preview images, so the result can be reviewed without opening Unity.

The body is the library's mannequin, so this is an intermediate character: it gives the Mage a
real skeleton, animation and a readable silhouette, and it is meant to be replaced by a designed
character model. The Unity side does not depend on this particular mesh.
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

# ----------------------------------------------------------------------------------- settings

# Animations kept from the library, and the names they are given.
ANIMATIONS = {
    "Idle_Loop": "Idle",
    "Walk_Loop": "Walk",
    "Jog_Fwd_Loop": "Jog",
    "Sprint_Loop": "Sprint",
    "Jump_Start": "JumpStart",
    "Jump_Loop": "JumpLoop",
    "Jump_Land": "JumpLand",
    "Spell_Simple_Shoot": "SpellShoot",
}

# Palette from the art bible's provisional families, as sRGB. Unity uses the same values.
PALETTE = {
    "Skin": (0.80, 0.63, 0.52),
    "Tunic": (0.24, 0.35, 0.40),       # slate blue
    "Trousers": (0.27, 0.22, 0.17),    # dark umber
    "Boots": (0.33, 0.21, 0.13),
    "Leather": (0.45, 0.30, 0.17),
    "Gloves": (0.20, 0.15, 0.12),
    "Cloak": (0.53, 0.27, 0.18),       # weathered terracotta
    "Metal": (0.62, 0.50, 0.29),       # muted brass
    "Crystal": (0.55, 0.78, 1.00),     # arcane blue, matches the Arcane Bolt
}

# Which clothing region each bone's area of the body belongs to.
BONE_REGION = {
    "Head": "Skin", "neck_01": "Skin",
    "spine_01": "Tunic", "spine_02": "Tunic", "spine_03": "Tunic",
    "clavicle_l": "Tunic", "clavicle_r": "Tunic",
    "upperarm_l": "Tunic", "upperarm_r": "Tunic",
    "lowerarm_l": "Leather", "lowerarm_r": "Leather",
    "pelvis": "Trousers", "thigh_l": "Trousers", "thigh_r": "Trousers",
    "calf_l": "Boots", "calf_r": "Boots", "foot_l": "Boots", "foot_r": "Boots",
    "ball_l": "Boots", "ball_r": "Boots",
}

MATERIAL_PREFIX = "MAGE_"


# ------------------------------------------------------------------------------------ helpers

def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def make_material(name):
    material = bpy.data.materials.new(MATERIAL_PREFIX + name)
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    r, g, b = (srgb_to_linear(c) for c in PALETTE[name])
    bsdf.inputs["Base Color"].default_value = (r, g, b, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.85
    if name == "Metal":
        bsdf.inputs["Metallic"].default_value = 0.6
        bsdf.inputs["Roughness"].default_value = 0.5
    if name == "Crystal":
        bsdf.inputs["Emission Color"].default_value = (r, g, b, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 2.0
    return material


def region_of_group(name):
    if name in BONE_REGION:
        return BONE_REGION[name]
    if name.endswith(("_l", "_r")) and name.split("_")[0] in ("index", "middle", "pinky", "ring", "thumb"):
        return "Gloves"
    if name.startswith("hand_"):
        return "Gloves"
    return "Tunic"


class Builder:
    """Collects one accessory's geometry and the bone weights of its vertices."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.weights = []  # per vertex: {bone: weight}
        self.material_slots = []

    def slot(self, material_name):
        if material_name not in self.material_slots:
            self.material_slots.append(material_name)
        return self.material_slots.index(material_name)

    def vert(self, co, weights):
        v = self.bm.verts.new(co)
        self.weights.append(dict(weights))
        return v

    def face(self, verts, material_name):
        f = self.bm.faces.new(verts)
        f.material_index = self.slot(material_name)
        return f

    def ring_band(self, centre, rx, ry, z0, z1, segments, weights_bottom, weights_top, material_name,
                  thickness=0.0, rx_top=None, ry_top=None, arc=None, shift=None):
        """A closed (or, with arc, open) band between two elliptical rings around a vertical axis."""
        rx_top = rx if rx_top is None else rx_top
        ry_top = ry if ry_top is None else ry_top
        start, span = (0.0, 2 * math.pi) if arc is None else arc
        closed = arc is None
        count = segments if closed else segments + 1
        bottom, top = [], []
        for i in range(count):
            a = start + span * i / segments
            ox = shift(a) if shift else 0.0
            bottom.append(self.vert((centre.x + math.cos(a) * rx, centre.y + math.sin(a) * ry + ox, z0), weights_bottom))
            top.append(self.vert((centre.x + math.cos(a) * rx_top, centre.y + math.sin(a) * ry_top + ox, z1), weights_top))
        for i in range(segments):
            j = (i + 1) % count
            self.face([bottom[i], bottom[j], top[j], top[i]], material_name)
        return bottom, top

    def ring_stack(self, centre, rings, segments, material_name):
        """Closed rings stacked from bottom to top, each (z, rx, ry, weights), joined into one surface."""
        layers = []
        for z, rx, ry, weights in rings:
            layers.append([self.vert((centre.x + math.cos(2 * math.pi * i / segments) * rx,
                                      centre.y + math.sin(2 * math.pi * i / segments) * ry, z), weights)
                           for i in range(segments)])
        for lower, upper in zip(layers, layers[1:]):
            for i in range(segments):
                j = (i + 1) % segments
                self.face([lower[i], lower[j], upper[j], upper[i]], material_name)
        return layers

    def box(self, centre, size, weights, material_name, rotation=None):
        rotation = rotation or Matrix.Identity(3)
        half = Vector(size) / 2
        corners = []
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    corners.append(self.vert(Vector(centre) + rotation @ Vector((sx * half.x, sy * half.y, sz * half.z)), weights))
        c = corners
        for quad in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
            self.face([c[k] for k in quad], material_name)

    def octahedron(self, centre, radius, height, weights, material_name):
        top = self.vert(Vector(centre) + Vector((0, 0, height)), weights)
        bottom = self.vert(Vector(centre) - Vector((0, 0, height)), weights)
        ring = [self.vert(Vector(centre) + Vector((math.cos(a) * radius, math.sin(a) * radius, 0)), weights)
                for a in (i * math.pi / 2 for i in range(4))]
        for i in range(4):
            j = (i + 1) % 4
            self.face([ring[i], ring[j], top], material_name)
            self.face([ring[j], ring[i], bottom], material_name)

    def to_object(self, armature, materials, solidify=0.0):
        mesh = bpy.data.meshes.new(self.name)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        self.bm.to_mesh(mesh)
        self.bm.free()
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)
        for name in self.material_slots:
            mesh.materials.append(materials[name])
        for index, weights in enumerate(self.weights):
            for bone, weight in weights.items():
                group = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
                group.add([index], weight, "REPLACE")
        if solidify > 0:
            modifier = obj.modifiers.new("Solidify", "SOLIDIFY")
            modifier.thickness = solidify
            modifier.offset = 0.0
            bpy.context.view_layer.objects.active = obj
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.parent = armature
        obj.matrix_parent_inverse = armature.matrix_world.inverted()
        modifier = obj.modifiers.new("Armature", "ARMATURE")
        modifier.object = armature
        return obj


# ----------------------------------------------------------------------------------- building

def measure(body, armature):
    """Bone positions and body cross-sections in world space, in the rest pose."""
    world = armature.matrix_world
    bones = {b.name: (world @ b.head_local, world @ b.tail_local) for b in armature.data.bones}
    vertices = [body.matrix_world @ v.co for v in body.data.vertices]

    def section(z, band=0.02, side=None):
        points = [v for v in vertices if abs(v.z - z) < band and (side is None or side(v))]
        xs = [p.x for p in points]
        ys = [p.y for p in points]
        centre = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, z))
        return centre, (max(xs) - min(xs)) / 2, (max(ys) - min(ys)) / 2

    group_names = {g.index: g.name for g in body.vertex_groups}

    def region_points(bone):
        points = []
        for v in body.data.vertices:
            best = max(v.groups, key=lambda g: g.weight, default=None)
            if best is not None and group_names[best.group] == bone:
                points.append(body.matrix_world @ v.co)
        return points

    return bones, section, region_points


def dress_body(body, materials):
    """Gives every face of the body the material of the clothing region it belongs to."""
    body.data.materials.clear()
    order = list(PALETTE.keys())
    for name in order:
        body.data.materials.append(materials[name])

    group_names = {g.index: g.name for g in body.vertex_groups}
    vertex_region = []
    for v in body.data.vertices:
        best = max(v.groups, key=lambda g: g.weight, default=None)
        vertex_region.append(region_of_group(group_names[best.group]) if best else "Tunic")

    for polygon in body.data.polygons:
        counts = {}
        for index in polygon.vertices:
            counts[vertex_region[index]] = counts.get(vertex_region[index], 0) + 1
        polygon.material_index = order.index(max(counts, key=counts.get))


def build_accessories(body, armature, materials):
    bones, section, region_points = measure(body, armature)

    foot, ball = bones["foot_l"][0], bones["ball_l"][0]
    forward = Vector((ball.x - foot.x, ball.y - foot.y, 0)).normalized()
    behind = -forward.y  # +1 when "behind" is +Y; the library's rest pose faces +Y, so behind is -Y
    parts = []

    # Hood: an egg-shaped shell around the head, open at the face, reaching down over the neck.
    head_points = region_points("Head")
    hx = [p.x for p in head_points]
    hy = [p.y for p in head_points]
    hz = [p.z for p in head_points]
    head_centre = Vector(((min(hx) + max(hx)) / 2, (min(hy) + max(hy)) / 2, (min(hz) + max(hz)) / 2))
    radius = Vector(((max(hx) - min(hx)) / 2, (max(hy) - min(hy)) / 2, (max(hz) - min(hz)) / 2))

    hood = Builder("Hood")
    rings, segments = 10, 16
    grid = []
    for r in range(rings + 1):
        polar = math.pi * r / rings  # 0 at the top
        row = []
        for s in range(segments):
            a = 2 * math.pi * s / segments
            direction = Vector((math.sin(polar) * math.cos(a), math.sin(polar) * math.sin(a), math.cos(polar)))
            scale = Vector((radius.x * 1.22, radius.y * 1.25, radius.z * 1.16))
            point = head_centre + Vector((direction.x * scale.x, direction.y * scale.y, direction.z * scale.z))
            if direction.dot(-forward) > 0.2:   # the back of the hood falls away from the head a little
                point += -forward * 0.025 * direction.dot(-forward)
            if polar > math.pi * 0.7:           # the lower edge rests on the shoulders
                point.z -= 0.05
            row.append(point)
        grid.append(row)
    verts = [[hood.vert(p, {"Head": 1.0}) for p in row] for row in grid]
    for r in range(rings):
        for s in range(segments):
            t = (s + 1) % segments
            corners = [grid[r][s], grid[r][t], grid[r + 1][t], grid[r + 1][s]]
            middle = sum(corners, Vector()) / 4 - head_centre
            facing_front = middle.normalized().dot(forward)
            # Opening for the face, and nothing below the chin at the front.
            if facing_front > 0.35 and middle.z < radius.z * 0.55:
                continue
            if r == rings - 1:
                continue
            hood.face([verts[r][s], verts[r][t], verts[r + 1][t], verts[r + 1][s]], "Cloak")
    parts.append(hood.to_object(armature, materials, solidify=0.012))

    # Mantle: a short flared cape over the shoulders, closed at the front by a clasp.
    neck_z = bones["neck_01"][0].z
    chest_centre, chest_rx, chest_ry = section(bones["spine_03"][0].z + 0.04)
    shoulder_span = abs(bones["upperarm_l"][0].x - bones["upperarm_r"][0].x) / 2
    # Profile: snug at the neck, out over the shoulders, then a short fall that covers the top of
    # the arms. It must reach past the shoulder joints, or they poke through when the arms hang.
    mantle = Builder("Mantle")
    shoulder_z = bones["upperarm_l"][0].z
    top_z = neck_z + 0.01
    bottom_z = bones["spine_03"][0].z - 0.02
    mid = {"spine_03": 1.0}
    shoulder_rx = shoulder_span + 0.065
    shoulder_ry = chest_ry + 0.035
    mantle_centre = Vector((chest_centre.x, chest_centre.y, 0))
    mantle.ring_stack(mantle_centre, [
        (bottom_z, shoulder_rx + 0.02, shoulder_ry + 0.015, mid),
        (shoulder_z - 0.02, shoulder_rx, shoulder_ry, mid),
        (shoulder_z + 0.045, shoulder_rx - 0.06, shoulder_ry - 0.015, mid),
        (top_z, 0.1, 0.09, mid),
    ], 28, "Cloak")
    parts.append(mantle.to_object(armature, materials, solidify=0.012))

    # Clasp and crystal sit on the front of the mantle, just below the collar.
    clasp_z = shoulder_z + 0.03
    clasp_point = Vector((mantle_centre.x, mantle_centre.y, clasp_z)) + forward * (shoulder_ry - 0.005)
    gear = Builder("Gear")
    gear.box(clasp_point, (0.07, 0.02, 0.05), mid, "Metal")
    gear.octahedron(clasp_point + forward * 0.015, 0.018, 0.03, mid, "Crystal")

    # Cloak: a short cape hanging behind, from the mantle to mid-thigh.
    cloak = Builder("Cloak")
    thigh_top, thigh_bottom = bones["thigh_l"]
    hem_z = (thigh_top.z + thigh_bottom.z) / 2
    back_angle = math.atan2(-forward.y, -forward.x)
    arc = (back_angle - math.radians(75), math.radians(150))
    rows = 6
    previous = None
    for row in range(rows + 1):
        t = row / rows
        z = bottom_z + 0.02 + (hem_z - bottom_z - 0.02) * t
        rx = (shoulder_span + 0.07) * (1 - t) + (shoulder_span + 0.02) * t
        ry = (chest_ry + 0.07) * (1 - t) + 0.24 * t
        weights = {"spine_03": max(0.0, 1 - 1.6 * t), "spine_01": min(1.0, 1.6 * t) * (1 - t)}
        if t > 0.6:
            weights["pelvis"] = (t - 0.6) / 0.4
        total = sum(weights.values())
        weights = {k: v / total for k, v in weights.items() if v > 0}
        current = []
        segments = 14
        for i in range(segments + 1):
            a = arc[0] + arc[1] * i / segments
            current.append(cloak.vert((chest_centre.x + math.cos(a) * rx, chest_centre.y + math.sin(a) * ry, z), weights))
        if previous:
            for i in range(segments):
                cloak.face([previous[i], previous[i + 1], current[i + 1], current[i]], "Cloak")
        previous = current
    parts.append(cloak.to_object(armature, materials, solidify=0.01))

    # Belt with buckle and two pouches.
    belt_z = bones["pelvis"][0].z + 0.07
    waist_centre, waist_rx, waist_ry = section(belt_z)
    hips = {"pelvis": 1.0}
    belt = Builder("Belt")
    belt.ring_band(Vector((waist_centre.x, waist_centre.y, 0)), waist_rx + 0.012, waist_ry + 0.012,
                   belt_z - 0.025, belt_z + 0.025, 24, hips, hips, "Leather")
    parts.append(belt.to_object(armature, materials, solidify=0.008))

    front = Vector((waist_centre.x, waist_centre.y, belt_z)) + forward * (waist_ry + 0.02)
    gear.box(front, (0.06, 0.015, 0.055), hips, "Metal")
    side = Vector((forward.y, -forward.x, 0))  # the character's right
    gear.box(Vector((waist_centre.x, waist_centre.y, belt_z - 0.06)) + side * (waist_rx + 0.03) + forward * 0.03,
             (0.05, 0.11, 0.1), hips, "Leather")
    gear.box(Vector((waist_centre.x, waist_centre.y, belt_z - 0.05)) - side * (waist_rx + 0.02) - forward * 0.05,
             (0.04, 0.08, 0.08), hips, "Leather")

    # Rune band: a thin glowing ring on the left wrist, the Mage's casting hand marker.
    elbow, wrist = bones["lowerarm_l"]
    band_point = elbow.lerp(wrist, 0.78)
    arm_points = [p for p in region_points("lowerarm_l") if (p - band_point).length < 0.05]
    band_radius = max((Vector((0, p.y - band_point.y, p.z - band_point.z)).length for p in arm_points), default=0.04)
    axis = (wrist - elbow).normalized()
    rotation = axis.to_track_quat("Z", "Y").to_matrix()
    rune = Builder("RuneBand")
    ring = []
    for i in range(16):
        a = 2 * math.pi * i / 16
        for offset in (-0.008, 0.008):
            local = Vector((math.cos(a) * (band_radius + 0.006), math.sin(a) * (band_radius + 0.006), offset))
            ring.append(rune.vert(band_point + rotation @ local, {"lowerarm_l": 1.0}))
    for i in range(16):
        j = (i + 1) % 16
        rune.face([ring[2 * i], ring[2 * j], ring[2 * j + 1], ring[2 * i + 1]], "Crystal")
    parts.append(rune.to_object(armature, materials, solidify=0.004))

    parts.append(gear.to_object(armature, materials))
    return parts


def keep_animations(armature):
    kept = {}
    for action in list(bpy.data.actions):
        short = action.name.split("|")[-1]
        if short in ANIMATIONS:
            action.name = ANIMATIONS[short]
            action.use_fake_user = True
            kept[action.name] = action
        else:
            bpy.data.actions.remove(action)
    missing = set(ANIMATIONS.values()) - set(kept)
    if missing:
        raise RuntimeError(f"Animations missing from the source file: {sorted(missing)}")
    if armature.animation_data is None:
        armature.animation_data_create()
    armature.animation_data.action = kept["Idle"]
    return kept


# ------------------------------------------------------------------------------------ preview

def render_previews(armature, actions, folder):
    os.makedirs(folder, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    scene.cycles.use_denoising = False  # some Blender builds ship without the denoiser
    scene.render.resolution_x = 480
    scene.render.resolution_y = 640
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("Preview")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.6, 0.66, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
    scene.world = world

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.0
    sun.data.color = (1.0, 0.9, 0.78)
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    scene.collection.objects.link(sun)

    camera = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    camera.data.lens = 60
    scene.collection.objects.link(camera)
    scene.camera = camera

    def shoot(name, action, frame, yaw_degrees):
        armature.animation_data.action = actions[action]
        scene.frame_set(frame)
        yaw = math.radians(yaw_degrees)
        target = Vector((0, 0, 0.95))
        # The character faces +Y in Blender, so a yaw of 0 looks at its front.
        position = target + Vector((math.sin(yaw) * 4.2, math.cos(yaw) * 4.2, 0.35))
        camera.location = position
        camera.rotation_euler = (target - position).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, name + ".png")
        bpy.ops.render.render(write_still=True)

    shoot("mage-front", "Idle", 1, 0)
    shoot("mage-side", "Idle", 1, 90)
    shoot("mage-back", "Idle", 1, 180)
    shoot("mage-three-quarter", "Idle", 20, 35)
    shoot("mage-jog", "Jog", 8, 60)
    shoot("mage-spell", "SpellShoot", 8, 30)


# --------------------------------------------------------------------------------------- main

def main():
    arguments = sys.argv[sys.argv.index("--") + 1:]
    if len(arguments) < 2:
        raise SystemExit("Usage: build_mage.py <UAL1_Standard.fbx> <output.fbx> [preview folder]")
    source, output = arguments[0], arguments[1]
    previews = arguments[2] if len(arguments) > 2 else None

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=source)

    armature = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")
    body.name = "MageBody"
    body.data.name = "MageBody"

    armature.data.pose_position = "REST"
    bpy.context.view_layer.update()

    materials = {name: make_material(name) for name in PALETTE}
    dress_body(body, materials)
    parts = build_accessories(body, armature, materials)

    # One skinned mesh with one material slot per clothing material.
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    for modifier in [m for m in body.modifiers if m.type == "ARMATURE"][1:]:
        body.modifiers.remove(modifier)
    for material in list(bpy.data.materials):
        if material.users == 0:
            bpy.data.materials.remove(material)

    actions = keep_animations(armature)
    armature.data.pose_position = "POSE"
    bpy.context.view_layer.update()

    triangles = sum(len(p.vertices) - 2 for p in body.data.polygons)
    print(f"[build_mage] mesh: {len(body.data.vertices)} vertices, {triangles} triangles, "
          f"materials: {[m.name for m in body.data.materials]}")
    print(f"[build_mage] animations: {sorted(actions)}")

    os.makedirs(os.path.dirname(os.path.abspath(output)), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=output,
        use_selection=False,
        object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        use_armature_deform_only=False,
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.5,
        embed_textures=False,
        path_mode="STRIP",
    )
    print(f"[build_mage] wrote {output} ({os.path.getsize(output)} bytes)")

    if previews:
        render_previews(armature, actions, previews)
        print(f"[build_mage] previews in {previews}")


if __name__ == "__main__":
    main()
