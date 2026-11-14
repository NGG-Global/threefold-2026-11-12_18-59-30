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
        /// was on its way to it is given up, so nothing calls into it afterwards.
        /// </summary>
        public static void Forget(ISimulated item, IMagicEffectReceiver receiver)
        {
            if (instance == null)
            {
                return;
            }

            instance.simulation.Remove(item);
            instance.scheduler.Cancel(receiver);
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
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
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
                             $"the reaction queue was full (capacity {budgets.queueCapacity}). Critical reactions are never dropped.");
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
