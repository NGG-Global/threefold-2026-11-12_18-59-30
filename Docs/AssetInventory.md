# Asset inventory

Third-party and generated art assets in the repository, with their real paths, sources and
licences. Checked against the files on `main` on 15 November 2026. Binary files are stored through
Git LFS; a cloud checkout holds only pointers until `git lfs pull` is run.

## Character

| Asset | Path | Source | Licence |
| --- | --- | --- | --- |
| Mage model: mesh, 65-bone Humanoid rig, 29 animation clips, 9 materials (10.5 MB) | `Assets/Threefold/Art/Characters/Mage/Mage.fbx` | Built by `Tools/blender/build_mage.py` from the two libraries below | CC0 (derived) |
| Universal Animation Library, Standard | `Tools/blender/sources/quaternius-ual1/UAL1_Standard.fbx` (outside `Assets/`, not imported) | Quaternius, https://quaternius.com/packs/universalanimationlibrary.html | CC0 1.0, `License.txt` beside it |
| Universal Animation Library 2, Standard | `Tools/blender/sources/quaternius-ual2/UAL2_Standard.fbx` (outside `Assets/`) | Quaternius, https://quaternius.com/packs/universalanimationlibrary2.html | CC0 1.0, `License.txt` beside it |

Details of the model and how it is built: `Docs/Characters/Mage.md`.

### Clips in the Mage model

Locomotion and traversal (unchanged since the model was first built):
Idle, Walk, Jog, Sprint, JumpStart, JumpLoop, JumpLand, SpellShoot, ClimbUp (from UAL2
`ClimbUp_1m`), Hang (posed in the build script).

Combat clips, added for `Docs/CombatAnimationArchitecture.md`. They keep the library names. Frames
at 30 per second. "Travel" is how far the hips move along the ground over the clip and "drop" how
far they sink, measured by `Tools/blender/analyse_combat_clips.py`; both are baked into the pose in
Unity, so the body moves away from the character's position and comes back.

| Clip | Library | Frames | Seconds | Travel | Drop | Used by |
| --- | --- | --- | --- | --- | --- | --- |
| `Punch_Jab` | 1 | 27 | 0.867 | 0 | 0 | Arcane stance (frame 1 held), Arcane Jab |
| `Punch_Cross` | 1 | 31 | 1.000 | 0 | 0 | Arcane Cut |
| `Spell_Simple_Enter` | 1 | 17 | 0.533 | 0.03 m | 0 | Arcane Lance; Arcane and Flame (mirrored) charge start |
| `Spell_Simple_Idle_Loop` | 1 | 64 | 2.100 | 0 | 0.01 m | Arcane and Flame (mirrored) charge hold |
| `Spell_Simple_Exit` | 1 | 14 | 0.433 | 0.03 m | 0 | Arcane and Flame (mirrored) charge cancel |
| `Sword_Idle` | 1 | 51 | 1.667 | 0 | 0 | Flame stance |
| `Sword_Attack` | 1 | 47 | 1.533 | 0 | 0.21 m | Ember Swipe, Ember Backswing (mirrored) |
| `Hit_Chest` | 1 | 11 | 0.333 | 0.03 m | 0 | Stagger |
| `Hit_Head` | 1 | 14 | 0.433 | 0 | 0 | Heavy stagger |
| `Push_Loop` | 1 | 81 | 2.667 | 0 | 0.05 m | Force Pulse release (frames 37–49) |
| `Death01` | 1 | 73 | 2.400 | **0.94 m** | 0.83 m | Defeat |
| `Sword_Regular_B` | 2 | 17 | 0.533 | 0.04 m | 0.09 m | Flare |
| `Melee_Hook` | 2 | 15 | 0.467 | **0.36 m** | 0.30 m | Bulwark Slam |
| `Shield_OneShot` | 2 | 26 | 0.833 | 0 | 0.04 m | Force Palm; Aegis charge start, hold (frame 8 held) and cancel |
| `Sword_Block` | 2 | 38 | 1.233 | 0 | 0.23 m | Aegis stance (frame 12 held) |
| `Idle_Shield_Loop` | 2 | 76 | 2.500 | 0 | 0.01 m | Block hold |
| `Idle_Shield_Break` | 2 | 33 | 1.067 | 0 | 0.05 m | Block impact |
| `OverhandThrow` | 2 | 41 | 1.333 | 0 | 0.25 m | Flame Wave release |
| `NinjaJump_Start` | 2 | 30 | 0.967 | **0.38 m** | 0 | Aerial attack and aerial release (from frame 6) |

### In the libraries, not in the model

Available for later work; adding one is a change to `build_mage.py`. Rendered and measured during
sprint 1; reasons they were left out:

| Clip | Library | Why not used now |
| --- | --- | --- |
| `Melee_Hook_Rec` | 2 | Recovery of the hook; its first pose is 3.7 cm from the hook's last, so joining them would pop |
| `Sword_Regular_A`, `_A_Rec`, `_B_Rec`, `_C`, `Sword_Regular_Combo`, `Sword_Heavy_Combo`, `Sword_Dash` | 2 | Deep lunges with about 0.3 m of travel, or several strikes in one clip |
| `Hit_Knockback`, `LayToIdle` | 2 | A knockdown and the getting up from it; the player has no knockdown |
| `NinjaJump_Idle_Loop`, `NinjaJump_Land` | 2 | Airborne hold and landing of the leap; useful for a future aerial ability |
| `Shield_Dash`, `Roll` | 2, 1 | Movement with travel; no dodge exists |
| `Pistol_*`, `Sitting_*`, `Farm_*`, `Zombie_*`, `Swim_*`, `Driving_Loop`, `Dance_Loop` and others | 1, 2 | Not combat |

Full measurements of every candidate: run `analyse_combat_clips.py` (it writes JSON and, on
request, frames for review).

## Visual effects packs

Three Asset Store packs, committed in `41d0d42`. Audit, classification and adaptation plan:
`Docs/MagicVFX_Audit.md`. The repository is private; the Asset Store licence applies if that changes.

| Pack | Path |
| --- | --- |
| Free Slash VFX | `Assets/Free Slash VFX/` |
| Magic Effects FREE (Hovl Studio) | `Assets/Hovl Studio/Magic effects pack/` |
| Free Fire VFX URP (Vefects) | `Assets/Vefects/Free Fire VFX URP/` |

## Generated on the laptop (not in GitHub)

Produced by the setup commands under `Assets/Threefold/Generated/`. They exist only where a setup
has run (the laptop and its build runner) and are never authored here.

| Folder | Made by | Contents |
| --- | --- | --- |
| `Generated/Characters/Mage/` | `MageCharacterSetup` | `MageAnimator.controller`, `MageVisual.prefab`, `MageUpperBody.mask`, `MageCombatUpperBody.mask`, `MageCombatAnimations.asset`, `Materials/` |
| `Generated/Disciplines/` | `DisciplineSetup` | The three discipline assets, their charged abilities and effects |
| `Generated/VFX/` | `MagicVfxSetup` | Adapted discipline effects |
| `Generated/Combat/`, `Environment/`, `VisualStyle/`, `Puzzles/`, `Navigation/` and others | The level setups | See the document of each system |

## Tools that produce or check art

| Tool | Purpose |
| --- | --- |
| `Tools/blender/build_mage.py` | Builds `Mage.fbx` from the two libraries |
| `Tools/blender/analyse_combat_clips.py` | Measures contact, reach, settle and travel of candidate clips; renders frames for review |
| `Tools/blender/compare_mage.py` | Compares two builds of the model: skeleton, mesh, materials, and every shared clip bone by bone |
| `Tools/visualstyle/render_previews.sh` | Renders the visual style previews outside Unity |
