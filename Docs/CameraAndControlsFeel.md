# Camera and controls feel: audit and first pass

**Status (16 November 2026).** An audit of the camera, the touch controls, the combat feedback and the
movement as the code has them, and a small first pass that adds nothing to the game until it is switched
on from the diagnostics. Rules tested outside Unity and in Unity on the build runner. **Nothing here has
been played.** Every judgement of feel in this document is a hypothesis until somebody has the game in
their hands; the checklist at the end is how to find out.

No combat rule, number or timing is changed, and with the settings at their defaults the game behaves
exactly as it did.

## Audit

Numbers are the scripts' defaults. A scene keeps the values it was assembled with, so a value somebody
tuned in a scene may differ.

### What the code does

| Area | Behaviour |
| --- | --- |
| Camera follow | In `LateUpdate`, after the character has moved in `Update`. Sideways it catches up in about 0.06 s, vertically in 0.18 s, never trailing by more than 1.2 m |
| Framing | 5 m behind, pivot 1.5 m up, shoulder offset 0.55 m right and 0.15 m up, starting pitch 15 degrees down, pitch limits -25 to 65 |
| Rotation | Look input turns a target heading; the view follows it in about 0.04 s. 0.15 degrees per look unit across, 0.12 up and down. Vertical not inverted |
| Recentering | **None**, automatic or by hand. The one automatic turn is the swing assist of `DisciplineCombat` (35 degrees a second, 6-degree dead zone, stops the moment the player turns the camera) |
| Collision | A sphere cast from the pivot; the camera closes in at once and eases back out at 6 m a second. The shoulder offset is shortened beside a wall |
| Shake | Amounts accumulate to at most 1 and decay at 2.6 a second. The angle is the amount squared times 1.6 degrees, on unscaled time |
| Movement | `ThirdPersonMotor` in `Update`, relative to the camera's forward direction. Acceleration 28, deceleration 34, turn in about 0.09 s. Input is not smoothed |
| Touch routing | A touch belongs to the control it began on until it lifts (`PointerRouter`). Buttons come before the joystick, and the look area last. A drag that begins on a button cannot turn the camera; two tests hold this |
| Touch look | Pixels are scaled by 1080 over the screen's height, then by the camera's sensitivity. No dead zone |
| Joystick | Dead zone 0.12, response exponent 1.4 |
| Button feedback | Buttons shrink to 0.92 and change colour while held. The attack button also flashes for 0.18 s on release: white for a tap, the discipline's colour for a charged release, red for a cancel |
| Hit feedback | The target flashes for 0.09 s and swells by a tenth for 0.16 s; a damage number rises for 0.8 s; the camera shakes |
| Hit-stop, hit sounds, combat haptics | **None exist** |
| Player options | **None**: no sensitivity, invert, or shake setting reaches the player |
| Tests | Routing, joystick, attack gesture and layout are well covered (93 tests). **The camera had none**; all its arithmetic is inside the Unity component |

### Hypotheses, not confirmed

Each of these follows from reading the code. None has been seen.

1. **Look speed differs between a phone and a tablet.** The buttons are sized by the screen's physical
   density, the look speed by its height in pixels only. The same finger travel in centimetres may turn
   the camera differently on a 6-inch and an 11-inch screen. To be measured on both.
2. **Shake is larger than the strike data intends.** `Health` announces every hit, and
   `CombatCameraFeedback` answers each of the player's hits with 0.24 (0.45 on a defeat), while the
   discipline's own feedback adds the strike's amount (0.08 to 0.30). Both run for a melee hit, so a light
   strike shakes with 0.32, not 0.08. Confirmed in the code. Whether it matters is not: the angle is the
   amount squared, so that is 0.16 degrees against 0.01. Left as it is; the shake setting scales all of it.
3. **Heavy shake nudges the heading.** The motor steers by the camera's transform, which includes the
   shake. At full shake that is up to 1.6 degrees for a moment.
4. **No recentering may be tiring on touch.** With one thumb on the joystick and the other on attack, the
   camera is not being turned, and nothing brings it round. This is the reason for the optional
   recentering below; it is a guess about comfort.

## The first pass

Small, and reversible from the device: everything is off or neutral until changed, and one button puts it
back.

| Addition | What it does | Default |
| --- | --- | --- |
| `CameraRecenter` (plain C#) | While the character moves and the player has left the camera alone for a delay, the view comes round towards his heading: the short way, eased in and out, never past the heading, and not at all when he runs towards the camera | **Off** |
| `FeelSettings`, `FeelSettingsStore` | Look speed for touch, shake strength, recentering on or off with its delay and speed; kept between launches | Look x1, shake x1: the game as it was |
| Diagnostics, **Feel** | A button per value; each press steps it and saves it. **Defaults** puts the game back as it was. A FEEL line shows the values in force | |
| Shake strength | Multiplies every shake; 0 switches shake off. It is also the first form of a reduce-shake option | x1 |

Recentering goes through the same target heading as look input, so the camera's own smoothing applies and
it cannot jump. `ThirdPersonCamera` only carries out what `CameraRecenter` decides.

### Hit-stop: evaluated, not implemented

A hit-stop freezes the action for a few frames on impact. Two ways exist, and neither is safe to add
without a decision:

- **Slowing game time** (`Time.timeScale`) would stretch every combat timer by the length of the stop:
  recovery, the combo window, the charge, enemy attacks and their telegraphs all run on game time. That
  changes combat timing and balance, which this pass must not do. It would also stop the world for other
  enemies in a group fight.
- **Freezing only the picture** (the Animator) would let the rules run on while the animation stands
  still. The project's rule is that animation follows the rules' clock and never delays a hit
  (`Docs/CombatAnimationArchitecture.md`); a frozen animation would then be late against its own contact
  and recovery moments until it was caught up.

For accessibility: a stop is a sudden change of motion, and some players find repeated stops unpleasant;
it would need a switch like the shake's. Recommendation: judge first, on a device, whether hits already
read clearly with flash, number and shake. If they do not, the next step is a stop of the picture only,
for the struck enemy only, of two or three frames, with the catch-up designed into
`CombatAnimationModel` and a switch in the feel settings.

### Haptics: evaluated, not implemented

The project has one haptics path (`DiscoveryHaptics`): gamepad rumble, and the phone's own vibration,
which is a fixed buzz of about half a second, off by default for that reason. Half a second is longer than
a strike, so it cannot confirm a hit, and repeated in a fight it would cost battery and be unpleasant. A
short pulse of 10 to 30 ms needs Android's vibration interface called directly and the vibrate permission
in the app's manifest, which is a project setting. Recommendation: decide after the first device test
whether hit confirmation is lacking; if so, add short pulses behind a switch that is off by default, with
the manifest change shown first.

### Hit sounds

There are no sound assets and no audio for combat. Nothing was added. A trigger already exists to hang
sound on later: `CombatEvents.DamageDealt` and the animation cues.

## Tests

`CameraFeelTests` (25 cases, outside Unity and in Unity): off by default; waits for the delay; look input
stops it at once and restarts the wait; never while standing; turns the short way and comes to rest at
the dead zone without passing the heading, from five starting angles; stays when the character runs
towards the camera; never faster than its limit and eases in; nearly the same result at 30, 60 and 120
frames a second; nonsense time turns nothing; the settings' defaults are the game as it was; every button
goes round its steps and back; saved nonsense becomes usable values.

Not testable here: `ThirdPersonCamera` itself (follow, collision, shake) lives in a Unity component and
needs the engine's physics; the feel of any of it.

## Playtesting checklist

Use a development APK. Open the diagnostics with a three-finger tap; the FEEL line shows the values in
force. Note the device, and for each line: fine, not fine, or a remark.

**First, the tools**
- [ ] The diagnostics' buttons answer to touch. If they do not, say so: the Feel panel cannot be used and
      needs another form.
- [ ] **Feel** opens the panel; pressing a button changes the value shown and the FEEL line.
- [ ] After closing and reopening the app the changed values are still there. **Defaults** restores them.

**Camera, with everything at its defaults**
- [ ] Walking and sprinting: the camera follows without lag that bothers you and without jitter.
- [ ] Turning the camera with a drag: it starts and stops with the finger; no drift afterwards.
- [ ] Along a wall and in a corner: the camera closes in without showing the inside of the wall, and
      comes back out without a jump.
- [ ] Jumping, and each aerial ability (Ascension, Fire Glide, Force Dash): the character stays in view;
      no lurch on landing.
- [ ] Is the character comfortably framed, or too large, too small, too central?

**Look speed**
- [ ] Is a full sweep of the thumb about a quarter turn? Try x0.75 and x1.25 and note which you prefer.
- [ ] If you have a phone and a tablet: does the same sweep turn the view the same amount on both?

**Recentering (switch "Camera comes round by itself" on)**
- [ ] Run in a curve without touching the camera: does the view come round behind him gently?
- [ ] Does it ever fight you, or bend your path when you meant to run straight?
- [ ] Run towards the camera: it must stay where it is.
- [ ] Turn the camera by hand while running: it must obey at once and leave it alone for a moment after.
- [ ] Try the delays and speeds; note the pair you prefer, or that you prefer it off.

**Touch controls**
- [ ] The joystick answers at once; no movement at rest.
- [ ] Moving, turning the camera and attacking at the same time with three fingers.
- [ ] A drag that starts on ATTACK, BLOCK or JUMP never turns the camera.
- [ ] Can you tell, without looking at the character, that a tap on ATTACK was taken? A charge? A cancel?
- [ ] Are any buttons too close to the edge, to each other, or under your palm?

**Combat feedback**
- [ ] Is it clear, each time, that a strike landed? What told you: the flash, the number, the shake?
- [ ] Shake at x1: too much, too little, about right? Try x0.5 and off.
- [ ] Does anything feel as if it should pause on impact, or buzz in the hand? That decides whether
      hit-stop and haptics are worth building.

Record the answers in `Docs/LivingMagicTesting.md` with the build's run number and commit, which the
diagnostics' first line shows.
