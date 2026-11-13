using System.Collections.Generic;
using Threefold.Combat;
using Threefold.InputHandling;
using UnityEngine;

namespace Threefold.Magic
{
    /// <summary>What the model needs to know about the disciplines. Slots are in selector order.</summary>
    public interface IDisciplineCombatData
    {
        int Count { get; }

        /// <summary>The melee combo of a discipline, or null if the slot has none.</summary>
        MeleeComboProfile GetMelee(int slot);

        /// <summary>Timing and movement of the discipline's charged attack, or null if it has none.</summary>
        ChargeProfile GetCharge(int slot);
    }

    /// <summary>Whether a discipline's charged attack could be used right now: mana and cooldown.</summary>
    public interface IChargedAttackGate
    {
        CastResult Check(int slot);
    }

    /// <summary>Everything the model reads in one frame.</summary>
    public struct CombatFrameInput
    {
        public AttackGestureFrame Gesture;
        public bool BlockHeld;

        /// <summary>Hanging, climbing or vaulting: the hands cannot attack.</summary>
        public bool HandsBusy;

        public bool Alive;

        /// <summary>The selected discipline.</summary>
        public int DisciplineSlot;

        /// <summary>Damage the character took since the last frame, after blocking.</summary>
        public float DamageTaken;
    }

    public enum CombatCommandKind
    {
        /// <summary>A melee swing began: show it and turn the character.</summary>
        MeleeStepStarted,

        /// <summary>A melee swing lands: resolve its hit.</summary>
        MeleeHit,

        /// <summary>A melee swing was abandoned before it landed.</summary>
        MeleeInterrupted,

        /// <summary>A charge began.</summary>
        ChargeBegan,

        /// <summary>A charge could not begin. <see cref="CombatCommand.Reason"/> says why.</summary>
        ChargeRefused,

        /// <summary>A charge ended without firing.</summary>
        ChargeCancelled,

        /// <summary>A charge was released: fire the charged attack with the given power and aim.</summary>
        ChargedReleased,

        /// <summary>Tell the input to abandon the attack press, so the controls show the cancellation.</summary>
        InterruptInput,
    }

    public readonly struct CombatCommand
    {
        public readonly CombatCommandKind Kind;

        /// <summary>The discipline the command belongs to.</summary>
        public readonly int Slot;

        public readonly int StepIndex;
        public readonly MeleeStepProfile Step;

        /// <summary>Charged release: 0 for no charge, 1 for a full charge.</summary>
        public readonly float Power;

        /// <summary>Charged release: aim from dragging, length 0 to 1.</summary>
        public readonly Vector2 Aim;

        public readonly CastResult Reason;

        public CombatCommand(CombatCommandKind kind, int slot, int stepIndex = 0, MeleeStepProfile step = null,
            float power = 0f, Vector2 aim = default, CastResult reason = CastResult.Success)
        {
            Kind = kind;
            Slot = slot;
            StepIndex = stepIndex;
            Step = step;
            Power = power;
            Aim = aim;
            Reason = reason;
        }
    }

    /// <summary>
    /// The sequencing of the Mage's combat: which melee swing happens when, when a charge begins,
    /// is refused, is interrupted or fires, and how much movement each state allows. It reads the
    /// attack gesture and the selected discipline and answers with commands; it never touches a
    /// Unity object, so it is tested outside Unity. <see cref="DisciplineCombat"/> carries the
    /// commands out.
    ///
    /// Rules:
    /// - A tap asks for the next swing of the selected discipline's combo. A swing in progress
    ///   finishes as the discipline it started with; switching discipline restarts the combo.
    /// - A charge begins only if the charged attack is affordable and off cooldown. Like a swing, it
    ///   finishes as the discipline it began with: switching while it is held changes the attack
    ///   after it, not this one. Its strength comes from how long it was held.
    /// - Blocking abandons a swing that has not landed and allows no new one. Climbing, defeat and
    ///   a heavy hit abandon a swing or a charge; nothing fires.
    /// </summary>
    public sealed class DisciplineCombatModel
    {
        private readonly IDisciplineCombatData data;
        private readonly IChargedAttackGate gate;
        private readonly MeleeComboMachine combo = new MeleeComboMachine();
        private readonly List<MeleeEvent> meleeEvents = new List<MeleeEvent>(4);

        private int lastSlot = -1;
        private int swingSlot;
        private int chargeSlot;
        private bool charging;
        private bool ignoreGestureUntilIdle;
        private float chargeStartTime;
        private float now;

        public DisciplineCombatModel(IDisciplineCombatData data, IChargedAttackGate gate)
        {
            this.data = data;
            this.gate = gate;
        }

        /// <summary>Movement speed while blocking, relative to normal.</summary>
        public float BlockMoveSpeedMultiplier { get; set; } = 0.55f;

        /// <summary>Damage in one frame that breaks a charge. 0 means a charge is never broken by damage.</summary>
        public float ChargeInterruptDamage { get; set; } = 10f;

        public MeleeComboMachine Combo => combo;

        public bool IsCharging => charging;

        /// <summary>The discipline of the charge being held. Meaningful only while charging.</summary>
        public int ChargeSlot => chargeSlot;

        /// <summary>Strength of the charge being held, 0 to 1.</summary>
        public float CurrentChargePower => ChargePower(chargeSlot);

        /// <summary>Seconds the current charge has been held, or 0.</summary>
        public float ChargeSeconds => charging ? Mathf.Max(0f, now - chargeStartTime) : 0f;

        /// <summary>Movement speed the current state allows, relative to normal.</summary>
        public float MoveSpeedMultiplier { get; private set; } = 1f;

        /// <summary>False while swinging, charging or blocking.</summary>
        public bool SprintAllowed { get; private set; } = true;

        /// <summary>Strength of the current charge for a discipline, 0 to 1.</summary>
        public float ChargePower(int slot)
        {
            ChargeProfile charge = data.GetCharge(slot);
            return charging && charge != null ? ChargeScaling.Power(ChargeSeconds, charge.fullChargeSeconds) : 0f;
        }

        /// <summary>Advances one frame and appends what should happen, in order.</summary>
        public void Tick(in CombatFrameInput input, float time, List<CombatCommand> commands)
        {
            now = time;
            int slot = Mathf.Clamp(input.DisciplineSlot, 0, Mathf.Max(0, data.Count - 1));
            AttackGestureFrame gesture = input.Gesture;

            if (!gesture.IsActive && !gesture.ChargeReleased && !gesture.Cancelled && !gesture.Tapped)
            {
                ignoreGestureUntilIdle = false;
            }

            if (lastSlot != slot)
            {
                if (lastSlot >= 0)
                {
                    combo.RestartChain();
                }

                lastSlot = slot;
            }

            if (!input.Alive || input.HandsBusy)
            {
                AbandonSwing(commands);
                BreakCharge(gesture, commands);
                UpdateMovement(input.BlockHeld);
                return;
            }

            if (input.BlockHeld)
            {
                AbandonSwing(commands);
            }

            HandleCharge(slot, gesture, input, commands);

            if (gesture.Tapped && !input.BlockHeld && !ignoreGestureUntilIdle)
            {
                combo.Request(time);
            }

            meleeEvents.Clear();
            combo.Tick(time, data.GetMelee(slot), meleeEvents);
            TranslateMelee(slot, commands);

            UpdateMovement(input.BlockHeld);
        }

        /// <summary>Stops everything without commands, for when the component is switched off.</summary>
        public void Reset()
        {
            combo.Reset();
            charging = false;
            ignoreGestureUntilIdle = false;
            MoveSpeedMultiplier = 1f;
            SprintAllowed = true;
        }

        private void HandleCharge(int slot, in AttackGestureFrame gesture, in CombatFrameInput input, List<CombatCommand> commands)
        {
            if (ignoreGestureUntilIdle)
            {
                return;
            }

            if (gesture.ChargeStarted && !charging)
            {
                CastResult result = data.GetCharge(slot) == null ? CastResult.NoAbility : gate.Check(slot);
                if (result != CastResult.Success)
                {
                    commands.Add(new CombatCommand(CombatCommandKind.ChargeRefused, slot, reason: result));
                    commands.Add(new CombatCommand(CombatCommandKind.InterruptInput, slot));
                    ignoreGestureUntilIdle = true;
                    return;
                }

                charging = true;
                chargeSlot = slot;
                chargeStartTime = now;
                commands.Add(new CombatCommand(CombatCommandKind.ChargeBegan, slot));
            }

            if (!charging)
            {
                return;
            }

            if (gesture.ChargeReleased)
            {
                float power = ChargePower(chargeSlot);
                charging = false;
                commands.Add(new CombatCommand(CombatCommandKind.ChargedReleased, chargeSlot, power: power, aim: gesture.Aim));
                return;
            }

            if (gesture.Cancelled || !gesture.IsCharging)
            {
                // Cancelled by the player, by blocking, or because the controls went away.
                charging = false;
                commands.Add(new CombatCommand(CombatCommandKind.ChargeCancelled, chargeSlot));
                return;
            }

            if (ChargeInterruptDamage > 0f && input.DamageTaken >= ChargeInterruptDamage)
            {
                BreakCharge(gesture, commands);
            }
        }

        /// <summary>Ends a charge from the game's side and tells the input to let go.</summary>
        private void BreakCharge(in AttackGestureFrame gesture, List<CombatCommand> commands)
        {
            if (charging)
            {
                charging = false;
                commands.Add(new CombatCommand(CombatCommandKind.ChargeCancelled, chargeSlot));
            }

            if (gesture.IsActive && !ignoreGestureUntilIdle)
            {
                commands.Add(new CombatCommand(CombatCommandKind.InterruptInput, chargeSlot));
                ignoreGestureUntilIdle = true;
            }
        }

        private void AbandonSwing(List<CombatCommand> commands)
        {
            meleeEvents.Clear();
            combo.Cancel(meleeEvents);
            TranslateMelee(swingSlot, commands);
        }

        private void TranslateMelee(int slot, List<CombatCommand> commands)
        {
            for (int i = 0; i < meleeEvents.Count; i++)
            {
                MeleeEvent melee = meleeEvents[i];
                switch (melee.Kind)
                {
                    case MeleeEventKind.StepStarted:
                        swingSlot = slot;
                        commands.Add(new CombatCommand(CombatCommandKind.MeleeStepStarted, slot, melee.StepIndex, melee.Step));
                        break;

                    case MeleeEventKind.Hit:
                        // The swing lands as the discipline it started with.
                        commands.Add(new CombatCommand(CombatCommandKind.MeleeHit, swingSlot, melee.StepIndex, melee.Step));
                        break;

                    case MeleeEventKind.Interrupted:
                        commands.Add(new CombatCommand(CombatCommandKind.MeleeInterrupted, swingSlot, melee.StepIndex, melee.Step));
                        break;
                }
            }

            meleeEvents.Clear();
        }

        private void UpdateMovement(bool blockHeld)
        {
            float multiplier = combo.MoveSpeedMultiplier;

            if (charging)
            {
                ChargeProfile charge = data.GetCharge(chargeSlot);
                if (charge != null)
                {
                    multiplier = Mathf.Min(multiplier, charge.moveSpeedMultiplier);
                }
            }

            if (blockHeld)
            {
                multiplier = Mathf.Min(multiplier, BlockMoveSpeedMultiplier);
            }

            MoveSpeedMultiplier = Mathf.Clamp01(multiplier);
            SprintAllowed = !charging && !blockHeld && !combo.IsSwinging;
        }
    }
}
