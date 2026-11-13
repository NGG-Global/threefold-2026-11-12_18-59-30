using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Threefold.EditorTools
{
    /// <summary>Helpers shared by the scene setup commands.</summary>
    internal static class SceneSetupUtility
    {
        /// <summary>Returns the component on the object, adding it (with Undo) if it is missing.</summary>
        internal static T EnsureComponent<T>(GameObject target, StringBuilder report) where T : Component
        {
            if (target.TryGetComponent(out T component))
            {
                return component;
            }

            component = Undo.AddComponent<T>(target);
            report.AppendLine($"Added {typeof(T).Name}.");
            return component;
        }

        /// <summary>Finds the first component of a type in a scene, including inactive objects.</summary>
        internal static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>Finds the first object with a given name in a scene, including inactive objects.</summary>
        internal static GameObject FindByName(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == objectName)
                    {
                        return child.gameObject;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Makes the scene at <paramref name="path"/> the open scene: keeps it if it is already open,
        /// opens it if it exists, or creates it. The user is first offered to save other open work;
        /// nothing is discarded silently.
        /// </summary>
        internal static bool TryOpenOrCreateScene(string path, string dialogTitle, out Scene scene)
        {
            scene = SceneManager.GetActiveScene();
            if (scene.path == path)
            {
                return true;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                return scene.IsValid();
            }

            CreateFolders(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (!EditorSceneManager.SaveScene(scene, path))
            {
                EditorUtility.DisplayDialog(dialogTitle, $"The scene could not be created at '{path}'.", "OK");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Opens or creates the scene without asking anything, discarding unsaved changes in the
        /// open scene. Only for the command line and build servers, where nobody has unsaved work.
        /// </summary>
        internal static Scene OpenOrCreateSceneForAutomation(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
            {
                return EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            }

            CreateFolders(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, path);
            return scene;
        }

        /// <summary>Creates a folder under Assets, and its parents, if missing.</summary>
        internal static void CreateFolders(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            CreateFolders(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        /// <summary>Tells Unity that an object was changed by code, so the change is saved.</summary>
        internal static void MarkChanged(Object changed)
        {
            EditorUtility.SetDirty(changed);

            if (PrefabUtility.IsPartOfPrefabInstance(changed))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(changed);
            }
        }
    }
}
