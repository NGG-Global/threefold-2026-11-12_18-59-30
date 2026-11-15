using System.Collections.Generic;
using System.IO;
using System.Text;
using Threefold.CombatFeedback;
using Threefold.Pooling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Makes project-owned, Android-ready copies of effects from the installed VFX packs. The pack's own prefab
    /// is never changed: it is instantiated, unpacked completely (so the copy no longer depends on it), adapted
    /// and saved under Generated/VFX/Packs. Adapting means:
    ///
    /// - lights, audio sources, heat-haze and distortion children are removed; particle collisions, the Lights
    ///   module and sub-emitters are switched off;
    /// - a renderer whose material is missing, or whose shader does not load, is switched off (it would draw pink);
    /// - Built-in pipeline particle materials (the Hovl pack) are replaced by URP Particles/Unlit copies that keep
    ///   the texture and the blending, with the colour brought down to at most 1.2;
    /// - URP materials with HDR colours brighter than 1.2 (the Free Slash pack, up to about 8.6) are replaced by
    ///   copies with the colours brought down; their Shader Graph shader is kept;
    /// - particle systems beyond the budget of the effect's role are switched off, the rest are capped in particles;
    /// - the effect is scaled, made pooled, and returned to its pool after its longest particle has gone.
    ///
    /// The copies keep using the packs' textures and meshes, which stay where they are.
    /// </summary>
    internal static class MagicVfxPackAdapter
    {
        internal const string Folder = MagicVfxSetup.Folder + "/Packs";
        internal const string MaterialFolder = Folder + "/Materials";
        private const float MaxColour = 1.2f;

        internal static string PathOf(string name)
        {
            return $"{Folder}/{name}.prefab";
        }

        internal static PooledObject Load(string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(name));
            return prefab != null ? prefab.GetComponent<PooledObject>() : null;
        }

        /// <summary>
        /// Adapts a pack prefab into Generated/VFX/Packs/<paramref name="name"/>.prefab unless the copy exists already.
        /// Returns the copy, or null when the pack prefab is not in the project.
        /// </summary>
        internal static PooledObject Adapt(string sourcePath, string name, Color tint, float tintAmount, MagicVfxRole role,
            float scale, StringBuilder report)
        {
            PooledObject existing = Load(name);
            if (existing != null)
            {
                return existing;
            }

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                report.AppendLine($"Pack effect not found, so '{name}' was not made: {sourcePath}");
                return null;
            }

            CombatSetup.EnsureFolder(Folder);
            CombatSetup.EnsureFolder(MaterialFolder);

            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance != null)
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
            else
            {
                instance = Object.Instantiate(source);
            }

            try
            {
                instance.name = name;
                instance.transform.localScale = instance.transform.localScale * scale;
                var changes = new Changes();
                RemoveHeavyParts(instance, changes);
                AdaptSystems(instance, role, tint, tintAmount, changes);

                if (!instance.TryGetComponent(out PooledObject _))
                {
                    instance.AddComponent<PooledObject>();
                }

                if (!instance.TryGetComponent(out PooledEffect effect))
                {
                    effect = instance.AddComponent<PooledEffect>();
                }

                bool held = role == MagicVfxRole.Barrier || role == MagicVfxRole.Attached;
                effect.Bind(null, held ? 30f : Mathf.Min(LongestSeconds(instance), MagicVfxBudget.MaxSeconds(role)), held);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, PathOf(name));
                report.AppendLine($"Adapted '{sourcePath}' into '{PathOf(name)}': {changes}.");
                return saved != null ? saved.GetComponent<PooledObject>() : null;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Sets an Android import limit on the pack textures that the adapted effects use, where none is set and
        /// the texture imports larger. Changes the textures' import settings (their .meta files), not the images.
        /// </summary>
        internal static int LimitAndroidTextures(int maxSize, StringBuilder report)
        {
            var textures = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Folder }))
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                foreach (string dependency in AssetDatabase.GetDependencies(prefabPath, true))
                {
                    if (IsPackAsset(dependency) && AssetImporter.GetAtPath(dependency) is TextureImporter)
                    {
                        textures.Add(dependency);
                    }
                }
            }

            int changed = 0;
            foreach (string path in textures)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                if (android.overridden || importer.maxTextureSize <= maxSize)
                {
                    continue;
                }

                android.overridden = true;
                android.maxTextureSize = maxSize;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
                changed++;
            }

            report.AppendLine($"Android import limit of {maxSize} px set on {changed} of {textures.Count} pack texture(s) used " +
                              "by the adapted effects. Textures that already had an Android setting were left alone.");
            return changed;
        }

        /// <summary>True for assets inside the three installed VFX packs.</summary>
        internal static bool IsPackAsset(string path)
        {
            return path.StartsWith("Assets/Free Slash VFX/") || path.StartsWith("Assets/Hovl Studio/") ||
                   path.StartsWith("Assets/Vefects/");
        }

        /// <summary>True for Built-in Render Pipeline shaders, which URP does not draw.</summary>
        internal static bool IsBuiltInShader(Shader shader)
        {
            if (shader == null)
            {
                return false;
            }

            string name = shader.name;
            return name.StartsWith("Particles/") || name.StartsWith("Legacy Shaders/") || name.StartsWith("Mobile/") ||
                   name == "Standard" || name.StartsWith("Standard (");
        }

        /// <summary>True when the material would draw pink or not at all.</summary>
        internal static bool IsBroken(Material material)
        {
            return material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader" ||
                   !material.shader.isSupported;
        }

        // ------------------------------------------------------------------ steps

        private sealed class Changes
        {
            public int Lights;
            public int Audio;
            public int Removed;
            public int Collisions;
            public int Disabled;
            public int Broken;
            public int Remapped;
            public int Softened;

            public override string ToString()
            {
                return $"{Lights} light(s) and {Audio} audio source(s) removed, {Removed} distortion part(s) removed, " +
                       $"{Collisions} collision module(s) off, {Disabled} system(s) over budget off, {Broken} renderer(s) " +
                       $"with a missing material off, {Remapped} Built-in material(s) remapped to URP, {Softened} HDR " +
                       "material(s) softened";
            }
        }

        private static void RemoveHeavyParts(GameObject root, Changes changes)
        {
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (RemoveLight(light))
                {
                    changes.Lights++;
                }
            }

            foreach (AudioSource audio in root.GetComponentsInChildren<AudioSource>(true))
            {
                Object.DestroyImmediate(audio);
                changes.Audio++;
            }

            Transform[] parts = root.GetComponentsInChildren<Transform>(true);
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                Transform part = parts[i];
                if (part == null || part == root.transform)
                {
                    continue;
                }

                string lower = part.name.ToLowerInvariant();
                if (lower.Contains("distort") || lower.Contains("haze") || lower.Contains("refract"))
                {
                    Object.DestroyImmediate(part.gameObject);
                    changes.Removed++;
                }
            }
        }

        /// <summary>
        /// Removes a light. Unity refuses to remove a component another one requires (URP's additional light data
        /// requires its Light), so those go first; a part that holds nothing but the light goes as a whole.
        /// Returns true only if the light is gone.
        /// </summary>
        internal static bool RemoveLight(Light light)
        {
            GameObject part = light.gameObject;
            Component[] components = part.GetComponents<Component>();
            bool onlyLight = part.transform.childCount == 0;
            foreach (Component component in components)
            {
                if (component is Light || component is Transform)
                {
                    continue;
                }

                if (Requires(component, typeof(Light)))
                {
                    Object.DestroyImmediate(component);
                }
                else
                {
                    onlyLight = false;
                }
            }

            if (onlyLight && part != part.transform.root.gameObject)
            {
                Object.DestroyImmediate(part);
                return true;
            }

            if (light != null)
            {
                Object.DestroyImmediate(light);
            }

            return light == null;
        }

        private static bool Requires(Component component, System.Type required)
        {
            if (component == null)
            {
                return false;
            }

            foreach (object attribute in component.GetType().GetCustomAttributes(typeof(RequireComponent), true))
            {
                var requirement = (RequireComponent)attribute;
                if (requirement.m_Type0 == required || requirement.m_Type1 == required || requirement.m_Type2 == required)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AdaptSystems(GameObject root, MagicVfxRole role, Color tint, float tintAmount, Changes changes)
        {
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            int maxSystems = MagicVfxBudget.MaxSystems(role);
            int perSystem = Mathf.Max(4, MagicVfxBudget.MaxParticles(role) / Mathf.Max(1, Mathf.Min(systems.Length, maxSystems)));
            int kept = 0;

            foreach (ParticleSystem system in systems)
            {
                ParticleSystem.CollisionModule collision = system.collision;
                if (collision.enabled)
                {
                    collision.enabled = false;
                    changes.Collisions++;
                }

                ParticleSystem.LightsModule lights = system.lights;
                lights.enabled = false;
                ParticleSystem.SubEmittersModule subEmitters = system.subEmitters;
                subEmitters.enabled = false;

                ParticleSystemRenderer particleRenderer = system.GetComponent<ParticleSystemRenderer>();
                bool drawn = particleRenderer != null && particleRenderer.enabled &&
                             particleRenderer.renderMode != ParticleSystemRenderMode.None;

                if (drawn && !FixMaterials(particleRenderer, tint, tintAmount, changes))
                {
                    SwitchOff(system, particleRenderer);
                    changes.Broken++;
                    continue;
                }

                if (!drawn)
                {
                    // Systems that draw nothing only feed others (sub-emitters, which are now off).
                    SwitchOff(system, particleRenderer);
                    continue;
                }

                if (kept >= maxSystems)
                {
                    SwitchOff(system, particleRenderer);
                    changes.Disabled++;
                    continue;
                }

                kept++;
                ParticleSystem.MainModule main = system.main;
                main.maxParticles = Mathf.Min(main.maxParticles, perSystem);
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (main.startColor.mode == ParticleSystemGradientMode.Color)
                {
                    main.startColor = Color.Lerp(main.startColor.color, tint, tintAmount);
                }

                particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
                particleRenderer.receiveShadows = false;
                particleRenderer.lightProbeUsage = LightProbeUsage.Off;
                particleRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            foreach (Renderer other in root.GetComponentsInChildren<Renderer>(true))
            {
                if (other is ParticleSystemRenderer)
                {
                    continue;
                }

                if (!FixMaterials(other, tint, tintAmount, changes))
                {
                    other.enabled = false;
                    changes.Broken++;
                }

                other.shadowCastingMode = ShadowCastingMode.Off;
                other.receiveShadows = false;
            }
        }

        private static void SwitchOff(ParticleSystem system, ParticleSystemRenderer particleRenderer)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.MainModule main = system.main;
            main.maxParticles = 0;
            if (particleRenderer != null)
            {
                particleRenderer.enabled = false;
            }
        }

        /// <summary>Replaces each material by its adapted copy. False if a material the renderer draws with is missing.</summary>
        private static bool FixMaterials(Renderer renderer, Color tint, float tintAmount, Changes changes)
        {
            Material[] materials = renderer.sharedMaterials;
            bool ok = materials.Length > 0;
            bool changed = false;

            for (int i = 0; i < materials.Length; i++)
            {
                // A particle renderer's second slot is its trail material, used only by the Trails module.
                bool trailSlot = renderer is ParticleSystemRenderer && i == 1;
                if (trailSlot && !TrailsUsed((ParticleSystemRenderer)renderer))
                {
                    continue;
                }

                Material material = materials[i];
                if (IsBroken(material))
                {
                    ok = false;
                    continue;
                }

                Material adapted = material;
                if (IsBuiltInShader(material.shader))
                {
                    adapted = UrpCopy(material, tint, tintAmount);
                    changes.Remapped++;
                }
                else if (PeakColour(material) > MaxColour)
                {
                    adapted = SoftenedCopy(material);
                    changes.Softened++;
                }

                if (adapted != material)
                {
                    materials[i] = adapted;
                    changed = true;
                }
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
            }

            return ok;
        }

        private static bool TrailsUsed(ParticleSystemRenderer renderer)
        {
            return renderer.TryGetComponent(out ParticleSystem system) && system.trails.enabled;
        }

        /// <summary>A URP Particles/Unlit copy of a Built-in particle material: same texture, same blending, softer colour.</summary>
        private static Material UrpCopy(Material source, Color tint, float tintAmount)
        {
            string path = $"{MaterialFolder}/{Safe(source.name)}_URP.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            var material = new Material(CombatSetup.FindShader(CombatSetup.ParticleShader)) { name = source.name + "_URP" };
            Texture texture = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
            if (texture != null && material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", source.GetTextureScale("_MainTex"));
                material.SetTextureOffset("_BaseMap", source.GetTextureOffset("_MainTex"));
            }

            Color colour = source.HasProperty("_Color") ? source.GetColor("_Color")
                : source.HasProperty("_TintColor") ? source.GetColor("_TintColor") : Color.white;
            colour = Limit(colour, MaxColour);
            colour = Color.Lerp(colour, new Color(tint.r, tint.g, tint.b, colour.a), tintAmount * 0.5f);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", colour);
            }

            // Additive when the original adds light (its destination blend is One), otherwise alpha blended.
            bool additive = source.HasProperty("_DstBlend") && Mathf.Approximately(source.GetFloat("_DstBlend"), (float)BlendMode.One);
            CombatSetup.SetFloatIfPresent(material, "_Surface", 1f);
            CombatSetup.SetFloatIfPresent(material, "_Blend", additive ? 2f : 0f);
            CombatSetup.SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            CombatSetup.SetFloatIfPresent(material, "_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            CombatSetup.SetFloatIfPresent(material, "_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>A copy of a pack material with its colours brought down to at most 1.2; same shader.</summary>
        private static Material SoftenedCopy(Material source)
        {
            string path = $"{MaterialFolder}/{Safe(source.name)}_Soft.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            var material = new Material(source) { name = source.name + "_Soft" };
            Shader shader = material.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Color)
                {
                    continue;
                }

                string property = shader.GetPropertyName(i);
                material.SetColor(property, Limit(material.GetColor(property), MaxColour));
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static float PeakColour(Material material)
        {
            float peak = 0f;
            Shader shader = material.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) == ShaderPropertyType.Color)
                {
                    Color colour = material.GetColor(shader.GetPropertyName(i));
                    peak = Mathf.Max(peak, Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b)));
                }
            }

            return peak;
        }

        private static Color Limit(Color colour, float max)
        {
            float peak = Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));
            if (peak <= max || peak <= 0f)
            {
                return colour;
            }

            float scale = max / peak;
            return new Color(colour.r * scale, colour.g * scale, colour.b * scale, colour.a);
        }

        private static float LongestSeconds(GameObject root)
        {
            float longest = 0.3f;
            foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!system.emission.enabled)
                {
                    continue;
                }

                ParticleSystem.MainModule main = system.main;
                ParticleSystem.MinMaxCurve life = main.startLifetime;
                float lifetime = life.mode == ParticleSystemCurveMode.TwoConstants ? life.constantMax : life.constant;
                float delay = main.startDelay.mode == ParticleSystemCurveMode.TwoConstants
                    ? main.startDelay.constantMax
                    : main.startDelay.constant;
                longest = Mathf.Max(longest, delay + (main.loop ? 0f : main.duration * 0.5f) + lifetime);
            }

            return longest;
        }

        private static string Safe(string name)
        {
            foreach (char bad in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(bad, '_');
            }

            return name;
        }
    }
}
