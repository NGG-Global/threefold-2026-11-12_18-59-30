using System.Collections.Generic;
using System.Text;
using Threefold.Characters;
using Threefold.CombatAnimation;
using Threefold.Magic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Puts the combat animation director on the Mage and connects it, as part of every level's setup (it runs
    /// after the disciplines are set up). It only adds what is missing and fills empty references; it never
    /// replaces a tuned set or a connected director.
    ///
    /// Two menu commands go with it:
    /// - Threefold > Animation > Apply Combat Animation Set: brings the set's clip lengths and the Animator
    ///   Controller up to date after the set was changed, and connects the director in the open scene.
    /// - Threefold > Animation > Validate Combat Animations: checks every slot against the model's clips, the
    ///   controller's states and the disciplines' timing, and reports what is missing or does not fit.
    /// </summary>
    internal static class CombatAnimationSetup
    {
        private const string DialogTitle = "Combat Animations";
        private const string ApplyMenu = "Threefold/Animation/Apply Combat Animation Set";
        private const string ValidateMenu = "Threefold/Animation/Validate Combat Animations";

        internal static void Ensure(Scene scene, GameObject mage, StringBuilder report)
        {
            if (mage == null)
            {
                return;
            }

            CharacterAnimationDriver driver = mage.GetComponent<CharacterAnimationDriver>();
            if (driver == null || driver.Animator == null)
            {
                report.AppendLine("The Mage has no animated model, so the combat animations were not set up.");
                return;
            }

            DisciplineCombat combat = mage.GetComponent<DisciplineCombat>();
            if (combat == null)
            {
                report.AppendLine("The Mage has no discipline combat, so the combat animations were not set up.");
                return;
            }

            CombatAnimationSet set = MageCharacterSetup.LoadCombatAnimationSet();
            if (set == null)
            {
                set = MageCharacterSetup.EnsureCombatAnimationSet(report);
            }

            if (!mage.TryGetComponent(out CombatAnimationDirector director))
            {
                director = Undo.AddComponent<CombatAnimationDirector>(mage);
                director.Bind(set, driver, combat);
                MarkChanged(director);
                report.AppendLine("Added the combat animation director: stances, three-step combos, charges, block, " +
                                  "stagger, aerial attacks and defeat, timed to the combat rules.");
                return;
            }

            Undo.RecordObject(director, DialogTitle);
            if (director.FillReferences(set, driver, combat))
            {
                MarkChanged(director);
                report.AppendLine("Connected the combat animation director's missing references.");
            }
            else
            {
                report.AppendLine("Kept the existing combat animation director.");
            }
        }

        [MenuItem(ApplyMenu, false, 40)]
        private static void ApplyFromMenu()
        {
            var report = new StringBuilder();
            if (MageCharacterSetup.ApplyCombatAnimations(report))
            {
                Scene scene = SceneManager.GetActiveScene();
                ThirdPersonMotor motor = FindInScene<ThirdPersonMotor>(scene);
                if (motor != null)
                {
                    Ensure(scene, motor.gameObject, report);
                }

                AssetDatabase.SaveAssets();
            }

            Debug.Log("[Threefold] " + DialogTitle + ":\n" + report);
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(DialogTitle, report.Length > 0 ? report.ToString() : "Everything was up to date.", "OK");
            }
        }

        [MenuItem(ValidateMenu, false, 41)]
        private static void ValidateFromMenu()
        {
            var report = new StringBuilder();
            int warnings = Validate(report);
            string summary = warnings == 0
                ? "No problems found. Seeing them play is still needed: see Docs/CombatAnimationArchitecture.md."
                : $"{warnings} warning(s). Details are in the Console.";
            Debug.Log("[Threefold] Combat animation validation:\n" + report);
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(DialogTitle, summary, "OK");
            }
        }

        /// <summary>
        /// Checks the Mage's combat animation set against the model, the Animator Controller and the disciplines.
        /// Writes every finding to the report and returns the number of warnings and errors.
        /// </summary>
        internal static int Validate(StringBuilder report)
        {
            CombatAnimationSet set = MageCharacterSetup.LoadCombatAnimationSet();
            if (set == null)
            {
                report.AppendLine("Error: there is no combat animation set. Run Threefold > Animation > Apply Combat Animation Set.");
                return 1;
            }

            var clips = new Dictionary<string, AnimationClip>();
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(CharacterModelPostprocessor.MageModelPath))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    clips[clip.name] = clip;
                }
            }

            var melee = new Dictionary<MagicDiscipline, MeleeComboProfile>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(DisciplineDefinition)))
            {
                var definition = AssetDatabase.LoadAssetAtPath<DisciplineDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition != null && !melee.ContainsKey(definition.Discipline))
                {
                    melee[definition.Discipline] = definition.Melee;
                }
            }

            var issues = new List<CombatAnimationIssue>();
            CombatAnimationValidation.Validate(set.Library, name =>
            {
                if (string.IsNullOrEmpty(name) || !clips.TryGetValue(name, out AnimationClip clip))
                {
                    return CombatClipInfo.Missing;
                }

                bool loops = AnimationUtility.GetAnimationClipSettings(clip).loopTime;
                return new CombatClipInfo(true, clip.isHumanMotion, loops, clip.length);
            }, discipline => melee.TryGetValue(discipline, out MeleeComboProfile combo) ? combo : DisciplineDefaults.Melee(discipline),
                issues);

            CheckController(set, clips, issues);

            int problems = 0;
            foreach (CombatAnimationIssue issue in issues)
            {
                report.AppendLine(issue.ToString());
                if (issue.Severity != CombatIssueSeverity.Info)
                {
                    problems++;
                }
            }

            if (melee.Count == 0)
            {
                report.AppendLine("Info: no discipline assets were found; the timing was checked against the default numbers.");
            }

            return problems;
        }

        /// <summary>Every assigned slot whose clip exists must have its state on the layers it plays on.</summary>
        private static void CheckController(CombatAnimationSet set, Dictionary<string, AnimationClip> clips,
            List<CombatAnimationIssue> issues)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                "Assets/Threefold/Generated/Characters/Mage/MageAnimator.controller");
            if (controller == null)
            {
                issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Warning,
                    "The Mage's Animator Controller has not been generated yet. Run a level setup or Apply Combat Animation Set."));
                return;
            }

            var layers = new Dictionary<string, HashSet<string>>();
            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                var names = new HashSet<string>();
                foreach (ChildAnimatorState child in layer.stateMachine.states)
                {
                    names.Add(child.state.name);
                }

                layers[layer.name] = names;
            }

            foreach (CombatLayer layer in new[] { CombatLayer.Stance, CombatLayer.FullBody, CombatLayer.UpperBody })
            {
                if (!layers.ContainsKey(CombatAnimatorNames.LayerName(layer)))
                {
                    issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Warning,
                        $"The Animator Controller has no layer '{CombatAnimatorNames.LayerName(layer)}'. Run Apply Combat Animation Set."));
                    return;
                }
            }

            set.Library.ForEachSlot((id, slot) =>
            {
                if (slot == null || !slot.IsAssigned || !clips.ContainsKey(slot.clip))
                {
                    return;
                }

                string layerName = id.Kind == CombatSlotKind.Stance ? CombatAnimatorNames.StanceLayer
                    : id.Kind == CombatSlotKind.Defeat ? CombatAnimatorNames.FullBodyLayer
                    : CombatAnimatorNames.UpperBodyLayer;
                if (!layers[layerName].Contains(id.StateName))
                {
                    issues.Add(new CombatAnimationIssue(CombatIssueSeverity.Warning,
                        $"The Animator Controller has no state '{id.StateName}' on '{layerName}'. Run Apply Combat Animation Set."));
                }
            });
        }
    }
}
