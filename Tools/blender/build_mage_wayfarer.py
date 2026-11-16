"""Builds the Mage's "Wayfarer" look: a project-owned visual variant on the same skeleton as Mage.fbx.

Run headless with Blender 4.0 (python3-numpy installed):

    blender -b --factory-startup --python Tools/blender/build_mage_wayfarer.py -- \
        <UAL1_Standard.fbx> <output MageWayfarer.fbx> <output MageWayfarer_Paint.png> \
        [<Mage.fbx, for posed previews> <folder for preview renders>]

Docs/Characters/Mage.md describes the result and Docs/CharacterIdentity.md the reasons. In short:

- Mage.fbx is not touched. It keeps the skeleton and every animation clip the game plays. This file
  holds a mesh and a skeleton only; Unity plays Mage.fbx's Humanoid clips on it.
- The body is still the library's mannequin (Quaternius, CC0), so this is still an intermediate
  character: it has no modelled face. What changes is everything built around the body: hair, a face
  drawn with a few marks, an asymmetric layered outfit, hand and forearm wraps, a bracer, boot cuffs,
  a half mantle with the hood worn down and a short split cloak.
- Every surface gets texture coordinates into one painted atlas (written by this script too), so the
  whole character is one painted material plus one glowing one, instead of nine flat colours.
- The skeleton gains a few bones that no animation clip moves: two per cloak tail, two for the hair
  tail, one for the sash end ("sec_" bones, swung at runtime by a light spring) and two for the face
  ("face_blink" for blinking, "face_brows"). The 65 library bones are unchanged.

Nothing here is checked in Unity. The setup in Unity falls back to Mage.fbx's own mesh if this file
does not import as the same Humanoid (MageCharacterSetup).
"""

import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_mage as base  # noqa: E402  (shared helpers: measure, key names of the library rig)

# ----------------------------------------------------------------------------------- settings

# The painted atlas: 4 x 4 tiles. Unity's materials and the previews read the same file.
ATLAS_SIZE = 1024
TILES_PER_SIDE = 4
TILE_MARGIN = 0.07  # share of a tile kept clear at each edge, so neighbours never bleed in

# Base colours as sRGB, from the art bible's provisional palette families.
PAINT = {
    "Skin": (0.82, 0.64, 0.52),
    "Hair": (0.30, 0.17, 0.12),       # dark chestnut
    "Ink": (0.16, 0.10, 0.09),        # the face's drawn marks: the contour line's warm dark
    "Tunic": (0.25, 0.37, 0.42),      # slate blue
    "Trousers": (0.29, 0.24, 0.19),   # dark umber
    "Boots": (0.34, 0.22, 0.14),
    "Leather": (0.46, 0.31, 0.18),
    "Wrap": (0.78, 0.71, 0.58),       # undyed linen
    "Cloak": (0.55, 0.28, 0.19),      # weathered terracotta
    "Trim": (0.72, 0.57, 0.32),       # ochre embroidery
    "Metal": (0.64, 0.52, 0.31),      # muted brass
    "Sash": (0.47, 0.20, 0.17),       # madder red, darker than the cloak
}
TILE_ORDER = list(PAINT.keys())

CRYSTAL = (0.55, 0.78, 1.00)          # arcane blue, as in Mage.fbx
MATERIAL_PAINT = "MAGE_Paint"
MATERIAL_CRYSTAL = "MAGE_Crystal"

# Bones added to the library skeleton. No clip moves them.
SECONDARY_PREFIX = "sec_"
FACE_EYES = "face_blink"
FACE_BROWS = "face_brows"

SEED = 20261115


# ------------------------------------------------------------------------------------ atlas

def tile_origin(key):
    index = TILE_ORDER.index(key)
    return index % TILES_PER_SIDE, index // TILES_PER_SIDE


def atlas_uv(key, u, v):
    """Where (u, v) of a tile (0 to 1 each) lies in the atlas."""
    column, row = tile_origin(key)
    span = 1.0 - 2.0 * TILE_MARGIN
    u = min(1.0, max(0.0, u))
    v = min(1.0, max(0.0, v))
    return ((column + TILE_MARGIN + u * span) / TILES_PER_SIDE,
            (row + TILE_MARGIN + v * span) / TILES_PER_SIDE)


def smooth_noise(rng, size, cells):
    """Soft value noise: a coarse random grid, enlarged smoothly. Values 0 to 1."""
    grid = rng.random((cells + 1, cells + 1))
    position = np.linspace(0.0, cells, size, endpoint=False)
    index = position.astype(int)
    fraction = position - index
    fraction = fraction * fraction * (3.0 - 2.0 * fraction)
    rows = grid[index] * (1.0 - fraction)[:, None] + grid[index + 1] * fraction[:, None]
    return rows[:, index] * (1.0 - fraction)[None, :] + rows[:, index + 1] * fraction[None, :]


def strokes(rng, size, count, length, width, vertical=True):
    """Brush strokes: soft streaks of lighter and darker paint. Values about -1 to 1."""
    canvas = np.zeros((size, size))
    ys, xs = np.mgrid[0:size, 0:size]
    for _ in range(count):
        cx, cy = rng.random() * size, rng.random() * size
        tone = rng.choice((-1.0, 1.0)) * (0.4 + 0.6 * rng.random())
        lean = (rng.random() - 0.5) * 0.5
        along = (ys - cy) + (xs - cx) * lean if vertical else (xs - cx) + (ys - cy) * lean
        across = (xs - cx) - (ys - cy) * lean if vertical else (ys - cy) - (xs - cx) * lean
        streak = np.exp(-(across / width) ** 2) * np.exp(-(along / length) ** 4)
        canvas += streak * tone
    return np.clip(canvas, -1.0, 1.0)


def band(v, centre, half_width, softness=0.01):
    """1 inside a horizontal band of the tile, falling off softly at its edges."""
    return np.clip((half_width - np.abs(v - centre)) / softness + 0.5, 0.0, 1.0)


def paint_tile(key, colour, rng, size):
    """
    One painted swatch. v runs up the garment (0 at its lower edge), u across it. Every tile gets slow
    washes of warmer and cooler, lighter and darker paint, visible strokes along the garment, and a
    little painted shade towards its lower edge; some get drawn lines (wrapping bands, seams, a sole).
    """
    margin = TILE_MARGIN
    axis = (np.arange(size) + 0.5) / size
    u = np.clip((axis[None, :] - margin) / (1.0 - 2.0 * margin), -0.2, 1.2) * np.ones((size, 1))
    v = np.clip((axis[:, None] - margin) / (1.0 - 2.0 * margin), -0.2, 1.2) * np.ones((1, size))

    value = np.zeros((size, size))       # lighter (+) and darker (-)
    warmth = np.zeros((size, size))      # warmer (+) and cooler (-)
    ink = np.zeros((size, size))         # drawn lines, 0 to 1

    wash, stroke_amount, shade = 0.10, 0.06, 0.10
    vertical = True
    if key == "Skin":
        wash, stroke_amount, shade = 0.05, 0.02, 0.05
    elif key == "Ink":
        wash, stroke_amount, shade = 0.03, 0.0, 0.0
    elif key == "Metal":
        wash, stroke_amount, shade = 0.08, 0.05, 0.04
    elif key == "Hair":
        wash, stroke_amount, shade = 0.10, 0.14, 0.12
    elif key == "Wrap":
        vertical = False
    elif key in ("Tunic", "Trousers"):
        stroke_amount = 0.035

    value += (smooth_noise(rng, size, 3) - 0.5) * 2.0 * wash
    value += (smooth_noise(rng, size, 9) - 0.5) * 2.0 * wash * 0.5
    warmth += (smooth_noise(rng, size, 4) - 0.5) * 2.0 * 0.06
    if stroke_amount > 0.0:
        value += strokes(rng, size, 70, size * 0.22, size * 0.012, vertical) * stroke_amount
    value -= shade * (1.0 - np.clip(v, 0.0, 1.0)) ** 2          # painted shade towards the lower edge
    value += 0.03 * np.clip(v, 0.0, 1.0)                         # and a little light towards the top

    if key == "Wrap":
        # Bandage turns: slanted bands, each with a drawn lower edge and a lighter upper face.
        phase = (v * 9.0 + u * 0.9) % 1.0
        ink += np.clip(1.0 - phase / 0.10, 0.0, 1.0) * 0.55
        value += (phase - 0.5) * 0.08
    elif key == "Hair":
        # Strand accents: fine streaks running down the locks.
        strands = np.sin(u * 46.0 + smooth_noise(rng, size, 6) * 5.0)
        value += strands * 0.05
        ink += np.clip(strands - 0.93, 0.0, 1.0) * 5.0 * 0.35
    elif key == "Boots":
        ink += band(v, 0.02, 0.035) * 0.75                       # the sole
        ink += band(v, 0.20, 0.006) * 0.4                        # seam above the foot
        value += band(v, 0.93, 0.07, 0.03) * 0.06                # the cuff's lighter, worn edge
    elif key in ("Tunic", "Cloak", "Trousers", "Sash"):
        # Fold marks: a few soft darker creases and a drawn hem.
        folds = strokes(rng, size, 9, size * 0.30, size * 0.02, True)
        value -= np.clip(folds, 0.0, 1.0) * (0.14 if key == "Cloak" else 0.08)
        ink += band(v, 0.015, 0.012) * 0.45
        if key == "Cloak":
            value += band(v, 0.07, 0.03, 0.04) * 0.07                # the hem, worn lighter above its drawn edge
            value -= band(v, 0.97, 0.10, 0.12) * 0.07                # and darker under the hood and the mantle
        if key == "Tunic":
            ink += band(u, 0.0, 0.006) * 0.3                     # the front closure, down the middle
    elif key == "Leather":
        # Wear: lighter at the edges, where leather rubs.
        edge = np.minimum(np.clip(v, 0.0, 1.0), 1.0 - np.clip(v, 0.0, 1.0))
        value += np.clip(0.08 - edge, 0.0, 0.08) * 1.2
        ink += band(v, 0.06, 0.006) * 0.35 + band(v, 0.94, 0.006) * 0.35   # stitching lines
    elif key == "Trim":
        # Embroidery: a small repeating mark between two drawn lines.
        ink += band(v, 0.12, 0.02) * 0.5 + band(v, 0.88, 0.02) * 0.5
        stitch = (np.abs(((u * 14.0) % 1.0) - 0.5) < 0.18) & (np.abs(v - 0.5) < 0.16)
        value -= stitch * 0.16
    elif key == "Metal":
        value += strokes(rng, size, 40, size * 0.10, size * 0.006, False) * 0.04
        value += band(v, 0.5, 0.12, 0.2) * 0.07                   # a restrained painted highlight

    base_colour = np.array(colour)
    warm = np.array((0.035, 0.0, -0.035))
    pixel = base_colour[None, None, :] * (1.0 + value[:, :, None] * 1.6) + warmth[:, :, None] * warm[None, None, :]
    line = np.array(PAINT["Ink"]) * 0.5 + base_colour * 0.25     # lines are coloured darks, not black
    ink = np.clip(ink, 0.0, 1.0)[:, :, None]
    pixel = pixel * (1.0 - ink) + line[None, None, :] * ink
    return np.clip(pixel, 0.0, 1.0)


def paint_atlas(path):
    rng = np.random.default_rng(SEED)
    tile = ATLAS_SIZE // TILES_PER_SIDE
    atlas = np.zeros((ATLAS_SIZE, ATLAS_SIZE, 4))
    atlas[:, :, 3] = 1.0
    atlas[:, :, :3] = np.array(PAINT["Ink"])[None, None, :]
    for key in TILE_ORDER:
        column, row = tile_origin(key)
        atlas[row * tile:(row + 1) * tile, column * tile:(column + 1) * tile, :3] = paint_tile(key, PAINT[key], rng, tile)

    image = bpy.data.images.new("MageWayfarer_Paint", ATLAS_SIZE, ATLAS_SIZE, alpha=False)
    image.pixels.foreach_set(atlas.astype(np.float32).ravel())   # row 0 is the bottom row, as in UV space
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    return image


# ------------------------------------------------------------------------------------ geometry

class Part:
    """One piece of clothing: vertices with bone weights and tile coordinates, faces with a paint key."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.weights = []
        self.coordinates = []
        self.crystal_faces = set()

    def vert(self, co, weights, uv=(0.5, 0.5)):
        vertex = self.bm.verts.new(co)
        total = sum(weights.values())
        self.weights.append({bone: weight / total for bone, weight in weights.items() if weight > 0.0})
        self.coordinates.append(uv)
        return vertex

    def face(self, verts, key):
        try:
            face = self.bm.faces.new(verts)
        except ValueError:
            return None
        if key == "Crystal":
            face.material_index = 1
        for loop in face.loops:
            u, v = self.coordinates[loop.vert.index] if loop.vert.index >= 0 else (0.5, 0.5)
            loop[self.uv].uv = (0.5, 0.5) if key == "Crystal" else atlas_uv(key, u, v)
        return face

    def index(self):
        self.bm.verts.index_update()

    def grid(self, rows, key, closed=False, key_of_row=None):
        """Joins rows of vertices (each row a list of the same length) into quads."""
        self.index()
        for r in range(len(rows) - 1):
            lower, upper = rows[r], rows[r + 1]
            count = len(lower)
            for i in range(count if closed else count - 1):
                j = (i + 1) % count
                self.face([lower[i], lower[j], upper[j], upper[i]], key_of_row(r) if key_of_row else key)

    def tube(self, points, radii, weights, key, sides=6, cap=True, up=Vector((0.0, 0.0, 1.0)), flatten=1.0):
        """A tapering tube along a line of points: a lock of hair, a strap, a roll. weights: one per point."""
        rows = []
        for i, point in enumerate(points):
            ahead = points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]
            ahead.normalize()
            side = ahead.cross(up)
            if side.length < 1e-4:
                side = ahead.cross(Vector((0.0, 1.0, 0.0)))
            side.normalize()
            other = side.cross(ahead).normalized()
            row = []
            for s in range(sides):
                a = 2.0 * math.pi * s / sides
                offset = side * math.cos(a) * radii[i] + other * math.sin(a) * radii[i] * flatten
                row.append(self.vert(point + offset, weights[i], (abs(a - math.pi) / math.pi, 1.0 - i / (len(points) - 1))))
            rows.append(row)
        self.grid(rows, key, closed=True)
        if cap:
            self.index()
            for row, flip in ((rows[0], True), (rows[-1], False)):
                self.face(list(reversed(row)) if flip else row, key)
        return rows

    def box(self, centre, size, weights, key, rotation=None):
        from mathutils import Matrix
        rotation = rotation or Matrix.Identity(3)
        half = Vector(size) / 2
        corners = []
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    corners.append(self.vert(Vector(centre) + rotation @ Vector((sx * half.x, sy * half.y, sz * half.z)),
                                             weights, ((sx + 1) / 2, (sz + 1) / 2)))
        self.index()
        for quad in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
            self.face([corners[k] for k in quad], key)

    def octahedron(self, centre, radius, height, weights, key):
        top = self.vert(Vector(centre) + Vector((0, 0, height)), weights)
        bottom = self.vert(Vector(centre) - Vector((0, 0, height)), weights)
        ring = [self.vert(Vector(centre) + Vector((math.cos(a) * radius, math.sin(a) * radius, 0)), weights)
                for a in (i * math.pi / 2 for i in range(4))]
        self.index()
        for i in range(4):
            j = (i + 1) % 4
            self.face([ring[i], ring[j], top], key)
            self.face([ring[j], ring[i], bottom], key)

    def face_outwards(self, origin=None):
        """
        Turns every connected piece so that it faces away from a point: the given origin, or the piece's own
        middle. Unity's materials do not draw the back of a face, and a flat or open piece has no inside for
        Blender to work its direction out from, so it is set here rather than guessed.
        """
        self.bm.normal_update()
        remaining = set(self.bm.faces)
        while remaining:
            island, stack = [], [remaining.pop()]
            while stack:
                face = stack.pop()
                island.append(face)
                for edge in face.edges:
                    for other in edge.link_faces:
                        if other in remaining:
                            remaining.remove(other)
                            stack.append(other)
            bmesh.ops.recalc_face_normals(self.bm, faces=island)   # consistent within the piece
            self.bm.normal_update()
            middle = origin if origin is not None else sum((f.calc_center_median() for f in island), Vector()) / len(island)
            facing = sum(f.normal.dot(f.calc_center_median() - middle) * f.calc_area() for f in island)
            if facing < 0.0:
                bmesh.ops.reverse_faces(self.bm, faces=island)
        self.bm.normal_update()

    def to_object(self, armature, materials, solidify=0.0, origin=None):
        mesh = bpy.data.meshes.new(self.name)
        if solidify > 0:
            bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)   # a closed shell after solidify
        else:
            self.face_outwards(origin)
        self.bm.to_mesh(mesh)
        self.bm.free()
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)
        for material in materials:
            mesh.materials.append(material)
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
        # The Mobile quality level skins with two bones per vertex, so no generated vertex may need more.
        most = max((len([g for g in v.groups if g.weight > 0.0]) for v in mesh.vertices), default=0)
        if most > 2:
            raise RuntimeError(f"'{self.name}' has a vertex weighted to {most} bones; the limit is two")
        obj.parent = armature
        obj.matrix_parent_inverse = armature.matrix_world.inverted()
        modifier = obj.modifiers.new("Armature", "ARMATURE")
        modifier.object = armature
        return obj


def smoothstep(edge0, edge1, x):
    t = min(1.0, max(0.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3.0 - 2.0 * t)


def mirrored(angle):
    """An angle as a tile coordinate with no seam: 0 at the front, 1 at the back, the same on both sides."""
    wrapped = (angle - math.pi / 2 + math.pi) % (2 * math.pi) - math.pi
    return abs(wrapped) / math.pi


# ------------------------------------------------------------------------------------ the body

# Which painted region each bone's part of the body belongs to. The outfit is asymmetric: the left
# (casting) arm is bare above wrapped forearm and hand, the right arm is sleeved under the mantle.
BODY_REGION = {
    "Head": "Skin", "neck_01": "Skin",
    "spine_01": "Tunic", "spine_02": "Tunic", "spine_03": "Tunic",
    "clavicle_l": "Tunic", "clavicle_r": "Tunic",
    "upperarm_l": "Tunic", "upperarm_r": "Tunic",
    "lowerarm_l": "Wrap", "lowerarm_r": "Tunic",
    "hand_l": "Wrap", "hand_r": "Wrap",
    "pelvis": "Trousers", "thigh_l": "Trousers", "thigh_r": "Trousers",
    "calf_l": "Boots", "calf_r": "Boots", "foot_l": "Boots", "foot_r": "Boots",
    "ball_l": "Boots", "ball_r": "Boots",
}
LEFT_SLEEVE_ENDS = 0.42   # share of the left upper arm the short sleeve covers


def paint_body(body, bones):
    """Gives every face of the mannequin its painted region and tile coordinates that follow the limb."""
    mesh = body.data
    group_names = {g.index: g.name for g in body.vertex_groups}
    dominant = []
    for vertex in mesh.vertices:
        best = max(vertex.groups, key=lambda g: g.weight, default=None)
        dominant.append(group_names[best.group] if best else "spine_02")

    def region_of(bone):
        if bone in BODY_REGION:
            return BODY_REGION[bone]
        return "Skin"   # fingers: the wraps stop at the knuckles

    world = body.matrix_world
    positions = [world @ v.co for v in mesh.vertices]
    layer = mesh.uv_layers.get("UVMap") or mesh.uv_layers.new(name="UVMap")
    mesh.uv_layers.active = layer
    for extra in [l for l in mesh.uv_layers if l.name != "UVMap"]:
        mesh.uv_layers.remove(extra)
    layer = mesh.uv_layers["UVMap"]

    neck_z = bones["neck_01"][0].z
    pelvis_z = bones["pelvis"][0].z
    knee_z = bones["calf_l"][0].z
    top_z = max(p.z for p in positions)
    elbow_l, shoulder_l = bones["lowerarm_l"][0], bones["upperarm_l"][0]

    def along(point, start, end):
        axis = end - start
        return (point - start).dot(axis) / axis.length_squared

    def around(point, start, end):
        axis = (end - start).normalized()
        radial = (point - start) - axis * (point - start).dot(axis)
        reference = Vector((0.0, 1.0, 0.0)) - axis * axis.y
        if reference.length < 1e-3:
            reference = Vector((0.0, 0.0, 1.0))
        reference.normalize()
        third = axis.cross(reference)
        return abs(math.atan2(radial.dot(third), radial.dot(reference))) / math.pi

    def coordinates(region, bone, point):
        if region in ("Tunic", "Trousers") and bone in ("spine_01", "spine_02", "spine_03", "clavicle_l", "clavicle_r", "pelvis"):
            low, high = (pelvis_z - 0.12, neck_z) if region == "Tunic" else (knee_z, pelvis_z + 0.12)
            return mirrored(math.atan2(point.y, point.x)), (point.z - low) / (high - low)
        if region == "Trousers":
            return around(point, *bones[bone]), (point.z - knee_z) / (pelvis_z + 0.12 - knee_z)
        if region == "Boots":
            return mirrored(math.atan2(point.y, point.x - math.copysign(0.089, point.x))), point.z / (knee_z + 0.02)
        if region == "Skin" and bone in ("Head", "neck_01"):
            return mirrored(math.atan2(point.y, point.x)), (point.z - neck_z) / (top_z - neck_z)
        if bone in bones:
            start, end = bones[bone]
            if bone.startswith(("upperarm", "lowerarm")):
                end = bones[("lowerarm_" if bone.startswith("upperarm") else "hand_") + bone[-1]][0]
            return around(point, start, end), along(point, start, end)
        return 0.5, 0.5

    for polygon in mesh.polygons:
        counts = {}
        for index in polygon.vertices:
            counts[dominant[index]] = counts.get(dominant[index], 0) + 1
        bone = max(counts, key=counts.get)
        region = region_of(bone)
        centre = sum((positions[i] for i in polygon.vertices), Vector()) / len(polygon.vertices)
        if bone == "upperarm_l" and along(centre, shoulder_l, elbow_l) > LEFT_SLEEVE_ENDS:
            region = "Skin"
        polygon.material_index = 0
        polygon.use_smooth = True
        for loop_index in polygon.loop_indices:
            point = positions[mesh.loops[loop_index].vertex_index]
            u, v = coordinates(region, bone, point)
            if bone == "upperarm_l" and region == "Tunic":
                v = 1.0 - along(point, shoulder_l, elbow_l) / LEFT_SLEEVE_ENDS   # the sleeve's hem at the tile's lower edge
            elif bone.startswith(("upperarm", "lowerarm", "hand")):
                v = 1.0 - v
            layer.data[loop_index].uv = atlas_uv(region, u, v)


# ------------------------------------------------------------------------------------ bones

def add_bones(armature, specifications):
    """Adds bones to the skeleton: (name, parent, head, tail) in world space. They deform, no clip moves them."""
    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="EDIT")
    to_local = armature.matrix_world.inverted()
    for name, parent, head, tail in specifications:
        bone = armature.data.edit_bones.new(name)
        bone.head = to_local @ head
        bone.tail = to_local @ tail
        bone.parent = armature.data.edit_bones[parent]
        bone.use_connect = False
        bone.use_deform = True
    bpy.ops.object.mode_set(mode="OBJECT")


# ------------------------------------------------------------------------------------ the outfit

def build_outfit(body, armature, materials):
    bones, section, region_points = base.measure(body, armature)
    positions = [body.matrix_world @ v.co for v in body.data.vertices]
    surface = BVHTree.FromPolygons(positions, [tuple(p.vertices) for p in body.data.polygons])

    forward = Vector((0.0, 1.0, 0.0))    # the library's rest pose faces +Y
    right = Vector((1.0, 0.0, 0.0))      # the character's right (bones ending in _r)
    up = Vector((0.0, 0.0, 1.0))
    parts = []
    new_bones = []

    def at_angle(centre, rx, ry, angle, z):
        return Vector((centre.x + math.cos(angle) * rx, centre.y + math.sin(angle) * ry, z))

    def on_surface(x, z, lift):
        """The point on the front of the body at (x, z), lifted off the surface."""
        hit, normal, _, _ = surface.ray_cast(Vector((x, 1.0, z)), Vector((0.0, -1.0, 0.0)))
        return hit + normal * lift, normal

    # ---- head measurements
    head_points = region_points("Head")
    lo = Vector((min(p.x for p in head_points), min(p.y for p in head_points), min(p.z for p in head_points)))
    hi = Vector((max(p.x for p in head_points), max(p.y for p in head_points), max(p.z for p in head_points)))
    head_centre = (lo + hi) / 2
    radius = (hi - lo) / 2
    head = {"Head": 1.0}

    # ---- hair: a swept-back mass with volume on top and at the back, parted off-centre, with a fringe
    # falling towards the left brow and a short tied tail. Sculpted as grouped locks, not strands.
    hair = Part("Hair")
    rings, segments = 9, 20
    rows = []
    for r in range(rings + 1):
        polar = math.pi * r / rings * 0.86
        row = []
        for s in range(segments):
            a = 2 * math.pi * s / segments
            direction = Vector((math.sin(polar) * math.cos(a), math.sin(polar) * math.sin(a), math.cos(polar)))
            front = direction.dot(forward)
            lump = 1.0 + 0.060 * math.sin(5.0 * a + 1.3) * math.sin(polar * 3.0)
            scale = Vector((radius.x * 1.15, radius.y * 1.14, radius.z * 1.12)) * lump
            point = head_centre + Vector((direction.x * scale.x, direction.y * scale.y, direction.z * scale.z))
            point += up * 0.026 * max(0.0, direction.z) - forward * 0.034 * max(0.0, -front)   # volume up and back
            point += right * 0.012 * max(0.0, direction.z)                                    # swept to one side
            row.append(hair.vert(point, head, (mirrored(a), 1.0 - r / rings)))
        rows.append(row)
    hair.index()
    for r in range(rings):
        for s in range(segments):
            t = (s + 1) % segments
            middle = sum((rows[r][s].co, rows[r][t].co, rows[r + 1][t].co, rows[r + 1][s].co), Vector()) / 4 - head_centre
            front = middle.normalized().dot(forward)
            height = middle.z / radius.z
            # The hairline: high over the forehead, lower over the temples, down to the nape behind.
            hairline = 0.50 if front > 0.55 else (0.50 - (0.55 - front) / 0.35 * 0.85 if front > 0.20 else -0.55)
            if height < hairline:
                continue
            hair.face([rows[r][s], rows[r][t], rows[r + 1][t], rows[r + 1][s]], "Hair")

    brow_z = head_centre.z + radius.z * 0.18
    crown = head_centre + up * radius.z * 1.05 + forward * radius.y * 0.35
    for start_x, end_x, drop, thickness in ((0.030, -0.070, 0.004, 0.024), (0.005, -0.045, 0.018, 0.021),
                                            (0.045, 0.074, 0.030, 0.019), (-0.025, -0.082, 0.045, 0.017)):
        tip, _ = on_surface(end_x, brow_z + drop, 0.016)
        root = crown + right * start_x
        middle = (root + tip) / 2 + forward * 0.040 + up * 0.012
        points = [root, root.lerp(middle, 0.6) + forward * 0.014, middle, middle.lerp(tip, 0.6) + forward * 0.006, tip]
        hair.tube(points, [thickness, thickness * 1.15, thickness, thickness * 0.75, thickness * 0.3],
                  [head] * 5, "Hair", sides=5, flatten=0.55)

    # Locks swept back over the crown and the back of the head. From behind, which is how the player sees
    # the character, the hair was a smooth cap; these give it an outline of its own: uneven, a little
    # wind-blown, higher on the side the fringe is parted from. Each is a short flattened tube that starts
    # inside the mass and ends in a point outside it. They move with the head; only the tail swings.
    for side_x, lift, reach, drop, thickness in ((0.050, 0.012, 0.110, 0.012, 0.030), (0.012, 0.018, 0.125, 0.000, 0.033),
                                                 (-0.034, 0.010, 0.115, 0.016, 0.030), (-0.066, 0.002, 0.090, 0.040, 0.026),
                                                 (0.074, 0.002, 0.086, 0.046, 0.025), (0.026, -0.030, 0.100, 0.070, 0.027),
                                                 (-0.022, -0.036, 0.094, 0.078, 0.025)):
        root = head_centre + right * side_x + up * (radius.z * 0.74 + lift * 0.4) - forward * radius.y * 0.40
        if lift < 0.0:
            root = head_centre + right * side_x - forward * radius.y * 0.80 + up * (radius.z * 0.30 + lift)
        tip = root - forward * reach + up * (lift - drop) + right * side_x * 0.25
        middle = (root + tip) / 2 + up * 0.014 - forward * 0.010
        hair.tube([root, root.lerp(middle, 0.7), middle, middle.lerp(tip, 0.6), tip],
                  [thickness, thickness * 1.2, thickness * 1.05, thickness * 0.7, thickness * 0.18],
                  [head] * 5, "Hair", sides=5, flatten=0.6)

    # The tail: gathered at the back of the head, tied, hanging to the base of the neck.
    back = head_centre - forward * (radius.y * 1.12 + 0.01) - up * radius.z * 0.10
    tail_points = [back + forward * 0.03, back - forward * 0.040 - up * 0.01, back - forward * 0.072 - up * 0.08,
                   back - forward * 0.080 - up * 0.18, back - forward * 0.066 - up * 0.29]
    hair_1, hair_2 = SECONDARY_PREFIX + "hair_01", SECONDARY_PREFIX + "hair_02"
    hair.tube(tail_points, [0.034, 0.040, 0.040, 0.030, 0.008],
              [head, {"Head": 0.5, hair_1: 0.5}, {hair_1: 1.0}, {hair_1: 0.3, hair_2: 0.7}, {hair_2: 1.0}],
              "Hair", sides=6)
    hair.tube([tail_points[1] + up * 0.004, tail_points[1] - forward * 0.016 - up * 0.008], [0.044, 0.044],
              [{"Head": 0.5, hair_1: 0.5}] * 2, "Trim", sides=6, cap=False)
    new_bones += [(hair_1, "Head", tail_points[1], tail_points[3]), (hair_2, hair_1, tail_points[3], tail_points[4])]
    parts.append(hair.to_object(armature, materials, solidify=0.0))

    # ---- the face: a few drawn marks on the mannequin's blank head. Not a modelled face.
    face = Part("Face")
    eye_z = head_centre.z + radius.z * 0.02
    eyes, brows = {FACE_EYES: 1.0}, {FACE_BROWS: 1.0}
    for side in (-1.0, 1.0):
        centre_x = side * radius.x * 0.42
        outline = []
        for i in range(8):                       # an almond: wider than tall, a little higher at the outer corner
            a = 2 * math.pi * i / 8
            x = centre_x + math.cos(a) * 0.0135
            z = eye_z + math.sin(a) * 0.0058 + side * math.cos(a) * 0.0015
            point, _ = on_surface(x, z, 0.0035)
            outline.append(face.vert(point, eyes, (0.5, 0.5)))
        face.index()
        face.face(outline, "Ink")
        strip = []
        for i in range(4):                       # the brow: a short tapering stroke, lower towards the nose
            x = centre_x + side * (-0.020 + 0.040 * i / 3)
            z = eye_z + 0.0185 + 0.004 * math.sin(math.pi * i / 3) + i * 0.0012
            half = 0.0038 * (1.0 - 0.45 * i / 3)
            upper, _ = on_surface(x, z + half, 0.004)
            lower, _ = on_surface(x, z - half, 0.004)
            strip.append((face.vert(lower, brows, (0.5, 0.5)), face.vert(upper, brows, (0.5, 0.5))))
        face.index()
        for (a0, a1), (b0, b1) in zip(strip, strip[1:]):
            face.face([a0, b0, b1, a1], "Ink")
    # The nose: a small wedge, so the profile is not a flat plane. The mouth: one short stroke.
    bridge, _ = on_surface(0.0, eye_z + 0.006, 0.001)
    tip, _ = on_surface(0.0, eye_z - 0.040, 0.017)
    base_l, _ = on_surface(-0.013, eye_z - 0.046, 0.0)
    base_r, _ = on_surface(0.013, eye_z - 0.046, 0.0)
    under, _ = on_surface(0.0, eye_z - 0.052, 0.002)
    nose = [face.vert(p, head, (0.5, 0.75)) for p in (bridge, tip, base_l, base_r, under)]
    face.index()
    for triangle in ((0, 2, 1), (0, 1, 3), (1, 2, 4), (1, 4, 3)):
        face.face([nose[k] for k in triangle], "Skin")
    mouth_z = eye_z - 0.074
    stroke = []
    for i in range(5):
        x = -0.019 + 0.038 * i / 4
        z = mouth_z + 0.0025 * abs(i - 2) / 2          # the corners a little up: approachable, not stern
        upper, _ = on_surface(x, z + 0.0018, 0.003)
        lower, _ = on_surface(x, z - 0.0018, 0.003)
        stroke.append((face.vert(lower, head, (0.5, 0.5)), face.vert(upper, head, (0.5, 0.5))))
    face.index()
    for (a0, a1), (b0, b1) in zip(stroke, stroke[1:]):
        face.face([a0, b0, b1, a1], "Ink")
    parts.append(face.to_object(armature, materials, origin=head_centre))
    eye_centre, _ = on_surface(0.0, eye_z, 0.0)
    new_bones += [(FACE_EYES, "Head", eye_centre, eye_centre + up * 0.03),
                  (FACE_BROWS, "Head", eye_centre + up * 0.021, eye_centre + up * 0.051)]

    # ---- torso measurements
    neck_z = bones["neck_01"][0].z
    shoulder_z = bones["upperarm_l"][0].z
    chest_centre, chest_rx, chest_ry = section(bones["spine_03"][0].z + 0.04)
    shoulder_span = abs(bones["upperarm_l"][0].x - bones["upperarm_r"][0].x) / 2
    centre = Vector((chest_centre.x, chest_centre.y, 0.0))
    chest = {"spine_03": 1.0}
    back_angle = -math.pi / 2

    # ---- half mantle: hangs over the right shoulder and upper arm and narrows to a collar on the left,
    # so the casting arm is free. Its lower rows follow the right arm.
    mantle = Part("Mantle")
    segments = 30
    deep_angle = math.radians(12.0)                      # where it hangs lowest: the right side, a little forward
    full = [(neck_z + 0.012, 0.100, 0.092), (shoulder_z + 0.050, shoulder_span + 0.012, chest_ry + 0.022),
            (shoulder_z - 0.020, shoulder_span + 0.072, chest_ry + 0.040),
            (shoulder_z - 0.105, shoulder_span + 0.088, chest_ry + 0.052),
            (shoulder_z - 0.190, shoulder_span + 0.094, chest_ry + 0.058),
            (shoulder_z - 0.215, shoulder_span + 0.096, chest_ry + 0.060)]
    collar = [(neck_z + 0.012, 0.100, 0.092), (neck_z + 0.004, 0.112, 0.104), (neck_z - 0.004, 0.122, 0.114),
              (neck_z - 0.012, 0.130, 0.122), (neck_z - 0.020, 0.136, 0.128), (neck_z - 0.026, 0.139, 0.131)]
    rows = []
    for r in range(len(full)):
        row = []
        for s in range(segments):
            a = 2 * math.pi * s / segments
            depth = smoothstep(0.18, 0.80, 0.5 + 0.5 * math.cos(a - deep_angle))
            z = collar[r][0] + (full[r][0] - collar[r][0]) * depth
            rx = collar[r][1] + (full[r][1] - collar[r][1]) * depth
            ry = collar[r][2] + (full[r][2] - collar[r][2]) * depth
            arm = max(0.0, math.cos(a)) * depth * (0.0, 0.0, 0.2, 0.4, 0.5, 0.5)[r]
            row.append(mantle.vert(at_angle(centre, rx, ry, a, z), {"spine_03": 1.0 - arm, "upperarm_r": arm},
                                   (mirrored(a), 1.0 - r / (len(full) - 1))))
        rows.append(row)
    rows.reverse()
    mantle.grid(rows, "Cloak", closed=True, key_of_row=lambda r: "Trim" if r == 0 else "Cloak")
    parts.append(mantle.to_object(armature, materials, solidify=0.012))

    # ---- the hood, worn down: a rolled cowl round the neck and its folded bag between the shoulder blades.
    cowl = Part("Cowl")
    cowl_centre = centre - forward * 0.018
    rows = []
    for r, (z, rx, ry) in enumerate(((shoulder_z + 0.040, 0.134, 0.128), (neck_z + 0.012, 0.166, 0.164),
                                     (neck_z + 0.058, 0.156, 0.160), (neck_z + 0.082, 0.116, 0.122),
                                     (neck_z + 0.066, 0.088, 0.092))):
        rows.append([cowl.vert(at_angle(cowl_centre, rx, ry, 2 * math.pi * s / 20, z + 0.014 * max(0.0, -math.sin(2 * math.pi * s / 20))),
                               {"spine_03": 0.8, "neck_01": 0.2} if r > 1 else chest, (mirrored(2 * math.pi * s / 20), r / 4))
                     for s in range(20)])
    cowl.grid(rows, "Cloak", closed=True)
    # The hood itself, lying open on the back: wide at the shoulders, deep enough to stand off the cloak,
    # and drawn down to a point that falls to the left, away from the mantle's side. It is the largest
    # shape between the head and the cloak when the character is seen from behind.
    bag_centre = Vector((-0.012, cowl_centre.y - 0.170, shoulder_z - 0.012))
    rows = []
    for r in range(8):
        along_hood = r / 7
        polar = math.pi * (0.07 + 0.86 * along_hood)
        taper = 1.0 - 0.55 * smoothstep(0.55, 1.0, along_hood)          # narrows towards the point
        lean = -0.050 * smoothstep(0.35, 1.0, along_hood)               # the point falls to the left
        rows.append([cowl.vert(bag_centre + Vector((math.sin(polar) * math.cos(a) * 0.140 * taper + lean,
                                                    math.sin(polar) * math.sin(a) * 0.066 * taper - 0.016 * math.cos(polar),
                                                    math.cos(polar) * 0.165 - 0.035)), chest,
                               (abs(a - math.pi) / math.pi, 1.0 - along_hood))
                     for a in (2 * math.pi * s / 10 for s in range(10))])
    cowl.grid(rows, "Cloak", closed=True)
    cowl.index()
    cowl.face(list(reversed(rows[0])), "Cloak")
    cowl.face(rows[-1], "Cloak")
    parts.append(cowl.to_object(armature, materials))

    # ---- clasp and crystal on the left of the chest, where the mantle's edge crosses.
    gear = Part("Gear")
    clasp, normal = on_surface(-0.060, shoulder_z + 0.012, 0.030)
    gear.box(clasp, (0.05, 0.016, 0.05), chest, "Metal")
    gear.octahedron(clasp + forward * 0.014, 0.016, 0.026, chest, "Crystal")

    # ---- short split cloak: two tails from under the hood, the right one longer. Each hangs from two bones
    # that a light spring swings at runtime; without it they follow the chest.
    cloak = Part("Cloak")
    top_z = shoulder_z - 0.035
    # The first version hung as two flat boards of even width with level hems; from behind the character
    # read as a rectangle. Each tail now widens from the shoulders, then narrows to a slanted hem that is
    # lowest at its outer corner, and its cloth runs in shallow folds, so the outline changes all the way
    # down and the two tails differ. The bones and their weights are as before.
    for side, hem_z, name in ((-1.0, 0.83, "l"), (1.0, 0.61, "r")):
        bone_1, bone_2 = f"{SECONDARY_PREFIX}cloak_{name}_01", f"{SECONDARY_PREFIX}cloak_{name}_02"
        start = back_angle + side * math.radians(3.0)
        span = side * math.radians(62.0)
        rows_count, columns = 7, 8
        rows = []
        middles = []
        for r in range(rows_count + 1):
            t = 0.955 if r == rows_count - 1 else (1.0 if r == rows_count else r / (rows_count - 1) * 0.955)
            z = top_z + (hem_z - top_z) * t
            rx = shoulder_span + 0.030 + 0.075 * t
            ry = chest_ry + 0.062 + (0.228 - chest_ry - 0.062) * smoothstep(0.0, 1.0, t)
            narrowing = 1.0 - 0.30 * smoothstep(0.45, 1.0, t)               # the tail draws in towards its hem
            if t < 0.30:
                weights = {"spine_03": 1.0 - t / 0.30 * 0.7, bone_1: t / 0.30 * 0.7}
            elif t < 0.65:
                weights = {"spine_03": 0.3 * (0.65 - t) / 0.35, bone_1: 1.0 - 0.3 * (0.65 - t) / 0.35}
            else:
                weights = {bone_1: (1.0 - t) / 0.35, bone_2: 1.0 - (1.0 - t) / 0.35}
            row = []
            for c in range(columns + 1):
                across = c / columns                                         # 0 at the split, 1 at the outer edge
                fold = 1.0 + 0.045 * t * math.sin(across * math.pi * 3.0 + (0.6 if side > 0 else 2.1))
                slant = 0.085 * t * across ** 1.5                            # the hem is lowest at the outer corner
                row.append(cloak.vert(at_angle(centre, rx * fold, ry * fold, start + span * across * narrowing, z - slant),
                                      weights, (across, 1.0 - t)))
            rows.append(row if side > 0 else list(reversed(row)))
            middles.append(at_angle(centre, rx, ry, start + span * 0.5 * narrowing, z))
        rows.reverse()
        cloak.grid(rows, "Cloak", key_of_row=lambda r: "Trim" if r == 0 else "Cloak")
        new_bones += [(bone_1, "spine_03", middles[1], middles[4]), (bone_2, bone_1, middles[4], middles[rows_count])]
    parts.append(cloak.to_object(armature, materials, solidify=0.010))

    # ---- belt, sash, tunic skirt
    belt_z = bones["pelvis"][0].z + 0.07
    waist_centre, waist_rx, waist_ry = section(belt_z)
    hip_centre, hip_rx, hip_ry = section(bones["pelvis"][0].z - 0.10)
    waist = Vector((waist_centre.x, waist_centre.y, 0.0))
    hips = {"pelvis": 1.0}

    # The tunic's skirt: hip length, cut on a slant (lower on the left), its hem following the thighs.
    skirt = Part("Skirt")
    segments = 24
    rows = []
    for r in range(5):
        t = (0.0, 0.33, 0.66, 0.90, 1.0)[r]
        row = []
        for s in range(segments):
            a = 2 * math.pi * s / segments
            hem = 0.790 + 0.050 * math.cos(a) + 0.012 * math.sin(a)
            z = (belt_z - 0.015) + (hem - belt_z + 0.015) * t
            rx = waist_rx + 0.014 + (hip_rx + 0.030 - waist_rx - 0.014) * smoothstep(0.0, 0.8, t)
            ry = waist_ry + 0.014 + (hip_ry + 0.034 - waist_ry - 0.014) * smoothstep(0.0, 0.8, t)
            leg = "thigh_r" if math.cos(a) > 0 else "thigh_l"
            share = 0.6 * t * t * min(1.0, abs(math.cos(a)) * 1.6 + 0.35)
            row.append(skirt.vert(at_angle(waist, rx, ry, a, z), {"pelvis": 1.0 - share, leg: share},
                                  (mirrored(a), (1.0 - t) * 0.45)))
        rows.append(row)
    rows.reverse()
    skirt.grid(rows, "Tunic", closed=True, key_of_row=lambda r: "Trim" if r == 0 else "Tunic")
    parts.append(skirt.to_object(armature, materials, solidify=0.008))

    belt = Part("Belt")
    for key, z0, z1, grow, tilt in (("Sash", belt_z - 0.040, belt_z + 0.034, 0.020, 0.018),
                                    ("Leather", belt_z - 0.020, belt_z + 0.016, 0.028, 0.0)):
        rows = []
        for r, z in enumerate((z0, z1)):
            rows.append([belt.vert(at_angle(waist, waist_rx + grow, waist_ry + grow, a, z + tilt * math.cos(a)), hips,
                                   (mirrored(a), r)) for a in (2 * math.pi * s / 24 for s in range(24))])
        belt.grid(rows, key, closed=True)
    parts.append(belt.to_object(armature, materials, solidify=0.008))

    # The sash's knot and hanging end on the right hip, on a bone of its own.
    sash_bone = SECONDARY_PREFIX + "sash_01"
    knot = Vector((waist.x, waist.y, belt_z)) + right * (waist_rx + 0.030) + forward * 0.045
    gear.box(knot, (0.045, 0.045, 0.055), hips, "Sash")
    sash = Part("SashEnd")
    end = knot - up * 0.30 + right * 0.02
    rows = []
    for r in range(6):
        t = (0.0, 0.25, 0.5, 0.75, 0.93, 1.0)[r]
        middle = knot.lerp(end, t) + right * 0.012 * math.sin(math.pi * t)
        half = 0.030 + 0.014 * t
        weights = {"pelvis": 1.0 - min(1.0, t * 2.5), sash_bone: min(1.0, t * 2.5)}
        rows.append([sash.vert(middle + Vector((-0.004, -half, 0.0)), weights, (0.0, 1.0 - t)),
                     sash.vert(middle + Vector((0.004, half, 0.0)), weights, (1.0, 1.0 - t))])
    rows.reverse()
    sash.grid(rows, "Sash", key_of_row=lambda r: "Trim" if r == 0 else "Sash")
    parts.append(sash.to_object(armature, materials, solidify=0.007))
    new_bones.append((sash_bone, "pelvis", knot - up * 0.03, end))

    # Buckle, a pouch on the left hip and a scroll case across the back of the belt.
    buckle = Vector((waist.x, waist.y, belt_z - 0.002)) + forward * (waist_ry + 0.036)
    gear.box(buckle, (0.055, 0.014, 0.05), hips, "Metal")
    gear.box(Vector((waist.x, waist.y, belt_z - 0.060)) - right * (waist_rx + 0.034) + forward * 0.020,
             (0.05, 0.10, 0.095), hips, "Leather")
    gear.box(Vector((waist.x, waist.y, belt_z - 0.022)) - right * (waist_rx + 0.062) + forward * 0.020,
             (0.012, 0.104, 0.03), hips, "Metal")
    case = Vector((waist.x, waist.y, belt_z - 0.045)) - forward * (waist_ry + 0.052)
    gear.tube([case - right * 0.11 + up * 0.02, case + right * 0.11 - up * 0.02], [0.026, 0.026], [hips, hips], "Leather", sides=8)
    for share in (-1.0, 1.0):
        cap = case + (right * 0.11 - up * 0.02) * share
        gear.tube([cap - (right * 0.012 - up * 0.002) * share, cap + (right * 0.012 - up * 0.002) * share],
                  [0.029, 0.029], [hips, hips], "Metal", sides=8)

    # ---- right forearm: a leather bracer with brass bands. Left forearm: wrapped (painted), with the rune band.
    elbow, wrist = bones["lowerarm_r"][0], bones["hand_r"][0]
    forearm = {"lowerarm_r": 1.0}

    def arm_radius(bone, point):
        near = [p for p in region_points(bone) if abs(p.x - point.x) < 0.035]
        return max((Vector((0.0, p.y - point.y, p.z - point.z)).length for p in near), default=0.045)

    start, finish = elbow.lerp(wrist, 0.30), elbow.lerp(wrist, 0.92)
    gear.tube([start, start.lerp(finish, 0.5), finish],
              [arm_radius("lowerarm_r", start) + 0.012, arm_radius("lowerarm_r", start.lerp(finish, 0.5)) + 0.010,
               arm_radius("lowerarm_r", finish) + 0.010], [forearm] * 3, "Leather", sides=10, cap=False)
    for share in (0.0, 1.0):
        point = start.lerp(finish, share)
        gear.tube([point - right * 0.008, point + right * 0.008], [arm_radius("lowerarm_r", point) + 0.017] * 2,
                  [forearm] * 2, "Metal", sides=10, cap=False)

    elbow, wrist = bones["lowerarm_l"][0], bones["hand_l"][0]
    point = elbow.lerp(wrist, 0.80)
    gear.tube([point + right * 0.008, point - right * 0.008], [arm_radius("lowerarm_l", point) + 0.011] * 2,
              [{"lowerarm_l": 1.0}] * 2, "Crystal", sides=12, cap=False)

    # ---- boots: a folded cuff below the knee and a strap at the ankle.
    for name in ("l", "r"):
        knee, ankle = bones[f"calf_{name}"][0], bones[f"foot_{name}"][0]
        shin = {f"calf_{name}": 1.0}
        leg_centre = Vector((knee.x, 0.0, 0.0))

        def leg_section(z):
            centre_at, rx, ry = section(z, side=lambda p, x=knee.x: abs(p.x - x) < 0.085)
            return Vector((centre_at.x, centre_at.y, 0.0)), rx, ry

        top = ankle.z + (knee.z - ankle.z) * 0.80
        rows = []
        for r, (z, grow) in enumerate(((top - 0.075, 0.016), (top - 0.010, 0.026), (top, 0.022), (top - 0.004, 0.010))):
            middle, rx, ry = leg_section(min(z, top - 0.01))
            rows.append([gear.vert(at_angle(middle, rx + grow, ry + grow, a, z), shin, (mirrored(a), 0.80 + 0.2 * min(1.0, r / 2)))
                         for a in (2 * math.pi * s / 10 for s in range(10))])
        gear.grid(rows, "Boots", closed=True)
        middle, rx, ry = leg_section(ankle.z + 0.035)
        rows = [[gear.vert(at_angle(middle, rx + 0.010, ry + 0.010, a, ankle.z + 0.035 + dz), shin, (mirrored(a), r))
                 for a in (2 * math.pi * s / 10 for s in range(10))] for r, dz in enumerate((-0.012, 0.012))]
        gear.grid(rows, "Leather", closed=True)
        _ = leg_centre

    parts.append(gear.to_object(armature, materials))
    return parts, new_bones


# ------------------------------------------------------------------------------------ preview

def render_previews(armature, body, mage_path, folder):
    """Renders the variant in the game's poses, with Mage.fbx's clips, and a sheet beside the previous look."""
    os.makedirs(folder, exist_ok=True)
    actions = {}
    if mage_path:
        before_objects = set(bpy.data.objects)
        before_actions = set(bpy.data.actions)
        bpy.ops.import_scene.fbx(filepath=mage_path)
        for action in [a for a in bpy.data.actions if a not in before_actions]:
            actions[action.name.split("|")[-1]] = action
        for obj in [o for o in bpy.data.objects if o not in before_objects]:
            bpy.data.objects.remove(obj, do_unlink=True)
        if armature.animation_data is None:
            armature.animation_data_create()

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    scene.cycles.use_denoising = False
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

    def shoot(name, action, frame, yaw_degrees, target_z=0.95, distance=4.2, size=(480, 640)):
        if action in actions:
            armature.animation_data.action = actions[action]
        scene.frame_set(frame)
        scene.render.resolution_x, scene.render.resolution_y = size
        yaw = math.radians(yaw_degrees)
        target = Vector((0, 0, target_z))
        position = target + Vector((math.sin(yaw) * distance, math.cos(yaw) * distance, 0.35 * distance / 4.2))
        camera.location = position
        camera.rotation_euler = (target - position).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, name + ".png")
        bpy.ops.render.render(write_still=True)

    shoot("wayfarer-front", "Idle", 1, 0)
    shoot("wayfarer-three-quarter", "Idle", 20, 35)
    shoot("wayfarer-side", "Idle", 1, 90)
    shoot("wayfarer-back", "Idle", 1, 180)
    shoot("wayfarer-head", "Idle", 1, 25, target_z=1.66, distance=1.25, size=(480, 480))
    shoot("wayfarer-jog", "Jog", 8, 60)
    shoot("wayfarer-arcane", "Arcane_Stance_Loop", 1, 30)
    shoot("wayfarer-flame", "Flame_Stance_Loop", 1, 30)
    shoot("wayfarer-aegis", "Aegis_Stance_Loop", 1, 30)
    shoot("wayfarer-strike", "Flame_Hook", 9, 40)
    shoot("wayfarer-hang", "Hang", 1, 160)


# --------------------------------------------------------------------------------------- main

def main():
    arguments = sys.argv[sys.argv.index("--") + 1:]
    if len(arguments) < 3:
        raise SystemExit("Usage: build_mage_wayfarer.py <UAL1_Standard.fbx> <output.fbx> <output paint.png> "
                         "[<Mage.fbx> <preview folder>]")
    source, output, paint_path = arguments[0], arguments[1], os.path.abspath(arguments[2])
    mage_path = arguments[3] if len(arguments) > 4 else None
    previews = arguments[4] if len(arguments) > 4 else None

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=source)
    armature = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")
    body.name = "MageBody"
    body.data.name = "MageBody"
    for action in list(bpy.data.actions):       # this file carries no animation: Mage.fbx has the clips
        bpy.data.actions.remove(action)
    armature.animation_data_clear()
    armature.data.pose_position = "REST"
    bpy.context.view_layer.update()
    library_bones = [b.name for b in armature.data.bones]

    image = paint_atlas(paint_path)
    paint = bpy.data.materials.new(MATERIAL_PAINT)
    paint.use_nodes = True
    bsdf = paint.node_tree.nodes.get("Principled BSDF")
    texture = paint.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = image
    paint.node_tree.links.new(texture.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.9
    crystal = bpy.data.materials.new(MATERIAL_CRYSTAL)
    crystal.use_nodes = True
    bsdf = crystal.node_tree.nodes.get("Principled BSDF")
    colour = tuple(base.srgb_to_linear(c) for c in CRYSTAL) + (1.0,)
    bsdf.inputs["Base Color"].default_value = colour
    bsdf.inputs["Emission Color"].default_value = colour
    bsdf.inputs["Emission Strength"].default_value = 2.0
    materials = [paint, crystal]

    body.data.materials.clear()
    for material in materials:
        body.data.materials.append(material)
    bones, _, _ = base.measure(body, armature)
    paint_body(body, bones)
    parts, new_bones = build_outfit(body, armature, materials)
    add_bones(armature, new_bones)

    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        for polygon in part.data.polygons:
            polygon.use_smooth = True
        part.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    for modifier in [m for m in body.modifiers if m.type == "ARMATURE"][1:]:
        body.modifiers.remove(modifier)
    for material in list(bpy.data.materials):
        if material.users == 0:
            bpy.data.materials.remove(material)
    if hasattr(body.data, "use_auto_smooth"):    # Blender 4.0; later versions smooth by angle differently
        body.data.use_auto_smooth = True
        body.data.auto_smooth_angle = math.radians(55.0)

    armature.data.pose_position = "POSE"
    bpy.context.view_layer.update()

    added = [b.name for b in armature.data.bones if b.name not in library_bones]
    unweighted = [name for name in added if name not in body.vertex_groups]
    if unweighted:
        raise RuntimeError(f"Added bones that move nothing: {unweighted}")
    triangles = sum(len(p.vertices) - 2 for p in body.data.polygons)
    print(f"[build_mage_wayfarer] mesh: {len(body.data.vertices)} vertices, {triangles} triangles, "
          f"materials: {[m.name for m in body.data.materials]}")
    print(f"[build_mage_wayfarer] bones: {len(library_bones)} from the library, added: {added}")

    os.makedirs(os.path.dirname(os.path.abspath(output)), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=output,
        use_selection=False,
        object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="OFF",
        add_leaf_bones=False,
        use_armature_deform_only=False,
        bake_anim=False,
        embed_textures=False,
        path_mode="STRIP",
    )
    print(f"[build_mage_wayfarer] wrote {output} ({os.path.getsize(output)} bytes) and {paint_path}")

    if previews:
        render_previews(armature, body, mage_path, previews)
        print(f"[build_mage_wayfarer] previews in {previews}")


if __name__ == "__main__":
    main()
