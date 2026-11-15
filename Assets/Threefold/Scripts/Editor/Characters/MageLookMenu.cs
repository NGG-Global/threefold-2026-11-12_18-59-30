using System.Text;
using Threefold.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Menu commands that switch the Mage between its two looks, so they can be compared in the same scene:
    /// the Wayfarer look (MageWayfarer.fbx) and the library look (the mesh inside Mage.fbx). Movement, combat,
    /// animation clips and effects are the same for both. A look chosen here is kept by later setup runs.
    ///
    /// Each command rebuilds the generated visual prefab in place, then reconnects and restyles the Mage of the
    /// open scene. Other scenes follow the prefab; run their level setup once to reconnect them.
    /// </summary>
    internal static class MageLookMenu
    {
        private const string DialogTitle = "Mage Look";
        private const string WayfarerMenu = "Threefold/Characters/Use Wayfarer Look";
        private const string LibraryMenu = "Threefold/Characters/Use Library Look";

        [MenuItem(WayfarerMenu, false, 60)]
        private static void UseWayfarer()
        {
            Use(MageLook.Wayfarer);
        }

        [MenuItem(LibraryMenu, false, 61)]
        private static void UseLibrary()
        {
            Use(MageLook.Library);
        }

        [MenuItem(WayfarerMenu, true)]
        [MenuItem(LibraryMenu, true)]
        private static bool CanUse()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static void Use(MageLook look)
        {
            var report = new StringBuilder();
            if (MageCharacterSetup.UseLook(look, report))
            {
                Scene scene = SceneManager.GetActiveScene();
                ThirdPersonMotor motor = FindInScene<ThirdPersonMotor>(scene);
                if (motor != null)
                {
                    MageCharacterSetup.Ensure(scene, motor.gameObject, report);
                    VisualStyleSetup.RestyleCharacters(scene, report);
                    EditorSceneManager.MarkSceneDirty(scene);
                }

                AssetDatabase.SaveAssets();
                report.AppendLine("This is a choice by hand: the setup commands keep it. Save the scene to keep the change here.");
            }

            Debug.Log("[Threefold] " + DialogTitle + ":\n" + report);
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(DialogTitle, report.ToString(), "OK");
            }
        }
    }
}
