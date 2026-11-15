using System.Collections.Generic;
using UnityEngine;

namespace Threefold.CombatAnimation
{
    /// <summary>
    /// Carries a <see cref="CombatAnimationModel"/>'s answers out on one Animator: finds the combat layers and speed
    /// parameters of its controller, cross-fades each layer to the state the model asks for (by name, at the clip time
    /// it gives), and sets the layer speeds and weights. Used by <see cref="CombatAnimationDirector"/> for the Mage and
    /// by <see cref="CombatStyleShowcase"/> for the comparison figures, so both play the combat animations the same way.
    ///
    /// The full-body stance layer is optional: a controller generated before it existed still works, with the stance on
    /// the upper body only.
    /// </summary>
    public sealed class CombatAnimatorBinding
    {
        private static readonly int EmptyHash = Animator.StringToHash(CombatAnimatorNames.EmptyState);

        private readonly int[] layerIndex = new int[CombatAnimatorNames.LayerCount];
        private readonly int[] speedParameter = new int[CombatAnimatorNames.LayerCount];
        private readonly int[] appliedVersion = new int[CombatAnimatorNames.LayerCount];
        private readonly Dictionary<CombatSlotId, int> stateHashes = new Dictionary<CombatSlotId, int>(64);
        private readonly Dictionary<CombatSlotId, int> twinHashes = new Dictionary<CombatSlotId, int>(64);

        private CombatAnimationLibrary library;

        /// <summary>The Animator being played, or null when not bound.</summary>
        public Animator Animator { get; private set; }

        public bool IsBound => Animator != null;

        /// <summary>True if the controller has the full-body stance layer.</summary>
        public bool HasFullStance => IsBound && layerIndex[(int)CombatLayer.StanceFull] >= 0;

        /// <summary>
        /// Binds to an Animator whose controller has the combat layers. Returns false, and stays unbound, if it has none
        /// (the problem says why).
        /// </summary>
        public bool Bind(Animator target, CombatAnimationLibrary slots, out string problem)
        {
            Release();
            problem = null;
            if (target == null || slots == null)
            {
                problem = "No Animator or no combat animation set.";
                return false;
            }

            if (target.runtimeAnimatorController == null)
            {
                problem = "The Animator has no controller.";
                return false;
            }

            for (int i = 0; i < CombatAnimatorNames.LayerCount; i++)
            {
                var layer = (CombatLayer)i;
                layerIndex[i] = target.GetLayerIndex(CombatAnimatorNames.LayerName(layer));
                appliedVersion[i] = -1;
                if (layerIndex[i] < 0 && layer != CombatLayer.StanceFull)
                {
                    problem = "The Animator Controller has no combat animation layers. Run Threefold > Animation > Apply " +
                              "Combat Animation Set.";
                    return false;
                }

                string speed = CombatAnimatorNames.SpeedParameter(layer);
                speedParameter[i] = layerIndex[i] >= 0 && speed != null && HasFloat(target, speed) ? Animator.StringToHash(speed) : 0;
            }

            stateHashes.Clear();
            twinHashes.Clear();
            slots.ForEachSlot((id, slot) =>
            {
                if (slot != null && slot.IsAssigned)
                {
                    stateHashes[id] = Animator.StringToHash(id.StateName);
                    twinHashes[id] = Animator.StringToHash(id.TwinStateName);
                }
            });

            library = slots;
            Animator = target;
            return true;
        }

        /// <summary>Sets every combat layer's weight to 0 and unbinds. Safe to call when not bound.</summary>
        public void Release()
        {
            if (Animator != null)
            {
                for (int i = 0; i < CombatAnimatorNames.LayerCount; i++)
                {
                    if (layerIndex[i] >= 0 && layerIndex[i] < Animator.layerCount)
                    {
                        Animator.SetLayerWeight(layerIndex[i], 0f);
                    }
                }
            }

            Animator = null;
            library = null;
        }

        /// <summary>Applies the model's layer states, speeds and weights. Does nothing when not bound.</summary>
        public void Apply(CombatAnimationModel model)
        {
            if (Animator == null || model == null || !Animator.isActiveAndEnabled)
            {
                return;
            }

            for (int i = 0; i < CombatAnimatorNames.LayerCount; i++)
            {
                int index = layerIndex[i];
                if (index < 0)
                {
                    continue;
                }

                var layer = (CombatLayer)i;
                CombatLayerState state = model.GetLayerState(layer);
                if (state.Version != appliedVersion[i])
                {
                    appliedVersion[i] = state.Version;
                    int hash = StateHash(state, index, out float seconds);
                    Animator.CrossFadeInFixedTime(hash, state.BlendSeconds, index, state.StartNormalized * seconds);
                }

                if (speedParameter[i] != 0)
                {
                    Animator.SetFloat(speedParameter[i], model.GetLayerSpeed(layer));
                }

                Animator.SetLayerWeight(index, model.GetLayerWeight(layer));
            }
        }

        /// <summary>The state to cross-fade to: the slot's (or its twin), or Empty if the controller lacks it.</summary>
        private int StateHash(in CombatLayerState state, int index, out float seconds)
        {
            seconds = 0f;
            if (state.Slot.IsNone)
            {
                return EmptyHash;
            }

            CombatAnimationSlot slot = library.GetSlot(state.Slot);
            if (slot == null)
            {
                return EmptyHash;
            }

            seconds = slot.Seconds;
            if (state.Twin && twinHashes.TryGetValue(state.Slot, out int twin) && Animator.HasState(index, twin))
            {
                return twin;
            }

            if (stateHashes.TryGetValue(state.Slot, out int main) && Animator.HasState(index, main))
            {
                return main;
            }

            return EmptyHash;
        }

        private static bool HasFloat(Animator target, string name)
        {
            int id = Animator.StringToHash(name);
            foreach (AnimatorControllerParameter parameter in target.parameters)
            {
                if (parameter.nameHash == id && parameter.type == AnimatorControllerParameterType.Float)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
