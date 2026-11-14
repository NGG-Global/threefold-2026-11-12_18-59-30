using Threefold.Persistence;
using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>
    /// Sets the budgets of the world simulation for the scene it is in. Optional: without it the
    /// defaults apply. The values are provisional and have not been measured on a device.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldSimulationBudgets : MonoBehaviour
    {
        [SerializeField] private ReactionBudgets budgets = new ReactionBudgets();

        [Tooltip("Seconds between writes of a changed world state to disk. It is also written when the app is paused or closed.")]
        [Min(0.5f)]
        [SerializeField] private float secondsBetweenSaves = 5f;

        public ReactionBudgets Budgets => budgets;

        private void Start()
        {
            WorldSimulation world = WorldSimulation.Instance;
            if (world != null)
            {
                world.Configure(budgets);
            }

            WorldStateSaver.SaveInterval = secondsBetweenSaves;
        }
    }
}
