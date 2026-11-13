# THREEFOLD — VISUAL ART BIBLE

**Version:** 1.0 — Foundational direction  
**Updated:** 2026-11-13  
**Status:** Approved visual direction; implementation details and world-specific designs remain provisional  
**Purpose:** Shared reference for art, 3D modeling, materials, animation, rendering, UI, and AI-assisted asset production.

---

## 1. Creative North Star

**Threefold is a classical high-fantasy 3D Android game rendered as a living illustration.** Its world should be adventurous, inviting, and credible. Characters, buildings, landscapes, and objects have believable physical form, but their visual finish resembles carefully painted and lightly inked fantasy artwork.

**Approved art direction: Soft Illustrated Realism with visible linework.**

The intended result is *not* a still watercolor picture mapped onto a game. It is a cohesive, fully navigable 3D world whose forms, materials, light, and selective linework retain the character of an illustration as the camera and objects move.

### Core pillars

1. **Believable forms:** Human anatomy, clothing construction, architecture, and environmental scale should feel grounded even where fantasy exaggeration is used.
2. **Painterly finish:** Visible, intentional painted color variation and surface strokes replace photographic noise and hyper-detailed materials.
3. **Delicate drawn definition:** Thin, selective contours and hand-authored interior marks reinforce form without turning every object into a comic-book cutout.
4. **Cinematic adventure:** Clear focal points, atmospheric depth, warm/cool color relationships, and a sense of exploration and wonder.
5. **Timeless, mobile-conscious design:** Art direction depends on strong shape and color design rather than expensive rendering effects or fashionable technical tricks.

### What it must not become

- Photorealistic fantasy with noisy scanned textures, glossy surfaces, or extreme microdetail.
- Broadly cartoonish or toy-like fantasy with oversized heads, rubbery anatomy, and plastic materials.
- Flat cel-shaded comic art with uniformly thick black outlines and hard two-tone shadows.
- A screen-wide watercolor/paper filter that obscures readability or breaks during camera movement.
- Generic high-fantasy spectacle that sacrifices a consistent visual language for visual clutter.

---

## 2. Style Hierarchy and Readability

The player should perceive **silhouette → major color shapes → lighting and volume → selected illustrated details** in that order.

- **Primary playable characters:** Most defined shapes and linework; distinct silhouettes, readable equipment, expressive faces.
- **Important creatures and interactable props:** Clear shape language and selective accents that support recognition.
- **Architecture and nearby environment:** Convincing mass and readable materials, with softer, less frequent linework.
- **Distant landscape:** Atmospheric simplification, larger value/color groups, minimal to no outlines.

Do not apply equal detail density to every object. Visual hierarchy is both an artistic and performance requirement.

---

## 3. Characters

### Proportions and anatomy

- Use grounded, broadly naturalistic anatomy; stylization should clarify personality and silhouette rather than caricature the body.
- Hands, faces, hair masses, and clothing shapes may be gently emphasized for readability at mobile viewing distances.
- Avoid exaggerated giant weapons or armor unless supported by character identity and world lore.
- Ensure characters read in both close-up dialogue/cinematic framing and typical gameplay camera distances.

### Faces and expression

- Model coherent facial planes and expressive brows, eyes, mouth, and cheek shapes.
- Use painted value shifts to describe facial volume; avoid photo-skin pores and overly glossy skin.
- Fine facial marks should be authored sparingly and remain stable during animation.
- Expressions should feel sincere and readable, not exaggerated slapstick.

### Hair, clothing, and equipment

- Hair: grouped, sculpted locks and painted strand accents; avoid costly individual strand rendering as the default.
- Clothing: recognizable weight, seams, layers, folds, and wear, simplified into major shape families.
- Armor: functional construction and distinctive silhouettes; restrained metallic response rather than mirror-like PBR.
- Weapons: believable grip and material construction; prioritize silhouette and usability over ornament density.

### Character linework

- Prioritize silhouette contour around the head, shoulders, hands, and distinctive gear.
- Paint selected inner lines into textures for seams, folds, facial accents, engravings, and edges.
- Keep line width and contrast subtle and consistent in screen space where feasible.
- Avoid uniformly outlining every polygon, wrinkle, or surface discontinuity.

### Character concept deliverables

For each approved major character: front / side / back views; close-up face and expressions; silhouette test; material and palette callouts; equipment breakdown; gameplay-distance preview; and a 3D feasibility note.

**Not yet decided:** playable cast, races/species, costume cultures, faction motifs, signature weapons, and character-specific palettes. Previously generated adventurers are exploratory references, **not canon characters**.

---

## 4. Environments and Worldbuilding

### Overall world character

Classical high fantasy balanced between wonder and grounded realism. Landscapes may be majestic, but geography, settlement placement, and material construction should remain intelligible.

### Terrain and nature

- Build readable terrain with strong near/mid/far depth and believable geological masses.
- Use foliage clusters and designed tree silhouettes, not indiscriminate density.
- Simplify individual leaf and grass detail; emphasize vegetation rhythm, color grouping, and wind response.
- Let distant terrain lose contrast and line definition through atmospheric perspective.
- Use rivers, cliffs, roads, and landmarks to orient the player visually.

### Architecture

- Favor coherent construction: plausible supports, wall thicknesses, roof logic, entrances, and routes.
- Stylize through proportion, silhouette, hand-painted surface treatment, and selective ornamentation.
- Reserve intricate detailing for important landmarks and areas the player approaches closely.
- Buildings should look designed for inhabitants and history, not assembled as decorative fantasy facades.

### Environmental storytelling

Weathering, repair, worn pathways, planted spaces, and practical objects should imply habitation and age. Use detail purposefully: each element should suggest a function, story, or navigational cue.

**Not yet decided:** geographic map, biome list, architecture families, settlement names, factions, or specific landmark designs. Castles, dragons, waterfalls, and mountain cities from early concept images are possibilities, **not required features**.

---

## 5. Materials and Texture Language

**Principle:** Physically understandable materials, artistically interpreted surfaces.

| Material | Desired treatment | Avoid |
|---|---|---|
| Skin | Soft painted gradients and controlled warmth | Photographic pores, waxy shine |
| Cloth | Broad pigment variation, selective woven/fold accents | Noisy fabric scans |
| Leather | Matte-to-satin, painted wear at functional edges | Uniform brown plastic |
| Metal | Restrained highlights and authored edge accents | Chrome reflections and extreme sparkle |
| Stone | Large color planes, hand-painted chips and seams | Dense photogrammetry noise |
| Wood | Directional grain accents and readable plank shapes | Repetitive high-frequency grain |
| Foliage | Designed color masses and selective painted leaves | Transparent overdraw-heavy leaf clouds |
| Water | Simplified color, reflections and motion cues | Expensive fully realistic water by default |

### Texture rules

- Favor hand-authored or hand-painted-looking albedo/base-color textures.
- Place strokes to reinforce geometry, not as random noise pasted across every surface.
- Use modest roughness/normal variation only where it supports the material.
- Maintain a consistent brushstroke scale across characters and props.
- Keep key marks legible at actual Android display sizes and gameplay camera distances.

---

## 6. Linework Language

**Linework is a signature of the style, but must remain secondary to form and lighting.**

### Three line families

1. **Outer contours:** Fine, slightly softened dark strokes on key characters and selected foreground objects.
2. **Structural marks:** Authored internal lines for clothing seams, folds, facial accents, architecture joints, and gear.
3. **Painterly edges:** Irregular value/color boundaries that suggest brushwork without requiring literal black lines.

### Rules

- Use colored darks (deep warm brown, blue-gray, muted plum) rather than universal pure black.
- Vary line emphasis by object importance, distance, and lighting.
- Avoid outlines on distant landscape silhouettes where atmospheric separation is sufficient.
- Test outlines in motion to prevent shimmering, broken seams, inconsistent thickness, and ugly overlap.
- Prefer texture-authored interior lines to computationally outlining every internal edge.

**Implementation remains provisional:** Inverted-hull outlines may work for selected character meshes; texture-authored lines should be the dependable baseline. Full-screen edge detection is optional, not the default.

---

## 7. Color, Value, and Lighting

### Color philosophy

- Rich but controlled fantasy color; avoid constant maximum saturation.
- Ground major surfaces in natural earth and mineral hues, with clearer accent colors for characters, magic, faction identity, and points of interest.
- Use warm/cool contrast to guide attention and reinforce mood.
- Preserve value separation among characters, enemies, interactables, and backgrounds.

**Provisional palette families (not final swatches):** warm parchment/ivory, weathered terracotta, moss/olive green, slate blue, desaturated teal, dark umber, and selective amber/gold accents.

### Lighting philosophy

- Soft, directional light that describes shape clearly.
- Controlled shadow transitions; avoid hard binary cartoon bands.
- Use subtle ambient fill to keep painted surface colors visible.
- Reserve dramatic light shafts, glows, and strong contrast for authored moments.
- Prefer baked/static lighting for suitable environment elements and carefully budgeted real-time lighting for moving subjects.

### Atmosphere

Use distance haze, simplified cloud forms, time-of-day color shifts, and composed light direction to create depth. Atmospheric effects must not obscure gameplay-relevant silhouettes.

---

## 8. Animation and Movement

- Ground motion in believable weight, momentum, and contact.
- Allow modest stylized timing and pose clarity so actions read on mobile.
- Maintain coherent clothing and equipment motion without requiring expensive simulation.
- Facial animation should prioritize meaningful expressions and stable illustrated features.
- Ensure linework and painted texture marks do not crawl or flicker during movement.
- Design effects (magic, impacts, dust) as restrained painterly shapes rather than noisy photorealistic particles.

**Not yet decided:** camera system, combat style, movement mechanics, and animation sets. Art must adapt to confirmed gameplay requirements rather than assume them.

---

## 9. Unity / Android Rendering Guidance

### Technical baseline

- **Engine:** Unity 6 (repository version should be verified before modifying configuration).
- **Platform:** Android.
- **Rendering direction:** Universal Render Pipeline (URP), subject to checking actual project setup.
- **Target:** Establish a reliable 30 FPS baseline on a representative mid-range Android device; assess 60 FPS mode after profiling.

### Recommended rendering architecture (proposal, not yet implemented)

1. Painted base-color materials as the visual foundation.
2. Soft, controllable light response, initially using stock URP-compatible shaders/material settings where possible.
3. Selective linework using painted textures; evaluate lightweight mesh/inverted-hull outlines only on appropriate characters.
4. Baked lighting for static environments where practical; limited dynamic lights and shadow casters.
5. Conservative transparent foliage, particles, full-screen passes, and post-processing.
6. Shared material presets, texture compression, mipmaps, LODs, batching/instancing where applicable.
7. Quality tiers only after actual device profiling identifies useful trade-offs.

### Performance cautions

- An illustrated style is **not automatically cheap**. Outlines can add draw calls and geometry passes; transparent foliage can increase overdraw; large painted textures still consume memory.
- Avoid fixing polygon, draw-call, or texture budgets without knowing camera framing, device class, and scene complexity.
- Validate in motion and on physical Android hardware; editor screenshots alone are not acceptance evidence.
- Any custom shader must be checked for compatibility with the actual URP version and Android graphics APIs in use.

---

## 10. Prototype Acceptance Test

The first visual prototype should be **a small isolated scene**, not a full environment or gameplay overhaul.

### Contents

- One believable humanoid test character with painted clothing, skin, hair, and one equipment piece.
- A small test setting: ground, stone structure, foliage cluster, and simple background depth.
- At least two lighting conditions, or an easily switchable lighting comparison.
- A slow camera orbit and a simple character movement/animation test.
- Optional outline toggle for direct A/B comparison.

### Evaluation questions

- Does it look like a **3D illustration**, not a flat painting or generic cartoon?
- Do realistic proportions and soft material transitions remain readable?
- Are lines visible but not dominant?
- Do strokes and contours remain stable as the camera moves?
- Do the character and environment clearly belong to the same visual world?
- Does it perform acceptably on the target Android test device?

**Prototype passes only after visual review and on-device performance verification.**

---

## 11. Asset Production and AI Collaboration

### Concept-to-game pipeline

1. Define the asset's gameplay role, camera distance, and narrative identity.
2. Create concept art with consistent character, palette, material, and linework guidance.
3. Approve silhouette and material language before detailed modeling.
4. Build or acquire the 3D model; unwrap and paint appropriate textures.
5. Apply shared illustrated material/shader presets.
6. Test in a representative Unity scene at gameplay distance and under different lighting.
7. Profile on Android, revise, and document final settings.

### Instructions for AI assistants and collaborators

- Treat this document as the art-direction baseline; do not treat generated reference images as final game assets.
- Do not invent lore, canon characters, factions, or game mechanics from visual examples.
- Distinguish **approved** decisions from **proposed** techniques.
- Favor free tools and workflows first, and explain any paid dependency.
- Work tablet-first through GitHub and AI coding agents where possible.
- Keep changes isolated and reviewable; never silently replace existing game rendering or art assets.
- Give the project owner **one small actionable step at a time** and wait for confirmation before advancing.
- Before making code changes, inspect the current repository state, Unity version, URP configuration, and active branches/PRs.
- Deliver testable results, comparisons, and concise explanations rather than claiming an aesthetic is achieved without review.

---

## 12. Approval Register

### Confirmed

- Project: Threefold, a 3D Android fantasy game built in Unity.
- Genre/world tone: classical high fantasy; adventurous; wonder balanced with grounded realism.
- Primary art direction: **Soft Illustrated Realism**.
- Realistic or believable proportions rather than overt cartoon stylization.
- Hand-painted/painterly surface appearance.
- **Visible but delicate and selective linework**.
- Timeless visual identity and mobile performance are priorities.
- Tablet-first, AI-assisted, stepwise development workflow.

### Proposed, pending validation

- Specific linework shaders and outline implementation.
- Detailed material shader behavior and lighting profile.
- Palette swatches and faction-specific color systems.
- Exact polycounts, texture sizes, quality tiers, and device performance budgets.
- Character designs, races, cultures, locations, creatures, and architecture families.
- Camera and gameplay-distance visual rules.

### Change control

When a visual decision is approved, record the date, decision, reason, and affected asset classes here. Update this Bible before propagating major stylistic changes to the project.

---

## 13. One-Sentence Art Prompt for Future Briefs

> Create a believable 3D classical high-fantasy world in **soft illustrated realism**: naturalistic anatomy and construction, carefully hand-painted surfaces, restrained cinematic lighting, subtle colored ink contours and selective internal linework, clear silhouettes, atmospheric depth, and a cohesive timeless look suitable for a performant Android game. Avoid photorealism, toy-like cartoon proportions, heavy comic outlines, and visual noise.
