using System.Text;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.Magic;
using UnityEditor;
using UnityEngine;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Gives the Mage its <see cref="CharacterPersonality"/>: the bearing of each discipline and the follow-through
    /// of hair and cloak. Safe to run repeatedly: the component is added only if it is missing, only empty
    /// references are filled, and values tuned in the Inspector are kept. To start again from
    /// <see cref="PersonalityDefaults"/>, remove the component and run the setup.
    /// </summary>
    internal static class MagePersonalitySetup
    {
        private const string DialogTitle = "Mage Personality Setup";

        internal static void Ensure(GameObject mage, GameObject visual, StringBuilder report)
        {
            if (mage == null || visual == null)
            {
                return;
            }

            Animator animator = visual.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                report.AppendLine("WARNING: the Mage's visual has no Animator, so the discipline bearing was not added.");
                return;
            }

            if (!mage.TryGetComponent(out CharacterPersonality personality))
            {
                personality = Undo.AddComponent<CharacterPersonality>(mage);
                report.AppendLine("Added the discipline bearing to the Mage (posture, breathing, hands and looks per " +
                                  "discipline; follow-through for hair and cloak bones).");
            }

            Undo.RecordObject(personality, DialogTitle);
            if (personality.FillReferences(animator, mage.GetComponent<PlayerDisciplines>(), mage.GetComponent<ThirdPersonMotor>(),
                    mage.GetComponent<DisciplineCombat>(), mage.GetComponent<Health>()))
            {
                MarkChanged(personality);
            }

            int secondaryBones = 0;
            foreach (Transform bone in animator.GetComponentsInChildren<Transform>(true))
            {
                if (SecondaryMotionModel.IsSecondary(bone.name))
                {
                    secondaryBones++;
                }
            }

            if (secondaryBones == 0)
            {
                report.AppendLine("The Mage model has no secondary bones (named 'sec_...'), so hair and cloak do not " +
                                  "follow through; the bearing works without them.");
            }
            else if (secondaryBones > SecondaryMotionModel.Capacity)
            {
                report.AppendLine($"WARNING: the Mage model has {secondaryBones} secondary bones; only the first " +
                                  $"{SecondaryMotionModel.Capacity} follow through.");
            }
        }
    }
}
