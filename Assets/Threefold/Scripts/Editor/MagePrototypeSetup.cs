using System.Text;
using Threefold.Cameras;
using Threefold.Characters;
using Threefold.InputHandling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Menu command that prepares a scene for the Mage movement prototype. It only adds what is
    /// missing and only fills references that are empty, so it can be run repeatedly and never
    /// replaces existing objects or values tuned in the Inspector. Every change can be undone.
    /// </summary>
    public static class MagePrototypeSetup
    {
        private const string MenuPath = "Threefold/Setup/Mage Prototype";
        private const string DialogTitle = "Mage Prototype Setup";
        private const string ScenePath = "Assets/Scenes/MagePrototype.unity";
        private const string ActionsAssetPath = "Assets/InputSystem_Actions.inputactions";

        private const string MageName = "Mage";
        private const string MageVisualName = "Visual";
        private const string GroundName = "PrototypeGround";
        private const string ObstaclesName = "PrototypeObstacles";
        private const string CameraName = "Main Camera";
        private const string LightName = "Directional Light";
        private const string MainCameraTag = "MainCamera";

        private const float CapsuleHeight = 1.8f;
        private const float CapsuleRadius = 0.35f;

        [MenuItem(MenuPath, false, 0)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(DialogTitle, "Exit Play mode before running the setup.", "OK");
                return;
            }

            if (!TryGetTargetScene(out Scene scene))
            {
                return;
            }

            Undo.SetCurrentGroupName(DialogTitle);
            int undoGroup = Undo.GetCurrentGroup();
            var report = new StringBuilder();

            GameObject ground = EnsureGround(scene, report);
            Vector3 origin = ground.transform.position;

            EnsureObstacles(scene, origin, report);
            EnsureDirectionalLight(scene, report);

            GameObject mage = EnsureCharacterRig(scene, origin, Quaternion.identity, null, report);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = mage;

            Finish(scene, report);
        }

        /// <summary>
        /// Adds everything that makes a scene playable, wherever its environment came from: the Mage,
        /// the camera, input, touch controls, combat, HUD and training dummies. Existing parts are kept.
        /// </summary>
        /// <param name="spawnPosition">Where a newly created Mage is placed.</param>
        /// <param name="spawnRotation">Which way a newly created Mage faces.</param>
        /// <param name="dummies">Where to place training dummies, or null for the default positions.</param>
        internal static GameObject EnsureCharacterRig(Scene scene, Vector3 spawnPosition, Quaternion spawnRotation,
            CombatSetup.DummyPlacement[] dummies, StringBuilder report)
        {
            GameObject mage = EnsureMage(scene, spawnPosition, spawnRotation, report);
            ThirdPersonMotor motor = EnsureComponent<ThirdPersonMotor>(mage, report);
            CharacterInputSource input = EnsureInput(mage, motor, report);
            ThirdPersonCamera cameraRig = EnsureCamera(scene, report);

            WireReferences(mage, motor, input, cameraRig, report);
            TouchControlsSetup.Ensure(scene, mage, report);
            CombatSetup.Ensure(scene, mage, report, dummies);
            return mage;
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        // ---------------------------------------------------------------- scene

        private static bool TryGetTargetScene(out Scene scene)
        {
            scene = SceneManager.GetActiveScene();
            if (scene.path == ScenePath)
            {
                return true;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                // Gives the user the chance to save or cancel; nothing open is discarded silently.
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return false;
                }

                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                return scene.IsValid();
            }

            string openSceneName = string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name;
            return EditorUtility.DisplayDialog(
                DialogTitle,
                $"The scene '{ScenePath}' does not exist in this project.\n\n" +
                $"Apply the Mage prototype setup to the scene that is open now ('{openSceneName}')? " +
                "Existing objects are kept; only missing ones are added.",
                "Apply to open scene",
                "Cancel");
        }

        // ---------------------------------------------------------- environment

        private static GameObject EnsureGround(Scene scene, StringBuilder report)
        {
            GameObject ground = FindByName(scene, GroundName);
            if (ground != null)
            {
                if (ground.GetComponentInChildren<Collider>() == null)
                {
                    report.AppendLine($"WARNING: '{GroundName}' has no collider, so the Mage will fall through it. " +
                                      "Add a collider to it.");
                }
                else
                {
                    report.AppendLine($"Kept existing '{GroundName}'.");
                }

                return ground;
            }

            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = GroundName;
            ground.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            ground.transform.localScale = new Vector3(5f, 1f, 5f);
            Undo.RegisterCreatedObjectUndo(ground, DialogTitle);
            report.AppendLine($"Created '{GroundName}'.");
            return ground;
        }

        private static void EnsureObstacles(Scene scene, Vector3 origin, StringBuilder report)
        {
            if (FindByName(scene, ObstaclesName) != null)
            {
                report.AppendLine($"Kept existing '{ObstaclesName}'.");
                return;
            }

            var root = new GameObject(ObstaclesName);
            root.transform.position = origin;

            // Sizes are chosen against the default jump height of 1.5 m.
            CreateBox(root.transform, "LowStep", new Vector3(4f, 0.25f, 4f), new Vector3(2f, 0.5f, 2f), Quaternion.identity);
            CreateBox(root.transform, "JumpBox", new Vector3(7f, 0.6f, 4f), new Vector3(2f, 1.2f, 2f), Quaternion.identity);
            CreateBox(root.transform, "Wall", new Vector3(0f, 1.5f, 10f), new Vector3(8f, 3f, 0.5f), Quaternion.identity);
            CreateBox(root.transform, "Ramp", new Vector3(-5f, 0.9f, 5f), new Vector3(3f, 0.2f, 6f), Quaternion.Euler(-20f, 0f, 0f));

            Undo.RegisterCreatedObjectUndo(root, DialogTitle);
            report.AppendLine($"Created '{ObstaclesName}' (step, jump box, wall and ramp for testing).");
        }

        private static void CreateBox(Transform parent, string name, Vector3 localPosition, Vector3 size, Quaternion rotation)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localRotation = rotation;
            box.transform.localScale = size;
        }

        private static void EnsureDirectionalLight(Scene scene, StringBuilder report)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Light existing in root.GetComponentsInChildren<Light>(true))
                {
                    if (existing.type == LightType.Directional)
                    {
                        return;
                    }
                }
            }

            var lightObject = new GameObject(LightName);
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            Undo.RegisterCreatedObjectUndo(lightObject, DialogTitle);
            report.AppendLine($"Created '{LightName}' because the scene had no directional light.");
        }

        // ----------------------------------------------------------------- mage

        private static GameObject EnsureMage(Scene scene, Vector3 origin, Quaternion rotation, StringBuilder report)
        {
            ThirdPersonMotor existingMotor = FindInScene<ThirdPersonMotor>(scene);
            GameObject mage = existingMotor != null ? existingMotor.gameObject : FindByName(scene, MageName);

            if (mage == null)
            {
                mage = new GameObject(MageName);
                mage.transform.SetPositionAndRotation(origin + Vector3.up * 0.1f, rotation);
                CreatePlaceholderVisual(mage.transform);
                Undo.RegisterCreatedObjectUndo(mage, DialogTitle);
                report.AppendLine($"Created '{MageName}' with a placeholder capsule body.");
            }
            else
            {
                report.AppendLine($"Kept existing '{mage.name}'.");
            }

            if (!mage.TryGetComponent(out CharacterController controller))
            {
                controller = Undo.AddComponent<CharacterController>(mage);
                controller.height = CapsuleHeight;
                controller.radius = CapsuleRadius;
                controller.center = new Vector3(0f, CapsuleHeight * 0.5f, 0f);
                controller.slopeLimit = 45f;
                controller.stepOffset = 0.3f;
                controller.skinWidth = 0.04f;
                controller.minMoveDistance = 0f;
                report.AppendLine("Added and configured CharacterController (1.8 m tall, pivot at the feet).");
            }

            foreach (Collider collider in mage.GetComponentsInChildren<Collider>(true))
            {
                if (!(collider is CharacterController))
                {
                    report.AppendLine($"WARNING: '{collider.name}' under '{mage.name}' has an extra " +
                                      $"{collider.GetType().Name}. Remove it; the CharacterController is the " +
                                      "character's only collider.");
                }
            }

            return mage;
        }

        /// <summary>A capsule body with a small block on the front so the facing direction is visible.</summary>
        private static void CreatePlaceholderVisual(Transform mage)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = MageVisualName;
            RemoveCollider(body);
            body.transform.SetParent(mage, false);
            body.transform.localPosition = new Vector3(0f, CapsuleHeight * 0.5f, 0f);
            body.transform.localScale = new Vector3(CapsuleRadius * 2f, CapsuleHeight * 0.5f, CapsuleRadius * 2f);

            GameObject facing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            facing.name = "FacingIndicator";
            RemoveCollider(facing);
            facing.transform.SetParent(mage, false);
            facing.transform.localPosition = new Vector3(0f, 1.45f, CapsuleRadius);
            facing.transform.localScale = new Vector3(0.2f, 0.12f, 0.3f);
        }

        private static void RemoveCollider(GameObject primitive)
        {
            if (primitive.TryGetComponent(out Collider collider))
            {
                Object.DestroyImmediate(collider);
            }
        }

        private static CharacterInputSource EnsureInput(GameObject mage, ThirdPersonMotor motor, StringBuilder report)
        {
            CharacterInputSource input = motor.InputSource;
            if (input == null)
            {
                input = mage.GetComponent<CharacterInputSource>();
            }

            if (input == null)
            {
                input = Undo.AddComponent<InputSystemCharacterInput>(mage);
                report.AppendLine($"Added {nameof(InputSystemCharacterInput)}.");
            }

            if (input is InputSystemCharacterInput inputSystemInput && inputSystemInput.ActionsAsset == null)
            {
                InputActionAsset actions = FindActionsAsset();
                if (actions != null)
                {
                    Undo.RecordObject(inputSystemInput, DialogTitle);
                    inputSystemInput.ActionsAsset = actions;
                    MarkChanged(inputSystemInput);
                    report.AppendLine($"Assigned input actions asset '{AssetDatabase.GetAssetPath(actions)}'.");
                }
                else
                {
                    report.AppendLine("WARNING: No input actions asset was found. Assign one to " +
                                      $"{nameof(InputSystemCharacterInput)} on '{mage.name}'.");
                }
            }

            return input;
        }

        private static InputActionAsset FindActionsAsset()
        {
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsAssetPath);
            if (actions != null)
            {
                return actions;
            }

            actions = InputSystem.actions;
            if (actions != null)
            {
                return actions;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:InputActionAsset"))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && candidate.FindActionMap("Player", false) != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        // --------------------------------------------------------------- camera

        private static ThirdPersonCamera EnsureCamera(Scene scene, StringBuilder report)
        {
            ThirdPersonCamera rig = FindInScene<ThirdPersonCamera>(scene);
            if (rig != null)
            {
                report.AppendLine($"Kept existing third-person camera on '{rig.name}'.");
                return rig;
            }

            GameObject cameraObject = FindMainCamera(scene);
            if (cameraObject == null)
            {
                cameraObject = new GameObject(CameraName);
                cameraObject.tag = MainCameraTag;
                cameraObject.AddComponent<Camera>();

                if (FindInScene<AudioListener>(scene) == null)
                {
                    cameraObject.AddComponent<AudioListener>();
                }

                Undo.RegisterCreatedObjectUndo(cameraObject, DialogTitle);
                report.AppendLine($"Created '{CameraName}'.");
            }

            rig = Undo.AddComponent<ThirdPersonCamera>(cameraObject);
            report.AppendLine($"Added {nameof(ThirdPersonCamera)} to '{cameraObject.name}'.");
            return rig;
        }

        private static GameObject FindMainCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.CompareTag(MainCameraTag))
                    {
                        return camera.gameObject;
                    }
                }
            }

            return null;
        }

        // ----------------------------------------------------------- references

        private static void WireReferences(GameObject mage, ThirdPersonMotor motor, CharacterInputSource input,
            ThirdPersonCamera cameraRig, StringBuilder report)
        {
            if (motor.InputSource == null || motor.CameraTransform == null)
            {
                Undo.RecordObject(motor, DialogTitle);

                if (motor.InputSource == null)
                {
                    motor.InputSource = input;
                }

                if (motor.CameraTransform == null)
                {
                    motor.CameraTransform = cameraRig.transform;
                }

                MarkChanged(motor);
                report.AppendLine("Connected the movement script to its input source and camera.");
            }

            bool cameraTargetAssigned = false;
            if (cameraRig.Target == null || cameraRig.InputSource == null)
            {
                Undo.RecordObject(cameraRig, DialogTitle);

                if (cameraRig.Target == null)
                {
                    cameraRig.Target = mage.transform;
                    cameraTargetAssigned = true;
                }

                if (cameraRig.InputSource == null)
                {
                    cameraRig.InputSource = input;
                }

                MarkChanged(cameraRig);
                report.AppendLine("Connected the camera to the Mage and its input source.");
            }

            // Only reposition a camera that has just been pointed at the Mage, never one already set up.
            if (cameraTargetAssigned)
            {
                Undo.RecordObject(cameraRig.transform, DialogTitle);
                cameraRig.SnapToTarget();
                MarkChanged(cameraRig.transform);
            }
        }

        // --------------------------------------------------------------- finish

        private static void Finish(Scene scene, StringBuilder report)
        {
            string summary = report.ToString().TrimEnd();
            Debug.Log($"[Threefold] {DialogTitle} finished.\n{summary}");

            bool canSave = !string.IsNullOrEmpty(scene.path);
            string message = summary + "\n\nPress Play, click the Game view to capture the mouse, then use " +
                             "WASD, Shift, Space and the mouse. The left mouse button attacks. To try the touch controls " +
                             "with the mouse, " +
                             "switch on Threefold > Controls > Preview Touch Controls In Editor first.";

            if (!canSave)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    message + "\n\nThis scene has never been saved. Save it with File > Save As.", "OK");
                return;
            }

            if (EditorUtility.DisplayDialog(DialogTitle, message, "Save scene", "Not now"))
            {
                if (!EditorSceneManager.SaveScene(scene))
                {
                    Debug.LogError($"[Threefold] The scene '{scene.path}' could not be saved. Save it manually.");
                }
            }
        }
    }
}
