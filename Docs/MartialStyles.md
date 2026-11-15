# Martial styles of the three disciplines

Sprint 2 of the combat animation work gives Arcane, Flame and Aegis melee styles that differ in stance,
rhythm, body mechanics and movement: flowing precision, explosive aggression and grounded power. It
builds on the combat animation architecture of Sprint 1 (`Docs/CombatAnimationArchitecture.md`):
the combat rules keep their own clock and lead, and the animation shows each strike's contact frame
when the rules land the hit.

**Status.** Written; compiled against the Unity 6000.6.0f1 assemblies (Editor and Android player
defines); the rules and the animation model pass 965 tests outside Unity (842 passed, 0 failed, 123
need the Unity engine). The new clips were built and measured in Blender 4.0.2 from the exported model.
**Nothing has been seen playing in Unity or on a device.** The clips are adapted CC0 library motion on
the mannequin, not designed choreography; the kick and the true palm strikes the brief asks for do not
exist in the libraries and are listed under [What needs new animation](#what-needs-new-animation).

## The three styles at a glance

| | Arcane: flowing precision | Flame: explosive aggression | Aegis: grounded power |
| --- | --- | --- | --- |
| Inspiration | Tai Chi, Baguazhang | Northern Shaolin | Hung Gar |
| Stance | Upright and relaxed, both hands open and floating in front of the body | Wide and low, fists up in a guard | The widest and lowest, rooted, forearm across the chest |
| Hips below standing / ankles apart | 0 m / 0.53 m | 0.16 m / 0.67 m | 0.27 m / 0.72 m |
| Strike 1 | **Rising Palm**: the left palm sinks and rises in an arc to a standing palm | **Ember Fist**: a straight rear-hand punch driven by the hips | **Force Palm**: a right palm thrust as the whole body sinks into it |
| Strike 2 | **Circling Palm**: the right palm circles outwards and round | **Ember Hook**: a lunging hook that turns the whole body | **Bulwark Forearm**: the forearm drawn in, then driven out with the hips |
| Strike 3 | **Arcane Lance**: a reaching spear hand | **Flare**: a coiling, spinning rising sweep (stands in for the kick) | **Bulwark Slam**: both arms rise and crash down into a deep lunge |
| Start-up (rules) | 0.10, 0.10, 0.12 s | 0.12, 0.12, 0.16 s | 0.18, 0.20, 0.26 s |
| Recovery (rules) | 0.17, 0.17, 0.28 s | 0.26, 0.26, 0.36 s | 0.34, 0.38, 0.46 s |
| Movement during a strike | Full speed throughout | 90% to 85%; down to 60%, 35%, 15% around the hit | 70% to 55%; down to 20%, 25% and 0% (rooted) around the hit |
| Whole body when standing | None (always upper body) | Hook and Flare | Force Palm and Slam |
| Hip turn / shoulder turn (largest strike) | 22° / 59° (Circling Palm) | 146° / 215° (Flare) | 75° / 115° (Slam) |
| Peak hand speed | 2.7 to 3.8 m/s | 9.5 to 41 m/s | 7.7 to 21 m/s |

All numbers are first estimates and measurements of placeholder clips; none has been played.

Review sheets, rendered from the exported model by `Tools/blender/analyse_martial_styles.py`. Each
shows the stance, then each strike at its wind-up, contact and follow-through, left to right. The
Circling Palm is shown as authored (left hand); the game plays it mirrored (right hand).

| Arcane | Flame | Aegis |
| --- | --- | --- |
| ![Arcane](MartialStyles/images/Arcane.png) | ![Flame](MartialStyles/images/Flame.png) | ![Aegis](MartialStyles/images/Aegis.png) |

## How each style was made

The libraries hold no martial-arts set: no kick, no palm strike, no horse stance. Every new clip starts
from motion an animator authored in Quaternius' Universal Animation Library 1 or 2 (CC0) and is adapted
by `Tools/blender/martial_styles.py`, which `Tools/blender/build_mage.py` runs. The adaptations are the
kind an animator makes to a library clip; no strike is generated from nothing:

- **Cut and retime**: a range of frames, or a few frames stretched over more (a slower gather).
- **Join**: a strike followed by its own recovery clip; a small pose gap at the join is spread over the
  first frames of the second clip.
- **Open hand / spear hand**: the finger pose of the library's own open casting hand, or straight fingers,
  blended in for the strike and out after it.
- **Standing palm**: the hand bent back around the contact.
- **Turn, lean, draw back, swing, bend**: a few degrees of extra rotation of the spine, an upper arm or an
  elbow, timed with a smooth curve around the contact, for hip and shoulder rotation and anticipation.
- **Flipped arm**: one arm's authored motion copied, mirrored, onto the other (the two-handed slam).
- **Wider stance**: thighs turned out, soles kept flat, the body lowered so the feet stay on the ground.
- **From and back to the stance**: every strike starts on its discipline's stance pose and ends on it.
  The blend runs while the authored wind-up gathers and while the recovery settles; the fast part of the
  strike is untouched. A strike shown on the upper body keeps the stance's hips and legs throughout, with
  its own hip turn layered on and the thighs turned back so the feet stay planted.

### Arcane: flowing precision

| Slot | Clip | Made from | Adaptation |
| --- | --- | --- | --- |
| Stance | `Arcane_Stance_Loop` (76 frames) | UAL1 `Idle_Loop` (breathing, legs), `Spell_Simple_Enter` frames 9 and 8 (arms) | Both hands lifted in front of the body, elbows bent, fingers 75% open, shoulders turned 6° |
| Rising Palm | `Arcane_RisingPalm` (31) | UAL1 `Spell_Simple_Enter` + `Spell_Simple_Exit` (joined without a gap) | Open hand, standing palm (30°) at full reach, 8° shoulder turn into it, hip turn kept at 40% |
| Circling Palm | `Arcane_CirclingPalm` (31), played mirrored | The same two clips | The arm swung outwards up to 34° on the way to the reach, so the palm traces a horizontal circle; 12° turn |
| Arcane Lance | `Arcane_Lance` (36) | UAL1 `Interact` frames 5 to 22 and 33 to 50 (the pause at full reach removed) | Spear hand (straight fingers), 10° turn and 5° lean into the reach |

The palms trace continuous arcs: hand speed peaks at 3.0, 3.8 and 2.7 m/s (about 13 cm per frame at
30 frames per second), against 9.5 to 41 m/s for Flame. These are the paths to attach magical ribbons to;
the `Effect`, `HitWindowOpened` and `HitWindowClosed` cues of the director (`CombatAnimationDirector.Cue`)
mark where a ribbon should start and stop, and the cue names the hand.

### Flame: explosive aggression

| Slot | Clip | Made from | Adaptation |
| --- | --- | --- | --- |
| Stance | `Flame_Stance_Loop` (51) | UAL1 `Sword_Idle` (legs, hips, breathing), `Punch_Jab` frame 1 (arms) | Fists up in the guard; stance widened by 8° per leg (ankles 0.53 m apart become 0.67 m) |
| Ember Fist | `Flame_Punch` (31) | UAL1 `Punch_Cross` | 10° more shoulder turn and 6° lean into the punch; hips and legs of the stance |
| Ember Hook | `Flame_Hook` (34) | UAL2 `Melee_Hook` + `Melee_Hook_Rec` | Joined (the 3.7 cm gap spread over 4 frames); from and back to the stance |
| Flare | `Flame_Flare` (43) | UAL2 `Sword_Regular_A` + `Sword_Regular_A_Rec` (joined without a gap) | From and back to the stance; contact at the fastest frame of the sweep |

### Aegis: grounded power

| Slot | Clip | Made from | Adaptation |
| --- | --- | --- | --- |
| Stance | `Aegis_Stance_Loop` (76) | UAL2 `Idle_Shield_Loop` (upper body, breathing), `Sword_Block` frame 12 (hips and legs) | Chest leaned back upright; stance widened by 13° per leg (0.72 m), hips 0.27 m below standing |
| Force Palm | `Aegis_ForcePalm` (35) | UAL2 `Sword_Block` frames 1 to 35 (the parry's forward drive and deep sink) | Right standing palm (40°), arm drawn back 22° and shoulders turned away first, legs kept low until the sink |
| Bulwark Forearm | `Aegis_Forearm` (29) | UAL2 `Shield_OneShot` | First 4 frames played over 7 (a slower gather), forearm drawn in 18°, 8° turn away then 12° drive; the clip's second movement replaced by the return to the stance |
| Bulwark Slam | `Aegis_Slam` (41) | UAL2 `OverhandThrow` | The left arm takes the right arm's motion, mirrored, so both arms rise and crash down together; both hands open |

## Timing: fitted to the combat clock

`CombatTimeWarp` (Sprint 1) plays each clip's wind-up at the speed that shows its contact frame when the
rules land the hit, and its recovery at the speed that ends it when the rules allow the next action
(0.5x to 3x). With the default numbers:

| Step | Rules: start-up / recovery | Clip | Clip wind-up | Wind-up speed | Starts at | Recovery speed | Visual recovery vs rules |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Rising Palm | 0.10 s / 0.17 s | `Arcane_RisingPalm` | 0.400 s | 3.00x | 10% | 3.00x | +8 ms |
| Circling Palm | 0.10 s / 0.17 s | `Arcane_CirclingPalm` | 0.433 s | 3.00x | 13% | 2.94x | 0 ms |
| Arcane Lance | 0.12 s / 0.28 s | `Arcane_Lance` | 0.300 s | 2.50x | 0% | 2.86x | 0 ms |
| Ember Fist | 0.12 s / 0.26 s | `Flame_Punch` | 0.267 s | 2.23x | 0% | 2.31x | 0 ms |
| Ember Hook | 0.12 s / 0.26 s | `Flame_Hook` | 0.266 s | 2.22x | 0% | 2.95x | 0 ms |
| Flare | 0.16 s / 0.36 s | `Flame_Flare` | 0.266 s | 1.66x | 0% | 2.96x | 0 ms |
| Force Palm | 0.18 s / 0.34 s | `Aegis_ForcePalm` | 0.167 s | 0.93x | 0% | 2.35x | 0 ms |
| Bulwark Forearm | 0.20 s / 0.38 s | `Aegis_Forearm` | 0.200 s | 1.00x | 0% | 1.76x | 0 ms |
| Bulwark Slam | 0.26 s / 0.46 s | `Aegis_Slam` | 0.400 s | 1.54x | 0% | 1.81x | 0 ms |

Every contact is shown within one frame of the hit; `MartialStylesTests` checks it for all nine strikes.
The two Arcane palms still run at the 3x limit and skip the first 10 to 13% of their wind-up (the hand
sinking out of the stance; the blend-in covers it). Aegis plays its wind-ups at about natural speed, so
its anticipation is the most visible; its recoveries hold the impact pose briefly, then rise.

Contact frames, measured on the exported model (30 frames per second): Rising Palm 13 (full reach),
Circling Palm 14, Lance 10, Ember Fist 9 (full extension), Hook 9 and Flare 9 (fastest frame of the
swing), Force Palm 6, Forearm 7, Slam 13. The values are in `CombatAnimationDefaults.cs`.

## What changed in the rules

| Change | Why | Where |
| --- | --- | --- |
| Aegis has a third strike, **Bulwark Forearm** (12 damage, 32 stagger, 0.7 force, 0.20 s / 0.38 s), between Force Palm and Bulwark Slam | The brief asks for three strikes per discipline; Aegis had two | `DisciplineDefaults` |
| Arcane start-ups 0.07, 0.07, 0.10 s became 0.10, 0.10, 0.12 s, with recoveries shortened by the same amount | At 0.07 s no anticipation can be shown even at 3x. Each strike's total time, and so the rhythm and the damage over time, is unchanged; the hit lands 20 to 30 ms later | `DisciplineDefaults` |
| Step names follow the choreography: Rising Palm, Circling Palm, Arcane Lance; Ember Fist, Ember Hook, Flare; Force Palm, Bulwark Forearm, Bulwark Slam | The old names (Jab, Cut, Swipe, Backswing) described other motions | `DisciplineDefaults` |
| **Commitment**: each step can restrict movement further for a moment around its hit (`committedMoveSpeedMultiplier`, `commitBeforeHitSeconds`, `commitAfterHitSeconds`) | Configurable movement restriction for committed strikes; Arcane uses none, the Slam roots the Mage | `MeleeStepProfile`, `MeleeComboMachine` |
| A tap made after the current strike has hit is always performed when its recovery ends | Before, a tap early in a long recovery (the Slam's is 0.46 s, the buffer 0.35 s) was forgotten | `MeleeComboMachine` |
| **Repeat guard**: a second tap within 0.08 s of the tap that started the strike is ignored (`repeatGuardSeconds`, 0 switches it off) | A double touch or a bouncing button no longer queues a second strike | `MeleeComboProfile`, `MeleeComboMachine` |

Hit detection, damage, stagger, reach, arcs and targets of the existing steps are unchanged. The Ember
Fist keeps the Ember Swipe's area (110 degrees, up to three targets), which is wider than a punch looks;
whether to narrow it is a design decision for the owner.

**Assets generated earlier** are upgraded once by the setup (discipline data version 3): old step names
are renamed, Arcane steps whose timing is still the old default take the new split, steps without a
commitment take the default one, and an Aegis combo that is still the two old strikes gets the forearm
strike between them. Tuned damage, stagger, reach, arcs and targets are kept.

## What changed in the animation system

| Change | Effect |
| --- | --- |
| A fourth combat layer, **CombatStanceFull** (no mask), under the upper-body stance layer | Standing still in combat, the whole stance shows, including the low, wide legs of Flame and Aegis; it fades out by 0.6 m/s, so the legs walk normally, while the upper-body stance stays up to 1.5 m/s as before. A controller without the layer still works, with the stance on the upper body only |
| **Hand-over**: a whole-body strike or release that is playing moves to the upper body, at the same moment of the clip, when the Mage moves faster than 1.0 m/s (0.6 + 0.4 margin) or leaves the ground | No foot sliding or mid-air lunge when the player moves or jumps during a committed strike; the strike carries on |
| A stagger no longer covers the arms of a whole-body strike the rules are still carrying out | Sprint 1's rule, "a stagger never hides a swing", now holds for the whole-body layer too |
| `CombatAnimatorBinding` (new) applies the model to an Animator; the director uses it | The comparison figures play exactly as the Mage does |
| The development overlay shows, under the discipline, what each combat layer plays, its speed and weight, and the swing phase, step and movement share | Stuck states and wrong layers can be spotted on a device |
| Combat animation set data version 2 | A set generated by Sprint 1 whose stances and strikes are still Sprint 1's defaults gets the new ones; a discipline changed by hand is kept and named in the setup report |

## Comparing the styles: the Discipline Range

`Threefold > Setup > Discipline Range` (and `Threefold > Setup > Martial Styles Showcase` in any scene)
adds three figures behind the start, on low plinths edged in each discipline's colour: Arcane, Flame and
Aegis from left to right as seen from the start, turned side-on so the strikes read in profile. Each loops
its whole combo with the real combat rules (`ComboShowcaseSequence` taps the real `MeleeComboMachine`), the
real animation model and the Mage's controller. A post in front of each figure jolts at the moment each
strike lands, so the contact frame can be checked against the hit. Every third round plays at 30% speed,
the rules' clock and the Animator together, to check blends and contact frames. The figures deal no damage
and have no colliders; their posts have none either.

## What needs new animation

These are the placeholders and what a designed animation set should supply. Each slot already falls back
safely, so replacing a clip is a data change (see [Replacing a clip](#replacing-a-clip)).

| Slot | Placeholder now | What is wrong with it | Replacement wanted |
| --- | --- | --- | --- |
| Flame strike 3 | `Flame_Flare`, a spinning rising backhand sweep | **The brief asks for a kick; there is no kick in either library** | A powerful kick: a spinning back kick or a stepping front snap kick, right foot, in place (no travel; the game moves the character), 1.0 to 1.4 s, contact 0.25 to 0.30 s after the start at full extension, starts and ends in the Flame stance |
| Arcane strikes 1 and 2 | Casting gestures with an open hand and a bent-back wrist | The palm is a casting reach, not a strike: no root, no sink, little body | A Tai Chi push (a sinking weight shift into a two-handed or single palm) and a Bagua circling palm change with a step, 0.9 to 1.1 s, contact 0.35 to 0.45 s in, hands open throughout, ending in the Arcane stance |
| Arcane strike 3 | A pointing gesture made into a spear hand | Little power and no weight shift | A piercing palm (spear hand) with a forward weight shift, 1.0 to 1.2 s, contact 0.25 to 0.3 s in |
| Aegis strike 1 | A sword parry with the hand opened | Both arms sweep wide; it reads as a parry with a palm | A Hung Gar tiger palm or butterfly palm thrust from the horse stance, 1.0 to 1.2 s, contact 0.15 to 0.2 s in, deep sink, held impact |
| Aegis strike 2 | A shield bash | A forearm block more than a strike | A bridge-hand forearm strike (kiu sau) with a stance shift, 0.9 to 1.0 s, contact about 0.2 s in |
| Aegis strike 3 | An overhand throw made two-handed | The lunge travels 0.34 m and comes back; the arms are a copied mirror | A two-handed downward or forward double palm (twin dragons) with a stomp, 1.2 to 1.4 s, contact 0.35 to 0.45 s in, in place |
| All three stances | Composed from idle loops and single frames | Breathing only; no weight shifts or hand movement | Idle loops of 2 to 3 s per discipline, with small weight shifts and, for Arcane, slow circling hands |
| All strikes | A mannequin with mitten hands | Open and closed hands read weakly | A designed character with modelled fingers (see `Docs/Characters/Mage.md`) |
| Flame and Aegis charge | Arcane's spell gesture mirrored, and a raised forearm | Unchanged from Sprint 1 | See `Docs/CombatAnimationArchitecture.md`, "Missing animations" |

Requirements for every replacement: Humanoid-compatible (the Quaternius rig, or any Humanoid that imports
cleanly), 30 frames per second, in place, starting and ending on the discipline's stance pose, a clear
contact frame at the effector's full extension or fastest frame, and a licence that allows commercial use
(CC0 preferred). Third-party animation must not be committed before its licence has been checked.

## Replacing a clip

1. If the new clip shares the Quaternius rig, add it to `Tools/blender/build_mage.py` (or derive it in
   `martial_styles.py`), rebuild, and check with `Tools/blender/compare_mage.py` that the other clips are
   unchanged. A separate Humanoid file goes under `Assets/Threefold/Art/Characters/`.
2. Measure its moments with `Tools/blender/analyse_martial_styles.py` (add it to `ATTACKS`).
3. Name it in the slot of `MageCombatAnimations` (or in `CombatAnimationDefaults` for new sets), with its
   contact, hit window and recovery end.
4. Run `Threefold > Animation > Apply Combat Animation Set`, then `Validate Combat Animations`.

## Laptop actions

The Android build workflow and every level setup now do the following on their own:

1. Unity re-imports `Mage.fbx` (41 clips; the 12 new ones are added, the 29 earlier ones are unchanged).
2. The setup upgrades the discipline assets (data version 3: the Aegis forearm strike, names, timing,
   commitment) and the combat animation set (version 2: the new stances and strikes), and rebuilds
   `MageAnimator.controller` in place with the full-body stance layer (controller version 4).
3. `Threefold > Setup > Discipline Range` adds the comparison figures. Save the scene afterwards.

Then, in the Editor: `Threefold > Animation > Validate Combat Animations` must report no warnings, and the
setup report must not say that a discipline's strikes were kept because they were changed by hand (unless
they were).

## Review in Unity and on a device

None of these has been seen. Each needs a person to look.

- [ ] The three comparison figures loop their combos; each post jolts as a hand (or both, for the slam) reaches it.
- [ ] In the slow-motion round, no limb snaps between strikes, into a strike, or back into the stance.
- [ ] Arcane: relaxed upright stance with open hands; palms flow in arcs, the second with the right hand; fast and smooth, never stiff.
- [ ] Flame: wide guard; the punch drives from the hips; the hook and the sweep turn the whole body standing, the upper body when moving.
- [ ] Aegis: low, wide stance when standing; legs walk normally when moving; strong wind-up, a held impact, a slower rise.
- [ ] Standing still, the low stances keep the feet on the ground (Humanoid body height with the full-body stance layer is not verified).
- [ ] Moving during a Flame hook or an Aegis slam: the legs return to walking without sliding; jumping during one shows no lunge in the air.
- [ ] Committed strikes slow the Mage around the hit (the slam roots it) and full movement returns after every strike.
- [ ] Repeated taps chain all three strikes of every discipline; a double touch gives one strike; a tap during a long Aegis recovery is not lost.
- [ ] Blocking, climbing, defeat and switching discipline still cancel or finish strikes as before.
- [ ] Joystick, camera drag, tap/hold/drag/release, discipline switching, JUMP and BLOCK behave as before; desktop keys and mouse as before.
- [ ] The development overlay's combat lines return to "-" for the upper and whole body after every strike.
- [ ] Frame time with four combat layers and three extra animated figures in the range (development overlay), on a mid-range phone.

## Verified and not verified

| Check | Result |
| --- | --- |
| The rebuilt `Mage.fbx` has the same skeleton (65 bones), mesh (7,978 vertices, 15,556 triangles) and materials; its 29 earlier clips are unchanged (largest difference 0.235 mm, on the posed Hang) | Verified with `compare_mage.py`, Blender 4.0.2 |
| The 12 new clips: contact frames, settle frames, hip and shoulder rotation, hip sink, travel, stance height and width | Measured with `analyse_martial_styles.py` on the exported file |
| Every join between a stance and a strike, and between consecutive strikes, as the game chains them: pose gap | 0.0 m for all 18 joins (hands, feet and head relative to the hips; hands and head only for upper-body strikes) |
| The code compiles against Unity 6000.6.0f1 (Editor and Android player defines) | Verified, `compile_check.sh` |
| Repository checks | 0 errors, 0 warnings |
| Tests outside Unity: 965 tests, 842 passed, 0 failed, 123 need the Unity engine (before: 933, 811, 0, 122) | Verified in the .NET runner. New: `MartialStylesTests` (26), 6 loop cases |
| Three strikes per discipline, distinct clips, distinct rhythm, contact within one frame of the hit, blends on every change, return to the stance, hand-over, movement restored, buffering, repeat guard, the comparison sequence, the upgrades | Tested in `MartialStylesTests` |
| No stuck state: 120 s of random play (taps, holds, blocks, switches, hits, jumps, climbing, movement) on three seeds leaves no swing, charge, slowdown or layer active, and every action ends with exactly one terminal cue | Tested |
| The animation model allocates nothing per frame | Verified in the .NET runner (`MobilePerformanceTests`) |
| Unity imports the 41-clip model, the new clips loop where they should, the setup upgrades the assets and builds the four layers | **Not verified.** `CombatAnimationSetupTests` checks it in Unity |
| A Humanoid layer without a mask (the full-body stance) lowers the hips correctly; an upper-body layer with the spine does not lift or turn the legs | **Not verified** |
| Anything plays, looks right, feels right, or performs on Android | **Not verified** |

## Code

| Layer | Files |
| --- | --- |
| Clips | `Tools/blender/martial_styles.py` (new), `build_mage.py`; measured by `analyse_martial_styles.py` (new) |
| Rules (plain C#) | `Runtime/Magic/DisciplineProfiles.cs` (commitment, repeat guard), `MeleeComboMachine.cs`, `DisciplineDefaults.cs` (version 3), `DisciplineDefinition.cs` |
| Animation (plain C#) | `Runtime/CombatAnimation/CombatAnimationDefaults.cs` (version 2), `CombatAnimationModel.cs` (full stance, hand-over, stagger rule), `CombatSlotId.cs`, `CombatAnimationSlots.cs`, `ComboShowcaseSequence.cs` (new) |
| Carrying out | `CombatAnimatorBinding.cs` (new), `CombatAnimationDirector.cs`, `CombatStyleShowcase.cs` (new), `DevTools/DevDiagnosticsOverlay.cs` |
| Editor | `Editor/Characters/CombatAnimatorBuilder.cs`, `MageCharacterSetup.cs`, `MartialStylesShowcaseSetup.cs` (new), `Editor/DisciplineRangeSetup.cs` |
| Tests | `Tests/Editor/MartialStylesTests.cs` (new); updated `DisciplineCombatTests`, `CombatAnimationTests`, `CombatAnimationSetupTests`, `CharacterSetupTests` |

## Sources and licences

| Asset | Author | Licence | Where |
| --- | --- | --- | --- |
| Universal Animation Library (Standard) | Quaternius | CC0 1.0 | `Tools/blender/sources/quaternius-ual1/`, https://quaternius.com/packs/universalanimationlibrary.html |
| Universal Animation Library 2 (Standard) | Quaternius | CC0 1.0 | `Tools/blender/sources/quaternius-ual2/`, https://quaternius.com/packs/universalanimationlibrary2.html |

The martial-art references (Tai Chi, Baguazhang, Northern Shaolin, Hung Gar) describe the intended
character of each style. They are not claims that the placeholder motion is authentic to those arts.
