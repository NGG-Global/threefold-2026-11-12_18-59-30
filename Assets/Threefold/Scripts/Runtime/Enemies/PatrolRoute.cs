using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>
    /// The points an enemy walks between while it is unaware, in order and then back to the
    /// first. Points are usually child objects, so the route can be moved in the Scene view.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PatrolRoute : MonoBehaviour
    {
        [SerializeField] private Transform[] points = new Transform[0];

        public int Count => points != null ? points.Length : 0;

        /// <summary>Sets the points. Called by the setup tooling.</summary>
        public void SetPoints(Transform[] routePoints)
        {
            points = routePoints ?? new Transform[0];
        }

        public Vector3 GetPoint(int index)
        {
            if (Count == 0)
            {
                return transform.position;
            }

            Transform point = points[((index % Count) + Count) % Count];
            return point != null ? point.position : transform.position;
        }

        private void OnDrawGizmos()
        {
            if (Count < 2)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.75f, 0.3f, 0.8f);
            for (int i = 0; i < Count; i++)
            {
                Vector3 from = GetPoint(i) + Vector3.up * 0.1f;
                Vector3 to = GetPoint(i + 1) + Vector3.up * 0.1f;
                Gizmos.DrawLine(from, to);
                Gizmos.DrawWireSphere(from, 0.25f);
            }
        }
    }
}
