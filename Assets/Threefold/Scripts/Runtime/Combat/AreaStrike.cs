using System.Collections.Generic;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>One attack that hits everything suitable inside a sector.</summary>
    public struct AreaStrikeRequest
    {
        /// <summary>Who attacks. Never hit, and its allies are passed over.</summary>
        public GameObject User;

        public AreaShape Shape;
        public float Damage;

        /// <summary>Poise damage. Negative uses the damage.</summary>
        public float Stagger;

        public SpellElement Element;

        /// <summary>Most targets hit; the nearest are chosen.</summary>
        public int MaxTargets;

        /// <summary>Heat added to each struck target that can be heated.</summary>
        public float Heat;

        /// <summary>Metres each struck target that can be pushed is moved, away from the origin.</summary>
        public float PushDistance;

        public LayerMask HitLayers;

        /// <summary>
        /// Height above the origin from which the line to each target is checked for walls. A wall or
        /// column between the two takes the blow.
        /// </summary>
        public float CoverCheckHeight;
    }

    /// <summary>
    /// Resolves an attack on a sector: finds what is there, lets <see cref="TargetSelection"/> choose,
    /// and applies damage, heat and force. Enemy strikes, the Mage's melee, the Flame Wave and the
    /// Force Pulse all go through here, so they agree on who can be hit: not the attacker, not its
    /// allies, not the defeated, and nothing behind cover.
    ///
    /// One overlap query per attack and pre-allocated buffers; no Rigidbody is involved.
    /// </summary>
    public static class AreaStrike
    {
        private const int MaxCandidates = 32;

        // Shared by every strike. Strikes resolve one at a time on the main thread.
        private static readonly Collider[] Overlaps = new Collider[64];
        private static readonly Health[] Found = new Health[MaxCandidates];
        private static readonly TargetCandidate[] Candidates = new TargetCandidate[MaxCandidates];
        private static readonly int[] Selected = new int[MaxCandidates];
        private static readonly ISpellReceiver[] Receivers = new ISpellReceiver[16];
        private static readonly RaycastHit[] CoverHits = new RaycastHit[16];

        // Applying damage runs other code (defeat, puzzles), which could start another strike. The targets
        // of each strike in progress are therefore copied out of the shared buffers before any damage.
        private const int MaxNesting = 4;
        private static readonly Health[][] HitTargets = CreateBuffers<Health>(MaxCandidates);
        private static readonly ISpellReceiver[][] HitReceivers = CreateBuffers<ISpellReceiver>(16);
        private static int nesting;

        /// <summary>Carries out the attack.</summary>
        /// <param name="struck">Optional. Receives the targets that were hit, nearest first.</param>
        /// <returns>How many targets were hit.</returns>
        public static int Resolve(in AreaStrikeRequest request, List<Health> struck)
        {
            struck?.Clear();

            GameObject user = request.User;
            if (user == null || nesting >= MaxNesting)
            {
                return 0;
            }

            Transform userTransform = user.transform;
            user.TryGetComponent(out Health userHealth);

            AreaShape shape = request.Shape;
            Vector3 origin = shape.Origin;
            Vector3 coverOrigin = origin + Vector3.up * request.CoverCheckHeight;

            // The sphere is raised so that it covers the body height of a target standing on the same ground.
            Vector3 centre = origin + Vector3.up * (shape.VerticalReach * 0.5f);
            float radius = Mathf.Sqrt(shape.Reach * shape.Reach + shape.VerticalReach * shape.VerticalReach * 0.25f);
            int overlapCount = Physics.OverlapSphereNonAlloc(centre, radius, Overlaps, request.HitLayers,
                QueryTriggerInteraction.Ignore);

            int candidateCount = 0;
            int receiverCount = 0;

            for (int i = 0; i < overlapCount; i++)
            {
                Collider other = Overlaps[i];
                Overlaps[i] = null;

                if (other == null || other.transform.IsChildOf(userTransform))
                {
                    continue;
                }

                Health target = other.GetComponentInParent<Health>();
                if (target == null)
                {
                    // Not a creature: perhaps a magical object that answers this kind of magic.
                    if (request.Element != SpellElement.None && receiverCount < Receivers.Length)
                    {
                        ISpellReceiver receiver = other.GetComponentInParent<ISpellReceiver>();
                        if (receiver != null && !ContainsReceiver(receiver, receiverCount) &&
                            TargetSelection.IsInside(shape, FlatClosestPoint(other, origin, other.bounds.center.y)))
                        {
                            Receivers[receiverCount++] = receiver;
                        }
                    }

                    continue;
                }

                // Measured to the nearest point of the collider's bounds, so a wide target is hit at its
                // edge. Bounds work for every collider type, including a CharacterController.
                Vector3 bodyPoint = FlatClosestPoint(other, origin, target.transform.position.y);

                int index = IndexOf(target, candidateCount);
                if (index >= 0)
                {
                    // A target with several colliders counts once: at a point inside the area if any of
                    // its colliders reaches in, and at the nearest such point.
                    bool newInside = TargetSelection.IsInside(shape, bodyPoint);
                    bool oldInside = TargetSelection.IsInside(shape, Candidates[index].Position);
                    bool nearer = (bodyPoint - origin).sqrMagnitude < (Candidates[index].Position - origin).sqrMagnitude;
                    if ((newInside && !oldInside) || (newInside == oldInside && nearer))
                    {
                        Candidates[index].Position = bodyPoint;
                    }

                    continue;
                }

                if (candidateCount >= MaxCandidates)
                {
                    continue;
                }

                Found[candidateCount] = target;
                Candidates[candidateCount] = new TargetCandidate
                {
                    Position = bodyPoint,
                    Alive = target.IsAlive,
                    Allied = userHealth != null && userHealth.IsAlliedWith(target),
                    IsSelf = target == userHealth,
                };
                candidateCount++;
            }

            // Cover is checked before the limit is applied, so a target behind a wall does not use up a place.
            for (int i = 0; i < candidateCount; i++)
            {
                if (TargetSelection.IsValid(Candidates[i]) && TargetSelection.IsInside(shape, Candidates[i].Position) &&
                    IsBehindCover(userTransform, userHealth, Found[i], coverOrigin, request.HitLayers))
                {
                    Candidates[i].Alive = false;
                }
            }

            int hitCount = TargetSelection.Select(shape, Candidates, candidateCount, request.MaxTargets, Selected);

            // Copy the result out and release the shared buffers before anything is damaged.
            Health[] targets = HitTargets[nesting];
            ISpellReceiver[] receivers = HitReceivers[nesting];

            for (int i = 0; i < hitCount; i++)
            {
                targets[i] = Found[Selected[i]];
            }

            for (int i = 0; i < receiverCount; i++)
            {
                receivers[i] = Receivers[i];
                Receivers[i] = null;
            }

            for (int i = 0; i < candidateCount; i++)
            {
                Found[i] = null;
            }

            nesting++;
            try
            {
                for (int i = 0; i < hitCount; i++)
                {
                    Health target = targets[i];
                    targets[i] = null;
                    if (target == null || !target.IsAlive)
                    {
                        continue;
                    }

                    Vector3 direction = target.transform.position - origin;
                    direction.y = 0f;
                    direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : shape.Forward;

                    target.ApplyDamage(new DamageInfo(request.Damage, target.AimPoint, direction, user, request.Stagger,
                        request.Element));
                    struck?.Add(target);

                    if (request.Element != SpellElement.None && receiverCount < receivers.Length)
                    {
                        ISpellReceiver receiver = target.GetComponentInParent<ISpellReceiver>();
                        if (receiver != null && System.Array.IndexOf(receivers, receiver, 0, receiverCount) < 0)
                        {
                            receivers[receiverCount++] = receiver;
                        }
                    }

                    if (!target.IsAlive)
                    {
                        continue;
                    }

                    if (request.Heat > 0f && target.TryGetComponent(out IHeatReceiver heat))
                    {
                        heat.ReceiveHeat(request.Heat, user);
                    }

                    // After the damage, so that a push is not overridden by the smaller knockback of a stagger.
                    if (request.PushDistance > 0f && target.TryGetComponent(out IForceReceiver force))
                    {
                        force.ReceiveForce(new ForceImpulse(direction, request.PushDistance, user));
                    }
                }

                for (int i = 0; i < receiverCount; i++)
                {
                    ISpellReceiver receiver = receivers[i];
                    receivers[i] = null;
                    receiver?.ReceiveSpell(new SpellImpact(request.Element, origin, shape.Forward, user));
                }
            }
            finally
            {
                nesting--;
            }

            return hitCount;
        }

        /// <summary>
        /// Fills a buffer with the possible targets near a point, for choosing which one a swing should
        /// turn towards. Uses the same rules as <see cref="Resolve"/> for who counts.
        /// </summary>
        /// <returns>How many candidates were written.</returns>
        /// <param name="verticalReach">Targets further above or below than this are left out.</param>
        public static int CollectCandidates(GameObject user, Vector3 origin, float range, float verticalReach,
            LayerMask layers, TargetCandidate[] candidates)
        {
            if (user == null || candidates == null)
            {
                return 0;
            }

            Transform userTransform = user.transform;
            user.TryGetComponent(out Health userHealth);

            int overlapCount = Physics.OverlapSphereNonAlloc(origin + Vector3.up, range + 1f, Overlaps, layers,
                QueryTriggerInteraction.Ignore);
            int count = 0;

            for (int i = 0; i < overlapCount; i++)
            {
                Collider other = Overlaps[i];
                Overlaps[i] = null;

                if (other == null || other.transform.IsChildOf(userTransform) || count >= candidates.Length ||
                    count >= MaxCandidates)
                {
                    continue;
                }

                Health target = other.GetComponentInParent<Health>();
                if (target == null || IndexOf(target, count) >= 0)
                {
                    continue;
                }

                // A target the swing could not hit must not attract it: one on a ledge above or below, or
                // one behind a wall.
                Vector3 position = FlatClosestPoint(other, origin, target.transform.position.y);
                bool reachable = target.IsAlive && Mathf.Abs(position.y - origin.y) <= verticalReach &&
                                 !IsBehindCover(userTransform, userHealth, target, origin + Vector3.up * (verticalReach * 0.5f), layers);

                Found[count] = target;
                candidates[count] = new TargetCandidate
                {
                    Position = position,
                    Alive = reachable,
                    Allied = userHealth != null && userHealth.IsAlliedWith(target),
                    IsSelf = target == userHealth,
                };
                count++;
            }

            for (int i = 0; i < count; i++)
            {
                Found[i] = null;
            }

            return count;
        }

        // Static state survives between Play sessions when domain reload is disabled in the Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            nesting = 0;
        }

        private static T[][] CreateBuffers<T>(int size)
        {
            var buffers = new T[MaxNesting][];
            for (int i = 0; i < buffers.Length; i++)
            {
                buffers[i] = new T[size];
            }

            return buffers;
        }

        private static Vector3 FlatClosestPoint(Collider collider, Vector3 origin, float height)
        {
            Vector3 closest = collider.bounds.ClosestPoint(origin);
            return new Vector3(closest.x, height, closest.z);
        }

        private static int IndexOf(Health target, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (Found[i] == target)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool ContainsReceiver(ISpellReceiver receiver, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(Receivers[i], receiver))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True if a wall, a column or other scenery lies between the attacker and the target. Creatures
        /// are not cover: an area attack reaches the second rank as well as the first.
        /// </summary>
        private static bool IsBehindCover(Transform userTransform, Health userHealth, Health target, Vector3 from,
            LayerMask layers)
        {
            Vector3 to = target.AimPoint;
            Vector3 offset = to - from;
            float length = offset.magnitude;
            if (length < 0.01f)
            {
                return false;
            }

            int count = Physics.RaycastNonAlloc(from, offset / length, CoverHits, length, layers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Transform hit = CoverHits[i].transform;
                if (hit.IsChildOf(userTransform) || hit.IsChildOf(target.transform))
                {
                    continue;
                }

                if (hit.GetComponentInParent<Health>() != null)
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }
}
