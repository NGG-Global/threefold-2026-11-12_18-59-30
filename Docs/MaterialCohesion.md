# Material cohesion and lighting

A pass over the materials and the lighting of the Reference Courtyard
(`Docs/VisualReferencePrototype.md`), within the approved direction of `Docs/ArtBible.md`, which is not
changed. The aim was a more coherent, painterly scene at no significant extra cost on the GPU.

**Status (16 November 2026).** Implemented, tested in Unity on the build runner, and rendered by Unity
before and after from the same viewpoints. **The change is modest and the scene is still built of
primitive forms; it does not match the owner's reference picture.** Nothing has been seen or measured on a
device. The look remains a proposal until the owner has reviewed it in the game and on a device.

The materials and the lighting preset are shared by every scene of the game, so the Fantasy Playground,
the Combat Arena, the Broken Foundry, the Style Lab and the main menu take the same changes the next time
they are assembled. Only the courtyard was rendered and looked at.

## What changed

### Shader: weathering on faces that look up

`Threefold/Illustrated Lit` is refined, not replaced. One thing was added: on faces that look up (tops of
walls, ledges, steps, paving), in soft patches, the surface's colour is multiplied by a tint
(`_TopTint`), as moss and damp stain stone where rain lies. Walls' faces take none.

- It reuses the texture sample that paint variation already makes and lives under the same keyword
  (`_PAINT_VARIATION`): **no new texture, no new sample, no new shader variant.**
- A strength of 0 leaves the colour as it was, so materials that do not ask for it are unchanged.
- The tint multiplies, so the painted joints and lines of the base map stay visible.
- The arithmetic is mirrored in `IllustratedLightingReference` (`TopTintAmount`, `ApplyTopTint`) and tested.

### Materials (`StyleMaterialPresets`, revision 2)

| Material | Before | After | Why |
| --- | --- | --- | --- |
| Stone | paint variation 0.12, no weathering | 0.20, weathering 0.5 (moss tint) | Walls were one flat beige; tops now carry soft moss |
| Dark stone | 0.12, none | 0.18, weathering 0.4 | The same, a little less |
| Paving | 0.12, none | 0.17, weathering 0.35 (grime tint) | A walked-on floor is grimed, not mossed |
| Moss | untinted | tint (0.84, 0.86, 0.80), softness 0.55 | Duller and nearer the stone it lies on |
| Foliage | untinted | tint (0.92, 0.96, 0.92) | A little deeper |
| Cloth, cool cloth | untinted, softness 0.5 | tints (0.93, 0.90, 0.88) and (0.90, 0.92, 0.94), softness 0.55 | Less saturated beside the stone, softer light |
| Wood | paint variation 0.08 | 0.12 | Planks vary a little more |
| Shade tints | cool (0.78, 0.85, 1.00), green (0.80, 0.92, 0.88) | (0.72, 0.82, 1.00), (0.78, 0.92, 0.86) | Cooler shade against the warmer sun |

No texture was regenerated or added, no material was added, and only metal has a highlight, as before.

### Lighting: Golden Afternoon (`LightingPreset`, revision 2)

Every changed value is recorded here. Nothing else about lighting or rendering was changed.

| Setting | Before | After |
| --- | --- | --- |
| Sun colour | (1.00, 0.93, 0.80) | (1.00, 0.90, 0.74) |
| Sun intensity | 1.10 | 1.15 |
| Ambient light from the sky | (0.60, 0.70, 0.88) | (0.55, 0.68, 0.93) |
| Ambient light from the ground | (0.44, 0.39, 0.32) | (0.50, 0.42, 0.31) |
| Haze colour | (0.66, 0.72, 0.78) | (0.69, 0.76, 0.82) |
| Sky colour below the horizon | (0.42, 0.46, 0.47), fixed when the sky material was made | (0.63, 0.70, 0.76), now part of the preset |
| Sky air thickness | 1.2, fixed when the sky material was made | 1.0, now part of the preset |
| Haze start and end, sun direction and height, shadow strength, ambient horizon colour, sky tint and exposure | | unchanged |

Overcast Morning keeps its values; it gained the two new sky fields with values that leave its look as it
was intended (below-horizon colour near its haze, air 1.2).

**Not changed:** project settings, the render-pipeline assets, quality levels, graphics presets, shadow
settings, post-processing. No full-screen effect was added. Contour lines are still drawn on characters
only; the environment carries its lines in its painted textures.

### Reaching what was generated before

Generated materials and assembled scenes are kept between runs, and both keep their values. A change of a
preset would therefore have reached only computers that had never generated the style. Both now carry a
revision:

- A preset material made under an older revision takes the presets' present tint, light response, paint
  variation and weathering, once. Its texture and everything else stay. A value somebody tuned by hand on
  such a material is replaced when the revision is raised.
- A scene's style controller takes the present values of the two provided lighting presets, once. Presets
  with other names are kept.

Nothing is deleted, and no texture is overwritten.

## Before and after

Rendered by Unity on the runner from identical viewpoints (`ReferenceCourtyardLayout.Viewpoints`), run 23
(before, commit `f65ffae`) and run 24 (after, commit `b085042`). They are Editor renders: Direct3D 11,
Intel integrated graphics, quality level `PC`, nothing animated, no HUD. They are not the game on a device.

| Before (run 23) | After (run 24) |
| --- | --- |
| ![](MaterialCohesion/images/before-run23-1-start.png) | ![](MaterialCohesion/images/after-run24-1-start.png) |
| ![](MaterialCohesion/images/before-run23-2-arch.png) | ![](MaterialCohesion/images/after-run24-2-arch.png) |
| ![](MaterialCohesion/images/before-run23-3-castle.png) | ![](MaterialCohesion/images/after-run24-3-castle.png) |

### What the pictures show

- **Atmosphere.** The yellow band at the horizon is gone; the sky is blue down to the land, and the far
  ranges and the castle now sit in the haze instead of standing against a differently coloured sky. This
  is the clearest gain.
- **Paving and steps.** Soft darker, greener patches lie on the floor and on the treads; the flat floor
  reads less uniform.
- **Stone.** Colour drifts a little more across the walls.
- **Warm and cool.** The shift is slight in these pictures.
- **Unchanged, and still the largest gap to the reference.** The forms: boxes, prisms and cones; trees as
  stacked cones; no clouds, water or sculpted detail; the grass texture's chequered repeat on large areas;
  the Mage in the model's rest pose.

### Rendering metrics

`build/renders/metrics.txt` of both runs is kept beside this document
(`MaterialCohesion/metrics-before-run23.txt`, `metrics-after-run24.txt`).

| Measure | Before | After |
| --- | --- | --- |
| Materials in the scene | 20 | 20 |
| Shader variants the materials ask for | 8 | 8 |
| Textures in the scene | 14 | 14 |
| Texture memory as the Editor holds it | 11.2 MB | 11.2 MB |
| Renderers / that cast shadows | 102 / 68 | 102 / 68 |
| Triangles in the scene (with the Mage, constructs and props) | 39,782 | 39,782 |
| Lights, full-screen effects added | 1, none | 1, none |
| Fragment stage, variants with paint variation (instructions after `spirv-opt -O`) | 180 and 194 | 197 and 211 |
| Fragment stage, the other compiled variants; every vertex stage | | unchanged (52 of 54 stages) |
| Texture samples in any stage | | unchanged |
| Render call per view, CPU, Editor (ms): start, arch, castle, wall, Mage, overview | 9.6, 7.1, 4.5, 5.7, 4.7, 5.6 | 10.2, 6.2, 4.6, 5.2, 5.1, 3.9 |

How to read them:

- The counts are exact and did not move: the pass added no material, texture or variant.
- The shader's fragment stage is about 9% longer (17 instructions) where paint variation is on, with no
  extra texture sample or branch. The count is a proxy made with glslang and `spirv-opt`, a different
  compiler from Unity's. It supports "no significant increase"; it does not prove a frame time.
- The render-call times are CPU time on the runner's laptop, they vary by a millisecond or two between
  runs in both directions, and they are not GPU time. They show no cost that stands out; they prove
  nothing about a phone.
- Unity's own per-frame counters (draw calls, set-pass calls) report nothing in batch mode, so they are
  absent. The Mage's painted atlas is 9.6 MB of the 11.2 MB in the Editor; on Android textures are
  compressed and far smaller, which this figure does not show.

## Asked for and not done, with reasons

| Asked for | State | Reason |
| --- | --- | --- |
| Readable edges, cracks and weathering | Weathering only | Edges and cracks belong in the painted textures. Generated textures are kept and may have been repainted by hand, so changing them means new texture files and a look at each; that is a step of its own |
| Prefer baked lighting for static objects | Not done | The shader has no lightmap path by design (`IllustratedForwardPass.hlsl`). Adding one means lightmap coordinates for every generated mesh, new shader variants, and a bake on the runner. The scene has one light with real-time shadows and per-vertex ambient light, which is already cheap. Worth doing only if device measurements show shadows are the cost |
| Selective linework | Unchanged | Already selective: contour lines on characters only, drawn lines in the environment's textures. An inverted-hull line on the flat-shaded stone modules would break at every hard edge |
| Reuse materials and atlases | Unchanged | The scene already shares 20 materials across 102 renderers, and the count did not grow |
| Measure rendering cost | Partly | Counts, shader size and Editor CPU times, as above. GPU time, overdraw and a phone's frame rate need a device |

## Not verified

- Anything on a device: appearance, frame rate, memory, heat.
- That Unity's shader compiler produces code of the size the proxy suggests.
- The other scenes with the new values: they were assembled and validated by the tests, not looked at.
- The scene in motion, and whether the weathering patches shimmer or swim as the camera moves. They are
  placed in world space, so they should stay on the stone; this has not been watched.
- The art bible's acceptance test (section 10).
