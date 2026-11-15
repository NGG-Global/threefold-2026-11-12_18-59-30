using System.Text;
using Threefold.CombatAnimation;
using Threefold.Magic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Adds the martial styles comparison to a scene (Docs/MartialStyles.md): three figures on low plinths behind the
    /// start of the Discipline Range, Arcane, Flame and Aegis from left to right as seen from the start, each looping its
    /// discipline's whole combo with the real combat timing (<see cref="CombatStyleShowcase"/>), turned side-on so the
    /// strikes read in profile, with a post in front of each that jolts at every contact. Every third round plays in slow
    /// motion.
    ///
    /// Safe to run repeatedly: it adds only what is missing, fills only empty references, keeps everything that exists
    /// (moved or tuned), registers Undo and marks the scene changed. The Discipline Range setup runs it.
    /// </summary>
    internal static class MartialStylesShowcaseSetup
    {
        private const string MenuPath = "Threefold/Setup/Martial Styles Showcase";
        private const string DialogTitle = "Martial Styles Showcase";
        internal const string RootName = "MartialStylesShowcase";

        /// <summary>Where the comparison stands in the Discipline Range: behind and to the left of the start.</summary>
        internal static readonly Vector3 RangeOrigin = new Vector3(-7.5f, 0f, -13.5f);

        /// <summary>Distance between the figures, and from each figure to its post, in metres.</summary>
        internal const float Spacing = 4f;
        internal const float TargetDistance = 1.15f;

        private const float FacingYaw = 90f;
        private static readonly Vector3 PlinthSize = new Vector3(1.5f, 0.12f, 1.5f);

        [MenuItem(MenuPath, false, 9)]
        private static void RunFromMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            var report = new StringBuilder();
            GameObject mage = SceneSetupUtility.FindInScene<Threefold.Characters.ThirdPersonMotor>(scene)?.gameObject;
            int added = Ensure(scene, mage, RangeOrigin, report);
            if (added > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            EditorUtility.DisplayDialog(DialogTitle, report.ToString().TrimEnd() + "\n\nSave the scene to keep the changes.", "OK");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>
        /// Adds the comparison to a scene, or completes it. <paramref name="mage"/> is optional: its disciplines give the
        /// figures their discipline assets, so the figures use the tuned timing. Returns the number of objects added or
        /// references filled.
        /// </summary>
        internal static int Ensure(Scene scene, GameObject mage, Vector3 origin, StringBuilder report)
        {
            var prefab = MageCharacterSetup.LoadVisualPrefab();
            CombatAnimationSet set = MageCharacterSetup.LoadCombatAnimationSet();
            if (prefab == null || set == null)
            {
                report.AppendLine("Skipped the martial styles comparison: the Mage's generated visual or combat animation set is " +
                                  "missing. Run the Mage setup first (any Threefold > Setup level command does it).");
                return 0;
            }

            EnvironmentAssets assets = EnvironmentAssets.Ensure(false, report);
            var disciplines = mage != null ? mage.GetComponent<PlayerDisciplines>() : null;
            int changes = 0;

            GameObject root = SceneSetupUtility.FindByName(scene, RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = origin;
                Undo.RegisterCreatedObjectUndo(root, DialogTitle);
                changes++;
            }

            for (int i = 0; i < DisciplineCatalog.Count; i++)
            {
                var discipline = (MagicDiscipline)i;
                // Left to right as seen from the start of the range, looking back towards the figures.
                float x = (1 - i) * Spacing;
                changes += EnsureStation(root.transform, discipline, new Vector3(x, 0f, 0f), prefab, set,
                    disciplines != null ? disciplines.GetDefinition(i) : null, assets);
            }

            report.AppendLine(changes > 0
                ? $"Martial styles comparison: added or connected {changes} item(s) (three figures looping their combos)."
                : "Kept the existing martial styles comparison.");
            return changes;
        }

        private static int EnsureStation(Transform root, MagicDiscipline discipline, Vector3 position, GameObject prefab,
            CombatAnimationSet set, DisciplineDefinition definition, EnvironmentAssets assets)
        {
            int changes = 0;
            string name = discipline + "Style";
            Transform station = root.Find(name);
            if (station == null)
            {
                var created = new GameObject(name);
                created.transform.SetParent(root, false);
                created.transform.localPosition = position;
                created.transform.localRotation = Quaternion.Euler(0f, FacingYaw, 0f);
                Undo.RegisterCreatedObjectUndo(created, DialogTitle);
                station = created.transform;
                changes++;
            }

            Transform plinth = station.Find("Plinth");
            if (plinth == null)
            {
                GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "Plinth";
                block.transform.SetParent(station, false);
                block.transform.localPosition = new Vector3(0f, PlinthSize.y * 0.5f, 0f);
                block.transform.localScale = PlinthSize;
                block.GetComponent<Renderer>().sharedMaterial = assets.DarkStoneMaterial;
                Undo.RegisterCreatedObjectUndo(block, DialogTitle);
                plinth = block.transform;
                changes++;
            }

            // A strip of the discipline's colour along the plinth's front edge (tinted at run time).
            Transform marker = station.Find("Marker");
            if (marker == null)
            {
                GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "Marker";
                Object.DestroyImmediate(strip.GetComponent<Collider>());
                strip.transform.SetParent(station, false);
                strip.transform.localPosition = new Vector3(-PlinthSize.x * 0.5f - 0.02f, PlinthSize.y * 0.5f, 0f);
                strip.transform.localScale = new Vector3(0.04f, PlinthSize.y * 0.8f, PlinthSize.z * 0.9f);
                strip.GetComponent<Renderer>().sharedMaterial = assets.StoneMaterial;
                Undo.RegisterCreatedObjectUndo(strip, DialogTitle);
                marker = strip.transform;
                changes++;
            }

            // The post the strikes land on: no collider, so it never blocks the camera or the Mage.
            Transform target = station.Find("Target");
            if (target == null)
            {
                GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = "Target";
                Object.DestroyImmediate(post.GetComponent<Collider>());
                post.transform.SetParent(station, false);
                post.transform.localPosition = new Vector3(0f, PlinthSize.y + 0.8f, TargetDistance);
                post.transform.localScale = new Vector3(0.22f, 0.8f, 0.22f);
                post.GetComponent<Renderer>().sharedMaterial = assets.StoneMaterial;
                Undo.RegisterCreatedObjectUndo(post, DialogTitle);
                target = post.transform;
                changes++;
            }

            Transform figure = station.Find("Figure");
            if (figure == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, station);
                instance.name = "Figure";
                instance.transform.localPosition = new Vector3(0f, PlinthSize.y, 0f);
                instance.transform.localRotation = Quaternion.identity;
                Undo.RegisterCreatedObjectUndo(instance, DialogTitle);
                figure = instance.transform;
                changes++;
            }

            var showcase = station.GetComponent<CombatStyleShowcase>();
            if (showcase == null)
            {
                showcase = Undo.AddComponent<CombatStyleShowcase>(station.gameObject);
                changes++;
            }

            Undo.RecordObject(showcase, DialogTitle);
            if (showcase.FillReferences(discipline, definition, set, figure.GetComponentInChildren<Animator>(true), target,
                    marker.GetComponent<Renderer>()))
            {
                EditorUtility.SetDirty(showcase);
                changes++;
            }

            return changes;
        }
    }
}
