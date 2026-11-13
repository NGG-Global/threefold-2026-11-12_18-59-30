using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Flat meshes for showing where an attack reaches: a sector on the ground for swings, waves,
    /// pulses and enemy warnings, and a ring for an aiming reticle. Built in code; no mesh assets.
    /// </summary>
    public static class AimShapes
    {
        private const float SegmentDegrees = 6f;

        /// <summary>
        /// A fan lying on the ground (facing up), with its point at the origin and opening towards +Z.
        /// </summary>
        public static Mesh BuildSector(float reach, float arcDegrees, string meshName = "Sector")
        {
            arcDegrees = Mathf.Clamp(arcDegrees, 10f, 360f);
            int segments = Mathf.Max(3, Mathf.CeilToInt(arcDegrees / SegmentDegrees));
            var vertices = new Vector3[segments + 2];
            var uvs = new Vector2[segments + 2];
            var triangles = new int[segments * 3];

            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            float start = -arcDegrees * 0.5f;

            for (int i = 0; i <= segments; i++)
            {
                float radians = (start + arcDegrees * i / segments) * Mathf.Deg2Rad;
                var direction = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                vertices[i + 1] = direction * reach;
                uvs[i + 1] = new Vector2(0.5f + direction.x * 0.5f, 0.5f + direction.z * 0.5f);
            }

            // Angles increase clockwise seen from above, so this winding faces upwards.
            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }

            var mesh = new Mesh { name = meshName };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A flat ring in the XY plane, facing -Z, for a reticle that is turned towards the camera.</summary>
        public static Mesh BuildRing(float innerRadius, float outerRadius, int segments = 32, string meshName = "Ring")
        {
            segments = Mathf.Max(8, segments);
            var vertices = new Vector3[segments * 2];
            var uvs = new Vector2[segments * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float radians = Mathf.PI * 2f * i / segments;
                var direction = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f);
                vertices[i * 2] = direction * innerRadius;
                vertices[i * 2 + 1] = direction * outerRadius;
                uvs[i * 2] = new Vector2(0.5f, 0.5f);
                uvs[i * 2 + 1] = new Vector2(0.5f + direction.x * 0.5f, 0.5f + direction.y * 0.5f);

                int next = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = i * 2 + 1;
                triangles[t + 2] = next * 2 + 1;
                triangles[t + 3] = i * 2;
                triangles[t + 4] = next * 2 + 1;
                triangles[t + 5] = next * 2;
            }

            var mesh = new Mesh { name = meshName };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
