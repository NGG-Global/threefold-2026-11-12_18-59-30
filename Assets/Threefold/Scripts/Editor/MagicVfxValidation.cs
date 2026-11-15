using System.Collections.Generic;
using System.Text;
using Threefold.Combat;
using Threefold.CombatFeedback;
using Threefold.Magic;
using Threefold.Pooling;
using UnityEditor;
using UnityEngine;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Checks every magical combat effect the disciplines and the world feedback use against the Android budget
    /// in <see cref="MagicVfxBudget"/>: no lights, collisions, audio, sub-emitters, missing or Built-in materials;
    /// few systems and particles; short bursts. It reads the prefabs and changes nothing. It also lists pack
    /// textures the effects use that import larger than 512 px on Android.
    /// </summary>
    public static class MagicVfxValidation
    {
        private const string MenuPath = "Threefold/Validate/Magic VFX";
        private const string TextureMenuPath = "Threefold/Setup/Limit VFX Pack Textures for Android";
        private const int AndroidTextureLimit = 512;

        [MenuItem(MenuPath, false, 30)]
        public static void Run()
        {
            var problems = new List<string>();
            int checkedCount = Validate(problems, out int largeTextures);
            var report = new StringBuilder();
            report.AppendLine($"Checked {checkedCount} magical effect prefab(s).");
            if (problems.Count == 0)
            {
                report.AppendLine("Every effect is within the Android budget.");
            }
            else
            {
                report.AppendLine($"{problems.Count} problem(s):");
                foreach (string problem in problems)
                {
                    report.AppendLine("- " + problem);
                }
            }

            if (largeTextures > 0)
            {
                report.AppendLine($"{largeTextures} pack texture(s) used by the effects import larger than {AndroidTextureLimit} px " +
                                  $"on Android. '{TextureMenuPath}' limits them.");
            }

            string text = report.ToString().TrimEnd();
            if (problems.Count == 0)
            {
                Debug.Log("[Threefold] " + text);
            }
            else
            {
                Debug.LogWarning("[Threefold] " + text);
            }

            EditorUtility.DisplayDialog("Magic VFX validation", text.Length > 1800 ? text.Substring(0, 1800) + "\n…(see the Console)" : text, "OK");
        }

        [MenuItem(TextureMenuPath, false, 5)]
        public static void LimitTextures()
        {
            if (!EditorUtility.DisplayDialog("Limit VFX pack textures",
                    $"Sets an Android import limit of {AndroidTextureLimit} px on the pack textures that the adapted magic " +
                    "effects use, where no Android setting exists yet. This changes those textures' .meta files (import " +
                    "settings only, not the images or the packs' prefabs). Continue?", "Limit", "Cancel"))
            {
                return;
            }

            var report = new StringBuilder();
            MagicVfxPackAdapter.LimitAndroidTextures(AndroidTextureLimit, report);
            Debug.Log("[Threefold] " + report.ToString().TrimEnd());
            EditorUtility.DisplayDialog("Limit VFX pack textures", report.ToString().TrimEnd() +
                "\n\nCommit the changed .meta files so the limit reaches every build.", "OK");
        }

        /// <summary>Checks the effects. Returns how many prefabs were checked.</summary>
        internal static int Validate(List<string> problems, out int largeTextures)
        {
            var roles = new Dictionary<PooledObject, MagicVfxRole>();
            CollectFromDisciplines(roles);
            CollectFromFolder(MagicVfxPrefabs.Folder, roles);
            CollectFromFolder(MagicVfxPackAdapter.Folder, roles);

            var textures = new HashSet<string>();
            foreach (KeyValuePair<PooledObject, MagicVfxRole> pair in roles)
            {
                MagicVfxSummary summary = Summarise(pair.Key.gameObject, pair.Value);
                MagicVfxBudget.Check(summary, problems);

                foreach (string dependency in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(pair.Key), true))
                {
                    if (MagicVfxPackAdapter.IsPackAsset(dependency) && AssetImporter.GetAtPath(dependency) is TextureImporter)
                    {
                        textures.Add(dependency);
                    }
                }
            }

            largeTextures = 0;
            foreach (string path in textures)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                int size = android.overridden ? android.maxTextureSize : importer.maxTextureSize;
                if (size > AndroidTextureLimit)
                {
                    largeTextures++;
                }
            }

            return roles.Count;
        }

        /// <summary>Counts what an effect prefab contains.</summary>
        internal static MagicVfxSummary Summarise(GameObject root, MagicVfxRole role)
        {
            var summary = new MagicVfxSummary
            {
                Name = root.name,
                Role = role,
                Pooled = root.TryGetComponent(out PooledObject _),
            };

            foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystemRenderer particleRenderer = system.GetComponent<ParticleSystemRenderer>();
                bool live = system.emission.enabled || system.GetComponent<ArcEmitterEffect>() != null ||
                            system.GetComponentInParent<ArcEmitterEffect>() != null ||
                            system.GetComponentInParent<InflowEffect>() != null;
                if (!live || particleRenderer == null || !particleRenderer.enabled)
                {
                    continue;
                }

                ParticleSystem.MainModule main = system.main;
                summary.ParticleSystems++;
                summary.MaxParticles += main.maxParticles;
                summary.Collisions += system.collision.enabled ? 1 : 0;
                summary.Lights += system.lights.enabled ? 1 : 0;
                summary.SubEmitters += system.subEmitters.enabled && system.subEmitters.subEmittersCount > 0 ? 1 : 0;

                ParticleSystem.MinMaxCurve life = main.startLifetime;
                float lifetime = life.mode == ParticleSystemCurveMode.TwoConstants ? life.constantMax : life.constant;
                summary.Seconds = Mathf.Max(summary.Seconds, (main.loop ? 0f : main.duration) + lifetime);
            }

            // A pooled one-off is returned to its pool at its lifetime, whatever its particle systems would do.
            if (root.TryGetComponent(out PooledEffect pooledEffect) && !pooledEffect.IsLooping)
            {
                summary.Seconds = pooledEffect.Lifetime;
            }

            summary.Lights += root.GetComponentsInChildren<Light>(true).Length;
            summary.AudioSources = root.GetComponentsInChildren<AudioSource>(true).Length;
            summary.TrailRenderers = root.GetComponentsInChildren<TrailRenderer>(true).Length;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (renderer is ParticleSystemRenderer && i == 1 &&
                        !(renderer.TryGetComponent(out ParticleSystem owner) && owner.trails.enabled))
                    {
                        continue;
                    }

                    if (MagicVfxPackAdapter.IsBroken(materials[i]))
                    {
                        summary.MissingMaterials++;
                    }
                    else if (MagicVfxPackAdapter.IsBuiltInShader(materials[i].shader))
                    {
                        summary.BuiltInShaders++;
                    }
                }

                if (materials.Length == 0)
                {
                    summary.MissingMaterials++;
                }
            }

            return summary;
        }

        private static void CollectFromDisciplines(Dictionary<PooledObject, MagicVfxRole> roles)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:DisciplineDefinition"))
            {
                var definition = AssetDatabase.LoadAssetAtPath<DisciplineDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition == null)
                {
                    continue;
                }

                DisciplinePresentation look = definition.Presentation;
                Add(roles, look.meleeImpactEffect, MagicVfxRole.Burst);
                Add(roles, look.meleeSwingEffect, MagicVfxRole.Burst);
                Add(roles, look.releaseEffect, MagicVfxRole.Burst);
                Add(roles, look.deflectEffect, MagicVfxRole.Burst);
                Add(roles, look.contactEffect, MagicVfxRole.Burst);
                Add(roles, look.heavyContactEffect, MagicVfxRole.Burst);
                Add(roles, look.lingerEffect, MagicVfxRole.Burst);
                Add(roles, look.chargeLoopEffect, MagicVfxRole.Attached);
                Add(roles, look.limbTrailEffect, MagicVfxRole.Attached);
                Add(roles, look.gatherEffect, MagicVfxRole.Attached);
                Add(roles, look.projectileTrailEffect, MagicVfxRole.Attached);
                Add(roles, look.areaWaveEffect, MagicVfxRole.Area);
                Add(roles, look.blockBarrierEffect, MagicVfxRole.Barrier);

                if (definition.ChargedAbility is ChargedProjectileDefinition shot)
                {
                    Add(roles, shot.ImpactEffectPrefab, MagicVfxRole.Burst);
                }
                else if (definition.ChargedAbility is ChargedAreaDefinition area)
                {
                    Add(roles, area.ImpactEffectPrefab, MagicVfxRole.Burst);
                }
            }
        }

        private static void CollectFromFolder(string folder, Dictionary<PooledObject, MagicVfxRole> roles)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null && prefab.TryGetComponent(out PooledObject pooled) && !roles.ContainsKey(pooled))
                {
                    roles[pooled] = RoleByName(prefab.name);
                }
            }
        }

        private static MagicVfxRole RoleByName(string name)
        {
            if (name.StartsWith("World"))
            {
                return MagicVfxRole.World;
            }

            if (name.Contains("Barrier"))
            {
                return MagicVfxRole.Barrier;
            }

            if (name.Contains("Wave") || name.Contains("Pulse"))
            {
                return MagicVfxRole.Area;
            }

            if (name.Contains("Trail") || name.Contains("Gather") || name.Contains("Charge") || name.Contains("Ribbon"))
            {
                return MagicVfxRole.Attached;
            }

            return MagicVfxRole.Burst;
        }

        private static void Add(Dictionary<PooledObject, MagicVfxRole> roles, PooledObject effect, MagicVfxRole role)
        {
            if (effect != null && !roles.ContainsKey(effect))
            {
                roles[effect] = role;
            }
        }
    }
}
