using System.Text;
using UnityEditor;
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
