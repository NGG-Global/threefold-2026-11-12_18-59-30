"""The three disciplines' martial styles, derived from the Universal Animation Library by Quaternius.

Imported by build_mage.py (Blender 4.x). Docs/MartialStyles.md explains every choice; this module only
carries the choices out.

Every clip made here starts from motion that an animator authored in the library. The changes are the
kind an animator makes when adapting a library clip, never a whole sequence generated from nothing:

- cut: a range of frames, optionally retimed (a stretch of frames played over more frames);
- join: one clip followed by another; a small pose gap at the join is spread over the first frames of
  the second clip, so neither the join nor the second clip's own motion jumps;
- open hand: the finger rotations of the library's own open hand (the casting hand of
  Spell_Simple_Idle_Loop), blended in and out, for palm strikes; or straight fingers for a spear hand;
- wrist: the hand bent back (a "standing palm") around the contact;
- turn and lean: a few degrees of extra rotation of the spine, spread over its three bones, timed with
  a smooth bump around the contact, for hip and shoulder rotation and for anticipation;
- arm swing: an extra rotation of an upper arm about the vertical, to bend a straight path into an arc;
- flipped arm: one arm's authored motion copied, mirrored, onto the other arm (two-handed strike);
- stance: the breathing of an authored idle loop, with the legs or the arms of an authored pose;
- wider stance: the thighs turned outwards at the hips, the feet turned back so the soles stay flat, and
  the body lowered by as much as the feet rose, so they stay on the ground;
- from and back to the stance: every strike's wind-up starts from its discipline's stance and its
  recovery ends in it (the blend runs while the authored wind-up gathers and while the authored recovery
  settles; the fast part of the strike is untouched), so strikes chain and return without a jump.

The names of the new clips are listed in STYLE_CLIPS. Clips ending in "_Loop" loop in Unity
(CharacterModelPostprocessor). The source clips used only here are removed before export.
"""

import math

import bpy
from mathutils import Matrix, Quaternion, Vector

# Library clips needed only as sources for the clips below (not exported themselves).
SOURCE_ANIMATIONS = ("Interact",)
SOURCE_ANIMATIONS_2 = ("Melee_Hook_Rec", "Sword_Regular_A", "Sword_Regular_A_Rec")

# The clips this module adds to the model, in the order they are built.
STYLE_CLIPS = (
    "Arcane_Stance_Loop", "Arcane_RisingPalm", "Arcane_CirclingPalm", "Arcane_Lance",
    "Flame_Stance_Loop", "Flame_Punch", "Flame_Hook", "Flame_Flare",
    "Aegis_Stance_Loop", "Aegis_ForcePalm", "Aegis_Forearm", "Aegis_Slam",
)

FINGERS = ("index", "middle", "ring", "pinky", "thumb")
ARM_CHAIN = ("clavicle", "upperarm", "lowerarm", "hand")
LEG_CHAIN = ("thigh", "calf", "foot", "ball")
SPINE = (("spine_01", 0.4), ("spine_02", 0.3), ("spine_03", 0.3))
TORSO = ("spine_01", "spine_02", "spine_03", "neck_01", "Head")

# Armature space: the armature object is turned 180 degrees about Z, so the character faces -Y here
# and up is +Z.
UP = Vector((0.0, 0.0, 1.0))
FORWARD = Vector((0.0, -1.0, 0.0))


# ----------------------------------------------------------------------------------- curves

def smoothstep(edge0, edge1, x):
    if edge1 == edge0:
        return 1.0 if x >= edge1 else 0.0
    t = min(1.0, max(0.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3.0 - 2.0 * t)


def bump(frame, start, peak, end):
    """0 before start, rising smoothly to 1 at peak, falling smoothly to 0 at end."""
    if frame <= peak:
        return smoothstep(start, peak, frame)
    return 1.0 - smoothstep(peak, end, frame)


def ramp(frame, rise_start, rise_end, fall_start=None, fall_end=None):
    """Rises smoothly to 1, holds, then optionally falls smoothly back to 0."""
    value = smoothstep(rise_start, rise_end, frame)
    if fall_start is not None:
        value *= 1.0 - smoothstep(fall_start, fall_end, frame)
    return value


# ----------------------------------------------------------------------------------- poses

def side_bones(names, side):
    return [f"{name}_{side}" for name in names]


def finger_bones(side):
    bones = []
    for finger in FINGERS:
        bones += [f"{finger}_0{i}_{side}" for i in (1, 2, 3)]
    return bones


def flip_name(name):
    if name.endswith("_l"):
        return name[:-2] + "_r"
    if name.endswith("_r"):
        return name[:-2] + "_l"
    return name


def flip_basis(basis):
    """The local transform of a bone mirrored across the character's centre (Blender's paste-flipped)."""
    location, rotation, scale = basis
    return (Vector((-location.x, location.y, location.z)),
            Quaternion((rotation.w, rotation.x, -rotation.y, -rotation.z)), scale.copy())


class Sampler:
    """Reads the local transform of every bone of a clip at a (possibly fractional) frame."""

    def __init__(self, armature):
        self.armature = armature
        self.scene = bpy.context.scene

    def sample(self, action, frame):
        self.armature.animation_data.action = action
        whole = int(math.floor(frame))
        self.scene.frame_set(whole, subframe=frame - whole)
        pose = {}
        for bone in self.armature.pose.bones:
            pose[bone.name] = (bone.location.copy(), bone.rotation_quaternion.copy(), bone.scale.copy())
        return pose

    def frames(self, action, first, last, count=None):
        """Samples first..last. With a count, that many evenly spaced samples (a retime)."""
        if count is None:
            return [self.sample(action, f) for f in range(first, last + 1)]
        return [self.sample(action, first + (last - first) * i / (count - 1)) for i in range(count)]


def join(first, second, settle_frames=0):
    """
    The second clip after the first. With settle frames, the difference between the first clip's last pose
    and the second clip's first pose is added to the second clip and faded out over that many frames, so
    the join does not jump and the second clip keeps its own motion after them.
    """
    if settle_frames <= 0:
        return first + second
    last = first[-1]
    start = second[0]
    settled = []
    for i, pose in enumerate(second):
        weight = 1.0 - smoothstep(0.0, float(settle_frames), float(i))
        if weight <= 0.0:
            settled.append(pose)
            continue
        adjusted = {}
        for name, (loc, rot, scale) in pose.items():
            delta_loc = last[name][0] - start[name][0]
            delta_rot = last[name][1] @ start[name][1].inverted()
            partial = Quaternion().slerp(delta_rot, weight)
            adjusted[name] = (loc + delta_loc * weight, partial @ rot, scale)
        settled.append(adjusted)
    return first + settled


# ----------------------------------------------------------------------------------- editing

class Editor:
    """
    Changes a list of poses frame by frame. Each frame is posed on the armature, edited in armature space
    (rotations about a bone's head, as an animator would rotate it), and read back as local transforms.
    """

    def __init__(self, armature, scratch):
        self.armature = armature
        self.scratch = scratch  # an empty action, so posing does not touch any real clip

    def _apply(self, pose):
        self.armature.animation_data.action = self.scratch
        for bone in self.armature.pose.bones:
            loc, rot, scale = pose[bone.name]
            bone.location = loc
            bone.rotation_mode = "QUATERNION"
            bone.rotation_quaternion = rot
            bone.scale = scale
        bpy.context.view_layer.update()

    def _read(self):
        return {b.name: (b.location.copy(), b.rotation_quaternion.copy(), b.scale.copy()) for b in self.armature.pose.bones}

    def rotate(self, bone_name, axis, degrees):
        """Rotates a bone about an armature-space axis through its head. Children follow."""
        if abs(degrees) < 1e-4:
            return
        bone = self.armature.pose.bones[bone_name]
        current = bone.matrix.copy()
        head = current.to_translation()
        rotation = Matrix.Rotation(math.radians(degrees), 4, axis.normalized())
        bone.matrix = Matrix.Translation(head) @ rotation @ Matrix.Translation(-head) @ current
        bpy.context.view_layer.update()

    def head(self, bone_name):
        return self.armature.pose.bones[bone_name].head.copy()

    def translate(self, bone_name, offset):
        """Moves a bone by an armature-space offset. Children follow."""
        bone = self.armature.pose.bones[bone_name]
        bone.matrix = Matrix.Translation(offset) @ bone.matrix
        bpy.context.view_layer.update()

    def edit(self, poses, function):
        """Calls function(editor, index) with each pose applied; returns the edited poses."""
        edited = []
        for index, pose in enumerate(poses):
            self._apply(pose)
            function(self, index)
            edited.append(self._read())
        return edited


def blend_local(pose, bones, target, weight):
    """Moves some bones' local rotations towards a target pose by weight (0 to 1), in place."""
    if weight <= 0.0:
        return
    for name in bones:
        loc, rot, scale = pose[name]
        pose[name] = (loc, rot.slerp(target[name][1], min(1.0, weight)), scale)


def copy_local(pose, source, bones, flipped=False, weight=1.0):
    """Copies bones' local transforms from another pose, optionally mirrored from the other side."""
    for name in bones:
        origin = source[flip_name(name)] if flipped else source[name]
        if flipped:
            origin = flip_basis(origin)
        if weight >= 1.0:
            pose[name] = (origin[0].copy(), origin[1].copy(), origin[2].copy())
        elif weight > 0.0:
            loc, rot, scale = pose[name]
            pose[name] = (loc.lerp(origin[0], weight), rot.slerp(origin[1], weight), scale)


def upper_bones():
    """
    The bones an upper-body strike shows, and the hips: the hips carry the spine, so they turn the whole upper
    body. (Whether Unity's upper-body layer takes the hips' rotation from the strike is not verified; either
    way, starting and ending on the stance's hips keeps the strike in step with the stance underneath.)
    """
    return (["pelvis"] + list(TORSO) + side_bones(ARM_CHAIN, "l") + side_bones(ARM_CHAIN, "r") + finger_bones("l")
            + finger_bones("r"))


def body_bones(armature):
    """Every bone that moves the body: all but the root."""
    return [b.name for b in armature.pose.bones if b.name != "root"]


def from_reference(poses, reference, bones, weight_of):
    """
    Blends bones from a reference pose into the clip: weight_of(i) is how much of the clip shows at frame i
    (0: the reference pose, 1: the clip as authored). Locations (the hips) are blended too.
    """
    for i, pose in enumerate(poses):
        clip_share = min(1.0, max(0.0, weight_of(i)))
        if clip_share >= 1.0:
            continue
        for name in bones:
            loc, rot, scale = pose[name]
            ref_loc, ref_rot, _ = reference[name]
            pose[name] = (ref_loc.lerp(loc, clip_share), ref_rot.slerp(rot, clip_share), scale)


def write_action(armature, name, poses):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    armature.animation_data.action = action
    for index, pose in enumerate(poses):
        frame = index + 1
        for bone in armature.pose.bones:
            loc, rot, scale = pose[bone.name]
            bone.location = loc
            bone.rotation_mode = "QUATERNION"
            bone.rotation_quaternion = rot
            bone.scale = scale
            bone.keyframe_insert("location", frame=frame)
            bone.keyframe_insert("rotation_quaternion", frame=frame)
    # One key per frame: linear interpolation, and quaternions kept continuous so that interpolation
    # between two keys never takes the long way round.
    for curve in action.fcurves:
        for point in curve.keyframe_points:
            point.interpolation = "LINEAR"
    _fix_quaternion_signs(action)
    return action


def _fix_quaternion_signs(action):
    curves = {}
    for curve in action.fcurves:
        if curve.data_path.endswith("rotation_quaternion"):
            curves.setdefault(curve.data_path, {})[curve.array_index] = curve
    for group in curves.values():
        if len(group) != 4:
            continue
        count = len(group[0].keyframe_points)
        previous = None
        for i in range(count):
            q = [group[c].keyframe_points[i].co[1] for c in range(4)]
            if previous is not None and sum(a * b for a, b in zip(q, previous)) < 0.0:
                for c in range(4):
                    group[c].keyframe_points[i].co[1] = -q[c]
                q = [-v for v in q]
            previous = q
        for c in range(4):
            group[c].update()


# ----------------------------------------------------------------------------------- the styles

class StyleBuilder:
    def __init__(self, armature, actions):
        self.armature = armature
        self.actions = actions
        self.sampler = Sampler(armature)
        self.scratch = bpy.data.actions.new("_style_scratch")
        self.editor = Editor(armature, self.scratch)
        # The library's own open hand: the casting hand of the spell idle, left side.
        self.open_hand = self.sampler.sample(actions["Spell_Simple_Idle_Loop"], 1)
        self.rest = {b.name: (Vector(), Quaternion(), Vector((1.0, 1.0, 1.0))) for b in armature.pose.bones}
        self.lead_sign = None
        # Measured once, before any clip is edited: it poses the armature itself.
        self.turn_sign("l")

    def clip(self, name, first, last, count=None):
        return self.sampler.frames(self.actions[name], first, last, count)

    def settle_into_stance(self, poses, stance, bones, enter_until, exit_start, legs=None):
        """
        The strike starts from the stance and returns to it: until frame index enter_until the stance gives way
        to the clip, and from exit_start to the last frame the clip gives way to the stance again. With legs
        (bones, start, end), those bones keep the stance until start and blend into the clip by end, so a
        full-body strike that begins standing does not bob up out of a low stance.
        """
        last = len(poses) - 1
        from_reference(poses, stance, bones,
                       lambda i: smoothstep(0.0, float(enter_until), i) * (1.0 - smoothstep(float(exit_start), float(last), i)))
        if legs:
            leg_bones, start, end = legs
            from_reference(poses, stance, leg_bones,
                           lambda i: smoothstep(float(start), float(end), i) * (1.0 - smoothstep(float(exit_start), float(last), i)))

    def keep_stance_legs(self, poses, stance, hip_share=1.0):
        """
        For a strike shown on the upper body only: the hips stay where the stance has them and the legs keep the
        stance, so the strike never lifts or drops the body. The clip's own turn of the hips (relative to its
        first frame, scaled by hip_share) is kept on top of the stance's, and the thighs are turned back by
        exactly the hips' turn, measured in armature space, so the hips turn while the legs and feet stay put.
        """
        legs = side_bones(LEG_CHAIN, "l") + side_bones(LEG_CHAIN, "r")
        identity = Quaternion()
        first = poses[0]["pelvis"][1]
        for pose in poses:
            delta = identity.slerp(pose["pelvis"][1] @ first.inverted(), hip_share)
            loc, _, scale = stance["pelvis"]
            pose["pelvis"] = (loc.copy(), delta @ stance["pelvis"][1], scale)
            copy_local(pose, stance, legs)

        reference = {}
        self.editor.edit([stance], lambda e, i: reference.update(
            q=self.armature.pose.bones["pelvis"].matrix.to_quaternion()))

        def counter_turn(editor, index):
            current = self.armature.pose.bones["pelvis"].matrix.to_quaternion()
            axis, angle = (current @ reference["q"].inverted()).to_axis_angle()
            if abs(angle) < 1e-5:
                return
            for side in ("l", "r"):
                editor.rotate(f"thigh_{side}", axis, -math.degrees(angle))

        return self.editor.edit(poses, counter_turn)

    @staticmethod
    def flipped(pose):
        return {flip_name(n): flip_basis(v) for n, v in pose.items()}

    # -------------------------------------------------------------- hands

    def open_hand_pose(self, side):
        """Finger rotations of the authored open hand, for either side."""
        if side == "l":
            return self.open_hand
        return {flip_name(n): flip_basis(v) for n, v in self.open_hand.items()}

    def open_fingers(self, poses, side, weight_of):
        target = self.open_hand_pose(side)
        bones = finger_bones(side)
        for i, pose in enumerate(poses):
            blend_local(pose, bones, target, weight_of(i))

    def spear_fingers(self, poses, side, weight_of):
        """Fingers straight and together: the rest pose of the finger bones."""
        bones = finger_bones(side)
        for i, pose in enumerate(poses):
            blend_local(pose, bones, self.rest, weight_of(i))

    # -------------------------------------------------------------- body

    def torso_axis(self):
        return UP

    def turn_sign(self, side):
        """+1 if a positive turn about UP brings that side's shoulder forward, else -1. Measured, not assumed."""
        if self.lead_sign is None:
            pose = self.sampler.sample(self.actions["Idle"], 1)

            def probe(editor, _):
                before = editor.head("upperarm_l").dot(FORWARD)
                editor.rotate("spine_01", UP, 10.0)
                after = editor.head("upperarm_l").dot(FORWARD)
                self.lead_sign = 1.0 if after > before else -1.0

            self.editor.edit([pose], probe)
        return self.lead_sign if side == "l" else -self.lead_sign

    def turn(self, editor, side, degrees):
        """Turns the torso so that the shoulder of that side comes forward by degrees, spread over the spine."""
        signed = degrees * self.turn_sign(side)
        for bone, share in SPINE:
            editor.rotate(bone, UP, signed * share)

    def lean(self, editor, degrees):
        """Leans the chest forward by degrees (negative: back), about the axis across the shoulders."""
        across = (editor.head("upperarm_l") - editor.head("upperarm_r"))
        across.z = 0.0
        if across.length < 1e-6:
            return
        # Rotating about "across" by a positive angle must move the chest towards FORWARD.
        axis = across.normalized()
        top = editor.head("spine_03") - editor.head("spine_01")
        moved = Matrix.Rotation(math.radians(5.0), 3, axis) @ top
        sign = 1.0 if moved.dot(FORWARD) > top.dot(FORWARD) else -1.0
        for bone, share in SPINE:
            editor.rotate(bone, axis, degrees * sign * share)

    def swing_arm(self, editor, side, degrees):
        """Swings an upper arm about the vertical through its shoulder; positive moves the hand outwards."""
        shoulder = editor.head(f"upperarm_{side}")
        hand = editor.head(f"hand_{side}")
        centre = editor.head("spine_03")
        probe = Matrix.Rotation(math.radians(5.0), 3, UP) @ (hand - shoulder) + shoulder
        outward_before = abs(hand.x - centre.x)
        outward_after = abs(probe.x - centre.x)
        sign = 1.0 if outward_after > outward_before else -1.0
        editor.rotate(f"upperarm_{side}", UP, degrees * sign)

    def bend_wrist(self, editor, side, degrees):
        """Bends the hand back (positive) or forward about the axis across the palm: a standing palm."""
        hand = self.armature.pose.bones[f"hand_{side}"]
        across = hand.matrix.to_3x3() @ Vector((1.0, 0.0, 0.0))
        # Positive must lift the back of the hand towards the forearm, which for this rig is a rotation
        # that moves the fingertips upwards when the palm faces down: measured on the middle finger.
        tip_before = editor.head(f"middle_04_leaf_{side}")
        editor.rotate(f"hand_{side}", across, 5.0)
        tip_after = editor.head(f"middle_04_leaf_{side}")
        editor.rotate(f"hand_{side}", across, -5.0)
        sign = 1.0 if tip_after.z > tip_before.z else -1.0
        editor.rotate(f"hand_{side}", across, degrees * sign)

    def widen_stance(self, editor, degrees):
        """
        Turns both thighs outwards at the hip by degrees, turns the feet back so the soles stay flat, and lowers
        the body by as much as the feet rose, so they stay on the ground. The stance becomes wider and lower.
        """
        before = (editor.head("foot_l").z + editor.head("foot_r").z) / 2.0
        centre_x = editor.head("pelvis").x
        for side in ("l", "r"):
            hip = editor.head(f"thigh_{side}")
            foot = editor.head(f"foot_{side}")
            probe = Matrix.Rotation(math.radians(3.0), 3, FORWARD) @ (foot - hip) + hip
            sign = 1.0 if abs(probe.x - centre_x) > abs(foot.x - centre_x) else -1.0
            editor.rotate(f"thigh_{side}", FORWARD, degrees * sign)
            editor.rotate(f"foot_{side}", FORWARD, -degrees * sign)
        after = (editor.head("foot_l").z + editor.head("foot_r").z) / 2.0
        editor.translate("pelvis", Vector((0.0, 0.0, before - after)))

    def bend_elbow(self, editor, side, degrees):
        """Bends an elbow further (positive) or straightens it, about its hinge."""
        shoulder = editor.head(f"upperarm_{side}")
        elbow = editor.head(f"lowerarm_{side}")
        wrist = editor.head(f"hand_{side}")
        hinge = (elbow - shoulder).cross(wrist - elbow)
        if hinge.length < 1e-4:
            hinge = editor.head("upperarm_l") - editor.head("upperarm_r")
        hinge.normalize()
        moved = Matrix.Rotation(math.radians(5.0), 3, hinge) @ (wrist - elbow) + elbow
        sign = 1.0 if (moved - shoulder).length < (wrist - shoulder).length else -1.0
        editor.rotate(f"lowerarm_{side}", hinge, degrees * sign)

    def draw_back_arm(self, editor, side, degrees):
        """Pulls an upper arm back (positive) about the axis across the shoulders: a chamber before a strike."""
        across = editor.head("upperarm_l") - editor.head("upperarm_r")
        across.z = 0.0
        axis = across.normalized()
        hand = editor.head(f"hand_{side}")
        shoulder = editor.head(f"upperarm_{side}")
        moved = Matrix.Rotation(math.radians(5.0), 3, axis) @ (hand - shoulder)
        sign = 1.0 if moved.dot(FORWARD) < (hand - shoulder).dot(FORWARD) else -1.0
        editor.rotate(f"upperarm_{side}", axis, degrees * sign)

    # -------------------------------------------------------------- Arcane

    def arcane(self):
        clips = {}

        # Stance: the breathing of the idle loop; both hands float in front of the body, open and soft, the
        # casting (left) hand a little higher and further forward. The arms come from the casting gesture
        # (frame 9 for the left, frame 8 mirrored for the right), with the elbows bent.
        lift_left = self.sampler.sample(self.actions["Spell_Simple_Enter"], 9)
        lift_right = self.sampler.sample(self.actions["Spell_Simple_Enter"], 8)
        stance = self.clip("Idle", 1, 76)
        for pose in stance:
            copy_local(pose, lift_left, side_bones(ARM_CHAIN, "l"))
            copy_local(pose, lift_right, side_bones(ARM_CHAIN, "r"), flipped=True)
        self.open_fingers(stance, "l", lambda i: 0.75)
        self.open_fingers(stance, "r", lambda i: 0.75)
        clips["Arcane_Stance_Loop"] = self.editor.edit(stance, lambda e, i: (
            self.bend_elbow(e, "l", 70.0), self.bend_elbow(e, "r", 80.0), self.turn(e, "l", 6.0)))
        guard = clips["Arcane_Stance_Loop"][0]
        upper = upper_bones()

        # Rising Palm: the casting hand rises in an arc to an open palm (Spell_Simple_Enter) and sinks back
        # (Spell_Simple_Exit). The two clips meet without a gap. The hand stays open through the sinking arc;
        # a standing palm at full reach (frame 14) and a small turn of the shoulders into it.
        rising = join(self.clip("Spell_Simple_Enter", 1, 17), self.clip("Spell_Simple_Exit", 1, 14))
        contact = 14 - 1
        self.open_fingers(rising, "l", lambda i: ramp(i, 4, 9))
        self.settle_into_stance(rising, guard, upper, enter_until=7, exit_start=19)
        clips["Arcane_RisingPalm"] = self.keep_stance_legs(self.editor.edit(rising, lambda e, i: (
            self.turn(e, "l", 8.0 * bump(i, 3, contact, 26)),
            self.bend_wrist(e, "l", 30.0 * bump(i, 9, contact, 21)))), guard, hip_share=0.4)

        # Circling Palm: the same authored arc bent outwards into a horizontal circle (the arm swings out
        # and comes round to the same reach), with more turn. Played mirrored, so the right hand strikes.
        circling = join(self.clip("Spell_Simple_Enter", 1, 17), self.clip("Spell_Simple_Exit", 1, 14))
        self.open_fingers(circling, "l", lambda i: ramp(i, 3, 8))
        # Played mirrored, so it starts from and returns to the mirror of the stance.
        self.settle_into_stance(circling, self.flipped(guard), upper, enter_until=7, exit_start=19)
        clips["Arcane_CirclingPalm"] = self.keep_stance_legs(self.editor.edit(circling, lambda e, i: (
            self.swing_arm(e, "l", 34.0 * math.sin(math.pi * min(1.0, i / contact)) if i <= contact else 0.0),
            self.turn(e, "l", 12.0 * bump(i, 2, contact, 27)),
            self.bend_wrist(e, "l", 18.0 * bump(i, 9, contact, 21)))), self.flipped(guard), hip_share=0.4)

        # Lance: the casting hand reaches straight out (Interact, frames 5 to 22, then 33 to 50 so the
        # pause at full reach is left out) as a spear hand, with the shoulders turning and the chest leaning
        # into the reach.
        lance = join(self.clip("Interact", 5, 22), self.clip("Interact", 33, 50), settle_frames=3)
        reach = 19 - 5
        self.spear_fingers(lance, "l", lambda i: ramp(i, 4, 11, 26, 34))
        self.settle_into_stance(lance, guard, upper, enter_until=7, exit_start=23)
        clips["Arcane_Lance"] = self.keep_stance_legs(self.editor.edit(lance, lambda e, i: (
            self.turn(e, "l", 10.0 * bump(i, 3, reach, 30)),
            self.lean(e, 5.0 * bump(i, 6, reach, 28)))), guard, hip_share=0.4)
        return clips

    # -------------------------------------------------------------- Flame

    def flame(self):
        clips = {}

        # Stance: the wide, low ready stance of the sword idle (legs, hips, spine, breathing) with the fists
        # up in the boxing guard (arms and fingers from Punch_Jab's first frame).
        guard = self.sampler.sample(self.actions["Punch_Jab"], 1)
        stance = self.clip("Sword_Idle", 1, 51)
        arms = side_bones(ARM_CHAIN, "l") + side_bones(ARM_CHAIN, "r") + finger_bones("l") + finger_bones("r")
        for pose in stance:
            copy_local(pose, guard, arms)
        # A little wider than the sword stance, so it reads as planted and ready to drive forward.
        clips["Flame_Stance_Loop"] = self.editor.edit(stance, lambda e, i: self.widen_stance(e, 8.0))
        ready = clips["Flame_Stance_Loop"][0]
        upper = upper_bones()
        body = body_bones(self.armature)

        # Ember Fist: the rear-hand cross, with the hips and shoulders driving harder into it and the chest
        # following the fist (frame 9 is full extension).
        punch = self.clip("Punch_Cross", 1, 31)
        extension = 9 - 1
        self.settle_into_stance(punch, ready, upper, enter_until=5, exit_start=18)
        clips["Flame_Punch"] = self.keep_stance_legs(self.editor.edit(punch, lambda e, i: (
            self.turn(e, "r", 10.0 * bump(i, 4, extension, 22)),
            self.lean(e, 6.0 * bump(i, 4, extension, 22)))), ready)

        # Ember Hook: the lunging hook and its own recovery back to the guard. The right hand is 3.7 cm apart
        # where they meet; the difference is spread over the recovery's first four frames.
        hook = join(self.clip("Melee_Hook", 1, 15), self.clip("Melee_Hook_Rec", 1, 19), settle_frames=4)
        self.settle_into_stance(hook, ready, body, enter_until=3, exit_start=23)
        clips["Flame_Hook"] = hook

        # Flare: a coil down, then an explosive rising backhand sweep with the whole body turning about
        # 135 degrees, and its own recovery turning back to the front. The two clips meet without a gap.
        flare = join(self.clip("Sword_Regular_A", 1, 14), self.clip("Sword_Regular_A_Rec", 1, 29))
        self.settle_into_stance(flare, ready, body, enter_until=4, exit_start=29)
        clips["Flame_Flare"] = flare
        return clips

    # -------------------------------------------------------------- Aegis

    def aegis(self):
        clips = {}

        # Stance: low and rooted. The hips and legs of the parry's braced frame 12 (the pelvis 23 cm lower than
        # standing), the upright upper body and breathing of the shield idle, forearm across the chest.
        braced = self.sampler.sample(self.actions["Sword_Block"], 12)
        stance = self.clip("Idle_Shield_Loop", 1, 76)
        legs = ["pelvis"] + side_bones(LEG_CHAIN, "l") + side_bones(LEG_CHAIN, "r")
        for pose in stance:
            copy_local(pose, braced, legs)
        upright = self._upright_correction(stance[0], braced)
        # The widest and lowest of the three, like a horse stance: rooted.
        clips["Aegis_Stance_Loop"] = self.editor.edit(stance, lambda e, i: (self.lean(e, upright), self.widen_stance(e, 13.0)))
        rooted = clips["Aegis_Stance_Loop"][0]
        upper = upper_bones()
        body = body_bones(self.armature)
        hips_and_legs = ["pelvis"] + side_bones(LEG_CHAIN, "l") + side_bones(LEG_CHAIN, "r")

        # Force Palm: the parry's forward sweep with its deep sink, as a right standing palm. The arm is drawn
        # back and the shoulders turn away first (anticipation), then the palm drives out as the body drops
        # (contact at frame 5) and holds the low pose briefly before rising.
        palm = self.clip("Sword_Block", 1, 35)
        contact = 5 - 1
        self.open_fingers(palm, "r", lambda i: ramp(i, 2, 4, 21, 29))
        # The parry begins standing and sinks by frame 5; the legs stay in the low stance until then.
        self.settle_into_stance(palm, rooted, upper, enter_until=2, exit_start=18, legs=(hips_and_legs, 2, 4))
        clips["Aegis_ForcePalm"] = self.editor.edit(palm, lambda e, i: (
            self.draw_back_arm(e, "r", 22.0 * bump(i, 0, 2, contact)),
            self.turn(e, "r", -8.0 * bump(i, 0, 2, contact) + 6.0 * bump(i, 3, 6, 18)),
            self.bend_wrist(e, "r", 40.0 * bump(i, 2, contact, 20))))

        # Bulwark Forearm: the shield bash. Its strike comes at frame 4, too early to show a wind-up, so its
        # first four frames are played over seven (a slower gather); the forearm is pulled in and the shoulders
        # turn away, then the forearm drives out with the hips (contact at frame 7 of the new clip).
        gather = self.clip("Shield_OneShot", 1, 4, count=7)
        bash = join(gather, self.clip("Shield_OneShot", 5, 26))
        contact = 7 - 1
        # The shield clip lowers the forearm in a second movement from frame 19; the return to the stance
        # replaces it.
        self.settle_into_stance(bash, rooted, upper, enter_until=4, exit_start=15)
        clips["Aegis_Forearm"] = self.keep_stance_legs(self.editor.edit(bash, lambda e, i: (
            self.draw_back_arm(e, "l", 18.0 * bump(i, 0, 4, contact)),
            self.turn(e, "l", -8.0 * bump(i, 0, 4, contact) + 12.0 * bump(i, 5, 8, 22)))), rooted)

        # Bulwark Slam: the overhand throw made two-handed. The left arm takes the right arm's authored motion,
        # mirrored, from the wind-up to the end of the follow-through, so both arms rise, open and crash down
        # together into the deep lunge; both hands open as palms for the strike (contact at frame 12).
        slam = self.clip("OverhandThrow", 1, 41)
        left_arm = side_bones(ARM_CHAIN, "l") + finger_bones("l")
        for i, pose in enumerate(slam):
            weight = ramp(i, 1, 5, 25, 33)
            copy_local(pose, dict(pose), left_arm, flipped=True, weight=weight)
        self.open_fingers(slam, "r", lambda i: ramp(i, 7, 10, 22, 30))
        self.open_fingers(slam, "l", lambda i: ramp(i, 7, 10, 22, 30))
        self.settle_into_stance(slam, rooted, body, enter_until=7, exit_start=26)
        clips["Aegis_Slam"] = slam
        return clips

    def _upright_correction(self, stance_pose, braced):
        """Degrees to lean the stance's chest back so it stands as upright as the shield idle's."""
        idle = self.sampler.sample(self.actions["Idle_Shield_Loop"], 1)
        tilt = {}

        def measure(editor, index):
            top = editor.head("spine_03") - editor.head("pelvis")
            tilt[index] = math.degrees(math.atan2(top.dot(FORWARD), top.z))

        self.editor.edit([idle, stance_pose], measure)
        return -(tilt[1] - tilt[0])

    # -------------------------------------------------------------- all

    def build(self):
        poses = {}
        poses.update(self.arcane())
        poses.update(self.flame())
        poses.update(self.aegis())
        actions = {}
        for name in STYLE_CLIPS:
            actions[name] = write_action(self.armature, name, poses[name])
        bpy.data.actions.remove(self.scratch)
        return actions


def build(armature, actions):
    """Adds the style clips to the armature's actions. Needs the source clips (SOURCE_ANIMATIONS too)."""
    return StyleBuilder(armature, actions).build()
