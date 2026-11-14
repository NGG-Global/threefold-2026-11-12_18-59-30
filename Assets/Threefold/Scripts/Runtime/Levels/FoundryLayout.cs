using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Levels
{
    /// <summary>How a solid of the level is finished. The setup command picks a material for each.</summary>
    public enum FoundrySurface
    {
        Paving = 0,
        Stone = 1,
        DarkStone = 2,
        Metal = 3,
    }

    /// <summary>The parts of the Broken Foundry, in the order the player comes through them.</summary>
    public enum FoundryArea
    {
        Forecourt = 0,
        OreHall = 1,
        LiftHall = 2,
        StairCorridor = 3,
        CastingFloor = 4,
        ExitCourt = 5,
        ExitYard = 6,
    }

    /// <summary>An axis-aligned solid of the level: a floor, a wall piece, a ledge, a step. Plain data.</summary>
    public readonly struct LayoutBox
    {
        public readonly string Name;
        public readonly Vector3 Min;
        public readonly Vector3 Max;
        public readonly FoundrySurface Surface;
        public readonly FoundryArea Area;

        /// <summary>Whether its top is meant to be stood on (a floor, a ledge, a step), as opposed to a wall top nobody should reach.</summary>
        public readonly bool Walkable;

        public LayoutBox(string name, Vector3 min, Vector3 max, FoundrySurface surface, FoundryArea area, bool walkable)
        {
            Name = name;
            Min = Vector3.Min(min, max);
            Max = Vector3.Max(min, max);
            Surface = surface;
            Area = area;
            Walkable = walkable;
        }

        public Vector3 Size => Max - Min;
        public Vector3 Centre => (Min + Max) * 0.5f;

        /// <summary>The centre of its underside, which is how the setup's block helper places a block.</summary>
        public Vector3 BaseCentre => new Vector3((Min.x + Max.x) * 0.5f, Min.y, (Min.z + Max.z) * 0.5f);

        public float Top => Max.y;

        public bool Contains(Vector3 point, float margin = 0f)
        {
            return point.x > Min.x - margin && point.x < Max.x + margin &&
                   point.y > Min.y - margin && point.y < Max.y + margin &&
                   point.z > Min.z - margin && point.z < Max.z + margin;
        }

        /// <summary>Horizontal distance from a point to the box's footprint; 0 inside it.</summary>
        public float HorizontalDistance(Vector3 point)
        {
            float dx = Mathf.Max(Min.x - point.x, 0f, point.x - Max.x);
            float dz = Mathf.Max(Min.z - point.z, 0f, point.z - Max.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Horizontal gap between two footprints; 0 if they touch or overlap.</summary>
        public float HorizontalGap(in LayoutBox other)
        {
            float dx = Mathf.Max(Min.x - other.Max.x, 0f, other.Min.x - Max.x);
            float dz = Mathf.Max(Min.z - other.Max.z, 0f, other.Min.z - Max.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public override string ToString() => $"{Name} [{Min} .. {Max}]";
    }

    /// <summary>Where something stands, and which way it faces (degrees about the vertical, 0 = north, +z).</summary>
    public readonly struct LayoutPoint
    {
        public readonly Vector3 Position;
        public readonly float Yaw;

        public LayoutPoint(Vector3 position, float yaw)
        {
            Position = position;
            Yaw = yaw;
        }
    }

    /// <summary>A box-shaped region that notices the player, given by the centre of its underside and its size.</summary>
    public readonly struct LayoutZone
    {
        public readonly Vector3 BaseCentre;
        public readonly Vector3 Size;

        public LayoutZone(Vector3 baseCentre, Vector3 size)
        {
            BaseCentre = baseCentre;
            Size = size;
        }

        public Vector3 Centre => BaseCentre + Vector3.up * (Size.y * 0.5f);

        public bool Contains(Vector3 point)
        {
            Vector3 local = point - Centre;
            return Mathf.Abs(local.x) <= Size.x * 0.5f && Mathf.Abs(local.y) <= Size.y * 0.5f && Mathf.Abs(local.z) <= Size.z * 0.5f;
        }
    }

    /// <summary>Pieces of machinery and ruin that stand on the floor.</summary>
    public enum FoundryProp
    {
        OreCart = 0,
        Crucible = 1,
        BrokenColumn = 2,
        Rubble = 3,
    }

    /// <summary>Where one prop stands.</summary>
    public readonly struct PropPlacement
    {
        public readonly FoundryProp Kind;
        public readonly LayoutPoint Point;

        public PropPlacement(FoundryProp kind, Vector3 position, float yaw)
        {
            Kind = kind;
            Point = new LayoutPoint(position, yaw);
        }
    }

    /// <summary>One checkpoint: where it is reached, where the player comes back, and the place it stands in.</summary>
    public readonly struct CheckpointLayout
    {
        public readonly string Name;
        public readonly int Order;
        public readonly LayoutZone Zone;
        public readonly LayoutPoint Respawn;
        public readonly FoundryPlace Place;

        public CheckpointLayout(string name, int order, LayoutZone zone, LayoutPoint respawn, FoundryPlace place)
        {
            Name = name;
            Order = order;
            Zone = zone;
            Respawn = respawn;
            Place = place;
        }
    }

    /// <summary>
    /// The geometry of the Broken Foundry, as plain numbers. The setup command builds the level from
    /// these and nothing else, and the tests check the same numbers against what the Mage can and
    /// cannot climb, jump and walk (<see cref="TraversalReach"/>), so a claim such as "the lift hall's
    /// ledge cannot be reached from the floor" is checked against the level that is actually built.
    ///
    /// Coordinates: x east, y up, z north, in metres. The player starts in the south and goes north.
    ///
    ///   Forecourt     z -38..-24  A walled yard before the foundry gate and the dormant crystal.
    ///   Ore Hall      z -23..-1   A damaged barricade in a partition, then a slag pit with a crumbling bridge.
    ///   Lift Hall     z   0..14   A 4.4 m ledge, reached from the rune lift once it is slid beneath it.
    ///   Stair         z  15..22   An enclosed stair down from the ledge.
    ///   Casting Floor z  22..42   The Ruins Guardian, the furnace and two loose scaffolds.
    ///   Exit Court    z  43..57   The sealed exit, with three ways through its 9.5 m wall.
    ///   Exit Yard     z  58..68   Beyond the wall: the way out, through an opening onto a short passage.
    /// </summary>
    public static class FoundryLayout
    {
        public const string SceneName = "BrokenFoundry";

        /// <summary>The prefix of every persistent id and puzzle key in the level, so the level can be forgotten on its own.</summary>
        public const string KeyPrefix = SceneName + "/";

        public const float WallThickness = 1f;
        public const float FloorThickness = 0.6f;

        public const float ForecourtWallHeight = 7f;
        public const float OreHallWallHeight = 7f;
        public const float CastingFloorWallHeight = 8f;

        /// <summary>The walls of every part with a raised place to stand on: high enough that nothing can be climbed from there.</summary>
        public const float HighWallHeight = 9.5f;

        public const float PitDepth = 2.4f;
        public const float LiftLedgeHeight = 4.4f;
        public const float BreachHeight = 5f;

        public const float StepRise = 0.2f;
        public const float StepTread = 0.3f;

        // ------------------------------------------------------------ areas

        public static readonly Vector2 ForecourtX = new Vector2(-10f, 10f);
        public static readonly Vector2 ForecourtZ = new Vector2(-38f, -24f);

        public static readonly Vector2 OreHallX = new Vector2(-9f, 9f);
        public static readonly Vector2 OreHallZ = new Vector2(-23f, -1f);
        public static readonly Vector2 PartitionZ = new Vector2(-16f, -15f);
        public static readonly Vector2 PitZ = new Vector2(-11.5f, -4.5f);

        public static readonly Vector2 LiftHallX = new Vector2(-8f, 8f);
        public static readonly Vector2 LiftHallZ = new Vector2(0f, 14f);
        public const float LiftLedgeSouthZ = 10f;

        public static readonly Vector2 StairX = new Vector2(4f, 6.6f);
        public static readonly Vector2 StairZ = new Vector2(15f, 22f);
        public const float StairRoofHeight = 7.4f;

        public static readonly Vector2 CastingFloorX = new Vector2(-10f, 10f);
        public static readonly Vector2 CastingFloorZ = new Vector2(22f, 42f);

        public static readonly Vector2 ExitCourtX = new Vector2(-9f, 9f);
        public static readonly Vector2 ExitCourtZ = new Vector2(43f, 57f);
        public const float ExitWallDepth = 1.2f;

        public static readonly Vector2 ExitYardX = new Vector2(-9f, 9f);
        public static readonly Vector2 ExitYardZ = new Vector2(58.2f, 68f);

        // ------------------------------------------------------------ openings

        public static readonly Vector2 ForecourtGateX = new Vector2(-2f, 2f);
        public const float ForecourtGateHeight = 4f;

        public static readonly Vector2 BarricadeDoorX = new Vector2(-1.3f, 1.3f);
        public const float BarricadeDoorHeight = 2.8f;

        public static readonly Vector2 OreHallNorthDoorX = new Vector2(-1.5f, 1.5f);
        public const float OreHallNorthDoorHeight = 3.2f;

        /// <summary>The doorway from the lift hall's ledge into the stair: its sill is the ledge.</summary>
        public static readonly Vector2 StairDoorX = StairX;
        public const float StairDoorTop = 7.4f;

        public const float CastingFloorEntryHeight = 3f;
        public static readonly Vector2 GuardianGateX = new Vector2(-2f, 2f);
        public const float GuardianGateHeight = 4f;

        public static readonly Vector2 ExitGateX = new Vector2(-2f, 2f);
        public const float ExitGateHeight = 4.5f;

        public static readonly Vector2 CulvertX = new Vector2(-7.2f, -4.8f);
        public const float CulvertHeight = 2.6f;

        public static readonly Vector2 BreachX = new Vector2(4.6f, 7f);

        /// <summary>The way out: an opening in the exit yard's north wall onto a short passage open to the sky.</summary>
        public static readonly Vector2 WayOutX = new Vector2(-2f, 2f);
        public const float WayOutHeight = 4.5f;
        public static readonly Vector2 PassageX = new Vector2(-2.5f, 2.5f);
        public static readonly Vector2 PassageZ = new Vector2(69f, 75f);

        /// <summary>
        /// An invisible wall across the passage, beyond the last checkpoint, so nobody walks out of the level.
        /// It is on the Ignore Raycast layer: it stops the character but not the camera, aim or projectiles.
        /// </summary>
        public static readonly LayoutBox OutwardBlocker = new LayoutBox("OutwardBlocker", new Vector3(PassageX.x, 0f, 72f),
            new Vector3(PassageX.y, 6f, 72.5f), FoundrySurface.Stone, FoundryArea.ExitYard, false);

        // ------------------------------------------------------------ magical objects and mechanisms

        public static readonly LayoutPoint Spawn = new LayoutPoint(new Vector3(0f, 0f, -34.5f), 0f);

        public static readonly LayoutPoint DormantCrystal = new LayoutPoint(new Vector3(-4.5f, 0f, -27.5f), 35f);
        public static readonly LayoutPoint ForecourtGate = new LayoutPoint(new Vector3(0f, 0f, -23.5f), 0f);

        /// <summary>The damaged barricade fills the partition's doorway exactly.</summary>
        public static readonly LayoutPoint DamagedBarricade = new LayoutPoint(new Vector3(0f, 0f, -15.5f), 0f);

        /// <summary>The bridge's deck runs north from here across the pit, its ends resting on the two landings.</summary>
        public static readonly LayoutPoint Bridge = new LayoutPoint(new Vector3(3f, 0f, -11.8f), 0f);
        public const float BridgeLength = 7.6f;

        /// <summary>
        /// The rune lift, a rune-locked slide of LM-06: its rail runs north, and at the far stop the
        /// block's far face is 0.15 m from the ledge.
        /// </summary>
        public static readonly LayoutPoint RuneLift = new LayoutPoint(new Vector3(-3.2f, 0f, 3.95f), 0f);

        /// <summary>Travel, size and height of the rune-locked slide's block, as its prefab builds it.</summary>
        public const float SlideTravel = 5f;
        public const float SlideBlockDepth = 1.8f;
        public const float SlideBlockHeight = 1.2f;

        public static readonly LayoutPoint Furnace = new LayoutPoint(new Vector3(-8.9f, 0f, 31f), 90f);

        /// <summary>The furnace's footprint along the wall, its depth from the wall, and the height of its base.</summary>
        public const float FurnaceWidth = 3.6f;
        public const float FurnaceDepth = 2.2f;
        public const float FurnaceBaseHeight = 2.4f;
        public const float FurnaceChimneyDepth = 1.7f;
        public const float FurnaceChimneyWidth = 2.8f;
        public const float FurnaceChimneyTop = 9f;

        /// <summary>Where the furnace's heat comes from: just in front of its mouth.</summary>
        public static readonly Vector3 FurnaceMouthLocal = new Vector3(0f, 1f, FurnaceDepth * 0.5f + 0.3f);

        /// <summary>
        /// The two loose scaffolds, each within toppling reach of the Guardian's patrol, and each more
        /// than three metres from a column: a column's square top must not be a jump away from a scaffold's.
        /// </summary>
        public static readonly LayoutPoint[] Scaffolds =
        {
            new LayoutPoint(new Vector3(6f, 0f, 30f), 0f),
            new LayoutPoint(new Vector3(-2.5f, 0f, 37f), 0f),
        };

        /// <summary>Footprint and height of a loose scaffold (the unstable structure of LM-03).</summary>
        public const float ScaffoldWidth = 1.3f;
        public const float ScaffoldHeight = 3.2f;

        public static readonly LayoutPoint[] Columns =
        {
            new LayoutPoint(new Vector3(-7f, 0f, 25f), 0f),
            new LayoutPoint(new Vector3(7f, 0f, 25f), 0f),
            new LayoutPoint(new Vector3(-7f, 0f, 39.5f), 0f),
            new LayoutPoint(new Vector3(7f, 0f, 39.5f), 0f),
        };

        /// <summary>
        /// Machinery and ruin standing on the floor. The carts and crucibles have flat tops a player can
        /// stand on, so each stands well away from any wall or column whose top it could otherwise lead to;
        /// broken columns have round tops that cannot be caught, and rubble does not collide.
        /// </summary>
        public static readonly PropPlacement[] Props =
        {
            new PropPlacement(FoundryProp.BrokenColumn, new Vector3(-7.5f, 0f, -35.5f), 20f),
            new PropPlacement(FoundryProp.BrokenColumn, new Vector3(7.5f, 0f, -32f), 140f),
            new PropPlacement(FoundryProp.Rubble, new Vector3(-6f, 0f, -31f), 30f),
            new PropPlacement(FoundryProp.Rubble, new Vector3(5f, 0f, -36.5f), 200f),
            new PropPlacement(FoundryProp.OreCart, new Vector3(6f, 0f, -28.5f), 20f),
            new PropPlacement(FoundryProp.OreCart, new Vector3(-5f, 0f, -19.5f), 0f),
            new PropPlacement(FoundryProp.Crucible, new Vector3(-6.5f, 0f, -13.2f), 0f),
            new PropPlacement(FoundryProp.Crucible, new Vector3(8.2f, 0f, 34.5f), 40f),
            new PropPlacement(FoundryProp.Crucible, new Vector3(-8.3f, 0f, 35.5f), 0f),
            new PropPlacement(FoundryProp.Rubble, new Vector3(-6f, 0f, 65f), 80f),
            new PropPlacement(FoundryProp.Rubble, new Vector3(7.5f, 0f, 66f), 10f),
        };

        /// <summary>The space a prop's collider takes (width, height, depth), or zero for a prop that does not collide.</summary>
        public static Vector3 PropSize(FoundryProp kind)
        {
            switch (kind)
            {
                case FoundryProp.OreCart: return new Vector3(1.2f, 1.1f, 1.8f);
                case FoundryProp.Crucible: return new Vector3(1.3f, 1.4f, 1.3f);
                case FoundryProp.BrokenColumn: return new Vector3(1.1f, 2.6f, 1.1f);
                default: return Vector3.zero;
            }
        }

        /// <summary>Whether the top of a prop is flat, so that it can be caught and stood on.</summary>
        public static bool PropHasFlatTop(FoundryProp kind)
        {
            return kind == FoundryProp.OreCart || kind == FoundryProp.Crucible;
        }

        /// <summary>The column modules of the environment: a square capital 1.2 m wide on top, 5.3 m up.</summary>
        public const float ColumnTop = 5.3f;
        public const float ColumnCapitalWidth = 1.2f;

        public static readonly LayoutPoint Guardian = new LayoutPoint(new Vector3(0f, 0f, 34f), 180f);
        public static readonly Vector3[] GuardianRoute = { new Vector3(-4f, 0f, 34f), new Vector3(4f, 0f, 34f) };
        public static readonly LayoutPoint GuardianGate = new LayoutPoint(new Vector3(0f, 0f, 42.5f), 0f);

        public static readonly LayoutPoint ExitGate = new LayoutPoint(new Vector3(0f, 0f, 57.6f), 0f);
        public static readonly LayoutPoint SlagPlug = new LayoutPoint(new Vector3(-6f, 0f, 57.6f), 0f);

        /// <summary>The counterweight, a second rune-locked slide: at its far stop its block is 0.4 m from the exit wall.</summary>
        public static readonly LayoutPoint Counterweight = new LayoutPoint(new Vector3(-3.2f, 0f, 50.7f), 0f);

        /// <summary>The ramp's foot. It rises north to the breach.</summary>
        public static readonly LayoutPoint Gantry = new LayoutPoint(new Vector3(5.8f, 0f, 48f), 0f);
        public static float GantryRun => ExitCourtZ.y - Gantry.Position.z;
        public static float GantryRise => BreachHeight;
        public static float GantryLength => Mathf.Sqrt(GantryRun * GantryRun + GantryRise * GantryRise);
        public static float GantryPitchDegrees => Mathf.Atan2(GantryRise, GantryRun) * Mathf.Rad2Deg;

        /// <summary>The width of a bridge or gantry deck.</summary>
        public const float DeckWidth = 1.8f;

        // ------------------------------------------------------------ zones

        public static readonly LayoutZone SlagHazard =
            new LayoutZone(new Vector3(0f, -PitDepth, (PitZ.x + PitZ.y) * 0.5f), new Vector3(OreHallX.y - OreHallX.x, 1.2f, PitZ.y - PitZ.x));

        /// <summary>Damage the slag deals before the player is put back at the last checkpoint.</summary>
        public const float SlagDamage = 25f;

        public static readonly CheckpointLayout[] Checkpoints =
        {
            new CheckpointLayout("Forecourt", 0, new LayoutZone(new Vector3(0f, 0f, -34.5f), new Vector3(6f, 3f, 4f)),
                Spawn, FoundryPlace.Forecourt),
            new CheckpointLayout("Ore Hall", 1, new LayoutZone(new Vector3(0f, 0f, -13.6f), new Vector3(6f, 3f, 2.4f)),
                new LayoutPoint(new Vector3(3f, 0f, -13f), 0f), FoundryPlace.OreHallLanding),
            new CheckpointLayout("Lift Hall", 2, new LayoutZone(new Vector3(0f, 0f, 1.6f), new Vector3(3f, 3f, 2f)),
                new LayoutPoint(new Vector3(0f, 0f, 2f), 0f), FoundryPlace.LiftHall),
            new CheckpointLayout("Upper Stair", 3, new LayoutZone(new Vector3(5.3f, LiftLedgeHeight, 14.5f), new Vector3(2.6f, 3f, 1f)),
                new LayoutPoint(new Vector3(5.3f, LiftLedgeHeight, 12.6f), 0f), FoundryPlace.LiftLedge),
            new CheckpointLayout("Exit Court", 4, new LayoutZone(new Vector3(0f, 0f, 44.4f), new Vector3(4f, 3f, 1.6f)),
                new LayoutPoint(new Vector3(0f, 0f, 44.8f), 0f), FoundryPlace.ExitCourt),
            new CheckpointLayout("Way Out", 5, new LayoutZone(new Vector3(0f, 0f, 64.5f), new Vector3(18f, 4f, 7f)),
                new LayoutPoint(new Vector3(0f, 0f, 63f), 0f), FoundryPlace.ExitYard),
        };

        /// <summary>The last checkpoint: reaching it is reaching the way out, which completes the level.</summary>
        public static CheckpointLayout ExitCheckpoint => Checkpoints[Checkpoints.Length - 1];

        // ------------------------------------------------------------ solids

        private static List<LayoutBox> solids;

        /// <summary>Every solid of the level: floors, walls, ledges, steps.</summary>
        public static IReadOnlyList<LayoutBox> Solids => solids ?? (solids = BuildSolids());

        /// <summary>Solids of one part of the level.</summary>
        public static List<LayoutBox> SolidsIn(FoundryArea area)
        {
            var result = new List<LayoutBox>();
            foreach (LayoutBox box in Solids)
            {
                if (box.Area == area)
                {
                    result.Add(box);
                }
            }

            return result;
        }

        public static LayoutBox Find(string name)
        {
            foreach (LayoutBox box in Solids)
            {
                if (box.Name == name)
                {
                    return box;
                }
            }

            throw new KeyNotFoundException($"The foundry layout has no solid named '{name}'.");
        }

        /// <summary>True if the point is inside any solid, with a margin (positive: the point must be that far clear).</summary>
        public static bool InsideAnySolid(Vector3 point, float margin = 0f)
        {
            foreach (LayoutBox box in Solids)
            {
                if (box.Contains(point, margin))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<LayoutBox> BuildSolids()
        {
            var list = new List<LayoutBox>();
            const float T = WallThickness;
            const float F = FloorThickness;

            // Forecourt.
            Add(list, "ForecourtFloor", -11f, -F, -39f, 11f, 0f, ForecourtZ.y, FoundrySurface.Paving, FoundryArea.Forecourt, true);
            Add(list, "ForecourtWallWest", -11f, 0f, -39f, -10f, ForecourtWallHeight, ForecourtZ.y, FoundrySurface.Stone, FoundryArea.Forecourt);
            Add(list, "ForecourtWallEast", 10f, 0f, -39f, 11f, ForecourtWallHeight, ForecourtZ.y, FoundrySurface.Stone, FoundryArea.Forecourt);
            Add(list, "ForecourtWallSouth", -11f, 0f, -39f, 11f, ForecourtWallHeight, -38f, FoundrySurface.Stone, FoundryArea.Forecourt);

            // The foundry's front, with its gate.
            WallAlongX(list, "Facade", -11f, 11f, ForecourtZ.y, ForecourtZ.y + T, 0f, OreHallWallHeight, ForecourtGateX, 0f,
                ForecourtGateHeight, FoundrySurface.Stone, FoundryArea.OreHall);

            // Ore Hall: two deep floor slabs whose faces are the walls of the slag pit, and the pit floor.
            float deep = -(PitDepth + F);
            Add(list, "OreHallFloorSouth", OreHallX.x, deep, ForecourtZ.y, OreHallX.y, 0f, PitZ.x, FoundrySurface.Paving, FoundryArea.OreHall, true);
            Add(list, "OreHallFloorNorth", OreHallX.x, deep, PitZ.y, OreHallX.y, 0f, 0f, FoundrySurface.Paving, FoundryArea.OreHall, true);
            Add(list, "SlagPitFloor", OreHallX.x, deep, PitZ.x, OreHallX.y, -PitDepth, PitZ.y, FoundrySurface.DarkStone, FoundryArea.OreHall, true);
            Add(list, "OreHallWallWest", OreHallX.x - T, deep, OreHallZ.x, OreHallX.x, OreHallWallHeight, 0f, FoundrySurface.Stone, FoundryArea.OreHall);
            Add(list, "OreHallWallEast", OreHallX.y, deep, OreHallZ.x, OreHallX.y + T, OreHallWallHeight, 0f, FoundrySurface.Stone, FoundryArea.OreHall);
            WallAlongX(list, "Partition", OreHallX.x, OreHallX.y, PartitionZ.x, PartitionZ.y, 0f, OreHallWallHeight, BarricadeDoorX, 0f,
                BarricadeDoorHeight, FoundrySurface.DarkStone, FoundryArea.OreHall);
            WallAlongX(list, "OreHallWallNorth", OreHallX.x - T, OreHallX.y + T, OreHallZ.y, 0f, 0f, OreHallWallHeight, OreHallNorthDoorX,
                0f, OreHallNorthDoorHeight, FoundrySurface.Stone, FoundryArea.OreHall);

            // Lift Hall: high walls because of the ledge, the ledge itself, and the doorway to the stair at the ledge's height.
            Add(list, "LiftHallFloor", LiftHallX.x - T, -F, 0f, LiftHallX.y + T, 0f, LiftHallZ.y + T, FoundrySurface.Paving, FoundryArea.LiftHall, true);
            Add(list, "LiftHallWallWest", LiftHallX.x - T, 0f, 0f, LiftHallX.x, HighWallHeight, LiftHallZ.y + T, FoundrySurface.Stone, FoundryArea.LiftHall);
            Add(list, "LiftHallWallEast", LiftHallX.y, 0f, 0f, LiftHallX.y + T, HighWallHeight, LiftHallZ.y + T, FoundrySurface.Stone, FoundryArea.LiftHall);
            WallAlongX(list, "LiftHallWallNorth", LiftHallX.x - T, LiftHallX.y + T, LiftHallZ.y, LiftHallZ.y + T, 0f, HighWallHeight, StairDoorX,
                LiftLedgeHeight, StairDoorTop, FoundrySurface.Stone, FoundryArea.LiftHall);
            Add(list, "LiftLedge", LiftHallX.x, 0f, LiftLedgeSouthZ, LiftHallX.y, LiftLedgeHeight, LiftHallZ.y, FoundrySurface.DarkStone, FoundryArea.LiftHall, true);

            // The enclosed stair, down from the ledge to the casting floor.
            Add(list, "StairWallWest", StairX.x - T, 0f, StairZ.x, StairX.x, StairRoofHeight, StairZ.y, FoundrySurface.Stone, FoundryArea.StairCorridor);
            Add(list, "StairWallEast", StairX.y, 0f, StairZ.x, StairX.y + T, StairRoofHeight, StairZ.y, FoundrySurface.Stone, FoundryArea.StairCorridor);
            Add(list, "StairRoof", StairX.x - T, StairRoofHeight, StairZ.x, StairX.y + T, StairRoofHeight + 0.4f, StairZ.y, FoundrySurface.DarkStone,
                FoundryArea.StairCorridor);
            int steps = StepCount;
            for (int i = 0; i < steps; i++)
            {
                float top = LiftLedgeHeight - StepRise * (i + 1);
                float z0 = StairZ.x + StepTread * i;
                Add(list, $"Step{i + 1:00}", StairX.x, -F, z0, StairX.y, top, z0 + StepTread, FoundrySurface.DarkStone, FoundryArea.StairCorridor, true);
            }

            // Casting floor.
            Add(list, "CastingFloor", CastingFloorX.x - T, -F, CastingFloorZ.x - T, CastingFloorX.y + T, 0f, CastingFloorZ.y + T,
                FoundrySurface.Paving, FoundryArea.CastingFloor, true);
            Add(list, "CastingWallWest", CastingFloorX.x - T, 0f, CastingFloorZ.x - T, CastingFloorX.x, CastingFloorWallHeight, CastingFloorZ.y + T,
                FoundrySurface.Stone, FoundryArea.CastingFloor);
            Add(list, "CastingWallEast", CastingFloorX.y, 0f, CastingFloorZ.x - T, CastingFloorX.y + T, CastingFloorWallHeight, CastingFloorZ.y + T,
                FoundrySurface.Stone, FoundryArea.CastingFloor);
            WallAlongX(list, "CastingWallSouth", CastingFloorX.x - T, CastingFloorX.y + T, CastingFloorZ.x - T, CastingFloorZ.x, 0f,
                CastingFloorWallHeight, StairX, 0f, CastingFloorEntryHeight, FoundrySurface.Stone, FoundryArea.CastingFloor);
            WallAlongX(list, "CastingWallNorth", CastingFloorX.x - T, CastingFloorX.y + T, CastingFloorZ.y, CastingFloorZ.y + T, 0f,
                CastingFloorWallHeight, GuardianGateX, 0f, GuardianGateHeight, FoundrySurface.Stone, FoundryArea.CastingFloor);

            // Exit court and its great north wall: the gate, the culvert and the breach.
            float exitWallSouth = ExitCourtZ.y;
            float exitWallNorth = ExitCourtZ.y + ExitWallDepth;
            Add(list, "ExitCourtFloor", ExitCourtX.x - T, -F, ExitCourtZ.x, ExitCourtX.y + T, 0f, exitWallNorth, FoundrySurface.Paving,
                FoundryArea.ExitCourt, true);
            Add(list, "ExitCourtWallWest", ExitCourtX.x - T, 0f, ExitCourtZ.x, ExitCourtX.x, HighWallHeight, exitWallNorth, FoundrySurface.Stone,
                FoundryArea.ExitCourt);
            Add(list, "ExitCourtWallEast", ExitCourtX.y, 0f, ExitCourtZ.x, ExitCourtX.y + T, HighWallHeight, exitWallNorth, FoundrySurface.Stone,
                FoundryArea.ExitCourt);
            float xw = ExitCourtX.x - T;
            float xe = ExitCourtX.y + T;
            Add(list, "ExitWallWest", xw, 0f, exitWallSouth, CulvertX.x, HighWallHeight, exitWallNorth, FoundrySurface.Stone, FoundryArea.ExitCourt);
            Add(list, "ExitWallOverCulvert", CulvertX.x, CulvertHeight, exitWallSouth, CulvertX.y, HighWallHeight, exitWallNorth, FoundrySurface.Stone,
                FoundryArea.ExitCourt);
            Add(list, "ExitWallBetween", CulvertX.y, 0f, exitWallSouth, ExitGateX.x, HighWallHeight, exitWallNorth, FoundrySurface.Stone,
                FoundryArea.ExitCourt);
            Add(list, "ExitWallOverGate", ExitGateX.x, ExitGateHeight, exitWallSouth, ExitGateX.y, HighWallHeight, exitWallNorth, FoundrySurface.Stone,
                FoundryArea.ExitCourt);
            Add(list, "ExitWallBeforeBreach", ExitGateX.y, 0f, exitWallSouth, BreachX.x, HighWallHeight, exitWallNorth, FoundrySurface.Stone,
                FoundryArea.ExitCourt);
            Add(list, "Breach", BreachX.x, 0f, exitWallSouth, BreachX.y, BreachHeight, exitWallNorth, FoundrySurface.DarkStone, FoundryArea.ExitCourt, true);
            Add(list, "ExitWallEast", BreachX.y, 0f, exitWallSouth, xe, HighWallHeight, exitWallNorth, FoundrySurface.Stone, FoundryArea.ExitCourt);

            // Exit yard, with a way down from the breach that is also a way back up.
            Add(list, "ExitYardFloor", ExitYardX.x - T, -F, ExitYardZ.x, ExitYardX.y + T, 0f, ExitYardZ.y + T, FoundrySurface.Paving,
                FoundryArea.ExitYard, true);
            Add(list, "ExitYardWallWest", ExitYardX.x - T, 0f, ExitYardZ.x, ExitYardX.x, HighWallHeight, ExitYardZ.y + T, FoundrySurface.Stone,
                FoundryArea.ExitYard);
            Add(list, "ExitYardWallEast", ExitYardX.y, 0f, ExitYardZ.x, ExitYardX.y + T, HighWallHeight, ExitYardZ.y + T, FoundrySurface.Stone,
                FoundryArea.ExitYard);
            WallAlongX(list, "ExitYardWallNorth", ExitYardX.x - T, ExitYardX.y + T, ExitYardZ.y, ExitYardZ.y + T, 0f, HighWallHeight, WayOutX, 0f,
                WayOutHeight, FoundrySurface.Stone, FoundryArea.ExitYard);
            Add(list, "PassageFloor", PassageX.x - T, -F, PassageZ.x, PassageX.y + T, 0f, PassageZ.y, FoundrySurface.Paving, FoundryArea.ExitYard, true);
            Add(list, "PassageWallWest", PassageX.x - T, 0f, PassageZ.x, PassageX.x, HighWallHeight, PassageZ.y, FoundrySurface.Stone, FoundryArea.ExitYard);
            Add(list, "PassageWallEast", PassageX.y, 0f, PassageZ.x, PassageX.y + T, HighWallHeight, PassageZ.y, FoundrySurface.Stone, FoundryArea.ExitYard);
            Add(list, "DescentHigh", BreachX.x, 0f, ExitYardZ.x, BreachX.y, DescentHighTop, ExitYardZ.x + 1.6f, FoundrySurface.DarkStone,
                FoundryArea.ExitYard, true);
            Add(list, "DescentLow", BreachX.x, 0f, ExitYardZ.x + 1.6f, BreachX.y, DescentLowTop, ExitYardZ.x + 3.2f, FoundrySurface.DarkStone,
                FoundryArea.ExitYard, true);

            return list;
        }

        public const float DescentHighTop = 3.4f;
        public const float DescentLowTop = 1.7f;

        /// <summary>Steps from the ledge down to the casting floor; the floor itself is the last.</summary>
        public static int StepCount => Mathf.RoundToInt(LiftLedgeHeight / StepRise) - 1;

        private static void Add(List<LayoutBox> list, string name, float x0, float y0, float z0, float x1, float y1, float z1,
            FoundrySurface surface, FoundryArea area, bool walkable = false)
        {
            list.Add(new LayoutBox(name, new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), surface, area, walkable));
        }

        /// <summary>A wall running east-west with one rectangular opening, built as the pieces around the opening.</summary>
        private static void WallAlongX(List<LayoutBox> list, string name, float x0, float x1, float z0, float z1, float y0, float y1,
            Vector2 openingX, float openingBottom, float openingTop, FoundrySurface surface, FoundryArea area)
        {
            Add(list, name + "Left", x0, y0, z0, openingX.x, y1, z1, surface, area);
            Add(list, name + "Right", openingX.y, y0, z0, x1, y1, z1, surface, area);
            if (openingTop < y1)
            {
                Add(list, name + "Lintel", openingX.x, openingTop, z0, openingX.y, y1, z1, surface, area);
            }

            if (openingBottom > y0)
            {
                Add(list, name + "Sill", openingX.x, y0, z0, openingX.y, openingBottom, z1, surface, area, true);
            }
        }
    }
}
