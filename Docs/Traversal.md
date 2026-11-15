# Traversal

Sprint 7 adds ledge catching and hanging, shimmying, climbing up, vaulting, climbing onto
obstacles, guarded edges with controlled drops, a heavier jump, landing recovery and softer
camera height tracking. A training course in the Fantasy Playground exercises all of it.

**Status:** the code compiles against Unity 6000.6.0f1 and its geometric rules pass their tests,
but **none of it has run in Unity**. How it feels, and whether every movement starts when it
should, can only be judged by playing. Expect tuning.

## What the player can do

| Situation | What happens | Input |
| --- | --- | --- |
| Jumping at a wall whose top is 1.25 to 2.25 m above the feet | The Mage catches the edge and hangs | Push towards the wall while in the air |
| Jumping at a lower ledge | The Mage climbs straight onto it | Push towards it |
| Hanging | Climb up | Jump, or keep pushing towards the wall for a moment |
| Hanging | Shimmy along the ledge, which stops where the ledge ends or is blocked | Push left or right |
| Hanging | Let go and drop | Pull away from the wall |
| Hanging | Jump off backwards | Pull away and press jump |
| Running at a thin obstacle up to 1.15 m high | Vault over it and keep running | Run into it (at least 2.5 m/s) |
| Walking or running into a deeper obstacle up to 1.25 m | Climb onto it | Push into it |
| Walking towards a drop deeper than 1.8 m | Stop at the edge | Keep pushing for a moment to lower yourself and hang from the edge; sprint or jump to go over |

**Design rules**

- Nothing starts unless the player pushes towards it, at least 60 degrees on centre. Walking past
  a wall, or slowly up to a rail, does nothing.
- Every movement checks, before it starts, that the character fits at each point of its path.
  If not, it does not start. The character is never moved through geometry.
- Invisible boundary walls cannot be climbed. They are on the Ignore Raycast layer, which
  traversal ignores.
- Stairs are recognised and walked up normally, never climbed.
- Casting is disabled while hanging, climbing or vaulting.

**Jumping and landing**

Since Sprint 4 a second press of Jump in the air starts the selected discipline's aerial ability; the first
jump below is unchanged. See `Docs/AerialAbilities.md`.

Coyote time (0.12 s), jump buffering (0.12 s), variable height and air control already existed
and are unchanged. New:

- Falling uses 1.35 times the gravity of rising, so jumps rise smoothly and come down with weight.
- Near the top of a held jump, gravity is reduced to 60%, which gives a moment of control.
- Landing faster than 13 m/s is a hard landing: speed is reduced to 35% for 0.25 s, and the
  landing animation always plays out.

**Camera**

The camera now follows height changes over 0.18 s instead of 0.06 s, so steps, jumps and climbs
do not jolt the view. It never trails by more than 1.2 m vertically, so long falls stay in frame.
Sideways following is unchanged.

## Mobile controls

No new buttons. Traversal uses the joystick and the existing jump button:

- push the joystick towards a ledge while jumping to catch it;
- push towards the wall, or press jump, to climb up;
- push sideways to shimmy, away to drop, away with jump to leap off;
- run into rails and crates to vault and climb.

Edge guarding is especially useful on a touchscreen, where a thumb can easily overshoot.

## The training course

Built by **Threefold > Setup > Fantasy Playground**, in dark stone, a few steps to the left of
the starting point:

| Element | Size | Tests |
| --- | --- | --- |
| VaultRail | 0.9 m high, 0.4 m deep | Vaulting at a run; walking up to it does nothing |
| MantleCrate | 1.1 m cube-ish | Climbing onto from the ground |
| StepLow, StepMantle | 0.25 m, 0.6 m | Walking up a step; climbing onto a low block |
| ClimbWall | 2.4 m high, 5 m long face | Climbing on from the crate or with a jump; shimmying; edge guard and lowering to hang |
| HangWall | 3.2 m, beside ClimbWall | Catching a ledge from the ground and hanging; climbing up |
| GapPlatform | 2.4 m, 1.65 m gap from ClimbWall | A jump across a gap at height; drops |

The existing stairs to the terrace and the jump course on the east side remain.

## How it is built

| File (under `Assets/Threefold/Scripts/Runtime/Characters`) | Role |
| --- | --- |
| `TraversalController.cs` | Detection with sphere casts and raycasts, the hang state, paths for climbs, vaults and drops |
| `TraversalRules.cs` | The decisions as plain functions: vault or climb, catch or climb onto, wall or floor, positions, path following |
| `ThirdPersonMotor.cs` | New: hand-over to traversal, blocking a direction at an edge, launches, fall and apex gravity, hard landings |
| `CharacterAnimationDriver.cs` | New parameters: Hanging, Shimmy, Climb, Vault, LandImpact |
| `ThirdPersonCamera.cs` | Separate vertical smoothing with a lag limit |

While a movement plays, the motor is paused and its collider switched off, and the traversal
component places the character along a short path with eased timing. The path is checked for
space beforehand. Physics queries use fixed buffers; nothing allocates memory per frame.

All distances, heights, speeds and durations are fields on the `Mage` object's Traversal
Controller and Third Person Motor, with tooltips.

## Animation

| Movement | Clip | Source |
| --- | --- | --- |
| Hanging, shimmying | Hang | **Posed in code** by `Tools/blender/build_mage.py` (arms up, legs relaxed). Neither library has a hang |
| Climbing up, climbing onto | ClimbUp | `ClimbUp_1m` from Quaternius' Universal Animation Library 2 (CC0), played at 1.2 times speed |
| Vaulting | ClimbUp | The same clip at 1.9 times speed |
| Hard landing | JumpLand | Plays out fully when landing faster than 13 m/s |

The character's position during these movements is procedural; the clips only show the body.
There is no dedicated shimmy, drop-to-hang or vault animation. The Animator has a `Shimmy`
parameter ready for a shimmy clip when one exists.

The Animator Controller is regenerated in place when its layout changes, so existing scenes and
the build machine pick up the new states automatically.

## Verified, and not verified

| Check | Result |
| --- | --- |
| All code compiles against Unity 6000.6.0f1, including with Android player defines | Passed (`Tools/ci/compile_check.sh`) |
| Traversal rules: classification, surfaces, input direction, positions, easing, paths | 18 tests passed outside Unity. One defect was found and fixed: a thin obstacle slightly too tall to vault did nothing instead of being climbed onto |
| The character model with the new Hang and ClimbUp animations | Built and rendered in Blender |
| **Any traversal movement in Unity** | **Not verified** |
| **Feel: timings, distances, camera** | **Not verified** |
| **The course layout in the scene** | **Not verified**; positions were checked by calculation against existing objects |
| **On Android** | **Not verified** |

## Known limitations

- Ledges must be roughly horizontal, on roughly vertical walls. Round columns and sloped roofs
  are not caught.
- Shimmying stops at a corner; it does not go around it.
- No wall running, swinging, ladders, ropes or climbing across faces without ledges.
- Moving platforms are not supported: a ledge is assumed not to move while hanging.
- While hanging, the character cannot be hit, because its collider is off. Nothing attacks it yet.
