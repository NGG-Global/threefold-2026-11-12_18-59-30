using System;
using System.Globalization;
using System.IO;
using System.Text;
using Threefold.CombatAnimation;
using Threefold.Magic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using L = Threefold.EditorTools.ReferenceCourtyardLayout;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Draws one strike as a strip of frames, the way the game plays it: the combat animation model decides
    /// what plays and how fast, the binding hands that to the Mage's own Animator Controller, and the Animator
    /// is advanced a thirtieth of a second at a time and drawn. It is the closest thing to footage that can be
    /// made without a person at the Editor or a device.
    ///
    /// What a strip shows: the real controller, layers, cross-fades and time-warp of a strike, frame by frame,
    /// once with the slot as it was before a change and once as it is. What it does not show: effects, the
    /// enemy, the bearing and the swing of cloak and hair, the camera, or anything on a device. Thirty frames a
    /// second is half the game's target; fast moments fall between frames.
    /// </summary>
    internal static class StrikeSequenceCapture
    {
        private const int FrameWidth = 320;
        private const int FrameHeight = 400;
        private const int Columns = 13;
        private const int Rows = 2;
        private const float Step = 1f / 30f;
        private const float SettleSeconds = 0.6f;

        /// <summary>Captures the Flame hook before and after the polish pass. Returns lines for the metrics file.</summary>
        internal static string CaptureHook(Scene scene, Camera view, string folder)
        {
            var log = new StringBuilder();
            Animator animator = FindMageAnimator(scene);
            CombatAnimationSet set = MageCharacterSetup.LoadCombatAnimationSet();
            if (animator == null || set == null)
            {
                return "strike sequence: not captured (no Mage Animator or no combat animation set)\n";
            }

            const int hook = 1;
            MeleeStepProfile rules = DisciplineDefaults.Melee(MagicDiscipline.Flame).steps[hook];
            CombatAnimationSlot slot = set.Library.GetDiscipline(MagicDiscipline.Flame).attacks[hook];
            float followThrough = slot.followThroughSpeed;
            float blendIn = slot.blendInSeconds;

            try
            {
                // Before: the slot as the first version generated it. Changed in memory only; the asset is not saved.
                slot.followThroughSpeed = 0f;
                slot.blendInSeconds = 0.06f;
                log.Append(Capture(animator, set, view, hook, rules, Path.Combine(folder, "HookSequence-1-before.png"), "before"));

                slot.followThroughSpeed = followThrough;
                slot.blendInSeconds = blendIn;
                log.Append(Capture(animator, set, view, hook, rules, Path.Combine(folder, "HookSequence-2-after.png"), "after"));
            }
            finally
            {
                slot.followThroughSpeed = followThrough;
                slot.blendInSeconds = blendIn;
            }

            return log.ToString();
        }

        private static string Capture(Animator animator, CombatAnimationSet set, Camera view, int step, MeleeStepProfile rules,
            string path, string label)
        {
            var binding = new CombatAnimatorBinding();
            if (!binding.Bind(animator, set.Library, out string problem))
            {
                return $"strike sequence {label}: not captured ({problem})\n";
            }

            var target = new RenderTexture(FrameWidth, FrameHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var frame = new Texture2D(FrameWidth, FrameHeight, TextureFormat.RGB24, false);
            var strip = new Texture2D(FrameWidth * Columns, FrameHeight * Rows, TextureFormat.RGB24, false);
            RenderTexture activeBefore = RenderTexture.active;
            RenderTexture targetBefore = view.targetTexture;
            var body = new CombatBodyState(true, 0f);
            var notes = new StringBuilder();

            try
            {
                foreach (SkinnedMeshRenderer skinned in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skinned.forceMatrixRecalculationPerRender = true;
                    skinned.updateWhenOffscreen = true;
                }

                // From the side, a little in front, at the height of the chest.
                Vector3 mage = animator.transform.position;
                view.targetTexture = target;
                view.fieldOfView = 40f;
                view.transform.SetPositionAndRotation(mage + new Vector3(3.4f, 1.25f, 1.2f),
                    Quaternion.LookRotation((mage + Vector3.up * 0.95f) - (mage + new Vector3(3.4f, 1.25f, 1.2f)), Vector3.up));

                animator.Rebind();
                animator.Update(0f);

                var model = new CombatAnimationModel(set.Library);
                model.SetDiscipline(MagicDiscipline.Flame);

                // Let the stance come in before the strike.
                float now = 0f;
                for (; now < SettleSeconds; now += Step)
                {
                    Advance(model, binding, animator, now, body);
                }

                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Vector3 handAtStart = hand != null ? hand.position : Vector3.zero;
                float farthest = 0f;

                float start = now;
                model.SwingStarted(MagicDiscipline.Flame, step, rules.startupSeconds, rules.recoverySeconds, start);
                bool landed = false;

                for (int index = 0; index < Columns * Rows; index++)
                {
                    if (!landed && now >= start + rules.startupSeconds - 1e-4f)
                    {
                        model.SwingLanded(start + rules.startupSeconds);
                        landed = true;
                    }

                    Advance(model, binding, animator, now, body);
                    view.Render();

                    RenderTexture.active = target;
                    frame.ReadPixels(new Rect(0, 0, FrameWidth, FrameHeight), 0, 0);
                    int column = index % Columns;
                    int row = Rows - 1 - index / Columns;   // the first row is the top one
                    strip.SetPixels(column * FrameWidth, row * FrameHeight, FrameWidth, FrameHeight, frame.GetPixels());

                    if (hand != null)
                    {
                        farthest = Mathf.Max(farthest, Vector3.Distance(hand.position, handAtStart));
                    }

                    CombatLayerState state = model.GetLayerState(CombatLayer.FullBody);
                    notes.Append(string.Format(CultureInfo.InvariantCulture, "  frame {0,2} at {1,5:0.000} s: {2}, speed x{3:0.00}, weight {4:0.00}\n",
                        index + 1, now - start, state.Slot.IsNone ? "stance" : "hook", model.GetLayerSpeed(CombatLayer.FullBody),
                        model.GetLayerWeight(CombatLayer.FullBody)));
                    model.Cues.Clear();
                    now += Step;
                }

                strip.Apply();
                File.WriteAllBytes(path, strip.EncodeToPNG());

                // If the Animator did nothing outside Play mode, the hand never left its place: say so plainly.
                string moved = farthest > 0.15f
                    ? string.Format(CultureInfo.InvariantCulture, "the right hand travelled up to {0:0.00} m from where it began", farthest)
                    : "THE HAND DID NOT MOVE: the Animator was not advanced outside Play mode, and the strip shows no strike";
                return $"strike sequence {label}: {Columns * Rows} frames at 30 a second written to {Path.GetFileName(path)}; {moved}\n{notes}";
            }
            catch (Exception exception)
            {
                return $"strike sequence {label}: failed ({exception.Message})\n";
            }
            finally
            {
                binding.Release();
                RenderTexture.active = activeBefore;
                view.targetTexture = targetBefore;
                UnityEngine.Object.DestroyImmediate(frame);
                UnityEngine.Object.DestroyImmediate(strip);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void Advance(CombatAnimationModel model, CombatAnimatorBinding binding, Animator animator, float now,
            CombatBodyState body)
        {
            model.Tick(now, body);
            binding.Apply(model);
            animator.Update(Step);
        }

        private static Animator FindMageAnimator(Scene scene)
        {
            Animator found = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != "Mage")
                {
                    continue;
                }

                foreach (Animator candidate in root.GetComponentsInChildren<Animator>(true))
                {
                    if (candidate.avatar != null && candidate.avatar.isHuman && candidate.runtimeAnimatorController != null)
                    {
                        found = candidate;
                    }
                }
            }

            return found;
        }
    }
}
