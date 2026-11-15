using System;
using System.Text;
using Threefold.CombatFeedback;
using Threefold.Magic;
using Threefold.Pooling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Builds the project-owned magical combat effects that are timed by the animation cues: limb trails,
    /// gathering force, contact bursts, lingering embers, charge buildups that concentrate, the Arcane sweep
    /// ribbon, the Flame Wave front, the Force Pulse ring, the Aegis barrier and deflection flash, the Arcane
    /// Arrow ribbon, and the arrivals of magic at objects. Every one uses the shared SoftGlow material on URP's
    /// Particles/Unlit shader, no lights, no collisions, no sub-emitters and a few dozen particles at most.
    ///
    /// An existing prefab is never rebuilt: change it in Unity, or delete it to have it generated again.
    /// Colours are the proposals of <see cref="DisciplineCatalog"/>, softened; nothing here is approved art.
    /// </summary>
    internal static class MagicVfxPrefabs
    {
        internal const string Folder = MagicVfxSetup.Folder + "/Body";

        internal static string PathOf(string name)
        {
            return $"{Folder}/{name}.prefab";
        }

        internal static PooledObject Load(string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(name));
            return prefab != null ? prefab.GetComponent<PooledObject>() : null;
        }

        /// <summary>Generates whichever of the effects are missing.</summary>
        internal static void EnsureAll(Material glow, StringBuilder report)
        {
            CombatSetup.EnsureFolder(Folder);
            Color arcane = Soft(DisciplineCatalog.Get(MagicDiscipline.Arcane).Color);
            Color flame = Soft(DisciplineCatalog.Get(MagicDiscipline.Flame).Color);
            Color aegis = Soft(DisciplineCatalog.Get(MagicDiscipline.Aegis).Color);
            Color ember = new Color(1f, 0.62f, 0.3f, 1f);

            // Arcane: precise and flowing.
            Ensure("ArcaneLimbTrail", report, root => LimbTrail(root, glow, arcane, 0.18f, 0.045f, sparkle: true));
            Ensure("ArcaneSweepRibbon", report, root => SweepRibbon(root, glow, arcane));
            Ensure("ArcaneContact", report, root => Contact(root, glow, arcane, 6, 18f, 2.5f, 0.05f));
            Ensure("ArcaneChargeGather", report, root => ChargeGather(root, glow, arcane, 26f, 0.03f, 0.07f));
            Ensure("ArcaneArrowRibbon", report, root => ProjectileRibbon(root, glow, arcane));

            // Flame: aggressive and directional.
            Ensure("FlameLimbTrail", report, root => FlameTrail(root, glow, flame));
            Ensure("FlameContact", report, root => Contact(root, glow, flame, 10, 32f, 4.5f, 0.09f));
            Ensure("FlameLinger", report, root => Embers(root, glow, ember));
            Ensure("FlameChargeHeat", report, root => ChargeHeat(root, glow, flame));
            Ensure("FlameWaveFront", report, root => WaveFront(root, glow, flame));

            // Aegis: compressed force and stability.
            Ensure("AegisLimbTrail", report, root => LimbTrail(root, glow, aegis, 0.1f, 0.07f, sparkle: false));
            Ensure("AegisGather", report, root => ChargeGather(root, glow, aegis, 22f, 0.04f, 0.08f));
            Ensure("AegisRipple", report, root => Ripple(root, glow, aegis, 1.3f, horizontal: false));
            Ensure("AegisHeavyRing", report, root => Ripple(root, glow, aegis, 3.2f, horizontal: true));
            Ensure("AegisChargeCompress", report, root => ChargeGather(root, glow, aegis, 34f, 0.04f, 0.09f));
            Ensure("AegisPulseRing", report, root => PulseRing(root, glow, aegis));
            Ensure("AegisBarrier", report, root => Barrier(root, glow, aegis));
            Ensure("AegisDeflectFlash", report, root => DeflectFlash(root, glow, aegis));

            // The world: magic arriving at objects.
            Ensure("WorldChargeInflow", report, root => Inflow(root, glow, arcane, 70f, 10, 0.3f, 0.32f));
            Ensure("WorldBraceArrival", report, root => Inflow(root, glow, aegis, 0f, 18, 0.05f, 0.4f));
            Ensure("WorldHeatArrival", report, root => Contact(root, glow, flame, 10, 40f, 2.2f, 0.11f, rise: 1.2f));
            Ensure("WorldForceArrival", report, root => Contact(root, glow, aegis, 10, 14f, 5f, 0.08f));
        }

        // ------------------------------------------------------------- builders

        private static void Ensure(string name, StringBuilder report, Action<GameObject> build)
        {
            string path = PathOf(name);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                return;
            }

            var root = new GameObject(name);
            root.AddComponent<PooledObject>();
            try
            {
                build(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                report.AppendLine($"Generated effect '{name}'.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>A thin ribbon drawn by the moving limb, with a few motes left along it.</summary>
        private static void LimbTrail(GameObject root, Material glow, Color color, float seconds, float width, bool sparkle)
        {
            Trail(root, glow, color, seconds, width, 0.8f);
            ParticleSystem motes = null;
            if (sparkle)
            {
                motes = Child(root, "Motes");
                Main(motes, 0.4f, true, 0.18f, 0.3f, 0.02f, 0.045f, color, 0f, 20, ParticleSystemSimulationSpace.World);
                Emission(motes, 0f, 9f);
                Shape(motes, ParticleSystemShapeType.Sphere, 0.03f);
                Fade(motes, true);
                Render(motes, glow, ParticleSystemRenderMode.Billboard);
            }

            root.AddComponent<PooledEffect>().Bind(motes, 5f, loop: true);
        }

        /// <summary>Fire on the fist: emitted by distance travelled, so it follows the acceleration of the punch.</summary>
        private static void FlameTrail(GameObject root, Material glow, Color color)
        {
            Trail(root, glow, Color.Lerp(color, new Color(1f, 0.85f, 0.5f), 0.3f), 0.12f, 0.08f, 0.7f);
            ParticleSystem fire = Child(root, "Flames");
            Main(fire, 0.4f, true, 0.12f, 0.24f, 0.08f, 0.18f, color, -0.15f, 40, ParticleSystemSimulationSpace.World);
            Emission(fire, 4f, 22f);
            Shape(fire, ParticleSystemShapeType.Sphere, 0.05f);
            Fade(fire, true);
            Render(fire, glow, ParticleSystemRenderMode.Billboard);
            root.AddComponent<PooledEffect>().Bind(fire, 5f, loop: true);
        }

        /// <summary>A ribbon swept round the body: particles emitted along an arc by script, joined by a ribbon trail.</summary>
        private static void SweepRibbon(GameObject root, Material glow, Color color)
        {
            ParticleSystem ribbon = root.AddComponent<ParticleSystem>();
            Main(ribbon, 1f, true, 0.2f, 0.24f, 0.05f, 0.08f, color, 0f, 48, ParticleSystemSimulationSpace.World);
            Emission(ribbon, 0f, 0f);
            Shape(ribbon, ParticleSystemShapeType.Sphere, 0.01f, enabled: false);
            Fade(ribbon, true);
            Render(ribbon, glow, ParticleSystemRenderMode.Billboard);

            ParticleSystem.TrailModule trails = ribbon.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.Ribbon;
            trails.ribbonCount = 1;
            trails.dieWithParticles = true;
            trails.inheritParticleColor = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1f));
            ribbon.GetComponent<ParticleSystemRenderer>().trailMaterial = glow;

            root.AddComponent<PooledEffect>().Bind(ribbon, 1f, loop: true);
            root.AddComponent<ArcEmitterEffect>().Bind(ribbon, ArcEmitterMode.Sweep, 140f, 0f, 0f, 0.25f, 0f);
        }

        /// <summary>A flash and a short cone of particles along the blow (+Z).</summary>
        private static void Contact(GameObject root, Material glow, Color color, int count, float coneDegrees, float speed,
            float size, float rise = 0f)
        {
            ParticleSystem core = root.AddComponent<ParticleSystem>();
            Main(core, 0.2f, false, 0.1f, 0.14f, size * 4f, size * 5f, Color.Lerp(color, Color.white, 0.35f), 0f, 2,
                ParticleSystemSimulationSpace.World);
            Burst(core, 1);
            Shape(core, ParticleSystemShapeType.Sphere, 0.01f, enabled: false);
            Fade(core, false);
            Grow(core, 0.6f, 1.2f);
            Render(core, glow, ParticleSystemRenderMode.Billboard);

            ParticleSystem cone = Child(root, "Burst");
            Main(cone, 0.3f, false, 0.12f, 0.26f, size * 0.6f, size, color, -rise * 0.2f, count + 2,
                ParticleSystemSimulationSpace.World);
            ParticleSystem.MainModule main = cone.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            Burst(cone, count);
            Shape(cone, ParticleSystemShapeType.Cone, 0.04f);
            ParticleSystem.ShapeModule shape = cone.shape;
            shape.angle = coneDegrees;
            Fade(cone, true);
            Render(cone, glow, ParticleSystemRenderMode.Stretch);
            cone.GetComponent<ParticleSystemRenderer>().velocityScale = 0.04f;

            root.AddComponent<PooledEffect>().Bind(null, 0.45f);
        }

        /// <summary>A soft disc that expands and fades: a pressure ripple. Flat on the ground when horizontal.</summary>
        private static void Ripple(GameObject root, Material glow, Color color, float finalSize, bool horizontal)
        {
            ParticleSystem ring = root.AddComponent<ParticleSystem>();
            Main(ring, 0.3f, false, 0.26f, 0.26f, finalSize, finalSize, new Color(color.r, color.g, color.b, 0.55f), 0f, 2,
                ParticleSystemSimulationSpace.World);
            Burst(ring, 1);
            Shape(ring, ParticleSystemShapeType.Sphere, 0.01f, enabled: false);
            Fade(ring, false);
            Grow(ring, 0.15f, 1f);
            Render(ring, glow, horizontal ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard);

            ParticleSystem dust = Child(root, "Dust");
            Main(dust, 0.3f, false, 0.2f, 0.32f, 0.08f, 0.16f, color, 0f, 16, ParticleSystemSimulationSpace.World);
            ParticleSystem.MainModule main = dust.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(horizontal ? 3f : 1.5f, horizontal ? 4.5f : 2.5f);
            Burst(dust, horizontal ? 14 : 8);
            Shape(dust, ParticleSystemShapeType.Circle, horizontal ? 0.4f : 0.1f);
            ParticleSystem.ShapeModule shape = dust.shape;
            shape.rotation = horizontal ? new Vector3(90f, 0f, 0f) : Vector3.zero; // the circle lies on the ground
            Fade(dust, true);
            Render(dust, glow, ParticleSystemRenderMode.Billboard);

            root.AddComponent<PooledEffect>().Bind(null, 0.5f);
        }

        /// <summary>Particles drawn inwards from a shell to the centre: the radius follows the charge.</summary>
        private static void ChargeGather(GameObject root, Material glow, Color color, float rate, float minSize, float maxSize)
        {
            ParticleSystem gather = root.AddComponent<ParticleSystem>();
            const float radius = 0.4f;
            const float life = 0.3f;
            Main(gather, 1f, true, life, life, minSize, maxSize, color, 0f, 24, ParticleSystemSimulationSpace.Local);
            ParticleSystem.MainModule main = gather.main;

            // Negative speed moves towards the centre: they arrive at the end of their life.
            main.startSpeed = -radius / life;
            Emission(gather, rate, 0f);
            Shape(gather, ParticleSystemShapeType.Sphere, radius);
            ParticleSystem.ShapeModule shape = gather.shape;
            shape.radiusThickness = 0f; // from the surface only
            FadeIn(gather);
            Render(gather, glow, ParticleSystemRenderMode.Billboard);

            ParticleSystem core = Child(root, "Core");
            Main(core, 1f, true, 0.4f, 0.4f, maxSize * 2.2f, maxSize * 2.6f, Color.Lerp(color, Color.white, 0.3f), 0f, 3,
                ParticleSystemSimulationSpace.Local);
            Emission(core, 6f, 0f);
            Shape(core, ParticleSystemShapeType.Sphere, 0.01f, enabled: false);
            Fade(core, false);
            Render(core, glow, ParticleSystemRenderMode.Billboard);

            root.AddComponent<PooledEffect>().Bind(null, 30f, loop: true);
        }

        /// <summary>Heat along a forearm: small rising flames close to the arm, denser as the charge grows.</summary>
        private static void ChargeHeat(GameObject root, Material glow, Color color)
        {
            ParticleSystem heat = root.AddComponent<ParticleSystem>();
            Main(heat, 1f, true, 0.2f, 0.34f, 0.05f, 0.12f, color, -0.25f, 24, ParticleSystemSimulationSpace.World);
            ParticleSystem.MainModule main = heat.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            Emission(heat, 28f, 0f);
            Shape(heat, ParticleSystemShapeType.Sphere, 0.12f);
            Fade(heat, true);
            Render(heat, glow, ParticleSystemRenderMode.Billboard);
            root.AddComponent<PooledEffect>().Bind(heat, 30f, loop: true);
        }

        /// <summary>Small embers that drift up and go out.</summary>
        private static void Embers(GameObject root, Material glow, Color color)
        {
            ParticleSystem embers = root.AddComponent<ParticleSystem>();
            Main(embers, 0.3f, false, 0.4f, 0.7f, 0.025f, 0.055f, color, -0.2f, 10, ParticleSystemSimulationSpace.World);
            ParticleSystem.MainModule main = embers.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
            Burst(embers, 8);
            Shape(embers, ParticleSystemShapeType.Sphere, 0.08f);
            Fade(embers, true);
            Render(embers, glow, ParticleSystemRenderMode.Billboard);
            root.AddComponent<PooledEffect>().Bind(embers, 0.8f);
        }

        /// <summary>The front of the Flame Wave: rising flames emitted along the advancing arc.</summary>
        private static void WaveFront(GameObject root, Material glow, Color color)
        {
            ParticleSystem front = root.AddComponent<ParticleSystem>();
            Main(front, 1f, true, 0.28f, 0.38f, 0.22f, 0.4f, color, -0.2f, 96, ParticleSystemSimulationSpace.World);
            Emission(front, 0f, 0f);
            Shape(front, ParticleSystemShapeType.Sphere, 0.01f, enabled: false);
            Fade(front, true);
            Render(front, glow, ParticleSystemRenderMode.Billboard);
            root.AddComponent<PooledEffect>().Bind(front, 1.2f, loop: true);
            root.AddComponent<ArcEmitterEffect>().Bind(front, ArcEmitterMode.Front, 26f, 0.5f, 0.25f, 1.4f, 1.1f);
        }

        /// <summary>The Force Pulse: a low, flat ring of pressure that expands over the area.</summary>
        private static void PulseRing(GameObject root, Material glow, Color color)
        {
            ParticleSystem ring = root.AddComponent<ParticleSystem>();
            Main(ring, 1f, true, 0.2f, 0.26f, 0.16f, 0.26f, new Color(color.r, color.g, color.b, 0.75f), 0f, 96,
                ParticleSystemSimulationSpace.World);
            Emission(ring, 0f, 0f);
            Shape(ring, ParticleSystemShapeType.Sphere, 0.01f, enabled: false);
            Fade(ring, true);
            Render(ring, glow, ParticleSystemRenderMode.HorizontalBillboard);
            root.AddComponent<PooledEffect>().Bind(ring, 1.2f, loop: true);
            root.AddComponent<ArcEmitterEffect>().Bind(ring, ArcEmitterMode.Front, 22f, 0.4f, 0.08f, 2.2f, 0.05f);
        }

        /// <summary>A restrained pane of soft motes held in front of the Mage while blocking.</summary>
        private static void Barrier(GameObject root, Material glow, Color color)
        {
            ParticleSystem pane = root.AddComponent<ParticleSystem>();
            Main(pane, 1f, true, 0.45f, 0.6f, 0.12f, 0.2f, new Color(color.r, color.g, color.b, 0.28f), 0f, 24,
                ParticleSystemSimulationSpace.Local);
            ParticleSystem.MainModule main = pane.main;
            main.startSpeed = 0f;
            Emission(pane, 36f, 0f);
            Shape(pane, ParticleSystemShapeType.Box, 1f);
            ParticleSystem.ShapeModule shape = pane.shape;
            shape.scale = new Vector3(1.1f, 1.2f, 0.04f);
            FadeInOut(pane);
            Render(pane, glow, ParticleSystemRenderMode.Billboard);
            root.AddComponent<PooledEffect>().Bind(pane, 30f, loop: true);
        }

        /// <summary>A clear flash where a projectile is turned: a bright core, sparks and a ring.</summary>
        private static void DeflectFlash(GameObject root, Material glow, Color color)
        {
            Contact(root, glow, Color.Lerp(color, Color.white, 0.5f), 10, 70f, 6f, 0.06f);
            ParticleSystem ring = Child(root, "Ring");
            Main(ring, 0.3f, false, 0.2f, 0.2f, 1.1f, 1.1f, new Color(color.r, color.g, color.b, 0.5f), 0f, 2,
                ParticleSystemSimulationSpace.World);
            Burst(ring, 1);
            Shape(ring, ParticleSystemShapeType.Sphere, 0.01f, enabled: false);
            Fade(ring, false);
            Grow(ring, 0.2f, 1f);
            Render(ring, glow, ParticleSystemRenderMode.Billboard);
        }

        /// <summary>A delicate ribbon that follows the Arcane Arrow, with a few motes shed along it.</summary>
        private static void ProjectileRibbon(GameObject root, Material glow, Color color)
        {
            Trail(root, glow, Color.Lerp(color, Color.white, 0.25f), 0.32f, 0.06f, 0.75f);
            ParticleSystem motes = Child(root, "Motes");
            Main(motes, 1f, true, 0.3f, 0.45f, 0.02f, 0.05f, color, 0f, 24, ParticleSystemSimulationSpace.World);
            Emission(motes, 0f, 3f);
            Shape(motes, ParticleSystemShapeType.Sphere, 0.05f);
            Fade(motes, true);
            Render(motes, glow, ParticleSystemRenderMode.Billboard);
            root.AddComponent<PooledEffect>().Bind(motes, 8f, loop: true);
        }

        /// <summary>Magic flowing into an object: emitted by script, timed to reach its centre.</summary>
        private static void Inflow(GameObject root, Material glow, Color color, float streamRate, int shell, float flowSeconds,
            float travelSeconds)
        {
            ParticleSystem flow = root.AddComponent<ParticleSystem>();
            Main(flow, 1f, true, travelSeconds, travelSeconds, 0.04f, 0.08f, color, 0f, 40,
                ParticleSystemSimulationSpace.World);
            Emission(flow, 0f, 0f);
            Shape(flow, ParticleSystemShapeType.Sphere, 0.01f, enabled: false);
            FadeInOut(flow);
            Render(flow, glow, ParticleSystemRenderMode.Billboard);
            root.AddComponent<PooledEffect>().Bind(flow, flowSeconds + travelSeconds + 0.5f, loop: true);
            root.AddComponent<InflowEffect>().Bind(flow, streamRate, shell, flowSeconds, travelSeconds);
        }

        // -------------------------------------------------------------- modules

        private static void Trail(GameObject root, Material glow, Color color, float seconds, float width, float alpha)
        {
            TrailRenderer trail = root.AddComponent<TrailRenderer>();
            trail.time = seconds;
            trail.minVertexDistance = 0.03f;
            trail.widthMultiplier = width;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.Lerp(color, Color.white, 0.3f), 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
            trail.sharedMaterial = glow;
            trail.numCapVertices = 2;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.lightProbeUsage = LightProbeUsage.Off;
            trail.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static ParticleSystem Child(GameObject root, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            return child.AddComponent<ParticleSystem>();
        }

        private static void Main(ParticleSystem system, float duration, bool loop, float minLife, float maxLife, float minSize,
            float maxSize, Color color, float gravity, int maxParticles, ParticleSystemSimulationSpace space)
        {
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.duration = duration;
            main.loop = loop;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.simulationSpace = space;
            main.maxParticles = maxParticles;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            ParticleSystem.CollisionModule collision = system.collision;
            collision.enabled = false;
            ParticleSystem.LightsModule lights = system.lights;
            lights.enabled = false;
        }

        private static void Emission(ParticleSystem system, float perSecond, float perMetre)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = perSecond;
            emission.rateOverDistance = perMetre;
            emission.SetBursts(new ParticleSystem.Burst[0]);
        }

        private static void Burst(ParticleSystem system, int count)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        private static void Shape(ParticleSystem system, ParticleSystemShapeType type, float radius, bool enabled = true)
        {
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = enabled;
            shape.shapeType = type;
            shape.radius = radius;
            shape.arc = 360f;
        }

        private static void Fade(ParticleSystem system, bool shrink)
        {
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(fade);

            if (shrink)
            {
                ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            }
        }

        private static void FadeIn(ParticleSystem system)
        {
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0.4f, 1f) });
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(fade);
        }

        private static void FadeInOut(ParticleSystem system)
        {
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(fade);
        }

        private static void Grow(ParticleSystem system, float from, float to)
        {
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from, 1f, to));
        }

        private static void Render(ParticleSystem system, Material material, ParticleSystemRenderMode mode)
        {
            ParticleSystemRenderer particleRenderer = system.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = mode;
            particleRenderer.sharedMaterial = material;
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
            particleRenderer.lightProbeUsage = LightProbeUsage.Off;
            particleRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // Keeps a close effect from covering the screen on a phone.
            particleRenderer.maxParticleSize = 0.5f;
        }

        /// <summary>Soft Illustrated Realism: accents readable without HDR neon multipliers.</summary>
        internal static Color Soft(Color color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            Color soft = Color.HSVToRGB(h, Mathf.Clamp01(s * 0.85f), Mathf.Clamp(v, 0.55f, 0.92f));
            soft.a = 1f;
            return soft;
        }
    }
}
