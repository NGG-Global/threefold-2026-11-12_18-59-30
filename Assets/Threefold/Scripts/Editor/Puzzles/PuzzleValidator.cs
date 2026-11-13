using System;
using System.Collections.Generic;
using Threefold.CombatFeedback;
using Threefold.Interaction;
using Threefold.Puzzles;
using Threefold.TouchControls;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Checks interaction and puzzles as part of the scene validation: the player can interact on
    /// every control scheme, and every puzzle element has the references and colliders it needs.
    /// Puzzle checks run only for the elements the scene contains.
    /// </summary>
    internal static class PuzzleValidator
    {
        internal static void Validate(Scene scene, GameObject mage, Action<string> problem)
        {
            ValidateInteraction(scene, mage, problem);
            ValidatePuzzles(scene, problem);
        }

        private static void ValidateInteraction(Scene scene, GameObject mage, Action<string> problem)
        {
            if (!mage.TryGetComponent(out Interactor interactor))
            {
                problem("The Mage has no interactor, so nothing in the world can be used.");
                return;
            }

            if (interactor.InputSource == null) problem("The Mage's interactor has no input source.");

            TouchControlsInput touch = Find<TouchControlsInput>(scene);
            if (touch != null && touch.InteractButton == null) problem("The touch controls have no interact button.");

            InteractionPrompt prompt = Find<InteractionPrompt>(scene);
            if (prompt == null)
            {
                if (Find<PlayerHud>(scene) != null) problem("There is no interaction prompt on the HUD.");
            }
            else
            {
                if (prompt.Interactor == null) problem("The interaction prompt is not connected to the Mage's interactor.");
                if (touch != null && prompt.TouchControls == null) problem("The interaction prompt does not show the touch interact button.");
            }
        }

        private static void ValidatePuzzles(Scene scene, Action<string> problem)
        {
            List<PuzzleElement> elements = FindAll<PuzzleElement>(scene);
            if (elements.Count == 0)
            {
                return;
            }

            var keys = new Dictionary<string, string>();
            foreach (PuzzleElement element in elements)
            {
                string key = element.PersistenceKey;
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                if (keys.TryGetValue(key, out string other))
                {
                    problem($"'{element.name}' and '{other}' share the persistence key '{key}', so they would overwrite each other's state.");
                }
                else
                {
                    keys.Add(key, element.name);
                }
            }

            foreach (PuzzleSequence sequence in FindAll<PuzzleSequence>(scene))
            {
                if (sequence.StepCount == 0) problem($"The puzzle sequence on '{sequence.name}' has no steps.");
                foreach (SequenceStep step in sequence.Steps)
                {
                    if (step == null || step.element == null) problem($"The puzzle sequence on '{sequence.name}' has an empty step.");
                    else if (step.element == sequence) problem($"The puzzle sequence on '{sequence.name}' lists itself as a step.");
                }
            }

            foreach (PuzzleDoor door in FindAll<PuzzleDoor>(scene))
            {
                if (door.Leaf.target == null) problem($"The door '{door.name}' has no moving leaf.");
                if (door.Control == DoorControl.Input && door.InputElement == null) problem($"The door '{door.name}' has no input, so it never opens.");
            }

            foreach (TimedMechanism timer in FindAll<TimedMechanism>(scene))
            {
                foreach (PuzzleElement target in timer.ResetWhenExpired)
                {
                    if (target == null) problem($"The timer on '{timer.name}' lists an element to reset that no longer exists.");
                }
            }

            foreach (SpellActivator activator in FindAll<SpellActivator>(scene))
            {
                if (activator.AcceptedElements == Combat.SpellElement.None) problem($"The activator '{activator.name}' accepts no kind of magic.");
                if (!HasSolidCollider(activator.gameObject)) problem($"The activator '{activator.name}' has no solid collider, so spells cannot hit it.");
            }

            foreach (PresenceZone zone in FindAll<PresenceZone>(scene))
            {
                Vector3 size = zone.DetectionSize;
                if (size.x <= 0f || size.y <= 0f || size.z <= 0f) problem($"The detection box of '{zone.name}' has no volume.");
            }

            foreach (InteractiveSwitch control in FindAll<InteractiveSwitch>(scene))
            {
                if (string.IsNullOrEmpty(control.Verb)) problem($"The switch '{control.name}' has no verb to show in the prompt.");
            }

            foreach (PuzzleGlow glow in FindAll<PuzzleGlow>(scene))
            {
                if (glow.Source == null) problem($"A glow on '{glow.name}' has no element to show.");
                foreach (Renderer target in glow.Renderers)
                {
                    if (target == null) problem($"A glow on '{glow.name}' lists a renderer that no longer exists.");
                    else if (glow.MaterialSlot >= target.sharedMaterials.Length) problem($"A glow on '{glow.name}' uses material slot {glow.MaterialSlot}, which '{target.name}' does not have.");
                }
            }

            foreach (SequenceIndicator indicator in FindAll<SequenceIndicator>(scene))
            {
                if (indicator.Sequence == null) problem($"The step lamps on '{indicator.name}' show no sequence.");
                else if (indicator.Lamps.Length != indicator.Sequence.StepCount) problem($"The step lamps on '{indicator.name}' do not match the number of steps.");
            }

            foreach (TimerIndicator indicator in FindAll<TimerIndicator>(scene))
            {
                if (indicator.Timer == null) problem($"The timer stones on '{indicator.name}' show no timer.");
            }

            foreach (PuzzleBeam beam in FindAll<PuzzleBeam>(scene))
            {
                if (beam.Source == null || beam.From == null || beam.To == null) problem($"The beam on '{beam.name}' is not fully connected.");
            }

            if (FindAll<PuzzleAudio>(scene).Count > 0 && Find<AudioListener>(scene) == null)
            {
                problem("The scene has puzzle sounds but no Audio Listener.");
            }
        }

        private static bool HasSolidCollider(GameObject target)
        {
            foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.isTrigger)
                {
                    return true;
                }
            }

            return false;
        }

        private static T Find<T>(Scene scene) where T : Component
        {
            return SceneSetupUtility.FindInScene<T>(scene);
        }

        private static List<T> FindAll<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                found.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return found;
        }
    }
}
