using System.Collections.Generic;
using System.IO;
using System.Text;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.CombatAnimation;
using Threefold.Magic;
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
    ///
    /// The mesh the Mage wears is its look (<see cref="MageLook"/>, Docs/Characters/Mage.md). Mage.fbx always
    /// supplies the animation clips. MageWayfarer.fbx, on the same skeleton, supplies the mesh when Unity has
    /// imported it as the same Humanoid; if it is missing or differs, the mesh inside Mage.fbx is used, as
    /// before, and the report says why. A visual prefab generated with another look is rebuilt in place once,
    /// so scenes that hold it follow.
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
        // 4: the full-body stance layer of the martial styles (Docs/MartialStyles.md).
        private const string ControllerVersion = "threefold-mage-controller-5";

        /// <summary>The upper-body layer of the Fire Glide pose.</summary>
        internal const string AerialLayer = "Aerial";
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

            // The Wayfarer look: one painted atlas, so its colour is white and the texture gives the colours.
            { PaintKey, Color.white },
        };

        /// <summary>The material of the Wayfarer look that carries the painted atlas.</summary>
        internal const string PaintKey = "Paint";

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
            GameObject prefab = EnsurePrefab(model, avatar, controller, materials, report);

            GameObject visual;
            if (existing != null)
            {
                visual = existing.gameObject;
                bool follows = prefab != null && PrefabUtility.GetCorrespondingObjectFromSource(visual) == prefab;
                report.AppendLine(follows
                    ? $"Kept the existing '{VisualName}'."
                    : $"Kept the existing '{VisualName}' as it is: it is not an instance of the generated prefab, so a change " +
                      "of look does not reach it. Delete it and run the setup again to use the generated one.");
            }
            else
            {
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
            MagePersonalitySetup.Ensure(mage, visual, report);
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
                else if (entry.Key == PaintKey && material.mainTexture == null)
                {
                    // Generated before the painted texture had been imported: it would stay white for good.
                    var paint = AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterModelPostprocessor.MagePaintTexturePath);
                    if (paint != null)
                    {
                        material.mainTexture = paint;
                        EditorUtility.SetDirty(material);
                        report.AppendLine("Gave the material 'Mage_Paint' its painted texture, which it lacked.");
                    }
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

            if (key == PaintKey)
            {
                var paint = AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterModelPostprocessor.MagePaintTexturePath);
                if (paint != null)
                {
                    material.mainTexture = paint;
                }
                else
                {
                    Debug.LogWarning($"[Threefold] The Mage's painted texture '{CharacterModelPostprocessor.MagePaintTexturePath}' " +
                                     "was not found; the Wayfarer look would be plain white.");
                }
            }

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
            controller.AddParameter(CharacterAnimationDriver.AerialMoveParameter, AnimatorControllerParameterType.Int);

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

            AddAerialStates(machine, fall, land, Clip);

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

            // ---- upper-body layer: Fire Glide, the arms held out over the falling legs for as long as it propels.
            // A layer of its own: the combat animation director silences the cast layer while it plays.
            // Placeholder clip until a propulsion pose is made (Docs/AerialAbilities.md, "What needs new animation").
            controller.AddLayer(AerialLayer);
            layers = controller.layers;
            int aerialIndex = layers.Length - 1;
            layers[aerialIndex].defaultWeight = 1f;
            layers[aerialIndex].avatarMask = mask;
            layers[aerialIndex].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;

            AnimatorStateMachine aerialMachine = controller.layers[aerialIndex].stateMachine;
            AnimatorState aerialEmpty = aerialMachine.AddState("Empty");
            aerialMachine.defaultState = aerialEmpty;
            AnimatorState glidePose = AddState(aerialMachine, "AerialGlide", Clip("Spell_Simple_Idle_Loop"));
            AnimatorStateTransition toGlide = aerialMachine.AddAnyStateTransition(glidePose);
            toGlide.AddCondition(AnimatorConditionMode.Equals, 2f, CharacterAnimationDriver.AerialMoveParameter);
            toGlide.canTransitionToSelf = false;
            Configure(toGlide, false, 0f, 0.1f);
            AnimatorStateTransition glideDone = glidePose.AddTransition(aerialEmpty);
            glideDone.AddCondition(AnimatorConditionMode.NotEqual, 2f, CharacterAnimationDriver.AerialMoveParameter);
            Configure(glideDone, false, 0f, 0.12f);

            EditorUtility.SetDirty(controller);
            SetVersion(ControllerPath, version);
            report.AppendLine("Generated the Mage's Animator Controller (locomotion, jump, fall, land, hang, climb, " +
                              $"vault, aerial abilities, combat layers with {combatStates} states, an upper-body cast " +
                              "layer and an upper-body aerial layer).");
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

        /// <summary>
        /// The generated MageVisual prefab, created first if it does not exist yet, with the same rules as
        /// <see cref="Ensure"/>: an existing prefab, its look and its materials are kept. For scenes that show
        /// the Mage without the playable rig, such as the main menu. Null if the model cannot be used.
        /// </summary>
        internal static GameObject EnsureVisualPrefab(StringBuilder report)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModelPostprocessor.MageModelPath);
            if (model == null)
            {
                report.AppendLine($"WARNING: the character model '{CharacterModelPostprocessor.MageModelPath}' was not found.");
                return null;
            }

            Avatar avatar = LoadSubAsset<Avatar>(CharacterModelPostprocessor.MageModelPath);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                report.AppendLine("WARNING: the Mage model has no valid Humanoid avatar. Check the model's Rig settings and the Console.");
                return null;
            }

            EnsureFolder(Folder);
            Dictionary<string, Material> materials = EnsureMaterials(report);
            CombatAnimationSet combatAnimations = EnsureCombatAnimationSet(report);
            AnimatorController controller = EnsureController(report, combatAnimations);
            return EnsurePrefab(model, avatar, controller, materials, report);
        }

        /// <summary>The generated MageVisual prefab (model, materials, Animator), or null before the setup has run.</summary>
        internal static GameObject LoadVisualPrefab()
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
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

            var kept = new List<MagicDiscipline>();
            int upgraded = set.Upgrade(kept);
            if (upgraded >= 0)
            {
                EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssetIfDirty(set);
                report.AppendLine($"Updated the combat animation set to the martial styles (Docs/MartialStyles.md): {upgraded} " +
                                  "discipline(s) now use the new stances and strikes.");
                foreach (MagicDiscipline discipline in kept)
                {
                    report.AppendLine($"Kept the {discipline} stance and strikes of the combat animation set as they are, because " +
                                      "they were changed by hand. To use the martial styles, assign the clips listed in " +
                                      "Docs/MartialStyles.md, then run Threefold > Animation > Apply Combat Animation Set.");
                }
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

        /// <summary>
        /// The aerial abilities on the base layer, driven by the AerialMove integer, which gameplay sets every frame:
        /// each pose lasts exactly as long as its ability and returns to the fall (or the landing) when the value
        /// goes back to 0, so no pose can stay stuck. The clips are placeholders from the library until poses are
        /// made for the abilities (Docs/AerialAbilities.md, "What needs new animation").
        /// </summary>
        private static void AddAerialStates(AnimatorStateMachine machine, AnimatorState fall, AnimatorState land,
            System.Func<string, AnimationClip> clip)
        {
            // Arcane Ascension: the shared leap (a gathering crouch, arms rising, the body extending upwards).
            AnimatorState ascend = AddState(machine, "AerialAscend", clip("NinjaJump_Start"));
            ascend.speed = 1.3f;
            AddAerialTransitions(machine, ascend, fall, land, 1);

            // Aegis Force Dash: the two-handed shove of Push_Loop from its thrust (frame 37 of 81), played fast.
            AnimatorState dash = AddState(machine, "AerialDash", clip("Push_Loop"));
            dash.cycleOffset = 37f / 81f;
            dash.speed = 2f;
            AddAerialTransitions(machine, dash, fall, land, 3);
        }

        private static void AddAerialTransitions(AnimatorStateMachine machine, AnimatorState pose, AnimatorState fall,
            AnimatorState land, int move)
        {
            AnimatorStateTransition enter = machine.AddAnyStateTransition(pose);
            enter.AddCondition(AnimatorConditionMode.Equals, move, CharacterAnimationDriver.AerialMoveParameter);
            enter.canTransitionToSelf = false;
            Configure(enter, false, 0f, 0.06f);

            AnimatorStateTransition toLand = pose.AddTransition(land);
            toLand.AddCondition(AnimatorConditionMode.If, 0f, CharacterAnimationDriver.GroundedParameter);
            Configure(toLand, false, 0f, 0.05f);

            AnimatorStateTransition toFall = pose.AddTransition(fall);
            toFall.AddCondition(AnimatorConditionMode.NotEqual, move, CharacterAnimationDriver.AerialMoveParameter);
            Configure(toFall, false, 0f, 0.15f);
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
        /// Why the Wayfarer model cannot be used, or null if it can: it must exist, be a valid Humanoid, and map
        /// every human bone to the same bone as Mage.fbx, whose clips it plays.
        /// </summary>
        internal static string WayfarerProblem(Avatar libraryAvatar, out GameObject model, out Avatar avatar)
        {
            model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModelPostprocessor.MageWayfarerModelPath);
            avatar = model != null ? LoadSubAsset<Avatar>(CharacterModelPostprocessor.MageWayfarerModelPath) : null;
            if (model == null)
            {
                return "the file is missing";
            }

            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                return "Unity did not import it as a valid Humanoid";
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterModelPostprocessor.MagePaintTexturePath) == null)
            {
                return $"its painted texture '{CharacterModelPostprocessor.MagePaintTexturePath}' is missing";
            }

            return MageLookRules.CompareHumanMappings(HumanMapping(libraryAvatar), HumanMapping(avatar));
        }

        private static List<KeyValuePair<string, string>> HumanMapping(Avatar avatar)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            foreach (HumanBone bone in avatar.humanDescription.human)
            {
                pairs.Add(new KeyValuePair<string, string>(bone.humanName, bone.boneName));
            }

            return pairs;
        }

        /// <summary>The look the generated visual prefab holds now, or null before the setup has run.</summary>
        internal static MageLook? CurrentLook()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                return null;
            }

            MageLookRules.ReadStamp(GetVersion(PrefabPath), out MageLook look, out bool _);
            return look;
        }

        /// <summary>
        /// Gives the generated visual prefab a look chosen by hand, which later setup runs keep. Returns false,
        /// with the reason in the report, if that look cannot be used. The caller restyles open scenes.
        /// </summary>
        internal static bool UseLook(MageLook look, StringBuilder report)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModelPostprocessor.MageModelPath);
            Avatar avatar = LoadSubAsset<Avatar>(CharacterModelPostprocessor.MageModelPath);
            if (model == null || avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                report.AppendLine($"The model '{CharacterModelPostprocessor.MageModelPath}' is missing or has no valid Humanoid " +
                                  "avatar, so nothing was changed.");
                return false;
            }

            if (look == MageLook.Wayfarer)
            {
                string problem = WayfarerProblem(avatar, out model, out avatar);
                if (problem != null)
                {
                    report.AppendLine($"The Wayfarer look ('{CharacterModelPostprocessor.MageWayfarerModelPath}') cannot be used: " +
                                      $"{problem}. Nothing was changed.");
                    return false;
                }
            }

            // Already this look: only record that it was chosen. A rebuild would give the model new identities
            // and leave every scene's references to it empty for no gain.
            if (CurrentLook() == look)
            {
                SetVersion(PrefabPath, MageLookRules.Stamp(look, true));
                report.AppendLine($"The Mage already has the {look} look; it is now kept as a choice by hand.");
                return true;
            }

            EnsureFolder(Folder);
            Dictionary<string, Material> materials = EnsureMaterials(report);
            CombatAnimationSet combatAnimations = EnsureCombatAnimationSet(report);
            AnimatorController controller = EnsureController(report, combatAnimations);
            return BuildPrefab(look, true, model, avatar, controller, materials, report) != null;
        }

        /// <summary>
        /// The generated visual prefab with the look the rules decide on (<see cref="MageLookRules.Decide"/>):
        /// created if missing, rebuilt in place if it holds another look, otherwise kept as it is.
        /// </summary>
        private static GameObject EnsurePrefab(GameObject libraryModel, Avatar libraryAvatar, AnimatorController controller,
            Dictionary<string, Material> materials, StringBuilder report)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            string problem = WayfarerProblem(libraryAvatar, out GameObject wayfarerModel, out Avatar wayfarerAvatar);
            MageLookRules.Decision decision = MageLookRules.Decide(existing != null, GetVersion(PrefabPath), problem == null);

            if (problem != null && decision.Look == MageLook.Library && !decision.ChosenByHand)
            {
                report.AppendLine($"{(wayfarerModel != null ? "WARNING: " : string.Empty)}The Wayfarer look " +
                                  $"('{CharacterModelPostprocessor.MageWayfarerModelPath}') is not used: {problem}. " +
                                  "The Mage wears the mesh of Mage.fbx.");
            }

            if (!decision.Build)
            {
                return existing;
            }

            bool wayfarer = decision.Look == MageLook.Wayfarer;
            return BuildPrefab(decision.Look, decision.ChosenByHand, wayfarer ? wayfarerModel : libraryModel,
                wayfarer ? wayfarerAvatar : libraryAvatar, controller, materials, report);
        }

        /// <summary>
        /// The model inside an empty root. The root is what the Mage holds; the inner object may be
        /// turned or scaled to correct the model, without affecting the Mage. An existing prefab is rebuilt in
        /// place: only its "Model" child is replaced, so scenes keep their instance and the root keeps
        /// whatever else was put on it.
        /// </summary>
        private static GameObject BuildPrefab(MageLook look, bool chosenByHand, GameObject model, Avatar avatar,
            AnimatorController controller, Dictionary<string, Material> materials, StringBuilder report)
        {
            bool rebuilding = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            GameObject root = rebuilding ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject(VisualName);
            GameObject prefab;
            try
            {
                Transform previous = root.transform.Find(ModelChildName);
                if (previous != null)
                {
                    Object.DestroyImmediate(previous.gameObject);
                }

                var inner = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                inner.name = ModelChildName;
                inner.transform.SetAsFirstSibling();
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
                prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (rebuilding)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else
                {
                    Object.DestroyImmediate(root);
                }
            }

            if (prefab == null)
            {
                report.AppendLine($"WARNING: the prefab '{PrefabPath}' could not be saved.");
                return null;
            }

            SetVersion(PrefabPath, MageLookRules.Stamp(look, chosenByHand));
            string name = look == MageLook.Wayfarer ? "the Wayfarer look" : "the library look (the mesh of Mage.fbx)";
            report.AppendLine(rebuilding
                ? $"Rebuilt the 'MageVisual' prefab with {name}. Scenes that hold it follow; run each level's setup once, " +
                  "so the Mage's animation driver and painted materials are connected to the new model."
                : $"Generated the 'MageVisual' prefab with {name}.");
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
