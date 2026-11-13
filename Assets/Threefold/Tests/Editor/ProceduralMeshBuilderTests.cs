using NUnit.Framework;
using Threefold.EditorTools;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// Checks the generated geometry. The most important property is that every face points
    /// outwards: a face that points inwards is invisible in the game.
    /// </summary>
    public sealed class ProceduralMeshBuilderTests
    {
        private Mesh mesh;

        [TearDown]
        public void TearDown()
        {
            if (mesh != null)
            {
                Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>Asserts that every triangle of the mesh faces away from the given interior point.</summary>
        private static void AssertFacesPointAwayFrom(Mesh target, Vector3 inside)
        {
            Vector3[] vertices = target.vertices;
            Vector3[] normals = target.normals;

            for (int subMesh = 0; subMesh < target.subMeshCount; subMesh++)
            {
                int[] indices = target.GetTriangles(subMesh);
                Assert.AreEqual(0, indices.Length % 3);

                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 a = vertices[indices[i]];
                    Vector3 b = vertices[indices[i + 1]];
                    Vector3 c = vertices[indices[i + 2]];

                    // Unity draws the side of a triangle that Cross(b - a, c - a) points towards.
                    Vector3 facing = Vector3.Cross(b - a, c - a);
                    Vector3 fromInside = (a + b + c) / 3f - inside;

                    Assert.Greater(facing.magnitude, 1e-6f, "The mesh contains a triangle with no area.");
                    Assert.Greater(Vector3.Dot(facing, fromInside), 0f, $"Triangle {i / 3} faces inwards.");
                    Assert.Greater(Vector3.Dot(normals[indices[i]], fromInside), 0f, $"Normal of triangle {i / 3} points inwards.");
                    Assert.AreEqual(1f, normals[indices[i]].magnitude, 0.001f);
                }
            }
        }

        [Test]
        public void BoxHasTwelveOutwardFacingTriangles()
        {
            var builder = new ProceduralMeshBuilder();
            var centre = new Vector3(2f, 1f, -3f);
            builder.AddBox(centre, new Vector3(4f, 2f, 1f), 0);

            mesh = builder.Build("Box");

            Assert.AreEqual(36, mesh.GetTriangles(0).Length);
            AssertFacesPointAwayFrom(mesh, centre);
        }

        [Test]
        public void BoxBoundsMatchItsSize()
        {
            var builder = new ProceduralMeshBuilder();
            builder.AddBox(new Vector3(0f, 1f, 0f), new Vector3(4f, 2f, 6f), 0);

            mesh = builder.Build("Box");

            Assert.AreEqual(4f, mesh.bounds.size.x, 0.001f);
            Assert.AreEqual(2f, mesh.bounds.size.y, 0.001f);
            Assert.AreEqual(6f, mesh.bounds.size.z, 0.001f);
            Assert.AreEqual(1f, mesh.bounds.center.y, 0.001f);
        }

        [Test]
        public void RotatedBoxStillFacesOutwards()
        {
            var builder = new ProceduralMeshBuilder();
            var centre = new Vector3(0f, 5f, 0f);
            builder.AddBox(centre, new Vector3(1f, 2f, 3f), Quaternion.Euler(30f, 115f, -50f), 0);

            mesh = builder.Build("RotatedBox");

            AssertFacesPointAwayFrom(mesh, centre);
        }

        [Test]
        public void TaperedPrismFacesOutwards()
        {
            var builder = new ProceduralMeshBuilder();
            builder.AddPrism(Vector3.zero, 0.5f, 0.3f, 4f, 10, 0);

            mesh = builder.Build("Column");

            // Ten sides of two triangles each, plus ten triangles in each of the two caps.
            Assert.AreEqual(40 * 3, mesh.GetTriangles(0).Length);
            AssertFacesPointAwayFrom(mesh, new Vector3(0f, 2f, 0f));
        }

        [Test]
        public void ConeHasNoDegenerateTrianglesAndFacesOutwards()
        {
            var builder = new ProceduralMeshBuilder();
            builder.AddPrism(Vector3.zero, 2f, 0f, 3f, 7, 0);

            mesh = builder.Build("Cone");

            // Seven side triangles and seven in the base. The tip adds none.
            Assert.AreEqual(14 * 3, mesh.GetTriangles(0).Length);
            AssertFacesPointAwayFrom(mesh, new Vector3(0f, 1f, 0f));
        }

        [Test]
        public void InvertedConeFacesOutwards()
        {
            var builder = new ProceduralMeshBuilder();
            builder.AddPrism(new Vector3(0f, -1f, 0f), 0f, 0.5f, 1f, 6, 0);

            mesh = builder.Build("InvertedCone");

            AssertFacesPointAwayFrom(mesh, new Vector3(0f, -0.3f, 0f));
        }

        [Test]
        public void PrismLyingOnItsSideFacesOutwards()
        {
            var builder = new ProceduralMeshBuilder();
            builder.AddPrism(new Vector3(-2f, 0.4f, 0f), 0.4f, 0.4f, 4f, 8, Quaternion.Euler(0f, 0f, -90f), 0);

            mesh = builder.Build("FallenColumn");

            // Rotated by -90 degrees about Z, the prism's axis runs along +X.
            Assert.AreEqual(4f, mesh.bounds.size.x, 0.01f);
            AssertFacesPointAwayFrom(mesh, new Vector3(0f, 0.4f, 0f));
        }

        [Test]
        public void ShapesGoToTheRequestedSubMesh()
        {
            var builder = new ProceduralMeshBuilder();
            builder.AddBox(Vector3.zero, Vector3.one, 0);
            builder.AddBox(new Vector3(0f, 2f, 0f), Vector3.one, 1);

            mesh = builder.Build("TwoMaterials");

            Assert.AreEqual(2, mesh.subMeshCount);
            Assert.AreEqual(36, mesh.GetTriangles(0).Length);
            Assert.AreEqual(36, mesh.GetTriangles(1).Length);
        }

        [Test]
        public void TextureScaleIsTheSameOnEveryBox()
        {
            var builder = new ProceduralMeshBuilder();
            builder.AddBox(Vector3.zero, new Vector3(8f, 8f, 8f), 0);

            mesh = builder.Build("UvBox");

            // An eight metre face spans two repeats at one repeat per four metres.
            Vector2 minimum = mesh.uv[0];
            Vector2 maximum = mesh.uv[0];
            foreach (Vector2 uv in mesh.uv)
            {
                minimum = Vector2.Min(minimum, uv);
                maximum = Vector2.Max(maximum, uv);
            }

            Assert.AreEqual(8f * ProceduralMeshBuilder.UvScale, maximum.x - minimum.x, 0.001f);
            Assert.AreEqual(8f * ProceduralMeshBuilder.UvScale, maximum.y - minimum.y, 0.001f);
        }

        [Test]
        public void GroundIsLevelUnderTheCourtyardAndNeverBelowIt()
        {
            Assert.AreEqual(FantasyPlaygroundSetup.TerrainHeight(0f, 0f), FantasyPlaygroundSetup.TerrainHeight(17f, -17f), 0.0001f);

            for (float x = -90f; x <= 90f; x += 7.5f)
            {
                for (float z = -90f; z <= 90f; z += 7.5f)
                {
                    Assert.GreaterOrEqual(FantasyPlaygroundSetup.TerrainHeight(x, z), -0.0501f,
                        $"The ground at ({x}, {z}) dips below the courtyard.");
                }
            }
        }

        [Test]
        public void GroundRisesTowardsTheForest()
        {
            Assert.Greater(FantasyPlaygroundSetup.TerrainHeight(0f, 80f), FantasyPlaygroundSetup.TerrainHeight(0f, 22f));
            Assert.Greater(FantasyPlaygroundSetup.TerrainHeight(-80f, 0f), 4f);
        }
    }
}
