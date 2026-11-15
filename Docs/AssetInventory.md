# Asset inventory

Third-party and generated art assets in the repository, with their real paths, sources and
licences. Checked against the files on `main` on 15 November 2026. Binary files are stored through
Git LFS; a cloud checkout holds only pointers until `git lfs pull` is run.

## Character

| Asset | Path | Source | Licence |
| --- | --- | --- | --- |
| Mage model: mesh, 65-bone Humanoid rig, 41 animation clips, 9 materials (14.7 MB) | `Assets/Threefold/Art/Characters/Mage/Mage.fbx` | Built by `Tools/blender/build_mage.py` from the two libraries below | CC0 (derived) |
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
| `Punch_Jab` | 1 | 27 | 0.867 | 0 | 0 | Sprint 1 Arcane stance and jab; source of the Flame guard (frame 1) |
| `Punch_Cross` | 1 | 31 | 1.000 | 0 | 0 | Sprint 1 Arcane Cut; source of `Flame_Punch` |
| `Spell_Simple_Enter` | 1 | 17 | 0.533 | 0.03 m | 0 | Arcane and Flame (mirrored) charge start; source of the Arcane stance and palms |
| `Spell_Simple_Idle_Loop` | 1 | 64 | 2.100 | 0 | 0.01 m | Arcane and Flame (mirrored) charge hold |
| `Spell_Simple_Exit` | 1 | 14 | 0.433 | 0.03 m | 0 | Arcane and Flame (mirrored) charge cancel |
| `Sword_Idle` | 1 | 51 | 1.667 | 0 | 0 | Sprint 1 Flame stance; source of `Flame_Stance_Loop` |
| `Sword_Attack` | 1 | 47 | 1.533 | 0 | 0.21 m | Sprint 1 Ember Swipe and Backswing (not used since Sprint 2) |
| `Hit_Chest` | 1 | 11 | 0.333 | 0.03 m | 0 | Stagger |
| `Hit_Head` | 1 | 14 | 0.433 | 0 | 0 | Heavy stagger |
| `Push_Loop` | 1 | 81 | 2.667 | 0 | 0.05 m | Force Pulse release (frames 37–49) |
| `Death01` | 1 | 73 | 2.400 | **0.94 m** | 0.83 m | Defeat |
| `Sword_Regular_B` | 2 | 17 | 0.533 | 0.04 m | 0.09 m | Sprint 1 Flare (not used since Sprint 2) |
| `Melee_Hook` | 2 | 15 | 0.467 | **0.36 m** | 0.30 m | Sprint 1 Bulwark Slam; source of `Flame_Hook` |
| `Shield_OneShot` | 2 | 26 | 0.833 | 0 | 0.04 m | Aegis charge start, hold (frame 8 held) and cancel; source of `Aegis_Forearm` |
| `Sword_Block` | 2 | 38 | 1.233 | 0 | 0.23 m | Sprint 1 Aegis stance; source of the Aegis stance legs and `Aegis_ForcePalm` |
| `Idle_Shield_Loop` | 2 | 76 | 2.500 | 0 | 0.01 m | Block hold |
| `Idle_Shield_Break` | 2 | 33 | 1.067 | 0 | 0.05 m | Block impact |
| `OverhandThrow` | 2 | 41 | 1.333 | 0 | 0.25 m | Flame Wave release; source of `Aegis_Slam` |
| `NinjaJump_Start` | 2 | 30 | 0.967 | **0.38 m** | 0 | Aerial attack and aerial release (from frame 6) |

### Martial-style clips (Sprint 2)

Derived from library motion by `Tools/blender/martial_styles.py` (`Docs/MartialStyles.md`); measured by
`Tools/blender/analyse_martial_styles.py`. "Travel" is the largest distance of the hips from where the
clip starts; every one of these clips ends where it started.

| Clip | Frames | Seconds | Made from | Travel | Used by |
| --- | --- | --- | --- | --- | --- |
| `Arcane_Stance_Loop` | 76 | 2.500 | UAL1 `Idle_Loop`, `Spell_Simple_Enter` | 0 | Arcane stance |
| `Arcane_RisingPalm` | 31 | 1.000 | UAL1 `Spell_Simple_Enter` + `Spell_Simple_Exit` | 0 | Rising Palm |
| `Arcane_CirclingPalm` | 31 | 1.000 | The same | 0 | Circling Palm (mirrored) |
| `Arcane_Lance` | 36 | 1.167 | UAL1 `Interact` | 0 | Arcane Lance |
| `Flame_Stance_Loop` | 51 | 1.667 | UAL1 `Sword_Idle`, `Punch_Jab` | 0 | Flame stance |
| `Flame_Punch` | 31 | 1.000 | UAL1 `Punch_Cross` | 0 | Ember Fist |
| `Flame_Hook` | 34 | 1.100 | UAL2 `Melee_Hook` + `Melee_Hook_Rec` | 0.46 m | Ember Hook |
| `Flame_Flare` | 43 | 1.400 | UAL2 `Sword_Regular_A` + `Sword_Regular_A_Rec` | 0.25 m | Flare |
| `Aegis_Stance_Loop` | 76 | 2.500 | UAL2 `Idle_Shield_Loop`, `Sword_Block` | 0 | Aegis stance |
| `Aegis_ForcePalm` | 35 | 1.133 | UAL2 `Sword_Block` | 0.03 m | Force Palm |
| `Aegis_Forearm` | 29 | 0.933 | UAL2 `Shield_OneShot` | 0 | Bulwark Forearm |
| `Aegis_Slam` | 41 | 1.333 | UAL2 `OverhandThrow` | 0.34 m | Bulwark Slam |

### In the libraries, not in the model

Available for later work; adding one is a change to `build_mage.py`. Rendered and measured during
sprint 1; reasons they were left out:

| Clip | Library | Why not used now |
| --- | --- | --- |
| `Melee_Hook_Rec` | 2 | Joined to the hook in `Flame_Hook` (the 3.7 cm gap spread over four frames); not exported on its own |
| `Sword_Regular_A`, `_A_Rec` | 2 | Joined in `Flame_Flare`; not exported on their own |
| `_B_Rec`, `_C`, `Sword_Regular_Combo`, `Sword_Heavy_Combo`, `Sword_Dash` | 2 | Deep lunges with about 0.3 m of travel, or several strikes in one clip |
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
| `Tools/blender/martial_styles.py` | Derives the martial-style clips from library motion (run by `build_mage.py`) |
| `Tools/blender/analyse_martial_styles.py` | Measures the martial-style clips in the exported model; renders the review sheets in `Docs/MartialStyles/images` |
| `Tools/blender/compare_mage.py` | Compares two builds of the model: skeleton, mesh, materials, and every shared clip bone by bone |
| `Tools/visualstyle/render_previews.sh` | Renders the visual style previews outside Unity |
