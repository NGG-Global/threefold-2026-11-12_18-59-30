using System.Collections.Generic;
using System.Text;
using Threefold.Combat;
using Threefold.Discovery;
using Threefold.Levels;
using Threefold.Pooling;
using Threefold.Puzzles;
using Threefold.WorldMagic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using M = Threefold.EditorTools.MagicalObjectSetup;

namespace Threefold.EditorTools
{
    /// <summary>
    /// The assets of the Broken Foundry: the definitions of its three new kinds of object (from
    /// <see cref="FoundryDefaults"/>), and prefabs for the dormant crystal, the furnace, the slag
    /// bridge, the crumbling gantry, the damaged barricade, the slag plug, the foundry gate and a few
    /// pieces of machinery. The damaged barricade and the slag plug use the definitions of LM-03's
    /// wooden barricade and heat-sensitive stone: same properties, different shape.
    ///
    /// Everything is built from primitive shapes in code. Generated once; existing assets are kept, so
    /// tuned values survive. Delete <c>Assets/Threefold/Generated/Foundry</c> to generate them again.
    /// </summary>
    internal sealed class BrokenFoundryAssets
    {
        internal const string Folder = "Assets/Threefold/Generated/Foundry";
        internal const string DecorFolder = Folder + "/Decor";
        private const string DialogTitle = "Broken Foundry";

        private static readonly Color CrystalColor = new Color(0.36f, 0.42f, 0.7f, 1f);
        private static readonly Color BeaconColor = new Color(0.85f, 0.9f, 1f, 1f);
        private static readonly Color EmberColor = new Color(1f, 0.52f, 0.2f, 1f);
        private static readonly Color SlagColor = new Color(0.98f, 0.42f, 0.12f, 1f);
        private static readonly Color CharredColor = new Color(0.12f, 0.1f, 0.09f, 1f);
        private static readonly Color SlagRockColor = new Color(0.22f, 0.2f, 0.2f, 1f);
        private static readonly Color StrawColor = new Color(0.78f, 0.68f, 0.4f, 1f);
        private static readonly Color RuneColor = new Color(0.42f, 0.46f, 0.74f, 1f);
        private static readonly Color StoneColor = new Color(0.47f, 0.45f, 0.42f, 1f);
        private static readonly Color DarkStoneColor = new Color(0.3f, 0.29f, 0.29f, 1f);
        private static readonly Color WoodColor = new Color(0.5f, 0.37f, 0.24f, 1f);
        private static readonly Color MetalColor = new Color(0.25f, 0.25f, 0.28f, 1f);
        private static readonly Color CrackedColor = new Color(0.3f, 0.28f, 0.27f, 1f);
        private static readonly Color BracedColor = new Color(1f, 0.86f, 0.5f, 1f);
        private static readonly Color StrainColor = new Color(0.62f, 0.32f, 0.22f, 1f);

        public readonly MagicalObjectDefinition[] Definitions = new MagicalObjectDefinition[FoundryDefaults.Count];
        public GameObject DormantCrystal;
        public GameObject Furnace;
        public GameObject SlagBridge;
        public GameObject Gantry;
        public GameObject DamagedBarricade;
        public GameObject SlagPlug;
        public GameObject Gate;
        public GameObject OreCart;
        public GameObject GearWheel;
        public GameObject Crucible;
        public GameObject Chain;

        public Material Stone;
        public Material DarkStone;
        public Material Wood;
        public Material Metal;
        public Material Crystal;
        public Material Beacon;
        public Material Ember;
        public Material Slag;
        public Material Charred;
        public Material SlagRock;
        public Material Straw;
        public Material Rune;

        public PooledObject Embers;
        public PooledObject Dust;
        public PooledObject Brace;
        public PooledObject Charge;
        public PooledObject Mote;

        public MagicalObjectDefinition Definition(FoundryObject kind) => Definitions[(int)kind];

        internal static string DefinitionPath(FoundryObject kind) => $"{Folder}/{kind}Definition.asset";

        internal static BrokenFoundryAssets Ensure(M.ExampleAssets examples, StringBuilder report)
        {
            CombatSetup.EnsureFolder(Folder);
            CombatSetup.EnsureFolder(DecorFolder);

            var assets = new BrokenFoundryAssets();
            assets.EnsureMaterials(report);

            for (int i = 0; i < FoundryDefaults.Count; i++)
            {
                assets.Definitions[i] = EnsureDefinition((FoundryObject)i, report);
            }

            MagicalObjectDefinition barricade = examples.Definitions[(int)ExampleObject.WoodenBarricade];
            MagicalObjectDefinition stone = examples.Definitions[(int)ExampleObject.HeatSensitiveStone];
            MagicalObjectDefinition gantry = assets.Definition(FoundryObject.CrumblingGantry);

            assets.DormantCrystal = EnsurePrefab("DormantCrystal", () => assets.BuildDormantCrystal(assets.Definition(FoundryObject.DormantCrystal)), report);
            assets.Furnace = EnsurePrefab("Furnace", () => assets.BuildFurnace(assets.Definition(FoundryObject.Furnace)), report);
            assets.SlagBridge = EnsurePrefab("SlagBridge",
                () => assets.BuildGantry("SlagBridge", gantry, FoundryLayout.BridgeLength, 0f, FoundryLayout.PitZ.y - FoundryLayout.PitZ.x), report);
            assets.Gantry = EnsurePrefab("CrumblingGantry",
                () => assets.BuildGantry("CrumblingGantry", gantry, FoundryLayout.GantryLength, FoundryLayout.GantryRise, FoundryLayout.GantryLength),
                report);
            assets.DamagedBarricade = EnsurePrefab("DamagedBarricade", () => assets.BuildDamagedBarricade(barricade), report);
            assets.SlagPlug = EnsurePrefab("SlagPlug", () => assets.BuildSlagPlug(stone), report);
            assets.Gate = EnsurePrefab("FoundryGate", assets.BuildGate, report);
            assets.OreCart = EnsurePrefab("Decor/OreCart", assets.BuildOreCart, report);
            assets.GearWheel = EnsurePrefab("Decor/GearWheel", assets.BuildGearWheel, report);
            assets.Crucible = EnsurePrefab("Decor/Crucible", assets.BuildCrucible, report);
            assets.Chain = EnsurePrefab("Decor/HangingChain", assets.BuildChain, report);

            report.AppendLine($"Broken Foundry assets are in '{Folder}'. New files there exist only on this computer until they are " +
                              "uploaded with the scene (see Docs/DevelopmentWorkflow.md).");
            return assets;
        }

        // ------------------------------------------------------------ materials and definitions

        private void EnsureMaterials(StringBuilder report)
        {
            // The painted materials of the visual style, where they can be had; plain ones otherwise. The
            // style is a layer on top of a level that works without it (see VisualStyleSetup.ApplyDuringSetup).
            Dictionary<string, Material> painted = null;
            try
            {
                painted = VisualStyleAssets.Ensure(report).ByName;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                report.AppendLine("WARNING: the painted materials of the visual style could not be generated; the foundry's " +
                                  "props use plain materials.");
            }

            Stone = Painted(painted, "Stone") ?? Lit("FoundryStone", StoneColor, report);
            DarkStone = Painted(painted, "StoneDark") ?? Lit("FoundryDarkStone", DarkStoneColor, report);
            Wood = Painted(painted, "Wood") ?? Lit("FoundryWood", WoodColor, report);
            Metal = Painted(painted, "Metal") ?? Lit("FoundryMetal", MetalColor, report);
            Crystal = Lit("FoundryCrystal", CrystalColor, report);
            Charred = Lit("FoundryCharred", CharredColor, report);
            SlagRock = Lit("FoundrySlagRock", SlagRockColor, report);
            Straw = Lit("FoundryStraw", StrawColor, report);
            Rune = Lit("FoundryRune", RuneColor, report);
            Beacon = Unlit("FoundryBeacon", BeaconColor, report);
            Ember = Unlit("FoundryEmber", EmberColor, report);
            Slag = Unlit("FoundrySlag", SlagColor, report);

            // The bursts are shared with the magical objects of the Discipline Range.
            CombatSetup.SharedAssets shared = CombatSetup.EnsureSharedAssets(report);
            string world = M.Folder;
            CombatSetup.EnsureFolder(world);
            Embers = CombatSetup.EnsureEffectPrefab($"{world}/ObjectEmbers.prefab", "ObjectEmbers", shared.Glow,
                new Color(1f, 0.62f, 0.3f, 1f), 8, 0.6f, report);
            Dust = CombatSetup.EnsureEffectPrefab($"{world}/ObjectDust.prefab", "ObjectDust", shared.Glow,
                new Color(0.95f, 0.9f, 0.76f, 1f), 12, 0.7f, report);
            Brace = CombatSetup.EnsureEffectPrefab($"{world}/ObjectBrace.prefab", "ObjectBrace", shared.Glow,
                new Color(1f, 0.93f, 0.7f, 1f), 8, 0.4f, report);
            Charge = CombatSetup.EnsureEffectPrefab($"{world}/ObjectCharge.prefab", "ObjectCharge", shared.Glow,
                new Color(0.62f, 0.7f, 1f, 1f), 12, 0.6f, report);
            Mote = CombatSetup.EnsureEffectPrefab($"{world}/DiscoveryMote.prefab", "DiscoveryMote", shared.Glow,
                new Color(1f, 0.97f, 0.88f, 1f), 3, 0.4f, report);
        }

        private static Material Painted(Dictionary<string, Material> painted, string name)
        {
            return painted != null && painted.TryGetValue(StyleMaterialPresets.Prefix + name, out Material material) ? material : null;
        }

        private static Material Lit(string name, Color color, StringBuilder report)
        {
            return CombatSetup.EnsureMaterial($"{Folder}/{name}.mat", () => CombatSetup.CreateColorMaterial(CombatSetup.LitShader, color), report);
        }

        private static Material Unlit(string name, Color color, StringBuilder report)
        {
            return CombatSetup.EnsureMaterial($"{Folder}/{name}.mat", () => CombatSetup.CreateColorMaterial(CombatSetup.UnlitShader, color), report);
        }

        private static MagicalObjectDefinition EnsureDefinition(FoundryObject kind, StringBuilder report)
        {
            string path = DefinitionPath(kind);
            var definition = AssetDatabase.LoadAssetAtPath<MagicalObjectDefinition>(path);
            if (definition != null)
            {
                return definition;
            }

            definition = ScriptableObject.CreateInstance<MagicalObjectDefinition>();
            definition.Configure(FoundryDefaults.Name(kind), FoundryDefaults.Description(kind), FoundryDefaults.Settings(kind));
            AssetDatabase.CreateAsset(definition, path);
            report.AppendLine($"Generated definition '{FoundryDefaults.Name(kind)}'.");
            return definition;
        }

        private static GameObject EnsurePrefab(string name, System.Func<GameObject> build, StringBuilder report)
        {
            string path = $"{Folder}/{name}.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing;
            }

            GameObject root = build();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            report.AppendLine($"Generated prefab '{name}'.");
            return prefab;
        }

        // ------------------------------------------------------------ magical prefabs

        /// <summary>
        /// A dormant crystal on an iron-bound plinth: the opening mechanism. It brightens as it charges and,
        /// once awake, lights a beacon and a light over itself for good.
        /// </summary>
        private GameObject BuildDormantCrystal(MagicalObjectDefinition definition)
        {
            var root = new GameObject("DormantCrystal");
            CapsuleCollider body = root.AddComponent<CapsuleCollider>();
            body.center = new Vector3(0f, 1.3f, 0f);
            body.height = 2.6f;
            body.radius = 0.55f;

            Transform visual = M.Group(root.transform, "Visual");
            M.Part(PrimitiveType.Cube, "Plinth", visual, new Vector3(0f, 0.25f, 0f), new Vector3(1.4f, 0.5f, 1.4f), DarkStone);
            M.Part(PrimitiveType.Cube, "Collar", visual, new Vector3(0f, 0.62f, 0f), new Vector3(1f, 0.25f, 1f), Metal, new Vector3(0f, 45f, 0f));
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f + 45f;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, 0.42f);
                M.Part(PrimitiveType.Cube, "Bracket", visual, offset + Vector3.up * 1f, new Vector3(0.1f, 0.9f, 0.1f), Metal,
                    new Vector3(-12f, angle, 0f));
            }

            GameObject shard = M.Part(PrimitiveType.Cube, "Crystal", visual, new Vector3(0f, 1.55f, 0f), new Vector3(0.5f, 1.7f, 0.5f), Crystal,
                new Vector3(0f, 45f, 6f));
            GameObject small = M.Part(PrimitiveType.Cube, "CrystalSmall", visual, new Vector3(0.32f, 1f, 0.1f), new Vector3(0.26f, 0.8f, 0.26f),
                Crystal, new Vector3(12f, 20f, -16f));
            GameObject smaller = M.Part(PrimitiveType.Cube, "CrystalSmaller", visual, new Vector3(-0.3f, 0.95f, -0.15f),
                new Vector3(0.2f, 0.6f, 0.2f), Crystal, new Vector3(-10f, -30f, 14f));

            GameObject beacon = M.Part(PrimitiveType.Sphere, "Beacon", root.transform, new Vector3(0f, 2.65f, 0f), new Vector3(0.4f, 0.4f, 0.4f),
                Beacon);
            beacon.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            beacon.SetActive(false);
            GameObject glow = PointLight(root.transform, "WakeLight", new Vector3(0f, 2.2f, 0f), new Color(0.7f, 0.78f, 1f), 7f, 1.6f);
            glow.SetActive(false);

            MagicalObject magical = root.AddComponent<MagicalObject>();
            magical.Bind(definition, null, new Collider[] { body });
            magical.SetAimPoint(new Vector3(0f, 1.5f, 0f));

            // The foundry gate follows this: it rises when the crystal wakes, and stays up.
            root.AddComponent<PropertyStateTrigger>().Configure(magical, PropertyCondition.Activated, true);

            PropertyStateVisuals visuals = root.AddComponent<PropertyStateVisuals>();
            visuals.Bind(magical, null, M.Renderers(shard, small, smaller));
            visuals.Configure(null,
                new[] { M.Shown(PropertyCondition.Activated, false, beacon, glow) },
                new[] { M.Effect(PropertyCondition.Activated, Charge) },
                null);

            DiscoveryClues clues = root.AddComponent<DiscoveryClues>();
            clues.Bind(magical, visuals, visual);
            clues.Configure(0.25f, new Color(0.78f, 0.84f, 1f, 1f), PropertyCondition.Activated | PropertyCondition.Destroyed, Mote);
            return root;
        }

        /// <summary>
        /// A furnace against a wall, its mouth facing forward (+z), with a chimney behind. Its mouth and
        /// iron front warm towards ember colour as it heats; while hot, a light burns in its mouth and its
        /// heat reaches out from just in front of the mouth. The top of the base around the chimney is too
        /// narrow to stand on.
        /// </summary>
        private GameObject BuildFurnace(MagicalObjectDefinition definition)
        {
            var root = new GameObject("Furnace");
            float width = FoundryLayout.FurnaceWidth;
            float depth = FoundryLayout.FurnaceDepth;
            float baseHeight = FoundryLayout.FurnaceBaseHeight;
            float front = depth * 0.5f;

            BoxCollider body = root.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, baseHeight * 0.5f, 0f);
            body.size = new Vector3(width, baseHeight, depth);

            float chimneyDepth = FoundryLayout.FurnaceChimneyDepth;
            float chimneyHeight = FoundryLayout.FurnaceChimneyTop - baseHeight;
            float chimneyZ = -front + chimneyDepth * 0.5f;
            var chimney = new GameObject("Chimney");
            chimney.transform.SetParent(root.transform, false);
            BoxCollider chimneyBox = chimney.AddComponent<BoxCollider>();
            chimneyBox.center = new Vector3(0f, baseHeight + chimneyHeight * 0.5f, chimneyZ);
            chimneyBox.size = new Vector3(FoundryLayout.FurnaceChimneyWidth, chimneyHeight, chimneyDepth);
            M.Part(PrimitiveType.Cube, "Stack", chimney.transform, chimneyBox.center, chimneyBox.size, Stone);
            for (int i = 0; i < 3; i++)
            {
                M.Part(PrimitiveType.Cube, "Band", chimney.transform, new Vector3(0f, baseHeight + 1.4f + i * 1.9f, chimneyZ),
                    new Vector3(FoundryLayout.FurnaceChimneyWidth + 0.1f, 0.14f, chimneyDepth + 0.1f), Metal);
            }

            Transform visual = M.Group(root.transform, "Visual");
            M.Part(PrimitiveType.Cube, "Plinth", visual, new Vector3(0f, 0.15f, 0f), new Vector3(width + 0.2f, 0.3f, depth + 0.2f), Stone);
            M.Part(PrimitiveType.Cube, "Body", visual, new Vector3(0f, 1.2f, -0.05f), new Vector3(width, 1.9f, depth - 0.1f), DarkStone);
            M.Part(PrimitiveType.Cube, "Cap", visual, new Vector3(0f, baseHeight - 0.1f, 0f), new Vector3(width + 0.1f, 0.2f, depth + 0.1f), Stone);

            // The iron front around the mouth, the dark mouth, and the ember bed that glows with the heat.
            GameObject frameTop = M.Part(PrimitiveType.Cube, "FrameTop", visual, new Vector3(0f, 1.78f, front + 0.02f),
                new Vector3(2.4f, 0.25f, 0.12f), Metal);
            GameObject frameLow = M.Part(PrimitiveType.Cube, "FrameLow", visual, new Vector3(0f, 0.42f, front + 0.02f),
                new Vector3(2.4f, 0.25f, 0.12f), Metal);
            GameObject frameLeft = M.Part(PrimitiveType.Cube, "FrameLeft", visual, new Vector3(-1.08f, 1.1f, front + 0.02f),
                new Vector3(0.25f, 1.6f, 0.12f), Metal);
            GameObject frameRight = M.Part(PrimitiveType.Cube, "FrameRight", visual, new Vector3(1.08f, 1.1f, front + 0.02f),
                new Vector3(0.25f, 1.6f, 0.12f), Metal);
            GameObject mouth = M.Part(PrimitiveType.Cube, "Mouth", visual, new Vector3(0f, 1.1f, front - 0.02f), new Vector3(1.9f, 1.1f, 0.05f),
                Charred);
            GameObject bed = M.Part(PrimitiveType.Cube, "EmberBed", visual, new Vector3(0f, 0.75f, front - 0.15f), new Vector3(1.7f, 0.3f, 0.3f),
                Charred);
            for (int i = 0; i < 4; i++)
            {
                M.Part(PrimitiveType.Cube, "Grate", visual, new Vector3(-0.6f + i * 0.4f, 1.1f, front + 0.06f), new Vector3(0.06f, 1f, 0.06f),
                    Metal);
            }

            GameObject heatLight = PointLight(root.transform, "HeatLight", new Vector3(0f, 1.2f, front + 0.5f), new Color(1f, 0.55f, 0.25f), 6f,
                2.2f);
            heatLight.SetActive(false);

            MagicalObject magical = root.AddComponent<MagicalObject>();
            magical.Bind(definition, null, new Collider[] { body });

            // The heat it sends comes from just in front of its mouth, so the furnace itself does not shield it.
            magical.SetAimPoint(FoundryLayout.FurnaceMouthLocal);

            PropertyStateVisuals visuals = root.AddComponent<PropertyStateVisuals>();
            visuals.Bind(magical, M.Renderers(mouth, bed, frameTop, frameLow, frameLeft, frameRight), null);
            visuals.Configure(null,
                new[] { M.Shown(PropertyCondition.Heated, false, heatLight) },
                new[] { M.Effect(PropertyCondition.Heated, Embers) },
                null);

            DiscoveryClues clues = root.AddComponent<DiscoveryClues>();
            clues.Bind(magical, visuals, visual);
            clues.Configure(0.1f, new Color(1f, 0.85f, 0.65f, 1f), PropertyCondition.None, Mote);
            return root;
        }

        /// <summary>
        /// A plank deck that gives way under weight unless braced, on hoist chains. Rising by
        /// <paramref name="rise"/> it is a ramp up to a ledge; level it is a bridge. When it gives way a
        /// level deck drops into what lies below, a ramp drops flat to the floor; a few seconds after nobody
        /// stands on it, it rises back into place. A trigger over the deck counts who stands on it.
        /// </summary>
        private GameObject BuildGantry(string name, MagicalObjectDefinition definition, float length, float rise, float loadLength)
        {
            var root = new GameObject(name);
            float run = Mathf.Sqrt(Mathf.Max(0.01f, length * length - rise * rise));
            float pitch = Mathf.Asin(Mathf.Clamp(rise / length, -1f, 1f)) * Mathf.Rad2Deg;
            bool ramp = rise > 0.01f;

            var deck = new GameObject("Deck");
            deck.transform.SetParent(root.transform, false);
            deck.transform.localPosition = new Vector3(0f, rise * 0.5f, run * 0.5f);
            deck.transform.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
            BoxCollider surface = deck.AddComponent<BoxCollider>();
            surface.center = Vector3.zero;
            surface.size = new Vector3(FoundryLayout.DeckWidth, 0.25f, length);
            M.MakeMovingBody(deck);

            Transform sway = M.Group(deck.transform, "Sway");
            int planks = Mathf.Max(4, Mathf.RoundToInt(length / 0.8f));
            for (int i = 0; i < planks; i++)
            {
                float z = -length * 0.5f + (i + 0.5f) * (length / planks);
                M.Part(PrimitiveType.Cube, "Plank", sway, new Vector3(0f, 0f, z), new Vector3(FoundryLayout.DeckWidth, 0.2f, length / planks - 0.06f),
                    Wood, new Vector3(0f, (i % 3 - 1) * 2.5f, 0f));
            }

            M.Part(PrimitiveType.Cube, "RailLeft", sway, new Vector3(-0.85f, 0.18f, 0f), new Vector3(0.08f, 0.12f, length), Metal);
            M.Part(PrimitiveType.Cube, "RailRight", sway, new Vector3(0.85f, 0.18f, 0f), new Vector3(0.08f, 0.12f, length), Metal);

            // Hoist chains rise straight up from the deck's corners: the gantry hangs, and is wound back up
            // when it falls. The deck is tilted on a ramp, so the chains are turned back to the vertical.
            Transform chains = M.Group(sway, "HoistChains");
            Vector3 upInDeck = Quaternion.Euler(pitch, 0f, 0f) * Vector3.up;
            foreach (float side in new[] { -0.9f, 0.9f })
            {
                foreach (float end in new[] { -0.42f, 0.42f })
                {
                    Vector3 foot = new Vector3(side, 0f, end * length);
                    M.Part(PrimitiveType.Cube, "Chain", chains, foot + upInDeck * 2f, new Vector3(0.06f, 4f, 0.06f), Metal,
                        new Vector3(pitch, 0f, 0f));
                }
            }

            if (ramp)
            {
                // Supports under the lower part: scenery, and they stay when the deck falls.
                Transform supports = M.Group(root.transform, "Supports");
                for (int i = 1; i <= 2; i++)
                {
                    float z = run * i / 3f;
                    float height = rise * i / 3f;
                    M.Part(PrimitiveType.Cube, "Support", supports, new Vector3(-0.7f, height * 0.5f, z), new Vector3(0.14f, height, 0.14f),
                        Charred, new Vector3(0f, 0f, 4f * i));
                    M.Part(PrimitiveType.Cube, "Support", supports, new Vector3(0.7f, height * 0.5f, z), new Vector3(0.14f, height, 0.14f),
                        Charred, new Vector3(0f, 0f, -6f * i));
                }
            }

            var zoneObject = new GameObject("LoadZone");
            zoneObject.transform.SetParent(deck.transform, false);
            BoxCollider zoneBox = zoneObject.AddComponent<BoxCollider>();
            zoneBox.isTrigger = true;
            zoneBox.center = new Vector3(0f, 0.9f, 0f);
            zoneBox.size = new Vector3(FoundryLayout.DeckWidth, 1.6f, Mathf.Min(loadLength, length));
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
                    M.Tint(PropertyCondition.Loaded, StrainColor, 0.35f), M.Tint(PropertyCondition.Braced, BracedColor, 0.45f),
                    M.Tint(PropertyCondition.Collapsed, CrackedColor, 0.3f),
                },
                null,
                new[]
                {
                    M.Effect(PropertyCondition.Loaded, Dust), M.Effect(PropertyCondition.Braced, Brace),
                    M.Effect(PropertyCondition.Collapsed, Dust),
                },
                null);

            // A ramp drops flat to the floor at the foot of its ledge; a bridge drops into the pit below it.
            Vector3 offset = ramp ? new Vector3(0.2f, -rise * 0.5f + 0.15f, 0f) : new Vector3(0.1f, -2f, 0f);
            Vector3 turn = ramp ? new Vector3(pitch, 0f, 6f) : new Vector3(4f, 0f, 7f);
            visuals.ConfigureMotion(sway, 1.2f, deck.transform, offset, turn);

            DiscoveryClues clues = root.AddComponent<DiscoveryClues>();
            clues.Bind(magical, visuals, sway);
            clues.Configure(0.08f, new Color(1f, 0.95f, 0.8f, 1f), PropertyCondition.Collapsed, Mote);
            return root;
        }

        /// <summary>
        /// The wooden barricade of LM-03, built to fill a 2.6 by 2.8 m doorway: split planks, a cracked post,
        /// scorch marks and straw at its foot. It burns away or breaks to stubs and ash.
        /// </summary>
        private GameObject BuildDamagedBarricade(MagicalObjectDefinition definition)
        {
            var root = new GameObject("DamagedBarricade");
            float width = FoundryLayout.BarricadeDoorX.y - FoundryLayout.BarricadeDoorX.x;
            float height = FoundryLayout.BarricadeDoorHeight;
            BoxCollider body = root.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, height * 0.5f, 0f);
            body.size = new Vector3(width, height, 0.4f);

            Transform whole = M.Group(root.transform, "Whole");
            Transform remains = M.Group(root.transform, "Remains");
            for (int i = -1; i <= 1; i++)
            {
                M.Part(PrimitiveType.Cube, "Post", whole, new Vector3(i * 1.18f, height * 0.5f, 0f), new Vector3(0.2f, height, 0.2f), Wood,
                    new Vector3(0f, 0f, i == 0 ? 4f : 0f));
                M.Part(PrimitiveType.Cube, "Stub", remains, new Vector3(i * 1.18f, 0.18f, 0f), new Vector3(0.2f, 0.36f, 0.2f), Charred,
                    new Vector3(0f, i * 17f, i * 6f));
            }

            float[] rows = { 0.4f, 0.95f, 1.5f, 2.05f, 2.6f };
            for (int i = 0; i < rows.Length; i++)
            {
                // Two planks are split: shorter, and askew.
                bool split = i == 1 || i == 3;
                float plankWidth = split ? width * 0.62f : width;
                float x = split ? (i == 1 ? -0.45f : 0.42f) : 0f;
                M.Part(PrimitiveType.Cube, "Plank", whole, new Vector3(x, rows[i], 0.13f), new Vector3(plankWidth, 0.24f, 0.07f), Wood,
                    new Vector3(0f, 0f, split ? (i == 1 ? 7f : -9f) : 0f));
            }

            M.Part(PrimitiveType.Cube, "Brace", whole, new Vector3(0f, height * 0.5f, -0.13f), new Vector3(width + 0.4f, 0.18f, 0.06f), Wood,
                new Vector3(0f, 0f, 38f));
            M.Part(PrimitiveType.Cube, "Scorch", whole, new Vector3(0.6f, 1.2f, 0.17f), new Vector3(0.6f, 0.5f, 0.02f), Charred);
            M.Part(PrimitiveType.Cube, "Scorch", whole, new Vector3(-0.7f, 2.2f, 0.17f), new Vector3(0.45f, 0.35f, 0.02f), Charred);

            // Dry straw packed at its foot, on the side the player comes from: it will burn.
            Transform clue = M.Group(whole, M.CluesName);
            M.Part(PrimitiveType.Cube, "Straw", clue, new Vector3(-0.6f, 0.14f, -0.32f), new Vector3(0.6f, 0.28f, 0.32f), Straw,
                new Vector3(0f, 12f, 0f));
            M.Part(PrimitiveType.Cube, "Straw", clue, new Vector3(0.55f, 0.12f, -0.3f), new Vector3(0.55f, 0.24f, 0.3f), Straw,
                new Vector3(0f, -18f, 0f));

            M.Part(PrimitiveType.Cube, "Ash", remains, new Vector3(0.2f, 0.04f, 0.2f), new Vector3(1.8f, 0.08f, 0.3f), Charred,
                new Vector3(0f, 20f, 0f));
            remains.gameObject.SetActive(false);

            MagicalObject magical = root.AddComponent<MagicalObject>();
            magical.Bind(definition, null, new Collider[] { body });
            magical.SetAimPoint(new Vector3(0f, 1.2f, 0f));

            PropertyStateVisuals visuals = root.AddComponent<PropertyStateVisuals>();
            visuals.Bind(magical, whole.GetComponentsInChildren<Renderer>(true), null);
            visuals.Configure(
                new[] { M.Tint(PropertyCondition.Weakened, CharredColor, 0.45f), M.Tint(PropertyCondition.Fractured, CharredColor, 0.8f) },
                new[] { M.Shown(PropertyCondition.Destroyed, true, whole.gameObject), M.Shown(PropertyCondition.Destroyed, false, remains.gameObject) },
                new[]
                {
                    M.Effect(PropertyCondition.Burning, Embers), M.Effect(PropertyCondition.Fractured, Dust),
                    M.Effect(PropertyCondition.Destroyed, Dust),
                },
                Embers);

            DiscoveryClues clues = root.AddComponent<DiscoveryClues>();
            clues.Bind(magical, visuals, whole);
            clues.Configure(0.08f, new Color(1f, 0.9f, 0.7f, 1f), PropertyCondition.Destroyed | PropertyCondition.Burning, Mote);
            clues.FillDestroyedMotionTarget(remains);
            return root;
        }

        /// <summary>
        /// LM-03's heat-sensitive stone as cooled slag choking a culvert, 2.4 by 2.6 m and as deep as the
        /// wall: vents and scorched rims on both faces say heat has worked on it before. It warms towards
        /// ember colour as it heats; heated, a strong blow shatters it into rubble.
        /// </summary>
        private GameObject BuildSlagPlug(MagicalObjectDefinition definition)
        {
            var root = new GameObject("SlagPlug");
            float width = FoundryLayout.CulvertX.y - FoundryLayout.CulvertX.x;
            float height = FoundryLayout.CulvertHeight;
            float depth = FoundryLayout.ExitWallDepth;
            BoxCollider body = root.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, height * 0.5f, 0f);
            body.size = new Vector3(width, height, depth);

            Transform whole = M.Group(root.transform, "Whole");
            M.Part(PrimitiveType.Cube, "Mass", whole, new Vector3(0f, height * 0.5f, 0f), new Vector3(width - 0.04f, height - 0.02f, depth - 0.04f),
                SlagRock);
            M.Part(PrimitiveType.Cube, "Lump", whole, new Vector3(-0.5f, 0.6f, -depth * 0.5f), new Vector3(0.9f, 0.7f, 0.3f), SlagRock,
                new Vector3(8f, 20f, 5f));
            M.Part(PrimitiveType.Cube, "Lump", whole, new Vector3(0.55f, 1.7f, -depth * 0.5f), new Vector3(0.7f, 0.6f, 0.26f), SlagRock,
                new Vector3(-6f, -25f, 9f));
            M.Part(PrimitiveType.Cube, "Drip", whole, new Vector3(0.1f, 2.2f, -depth * 0.5f - 0.05f), new Vector3(0.18f, 0.6f, 0.12f), Charred,
                new Vector3(0f, 0f, 6f));

            Transform clue = M.Group(whole, M.CluesName);
            for (int side = -1; side <= 1; side += 2)
            {
                float face = side * (depth * 0.5f + 0.01f);
                M.Part(PrimitiveType.Cube, "Scorch", clue, new Vector3(0f, 1.1f, face), new Vector3(1.3f, 0.8f, 0.02f), Charred);
                for (int i = 0; i < 3; i++)
                {
                    M.Part(PrimitiveType.Cube, "Vent", clue, new Vector3(0f, 0.95f + i * 0.17f, face + side * 0.015f),
                        new Vector3(0.8f, 0.05f, 0.03f), Metal);
                }
            }

            Transform rubble = M.Group(root.transform, "Rubble");
            M.Part(PrimitiveType.Cube, "Chunk", rubble, new Vector3(-0.7f, 0.2f, -0.3f), new Vector3(0.7f, 0.4f, 0.6f), SlagRock, new Vector3(0f, 25f, 0f));
            M.Part(PrimitiveType.Cube, "Chunk", rubble, new Vector3(0.6f, 0.15f, 0.2f), new Vector3(0.6f, 0.3f, 0.5f), SlagRock, new Vector3(0f, -35f, 0f));
            M.Part(PrimitiveType.Cube, "Chunk", rubble, new Vector3(0.1f, 0.25f, -0.6f), new Vector3(0.5f, 0.5f, 0.45f), SlagRock,
                new Vector3(18f, 50f, 9f));
            M.Part(PrimitiveType.Cube, "Chunk", rubble, new Vector3(-0.2f, 0.1f, 0.5f), new Vector3(0.4f, 0.2f, 0.4f), Charred, new Vector3(0f, 12f, 0f));
            rubble.gameObject.SetActive(false);

            MagicalObject magical = root.AddComponent<MagicalObject>();
            magical.Bind(definition, null, new Collider[] { body });
            magical.SetAimPoint(new Vector3(0f, 1.2f, -depth * 0.5f));

            PropertyStateVisuals visuals = root.AddComponent<PropertyStateVisuals>();
            visuals.Bind(magical, whole.GetComponentsInChildren<Renderer>(true), null);
            visuals.Configure(
                new[] { M.Tint(PropertyCondition.Weakened, CrackedColor, 0.25f), M.Tint(PropertyCondition.Fractured, CrackedColor, 0.45f) },
                new[] { M.Shown(PropertyCondition.Destroyed, true, whole.gameObject), M.Shown(PropertyCondition.Destroyed, false, rubble.gameObject) },
                new[]
                {
                    M.Effect(PropertyCondition.Heated, Embers), M.Effect(PropertyCondition.Fractured, Dust),
                    M.Effect(PropertyCondition.Destroyed, Dust),
                },
                null);

            DiscoveryClues clues = root.AddComponent<DiscoveryClues>();
            clues.Bind(magical, visuals, whole);
            clues.Configure(0.08f, new Color(1f, 0.86f, 0.72f, 1f), PropertyCondition.Destroyed, Mote);
            clues.FillDestroyedMotionTarget(rubble);
            return root;
        }

        // ------------------------------------------------------------ mechanisms and machinery

        /// <summary>
        /// A portcullis of iron bars on a timber beam, 4 by 4.5 m, that sinks into the floor when its input
        /// is active. It has no magic of its own. Through its bars the next part of the foundry can be seen.
        /// </summary>
        private GameObject BuildGate()
        {
            var root = new GameObject("FoundryGate");
            const float width = 4f;
            const float height = 4.5f;

            var leaf = new GameObject("Leaf");
            leaf.transform.SetParent(root.transform, false);
            BoxCollider box = leaf.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, height * 0.5f, 0f);
            box.size = new Vector3(width, height, 0.3f);
            M.MakeMovingBody(leaf);

            for (int i = 0; i < 7; i++)
            {
                M.Part(PrimitiveType.Cube, "Bar", leaf.transform, new Vector3(-1.8f + i * 0.6f, height * 0.5f, 0f), new Vector3(0.12f, height, 0.12f),
                    Metal);
            }

            foreach (float y in new[] { 0.5f, 1.7f, 2.9f, 4.1f })
            {
                M.Part(PrimitiveType.Cube, "Band", leaf.transform, new Vector3(0f, y, 0f), new Vector3(width, 0.14f, 0.18f), Metal);
            }

            M.Part(PrimitiveType.Cube, "Beam", leaf.transform, new Vector3(0f, 2.3f, -0.2f), new Vector3(width, 0.4f, 0.25f), Wood);

            PuzzleDoor door = root.AddComponent<PuzzleDoor>();
            door.Control = DoorControl.Input;
            door.ConfigureMotion(leaf.transform, new Vector3(0f, -height - 0.2f, 0f), Vector3.zero, 2.4f, 1.6f);

            AudioSource source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 3f;
            source.maxDistance = 40f;
            source.dopplerLevel = 0f;
            root.AddComponent<PuzzleAudio>().Bind(door, PuzzleTone.Rumble, PuzzleTone.Rumble, PuzzleTone.None);
            return root;
        }

        private GameObject BuildOreCart()
        {
            var root = new GameObject("OreCart");
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.55f, 0f);
            box.size = new Vector3(1.2f, 1.1f, 1.8f);
            M.Part(PrimitiveType.Cube, "Tub", root.transform, new Vector3(0f, 0.65f, 0f), new Vector3(1.2f, 0.7f, 1.8f), Wood);
            M.Part(PrimitiveType.Cube, "Rim", root.transform, new Vector3(0f, 1.02f, 0f), new Vector3(1.26f, 0.08f, 1.86f), Metal);
            foreach (float x in new[] { -0.62f, 0.62f })
            {
                foreach (float z in new[] { -0.6f, 0.6f })
                {
                    M.Part(PrimitiveType.Cylinder, "Wheel", root.transform, new Vector3(x, 0.25f, z), new Vector3(0.5f, 0.05f, 0.5f), Metal,
                        new Vector3(0f, 0f, 90f));
                }
            }

            M.Part(PrimitiveType.Cube, "Ore", root.transform, new Vector3(0.1f, 1.08f, -0.2f), new Vector3(0.9f, 0.3f, 1.2f), SlagRock,
                new Vector3(0f, 12f, 4f));
            return root;
        }

        private GameObject BuildGearWheel()
        {
            var root = new GameObject("GearWheel");
            M.Part(PrimitiveType.Cylinder, "Disc", root.transform, Vector3.zero, new Vector3(1.6f, 0.06f, 1.6f), Metal, new Vector3(90f, 0f, 0f));
            M.Part(PrimitiveType.Cylinder, "Hub", root.transform, Vector3.zero, new Vector3(0.4f, 0.12f, 0.4f), Metal, new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f;
                Vector3 position = Quaternion.Euler(0f, 0f, angle) * new Vector3(0f, 0.86f, 0f);
                M.Part(PrimitiveType.Cube, "Tooth", root.transform, position, new Vector3(0.18f, 0.22f, 0.12f), Metal, new Vector3(0f, 0f, angle));
            }

            return root;
        }

        private GameObject BuildCrucible()
        {
            var root = new GameObject("Crucible");
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.7f, 0f);
            box.size = new Vector3(1.3f, 1.4f, 1.3f);
            M.Part(PrimitiveType.Cylinder, "Body", root.transform, new Vector3(0f, 0.65f, 0f), new Vector3(1.2f, 0.6f, 1.2f), Metal);
            M.Part(PrimitiveType.Cylinder, "Rim", root.transform, new Vector3(0f, 1.27f, 0f), new Vector3(1.32f, 0.05f, 1.32f), Metal);
            M.Part(PrimitiveType.Cube, "Spout", root.transform, new Vector3(0f, 1.15f, 0.68f), new Vector3(0.3f, 0.12f, 0.35f), Metal,
                new Vector3(-15f, 0f, 0f));
            M.Part(PrimitiveType.Cylinder, "Cinders", root.transform, new Vector3(0f, 1.22f, 0f), new Vector3(1f, 0.02f, 1f), Charred);
            return root;
        }

        /// <summary>A chain hanging 4.2 m with a hook: two shapes, because on a phone every separate part costs.</summary>
        private GameObject BuildChain()
        {
            var root = new GameObject("HangingChain");
            M.Part(PrimitiveType.Cube, "Chain", root.transform, new Vector3(0f, -2.1f, 0f), new Vector3(0.08f, 4.2f, 0.08f), Metal,
                new Vector3(0f, 45f, 0f));
            M.Part(PrimitiveType.Cube, "Hook", root.transform, new Vector3(0f, -4.35f, 0f), new Vector3(0.24f, 0.24f, 0.06f), Metal,
                new Vector3(0f, 0f, 45f));
            return root;
        }

        private static GameObject PointLight(Transform parent, string name, Vector3 localPosition, Color color, float range, float intensity)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = localPosition;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            return lightObject;
        }
    }
}
