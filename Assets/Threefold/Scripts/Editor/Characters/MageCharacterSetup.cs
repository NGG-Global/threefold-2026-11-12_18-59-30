using System.Collections.Generic;
using System.IO;
using System.Text;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.CombatAnimation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Replaces the Mage's placeholder capsule with the character model. It generates the
    /// character's materials, its combat animation set, its Animator Controller (locomotion,
    /// traversal, the combat layers built from the set, and the casting gesture) and a visual
    /// prefab, puts that prefab under the Mage, hides the capsule, and connects the animation driver.
    ///
    /// It runs as part of the scene setup commands and the automated build. It is safe to run
    /// repeatedly: existing assets and an existing visual are kept. To regenerate them with
    /// defaults, delete Assets/Threefold/Generated/Characters/Mage and the Mage's "MageVisual" child.
    ///
    /// If the model file is missing, nothing changes and the capsule stays.
    /// </summary>
    internal static class MageCharacterSetup
    {
        private const string DialogTitle = "Mage Character Setup";
        private const string Folder = "Assets/Threefold/Generated/Characters/Mage";
        private const string ControllerPath = Folder + "/MageAnimator.controller";
        private const string MaskPath = Folder + "/MageUpperBody.mask";
        private const string CombatMaskPath = Folder + "/MageCombatUpperBody.mask";
        internal const string CombatAnimationSetPath = Folder + "/MageCombatAnimations.asset";
        private const string PrefabPath = Folder + "/MageVisual.prefab";
        private const string VisualName = "MageVisual";
        private const string ModelChildName = "Model";
        private const string MaterialPrefix = "MAGE_";
        private const float ExpectedHeight = 1.8f;

        // Raised whenever the controller's layout changes, so existing controllers are rebuilt. The combat
        // animation set's signature is stored with it, so changing a clip in the set rebuilds it too.
        // 3: the combat layers (Docs/CombatAnimationArchitecture.md).
        private const string ControllerVersion = "threefold-mage-controller-3";
        private const float HardLandingSpeed = 13f;

        private const string LitShader = "Universal Render Pipeline/Lit";

        // Same palette as Tools/blender/build_mage.py, as sRGB.
        private static readonly Dictionary<string, Color> Palette = new Dictionary<string, Color>
        {
            { "Skin", new Color(0.80f, 0.63f, 0.52f) },
            { "Tunic", new Color(0.24f, 0.35f, 0.40f) },
            { "Trousers", new Color(0.27f, 0.22f, 0.17f) },
            { "Boots", new Color(0.33f, 0.21f, 0.13f) },
            { "Leather", new Color(0.45f, 0.30f, 0.17f) },
            { "Gloves", new Color(0.20f, 0.15f, 0.12f) },
            { "Cloak", new Color(0.53f, 0.27f, 0.18f) },
            { "Metal", new Color(0.62f, 0.50f, 0.29f) },
            { "Crystal", new Color(0.55f, 0.78f, 1.00f) },
        };

        // Placeholder parts created by the grey-box setup, hidden once the model is in place.
        private static readonly string[] PlaceholderNames = { "Visual", "FacingIndicator" };

        /// <summary>The palette key of a material, from its name in the model ("MAGE_Cloak" gives "Cloak").</summary>
        internal static string MaterialKey(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
            {
                return materialName;
            }

            string name = materialName.Replace(" (Instance)", string.Empty).Trim();
            return name.StartsWith(MaterialPrefix) ? name.Substring(MaterialPrefix.Length) : name;
        }

        internal static void Ensure(Scene scene, GameObject mage, StringBuilder report)
        {
            Transform existing = mage.transform.Find(VisualName);

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModelPostprocessor.MageModelPath);
            if (model == null)
            {
                if (existing == null)
                {
                    report.AppendLine($"The character model '{CharacterModelPostprocessor.MageModelPath}' was not found, " +
                                      "so the Mage keeps its placeholder capsule.");
                }

                return;
            }

            Avatar avatar = LoadSubAsset<Avatar>(CharacterModelPostprocessor.MageModelPath);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                report.AppendLine("WARNING: the Mage model has no valid Humanoid avatar, so it was not attached. " +
                                  "Check the model's Rig settings and the Console.");
                return;
            }

            EnsureFolder(Folder);
            Dictionary<string, Material> materials = EnsureMaterials(report);
            CombatAnimationSet combatAnimations = EnsureCombatAnimationSet(report);
            AnimatorController controller = EnsureController(report, combatAnimations);

            GameObject visual;
            if (existing != null)
            {
                visual = existing.gameObject;
                report.AppendLine($"Kept the existing '{VisualName}'.");
            }
            else
            {
                GameObject prefab = EnsurePrefab(model, avatar, controller, materials, report);
                if (prefab == null)
                {
                    return;
                }

                visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, mage.transform);
                visual.name = VisualName;
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;
                Undo.RegisterCreatedObjectUndo(visual, DialogTitle);
                report.AppendLine("Attached the Mage character model.");
            }

            HidePlaceholder(mage, report);
            ConnectDriver(mage, visual, report);
        }

        // ------------------------------------------------------------------ assets

        private static Dictionary<string, Material> EnsureMaterials(StringBuilder report)
        {
            string folder = Folder + "/Materials";
            EnsureFolder(folder);

            var materials = new Dictionary<string, Material>();
            foreach (KeyValuePair<string, Color> entry in Palette)
            {
                string path = $"{folder}/Mage_{entry.Key}.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null)
                {
                    material = CreateMaterial(entry.Key, entry.Value);
                    AssetDatabase.CreateAsset(material, path);
                    report.AppendLine($"Generated material 'Mage_{entry.Key}'.");
                }

                materials[entry.Key] = material;
            }

            return materials;
        }

        /// <summary>Soft, low-gloss materials: the art direction asks for painted surfaces, not glossy ones.</summary>
        private static Material CreateMaterial(string key, Color color)
        {
            Shader shader = Shader.Find(LitShader);
            if (shader == null)
            {
                Debug.LogWarning($"[Threefold] Shader '{LitShader}' was not found; the Mage's materials use a fallback.");
                shader = Shader.Find("Sprites/Default");
            }

            var material = new Material(shader) { color = color, enableInstancing = true };
            SetFloat(material, "_Smoothness", 0.12f);

            if (key == "Metal")
            {
                SetFloat(material, "_Metallic", 0.5f);
                SetFloat(material, "_Smoothness", 0.35f);
            }

            if (key == "Crystal" && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.6f);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            return material;
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        /// <summary>
        /// Locomotion blended by speed, a jump and fall sequence, the combat layers built from the combat
        /// animation set, and an upper-body layer for the casting gesture, so the Mage can cast while running.
        /// </summary>
        private static AnimatorController EnsureController(StringBuilder report, CombatAnimationSet combatAnimations)
        {
            string version = ControllerVersion + (combatAnimations != null ? ":" + combatAnimations.ControllerSignature : string.Empty);
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (existing != null && GetVersion(ControllerPath) == version)
            {
                return existing;
            }

            Dictionary<string, AnimationClip> clips = LoadModelClips();

            AnimationClip Clip(string name)
            {
                if (!clips.TryGetValue(name, out AnimationClip clip))
                {
                    report.AppendLine($"WARNING: the Mage model has no '{name}' animation.");
                }

                return clip;
            }

            AnimatorController controller;
            if (existing == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }
            else
            {
                // Rebuilt in place, so the prefab and scenes that use it keep their reference.
                controller = existing;
                ClearController(controller);
                controller.AddLayer("Base Layer");
                report.AppendLine("Rebuilt the Mage's Animator Controller with the new layout.");
            }

            controller.AddParameter(CharacterAnimationDriver.SpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(CharacterAnimationDriver.GroundedParameter, AnimatorControllerParameterType.Bool);
            controller.AddParameter(CharacterAnimationDriver.VerticalSpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(CharacterAnimationDriver.JumpParameter, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(CharacterAnimationDriver.CastParameter, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(CharacterAnimationDriver.LandImpactParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(CharacterAnimationDriver.HangingParameter, AnimatorControllerParameterType.Bool);
            controller.AddParameter(CharacterAnimationDriver.ShimmyParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(CharacterAnimationDriver.ClimbParameter, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(CharacterAnimationDriver.VaultParameter, AnimatorControllerParameterType.Trigger);

            // Grounded starts true so the first frame does not play a fall.
            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == CharacterAnimationDriver.GroundedParameter)
                {
                    parameters[i].defaultBool = true;
                }
            }

            controller.parameters = parameters;

            // ---- base layer: locomotion, jump, fall, land
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = CharacterAnimationDriver.SpeedParameter;
            tree.useAutomaticThresholds = false;

            // Thresholds in metres per second. The motor walks at 4 and sprints at 7.
            AddChild(tree, Clip("Idle"), 0f);
            AddChild(tree, Clip("Walk"), 1.5f);
            AddChild(tree, Clip("Jog"), 4f);
            AddChild(tree, Clip("Sprint"), 7f);
            machine.defaultState = locomotion;

            AnimatorState jumpStart = AddState(machine, "JumpStart", Clip("JumpStart"));
            AnimatorState fall = AddState(machine, "Fall", Clip("JumpLoop"));
            AnimatorState land = AddState(machine, "Land", Clip("JumpLand"));

            // ---- traversal: hanging, climbing up or onto, vaulting
            AnimatorState hang = AddState(machine, "Hang", Clip("Hang"));
            AnimatorState climb = AddState(machine, "Climb", Clip("ClimbUp"));
            climb.speed = 1.2f;
            AnimatorState vault = AddState(machine, "Vault", Clip("ClimbUp"));
            vault.speed = 1.9f;

            AnimatorStateTransition toHang = machine.AddAnyStateTransition(hang);
            toHang.AddCondition(AnimatorConditionMode.If, 0f, CharacterAnimationDriver.HangingParameter);
            toHang.canTransitionToSelf = false;
            Configure(toHang, false, 0f, 0.1f);

            AnimatorStateTransition toClimb = machine.AddAnyStateTransition(climb);
            toClimb.AddCondition(AnimatorConditionMode.If, 0f, CharacterAnimationDriver.ClimbParameter);
            toClimb.canTransitionToSelf = false;
            Configure(toClimb, false, 0f, 0.08f);

            AnimatorStateTransition toVault = machine.AddAnyStateTransition(vault);
            toVault.AddCondition(AnimatorConditionMode.If, 0f, CharacterAnimationDriver.VaultParameter);
            toVault.canTransitionToSelf = false;
            Configure(toVault, false, 0f, 0.05f);

            AnimatorStateTransition letGo = hang.AddTransition(fall);
            letGo.AddCondition(AnimatorConditionMode.IfNot, 0f, CharacterAnimationDriver.HangingParameter);
            Configure(letGo, false, 0f, 0.1f);

            AnimatorStateTransition climbDone = climb.AddTransition(locomotion);
            Configure(climbDone, true, 0.92f, 0.12f);

            AnimatorStateTransition vaultDone = vault.AddTransition(locomotion);
            Configure(vaultDone, true, 0.9f, 0.1f);

            AnimatorStateTransition toJump = machine.AddAnyStateTransition(jumpStart);
            toJump.AddCondition(AnimatorConditionMode.If, 0f, CharacterAnimationDriver.JumpParameter);
            Configure(toJump, false, 0f, 0.05f);
            toJump.canTransitionToSelf = false;

            AnimatorStateTransition startToFall = jumpStart.AddTransition(fall);
            Configure(startToFall, true, 0.8f, 0.1f);

            AnimatorStateTransition startToLand = jumpStart.AddTransition(land);
            startToLand.AddCondition(AnimatorConditionMode.If, 0f, CharacterAnimationDriver.GroundedParameter);
            Configure(startToLand, false, 0f, 0.05f);

            // Walking off a ledge: only once really falling, so steps and slopes do not trigger it.
            AnimatorStateTransition walkOff = locomotion.AddTransition(fall);
            walkOff.AddCondition(AnimatorConditionMode.IfNot, 0f, CharacterAnimationDriver.GroundedParameter);
            walkOff.AddCondition(AnimatorConditionMode.Less, -2f, CharacterAnimationDriver.VerticalSpeedParameter);
            Configure(walkOff, false, 0f, 0.15f);

            AnimatorStateTransition fallToLand = fall.AddTransition(land);
            fallToLand.AddCondition(AnimatorConditionMode.If, 0f, CharacterAnimationDriver.GroundedParameter);
            Configure(fallToLand, false, 0f, 0.05f);

            // Keep running out of a landing; otherwise let the landing play.
            // A hard landing always plays out; a normal one can be run out of.
            AnimatorStateTransition landMoving = land.AddTransition(locomotion);
            landMoving.AddCondition(AnimatorConditionMode.Greater, 1f, CharacterAnimationDriver.SpeedParameter);
            landMoving.AddCondition(AnimatorConditionMode.Less, HardLandingSpeed, CharacterAnimationDriver.LandImpactParameter);
            Configure(landMoving, false, 0f, 0.1f);

            AnimatorStateTransition landDone = land.AddTransition(locomotion);
            Configure(landDone, true, 0.6f, 0.15f);

            // ---- combat layers: stance, full body, upper body
            int combatStates = 0;
            if (combatAnimations != null)
            {
                AvatarMask combatMask = EnsureCombatUpperBodyMask(combatAnimations.Library.tuning);
                combatStates = CombatAnimatorBuilder.AddCombatLayers(controller, combatAnimations.Library, clips, combatMask, report);
            }

            // ---- upper-body layer: casting (the gesture used when the combat layers are not playing)
            AvatarMask mask = EnsureUpperBodyMask();
            controller.AddLayer(CharacterAnimationDriver.CastLayer);
            AnimatorControllerLayer[] layers = controller.layers;
            int castIndex = layers.Length - 1;
            layers[castIndex].defaultWeight = 1f;
            layers[castIndex].avatarMask = mask;
            layers[castIndex].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            AnimatorStateMachine castMachine = controller.layers[castIndex].stateMachine;
            AnimatorState empty = castMachine.AddState("Empty");
            castMachine.defaultState = empty;
            AnimatorState cast = AddState(castMachine, "SpellShoot", Clip("SpellShoot"));

            AnimatorStateTransition toCast = castMachine.AddAnyStateTransition(cast);
            toCast.AddCondition(AnimatorConditionMode.If, 0f, CharacterAnimationDriver.CastParameter);
            toCast.canTransitionToSelf = true; // rapid casts restart the gesture
            Configure(toCast, false, 0f, 0.05f);

            AnimatorStateTransition castDone = cast.AddTransition(empty);
            Configure(castDone, true, 0.85f, 0.15f);

            EditorUtility.SetDirty(controller);
            SetVersion(ControllerPath, version);
            report.AppendLine("Generated the Mage's Animator Controller (locomotion, jump, fall, land, hang, climb, " +
                              $"vault, combat layers with {combatStates} states, and an upper-body cast layer).");
            return controller;
        }

        /// <summary>The model's animation clips by name.</summary>
        private static Dictionary<string, AnimationClip> LoadModelClips()
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(CharacterModelPostprocessor.MageModelPath))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    clips[clip.name] = clip;
                }
            }

            return clips;
        }

        internal static CombatAnimationSet LoadCombatAnimationSet()
        {
            return AssetDatabase.LoadAssetAtPath<CombatAnimationSet>(CombatAnimationSetPath);
        }

        /// <summary>
        /// The Mage's combat animation set: created from <see cref="CombatAnimationDefaults"/> if it does not
        /// exist, and otherwise kept as tuned. Only the clip lengths are brought up to date from the model,
        /// because they are measurements, not choices.
        /// </summary>
        internal static CombatAnimationSet EnsureCombatAnimationSet(StringBuilder report)
        {
            EnsureFolder(Folder);
            CombatAnimationSet set = LoadCombatAnimationSet();
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<CombatAnimationSet>();
                set.Configure(CombatAnimationDefaults.CreateMage(), CombatAnimationDefaults.DataVersion);
                AssetDatabase.CreateAsset(set, CombatAnimationSetPath);
                report.AppendLine("Generated the Mage's combat animation set 'MageCombatAnimations' (three disciplines, " +
                                  "block, stagger, aerial and defeat slots).");
            }

            Dictionary<string, AnimationClip> clips = LoadModelClips();
            int updated = 0;
            set.Library.ForEachSlot((id, slot) =>
            {
                if (slot != null && slot.IsAssigned && clips.TryGetValue(slot.clip, out AnimationClip clip) &&
                    Mathf.Abs(clip.length - slot.clipSeconds) > 0.001f)
                {
                    slot.clipSeconds = clip.length;
                    updated++;
                }
            });

            if (updated > 0)
            {
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssetIfDirty(set);
                report.AppendLine($"Updated {updated} clip length(s) in the combat animation set from the model.");
            }

            return set;
        }

        /// <summary>
        /// Brings the combat animation set and the Animator Controller up to date, without touching any scene.
        /// Returns false if the model is missing or is not Humanoid.
        /// </summary>
        internal static bool ApplyCombatAnimations(StringBuilder report)
        {
            Avatar avatar = LoadSubAsset<Avatar>(CharacterModelPostprocessor.MageModelPath);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                report.AppendLine($"The model '{CharacterModelPostprocessor.MageModelPath}' is missing or has no valid Humanoid " +
                                  "avatar, so nothing was changed.");
                return false;
            }

            CombatAnimationSet set = EnsureCombatAnimationSet(report);
            EnsureController(report, set);
            return true;
        }

        private static AvatarMask EnsureCombatUpperBodyMask(CombatAnimationTuning tuning)
        {
            bool torso = tuning == null || tuning.upperBodyIncludesTorso;
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(CombatMaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                CombatAnimatorBuilder.ConfigureUpperBodyMask(mask, torso);
                AssetDatabase.CreateAsset(mask, CombatMaskPath);
                return mask;
            }

            // Derived from the set, so it follows the set's torso choice.
            CombatAnimatorBuilder.ConfigureUpperBodyMask(mask, torso);
            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static void ClearController(AnimatorController controller)
        {
            while (controller.layers.Length > 0)
            {
                controller.RemoveLayer(controller.layers.Length - 1);
            }

            while (controller.parameters.Length > 0)
            {
                controller.RemoveParameter(0);
            }
        }

        private static string GetVersion(string path)
        {
            AssetImporter importer = AssetImporter.GetAtPath(path);
            return importer != null ? importer.userData : null;
        }

        private static void SetVersion(string path, string version)
        {
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer != null && importer.userData != version)
            {
                importer.userData = version;
                EditorUtility.SetDirty(importer);
                AssetDatabase.WriteImportSettingsIfDirty(path);
            }
        }

        private static AvatarMask EnsureUpperBodyMask()
        {
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask != null)
            {
                return mask;
            }

            mask = new AvatarMask();
            for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
            {
                var bodyPart = (AvatarMaskBodyPart)part;
                bool upper = bodyPart == AvatarMaskBodyPart.Head
                             || bodyPart == AvatarMaskBodyPart.LeftArm || bodyPart == AvatarMaskBodyPart.RightArm
                             || bodyPart == AvatarMaskBodyPart.LeftFingers || bodyPart == AvatarMaskBodyPart.RightFingers;
                mask.SetHumanoidBodyPartActive(bodyPart, upper);
            }

            AssetDatabase.CreateAsset(mask, MaskPath);
            return mask;
        }

        private static void AddChild(BlendTree tree, AnimationClip clip, float threshold)
        {
            if (clip != null)
            {
                tree.AddChild(clip, threshold);
            }
        }

        private static AnimatorState AddState(AnimatorStateMachine machine, string name, AnimationClip clip)
        {
            AnimatorState state = machine.AddState(name);
            state.motion = clip;
            return state;
        }

        private static void Configure(AnimatorStateTransition transition, bool hasExitTime, float exitTime, float duration)
        {
            transition.hasExitTime = hasExitTime;
            transition.exitTime = exitTime;
            transition.hasFixedDuration = true;
            transition.duration = duration;
        }

        /// <summary>
        /// The model inside an empty root. The root is what the Mage holds; the inner object may be
        /// turned or scaled to correct the model, without affecting the Mage.
        /// </summary>
        private static GameObject EnsurePrefab(GameObject model, Avatar avatar, AnimatorController controller,
            Dictionary<string, Material> materials, StringBuilder report)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                return existing;
            }

            var root = new GameObject(VisualName);
            var inner = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            inner.name = ModelChildName;
            inner.transform.localPosition = Vector3.zero;
            inner.transform.localRotation = Quaternion.identity;

            if (!inner.TryGetComponent(out Animator animator))
            {
                animator = inner.AddComponent<Animator>();
            }

            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            foreach (SkinnedMeshRenderer renderer in inner.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Material[] shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    if (shared[i] != null && materials.TryGetValue(MaterialKey(shared[i].name), out Material material))
                    {
                        shared[i] = material;
                    }
                }

                renderer.sharedMaterials = shared;
            }

            CorrectFacingAndSize(inner, animator, report);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            if (prefab == null)
            {
                report.AppendLine($"WARNING: the prefab '{PrefabPath}' could not be saved.");
                return null;
            }

            report.AppendLine("Generated the 'MageVisual' prefab.");
            return prefab;
        }

        /// <summary>
        /// Turns the model to face forward and scales it to the character's height if the exported
        /// file does not match Unity's conventions. Reports every correction it makes.
        /// </summary>
        private static void CorrectFacingAndSize(GameObject inner, Animator animator, StringBuilder report)
        {
            Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform toes = animator.GetBoneTransform(HumanBodyBones.LeftToes);

            if (foot != null && toes != null)
            {
                Vector3 pointing = toes.position - foot.position;
                pointing.y = 0f;

                if (pointing.sqrMagnitude > 1e-6f && Vector3.Dot(pointing.normalized, Vector3.forward) < -0.5f)
                {
                    inner.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    report.AppendLine("Turned the Mage model by 180 degrees so it faces forward.");
                }
            }

            var bounds = new Bounds();
            bool any = false;
            foreach (SkinnedMeshRenderer renderer in inner.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null)
                {
                    continue;
                }

                Bounds local = renderer.sharedMesh.bounds;
                Vector3 size = Vector3.Scale(local.size, renderer.transform.lossyScale);
                Bounds world = new Bounds(renderer.transform.TransformPoint(local.center), size);

                if (any)
                {
                    bounds.Encapsulate(world);
                }
                else
                {
                    bounds = world;
                    any = true;
                }
            }

            float height = Mathf.Max(bounds.size.y, Mathf.Max(bounds.size.x, bounds.size.z));
            if (any && height > 0.01f && (height < 1.5f || height > 2.1f))
            {
                float factor = ExpectedHeight / height;
                inner.transform.localScale = Vector3.one * factor;
                report.AppendLine($"Scaled the Mage model by {factor:0.###} (its size was {height:0.##} m).");
            }
        }

        // ------------------------------------------------------------------- scene

        private static void HidePlaceholder(GameObject mage, StringBuilder report)
        {
            foreach (string name in PlaceholderNames)
            {
                Transform part = mage.transform.Find(name);
                if (part != null && part.gameObject.activeSelf)
                {
                    Undo.RecordObject(part.gameObject, DialogTitle);
                    part.gameObject.SetActive(false);
                    report.AppendLine($"Hid the placeholder '{name}' (kept in the scene, not deleted).");
                }
            }
        }

        private static void ConnectDriver(GameObject mage, GameObject visual, StringBuilder report)
        {
            Animator animator = visual.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                report.AppendLine($"WARNING: '{VisualName}' has no Animator, so the Mage will not animate.");
                return;
            }

            if (!mage.TryGetComponent(out CharacterAnimationDriver driver))
            {
                driver = Undo.AddComponent<CharacterAnimationDriver>(mage);
                report.AppendLine("Added the animation driver to the Mage.");
            }

            if (driver.Animator != animator)
            {
                Undo.RecordObject(driver, DialogTitle);
                driver.Bind(mage.GetComponent<ThirdPersonMotor>(), animator, mage.GetComponent<AbilityCaster>());
                MarkChanged(driver);
            }
        }

        private static T LoadSubAsset<T>(string path) where T : Object
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is T typed)
                {
                    return typed;
                }
            }

            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
