# Magical combat VFX (Sprint 3)

Sprint 3 integrates the installed VFX packs into the three disciplines and times every effect on the
Mage's body to the moments of each attack, as the combat animation cues report them. It builds on the
audit and the first integration (`Docs/MagicVFX_Audit.md`, pull request #26) and on the combat animation
cues (`Docs/CombatAnimationArchitecture.md`).

**Status:** written; the rules are tested outside Unity (38 tests); everything compiles against the Unity
6000.6.0f1 assemblies for the Editor and with Android player defines. **Nothing here has been seen in
Unity, played, or measured on a device.** The effects have not been generated yet (that is a laptop
step), so their look, scale and timing on screen are unverified, and every colour and size is a
proposal against the Art Bible, not approved art.

## What the player sees

| | Arcane: precise, flowing | Flame: aggressive, directional | Aegis: compressed force, stability |
| --- | --- | --- | --- |
| **Strike, before contact** | A thin ribbon drawn by the striking hand from the strike's effect moment; a ribbon swept round the body along the strike's arc | Fire on the fist from the start of the wind-up, emitted by distance travelled (it follows the punch's acceleration) and growing to full strength at contact | Force gathering into the striking palm or forearm through the wind-up; a short streak in the fast part only |
| **Contact (on a hit)** | A small concentrated flash and a narrow cone of motes at the hand, along the blow; the adapted Free Slash impact on what was struck | A flash and a wide cone of flame at the fist; the adapted Hovl sparks on what was struck | A pressure ripple at the palm and on what was struck; on the heavy slam (hit or miss) a ripple flat on the ground with a ring of dust |
| **After contact** | The ribbon runs on through the follow-through and fades | Embers left where the fist was, which drift up and go out | Nothing lingers |
| **Charge** | Motes drawn into the casting hand from a shell that shrinks as the charge grows | Heat along both forearms, denser and tighter as the charge grows | Energy pressed inwards in front of the chest, the shell shrinking as the charge grows |
| **Release** | A burst at the casting hand, along the aim; a delicate ribbon follows the Arcane Arrow and fades where it ends; the adapted Free Slash impact where it strikes | A burst at the hand; a front of rising flame travels out across the Flame Wave's area; a flame burst on each target | A burst between the hands; a flat pressure ring travels out across the Force Pulse's area; the adapted Hovl ground burst on each target |
| **Defence** | (the shared guard arc) | (the shared guard arc) | A restrained pane of soft motes in front of the Mage while blocking; a flash with a ring and sparks where a projectile is turned |
| **The world** | Charge streams from where it struck into the object's centre and gathers in from a shell round it | Fire licks the object where the heat struck it | A pressure streak along the push; a brace closes in round a stabilised object |

What an object becomes afterwards (activated, burning, collapsed, braced) is still shown by its own state
visuals from its definition (`PropertyStateVisuals`), unchanged.

## How it works

```
DisciplineCombat ── swings, charges ──► CombatAnimationDirector ── cues ──┐
        │                                                                  ▼
        │ (no active director: the same moments from the events) ──► BodyEffectSequencer (plain C#)
        │                                                                  │ commands
        ▼                                                                  ▼
DisciplineFeedback (ground arcs, impacts on targets,           DisciplineBodyEffects (pooled effects on
 status, guard spark, camera)                                   hand / forearm / foot / chest sockets)
MagicalObject.AnyEffectReceived ──► WorldReactionVfx (plain C#) ──► WorldMagicFeedback
```

- **Timing.** `BodyEffectSequencer` reads the cues (wind-up, effect, hit window, contact, release,
  recovery, cancellation) and answers with commands: start, grow, stop, burst. The cues follow the
  rules' clock, so a contact burst appears on the frame the rules resolve the hit, and the hand trail is
  drawn during the part of the clip that the animation shows as the strike. When no director is active
  (no animation set), the same moments are taken from the combat events: the effect moment and hit
  window at the start of the swing, the contact when it lands.
- **Data per discipline.** `DisciplinePresentation` (in each discipline asset) holds the effects and a
  `BodyEffectTiming`: when the trail starts (wind-up, effect moment or hit window), whether it runs
  through the follow-through, how strong it starts, whether force gathers before the strike, whether
  embers linger, the heavy-strike threshold, where the charge sits and how it concentrates, and how long
  effects drain. Starting values: `MagicVfxDefaults`. Nothing in the code is written per discipline.
- **Attachment.** `DisciplineVfxSockets` resolves hands, forearms, feet and chest from the Humanoid bones
  (fallback points without bones). Each strike's hand or foot comes from its animation slot, through
  the cue. Trails and charges are attached to bones and draw in world space; contact bursts, embers,
  waves and arrivals are spawned in the world.
- **Pooling.** Every effect is a `PooledObject` spawned through `Pools`. Instances carry a generation
  number, so an effect recycled by its pool's cap is never stopped by its previous owner, and an instance
  recycled while attached to a bone is moved back under its pool first.
- **Cancellation.** A cancelled strike or charge stops every effect it started, its swing ribbon or slash
  too, with a short drain (0.08 s), and shows no contact, embers or release. Scripted emitters stop
  emitting the moment their effect is stopped.
- **Hits and misses.** The contact flash at the limb and Flame's embers follow a strike that hit something
  (the rules' hit count). A miss shows the trail and nothing at contact. The Aegis slam's ground ring shows
  either way, because the slam strikes the ground. The sequencer's tests prove that every effect that
  starts is stopped exactly once, in 15,000 frames of random cues, including late cues from older actions.
- **No damage from effects.** Effects read the rules and never call them: none of the new effect classes
  refers to `Health`, `AreaStrike`, a cast or `ReceiveEffect`. The contact and release cues arrive after
  the rules resolved the hit; the Flame Wave front and Force Pulse ring travel over an area whose hit was
  resolved at release. The only additions on the gameplay side are two events (`Projectile.Launched`,
  `MagicalObject.AnyEffectReceived`), raised after the rules have acted (a projectile only once it is in
  flight; an object once its reactions and save are done). A listener that throws is logged and cannot
  stop the rules.
- **Subscriptions.** `DisciplineBodyEffects` subscribes once, guarded by a flag, and only while the game
  runs: the setup command binding it in edit mode leaves nothing subscribed (the project enters Play mode
  without a domain or scene reload).
- **A swing per action.** Each action keeps the swing it began with, so a long frame that starts the next
  swing before this one's contact is shown cannot turn its contact burst.
- **Duplicates.** When `DisciplineBodyEffects` shows a combat, `DisciplineFeedback` leaves the swing
  effect, the charge and the release to it, so none is shown twice.
- **Scripted emitters.** The Arcane sweep ribbon, the wave fronts and the inflows emit their particles by
  script into a world-space particle system (`ArcEmitterEffect`, `InflowEffect`), with positions from
  `MagicVfxGeometry`. The arc is exact, does not depend on a shape module's orientation, and emits the
  same number of particles per second at 15 or 120 frames per second (tested).

The aerial abilities of Sprint 4 add their own effects through the same system (`AerialAbilityEffects`;
see `Docs/AerialAbilities.md`).

## Quality levels and intensity

| | Performance | Balanced, Quality |
| --- | --- | --- |
| Particles per burst | 55% | as authored |
| Trail length | 60% | as authored |
| Scripted emitters (waves, ribbons, inflows) | half the rate | as authored |
| Secondary effects (gathering force, embers, the shell of an inflow) | off | on |

The silhouette of each discipline (trail, contact, charge, release, wave) is the same at every level.
`DisciplineBodyEffects.Intensity` and `WorldMagicFeedback.Intensity` (0.25 to 1, in the Inspector)
reduce everything further, down to a floor at which contacts, releases and waves still show. There is
no menu for it yet. The levels are proposals: **no performance improvement is claimed**, because
nothing has been measured on a device.

## Asset mapping

Facts about the packs (systems, particle limits, lights, collisions, shaders, HDR colours, texture
sizes) are in `Docs/MagicVfx/PackInventory.md`, generated by `Tools/vfx/analyse_vfx_packs.py` from the
prefab and material YAML. Run it again after changing a pack.

Procedural effects are in `Assets/Threefold/Generated/VFX/Body/`, built by `MagicVfxPrefabs` on the shared
SoftGlow material with URP Particles/Unlit. Pack adaptations are in `Assets/Threefold/Generated/VFX/Packs/`,
built by `MagicVfxPackAdapter`.

| Slot | Arcane | Flame | Aegis |
| --- | --- | --- | --- |
| Limb trail | `ArcaneLimbTrail` (procedural: trail renderer, motes) | `FlameLimbTrail` (procedural: trail, flames by distance) | `AegisLimbTrail` (procedural: trail) |
| Swing effect | `ArcaneSweepRibbon` (procedural ribbon, scripted arc) | `FlameSlash_FreeSlash` (Free Slash `Slash Fire VFX`, adapted) | none (Aegis has no slash) |
| Gather | - | - | `AegisGather` (procedural) |
| Contact | `ArcaneContact` (procedural) | `FlameContact` (procedural) | `AegisRipple` (procedural) |
| Heavy contact | - | - | `AegisHeavyRing` (procedural) |
| Linger | - | `FlameLinger` (procedural embers) | - |
| Impact on targets | `ArcaneImpact_FreeSlash` (Free Slash `Impact`, adapted) | `FlameSparks_Hovl` (Hovl `Sparks explode red`, adapted) | `AegisRipple` |
| Charge | `ArcaneChargeGather` (procedural) | `FlameChargeHeat` (procedural) | `AegisChargeCompress` (procedural) |
| Release | unchanged (`ArcaneReleaseBurst`) | unchanged (`FlameReleaseBurst`) | unchanged (`AegisReleaseBurst`) |
| Projectile ribbon / impact | `ArcaneArrowRibbon` / `ArcaneImpact_FreeSlash` | - | - |
| Area wave / area impact | - | `FlameWaveFront` / `WorldHeatArrival` | `AegisPulseRing` / `ForcePulseImpact_Hovl` (Hovl `Ground AOE explosion`, adapted) |
| Block barrier / deflection | - / shared `DeflectSpark` | - / shared `DeflectSpark` | `AegisBarrier` / `AegisDeflectFlash` |
| World arrivals (shared) | `WorldChargeInflow` | `WorldHeatArrival` | `WorldForceArrival`, `WorldBraceArrival` |

The burn effect on burning targets keeps the first integration's choice (the Vefects small fire if it was
adapted, otherwise the procedural embers).

**Why procedural for the body effects.** They must start, grow and stop on cue, attach to bones, follow a
charge's power and fit the budget; the pack prefabs are one-shot bursts with 4 to 11 systems and up to
11,000 particles. **Why these pack effects.** They add shapes soft particles cannot: the Free Slash mesh
slash and impact, the Hovl spark streaks and ground burst.

**Adaptation of a pack effect** (never editing the pack's own prefab): instantiated, unpacked completely,
lights (with the URP light data that requires them), audio sources and distortion children removed, collisions, the Lights module and sub-emitters
off, renderers with a missing material off (no pink), Built-in materials replaced by URP Particles/Unlit
copies that keep texture and blending, HDR colours above 1.2 brought down in project-owned copies of the
materials (the Free Slash shader graphs are kept), systems over budget switched off, particle limits cut,
scaled, pooled. Not considered usable as stock: anything with a light, Laser, Meteors, Portals, the
Vefects heat haze (it needs the opaque texture, which the mobile pipeline asset leaves off).

**Which slots the setup fills.** An empty slot, or one holding a default that an earlier version of the
setup generated (the SoftGlow placeholders, the first sprint's procedural bursts in `Generated/VFX`, and
the first pack adaptations in `Generated/VFX/Adapted`). A slot holding anything else is kept. The
setup's report lists every slot it set. `Generated/VFX/Adapted` is no longer used once the setup has run
and may be deleted by the owner.

## Android

Inspected, from the packs' files (`Docs/MagicVfx/PackInventory.md`):

| Risk | Found in the packs | Done |
| --- | --- | --- |
| Particle counts | 1,000 particles per system as the limit everywhere; 3 to 11 systems per prefab | Budget per role (`MagicVfxBudget`): bursts 3 systems and 48 particles, attached 3 and 64, areas 3 and 96, world 2 and 40, barrier 2 and 32. Procedural effects are built within it; adapted ones are cut to it |
| Real-time lights | Point lights in the Free Slash slashes; the Lights module in Free Slash, Hovl circles and every Vefects fire | None in any combat effect; removed in adaptations |
| Particle collisions | Free Slash slashes and projectiles; Hovl sparks and AoE | Off: hits are resolved by the rules |
| Shader compatibility | Hovl: Built-in particle shaders; Free Slash: URP Shader Graph; Vefects: hand-written URP shaders; several missing materials | Hovl remapped to URP Particles/Unlit; Free Slash graphs kept (URP; Android compilation unverified); Vefects not used in new effects; missing materials switched off |
| Transparent overdraw | HDR colours up to about 8.6 (Free Slash) and 6.35 (Hovl); large billboards | Colours brought to 1.2 at most; procedural particles are small, and none is drawn larger than half the screen |
| Texture resolution | Free Slash textures import at 2,048 px, none with an Android setting; Hovl and Vefects mostly 256 to 512 | `Threefold > Setup > Limit VFX Pack Textures for Android` sets a 512 px Android limit on the pack textures the adapted effects use (their `.meta` files). Run by hand, not by the setup |
| Material variants | Each pack its own materials | Procedural effects share one material (SoftGlow); pack copies are shared per source material |
| Trail complexity | Trails modules in a few pack prefabs | Trail renderers of 0.1 to 0.32 s, 3 cm between points, 2 cap vertices; 60% length on Performance |
| Instantiate and Destroy | - | Pooled throughout; the sequencer allocates nothing per frame (tested in the .NET runner) |

`Threefold > Validate > Magic VFX` checks every effect the disciplines and the world feedback use against
the budget and reports pack textures larger than 512 px on Android. A batch-mode Edit Mode test runs the
same check.

## Tuning

- **Timing of a discipline:** the discipline asset, Presentation, *Body effects*: `bodyTiming`. Set
  `configured` on to use your own values (the setup does this when it writes the defaults).
- **Look of an effect:** the prefab in `Generated/VFX/Body` or `Generated/VFX/Packs`. The setup never
  rebuilds an existing prefab; delete one to have it generated again.
- **Which effect a slot uses:** the slot in the discipline asset. The setup keeps your choice.
- **Wave travel time:** `areaWaveSeconds` in the discipline asset (0.22 s by default). Visual only.
- **Intensity:** `DisciplineBodyEffects` and `WorldMagicFeedback` on `CombatVisuals`.
- **World arrival repeat:** `WorldMagicFeedback.repeatSeconds` (0.5 s): the same arrival on the same object
  at most this often, so a held Aegis guard does not flood an object.

## Missing effects

| Wanted | Stand-in now |
| --- | --- |
| A kick for Flame (fire following a foot) | The Flare is a sweep with the right hand; the foot sockets and foot effectors are supported, no strike uses them yet |
| Painted, non-photoreal flame textures | Soft round particles on the SoftGlow texture; the Vefects fire is realistic and uses its own shaders |
| A ring or shockwave texture for the Aegis ripples | A soft disc that grows and fades |
| A crystalline lattice for the Aegis brace | Particles closing in from a shell |
| Release effects designed per discipline | The first integration's procedural bursts, now placed at the casting hand |
| A visual where the Arcane Arrow leaves the hand | The arrow starts at the caster's cast point (chest height), as the rules decide; the release burst is at the hand. Moving the rules' cast point to the hand would let the animation decide where a shot starts, which the combat rules do not allow |
| Impact flashes delayed to the moment the Flame Wave front reaches each target | Impacts show at release, when the rules resolve the hit; the front takes 0.22 s to cross the area, so a far target flashes up to that much before the front arrives |
| A menu for effect intensity | The Inspector value |

## Laptop actions

1. Pull `main` and let Unity import. `git lfs pull` if the pack textures are pointers.
2. Run **Threefold > Setup > Magic VFX** (or **Threefold > Setup > Disciplines**, which runs it) in each
   scene with the Mage: the Discipline Range first, then the Combat Arena, the Fantasy Playground and the
   Broken Foundry. Read the report; save each scene.
3. Run **Threefold > Validate > Magic VFX** and note the result.
4. Optional: run **Threefold > Setup > Limit VFX Pack Textures for Android**, then commit the changed `.meta`
   files of the pack textures.
5. Commit the generated files under `Assets/Threefold/Generated/VFX/` and the changed discipline assets.
6. Review in the Discipline Range (below), then on a phone.

## Review in Unity and on a device

- [ ] Each strike of each discipline: the trail is on the striking hand, starts and stops at sensible
      moments, and the contact flash coincides with the hit.
- [ ] Flame: the fire grows into the punch; embers after a hit, none after a miss or a cancel.
- [ ] Aegis: the palm gathers before the strike; the slam's ground ring reads as heavier than the palm.
- [ ] Charge each discipline and cancel half way: the buildup grows, then disappears at once; no release.
- [ ] Arcane Arrow: the ribbon follows it and fades where it hits; no streak from the previous shot.
- [ ] Flame Wave and Force Pulse: the front travels over the shown area.
- [ ] Block with Aegis: a restrained pane, gone when the guard drops; switch discipline while blocking.
- [ ] Charge a dormant crystal with Arcane: the charge flows into it; heat a brazier, push a crate, brace
      the crumbling walkway.
- [ ] No pink materials; `Threefold > Validate > Magic VFX` clean.
- [ ] Performance preset: the same silhouettes with fewer particles.
- [ ] On a phone: frame time in the development overlay during a busy fight, before and after.

## Verified and not verified

| | |
| --- | --- |
| Pack contents (systems, particles, lights, collisions, shaders, HDR colours, texture limits, missing materials) | Verified from the files, by the analysis tool |
| Sequencing rules: start, grow, stop, burst; cancellation; stale cues; stop exactly once; no allocation | Tested outside Unity (`MagicVfxSequencerTests`) |
| Geometry, quality levels, intensity, budget, world arrivals, per-discipline timing | Tested outside Unity (`MagicVfxRulesTests`) |
| Compiles for the Editor and with Android player defines | Verified (`Tools/ci/compile_check.sh`) |
| The setup generates, adapts and wires the effects; every effect is within budget | Batch-mode Edit Mode tests written; **not run** (they need Unity) |
| What any effect looks like, its scale, colour, timing on screen | **Not verified** |
| Bone attachment on the real Mage model | **Not verified** |
| Free Slash shader graphs compiling for Android | **Not verified** |
| Frame time, overdraw, memory on a device | **Not measured** |

## Code

| Part | Files |
| --- | --- |
| Rules (plain C#) | `BodyEffectSequencer.cs`, `MagicVfxGeometry.cs`, `MagicVfxQuality.cs`, `MagicVfxBudget.cs`, `WorldReactionVfx.cs` (CombatFeedback); `BodyEffectTiming.cs`, `MagicVfxDefaults.cs` (Magic) |
| Runtime | `DisciplineBodyEffects.cs`, `WorldMagicFeedback.cs`, `ArcEmitterEffect.cs`, `InflowEffect.cs`, `PooledEffect.cs`, `DisciplineVfxSockets.cs`, `DisciplineFeedback.cs` |
| Hooks | `Projectile.Launched`, `MagicalObject.AnyEffectReceived`, `PooledObject.Generation` |
| Editor | `MagicVfxSetup.cs`, `MagicVfxPrefabs.cs`, `MagicVfxPackAdapter.cs`, `MagicVfxValidation.cs` |
| Tests | `MagicVfxSequencerTests.cs`, `MagicVfxRulesTests.cs`, `MagicVfxSetupTests.cs` |
| Tools | `Tools/vfx/analyse_vfx_packs.py` → `Docs/MagicVfx/PackInventory.md` |
