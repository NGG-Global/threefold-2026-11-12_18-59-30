using System.Collections.Generic;
using UnityEngine;

namespace Threefold.EditorTools
{
    /// <summary>
    /// The numbers of the Reference Courtyard (Docs/VisualReferencePrototype.md): a small courtyard for judging
    /// the visual direction against a reference picture. The scene is built only from these numbers; change
    /// them here, never the generated scene, and keep <c>ReferenceCourtyardLayoutTests</c> passing.
    ///
    /// The composition is decided here, in plain arithmetic that can be tested without Unity: where the Mage
    /// starts and looks, where the game's camera therefore stands, and what it then sees. The great arch stands
    /// on the axis of the view; the castle stands to the right of the arch and above every wall and tree in
    /// front of it; the rune dais lies between the Mage and the arch.
    ///
    /// Distances are metres. +Z is the way the Mage faces at the start, +X is to the right of the view.
    /// </summary>
    internal static class ReferenceCourtyardLayout
    {
        // ------------------------------------------------------------ courtyard

        /// <summary>Half the paved courtyard's width (X) and depth (Z).</summary>
        internal const float HalfWidth = 12f;
        internal const float HalfDepth = 13f;

        /// <summary>Half the side of the grass terrace the courtyard stands on. Beyond it the land falls to the valley.</summary>
        internal const float TerraceHalf = 46f;

        /// <summary>How far below the terrace the valley floor lies.</summary>
        internal const float ValleyDrop = 16f;

        internal static readonly Vector3 Spawn = new Vector3(-0.8f, 0f, -9.5f);

        /// <summary>The rune dais: the focal point on the ground, between the Mage and the arch.</summary>
        internal static readonly Vector3 Dais = new Vector3(0f, 0f, 1.5f);

        // ------------------------------------------------------------ the great arch

        internal static readonly Vector3 Arch = new Vector3(0f, 0f, 10f);

        /// <summary>Half the clear width of the opening, and the height at which the round head springs.</summary>
        internal const float ArchRadius = 2.6f;
        internal const float ArchSpring = 4.2f;
        internal const float ArchPierWidth = 1.7f;
        internal const float ArchDepth = 1.5f;

        /// <summary>The length of the wall that runs on from each pier, stepping down as a ruin.</summary>
        internal const float ArchWing = 3.2f;

        /// <summary>The top of the masonry above the crown, before the ruined steps.</summary>
        internal const float ArchTop = 8.2f;

        internal static float ArchHalfExtent => ArchRadius + ArchPierWidth + ArchWing;

        // ------------------------------------------------------------ the game's camera at the start

        // The values ThirdPersonCamera starts with (its serialized defaults). If they are tuned there, the
        // composition tests here no longer describe the first view; ReferenceCourtyardAssemblyTests compares them.
        internal const float CameraDistance = 5f;
        internal const float CameraStartPitch = 15f;
        internal const float CameraTargetHeight = 1.5f;
        internal const float CameraShoulder = 0.55f;
        internal const float CameraVerticalFov = 60f;
        internal const float ViewAspect = 16f / 9f;

        /// <summary>Where the game's camera stands when the scene starts: behind and above the Mage, looking along +Z and down.</summary>
        internal static Vector3 StartCameraPosition
        {
            get
            {
                float pitch = CameraStartPitch * Mathf.Deg2Rad;
                return Spawn + new Vector3(CameraShoulder, CameraTargetHeight + CameraDistance * Mathf.Sin(pitch),
                    -CameraDistance * Mathf.Cos(pitch));
            }
        }

        /// <summary>Half the horizontal field of view, in degrees.</summary>
        internal static float HalfHorizontalFov =>
            Mathf.Atan(Mathf.Tan(CameraVerticalFov * 0.5f * Mathf.Deg2Rad) * ViewAspect) * Mathf.Rad2Deg;

        /// <summary>The highest elevation the first view shows, in degrees above the horizon.</summary>
        internal static float TopOfViewElevation => CameraVerticalFov * 0.5f - CameraStartPitch;

        /// <summary>The direction of a point as the start camera sees it: degrees to the right of the view's axis.</summary>
        internal static float Bearing(Vector3 point)
        {
            Vector3 from = StartCameraPosition;
            return Mathf.Atan2(point.x - from.x, point.z - from.z) * Mathf.Rad2Deg;
        }

        /// <summary>Degrees above the start camera's horizon.</summary>
        internal static float Elevation(Vector3 point)
        {
            Vector3 from = StartCameraPosition;
            float ground = Mathf.Sqrt((point.x - from.x) * (point.x - from.x) + (point.z - from.z) * (point.z - from.z));
            return Mathf.Atan2(point.y - from.y, ground) * Mathf.Rad2Deg;
        }

        // ------------------------------------------------------------ distant scenery

        /// <summary>The foot of the castle: the top of its hill.</summary>
        internal static readonly Vector3 Castle = new Vector3(118f, 14f, 215f);

        internal const float CastleHalfWidth = 26f;
        internal const float CastleHeight = 34f;

        /// <summary>The style's haze begins here and is complete there (LightingPreset.GoldenAfternoon). Compared in the Unity test.</summary>
        internal const float HazeEnd = 360f;

        internal readonly struct Hill
        {
            internal readonly Vector3 Position;
            internal readonly float Scale;
            internal readonly float Yaw;

            /// <summary>True for a near, wooded hill; false for a far, blue mountain.</summary>
            internal readonly bool Wooded;

            internal Hill(float x, float y, float z, float scale, float yaw, bool wooded)
            {
                Position = new Vector3(x, y, z);
                Scale = scale;
                Yaw = yaw;
                Wooded = wooded;
            }
        }

        /// <summary>The valley: wooded hills in the middle distance, blue mountains behind them, a ring behind the viewer.</summary>
        internal static readonly Hill[] Hills =
        {
            // Wooded hills on both sides of the valley, leaving the axis of the arch open.
            new Hill(-95f, -ValleyDrop, 150f, 0.55f, 20f, true),
            new Hill(-40f, -ValleyDrop, 205f, 0.45f, 140f, true),
            new Hill(62f, -ValleyDrop, 170f, 0.4f, 250f, true),
            new Hill(175f, -ValleyDrop, 150f, 0.6f, 80f, true),
            new Hill(-170f, -ValleyDrop, 95f, 0.6f, 300f, true),

            // Mountains closing the valley.
            new Hill(-150f, -ValleyDrop, 300f, 1.25f, 0f, false),
            new Hill(-20f, -ValleyDrop, 335f, 1.0f, 70f, false),
            new Hill(120f, -ValleyDrop, 320f, 1.3f, 200f, false),
            new Hill(270f, -ValleyDrop, 240f, 1.2f, 130f, false),
            new Hill(-285f, -ValleyDrop, 170f, 1.2f, 40f, false),

            // Behind and beside the viewer, so that turning round never shows an empty horizon.
            new Hill(-300f, -ValleyDrop, -60f, 1.1f, 10f, false),
            new Hill(-190f, -ValleyDrop, -260f, 1.3f, 95f, false),
            new Hill(40f, -ValleyDrop, -310f, 1.2f, 180f, false),
            new Hill(250f, -ValleyDrop, -210f, 1.25f, 260f, false),
            new Hill(315f, -ValleyDrop, 20f, 1.1f, 330f, false),
        };

        // ------------------------------------------------------------ walls and columns

        internal enum Module
        {
            Wall,
            WallBroken,
            WallLow,
            Column,
            ColumnBroken,
            ColumnFallen,
            Rubble,
        }

        internal readonly struct Placement
        {
            internal readonly Module Module;
            internal readonly Vector3 Position;
            internal readonly float Yaw;

            internal Placement(Module module, float x, float z, float yaw)
            {
                Module = module;
                Position = new Vector3(x, 0f, z);
                Yaw = yaw;
            }
        }

        /// <summary>The height of a module's highest stone, for the composition tests (EnvironmentAssets builds them).</summary>
        internal static float Height(Module module)
        {
            switch (module)
            {
                case Module.Wall: return 2.8f;
                case Module.WallBroken: return 2.3f;
                case Module.WallLow: return 1f;
                case Module.Column: return 4.6f;
                case Module.ColumnBroken: return 2.6f;
                default: return 0.8f;
            }
        }

        /// <summary>
        /// The left side is a standing wall with columns, as an arcade; the right side is low and broken, so
        /// that the valley and the castle are seen over it. Yaw 90 turns a wall to run along Z.
        /// </summary>
        internal static readonly Placement[] Modules =
        {
            // Left: a standing wall, front to back, broken at its far end.
            new Placement(Module.Wall, -12.3f, -8f, 90f),
            new Placement(Module.Wall, -12.3f, -4f, 90f),
            new Placement(Module.Wall, -12.3f, 0f, 90f),
            new Placement(Module.Wall, -12.3f, 4f, 90f),
            new Placement(Module.WallBroken, -12.3f, 8f, 90f),
            new Placement(Module.Column, -10.6f, -6f, 0f),
            new Placement(Module.Column, -10.6f, -2f, 0f),
            new Placement(Module.Column, -10.6f, 2f, 0f),
            new Placement(Module.ColumnBroken, -10.6f, 6f, 25f),

            // Right: low and ruined.
            new Placement(Module.WallLow, 12.3f, -7f, 90f),
            new Placement(Module.WallBroken, 12.3f, -2.6f, 270f),
            new Placement(Module.WallLow, 12.3f, 2.4f, 90f),
            new Placement(Module.ColumnFallen, 9.2f, 6.2f, 70f),
            new Placement(Module.Rubble, 10.4f, 4.4f, 30f),
            new Placement(Module.Rubble, -9.4f, 9.4f, 200f),

            // Behind the Mage: a low wall closes the courtyard, with a gap in the middle.
            new Placement(Module.WallLow, -7f, -13.3f, 0f),
            new Placement(Module.WallLow, 7f, -13.3f, 0f),

            // Two broken columns before the arch carry the lights that frame it.
            new Placement(Module.ColumnBroken, -5.4f, 6.4f, 0f),
            new Placement(Module.ColumnBroken, 5.4f, 6.4f, 180f),
        };

        /// <summary>Where the two lights stand: on the broken columns before the arch.</summary>
        internal static readonly Vector3[] Lights =
        {
            new Vector3(-5.4f, 2.6f, 6.4f),
            new Vector3(5.4f, 2.6f, 6.4f),
        };

        // ------------------------------------------------------------ vegetation

        internal readonly struct Tree
        {
            internal readonly Vector3 Position;
            internal readonly int Kind;
            internal readonly float Scale;
            internal readonly float Yaw;

            internal Tree(float x, float z, int kind, float scale, float yaw)
            {
                Position = new Vector3(x, 0f, z);
                Kind = kind;
                Scale = scale;
                Yaw = yaw;
            }

            /// <summary>The tallest tree module is about 8.5 m at scale 1 (EnvironmentAssets.BuildTree).</summary>
            internal float Height => 8.5f * Scale;
        }

        /// <summary>Few, and placed: tall trees frame the left and the far right; none stands before the castle.</summary>
        internal static readonly Tree[] Trees =
        {
            new Tree(-17f, 13f, 1, 1.7f, 20f),
            new Tree(-20f, 4f, 0, 1.35f, 140f),
            new Tree(-18.5f, -6f, 2, 1.5f, 260f),
            new Tree(-9f, 19f, 0, 1.25f, 75f),
            new Tree(30f, 6f, 1, 1.6f, 310f),
            new Tree(27f, -9f, 0, 1.3f, 40f),
            new Tree(-27f, 21f, 1, 1.5f, 190f),
        };

        internal readonly struct Vine
        {
            /// <summary>The middle of the vine's upper edge, on the surface it hangs from.</summary>
            internal readonly Vector3 Top;

            /// <summary>The direction the surface faces: the vine stands this way off it.</summary>
            internal readonly Vector3 Facing;

            internal readonly float Width;
            internal readonly float Drop;
            internal readonly int Seed;

            internal Vine(Vector3 top, Vector3 facing, float width, float drop, int seed)
            {
                Top = top;
                Facing = facing;
                Width = width;
                Drop = drop;
                Seed = seed;
            }
        }

        /// <summary>
        /// Climbing vines, selectively: on the arch, where the eye goes, and on the standing wall. Each is one
        /// opaque mesh of leaves; none uses transparency.
        /// </summary>
        internal static readonly Vine[] Vines =
        {
            // The arch, on the side the Mage sees: over the crown, and down the left pier.
            new Vine(new Vector3(-1.2f, ArchTop, Arch.z - ArchDepth * 0.5f), Vector3.back, 4.6f, 2.3f, 11),
            new Vine(new Vector3(-(ArchRadius + ArchPierWidth * 0.5f), 6.4f, Arch.z - ArchDepth * 0.5f), Vector3.back, 1.9f, 4.4f, 12),
            new Vine(new Vector3(ArchRadius + ArchPierWidth + 1.2f, 5.2f, Arch.z - ArchDepth * 0.5f), Vector3.back, 2.6f, 2.2f, 13),

            // The standing wall on the left, on the side facing the courtyard.
            new Vine(new Vector3(-11.95f, 2.7f, -6.2f), Vector3.right, 3.4f, 1.9f, 21),
            new Vine(new Vector3(-11.95f, 2.5f, 1.4f), Vector3.right, 4.2f, 2.1f, 22),
            new Vine(new Vector3(-11.95f, 2.2f, 7.6f), Vector3.right, 2.4f, 1.6f, 23),

            // One on the broken wall to the right.
            new Vine(new Vector3(11.95f, 2.1f, -3.4f), Vector3.left, 1.8f, 1.5f, 31),
        };

        /// <summary>Grass and moss: at the edges of the paving and in a few broken places, not everywhere.</summary>
        internal static readonly Vector3[] GrassPatches =
        {
            new Vector3(-14.5f, 0f, -10f), new Vector3(-14.8f, 0f, 2f), new Vector3(-14.2f, 0f, 11f),
            new Vector3(14.6f, 0f, -9f), new Vector3(15f, 0f, 1f), new Vector3(14.4f, 0f, 9.5f),
            new Vector3(-6f, 0f, 15.5f), new Vector3(6.5f, 0f, 15.8f), new Vector3(0f, 0f, 17.5f),
            new Vector3(-6f, 0f, -16f), new Vector3(5f, 0f, -16.5f),
        };

        internal static readonly Vector3[] MossPatches =
        {
            new Vector3(-9.2f, 0f, -3.4f), new Vector3(8.6f, 0f, 1.2f), new Vector3(-3.8f, 0f, 7.9f),
            new Vector3(4.4f, 0f, 8.3f), new Vector3(9.4f, 0f, -8.6f), new Vector3(-7.6f, 0f, 9.6f),
        };

        /// <summary>The three training constructs, in an arc round the dais, as the game's objective asks for.</summary>
        internal static readonly Vector3[] Constructs =
        {
            new Vector3(-6.2f, 0f, 2.6f),
            new Vector3(0f, 0f, 5.6f),
            new Vector3(6.2f, 0f, 2.6f),
        };

        /// <summary>The wood, cloth and metal props of the style (crates, banners, brazier): right of the arch.</summary>
        internal static readonly Vector3 Props = new Vector3(5.2f, 0f, -3.8f);
        internal const float PropsYaw = -35f;

        // ------------------------------------------------------------ budgets

        /// <summary>
        /// The most triangles the whole generated environment may hold (the Mage, the constructs and the HUD
        /// are not counted). A design limit for a small scene, not a measured budget for any phone.
        /// </summary>
        internal const int MaxEnvironmentTriangles = 30000;

        // ------------------------------------------------------------ review viewpoints

        internal readonly struct Viewpoint
        {
            internal readonly string Name;
            internal readonly Vector3 Position;
            internal readonly Vector3 Target;

            internal Viewpoint(string name, Vector3 position, Vector3 target)
            {
                Name = name;
                Position = position;
                Target = target;
            }
        }

        /// <summary>
        /// The views rendered for review by ReviewRenders. The first is where the game's camera stands at the
        /// start; the others look at what the first cannot judge.
        /// </summary>
        internal static IReadOnlyList<Viewpoint> Viewpoints => new[]
        {
            new Viewpoint("1-start", StartCameraPosition,
                StartCameraPosition + new Vector3(0f, -Mathf.Tan(CameraStartPitch * Mathf.Deg2Rad), 1f) * 20f),
            new Viewpoint("2-arch", new Vector3(-3.5f, 1.7f, 0.5f), new Vector3(0.6f, 4.6f, Arch.z)),
            new Viewpoint("3-castle", new Vector3(3f, 2.2f, 2f), Castle + Vector3.up * (CastleHeight * 0.4f)),
            new Viewpoint("4-wall-and-vines", new Vector3(-3f, 1.8f, -3f), new Vector3(-12f, 1.8f, 1.5f)),
            new Viewpoint("5-mage", Spawn + new Vector3(1.6f, 1.5f, 3.2f), Spawn + Vector3.up * 1.1f),
            new Viewpoint("6-overview", new Vector3(16f, 15f, -24f), new Vector3(0f, 1.5f, 4f)),
        };
    }
}
