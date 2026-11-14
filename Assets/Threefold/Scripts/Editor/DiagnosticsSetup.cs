using System.Text;
using Threefold.Cameras;
using Threefold.DevTools;
using Threefold.Magic;
using Threefold.Objectives;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Adds the development diagnostics overlay (<see cref="DevDiagnosticsOverlay"/>) to every playable
    /// scene, as part of the character rig. A scene that already has one (the Broken Foundry adds its own
    /// with the level's progress report) keeps it; only empty references are filled. The overlay switches
    /// itself off in builds that are not development builds.
    /// </summary>
    internal static class DiagnosticsSetup
    {
        private const string DialogTitle = "Development Diagnostics";
        internal const string ObjectName = "Dev Diagnostics";

        internal static DevDiagnosticsOverlay Ensure(Scene scene, GameObject mage, StringBuilder report)
        {
            DevDiagnosticsOverlay overlay = FindInScene<DevDiagnosticsOverlay>(scene);
            if (overlay == null)
            {
                var host = new GameObject(ObjectName);
                SceneManager.MoveGameObjectToScene(host, scene);
                Undo.RegisterCreatedObjectUndo(host, DialogTitle);
                overlay = host.AddComponent<DevDiagnosticsOverlay>();
                report.AppendLine("Added the development diagnostics (development builds only; F3, back-quote, gamepad Select or a three-finger tap).");
            }

            ThirdPersonCamera rig = FindInScene<ThirdPersonCamera>(scene);
            Undo.RecordObject(overlay, DialogTitle);
            overlay.BindMissing(mage != null ? mage.GetComponent<PlayerDisciplines>() : null, rig != null ? rig.transform : null,
                FindInScene<PrototypeSession>(scene), null);
            MarkChanged(overlay);
            return overlay;
        }
    }
}
