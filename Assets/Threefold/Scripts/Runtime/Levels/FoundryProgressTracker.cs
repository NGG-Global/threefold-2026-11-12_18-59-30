using System.Text;
using Threefold.DevTools;
using Threefold.Enemies;
using Threefold.Objectives;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Levels
{
    /// <summary>
    /// Reads the Broken Foundry's lasting progress from the objects themselves (the crystal awake, the
    /// barricade gone, the lift at its stop, the Guardian defeated, the plug broken, the counterweight at
    /// its stop) and shows it in the development diagnostics, with the routes that are open and the
    /// current checkpoint. It decides nothing and changes nothing: every state it reads is the object's
    /// own, and the routes come from <see cref="FoundryRoutes"/>. It has no update.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoundryProgressTracker : MonoBehaviour, IDevReport
    {
        [SerializeField] private MagicalObject dormantCrystal;
        [SerializeField] private MagicalObject damagedBarricade;
        [SerializeField] private MagicalObject runeLift;
        [SerializeField] private PersistentDefeat guardian;
        [SerializeField] private MagicalObject slagPlug;
        [SerializeField] private MagicalObject counterweight;
        [SerializeField] private CheckpointRoute route;

        public string ReportTitle => "Puzzle progression";

        /// <summary>Connects the tracker. Called by the setup tooling; empty arguments leave what is set.</summary>
        public void BindMissing(MagicalObject crystal, MagicalObject barricade, MagicalObject lift, PersistentDefeat guardianOutcome,
            MagicalObject plug, MagicalObject weight, CheckpointRoute checkpointRoute)
        {
            dormantCrystal = dormantCrystal != null ? dormantCrystal : crystal;
            damagedBarricade = damagedBarricade != null ? damagedBarricade : barricade;
            runeLift = runeLift != null ? runeLift : lift;
            guardian = guardian != null ? guardian : guardianOutcome;
            slagPlug = slagPlug != null ? slagPlug : plug;
            counterweight = counterweight != null ? counterweight : weight;
            route = route != null ? route : checkpointRoute;
        }

        public bool IsComplete => dormantCrystal != null && damagedBarricade != null && runeLift != null && guardian != null &&
                                  slagPlug != null && counterweight != null && route != null;

        /// <summary>What has changed for good, read from the objects as they are now.</summary>
        public FoundryProgress Current
        {
            get
            {
                FoundryProgress progress = FoundryProgress.None;
                if (Meets(dormantCrystal, PropertyCondition.Activated)) progress |= FoundryProgress.GateOpen;
                if (damagedBarricade != null && damagedBarricade.IsDestroyed) progress |= FoundryProgress.BarricadeCleared;
                if (Meets(runeLift, PropertyCondition.AtTravelLimit)) progress |= FoundryProgress.LiftInPlace;
                if (guardian != null && guardian.IsActive) progress |= FoundryProgress.GuardianDefeated;
                if (slagPlug != null && slagPlug.IsDestroyed) progress |= FoundryProgress.PlugBroken;
                if (Meets(counterweight, PropertyCondition.AtTravelLimit)) progress |= FoundryProgress.ExitGateOpen;
                return progress;
            }
        }

        public void AppendReport(StringBuilder text)
        {
            FoundryProgress progress = Current;
            Line(text, progress, FoundryProgress.GateOpen, "Foundry gate open (dormant crystal awake)");
            Line(text, progress, FoundryProgress.BarricadeCleared, "Damaged barricade cleared");
            Line(text, progress, FoundryProgress.LiftInPlace, "Rune lift under the ledge");
            Line(text, progress, FoundryProgress.GuardianDefeated, "Ruins Guardian defeated");
            Line(text, progress, FoundryProgress.PlugBroken, "Slag plug broken (culvert open)");
            Line(text, progress, FoundryProgress.ExitGateOpen, "Counterweight at its stop (exit gate open)");

            Checkpoint current = route != null ? route.Current : null;
            text.Append("Checkpoint: ").Append(current != null ? current.name : "none yet").AppendLine();

            // Which ways through the exit wall stand open now; the breach is open whenever the gantry is braced.
            text.Append("Open through the exit wall: ");
            bool any = false;
            foreach (ExitRoute exit in new[] { ExitRoute.Culvert, ExitRoute.Gate })
            {
                if ((progress & FoundryRoutes.ChangeFor(exit)) != 0)
                {
                    text.Append(any ? ", " : string.Empty).Append(exit);
                    any = true;
                }
            }

            text.Append(any ? string.Empty : "nothing").AppendLine("; the breach needs a brace each time");
        }

        private static bool Meets(MagicalObject target, PropertyCondition condition)
        {
            return target != null && target.Meets(condition);
        }

        private static void Line(StringBuilder text, FoundryProgress progress, FoundryProgress flag, string label)
        {
            text.Append((progress & flag) != 0 ? "[x] " : "[ ] ").Append(label).AppendLine();
        }
    }
}
