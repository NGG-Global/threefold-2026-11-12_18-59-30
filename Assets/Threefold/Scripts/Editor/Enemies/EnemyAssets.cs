using System.Text;
using Threefold.Combat;
using Threefold.CombatFeedback;
using Threefold.Enemies;
using Threefold.Pooling;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using static Threefold.EditorTools.CombatSetup;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Generates everything the two enemies need, in Assets/Threefold/Generated/Enemies:
    /// materials, effects, the wisp's projectile, the three abilities, the enemy definitions,
    /// three difficulty levels, and the Ruins Guardian and Arcane Wisp prefabs. The enemies are
    /// built from simple shapes, so no model or texture files are involved.
    ///
    /// Existing assets are kept, so tuned values survive. To regenerate with defaults, delete the
    /// whole folder: the assets refer to each other.
    /// </summary>
    internal sealed class EnemyAssets
    {
        internal const string Folder = "Assets/Threefold/Generated/Enemies";

        private const string GuardianStonePath = Folder + "/GuardianStone.mat";
        private const string GuardianTrimPath = Folder + "/GuardianTrim.mat";
        private const string GuardianCorePath = Folder + "/GuardianCore.mat";
        private const string WispCorePath = Folder + "/WispCore.mat";
        private const string WispShardPath = Folder + "/WispShard.mat";
        private const string AlertCuePath = Folder + "/AlertCue.mat";
        private const string TelegraphAreaPath = Folder + "/TelegraphArea.mat";

        private const string GuardianImpactPath = Folder + "/GuardianImpact.prefab";
        private const string GuardianDefeatPath = Folder + "/GuardianDefeat.prefab";
        private const string WispImpactPath = Folder + "/WispImpact.prefab";
        private const string WispDefeatPath = Folder + "/WispDefeat.prefab";
        private const string MotePrefabPath = Folder + "/ArcaneMote.prefab";

        private const string SlamPath = Folder + "/GuardianSlam.asset";
        private const string ShockwavePath = Folder + "/GuardianShockwave.asset";
        private const string MoteSpellPath = Folder + "/ArcaneMote.asset";

        private const string GuardianDefinitionPath = Folder + "/RuinsGuardian.asset";
        private const string WispDefinitionPath = Folder + "/ArcaneWisp.asset";
        private const string EasyPath = Folder + "/DifficultyEasy.asset";
        private const string NormalPath = Folder + "/DifficultyNormal.asset";
        private const string HardPath = Folder + "/DifficultyHard.asset";

        private const string GuardianPrefabPath = Folder + "/RuinsGuardian.prefab";
        private const string WispPrefabPath = Folder + "/ArcaneWisp.prefab";

        // Weathered slate stone with amber light for the Guardian; violet arcane light for the Wisp.
        // Both stay clear of the Mage's blue bolt and of the courtyard's warm stone (Art Bible, section 7).
        private static readonly Color GuardianStoneColor = new Color(0.4f, 0.42f, 0.47f, 1f);
        private static readonly Color GuardianTrimColor = new Color(0.47f, 0.33f, 0.25f, 1f);
        private static readonly Color GuardianCoreColor = new Color(2.2f, 1.15f, 0.35f, 1f);
        private static readonly Color GuardianLight = new Color(1f, 0.62f, 0.25f, 1f);
        private static readonly Color WispCoreColor = new Color(1.5f, 0.75f, 2.6f, 1f);
        private static readonly Color WispShardColor = new Color(0.28f, 0.24f, 0.38f, 1f);
        private static readonly Color WispLight = new Color(0.82f, 0.52f, 1f, 1f);

        private readonly StringBuilder report;
        private SharedAssets shared;

        private EnemyAssets(StringBuilder report)
        {
            this.report = report;
        }

        public EnemyDefinition GuardianDefinition { get; private set; }
        public EnemyDefinition WispDefinition { get; private set; }
        public EnemyDifficulty Easy { get; private set; }
        public EnemyDifficulty Normal { get; private set; }
        public EnemyDifficulty Hard { get; private set; }
        public GameObject GuardianPrefab { get; private set; }
        public GameObject WispPrefab { get; private set; }

        /// <summary>Creates whatever is missing and returns the full set.</summary>
        internal static EnemyAssets Ensure(StringBuilder report)
        {
            var assets = new EnemyAssets(report);
            assets.Build();
            return assets;
        }

        private void Build()
        {
            shared = EnsureSharedAssets(report);
            CombatSetup.EnsureFolder(Folder);

            Material stone = EnsureMaterial(GuardianStonePath, () => CreateColorMaterial(LitShader, GuardianStoneColor), report);
            Material trim = EnsureMaterial(GuardianTrimPath, () => CreateColorMaterial(LitShader, GuardianTrimColor), report);
            Material guardianCore = EnsureMaterial(GuardianCorePath, () => CreateColorMaterial(UnlitShader, GuardianCoreColor), report);
            Material wispCore = EnsureMaterial(WispCorePath, () => CreateColorMaterial(UnlitShader, WispCoreColor), report);
            Material shard = EnsureMaterial(WispShardPath, () => CreateColorMaterial(LitShader, WispShardColor), report);
            Material alert = EnsureMaterial(AlertCuePath, () => CreateColorMaterial(UnlitShader, new Color(2.4f, 1.9f, 0.6f, 1f)), report);
            Material area = EnsureMaterial(TelegraphAreaPath, CreateTelegraphMaterial, report);

            PooledObject guardianImpact = EnsureEffectPrefab(GuardianImpactPath, "GuardianImpact", shared.Glow, GuardianLight, 22, 0.8f, report);
            PooledObject guardianDefeat = EnsureEffectPrefab(GuardianDefeatPath, "GuardianDefeat", shared.Glow, GuardianLight, 34, 1.2f, report);
            PooledObject wispImpact = EnsureEffectPrefab(WispImpactPath, "WispImpact", shared.Glow, WispLight, 12, 0.6f, report);
            PooledObject wispDefeat = EnsureEffectPrefab(WispDefeatPath, "WispDefeat", shared.Glow, WispLight, 26, 1f, report);

            Projectile mote = EnsureMotePrefab(wispCore);

            MeleeStrikeDefinition slam = EnsureStrike(SlamPath, "Guardian Slam", 24f, 2.7f, 110f, guardianImpact);
            MeleeStrikeDefinition shockwave = EnsureStrike(ShockwavePath, "Guardian Shockwave", 18f, 3.6f, 360f, guardianImpact);
            ProjectileSpellDefinition moteSpell = EnsureMoteSpell(mote, wispImpact);

            GuardianDefinition = EnsureDefinition(GuardianDefinitionPath, "Ruins Guardian", EnemyArchetypes.RuinsGuardian());
            WispDefinition = EnsureDefinition(WispDefinitionPath, "Arcane Wisp", EnemyArchetypes.ArcaneWisp());

            Easy = EnsureDifficulty(EasyPath, EnemyDifficultySettings.Easy());
            Normal = EnsureDifficulty(NormalPath, EnemyDifficultySettings.Normal());
            Hard = EnsureDifficulty(HardPath, EnemyDifficultySettings.Hard());

            AssetDatabase.SaveAssets();

            var guardianParts = new Materials { Body = stone, Trim = trim, Core = guardianCore, Alert = alert, Area = area };
            var wispParts = new Materials { Body = shard, Trim = shard, Core = wispCore, Alert = alert, Area = area };

            GuardianPrefab = EnsureGuardianPrefab(guardianParts, slam, shockwave, guardianDefeat);
            WispPrefab = EnsureWispPrefab(wispParts, moteSpell, wispDefeat);
            AssetDatabase.SaveAssets();
        }

        private struct Materials
        {
            public Material Body;
            public Material Trim;
            public Material Core;
            public Material Alert;
            public Material Area;
        }

        // ----------------------------------------------------------- materials

        /// <summary>Transparent, unlit, two-sided and without depth writing. Coloured per enemy through property blocks.</summary>
        private static Material CreateTelegraphMaterial()
        {
            var material = new Material(FindShader(ParticleShader));
            material.color = Color.white;

            SetFloatIfPresent(material, "_Surface", 1f);
            SetFloatIfPresent(material, "_Blend", 0f);
            SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetFloatIfPresent(material, "_ZWrite", 0f);
            SetFloatIfPresent(material, "_Cull", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        // ------------------------------------------------------------ abilities

        private Projectile EnsureMotePrefab(Material coreMaterial)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(MotePrefabPath);
            if (existing != null)
            {
                return existing.GetComponent<Projectile>();
            }

            var root = new GameObject("ArcaneMote");
            root.AddComponent<PooledObject>();

            GameObject core = CreatePart(PrimitiveType.Sphere, "Core", root.transform, Vector3.zero,
                new Vector3(0.3f, 0.3f, 0.3f), coreMaterial);
            Renderer coreRenderer = core.GetComponent<Renderer>();
            coreRenderer.shadowCastingMode = ShadowCastingMode.Off;
            coreRenderer.receiveShadows = false;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.95f, 0.75f, 1f), 0f), new GradientColorKey(new Color(0.55f, 0.3f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });

            TrailRenderer trail = root.AddComponent<TrailRenderer>();
            trail.time = 0.3f;
            trail.minVertexDistance = 0.1f;
            trail.widthMultiplier = 0.3f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.colorGradient = gradient;
            trail.sharedMaterial = shared.Glow;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;

            root.AddComponent<Projectile>().Bind(trail);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, MotePrefabPath);
            Object.DestroyImmediate(root);
            report.AppendLine("Generated projectile prefab 'ArcaneMote'.");
            return prefab.GetComponent<Projectile>();
        }

        private MeleeStrikeDefinition EnsureStrike(string path, string displayName, float damage, float reach, float arc,
            PooledObject impact)
        {
            var existing = AssetDatabase.LoadAssetAtPath<MeleeStrikeDefinition>(path);
            if (existing != null)
            {
                if (existing.ImpactEffectPrefab == null && impact != null)
                {
                    existing.ConfigureStrike(existing.Damage, existing.Reach, existing.ArcDegrees, impact);
                    EditorUtility.SetDirty(existing);
                    report.AppendLine($"Reconnected '{displayName}' to its regenerated effect.");
                }

                return existing;
            }

            var strike = ScriptableObject.CreateInstance<MeleeStrikeDefinition>();

            // No mana and no cooldown: the enemy's own pacing decides when it attacks.
            strike.ConfigureAbility(displayName, 0f, 0f);
            strike.ConfigureStrike(damage, reach, arc, impact);
            AssetDatabase.CreateAsset(strike, path);
            report.AppendLine($"Generated ability '{displayName}' ({damage:0} damage, {reach:0.#} m, {arc:0} degrees).");
            return strike;
        }

        private ProjectileSpellDefinition EnsureMoteSpell(Projectile mote, PooledObject impact)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ProjectileSpellDefinition>(MoteSpellPath);
            if (existing != null)
            {
                if (existing.ProjectilePrefab == null || existing.ImpactEffectPrefab == null)
                {
                    existing.ConfigureProjectile(mote, impact, existing.Damage, existing.Speed, existing.MaxRange, existing.Radius);
                    EditorUtility.SetDirty(existing);
                    report.AppendLine("Reconnected 'Arcane Mote' to its regenerated prefabs.");
                }

                return existing;
            }

            var spell = ScriptableObject.CreateInstance<ProjectileSpellDefinition>();
            spell.ConfigureAbility("Arcane Mote", 0f, 0f);

            // Slow enough to sidestep at 10 m: it takes about 0.8 s to arrive.
            spell.ConfigureProjectile(mote, impact, 12f, 13f, 22f, 0.22f);
            spell.ConfigureLimits(16, 6f);
            AssetDatabase.CreateAsset(spell, MoteSpellPath);
            report.AppendLine("Generated ability 'Arcane Mote' (12 damage, 13 m/s).");
            return spell;
        }

        private EnemyDefinition EnsureDefinition(string path, string displayName, EnemyTuning tuning)
        {
            var existing = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
            if (existing != null)
            {
                return existing;
            }

            var definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            definition.Configure(displayName, tuning);
            AssetDatabase.CreateAsset(definition, path);
            report.AppendLine($"Generated enemy definition '{displayName}'.");
            return definition;
        }

        private EnemyDifficulty EnsureDifficulty(string path, EnemyDifficultySettings settings)
        {
            var existing = AssetDatabase.LoadAssetAtPath<EnemyDifficulty>(path);
            if (existing != null)
            {
                return existing;
            }

            var difficulty = ScriptableObject.CreateInstance<EnemyDifficulty>();
            difficulty.Configure(settings);
            AssetDatabase.CreateAsset(difficulty, path);
            report.AppendLine($"Generated difficulty '{settings.displayName}'.");
            return difficulty;
        }

        // ---------------------------------------------------------------- enemies

        /// <summary>
        /// A stone construct about 2.5 m tall: broad shoulders, heavy arms on shoulder pivots, and
        /// an amber core in its chest that flares when it winds up an attack.
        /// </summary>
        private GameObject EnsureGuardianPrefab(Materials m, MeleeStrikeDefinition slam, MeleeStrikeDefinition shockwave,
            PooledObject defeatEffect)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(GuardianPrefabPath);
            if (existing != null)
            {
                return existing;
            }

            EnemyTuning tuning = GuardianDefinition.Tuning;
            var root = new GameObject("RuinsGuardian");

            CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 1.2f, 0f);
            capsule.height = 2.4f;
            capsule.radius = 0.6f;

            NavMeshAgent agent = AddAgent(root, 0.6f, 2.4f, tuning, 8f, 40);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var body = new GameObject("Body");
            body.transform.SetParent(visual.transform, false);
            Transform b = body.transform;

            CreatePart(PrimitiveType.Cube, "Hips", b, new Vector3(0f, 0.95f, 0f), new Vector3(0.9f, 0.5f, 0.6f), m.Trim);
            CreatePart(PrimitiveType.Cube, "LegLeft", b, new Vector3(-0.3f, 0.4f, 0f), new Vector3(0.38f, 0.8f, 0.42f), m.Body);
            CreatePart(PrimitiveType.Cube, "LegRight", b, new Vector3(0.3f, 0.4f, 0f), new Vector3(0.38f, 0.8f, 0.42f), m.Body);
            CreatePart(PrimitiveType.Cube, "Torso", b, new Vector3(0f, 1.6f, 0f), new Vector3(1.3f, 1f, 0.8f), m.Body);
            CreatePart(PrimitiveType.Cube, "Mantle", b, new Vector3(0f, 2.15f, -0.05f), new Vector3(1.6f, 0.25f, 0.85f), m.Trim);
            CreatePart(PrimitiveType.Cube, "Head", b, new Vector3(0f, 2.48f, 0.05f), new Vector3(0.5f, 0.45f, 0.5f), m.Body);
            CreatePart(PrimitiveType.Cube, "Visor", b, new Vector3(0f, 2.5f, 0.31f), new Vector3(0.34f, 0.08f, 0.04f), m.Core);
            GameObject core = CreatePart(PrimitiveType.Sphere, "Core", b, new Vector3(0f, 1.65f, 0.38f),
                new Vector3(0.36f, 0.36f, 0.2f), m.Core);
            core.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            Transform leftArm = CreateArm(b, "ArmLeft", new Vector3(-0.92f, 2.05f, 0f), m);
            Transform rightArm = CreateArm(b, "ArmRight", new Vector3(0.92f, 2.05f, 0f), m);

            Transform eyes = CreateEmpty(root.transform, "Eyes", new Vector3(0f, 2.45f, 0.3f));
            Transform castPoint = CreateEmpty(root.transform, "CastPoint", new Vector3(0f, 1.65f, 0.55f));
            GameObject alertCue = CreateAlertCue(root.transform, 3.15f, m.Alert);

            Health health = root.AddComponent<Health>();
            health.Configure(tuning.maxHealth, new Vector3(0f, 1.5f, 0f));
            health.Team = CombatTeam.Enemy;

            FinishEnemy(root, health, agent, visual, body.transform, leftArm, rightArm, null, alertCue, eyes, castPoint,
                new Collider[] { capsule }, defeatEffect, m, EnemyPresentation.MotionStyle.Heavy, 3f,
                GuardianDefinition, new AbilityDefinition[] { slam, shockwave }, 0.9f, false);

            agent.stoppingDistance = 1.6f;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, GuardianPrefabPath);
            Object.DestroyImmediate(root);
            report.AppendLine("Generated enemy prefab 'RuinsGuardian'.");
            return prefab;
        }

        /// <summary>
        /// A floating violet core about 1.6 m above the ground, circled by three dark crystal
        /// shards. Small, bright and quick to read against stone.
        /// </summary>
        private GameObject EnsureWispPrefab(Materials m, ProjectileSpellDefinition moteSpell, PooledObject defeatEffect)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(WispPrefabPath);
            if (existing != null)
            {
                return existing;
            }

            EnemyTuning tuning = WispDefinition.Tuning;
            const float hover = 1.6f;
            var root = new GameObject("ArcaneWisp");

            SphereCollider sphere = root.AddComponent<SphereCollider>();
            sphere.center = new Vector3(0f, hover, 0f);
            sphere.radius = 0.45f;

            NavMeshAgent agent = AddAgent(root, 0.4f, 2f, tuning, 14f, 60);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var body = new GameObject("Body");
            body.transform.SetParent(visual.transform, false);
            Transform b = body.transform;

            GameObject core = CreatePart(PrimitiveType.Sphere, "Core", b, new Vector3(0f, hover, 0f),
                new Vector3(0.5f, 0.5f, 0.5f), m.Core);
            core.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var orbit = new GameObject("Orbit");
            orbit.transform.SetParent(b, false);
            orbit.transform.localPosition = new Vector3(0f, hover, 0f);

            for (int i = 0; i < 3; i++)
            {
                float angle = i * 120f;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, 0.62f);
                GameObject shard = CreatePart(PrimitiveType.Cube, "Shard" + (i + 1), orbit.transform, offset,
                    new Vector3(0.14f, 0.36f, 0.14f), m.Body);
                shard.transform.localRotation = Quaternion.Euler(20f, angle, 35f);
            }

            Transform eyes = CreateEmpty(root.transform, "Eyes", new Vector3(0f, hover + 0.1f, 0.25f));
            Transform castPoint = CreateEmpty(root.transform, "CastPoint", new Vector3(0f, hover, 0.45f));
            GameObject alertCue = CreateAlertCue(root.transform, hover + 0.85f, m.Alert);

            Health health = root.AddComponent<Health>();
            health.Configure(tuning.maxHealth, new Vector3(0f, hover, 0f));
            health.Team = CombatTeam.Enemy;

            FinishEnemy(root, health, agent, visual, body.transform, null, null, orbit.transform, alertCue, eyes, castPoint,
                new Collider[] { sphere }, defeatEffect, m, EnemyPresentation.MotionStyle.Floating, hover + 0.75f,
                WispDefinition, new AbilityDefinition[] { moteSpell }, 0.7f, true);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, WispPrefabPath);
            Object.DestroyImmediate(root);
            report.AppendLine("Generated enemy prefab 'ArcaneWisp'.");
            return prefab;
        }

        private static NavMeshAgent AddAgent(GameObject root, float radius, float height, EnemyTuning tuning,
            float acceleration, int priority)
        {
            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = radius;
            agent.height = height;
            agent.speed = tuning.walkSpeed;
            agent.acceleration = acceleration;

            // The enemy turns itself, at the speed set in its tuning.
            agent.angularSpeed = 0f;
            agent.updateRotation = false;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

            // Lower numbers have right of way: heavy enemies push through, light ones step aside.
            agent.avoidancePriority = priority;
            return agent;
        }

        /// <summary>The components and parts both enemies share: abilities, aim, telegraph, feedback and behaviour.</summary>
        private void FinishEnemy(GameObject root, Health health, NavMeshAgent agent, GameObject visual, Transform body,
            Transform leftArm, Transform rightArm, Transform orbit, GameObject alertCue, Transform eyes, Transform castPoint,
            Collider[] colliders, PooledObject defeatEffect, Materials m, EnemyPresentation.MotionStyle style,
            float barHeight, EnemyDefinition definition, AbilityDefinition[] abilities, float chargeSize, bool aimLine)
        {
            TargetAimProvider aim = root.AddComponent<TargetAimProvider>();

            AbilityCaster caster = root.AddComponent<AbilityCaster>();
            caster.Bind(null, health, aim);
            caster.SetCastPoint(castPoint);
            for (int i = 0; i < abilities.Length; i++)
            {
                caster.SetAbility(i, abilities[i]);
            }

            // Telegraph parts sit outside the visual root, so the hit flash does not recolour them.
            var telegraphRoot = new GameObject("Telegraph");
            telegraphRoot.transform.SetParent(root.transform, false);

            MeshFilter outline = CreateAreaPart(telegraphRoot.transform, "AreaOutline", 0.04f, m.Area);
            MeshFilter fill = CreateAreaPart(telegraphRoot.transform, "AreaFill", 0.05f, m.Area);

            GameObject charge = CreatePart(PrimitiveType.Quad, "ChargeGlow", telegraphRoot.transform,
                castPoint.localPosition, Vector3.one, shared.Glow);
            Renderer chargeRenderer = charge.GetComponent<Renderer>();
            chargeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            chargeRenderer.receiveShadows = false;
            chargeRenderer.enabled = false;

            LineRenderer line = null;
            if (aimLine)
            {
                var lineObject = new GameObject("AimLine");
                lineObject.transform.SetParent(telegraphRoot.transform, false);
                line = lineObject.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.widthMultiplier = 0.05f;
                line.numCapVertices = 2;
                line.sharedMaterial = shared.Glow;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
            }

            EnemyTelegraph telegraph = root.AddComponent<EnemyTelegraph>();
            telegraph.Bind(outline, fill, chargeRenderer, line, aim, castPoint);
            if (style == EnemyPresentation.MotionStyle.Floating)
            {
                telegraph.SetColors(new Color(0.85f, 0.5f, 1f, 0.18f), new Color(0.75f, 0.35f, 1f, 0.4f),
                    new Color(1f, 0.3f, 0.9f, 0.6f), WispCoreColor);
            }
            else
            {
                telegraph.SetColors(new Color(1f, 0.52f, 0.2f, 0.18f), new Color(1f, 0.4f, 0.12f, 0.42f),
                    new Color(1f, 0.25f, 0.1f, 0.6f), GuardianCoreColor);
            }

            SerializedObject telegraphData = new SerializedObject(telegraph);
            telegraphData.FindProperty("chargeSize").floatValue = chargeSize;
            telegraphData.ApplyModifiedPropertiesWithoutUndo();

            CreateHealthBar(root.transform, health, barHeight);
            root.AddComponent<HitFlash>().Bind(health, visual.transform);

            EnemyController controller = root.AddComponent<EnemyController>();
            controller.Bind(definition, caster, aim, telegraph, eyes, colliders, defeatEffect);

            EnemyPresentation presentation = root.AddComponent<EnemyPresentation>();
            presentation.Bind(controller, style, visual, body, leftArm, rightArm, orbit, alertCue);

            // Switched on by the controller in Start, once the navigation mesh is loaded, so the
            // agent is never created before there is a mesh to stand on.
            agent.enabled = false;
        }

        private static Transform CreateArm(Transform parent, string name, Vector3 shoulder, Materials m)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = shoulder;

            CreatePart(PrimitiveType.Cube, "Upper", pivot.transform, new Vector3(0f, -0.45f, 0f), new Vector3(0.42f, 0.9f, 0.42f), m.Body);
            CreatePart(PrimitiveType.Cube, "Fist", pivot.transform, new Vector3(0f, -1.1f, 0.04f), new Vector3(0.55f, 0.48f, 0.55f), m.Trim);
            return pivot.transform;
        }

        private static Transform CreateEmpty(Transform parent, string name, Vector3 localPosition)
        {
            var point = new GameObject(name);
            point.transform.SetParent(parent, false);
            point.transform.localPosition = localPosition;
            return point.transform;
        }

        /// <summary>A small glowing diamond above the head, shown for a moment when the enemy notices the player.</summary>
        private static GameObject CreateAlertCue(Transform parent, float height, Material material)
        {
            GameObject cue = CreatePart(PrimitiveType.Cube, "AlertCue", parent, new Vector3(0f, height, 0f),
                new Vector3(0.18f, 0.18f, 0.18f), material);
            cue.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            cue.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            cue.SetActive(false);
            return cue;
        }

        private static MeshFilter CreateAreaPart(Transform parent, string name, float height, Material material)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = new Vector3(0f, height, 0f);

            MeshFilter filter = part.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = part.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.enabled = false;
            return filter;
        }

        /// <summary>The same floating bar as the training dummies, shown only while damaged.</summary>
        private void CreateHealthBar(Transform parent, Health health, float height)
        {
            var bar = new GameObject("HealthBar");
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = new Vector3(0f, height, 0f);

            var barVisuals = new GameObject("Bar");
            barVisuals.transform.SetParent(bar.transform, false);
            CreatePart(PrimitiveType.Quad, "Back", barVisuals.transform, Vector3.zero, new Vector3(1f, 0.1f, 1f), shared.BarBack);
            GameObject fill = CreatePart(PrimitiveType.Quad, "Fill", barVisuals.transform, new Vector3(0f, 0f, -0.005f),
                new Vector3(0.96f, 0.06f, 1f), shared.BarFill);
            barVisuals.SetActive(false);

            bar.AddComponent<WorldHealthBar>().Bind(health, fill.transform, barVisuals);
        }
    }
}
