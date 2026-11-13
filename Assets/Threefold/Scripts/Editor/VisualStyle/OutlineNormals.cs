using System;
using System.Collections.Generic;
using UnityEngine;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Smoothed normals for contour lines. A flat-shaded mesh stores several vertices at each
    /// corner, one per face, each with that face's normal. Pushing those outwards separately tears
    /// the outline open along every edge. Here every vertex at the same position receives the same
    /// direction, the average of the distinct normals that meet there, so the pushed-out shell
    /// stays closed.
    ///
    /// The result is stored in the mesh's tangents by the model importer. Unity deforms tangents
    /// with the skeleton exactly as it deforms normals, so the outline follows an animated
    /// character. The style's materials use no normal maps, so the tangent channel is free.
    /// </summary>
    public static class OutlineNormals
    {
        /// <summary>Positions closer together than this, in metres, count as the same corner.</summary>
        public const float WeldDistance = 0.0001f;

        /// <summary>
        /// Returns one direction per vertex. Vertices that share a position share the result.
        /// </summary>
        public static Vector3[] Smooth(Vector3[] positions, Vector3[] normals)
        {
            if (positions == null || normals == null || positions.Length != normals.Length)
            {
                throw new ArgumentException("Positions and normals must have the same length.");
            }

            int count = positions.Length;
            var groups = new Dictionary<(int, int, int), List<int>>(count);

            for (int i = 0; i < count; i++)
            {
                (int, int, int) key = Key(positions[i]);
                if (!groups.TryGetValue(key, out List<int> members))
                {
                    members = new List<int>(4);
                    groups.Add(key, members);
                }

                members.Add(i);
            }

            var result = new Vector3[count];
            var distinct = new List<Vector3>(8);

            foreach (List<int> members in groups.Values)
            {
                // Each distinct normal counts once, however many vertices repeat it. Otherwise a
                // face that happens to be split into more triangles would pull the average its way.
                distinct.Clear();
                foreach (int index in members)
                {
                    Vector3 normal = normals[index];
                    bool known = false;
                    for (int d = 0; d < distinct.Count; d++)
                    {
                        if ((distinct[d] - normal).sqrMagnitude < 0.0001f)
                        {
                            known = true;
                            break;
                        }
                    }

                    if (!known)
                    {
                        distinct.Add(normal);
                    }
                }

                Vector3 sum = Vector3.zero;
                foreach (Vector3 normal in distinct)
                {
                    sum += normal;
                }

                // Opposite normals (the two sides of a thin sheet at one position) cancel out; then
                // each vertex keeps its own normal.
                bool usable = sum.sqrMagnitude > 0.000001f;
                Vector3 average = usable ? sum.normalized : Vector3.zero;

                foreach (int index in members)
                {
                    result[index] = usable ? average : normals[index];
                }
            }

            return result;
        }

        private static (int, int, int) Key(Vector3 position)
        {
            return ((int)Math.Round(position.x / WeldDistance), (int)Math.Round(position.y / WeldDistance),
                (int)Math.Round(position.z / WeldDistance));
        }
    }
}
