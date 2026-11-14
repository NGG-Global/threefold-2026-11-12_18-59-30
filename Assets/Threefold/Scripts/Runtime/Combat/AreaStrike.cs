using System.Collections.Generic;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>One attack that hits everything suitable inside a sector.</summary>
    public struct AreaStrikeRequest
    {
        /// <summary>Who attacks. Never hit, and its allies are passed over.</summary>
        public GameObject User;

        public AreaShape Shape;

        /// <summary>Damage to the health of each creature struck. 0 for an attack that only carries effects.</summary>
        public float Damage;

        /// <summary>Poise damage. Negative uses the damage.</summary>
        public float Stagger;

        /// <summary>The kind of magic the damage carries.</summary>
        public SpellElement Element;

        /// <summary>Most creatures hit; the nearest are chosen.</summary>
        public int MaxTargets;

        /// <summary>
        /// The magical effects the attack carries, apart from its damage. They go to every creature
        /// struck and to every object in the area that responds to magic.
        /// </summary>
        public EffectPayload Effects;

        /// <summary>The discipline the attack came from, as its selector slot, or -1.</summary>
        public int Discipline;

        public LayerMask HitLayers;

        /// <summary>
        /// Height above the origin from which the line to each target is checked for walls. A wall or
        /// column between the two takes the blow.
        /// </summary>
        public float CoverCheckHeight;
    }

    /// <summary>
    /// Resolves an attack on a sector: finds what is there, lets <see cref="TargetSelection"/> choose,
    /// then applies damage to creatures and delivers the attack's magical effects to creatures and
    /// objects alike. Enemy strikes, the Mage's melee, the Flame Wave, the Force Pulse and the Aegis
    /// guard all go through here, so they agree on who can be reached: not the attacker, not its
    /// allies, not the defeated, and nothing behind cover.
    ///
    /// Damage and effects are separate. Damage goes to <see cref="Health"/>; effects go to
    /// <see cref="IMagicEffectReceiver"/>, which a creature and an object without health can both be.
    /// All effects of one attack share one event identity.
    ///
    /// One overlap query per attack and pre-allocated buffers; no Rigidbody is involved.
    /// </summary>
    public static class AreaStrike
    {
        private const int MaxCandidates = 32;
        private const int MaxObjects = 16;

        // Shared by every strike. Strikes resolve one at a time on the main thread.
        private static readonly Collider[] Overlaps = new Collider[64];
        private static readonly Health[] Found = new Health[MaxCandidates];
        private static readonly TargetCandidate[] Candidates = new TargetCandidate[MaxCandidates];
        private static readonly int[] Selected = new int[MaxCandidates];
        private static readonly IMagicEffectReceiver[] Objects = new IMagicEffectReceiver[MaxObjects];
        private static readonly Vector3[] ObjectPoints = new Vector3[MaxObjects];
        private static readonly RaycastHit[] CoverHits = new RaycastHit[16];

        // Applying damage and effects runs other code (defeat, puzzles), which could start another strike.
        // The targets of each strike in progress are therefore copied out of the shared buffers first.
        private const int MaxNesting = 4;
        private static readonly Health[][] HitTargets = CreateBuffers<Health>(MaxCandidates);
        private static readonly IMagicEffectReceiver[][] HitObjects = CreateBuffers<IMagicEffectReceiver>(MaxObjects);
        private static readonly Vector3[][] HitObjectPoints = CreateBuffers<Vector3>(MaxObjects);
        private static int nesting;

        /// <summary>Carries out the attack.</summary>
        /// <param name="struck">Optional. Receives the creatures that were hit, nearest first.</param>
        /// <returns>How many creatures were hit.</returns>
        public static int Resolve(in AreaStrikeRequest request, List<Health> struck)
        {
            return Resolve(request, struck, out _);
        }

        /// <summary>Carries out the attack.</summary>
        /// <param name="struck">Optional. Receives the creatures that were hit, nearest first.</param>
        /// <param name="objectsAffected">
        /// How many objects (not creatures) had a property changed by the attack's effects. One that was
        /// reached but resisted, or has no matching property, is not counted.
        /// </param>
        /// <returns>How many creatures were hit.</returns>
        public static int Resolve(in AreaStrikeRequest request, List<Health> struck, out int objectsAffected)
        {
            objectsAffected = 0;
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
            bool carriesEffects = !request.Effects.IsEmpty;

            // The sphere is raised so that it covers the body height of a target standing on the same ground.
            Vector3 centre = origin + Vector3.up * (shape.VerticalReach * 0.5f);
            float radius = Mathf.Sqrt(shape.Reach * shape.Reach + shape.VerticalReach * shape.VerticalReach * 0.25f);
            int overlapCount = Physics.OverlapSphereNonAlloc(centre, radius, Overlaps, request.HitLayers,
                QueryTriggerInteraction.Ignore);

            int candidateCount = 0;
            int objectCount = 0;

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
                    // Not a creature: perhaps an object that responds to magic.
                    if (carriesEffects && objectCount < MaxObjects)
                    {
                        objectCount = ConsiderObject(other, shape, origin, coverOrigin, userTransform, request.HitLayers,
                            objectCount);
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
                    IsBehindCover(userTransform, Found[i].transform, coverOrigin, Found[i].AimPoint, request.HitLayers))
                {
                    Candidates[i].Alive = false;
                }
            }

            int hitCount = TargetSelection.Select(shape, Candidates, candidateCount, request.MaxTargets, Selected);

            // Copy the result out and release the shared buffers before anything is touched.
            Health[] targets = HitTargets[nesting];
            IMagicEffectReceiver[] objects = HitObjects[nesting];
            Vector3[] objectPoints = HitObjectPoints[nesting];

            for (int i = 0; i < hitCount; i++)
            {
                targets[i] = Found[Selected[i]];
            }

            for (int i = 0; i < objectCount; i++)
            {
                objects[i] = Objects[i];
                objectPoints[i] = ObjectPoints[i];
                Objects[i] = null;
            }

            for (int i = 0; i < candidateCount; i++)
            {
                Found[i] = null;
            }

            int eventId = carriesEffects ? MagicEffectEvents.NextId() : 0;

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

                    Vector3 direction = FlatDirection(origin, target.transform.position, shape.Forward);

                    if (request.Damage > 0f)
                    {
                        target.ApplyDamage(new DamageInfo(request.Damage, target.AimPoint, direction, user, request.Stagger,
                            request.Element));
                    }

                    struck?.Add(target);

                    // Effects after the damage, so that a push is not overridden by the smaller knockback of
                    // a stagger. A creature the damage defeated takes none.
                    if (carriesEffects && target.IsAlive && target.TryGetComponent(out IMagicEffectReceiver receiver))
                    {
                        Deliver(receiver, request.Effects, direction, target.AimPoint, user, request.Discipline, eventId);
                    }
                }

                for (int i = 0; i < objectCount; i++)
                {
                    IMagicEffectReceiver receiver = objects[i];
                    objects[i] = null;
                    if (receiver == null)
                    {
                        continue;
                    }

                    Vector3 direction = FlatDirection(origin, objectPoints[i], shape.Forward);
                    if (Deliver(receiver, request.Effects, direction, objectPoints[i], user, request.Discipline, eventId))
                    {
                        objectsAffected++;
                    }
                }
            }
            finally
            {
                nesting--;
            }

            return hitCount;
        }

        /// <summary>
        /// Hands a payload to one receiver as separate effects, in a fixed order, all with the same
        /// event identity. Also used by projectiles, so every attack delivers effects the same way.
        /// </summary>
        /// <returns>True if at least one of the effects changed something on the receiver.</returns>
        public static bool Deliver(IMagicEffectReceiver receiver, in EffectPayload payload, Vector3 direction, Vector3 point,
            GameObject source, int discipline, int eventId)
        {
            if (receiver == null)
            {
                return false;
            }

            EffectTargets targets = payload.targets == EffectTargets.None ? EffectTargets.All : payload.targets;
            bool applied = false;

            if (payload.heat > 0f)
            {
                applied |= receiver.ReceiveEffect(new MagicEffect(MagicEffectKind.Heat, payload.heat, direction, 0f, point,
                    source, discipline, targets, eventId)) == EffectResponse.Applied;
            }

            if (payload.arcaneCharge > 0f)
            {
                applied |= receiver.ReceiveEffect(new MagicEffect(MagicEffectKind.ArcaneCharge, payload.arcaneCharge,
                    direction, 0f, point, source, discipline, targets, eventId)) == EffectResponse.Applied;
            }

            if (payload.force > 0f)
            {
                applied |= receiver.ReceiveEffect(new MagicEffect(MagicEffectKind.Force, payload.force, direction, 0f, point,
                    source, discipline, targets, eventId)) == EffectResponse.Applied;
            }

            if (payload.stabilize > 0f)
            {
                applied |= receiver.ReceiveEffect(new MagicEffect(MagicEffectKind.Stabilize, payload.stabilize, direction,
                    payload.stabilizeSeconds, point, source, discipline, targets, eventId)) == EffectResponse.Applied;
            }

            return applied;
        }

        /// <summary>
        /// Fills a buffer with the possible targets near a point, for choosing which one a swing should
        /// turn towards. Uses the same rules as <see cref="Resolve"/> for who counts.
        /// </summary>
        /// <param name="verticalReach">Targets further above or below than this are left out.</param>
        /// <returns>How many candidates were written.</returns>
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
                                 !IsBehindCover(userTransform, target.transform, origin + Vector3.up * (verticalReach * 0.5f),
                                     target.AimPoint, layers);

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

        /// <summary>Adds an object that responds to magic if it lies in the area, is not listed yet and is not behind cover.</summary>
        private static int ConsiderObject(Collider other, in AreaShape shape, Vector3 origin, Vector3 coverOrigin,
            Transform userTransform, LayerMask layers, int count)
        {
            IMagicEffectReceiver receiver = other.GetComponentInParent<IMagicEffectReceiver>();
            if (receiver == null)
            {
                return count;
            }

            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(Objects[i], receiver))
                {
                    return count;
                }
            }

            Vector3 nearest = other.bounds.ClosestPoint(coverOrigin);
            if (!TargetSelection.IsInside(shape, new Vector3(nearest.x, origin.y, nearest.z)))
            {
                return count;
            }

            Transform receiverTransform = receiver is Component component ? component.transform : other.transform;
            if (IsBehindCover(userTransform, receiverTransform, coverOrigin, nearest, layers))
            {
                return count;
            }

            Objects[count] = receiver;
            ObjectPoints[count] = nearest;
            return count + 1;
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

        private static Vector3 FlatDirection(Vector3 from, Vector3 to, Vector3 fallback)
        {
            Vector3 direction = to - from;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : fallback;
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

        /// <summary>
        /// True if a wall, a column or other scenery lies between the attacker and the point on the
        /// target. Creatures are not cover: an area attack reaches the second rank as well as the first.
        /// </summary>
        public static bool IsBehindCover(Transform userTransform, Transform targetTransform, Vector3 from, Vector3 to,
            LayerMask layers)
        {
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
                if (hit.IsChildOf(userTransform) || hit.IsChildOf(targetTransform))
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
