using System.Collections.Generic;
using System.Text;
using Threefold.CombatAnimation;
using UnityEditor.Animations;
using UnityEngine;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Adds the combat animation layers to an Animator Controller, from a combat animation set:
    ///
    /// - CombatStance (upper-body mask): each discipline's stance;
    /// - CombatFullBody (no mask): full-body slots, aerial slots and defeat;
    /// - CombatUpperBody (upper-body mask): every action slot, including the full-body ones, which play here
    ///   while moving.
    ///
    /// Each layer starts in an empty state and has no transitions: <see cref="CombatAnimationDirector"/>
    /// cross-fades to the state it wants by name, so no trigger can be left set after an action is cancelled.
    /// A one-shot slot gets a second state ("_B") so the same slot can follow itself with a blend. States
    /// play at the layer's speed parameter, which the director sets to fit each clip to the combat timing; a
    /// held pose plays at speed 0. The layers start at weight 0, so without the director nothing shows.
    /// </summary>
    internal static class CombatAnimatorBuilder
    {
        /// <summary>Adds the parameters and the three layers. Returns the number of states added.</summary>
        internal static int AddCombatLayers(AnimatorController controller, CombatAnimationLibrary library,
            IReadOnlyDictionary<string, AnimationClip> clips, AvatarMask upperBodyMask, StringBuilder report)
        {
            AddSpeedParameter(controller, CombatAnimatorNames.FullBodySpeed);
            AddSpeedParameter(controller, CombatAnimatorNames.UpperBodySpeed);

            AnimatorStateMachine stance = AddLayer(controller, CombatAnimatorNames.StanceLayer, upperBodyMask);
            AnimatorStateMachine full = AddLayer(controller, CombatAnimatorNames.FullBodyLayer, null);
            AnimatorStateMachine upper = AddLayer(controller, CombatAnimatorNames.UpperBodyLayer, upperBodyMask);

            int added = 0;
            var missing = new List<string>();

            library.ForEachSlot((id, slot) =>
            {
                if (slot == null || !slot.IsAssigned)
                {
                    return;
                }

                if (clips == null || !clips.TryGetValue(slot.clip, out AnimationClip clip) || clip == null)
                {
                    missing.Add($"{id.StateName} ('{slot.clip}')");
                    return;
                }

                switch (id.Kind)
                {
                    case CombatSlotKind.Stance:
                        AddState(stance, id.StateName, clip, slot, null);
                        added++;
                        break;

                    case CombatSlotKind.Defeat:
                        AddState(full, id.StateName, clip, slot, CombatAnimatorNames.FullBodySpeed);
                        added++;
                        break;

                    default:
                        added += AddAction(upper, id, clip, slot, CombatAnimatorNames.UpperBodySpeed);
                        if (slot.scope == CombatBodyScope.FullBody)
                        {
                            added += AddAction(full, id, clip, slot, CombatAnimatorNames.FullBodySpeed);
                        }

                        break;
                }
            });

            foreach (string slot in missing)
            {
                report?.AppendLine($"Missing animation: the combat slot {slot} names a clip the Mage model does not contain; " +
                                   "it plays nothing until a clip is assigned.");
            }

            return added;
        }

        /// <summary>
        /// The mask of the combat upper-body layers: head, arms and fingers, and the spine unless the set says
        /// otherwise. Changing an existing mask in place keeps the controller's reference to it.
        /// </summary>
        internal static void ConfigureUpperBodyMask(AvatarMask mask, bool includeTorso)
        {
            for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
            {
                var bodyPart = (AvatarMaskBodyPart)part;
                bool upper = bodyPart == AvatarMaskBodyPart.Head
                             || bodyPart == AvatarMaskBodyPart.LeftArm || bodyPart == AvatarMaskBodyPart.RightArm
                             || bodyPart == AvatarMaskBodyPart.LeftFingers || bodyPart == AvatarMaskBodyPart.RightFingers
                             || (includeTorso && bodyPart == AvatarMaskBodyPart.Body);
                mask.SetHumanoidBodyPartActive(bodyPart, upper);
            }
        }

        private static int AddAction(AnimatorStateMachine machine, CombatSlotId id, AnimationClip clip, CombatAnimationSlot slot,
            string speedParameter)
        {
            AddState(machine, id.StateName, clip, slot, speedParameter);
            if (id.IsHeld)
            {
                return 1;
            }

            AddState(machine, id.TwinStateName, clip, slot, speedParameter);
            return 2;
        }

        private static void AddState(AnimatorStateMachine machine, string name, AnimationClip clip, CombatAnimationSlot slot,
            string speedParameter)
        {
            AnimatorState state = machine.AddState(name);
            state.motion = clip;
            state.mirror = slot.mirror;
            state.writeDefaultValues = true;

            if (slot.holdPose)
            {
                state.speed = 0f;
            }
            else if (speedParameter != null)
            {
                state.speedParameterActive = true;
                state.speedParameter = speedParameter;
            }
        }

        private static AnimatorStateMachine AddLayer(AnimatorController controller, string name, AvatarMask mask)
        {
            controller.AddLayer(name);
            AnimatorControllerLayer[] layers = controller.layers;
            int index = layers.Length - 1;
            layers[index].avatarMask = mask;
            layers[index].defaultWeight = 0f;
            layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            AnimatorStateMachine machine = controller.layers[index].stateMachine;
            machine.defaultState = machine.AddState(CombatAnimatorNames.EmptyState);
            return machine;
        }

        private static void AddSpeedParameter(AnimatorController controller, string name)
        {
            controller.AddParameter(name, AnimatorControllerParameterType.Float);
            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == name)
                {
                    parameters[i].defaultFloat = 1f;
                }
            }

            controller.parameters = parameters;
        }
    }
}
