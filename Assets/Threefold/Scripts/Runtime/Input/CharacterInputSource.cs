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
        /// True only on the frame the interact control was pressed: open a door, pull a lever.
        /// Sources without an interact control report false.
        /// </summary>
        public virtual bool InteractPressedThisFrame => false;
    }
}
