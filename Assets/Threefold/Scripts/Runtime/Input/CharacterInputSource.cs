using UnityEngine;

namespace Threefold.InputHandling
{
    /// <summary>
    /// Device-independent description of what the player wants the character to do.
    /// Gameplay code (movement, camera, later spellcasting) reads only this type, so a new
    /// control scheme such as touch is added by writing another subclass, not by changing gameplay.
    /// </summary>
    public abstract class CharacterInputSource : MonoBehaviour
    {
        /// <summary>Desired movement on the ground plane. X is right, Y is forward. Length is 0 to 1.</summary>
        public abstract Vector2 Move { get; }

        /// <summary>
        /// Camera look input for the current frame, in look units (one unit is one pixel of mouse travel).
        /// Positive X turns right, positive Y looks up. Already frame-rate independent.
        /// </summary>
        public abstract Vector2 LookDelta { get; }

        /// <summary>True while the sprint control is held.</summary>
        public abstract bool SprintHeld { get; }

        /// <summary>True while the jump control is held. Used for variable jump height.</summary>
        public abstract bool JumpHeld { get; }

        /// <summary>True only on the frame the jump control was pressed.</summary>
        public abstract bool JumpPressedThisFrame { get; }

        /// <summary>True while the primary attack control is held.</summary>
        public abstract bool AttackHeld { get; }

        /// <summary>True only on the frame the primary attack control was pressed.</summary>
        public abstract bool AttackPressedThisFrame { get; }

        /// <summary>
        /// The attack control read as a gesture: tap, charge, aim, release or cancel. Gameplay should
        /// react to this rather than to <see cref="AttackHeld"/>, so a tap and a charge are never
        /// confused. Sources without gesture support report Idle.
        /// </summary>
        public virtual AttackGestureFrame AttackGesture => default;

        /// <summary>True while the block control is held.</summary>
        public virtual bool BlockHeld => false;

        /// <summary>True only on the frame the block control was pressed.</summary>
        public virtual bool BlockPressedThisFrame => false;

        /// <summary>True only on the frame the block control was let go (or its touch was cancelled).</summary>
        public virtual bool BlockReleasedThisFrame => false;

        /// <summary>
        /// The discipline slot the player chose this frame (0 is the first), or -1. Touch reports the
        /// selector, the keyboard its number keys.
        /// </summary>
        public virtual int DisciplineSlotRequested => -1;

        /// <summary>-1 or +1 on the frame the player stepped to the previous or next discipline, otherwise 0.</summary>
        public virtual int DisciplineCycleRequested => 0;

        /// <summary>
        /// True only on the frame the interact control was pressed: open a door, pull a lever.
        /// Sources without an interact control report false.
        /// </summary>
        public virtual bool InteractPressedThisFrame => false;
    }
}
