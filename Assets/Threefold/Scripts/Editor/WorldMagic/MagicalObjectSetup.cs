using System.Collections.Generic;
using System.Text;
using Threefold.Characters;
using Threefold.Discovery;
using Threefold.Persistence;
using Threefold.Pooling;
using Threefold.Puzzles;
using Threefold.WorldMagic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Generates the example objects that respond to magic, and the tools to make more of them.
    ///
    /// Threefold > Setup > Magical Objects creates six definitions and six prefabs from the values
    /// in <see cref="WorldMagicDefaults"/>: an arcane crystal, a wooden barricade, a heat-sensitive
    /// stone barrier, a movable platform, an unstable structure and a reinforced door, plus a small
    /// gate that the crystal opens. In the Discipline Range it places them in a lane on the left.
    ///
    /// Threefold > World Magic > Make Selection Magical turns any selected object into one that
    /// responds to magic: it adds the components and connects colliders and renderers. The object
    /// then only needs a definition.
    ///
    /// Both are safe to run repeatedly. Existing definitions and prefabs are kept as they are, so
    /// tuned values are not lost. To regenerate with defaults, delete the folder
    /// Assets/Threefold/Generated/WorldMagic.
    /// </summary>
    public static partial class MagicalObjectSetup
    {
        private const string MenuPath = "Threefold/Setup/Magical Objects";
        private const string MakeMenuPath = "Threefold/World Magic/Make Selection Magical";
        private const string DialogTitle = "Magical Objects";
        internal const string Folder = "Assets/Threefold/Generated/WorldMagic";
        internal const string GroupName = "MagicalObjects";
        internal const string GateInstanceName = "Crystal Gate";
        internal const string ChainInstanceName = "Chain Barricade";
        private const string GatePrefabPath = Folder + "/CrystalGate.prefab";
        private const string AssignIdsMenuPath = "Threefold/World Magic/Assign Persistent Ids";

        /// <summary>How many barricades stand in the chain-reaction row: one more than a fire can reach.</summary>
        internal const int ChainRowCount = 5;

        private static readonly Color StoneColor = new Color(0.45f, 0.44f, 0.42f, 1f);
        private static readonly Color PaleStoneColor = new Color(0.66f, 0.63f, 0.58f, 1f);
        private static readonly Color SandstoneColor = new Color(0.6f, 0.53f, 0.42f, 1f);
        private static readonly Color PlatformColor = new Color(0.4f, 0.44f, 0.5f, 1f);
        private static readonly Color MetalColor = new Color(0.24f, 0.24f, 0.27f, 1f);
        private static readonly Color WoodColor = new Color(0.52f, 0.38f, 0.24f, 1f);
        private static readonly Color DoorWoodColor = new Color(0.36f, 0.26f, 0.18f, 1f);
        private static readonly Color CharredColor = new Color(0.12f, 0.1f, 0.09f, 1f);
        private static readonly Color CrackedColor = new Color(0.3f, 0.28f, 0.27f, 1f);
        private static readonly Color CrystalColor = new Color(0.36f, 0.4f, 0.66f, 1f);
        private static readonly Color BeaconColor = new Color(0.85f, 0.88f, 1f, 1f);
        private static readonly Color BracedColor = new Color(1f, 0.86f, 0.5f, 1f);
        private static readonly Color MovingColor = new Color(0.78f, 0.83f, 0.92f, 1f);

        /// <summary>Where one example stands, in the space of the lane it is placed in.</summary>
        internal readonly struct ExamplePlacement
        {
            public readonly Vector3 Position;
            public readonly float Yaw;

            public ExamplePlacement(Vector3 position, float yaw)
            {
                Position = position;
                Yaw = yaw;
            }
        }

        /// <summary>
        /// The examples stand in a lane along its forward axis, their faces turned to the lane's right,
        /// where the player walks. Indexed by <see cref="ExampleObject"/>.
        /// </summary>
        internal static readonly ExamplePlacement[] Lane =
        {
            new ExamplePlacement(new Vector3(0f, 0f, 0f), 0f),      // Arcane crystal
            new ExamplePlacement(new Vector3(0f, 0f, 4f), 90f),     // Wooden barricade
            new ExamplePlacement(new Vector3(0f, 0f, 8f), 90f),     // Heat-sensitive stone
            new ExamplePlacement(new Vector3(0f, 0f, 12f), 0f),     // Movable platform: slides 6 m forward
            new ExamplePlacement(new Vector3(0f, 0f, 22f), 0f),     // Unstable structure: falls away from the player
            new ExamplePlacement(new Vector3(0f, 0f, 27f), 90f),    // Reinforced door
        };

        /// <summary>The gate the crystal opens stands behind it.</summary>
        internal static readonly ExamplePlacement GatePlacement = new ExamplePlacement(new Vector3(-2.6f, 0f, 0f), 90f);

        /// <summary>
        /// A row of barricades behind the lane, each within reach of the next one's fire and of no
        /// other. Lighting the first shows a chain reaction and where the depth limit ends it: with
        /// the default limit of three, the fifth stays unlit.
        /// </summary>
        internal static ExamplePlacement ChainPlacement(int index)
        {
            return new ExamplePlacement(new Vector3(-3.8f, 0f, 4f + index * 3.2f), 90f);
        }

        /// <summary>The lane in the Discipline Range: along the left edge of its floor.</summary>
        internal static readonly Vector3 RangeLaneOrigin = new Vector3(-8.5f, 0f, -5f);

        /// <summary>The generated definitions and prefabs, indexed by <see cref="ExampleObject"/>.</summary>
        internal sealed class ExampleAssets
        {
            public readonly MagicalObjectDefinition[] Definitions = new MagicalObjectDefinition[WorldMagicDefaults.Count];
            public readonly GameObject[] Prefabs = new GameObject[WorldMagicDefaults.Count];
            public GameObject Gate;
        }

        private struct Look
        {
            public Material Stone;
            public Material PaleStone;
            public Material Sandstone;
            public Material Platform;
            public Material Metal;
            public Material Wood;
            public Material DoorWood;
            public Material Charred;
            public Material Crystal;
            public Material Beacon;
            public PooledObject Embers;
            public PooledObject Dust;
            public PooledObject Brace;
            public PooledObject Charge;
            public Material Straw;
            public PooledObject Mote;
        }

        /// <summary>The name of the group that holds an example's built clues: straw, vents, cracks.</summary>
        internal const string CluesName = "Clues";

        [MenuItem(MenuPath, false, 8)]
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

            ExampleAssets assets = EnsureAssets(report);
            AssetDatabase.SaveAssets();

            bool isRange = scene.path == DisciplineRangeSetup.ScenePath;
            bool alreadyPlaced = FindGroup(scene) != null;
            if (isRange || alreadyPlaced)
            {
                // The range has a lane reserved for them; a scene that already holds them only gets what is missing.
                Vector3 origin = isRange ? RangeLaneOrigin : FindGroup(scene).transform.position;
                float yaw = isRange ? 0f : FindGroup(scene).transform.eulerAngles.y;
                EnsureExamples(scene, assets, origin, yaw, report);
            }
            else if (EditorUtility.DisplayDialog(DialogTitle,
                         $"The definitions and prefabs are in '{Folder}'.\n\nPlace the six examples in the open scene as well? " +
                         "They are set in a lane 28 m long that starts 5 m ahead of the Mage and 4 m to the left " +
                         "(at the scene's origin if there is no Mage). Nothing that is in the scene is changed.",
                         "Place them", "Not now"))
            {
                ThirdPersonMotor motor = FindInScene<ThirdPersonMotor>(scene);
                Vector3 origin = Vector3.zero;
                float yaw = 0f;
                if (motor != null)
                {
                    Transform mage = motor.transform;
                    yaw = mage.eulerAngles.y;
                    Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
                    origin = mage.position + facing * new Vector3(-4f, 0f, 5f);
                }

                EnsureExamples(scene, assets, origin, yaw, report);
            }
            else
            {
                report.AppendLine("The examples were not placed in the open scene.");
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);

            var problems = new StringBuilder();
            int count = MagicalObjectValidator.Validate(scene, problems);
            report.AppendLine(count == 0
                ? "Validation: every magical object in the scene is complete."
                : $"Validation found {count} problem(s):\n{problems.ToString().TrimEnd()}");

            string summary = report.ToString().TrimEnd();
            Debug.Log($"[Threefold] {DialogTitle} finished.\n{summary}");
            EditorUtility.DisplayDialog(DialogTitle, summary + "\n\nSave the scene with Ctrl+S.", "OK");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        // ------------------------------------------------------------ authoring

        /// <summary>
        /// Makes the selected scene objects respond to magic: adds the object and visuals components,
        /// lists the colliders and renderers, and uses a selected definition asset if there is one.
        /// Nothing that is already set is changed.
        /// </summary>
        [MenuItem(MakeMenuPath, false, 60)]
        public static void MakeSelectionMagical()
        {
            MagicalObjectDefinition definition = null;
            foreach (Object selected in Selection.objects)
            {
                if (selected is MagicalObjectDefinition found)
                {
                    definition = found;
                    break;
                }
            }

            Undo.SetCurrentGroupName("Make Selection Magical");
            int undoGroup = Undo.GetCurrentGroup();
            var report = new StringBuilder();
            int made = 0;

            foreach (GameObject target in Selection.gameObjects)
            {
                if (EditorUtility.IsPersistent(target))
                {
                    continue;
                }

                MakeMagical(target, definition, report);
                made++;
            }

            Undo.CollapseUndoOperations(undoGroup);

            if (made == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "Select one or more objects in the scene first. To give them a definition in the same step, " +
                    "also select the definition asset (Ctrl+click it in the Project window).", "OK");
                return;
            }

            string summary = report.ToString().TrimEnd();
            Debug.Log($"[Threefold] Make Selection Magical finished.\n{summary}");
            EditorUtility.DisplayDialog(DialogTitle, summary, "OK");
        }

        [MenuItem(MakeMenuPath, true)]
        private static bool CanMakeSelectionMagical()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && Selection.gameObjects.Length > 0;
        }

        /// <summary>Adds what an object needs to respond to magic. Keeps everything that is already there.</summary>
        internal static MagicalObject MakeMagical(GameObject target, MagicalObjectDefinition definition, StringBuilder report)
        {
            bool addedObject = !target.TryGetComponent(out MagicalObject magical);
            if (addedObject)
            {
                magical = Undo.AddComponent<MagicalObject>(target);

                var solid = new List<Collider>();
                foreach (Collider part in target.GetComponentsInChildren<Collider>(true))
                {
                    if (!part.isTrigger)
                    {
                        solid.Add(part);
                    }
                }

                magical.Bind(definition, null, solid.ToArray());

                Bounds bounds = default;
                bool hasBounds = false;
                foreach (Collider part in solid)
                {
                    if (hasBounds)
                    {
                        bounds.Encapsulate(part.bounds);
                    }
                    else
                    {
                        bounds = part.bounds;
                        hasBounds = true;
                    }
                }

                // Bounds are empty for a collider that is switched off; the default aim point is kept then.
                if (hasBounds && bounds.size.sqrMagnitude > 0f)
                {
                    magical.SetAimPoint(target.transform.InverseTransformPoint(bounds.center));
                }

                report.AppendLine($"'{target.name}': added MagicalObject with {solid.Count} collider(s).");
            }
            else if (magical.Definition == null && definition != null)
            {
                Undo.RecordObject(magical, DialogTitle);
                magical.FillDefinition(definition);
                report.AppendLine($"'{target.name}': gave the existing MagicalObject the definition '{definition.name}'.");
            }
            else
            {
                report.AppendLine($"'{target.name}': kept its existing MagicalObject.");
            }

            if (!target.TryGetComponent(out PropertyStateVisuals _))
            {
                PropertyStateVisuals visuals = Undo.AddComponent<PropertyStateVisuals>(target);
                Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
                visuals.Bind(magical, renderers, null);
                report.AppendLine($"'{target.name}': added PropertyStateVisuals with {renderers.Length} renderer(s). " +
                                  "Add tints, parts and effects per condition in the Inspector.");
            }

            if (!target.TryGetComponent(out DiscoveryClues _))
            {
                // The part that makes the small movements must not be the object itself, which other things
                // move and measure. The first mesh below it is used; without one, answers show no movement.
                Transform moving = null;
                foreach (Renderer candidate in target.GetComponentsInChildren<Renderer>(true))
                {
                    if (candidate.transform != target.transform)
                    {
                        moving = candidate.transform;
                        break;
                    }
                }

                DiscoveryClues clues = Undo.AddComponent<DiscoveryClues>(target);
                clues.Bind(magical, target.GetComponent<PropertyStateVisuals>(), moving);

                // Its idle clue follows from its properties: a crate that only burns must not pulse like a crystal.
                ClueKinds kinds = Compatibility.CluesFor(magical.Definition != null ? magical.Definition.Properties : null);
                clues.SetIdleClue(ClueDefaults.IdleStrength(kinds), ClueDefaults.Accent(kinds));
                report.AppendLine(moving != null
                    ? $"'{target.name}': added DiscoveryClues; '{moving.name}' makes its small movements."
                    : $"'{target.name}': added DiscoveryClues without a moving part. Put its mesh on a child object and " +
                      "assign it as Motion Target, so that its answers do not rely on colour alone.");
            }

            MarkChanged(magical);

            if (magical.Definition == null)
            {
                report.AppendLine($"'{target.name}': it has no definition yet. Create one with Assets > Create > Threefold > " +
                                  "Magical Object Definition and assign it.");
            }
            else
            {
                var problems = new List<string>();
                MagicalObjectValidator.ValidateObject(magical, problems.Add);
                foreach (string problem in problems)
                {
                    report.AppendLine("  Problem: " + problem);
                }
            }

            return magical;
        }

        // --------------------------------------------------------------- assets

        /// <summary>Generates the missing definitions, materials, effects and prefabs.</summary>
        internal static ExampleAssets EnsureAssets(StringBuilder report)
        {
            CombatSetup.EnsureFolder(Folder);
            CombatSetup.SharedAssets shared = CombatSetup.EnsureSharedAssets(report);

            Look look = default;
            look.Stone = LitMaterial("ExampleStone", StoneColor, report);
            look.PaleStone = LitMaterial("ExamplePaleStone", PaleStoneColor, report);
            look.Sandstone = LitMaterial("ExampleSandstone", SandstoneColor, report);
            look.Platform = LitMaterial("ExamplePlatform", PlatformColor, report);
            look.Metal = LitMaterial("ExampleMetal", MetalColor, report);
            look.Wood = LitMaterial("ExampleWood", WoodColor, report);
            look.DoorWood = LitMaterial("ExampleDoorWood", DoorWoodColor, report);
            look.Charred = LitMaterial("ExampleCharred", CharredColor, report);
            look.Crystal = LitMaterial("ExampleCrystal", CrystalColor, report);
            look.Beacon = CombatSetup.EnsureMaterial($"{Folder}/ExampleBeacon.mat",
                () => CombatSetup.CreateColorMaterial(CombatSetup.UnlitShader, BeaconColor), report);

            look.Embers = CombatSetup.EnsureEffectPrefab($"{Folder}/ObjectEmbers.prefab", "ObjectEmbers", shared.Glow,
                new Color(1f, 0.62f, 0.3f, 1f), 8, 0.6f, report);
            look.Dust = CombatSetup.EnsureEffectPrefab($"{Folder}/ObjectDust.prefab", "ObjectDust", shared.Glow,
                new Color(0.95f, 0.9f, 0.76f, 1f), 12, 0.7f, report);
            look.Brace = CombatSetup.EnsureEffectPrefab($"{Folder}/ObjectBrace.prefab", "ObjectBrace", shared.Glow,
                new Color(1f, 0.93f, 0.7f, 1f), 8, 0.4f, report);
            look.Charge = CombatSetup.EnsureEffectPrefab($"{Folder}/ObjectCharge.prefab", "ObjectCharge", shared.Glow,
                new Color(0.62f, 0.7f, 1f, 1f), 12, 0.6f, report);
            look.Straw = LitMaterial("ExampleStraw", new Color(0.78f, 0.68f, 0.4f, 1f), report);
            look.Mote = CombatSetup.EnsureEffectPrefab($"{Folder}/DiscoveryMote.prefab", "DiscoveryMote", shared.Glow,
                new Color(1f, 0.97f, 0.88f, 1f), 3, 0.4f, report);

            var assets = new ExampleAssets();
            for (int i = 0; i < WorldMagicDefaults.Count; i++)
            {
                var example = (ExampleObject)i;
                assets.Definitions[i] = EnsureDefinition(example, report);
                assets.Prefabs[i] = EnsurePrefab(example, assets.Definitions[i], look, report);
                EnsureDiscovery(example, look, report);
            }

            assets.Gate = EnsureGatePrefab(look, report);

            report.AppendLine($"Magical object assets are in '{Folder}'. New files there exist only on this computer until " +
                              "they are uploaded with the scene (see Docs/DevelopmentWorkflow.md).");
            return assets;
        }

        internal static string DefinitionPath(ExampleObject example)
        {
            return $"{Folder}/{example}Definition.asset";
        }

        internal static string PrefabPath(ExampleObject example)
        {
            return $"{Folder}/{example}.prefab";
        }

        private static Material LitMaterial(string name, Color color, StringBuilder report)
        {
            return CombatSetup.EnsureMaterial($"{Folder}/{name}.mat",
                () => CombatSetup.CreateColorMaterial(CombatSetup.LitShader, color), report);
        }

        private static MagicalObjectDefinition EnsureDefinition(ExampleObject example, StringBuilder report)
        {
            string path = DefinitionPath(example);
            var definition = AssetDatabase.LoadAssetAtPath<MagicalObjectDefinition>(path);
            if (definition != null)
            {
                // A definition generated before LM-04 is given, once, what that sprint added: what is
                // saved and its reactions. Those values did not exist before; every other value is kept.
                if (definition.DataVersion < WorldMagicDefaults.DataVersion)
                {
                    Undo.RecordObject(definition, DialogTitle);
                }

                if (definition.Upgrade(WorldMagicDefaults.Settings(example)))
                {
                    EditorUtility.SetDirty(definition);
                    report.AppendLine($"Updated definition '{WorldMagicDefaults.Name(example)}' with what is saved and how " +
                                      "it reacts. Its other values were kept.");
                }

                return definition;
            }

            definition = ScriptableObject.CreateInstance<MagicalObjectDefinition>();
            definition.Configure(WorldMagicDefaults.Name(example), WorldMagicDefaults.Description(example),
                WorldMagicDefaults.Settings(example));
            AssetDatabase.CreateAsset(definition, path);
            report.AppendLine($"Generated definition '{WorldMagicDefaults.Name(example)}' " +
                              $"({Describe(definition.Properties)}).");
            return definition;
        }

        private static string Describe(MagicalPropertySettings settings)
        {
            var parts = new List<string>();
            if (settings.thermal) parts.Add("thermal");
            if (settings.structural) parts.Add("structural");
            if (settings.kinetic) parts.Add("kinetic");
            if (settings.arcane) parts.Add("arcane-reactive");
            if (settings.stability) parts.Add("stability");
            return string.Join(", ", parts);
        }

        private static GameObject EnsurePrefab(ExampleObject example, MagicalObjectDefinition definition, Look look,
            StringBuilder report)
        {
            string path = PrefabPath(example);
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing;
            }

            GameObject root;
            switch (example)
            {
                case ExampleObject.ArcaneCrystal:
                    root = BuildCrystal(definition, look);
                    break;
                case ExampleObject.WoodenBarricade:
                    root = BuildBarricade(definition, look);
                    break;
                case ExampleObject.HeatSensitiveStone:
                    root = BuildStone(definition, look);
                    break;
                case ExampleObject.MovablePlatform:
                    root = BuildPlatform(definition, look);
                    break;
                case ExampleObject.UnstableStructure:
                    root = BuildUnstableStructure(definition, look);
                    break;
                default:
                    root = BuildDoor(definition, look);
                    break;
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            report.AppendLine($"Generated prefab '{example}'.");
            return prefab;
        }

        // ------------------------------------------------------------ discovery

        /// <summary>
        /// Gives an example prefab what discovery needs, whether the prefab was just generated or has
        /// existed since an earlier sprint: its built clues (straw at the barricade's foot, vents and
        /// scorch marks on the stone, cracks and debris on the unstable structure) and the component
        /// that hints and answers. Whatever is already there is kept.
        /// </summary>
        private static void EnsureDiscovery(ExampleObject example, Look look, StringBuilder report)
        {
            string path = PrefabPath(example);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                return;
            }

            DiscoveryClues existing = asset.GetComponent<DiscoveryClues>();
            bool hasComponent = existing != null;
            bool hasParts = !HasBuiltClues(example) || FindDeep(asset.transform, CluesName) != null;
            Transform remainsInAsset = FindRemains(example, asset.transform);
            bool hasRemains = remainsInAsset == null || (hasComponent && existing.MotionTargetWhenDestroyed != null);
            if (hasComponent && hasParts && hasRemains)
            {
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!root.TryGetComponent(out MagicalObject magical))
                {
                    return;
                }

                if (!hasParts)
                {
                    AddBuiltClues(example, root.transform, look);
                }

                if (!root.TryGetComponent(out DiscoveryClues clues))
                {
                    clues = root.AddComponent<DiscoveryClues>();
                    clues.Bind(magical, root.GetComponent<PropertyStateVisuals>(), FindMotionTarget(example, root.transform));
                    ConfigureClues(example, clues, look);
                }

                // What is left of a destroyed object is what moves when the blow that destroyed it is answered.
                clues.FillDestroyedMotionTarget(FindRemains(example, root.transform));

                PrefabUtility.SaveAsPrefabAsset(root, path);
                report.AppendLine($"Gave prefab '{example}' its discovery clues.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool HasBuiltClues(ExampleObject example)
        {
            return example == ExampleObject.WoodenBarricade || example == ExampleObject.HeatSensitiveStone ||
                   example == ExampleObject.UnstableStructure;
        }

        /// <summary>
        /// The part of each example that makes the small movements. Never the part something else moves:
        /// the visible block rather than the sliding platform, the door's planks rather than its leaf.
        /// </summary>
        private static Transform FindMotionTarget(ExampleObject example, Transform root)
        {
            switch (example)
            {
                case ExampleObject.ArcaneCrystal: return root.Find("Visual");
                case ExampleObject.WoodenBarricade: return root.Find("Whole");
                case ExampleObject.HeatSensitiveStone: return root.Find("Whole");
                case ExampleObject.MovablePlatform: return root.Find("Platform/Block");
                case ExampleObject.UnstableStructure: return root.Find("Body/Sway");
                default: return root.Find("Leaf/Whole");
            }
        }

        /// <summary>The group that appears when an example is destroyed, for those that have one.</summary>
        private static Transform FindRemains(ExampleObject example, Transform root)
        {
            switch (example)
            {
                case ExampleObject.WoodenBarricade: return root.Find("Remains");
                case ExampleObject.HeatSensitiveStone: return root.Find("Rubble");
                case ExampleObject.ReinforcedDoor: return root.Find("Splinters");
                default: return null;
            }
        }

        private static void ConfigureClues(ExampleObject example, DiscoveryClues clues, Look look)
        {
            // Objects whose clue is something built (straw, vents, a rail, cracks) only breathe a trace;
            // the two that hold charge pulse a little more, because the pulse is their clue.
            const PropertyCondition Gone = PropertyCondition.Destroyed;
            switch (example)
            {
                case ExampleObject.ArcaneCrystal:
                    clues.Configure(0.25f, new Color(0.78f, 0.84f, 1f, 1f), Gone | PropertyCondition.Activated, look.Mote);
                    break;
                case ExampleObject.WoodenBarricade:
                    clues.Configure(0.08f, new Color(1f, 0.9f, 0.7f, 1f), Gone | PropertyCondition.Burning, look.Mote);
                    break;
                case ExampleObject.HeatSensitiveStone:
                    clues.Configure(0.08f, new Color(1f, 0.86f, 0.72f, 1f), Gone, look.Mote);
                    break;
                case ExampleObject.MovablePlatform:
                    clues.Configure(0.08f, new Color(0.86f, 0.9f, 1f, 1f), Gone | PropertyCondition.AtTravelLimit, look.Mote);
                    break;
                case ExampleObject.UnstableStructure:
                    clues.Configure(0.08f, new Color(1f, 0.95f, 0.8f, 1f), Gone | PropertyCondition.Collapsed, look.Mote);
                    break;
                default:
                    clues.Configure(0.2f, new Color(0.78f, 0.84f, 1f, 1f),
                        Gone | PropertyCondition.Activated | PropertyCondition.Fractured, look.Mote);
                    break;
            }
        }

        /// <summary>Level 1: features that belong to the object and tell what it is made of or how it stands.</summary>
        private static void AddBuiltClues(ExampleObject example, Transform root, Look look)
        {
            switch (example)
            {
                case ExampleObject.WoodenBarricade:
                {
                    // Dry straw packed at its foot: it will burn.
                    Transform parent = root.Find("Whole");
                    Transform clues = Group(parent != null ? parent : root, CluesName);
                    Part(PrimitiveType.Cube, "Straw", clues, new Vector3(-0.55f, 0.14f, 0.24f), new Vector3(0.55f, 0.28f, 0.3f),
                        look.Straw, new Vector3(0f, 12f, 0f));
                    Part(PrimitiveType.Cube, "Straw", clues, new Vector3(0.6f, 0.12f, -0.22f), new Vector3(0.5f, 0.24f, 0.28f),
                        look.Straw, new Vector3(0f, -18f, 0f));
                    break;
                }

                case ExampleObject.HeatSensitiveStone:
                {
                    // Vents with scorched stone around them, on both faces: heat has been at work here before.
                    Transform parent = root.Find("Whole");
                    Transform clues = Group(parent != null ? parent : root, CluesName);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Part(PrimitiveType.Cube, "Scorch", clues, new Vector3(0f, 0.75f, side * 0.405f), new Vector3(1f, 0.6f, 0.02f),
                            look.Charred);
                        for (int i = 0; i < 3; i++)
                        {
                            Part(PrimitiveType.Cube, "Vent", clues, new Vector3(0f, 0.6f + i * 0.15f, side * 0.42f),
                                new Vector3(0.6f, 0.05f, 0.03f), look.Metal);
                        }
                    }

                    break;
                }

                case ExampleObject.UnstableStructure:
                {
                    // Cracks across its blocks, and pieces that have already come away.
                    Transform parent = root.Find("Body/Sway");
                    Transform clues = Group(parent != null ? parent : root, CluesName);
                    Part(PrimitiveType.Cube, "Crack", clues, new Vector3(0.15f, 1f, 0.66f), new Vector3(0.04f, 0.9f, 0.02f), look.Charred,
                        new Vector3(0f, 0f, 18f));
                    Part(PrimitiveType.Cube, "Crack", clues, new Vector3(-0.2f, 2f, 0.6f), new Vector3(0.04f, 0.7f, 0.02f), look.Charred,
                        new Vector3(0f, 0f, -24f));
                    Part(PrimitiveType.Cube, "Crack", clues, new Vector3(-0.1f, 1.2f, -0.66f), new Vector3(0.04f, 0.8f, 0.02f), look.Charred,
                        new Vector3(0f, 0f, -15f));

                    // The debris lies on the ground and stays there when the structure comes down.
                    Transform debris = Group(root, "Debris");
                    Part(PrimitiveType.Cube, "Chip", debris, new Vector3(0.9f, 0.1f, 0.5f), new Vector3(0.3f, 0.2f, 0.25f), look.Sandstone,
                        new Vector3(0f, 30f, 0f));
                    Part(PrimitiveType.Cube, "Chip", debris, new Vector3(-0.85f, 0.08f, -0.4f), new Vector3(0.22f, 0.16f, 0.2f),
                        look.Sandstone, new Vector3(0f, -20f, 0f));
                    Part(PrimitiveType.Cube, "Chip", debris, new Vector3(0.7f, 0.06f, -0.7f), new Vector3(0.18f, 0.12f, 0.16f), look.Stone,
                        new Vector3(0f, 55f, 0f));
                    break;
                }
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != root && child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        // -------------------------------------------------------------- prefabs

        /// <summary>A crystal on a plinth. It brightens as it charges and lights a beacon while activated.</summary>
        private static GameObject BuildCrystal(MagicalObjectDefinition definition, Look look)
        {
            var root = new GameObject(ExampleObject.ArcaneCrystal.ToString());

            CapsuleCollider body = root.AddComponent<CapsuleCollider>();
            body.center = new Vector3(0f, 1f, 0f);
            body.height = 2f;
            body.radius = 0.45f;

            Transform visual = Group(root.transform, "Visual");
            Part(PrimitiveType.Cube, "Plinth", visual, new Vector3(0f, 0.2f, 0f), new Vector3(0.9f, 0.4f, 0.9f), look.Stone);
            GameObject shard = Part(PrimitiveType.Cube, "Crystal", visual, new Vector3(0f, 1.15f, 0f),
                new Vector3(0.42f, 1.3f, 0.42f), look.Crystal, new Vector3(0f, 45f, 8f));
            GameObject small = Part(PrimitiveType.Cube, "CrystalSmall", visual, new Vector3(0.28f, 0.75f, 0.1f),
                new Vector3(0.22f, 0.7f, 0.22f), look.Crystal, new Vector3(12f, 20f, -14f));

            GameObject beacon = Part(PrimitiveType.Sphere, "Beacon", root.transform, new Vector3(0f, 2.2f, 0f),
                new Vector3(0.34f, 0.34f, 0.34f), look.Beacon);
            beacon.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            beacon.SetActive(false);

            MagicalObject magical = root.AddComponent<MagicalObject>();
            magical.Bind(definition, null, new Collider[] { body });
            magical.SetAimPoint(new Vector3(0f, 1.15f, 0f));

            // Anything a puzzle can listen to may follow the crystal: a door, a timer, a sequence.
            root.AddComponent<PropertyStateTrigger>().Configure(magical, PropertyCondition.Activated, false);

            PropertyStateVisuals visuals = root.AddComponent<PropertyStateVisuals>();
            visuals.Bind(magical, null, Renderers(shard, small));
            visuals.Configure(null,
                new[] { Shown(PropertyCondition.Activated, false, beacon) },
                new[] { Effect(PropertyCondition.Activated, look.Charge) },
                null);
            return root;
        }

        /// <summary>Posts and planks. It chars as it weakens, burns, and leaves stubs.</summary>
        private static GameObject BuildBarricade(MagicalObjectDefinition definition, Look look)
        {
            var root = new GameObject(ExampleObject.WoodenBarricade.ToString());

            BoxCollider body = root.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 0.85f, 0f);
            body.size = new Vector3(2.6f, 1.7f, 0.4f);

            Transform whole = Group(root.transform, "Whole");
            Transform remains = Group(root.transform, "Remains");
            for (int i = -1; i <= 1; i++)
            {
                Part(PrimitiveType.Cube, "Post", whole, new Vector3(i * 1.1f, 0.85f, 0f), new Vector3(0.18f, 1.7f, 0.18f), look.Wood);
                Part(PrimitiveType.Cube, "Stub", remains, new Vector3(i * 1.1f, 0.18f, 0f), new Vector3(0.18f, 0.36f, 0.18f),
                    look.Charred, new Vector3(0f, i * 17f, i * 6f));
            }

            Part(PrimitiveType.Cube, "PlankLow", whole, new Vector3(0f, 0.45f, 0.12f), new Vector3(2.6f, 0.26f, 0.07f), look.Wood);
            Part(PrimitiveType.Cube, "PlankMid", whole, new Vector3(0f, 0.9f, 0.12f), new Vector3(2.6f, 0.26f, 0.07f), look.Wood);
            Part(PrimitiveType.Cube, "PlankHigh", whole, new Vector3(0f, 1.35f, 0.12f), new Vector3(2.6f, 0.26f, 0.07f), look.Wood);
            Part(PrimitiveType.Cube, "Brace", whole, new Vector3(0f, 0.9f, -0.12f), new Vector3(2.7f, 0.2f, 0.06f), look.Wood,
                new Vector3(0f, 0f, 27f));
            Part(PrimitiveType.Cube, "Ash", remains, new Vector3(0.2f, 0.04f, 0.2f), new Vector3(1.6f, 0.08f, 0.24f), look.Charred,
                new Vector3(0f, 20f, 0f));
            remains.gameObject.SetActive(false);

            MagicalObject magical = root.AddComponent<MagicalObject>();
            magical.Bind(definition, null, new Collider[] { body });
            magical.SetAimPoint(new Vector3(0f, 0.9f, 0f));

            PropertyStateVisuals visuals = root.AddComponent<PropertyStateVisuals>();
            visuals.Bind(magical, whole.GetComponentsInChildren<Renderer>(true), null);
            visuals.Configure(
                new[] { Tint(PropertyCondition.Weakened, CharredColor, 0.45f), Tint(PropertyCondition.Fractured, CharredColor, 0.8f) },
                new[]
                {
                    Shown(PropertyCondition.Destroyed, true, whole.gameObject),
                    Shown(PropertyCondition.Destroyed, false, remains.gameObject),
                },
                new[]
                {
                    Effect(PropertyCondition.Burning, look.Embers), Effect(PropertyCondition.Fractured, look.Dust),
                    Effect(PropertyCondition.Destroyed, look.Dust),
                },
                look.Embers);
            return root;
        }

        /// <summary>A stone slab. It glows as it heats; hot, it shatters into rubble under a strong push.</summary>
        private static GameObject BuildStone(MagicalObjectDefinition definition, Look look)
        {
            var root = new GameObject(ExampleObject.HeatSensitiveStone.ToString());

            BoxCollider body = root.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 1.1f, 0f);
            body.size = new Vector3(2.2f, 2.2f, 0.8f);

            Transform whole = Group(root.transform, "Whole");
            Part(PrimitiveType.Cube, "Slab", whole, new Vector3(0f, 1f, 0f), new Vector3(2.2f, 2f, 0.8f), look.PaleStone);
            Part(PrimitiveType.Cube, "Cap", whole, new Vector3(0f, 2.1f, 0f), new Vector3(2.4f, 0.2f, 0.95f), look.Stone);

            Transform rubble = Group(root.transform, "Rubble");
            Part(PrimitiveType.Cube, "Chunk", rubble, new Vector3(-0.6f, 0.2f, 0.1f), new Vector3(0.7f, 0.4f, 0.6f), look.PaleStone,
                new Vector3(0f, 25f, 0f));
            Part(PrimitiveType.Cube, "Chunk", rubble, new Vector3(0.55f, 0.15f, -0.1f), new Vector3(0.6f, 0.3f, 0.5f), look.PaleStone,
                new Vector3(0f, -35f, 0f));
            Part(PrimitiveType.Cube, "Chunk", rubble, new Vector3(0.1f, 0.28f, 0.2f), new Vector3(0.5f, 0.56f, 0.45f), look.PaleStone,
                new Vector3(18f, 50f, 9f));
            Part(PrimitiveType.Cube, "Chunk", rubble, new Vector3(-0.2f, 0.1f, -0.3f), new Vector3(0.4f, 0.2f, 0.4f), look.Stone,
                new Vector3(0f, 12f, 0f));
            rubble.gameObject.SetActive(false);

            MagicalObject magical = root.AddComponent<MagicalObject>();
            magical.Bind(definition, null, new Collider[] { body });
            magical.SetAimPoint(new Vector3(0f, 1.1f, 0f));

            PropertyStateVisuals visuals = root.AddComponent<PropertyStateVisuals>();
            visuals.Bind(magical, whole.GetComponentsInChildren<Renderer>(true), null);
            visuals.Configure(
                new[] { Tint(PropertyCondition.Weakened, CrackedColor, 0.25f), Tint(PropertyCondition.Fractured, CrackedColor, 0.45f) },
                new[]
                {
                    Shown(PropertyCondition.Destroyed, true, whole.gameObject),
                    Shown(PropertyCondition.Destroyed, false, rubble.gameObject),
                },
                new[]
                {
                    Effect(PropertyCondition.Heated, look.Embers), Effect(PropertyCondition.Fractured, look.Dust),
                    Effect(PropertyCondition.Destroyed, look.Dust),
                },
                null);
            return root;
        }

        /// <summary>A block on a rail. Force slides it between the two stops.</summary>
        private static GameObject BuildPlatform(MagicalObjectDefinition definition, Look look)
        {
            var root = new GameObject(ExampleObject.MovablePlatform.ToString());
            KineticSettings kinetic = definition.Properties.kineticSettings;
            float travel = Mathf.Max(1f, kinetic.maxTravel - kinetic.minTravel);
            float middle = (kinetic.maxTravel + kinetic.minTravel) * 0.5f;

            // The rail shows where the block can go. It is drawn from the travel in the definition.
            Transform rail = Group(root.transform, "Rail");
            Part(PrimitiveType.Cube, "Track", rail, new Vector3(0f, 0.03f, middle), new Vector3(0.35f, 0.06f, travel + 2.3f), look.Metal);
            Part(PrimitiveType.Cube, "StopNear", rail, new Vector3(0f, 0.15f, kinetic.minTravel - 1.15f),
                new Vector3(1.2f, 0.3f, 0.2f), look.Metal);
            Part(PrimitiveType.Cube, "StopFar", rail, new Vector3(0f, 0.15f, kinetic.maxTravel + 1.15f),
                new Vector3(1.2f, 0.3f, 0.2f), look.Metal);

            var block = new GameObject("Platform");
            block.transform.SetParent(root.transform, false);
            BoxCollider body = block.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 0.5f, 0f);
            body.size = new Vector3(2f, 1f, 2f);
            MakeMovingBody(block);

            Part(PrimitiveType.Cube, "Block", block.transform, new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 2f), look.Platform);
            Part(PrimitiveType.Cube, "Inlay", block.transform, new Vector3(0f, 1.01f, 0f), new Vector3(1.4f, 0.04f, 1.4f), look.Metal);

            MagicalObject magical = root.AddComponent<MagicalObject>();
            magical.Bind(definition, block.transform, new Collider[] { body });
            magical.SetAimPoint(new Vector3(0f, 0.6f, 0f));

            PropertyStateVisuals visuals = root.AddComponent<PropertyStateVisuals>();
            visuals.Bind(magical, block.GetComponentsInChildren<Renderer>(true), null);
            visuals.Configure(
                new[] { Tint(PropertyCondition.Moving, MovingColor, 0.25f) },
                null,
                new[] { Effect(PropertyCondition.Moving, look.Dust), Effect(PropertyCondition.AtTravelLimit, look.Dust) },
                null);
            return root;
        }

        /// <summary>A leaning stack of blocks. It sways, turns gold while braced, and topples when it collapses.</summary>
        private static GameObject BuildUnstableStructure(MagicalObjectDefinition definition, Look look)
        {
            var root = new GameObject(ExampleObject.UnstableStructure.ToString());

            // The collider is on the part that falls, so the fallen stack lies where it is seen to lie.
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            BoxCollider box = body.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 1.6f, 0f);
            box.size = new Vector3(1.3f, 3.2f, 1.3f);
            MakeMovingBody(body);

            // Only the look sways; the collider stays still until the collapse.
            Transform sway = Group(body.transform, "Sway");
            Part(PrimitiveType.Cube, "Base", sway, new Vector3(0f, 0.45f, 0f), new Vector3(1.3f, 0.9f, 1.3f), look.Sandstone);
            Part(PrimitiveType.Cube, "Middle", sway, new Vector3(0.06f, 1.4f, -0.04f), new Vector3(1.05f, 1f, 1.05f), look.Sandstone,
                new Vector3(0f, 9f, 0f));
            Part(PrimitiveType.Cube, "Upper", sway, new Vector3(-0.05f, 2.35f, 0.05f), new Vector3(1.15f, 0.9f, 1.15f), look.Sandstone,
                new Vector3(0f, -6f, 0f));
            Part(PrimitiveType.Cube, "Cap", sway, new Vector3(0.08f, 3f, 0f), new Vector3(0.9f, 0.4f, 0.9f), look.Stone,
                new Vector3(0f, 14f, 0f));

            MagicalObject magical = root.AddComponent<MagicalObject>();
            magical.Bind(definition, null, new Collider[] { box });
            magical.SetAimPoint(new Vector3(0f, 1.6f, 0f));

            PropertyStateVisuals visuals = root.AddComponent<PropertyStateVisuals>();
            visuals.Bind(magical, sway.GetComponentsInChildren<Renderer>(true), null);
            visuals.Configure(
                new[] { Tint(PropertyCondition.Braced, BracedColor, 0.45f), Tint(PropertyCondition.Collapsed, CrackedColor, 0.3f) },
                null,
                new[]
                {
                    Effect(PropertyCondition.Braced, look.Brace), Effect(PropertyCondition.Weakened, look.Dust),
                    Effect(PropertyCondition.Collapsed, look.Dust),
                },
                null);

            // It topples to its left, away from the side the lane is walked on, and comes to rest on the ground.
            visuals.ConfigureMotion(sway, 1.5f, body.transform, new Vector3(-0.3f, 0.55f, 0f), new Vector3(0f, 0f, 82f));
            return root;
        }

        /// <summary>
        /// A heavy door in a stone frame, with an arcane lock in the lintel. Two ways through: charge
        /// the lock, or fracture the leaf with heavy force. Either makes the door give way; more force
        /// destroys the leaf altogether.
        /// </summary>
        private static GameObject BuildDoor(MagicalObjectDefinition definition, Look look)
        {
            var root = new GameObject(ExampleObject.ReinforcedDoor.ToString());

            Transform frame = Group(root.transform, "Frame");
            SolidPart("PostLeft", frame, new Vector3(-1.4f, 1.6f, 0f), new Vector3(0.5f, 3.2f, 0.7f), look.Stone);
            SolidPart("PostRight", frame, new Vector3(1.4f, 1.6f, 0f), new Vector3(0.5f, 3.2f, 0.7f), look.Stone);
            SolidPart("Lintel", frame, new Vector3(0f, 3.4f, 0f), new Vector3(3.3f, 0.4f, 0.7f), look.Stone);
            GameObject gem = Part(PrimitiveType.Sphere, "Lock", frame, new Vector3(0f, 3.4f, 0f), new Vector3(0.4f, 0.3f, 0.95f),
                look.Crystal);

            var leaf = new GameObject("Leaf");
            leaf.transform.SetParent(root.transform, false);
            BoxCollider leafBox = leaf.AddComponent<BoxCollider>();
            leafBox.center = new Vector3(0f, 1.6f, 0f);
            leafBox.size = new Vector3(2.3f, 3.2f, 0.3f);
            MakeMovingBody(leaf);

            Transform whole = Group(leaf.transform, "Whole");
            Part(PrimitiveType.Cube, "Slab", whole, new Vector3(0f, 1.6f, 0f), new Vector3(2.3f, 3.2f, 0.28f), look.DoorWood);
            Part(PrimitiveType.Cube, "BandLow", whole, new Vector3(0f, 0.8f, 0f), new Vector3(2.34f, 0.16f, 0.32f), look.Metal);
            Part(PrimitiveType.Cube, "BandHigh", whole, new Vector3(0f, 2.4f, 0f), new Vector3(2.34f, 0.16f, 0.32f), look.Metal);

            Transform splinters = Group(root.transform, "Splinters");
            Part(PrimitiveType.Cube, "Plank", splinters, new Vector3(-0.5f, 0.05f, 0.4f), new Vector3(0.9f, 0.1f, 0.3f), look.DoorWood,
                new Vector3(0f, 25f, 0f));
            Part(PrimitiveType.Cube, "Plank", splinters, new Vector3(0.4f, 0.05f, -0.3f), new Vector3(1.1f, 0.1f, 0.28f), look.DoorWood,
                new Vector3(0f, -40f, 0f));
            Part(PrimitiveType.Cube, "Band", splinters, new Vector3(0.1f, 0.04f, 0.1f), new Vector3(1.4f, 0.08f, 0.14f), look.Metal,
                new Vector3(0f, 70f, 0f));
            splinters.gameObject.SetActive(false);

            // Only the leaf goes when the door is destroyed; the frame stays solid.
            MagicalObject magical = root.AddComponent<MagicalObject>();
            magical.Bind(definition, null, new Collider[] { leafBox });
            magical.SetAimPoint(new Vector3(0f, 3.3f, 0f));

            // One trigger on two properties: the door gives way when its lock is activated or when force
            // has fractured it. It latches, so the door then stays open.
            PropertyStateTrigger trigger = root.AddComponent<PropertyStateTrigger>();
            trigger.Configure(magical, PropertyCondition.Activated | PropertyCondition.Fractured, true);

            PuzzleDoor door = root.AddComponent<PuzzleDoor>();
            door.Control = DoorControl.Input;
            door.InputElement = trigger;
            door.ConfigureMotion(leaf.transform, new Vector3(0f, -3.15f, 0f), Vector3.zero, 1.8f, 1.2f);

            PropertyStateVisuals visuals = root.AddComponent<PropertyStateVisuals>();
            visuals.Bind(magical, whole.GetComponentsInChildren<Renderer>(true), Renderers(gem));
            visuals.Configure(
                new[] { Tint(PropertyCondition.Weakened, CrackedColor, 0.3f), Tint(PropertyCondition.Fractured, CrackedColor, 0.55f) },
                new[]
                {
                    Shown(PropertyCondition.Destroyed, true, whole.gameObject),
                    Shown(PropertyCondition.Destroyed, false, splinters.gameObject),
                },
                new[]
                {
                    Effect(PropertyCondition.Weakened, look.Dust), Effect(PropertyCondition.Fractured, look.Dust),
                    Effect(PropertyCondition.Destroyed, look.Dust), Effect(PropertyCondition.Activated, look.Charge),
                },
                null);
            return root;
        }

        /// <summary>
        /// A low bar between two posts, with no magic of its own: a puzzle door that waits for an input.
        /// In the range its input is the crystal, which shows a property driving a mechanism.
        /// </summary>
        private static GameObject EnsureGatePrefab(Look look, StringBuilder report)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(GatePrefabPath);
            if (existing != null)
            {
                return existing;
            }

            var root = new GameObject("CrystalGate");
            SolidPart("PostLeft", root.transform, new Vector3(-1.35f, 0.7f, 0f), new Vector3(0.3f, 1.4f, 0.3f), look.Stone);
            SolidPart("PostRight", root.transform, new Vector3(1.35f, 0.7f, 0f), new Vector3(0.3f, 1.4f, 0.3f), look.Stone);

            var bar = new GameObject("Bar");
            bar.transform.SetParent(root.transform, false);
            BoxCollider barBox = bar.AddComponent<BoxCollider>();
            barBox.center = new Vector3(0f, 0.65f, 0f);
            barBox.size = new Vector3(2.4f, 1.1f, 0.2f);
            MakeMovingBody(bar);
            Part(PrimitiveType.Cube, "BarLow", bar.transform, new Vector3(0f, 0.4f, 0f), new Vector3(2.4f, 0.16f, 0.16f), look.Metal);
            Part(PrimitiveType.Cube, "BarHigh", bar.transform, new Vector3(0f, 1f, 0f), new Vector3(2.4f, 0.16f, 0.16f), look.Metal);

            PuzzleDoor door = root.AddComponent<PuzzleDoor>();
            door.Control = DoorControl.Input;
            door.ConfigureMotion(bar.transform, new Vector3(0f, -1.25f, 0f), Vector3.zero, 1.2f, 1.2f);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, GatePrefabPath);
            Object.DestroyImmediate(root);
            report.AppendLine("Generated prefab 'CrystalGate' (a gate that is open while its input is active).");
            return prefab;
        }

        // ---------------------------------------------------------------- scene

        internal static GameObject FindGroup(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == GroupName)
                {
                    return root;
                }
            }

            return null;
        }

        /// <summary>
        /// Places the examples that are not in the scene yet, in a lane that starts at
        /// <paramref name="origin"/> and runs along the direction given by <paramref name="yaw"/>.
        /// Examples that are already there are kept where they are and as they are.
        /// </summary>
        internal static GameObject EnsureExamples(Scene scene, ExampleAssets assets, Vector3 origin, float yaw,
            StringBuilder report)
        {
            GameObject group = FindGroup(scene);
            if (group == null)
            {
                group = new GameObject(GroupName);
                SceneManager.MoveGameObjectToScene(group, scene);
                group.transform.SetPositionAndRotation(origin, Quaternion.Euler(0f, yaw, 0f));
                Undo.RegisterCreatedObjectUndo(group, DialogTitle);
            }

            int placed = 0;
            MagicalObject crystal = null;
            for (int i = 0; i < WorldMagicDefaults.Count; i++)
            {
                var example = (ExampleObject)i;
                string instanceName = WorldMagicDefaults.Name(example);
                Transform existing = group.transform.Find(instanceName);

                if (existing == null && assets.Prefabs[i] != null)
                {
                    existing = PlaceInGroup(assets.Prefabs[i], group.transform, instanceName, Lane[i]).transform;
                    placed++;
                }
                else if (existing == null)
                {
                    report.AppendLine($"WARNING: the prefab for '{instanceName}' could not be created, so it was not placed.");
                }

                if (example == ExampleObject.ArcaneCrystal && existing != null)
                {
                    crystal = existing.GetComponent<MagicalObject>();
                }
            }

            Transform gate = group.transform.Find(GateInstanceName);
            if (gate == null && assets.Gate != null)
            {
                gate = PlaceInGroup(assets.Gate, group.transform, GateInstanceName, GatePlacement).transform;
                placed++;
            }

            // The gate follows the crystal. Only an empty input is filled.
            if (gate != null && crystal != null && gate.TryGetComponent(out PuzzleDoor door) && door.InputElement == null &&
                crystal.TryGetComponent(out PropertyStateTrigger trigger))
            {
                Undo.RecordObject(door, DialogTitle);
                door.InputElement = trigger;
                MarkChanged(door);
                report.AppendLine("Connected the crystal gate to the arcane crystal: it is open while the crystal is activated.");
            }

            // The chain-reaction row: the same barricade, five times. They have no identifier, so a
            // restart brings them back and the chain can be tried again.
            GameObject barricadePrefab = assets.Prefabs[(int)ExampleObject.WoodenBarricade];
            for (int i = 0; i < ChainRowCount && barricadePrefab != null; i++)
            {
                string instanceName = $"{ChainInstanceName} {i + 1}";
                if (group.transform.Find(instanceName) == null)
                {
                    PlaceInGroup(barricadePrefab, group.transform, instanceName, ChainPlacement(i));
                    placed++;
                }
            }

            // The six examples keep what their definitions name, under an identifier of their own.
            int identified = 0;
            MagicalObject doorObject = null;
            for (int i = 0; i < WorldMagicDefaults.Count; i++)
            {
                var example = (ExampleObject)i;
                Transform instance = group.transform.Find(WorldMagicDefaults.Name(example));
                if (instance == null || !instance.TryGetComponent(out MagicalObject magical))
                {
                    continue;
                }

                if (example == ExampleObject.ReinforcedDoor)
                {
                    doorObject = magical;
                }

                bool keepsSomething = magical.Definition != null && magical.Definition.Properties.persisted != PersistedState.None;
                if (keepsSomething && string.IsNullOrEmpty(magical.PersistentId))
                {
                    Undo.RecordObject(magical, DialogTitle);
                    magical.FillPersistentId(MakePersistentId(scene, instance.name));
                    MarkChanged(magical);
                    identified++;
                }
            }

            if (identified > 0)
            {
                report.AppendLine($"Gave {identified} example object(s) a persistent id: what their definitions name is saved.");
            }

            // The crystal feeds the door's lock: activating it gives the lock a head start.
            if (crystal != null && doorObject != null && crystal.LinkedTargetCount == 0)
            {
                Undo.RecordObject(crystal, DialogTitle);
                crystal.FillLinkedTargets(new[] { doorObject.gameObject });
                MarkChanged(crystal);
                report.AppendLine("Linked the arcane crystal to the reinforced door: when it activates, it charges the door's lock.");
            }

            // The budgets of the world simulation, where they can be tuned for this scene.
            if (group.GetComponent<WorldSimulationBudgets>() == null)
            {
                Undo.AddComponent<WorldSimulationBudgets>(group);
                report.AppendLine($"Added the world simulation budgets to '{GroupName}' (provisional values).");
            }

            report.AppendLine(placed > 0
                ? $"Placed {placed} example object(s) in '{GroupName}'."
                : $"Kept the example objects in '{GroupName}'.");
            return group;
        }

        /// <summary>An identifier from the scene and the object's name: readable, and stable as long as neither is renamed.</summary>
        internal static string MakePersistentId(Scene scene, string objectName)
        {
            string sceneName = string.IsNullOrEmpty(scene.name) ? "Scene" : scene.name;
            return sceneName + "/" + objectName;
        }

        // -------------------------------------------------------- identifiers

        /// <summary>
        /// Gives every selected magical object that has none a persistent id. Ids that exist are never
        /// changed: changing one would orphan what was saved under it.
        /// </summary>
        [MenuItem(AssignIdsMenuPath, false, 61)]
        public static void AssignPersistentIds()
        {
            Scene scene = SceneManager.GetActiveScene();
            var targets = new List<MagicalObject>();
            foreach (GameObject selected in Selection.gameObjects)
            {
                if (!EditorUtility.IsPersistent(selected) && selected.TryGetComponent(out MagicalObject magical))
                {
                    targets.Add(magical);
                }
            }

            // Which objects keep their state is a decision per object: a practice target is meant to come
            // back. So nothing is assigned without a selection.
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "Select the magical objects in the scene that should keep their state, then run this command again. " +
                    "Objects without an id are as new after every reload, which is what a practice target wants.", "OK");
                return;
            }

            var report = new StringBuilder();
            int assigned = AssignPersistentIds(scene, targets, report);
            if (assigned > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            string summary = assigned > 0
                ? report.ToString().TrimEnd() + "\n\nSave the scene with Ctrl+S."
                : "Every selected magical object already has a persistent id.";
            Debug.Log($"[Threefold] Assign Persistent Ids.\n{summary}");
            EditorUtility.DisplayDialog(DialogTitle, summary, "OK");
        }

        [MenuItem(AssignIdsMenuPath, true)]
        private static bool CanAssignPersistentIds()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && Selection.gameObjects.Length > 0;
        }

        /// <summary>Fills empty ids so that every id in the scene is unique. Returns how many were assigned.</summary>
        internal static int AssignPersistentIds(Scene scene, List<MagicalObject> targets, StringBuilder report)
        {
            var taken = new HashSet<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MagicalObject magical in root.GetComponentsInChildren<MagicalObject>(true))
                {
                    if (!string.IsNullOrEmpty(magical.PersistentId))
                    {
                        taken.Add(magical.PersistentId);
                    }
                }
            }

            int assigned = 0;
            foreach (MagicalObject magical in targets)
            {
                if (magical == null || !string.IsNullOrEmpty(magical.PersistentId))
                {
                    continue;
                }

                string wanted = MakePersistentId(scene, magical.name);
                string id = wanted;
                for (int suffix = 2; taken.Contains(id); suffix++)
                {
                    id = $"{wanted} ({suffix})";
                }

                Undo.RecordObject(magical, DialogTitle);
                magical.FillPersistentId(id);
                MarkChanged(magical);
                taken.Add(id);
                assigned++;
                report.AppendLine($"'{magical.name}': {id}");
            }

            return assigned;
        }

        internal static GameObject PlaceInGroup(GameObject prefab, Transform group, string instanceName, ExamplePlacement placement)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            instance.name = instanceName;
            instance.transform.localPosition = placement.Position;
            instance.transform.localRotation = Quaternion.Euler(0f, placement.Yaw, 0f);
            Undo.RegisterCreatedObjectUndo(instance, DialogTitle);
            return instance;
        }

        // -------------------------------------------------------------- helpers

        internal static Transform Group(Transform parent, string name)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        /// <summary>A visual part without a collider.</summary>
        internal static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale,
            Material material)
        {
            return CombatSetup.CreatePart(type, name, parent, position, scale, material);
        }

        internal static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale,
            Material material, Vector3 eulerAngles)
        {
            GameObject part = CombatSetup.CreatePart(type, name, parent, position, scale, material);
            part.transform.localRotation = Quaternion.Euler(eulerAngles);
            return part;
        }

        /// <summary>A block that is seen and is solid.</summary>
        internal static GameObject SolidPart(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = CombatSetup.CreatePart(PrimitiveType.Cube, name, parent, position, scale, material);
            part.AddComponent<BoxCollider>();
            return part;
        }

        /// <summary>
        /// A collider that moves is given a kinematic body, as the puzzle doors are, so that physics
        /// treats it as moving. The magic itself does not need one.
        /// </summary>
        internal static void MakeMovingBody(GameObject part)
        {
            Rigidbody body = part.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        internal static Renderer[] Renderers(params GameObject[] parts)
        {
            var renderers = new Renderer[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                renderers[i] = parts[i].GetComponent<Renderer>();
            }

            return renderers;
        }

        internal static ConditionTint Tint(PropertyCondition when, Color color, float strength)
        {
            return new ConditionTint { when = when, color = color, strength = strength };
        }

        internal static ConditionObjects Shown(PropertyCondition when, bool invert, params GameObject[] objects)
        {
            return new ConditionObjects { when = when, invert = invert, objects = objects };
        }

        internal static ConditionEffect Effect(PropertyCondition onEnter, PooledObject effect)
        {
            return new ConditionEffect { onEnter = onEnter, effect = effect };
        }
    }
}
