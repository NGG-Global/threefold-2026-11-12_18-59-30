# Main menu (first step)

The main menu is a live 3D scene, not a picture: the Mage stands on an old stone terrace above a valley,
the camera looks at him from slightly below, the menu is on the left and his name is under his feet.
This document covers what this first step builds, what it deliberately leaves out, what was checked and
what the owner has to look at.

**Status:** written; the rules, the interface measurements and the framing are tested outside Unity
(28 tests); everything compiles against the Unity 6000.6.0f1 assemblies for the Editor and with Android
player defines. **Nothing has been seen in Unity or on a device.** The scene has not been generated; the
look of the interface, the framing and the landscape are proposals until the owner has reviewed them in
the Editor and on a device.

## How to see it

On the laptop: **Threefold > Setup > Main Menu**, then Play. The command creates
`Assets/Scenes/MainMenu.unity`, builds everything described below and adds the scene to Build Settings.
Run **Threefold > Setup > Fantasy Playground** once as well if it has not been run: Continue and New Game
load that scene.

The Android build still starts in the Fantasy Playground; the menu becomes its first scene only after the
owner has seen it (see [Next steps](#next-steps)). Since the visual-review build, the development APK holds
the menu as a review scene: open the diagnostics in any level, press **Scenes** and choose **Main Menu**.
Continue and New Game lead from there to the Fantasy Playground. The menu itself has no diagnostics.

## Decisions and proposals

| | State |
| --- | --- |
| A live 3D main menu with the protagonist, a real-time environment and a separate interface | Asked for by the owner (brief of 15 November 2026) |
| Four entries: Continue Adventure, New Game, Settings, Quit; no character creation, class selection, multiplayer or character switching | Asked for by the owner |
| The name **Eryn**, the title **The Young Mage**, and Eryn's described appearance (a young man of 20 to 22, dark brown hair, blue-grey hooded cloak, wooden staff with a crystal) | **Proposal.** The owner chose to treat it as a proposal for now. The Art Bible still lists the playable cast as not decided. The name and title are two constants in `MainMenuLook` |
| The colours and shapes of the interface, the framing, the landscape | **Proposal**, not reviewed |

## What the brief asks for, and what this step contains

| Brief | In this step | Not in this step, and why |
| --- | --- | --- |
| Eryn as described, with an expressive modelled face | The Mage as he is in the game: the Wayfarer look on the library mannequin, with drawn facial marks | The model in the repository is an intermediate character (`Docs/CharacterIdentity.md`). It has swept-back hair with a tied tail and no hood. Eryn as described needs a designed character from an artist or new source material |
| Staff held loosely in one hand | A plain staff with a crystal, built from code, leaning against a column beside him. A placeholder | Holding it needs a staff on the model and a hand pose. On a 4:3 screen the column and the staff are outside the picture |
| Weight on one leg, relaxed posture | The game's idle animation with the discipline bearing on top | A menu idle with shifted weight is new animation |
| Breathing, blinking, hair and clothing that move | Breathing, blinking, brow movement, and hair and cloak bones on springs, from the existing `CharacterPersonality` | The springs react to the body's movement. There is no wind, so on a standing character they mostly rest |
| Posture changes, inspecting the staff, shimmer at the fingertips | None | New animation and a new effect; see [What needs new art or animation](#what-needs-new-art-or-animation) |
| Terrace above a wide valley, golden late-afternoon light, haze | Paved terrace with the remains of a parapet, two columns, rubble, moss and grass; a valley of 260 m with a forest, rocks and distant ruins; the mountains and the ruined spire of the levels; the levels' late-afternoon sun, sky and distance fog; the visual style on top | Everything comes from the existing environment kit |
| Town, river, ivy, wildflowers, drifting clouds, moving foliage | None. Moss stands in for ivy | The kit has no buildings, water, flowers or clouds, and the style's shader has no wind |
| Logo top left, four entries below it, large enough to touch | The title in letters with a gold rule and three marks; four entries of 232 by 48 units | A drawn logo and a fantasy typeface are new assets (a font file needs a licence record). The text uses Unity's built-in font |
| Carved stone, aged bronze, gold accents, parchment lettering | Flat colours in those materials' hues: a bronze frame round a dark plate, two fine gold lines, a mark at each end | Carved or painted surfaces need textures |
| Warm highlight on the selected entry | A warm highlight that brightens and dims over 2.6 seconds | Transitions between menu states are immediate |
| Name plate near Eryn, optionally his level | "ERYN / The Young Mage" under his feet, placed from the camera's view for any screen shape | The game has no character level |
| Settings | Opens the graphics panel (Performance, Balanced, Quality) that every level already has | Sound and control settings do not exist yet |

## How it behaves

- **Continue Adventure** can be chosen when the world state holds anything (`WorldState.Current`), and is
  dimmed otherwise. In the Editor the world state is kept in memory only, so Continue is dimmed there unless
  **Threefold > World State > Keep Saved State Between Play Sessions** is ticked.
- **New Game** starts at once when nothing is saved. When something is saved it asks first; the answer that
  keeps the saved adventure is highlighted, and Back (Escape) keeps it too. Beginning anew calls
  `WorldState.NewGame`, which forgets everything in memory and on disk.
- Continue and New Game both load the Fantasy Playground. Which level the player was in is not remembered
  yet, so Continue resumes with the remembered world, not at the remembered place.
- **Settings** opens the graphics panel. The entries are hidden while it is open.
- **Quit** closes the app. In the Editor it only writes a line to the Console.
- Keyboard: Up and Down move the highlight, Enter or Space chooses, Escape leaves the question.
- If the game scene is not in Build Settings, the menu says that the adventure could not be opened and stays
  usable; the Console names the scene.

The project's buttons read the pointer themselves and do not block one another, so one touch could press an
entry and, in the same frame, whatever appears in its place (the answers to the question stand where the
entries were). The rules therefore accept nothing after a change of state until the pointer has been seen
released. Several of the tests are about this.

## How it is built

| Part | Where | Kind |
| --- | --- | --- |
| Rules: entries, highlight, question, commands | `MainMenuModel` | Plain C# |
| Interface measurements, in canvas units (1 unit = 1 dp) | `MainMenuLayout` | Plain C# |
| Scene measurements: character, camera, terrace, land; where a point appears on screen | `MainMenuStage` | Plain C#, vector arithmetic only |
| Colours, captions, the highlight's rhythm, name and title | `MainMenuLook` | Plain C# |
| Carries the rules out: buttons, keys, loading, the name plate's place | `MainMenuScreen` | Component |
| Builds the scene | `MainMenuSetup` (**Threefold > Setup > Main Menu**) | Editor command |

Change numbers in `MainMenuStage` and `MainMenuLayout`, never in the generated scene; the tests check them.
The command can be run repeatedly: the generated environment is replaced, and the camera, the character,
the interface and anything added by hand are kept.

**Framing.** Vertical field of view 30 degrees; camera 0.62 m above the floor, 4.86 m in front of the
character and 0.8 m to his left, looking up by 2 degrees. From `MainMenuStage.Project`:

| Screen | Feet (across, up) | Head (up) | Share of the height |
| --- | --- | --- | --- |
| 4:3 | 0.73, 0.20 | 0.90 | 0.70 |
| 3:2 | 0.71, 0.20 | 0.90 | 0.70 |
| 16:9 | 0.67, 0.20 | 0.90 | 0.70 |
| 21:9 | 0.63, 0.20 | 0.90 | 0.70 |

The parapet is open in the middle: with the camera this low, a parapet across the view would hide the
valley. The valley floor comes into view over the terrace's edge about 113 m from the camera.

**Cost.** One camera, one directional light with shadows, no additional lights. The scene has about 140
static objects from the kit and one land mesh of about 7,800 triangles (both counted from the setup code,
not measured in Unity). The interface is one canvas, with the entries on a canvas of their own because the highlight
changes every frame. Nothing has been profiled.

## What was checked

- `python3 Tools/ci/check_unity_repo.py`: no errors.
- `Tools/ci/compile_check.sh`: all assemblies compile; 1,147 tests, 1,016 passed, 0 failed, 131 need the
  Unity engine. `MainMenuTests` adds 28 that run outside Unity:
  - rules (14): what can be chosen with and without a saved game, the question, the same touch never
    answering it, the settings panel, the highlight skipping what cannot be chosen, a level that fails to load;
  - interface (4): entries of at least 48 units that do not overlap, the column fitting a canvas 340 units
    high, the question standing in the entries' place, the highlight staying between its limits;
  - framing (5): the character filling 67 to 73% of the height with room under his feet, standing right of
    centre and wholly on screen, the menu column never reaching him and the name plate clear of the column,
    from 3:2 to 21:9 on the smallest canvas and on a 4:3 tablet;
  - land (5): under the terrace at its edge, the valley floor in view, nothing in front hiding the spire, the
    far hills meeting the spire, no steps the grid cannot draw.
- `MainMenuAssemblyTests` (2) build the scene in Unity and compare the plain projection with the engine's
  camera. They need Unity in batch mode and have **not run**.

**Not checked:** that Unity generates the scene; how any of it looks; that the Mage rests in his idle
animation on the terrace; text sizes and legibility over the landscape; performance on a device. The 4:3
tablet in the tests is assumed to be at least 600 units high; that is an assumption about devices. The
48-unit touch target follows the Android accessibility guidance as I know it; verify it against the current
Android documentation.

## What needs new art or animation

| Missing | Stands in for it now |
| --- | --- |
| Eryn as a designed character: face, hair, hooded cloak, embroidery | The Wayfarer look on the mannequin |
| A staff on the model, and a hand pose that holds it | A staff leaning on a column |
| A menu idle: weight on one leg, posture changes, a look at the staff | The game's idle animation |
| A shimmer at the fingertips | Nothing |
| Town, river, ivy, wildflowers, clouds | Forest, rocks, ruins, moss |
| Wind in grass, foliage, hair and cloak | Nothing |
| A drawn logo and a typeface | Unity's built-in font |
| Carved stone and bronze surfaces for the interface | Flat colours |

## Next steps

One at a time, each after the owner has seen the one before:

1. The owner reviews the scene in the Editor: framing, the character's stance, the interface's size and
   colours, the landscape. Numbers are then tuned in `MainMenuStage`, `MainMenuLayout` and `MainMenuLook`.
2. Make the menu the first scene of the Android build, with its own validation in the build, and review it
   on a device.
3. Remember the level the player was in, so that Continue resumes there, and add a way back to the menu
   from a level.
4. Decide on Eryn (record it in the Art Bible), then the character art, the staff and the menu idle.
5. Landscape pieces (town, river, clouds, wind), a typeface and a logo, each with its source and licence.
