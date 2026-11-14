# Visual style: Soft Illustrated Realism

Sprint 11 implements the art direction of `Docs/ArtBible.md` in Unity's Universal Render
Pipeline: painted materials, soft light with warm and cool contrast, delicate linework, and the
tooling to apply it. It is applied to the Fantasy Playground and the Combat Arena, and a small
test scene, the Style Lab, is provided for judging it.

**Status: a proposal for review, not an approved result.** The art bible lists shaders, lighting
profile, palette swatches and quality tiers as "proposed, pending validation" (section 12), and
says a prototype "passes only after visual review and on-device performance verification"
(section 10). Neither has happened. **Nothing here has run in Unity, and nothing has been
measured on a device.** What was checked outside Unity is listed under [Verification](#verification).

The art bible is in the repository as `Docs/ArtBible.md` (version 1.0). The sprint brief named
`THREEFOLD_ART_BIBLE.md`; no file of that name exists, and this one carries the same title. If a
newer version exists elsewhere, this work should be checked against it.

## What it looks like

These images were rendered outside Unity by `Tools/visualstyle/render_previews.sh`. The textures
are exactly what the game will generate. The lighting is computed with a C# copy of the shader's
arithmetic. They are **not screenshots of the game**: Unity's renderer, shadow maps, fog and the
real meshes are not involved.

| Image | Shows |
| --- | --- |
| ![Textures](VisualStyle/images/textures.png) | The twelve painted textures, each tiled two by two. Top row: stone, dark stone, paving, wood. Middle: bark, two cloths, metal. Bottom: foliage, grass, moss, ground |
| ![Materials](VisualStyle/images/materials_GoldenAfternoon.png) | Every material preset on a ball and a block under the default lighting, Golden Afternoon. In reading order: stone, dark stone, paving, wood, bark, cloth, cool cloth; then metal, foliage, grass, moss, ground, mountain |
| ![Overcast](VisualStyle/images/materials_OvercastMorning.png) | The same under the second lighting condition, Overcast Morning |
| ![Comparison](VisualStyle/images/light_response_comparison.png) | Stone, wood, cloth, metal and foliage three ways. Top: flat colour, plain light response. Middle: painted texture, plain response. Bottom: painted texture, the style's response: softer turning, cooler shade, a small highlight on metal only |
| ![Line weight](VisualStyle/images/outline_weights.png) | The contour line on a 1.8 m figure on a 1080-pixel-high screen: at 5 m without and with the line, then at 12 m, 25 m and 40 m. The line keeps its width, then fades out with distance |

Full-size samples: [stone](VisualStyle/images/texture_Stone.png), [foliage](VisualStyle/images/texture_Foliage.png).

## How to use it

1. Pull `main` on the laptop and let Unity import. The Console must show no red errors. Look in
   particular for errors naming `IllustratedLit`.
2. Run **Threefold > Setup > Fantasy Playground** and choose Rebuild. The level is now built with
   the style. (Or, in any scene that already has the level, run **Threefold > Visual Style > Apply
   To Open Scene**.)
3. Run **Threefold > Visual Style > Check Rendering Settings**. It lists what it found and
   proposes one change (see [Project settings](#project-settings)). Decide there.
4. Press Play. Use the three buttons in the top right corner, or the keys, to compare:

| Button | Key | Switches |
| --- | --- | --- |
| Quality | F6 | Low, Medium, High |
| Lines | F7 | The Mage's contour line on and off |
| Light | F8 | Golden Afternoon and Overcast Morning |

The buttons appear in the Editor and in development builds only. What is chosen with them is
kept for the rest of the run, including through the portal to the arena.

**The Editor does not start at the level a phone uses.** In the Editor the controller starts at
High, which adds the project's bloom, tone mapping and vignette. A phone starts at Medium or Low,
without them. Press F6 until the panel reads Medium to see in the Editor what a mid-range phone
is given. The preview images above correspond to Medium: they contain no post-processing.

| Command (under **Threefold >**) | What it does |
| --- | --- |
| Visual Style > Apply To Open Scene | Generates missing style assets, replaces grey-box materials in the scene, gives the Mage its line, sets the lighting, adds the controller and the test panel |
| Visual Style > Remove From Open Scene | Puts the grey-box materials back, sets the lighting of the generated levels (playground, arena, lab) and removes what was added, so the two looks can be compared. The assets stay. In a scene lit by hand, the lighting it sets is the generated levels', not that scene's own |
| Visual Style > Check Rendering Settings | Reports on the project's rendering settings. Changes one of them only if you choose "Change". Not available in Play mode |
| Visual Style > Repaint Textures | Paints the textures again from code, after asking. Overwrites textures edited by hand |
| Setup > Style Lab | Builds `Assets/Scenes/StyleLab.unity`, the isolated test scene |

Generated files, to upload from the laptop afterwards (see `Docs/DevelopmentWorkflow.md`):
`Assets/Threefold/Generated/VisualStyle` (13 textures and 23 materials: thirteen presets, the
Mage's nine and the sky), the changed scenes,
and `Assets/Settings/*_RPAsset.asset` if you accepted the settings change.

**Existing assets are kept.** A texture repainted by hand, or a material tuned in the Inspector,
survives every later run. Delete an asset to have it generated again. One exception: a material
that was created with the stock shader, because the style's shader was unusable at the time, is
moved to the style's shader on the next run once that shader works. Its colour and texture stay.

### The Style Lab

The art bible asks for the first prototype to be "a small isolated scene" (section 10). The lab
holds what that section lists: the Mage as the test character, playable; paved ground, an arch
with columns and walls, a foliage cluster, mountains and haze; props in wood, cloth and metal;
two lighting conditions and the outline switch. One training construct stands on a plinth so the
usual objective works and there is something to cast at. The lab is not part of the Android
build.

One difference from the bible's list: there is no scripted camera orbit. The camera is the
game's own; orbit it by hand to check that lines and brushwork hold steady in motion.

## Materials

Principle from the art bible (section 5): "physically understandable materials, artistically
interpreted surfaces".

**Painted base colour maps are the foundation.** They are painted in code
(`PaintedTextureGenerator`): slow washes of slightly warmer and cooler colour, visible brush
strokes that follow the material's direction, and drawn lines for joints and seams. Vegetation is
built as masses of shade and light with leaves or blades painted over them, not scattered marks.
Every texture tiles without seams. The same code runs in Unity and in the preview tool, so the
previews show the real textures.

| Preset | Family | Texture | Replaces grey-box material |
| --- | --- | --- | --- |
| `Illustrated_Stone` | Stone | Coursed masonry, 512 | Stone |
| `Illustrated_StoneDark` | Stone | Coursed masonry in slate, 512 | StoneDark |
| `Illustrated_Paving` | Stone | Flagstones one metre across, 512 | Paving |
| `Illustrated_Wood` | Wood | Boards with grain and seams, 512 | (new) |
| `Illustrated_Bark` | Wood | Vertical ridges, 256 | Bark |
| `Illustrated_Cloth`, `Illustrated_ClothCool` | Cloth | Broad washes, faint weave, fold marks, 256 | (new) |
| `Illustrated_Metal` | Metal | Smooth planes, patina, fine scratches, 256 | (new) |
| `Illustrated_Foliage` | Vegetation | Leaf masses, 256 | Foliage |
| `Illustrated_Grass` | Vegetation | Blades in tufts, 256, two-sided | Grass |
| `Illustrated_Moss` | Vegetation | Soft blotches, 256 | Moss |
| `Illustrated_Ground` | Terrain | Turf seen from above, 256 | Ground |
| `Illustrated_Mountain` | Terrain | Plain colour, no texture | Mountain |

A texture covers 4 m, so 512 pixels give 128 pixels per metre.

**Readable differences between families** come from colour (a test requires the five base
colours to be clearly apart), from structure (blocks, boards, weave, leaves), and from light
response: everything is matte except metal, which has a small, soft highlight at 30% strength.
Nothing is glossy.

**Wood, cloth and metal** did not exist in the courtyard, which is built of stone and vegetation.
Applying the style adds a short row of props (a brazier, a banner stand with a warm and a cool
banner, two crates) against the front of the terrace, east of the stair, so all five families can
be judged in the level. Removing the style removes them.

**The Mage** keeps the colours of its existing materials and gains the soft light response and
the contour line. Its model has no texture coordinates for painted detail, so its surfaces are
flat colour. Painted character textures are asset work for later.

**Not styled:** the mana crystal, runes, spell effects, puzzle glows and the enemies keep their
existing materials. The enemies are built from simple shapes with hard edges, on which a contour
line would break up; they need smoothed normals first.

**Paint variation.** A tiled texture repeats every 4 m, which shows on large floors and hills. The
shader can drift value and warmth slowly across the world (one extra texture sample from a small
shared map) to hide this. It is on for environment materials and off for characters, where it
would slide across the surface as the character moves.

## Lighting

From the art bible (section 7): soft, directional light; controlled shadow transitions; ambient
fill that keeps painted colour visible; warm and cool contrast; distance haze that does not hide
gameplay.

| | Golden Afternoon (default) | Overcast Morning |
| --- | --- | --- |
| Sun | Warm (1.00, 0.93, 0.80), intensity 1.1, 38° above the horizon | Cool white (0.92, 0.95, 1.00), intensity 0.7, 58° |
| Shadow strength | 0.8 | 0.5 |
| Ambient sky, horizon, ground | Cool blue, neutral, warm brown | Pale grey-blue, grey, grey |
| Haze | Linear, from 26 m to 360 m | Linear, from 20 m to 280 m |

How each requirement is met:

- **Soft light, gentle transitions.** The shader replaces the usual hard terminator with a
  wrapped, eased ramp: light carries a little past the edge of a form and never forms a band.
  Shadow strength below 1 keeps cast shadows from going black.
- **Painted colour stays visible in shade.** Ambient light is strong relative to the sun. A test
  requires a wall in shade to be between 40% and 85% as bright on screen as a wall in light,
  under both presets.
- **Warm and cool.** Where the sun does not reach, ambient light is multiplied by a cool shade
  tint; the sun itself is warm.
- **Atmospheric depth.** Linear fog, starting beyond fighting distance. Thin lines in textures
  fade with distance through mip-mapping, and contour lines take the fog colour.
- **No new full-screen effects.** Colour comes from light and materials, not from grading. The
  existing bloom and tone mapping run only at the High quality level.

Lighting is real-time, as before. Baked lighting, which the art bible prefers for static
scenery, is not set up, and the style's shader does not read lightmaps yet.

## Linework

The art bible (section 6) names three families of line and says: "texture-authored lines should
be the dependable baseline", inverted-hull outlines "may work for selected character meshes", and
"full-screen edge detection is optional, not the default".

| Family | Implementation | Cost |
| --- | --- | --- |
| Outer contours | An inverted hull on the Mage only: the mesh drawn a second time, inside out and pushed outwards, in one colour | One extra draw of the Mage |
| Structural marks | Drawn into the textures: masonry joints, flagstone joints, board seams, bark ridges | None at runtime |
| Painterly edges | Brush strokes and tone steps between blocks and washes, in the textures | None at runtime |

- **Thin and understated.** The contour is 0.22% of the screen's height: about 2.4 pixels at
  1080p, and the same share of any screen. Its colour is a deep warm brown, not black. Texture
  lines use coloured darks too: blue-grey on stone, umber on wood and paving.
- **Stable in motion.** The width is set in screen space, so it does not thicken or thin as the
  camera moves. There is no depth or normal edge detection, which is what usually flickers.
  Anti-aliasing (2 samples at Medium) steadies thin lines further.
- **Minimal on distant things.** The contour fades between 12 m and 28 m and is gone beyond.
  Nothing in the environment is outlined. Texture lines soften with distance by themselves.
- **Close to the camera.** Hull pixels nearer than 0.9 m are not drawn. Without this, the inside
  of the hull would show as a dark shell when a wall presses the camera into the character.
- **Closed outlines on flat-shaded parts.** The Mage's clothing is flat-shaded, which would tear
  an inverted hull open along every edge. On import, smoothed normals are stored in the model's
  unused tangent channel, and the outline follows those. Unity deforms tangents with the
  skeleton, so this holds during animation.

No full-screen edge detection is used.

## Quality levels

Since LM-08 the three levels are the player's **graphics presets**, Low as **Performance**, Medium
as **Balanced** and High as **Quality**, chosen from the Graphics button in each level and kept
between launches. Each also sets a frame-rate target. See `Docs/MobilePerformance.md`.

| | Low (Performance) | Medium (Balanced) | High (Quality) |
| --- | --- | --- | --- |
| Intended for | Older or low-end phones | A mid-range Android phone | High-end phones, the Editor |
| Frame-rate target (LM-08) | 30 | 60 | 60 |
| Contour lines | On (off before LM-08) | On | On |
| Shadows | Crisp edges, 512 map, 22 m | Soft edges (4 samples), 1024 map, 35 m | Soft edges, 2048 map, 50 m |
| Render scale | 0.7 | 0.85 | 1.0 |
| Anti-aliasing | Off | 2 samples | 4 samples |
| Post-processing, HDR | Off | Off | On |

**Medium and Low switch post-processing and HDR off.** The earlier playground had bloom, tone
mapping and vignette on for every device. On a phone they are now off, so the mana crystal, the
runes and the puzzle glows lose their bloom, and colours are not tone-mapped. This follows the
brief ("avoid expensive screen-space effects unless justified by profiling") and is the look the
previews were judged in. To compare on a device, tick Post Processing and HDR for Medium on the
`VisualStyle` object and build again.

These are starting points chosen by reasoning about cost, **not measured**. The art bible asks
for quality tiers "only after actual device profiling identifies useful trade-offs" (section 9);
the brief for this sprint asked for presets now. Treat the numbers as provisional until a device
has been profiled.

At the start the controller takes a level chosen earlier in the run, then the player's saved
choice, and otherwise picks one from what the device reports: on a phone, Low if it
has less than about 3.5 GB of memory or an old graphics level, otherwise Medium; never High. In
the Editor and on desktop, High. This is a rough rule. Switch it off on the `VisualStyle` object
(Choose Quality For Device) to set the level by hand.

Levels and lighting presets are edited on the `VisualStyle` object in each scene.

## Project settings

"Do not overwrite existing project settings without checking them first" was a condition of this
sprint. How it is met:

- **Nothing is changed by applying the style in an open Editor.** The scene commands only report.
- **Quality levels do not edit project files.** While the game runs, the controller sets render
  scale, shadow distance, shadow map size, anti-aliasing and HDR on the active URP asset in
  memory, and puts the previous values back when it is disabled. Nothing is saved.
- **One setting cannot be set at runtime:** whether a URP asset allows soft shadows. In
  `Assets/Settings/Mobile_RPAsset.asset` it is off. **Check Rendering Settings** shows this and
  offers to switch it on; it changes nothing unless you choose "Change". Without it, shadows have
  crisp edges at every level, and everything else works.
- **On a build server only** (Unity running without a window, as in the Android build workflow),
  that one change is made without asking, because nothing there is kept.

The check also reports, without changing anything: colour space (Linear, correct), the SRP
Batcher, requested depth or opaque textures, renderers set to Deferred or Deferred+, which the
style's shader does not support, and depth priming, under which contour lines are not drawn
(it is disabled on both of the project's renderers).

Current values found in the repository: Linear colour space; `Mobile_RPAsset` (Android) uses a
Forward renderer without renderer features, render scale 0.8, 1024 shadow map, 50 m shadow
distance, HDR on, soft shadows off; `PC_RPAsset` (the Editor) uses Forward+ with ambient
occlusion.

## Performance

**Nothing below was measured.** These are costs read from the code. The art bible is explicit:
"An illustrated style is not automatically cheap", and "editor screenshots alone are not
acceptance evidence" (section 9).

| Feature | Cost, reasoned from the code | How to switch it for comparison |
| --- | --- | --- |
| The lit shader | One texture sample, plus one more with paint variation. No normal map, no reflection probe, no physically based lighting. Ambient light is computed per vertex. Expected to cost about as much as URP's Simple Lit and less than URP Lit, which the grey-box materials use | Remove the style |
| Contour line | The Mage's mesh is drawn twice: about 15,600 extra triangles and nine extra draws (one per material). Its pixels are a single flat colour. Switched off (F7), the nine draws remain but the hull is folded into a point and fills no pixels. Since LM-08 it stays on at every level, as part of the illustrated look | F7 |
| Shadows | One shadow map sample per pixel with crisp edges; four with soft edges | F6 |
| Render scale | The largest single lever: at 0.7, half the pixels of full resolution are drawn | F6 |
| Anti-aliasing | Usually cheap on phone graphics chips at 2 samples, but it adds memory | F6 |
| Post-processing | Full-screen passes; only at High | F6 |
| Textures | 13 textures, about 0.9 MB of graphics memory with ASTC 6x6 compression and mipmaps (four at 512, eight at 256, one at 128) | |
| Props | Six small objects, four of them with colliders | Remove the style |
| Test panel | Three small UI buttons, in development builds only | |

Further points:

- **No full-screen effect was added**, and no depth or opaque texture is requested.
- **Batching.** The shader is written to be compatible with the SRP Batcher (one material buffer
  shared by all passes). Whether Unity's Frame Debugger confirms this has not been checked.
- **Shader variants.** The lit pass declares 1,536 keyword combinations before stripping. URP
  removes combinations that the project's pipeline assets cannot reach when it builds (its
  stripper applies to every shader). How many remain, and what they cost in build time and
  memory, has not been measured.
- **Vegetation is opaque geometry**, as before: no alpha testing and no transparency.
- **Draw calls** are unchanged by the style, apart from the Mage's line and the props.

**What to measure on a device, in this order:**

1. Frame time in the playground at Low, Medium and High, standing at the start and on the terrace.
2. Medium with lines on and off (F7): the cost of the contour.
3. Medium against the grey-box look (Remove From Open Scene, build again): the cost of the shader
   and textures.
4. Soft against crisp shadows, once the project setting is on.
5. Ten minutes of play at Medium, for heat and throttling.
6. The Combat Arena's last court with all enemies fighting.

Target from the art bible: a reliable 30 frames per second on a representative mid-range device;
60 to be assessed afterwards.

## How it is built

| Layer | Files | Notes |
| --- | --- | --- |
| Shader | `Assets/Threefold/Shaders/IllustratedLit.shader` and four `.hlsl` files | For URP 17.6. Its structure follows URP's Simple Lit; only the lighting function and the outline pass are its own |
| Style rules, plain C# | `Scripts/Editor/VisualStyle/StylePalette`, `PaintedTextureGenerator`, `StyleMaterialPresets`, `IllustratedLightingReference`, `OutlineNormals` | No Unity engine calls: tested and previewed outside Unity |
| Asset generation | `Scripts/Editor/VisualStyle/VisualStyleAssets` | Falls back to URP's Simple Lit if Unity reports that the style's shader did not compile, so nothing turns pink |
| Scene tooling | `Scripts/Editor/VisualStyle/VisualStyleSetup`, `RenderingSettingsCheck`, `StyleProps`, `StyleLabSetup` | Menu commands |
| Runtime | `Scripts/Runtime/VisualStyle/VisualStyleController`, `UrpQualityBridge`, `VisualStylePresets`, `VisualStyleTestPanel` | Quality levels, lighting, switches |
| Import | `Scripts/Editor/Characters/CharacterModelPostprocessor` | Stores smoothed normals in tangents; the Mage is re-imported once |
| Checks | `Tools/ci/shader_check.py`, `Tools/visualstyle/` | Shader compile check and preview renderer |

`IllustratedLightingReference` repeats the shader's arithmetic in C#. The two must be changed
together; a test compares the constants and the property names in the shader source with the
code that uses them.

The runtime assembly now refers to the URP assemblies (for quality levels). No package was added.

## Verification

### Checked outside Unity

| What | How | Result |
| --- | --- | --- |
| C# compiles for the Editor and for Android | `Tools/ci/compile_check.sh`, against the real Unity 6000.6.0f1 assemblies | Passed |
| The shader's code is valid against the real URP 17.6 shader library | `Tools/ci/shader_check.py` compiles every pass with glslang's HLSL front end, for 54 combinations of pass, stage and keywords (shadows, soft shadows, three fog modes, Forward+, extra lights, instancing, each material option) | Passed. The checker was validated first: it compiles Unity's own Lit, Simple Lit and Unlit shaders without errors, and it caught three errors planted in different keyword branches |
| The style's rules | 22 tests that need no engine: textures are deterministic, seamless and on-palette; the light ramp has no band; shade is tinted and light is not; shade stays readable; the highlight is small; line width, fade and switch; smoothed normals; presets; shader source against C# | 22 passed. The seam test was shown to fail when wrap-around is broken |
| All tests | 292 in total | 203 passed, 0 failed, 89 need the Unity engine and did not run |
| Repository rules | `Tools/ci/check_unity_repo.py` | 0 errors |

### Checked by eye

The preview images above were looked at and the style was adjusted from them:

- the first vegetation textures were scattered marks on flat green and were rebuilt as masses;
- the first lighting left shade almost black and the sun too orange; ambient light was raised and
  the sun cooled;
- wood and red cloth were too close in colour and were separated.

This judges textures, palette, light response and line weight. It does **not** judge the game.

### Not verified: needs the Unity Editor

- **That Unity's own shader compiler accepts the shader.** glslang is a different compiler, and it
  was run with desktop precision. If Unity reports errors, materials are created with the stock
  shader instead and the setup report says so.
- Everything visual in the engine: the playground and arena with the style, shadows, fog, the
  props, the lab, how the textures sit on the level's meshes.
- The contour line on the real Mage: whether it stays closed during animation, how it looks on
  thin parts such as the cloak, and that the model re-imports with smoothed normals in its
  tangents.
- That URP draws the outline pass together with the lit pass, and that the SRP Batcher accepts
  the shader. Both follow from the URP source that was read, but neither was observed.
- The setup commands: generation of textures and materials, the material swap on prefab
  instances, Remove, the settings check, running everything twice.
- The 2 new tests that build scenes in batch mode.
- Interaction with Sprint 10's puzzle in the playground. Its stonework uses the shared stone
  materials and should be painted with the rest; its glows are left as they are.

### Reading the first Android build

The build workflow assembles both levels from code and applies the style while doing so. It is
the first time Unity compiles this shader. In the build log (`threefold-build.log` in the run's
log artifact) look for:

| Text in the log | Meaning |
| --- | --- |
| `The visual style is applied. It is a proposal` | The style's own shader was accepted when the materials were made |
| `New style materials use 'Universal Render Pipeline/Simple Lit'` | Unity rejected the style's shader. The build continues with the stock shader: painted textures, no soft light response, no contour lines |
| `The visual style could not be applied` | The style's setup failed. The level was still built, without the style or with part of it. The error follows in the log |
| `Shader error` together with `Illustrated Lit` (the exact wording is Unity's and may differ) | A variant failed to compile for the phone's graphics interface. Objects using it would be drawn in Unity's bright pink error colour on the device |

The check made when the materials are created cannot see errors that only arise for the phone's
graphics interface. Those appear later in the build, as the last row describes. The build server
normally runs without a graphics device; the setup does not rely on one.

### Not verified: needs an Android device

- Frame rate, memory and heat at each quality level.
- That the shader compiles and looks the same on the device's graphics interface (Vulkan or
  OpenGL ES 3) with mobile precision.
- Whether thin lines shimmer at phone resolution, with and without anti-aliasing.
- Whether the automatic choice of quality level suits real devices.
- Readability of the painted detail at phone size and gameplay distance.

## Relation to the art bible

| Art bible | This sprint |
| --- | --- |
| Section 5, materials | Presets for stone, wood, cloth, metal and foliage; painted base colour; matte, with a restrained highlight on metal only |
| Section 6, linework | Texture-authored lines as the baseline; inverted hull on one character; no edge detection |
| Section 7, colour and light | Palette from the listed families; soft, wrapped light; cool shade against a warm sun; haze |
| Section 9, rendering | A custom shader was written where the bible suggests starting with stock shaders: stock shaders cannot draw the line or soften the terminator. A stock fallback exists. No baked lighting yet |
| Section 9, item 7 | Quality tiers were requested now; the bible wants them after profiling. They are provisional |
| Section 10, prototype | The Style Lab is the isolated scene. The style was also applied to the playground and arena at the brief's request, as an explicit, reversible step |
| Section 12, approvals | Nothing was moved to "Confirmed". An implementation note was added under "Proposed" |

## Known limits

- Not run in Unity; not measured on a device.
- Environment meshes are low-polygon and flat-shaded. The style changes their materials and
  light, not their shapes; the art bible's "believable forms" will need modelled assets.
- Masonry joints are in the texture, so they do not line up with the edges of a block.
- A line of full width on a small, mid-distance figure reads heavier than on a near one. Only the
  Mage is outlined, about 5 m from the camera, so this does not arise yet.
- Characters have no painted textures. Enemies, effects and puzzle glows are not styled.
- The shader does not support lightmaps, normal maps, transparency, decals, light cookies,
  shadows from point and spot lights, ambient occlusion in the lit pass, or Deferred rendering.
  **Do not bake lighting with the style applied**: static scenery would probably lose its
  ambient light, because the shader has no lightmap variant.
- Contour lines are not drawn if depth priming is enabled on a renderer. It is disabled now, and
  the settings check warns if that changes.
- The contour's pass can only be enabled on a material from code, not in the Inspector.
- Quality levels change values on the URP asset in memory and put them back when Play mode ends.
  The code never saves them. One caution: if the URP asset itself is edited in the Inspector
  while the game is playing, Unity saves the whole asset, including the level's values at that
  moment. Edit the URP assets outside Play mode.
- No in-game menu for quality; the test panel is for development builds.

## Sources

- `Docs/ArtBible.md`, version 1.0.
- URP 17.6.0 and Core 17.6.0 source, as bundled with Unity 6000.6.0f1: `Shaders/SimpleLit.shader`
  and its includes (pass structure and keywords); `Runtime/Passes/DrawObjectsPass.cs` (the opaque
  pass draws `SRPDefaultUnlit` together with `UniversalForward`);
  `Runtime/Data/UniversalRenderPipelineAsset.cs` (which settings can be changed at runtime; "Low
  quality soft shadows. Recommended for mobile. 4 PCF sample filtering.");
  `Editor/ShaderScriptableStripper.cs` (variant stripping).
- ASTC 6x6 stores a 6 by 6 pixel block in 128 bits, about 3.56 bits per pixel; the memory figure
  follows from that and one third extra for mipmaps.
