using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Threefold.Levels;
using Threefold.Objectives;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// Sprint LM-07: the Broken Foundry's geometry, checked against what the Mage can climb, catch and
    /// jump. The numbers are the ones the setup command builds the level from (<see cref="FoundryLayout"/>)
    /// and the motor's and traversal controller's defaults (<see cref="TraversalReach"/>). This checks the
    /// level's design by arithmetic; it cannot show that physics, the character controller or the camera
    /// behave as calculated, which needs Unity.
    /// </summary>
    public sealed class BrokenFoundryLayoutTests
    {
        private static readonly TraversalReach Reach = TraversalReach.Defaults;

        /// <summary>Extra height a ledge must clear beyond the highest catchable one, for what the arithmetic cannot know.</summary>
        private const float Margin = 0.4f;

        // ------------------------------------------------------------- reach itself

        [Test]
        public void TraversalReach_MatchesTheDocumentedFigures()
        {
            Assert.AreEqual(1.53f, Reach.ApexHeight, 0.05f, "A held jump rises about 1.5 m.");
            Assert.AreEqual(3.78f, Reach.HighestCatchableLedge, 0.06f, "Docs/Traversal.md: ledges up to about 3.75 m are caught.");
            Assert.That(Reach.LongestJump, Is.InRange(3.5f, 5f), "A sprinting jump clears about four metres.");
            Assert.IsTrue(Reach.EdgeGuardStops(2f));
            Assert.IsFalse(Reach.EdgeGuardStops(1.7f));
        }

        // ------------------------------------------------------------- doors, stairs, slopes

        [Test]
        public void EveryOpeningLetsTheMageThrough()
        {
            var openings = new (string Name, float Width, float Height)[]
            {
                ("Foundry gate", FoundryLayout.ForecourtGateX.y - FoundryLayout.ForecourtGateX.x, FoundryLayout.ForecourtGateHeight),
                ("Barricade doorway", FoundryLayout.BarricadeDoorX.y - FoundryLayout.BarricadeDoorX.x, FoundryLayout.BarricadeDoorHeight),
                ("Ore Hall north door", FoundryLayout.OreHallNorthDoorX.y - FoundryLayout.OreHallNorthDoorX.x, FoundryLayout.OreHallNorthDoorHeight),
                ("Stair door", FoundryLayout.StairDoorX.y - FoundryLayout.StairDoorX.x, FoundryLayout.StairDoorTop - FoundryLayout.LiftLedgeHeight),
                ("Casting floor entry", FoundryLayout.StairX.y - FoundryLayout.StairX.x, FoundryLayout.CastingFloorEntryHeight - FoundryLayout.StepRise),
                ("Guardian gate", FoundryLayout.GuardianGateX.y - FoundryLayout.GuardianGateX.x, FoundryLayout.GuardianGateHeight),
                ("Exit gate", FoundryLayout.ExitGateX.y - FoundryLayout.ExitGateX.x, FoundryLayout.ExitGateHeight),
                ("Culvert", FoundryLayout.CulvertX.y - FoundryLayout.CulvertX.x, FoundryLayout.CulvertHeight),
                ("Way out", FoundryLayout.WayOutX.y - FoundryLayout.WayOutX.x, FoundryLayout.WayOutHeight),
            };

            foreach ((string name, float width, float height) in openings)
            {
                Assert.IsTrue(Reach.FitsBetween(width), $"{name} is wide enough ({width} m).");
                Assert.IsTrue(Reach.FitsUnder(height), $"{name} is high enough ({height} m).");
            }
        }

        [Test]
        public void TheStairIsWalkedAndTheGantryIsNotTooSteep()
        {
            Assert.LessOrEqual(FoundryLayout.StepRise, Reach.StepOffset, "Each step is within the controller's step height.");
            Assert.AreEqual(0f, FoundryLayout.LiftLedgeHeight - FoundryLayout.StepRise * (FoundryLayout.StepCount + 1), 0.001f,
                "The steps come down exactly from the ledge to the casting floor.");
            Assert.LessOrEqual(FoundryLayout.GantryPitchDegrees, Reach.SlopeLimit - 10f, "The gantry is well within the slope limit.");
            Assert.AreEqual(FoundryLayout.BreachHeight, FoundryLayout.GantryRise, 0.001f, "The gantry ends at the breach.");
        }

        // ------------------------------------------------------------- what needs what

        [Test]
        public void TheLiftHallLedge_CannotBeReachedFromTheFloor_ButFromTheLiftAtItsStop()
        {
            float ledge = FoundryLayout.LiftLedgeHeight;
            Assert.Greater(ledge, Reach.HighestCatchableLedge + Margin, "From the floor it is out of reach.");
            Assert.LessOrEqual(ledge - FoundryLayout.SlideBlockHeight, Reach.HighestCatchableLedge - Margin,
                "From the lift's block it is within reach.");

            Surface[] hall = AreaSurfaces(FoundryArea.LiftHall);
            Surface start = Lift(FoundryLayout.RuneLift, 0f);
            Surface stop = Lift(FoundryLayout.RuneLift, FoundryLayout.SlideTravel);
            Assert.That(stop.GapTo(Surface.Of(FoundryLayout.Find("LiftLedge"))), Is.LessThan(0.3f), "At its stop the block is against the ledge.");

            Assert.IsFalse(Closure(hall.Append(start), "LiftHallFloor").Contains("LiftLedge"), "Not with the lift where it starts.");
            Assert.IsTrue(Closure(hall.Append(stop), "LiftHallFloor").Contains("LiftLedge"), "With the lift at its stop.");
        }

        [Test]
        public void TheSlagPit_CannotBeJumped_ButIsCrossedOnTheBridge_AndItsEdgeIsGuarded()
        {
            float span = FoundryLayout.PitZ.y - FoundryLayout.PitZ.x;
            Assert.Greater(span, Reach.LongestJump + 1.5f, "Far wider than the longest jump.");
            Assert.IsTrue(Reach.EdgeGuardStops(FoundryLayout.PitDepth), "Walking at the edge, the Mage stops instead of falling in.");

            float bridgeStart = FoundryLayout.Bridge.Position.z;
            float bridgeEnd = bridgeStart + FoundryLayout.BridgeLength;
            Assert.Less(bridgeStart, FoundryLayout.PitZ.x, "The bridge rests on the south landing.");
            Assert.Greater(bridgeEnd, FoundryLayout.PitZ.y, "And on the north landing.");

            Surface[] hall = AreaSurfaces(FoundryArea.OreHall);
            Assert.IsFalse(Closure(hall, "OreHallFloorSouth").Contains("OreHallFloorNorth"), "Without the bridge there is no way across.");
            Surface bridge = new Surface("Bridge", FoundryLayout.Bridge.Position.x - FoundryLayout.DeckWidth * 0.5f,
                FoundryLayout.Bridge.Position.x + FoundryLayout.DeckWidth * 0.5f, bridgeStart, bridgeEnd, 0.125f, -0.125f, true);
            Assert.IsTrue(Closure(hall.Append(bridge), "OreHallFloorSouth").Contains("OreHallFloorNorth"));

            // The slag is below the edge guard's depth and the hazard covers the whole pit.
            LayoutZone hazard = FoundryLayout.SlagHazard;
            Assert.AreEqual(FoundryLayout.OreHallX.y - FoundryLayout.OreHallX.x, hazard.Size.x, 0.001f);
            Assert.AreEqual(span, hazard.Size.z, 0.001f);
            Assert.IsTrue(hazard.Contains(new Vector3(0f, -FoundryLayout.PitDepth + 0.1f, -8f)), "A fall to the pit's floor lands in it.");
            Assert.IsTrue(hazard.Contains(new Vector3(3f, -1.875f, -8f)), "So does a fall onto the bridge's sunken deck.");
            Assert.IsFalse(hazard.Contains(new Vector3(3f, 0.125f, -8f)), "Standing on the bridge is not in it.");
        }

        [Test]
        public void TheBreach_CannotBeReachedFromTheCourt_ButFromTheGantry_AndTheWayDownIsWalkable()
        {
            Assert.Greater(FoundryLayout.BreachHeight, Reach.HighestCatchableLedge + Margin, "From the floor it is out of reach.");
            Assert.Greater(FoundryLayout.BreachHeight - FoundryLayout.SlideBlockHeight, Reach.HighestCatchableLedge,
                "Not even from the counterweight's block.");

            Surface[] court = AreaSurfaces(FoundryArea.ExitCourt).Concat(new[]
            {
                Lift(FoundryLayout.Counterweight, 0f), Lift(FoundryLayout.Counterweight, FoundryLayout.SlideTravel),
            }).ToArray();
            Assert.IsFalse(Closure(court, "ExitCourtFloor").Contains("Breach"));
            Assert.IsTrue(Closure(court.Concat(GantrySegments()).ToArray(), "ExitCourtFloor").Contains("Breach"));

            // Breach, high block, low block, floor: each drop shallower than the edge guard, so it is walked off,
            // and each a step that can be climbed back.
            float[] tops = { FoundryLayout.BreachHeight, FoundryLayout.DescentHighTop, FoundryLayout.DescentLowTop, 0f };
            for (int i = 1; i < tops.Length; i++)
            {
                float drop = tops[i - 1] - tops[i];
                Assert.IsFalse(Reach.EdgeGuardStops(drop), $"The drop to {tops[i]} m is walked off.");
                Assert.IsTrue(Reach.CanReachLedge(drop), $"And can be climbed back ({drop} m).");
            }
        }

        // ------------------------------------------------------------- nothing unintended

        [Test]
        public void NoWallTopOrColumnTopCanBeReached_FromAnywhereThePlayerCanStand()
        {
            // Everything the player may stand on: floors, the ledge, the steps, the breach and its way down,
            // props, scaffolds, the lift and the counterweight at both ends, the bridge, and the gantry.
            var surfaces = new List<Surface>();
            foreach (LayoutBox box in FoundryLayout.Solids)
            {
                if (box.Name != PitFloor)
                {
                    surfaces.Add(Surface.Of(box));
                }
            }

            foreach (PropPlacement prop in FoundryLayout.Props)
            {
                Vector3 size = FoundryLayout.PropSize(prop.Kind);
                if (size.y > 0f && FoundryLayout.PropHasFlatTop(prop.Kind))
                {
                    surfaces.Add(Surface.Square(prop.Kind.ToString(), prop.Point.Position, Mathf.Max(size.x, size.z) * 0.71f * 2f, size.y, true));
                }
            }

            foreach (LayoutPoint column in FoundryLayout.Columns)
            {
                surfaces.Add(Surface.Square("Column", column.Position, FoundryLayout.ColumnCapitalWidth, FoundryLayout.ColumnTop, false));
            }

            foreach (LayoutPoint scaffold in FoundryLayout.Scaffolds)
            {
                surfaces.Add(Surface.Square("Scaffold", scaffold.Position, FoundryLayout.ScaffoldWidth, FoundryLayout.ScaffoldHeight, true));
            }

            foreach (LayoutPoint slide in new[] { FoundryLayout.RuneLift, FoundryLayout.Counterweight })
            {
                surfaces.Add(Lift(slide, 0f));
                surfaces.Add(Lift(slide, FoundryLayout.SlideTravel));
            }

            surfaces.Add(new Surface("Bridge", 2.1f, 3.9f, FoundryLayout.Bridge.Position.z, FoundryLayout.Bridge.Position.z + FoundryLayout.BridgeLength,
                0.125f, -0.125f, true));
            surfaces.AddRange(GantrySegments());

            // The furnace's chimney top is not to be reached either; the strip in front of it is too narrow to stand on.
            Vector3 furnace = FoundryLayout.Furnace.Position;
            surfaces.Add(new Surface("FurnaceChimney", -10f, -10f + FoundryLayout.FurnaceChimneyDepth, furnace.z - FoundryLayout.FurnaceChimneyWidth * 0.5f,
                furnace.z + FoundryLayout.FurnaceChimneyWidth * 0.5f, FoundryLayout.FurnaceChimneyTop, FoundryLayout.FurnaceBaseHeight, false));
            Assert.IsFalse(Reach.CanStandOn(FoundryLayout.FurnaceWidth, FoundryLayout.FurnaceDepth - FoundryLayout.FurnaceChimneyDepth),
                "The top of the furnace's base, in front of the chimney, is too narrow to stand on.");

            string[] starts = surfaces.Where(s => s.Allowed && s.Top <= 0.2f && s.Large).Select(s => s.Name).ToArray();
            Dictionary<string, string> reachedFrom = ClosureWithPaths(surfaces, starts);

            var forbidden = new List<string>();
            foreach (Surface surface in surfaces)
            {
                if (!surface.Allowed && reachedFrom.ContainsKey(surface.Name) && Standable(surface, surfaces))
                {
                    forbidden.Add($"{surface.Name} (from {reachedFrom[surface.Name]})");
                }
            }

            CollectionAssert.IsEmpty(forbidden, "Places nobody should reach: " + string.Join(", ", forbidden));
        }

        [Test]
        public void TheCheckpointsLieOnTheFloorTheyBelongTo_AndTheirRespawnPointsAreClear()
        {
            Assert.AreEqual(Enumerable.Range(0, FoundryLayout.Checkpoints.Length), FoundryLayout.Checkpoints.Select(c => c.Order),
                "Numbered in the order they are played.");

            foreach (CheckpointLayout checkpoint in FoundryLayout.Checkpoints)
            {
                Vector3 respawn = checkpoint.Respawn.Position;
                Assert.IsFalse(FoundryLayout.InsideAnySolid(respawn + Vector3.up * 0.95f, Reach.CapsuleRadius),
                    $"'{checkpoint.Name}': the Mage fits at the respawn point.");
                Assert.IsTrue(StandsOnWalkable(respawn), $"'{checkpoint.Name}': the respawn point is on a floor.");
                Assert.IsTrue(StandsOnWalkable(checkpoint.Zone.BaseCentre), $"'{checkpoint.Name}': its box stands on a floor.");
                Assert.IsFalse(FoundryLayout.SlagHazard.Contains(respawn), $"'{checkpoint.Name}': nobody comes back into the slag.");
            }

            Assert.IsTrue(FoundryLayout.Checkpoints[0].Zone.Contains(FoundryLayout.Spawn.Position), "The first checkpoint is where the level starts.");
            Assert.IsTrue(StandsOnWalkable(FoundryLayout.Spawn.Position));
        }

        [Test]
        public void TheMagicalObjectsFitWhereTheyStand()
        {
            // The rune-locked slide's block and travel are what the layout counts on.
            MagicalPropertySettings slide = CombinationDefaults.Settings(CombinationExample.RuneLockedSlide);
            Assert.AreEqual(FoundryLayout.SlideTravel, slide.kineticSettings.maxTravel, 0.001f);
            Assert.AreEqual(0f, slide.kineticSettings.minTravel, 0.001f);

            // At its stop the counterweight is clear of the exit wall, and its block does not stand in the gate's path.
            Surface weight = Lift(FoundryLayout.Counterweight, FoundryLayout.SlideTravel);
            Assert.Less(weight.MaxZ, FoundryLayout.ExitCourtZ.y, "Clear of the wall, so the push is not blocked.");
            Assert.Less(weight.MaxX, FoundryLayout.ExitGateX.x, "Not in front of the exit gate.");
            Assert.Greater(weight.MinX, FoundryLayout.CulvertX.y, "Not in front of the culvert.");

            // The lift's block stops short of the ledge, and the slag plug and the barricade fill their openings.
            Surface lift = Lift(FoundryLayout.RuneLift, FoundryLayout.SlideTravel);
            Assert.That(FoundryLayout.LiftLedgeSouthZ - lift.MaxZ, Is.InRange(0.05f, 0.3f));
            Assert.AreEqual((FoundryLayout.CulvertX.x + FoundryLayout.CulvertX.y) * 0.5f, FoundryLayout.SlagPlug.Position.x, 0.001f);
            Assert.AreEqual((FoundryLayout.BarricadeDoorX.x + FoundryLayout.BarricadeDoorX.y) * 0.5f, FoundryLayout.DamagedBarricade.Position.x, 0.001f);
            Assert.That(FoundryLayout.DamagedBarricade.Position.z, Is.InRange(FoundryLayout.PartitionZ.x, FoundryLayout.PartitionZ.y));
        }

        [Test]
        public void TheFurnacesHeat_ReachesIntoTheHall_ButNotTheGuardiansPatrol_SoTheGuardianMustBeLured()
        {
            Vector3 mouth = FoundryLayout.Furnace.Position + RotateYaw(FoundryLayout.FurnaceMouthLocal, FoundryLayout.Furnace.Yaw);
            Assert.Greater(mouth.x, FoundryLayout.CastingFloorX.x + FoundryLayout.FurnaceDepth, "The heat comes from in front of the furnace.");

            const float guardianRadius = 0.8f;
            foreach (Vector3 point in FoundryLayout.GuardianRoute)
            {
                float distance = Vector2.Distance(new Vector2(mouth.x, mouth.z), new Vector2(point.x, point.z));
                Assert.Greater(distance, FoundryDefaults.FurnaceHeatRadius + guardianRadius,
                    "On its patrol the Guardian stays out of the furnace's heat; luring it there is the player's idea.");
            }

            // A scaffold stands where its fall reaches the patrol: the collapse sends force 3.5 within 3.5 m.
            float collapseRadius = WorldMagicDefaults.Settings(ExampleObject.UnstableStructure).emissions[0].radius;
            bool any = false;
            foreach (LayoutPoint scaffold in FoundryLayout.Scaffolds)
            {
                float nearest = DistanceToSegment(scaffold.Position, FoundryLayout.GuardianRoute[0], FoundryLayout.GuardianRoute[1]);
                any |= nearest < collapseRadius;
            }

            Assert.IsTrue(any, "At least one scaffold can come down on the Guardian as it patrols.");
        }

        // ------------------------------------------------------------- helpers

        private readonly struct Surface
        {
            public readonly string Name;
            public readonly float MinX, MaxX, MinZ, MaxZ, Top, Bottom;
            public readonly bool Allowed;

            public Surface(string name, float minX, float maxX, float minZ, float maxZ, float top, float bottom, bool allowed)
            {
                Name = name;
                MinX = minX;
                MaxX = maxX;
                MinZ = minZ;
                MaxZ = maxZ;
                Top = top;
                Bottom = bottom;
                Allowed = allowed;
            }

            public static Surface Of(LayoutBox box) =>
                new Surface(box.Name, box.Min.x, box.Max.x, box.Min.z, box.Max.z, box.Max.y, box.Min.y, box.Walkable);

            public static Surface Square(string name, Vector3 centre, float width, float top, bool allowed) =>
                new Surface($"{name} at {centre.x},{centre.z}", centre.x - width * 0.5f, centre.x + width * 0.5f, centre.z - width * 0.5f,
                    centre.z + width * 0.5f, top, 0f, allowed);

            public float Width => MaxX - MinX;
            public float Depth => MaxZ - MinZ;

            /// <summary>Big enough to take a run-up on.</summary>
            public bool Large => Mathf.Max(Width, Depth) >= 3f;

            public float GapTo(in Surface other)
            {
                float dx = Mathf.Max(MinX - other.MaxX, 0f, other.MinX - MaxX);
                float dz = Mathf.Max(MinZ - other.MaxZ, 0f, other.MinZ - MaxZ);
                return Mathf.Sqrt(dx * dx + dz * dz);
            }

            public bool Covers(in Surface other)
            {
                return MinX <= other.MinX + 0.01f && MaxX >= other.MaxX - 0.01f && MinZ <= other.MinZ + 0.01f && MaxZ >= other.MaxZ - 0.01f;
            }
        }

        private static Surface Lift(LayoutPoint slide, float travel)
        {
            float half = FoundryLayout.SlideBlockDepth * 0.5f;
            float z = slide.Position.z + travel;
            return new Surface($"Slide at {slide.Position.z}+{travel}", slide.Position.x - half, slide.Position.x + half, z - half, z + half,
                FoundryLayout.SlideBlockHeight, 0f, true);
        }

        /// <summary>The gantry as six level pieces, from its foot to the breach.</summary>
        private static IEnumerable<Surface> GantrySegments()
        {
            const int pieces = 6;
            LayoutPoint foot = FoundryLayout.Gantry;
            float half = FoundryLayout.DeckWidth * 0.5f;
            for (int i = 0; i < pieces; i++)
            {
                float z0 = foot.Position.z + FoundryLayout.GantryRun * i / pieces;
                float z1 = foot.Position.z + FoundryLayout.GantryRun * (i + 1) / pieces;
                float top = FoundryLayout.GantryRise * (i + 1) / pieces;
                yield return new Surface($"Gantry{i + 1}", foot.Position.x - half, foot.Position.x + half, z0, z1, top, top - 0.3f, true);
            }
        }

        /// <summary>The slag pit's floor is not a place to stand: falling in is the hazard, which puts the player back.</summary>
        private const string PitFloor = "SlagPitFloor";

        private static Surface[] AreaSurfaces(FoundryArea area)
        {
            return FoundryLayout.SolidsIn(area).Where(box => box.Name != PitFloor).Select(Surface.Of).ToArray();
        }

        private static HashSet<string> Closure(IEnumerable<Surface> surfaces, string start)
        {
            return new HashSet<string>(ClosureWithPaths(surfaces.ToList(), new[] { start }).Keys);
        }

        /// <summary>
        /// Every surface reachable from the starts by climbing, catching, stepping or jumping, with the
        /// surface each was first reached from. A climb must be within reach and close enough to get to:
        /// from a large surface with a run-up, a small one without. A solid whose underside is more than a
        /// head above the surface is an overhang or a ceiling there: its top cannot be climbed to from it.
        /// </summary>
        private static Dictionary<string, string> ClosureWithPaths(List<Surface> surfaces, IEnumerable<string> starts)
        {
            var reached = new Dictionary<string, string>();
            var open = new Queue<Surface>();
            foreach (string start in starts)
            {
                Surface first = surfaces.First(s => s.Name == start);
                if (!reached.ContainsKey(first.Name))
                {
                    reached[first.Name] = "start";
                    open.Enqueue(first);
                }
            }

            while (open.Count > 0)
            {
                Surface from = open.Dequeue();
                if (!from.Allowed)
                {
                    continue;
                }

                foreach (Surface to in surfaces)
                {
                    if (reached.ContainsKey(to.Name) || !CanGo(from, to))
                    {
                        continue;
                    }

                    reached[to.Name] = from.Name;
                    open.Enqueue(to);
                }
            }

            return reached;
        }

        private static bool CanGo(in Surface from, in Surface to)
        {
            float climb = to.Top - from.Top;
            if (climb > Reach.HighestCatchableLedge || to.Bottom > from.Top + Reach.CapsuleHeight + 0.1f)
            {
                return false;
            }

            float gap = from.GapTo(to);
            float allowed;
            if (climb > 0.3f)
            {
                allowed = from.Large ? 2.6f : 2f;
            }
            else
            {
                allowed = from.Large ? Reach.JumpDistance(Reach.SprintSpeed, Mathf.Max(0f, -climb)) : 2.5f;
            }

            return gap <= allowed;
        }

        /// <summary>A top that has room to stand and nothing resting on it.</summary>
        private static bool Standable(in Surface surface, List<Surface> all)
        {
            if (!Reach.CanStandOn(surface.Width, surface.Depth))
            {
                return false;
            }

            foreach (Surface other in all)
            {
                if (other.Name != surface.Name && Mathf.Abs(other.Bottom - surface.Top) < 0.05f && other.Covers(surface))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool StandsOnWalkable(Vector3 point)
        {
            foreach (LayoutBox box in FoundryLayout.Solids)
            {
                if (box.Walkable && Mathf.Abs(box.Top - point.y) < 0.05f && box.HorizontalDistance(point) <= 0f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A turn about the vertical, as Quaternion.Euler(0, yaw, 0) would give, without the engine.</summary>
        private static Vector3 RotateYaw(Vector3 v, float yaw)
        {
            float radians = yaw * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector3(v.x * cos + v.z * sin, v.y, -v.x * sin + v.z * cos);
        }

        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            var p = new Vector2(point.x, point.z);
            var u = new Vector2(a.x, a.z);
            var v = new Vector2(b.x, b.z);
            Vector2 along = v - u;
            float t = Mathf.Clamp01(Vector2.Dot(p - u, along) / Mathf.Max(0.0001f, along.sqrMagnitude));
            return Vector2.Distance(p, u + along * t);
        }
    }

    internal static class SurfaceArrayExtensions
    {
        public static T[] Append<T>(this T[] array, T item)
        {
            var result = new T[array.Length + 1];
            array.CopyTo(result, 0);
            result[array.Length] = item;
            return result;
        }
    }
}
