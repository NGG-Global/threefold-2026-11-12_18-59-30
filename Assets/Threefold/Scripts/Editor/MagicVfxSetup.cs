using System.Text;
using Threefold.Cameras;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.CombatFeedback;
using Threefold.Magic;
using Threefold.Pooling;
using Threefold.VisualStyle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Builds Threefold-owned magical combat VFX from Soft Illustrated Realism–tuned procedural
    /// bursts, and optionally adapts installed Asset Store packs (Free Slash, Hovl, Vefects) into
    /// copies under Generated/VFX. Third-party source prefabs are never edited. Safe to run
    /// repeatedly: existing adapted assets are kept; only empty presentation references are filled.
    /// </summary>
    public static class MagicVfxSetup
    {
        private const string MenuPath = "Threefold/Setup/Magic VFX";
        private const string DialogTitle = "Magic VFX Setup";
        internal const string Folder = "Assets/Threefold/Generated/VFX";

        private const string FreeSlashWater = "Assets/Free Slash VFX/Prefabs/Slash Water VFX.prefab";
        private const string FreeSlashFire = "Assets/Free Slash VFX/Prefabs/Slash Fire VFX.prefab";
        private const string FreeSlashImpact = "Assets/Free Slash VFX/Prefabs/Impact.prefab";
        private const string FreeSlashProjectileWater = "Assets/Free Slash VFX/Prefabs/Projectiles/Slash Projectile VFX Water.prefab";
        private const string HovlChargeBlue = "Assets/Hovl Studio/Magic effects pack/Prefabs/Slash effects/Charge slash blue.prefab";
        private const string HovlStonesHit = "Assets/Hovl Studio/Magic effects pack/Prefabs/Hits and explosions/Stones hit.prefab";
        private const string HovlGroundAoe = "Assets/Hovl Studio/Magic effects pack/Prefabs/AoE effects/Ground AOE explosion.prefab";
        private const string HovlShieldBlue = "Assets/Hovl Studio/Magic effects pack/Prefabs/Magic shields/Magic shield blue.prefab";
        private const string HovlSparksYellow = "Assets/Hovl Studio/Magic effects pack/Prefabs/Sparks/Sparks explode yellow.prefab";
        private const string VefectsFireSimple = "Assets/Vefects/Free Fire VFX URP/Particles/VFX_Fire_01_Small_Simple.prefab";

        [MenuItem(MenuPath, false, 4)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(DialogTitle, "Exit Play mode before running the setup.", "OK");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            Undo.SetCurrentGroupName(DialogTitle);
            int undoGroup = Undo.GetCurrentGroup();
            var report = new StringBuilder();

            EnsureAssets(report);
            ThirdPersonMotor motor = FindInScene<ThirdPersonMotor>(scene);
            if (motor != null)
            {
                EnsureSceneWiring(scene, motor.gameObject, report);
            }
            else
            {
                report.AppendLine("No Mage in the open scene: assets were generated, scene wiring skipped.");
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);

            string summary = report.ToString().TrimEnd();
            Debug.Log($"[Threefold] {DialogTitle} finished.\n{summary}");
            EditorUtility.DisplayDialog(DialogTitle, summary + "\n\nSave the scene with Ctrl+S.", "OK");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>Generates missing VFX assets and fills empty discipline presentation refs.</summary>
        internal static void Ensure(Scene scene, GameObject mage, StringBuilder report)
        {
            EnsureAssets(report);
            if (mage != null)
            {
                EnsureSceneWiring(scene, mage, report);
            }
        }

        internal static void EnsureAssets(StringBuilder report)
        {
            CombatSetup.EnsureFolder(Folder);
            CombatSetup.SharedAssets shared = CombatSetup.EnsureSharedAssets(report);
            Material glow = shared.Glow;

            for (int slot = 0; slot < DisciplineCatalog.Count; slot++)
            {
                MagicDiscipline discipline = (MagicDiscipline)slot;
                DisciplineInfo info = DisciplineCatalog.Get(discipline);
                EnsureDisciplineEffects(discipline, info, glow, report);
            }

            EnsureSharedEffects(glow, report);
            TryAdaptPacks(glow, report);
            WireDisciplinePresentations(report);
            AssetDatabase.SaveAssets();
        }

        // ----------------------------------------------------------- procedural

        private static void EnsureDisciplineEffects(MagicDiscipline discipline, DisciplineInfo info, Material glow,
            StringBuilder report)
        {
            string key = info.Name;
            EnsureSlashPrefab($"{Folder}/{key}MeleeSlash.prefab", key + "MeleeSlash", discipline, info.Color, glow, report);
            EnsureImpactPrefab($"{Folder}/{key}MeleeImpact.prefab", key + "MeleeImpact", discipline, info.Color, glow, report);
            EnsureChargePrefab($"{Folder}/{key}ChargeLoop.prefab", key + "ChargeLoop", info.Color, glow, report);
            EnsureReleasePrefab($"{Folder}/{key}ReleaseBurst.prefab", key + "ReleaseBurst", discipline, info.Color, glow, report);
        }

        private static void EnsureSharedEffects(Material glow, StringBuilder report)
        {
            EnsureImpactPrefab($"{Folder}/DeflectSpark.prefab", "DeflectSpark", MagicDiscipline.Aegis,
                new Color(1f, 0.97f, 0.88f, 1f), glow, report, particleCount: 10, lifetime: 0.3f, radius: 0.12f);
            EnsureEmberPrefab($"{Folder}/BurnEmbers.prefab", "BurnEmbers", new Color(1f, 0.55f, 0.22f, 1f), glow, report);
        }

        private static PooledObject EnsureSlashPrefab(string path, string name, MagicDiscipline discipline, Color color,
            Material glow, StringBuilder report)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing.GetComponent<PooledObject>();
            }

            var root = new GameObject(name);
            root.AddComponent<PooledObject>();

            ParticleSystem burst = root.AddComponent<ParticleSystem>();
            ConfigureSlashBurst(burst, glow, color, discipline);
            float lifetime = discipline == MagicDiscipline.Aegis ? 0.42f : 0.34f;
            root.AddComponent<PooledEffect>().Bind(burst, lifetime);

            if (discipline == MagicDiscipline.Flame)
            {
                ParticleSystem sparks = CreateChildSystem(root.transform, "Sparks");
                ConfigureSparks(sparks, glow, Color.Lerp(color, new Color(1f, 0.85f, 0.35f), 0.35f), 8);
            }
            else if (discipline == MagicDiscipline.Aegis)
            {
                ParticleSystem ring = CreateChildSystem(root.transform, "ForceRing");
                ConfigureForceRing(ring, glow, color);
            }
            else
            {
                // Arcane: a second thin cone for a precise double-beat without a bone-driven ribbon.
                ParticleSystem edge = CreateChildSystem(root.transform, "Edge");
                ConfigureSlashBurst(edge, glow, Color.Lerp(color, Color.white, 0.4f), MagicDiscipline.Arcane);
                ParticleSystem.MainModule edgeMain = edge.main;
                edgeMain.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
                ParticleSystem.EmissionModule edgeEmission = edge.emission;
                edgeEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            report.AppendLine($"Generated melee slash '{name}'.");
            return prefab.GetComponent<PooledObject>();
        }

        private static PooledObject EnsureImpactPrefab(string path, string name, MagicDiscipline discipline, Color color,
            Material glow, StringBuilder report, int particleCount = 12, float lifetime = 0.45f, float radius = 0.1f)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing.GetComponent<PooledObject>();
            }

            var root = new GameObject(name);
            root.AddComponent<PooledObject>();
            ParticleSystem burst = root.AddComponent<ParticleSystem>();
            ConfigureImpactBurst(burst, glow, color, discipline, particleCount, radius);
            root.AddComponent<PooledEffect>().Bind(burst, lifetime);

            if (discipline == MagicDiscipline.Aegis)
            {
                ParticleSystem shock = CreateChildSystem(root.transform, "Shockwave");
                ConfigureShockwave(shock, glow, Color.Lerp(color, Color.white, 0.35f));
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            report.AppendLine($"Generated impact '{name}'.");
            return prefab.GetComponent<PooledObject>();
        }

        private static PooledObject EnsureChargePrefab(string path, string name, Color color, Material glow,
            StringBuilder report)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing.GetComponent<PooledObject>();
            }

            var root = new GameObject(name);
            root.AddComponent<PooledObject>();
            ParticleSystem loop = root.AddComponent<ParticleSystem>();
            ConfigureChargeLoop(loop, glow, color);
            root.AddComponent<PooledEffect>().Bind(loop, 30f, loop: true);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            report.AppendLine($"Generated charge loop '{name}'.");
            return prefab.GetComponent<PooledObject>();
        }

        private static PooledObject EnsureReleasePrefab(string path, string name, MagicDiscipline discipline, Color color,
            Material glow, StringBuilder report)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing.GetComponent<PooledObject>();
            }

            var root = new GameObject(name);
            root.AddComponent<PooledObject>();
            ParticleSystem burst = root.AddComponent<ParticleSystem>();
            ConfigureImpactBurst(burst, glow, color, discipline, particleCount: 14, radius: 0.14f);
            root.AddComponent<PooledEffect>().Bind(burst, 0.4f);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            report.AppendLine($"Generated release burst '{name}'.");
            return prefab.GetComponent<PooledObject>();
        }

        private static PooledObject EnsureEmberPrefab(string path, string name, Color color, Material glow,
            StringBuilder report)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing.GetComponent<PooledObject>();
            }

            var root = new GameObject(name);
            root.AddComponent<PooledObject>();
            ParticleSystem embers = root.AddComponent<ParticleSystem>();
            ConfigureEmbers(embers, glow, color);
            root.AddComponent<PooledEffect>().Bind(embers, 0.65f);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            report.AppendLine($"Generated burn embers '{name}'.");
            return prefab.GetComponent<PooledObject>();
        }

        // -------------------------------------------------------------- shapes

        private static void ConfigureSlashBurst(ParticleSystem particles, Material material, Color color,
            MagicDiscipline discipline)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            int count = discipline == MagicDiscipline.Arcane ? 10 : discipline == MagicDiscipline.Flame ? 14 : 12;
            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.35f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.4f);
            main.startSpeed = discipline == MagicDiscipline.Aegis
                ? new ParticleSystem.MinMaxCurve(1.2f, 3.2f)
                : new ParticleSystem.MinMaxCurve(2.5f, 7f);
            main.startSize = discipline == MagicDiscipline.Arcane
                ? new ParticleSystem.MinMaxCurve(0.06f, 0.16f)
                : new ParticleSystem.MinMaxCurve(0.1f, 0.28f);
            main.startColor = SoftColor(color);
            main.gravityModifier = discipline == MagicDiscipline.Flame ? 0.15f : 0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count * 2;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            if (discipline == MagicDiscipline.Aegis)
            {
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.35f;
                shape.radiusThickness = 0.2f;
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = discipline == MagicDiscipline.Arcane ? 12f : 28f;
                shape.radius = 0.05f;
                shape.length = 0.2f;
            }

            ApplyFadeAndShrink(particles);
            ApplyRenderer(particles, material);
        }

        private static void ConfigureImpactBurst(ParticleSystem particles, Material material, Color color,
            MagicDiscipline discipline, int particleCount, float radius)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.45f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
            main.startSpeed = discipline == MagicDiscipline.Aegis
                ? new ParticleSystem.MinMaxCurve(1.2f, 3.5f)
                : new ParticleSystem.MinMaxCurve(1.8f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            main.startColor = SoftColor(color);
            main.gravityModifier = discipline == MagicDiscipline.Flame ? 0.35f : 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = particleCount * 2;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)particleCount) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;

            ApplyFadeAndShrink(particles);
            ApplyRenderer(particles, material);
        }

        private static void ConfigureChargeLoop(ParticleSystem particles, Material material, Color color)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startColor = SoftColor(color);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 18;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 14f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            ApplyFadeAndShrink(particles);
            ApplyRenderer(particles, material);
        }

        private static void ConfigureSparks(ParticleSystem particles, Material material, Color color, int count)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.3f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = SoftColor(color);
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count * 2;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.02f, (short)count) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.04f;

            ApplyFadeAndShrink(particles);
            ApplyRenderer(particles, material);
        }

        private static void ConfigureForceRing(ParticleSystem particles, Material material, Color color)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.4f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startColor = SoftColor(color);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 20;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.25f;
            shape.radiusThickness = 0f;

            ApplyFadeAndShrink(particles);
            ApplyRenderer(particles, material);
        }

        private static void ConfigureShockwave(ParticleSystem particles, Material material, Color color)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.35f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.28f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.55f);
            main.startColor = SoftColor(color);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.05f;

            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1.8f));

            ApplyFadeAndShrink(particles, shrink: false);
            ApplyRenderer(particles, material);
        }

        private static void ConfigureEmbers(ParticleSystem particles, Material material, Color color)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = SoftColor(color);
            main.gravityModifier = -0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 16;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.15f;

            ApplyFadeAndShrink(particles);
            ApplyRenderer(particles, material);
        }

        private static ParticleSystem CreateChildSystem(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.AddComponent<ParticleSystem>();
        }

        private static void ApplyFadeAndShrink(ParticleSystem particles, bool shrink = true)
        {
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

            if (!shrink)
            {
                return;
            }

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));
        }

        private static void ApplyRenderer(ParticleSystem particles, Material material)
        {
            ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = material;
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
            particleRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            particleRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>Soft Illustrated Realism: keep accents readable without HDR neon multipliers.</summary>
        private static Color SoftColor(Color color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            s = Mathf.Clamp01(s * 0.85f);
            v = Mathf.Clamp(v, 0.55f, 0.92f);
            Color soft = Color.HSVToRGB(h, s, v);
            soft.a = color.a;
            return soft;
        }

        // ---------------------------------------------------------- pack adapt

        private static void TryAdaptPacks(Material glow, StringBuilder report)
        {
            AdaptOne(FreeSlashWater, $"{Folder}/Adapted/ArcaneSlash_FreeSlash.prefab", glow,
                SoftColor(DisciplineCatalog.Get(MagicDiscipline.Arcane).Color), maxSystems: 3, report);
            AdaptOne(FreeSlashFire, $"{Folder}/Adapted/FlameSlash_FreeSlash.prefab", glow,
                SoftColor(DisciplineCatalog.Get(MagicDiscipline.Flame).Color), maxSystems: 3, report);
            AdaptOne(FreeSlashImpact, $"{Folder}/Adapted/SharedImpact_FreeSlash.prefab", glow,
                SoftColor(DisciplineCatalog.Get(MagicDiscipline.Arcane).Color), maxSystems: 3, report);
            AdaptOne(FreeSlashProjectileWater, $"{Folder}/Adapted/ArcaneArrowBody_FreeSlash.prefab", glow,
                SoftColor(DisciplineCatalog.Get(MagicDiscipline.Arcane).Color), maxSystems: 3, report, stripCollision: true);

            AdaptOne(HovlChargeBlue, $"{Folder}/Adapted/AegisSlash_Hovl.prefab", glow,
                SoftColor(DisciplineCatalog.Get(MagicDiscipline.Aegis).Color), maxSystems: 4, report, remapBuiltIn: true);
            AdaptOne(HovlStonesHit, $"{Folder}/Adapted/AegisImpact_Hovl.prefab", glow,
                SoftColor(DisciplineCatalog.Get(MagicDiscipline.Aegis).Color), maxSystems: 3, report, remapBuiltIn: true);
            AdaptOne(HovlGroundAoe, $"{Folder}/Adapted/ForcePulse_Hovl.prefab", glow,
                SoftColor(DisciplineCatalog.Get(MagicDiscipline.Aegis).Color), maxSystems: 3, report, remapBuiltIn: true);
            AdaptOne(HovlShieldBlue, $"{Folder}/Adapted/AegisShield_Hovl.prefab", glow,
                SoftColor(DisciplineCatalog.Get(MagicDiscipline.Aegis).Color), maxSystems: 2, report, remapBuiltIn: true);
            AdaptOne(HovlSparksYellow, $"{Folder}/Adapted/FlameSparks_Hovl.prefab", glow,
                SoftColor(DisciplineCatalog.Get(MagicDiscipline.Flame).Color), maxSystems: 1, report, remapBuiltIn: true);

            AdaptOne(VefectsFireSimple, $"{Folder}/Adapted/FlameBurn_Vefects.prefab", glow,
                SoftColor(DisciplineCatalog.Get(MagicDiscipline.Flame).Color), maxSystems: 3, report, stripAudio: true,
                stripDistortion: true);
        }

        private static void AdaptOne(string sourcePath, string destPath, Material fallbackGlow, Color tint,
            int maxSystems, StringBuilder report, bool remapBuiltIn = false, bool stripCollision = true,
            bool stripAudio = true, bool stripDistortion = false)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(destPath) != null)
            {
                return;
            }

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                return;
            }

            CombatSetup.EnsureFolder(Folder + "/Adapted");
            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                instance = Object.Instantiate(source);
            }

            instance.name = System.IO.Path.GetFileNameWithoutExtension(destPath);
            StripHeavyModules(instance, stripCollision, stripAudio, stripDistortion);
            CapParticleSystems(instance, maxSystems, tint, remapBuiltIn, fallbackGlow);

            if (instance.GetComponent<PooledObject>() == null)
            {
                instance.AddComponent<PooledObject>();
            }

            if (instance.GetComponent<PooledEffect>() == null)
            {
                ParticleSystem primary = instance.GetComponentInChildren<ParticleSystem>();
                instance.AddComponent<PooledEffect>().Bind(primary, 0.5f);
            }

            PrefabUtility.SaveAsPrefabAsset(instance, destPath);
            Object.DestroyImmediate(instance);
            report.AppendLine($"Adapted '{sourcePath}' → '{destPath}'.");
        }

        private static void StripHeavyModules(GameObject root, bool stripCollision, bool stripAudio, bool stripDistortion)
        {
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                Object.DestroyImmediate(light);
            }

            if (stripAudio)
            {
                foreach (AudioSource audio in root.GetComponentsInChildren<AudioSource>(true))
                {
                    Object.DestroyImmediate(audio);
                }
            }

            foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.LightsModule lights = system.lights;
                lights.enabled = false;

                if (stripCollision)
                {
                    ParticleSystem.CollisionModule collision = system.collision;
                    collision.enabled = false;
                }

                ParticleSystem.MainModule main = system.main;
                if (main.maxParticles > 48)
                {
                    main.maxParticles = 48;
                }
            }

            if (stripDistortion)
            {
                Transform[] children = root.GetComponentsInChildren<Transform>(true);
                for (int i = children.Length - 1; i >= 0; i--)
                {
                    Transform child = children[i];
                    if (child == null || child == root.transform)
                    {
                        continue;
                    }

                    string n = child.name.ToLowerInvariant();
                    if (n.Contains("distort") || n.Contains("haze") || n.Contains("heat"))
                    {
                        Object.DestroyImmediate(child.gameObject);
                    }
                }
            }
        }

        private static void CapParticleSystems(GameObject root, int maxSystems, Color tint, bool remapBuiltIn,
            Material fallbackGlow)
        {
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                if (i >= maxSystems)
                {
                    systems[i].gameObject.SetActive(false);
                    continue;
                }

                ParticleSystemRenderer renderer = systems[i].GetComponent<ParticleSystemRenderer>();
                if (renderer == null)
                {
                    continue;
                }

                if (remapBuiltIn || renderer.sharedMaterial == null)
                {
                    renderer.sharedMaterial = fallbackGlow;
                }

                ParticleSystem.MainModule main = systems[i].main;
                main.startColor = new ParticleSystem.MinMaxGradient(tint);
            }
        }

        // --------------------------------------------------------------- wire

        private static void WireDisciplinePresentations(StringBuilder report)
        {
            for (int slot = 0; slot < DisciplineCatalog.Count; slot++)
            {
                MagicDiscipline discipline = (MagicDiscipline)slot;
                string key = DisciplineCatalog.Get(discipline).Name;
                string path = $"{DisciplineSetup.Folder}/{key}.asset";
                DisciplineDefinition definition = AssetDatabase.LoadAssetAtPath<DisciplineDefinition>(path);
                if (definition == null)
                {
                    continue;
                }

                DisciplinePresentation look = definition.Presentation;
                bool changed = false;

                changed |= Assign(ref look.meleeSwingEffect, PreferAdapted(discipline, "slash",
                    $"{Folder}/{key}MeleeSlash.prefab"), replacePlaceholder: true);
                changed |= Assign(ref look.meleeImpactEffect, PreferAdapted(discipline, "impact",
                    $"{Folder}/{key}MeleeImpact.prefab"), replacePlaceholder: true);
                changed |= Assign(ref look.chargeLoopEffect, AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{Folder}/{key}ChargeLoop.prefab")?.GetComponent<PooledObject>(), replacePlaceholder: true);
                changed |= Assign(ref look.releaseEffect, AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{Folder}/{key}ReleaseBurst.prefab")?.GetComponent<PooledObject>(), replacePlaceholder: true);
                changed |= Assign(ref look.deflectEffect, AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{Folder}/DeflectSpark.prefab")?.GetComponent<PooledObject>(), replacePlaceholder: true);

                if (look.effectHeight < 0.5f)
                {
                    look.effectHeight = 1.15f;
                    changed = true;
                }

                if (changed)
                {
                    EditorUtility.SetDirty(definition);
                    report.AppendLine($"Wired magical VFX into discipline '{key}'.");
                }

                WireChargedExtras(definition, discipline, report);
            }
        }

        private static void WireChargedExtras(DisciplineDefinition definition, MagicDiscipline discipline,
            StringBuilder report)
        {
            if (definition.ChargedAbility is ChargedProjectileDefinition shot)
            {
                PooledObject adaptedBody = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{Folder}/Adapted/ArcaneArrowBody_FreeSlash.prefab")?.GetComponent<PooledObject>();
                // Keep the generated ArcaneArrow projectile for gameplay; adapted pack body is optional art.
                // Impact is already on the ability; fill only if empty.
                if (shot.ImpactEffectPrefab == null)
                {
                    PooledObject impact = PreferAdapted(discipline, "impact", $"{Folder}/ArcaneMeleeImpact.prefab");
                    if (impact != null)
                    {
                        shot.ConfigureProjectile(shot.ProjectilePrefab, impact, shot.Projectile);
                        EditorUtility.SetDirty(shot);
                        report.AppendLine("Filled Arcane Arrow impact effect.");
                    }
                }

                _ = adaptedBody;
            }
            else if (definition.ChargedAbility is ChargedAreaDefinition area && area.ImpactEffectPrefab == null)
            {
                PooledObject impact = PreferAdapted(discipline, "impact",
                    $"{Folder}/{DisciplineCatalog.Get(discipline).Name}MeleeImpact.prefab");
                if (discipline == MagicDiscipline.Aegis)
                {
                    PooledObject pulse = AssetDatabase.LoadAssetAtPath<GameObject>(
                        $"{Folder}/Adapted/ForcePulse_Hovl.prefab")?.GetComponent<PooledObject>();
                    if (pulse != null)
                    {
                        impact = pulse;
                    }
                }

                if (impact != null)
                {
                    area.ConfigureArea(area.Area, impact);
                    EditorUtility.SetDirty(area);
                    report.AppendLine($"Filled charged area impact for '{definition.DisplayName}'.");
                }
            }
        }

        private static PooledObject PreferAdapted(MagicDiscipline discipline, string kind, string proceduralPath)
        {
            string adapted = null;
            if (kind == "slash")
            {
                switch (discipline)
                {
                    case MagicDiscipline.Arcane:
                        adapted = $"{Folder}/Adapted/ArcaneSlash_FreeSlash.prefab";
                        break;
                    case MagicDiscipline.Flame:
                        adapted = $"{Folder}/Adapted/FlameSlash_FreeSlash.prefab";
                        break;
                    case MagicDiscipline.Aegis:
                        adapted = $"{Folder}/Adapted/AegisSlash_Hovl.prefab";
                        break;
                }
            }
            else if (kind == "impact")
            {
                switch (discipline)
                {
                    case MagicDiscipline.Aegis:
                        adapted = $"{Folder}/Adapted/AegisImpact_Hovl.prefab";
                        break;
                    default:
                        adapted = $"{Folder}/Adapted/SharedImpact_FreeSlash.prefab";
                        break;
                }
            }

            if (!string.IsNullOrEmpty(adapted))
            {
                PooledObject fromPack = AssetDatabase.LoadAssetAtPath<GameObject>(adapted)?.GetComponent<PooledObject>();
                if (fromPack != null)
                {
                    return fromPack;
                }
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(proceduralPath)?.GetComponent<PooledObject>();
        }

        private static bool Assign(ref PooledObject field, PooledObject value, bool replacePlaceholder)
        {
            if (value == null)
            {
                return false;
            }

            if (field == null)
            {
                field = value;
                return true;
            }

            if (replacePlaceholder && IsSoftGlowPlaceholder(field) && field != value)
            {
                field = value;
                return true;
            }

            return false;
        }

        /// <summary>True for the temporary SoftGlow bursts generated under Generated/Disciplines.</summary>
        private static bool IsSoftGlowPlaceholder(PooledObject effect)
        {
            string path = AssetDatabase.GetAssetPath(effect);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path.StartsWith(DisciplineSetup.Folder) &&
                   (path.Contains("StrikeImpact") || path.Contains("ChargedImpact") || path.Contains("Release") ||
                    path.Contains("Embers") || path.Contains("GuardSpark") || path.Contains("PushDust"));
        }

        // --------------------------------------------------------------- scene

        private static void EnsureSceneWiring(Scene scene, GameObject mage, StringBuilder report)
        {
            Animator animator = mage.GetComponentInChildren<Animator>(true);
            if (!mage.TryGetComponent(out DisciplineVfxSockets sockets))
            {
                sockets = Undo.AddComponent<DisciplineVfxSockets>(mage);
                sockets.Bind(animator);
                MarkChanged(sockets);
                report.AppendLine("Added VFX hand sockets on the Mage.");
            }
            else if (animator != null)
            {
                Undo.RecordObject(sockets, DialogTitle);
                sockets.Bind(animator);
                MarkChanged(sockets);
            }

            GameObject visuals = FindByName(scene, "CombatVisuals");
            if (visuals == null)
            {
                return;
            }

            if (!visuals.TryGetComponent(out DisciplineFeedback feedback))
            {
                return;
            }

            VisualStyleController style = FindInScene<VisualStyleController>(scene)
                ?? mage.GetComponentInChildren<VisualStyleController>(true);

            Undo.RecordObject(feedback, DialogTitle);
            feedback.BindPresentation(sockets, style);

            PooledObject burn = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/BurnEmbers.prefab")
                ?.GetComponent<PooledObject>();
            PooledObject adaptedBurn = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{Folder}/Adapted/FlameBurn_Vefects.prefab")?.GetComponent<PooledObject>();
            if (adaptedBurn != null)
            {
                burn = adaptedBurn;
            }

            PooledObject deflect = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/DeflectSpark.prefab")
                ?.GetComponent<PooledObject>();

            bool changed = feedback.FillEffects(burn, null, deflect);
            if (burn != null && feedback.BurnEffect != null && IsSoftGlowPlaceholder(feedback.BurnEffect))
            {
                changed |= feedback.ReplaceBurnEffect(burn);
            }

            if (deflect != null && feedback.GuardEffect != null && IsSoftGlowPlaceholder(feedback.GuardEffect))
            {
                changed |= feedback.ReplaceGuardEffect(deflect);
            }

            if (changed)
            {
                report.AppendLine("Connected shared burn/deflect VFX on CombatVisuals.");
            }

            MarkChanged(feedback);
        }
    }
}
