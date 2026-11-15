# The Mage character

The playable Mage is a rigged, animated humanoid. Since Sprint 5 it has two looks on one skeleton:

| Look | File | What it is |
| --- | --- | --- |
| **Wayfarer** (used when Unity accepts it) | `Assets/Threefold/Art/Characters/Mage/MageWayfarer.fbx` and `MageWayfarer_Paint.png` | Hair, a face drawn with a few marks, an asymmetric layered outfit, painted from one texture atlas. Mesh and skeleton only |
| **Library** (the fallback, and the source of every animation) | `Assets/Threefold/Art/Characters/Mage/Mage.fbx` | The first, hooded outfit in nine flat colours, and all 41 animation clips. Unchanged by Sprint 5 |

**Both are intermediate characters, not production art.** The body under both is the mannequin of an
animation library: it has no modelled face, and its clothing is built by a script, not designed and
sculpted by an artist. The Wayfarer look is a proposal for the Mage's identity that can be judged in the
game now; what a production character still needs is listed in `Docs/CharacterIdentity.md`. Nothing in
gameplay depends on either mesh.

How the Mage behaves in each discipline (posture, breathing, hands, head, blinking, the swing of cloak
and hair) is also described in `Docs/CharacterIdentity.md`.

## Before and after

Rendered in Blender from the exported files, before Unity, with Blender's own lighting. They are not
screenshots of the game: the game's shader, contour line and lighting are not in them, and the cloak
and hair hang as they are modelled, without the runtime swing.

| | Front | Three-quarter | Side | Back |
| --- | --- | --- | --- | --- |
| Before (Library) | ![Front](images/mage-front.png) | ![Three-quarter](images/mage-three-quarter.png) | ![Side](images/mage-side.png) | ![Back](images/mage-back.png) |
| After (Wayfarer) | ![Front](images/wayfarer-front.png) | ![Three-quarter](images/wayfarer-three-quarter.png) | ![Side](images/wayfarer-side.png) | ![Back](images/wayfarer-back.png) |

The Wayfarer look in the game's own animation clips (played from `Mage.fbx`):

| Head | Arcane stance | Flame stance | Aegis stance |
| --- | --- | --- | --- |
| ![Head](images/wayfarer-head.png) | ![Arcane](images/wayfarer-arcane.png) | ![Flame](images/wayfarer-flame.png) | ![Aegis](images/wayfarer-aegis.png) |

| Jog | Ember Hook (contact) | Hanging |
| --- | --- | --- |
| ![Jog](images/wayfarer-jog.png) | ![Strike](images/wayfarer-strike.png) | ![Hang](images/wayfarer-hang.png) |

In the Ember Hook image the cloak tails stand out stiffly behind the lunging body. That is the model
without the game's secondary motion, which lets them hang and swing; it is also what the Mage looks
like if that component is switched off.

The earlier renders of the Library look in motion: [jog](images/mage-jog.png),
[casting](images/mage-spell.png), [hanging](images/mage-hang.png), [climbing](images/mage-climb.png).

## What was found (Sprint 5 inspection)

Read from the files with Blender 4.0.2, not assumed.

| Question | Finding |
| --- | --- |
| Model and mesh | `Mage.fbx`: one skinned mesh, 7,978 vertices, 15,556 triangles. The body is the library mannequin (13,744 triangles, most of them in the fingers); the rest is script-built clothing |
| Rig and skeleton | The library's 65 bones: hips, three spine bones, neck, head, clavicles, arms, 15 finger bones per hand with leaf bones, legs, feet, toes. Rest pose is a T-pose, 1.83 m tall |
| Humanoid Avatar | Imported as Humanoid by `CharacterModelPostprocessor`. The setup's Humanoid check passed on the laptop runner for the first version of the file (Android build run 13); later versions have the same skeleton (`compare_mage.py`) |
| Materials and shaders | Nine flat-colour materials. In Unity they are generated as URP Lit, and replaced by the style's `Threefold/Illustrated Lit` with the contour line when the visual style is applied |
| Texture resolution | **None.** The file has no textures, and every texture coordinate of the mannequin is (0, 0), so it could not carry one |
| Facial geometry | **None.** The head is a smooth egg: no eyes, nose, mouth or ears, no jaw or eye bones, no blend shapes (the importer also has blend shapes switched off) |
| Clothing and accessories | A full hood, a symmetric shoulder mantle, a short back cloak, a belt with two pouches, a clasp crystal and a wrist band, all rigid: no bone of their own |
| Animation compatibility | 41 clips on this skeleton (10 for movement and traversal, 19 library combat clips, 12 martial-style clips). Any mesh on the same skeleton plays them unchanged |
| Hand and foot effect points | Not in the model. `DisciplineVfxSockets` asks the Animator for the Humanoid hand, forearm, foot and chest bones at runtime, so they depend on the skeleton only |
| Placeholder or production asset | **An intermediate placeholder**, as this document has said since the model was introduced. It did not support facial animation or cloth movement in any form |

## The Wayfarer look

The brief for the Mage's identity: a recognisable fantasy adventurer, approachable but capable, not a
generic wizard. What the Wayfarer look does with it:

| Part | Description |
| --- | --- |
| Head and hair | Dark chestnut hair swept back with volume, a fringe falling towards the left brow, the sides framing the face, and a short tied tail. Built as grouped locks. No hat and no hood over the face |
| Face | **Drawn marks on the mannequin's blank head, not a modelled face**: two eyes and brows in the contour line's warm dark, a small nose wedge, one short mouth stroke with the corners slightly up. Eyes and brows each follow a bone of their own, so the Mage can blink and the brows can move |
| Hood and mantle | The hood is worn down: a rolled cowl round the neck and its folded bag between the shoulder blades. A half mantle hangs over the right shoulder and narrows to a collar on the left, so the casting arm is free |
| Cloak | Short and split: two tails from under the hood, the left to the hip, the right longer. Each has two bones for secondary motion |
| Tunic | Slate blue, hip length, its hem cut on a slant (lower on the left) with an embroidered edge. Short sleeve on the left arm, long on the right |
| Arms and hands | Left forearm and both hands wrapped in undyed linen up to the knuckles, fingers free, with the glowing rune band on the left wrist. A leather bracer with two brass bands on the right forearm |
| Belt | A madder-red sash under a leather belt with a brass buckle; the sash's end hangs at the right hip on a bone of its own. A pouch on the left hip and a scroll case across the back |
| Legs and feet | Umber trousers; knee boots with a folded cuff, an ankle strap and a painted sole |
| Magical details | Kept few: the crystal clasp on the left of the chest and the rune band, both in the Arcane blue |
| Surface | One painted atlas (1024 x 1024, twelve tiles of 256): washes of warmer and cooler paint, brush strokes that run down each garment, painted shade towards hems, and drawn lines for hems, the sole, stitching, the wrapping bands and hair strands. One further material glows (crystal and rune band) |
| Size | 8,859 vertices, 16,960 triangles, two materials, 74 bones (the library's 65 and nine added), 0.70 MB file and a 0.51 MB texture |

Colours are from the art bible's provisional palette families: slate blue, dark umber, weathered
terracotta, warm leather, undyed linen, a restrained brass and ochre.

### The added bones

No animation clip moves them; `CharacterPersonality` does at runtime, and without it they stay at rest.

| Bone | Below | Moves |
| --- | --- | --- |
| `sec_hair_01`, `sec_hair_02` | Head | The hair tail |
| `sec_cloak_l_01`, `_02` and `sec_cloak_r_01`, `_02` | Top spine bone | The two cloak tails |
| `sec_sash_01` | Hips | The hanging end of the sash |
| `face_eyes` | Head | The two eye marks: squashed along the vertical, they close |
| `face_brows` | Head | The two brow marks: a few millimetres up or down |

### How the two looks coexist in Unity

`Mage.fbx` is not changed: it remains the source of the skeleton, the Humanoid avatar that the
checks know, and every clip. `MageCharacterSetup` uses the Wayfarer mesh only if all of this holds:

1. `MageWayfarer.fbx` and its texture exist;
2. Unity imported the model as a valid Humanoid;
3. its Humanoid mapping names the same bone for every human bone as `Mage.fbx`'s does, and maps no
   others. This catches an added bone mistaken for a body part.

Otherwise the Mage wears the mesh of `Mage.fbx`, exactly as before, and the setup report says why.
A visual prefab that was generated earlier is rebuilt in place once (only its `Model` child is
replaced), so scenes that hold it follow. Running a level's setup again afterwards reconnects the
animation driver, the effect sockets and the painted materials to the new model; until then both
find the new model by themselves when the scene starts.

Two menu commands switch the look by hand, for comparing them in the same scene:
**Threefold > Characters > Use Wayfarer Look** and **Use Library Look**. A look chosen there is kept
by later setup runs.

## How it is made

Everything is generated by code from free sources and can be rebuilt with one command per file.

1. `Tools/blender/build_mage.py` (Blender 4) reads the library files in
   `Tools/blender/sources/`, dresses the body in the first outfit, keeps 28 library animations,
   poses Hang, adds the 12 martial-style clips (`martial_styles.py`) and writes `Mage.fbx`.
2. `Tools/blender/build_mage_wayfarer.py` reads the first library file, paints the atlas, gives every
   face of the body texture coordinates that follow its limb, builds the hair, face marks and outfit
   around the skeleton, adds the nine bones, and writes `MageWayfarer.fbx` and
   `MageWayfarer_Paint.png`. It can also render the preview images above.
3. In Unity, `CharacterModelPostprocessor` imports every model in `Assets/Threefold/Art/Characters`
   as Humanoid, names the clips, marks the loops and keeps the animations in place, and imports the
   painted texture clamped and mip-mapped. No Inspector work.
4. `MageCharacterSetup` generates the materials, the Animator Controller, the masks and a
   `MageVisual` prefab in `Assets/Threefold/Generated/Characters/Mage`, puts the prefab under the
   Mage, hides the capsule, connects the animation driver and adds `CharacterPersonality`.

Step 4 runs inside **Threefold > Setup > Fantasy Playground**, **Threefold > Setup > Mage
Prototype**, the other level setups and the automated Android build. Nothing has to be run separately.

To rebuild the Wayfarer look after changing its script, and check it:

```
blender -b --factory-startup --python Tools/blender/build_mage_wayfarer.py -- \
  Tools/blender/sources/quaternius-ual1/UAL1_Standard.fbx \
  Assets/Threefold/Art/Characters/Mage/MageWayfarer.fbx \
  Assets/Threefold/Art/Characters/Mage/MageWayfarer_Paint.png \
  Assets/Threefold/Art/Characters/Mage/Mage.fbx Docs/Characters/images

blender -b --factory-startup --python Tools/blender/check_mage_wayfarer.py -- \
  Assets/Threefold/Art/Characters/Mage/Mage.fbx Assets/Threefold/Art/Characters/Mage/MageWayfarer.fbx
```

The check fails unless every one of `Mage.fbx`'s 65 bones is in the variant with the same parent and
the same rest position, the extra bones are only the expected ones and each moves something, the
variant holds no animation, every texture coordinate lies clear of its tile's border, and the mesh
stays under 20,000 triangles with its two materials.

To rebuild `Mage.fbx` (not needed for the Wayfarer look):

```
blender -b --factory-startup --python Tools/blender/build_mage.py -- \
  Tools/blender/sources/quaternius-ual1/UAL1_Standard.fbx \
  Tools/blender/sources/quaternius-ual2/UAL2_Standard.fbx \
  Assets/Threefold/Art/Characters/Mage/Mage.fbx Docs/Characters/images
```

Claude Code can run these in a cloud session; Blender is installed there from Ubuntu's packages.

After a rebuild of `Mage.fbx`, compare it with the previous file, so a change touches only what it
meant to:

```
blender -b --factory-startup --python Tools/blender/compare_mage.py -- <previous Mage.fbx> \
  Assets/Threefold/Art/Characters/Mage/Mage.fbx
```

It fails if the skeleton, mesh or materials differ, or if any clip both files share has moved a bone
by more than 1 mm.

## How it moves

`CharacterAnimationDriver` on the Mage reads the movement and combat state and sets these
Animator parameters. Gameplay never reads the Animator.

| Parameter | From | Drives |
| --- | --- | --- |
| Speed | Horizontal speed in m/s | Idle (0), Walk (1.5), Jog (4, normal movement), Sprint (7) |
| Grounded | Ground contact | Falling and landing |
| VerticalSpeed | Vertical speed | Falling only once really falling, not on steps |
| Jump | The motor's jump event | JumpStart, then the fall loop |
| Cast | A successful cast | SpellShoot on the upper body, so the Mage can cast while running. While the combat animation director plays the disciplines (`Docs/CombatAnimationArchitecture.md`), it plays each discipline's own release instead and this layer is silent |

The Animator applies no root motion: the CharacterController moves the character, the animations
only show it. The Mage turns to face its aim when casting, as before.

## Replacing the model later

A designed character replaces the Wayfarer look, not `Mage.fbx`:

- **On the library's skeleton** (same 65 bone names): put its mesh in
  `Assets/Threefold/Art/Characters/Mage/` and point `CharacterModelPostprocessor.MageWayfarerModelPath`
  at it. The same three conditions apply, the same clips play, and nothing else changes. Bones named
  `sec_...` swing, and `face_eyes` and `face_brows` are driven, if the model has them.
- **On another Humanoid skeleton**: Unity retargets the clips, which is the purpose of Humanoid, but
  the third condition (same bone names) then has to be replaced by a check that suits that rig, and
  every clip has to be looked at again: contact poses were measured on this skeleton
  (`Docs/MartialStyles.md`).

Movement, camera, combat and input do not change in either case.

## Verified, and not verified

| Check | Result |
| --- | --- |
| The scripts build both files | Done here, in Blender 4.0.2 headless |
| `Mage.fbx` is unchanged by Sprint 5 | It was not rebuilt; Git shows the same file |
| The Wayfarer model's skeleton contains `Mage.fbx`'s 65 bones with the same parents and rest positions | `check_mage_wayfarer.py` passed |
| The Wayfarer model deforms with the game's clips | Seen in the Blender renders above, in eleven poses. Raising the right arm lifts the mantle's edge with it; a deep lunge shows the cloak stiff without the runtime swing |
| The new C# compiles against Unity 6000.6.0f1 | `Tools/ci/compile_check.sh` passed |
| The rules that choose the look, the import rules and the naming rules | Tests passed outside Unity (`CharacterSetupTests`) |
| Unity imports `Mage.fbx` as a valid Humanoid | The first version of the file was, on the laptop runner (Android build run 13). The file with all 41 clips has the same skeleton and mesh (`compare_mage.py`); its import is recorded in `Docs/LivingMagicTesting.md` where a build ran |
| **Unity imports `MageWayfarer.fbx` as the same Humanoid** | **Not verified.** If it does not, the setup keeps the Library look and reports the reason |
| **The visual prefab is rebuilt in place and scenes follow** | **Not verified.** Written against Unity's prefab API and compiled only |
| **How the Wayfarer look appears in the game: shader, contour line on hair and cloak, texture filtering, on Android** | **Not verified** |
| **Blinking, brows and the swing of cloak, hair and sash in the game** | **Not verified.** The rules behind them are tested outside Unity (`Docs/CharacterIdentity.md`) |

## Known limitations

- **Mannequin body, drawn face.** The Wayfarer look has eyes, brows, a nose wedge and a mouth
  stroke, which read as a face at gameplay distance. Close up it is a stylised figure, not the
  believable, expressive face the art bible asks for. There are no ears, no eyelids, no mouth
  movement, and the hands are the mannequin's.
- **Painted, but not hand-painted.** The atlas is painted by a script: washes, strokes, hems and
  seams. It has no painted folds that follow the actual drape, no painted light on the face, and no
  wear placed by an eye. Metal parts share the painted material and have no highlight of their own.
- **Script-built clothing.** Shapes are rings, tubes and boxes fitted to the body's measurements.
  The skirt's hem follows the thighs but can still be crossed by a high knee; the mantle's right edge
  lifts with the arm.
- **Secondary motion needs the game.** Cloak, hair and sash are rigid in the files and in Blender.
- **Cast hand.** The cast animation raises the left hand, and bolts leave from the chest, as before.
- **Generic clips.** Hit, block and death clips are library clips, not designed for the Mage.

## Sources and licences

| Asset | Author | Licence | Where |
| --- | --- | --- | --- |
| Universal Animation Library (Standard): rig, mannequin, animations | Quaternius | CC0 1.0 | `Tools/blender/sources/quaternius-ual1/`, https://quaternius.com/packs/universalanimationlibrary.html |
| Universal Animation Library 2 (Standard): ClimbUp animation | Quaternius | CC0 1.0 | `Tools/blender/sources/quaternius-ual2/`, https://quaternius.com/packs/universalanimationlibrary2.html |

`MageWayfarer.fbx` and `MageWayfarer_Paint.png` are derived from the first library alone (the mannequin and
its skeleton); everything else in them is generated by `build_mage_wayfarer.py`. No other asset was added.

CC0 places the work in the public domain: it can be used, changed and distributed, commercially
too, without attribution. Credit is given anyway.
