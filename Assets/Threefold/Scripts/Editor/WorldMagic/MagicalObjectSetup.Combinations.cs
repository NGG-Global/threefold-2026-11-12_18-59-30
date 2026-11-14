using System.Collections.Generic;
using System.IO;
using System.Text;
using Threefold.Combat;
using Threefold.Discovery;
using Threefold.Enemies;
using Threefold.Persistence;
using Threefold.Puzzles;
using Threefold.WorldMagic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// The combination court of Sprint LM-06, in the Discipline Range: three objects whose properties
    /// call for a second discipline (a rune-locked slide, a crumbling walkway, dry kindling beside a
    /// barricade), a ledge the walkway leads up to, and a Cinder Wisp on the ledge whose fiery motes
    /// the Aegis guard can turn into what burns. Built from <see cref="CombinationDefaults"/> and the
    /// enemy assets; nothing in it has a script of its own.
    ///
    /// Safe to run repeatedly: what is already in the court is kept as it is, and only what is missing
    /// is added. The navigation mesh of the range is baked again on every run.
    /// </summary>
    public static partial class MagicalObjectSetup
    {
        internal const string CourtName = "CombinationCourt";
        internal const string CourtLedgeName = "Ledge";
        internal const string CourtGateName = "Slide Gate";
        internal const string CourtBarricadeName = "Court Barricade";
        internal const string CourtWispName = "Cinder Wisp";
        internal const string RangeNavMeshPath = "Assets/Threefold/Generated/Navigation/DisciplineRangeNavMesh.asset";

        /// <summary>The court in the Discipline Range: along the right edge of its floor, facing the centre.</summary>
        internal static readonly Vector3 RangeCourtOrigin = new Vector3(9.5f, 0f, -6f);

        private static readonly Color RuneColor = new Color(0.42f, 0.46f, 0.74f, 1f);
        private static readonly Color StrainColor = new Color(0.62f, 0.32f, 0.22f, 1f);
        private static readonly Color BrushColor = new Color(0.66f, 0.55f, 0.3f, 1f);

        /// <summary>Where each object stands in the court, indexed by <see cref="CombinationExample"/>.</summary>
        internal static readonly ExamplePlacement[] Court =
        {
            new ExamplePlacement(new Vector3(0f, 0f, 0f), 0f),       // Rune-locked slide: its rail runs 5 m along the edge
            new ExamplePlacement(new Vector3(0f, 0f, 17f), 0f),      // Crumbling walkway: rises along the edge to the ledge
            new ExamplePlacement(new Vector3(-1.6f, 0f, 13f), 0f),   // Dry kindling, in front of the barricade
        };

        internal static readonly ExamplePlacement CourtGatePlacement = new ExamplePlacement(new Vector3(0f, 0f, 8.6f), 0f);
        internal static readonly ExamplePlacement CourtBarricadePlacement = new ExamplePlacement(new Vector3(0f, 0f, 13f), 90f);

        /// <summary>How far the walkway runs along the ground before it meets the ledge.</summary>
        internal static float WalkwayRun =>
            Mathf.Sqrt(CombinationDefaults.WalkwayLength * CombinationDefaults.WalkwayLength -
                       CombinationDefaults.WalkwayRise * CombinationDefaults.WalkwayRise);

        internal static Vector3 LedgeCentre => new Vector3(0f, CombinationDefaults.WalkwayRise * 0.5f, 17f + WalkwayRun + 2.5f);
        internal static readonly Vector3 LedgeSize = new Vector3(5f, CombinationDefaults.WalkwayRise, 5f);

        /// <summary>The generated definitions and prefabs of the court, indexed by <see cref="CombinationExample"/>.</summary>
        internal sealed class CombinationAssets
        {
            public readonly MagicalObjectDefinition[] Definitions = new MagicalObjectDefinition[CombinationDefaults.Count];
            public readonly GameObject[] Prefabs = new GameObject[CombinationDefaults.Count];
        }

        internal static string DefinitionPath(CombinationExample example)
        {
            return $"{Folder}/{example}Definition.asset";
        }

        internal static string PrefabPath(CombinationExample example)
        {
            return $"{Folder}/{example}.prefab";
        }

        /// <summary>The materials and bursts the court's objects use, generated if missing (they are shared with the six examples).</summary>
        private static Look CourtLook(StringBuilder report)
        {
            CombatSetup.SharedAssets shared = CombatSetup.EnsureSharedAssets(report);
            Look look = default;
            look.Stone = LitMaterial("ExampleStone", StoneColor, report);
            look.Platform = LitMaterial("ExamplePlatform", PlatformColor, report);
            look.Metal = LitMaterial("ExampleMetal", MetalColor, report);
            look.Wood = LitMaterial("ExampleWood", WoodColor, report);
            look.Charred = LitMaterial("ExampleCharred", CharredColor, report);
            look.Crystal = LitMaterial("ExampleCrystal", CrystalColor, report);
            look.Straw = LitMaterial("ExampleStraw", new Color(0.78f, 0.68f, 0.4f, 1f), report);
            look.Sandstone = LitMaterial("ExampleBrush", BrushColor, report);
            look.PaleStone = LitMaterial("ExampleRune", RuneColor, report);
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
            look.Mote = CombatSetup.EnsureEffectPrefab($"{Folder}/DiscoveryMote.prefab", "DiscoveryMote", shared.Glow,
                new Color(1f, 0.97f, 0.88f, 1f), 3, 0.4f, report);
            return look;
        }

        internal static CombinationAssets EnsureCombinationAssets(StringBuilder report)
        {
            CombatSetup.EnsureFolder(Folder);
            Look look = CourtLook(report);

            var assets = new CombinationAssets();
            for (int i = 0; i < CombinationDefaults.Count; i++)
            {
                var example = (CombinationExample)i;
                assets.Definitions[i] = EnsureCombinationDefinition(example, report);
                assets.Prefabs[i] = EnsureCombinationPrefab(example, assets.Definitions[i], look, report);
            }

            return assets;
        }

        private static MagicalObjectDefinition EnsureCombinationDefinition(CombinationExample example, StringBuilder report)
        {
            string path = DefinitionPath(example);
            var definition = AssetDatabase.LoadAssetAtPath<MagicalObjectDefinition>(path);
            if (definition != null)
            {
                return definition;
            }

            definition = ScriptableObject.CreateInstance<MagicalObjectDefinition>();
            definition.Configure(CombinationDefaults.Name(example), CombinationDefaults.Description(example),
                CombinationDefaults.Settings(example));
            AssetDatabase.CreateAsset(definition, path);
            report.AppendLine($"Generated definition '{CombinationDefaults.Name(example)}' ({Describe(definition.Properties)}).");
            return definition;
        }

        private static GameObject EnsureCombinationPrefab(CombinationExample example, MagicalObjectDefinition definition, Look look,
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
                case CombinationExample.RuneLockedSlide:
                    root = BuildRuneLockedSlide(definition, look);
                    break;
                case CombinationExample.CrumblingWalkway:
                    root = BuildCrumblingWalkway(definition, look);
                    break;
                default:
                    root = BuildDryKindling(definition, look);
                    break;
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            report.AppendLine($"Generated prefab '{example}'.");
            return prefab;
        }

        /// <summary>
        /// A block on a rail beside a rune lock on a post. The lock brightens as it charges and lights
        /// while activated; only then does force slide the block. A trigger follows the block's far stop.
        /// </summary>
        private static GameObject BuildRuneLockedSlide(MagicalObjectDefinition definition, Look look)
        {
            var root = new GameObject(CombinationExample.RuneLockedSlide.ToString());
            KineticSettings kinetic = definition.Properties.kineticSettings;
            float travel = Mathf.Max(1f, kinetic.maxTravel - kinetic.minTravel);
            float middle = (kinetic.maxTravel + kinetic.minTravel) * 0.5f;

            Transform rail = Group(root.transform, "Rail");
            Part(PrimitiveType.Cube, "Track", rail, new Vector3(0f, 0.03f, middle), new Vector3(0.35f, 0.06f, travel + 2.3f), look.Metal);
            Part(PrimitiveType.Cube, "StopNear", rail, new Vector3(0f, 0.15f, kinetic.minTravel - 1.15f),
                new Vector3(1.2f, 0.3f, 0.2f), look.Metal);
            Part(PrimitiveType.Cube, "StopFar", rail, new Vector3(0f, 0.15f, kinetic.maxTravel + 1.15f),
                new Vector3(1.2f, 0.3f, 0.2f), look.Metal);

            var block = new GameObject("Block");
            block.transform.SetParent(root.transform, false);
            BoxCollider body = block.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 0.6f, 0f);
            body.size = new Vector3(1.8f, 1.2f, 1.8f);
            MakeMovingBody(block);
            GameObject stone = Part(PrimitiveType.Cube, "Stone", block.transform, new Vector3(0f, 0.6f, 0f), new Vector3(1.8f, 1.2f, 1.8f),
                look.Platform);
            GameObject rune = Part(PrimitiveType.Cube, "Rune", block.transform, new Vector3(-0.91f, 0.65f, 0f),
                new Vector3(0.03f, 0.5f, 0.5f), look.PaleStone, new Vector3(0f, 0f, 0f));

            // The lock stands beside the rail, on the side the player walks; arrows strike it.
            var lockPost = new GameObject("Lock");
            lockPost.transform.SetParent(root.transform, false);
            lockPost.transform.localPosition = new Vector3(-1.5f, 0f, -0.6f);
            BoxCollider lockBox = lockPost.AddComponent<BoxCollider>();
            lockBox.center = new Vector3(0f, 0.75f, 0f);
            lockBox.size = new Vector3(0.6f, 1.5f, 0.6f);
            Part(PrimitiveType.Cube, "Post", lockPost.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.45f, 1f, 0.45f), look.Stone);
            GameObject crystal = Part(PrimitiveType.Cube, "Crystal", lockPost.transform, new Vector3(0f, 1.25f, 0f),
                new Vector3(0.28f, 0.5f, 0.28f), look.Crystal, new Vector3(0f, 45f, 0f));
            GameObject glow = Part(PrimitiveType.Sphere, "Lit", lockPost.transform, new Vector3(0f, 1.65f, 0f),
                new Vector3(0.24f, 0.24f, 0.24f), look.Beacon);
            glow.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            glow.SetActive(false);

            MagicalObject magical = root.AddComponent<MagicalObject>();
            magical.Bind(definition, block.transform, new Collider[] { body, lockBox });
            magical.SetAimPoint(new Vector3(0f, 0.6f, 0f));

            // A puzzle can follow the block reaching its far stop: the court's gate opens for good.
            root.AddComponent<PropertyStateTrigger>().Configure(magical, PropertyCondition.AtTravelLimit, true);

            PropertyStateVisuals visuals = root.AddComponent<PropertyStateVisuals>();
            visuals.Bind(magical, Renderers(stone), Renderers(crystal, rune));
            visuals.Configure(
                new[] { Tint(PropertyCondition.Moving, MovingColor, 0.25f) },
                new[] { Shown(PropertyCondition.Activated, false, glow) },
                new[]
                {
                    Effect(PropertyCondition.Activated, look.Charge), Effect(PropertyCondition.Moving, look.Dust),
                    Effect(PropertyCondition.AtTravelLimit, look.Dust),
                },
                null);

            DiscoveryClues clues = root.AddComponent<DiscoveryClues>();
            clues.Bind(magical, visuals, lockPost.transform);
            clues.Configure(0.22f, new Color(0.78f, 0.84f, 1f, 1f), PropertyCondition.AtTravelLimit, look.Mote);
            return root;
        }

        /// <summary>
        /// A plank walkway rising to a ledge, on worn supports. It sways, strains under weight, turns gold
        /// while braced, and drops to the ground when it gives way. A trigger over the planks counts who
        /// stands on it.
        /// </summary>
        private static GameObject BuildCrumblingWalkway(MagicalObjectDefinition definition, Look look)
        {
            var root = new GameObject(CombinationExample.CrumblingWalkway.ToString());
            float length = CombinationDefaults.WalkwayLength;
            float rise = CombinationDefaults.WalkwayRise;
            float run = WalkwayRun;
            float pitch = Mathf.Asin(rise / length) * Mathf.Rad2Deg;

            // The deck is tilted as a whole; its collider is the walking surface and falls with it.
            var deck = new GameObject("Deck");
            deck.transform.SetParent(root.transform, false);
            deck.transform.localPosition = new Vector3(0f, rise * 0.5f, run * 0.5f);
            deck.transform.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
            BoxCollider surface = deck.AddComponent<BoxCollider>();
            surface.center = Vector3.zero;
            surface.size = new Vector3(1.8f, 0.25f, length);
            MakeMovingBody(deck);

            Transform sway = Group(deck.transform, "Sway");
            int planks = 9;
            for (int i = 0; i < planks; i++)
            {
                float z = -length * 0.5f + (i + 0.5f) * (length / planks);
                Part(PrimitiveType.Cube, "Plank", sway, new Vector3(0f, 0f, z), new Vector3(1.8f, 0.2f, length / planks - 0.06f),
                    look.Wood, new Vector3(0f, (i % 3 - 1) * 2.5f, 0f));
            }

            Part(PrimitiveType.Cube, "RailLeft", sway, new Vector3(-0.85f, 0.18f, 0f), new Vector3(0.08f, 0.12f, length), look.Charred);
            Part(PrimitiveType.Cube, "RailRight", sway, new Vector3(0.85f, 0.18f, 0f), new Vector3(0.08f, 0.12f, length), look.Charred);

            // Supports under the lower half: they are scenery and do not fall.
            Transform supports = Group(root.transform, "Supports");
            for (int i = 1; i <= 2; i++)
            {
                float z = run * i / 3f;
                float height = rise * i / 3f;
                Part(PrimitiveType.Cube, "Support", supports, new Vector3(-0.7f, height * 0.5f, z), new Vector3(0.14f, height, 0.14f),
                    look.Charred, new Vector3(0f, 0f, 4f * i));
                Part(PrimitiveType.Cube, "Support", supports, new Vector3(0.7f, height * 0.5f, z), new Vector3(0.14f, height, 0.14f),
                    look.Charred, new Vector3(0f, 0f, -6f * i));
            }

            // Weight: a trigger over the planks counts who stands on them.
            var zoneObject = new GameObject("LoadZone");
            zoneObject.transform.SetParent(deck.transform, false);
            BoxCollider zoneBox = zoneObject.AddComponent<BoxCollider>();
            zoneBox.isTrigger = true;
            zoneBox.center = new Vector3(0f, 0.9f, 0f);
            zoneBox.size = new Vector3(1.8f, 1.6f, length);
            LoadZone zone = zoneObject.AddComponent<LoadZone>();

            MagicalObject magical = root.AddComponent<MagicalObject>();
            magical.Bind(definition, null, new Collider[] { surface });
            magical.SetAimPoint(new Vector3(0f, rise * 0.4f, run * 0.4f));
            magical.BindLoadZone(zone);
            zone.Bind(magical);

            PropertyStateVisuals visuals = root.AddComponent<PropertyStateVisuals>();
            visuals.Bind(magical, sway.GetComponentsInChildren<Renderer>(true), null);
            visuals.Configure(
                new[]
                {
                    Tint(PropertyCondition.Loaded, StrainColor, 0.35f), Tint(PropertyCondition.Braced, BracedColor, 0.45f),
                    Tint(PropertyCondition.Collapsed, CrackedColor, 0.3f),
                },
                null,
                new[]
                {
                    Effect(PropertyCondition.Loaded, look.Dust), Effect(PropertyCondition.Braced, look.Brace),
                    Effect(PropertyCondition.Collapsed, look.Dust),
                },
                null);

            // It drops: the deck falls flat onto the ground at the foot of the ledge.
            visuals.ConfigureMotion(sway, 1.2f, deck.transform, new Vector3(0.2f, -rise * 0.5f + 0.15f, 0f),
                new Vector3(pitch, 0f, 6f));

            DiscoveryClues clues = root.AddComponent<DiscoveryClues>();
            clues.Bind(magical, visuals, sway);
            clues.Configure(0.08f, new Color(1f, 0.95f, 0.8f, 1f), PropertyCondition.Collapsed, look.Mote);
            return root;
        }

        /// <summary>A low heap of dry brush and straw. It catches fire at a touch of heat, burns away and leaves ash.</summary>
        private static GameObject BuildDryKindling(MagicalObjectDefinition definition, Look look)
        {
            var root = new GameObject(CombinationExample.DryKindling.ToString());

            BoxCollider body = root.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 0.35f, 0f);
            body.size = new Vector3(1.4f, 0.7f, 1.4f);

            Transform whole = Group(root.transform, "Whole");
            Part(PrimitiveType.Cube, "Brush", whole, new Vector3(0f, 0.25f, 0f), new Vector3(1.2f, 0.5f, 1.1f), look.Sandstone,
                new Vector3(0f, 20f, 0f));
            Part(PrimitiveType.Cube, "Brush", whole, new Vector3(0.2f, 0.5f, -0.1f), new Vector3(0.8f, 0.4f, 0.7f), look.Sandstone,
                new Vector3(8f, -25f, 6f));
            Part(PrimitiveType.Cube, "Straw", whole, new Vector3(-0.35f, 0.15f, 0.4f), new Vector3(0.6f, 0.3f, 0.35f), look.Straw,
                new Vector3(0f, 35f, 0f));
            Part(PrimitiveType.Cube, "Twig", whole, new Vector3(0.1f, 0.62f, 0.2f), new Vector3(1f, 0.06f, 0.06f), look.Wood,
                new Vector3(0f, 30f, 18f));

            Transform remains = Group(root.transform, "Remains");
            Part(PrimitiveType.Cube, "Ash", remains, new Vector3(0f, 0.03f, 0f), new Vector3(1.2f, 0.06f, 1f), look.Charred,
                new Vector3(0f, 15f, 0f));
            remains.gameObject.SetActive(false);

            MagicalObject magical = root.AddComponent<MagicalObject>();
            magical.Bind(definition, null, new Collider[] { body });
            magical.SetAimPoint(new Vector3(0f, 0.4f, 0f));

            PropertyStateVisuals visuals = root.AddComponent<PropertyStateVisuals>();
            visuals.Bind(magical, whole.GetComponentsInChildren<Renderer>(true), null);
            visuals.Configure(
                new[] { Tint(PropertyCondition.Heated, CharredColor, 0.2f), Tint(PropertyCondition.Weakened, CharredColor, 0.55f) },
                new[]
                {
                    Shown(PropertyCondition.Destroyed, true, whole.gameObject),
                    Shown(PropertyCondition.Destroyed, false, remains.gameObject),
                },
                new[] { Effect(PropertyCondition.Burning, look.Embers), Effect(PropertyCondition.Destroyed, look.Dust) },
                look.Embers);

            DiscoveryClues clues = root.AddComponent<DiscoveryClues>();
            clues.Bind(magical, visuals, whole);
            clues.Configure(0.08f, new Color(1f, 0.9f, 0.7f, 1f), PropertyCondition.Destroyed | PropertyCondition.Burning, look.Mote);
            clues.FillDestroyedMotionTarget(remains);
            return root;
        }

        // ---------------------------------------------------------------- scene

        internal static GameObject FindCourt(Scene scene)
        {
            return FindByName(scene, CourtName);
        }

        /// <summary>
        /// Places what the court lacks: the three objects, the gate the slide opens, the barricade behind
        /// the kindling, the ledge, and a Cinder Wisp on the ledge with the director that paces it. Then
        /// bakes the range's navigation mesh from the range's architecture and the ledge.
        /// </summary>
        internal static GameObject EnsureCombinationCourt(Scene scene, CombinationAssets assets, ExampleAssets examples,
            EnemyAssets enemies, GameObject mage, Transform rangeArchitecture, StringBuilder report)
        {
            GameObject court = FindCourt(scene);
            if (court == null)
            {
                court = new GameObject(CourtName);
                SceneManager.MoveGameObjectToScene(court, scene);
                court.transform.SetPositionAndRotation(RangeCourtOrigin, Quaternion.identity);
                Undo.RegisterCreatedObjectUndo(court, DialogTitle);
            }

            int placed = 0;
            for (int i = 0; i < CombinationDefaults.Count; i++)
            {
                var example = (CombinationExample)i;
                string instanceName = CombinationDefaults.Name(example);
                if (court.transform.Find(instanceName) == null && assets.Prefabs[i] != null)
                {
                    PlaceInGroup(assets.Prefabs[i], court.transform, instanceName, Court[i]);
                    placed++;
                }
            }

            Transform slide = court.transform.Find(CombinationDefaults.Name(CombinationExample.RuneLockedSlide));
            Transform gate = court.transform.Find(CourtGateName);
            if (gate == null && examples.Gate != null)
            {
                gate = PlaceInGroup(examples.Gate, court.transform, CourtGateName, CourtGatePlacement).transform;
                placed++;
            }

            // The gate opens for good once the block has been slid to its far stop. Only an empty input is filled.
            if (gate != null && slide != null && gate.TryGetComponent(out PuzzleDoor door) && door.InputElement == null &&
                slide.TryGetComponent(out PropertyStateTrigger trigger))
            {
                Undo.RecordObject(door, DialogTitle);
                door.InputElement = trigger;
                MarkChanged(door);
                report.AppendLine("Connected the court gate to the rune-locked slide: it opens when the block reaches its far stop.");
            }

            GameObject barricadePrefab = examples.Prefabs[(int)ExampleObject.WoodenBarricade];
            if (court.transform.Find(CourtBarricadeName) == null && barricadePrefab != null)
            {
                PlaceInGroup(barricadePrefab, court.transform, CourtBarricadeName, CourtBarricadePlacement);
                placed++;
            }

            // Where the slide was left is kept, under an identifier of its own.
            if (slide != null && slide.TryGetComponent(out MagicalObject slideObject) && string.IsNullOrEmpty(slideObject.PersistentId) &&
                slideObject.Definition != null && slideObject.Definition.Properties.persisted != PersistedState.None)
            {
                Undo.RecordObject(slideObject, DialogTitle);
                slideObject.FillPersistentId(MakePersistentId(scene, slide.name));
                MarkChanged(slideObject);
                report.AppendLine("Gave the rune-locked slide a persistent id: where it was slid is saved.");
            }

            Transform ledge = court.transform.Find(CourtLedgeName);
            if (ledge == null)
            {
                ledge = CombatSetup.CreatePart(PrimitiveType.Cube, CourtLedgeName, court.transform, LedgeCentre, LedgeSize,
                    LitMaterial("ExampleStone", StoneColor, report)).transform;
                ledge.gameObject.AddComponent<BoxCollider>();
                ledge.gameObject.isStatic = true;
                Undo.RegisterCreatedObjectUndo(ledge.gameObject, DialogTitle);
                placed++;
            }

            if (enemies != null && enemies.CinderPrefab != null && court.transform.Find(CourtWispName) == null)
            {
                var wisp = (GameObject)PrefabUtility.InstantiatePrefab(enemies.CinderPrefab, court.transform);
                wisp.name = CourtWispName;
                wisp.transform.localPosition = new Vector3(0f, CombinationDefaults.WalkwayRise, LedgeCentre.z);
                wisp.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                Undo.RegisterCreatedObjectUndo(wisp, DialogTitle);
                placed++;
                report.AppendLine("Placed a Cinder Wisp on the ledge. Its motes carry heat; the Aegis guard turns them aside.");
            }

            if (enemies != null && mage != null && court.transform.Find(CourtWispName) != null)
            {
                CombatArenaSetup.EnsureDirector(scene, mage, enemies, report);
            }

            BakeRangeNavMesh(rangeArchitecture, ledge, report);

            report.AppendLine(placed > 0
                ? $"Placed {placed} part(s) of the combination court in '{CourtName}'."
                : $"Kept the combination court in '{CourtName}'.");
            return court;
        }

        /// <summary>
        /// Bakes the range's walkable area from its architecture and the ledge, and stores it as an asset
        /// that a NavMeshSurface on the range's architecture loads with the scene, as in the Combat Arena.
        /// The existing asset is updated in place. Magical objects, dummies and the Mage are not sources,
        /// so they cut no holes in it.
        /// </summary>
        private static void BakeRangeNavMesh(Transform rangeArchitecture, Transform ledge, StringBuilder report)
        {
            if (rangeArchitecture == null)
            {
                report.AppendLine("WARNING: no range architecture to carry the navigation mesh; the Cinder Wisp will not move.");
                return;
            }

            // The architecture stands at the world's origin, so the data, built in world space, lands where it was built.
            NavMeshSurface surface = rangeArchitecture.GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                surface = Undo.AddComponent<NavMeshSurface>(rangeArchitecture.gameObject);
            }

            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            foreach (Transform root in new[] { rangeArchitecture, ledge })
            {
                if (root == null)
                {
                    continue;
                }

                var found = new List<NavMeshBuildSource>();
                UnityEngine.AI.NavMeshBuilder.CollectSources(root, surface.layerMask, NavMeshCollectGeometry.PhysicsColliders,
                    surface.defaultArea, markups, found);
                sources.AddRange(found);
            }

            NavMeshBuildSettings settings = surface.GetBuildSettings();
            var bounds = new Bounds(new Vector3(0f, 3f, 12f), new Vector3(30f, 10f, 60f));

            CreateFolders(Path.GetDirectoryName(RangeNavMeshPath).Replace('\\', '/'));
            NavMeshData data = AssetDatabase.LoadAssetAtPath<NavMeshData>(RangeNavMeshPath);
            bool created = data == null;
            if (created)
            {
                data = new NavMeshData(settings.agentTypeID) { name = Path.GetFileNameWithoutExtension(RangeNavMeshPath) };
            }

            bool built = UnityEngine.AI.NavMeshBuilder.UpdateNavMeshData(data, settings, sources, bounds);
            if (created)
            {
                AssetDatabase.CreateAsset(data, RangeNavMeshPath);
            }
            else
            {
                EditorUtility.SetDirty(data);
            }

            surface.navMeshData = data;
            surface.RemoveData();
            surface.AddData();
            MarkChanged(surface);

            report.AppendLine(built
                ? $"Baked the range's navigation mesh from {sources.Count} collider(s) into '{RangeNavMeshPath}'."
                : "WARNING: the range's navigation mesh could not be built. The Cinder Wisp will not move.");
        }
    }
}
