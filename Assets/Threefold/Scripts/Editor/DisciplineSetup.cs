using System.Text;
using Threefold.Cameras;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.CombatFeedback;
using Threefold.InputHandling;
using Threefold.Magic;
using Threefold.Pooling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Generates the three disciplines (Arcane, Flame, Aegis) and connects them to the Mage: the
    /// discipline assets with their melee combos, the charged abilities (Arcane Arrow, Flame Wave,
    /// Force Pulse), the projectile and effect prefabs, the combat component, the aim preview, the
    /// guard arc and the feedback. It runs as part of Threefold > Setup > Mage Prototype and the
    /// other scene commands, and is also available on its own.
    ///
    /// It is safe to run repeatedly. Assets that already exist are kept as they are, so values
    /// tuned later are not lost; only empty references are filled. To regenerate with defaults,
    /// delete the whole folder Assets/Threefold/Generated/Disciplines.
    /// </summary>
    public static class DisciplineSetup
    {
        private const string MenuPath = "Threefold/Setup/Disciplines";
        private const string DialogTitle = "Discipline Setup";
        internal const string Folder = "Assets/Threefold/Generated/Disciplines";

        private const string ShapeMaterialPath = Folder + "/AimShape.mat";
        private const string ArrowCoreMaterialPath = Folder + "/ArcaneArrowCore.mat";
        private const string ArrowPrefabPath = Folder + "/ArcaneArrow.prefab";
        private const string SweepPrefabPath = Folder + "/SweepShape.prefab";
        private const string BurnEffectPath = Folder + "/Embers.prefab";
        private const string PushEffectPath = Folder + "/PushDust.prefab";
        private const string GuardEffectPath = Folder + "/GuardSpark.prefab";

        private const string VisualsName = "CombatVisuals";

        private static readonly Color EmberColor = new Color(1f, 0.62f, 0.3f, 1f);
        private static readonly Color DustColor = new Color(0.95f, 0.9f, 0.76f, 1f);
        private static readonly Color GuardColor = new Color(1f, 0.97f, 0.88f, 1f);

        [MenuItem(MenuPath, false, 3)]
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
                    "it creates the Mage, the controls, combat and the disciplines together.", "OK");
                return;
            }

            Undo.SetCurrentGroupName(DialogTitle);
            int undoGroup = Undo.GetCurrentGroup();
            var report = new StringBuilder();

            TouchControlsSetup.Ensure(scene, motor.gameObject, report);
            CombatSetup.Ensure(scene, motor.gameObject, report);
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

        /// <summary>Generates missing discipline assets and connects them to the given Mage.</summary>
        internal static void Ensure(Scene scene, GameObject mage, StringBuilder report)
        {
            DisciplineDefinition[] definitions = EnsureAssets(report, out SharedEffects effects, out Material shapeMaterial);
            AssetDatabase.SaveAssets();

            EnsureMageDisciplines(scene, mage, definitions, report);
            EnsureVisuals(scene, mage, effects, shapeMaterial, report);
            MagicVfxSetup.Ensure(scene, mage, report);
        }

        // --------------------------------------------------------------- assets

        internal struct SharedEffects
        {
            public PooledObject Burn;
            public PooledObject Push;
            public PooledObject Guard;
        }

        /// <summary>Generates the discipline assets and everything they refer to. Returns them in selector order.</summary>
        internal static DisciplineDefinition[] EnsureAssets(StringBuilder report, out SharedEffects effects,
            out Material shapeMaterial)
        {
            CombatSetup.EnsureFolder(Folder);
            CombatSetup.SharedAssets shared = CombatSetup.EnsureSharedAssets(report);
            Material glow = shared.Glow;

            shapeMaterial = CombatSetup.EnsureMaterial(ShapeMaterialPath, EnemyAssets.CreateTelegraphMaterial, report);
            PooledObject sweep = EnsureSweepPrefab(shapeMaterial, report);

            effects = new SharedEffects
            {
                Burn = CombatSetup.EnsureEffectPrefab(BurnEffectPath, "Embers", glow, EmberColor, 6, 0.6f, report),
                Push = CombatSetup.EnsureEffectPrefab(PushEffectPath, "PushDust", glow, DustColor, 8, 0.5f, report),
                Guard = CombatSetup.EnsureEffectPrefab(GuardEffectPath, "GuardSpark", glow, GuardColor, 8, 0.35f, report),
            };

            var definitions = new DisciplineDefinition[DisciplineCatalog.Count];
            for (int slot = 0; slot < definitions.Length; slot++)
            {
                definitions[slot] = EnsureDiscipline((MagicDiscipline)slot, glow, sweep, report);
            }

            report.AppendLine($"Discipline assets are in '{Folder}'. New files there exist only on this computer until " +
                              "they are uploaded with the scene (see Docs/DevelopmentWorkflow.md).");
            return definitions;
        }

        private static DisciplineDefinition EnsureDiscipline(MagicDiscipline discipline, Material glow, PooledObject sweep,
            StringBuilder report)
        {
            DisciplineInfo info = DisciplineCatalog.Get(discipline);
            ChargedAttackDefaults chargedDefaults = DisciplineDefaults.Charged(discipline);
            string key = info.Name;

            PooledObject strikeImpact = CombatSetup.EnsureEffectPrefab($"{Folder}/{key}StrikeImpact.prefab",
                key + "StrikeImpact", glow, info.Color, 10, 0.45f, report);
            PooledObject chargedImpact = CombatSetup.EnsureEffectPrefab($"{Folder}/{key}ChargedImpact.prefab",
                key + "ChargedImpact", glow, info.Color, 18, 0.75f, report);
            PooledObject release = CombatSetup.EnsureEffectPrefab($"{Folder}/{key}Release.prefab",
                key + "Release", glow, info.Color, 8, 0.4f, report);

            ChargedAbilityDefinition charged = EnsureChargedAbility(chargedDefaults, glow, chargedImpact, report);

            string path = $"{Folder}/{key}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<DisciplineDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<DisciplineDefinition>();
                definition.ConfigureIdentity(discipline, info.Name, info.Color, info.Icon, DisciplineDefaults.Element(discipline));
                definition.ConfigureCombat(DisciplineDefaults.Melee(discipline), charged);
                definition.ConfigureGuard(DisciplineDefaults.Guard(discipline));
                definition.MarkCurrent();
                definition.Presentation.meleeImpactEffect = strikeImpact;
                definition.Presentation.sweepEffect = sweep;
                definition.Presentation.releaseEffect = release;
                AssetDatabase.CreateAsset(definition, path);

                MeleeComboProfile melee = definition.Melee;
                report.AppendLine($"Generated discipline '{info.Name}': {melee.StepCount}-step melee combo, charged attack " +
                                  $"'{chargedDefaults.Name}' ({chargedDefaults.ManaCost:0} mana, " +
                                  $"{chargedDefaults.Charge.fullChargeSeconds:0.#} s to full charge).");
                return definition;
            }

            // An existing asset keeps its values. Only references that point at nothing are filled, which
            // also repairs it after a prefab was regenerated.
            bool changed = definition.FillChargedAbility(charged);

            // An asset generated before the world effects existed is given them, once. Values that are
            // already set are kept; after this the asset is never touched for it again.
            if (definition.Upgrade())
            {
                EditorUtility.SetDirty(definition);
                if (definition.ChargedAbility != null)
                {
                    EditorUtility.SetDirty(definition.ChargedAbility);
                }

                report.AppendLine($"Updated discipline '{info.Name}' with what later sprints added (force, bracing, " +
                                  "arcane charge and deflection where it has them). Its other values were kept.");
            }
            DisciplinePresentation look = definition.Presentation;

            if (look.meleeImpactEffect == null && strikeImpact != null)
            {
                look.meleeImpactEffect = strikeImpact;
                changed = true;
            }

            if (look.sweepEffect == null && sweep != null)
            {
                look.sweepEffect = sweep;
                changed = true;
            }

            if (look.releaseEffect == null && release != null)
            {
                look.releaseEffect = release;
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(definition);
                report.AppendLine($"Reconnected discipline '{info.Name}' to its generated assets.");
            }

            return definition;
        }

        private static ChargedAbilityDefinition EnsureChargedAbility(ChargedAttackDefaults defaults, Material glow,
            PooledObject impact, StringBuilder report)
        {
            string path = $"{Folder}/{defaults.Name.Replace(" ", string.Empty)}.asset";

            if (defaults.Projectile != null)
            {
                Projectile arrow = EnsureArrowPrefab(glow, report);
                var existing = AssetDatabase.LoadAssetAtPath<ChargedProjectileDefinition>(path);
                if (existing != null)
                {
                    // Each reference is filled only if it is empty, so a prefab assigned by hand is kept.
                    bool needsProjectile = existing.ProjectilePrefab == null && arrow != null;
                    bool needsImpact = existing.ImpactEffectPrefab == null && impact != null;
                    if (needsProjectile || needsImpact)
                    {
                        existing.ConfigureProjectile(needsProjectile ? arrow : existing.ProjectilePrefab,
                            needsImpact ? impact : existing.ImpactEffectPrefab, existing.Projectile);
                        EditorUtility.SetDirty(existing);
                        report.AppendLine($"Reconnected '{defaults.Name}' to its regenerated prefabs.");
                    }

                    return existing;
                }

                var projectile = ScriptableObject.CreateInstance<ChargedProjectileDefinition>();
                projectile.ConfigureAbility(defaults.Name, defaults.ManaCost, defaults.CooldownSeconds);
                projectile.ConfigureCharge(defaults.Charge);
                projectile.ConfigureProjectile(arrow, impact, defaults.Projectile);
                AssetDatabase.CreateAsset(projectile, path);
                report.AppendLine($"Generated charged ability '{defaults.Name}' ({defaults.Charge.damageMin:0} to " +
                                  $"{defaults.Charge.damageMax:0} damage, range {defaults.Projectile.range:0} m).");
                return projectile;
            }

            var existingArea = AssetDatabase.LoadAssetAtPath<ChargedAreaDefinition>(path);
            if (existingArea != null)
            {
                if (existingArea.ImpactEffectPrefab == null && impact != null)
                {
                    existingArea.ConfigureArea(existingArea.Area, impact);
                    EditorUtility.SetDirty(existingArea);
                    report.AppendLine($"Reconnected '{defaults.Name}' to its regenerated effect.");
                }

                return existingArea;
            }

            var area = ScriptableObject.CreateInstance<ChargedAreaDefinition>();
            area.ConfigureAbility(defaults.Name, defaults.ManaCost, defaults.CooldownSeconds);
            area.ConfigureCharge(defaults.Charge);
            area.ConfigureArea(defaults.Area, impact);
            AssetDatabase.CreateAsset(area, path);
            report.AppendLine($"Generated charged ability '{defaults.Name}' (reach {defaults.Area.reachMin:0.#} to " +
                              $"{defaults.Area.reachMax:0.#} m, {defaults.Area.arcDegrees:0} degrees, up to " +
                              $"{defaults.Area.maxTargets} targets).");
            return area;
        }

        /// <summary>The Arcane Arrow: a longer, heavier bolt than the Arcane Bolt, with a longer trail.</summary>
        private static Projectile EnsureArrowPrefab(Material glow, StringBuilder report)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPrefabPath);
            if (existing != null)
            {
                return existing.GetComponent<Projectile>();
            }

            Color arcane = DisciplineCatalog.Get(MagicDiscipline.Arcane).Color;
            Material core = CombatSetup.EnsureMaterial(ArrowCoreMaterialPath,
                () => CombatSetup.CreateColorMaterial(CombatSetup.UnlitShader, Color.Lerp(arcane, Color.white, 0.55f)), report);

            var root = new GameObject("ArcaneArrow");
            root.AddComponent<PooledObject>();

            // Stretched along its flight, so it reads as an arrow rather than a ball.
            GameObject head = CombatSetup.CreatePart(PrimitiveType.Sphere, "Head", root.transform, Vector3.zero,
                new Vector3(0.16f, 0.16f, 0.6f), core);
            head.GetComponent<Renderer>().receiveShadows = false;
            head.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.Lerp(arcane, Color.white, 0.4f), 0f), new GradientColorKey(arcane, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });

            TrailRenderer trail = root.AddComponent<TrailRenderer>();
            trail.time = 0.3f;
            trail.minVertexDistance = 0.1f;
            trail.widthMultiplier = 0.3f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.colorGradient = gradient;
            trail.sharedMaterial = glow;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;

            Projectile projectile = root.AddComponent<Projectile>();
            projectile.Bind(trail);
            projectile.Element = SpellElement.Arcane;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ArrowPrefabPath);
            Object.DestroyImmediate(root);
            report.AppendLine("Generated projectile prefab 'ArcaneArrow'.");
            return prefab.GetComponent<Projectile>();
        }

        /// <summary>The flat shape flashed on the ground for swings, waves and pulses. Its mesh is made at run time.</summary>
        private static PooledObject EnsureSweepPrefab(Material shapeMaterial, StringBuilder report)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(SweepPrefabPath);
            if (existing != null)
            {
                return existing.GetComponent<PooledObject>();
            }

            var root = new GameObject("SweepShape");
            root.AddComponent<PooledObject>();
            MeshFilter filter = root.AddComponent<MeshFilter>();
            MeshRenderer shape = root.AddComponent<MeshRenderer>();
            ConfigureShapeRenderer(shape, shapeMaterial);
            root.AddComponent<SweepEffect>().Bind(filter, shape);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, SweepPrefabPath);
            Object.DestroyImmediate(root);
            report.AppendLine("Generated effect prefab 'SweepShape' (the arc of swings, waves and pulses).");
            return prefab.GetComponent<PooledObject>();
        }

        private static void ConfigureShapeRenderer(MeshRenderer shape, Material material)
        {
            shape.sharedMaterial = material;
            shape.shadowCastingMode = ShadowCastingMode.Off;
            shape.receiveShadows = false;
            shape.lightProbeUsage = LightProbeUsage.Off;
            shape.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ---------------------------------------------------------------- scene

        private static void EnsureMageDisciplines(Scene scene, GameObject mage, DisciplineDefinition[] definitions,
            StringBuilder report)
        {
            ThirdPersonMotor motor = mage.GetComponent<ThirdPersonMotor>();
            CharacterInputSource input = motor != null && motor.InputSource != null
                ? motor.InputSource
                : mage.GetComponent<CharacterInputSource>();

            if (!mage.TryGetComponent(out PlayerDisciplines disciplines))
            {
                disciplines = Undo.AddComponent<PlayerDisciplines>(mage);
                disciplines.Bind(input);
                MarkChanged(disciplines);
                report.AppendLine("Added discipline selection to the Mage.");
            }

            bool assigned = false;
            for (int slot = 0; slot < definitions.Length; slot++)
            {
                if (disciplines.GetDefinition(slot) == null && definitions[slot] != null)
                {
                    if (!assigned)
                    {
                        Undo.RecordObject(disciplines, DialogTitle);
                    }

                    disciplines.SetDefinition(slot, definitions[slot]);
                    assigned = true;
                }
            }

            if (assigned)
            {
                MarkChanged(disciplines);
                report.AppendLine("Gave the Mage the Arcane, Flame and Aegis disciplines.");
            }

            if (!mage.TryGetComponent(out DisciplineCombat combat))
            {
                combat = Undo.AddComponent<DisciplineCombat>(mage);
                combat.Bind(input, disciplines, mage.GetComponent<AbilityCaster>(), mage.GetComponent<Mana>(),
                    mage.GetComponent<Health>(), motor, FindInScene<ThirdPersonCamera>(scene));
                MarkChanged(combat);
                report.AppendLine("Added discipline combat to the Mage: a tap swings the selected discipline's combo, " +
                                  "hold and release fires its charged attack. The Staff Strike and the placeholder bolt " +
                                  "are switched off while it is active.");

                if (mage.GetComponent<AbilityCaster>() == null)
                {
                    report.AppendLine("WARNING: the Mage has no ability caster, so charged attacks cannot be used. " +
                                      "Run Threefold > Setup > Combat.");
                }
            }

            // Sprint 4: the disciplines' aerial abilities (Docs/AerialAbilities.md). Added once; its tuning is kept.
            if (!mage.TryGetComponent(out AerialAbilities aerial))
            {
                aerial = Undo.AddComponent<AerialAbilities>(mage);
                aerial.Bind(motor, input, disciplines, combat, mage.GetComponent<PlayerGuard>(), mage.GetComponent<Health>(),
                    mage.GetComponent<TraversalController>(), FindInScene<ThirdPersonCamera>(scene));
                MarkChanged(aerial);
                report.AppendLine("Added the aerial abilities: in the air, a second press of Jump starts Arcane Ascension, " +
                                  "Flame Fire Glide (hold) or Aegis Force Dash, by the selected discipline.");
            }

            if (mage.TryGetComponent(out PlayerGuard guard) && guard.Health == null)
            {
                Undo.RecordObject(guard, DialogTitle);
                guard.BindBody(mage.GetComponent<Health>(), motor);
                MarkChanged(guard);
                report.AppendLine("Connected the block to the Mage's health: guarded hits from the front are reduced.");
            }

            // LM-06: hazards in the world reach the Mage by explicit rules (heat from fires, a shove from a collapse).
            if (mage.GetComponent<PlayerEffectReceiver>() == null)
            {
                Undo.AddComponent<PlayerEffectReceiver>(mage);
                report.AppendLine("Added the Mage's hazard rules: fire nearby heats the Mage and can set it alight briefly, a " +
                                  "structure coming down shoves it. The Mage's own attacks never reach it.");
            }
        }

        /// <summary>The aim preview, the guard arc and the feedback, on an object of their own beside the Mage.</summary>
        private static void EnsureVisuals(Scene scene, GameObject mage, SharedEffects effects, Material shapeMaterial,
            StringBuilder report)
        {
            DisciplineCombat combat = mage.GetComponent<DisciplineCombat>();
            PlayerGuard guard = mage.GetComponent<PlayerGuard>();
            ThirdPersonCamera rig = FindInScene<ThirdPersonCamera>(scene);

            GameObject root = FindByName(scene, VisualsName);
            if (root == null)
            {
                root = new GameObject(VisualsName);
                Undo.RegisterCreatedObjectUndo(root, DialogTitle);
                report.AppendLine($"Created '{VisualsName}' (aim preview, guard arc and attack feedback).");
            }

            if (!root.TryGetComponent(out ChargeAimPreview preview))
            {
                MeshRenderer area = CreateShape(root.transform, "ChargeArea", shapeMaterial, out MeshFilter areaFilter);
                MeshRenderer reticle = CreateShape(root.transform, "ChargeReticle", shapeMaterial, out MeshFilter reticleFilter);
                preview = Undo.AddComponent<ChargeAimPreview>(root);
                preview.Bind(combat, areaFilter, area, reticleFilter, reticle);
                MarkChanged(preview);
                report.AppendLine("Added the charge aim preview: a reticle for the Arcane Arrow, a ground outline for the " +
                                  "Flame Wave and the Force Pulse.");
            }

            if (!root.TryGetComponent(out GuardVisual guardVisual) && guard != null)
            {
                MeshRenderer arc = CreateShape(root.transform, "GuardArc", shapeMaterial, out MeshFilter arcFilter);
                guardVisual = Undo.AddComponent<GuardVisual>(root);
                guardVisual.Bind(guard, arcFilter, arc);
                MarkChanged(guardVisual);
                report.AppendLine("Added the guard arc shown while blocking.");
            }

            CharacterAnimationDriver driver = mage.GetComponentInChildren<CharacterAnimationDriver>(true);
            if (!root.TryGetComponent(out DisciplineFeedback feedback))
            {
                feedback = Undo.AddComponent<DisciplineFeedback>(root);
                feedback.Bind(combat, rig, driver, guard);
                feedback.BindEffects(effects.Burn, effects.Push, effects.Guard);
                MarkChanged(feedback);
                report.AppendLine("Added attack feedback: swing arcs, impact bursts, embers, a guard spark and light camera shake.");
            }
            else
            {
                Undo.RecordObject(feedback, DialogTitle);
                if (feedback.FillEffects(effects.Burn, effects.Push, effects.Guard))
                {
                    MarkChanged(feedback);
                    report.AppendLine("Reconnected the attack feedback to its regenerated effects.");
                }
            }
        }

        private static MeshRenderer CreateShape(Transform parent, string name, Material material, out MeshFilter filter)
        {
            var shape = new GameObject(name);
            shape.transform.SetParent(parent, false);
            filter = shape.AddComponent<MeshFilter>();
            MeshRenderer shapeRenderer = shape.AddComponent<MeshRenderer>();
            ConfigureShapeRenderer(shapeRenderer, material);
            shapeRenderer.enabled = false;
            Undo.RegisterCreatedObjectUndo(shape, DialogTitle);
            return shapeRenderer;
        }
    }
}
