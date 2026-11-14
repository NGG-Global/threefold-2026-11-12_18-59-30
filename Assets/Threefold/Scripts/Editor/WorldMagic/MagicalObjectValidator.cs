using System;
using System.Collections.Generic;
using System.Text;
using Threefold.Combat;
using Threefold.Persistence;
using Threefold.WorldMagic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Checks that every object meant to respond to magic can do so: it has a definition whose
    /// values make sense, a solid collider for attacks to find, and nothing that waits for a state
    /// the object can never reach. It runs as part of the scene validation, at the end of the
    /// magical object setup, and on demand from the menu, where it also checks every definition
    /// asset in the project.
    ///
    /// The checks on values are in <see cref="MagicalObjectRules"/>, in plain C#, so they are tested
    /// outside Unity; this class adds the checks that need the scene.
    /// </summary>
    public static class MagicalObjectValidator
    {
        private const string MenuPath = "Threefold/Validate/Magical Objects";
        private const string DialogTitle = "Magical Object Validation";

        [MenuItem(MenuPath, false, 41)]
        public static void ValidateFromMenu()
        {
            var problems = new StringBuilder();
            Scene scene = SceneManager.GetActiveScene();
            int count = Validate(scene, problems);

            // A definition used in the scene has been reported with its object; the rest are checked here.
            var used = new HashSet<MagicalObjectDefinition>();
            int objects = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MagicalObject magical in root.GetComponentsInChildren<MagicalObject>(true))
                {
                    objects++;
                    if (magical.Definition != null)
                    {
                        used.Add(magical.Definition);
                    }
                }
            }

            count += ValidateDefinitionAssets(problems, used, out int definitions);

            string message = count == 0
                ? $"No problems found in {objects} magical object(s) in the open scene and {definitions} definition(s) in the project."
                : $"{count} problem(s) found:\n\n{problems.ToString().TrimEnd()}";

            if (count == 0)
            {
                Debug.Log($"[Threefold] {DialogTitle}: {message}");
            }
            else
            {
                Debug.LogError($"[Threefold] {DialogTitle}: {message}");
            }

            EditorUtility.DisplayDialog(DialogTitle, message, "OK");
        }

        /// <summary>Appends one line per problem with the magical objects of a scene.</summary>
        /// <returns>The number of problems found.</returns>
        internal static int Validate(Scene scene, StringBuilder problems)
        {
            int count = 0;
            Validate(scene, text =>
            {
                count++;
                problems.AppendLine("- " + text);
            });
            return count;
        }

        /// <summary>Reports every problem with the magical objects of a scene. Reports nothing for a scene without any.</summary>
        internal static void Validate(Scene scene, Action<string> problem)
        {
            // The values of a definition are reported once, however many objects share it.
            var checkedDefinitions = new HashSet<MagicalObjectDefinition>();

            // Two objects with one identifier would overwrite each other's saved state.
            var ids = new List<string>();
            var owners = new Dictionary<string, List<string>>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MagicalObject magical in root.GetComponentsInChildren<MagicalObject>(true))
                {
                    string id = magical.PersistentId;
                    ids.Add(id);
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    if (!owners.TryGetValue(id, out List<string> names))
                    {
                        names = new List<string>();
                        owners.Add(id, names);
                    }

                    names.Add(magical.name);
                }
            }

            var duplicates = new List<string>();
            PersistentIdRegistry.FindDuplicates(ids, duplicates);
            foreach (string id in duplicates)
            {
                problem($"The persistent id '{id}' is used by {owners[id].Count} objects ('{string.Join("', '", owners[id])}'). " +
                        "Only the first would be saved. Clear the id on the copies and run Threefold > World Magic > " +
                        "Assign Persistent Ids.");
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MagicalObject magical in root.GetComponentsInChildren<MagicalObject>(true))
                {
                    ValidateObject(magical, problem, checkedDefinitions);
                }

                foreach (PropertyStateTrigger trigger in root.GetComponentsInChildren<PropertyStateTrigger>(true))
                {
                    ValidateTrigger(trigger, problem);
                }

                foreach (PropertyStateVisuals visuals in root.GetComponentsInChildren<PropertyStateVisuals>(true))
                {
                    ValidateVisuals(visuals, problem);
                }
            }
        }

        /// <summary>Checks one object: its definition, its colliders and the part that moves.</summary>
        internal static void ValidateObject(MagicalObject magical, Action<string> problem,
            HashSet<MagicalObjectDefinition> checkedDefinitions = null)
        {
            string name = magical.name;
            MagicalObjectDefinition definition = magical.Definition;
            if (definition == null)
            {
                problem($"The magical object '{name}' has no definition, so it responds to nothing.");
            }
            else if (checkedDefinitions == null || checkedDefinitions.Add(definition))
            {
                var found = new List<string>();
                MagicalObjectRules.Validate(definition.Properties, found);
                foreach (string text in found)
                {
                    problem($"The definition '{definition.name}' used by '{name}': {text}");
                }
            }

            bool solid = false;
            foreach (Collider part in magical.GetComponentsInChildren<Collider>(true))
            {
                solid |= !part.isTrigger;
            }

            if (!solid)
            {
                problem($"The magical object '{name}' has no solid collider, so no attack can find it.");
            }

            // A creature answers through its own script; with both, an attack would treat the object as a creature.
            if (magical.GetComponentInParent<Health>(true) != null)
            {
                problem($"The magical object '{name}' is on something with Health. Creatures respond to magic through their " +
                        "own scripts; use one or the other.");
            }

            if (definition != null)
            {
                ValidateWorldState(magical, definition.Properties, problem);
            }

            if (definition != null && definition.Properties.kinetic)
            {
                Transform mover = magical.MoveTarget;
                if (mover.gameObject.isStatic)
                {
                    problem($"The magical object '{name}' can be pushed, but its moving part '{mover.name}' is marked static.");
                }

                if (mover.GetComponentInChildren<Collider>(true) == null)
                {
                    problem($"The magical object '{name}' can be pushed, but its moving part '{mover.name}' carries no " +
                            "collider, so the object would move without what blocks and what is hit.");
                }
            }
        }

        /// <summary>What LM-04 added: the identifier, and where the object's reactions go.</summary>
        private static void ValidateWorldState(MagicalObject magical, MagicalPropertySettings settings, Action<string> problem)
        {
            string name = magical.name;
            if (!string.IsNullOrEmpty(magical.PersistentId) && settings.persisted == PersistedState.None)
            {
                problem($"The magical object '{name}' has a persistent id, but its definition saves nothing. " +
                        "Either tick what is to be kept in the definition, or clear the id.");
            }

            bool usesLinks = false;
            if (settings.emissions != null)
            {
                foreach (ReactionEmission rule in settings.emissions)
                {
                    usesLinks |= rule != null && rule.toLinkedTargets;

                    // With a radius the reaction still reaches something; with links only, it needs them.
                    if (rule != null && rule.toLinkedTargets && rule.radius <= 0f && magical.LinkedTargetCount == 0)
                    {
                        problem($"The magical object '{name}' has a reaction that goes to its linked targets only, but none " +
                                "are listed on the object. It reaches nothing.");
                    }
                }
            }

            for (int i = 0; i < magical.LinkedTargetCount; i++)
            {
                GameObject linked = magical.GetLinkedTarget(i);
                if (linked == null)
                {
                    problem($"The magical object '{name}' lists a linked target that no longer exists.");
                }
                else if (linked.GetComponent<IMagicEffectReceiver>() == null)
                {
                    problem($"The magical object '{name}' is linked to '{linked.name}', which cannot receive magical effects.");
                }
                else if (linked == magical.gameObject)
                {
                    problem($"The magical object '{name}' is linked to itself.");
                }
            }

            if (magical.LinkedTargetCount > 0 && !usesLinks)
            {
                problem($"The magical object '{name}' lists linked targets, but none of its reactions goes to them.");
            }
        }

        private static void ValidateTrigger(PropertyStateTrigger trigger, Action<string> problem)
        {
            MagicalObject source = trigger.Source != null ? trigger.Source : trigger.GetComponent<MagicalObject>();
            if (source == null)
            {
                problem($"The property trigger on '{trigger.name}' watches no magical object.");
                return;
            }

            if (trigger.ActiveWhen == PropertyCondition.None)
            {
                problem($"The property trigger on '{trigger.name}' has no condition, so it never becomes active.");
            }
            else if (source.Definition != null &&
                     (MagicalObjectRules.Reachable(source.Definition.Properties) & trigger.ActiveWhen) == 0)
            {
                problem($"The property trigger on '{trigger.name}' waits for '{trigger.ActiveWhen}', which " +
                        $"'{source.name}' can never reach with its properties.");
            }
        }

        private static void ValidateVisuals(PropertyStateVisuals visuals, Action<string> problem)
        {
            MagicalObject source = visuals.Source != null ? visuals.Source : visuals.GetComponent<MagicalObject>();
            if (source == null)
            {
                problem($"The property visuals on '{visuals.name}' show no magical object.");
                return;
            }

            if (source.Definition == null)
            {
                return;
            }

            PropertyCondition unreachable = visuals.WatchedConditions & ~MagicalObjectRules.Reachable(source.Definition.Properties);
            if (unreachable != PropertyCondition.None)
            {
                problem($"The property visuals on '{visuals.name}' are set up for '{unreachable}', which '{source.name}' " +
                        "can never reach with its properties. Those parts never show.");
            }
        }

        /// <summary>Checks the values of every definition asset in the project.</summary>
        private static int ValidateDefinitionAssets(StringBuilder problems, HashSet<MagicalObjectDefinition> alreadyReported,
            out int definitions)
        {
            int count = 0;
            definitions = 0;
            var found = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(MagicalObjectDefinition)))
            {
                var definition = AssetDatabase.LoadAssetAtPath<MagicalObjectDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition == null)
                {
                    continue;
                }

                definitions++;
                if (alreadyReported.Contains(definition))
                {
                    continue;
                }

                found.Clear();
                MagicalObjectRules.Validate(definition.Properties, found);
                foreach (string text in found)
                {
                    count++;
                    problems.AppendLine($"- The definition '{definition.name}': {text}");
                }
            }

            return count;
        }
    }
}
