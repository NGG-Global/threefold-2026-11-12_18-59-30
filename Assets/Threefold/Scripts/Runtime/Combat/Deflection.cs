using System;
using Threefold.WorldMagic;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// Whether, and how, force can turn a projectile aside. Carried by a projectile from its launch.
    /// Only projectiles designated here can be deflected; the Mage's own shots are not.
    /// </summary>
    [Serializable]
    public struct ProjectileDeflection
    {
        [Tooltip("Designated: a force at least as strong as below turns this projectile aside.")]
        public bool deflectable;

        [Tooltip("The least force that turns it. The Aegis guard and Aegis attacks carry force; the other disciplines do not.")]
        [Min(0f)]
        public float forceToDeflect;

        [Tooltip("How often one projectile can be turned. It ends ping-pong: a deflected shot cannot be sent back again.")]
        [Min(0)]
        public int maxDeflections;

        [Tooltip("Speed after a deflection, as a share of the speed before.")]
        [Min(0.1f)]
        public float speedMultiplier;

        public bool IsDesignated => deflectable && maxDeflections > 0;

        public static ProjectileDeflection Designated(float forceToDeflect, int maxDeflections = 1, float speedMultiplier = 1.3f)
        {
            return new ProjectileDeflection
            {
                deflectable = true,
                forceToDeflect = Mathf.Max(0f, forceToDeflect),
                maxDeflections = Mathf.Max(0, maxDeflections),
                speedMultiplier = Mathf.Max(0.1f, speedMultiplier),
            };
        }
    }

    /// <summary>A projectile about to strike something that might turn it aside.</summary>
    public readonly struct IncomingProjectile
    {
        public readonly Vector3 Position;

        /// <summary>The direction it travels in.</summary>
        public readonly Vector3 Direction;

        /// <summary>Who launched it, or who last turned it. May be null.</summary>
        public readonly GameObject Owner;

        public readonly CombatTeam OwnerTeam;

        public readonly ProjectileDeflection Deflection;

        public IncomingProjectile(Vector3 position, Vector3 direction, GameObject owner, CombatTeam ownerTeam,
            ProjectileDeflection deflection)
        {
            Position = position;
            Direction = direction;
            Owner = owner;
            OwnerTeam = ownerTeam;
            Deflection = deflection;
        }
    }

    /// <summary>A force that would turn a projectile: who applies it, where it sends it, and how strong it is.</summary>
    public readonly struct DeflectionPush
    {
        public readonly GameObject Deflector;
        public readonly CombatTeam Team;

        /// <summary>Where the force sends the projectile.</summary>
        public readonly Vector3 Direction;

        public readonly float Force;

        /// <summary>A deflected shot goes back at its sender if the sender lies within this angle of the push.</summary>
        public readonly float ReturnAssistDegrees;

        /// <summary>The deflector's discipline, as its selector slot, or -1.</summary>
        public readonly int Discipline;

        public DeflectionPush(GameObject deflector, CombatTeam team, Vector3 direction, float force, float returnAssistDegrees,
            int discipline)
        {
            Deflector = deflector;
            Team = team;
            Direction = direction;
            Force = force;
            ReturnAssistDegrees = returnAssistDegrees;
            Discipline = discipline;
        }
    }

    /// <summary>Something that can turn a projectile aside when it is about to be struck, such as a guard with force.</summary>
    public interface IProjectileDeflector
    {
        /// <summary>Offers a push against the incoming projectile. The projectile decides whether the push turns it.</summary>
        bool TryDeflect(in IncomingProjectile incoming, out DeflectionPush push);
    }

    /// <summary>
    /// FORCE + PROJECTILE = DEFLECTION. A designated projectile is turned aside by a force at least as
    /// strong as it demands, from anyone not on its owner's side, a set number of times at most. It
    /// keeps what it carries (its damage, its kind of magic, its effects); it changes direction and
    /// owner, so it now passes over the deflector and its allies and strikes the deflector's
    /// opponents, and what it does is credited to the deflector. Plain C#, tested outside Unity.
    /// </summary>
    public static class DeflectionRules
    {
        /// <summary>A deflected shot goes back at its sender if the sender lies within this angle of the push.</summary>
        public const float DefaultReturnAssistDegrees = 25f;

        /// <summary>Whether this force turns this projectile now.</summary>
        public static bool CanDeflect(in ProjectileDeflection designation, int deflectionsSoFar, float force,
            CombatTeam ownerTeam, CombatTeam deflectorTeam)
        {
            if (!designation.IsDesignated || deflectionsSoFar >= designation.maxDeflections || force <= 0f)
            {
                return false;
            }

            // A side does not turn its own shots: no accidental return to the sender's friends.
            if (CombatTeams.AreAllied(ownerTeam, deflectorTeam))
            {
                return false;
            }

            return designation.forceToDeflect <= 0f || CombinationRules.Overcomes(force, designation.forceToDeflect);
        }

        /// <summary>Whether a guard facing <paramref name="guardForward"/> catches a projectile travelling in <paramref name="travel"/>.</summary>
        public static bool GuardCatches(Vector3 guardForward, Vector3 travel, float guardArcDegrees)
        {
            return BlockRules.Covers(guardForward, travel, guardArcDegrees);
        }

        /// <summary>
        /// Where a deflected projectile goes: back at its sender if the sender lies within the assist
        /// angle of the push, otherwise along the push. The angle is measured along the ground, so a
        /// sender on a ledge above is found as well as one level with the push.
        /// </summary>
        public static Vector3 Redirect(Vector3 push, Vector3 from, bool hasSender, Vector3 senderPoint, float assistDegrees)
        {
            Vector3 direction = push.sqrMagnitude > 0.0001f ? push.normalized : Vector3.forward;
            if (hasSender && assistDegrees > 0f)
            {
                Vector3 toSender = senderPoint - from;
                Vector3 flatPush = new Vector3(direction.x, 0f, direction.z);
                Vector3 flatSender = new Vector3(toSender.x, 0f, toSender.z);
                if (toSender.sqrMagnitude > 0.01f && flatPush.sqrMagnitude > 0.0001f && flatSender.sqrMagnitude > 0.0001f &&
                    Vector3.Angle(flatPush, flatSender) <= assistDegrees)
                {
                    return toSender.normalized;
                }
            }

            return direction;
        }

        /// <summary>
        /// The launch of a projectile after it was turned: what it carries is kept, its direction, speed
        /// and owner change, and its range starts again. Effects it only implied by its kind of magic
        /// are written out, so they are kept exactly.
        /// </summary>
        public static ProjectileLaunch Deflected(in ProjectileLaunch launch, in DeflectionPush push, Vector3 direction,
            SpellElement carried)
        {
            EffectPayload effects = launch.Effects.IsEmpty ? EffectPayload.FromElement(carried, launch.Damage) : launch.Effects;
            float speed = launch.Speed * Mathf.Max(0.1f, launch.Deflection.speedMultiplier);
            return new ProjectileLaunch(push.Deflector, direction.normalized, speed, launch.MaxRange, launch.Radius, launch.Damage,
                launch.HitLayers, launch.ImpactEffect, launch.NoiseRadius, launch.Stagger, carried, effects, launch.VisualScale,
                push.Discipline, launch.Deflection);
        }
    }
}
