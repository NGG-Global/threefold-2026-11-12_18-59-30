using System.Text;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.Discovery;
using Threefold.Magic;
using Threefold.VisualStyle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Adds discovery to a scene: one object named Discovery with the director (clues, resonance and
    /// answers), and the optional sound and haptic hooks, connected to the Mage. It runs as part of
    /// Threefold > Setup > Mage Prototype and the other scene commands, and is available on its own.
    ///
    /// The clue components of the example objects are added to their prefabs by
    /// Threefold > Setup > Magical Objects (see MagicalObjectSetup).
    ///
    /// Safe to run repeatedly: an existing director is kept with its tuned options, and only
    /// connections that are empty are filled.
    /// </summary>
    public static class DiscoverySetup
    {
        private const string MenuPath = "Threefold/Setup/Discovery";
        private const string DialogTitle = "Discovery Setup";
        internal const string HostName = "Discovery";

        [MenuItem(MenuPath, false, 9)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(DialogTitle, "Exit Play mode before running the setup.", "OK");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            ThirdPersonMotor motor = FindInScene<ThirdPersonMotor>(scene);
            if (motor == null)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "The open scene has no Mage yet. Run Threefold > Setup > Mage Prototype first.", "OK");
                return;
            }

            Undo.SetCurrentGroupName(DialogTitle);
            int undoGroup = Undo.GetCurrentGroup();
            var report = new StringBuilder();

            Ensure(scene, motor.gameObject, report);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);

            string summary = report.ToString().TrimEnd();
            Debug.Log($"[Threefold] {DialogTitle} finished.\n{summary}");
            EditorUtility.DisplayDialog(DialogTitle, summary + "\n\nSave the scene with Ctrl+S.", "OK");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>Adds the discovery object if the scene has none and connects it to the given Mage.</summary>
        internal static DiscoveryDirector Ensure(Scene scene, GameObject mage, StringBuilder report)
        {
            DiscoveryDirector director = FindInScene<DiscoveryDirector>(scene);
            if (director == null)
            {
                // An object of its own, not on the Mage: its sound source is moved to wherever a sound comes from.
                var host = new GameObject(HostName);
                SceneManager.MoveGameObjectToScene(host, scene);
                Undo.RegisterCreatedObjectUndo(host, DialogTitle);

                director = host.AddComponent<DiscoveryDirector>();
                host.AddComponent<DiscoveryAudio>().Bind(director);
                host.AddComponent<DiscoveryHaptics>().Bind(director);
                report.AppendLine("Added the discovery director, with placeholder sounds and haptic hooks.");
            }

            ThirdPersonMotor motor = mage.GetComponent<ThirdPersonMotor>();
            Transform view = motor != null ? motor.CameraTransform : null;

            Undo.RecordObject(director, DialogTitle);
            if (director.FillReferences(mage.transform, view, mage.GetComponent<PlayerDisciplines>(),
                    mage.GetComponent<DisciplineCombat>(), mage.GetComponent<PlayerGuard>()))
            {
                MarkChanged(director);
                report.AppendLine("Connected the discovery director to the Mage, the camera and the disciplines.");
            }

            // With the game's quality setting connected, the richness of answers follows the chosen level.
            if (director.FillStyle(FindInScene<VisualStyleController>(scene)))
            {
                MarkChanged(director);
                report.AppendLine("Connected the discovery director to the scene's quality setting.");
            }

            return director;
        }
    }
}
