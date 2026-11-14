using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Threefold.Magic;
using Threefold.Objectives;
using Threefold.WorldMagic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Threefold.DevTools
{
    /// <summary>
    /// Development-only diagnostics for testing a level: the current discipline, the magical object in
    /// the middle of the view with its properties and state, every temporary state running, the
    /// reaction queue, the level's puzzle progression, and frame timing. It also offers a restart, a
    /// retry from the last checkpoint and a way to load another scene in the build.
    ///
    /// Hidden in normal play. It switches itself off completely in a build that is not a development
    /// build. To show or hide it: F3 or the back-quote key, the gamepad's Select (View) button, or a
    /// three-finger tap. Numbers are shown here only; the player never sees them (Docs/Discovery.md).
    ///
    /// Drawn with Unity's immediate-mode GUI, which allocates while shown; that is acceptable for a
    /// development tool and costs nothing while hidden apart from recording the frame time. Whether
    /// its buttons take touches on a device with the Input System as the only input handler has not
    /// been checked.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DevDiagnosticsOverlay : MonoBehaviour
    {
        private const float RefreshSeconds = 0.25f;
        private const float RescanSeconds = 2f;
        private const int MaxTemporaryLines = 8;
        private const float AimDistance = 40f;

        [SerializeField] private PlayerDisciplines disciplines;
        [SerializeField] private Transform view;
        [SerializeField] private PrototypeSession session;

        [Tooltip("Components that describe a level's progression (they implement IDevReport).")]
        [SerializeField] private MonoBehaviour[] reports = new MonoBehaviour[0];

        [Tooltip("Shown when the scene starts. Off: shown only when asked for.")]
        [SerializeField] private bool visibleAtStart = false;

        private readonly FrameTimingStats frames = new FrameTimingStats(240);
        private readonly StringBuilder text = new StringBuilder(2048);
        private readonly List<MagicalObject> magicalObjects = new List<MagicalObject>();
        private readonly RaycastHit[] hits = new RaycastHit[8];

        private bool visible;
        private string shown = string.Empty;
        private float nextRefresh;
        private float nextRescan;
        private int touchesBefore;
        private long deliveredBefore;
        private float deliveredAt;
        private float deliveredPerSecond;
        private Vector2 scroll;
        private GUIStyle labelStyle;
        private GUIStyle buttonStyle;

        public bool Visible => visible;

        /// <summary>Connects the overlay. Called by the setup tooling; empty arguments leave what is set.</summary>
        public void BindMissing(PlayerDisciplines playerDisciplines, Transform viewTransform, PrototypeSession owningSession,
            MonoBehaviour[] progressReports)
        {
            disciplines = disciplines != null ? disciplines : playerDisciplines;
            view = view != null ? view : viewTransform;
            session = session != null ? session : owningSession;
            if ((reports == null || reports.Length == 0) && progressReports != null)
            {
                reports = progressReports;
            }
        }

        public void SetVisible(bool show)
        {
            visible = show;
            nextRefresh = 0f;
            nextRescan = 0f;
        }

        private void Awake()
        {
            // Never part of a release build's play.
            if (!Debug.isDebugBuild)
            {
                enabled = false;
                return;
            }

            visible = visibleAtStart;
        }

        private void Update()
        {
            frames.Add(Time.unscaledDeltaTime);

            if (TogglePressed())
            {
                SetVisible(!visible);
            }

            if (!visible || Time.unscaledTime < nextRefresh)
            {
                return;
            }

            nextRefresh = Time.unscaledTime + RefreshSeconds;
            if (Time.unscaledTime >= nextRescan)
            {
                nextRescan = Time.unscaledTime + RescanSeconds;
                magicalObjects.Clear();
                magicalObjects.AddRange(FindObjectsByType<MagicalObject>());
            }

            Compose();
        }

        private bool TogglePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.f3Key.wasPressedThisFrame || keyboard.backquoteKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null && gamepad.selectButton.wasPressedThisFrame)
            {
                return true;
            }

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                return false;
            }

            int touches = 0;
            foreach (var touch in touchscreen.touches)
            {
                if (touch.press.isPressed)
                {
                    touches++;
                }
            }

            bool tapped = touches >= 3 && touchesBefore < 3;
            touchesBefore = touches;
            return tapped;
        }

        private void Compose()
        {
            text.Length = 0;
            CultureInfo invariant = CultureInfo.InvariantCulture;

            FrameTimingSummary timing = frames.Summarize();
            text.Append("FRAME  ").Append(timing.AverageFps.ToString("0", invariant)).Append(" fps, ")
                .Append(timing.AverageMs.ToString("0.0", invariant)).Append(" ms average, slowest 5% ")
                .Append(timing.SlowestMs.ToString("0.0", invariant)).Append(" ms, worst ")
                .Append(timing.WorstMs.ToString("0.0", invariant)).Append(" ms (").Append(timing.Samples).Append(" frames)").AppendLine();
            string[] qualities = QualitySettings.names;
            int quality = QualitySettings.GetQualityLevel();
            text.Append("       quality ").Append(quality >= 0 && quality < qualities.Length ? qualities[quality] : quality.ToString())
                .Append(", target ").Append(Application.targetFrameRate <= 0 ? "platform default" : Application.targetFrameRate + " fps")
                .AppendLine();

            text.Append("DISCIPLINE  ").Append(disciplines != null ? disciplines.Current.ToString() : "(no disciplines)").AppendLine();

            AppendTarget();
            AppendTemporary();
            AppendQueue();

            if (reports != null)
            {
                foreach (MonoBehaviour behaviour in reports)
                {
                    if (behaviour is IDevReport report && behaviour != null)
                    {
                        text.Append(report.ReportTitle.ToUpperInvariant()).AppendLine();
                        report.AppendReport(text);
                    }
                }
            }

            shown = text.ToString();
        }

        private void AppendTarget()
        {
            text.Append("TARGET  ");
            MagicalObject target = FindAimed();
            if (target == null || target.Properties == null)
            {
                text.AppendLine("nothing magical in the middle of the view");
                return;
            }

            text.Append(target.name).Append(" (").Append(target.Definition != null ? target.Definition.name : "no definition").Append(')').AppendLine();
            text.Append("        properties: ").Append(MagicalStateText.Properties(target.Properties.Settings)).AppendLine();
            text.Append("        state: ").Append(MagicalStateText.State(target.Properties)).AppendLine();
            if (!string.IsNullOrEmpty(target.PersistentId))
            {
                text.Append("        saved as: ").Append(target.PersistentId).AppendLine();
            }
        }

        private MagicalObject FindAimed()
        {
            if (view == null)
            {
                return null;
            }

            int count = Physics.RaycastNonAlloc(view.position, view.forward, hits, AimDistance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            MagicalObject nearest = null;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                MagicalObject found = hits[i].collider.GetComponentInParent<MagicalObject>();
                if (found != null && hits[i].distance < nearestDistance)
                {
                    nearest = found;
                    nearestDistance = hits[i].distance;
                }
            }

            return nearest;
        }

        private void AppendTemporary()
        {
            text.Append("TEMPORARY STATES").AppendLine();
            int lines = 0;
            int more = 0;
            foreach (MagicalObject magical in magicalObjects)
            {
                if (magical == null || magical.Properties == null)
                {
                    continue;
                }

                string running = MagicalStateText.Temporary(magical.Properties);
                if (running.Length == 0)
                {
                    continue;
                }

                if (lines < MaxTemporaryLines)
                {
                    text.Append("        ").Append(magical.name).Append(": ").Append(running).AppendLine();
                    lines++;
                }
                else
                {
                    more++;
                }
            }

            if (lines == 0)
            {
                text.AppendLine("        none running");
            }
            else if (more > 0)
            {
                text.Append("        and ").Append(more).AppendLine(" more");
            }
        }

        private void AppendQueue()
        {
            text.Append("REACTIONS  ");
            WorldSimulation simulation = WorldSimulation.Existing;
            if (simulation == null || simulation.Scheduler == null)
            {
                text.AppendLine("no world simulation yet (nothing has changed)");
                return;
            }

            ReactionScheduler scheduler = simulation.Scheduler;
            ReactionStatistics statistics = scheduler.Statistics;
            float now = Time.unscaledTime;
            if (now - deliveredAt >= 1f)
            {
                deliveredPerSecond = deliveredAt > 0f ? (statistics.Delivered - deliveredBefore) / (now - deliveredAt) : 0f;
                deliveredBefore = statistics.Delivered;
                deliveredAt = now;
            }

            CultureInfo invariant = CultureInfo.InvariantCulture;
            text.Append("waiting ").Append(scheduler.Count).Append(" (critical ").Append(scheduler.CriticalCount).Append("), delivered ")
                .Append(statistics.Delivered).Append(" (").Append(deliveredPerSecond.ToString("0.0", invariant)).Append("/s), peak waiting ")
                .Append(statistics.PeakWaiting).AppendLine();
            text.Append("        refused at the depth limit ").Append(statistics.DepthLimited).Append(", duplicates ").Append(statistics.Duplicates)
                .Append(", dropped ").Append(statistics.Dropped).Append(", critical overflow ").Append(statistics.CriticalOverflow)
                .Append(", target gone ").Append(statistics.Invalidated).AppendLine();

            SimulationSet active = simulation.Simulation;
            if (active != null)
            {
                text.Append("        simulated objects ").Append(active.ActiveCount).Append(" of ").Append(simulation.Budgets.maxActiveObjects)
                    .Append(", waiting ").Append(active.WaitingCount).Append(", peak ").Append(active.PeakActive).AppendLine();
            }
        }

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            if (labelStyle == null || buttonStyle == null)
            {
                int size = Mathf.Max(12, Mathf.RoundToInt(Screen.height / 48f));
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = size, wordWrap = true, richText = false };
                labelStyle.normal.textColor = new Color(1f, 0.97f, 0.9f, 1f);
                buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = size };
            }

            Rect safe = Screen.safeArea;
            float width = Mathf.Min(safe.width - 24f, Mathf.Max(520f, safe.width * 0.55f));
            var area = new Rect(safe.x + 12f, Screen.height - safe.yMax + 12f, width, safe.height - 24f);

            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 8f, area.width - 16f, area.height - 16f));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Hide", buttonStyle))
            {
                SetVisible(false);
            }

            if (session != null && GUILayout.Button("Restart", buttonStyle))
            {
                session.Restart();
            }

            if (session != null && GUILayout.Button("Retry from checkpoint", buttonStyle))
            {
                session.RetryFromCheckpoint();
            }

            int scenes = SceneManager.sceneCountInBuildSettings;
            if (scenes > 1 && GUILayout.Button("Next scene", buttonStyle))
            {
                int next = (SceneManager.GetActiveScene().buildIndex + 1) % scenes;
                SceneManager.LoadScene(next);
            }

            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(shown, labelStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
