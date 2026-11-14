using System;
using System.Collections.Generic;

namespace Threefold.Levels
{
    /// <summary>The places of the Broken Foundry the player can stand in, from the start to the way out.</summary>
    public enum FoundryPlace
    {
        Forecourt = 0,
        OreHallEntry = 1,
        OreHallLanding = 2,
        OreHallNorth = 3,
        LiftHall = 4,
        LiftLedge = 5,
        CastingFloor = 6,
        ExitCourt = 7,
        Breach = 8,
        ExitYard = 9,
    }

    /// <summary>What has changed for good in the level. Each is saved, and each only ever opens a way.</summary>
    [Flags]
    public enum FoundryProgress
    {
        None = 0,
        GateOpen = 1 << 0,
        BarricadeCleared = 1 << 1,
        LiftInPlace = 1 << 2,
        GuardianDefeated = 1 << 3,
        PlugBroken = 1 << 4,
        ExitGateOpen = 1 << 5,
    }

    /// <summary>The disciplines an action uses, as a set.</summary>
    [Flags]
    public enum DisciplineSet
    {
        None = 0,
        Arcane = 1 << 0,
        Flame = 1 << 1,
        Aegis = 1 << 2,
    }

    /// <summary>How the player leaves the exit court for the way out.</summary>
    public enum ExitRoute
    {
        None = 0,

        /// <summary>Weaken and break: heat the slag plug, then shatter it with force, and walk through the culvert.</summary>
        Culvert = 1,

        /// <summary>Power and reposition: light the counterweight's rune lock, slide it to its stop, and the exit gate rises.</summary>
        Gate = 2,

        /// <summary>Brace and climb: brace the crumbling gantry, walk up it to the breach, and climb down beyond.</summary>
        Breach = 3,
    }

    /// <summary>A way from one place to another, and what it needs.</summary>
    public readonly struct FoundryPassage
    {
        public readonly FoundryPlace From;
        public readonly FoundryPlace To;

        /// <summary>Everything listed must have happened.</summary>
        public readonly FoundryProgress Requires;

        /// <summary>The way is a crumbling structure that must be braced each time it is crossed. Bracing is always possible again.</summary>
        public readonly bool NeedsBrace;

        public readonly ExitRoute Route;

        public FoundryPassage(FoundryPlace from, FoundryPlace to, FoundryProgress requires = FoundryProgress.None, bool needsBrace = false,
            ExitRoute route = ExitRoute.None)
        {
            From = from;
            To = to;
            Requires = requires;
            NeedsBrace = needsBrace;
            Route = route;
        }

        public bool IsOpen(FoundryProgress progress, bool canBrace)
        {
            return (progress & Requires) == Requires && (!NeedsBrace || canBrace);
        }
    }

    /// <summary>Something the player does that changes the level for good, where it can be done, and with what.</summary>
    public readonly struct FoundryAction
    {
        public readonly string Name;
        public readonly FoundryPlace[] Places;
        public readonly FoundryProgress Grants;

        /// <summary>Each element is one way of doing it: the set of disciplines that way needs, all of them.</summary>
        public readonly DisciplineSet[] Ways;

        public FoundryAction(string name, FoundryProgress grants, FoundryPlace[] places, params DisciplineSet[] ways)
        {
            Name = name;
            Grants = grants;
            Places = places;
            Ways = ways;
        }

        public bool PossibleWith(DisciplineSet available)
        {
            foreach (DisciplineSet way in Ways)
            {
                if ((available & way) == way)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The level as a graph: places joined by passages that need some lasting change (an open gate, a
    /// slid block) or a brace, and the actions that make those changes. It mirrors what the setup
    /// command builds; the layout tests check each passage's geometric claim against
    /// <see cref="FoundryLayout"/> (that the ledge cannot be climbed without the lift, that the pit
    /// cannot be jumped, and so on). With it the tests show, without Unity, that every route of the
    /// design reaches the way out, that both kinds of final solution are complete, and that no order
    /// of actions leaves the player somewhere the way out can no longer be reached. Plain C#.
    /// </summary>
    public static class FoundryRoutes
    {
        public static readonly FoundryPassage[] Passages =
        {
            new FoundryPassage(FoundryPlace.Forecourt, FoundryPlace.OreHallEntry, FoundryProgress.GateOpen),
            new FoundryPassage(FoundryPlace.OreHallEntry, FoundryPlace.Forecourt, FoundryProgress.GateOpen),
            new FoundryPassage(FoundryPlace.OreHallEntry, FoundryPlace.OreHallLanding, FoundryProgress.BarricadeCleared),
            new FoundryPassage(FoundryPlace.OreHallLanding, FoundryPlace.OreHallEntry, FoundryProgress.BarricadeCleared),
            new FoundryPassage(FoundryPlace.OreHallLanding, FoundryPlace.OreHallNorth, needsBrace: true),
            new FoundryPassage(FoundryPlace.OreHallNorth, FoundryPlace.OreHallLanding, needsBrace: true),
            new FoundryPassage(FoundryPlace.OreHallNorth, FoundryPlace.LiftHall),
            new FoundryPassage(FoundryPlace.LiftHall, FoundryPlace.OreHallNorth),
            new FoundryPassage(FoundryPlace.LiftHall, FoundryPlace.LiftLedge, FoundryProgress.LiftInPlace),
            new FoundryPassage(FoundryPlace.LiftLedge, FoundryPlace.LiftHall),
            new FoundryPassage(FoundryPlace.LiftLedge, FoundryPlace.CastingFloor),
            new FoundryPassage(FoundryPlace.CastingFloor, FoundryPlace.LiftLedge),
            new FoundryPassage(FoundryPlace.CastingFloor, FoundryPlace.ExitCourt, FoundryProgress.GuardianDefeated),
            new FoundryPassage(FoundryPlace.ExitCourt, FoundryPlace.CastingFloor, FoundryProgress.GuardianDefeated),
            new FoundryPassage(FoundryPlace.ExitCourt, FoundryPlace.ExitYard, FoundryProgress.PlugBroken, route: ExitRoute.Culvert),
            new FoundryPassage(FoundryPlace.ExitYard, FoundryPlace.ExitCourt, FoundryProgress.PlugBroken, route: ExitRoute.Culvert),
            new FoundryPassage(FoundryPlace.ExitCourt, FoundryPlace.ExitYard, FoundryProgress.ExitGateOpen, route: ExitRoute.Gate),
            new FoundryPassage(FoundryPlace.ExitYard, FoundryPlace.ExitCourt, FoundryProgress.ExitGateOpen, route: ExitRoute.Gate),
            new FoundryPassage(FoundryPlace.ExitCourt, FoundryPlace.Breach, needsBrace: true, route: ExitRoute.Breach),
            new FoundryPassage(FoundryPlace.Breach, FoundryPlace.ExitCourt, needsBrace: true, route: ExitRoute.Breach),
            new FoundryPassage(FoundryPlace.Breach, FoundryPlace.ExitYard, route: ExitRoute.Breach),
            new FoundryPassage(FoundryPlace.ExitYard, FoundryPlace.Breach, route: ExitRoute.Breach),
        };

        public static readonly FoundryAction[] Actions =
        {
            new FoundryAction("Wake the dormant crystal", FoundryProgress.GateOpen, new[] { FoundryPlace.Forecourt },
                DisciplineSet.Arcane),
            new FoundryAction("Clear the damaged barricade", FoundryProgress.BarricadeCleared,
                new[] { FoundryPlace.OreHallEntry, FoundryPlace.OreHallLanding }, DisciplineSet.Flame, DisciplineSet.Aegis),
            new FoundryAction("Slide the rune lift under the ledge", FoundryProgress.LiftInPlace, new[] { FoundryPlace.LiftHall },
                DisciplineSet.Arcane | DisciplineSet.Aegis),
            new FoundryAction("Defeat the Ruins Guardian", FoundryProgress.GuardianDefeated, new[] { FoundryPlace.CastingFloor },
                DisciplineSet.Arcane, DisciplineSet.Flame, DisciplineSet.Aegis),
            new FoundryAction("Heat and shatter the slag plug", FoundryProgress.PlugBroken,
                new[] { FoundryPlace.ExitCourt, FoundryPlace.ExitYard }, DisciplineSet.Flame | DisciplineSet.Aegis),
            new FoundryAction("Slide the counterweight to its stop", FoundryProgress.ExitGateOpen, new[] { FoundryPlace.ExitCourt },
                DisciplineSet.Arcane | DisciplineSet.Aegis),
        };

        public const DisciplineSet AllDisciplines = DisciplineSet.Arcane | DisciplineSet.Flame | DisciplineSet.Aegis;

        /// <summary>Bracing a crumbling structure is the Aegis guard.</summary>
        public static bool CanBrace(DisciplineSet available) => (available & DisciplineSet.Aegis) != 0;

        /// <summary>Every place the player can walk to from <paramref name="from"/> as the level stands.</summary>
        public static HashSet<FoundryPlace> Reachable(FoundryPlace from, FoundryProgress progress, DisciplineSet available)
        {
            bool brace = CanBrace(available);
            var seen = new HashSet<FoundryPlace> { from };
            var open = new Queue<FoundryPlace>();
            open.Enqueue(from);
            while (open.Count > 0)
            {
                FoundryPlace place = open.Dequeue();
                foreach (FoundryPassage passage in Passages)
                {
                    if (passage.From == place && passage.IsOpen(progress, brace) && seen.Add(passage.To))
                    {
                        open.Enqueue(passage.To);
                    }
                }
            }

            return seen;
        }

        /// <summary>
        /// Every lasting state of the level the player can bring about from a starting state, by any
        /// actions in any order, each from wherever the player can stand at the time.
        /// </summary>
        public static HashSet<FoundryProgress> ReachableStates(FoundryPlace start, FoundryProgress progress, DisciplineSet available)
        {
            var states = new HashSet<FoundryProgress> { progress };
            var open = new Queue<FoundryProgress>();
            open.Enqueue(progress);
            while (open.Count > 0)
            {
                FoundryProgress state = open.Dequeue();
                HashSet<FoundryPlace> places = Reachable(start, state, available);
                foreach (FoundryAction action in Actions)
                {
                    if ((state & action.Grants) == action.Grants || !action.PossibleWith(available) || !CanStandAt(places, action))
                    {
                        continue;
                    }

                    FoundryProgress next = state | action.Grants;
                    if (states.Add(next))
                    {
                        open.Enqueue(next);
                    }
                }
            }

            return states;
        }

        /// <summary>Whether the way out can still be reached from here, with whatever the player may yet do.</summary>
        public static bool CanFinish(FoundryPlace start, FoundryProgress progress, DisciplineSet available)
        {
            foreach (FoundryProgress state in ReachableStates(start, progress, available))
            {
                if (Reachable(start, state, available).Contains(FoundryPlace.ExitYard))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Every way of leaving the exit court that works from a given state: what would be needed and
        /// which route it takes. Used to show that the routes are complete and genuinely different.
        /// </summary>
        public static List<ExitRoute> RoutesAvailable(FoundryProgress progress, DisciplineSet available)
        {
            var routes = new List<ExitRoute>();
            foreach (ExitRoute route in new[] { ExitRoute.Culvert, ExitRoute.Gate, ExitRoute.Breach })
            {
                if (RouteCanFinish(route, progress, available))
                {
                    routes.Add(route);
                }
            }

            return routes;
        }

        /// <summary>
        /// Whether the way out can be reached from the exit court by one route alone: the other routes'
        /// passages are treated as closed, so nothing of theirs is used.
        /// </summary>
        public static bool RouteCanFinish(ExitRoute route, FoundryProgress progress, DisciplineSet available)
        {
            bool brace = CanBrace(available);
            FoundryProgress state = progress;

            // Do every action the route allows from the court, as often as it opens something.
            for (int round = 0; round < Actions.Length; round++)
            {
                bool changed = false;
                HashSet<FoundryPlace> places = ReachableBy(route, FoundryPlace.ExitCourt, state, brace);
                foreach (FoundryAction action in Actions)
                {
                    if ((state & action.Grants) == action.Grants || !action.PossibleWith(available) || !CanStandAt(places, action) ||
                        !Serves(action.Grants, route))
                    {
                        continue;
                    }

                    state |= action.Grants;
                    changed = true;
                }

                if (!changed)
                {
                    break;
                }
            }

            return ReachableBy(route, FoundryPlace.ExitCourt, state, brace).Contains(FoundryPlace.ExitYard);
        }

        /// <summary>The disciplines a route needs from the exit court, all of them.</summary>
        public static DisciplineSet DisciplinesFor(ExitRoute route)
        {
            switch (route)
            {
                case ExitRoute.Culvert: return DisciplineSet.Flame | DisciplineSet.Aegis;
                case ExitRoute.Gate: return DisciplineSet.Arcane | DisciplineSet.Aegis;
                case ExitRoute.Breach: return DisciplineSet.Aegis;
                default: return DisciplineSet.None;
            }
        }

        /// <summary>The lasting change a route turns on, if any. The breach needs none: it is crossed while braced.</summary>
        public static FoundryProgress ChangeFor(ExitRoute route)
        {
            switch (route)
            {
                case ExitRoute.Culvert: return FoundryProgress.PlugBroken;
                case ExitRoute.Gate: return FoundryProgress.ExitGateOpen;
                default: return FoundryProgress.None;
            }
        }

        private static bool Serves(FoundryProgress grants, ExitRoute route)
        {
            return grants == ChangeFor(route);
        }

        private static HashSet<FoundryPlace> ReachableBy(ExitRoute route, FoundryPlace from, FoundryProgress progress, bool brace)
        {
            var seen = new HashSet<FoundryPlace> { from };
            var open = new Queue<FoundryPlace>();
            open.Enqueue(from);
            while (open.Count > 0)
            {
                FoundryPlace place = open.Dequeue();
                foreach (FoundryPassage passage in Passages)
                {
                    bool allowed = passage.Route == ExitRoute.None || passage.Route == route;
                    if (allowed && passage.From == place && passage.IsOpen(progress, brace) && seen.Add(passage.To))
                    {
                        open.Enqueue(passage.To);
                    }
                }
            }

            return seen;
        }

        private static bool CanStandAt(HashSet<FoundryPlace> places, in FoundryAction action)
        {
            foreach (FoundryPlace place in action.Places)
            {
                if (places.Contains(place))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The least the level must have changed for the player to stand at a place, starting from nothing.</summary>
        public static FoundryProgress LeastProgressToReach(FoundryPlace place, DisciplineSet available)
        {
            FoundryProgress best = FoundryProgress.None;
            int bestCount = int.MaxValue;
            foreach (FoundryProgress state in ReachableStates(FoundryPlace.Forecourt, FoundryProgress.None, available))
            {
                if (!Reachable(FoundryPlace.Forecourt, state, available).Contains(place))
                {
                    continue;
                }

                int count = CountBits((int)state);
                if (count < bestCount)
                {
                    best = state;
                    bestCount = count;
                }
            }

            return bestCount == int.MaxValue ? (FoundryProgress)(-1) : best;
        }

        private static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }

            return count;
        }
    }
}
