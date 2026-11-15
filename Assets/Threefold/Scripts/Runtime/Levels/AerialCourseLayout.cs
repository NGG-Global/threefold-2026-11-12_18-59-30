using UnityEngine;

namespace Threefold.Levels
{
    /// <summary>One block of the aerial course: its size, the centre of its underside, and what it tests.</summary>
    public readonly struct CourseBlock
    {
        public readonly string Name;
        public readonly Vector3 Size;
        public readonly Vector3 BaseCentre;
        public readonly string Purpose;

        public CourseBlock(string name, Vector3 size, Vector3 baseCentre, string purpose)
        {
            Name = name;
            Size = size;
            BaseCentre = baseCentre;
            Purpose = purpose;
        }

        public float Top => BaseCentre.y + Size.y;
        public float MinZ => BaseCentre.z - Size.z * 0.5f;
        public float MaxZ => BaseCentre.z + Size.z * 0.5f;
        public float MinX => BaseCentre.x - Size.x * 0.5f;
        public float MaxX => BaseCentre.x + Size.x * 0.5f;
    }

    /// <summary>
    /// The aerial course at the far end of the Discipline Range, beyond the arrow lane: one station per aerial
    /// ability, each sized so that a normal jump falls short and the ability succeeds with a margin. Numbers in
    /// metres, in the range's coordinates (the range floor ends at z = 40). Built by the Discipline Range setup;
    /// <c>AerialCourseLayoutTests</c> checks every station against the motor, the traversal limits
    /// (<see cref="TraversalReach"/>) and the aerial abilities' default tuning at 15 to 120 frames per second.
    /// Change the numbers here, never the generated scene.
    ///
    /// - Ascension pillar: 4.2 m, at least 6 m from anything that could be jumped from. A full jump and a ledge
    ///   catch reach about 3.9 m; Arcane Ascension at the top of a jump lifts the feet to about 4.6 m.
    /// - Glide tower and landing: steps up to a 4.2 m tower, a 12 m gap, and a platform 1 m high. A jump from the
    ///   tower carries about 6.5 m, a dash or an ascension about 11 m, Flame's glide about 16 m.
    /// - Dash gap: two platforms 1 m high, 7 m apart. A jump carries about 5 m, a jump and a dash about 9 m.
    /// - Dash wall: a wall 3.5 m beyond a floor marker, to dash into.
    /// </summary>
    public static class AerialCourseLayout
    {
        public const float FloorStartZ = 40f;
        public const float FloorLength = 32f;
        public const float FloorWidth = 26f;
        public const float FloorThickness = 0.6f;

        public const float PillarHeight = 4.2f;
        public const float TowerHeight = 4.2f;
        public const float GlideGap = 12f;
        public const float GlideLandingHeight = 1f;
        public const float GlideLandingDepth = 6f;
        public const float DashPlatformHeight = 1f;
        public const float DashGap = 7f;
        public const float DashWallDistance = 3.5f;

        /// <summary>Height of each step up to the glide tower: low enough to climb onto from standing.</summary>
        public const float StepRise = 1.05f;

        private const float GlideLaneX = -9.5f;
        private const float DashLaneX = 7.5f;
        private const float TowerStartZ = 46f;
        private const float TowerDepth = 4f;
        private const float DashStartZ = 44f;
        private const float PlatformDepth = 6f;

        /// <summary>The pillar stands at least this far, horizontally, from any block a jump could start from.</summary>
        public const float PillarClearance = 6f;

        /// <summary>Where the glide tower's edge is, facing the gap.</summary>
        public static float TowerEdgeZ => TowerStartZ + TowerDepth;

        /// <summary>Every block of the course, the floor first.</summary>
        public static CourseBlock[] Blocks()
        {
            float landingNear = TowerEdgeZ + GlideGap;
            float dashNearEdge = DashStartZ + PlatformDepth;
            float dashFar = dashNearEdge + DashGap;
            float stepsTop = TowerHeight - StepRise;

            return new[]
            {
                new CourseBlock("AerialFloor", new Vector3(FloorWidth, FloorThickness, FloorLength),
                    new Vector3(0f, -FloorThickness, FloorStartZ + FloorLength * 0.5f), "The course's floor"),

                // Arcane: a pillar that only Ascension reaches, standing alone so nothing can be climbed to it.
                new CourseBlock("AscensionPillar", new Vector3(3f, PillarHeight, 3f), new Vector3(0f, 0f, 69f),
                    "Arcane vertical clearance: out of reach of a jump and a ledge catch"),

                // Flame: steps up to a tower, a long gap, and a lower landing.
                new CourseBlock("GlideStep1", new Vector3(4f, stepsTop - StepRise * 2f, 2f),
                    new Vector3(GlideLaneX, 0f, TowerStartZ - 5f), "Step to the glide tower"),
                new CourseBlock("GlideStep2", new Vector3(4f, stepsTop - StepRise, 2f),
                    new Vector3(GlideLaneX, 0f, TowerStartZ - 3f), "Step to the glide tower"),
                new CourseBlock("GlideStep3", new Vector3(4f, stepsTop, 2f),
                    new Vector3(GlideLaneX, 0f, TowerStartZ - 1f), "Step to the glide tower"),
                new CourseBlock("GlideTower", new Vector3(4f, TowerHeight, TowerDepth),
                    new Vector3(GlideLaneX, 0f, TowerStartZ + TowerDepth * 0.5f), "Flame glide take-off"),
                new CourseBlock("GlideLanding", new Vector3(4f, GlideLandingHeight, GlideLandingDepth),
                    new Vector3(GlideLaneX, 0f, landingNear + GlideLandingDepth * 0.5f),
                    "Flame glide landing: beyond a jump, a dash and an ascension from the tower"),

                // Aegis: two platforms a dash apart.
                new CourseBlock("DashStart", new Vector3(4f, DashPlatformHeight, PlatformDepth),
                    new Vector3(DashLaneX, 0f, DashStartZ + PlatformDepth * 0.5f), "Aegis dash take-off"),
                new CourseBlock("DashLanding", new Vector3(4f, DashPlatformHeight, PlatformDepth),
                    new Vector3(DashLaneX, 0f, dashFar + PlatformDepth * 0.5f), "Aegis dash landing: beyond a jump"),

                // Aegis: a wall to dash into, and a flat marker to start from.
                new CourseBlock("DashWallMarker", new Vector3(1f, 0.04f, 0.4f), new Vector3(0f, 0f, 49.8f),
                    "Stand here and dash forward into the wall"),
                new CourseBlock("DashWall", new Vector3(3f, 2.6f, 0.6f), new Vector3(0f, 0f, 49.8f + DashWallDistance + 0.3f),
                    "A wall that stops a dash"),
            };
        }

        public static CourseBlock Find(string name)
        {
            foreach (CourseBlock block in Blocks())
            {
                if (block.Name == name)
                {
                    return block;
                }
            }

            return default;
        }

        /// <summary>Where the Mage stands to try a station, in the range's coordinates, facing +z.</summary>
        public static Vector3 SignPosition(string station)
        {
            switch (station)
            {
                case "Ascension":
                    return new Vector3(0f, 0f, 64.5f);
                case "Glide":
                    return new Vector3(GlideLaneX, 0f, 41f);
                case "Dash":
                    return new Vector3(DashLaneX, 0f, 42f);
                default:
                    return new Vector3(0f, 0f, 49.8f);
            }
        }
    }
}
