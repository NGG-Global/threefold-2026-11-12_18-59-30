using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Builds low-polygon meshes from simple solids: boxes, prisms, cones and free triangles.
    /// Faces are flat shaded, which gives the faceted, stylised look of the environment, and each
    /// shape is assigned to a sub-mesh so one mesh can use several materials (stone and moss, for
    /// example).
    ///
    /// Every face is given the direction it should face. The builder orders the triangle's corners
    /// to match, so a shape can never come out inside-out.
    /// </summary>
    internal sealed class ProceduralMeshBuilder
    {
        /// <summary>Texture repeats per metre. One repeat every four metres.</summary>
        public const float UvScale = 0.25f;

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<List<int>> triangles = new List<List<int>>();

        public int VertexCount => vertices.Count;

        /// <summary>Adds a flat-shaded triangle that faces roughly towards <paramref name="outward"/>.</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, int subMesh)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-12f)
            {
                return;
            }

            // Unity treats a triangle as front-facing when Cross(b - a, c - a) points at the viewer.
            if (Vector3.Dot(normal, outward) < 0f)
            {
                Vector3 swap = b;
                b = c;
                c = swap;
                normal = -normal;
            }

            normal.Normalize();
            AddRawTriangle(a, b, c, normal, subMesh);
        }

        /// <summary>
        /// Adds a triangle with a given normal and without reordering its corners. Used for foliage,
        /// which is drawn double-sided and lit as if it faced upwards.
        /// </summary>
        public void AddRawTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, int subMesh)
        {
            List<int> indices = GetSubMesh(subMesh);
            int start = vertices.Count;

            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            uvs.Add(ProjectUv(a, normal));
            uvs.Add(ProjectUv(b, normal));
            uvs.Add(ProjectUv(c, normal));

            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
        }

        /// <summary>Adds a flat four-sided face. Corners are given in order around its edge.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, int subMesh)
        {
            AddTriangle(a, b, c, outward, subMesh);
            AddTriangle(a, c, d, outward, subMesh);
        }

        /// <summary>Adds a box.</summary>
        /// <param name="centre">Centre of the box.</param>
        /// <param name="size">Full width, height and depth.</param>
        public void AddBox(Vector3 centre, Vector3 size, Quaternion rotation, int subMesh)
        {
            Vector3 half = size * 0.5f;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;

            AddBoxFace(centre, right, up, forward, half.x, half.y, half.z, subMesh);
            AddBoxFace(centre, -right, up, forward, half.x, half.y, half.z, subMesh);
            AddBoxFace(centre, up, forward, right, half.y, half.z, half.x, subMesh);
            AddBoxFace(centre, -up, forward, right, half.y, half.z, half.x, subMesh);
            AddBoxFace(centre, forward, right, up, half.z, half.x, half.y, subMesh);
            AddBoxFace(centre, -forward, right, up, half.z, half.x, half.y, subMesh);
        }

        public void AddBox(Vector3 centre, Vector3 size, int subMesh)
        {
            AddBox(centre, size, Quaternion.identity, subMesh);
        }

        /// <summary>
        /// Adds an upright prism with a regular polygon as its base. With different radii it is a
        /// tapered column; with a radius of zero at one end it is a cone.
        /// </summary>
        /// <param name="baseCentre">Centre of the bottom face.</param>
        /// <param name="rotation">Rotation about the centre of the bottom face.</param>
        public void AddPrism(Vector3 baseCentre, float bottomRadius, float topRadius, float height, int sides,
            Quaternion rotation, int subMesh)
        {
            sides = Mathf.Max(3, sides);
            Vector3 up = rotation * Vector3.up;
            Vector3 topCentre = baseCentre + up * height;

            for (int i = 0; i < sides; i++)
            {
                float angleA = i * Mathf.PI * 2f / sides;
                float angleB = (i + 1) * Mathf.PI * 2f / sides;
                float angleMid = (angleA + angleB) * 0.5f;

                Vector3 directionA = rotation * new Vector3(Mathf.Cos(angleA), 0f, Mathf.Sin(angleA));
                Vector3 directionB = rotation * new Vector3(Mathf.Cos(angleB), 0f, Mathf.Sin(angleB));
                Vector3 outward = rotation * new Vector3(Mathf.Cos(angleMid), 0f, Mathf.Sin(angleMid));

                Vector3 bottomA = baseCentre + directionA * bottomRadius;
                Vector3 bottomB = baseCentre + directionB * bottomRadius;
                Vector3 topA = topCentre + directionA * topRadius;
                Vector3 topB = topCentre + directionB * topRadius;

                // Degenerate halves (at the tip of a cone) are skipped inside AddTriangle.
                AddTriangle(bottomA, bottomB, topB, outward, subMesh);
                AddTriangle(bottomA, topB, topA, outward, subMesh);

                if (topRadius > 0.0001f)
                {
                    AddTriangle(topCentre, topA, topB, up, subMesh);
                }

                if (bottomRadius > 0.0001f)
                {
                    AddTriangle(baseCentre, bottomA, bottomB, -up, subMesh);
                }
            }
        }

        public void AddPrism(Vector3 baseCentre, float bottomRadius, float topRadius, float height, int sides, int subMesh)
        {
            AddPrism(baseCentre, bottomRadius, topRadius, height, sides, Quaternion.identity, subMesh);
        }

        /// <summary>Creates the mesh. Sub-meshes that received no triangles are still created, so material slots stay aligned.</summary>
        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };

            if (vertices.Count > 65000)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = Mathf.Max(1, triangles.Count);

            for (int i = 0; i < triangles.Count; i++)
            {
                mesh.SetTriangles(triangles[i], i);
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        private void AddBoxFace(Vector3 centre, Vector3 normal, Vector3 axisA, Vector3 axisB, float distance,
            float halfA, float halfB, int subMesh)
        {
            Vector3 faceCentre = centre + normal * distance;
            Vector3 a = axisA * halfA;
            Vector3 b = axisB * halfB;

            AddQuad(faceCentre - a - b, faceCentre + a - b, faceCentre + a + b, faceCentre - a + b, normal, subMesh);
        }

        private List<int> GetSubMesh(int subMesh)
        {
            while (triangles.Count <= subMesh)
            {
                triangles.Add(new List<int>());
            }

            return triangles[subMesh];
        }

        /// <summary>
        /// Projects the texture along the axis the face mostly points along, at a fixed scale in
        /// metres. Textures therefore have the same size on every module, whatever its dimensions.
        /// </summary>
        private static Vector2 ProjectUv(Vector3 position, Vector3 normal)
        {
            float x = Mathf.Abs(normal.x);
            float y = Mathf.Abs(normal.y);
            float z = Mathf.Abs(normal.z);

            if (y >= x && y >= z)
            {
                return new Vector2(position.x, position.z) * UvScale;
            }

            return x >= z
                ? new Vector2(position.z, position.y) * UvScale
                : new Vector2(position.x, position.y) * UvScale;
        }
    }
}
