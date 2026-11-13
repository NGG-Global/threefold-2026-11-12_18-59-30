# Mage combat foundation

Sprint 3 gives the Mage a first spell, Arcane Bolt, with the systems around it: health, mana,
aiming, projectiles, a training dummy, a HUD and combat feedback.

**Status:** the code has passed a syntax check and the repository checks only. It has not been
compiled in Unity, and nothing here has been played. This sprint is built on the touch controls
of Sprint 2, which are in the same unverified state.

## What the player can do

- Attack with the **left mouse button** (after the click that captures the mouse), the gamepad's
  west button, or the on-screen **attack button**. Holding the control keeps firing.
- Aim with the camera. The bolt goes to whatever is under the crosshair in the middle of the
  screen. A target close to the crosshair is aimed at automatically, which makes aiming with a
  thumb forgiving.
- Attack while moving. The Mage turns to face the aim for a moment and keeps moving.
- Each bolt costs mana. Mana regenerates after a short pause. With too little mana the mana bar
  and the crosshair flash red and nothing is cast.

Default values: Arcane Bolt costs 12 mana, has a 0.35 s cooldown and deals 20 damage. The Mage
has 100 health and 100 mana; mana returns at 9 per second, 0.9 s after the last spend. A
training dummy has 100 health and returns 4 s after it is defeated.

## Feedback

| Moment | Feedback |
| --- | --- |
| Cast | Crosshair kick, light camera shake, the Mage turns to face the aim |
| Flight | Glowing core with an additive trail |
| Impact | Particle burst, firmer camera shake |
| Enemy hit | The target flashes and jolts, a damage number rises, a health bar appears above it |
| Enemy defeated | Larger burst, stronger shake, the number is highlighted, the dummy disappears and later returns |
| Not enough mana | Mana bar and crosshair flash red |

All effects are generated: a small glow texture, unlit and additive materials, one particle
burst per impact. There are no lights, no particle collisions and no image or model files.

## Set up

1. Pull the branch on the laptop and let Unity import. The Console must show no red errors.
2. Run **Threefold > Setup > Mage Prototype**. It now also sets up touch controls and combat. In
   a scene that already has the Mage, **Threefold > Setup > Combat** adds only what is missing.
3. Save the scene.
4. Upload the new files from the laptop (see `Docs/DevelopmentWorkflow.md`). The command creates
   assets in `Assets/Threefold/Generated/Combat`, and they exist only on the laptop until uploaded.

The command generates and connects:

| Generated | What it is |
| --- | --- |
| `ArcaneBolt.asset` | The spell definition: cost, cooldown, damage, speed, range |
| `ArcaneBolt.prefab`, `ArcaneImpact.prefab` | Projectile and impact effect |
| `TrainingDummy.prefab`, `DummyBurst.prefab` | Practice target and its defeat effect |
| `SoftGlow.png` and six materials | Visuals for the above |
| In the scene | Health, Mana, aiming, caster and attack input on the Mage; four training dummies; `CombatHud` with health bar, mana bar, crosshair and damage numbers; camera shake; the attack button |

Existing assets and scene objects are kept as they are. Delete a generated asset to have it
recreated with default values.

## Architecture

Rules, presentation and input are separate, and they are joined only by events and by the setup
command.

| Layer | Files (under `Assets/Threefold/Scripts/Runtime`) | Knows about |
| --- | --- | --- |
| Rules | `Combat/ResourcePool`, `Health`, `Mana`, `AbilityCaster`, `AbilityDefinition`, `ProjectileSpellDefinition`, `Projectile`, `DamageInfo`, `CombatEvents`, `TrainingDummy` | Nothing about input, UI or effects |
| Aiming | `Combat/AimProvider`, `CameraAimProvider` | The camera, for the player |
| Input bridge | `Combat/PlayerAbilityInput` | `CharacterInputSource` and `AbilityCaster` |
| Presentation | `CombatFeedback/PlayerHud`, `ResourceBar`, `DamageNumbers`, `HitFlash`, `WorldHealthBar`, `CombatCameraFeedback`, `PooledEffect` | Listens to events from the rules |
| Pooling | `Pooling/Pools`, `Pool`, `PooledObject` | Nothing about combat |

How this supports three classes:

- `AbilityCaster` applies cooldown and mana cost to any `AbilityDefinition` and does not know
  what an ability does. It has slots; slot 0 is the primary attack.
- Each kind of ability is a subclass of `AbilityDefinition`. The Mage uses
  `ProjectileSpellDefinition`. A Rogue melee strike or a Monk dash would be further subclasses,
  with their own behaviour and their own assets, without changing the caster.
- `Mana` is optional on a caster, so a class with a different resource, or none, is possible.
- `AimProvider` is abstract. The player aims with the camera; an enemy would aim at its target.
- A new spell of an existing kind needs no code: duplicate `ArcaneBolt.asset` and change its values.

## Performance

- Projectiles and effects are pooled. Each spell has a limit on live projectiles (12 by
  default); beyond it the oldest is reused, so their number cannot grow without bound.
- A projectile moves with one sphere sweep per frame. It has no Rigidbody and no collider.
- Physics queries use pre-allocated buffers. Damage number texts are cached. Nothing in the
  combat loop allocates memory per frame.
- Damage numbers have their own canvas, so moving them does not redraw the HUD bars.

## Tuning

| To change | Where |
| --- | --- |
| Spell cost, cooldown, damage, speed, range, hit radius | `Assets/Threefold/Generated/Combat/ArcaneBolt.asset` |
| Mage health and mana, regeneration | `Mage` object: Health, Mana |
| Aim assist angle and range | `Mage` object: Camera Aim Provider |
| How long the Mage faces its aim | `Mage` object: Player Ability Input |
| Camera shake strength | `Main Camera`: Third Person Camera (Shake), Combat Camera Feedback |
| Camera shoulder offset | `Main Camera`: Third Person Camera (Shoulder Offset) |
| Dummy health and return time | `TrainingDummy.prefab` |

## Changes to earlier systems

- **Camera:** now sits over the Mage's shoulder (Shoulder Offset), so the screen centre is clear
  for aiming, and it can shake.
- **Movement:** `ThirdPersonMotor.FaceDirection` lets an ability turn the character while it moves.
- **Input:** `CharacterInputSource` gained `AttackHeld` and `AttackPressedThisFrame`. A tap on
  the touchscreen outside the attack button does not attack.
- **Touch controls:** an attack button above Jump. Controls created in Sprint 2 are upgraded in
  place by the setup command; their other parts are not changed.

## Tests

Edit Mode tests in `Assets/Threefold/Tests/Editor` cover the resource rules, health (damage,
defeat, invulnerability, healing, revival, event reporting), mana (spending, refusal,
regeneration and its delay) and the caster (cost, cooldown, refusal reasons, defeated caster).

Not covered by automated tests: projectile flight and collision, aiming and aim assist,
pooling, every visual effect, the HUD, the generated assets, and the setup command itself.
These need a run in the Editor.

## Known limits

- Not compiled in Unity or played. Feel (speed, shake, aim assist) is set by judgement and will
  need tuning once it can be seen.
- Until Sprint 8 nothing damaged the Mage. Enemies now do; see `Docs/EnemyAI.md`.
- The generated glow material sets URP shader properties from code. If it does not look
  additive, correct it in the Inspector or report it.
- Damage numbers use Unity's built-in font and fall back to a device font. If neither is
  available, numbers are not shown and a warning is logged.
- There is no sound, and no cast animation.
