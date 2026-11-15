using System.Collections.Generic;
using Threefold.CombatAnimation;
using Threefold.Magic;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>An effect that stays on the body for a while and must be stopped.</summary>
    public enum BodyEffectLayer
    {
        /// <summary>The trail on the striking hand or foot.</summary>
        Trail = 0,

        /// <summary>Energy gathering at the striking limb before contact.</summary>
        Gather = 1,

        /// <summary>The buildup of a charged attack.</summary>
        Charge = 2,
    }

    /// <summary>A one-off effect.</summary>
    public enum BodyEffectBurst
    {
        /// <summary>The oriented ribbon or slash around the body.</summary>
        Swing = 0,

        /// <summary>Concentrated energy at the limb at the moment of contact.</summary>
        Contact = 1,

        /// <summary>The contact of a heavy strike.</summary>
        HeavyContact = 2,

        /// <summary>Embers or motes left behind after a strike that made contact.</summary>
        Linger = 3,

        /// <summary>A charged attack leaves the hand.</summary>
        Release = 4,
    }

    public enum BodyEffectCommandKind
    {
        Start = 0,
        SetIntensity = 1,
        Stop = 2,
        Burst = 3,
    }

    /// <summary>What the presentation should do. It carries no damage and changes no rule.</summary>
    public readonly struct BodyEffectCommand
    {
        public readonly BodyEffectCommandKind Kind;
        public readonly BodyEffectLayer Layer;
        public readonly BodyEffectBurst Burst;
        public readonly int ActionId;
        public readonly int Discipline;
        public readonly CombatEffector Effector;

        /// <summary>Start and SetIntensity: strength 0 to 1. Charge: also the gathering radius in <see cref="Radius"/>.</summary>
        public readonly float Intensity;

        public readonly float Radius;

        /// <summary>Stop: seconds to let the effect fade before it is returned.</summary>
        public readonly float DrainSeconds;

        public BodyEffectCommand(BodyEffectCommandKind kind, BodyEffectLayer layer, BodyEffectBurst burst, int actionId,
            int discipline, CombatEffector effector, float intensity, float radius, float drainSeconds)
        {
            Kind = kind;
            Layer = layer;
            Burst = burst;
            ActionId = actionId;
            Discipline = discipline;
            Effector = effector;
            Intensity = intensity;
            Radius = radius;
            DrainSeconds = drainSeconds;
        }
    }

    /// <summary>Everything the sequencer needs to know about an action when it begins.</summary>
    public struct BodyActionInfo
    {
        public CombatActionKind Action;
        public int ActionId;
        public int Discipline;
        public int StepIndex;
        public CombatEffector Effector;

        /// <summary>The strike uses the heavy contact effect.</summary>
        public bool Heavy;

        /// <summary>Seconds from the start of the strike to its contact, from the rules (the step's start-up).</summary>
        public float SecondsToContact;

        public BodyEffectTiming Timing;
    }

    /// <summary>
    /// Decides, from the moments of the Mage's attacks, which body effects start, grow, stop and burst. It reads
    /// the combat cues (wind-up, effect, hit window, contact, release, recovery, cancellation), which come from the
    /// rules' clock, and answers with commands; it never touches a Unity object, so it is tested outside Unity.
    /// <see cref="DisciplineBodyEffects"/> carries the commands out.
    ///
    /// Guarantees:
    /// - every effect that starts is stopped exactly once, at the latest when its action ends;
    /// - a cancelled action shows no contact, linger or release burst, and its effects stop with a short drain;
    /// - contact and release bursts are shown at most once per action;
    /// - nothing of an action is shown after it has ended, or after a newer action began;
    /// - a strike's trail grows only towards its contact, never past full strength.
    /// No command deals damage: the hit and the release have already been resolved by the rules.
    /// Allocation-free once its command list has grown.
    /// </summary>
    public sealed class BodyEffectSequencer
    {
        private const float IntensityStep = 0.02f;

        private BodyActionInfo current;
        private bool active;
        private float beginTime;
        private bool trailOn;
        private bool gatherOn;
        private bool chargeOn;
        private bool swingShown;
        private bool contactShown;
        private bool lingerShown;
        private bool releaseShown;
        private float trailIntensity;
        private float chargeIntensity;

        /// <summary>The action being shown, or 0.</summary>
        public int CurrentActionId => active ? current.ActionId : 0;

        public bool IsActive => active;

        public bool IsOn(BodyEffectLayer layer)
        {
            switch (layer)
            {
                case BodyEffectLayer.Trail:
                    return trailOn;
                case BodyEffectLayer.Gather:
                    return gatherOn;
                default:
                    return chargeOn;
            }
        }

        /// <summary>
        /// An action begins (its wind-up). An action still being shown is ended first, as cancelled if it never
        /// reached its contact or release.
        /// </summary>
        public void Begin(in BodyActionInfo info, float now, List<BodyEffectCommand> output)
        {
            if (active)
            {
                bool landed = contactShown || releaseShown;
                End(!landed, output);
            }

            current = info;
            if (current.Timing == null)
            {
                current.Timing = MagicVfxDefaults.For((MagicDiscipline)Mathf.Max(0, info.Discipline));
            }

            active = true;
            beginTime = now;
            trailOn = gatherOn = chargeOn = false;
            swingShown = contactShown = lingerShown = releaseShown = false;

            BodyEffectTiming timing = current.Timing;
            if (current.Action == CombatActionKind.Charged)
            {
                chargeIntensity = timing.chargeStartIntensity;
                StartLayer(BodyEffectLayer.Charge, chargeIntensity, timing.chargeRadiusAtStart, output);
                return;
            }

            if (timing.gatherBeforeStrike)
            {
                StartLayer(BodyEffectLayer.Gather, 1f, 0f, output);
            }

            ReachMoment(BodyEffectMoment.Windup, output);
        }

        /// <summary>A moment of the action. Cues of an older action, or of none, are ignored.</summary>
        public void Cue(CombatCueKind kind, int actionId, List<BodyEffectCommand> output)
        {
            if (!active || actionId != current.ActionId)
            {
                return;
            }

            switch (kind)
            {
                case CombatCueKind.Effect:
                    if (current.Action == CombatActionKind.Melee)
                    {
                        ReachMoment(BodyEffectMoment.Effect, output);
                    }

                    break;

                case CombatCueKind.HitWindowOpened:
                    if (current.Action == CombatActionKind.Melee)
                    {
                        // A missed effect moment still lets the effects that start before the window appear.
                        ReachMoment(BodyEffectMoment.HitWindow, output);
                    }

                    break;

                case CombatCueKind.Contact:
                    Contact(output);
                    break;

                case CombatCueKind.HitWindowClosed:
                    if (current.Action == CombatActionKind.Melee)
                    {
                        StopLayer(BodyEffectLayer.Trail, current.Timing.drainSeconds, output);
                        Linger(output);
                    }

                    break;

                case CombatCueKind.Release:
                    Release(output);
                    break;

                case CombatCueKind.Recovered:
                    End(false, output);
                    break;

                case CombatCueKind.Cancelled:
                    End(true, output);
                    break;
            }
        }

        /// <summary>
        /// Advances the growing effects: a trail that intensifies towards contact, and a charge that grows with
        /// its power (0 to 1, from the rules).
        /// </summary>
        public void Tick(float now, float chargePower, List<BodyEffectCommand> output)
        {
            if (!active)
            {
                return;
            }

            BodyEffectTiming timing = current.Timing;

            if (trailOn && timing.trailStartIntensity < 1f && !contactShown)
            {
                float wanted = TrailIntensityAt(timing.trailStartIntensity, now - beginTime, current.SecondsToContact);
                if (wanted - trailIntensity >= IntensityStep || (wanted >= 1f && trailIntensity < 1f))
                {
                    trailIntensity = wanted;
                    output.Add(Command(BodyEffectCommandKind.SetIntensity, BodyEffectLayer.Trail, default, trailIntensity, 0f, 0f));
                }
            }

            if (chargeOn)
            {
                float power = Mathf.Clamp01(chargePower);
                float wanted = Mathf.Lerp(timing.chargeStartIntensity, 1f, power);
                if (Mathf.Abs(wanted - chargeIntensity) >= IntensityStep || (power >= 1f && chargeIntensity < 1f))
                {
                    chargeIntensity = wanted;
                    float radius = Mathf.Lerp(timing.chargeRadiusAtStart, timing.chargeRadiusAtFull, power);
                    output.Add(Command(BodyEffectCommandKind.SetIntensity, BodyEffectLayer.Charge, default, chargeIntensity,
                        radius, 0f));
                }
            }
        }

        /// <summary>Stops everything at once, for when the presentation is switched off.</summary>
        public void Reset(List<BodyEffectCommand> output)
        {
            if (active)
            {
                End(true, output);
            }
        }

        /// <summary>
        /// Strength of a trail that grows towards contact: <paramref name="startIntensity"/> at the start of the strike,
        /// 1 at contact, and 1 afterwards. A strike without start-up is at full strength at once.
        /// </summary>
        public static float TrailIntensityAt(float startIntensity, float sinceStart, float secondsToContact)
        {
            if (secondsToContact <= 0.0001f)
            {
                return 1f;
            }

            float t = Mathf.Clamp01(sinceStart / secondsToContact);
            return Mathf.Lerp(Mathf.Clamp01(startIntensity), 1f, t * t);
        }

        // ------------------------------------------------------------------ steps

        private void ReachMoment(BodyEffectMoment moment, List<BodyEffectCommand> output)
        {
            BodyEffectTiming timing = current.Timing;
            if (contactShown)
            {
                return;
            }

            if (!trailOn && timing.trailFrom != BodyEffectMoment.None && timing.trailFrom <= moment)
            {
                trailIntensity = TrailIntensityAt(timing.trailStartIntensity, 0f, current.SecondsToContact);
                StartLayer(BodyEffectLayer.Trail, trailIntensity, 0f, output);
            }

            if (!swingShown && timing.swingFrom != BodyEffectMoment.None && timing.swingFrom <= moment)
            {
                swingShown = true;
                output.Add(Command(BodyEffectCommandKind.Burst, default, BodyEffectBurst.Swing, 1f, 0f, 0f));
            }
        }

        private void Contact(List<BodyEffectCommand> output)
        {
            if (current.Action != CombatActionKind.Melee || contactShown)
            {
                return;
            }

            contactShown = true;
            BodyEffectTiming timing = current.Timing;

            // The gathered force is spent in the strike.
            StopLayer(BodyEffectLayer.Gather, timing.cancelDrainSeconds, output);

            if (trailOn)
            {
                if (trailIntensity < 1f && timing.trailStartIntensity < 1f)
                {
                    trailIntensity = 1f;
                    output.Add(Command(BodyEffectCommandKind.SetIntensity, BodyEffectLayer.Trail, default, 1f, 0f, 0f));
                }

                if (!timing.trailUntilWindowCloses)
                {
                    StopLayer(BodyEffectLayer.Trail, timing.drainSeconds, output);
                }
            }

            output.Add(Command(BodyEffectCommandKind.Burst, default,
                current.Heavy ? BodyEffectBurst.HeavyContact : BodyEffectBurst.Contact, 1f, 0f, 0f));
        }

        private void Linger(List<BodyEffectCommand> output)
        {
            if (contactShown && !lingerShown && current.Timing.lingerAfterStrike)
            {
                lingerShown = true;
                output.Add(Command(BodyEffectCommandKind.Burst, default, BodyEffectBurst.Linger, 1f, 0f, 0f));
            }
        }

        private void Release(List<BodyEffectCommand> output)
        {
            if (current.Action != CombatActionKind.Charged || releaseShown)
            {
                return;
            }

            releaseShown = true;
            StopLayer(BodyEffectLayer.Charge, current.Timing.cancelDrainSeconds, output);
            output.Add(Command(BodyEffectCommandKind.Burst, default, BodyEffectBurst.Release, 1f, 0f, 0f));
        }

        private void End(bool cancelled, List<BodyEffectCommand> output)
        {
            BodyEffectTiming timing = current.Timing;
            float drain = cancelled ? timing.cancelDrainSeconds : timing.drainSeconds;

            if (!cancelled && current.Action == CombatActionKind.Melee)
            {
                Linger(output);
            }

            StopLayer(BodyEffectLayer.Trail, drain, output);
            StopLayer(BodyEffectLayer.Gather, cancelled ? timing.cancelDrainSeconds : drain, output);
            StopLayer(BodyEffectLayer.Charge, timing.cancelDrainSeconds, output);
            active = false;
        }

        private void StartLayer(BodyEffectLayer layer, float intensity, float radius, List<BodyEffectCommand> output)
        {
            switch (layer)
            {
                case BodyEffectLayer.Trail:
                    trailOn = true;
                    break;
                case BodyEffectLayer.Gather:
                    gatherOn = true;
                    break;
                default:
                    chargeOn = true;
                    break;
            }

            output.Add(Command(BodyEffectCommandKind.Start, layer, default, intensity, radius, 0f));
        }

        private void StopLayer(BodyEffectLayer layer, float drainSeconds, List<BodyEffectCommand> output)
        {
            switch (layer)
            {
                case BodyEffectLayer.Trail:
                    if (!trailOn)
                    {
                        return;
                    }

                    trailOn = false;
                    break;

                case BodyEffectLayer.Gather:
                    if (!gatherOn)
                    {
                        return;
                    }

                    gatherOn = false;
                    break;

                default:
                    if (!chargeOn)
                    {
                        return;
                    }

                    chargeOn = false;
                    break;
            }

            output.Add(Command(BodyEffectCommandKind.Stop, layer, default, 0f, 0f, drainSeconds));
        }

        private BodyEffectCommand Command(BodyEffectCommandKind kind, BodyEffectLayer layer, BodyEffectBurst burst,
            float intensity, float radius, float drain)
        {
            return new BodyEffectCommand(kind, layer, burst, current.ActionId, current.Discipline, current.Effector,
                intensity, radius, drain);
        }
    }
}
