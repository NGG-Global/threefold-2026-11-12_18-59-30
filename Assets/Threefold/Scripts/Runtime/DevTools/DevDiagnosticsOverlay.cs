using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Threefold.Combat;
using Threefold.Magic;
using Threefold.Objectives;
using Threefold.VisualStyle;
using Threefold.WorldMagic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace Threefold.DevTools
{
    /// <summary>
    /// Development-only diagnostics for testing a level: frame timing against the graphics preset's
    /// target, CPU and GPU time where Unity reports them, approximate memory and garbage collections,
    /// enemies and projectiles, performance warnings (<see cref="PerformanceWarnings"/>), the current
    /// discipline, the magical object in the middle of the view with its properties and state, every
    /// temporary state running, the reaction queue, and the level's puzzle progression. It also offers
    /// a restart, a retry from the last checkpoint and a way to load another scene in the build.
    ///
    /// Hidden in normal play. It switches itself off completely in a build that is not a development
    /// build. To show or hide it: F3 or the back-quote key, the gamepad's Select (View) button, or a
    /// three-finger tap. Numbers are shown here only; the player never sees them (Docs/Discovery.md).
    ///
    /// Drawn with Unity's immediate-mode GUI, which allocates while shown; that is acceptable for a
    /// development tool. While hidden it records the frame time, and Unity still calls its OnGUI each
    /// frame, which returns at once; the frame times it shows include that small cost. Whether its
    /// buttons take touches on a device with the Input System as the only input handler has not been checked.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DevDiagnosticsOverlay : MonoBehaviour
    {
        private const float RefreshSeconds = 0.25f;
        private const float RescanSeconds = 2f;
        private const int MaxTemporaryLines = 8;
        private const float AimDistance = 40f;
        private const float StatsWindowSeconds = 10f;

        [SerializeField] private PlayerDisciplines disciplines;
        [SerializeField] private Transform view;
        [SerializeField] private PrototypeSession session;

        [Tooltip("Components that describe a level's progression (they implement IDevReport).")]
        [SerializeField] private MonoBehaviour[] reports = new MonoBehaviour[0];

        [Tooltip("Shown when the scene starts. Off: shown only when asked for.")]
        [SerializeField] private bool visibleAtStart = false;

        [Tooltip("A warning is shown when this many projectiles are in flight at once. A rule of thumb, not a measured budget.")]
        [Min(1)]
        [SerializeField] private int projectileWarningCount = 24;

        private readonly FrameTimingStats frames = new FrameTimingStats(240);
        private readonly StringBuilder text = new StringBuilder(2048);
        private readonly List<MagicalObject> magicalObjects = new List<MagicalObject>();
        private readonly List<MonoBehaviour> characterReports = new List<MonoBehaviour>(16);
        private readonly RaycastHit[] hits = new RaycastHit[8];
        private readonly FrameTiming[] timings = new FrameTiming[1];
        private readonly List<string> warnings = new List<string>(8);

        // Counters over windows of ten seconds, kept while hidden too, so warnings have something to go on at once.
        private float windowStart = -1f;
        private int collectionsAtStart;
        private long droppedAtStart;
        private long overflowAtStart;
        private float lastWindowSeconds;
        private int lastWindowCollections;
        private long lastWindowDropped;
        private long lastWindowOverflow;
        private int temporaryCount;

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
            UpdateWindow();
            if (visible)
            {
                // CPU and GPU times of recent frames, where the platform reports them.
                FrameTimingManager.CaptureFrameTimings();
            }

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

            VisualStyleController style = VisualStyleController.Active;
            string[] qualities = QualitySettings.names;
            int quality = QualitySettings.GetQualityLevel();
            text.Append("       preset ").Append(style != null ? style.PresetName : "(no style controller)")
                .Append(", Unity quality ").Append(quality >= 0 && quality < qualities.Length ? qualities[quality] : quality.ToString())
                .Append(", target ").Append(Application.targetFrameRate <= 0 ? "platform default" : Application.targetFrameRate + " fps");
            if (style != null && style.FrameRateFellBack)
            {
                text.Append(" (lowered from ").Append(style.RequestedFrameRate).Append(')');
            }

            text.AppendLine();

            float cpuMs = 0f;
            float gpuMs = 0f;
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                cpuMs = (float)timings[0].cpuMainThreadFrameTime;
                gpuMs = (float)timings[0].gpuFrameTime;
            }

            text.Append("       main thread ").Append(cpuMs > 0f ? cpuMs.ToString("0.0", invariant) + " ms" : "not reported")
                .Append(", GPU ").Append(gpuMs > 0f ? gpuMs.ToString("0.0", invariant) + " ms" : "not reported").AppendLine();

            AppendMemory();
            AppendActors();
            AppendWarnings(timing, style, cpuMs, gpuMs);

            text.Append("DISCIPLINE  ").Append(disciplines != null ? disciplines.Current.ToString() : "(no disciplines)").AppendLine();
            AppendCharacterReports();

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

        /// <summary>Reports of the Mage's own components (its combat animations), shown under its discipline.</summary>
        private void AppendCharacterReports()
        {
            if (disciplines == null)
            {
                return;
            }

            disciplines.GetComponents(characterReports);
            foreach (MonoBehaviour behaviour in characterReports)
            {
                if (behaviour is IDevReport report && behaviour.isActiveAndEnabled &&
                    (reports == null || System.Array.IndexOf(reports, behaviour) < 0))
                {
                    text.Append(report.ReportTitle.ToUpperInvariant()).AppendLine();
                    report.AppendReport(text);
                }
            }

            characterReports.Clear();
        }

        private void AppendMemory()
        {
            // Approximate: Unity's own allocators and the managed heap, as the profiling interface reports them.
            text.Append("MEMORY  Unity ").Append(PerformanceWarnings.Megabytes(Profiler.GetTotalAllocatedMemoryLong()))
                .Append(" of ").Append(PerformanceWarnings.Megabytes(Profiler.GetTotalReservedMemoryLong())).Append(" reserved, managed ")
                .Append(PerformanceWarnings.Megabytes(Profiler.GetMonoUsedSizeLong())).Append(" of ")
                .Append(PerformanceWarnings.Megabytes(Profiler.GetMonoHeapSizeLong())).Append(", device ")
                .Append(SystemInfo.systemMemorySize).Append(" MB").AppendLine();
            text.Append("        garbage collections ").Append(System.GC.CollectionCount(0));
            if (lastWindowSeconds > 0f)
            {
                text.Append(" (").Append(lastWindowCollections).Append(" in the last ")
                    .Append(lastWindowSeconds.ToString("0", CultureInfo.InvariantCulture)).Append(" s)");
            }

            text.AppendLine();
        }

        private void AppendActors()
        {
            text.Append("ACTORS  enemies ").Append(CountEnemies()).Append(", projectiles in flight ").Append(Projectile.FlyingCount)
                .Append(", objects with temporary states ").Append(temporaryCount).AppendLine();
        }

        private static int CountEnemies()
        {
            int count = 0;
            IReadOnlyList<Health> active = Health.Active;
            for (int i = 0; i < active.Count; i++)
            {
                Health health = active[i];
                if (health != null && health.IsAlive && health.Team == CombatTeam.Enemy)
                {
                    count++;
                }
            }

            return count;
        }

        private void AppendWarnings(in FrameTimingSummary timing, VisualStyleController style, float cpuMs, float gpuMs)
        {
            var snapshot = new PerformanceSnapshot
            {
                AverageMs = timing.AverageMs,
                SlowestMs = timing.SlowestMs,
                TargetFps = Application.targetFrameRate,
                FellBack = style != null && style.FrameRateFellBack,
                CpuMs = cpuMs,
                GpuMs = gpuMs,
                Collections = lastWindowCollections,
                CollectionSeconds = lastWindowSeconds,
                DroppedSinceLastLook = lastWindowDropped,
                CriticalOverflowSinceLastLook = lastWindowOverflow,
                Enemies = CountEnemies(),
                Projectiles = Projectile.FlyingCount,
                ProjectileLimit = projectileWarningCount,
            };

            WorldSimulation simulation = WorldSimulation.Existing;
            if (simulation != null && simulation.Scheduler != null)
            {
                snapshot.ReactionsWaiting = simulation.Scheduler.Count;
                if (simulation.Simulation != null)
                {
                    snapshot.SimulatedObjects = simulation.Simulation.ActiveCount;
                    snapshot.SimulationWaiting = simulation.Simulation.WaitingCount;
                    snapshot.SimulationLimit = simulation.Budgets.maxActiveObjects;
                }
            }

            warnings.Clear();
            int count = PerformanceWarnings.Collect(snapshot, warnings);
            text.Append("WARNINGS  ").Append(count == 0 ? "none" : count.ToString()).AppendLine();
            for (int i = 0; i < warnings.Count; i++)
            {
                text.Append("        ! ").Append(warnings[i]).AppendLine();
            }
        }

        /// <summary>Closes a window of ten seconds when it is over: garbage collections and refused reactions in it.</summary>
        private void UpdateWindow()
        {
            float now = Time.unscaledTime;
            ReactionStatistics statistics = default;
            WorldSimulation simulation = WorldSimulation.Existing;
            if (simulation != null && simulation.Scheduler != null)
            {
                statistics = simulation.Scheduler.Statistics;
            }

            if (windowStart < 0f)
            {
                windowStart = now;
                collectionsAtStart = System.GC.CollectionCount(0);
                droppedAtStart = statistics.Dropped;
                overflowAtStart = statistics.CriticalOverflow;
                return;
            }

            if (now - windowStart < StatsWindowSeconds)
            {
                return;
            }

            int collections = System.GC.CollectionCount(0);
            lastWindowSeconds = now - windowStart;
            lastWindowCollections = Mathf.Max(0, collections - collectionsAtStart);
            lastWindowDropped = PerformanceWarnings.Since(statistics.Dropped, droppedAtStart);
            lastWindowOverflow = PerformanceWarnings.Since(statistics.CriticalOverflow, overflowAtStart);
            windowStart = now;
            collectionsAtStart = collections;
            droppedAtStart = statistics.Dropped;
            overflowAtStart = statistics.CriticalOverflow;
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

            // The first thing the view meets, so nothing behind a wall is reported. The Mage itself is skipped.
            int nearest = -1;
            for (int i = 0; i < count; i++)
            {
                bool player = disciplines != null && hits[i].collider.transform.IsChildOf(disciplines.transform);
                if (!player && (nearest < 0 || hits[i].distance < hits[nearest].distance))
                {
                    nearest = i;
                }
            }

            return nearest >= 0 ? hits[nearest].collider.GetComponentInParent<MagicalObject>() : null;
        }

        private void AppendTemporary()
        {
            text.Append("TEMPORARY STATES").AppendLine();
            temporaryCount = 0;
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

                temporaryCount++;

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
