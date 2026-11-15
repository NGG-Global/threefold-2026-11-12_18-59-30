using System.Collections.Generic;

namespace Threefold.CombatFeedback
{
    /// <summary>What an effect is used for, which sets how much it may cost.</summary>
    public enum MagicVfxRole
    {
        /// <summary>A short one-off: a contact, an impact, a release, a flash.</summary>
        Burst = 0,

        /// <summary>Attached to the body while an action lasts: a trail, a gathering, a charge.</summary>
        Attached = 1,

        /// <summary>A wave or pulse that travels over an area.</summary>
        Area = 2,

        /// <summary>Shown on a magical object as the world reacts.</summary>
        World = 3,

        /// <summary>The guard's barrier, held while blocking.</summary>
        Barrier = 4,
    }

    /// <summary>What an effect prefab contains, as counted by the Editor validation.</summary>
    public struct MagicVfxSummary
    {
        public string Name;
        public MagicVfxRole Role;
        public int ParticleSystems;

        /// <summary>The sum of the particle systems' particle limits.</summary>
        public int MaxParticles;

        public int TrailRenderers;

        /// <summary>Light components plus particle systems with the Lights module on.</summary>
        public int Lights;

        public int Collisions;
        public int AudioSources;

        /// <summary>Renderers with no material, or a material without a shader that loads (pink in the build).</summary>
        public int MissingMaterials;

        /// <summary>Materials that use a Built-in Render Pipeline shader, which URP does not draw.</summary>
        public int BuiltInShaders;

        public int SubEmitters;

        /// <summary>Longest particle system duration plus lifetime, in seconds. Ignored for looping roles.</summary>
        public float Seconds;

        public bool Pooled;
    }

    /// <summary>
    /// The budget every magical combat effect is held to on Android: no lights, no particle collisions, no audio
    /// on visual prefabs, no missing or Built-in materials, few systems and particles, short bursts. The counts are
    /// proposals, set from the existing presentation contract (one shape and 6 to 18 soft particles per burst) with
    /// room for a trail; they are not measured limits. Plain C# so the rules are tested outside Unity; the Editor
    /// validation command counts the prefabs and asks these rules.
    /// </summary>
    public static class MagicVfxBudget
    {
        public static int MaxSystems(MagicVfxRole role)
        {
            switch (role)
            {
                case MagicVfxRole.Burst:
                    return 3;
                case MagicVfxRole.Area:
                case MagicVfxRole.Attached:
                    return 3;
                default:
                    return 2;
            }
        }

        public static int MaxParticles(MagicVfxRole role)
        {
            switch (role)
            {
                case MagicVfxRole.Burst:
                    return 48;
                case MagicVfxRole.Area:
                    return 96;
                case MagicVfxRole.Attached:
                    return 64;
                case MagicVfxRole.Barrier:
                    return 32;
                default:
                    return 40;
            }
        }

        public static float MaxSeconds(MagicVfxRole role)
        {
            return role == MagicVfxRole.Area ? 1.4f : 1.2f;
        }

        /// <summary>Adds a sentence for each rule the effect breaks. Returns how many it breaks.</summary>
        public static int Check(in MagicVfxSummary effect, List<string> problems)
        {
            int before = problems.Count;
            string name = string.IsNullOrEmpty(effect.Name) ? "Effect" : effect.Name;

            if (!effect.Pooled)
            {
                problems.Add($"{name}: has no PooledObject, so it would be created and destroyed instead of reused.");
            }

            if (effect.Lights > 0)
            {
                problems.Add($"{name}: has {effect.Lights} real-time light(s). Combat effects use none.");
            }

            if (effect.Collisions > 0)
            {
                problems.Add($"{name}: {effect.Collisions} particle system(s) collide with the world. Hits are resolved by the rules, not by particles.");
            }

            if (effect.AudioSources > 0)
            {
                problems.Add($"{name}: has {effect.AudioSources} AudioSource(s). Sound is not part of a visual prefab.");
            }

            if (effect.MissingMaterials > 0)
            {
                problems.Add($"{name}: {effect.MissingMaterials} renderer(s) have a missing material and would draw pink.");
            }

            if (effect.BuiltInShaders > 0)
            {
                problems.Add($"{name}: {effect.BuiltInShaders} material(s) use a Built-in pipeline shader, which URP does not draw.");
            }

            if (effect.SubEmitters > 0)
            {
                problems.Add($"{name}: uses sub-emitters, which multiply particle counts unpredictably.");
            }

            int maxSystems = MaxSystems(effect.Role);
            if (effect.ParticleSystems > maxSystems)
            {
                problems.Add($"{name}: {effect.ParticleSystems} particle systems; at most {maxSystems} for its role ({effect.Role}).");
            }

            int maxParticles = MaxParticles(effect.Role);
            if (effect.MaxParticles > maxParticles)
            {
                problems.Add($"{name}: up to {effect.MaxParticles} particles; at most {maxParticles} for its role ({effect.Role}).");
            }

            if (effect.TrailRenderers > 2)
            {
                problems.Add($"{name}: {effect.TrailRenderers} trail renderers; at most 2.");
            }

            bool held = effect.Role == MagicVfxRole.Attached || effect.Role == MagicVfxRole.Barrier;
            if (!held && effect.Seconds > MaxSeconds(effect.Role))
            {
                problems.Add($"{name}: lasts {effect.Seconds:0.##} s; at most {MaxSeconds(effect.Role):0.##} s for its role ({effect.Role}).");
            }

            return problems.Count - before;
        }
    }
}
