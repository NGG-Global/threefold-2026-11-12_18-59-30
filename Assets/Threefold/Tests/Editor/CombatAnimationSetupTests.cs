using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Threefold.Characters;
using Threefold.CombatAnimation;
using Threefold.EditorTools;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.Tests
{
    /// <summary>
    /// The combat animations inside Unity: the model really contains every clip the defaults name, as Humanoid
    /// clips with the right loop setting, and a level's setup gives the Mage a connected director and an
    /// Animator Controller with a state for every slot. These need Unity; outside it they cannot run.
    /// </summary>
    public sealed class CombatAnimationSetupTests
    {
        private static Dictionary<string, AnimationClip> ModelClips()
        {
            return AssetDatabase.LoadAllAssetsAtPath(CharacterModelPostprocessor.MageModelPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__"))
                .GroupBy(clip => clip.name)
                .ToDictionary(group => group.Key, group => group.First());
        }

        [Test]
        public void EveryDefaultCombatClipIsInTheModelAsAHumanoidClip()
        {
            Dictionary<string, AnimationClip> clips = ModelClips();
            if (clips.Count == 0)
            {
                Assert.Ignore("The Mage model is not imported here (are the Git LFS files pulled?).");
            }

            foreach (string name in CombatAnimationDefaults.RequiredClips)
            {
                Assert.IsTrue(clips.TryGetValue(name, out AnimationClip clip), $"'{name}' is in the Mage model");
                Assert.IsTrue(clip.isHumanMotion, $"'{name}' is a Humanoid clip, so it can be retargeted and mirrored");
                Assert.AreEqual(CharacterModelPostprocessor.IsLooping(name), AnimationUtility.GetAnimationClipSettings(clip).loopTime,
                    $"'{name}' loop setting");
            }
        }

        [Test]
        public void ALevelSetupGivesTheMageAConnectedDirectorAndAStateForEverySlot()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("Runs only in batch mode, because it replaces the open scene without saving it.");
            }

            Scene scene = DisciplineRangeSetup.BuildForAutomation();
            GameObject mage = SceneSetupUtility.FindInScene<ThirdPersonMotor>(scene).gameObject;
            var director = mage.GetComponent<CombatAnimationDirector>();
            Assert.IsNotNull(director, "the Mage has a combat animation director");
            Assert.IsNotNull(director.AnimationSet);
            Assert.IsNotNull(director.Driver);
            Assert.IsNotNull(director.Combat);

            var report = new StringBuilder();
            Assert.AreEqual(0, CombatAnimationSetup.Validate(report), report.ToString());

            var controller = director.Driver.Animator.runtimeAnimatorController as AnimatorController;
            Assert.IsNotNull(controller, "the Mage's Animator has the generated controller");

            var layers = controller.layers.ToDictionary(layer => layer.name);
            foreach (CombatLayer layer in new[] { CombatLayer.Stance, CombatLayer.FullBody, CombatLayer.UpperBody })
            {
                Assert.IsTrue(layers.ContainsKey(CombatAnimatorNames.LayerName(layer)), CombatAnimatorNames.LayerName(layer));
            }

            Assert.IsTrue(layers.ContainsKey(CharacterAnimationDriver.CastLayer), "the casting gesture is kept");

            // The director finds states by the hash of their names; the controller's states must answer to them.
            director.AnimationSet.Library.ForEachSlot((id, slot) =>
            {
                if (slot == null || !slot.IsAssigned)
                {
                    return;
                }

                string layerName = id.Kind == CombatSlotKind.Stance ? CombatAnimatorNames.StanceLayer
                    : id.Kind == CombatSlotKind.Defeat ? CombatAnimatorNames.FullBodyLayer
                    : CombatAnimatorNames.UpperBodyLayer;
                int hash = Animator.StringToHash(id.StateName);
                Assert.IsTrue(layers[layerName].stateMachine.states.Any(child => child.state.nameHash == hash),
                    $"{layerName} has {id.StateName}");

                if (!id.IsHeld)
                {
                    int twin = Animator.StringToHash(id.TwinStateName);
                    Assert.IsTrue(layers[layerName].stateMachine.states.Any(child => child.state.nameHash == twin),
                        $"{layerName} has {id.TwinStateName}");
                }

                if (slot.scope == CombatBodyScope.FullBody && id.Kind != CombatSlotKind.Defeat)
                {
                    Assert.IsTrue(layers[CombatAnimatorNames.FullBodyLayer].stateMachine.states.Any(child => child.state.nameHash == hash),
                        $"{CombatAnimatorNames.FullBodyLayer} has {id.StateName}");
                }
            });
        }
    }
}
