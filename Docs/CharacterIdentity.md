# The Mage's identity and bearing

Sprint 5 gives the Mage a recognisable look and a way of carrying itself that differs by discipline,
without changing how it moves or fights. This document covers what was implemented, what it rests on,
what was checked, and which art still has to be made by an artist.

**Status.** Written; compiled against the Unity 6000.6.0f1 assemblies (Editor and Android player
defines); 1,117 tests outside Unity: 988 passed, 0 failed, 129 need the Unity engine. The model was
built and rendered in Blender 4.0.2. **Nothing has been seen in Unity or on a device.** The look, the
bearing and every number are proposals until the owner has reviewed them there.

## Implemented, and proposed

| | Implemented in this sprint | Proposed for later (needs an artist or new source material) |
| --- | --- | --- |
| Body | The library mannequin, unchanged | A designed body with believable proportions and hands |
| Face | Drawn marks (eyes, brows, nose wedge, mouth stroke); blinking and brow movement through two bones | A modelled face with eyelids, mouth and facial bones or blend shapes; real expressions |
| Hair | Script-built locks, a fringe and a tied tail that swings | Sculpted hair with painted strands |
| Outfit | The Wayfarer outfit, script-built around the skeleton (`Docs/Characters/Mage.md`) | A designed costume with real drape, seams and wear |
| Surface | One atlas painted by a script: washes, strokes, hems, seams | Hand-painted textures; painted light on the face; a highlight for metal |
| Bearing | Posture, breathing, hands, head and blink rhythm per discipline, as small rotations on top of the clips | Authored idle and stance animation per discipline |
| Reactions | A restrained flinch on a hit and a brace on a charged release, on top of the existing clips | Authored hit reactions and cast follow-through for the Mage |
| Cloak, hair, sash | Seven bones on a light spring: they hang, lag and settle | More bones or authored secondary animation if the result is too stiff |

## 1. What the Mage was

The inspection is recorded in `Docs/Characters/Mage.md` ("What was found"). In short: the character was
an **intermediate placeholder**, a library mannequin in a script-built hooded outfit with nine flat
colours. It had a sound 65-bone Humanoid skeleton and 41 working clips, and no face, no hair, no texture
coordinates, no bones for cloth or hair, and no support for facial animation of any kind.

No other character asset is in the repository. The two free CC0 packs by the same author that would give
this rig a real face and designed clothing (Universal Base Characters; Modular Character Outfits -
Fantasy) are downloaded through a pay-what-you-want page that needs a person; they were not fetched and
nothing from them is used. See [What a production character needs](#7-what-a-production-character-needs).

## 2. The strategy

The skeleton and its clips are suitable; the mesh on them was the weak part. So:

- **`Mage.fbx` is not touched.** It keeps the skeleton, the Humanoid avatar and every clip. All combat
  timing was measured on it (`Docs/MartialStyles.md`) and stays valid.
- **The new look is a separate, project-owned file**, `MageWayfarer.fbx`, on the same skeleton. Unity
  plays `Mage.fbx`'s clips on it. It is used only when Unity imports it as the same Humanoid; otherwise
  the Mage looks exactly as before (`Docs/Characters/Mage.md`, "How the two looks coexist").
- **Personality is a layer on top of the clips**, not new clips: small rotations of the spine, neck,
  head, shoulders, arms and hands, decided in plain C#. No clip was edited, so no strike, stance or
  measured contact pose changed.
- **The Wayfarer look is not presented as finished art.** It is a stand-in that carries the intended
  identity, so that identity can be judged in play.

## 3. The look

Described part by part, with before and after images, in `Docs/Characters/Mage.md`. Against the brief:

| Asked for | In the Wayfarer look |
| --- | --- |
| Expressive and approachable but capable | An open face with the mouth's corners slightly up; practical, worn clothing; wrapped hands |
| Believable human proportions | The mannequin's: 1.83 m, naturalistic. Not altered |
| Distinctive head and hair silhouette | Swept-back hair with a fringe to one side and a tied tail, instead of a hood |
| Asymmetrical layered clothing | Half mantle over the right shoulder only; one short and one long sleeve; slanted tunic hem; cloak tails of different length; sash end on one hip |
| Forearm wraps or bracers | Linen wraps on the left forearm and both hands; a leather bracer on the right |
| Comfortable boots | Knee boots with a folded cuff and an ankle strap |
| Short split cloak or mantle | Both: a half mantle, and a cloak split into two tails that swing |
| Restrained magical details | The crystal clasp and the rune band only |
| Painterly materials, delicate linework | A painted atlas with drawn hems, seams and bands; the style's contour line as before |
| Avoided | No hat, no hood over the face, no armour, nothing large on the back, no skin shader, no thick outline, no cloth simulation |

## 4. Bearing by discipline

`CharacterPersonality` on the Mage adds each discipline's bearing after the Animator has posed the
body. What it adds is decided by `PersonalityPoseModel` (plain C#) from the data in a
`PersonalityProfile` per discipline; the starting values are in `PersonalityDefaults`, and a component
that has been set up keeps its own copy, which is the truth afterwards. The differences between
disciplines are data only.

| | Arcane: relaxed, observant | Flame: restless, ready | Aegis: grounded, sure |
| --- | --- | --- | --- |
| Lean of the chest | 0° | 5° forward | 1° |
| Chest | Slightly open (2°) | Slightly closed (-1°) | Squared and open (3.5°) |
| Breathing | 11 per minute, shallow (1.1°) | 19 per minute (1.6°) | 8 per minute, the slowest (1.4°) |
| Hands | Slow circles: 1.6°, 9 per minute | Hardly circle; restless instead | Still |
| Shoulders and hands, restlessness | 0.15° | 1.4°, 50 per minute | 0.05° |
| Weight shift | 0.6°, 5 per minute | 1.1°, 13 per minute | 0.25°, 3 per minute |
| Looks around | Every 3.5 to 7 s, 14°, held 1.4 s, calm turn (0.5 s) | Every 1.6 to 3.5 s, 18°, held 0.5 s, sharp turn (0.2 s) | Every 6 to 11 s, 9°, held 2.2 s, slow turn (0.8 s) |
| Blinks | Every 3 to 6 s, 0.16 s | Every 4.5 to 9 s, 0.10 s | Every 4 to 8 s, 0.20 s |
| Brows at rest | Neutral | Lowered 1.5 mm | Neutral |
| Time to change into it | 0.45 s | 0.3 s | 0.6 s |

These sit on top of the three stance loops that already differ in posture
(`Docs/MartialStyles.md`): upright with floating hands, wide with a guard, low and rooted.

**Changing discipline** blends the three profiles' weights over the new profile's blend time. The
weights always sum to one, so the body moves from one bearing to the other without a jump; a test
bounds the change per frame at 20 to 120 frames per second.

**The bearing never competes with the rules or the clips.** How much of it shows depends on what the
Mage is doing, read from existing components and never written back:

| Doing | Posture | Breath | Arms and hands | Looks | Sway | Reactions |
| --- | --- | --- | --- | --- | --- | --- |
| Idle or in stance | 100% | 100% | 100% | 100% | 100% | 100% |
| Moving | 50% | 100% | 25% | 35% | 0 | 100% |
| Airborne | 25% | 50% | 0 | 0 | 0 | 60% |
| Striking, charging, climbing, an aerial ability | 35% | 50% | **0** | 0 | 0 | 60% |
| Blocking | 50% | 70% | 0 | 0 | 30% | 50% |
| Staggered (0.35 s after a hit) | 20% | 50% | 0 | 0 | 0 | 100% |
| Defeated | 0 | 0 | 0 | 0 | 0 | 0 |

The arms' share reaches zero within 0.06 s of an action starting, which is before the fastest strike's
0.10 s start-up ends, so an authored strike reaches its measured contact pose with the arms as
authored. The spine keeps a part of the posture during a strike; at most that moves a hand by about a
centimetre. Legs, hips and the character's position are never touched. Every output has a hard limit,
whatever the data says.

## 5. Face and secondary motion

The brief says not to assume the character supports facial animation or clothing physics. It did not.
What exists now is what the Wayfarer model was given bones for, and no more.

**Face** (only on a model with `face_eyes` and `face_brows`):

- **Blinking.** The eye marks are squashed to a tenth along the head's vertical, at each discipline's
  rhythm, from a seeded generator, so runs are repeatable. A hit of half strength or more forces a blink.
- **Brows.** They move up to 4 mm: lowered in Flame and during any strike or charge (focus), raised up
  to 3 mm by a hit, neutral otherwise.
- **Not possible on this model:** eyelids, eye direction, mouth movement, cheeks. Those need a
  modelled face.

**Head.** The idle looks around are the head movement asked for; they stop during actions.

**Reactions**, both restrained and on top of the existing hit and release clips:

- **Hit.** Chest and head flinch away from the direction of the damage by up to 4° and swing back as a
  damped spring (3.2 cycles per second). Strength is the damage of the hit over 25.
- **Charged release.** The chest braces back by up to 2.5° when a full charge leaves the hands and settles.

**Hair, cloak and sash.** `SecondaryMotionModel` (plain C#) treats each `sec_` bone's tip as a point on
a damped spring. The bone is pulled to where it would hang (the animated direction, pulled towards
straight down by its share of gravity), is left behind for a moment when the body moves, and may not
swing past a limit:

| Kind | Bones in the model | Stiffness | Gravity share | Limit |
| --- | --- | --- | --- | --- |
| Cloak | 4 (two tails of two) | Soft, slow | 0.85 | 75° |
| Sash | 1 | Medium | 0.85 | 75° |
| Hair | 2 | Firm, quick | 0.5 | 50° |

A test holds a cloak within 20° of straight down while the chest pitches 80° forward, at every frame
rate tried. This is not cloth simulation: bones do not collide with the body or each other, and
nothing connects one chain to another, so a tail can pass through a leg in an extreme pose.

## 6. Integration with gameplay

Nothing in gameplay reads the mesh or the bones that this sprint moves. Each point below was
established by reading the code, and is covered by tests where a rule is involved. **None was observed
in Unity.**

| Check asked for | How it holds | Evidence |
| --- | --- | --- |
| All three combat animation sets work | `Mage.fbx` and its clips are unchanged; the controller, the combat animation set and the director are unchanged | No change to `CombatAnimation*`; `CombatAnimationTests`, `MartialStylesTests`, `CombatAnimationSetupTests` pass as before |
| Hand and foot effects stay aligned | `DisciplineVfxSockets` asks the Animator for the Humanoid hand, forearm, foot and chest bones; effects are parented to them. The Wayfarer model must map the same bones or it is not used | `DisciplineVfxSockets.cs`; `MageLookRules.CompareHumanMappings` and its tests |
| Arcane Ascension, Fire Glide, Force Dash | Their poses follow the `AerialMove` integer and clips in `Mage.fbx`; the rules are untouched. The bearing treats an aerial ability as an action and leaves the arms alone | `AerialAbilityTests`, `AerialCourseLayoutTests` pass as before |
| Melee hit detection | Hits are resolved from the Mage's root position and facing (`DisciplineCombat`, `TargetSelection`, `AreaStrike`); no bone is read | `DisciplineCombatTests`, `MeleeStrikeTests` pass as before |
| Camera framing | The camera follows the Mage's root with a fixed offset of 1.5 m; the model's height is unchanged (1.83 m body; the hair adds about 3 cm) | `ThirdPersonCamera.cs` |
| Colliders | The `CharacterController` (height 1.8, radius 0.35) is on the root and is not derived from the mesh | `MagePrototypeSetup.cs` |
| No retargeting regression | The Wayfarer skeleton contains the 65 bones with identical parents and rest positions, and the setup refuses the model unless its Humanoid mapping is identical | `Tools/blender/check_mage_wayfarer.py`; the setup's check |

Two reference fields would be left empty in a scene whose level setup has not run since the prefab
was rebuilt (the animation driver's and the effect sockets' Animator). Both, and the bearing
component, now find the model below the Mage by themselves when the scene starts.

## 7. What a production character needs

Listed so the art can be commissioned or sourced; none of it exists yet.

1. **A designed Mage**, to the art bible's character deliverables: front, side and back views, a face
   close-up with expressions, a silhouette test, palette and material callouts, a gameplay-distance
   preview. The Wayfarer look can serve as the brief's starting point if the owner approves its direction.
2. **A body with a face**, on the library's 65-bone skeleton if the clips are to be kept as they are.
   Quaternius' Universal Base Characters (CC0) is built for this rig; someone has to download its free
   "Standard" files from the author's page and add them to `Tools/blender/sources/` with their licence.
   Whether its face has bones or blend shapes has not been checked.
3. **Facial animation support**: eyelids and brows at least, a mouth if dialogue is planned. With
   blend shapes, the importer's blend shape import has to be switched on (it is off).
4. **A sculpted, UV-mapped costume and hair** with hand-painted textures, including painted folds, wear
   and interior lines placed by an artist.
5. **Authored animation**: an idle and a stance per discipline with the bearing built in; hit
   reactions and a cast follow-through for the Mage; the motions already listed as missing in
   `Docs/MartialStyles.md` and `Docs/AerialAbilities.md`.
6. **A decision on secondary motion** once the cloak is seen in play: keep the springs, add bones, or
   author it.

## 8. Android performance considerations

Estimates from the structure of the work; **nothing was measured on a device.**

| Cost | Library look | Wayfarer look |
| --- | --- | --- |
| Triangles | 15,556 | 16,960 (budget in the check: 20,000) |
| Materials | 9 | 2 |
| Draws of the character per frame (lit pass and contour line per material) | 18 | 4 |
| Shadow-caster draws | 9 | 2 |
| Textures | None | One 1024 x 1024 atlas. Unity's default Android compression applies; about 0.6 MB of graphics memory with mipmaps at ASTC 6x6 and 1.4 MB at ASTC 4x4 (calculated, not read from a build) |
| Skinned bones | 65 | 74 |

- **Fewer draws is the main gain.** The character went from nine materials to two, so its draw count
  falls from 18 to 4 with the contour line on.
- **Skinning.** The Mobile quality level skins with two bones per vertex. Every generated part is
  weighted to at most two bones, so it deforms on a phone as in the previews. The mannequin's own
  weights (up to six bones in the hands) are reduced by Unity as before.
- **The bearing** runs once per frame for one character: about ten bone rotations, seven springs, no
  physics queries, no allocation (measured in the .NET runner by `PersonalityTests` and
  `SecondaryMotionTests`; Unity's own runner cannot count allocations). It does nothing while the
  character is not visible.
- **Performance preset.** The springs step every other frame at the Performance preset
  (`VisualStyleController.Active`); the bearing itself is the same at every preset.
- **No cloth simulation, no extra lights, no transparency.** Hair and cloak are opaque geometry.
- **To measure on a device:** frame time in the playground with the Wayfarer and the Library look
  (the two menu commands), at Performance and Balanced; and the same with the bearing component
  disabled.

## Where things are

| Part | Files |
| --- | --- |
| Model and texture | `Assets/Threefold/Art/Characters/Mage/MageWayfarer.fbx`, `MageWayfarer_Paint.png` |
| Build and check | `Tools/blender/build_mage_wayfarer.py`, `Tools/blender/check_mage_wayfarer.py` |
| Look selection | `Scripts/Editor/Characters/MageLookRules` (plain C#), `MageCharacterSetup`, `MageLookMenu`, `CharacterModelPostprocessor` |
| Bearing rules | `Scripts/Runtime/Characters/Personality/PersonalityPoseModel`, `PersonalityProfile`, `PersonalityDefaults` (plain C#) |
| Secondary motion rules | `Scripts/Runtime/Characters/Personality/SecondaryMotionModel` (plain C#) |
| Carrying it out | `Scripts/Runtime/Characters/Personality/CharacterPersonality`; added by `Scripts/Editor/Characters/MagePersonalitySetup` |
| Tests | `CharacterSetupTests` (looks), `PersonalityTests`, `SecondaryMotionTests`, `CharacterPersonalitySetupTests` (needs Unity) |

## On the laptop

1. Pull `main` and let Unity import. The Console must show no red errors.
2. Run **Threefold > Setup > Fantasy Playground** (and each other level's setup once). Read the report:
   it says whether the Mage now wears the Wayfarer look, or why not.
3. Press Play. Switch disciplines while standing still and watch posture, breathing, hands and head.
   Run, stop, jump and strike, and watch the cloak tails, the hair tail and the sash.
4. Compare the looks with **Threefold > Characters > Use Library Look** and **Use Wayfarer Look**.
5. If something looks wrong, the bearing can be switched off on the Mage's `CharacterPersonality`
   component (Bearing Enabled, Secondary Quality) without affecting anything else.

## Not verified

- That Unity imports `MageWayfarer.fbx` as a Humanoid with the same mapping as `Mage.fbx`. The extra
  bones are the risk; the setup checks for it and falls back.
- Every sign and axis of the bearing on the real rig: that "forward lean" leans forward, that looks
  turn the head sideways, that the eyes close along the vertical. The component measures axes from the
  skeleton, but it has only been compiled.
- How the bearing reads at gameplay distance, whether Flame's restlessness is too much or Aegis's
  stillness too little, and whether the cloak's spring is too soft or too stiff.
- The contour line on the hair, the face marks and the cloak tails; the atlas's filtering at distance.
- The rebuild of the visual prefab in place, and the two menu commands.
- Frame time, memory and battery on a device.
