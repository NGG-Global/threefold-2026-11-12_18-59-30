using UnityEngine;
using Random = System.Random;
using L = Threefold.EditorTools.ReferenceCourtyardLayout;

namespace Threefold.EditorTools
{
    /// <summary>
    /// The meshes the Reference Courtyard adds to the environment modules: the great arch, the worn details of
    /// the paving, the vines, the lights and the distant castle. All are built from boxes, prisms and flat
    /// leaves by code, so they have no source file and no licence of their own (Docs/VisualReferencePrototype.md).
    ///
    /// Sub-mesh 0 is the main surface and sub-mesh 1 the accent, as in EnvironmentAssets. A mesh asset is kept
    /// once generated (EnvironmentAssets.EnsureMesh), so a change of shape needs a new name: raise the number
    /// in <see cref="Version"/>.
    /// </summary>
    internal static class ReferenceCourtyardMeshes
    {
        /// <summary>Part of every mesh's name. Raise it when a builder here changes, so the new shape is generated.</summary>
        internal const string Version = "v2";

        private const int Main = 0;
        private const int Accent = 1;

        internal static string Name(string part)
        {
            return "RefCourt_" + part + "_" + Version;
        }

        // ------------------------------------------------------------ the great arch

        /// <summary>
        /// A tall round-headed gateway: two piers on plinths, a ring of stones, masonry over the crown that has
        /// lost its right-hand corner, and a wall running on from each pier, stepping down as a ruin. Moss lies
        /// on the tops. The origin is the middle of the threshold; the opening looks along Z.
        /// </summary>
        internal static Mesh BuildGreatArch()
        {
            const float radius = L.ArchRadius;
            const float spring = L.ArchSpring;
            const float pier = L.ArchPierWidth;
            const float depth = L.ArchDepth;
            const float ring = 0.9f;
            const int stones = 13;

            var b = new ProceduralMeshBuilder();
            float pierCentre = radius + pier * 0.5f;
            float outer = radius + pier;

            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * pierCentre;
                b.AddBox(new Vector3(x, 0.25f, 0f), new Vector3(pier + 0.4f, 0.5f, depth + 0.4f), Main);
                b.AddBox(new Vector3(x, spring * 0.5f, 0f), new Vector3(pier, spring, depth), Main);
                b.AddBox(new Vector3(x, spring + 0.15f, 0f), new Vector3(pier + 0.3f, 0.3f, depth + 0.25f), Main);
            }

            // The ring of stones, each turned so that it points away from the centre of the head.
            float ringCentre = spring + 0.3f;
            float mean = radius + ring * 0.5f;
            float stoneLength = Mathf.PI * mean / stones * 1.04f;
            for (int i = 0; i < stones; i++)
            {
                float degrees = (i + 0.5f) * 180f / stones;
                float radians = degrees * Mathf.Deg2Rad;
                bool keystone = i == stones / 2;
                var centre = new Vector3(Mathf.Cos(radians) * mean, ringCentre + Mathf.Sin(radians) * mean, 0f);
                var size = new Vector3(stoneLength, ring * (keystone ? 1.2f : 1f), depth * (keystone ? 1.1f : 1.02f));
                b.AddBox(centre, size, Quaternion.Euler(0f, 0f, degrees - 90f), Main);
            }

            // The corners between the ring and the piers' lines, in courses. The right-hand corner has lost its top.
            float ringOuter = radius + ring;
            const int courses = 5;
            float course = ringOuter / courses;
            for (int side = -1; side <= 1; side += 2)
            {
                int kept = side < 0 ? courses : courses - 2;
                for (int k = 0; k < kept; k++)
                {
                    float low = k * course;
                    float inner = Mathf.Sqrt(Mathf.Max(0f, ringOuter * ringOuter - low * low)) - 0.12f;
                    if (inner >= outer - 0.05f)
                    {
                        continue;
                    }

                    float width = outer - inner;
                    b.AddBox(new Vector3(side * (inner + width * 0.5f), ringCentre + low + course * 0.5f, 0f),
                        new Vector3(width, course, depth * 0.94f), Main);
                }
            }

            // Masonry over the crown: whole on the left, stepping down to the right, with moss on every top.
            float crown = ringCentre + ringOuter;
            float top = L.ArchTop;
            AddCapped(b, -outer, 0.9f, crown - 0.1f, top, depth * 0.94f);
            AddCapped(b, 0.9f, 2.4f, crown - 0.1f, top - 0.7f, depth * 0.94f);
            float rightCorner = ringCentre + (courses - 2) * course;
            AddCapped(b, 2.4f, outer, rightCorner - 0.05f, rightCorner + 0.5f, depth * 0.9f);

            // The walls that run on from the piers.
            float third = L.ArchWing / 3f;
            float[] left = { 5.9f, 4.5f, 3.1f };
            float[] right = { 4.3f, 2.7f, 1.3f };
            for (int i = 0; i < 3; i++)
            {
                AddCapped(b, -outer - (i + 1) * third, -outer - i * third, 0f, left[i], depth * 0.8f);
                AddCapped(b, outer + i * third, outer + (i + 1) * third, 0f, right[i], depth * 0.8f);
            }

            return b.Build(Name("GreatArch"));
        }

        /// <summary>A course of wall between two X positions, with a thin cap of moss on its top.</summary>
        private static void AddCapped(ProceduralMeshBuilder b, float fromX, float toX, float bottom, float top, float depth)
        {
            float width = toX - fromX;
            float centre = (fromX + toX) * 0.5f;
            b.AddBox(new Vector3(centre, (bottom + top) * 0.5f, 0f), new Vector3(width, top - bottom, depth), Main);
            b.AddBox(new Vector3(centre, top + 0.03f, 0f), new Vector3(width * 0.9f, 0.06f, depth * 0.85f), Accent);
        }

        // ------------------------------------------------------------ paving

        /// <summary>
        /// What makes the flat paving read as old: single slabs standing a centimetre or two proud of it, in a
        /// second tone, and a kerb round the courtyard with gaps. The dais in the middle is left clear.
        /// (The first version stood up to five centimetres proud in a much darker stone, and in the renders
        /// of run 21 the slabs read as loose plates lying on the floor.)
        /// </summary>
        internal static Mesh BuildFlagstones()
        {
            var random = new Random(4107);
            var b = new ProceduralMeshBuilder();

            int placed = 0;
            for (int attempt = 0; attempt < 400 && placed < 36; attempt++)
            {
                float x = EnvironmentAssets.Range(random, -L.HalfWidth + 1.4f, L.HalfWidth - 1.4f);
                float z = EnvironmentAssets.Range(random, -L.HalfDepth + 1.4f, L.HalfDepth - 1.4f);
                float fromDais = Mathf.Sqrt((x - L.Dais.x) * (x - L.Dais.x) + (z - L.Dais.z) * (z - L.Dais.z));
                if (fromDais < 4.2f)
                {
                    continue;
                }

                var size = new Vector3(EnvironmentAssets.Range(random, 0.9f, 1.9f), EnvironmentAssets.Range(random, 0.008f, 0.02f),
                    EnvironmentAssets.Range(random, 0.8f, 1.6f));
                float yaw = EnvironmentAssets.Range(random, -6f, 6f) + (random.Next(2) == 0 ? 0f : 90f);
                b.AddBox(new Vector3(x, size.y * 0.5f, z), size, Quaternion.Euler(0f, yaw, 0f), random.Next(4) == 0 ? Accent : Main);
                placed++;
            }

            // The kerb: stones of uneven length along each side, with one in six missing.
            const float kerb = 0.5f;
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float half = alongX ? L.HalfWidth : L.HalfDepth;
                float offset = (alongX ? L.HalfDepth : L.HalfWidth) + kerb * 0.5f;
                float sign = side % 2 == 0 ? 1f : -1f;

                float at = -half;
                while (at < half)
                {
                    float length = Mathf.Min(EnvironmentAssets.Range(random, 1.2f, 2.6f), half - at);
                    if (random.Next(6) != 0 && length > 0.4f)
                    {
                        float height = EnvironmentAssets.Range(random, 0.14f, 0.24f);
                        float middle = at + length * 0.5f;
                        Vector3 centre = alongX ? new Vector3(middle, height * 0.5f, sign * offset) : new Vector3(sign * offset, height * 0.5f, middle);
                        Vector3 size = alongX ? new Vector3(length - 0.06f, height, kerb) : new Vector3(kerb, height, length - 0.06f);
                        b.AddBox(centre, size, Main);
                    }

                    at += length;
                }
            }

            return b.Build(Name("Flagstones"));
        }

        // ------------------------------------------------------------ vines

        /// <summary>
        /// A hanging vine as one opaque mesh: flat leaves of two triangles each, thick at the top and ragged at
        /// the lower edge, over a few stems. Nothing is transparent, so it costs no overdraw beyond its own
        /// small leaves. In the mesh's own space X runs along the wall, Y is up, and Z points off the wall; the
        /// origin is the middle of the upper edge. All leaves share the wall's direction as their normal, so the
        /// vine is lit as one mass and does not flicker leaf by leaf. (The first version had half as many
        /// leaves, standing further off the wall on thicker stems; in the renders of run 21 it read as loose
        /// leaves on wires.)
        /// </summary>
        internal static Mesh BuildVine(float width, float drop, int seed)
        {
            var random = new Random(seed);
            var b = new ProceduralMeshBuilder();
            float phaseA = EnvironmentAssets.Range(random, 0f, 6.28f);
            float phaseB = EnvironmentAssets.Range(random, 0f, 6.28f);

            // How far down the vine reaches at a place along the wall: between 45% and 100% of the drop.
            float Reach(float x)
            {
                float wave = 0.5f + 0.3f * Mathf.Sin(x * 2.1f + phaseA) + 0.2f * Mathf.Sin(x * 5.3f + phaseB);
                return drop * (0.45f + 0.55f * Mathf.Clamp01(wave));
            }

            int stems = Mathf.Max(2, Mathf.RoundToInt(width * 1.2f));
            for (int i = 0; i < stems; i++)
            {
                float x = -width * 0.5f + (i + EnvironmentAssets.Range(random, 0.2f, 0.8f)) * width / stems;
                float reach = Reach(x) * 0.8f;
                float sway = EnvironmentAssets.Range(random, -0.18f, 0.18f);
                var topLeft = new Vector3(x - 0.012f, 0.05f, 0.02f);
                var topRight = new Vector3(x + 0.012f, 0.05f, 0.02f);
                var bottomRight = new Vector3(x + sway + 0.008f, -reach, 0.02f);
                var bottomLeft = new Vector3(x + sway - 0.008f, -reach, 0.02f);
                b.AddQuad(topLeft, topRight, bottomRight, bottomLeft, Vector3.forward, Accent);
            }

            int leaves = Mathf.RoundToInt(width * drop * 30f);
            for (int i = 0; i < leaves; i++)
            {
                float x = EnvironmentAssets.Range(random, -width * 0.5f, width * 0.5f);

                // More leaves near the top than at the tips.
                float along = Mathf.Pow(EnvironmentAssets.Range(random, 0f, 1f), 1.25f);
                float y = 0.12f - along * Reach(x);
                float z = EnvironmentAssets.Range(random, 0.03f, 0.1f);
                float size = EnvironmentAssets.Range(random, 0.13f, 0.24f) * (1f - 0.25f * along);
                float roll = EnvironmentAssets.Range(random, 0f, Mathf.PI * 2f);

                var centre = new Vector3(x, y, z);
                var longAxis = new Vector3(Mathf.Cos(roll), Mathf.Sin(roll), EnvironmentAssets.Range(random, -0.25f, 0.25f)) * size;
                var shortAxis = new Vector3(-Mathf.Sin(roll), Mathf.Cos(roll), 0f) * (size * 0.62f);

                // A leaf: a kite with its point down the long axis.
                Vector3 tip = centre + longAxis * 1.15f;
                Vector3 stalk = centre - longAxis * 0.85f;
                AddLeafHalf(b, stalk, centre + shortAxis, tip);
                AddLeafHalf(b, stalk, tip, centre - shortAxis);
            }

            return b.Build(Name("Vine" + seed));
        }

        /// <summary>Half a leaf: wound so that it is seen from in front of the wall, and lit as the wall's face is.</summary>
        private static void AddLeafHalf(ProceduralMeshBuilder b, Vector3 a, Vector3 second, Vector3 third)
        {
            bool facesWall = Vector3.Dot(Vector3.Cross(second - a, third - a), Vector3.forward) < 0f;
            b.AddRawTriangle(a, facesWall ? third : second, facesWall ? second : third, Vector3.forward, Main);
        }

        // ------------------------------------------------------------ lights

        /// <summary>A small glowing crystal in a stone cup, for the tops of the two columns before the arch. It lights nothing.</summary>
        internal static Mesh BuildLight()
        {
            var b = new ProceduralMeshBuilder();
            b.AddPrism(Vector3.zero, 0.32f, 0.42f, 0.16f, 8, Main);
            b.AddPrism(new Vector3(0f, 0.16f, 0f), 0.2f, 0.26f, 0.34f, 6, Accent);
            b.AddPrism(new Vector3(0f, 0.5f, 0f), 0.26f, 0f, 0.42f, 6, Accent);
            return b.Build(Name("Light"));
        }

        // ------------------------------------------------------------ ridges

        /// <summary>
        /// A range of hills or mountains as one mesh: peaks of uneven height and width along a line, each with
        /// a lower shoulder, overlapping so that no gap shows the valley floor between them. Seen from far
        /// off through haze it is an outline; the single pyramids of the environment's mountain module, used
        /// for the view ahead at first, read as pyramids (renders of run 21). The ridge runs along X, its
        /// foot on the origin's level. Wooded ridges are rounder: their peaks are cut off low and capped.
        /// </summary>
        internal static Mesh BuildRidge(float length, float height, int peaks, int seed, bool wooded)
        {
            var random = new Random(seed);
            var b = new ProceduralMeshBuilder();
            peaks = Mathf.Max(3, peaks);
            float spacing = length / peaks;

            for (int i = 0; i < peaks; i++)
            {
                float x = -length * 0.5f + (i + 0.5f) * spacing + EnvironmentAssets.Range(random, -0.2f, 0.2f) * spacing;
                float z = EnvironmentAssets.Range(random, -0.25f, 0.25f) * spacing;

                // Highest towards the middle of the range, never the same twice.
                float towardsMiddle = 1f - Mathf.Abs((i + 0.5f) / peaks - 0.5f) * 1.1f;
                float peak = height * Mathf.Clamp01(towardsMiddle) * EnvironmentAssets.Range(random, 0.62f, 1f);
                peak = Mathf.Max(peak, height * 0.28f);
                float radius = spacing * EnvironmentAssets.Range(random, 1.05f, 1.6f) + peak * (wooded ? 0.9f : 0.35f);
                Quaternion turn = Quaternion.Euler(0f, EnvironmentAssets.Range(random, 0f, 60f), 0f);
                var foot = new Vector3(x, 0f, z);

                if (wooded)
                {
                    b.AddPrism(foot, radius, radius * 0.42f, peak * 0.72f, 8, turn, Main);
                    b.AddPrism(foot + Vector3.up * (peak * 0.72f), radius * 0.42f, 0f, peak * 0.28f, 8, turn, Main);
                }
                else
                {
                    b.AddPrism(foot, radius, 0f, peak, 6, turn, Main);

                    // A shoulder beside the peak breaks the cone's straight side.
                    float side = random.Next(2) == 0 ? -1f : 1f;
                    b.AddPrism(foot + new Vector3(side * radius * 0.45f, 0f, -radius * 0.2f), radius * 0.7f, 0f,
                        peak * EnvironmentAssets.Range(random, 0.5f, 0.75f), 5, turn, Main);
                }
            }

            return b.Build(Name("Ridge" + seed));
        }

        // ------------------------------------------------------------ the castle

        /// <summary>
        /// A castle on a hill, to be seen from two hundred metres through haze: a silhouette, with no detail
        /// that the distance would not show. A ring of walls with round towers, a keep, one tall tower with a
        /// spire, and a viaduct running off to the left. The origin is the top of the hill; the hill reaches down
        /// to the valley floor. Sub-mesh 0 is the castle, sub-mesh 1 the hill.
        /// </summary>
        internal static Mesh BuildCastle()
        {
            var b = new ProceduralMeshBuilder();

            float foot = L.Castle.y + L.ValleyDrop;
            b.AddPrism(new Vector3(0f, -foot, 0f), 95f, 34f, foot, 9, Accent);
            b.AddPrism(new Vector3(-58f, -foot, 18f), 60f, 12f, foot * 0.7f, 7, Quaternion.Euler(0f, 25f, 0f), Accent);

            // The ring of walls and its towers.
            const int towers = 6;
            const float ringRadius = L.CastleHalfWidth - 4f;
            for (int i = 0; i < towers; i++)
            {
                float angle = (i + 0.5f) * Mathf.PI * 2f / towers;
                float next = (i + 1.5f) * Mathf.PI * 2f / towers;
                var here = new Vector3(Mathf.Cos(angle) * ringRadius, 0f, Mathf.Sin(angle) * ringRadius);
                var there = new Vector3(Mathf.Cos(next) * ringRadius, 0f, Mathf.Sin(next) * ringRadius);

                float height = 11f + (i % 3) * 2.5f;
                b.AddPrism(here + Vector3.down, 3.4f, 3.1f, height + 1f, 8, Main);
                b.AddPrism(here + Vector3.up * height, 4f, 0f, 6.5f, 8, Main);

                Vector3 middle = (here + there) * 0.5f;
                float length = Vector3.Distance(here, there);
                float yaw = Mathf.Atan2(there.x - here.x, there.z - here.z) * Mathf.Rad2Deg;
                b.AddBox(middle + Vector3.up * 3.5f, new Vector3(2.2f, 9f, length), Quaternion.Euler(0f, yaw, 0f), Main);
            }

            // The keep, and the great tower with its spire.
            b.AddBox(new Vector3(2f, 6.5f, 0f), new Vector3(17f, 15f, 14f), Main);
            b.AddBox(new Vector3(4f, 16.5f, -1f), new Vector3(10f, 7f, 9f), Main);
            b.AddPrism(new Vector3(4f, 20f, -1f), 6.5f, 0f, 5f, 4, Quaternion.Euler(0f, 45f, 0f), Main);
            b.AddPrism(new Vector3(-5f, -1f, 3f), 4.2f, 3.6f, 25f, 8, Main);
            b.AddPrism(new Vector3(-5f, 24f, 3f), 4.8f, 0f, L.CastleHeight - 24f, 8, Main);

            // The viaduct: a deck on tall piers, running left from the walls and down across the saddle.
            const int spans = 7;
            const float span = 11f;
            float start = -ringRadius - 2f;
            for (int i = 0; i < spans; i++)
            {
                float x = start - i * span;
                b.AddBox(new Vector3(x - span * 0.5f, 3.2f, 6f), new Vector3(span + 0.2f, 1.6f, 3.2f), Main);
                b.AddBox(new Vector3(x - span, 3.2f - 12f, 6f), new Vector3(2.4f, 24f, 3f), Main);
            }

            return b.Build(Name("Castle"));
        }
    }
}
