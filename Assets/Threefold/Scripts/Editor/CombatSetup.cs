using System.IO;
using System.Text;
using Threefold.Cameras;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.CombatFeedback;
using Threefold.InputHandling;
using Threefold.Interface;
using Threefold.Pooling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Generates everything the Mage's first spell needs and places it in the scene: materials, a
    /// glow texture, the projectile, impact and dummy prefabs, the spell asset, combat components
    /// on the Mage, training dummies, the HUD and camera feedback. It runs as part of
    /// Threefold > Setup > Mage Prototype and is also available on its own.
    ///
    /// It is safe to run repeatedly. Assets that already exist are kept as they are, so values
    /// tuned later are not lost. To regenerate with defaults, delete the whole folder
    /// Assets/Threefold/Generated/Combat: assets refer to each other, so deleting only one of
    /// them can leave the others pointing at nothing.
    /// </summary>
    public static class CombatSetup
    {
        private const string MenuPath = "Threefold/Setup/Combat";
        private const string DialogTitle = "Combat Setup";
        private const string Folder = "Assets/Threefold/Generated/Combat";

        private const string GlowTexturePath = Folder + "/SoftGlow.png";
        private const string GlowMaterialPath = Folder + "/ArcaneGlow.mat";
        private const string CoreMaterialPath = Folder + "/ArcaneBoltCore.mat";
        private const string DummyBodyMaterialPath = Folder + "/TrainingDummyBody.mat";
        private const string DummyPostMaterialPath = Folder + "/TrainingDummyPost.mat";
        private const string BarBackMaterialPath = Folder + "/HealthBarBack.mat";
        private const string BarFillMaterialPath = Folder + "/HealthBarFill.mat";
        private const string ImpactPrefabPath = Folder + "/ArcaneImpact.prefab";
        private const string DefeatPrefabPath = Folder + "/DummyBurst.prefab";
        private const string BoltPrefabPath = Folder + "/ArcaneBolt.prefab";
        private const string DummyPrefabPath = Folder + "/TrainingDummy.prefab";
        private const string SpellPath = Folder + "/ArcaneBolt.asset";

        private const string DummiesName = "TrainingDummies";
        internal const string ConstructsName = "TrainingConstructs";
        private const string ConstructPrefabPath = Folder + "/TrainingConstruct.prefab";
        private const string ConstructBodyMaterialPath = Folder + "/TrainingConstructBody.mat";
        private const string ConstructCoreMaterialPath = Folder + "/TrainingConstructCore.mat";
        private const string GroundName = "PrototypeGround";
        private const string HudName = "CombatHud";
        private const int UiLayer = 5;

        internal const string LitShader = "Universal Render Pipeline/Lit";
        internal const string UnlitShader = "Universal Render Pipeline/Unlit";
        internal const string ParticleShader = "Universal Render Pipeline/Particles/Unlit";

        private static readonly Color ArcaneColor = new Color(0.55f, 0.75f, 1f, 1f);
        private static readonly Color ArcaneCoreColor = new Color(0.82f, 0.93f, 1f, 1f);
        private static readonly Color StrawColor = new Color(0.72f, 0.6f, 0.38f, 1f);
        private static readonly Color WoodColor = new Color(0.34f, 0.25f, 0.18f, 1f);
        private static readonly Color HudFrameColor = new Color(0.05f, 0.05f, 0.07f, 0.55f);
        private static readonly Color HealthColor = new Color(0.86f, 0.24f, 0.22f, 0.95f);
        private static readonly Color ManaColor = new Color(0.33f, 0.56f, 0.98f, 0.95f);
        private static readonly Color CrosshairColor = new Color(1f, 0.97f, 0.9f, 0.8f);

        private static readonly Vector3[] ConstructPositions =
        {
            new Vector3(-7f, 0f, 7f), new Vector3(7f, 0f, 7f), new Vector3(0f, 0f, -7f),
        };

        private static readonly Vector3[] DummyPositions =
        {
            new Vector3(-3f, 0f, 7.5f), new Vector3(0f, 0f, 8f), new Vector3(3f, 0f, 7.5f), new Vector3(9f, 0f, 15f),
        };

        [MenuItem(MenuPath, false, 2)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(DialogTitle, "Exit Play mode before running the setup.", "OK");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            ThirdPersonMotor motor = FindInScene<ThirdPersonMotor>(scene);
            if (motor == null)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "The open scene has no Mage yet. Run Threefold > Setup > Mage Prototype instead; " +
                    "it creates the Mage, the touch controls and combat together.", "OK");
                return;
            }

            Undo.SetCurrentGroupName(DialogTitle);
            int undoGroup = Undo.GetCurrentGroup();
            var report = new StringBuilder();

            TouchControlsSetup.Ensure(scene, motor.gameObject, report);
            Ensure(scene, motor.gameObject, report);

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

        /// <summary>Where a training dummy stands, in world space.</summary>
        internal readonly struct DummyPlacement
        {
            public readonly Vector3 Position;
            public readonly float Yaw;

            /// <summary>True for an objective target, false for a practice dummy that returns by itself.</summary>
            public readonly bool Construct;

            public DummyPlacement(Vector3 position, float yaw, bool construct = false)
            {
                Position = position;
                Yaw = yaw;
                Construct = construct;
            }
        }

        /// <summary>Generates missing combat assets and adds missing combat objects to the scene.</summary>
        internal static void Ensure(Scene scene, GameObject mage, StringBuilder report)
        {
            Ensure(scene, mage, report, null);
        }

        /// <summary>Assets that other generators reuse: the soft glow and the world health bar materials.</summary>
        internal sealed class SharedAssets
        {
            public Texture2D GlowTexture;
            public Material Glow;
            public Material BarBack;
            public Material BarFill;
        }

        /// <summary>Creates the shared glow texture and the materials other generators reuse.</summary>
        internal static SharedAssets EnsureSharedAssets(StringBuilder report)
        {
            EnsureFolder(Folder);

            var shared = new SharedAssets();
            shared.GlowTexture = EnsureGlowTexture(report);
            Texture2D glow = shared.GlowTexture;
            shared.Glow = EnsureMaterial(GlowMaterialPath, () => CreateGlowMaterial(glow), report);
            shared.BarBack = EnsureMaterial(BarBackMaterialPath,
                () => CreateColorMaterial(UnlitShader, new Color(0.06f, 0.06f, 0.07f, 1f)), report);
            shared.BarFill = EnsureMaterial(BarFillMaterialPath,
                () => CreateColorMaterial(UnlitShader, new Color(0.88f, 0.24f, 0.2f, 1f)), report);
            return shared;
        }

        /// <param name="dummies">Where to place training dummies, or null for the default positions.</param>
        internal static void Ensure(Scene scene, GameObject mage, StringBuilder report, DummyPlacement[] dummies)
        {
            SharedAssets shared = EnsureSharedAssets(report);
            Material glowMaterial = shared.Glow;
            Material barBackMaterial = shared.BarBack;
            Material barFillMaterial = shared.BarFill;
            Material coreMaterial = EnsureMaterial(CoreMaterialPath, () => CreateColorMaterial(UnlitShader, ArcaneCoreColor), report);
            Material bodyMaterial = EnsureMaterial(DummyBodyMaterialPath, () => CreateColorMaterial(LitShader, StrawColor), report);
            Material postMaterial = EnsureMaterial(DummyPostMaterialPath, () => CreateColorMaterial(LitShader, WoodColor), report);

            PooledObject impact = EnsureEffectPrefab(ImpactPrefabPath, "ArcaneImpact", glowMaterial, ArcaneColor, 14, 0.7f, report);
            PooledObject burst = EnsureEffectPrefab(DefeatPrefabPath, "DummyBurst", glowMaterial,
                new Color(1f, 0.82f, 0.5f, 1f), 28, 1.1f, report);
            Projectile bolt = EnsureBoltPrefab(coreMaterial, glowMaterial, report);
            ProjectileSpellDefinition spell = EnsureSpell(bolt, impact, report);
            GameObject dummyPrefab = EnsureDummyPrefab(DummyPrefabPath, "TrainingDummy", bodyMaterial, postMaterial, null,
                barBackMaterial, barFillMaterial, burst, 100f, true, report);

            Material constructBody = EnsureMaterial(ConstructBodyMaterialPath,
                () => CreateColorMaterial(LitShader, new Color(0.3f, 0.33f, 0.42f, 1f)), report);
            Material constructCore = EnsureMaterial(ConstructCoreMaterialPath,
                () => CreateColorMaterial(UnlitShader, new Color(0.55f, 0.9f, 1f, 1f)), report);
            GameObject constructPrefab = EnsureDummyPrefab(ConstructPrefabPath, "TrainingConstruct", constructBody, postMaterial,
                constructCore, barBackMaterial, barFillMaterial, burst, 140f, false, report);

            AssetDatabase.SaveAssets();

            AbilityCaster caster = EnsureMageCombat(mage, spell, report);
            EnsureTargets(scene, dummyPrefab, constructPrefab, dummies, report);
            EnsureHud(scene, mage, caster, report);
            EnsureCameraFeedback(scene, mage, caster, report);

            report.AppendLine($"Combat assets are in '{Folder}'. They are new files; upload them from the laptop " +
                              "together with the scene (see Docs/DevelopmentWorkflow.md).");
        }

        // --------------------------------------------------------------- assets

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>A soft round glow used by the trail and the particles, so they have no hard edges.</summary>
        private static Texture2D EnsureGlowTexture(StringBuilder report)
        {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(GlowTexturePath);
            if (existing != null)
            {
                return existing;
            }

            const int size = 64;
            var pixels = new Color32[size * size];
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                    float falloff = Mathf.Clamp01(1f - distance);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(falloff * falloff * 255f));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path.GetFullPath(GlowTexturePath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(GlowTexturePath);
            if (AssetImporter.GetAtPath(GlowTexturePath) is TextureImporter importer)
            {
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            report.AppendLine("Generated the glow texture.");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(GlowTexturePath);
        }

        internal static Material EnsureMaterial(string path, System.Func<Material> create, StringBuilder report)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Material material = create();
            AssetDatabase.CreateAsset(material, path);
            report.AppendLine($"Generated material '{Path.GetFileNameWithoutExtension(path)}'.");
            return material;
        }

        internal static Shader FindShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Threefold] Shader '{shaderName}' was not found. Falling back to a built-in shader; " +
                                 "the generated material may look different from the intended one.");
                shader = Shader.Find("Sprites/Default");
            }

            return shader;
        }

        internal static Material CreateColorMaterial(string shaderName, Color color)
        {
            var material = new Material(FindShader(shaderName));
            material.color = color;
            SetFloatIfPresent(material, "_Smoothness", 0.15f);
            return material;
        }

        /// <summary>Additive, unlit and without depth writing: cheap on mobile and it always reads as light.</summary>
        private static Material CreateGlowMaterial(Texture2D glow)
        {
            var material = new Material(FindShader(ParticleShader));
            material.mainTexture = glow;
            material.color = Color.white;

            SetFloatIfPresent(material, "_Surface", 1f);
            SetFloatIfPresent(material, "_Blend", 2f);
            SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.One);
            SetFloatIfPresent(material, "_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        internal static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        internal static PooledObject EnsureEffectPrefab(string path, string prefabName, Material material, Color color,
            int particleCount, float lifetime, StringBuilder report)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing.GetComponent<PooledObject>();
            }

            var root = new GameObject(prefabName);
            root.AddComponent<PooledObject>();

            ParticleSystem particles = root.AddComponent<ParticleSystem>();
            ConfigureBurst(particles, material, color, particleCount);
            root.AddComponent<PooledEffect>().Bind(particles, lifetime);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            report.AppendLine($"Generated effect prefab '{prefabName}'.");
            return prefab.GetComponent<PooledObject>();
        }

        /// <summary>A single small burst that fades and shrinks. Few particles, no collision, no lights.</summary>
        private static void ConfigureBurst(ParticleSystem particles, Material material, Color color, int count)
        {
            // The duration cannot be changed while a system is playing.
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.34f);
            main.startColor = new ParticleSystem.MinMaxGradient(color);
            main.gravityModifier = 0.25f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count * 2;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = material;
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
        }

        private static Projectile EnsureBoltPrefab(Material coreMaterial, Material glowMaterial, StringBuilder report)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(BoltPrefabPath);
            if (existing != null)
            {
                return existing.GetComponent<Projectile>();
            }

            var root = new GameObject("ArcaneBolt");
            root.AddComponent<PooledObject>();

            GameObject core = CreatePart(PrimitiveType.Sphere, "Core", root.transform, Vector3.zero,
                new Vector3(0.2f, 0.2f, 0.2f), coreMaterial);
            core.GetComponent<Renderer>().receiveShadows = false;

            var trailGradient = new Gradient();
            trailGradient.SetKeys(
                new[] { new GradientColorKey(ArcaneCoreColor, 0f), new GradientColorKey(new Color(0.5f, 0.42f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });

            TrailRenderer trail = root.AddComponent<TrailRenderer>();
            trail.time = 0.22f;
            trail.minVertexDistance = 0.08f;
            trail.widthMultiplier = 0.24f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.colorGradient = trailGradient;
            trail.sharedMaterial = glowMaterial;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;

            root.AddComponent<Projectile>().Bind(trail);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, BoltPrefabPath);
            Object.DestroyImmediate(root);
            report.AppendLine("Generated projectile prefab 'ArcaneBolt'.");
            return prefab.GetComponent<Projectile>();
        }

        private static ProjectileSpellDefinition EnsureSpell(Projectile bolt, PooledObject impact, StringBuilder report)
        {
            ProjectileSpellDefinition existing = AssetDatabase.LoadAssetAtPath<ProjectileSpellDefinition>(SpellPath);
            if (existing != null)
            {
                // A regenerated prefab has a new identity, which leaves the spell pointing at nothing.
                if (existing.ProjectilePrefab == null || existing.ImpactEffectPrefab == null)
                {
                    existing.ConfigureProjectile(bolt, impact, existing.Damage, existing.Speed, existing.MaxRange,
                        existing.Radius);
                    EditorUtility.SetDirty(existing);
                    report.AppendLine("Reconnected the spell asset to its regenerated prefabs.");
                }

                return existing;
            }

            ProjectileSpellDefinition spell = ScriptableObject.CreateInstance<ProjectileSpellDefinition>();
            spell.ConfigureAbility("Arcane Bolt", 12f, 0.35f);
            spell.ConfigureProjectile(bolt, impact, 20f, 26f, 45f, 0.18f);
            AssetDatabase.CreateAsset(spell, SpellPath);
            report.AppendLine("Generated spell asset 'ArcaneBolt' (12 mana, 0.35 s cooldown, 20 damage).");
            return spell;
        }

        /// <summary>
        /// Builds a stationary target. With a core material it becomes a magical construct: a
        /// glowing gem is set in its chest and head, so objective targets are told apart at a glance.
        /// </summary>
        private static GameObject EnsureDummyPrefab(string path, string prefabName, Material body, Material post,
            Material core, Material barBack, Material barFill, PooledObject defeatEffect, float maxHealth, bool respawns,
            StringBuilder report)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing;
            }

            var root = new GameObject(prefabName);

            Health health = root.AddComponent<Health>();
            health.Configure(maxHealth, new Vector3(0f, 1.15f, 0f));

            CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.95f, 0f);
            capsule.height = 1.9f;
            capsule.radius = 0.4f;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            CreatePart(PrimitiveType.Cylinder, "Post", visual.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.12f, 0.5f, 0.12f), post);
            CreatePart(PrimitiveType.Capsule, "Body", visual.transform, new Vector3(0f, 1.15f, 0f), new Vector3(0.6f, 0.45f, 0.6f), body);
            CreatePart(PrimitiveType.Sphere, "Head", visual.transform, new Vector3(0f, 1.78f, 0f), new Vector3(0.36f, 0.36f, 0.36f), body);
            CreatePart(PrimitiveType.Cube, "Arms", visual.transform, new Vector3(0f, 1.32f, 0f), new Vector3(1.1f, 0.1f, 0.1f), post);

            if (core != null)
            {
                CreatePart(PrimitiveType.Sphere, "ChestCore", visual.transform, new Vector3(0f, 1.2f, 0.27f),
                    new Vector3(0.24f, 0.24f, 0.16f), core);
                CreatePart(PrimitiveType.Sphere, "HeadCore", visual.transform, new Vector3(0f, 1.8f, 0.15f),
                    new Vector3(0.14f, 0.1f, 0.1f), core);
            }

            var bar = new GameObject("HealthBar");
            bar.transform.SetParent(root.transform, false);
            bar.transform.localPosition = new Vector3(0f, 2.25f, 0f);

            var barVisuals = new GameObject("Bar");
            barVisuals.transform.SetParent(bar.transform, false);
            CreatePart(PrimitiveType.Quad, "Back", barVisuals.transform, Vector3.zero, new Vector3(0.9f, 0.09f, 1f), barBack);
            GameObject fill = CreatePart(PrimitiveType.Quad, "Fill", barVisuals.transform, new Vector3(0f, 0f, -0.005f),
                new Vector3(0.86f, 0.055f, 1f), barFill);
            barVisuals.SetActive(false);

            bar.AddComponent<WorldHealthBar>().Bind(health, fill.transform, barVisuals);
            root.AddComponent<HitFlash>().Bind(health, visual.transform);
            TrainingDummy dummy = root.AddComponent<TrainingDummy>();
            dummy.Bind(visual, new Collider[] { capsule }, defeatEffect);
            dummy.Respawns = respawns;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            report.AppendLine(respawns
                ? $"Generated prefab '{prefabName}' ({maxHealth:0} health, returns 4 s after defeat)."
                : $"Generated prefab '{prefabName}' ({maxHealth:0} health, stays down until the level restarts).");
            return prefab;
        }

        /// <summary>A primitive mesh used purely as a visual: its collider is removed and it casts no shadow.</summary>
        internal static GameObject CreatePart(PrimitiveType type, string name, Transform parent, Vector3 localPosition,
            Vector3 localScale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;

            if (part.TryGetComponent(out Collider partCollider))
            {
                Object.DestroyImmediate(partCollider);
            }

            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;

            Renderer partRenderer = part.GetComponent<Renderer>();
            partRenderer.sharedMaterial = material;
            partRenderer.shadowCastingMode = type == PrimitiveType.Quad ? ShadowCastingMode.Off : ShadowCastingMode.On;
            return part;
        }

        // ---------------------------------------------------------------- scene

        private static AbilityCaster EnsureMageCombat(GameObject mage, ProjectileSpellDefinition spell, StringBuilder report)
        {
            if (!mage.TryGetComponent(out Health health))
            {
                health = Undo.AddComponent<Health>(mage);
                health.Configure(100f, new Vector3(0f, 1.1f, 0f));
                MarkChanged(health);
                report.AppendLine("Added Health to the Mage (100).");
            }

            // Enemies' projectiles and strikes pass over their own allies; the Mage must be on the other side.
            if (health.Team == CombatTeam.Neutral)
            {
                Undo.RecordObject(health, DialogTitle);
                health.Team = CombatTeam.Player;
                MarkChanged(health);
                report.AppendLine("Put the Mage on the Player team.");
            }

            if (!mage.TryGetComponent(out Mana mana))
            {
                mana = Undo.AddComponent<Mana>(mage);
                mana.Configure(100f, 9f, 0.9f);
                MarkChanged(mana);
                report.AppendLine("Added Mana to the Mage (100, regenerates 9 per second after 0.9 s).");
            }

            if (!mage.TryGetComponent(out CameraAimProvider aim))
            {
                aim = Undo.AddComponent<CameraAimProvider>(mage);
                report.AppendLine("Added camera-based aiming to the Mage.");
            }

            if (!mage.TryGetComponent(out AbilityCaster caster))
            {
                caster = Undo.AddComponent<AbilityCaster>(mage);
                caster.Bind(mana, health, aim);
                MarkChanged(caster);
                report.AppendLine("Added the ability caster to the Mage.");
            }

            if (caster.GetAbility(0) == null && spell != null)
            {
                Undo.RecordObject(caster, DialogTitle);
                caster.SetAbility(0, spell);
                MarkChanged(caster);
                report.AppendLine("Assigned Arcane Bolt as the Mage's primary attack.");
            }

            if (!mage.TryGetComponent(out PlayerAbilityInput abilityInput))
            {
                abilityInput = Undo.AddComponent<PlayerAbilityInput>(mage);

                ThirdPersonMotor motor = mage.GetComponent<ThirdPersonMotor>();
                CharacterInputSource input = motor != null && motor.InputSource != null
                    ? motor.InputSource
                    : mage.GetComponent<CharacterInputSource>();

                abilityInput.Bind(input, caster, motor);
                MarkChanged(abilityInput);
                report.AppendLine("Connected the attack input to the Mage's caster.");

                if (input == null)
                {
                    report.AppendLine("WARNING: the Mage has no input source, so attacks cannot be triggered.");
                }
            }

            return caster;
        }

        /// <summary>
        /// Places the practice dummies and the objective constructs, each group under its own parent.
        /// A group that already exists in the scene is kept as it is.
        /// </summary>
        private static void EnsureTargets(Scene scene, GameObject dummyPrefab, GameObject constructPrefab,
            DummyPlacement[] placements, StringBuilder report)
        {
            if (placements == null)
            {
                // Default layout of the grey-box prototype, relative to its ground plane.
                GameObject ground = FindByName(scene, GroundName);
                Vector3 origin = ground != null ? ground.transform.position : Vector3.zero;

                placements = new DummyPlacement[DummyPositions.Length + ConstructPositions.Length];
                for (int i = 0; i < DummyPositions.Length; i++)
                {
                    placements[i] = new DummyPlacement(origin + DummyPositions[i], 180f);
                }

                for (int i = 0; i < ConstructPositions.Length; i++)
                {
                    placements[DummyPositions.Length + i] = new DummyPlacement(origin + ConstructPositions[i], 180f, true);
                }
            }

            PlaceGroup(scene, DummiesName, "TrainingDummy", dummyPrefab, placements, false, report);
            PlaceGroup(scene, ConstructsName, "TrainingConstruct", constructPrefab, placements, true, report);
        }

        private static void PlaceGroup(Scene scene, string groupName, string instanceName, GameObject prefab,
            DummyPlacement[] placements, bool constructs, StringBuilder report)
        {
            if (FindByName(scene, groupName) != null)
            {
                report.AppendLine($"Kept existing '{groupName}'.");
                return;
            }

            bool any = false;
            for (int i = 0; i < placements.Length && !any; i++)
            {
                any = placements[i].Construct == constructs;
            }

            // A level without targets of this kind, such as the combat arena, gets no empty group.
            if (!any)
            {
                return;
            }

            if (prefab == null)
            {
                report.AppendLine($"WARNING: the prefab for '{groupName}' could not be created, so none were placed.");
                return;
            }

            var parent = new GameObject(groupName);
            int placed = 0;

            for (int i = 0; i < placements.Length; i++)
            {
                if (placements[i].Construct != constructs)
                {
                    continue;
                }

                placed++;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                instance.name = instanceName + " " + placed;
                instance.transform.SetPositionAndRotation(placements[i].Position, Quaternion.Euler(0f, placements[i].Yaw, 0f));
            }

            Undo.RegisterCreatedObjectUndo(parent, DialogTitle);
            report.AppendLine($"Placed {placed} object(s) in '{groupName}'.");
        }

        private static void EnsureHud(Scene scene, GameObject mage, AbilityCaster caster, StringBuilder report)
        {
            if (FindInScene<PlayerHud>(scene) != null)
            {
                report.AppendLine("Kept existing combat HUD.");
                return;
            }

            var root = new GameObject(HudName, typeof(Canvas), typeof(CanvasGroup));
            root.layer = UiLayer;

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            root.AddComponent<DensityCanvasScaler>();

            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            // Damage numbers move every frame. Their own canvas keeps that from redrawing the bars.
            RectTransform numbers = TouchControlsSetup.CreateRect("DamageNumbers", root.transform);
            TouchControlsSetup.Stretch(numbers);
            numbers.gameObject.AddComponent<Canvas>();
            numbers.gameObject.AddComponent<DamageNumbers>();

            RectTransform safeArea = TouchControlsSetup.CreateRect("SafeArea", root.transform);
            TouchControlsSetup.Stretch(safeArea);
            safeArea.gameObject.AddComponent<SafeAreaFitter>();

            ResourceBar healthBar = BuildBar("HealthBar", safeArea, new Vector2(24f, -24f), new Vector2(220f, 14f), HealthColor);
            ResourceBar manaBar = BuildBar("ManaBar", safeArea, new Vector2(24f, -44f), new Vector2(220f, 10f), ManaColor);

            // The crosshair marks the exact screen centre, so it sits outside the safe area.
            Image crosshair = TouchControlsSetup.CreateShape("Crosshair", root.transform, ProceduralShape.Ring, CrosshairColor);
            TouchControlsSetup.Centre(crosshair.rectTransform, 26f);
            Image dot = TouchControlsSetup.CreateShape("Dot", crosshair.transform, ProceduralShape.Disc, CrosshairColor);
            TouchControlsSetup.Centre(dot.rectTransform, 4f);

            PlayerHud hud = root.AddComponent<PlayerHud>();
            hud.Bind(mage.GetComponent<Health>(), mage.GetComponent<Mana>(), caster, healthBar, manaBar, crosshair);

            Undo.RegisterCreatedObjectUndo(root, DialogTitle);
            report.AppendLine($"Created '{HudName}' (health bar, mana bar, crosshair, damage numbers).");
        }

        private static ResourceBar BuildBar(string name, RectTransform parent, Vector2 position, Vector2 size, Color color)
        {
            RectTransform frame = TouchControlsSetup.CreateRect(name, parent);
            frame.anchorMin = new Vector2(0f, 1f);
            frame.anchorMax = new Vector2(0f, 1f);
            frame.pivot = new Vector2(0f, 1f);
            frame.sizeDelta = size;
            frame.anchoredPosition = position;

            Image frameImage = frame.gameObject.AddComponent<Image>();
            frameImage.color = HudFrameColor;
            frameImage.raycastTarget = false;

            RectTransform area = TouchControlsSetup.CreateRect("FillArea", frame);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(2f, 2f);
            area.offsetMax = new Vector2(-2f, -2f);

            RectTransform fill = TouchControlsSetup.CreateRect("Fill", area);
            TouchControlsSetup.Stretch(fill);
            Image fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = color;
            fillImage.raycastTarget = false;

            ResourceBar bar = frame.gameObject.AddComponent<ResourceBar>();
            bar.Bind(fill, fillImage, frameImage);
            return bar;
        }

        private static void EnsureCameraFeedback(Scene scene, GameObject mage, AbilityCaster caster, StringBuilder report)
        {
            ThirdPersonCamera rig = FindInScene<ThirdPersonCamera>(scene);
            if (rig == null)
            {
                report.AppendLine("WARNING: no third-person camera was found, so camera shake is not connected.");
                return;
            }

            if (rig.TryGetComponent(out CombatCameraFeedback _))
            {
                return;
            }

            CombatCameraFeedback feedback = Undo.AddComponent<CombatCameraFeedback>(rig.gameObject);
            feedback.Bind(rig, mage, caster);
            MarkChanged(feedback);
            report.AppendLine("Added camera shake for casting and hits.");
        }
    }
}
