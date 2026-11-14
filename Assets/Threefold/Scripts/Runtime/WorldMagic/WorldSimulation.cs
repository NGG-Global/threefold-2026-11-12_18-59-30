using Threefold.Persistence;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>
    /// Runs the world's temporary state and its chain reactions: the one component with an update,
    /// in place of an update or a coroutine per object. Each frame it advances the objects that are
    /// changing (<see cref="SimulationSet"/>) and carries out a bounded number of waiting reactions
    /// (<see cref="ReactionScheduler"/>). While nothing is changing and nothing waits, it switches
    /// itself off. Saving is not its business; see WorldStateSaver.
    ///
    /// It is created by itself the first time something needs it, so no scene has to be changed. To
    /// tune the budgets for a scene, add a <see cref="WorldSimulationBudgets"/> component to it.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class WorldSimulation : MonoBehaviour
    {
        private const float RefusalLogSeconds = 5f;

        private static WorldSimulation instance;
        private static bool quitting;

        private readonly ReactionBudgets budgets = new ReactionBudgets();
        private SimulationSet simulation;
        private ReactionScheduler scheduler;
        private float sinceRefusalLog = RefusalLogSeconds;
        private long reportedDrops;

        /// <summary>The running simulation, or null if nothing has needed one yet.</summary>
        public static WorldSimulation Existing => instance;

        public ReactionBudgets Budgets => budgets;
        public SimulationSet Simulation => simulation;
        public ReactionScheduler Scheduler => scheduler;

        /// <summary>The simulation, created if there is none. Null outside Play mode and while the game is closing.</summary>
        public static WorldSimulation Instance
        {
            get
            {
                if (instance != null)
                {
                    return instance;
                }

                if (quitting || !Application.isPlaying)
                {
                    return null;
                }

                var host = new GameObject("WorldSimulation");
                DontDestroyOnLoad(host);
                return host.AddComponent<WorldSimulation>();
            }
        }

        /// <summary>Asks for an object to be simulated. False if it has to wait, or there is no simulation.</summary>
        public static bool Wake(ISimulated item)
        {
            WorldSimulation world = Instance;
            if (world == null)
            {
                return false;
            }

            world.enabled = true;
            return world.simulation.Wake(item);
        }

        /// <summary>Offers a secondary reaction. It is carried out within the budgets, in a fixed order.</summary>
        public static EnqueueResult React(IMagicEffectReceiver target, in MagicEffect effect, ReactionPriority priority)
        {
            WorldSimulation world = Instance;
            if (world == null)
            {
                return EnqueueResult.InvalidTarget;
            }

            world.enabled = true;
            return world.scheduler.Enqueue(target, effect, priority);
        }

        /// <summary>
        /// An object is gone (destroyed, unloaded, switched off): it leaves the simulation and whatever
        /// was on its way to it is given up, so nothing calls into it afterwards. A critical reaction
        /// given up is reported, unless <paramref name="report"/> is false because the object's state
        /// was just set as a whole (a reload, a restore) and what was on its way is meant to go.
        /// </summary>
        public static void Forget(ISimulated item, IMagicEffectReceiver receiver, bool report = true)
        {
            if (instance == null)
            {
                return;
            }

            instance.simulation.Remove(item);
            instance.scheduler.Cancel(receiver, report);
        }

        /// <summary>Sets the budgets. Called by <see cref="WorldSimulationBudgets"/>.</summary>
        public void Configure(ReactionBudgets values)
        {
            if (values == null)
            {
                return;
            }

            budgets.maxDepth = values.maxDepth;
            budgets.reactionsPerFrame = values.reactionsPerFrame;
            budgets.queueCapacity = values.queueCapacity;
            budgets.maxActiveObjects = values.maxActiveObjects;
        }

        /// <summary>A target can receive a reaction if it still exists and is switched on.</summary>
        private static bool IsAlive(IMagicEffectReceiver target)
        {
            if (target is Behaviour behaviour)
            {
                return behaviour != null && behaviour.enabled && behaviour.gameObject.activeInHierarchy;
            }

            if (target is Object unityObject)
            {
                return unityObject != null;
            }

            return target != null;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
            simulation = new SimulationSet(budgets);
            scheduler = new ReactionScheduler(budgets) { IsValidTarget = IsAlive };
            scheduler.Refused += ReportCritical;
            WorldState.Reloaded += HandleWorldReloaded;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
                WorldState.Reloaded -= HandleWorldReloaded;
            }
        }

        /// <summary>
        /// The world takes its state from the store again. What was on its way belonged to the world as
        /// it was, and is dropped: a restarted enemy is not heated by a fire that no longer burns.
        /// </summary>
        private void HandleWorldReloaded(WorldReload reload)
        {
            int criticalForgotten = scheduler.Clear();
            if (criticalForgotten > 0)
            {
                Debug.Log($"[Threefold] The world was reloaded with {criticalForgotten} critical reaction(s) still on their " +
                          "way. They were dropped with the rest of the temporary state.");
            }
        }

        /// <summary>
        /// A puzzle may depend on a critical reaction, so every one that is not carried out is reported
        /// at once, with the reason. They are rare by design; flavour reactions are summarised instead.
        /// </summary>
        private void ReportCritical(QueuedReaction reaction, EnqueueResult reason)
        {
            if (reaction.Priority != ReactionPriority.Critical)
            {
                return;
            }

            Object context = reaction.Target as Object;
            string target = context != null ? $"'{context.name}'" : "a target that is gone";
            string why;
            switch (reason)
            {
                case EnqueueResult.DepthLimit:
                    why = $"it would be {reaction.Effect.Depth} steps from what the player did, and chains may go " +
                          $"{budgets.maxDepth} (World Simulation Budgets)";
                    break;
                case EnqueueResult.Duplicate:
                    why = "the target takes each kind of effect once per event, and this one has already reached it, " +
                          "or is on its way, from the same event by another path";
                    break;
                case EnqueueResult.InvalidTarget:
                    why = "the target was destroyed, unloaded or switched off before it arrived";
                    break;
                default:
                    why = reason.ToString();
                    break;
            }

            Debug.LogWarning($"[Threefold] A critical reaction ({reaction.Effect.Kind} {reaction.Effect.Magnitude:0.##}) for " +
                             $"{target} was not carried out: {why}. Check the puzzle that depends on it.", context);
        }

        private void Update()
        {
            simulation.Tick(Time.deltaTime);
            scheduler.Process();
            ReportRefusals(false);

            if (simulation.IsIdle && scheduler.Count == 0)
            {
                // Nothing to advance and nothing waiting: no update until something changes. Anything
                // that was dropped is reported before going quiet, so it is never left unsaid.
                ReportRefusals(true);
                enabled = false;
            }
        }

        private void OnApplicationQuit()
        {
            quitting = true;
        }

        /// <summary>Reactions refused because a queue was full are reported, at most every few seconds.</summary>
        private void ReportRefusals(bool now)
        {
            if (!now)
            {
                sinceRefusalLog += Time.unscaledDeltaTime;
            }

            long dropped = scheduler.Statistics.Dropped;
            if (dropped == reportedDrops || (!now && sinceRefusalLog < RefusalLogSeconds))
            {
                return;
            }

            Debug.LogWarning($"[Threefold] {dropped - reportedDrops} reaction(s) of normal or low priority were dropped because " +
                             $"the reaction queue was full (capacity {budgets.queueCapacity}). Critical reactions are never refused for lack of room.");
            reportedDrops = dropped;
            sinceRefusalLog = 0f;
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            quitting = false;
        }
    }
}
