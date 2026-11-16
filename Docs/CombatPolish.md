# Combat animation, effects and impact polish

**Status (16 November 2026).** An audit of the combat animation system and of the imported asset packs, and
a first, deliberately small reference implementation on one strike: the Flame hook. Rules tested outside
Unity and in Unity on the build runner; the strike drawn frame by frame by Unity before and after.
**Nothing has been played, and no effect has been seen:** the frames show the animation only. Whether the hook now feels fluid, and whether
the impact reads as stronger, are for the owner to judge on a device before any of this is extended.

No combat rule, number or timing is changed: start-up, recovery, damage, the combo window and the moment
of contact are what they were.

## 1. Why the animation looks stiff

Found in the code and the data. None of it has been watched in play; the ranking is by how directly the
data shows it.

| # | Cause | Kind |
| --- | --- | --- |
| 1 | **Every follow-through is squeezed.** The part of a strike after contact is fitted into the rules' recovery time, so it plays at 1.8 to 3 times its own speed, and the speed jumps at contact (the Flame flare from 1.66x to 2.96x). The arm snaps back instead of carrying through | Shown by the data |
| 2 | **The wind-up is mostly a cross-fade.** The Arcane palms have 0.10 s of start-up and a 0.08 s blend in, at three times speed: about three source frames, four fifths of them inside a blend. Flame: half | Shown by the data |
| 3 | **Nothing carries weight through the body.** Strikes never move the character (lunges are baked into the pose and return), the legs of upper-body strikes are pinned to the stance, and nothing settles after contact | Shown by the data |
| 4 | **The clips are placeholders.** Nine strikes from eight general library motions; the two Arcane palms are the same source. `Docs/MartialStyles.md` lists them all under "What needs new animation" | Shown by the data |
| 5 | **A struck enemy barely answers.** A normal hit gave a flash and a tenth of swell. Enemies are primitives without animation | Shown by the code; its effect on feel is a hypothesis |
| 6 | **Layer hand-overs inside a combo.** When an upper-body strike is followed by a whole-body one, the outgoing arms sit on a higher layer for 0.1 s and may override the incoming wind-up | Hypothesis |
| 7 | **Striking on the move.** The legs come from a speed-only locomotion blend with no strafe or turn steps, under a body that turns to face the swing in 0.05 s | Hypothesis |

One correction to the record: `Docs/MartialStyles.md` says strikes join at 0.0 m. That measures a clip's
last frame against the next clip's first. The game chains from an earlier point (the recovery end) and
enters the Arcane palms a tenth of the way in, so it does not describe the joins that are played.

## 2. Imported assets: what is in the repository

Only what is in Git can be used from here. **If newer Asset Store packs were imported on the laptop, they
have not been uploaded**, and nothing below knows of them (`Docs/DevelopmentWorkflow.md`, "Uploading laptop
work").

| Pack | Contents | Used by the game |
| --- | --- | --- |
| Free Slash VFX (MaykerStudio) | 12 prefabs, 7 URP Shader Graphs, 5 meshes | 2 current (impact, fire slash), 2 from the first adaptation |
| Hovl Studio Magic effects pack | 76 prefabs, 33 materials on Built-in shaders, 4 meshes | 3 current (red sparks, ground burst, blue charge slash), 3 from the first adaptation; now also the explosion |
| Vefects Free Fire VFX URP | 16 prefabs, 3 URP shaders, 3 fire sounds | 1 from the first adaptation (burn) |
| Quaternius Universal Animation Library 1 and 2 (CC0) | The only character animation in the project | Baked into `Mage.fbx` (41 clips) |

- **No third-party animation clips, controllers or character packs are in the repository**, and no audio
  except the three fire sounds.
- Of 104 pack prefabs the game used 10. Unused: 7 hits and explosions, 12 area effects, 3 shields, 7
  auras, 4 magic circles, 9 slashes, 17 spark variants, 9 smoke and dust, 3 projectiles.
- **Android:** the mobile render-pipeline asset provides neither a depth nor an opaque texture, so the
  packs' soft-particle fades, scene-colour reads and heat haze cannot work as authored. Hovl's materials
  are on Built-in shaders and are remapped to URP copies by `MagicVfxPackAdapter`. Pack prefabs carry
  lights, collision, sub-emitters and limits of thousands of particles; the adapter removes or caps them.
- **Licence:** all three packs are under the Standard Unity Asset Store EULA, so the repository must stay
  private. The rock model in Free Slash VFX is CC BY 4.0 and must be credited if it ships. No pack file is
  edited: the adapter makes project-owned copies.

## 3. The reference strike: the Flame hook

Chosen because it is a whole-body strike with a swing effect, an impact on a target, an enemy's answer
and a recovery into movement or the next strike: every part of the chain in one action.

### What was changed

| Part | Before | Now |
| --- | --- | --- |
| Follow-through | Played at 2.95x to end with the rules' 0.26 s recovery | Plays at 1.6x at most and lasts 0.48 s. The arm slows after the blow instead of speeding up |
| The 0.22 s by which it outlasts the rules | | A tail: the rules do not wait. The next strike cuts into it; movement hands it to the upper body as before |
| Blend into the strike | 0.06 s of a 0.12 s wind-up | 0.05 s, the shortest the project's rule for strikes allows |
| Impact on the target | Red sparks | Red sparks, and from the second strike of a combo a short burst of fire under them: Hovl's `Explosion`, which was in the pack and unused. One for each swing |
| The struck body | Flash and a tenth of swell | Also gives: squashed across and stretched upwards by 7%, swings back once, at rest in 0.3 s |

How it is built, so the same can be applied to other strikes:

- `CombatAnimationSlot.followThroughSpeed` names the fastest the part after contact may play; 0 keeps the
  old fitting. `CombatTimeWarp` honours it. Only the hook sets it.
- `DisciplinePresentation.heavyImpactEffect` and `heavyImpactFromStep`: data, not code per discipline.
  Only Flame has one assigned, so the Flame flare shows it as well; Arcane and Aegis are unchanged.
- `ImpactLayers` (plain C#) holds the two rules: which strike shows the heavy layer, and the curve of the
  give. `HitFlash` carries the give out on every body that already flashed.
- Generated assets are kept between runs, so the animation set takes the hook's values through its data
  version (3); a slot that was tuned by hand is left alone. The adapted effect is a new prefab.

### The sequence, as the rules and the animation now run

1. **Tap.** The rules start the hook; the swing effect starts with it.
2. **0 to 0.12 s, wind-up.** The clip's coil at 2.2x; the first 0.05 s blends in from the stance.
3. **0.12 s, contact.** The rules resolve the hit and deal the damage. On that frame: the clip shows its
   contact pose, the swing effect is stopped, the sparks and the burst appear on the target, the target
   flashes, swells and gives, the number rises, the camera shakes.
4. **0.12 to 0.38 s, the rules' recovery.** The arm carries through at 1.6x.
5. **0.38 s.** The Mage is free. A buffered third strike starts here and cuts into the tail.
6. **To 0.60 s, the tail,** if nothing else was asked for: the arm settles and the body returns to the stance.

Nothing in the chain waits for anything else: damage and every effect come from the rules' contact, as
before.

### What Unity's frames show

`StrikeSequenceCapture` plays the hook the way the game does (the animation model decides, the binding
hands that to the Mage's own Animator Controller, the Animator is advanced a thirtieth of a second at a
time) and draws 26 frames, once with the slot as it was generated before and once as it is now. Run 30 on
the build runner; Editor renders, no effects, no enemy, no swing of cloak or hair.

Before:

![The hook before](CombatPolish/images/hook-sequence-before-run30.png)

After:

![The hook after](CombatPolish/images/hook-sequence-after-run30.png)

Read from the frames and from the model's own record of each frame:

- **The wind-up and the blow are the same in both**: frames 1 to 4, at 2.22x, contact at 0.12 s.
- **Before**, the body is back in the stance by frame 13 (0.40 s): the lunge is held for about five frames
  and then pulled back at 2.95x.
- **After**, the lunge and its return last to frame 18 (0.57 s) at 1.6x. The Mage is free at 0.38 s, as
  before; the last 0.19 s are the tail.
- **A caution the frames raise.** The library clip itself holds the extended pose for a long moment. At
  1.6x that hold is about nine frames (0.3 s), and it may read as the Mage standing in the lunge rather
  than as follow-through. If it does, the value to try next is 2.0x to 2.2x; it is one number in the
  animation set. The frames cannot settle this; motion on a device can.
- The first frame of a strike is drawn at a little over half weight (0.56 before, 0.67 after): the layer
  fades in while the state cross-fades, as the audit found.

## 4. Evaluated and left alone

- **Hit-stop and haptics:** as in `Docs/CameraAndControlsFeel.md`. Not built; they would change combat
  timing or need a permission, and both need the owner's decision.
- **Sound:** the only combat-like sounds are three fire loops in the Vefects pack. Effects may not carry
  an audio source (`MagicVfxBudget`), and there is no audio system for combat. Not added.
- **Heat distortion:** the mobile pipeline has no opaque texture; the adapter removes it. Not added.
- **Root motion or a lunge:** would move the character and so change reach, which is a rule.
- **New animation clips:** none are in the repository to retarget.

## 5. Performance

| Risk | Mitigation | Measured |
| --- | --- | --- |
| The burst is a pack effect authored for desktop: 4 systems, limits of 4,000 particles, Built-in materials | Adapted in the Burst role: at most 3 systems and 48 particles in all, URP copies of the materials, no light, collision or sub-emitter, pooled | Checked by `MagicVfxSetupTests` in Unity |
| Two effects on a target at once | One heavy layer for each swing, on the first target only | By rule (`ImpactLayers.MaxHeavyPerSwing`) |
| Additive particles overlap near the camera | Short-lived (under 1.2 s); nothing else was added to the screen | **Not measured**: overdraw needs a device |
| The give | One scale write for 0.3 s on a body that is already being scaled; no allocation | By reading |
| The longer follow-through | No cost: the same clip for 0.22 s longer | |

No frame rate is claimed. Nothing has run on a phone.

## 6. What remains wrong

- The hook is still a placeholder motion on a mannequin, and 0.12 s is very little time for a wind-up. The
  pass cannot add anticipation without changing the rules.
- The tail cuts into the next strike from wherever the arm is. Whether that reads as flow or as a pop
  has to be seen.
- The enemy's answer is a squash on a primitive, not a reaction of a character.
- Causes 2, 3, 6 and 7 above are untouched.
- The other eight strikes, the charged attacks and the Arcane and Aegis effects are as they were.

## 7. Plan for the rest, after review

1. **Judge the hook on a device.** If the follow-through is right, give every strike its own
   `followThroughSpeed`, heaviest first (Flare, Slam, Force Palm, Forearm, Lance), each with a strip.
2. **Wind-ups.** For the fast strikes, either accept a shorter blend rule for strikes that begin on their
   stance, or lengthen start-up, which is a balance decision for the owner.
3. **Impact layers for Arcane and Aegis** from the unused pack effects: a Hovl hit for Arcane, the ground
   burst or stones for Aegis; then the charged releases, stage by stage (gather, release, travel, impact,
   fade).
4. **Enemy reactions:** a flinch in `EnemyPresentation` on every hit, away from the blow.
5. **Hand-overs and striking on the move** (causes 6 and 7): measure first with strips of the Flame and
   Aegis combos.
6. **New motion.** The largest gain is better clips. That needs an animation pack on the laptop to be
   uploaded, or authored clips; anything from outside needs its licence recorded.
