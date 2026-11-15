using System.Collections.Generic;
using System.Text;
using Threefold.Characters;
using Threefold.Interface;
using Threefold.MainMenu;
using Threefold.VisualStyle;
using Threefold.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Threefold.EditorTools.SceneSetupUtility;
using Random = System.Random;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Builds the main menu scene (Docs/MainMenu.md): the Mage standing on an old stone terrace above a
    /// valley, seen by a fixed camera, with the menu on the left and his name under his feet. Everything is
    /// placed from <see cref="MainMenuStage"/> and <see cref="MainMenuLayout"/>; change the numbers there.
    ///
    /// The scene is live 3D, not a picture: the character is the game's own model with its idle animation
    /// and bearing, the land is the environment kit of the levels, and the interface is separate from both.
    ///
    /// Safe to run repeatedly. The generated environment is replaced; the camera, the character, the
    /// interface and anything added by hand are kept, and only their empty references are filled.
    /// </summary>
    public static class MainMenuSetup
    {
        private const string MenuPath = "Threefold/Setup/Main Menu";
        private const string DialogTitle = "Main Menu";
        internal const string ScenePath = "Assets/Scenes/MainMenu.unity";
        private const string GeneratorName = "MainMenu";
        private const string RootName = "MainMenu Environment";
        internal const string CameraName = "MenuCamera";
        internal const string CharacterName = "MenuCharacter";
        internal const string InterfaceName = "MainMenu";
        private const string VisualName = "MageVisual";
        private const string GraphicsButtonName = "GraphicsButton";
        private const int UiLayer = 5;

        /// <summary>The standing column at the right edge of the picture; the staff leans on it.</summary>
        private static readonly Vector3 StandingColumn = new Vector3(1.8f, 0f, 1.4f);

        [MenuItem(MenuPath, false, 7)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(DialogTitle, "Exit Play mode before running the setup.", "OK");
                return;
            }

            if (!TryOpenOrCreateScene(ScenePath, DialogTitle, out Scene scene))
            {
                return;
            }

            if (FantasyPlaygroundSetup.FindEnvironmentRoot(scene, GeneratorName) != null && !EditorUtility.DisplayDialog(
                    DialogTitle,
                    "This scene already contains the main menu.\n\nRebuild its environment? The camera, the character, " +
                    "the interface and anything you added yourself are kept.", "Rebuild", "Cancel"))
            {
                return;
            }

            var report = new StringBuilder();
            GameObject character = Assemble(scene, report);
            bool saved = EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = character;

            string summary = report.ToString().TrimEnd();
            Debug.Log($"[Threefold] {DialogTitle} finished.\n{summary}");
            EditorUtility.DisplayDialog(DialogTitle,
                summary + (saved ? "\n\nThe scene was saved. " : "\n\nThe scene could NOT be saved; save it with Ctrl+S. ") +
                "Press Play to see the menu.", "OK");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>Builds the scene without asking anything, for tests and build servers. Discards unsaved work in the open scene.</summary>
        internal static Scene BuildForAutomation()
        {
            Scene scene = OpenOrCreateSceneForAutomation(ScenePath);

            var report = new StringBuilder();
            Assemble(scene, report);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[Threefold] {DialogTitle} assembled for automation.\n{report.ToString().TrimEnd()}");
            return scene;
        }

        /// <summary>Reports what a main menu scene lacks. Returns the number of problems.</summary>
        internal static int Validate(Scene scene, StringBuilder problems)
        {
            int count = 0;

            void Problem(string text)
            {
                problems.AppendLine(text);
                count++;
            }

            Camera viewCamera = FindInScene<Camera>(scene);
            if (viewCamera == null)
            {
                Problem("The scene has no camera.");
            }

            GameObject character = FindByName(scene, CharacterName);
            Animator animator = character != null ? character.GetComponentInChildren<Animator>(true) : null;
            if (animator == null)
            {
                Problem("The character is missing, or has no Animator.");
            }
            else if (animator.runtimeAnimatorController == null || animator.avatar == null)
            {
                Problem("The character's Animator has no controller or no avatar, so he would stand in a T-pose.");
            }

            MainMenuScreen screen = FindInScene<MainMenuScreen>(scene);
            if (screen == null)
            {
                Problem("The scene has no MainMenuScreen.");
            }
            else
            {
                var serialized = new SerializedObject(screen);
                SerializedProperty buttons = serialized.FindProperty("entryButtons");
                if (buttons == null || buttons.arraySize != MainMenuModel.EntryCount)
                {
                    Problem($"The menu needs {MainMenuModel.EntryCount} entry buttons.");
                }
                else
                {
                    for (int i = 0; i < buttons.arraySize; i++)
                    {
                        if (buttons.GetArrayElementAtIndex(i).objectReferenceValue == null)
                        {
                            Problem($"Entry button {i} of the menu is not connected.");
                        }
                    }
                }

                if (string.IsNullOrEmpty(screen.GameScene))
                {
                    Problem("The menu does not name the scene that Continue and New Game load.");
                }
            }

            return count;
        }

        private static GameObject Assemble(Scene scene, StringBuilder report)
        {
            EnvironmentAssets assets = EnvironmentAssets.Ensure(false, report);

            GameObject oldRoot = FantasyPlaygroundSetup.FindEnvironmentRoot(scene, GeneratorName);
            if (oldRoot != null)
            {
                Undo.DestroyObjectImmediate(oldRoot);
            }

            var root = new GameObject(RootName);
            root.AddComponent<GeneratedEnvironment>().Generator = GeneratorName;
            Undo.RegisterCreatedObjectUndo(root, DialogTitle);

            Transform terrace = FantasyPlaygroundSetup.CreateGroup(root.transform, "Terrace");
            Transform valley = FantasyPlaygroundSetup.CreateGroup(root.transform, "Valley");
            Transform background = FantasyPlaygroundSetup.CreateGroup(root.transform, "Background");
            Transform lighting = FantasyPlaygroundSetup.CreateGroup(root.transform, "Lighting");

            BuildTerrace(assets, terrace);
            BuildValley(assets, valley);
            FantasyPlaygroundSetup.BuildBackground(assets, background);
            FantasyPlaygroundSetup.BuildLighting(assets, lighting, report);
            report.AppendLine("Built the terrace, the valley with its forest and ruins, the mountains and the late-afternoon light.");

            Camera viewCamera = EnsureCamera(scene, report);
            GameObject character = EnsureCharacter(scene, report);

            // The style: painted materials, the contour line on the character, its lighting and the graphics menu.
            VisualStyleSetup.ApplyDuringSetup(scene, report, null);

            EnsureInterface(scene, report);
            FantasyPlaygroundSetup.AddToBuildSettings(ScenePath, false, report);

            var problems = new StringBuilder();
            int problemCount = Validate(scene, problems);
            report.AppendLine(problemCount == 0
                ? "The main menu scene is complete."
                : $"WARNING: the main menu scene has {problemCount} problem(s):\n{problems.ToString().TrimEnd()}");

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            return character != null ? character : viewCamera.gameObject;
        }

        // ---------------------------------------------------------- environment

        private static void BuildTerrace(EnvironmentAssets assets, Transform parent)
        {
            float width = MainMenuStage.TerraceHalfWidth * 2f;
            float depth = MainMenuStage.TerraceEdge - MainMenuStage.TerraceBack;
            float middle = (MainMenuStage.TerraceEdge + MainMenuStage.TerraceBack) * 0.5f;

            FantasyPlaygroundSetup.PlaceBlock(assets, parent, "TerraceFloor",
                new Vector3(width, MainMenuStage.TerraceThickness, depth),
                new Vector3(0f, -MainMenuStage.TerraceThickness, middle), assets.PavingMaterial);

            // What is left of the parapet, to both sides. The middle is open, or the valley would be hidden.
            float edge = MainMenuStage.TerraceEdge - 0.3f;
            FantasyPlaygroundSetup.PlaceBlock(assets, parent, "ParapetLeft", new Vector3(3.6f, 0.55f, 0.5f),
                new Vector3(-6.6f, 0f, edge), assets.StoneMaterial);
            FantasyPlaygroundSetup.PlaceBlock(assets, parent, "ParapetLeftStub", new Vector3(0.9f, 0.3f, 0.5f),
                new Vector3(-4.35f, 0f, edge), assets.StoneMaterial);
            FantasyPlaygroundSetup.PlaceBlock(assets, parent, "ParapetRight", new Vector3(3.2f, 0.55f, 0.5f),
                new Vector3(5.6f, 0f, edge), assets.StoneMaterial);

            // Old columns: a broken one behind the menu, a standing one at the right edge of the picture.
            FantasyPlaygroundSetup.Place(assets.ColumnBroken, parent, new Vector3(-3.2f, 0f, 1.4f), 25f);
            FantasyPlaygroundSetup.Place(assets.Column, parent, StandingColumn, 0f);
            FantasyPlaygroundSetup.Place(assets.Rubble, parent, new Vector3(-4.6f, 0f, 0.6f), 70f);

            // Growth between the stones. Moss stands in for ivy; the kit has no flowers.
            FantasyPlaygroundSetup.Place(assets.MossPatch, parent, new Vector3(-2.3f, 0.012f, 1.5f), 40f, 1.2f);
            FantasyPlaygroundSetup.Place(assets.MossPatch, parent, new Vector3(2.7f, 0.013f, 0.7f), 160f);
            FantasyPlaygroundSetup.Place(assets.MossPatch, parent, new Vector3(0.9f, 0.014f, 2f), 250f, 0.8f);
            FantasyPlaygroundSetup.Place(assets.GrassPatches[0], parent, new Vector3(-1.4f, 0f, 2.1f), 0f, 0.6f);
            FantasyPlaygroundSetup.Place(assets.GrassPatches[1 % assets.GrassPatches.Length], parent,
                new Vector3(1.2f, 0f, 2.15f), 110f, 0.5f);
            FantasyPlaygroundSetup.Place(assets.GrassPatches[0], parent, new Vector3(3.1f, 0f, 1.9f), 200f, 0.55f);

            BuildStaff(assets, parent);
        }

        /// <summary>
        /// A plain wooden staff with a small crystal, leaning against the standing column. A placeholder:
        /// for the character to hold it, the model needs a staff and a hand pose (Docs/MainMenu.md).
        /// </summary>
        private static void BuildStaff(EnvironmentAssets assets, Transform parent)
        {
            Mesh mesh = assets.EnsureMesh("MainMenuStaff", () =>
            {
                var b = new ProceduralMeshBuilder();
                b.AddPrism(Vector3.zero, 0.024f, 0.019f, 1.62f, 7, 0);
                b.AddPrism(new Vector3(0f, 1.62f, 0f), 0.019f, 0.034f, 0.07f, 7, 0);
                b.AddPrism(new Vector3(0f, 1.69f, 0f), 0.022f, 0.03f, 0.035f, 6, 1);
                b.AddPrism(new Vector3(0f, 1.725f, 0f), 0.03f, 0f, 0.075f, 6, 1);
                return b.Build("MainMenuStaff");
            });

            var staff = new GameObject("Staff");
            staff.transform.SetParent(parent, false);

            // Its foot stands clear of the column's base (1.1 m square) and its head rests on the shaft (0.42 m thick).
            var foot = new Vector3(StandingColumn.x - 0.65f, 0f, StandingColumn.z - 0.3f);
            Vector3 towards = new Vector3(StandingColumn.x - foot.x, 0f, StandingColumn.z - foot.z).normalized;
            staff.transform.SetPositionAndRotation(foot,
                Quaternion.AngleAxis(9.7f, new Vector3(towards.z, 0f, -towards.x)));

            staff.AddComponent<MeshFilter>().sharedMesh = mesh;
            staff.AddComponent<MeshRenderer>().sharedMaterials = new[] { assets.BarkMaterial, assets.CrystalMaterial };
            staff.isStatic = true;
        }

        private static void BuildValley(EnvironmentAssets assets, Transform parent)
        {
            Mesh mesh = assets.EnsureMesh("MainMenuValley", BuildValleyMesh);
            GameObject land = FantasyPlaygroundSetup.CreateMeshObject(parent, "Land", mesh, assets.GroundMaterial, Vector3.zero, false);
            land.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Two near trees on the level ground beside the terrace; the right one frames the picture.
            PlaceOnLand(assets.Trees[1 % assets.Trees.Length], parent, 6.6f, 8.5f, 205f, 1.3f);
            PlaceOnLand(assets.Trees[0], parent, -8.4f, 7.5f, 60f, 1.2f);

            // The forest of the valley and its hills, thinner along the view to the spire.
            var random = new Random(20261115);
            int planted = 0;
            for (int attempt = 0; attempt < 500 && planted < 90; attempt++)
            {
                float x = EnvironmentAssets.Range(random, -120f, 120f);
                float z = EnvironmentAssets.Range(random, 24f, 245f);
                if (Mathf.Abs(x) < 9f + z * 0.03f && random.NextDouble() < 0.8)
                {
                    continue;
                }

                GameObject tree = assets.Trees[planted % assets.Trees.Length];
                PlaceOnLand(tree, parent, x, z, EnvironmentAssets.Range(random, 0f, 360f), EnvironmentAssets.Range(random, 1.1f, 1.9f));
                planted++;
            }

            for (int i = 0; i < 14; i++)
            {
                float x = EnvironmentAssets.Range(random, -70f, 70f);
                float z = EnvironmentAssets.Range(random, 30f, 200f);
                PlaceOnLand(assets.Rocks[i % assets.Rocks.Length], parent, x, z, EnvironmentAssets.Range(random, 0f, 360f),
                    EnvironmentAssets.Range(random, 1.5f, 3.5f));
            }

            // Ruins far below, large enough to read at that distance.
            float visible = MainMenuStage.FirstVisibleFloorDistance();
            PlaceOnLand(assets.ArchRuined, parent, -16f, visible + 30f, 20f, 2.4f);
            PlaceOnLand(assets.ColumnBroken, parent, -9f, visible + 34f, 0f, 2.4f);
            PlaceOnLand(assets.ColumnFallen, parent, -22f, visible + 26f, 75f, 2.4f);
            PlaceOnLand(assets.Column, parent, 14f, visible + 48f, 0f, 2.6f);
            PlaceOnLand(assets.WallBroken, parent, 20f, visible + 52f, 100f, 2.6f);
        }

        private static void PlaceOnLand(GameObject prefab, Transform parent, float x, float z, float yaw, float scale)
        {
            GameObject instance = FantasyPlaygroundSetup.Place(prefab, parent,
                new Vector3(x, MainMenuStage.ValleyHeight(x, z) - 0.05f, z), yaw, scale);

            // Nothing walks here: the colliders of the kit's prefabs are not needed.
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
        }

        /// <summary>
        /// The land beyond the terrace as one mesh. Its grid is fine near the terrace and coarse far away,
        /// because distant cells cover few pixels.
        /// </summary>
        private static Mesh BuildValleyMesh()
        {
            List<float> xs = GridLines(0f, MainMenuStage.ValleyHalfWidth, true);
            List<float> zs = GridLines(MainMenuStage.TerraceEdge - 0.2f, MainMenuStage.ValleyLength, false);
            int row = xs.Count;

            var vertices = new Vector3[row * zs.Count];
            var uvs = new Vector2[vertices.Length];
            for (int zi = 0; zi < zs.Count; zi++)
            {
                for (int xi = 0; xi < row; xi++)
                {
                    float x = xs[xi];
                    float z = zs[zi];
                    vertices[zi * row + xi] = new Vector3(x, MainMenuStage.ValleyHeight(x, z), z);
                    uvs[zi * row + xi] = new Vector2(x, z) * ProceduralMeshBuilder.UvScale;
                }
            }

            var triangles = new List<int>((row - 1) * (zs.Count - 1) * 6);
            for (int zi = 0; zi < zs.Count - 1; zi++)
            {
                for (int xi = 0; xi < row - 1; xi++)
                {
                    int v00 = zi * row + xi;
                    int v10 = v00 + 1;
                    int v01 = v00 + row;
                    int v11 = v01 + 1;

                    // Ordered so that the surface faces upwards.
                    triangles.Add(v00);
                    triangles.Add(v01);
                    triangles.Add(v11);
                    triangles.Add(v00);
                    triangles.Add(v11);
                    triangles.Add(v10);
                }
            }

            var land = new Mesh { name = "MainMenuValley" };
            land.vertices = vertices;
            land.uv = uvs;
            land.triangles = triangles.ToArray();
            land.RecalculateNormals();
            land.RecalculateBounds();
            return land;
        }

        /// <summary>Grid lines from <paramref name="start"/> outwards, 1.5 m apart at first, then 3 m, then 8 m.</summary>
        private static List<float> GridLines(float start, float end, bool mirrored)
        {
            var lines = new List<float>();
            float travelled = 0f;
            float position = start;
            while (position < end)
            {
                lines.Add(position);
                float step = travelled < 18f ? 1.5f : travelled < 60f ? 3f : 8f;
                position += step;
                travelled += step;
            }

            lines.Add(end);

            if (!mirrored)
            {
                return lines;
            }

            var both = new List<float>(lines.Count * 2);
            for (int i = lines.Count - 1; i > 0; i--)
            {
                both.Add(-lines[i]);
            }

            both.AddRange(lines);
            return both;
        }

        // ----------------------------------------------------- camera, character

        private static Camera EnsureCamera(Scene scene, StringBuilder report)
        {
            Camera viewCamera = FindInScene<Camera>(scene);
            if (viewCamera == null)
            {
                var host = new GameObject(CameraName);
                host.tag = "MainCamera";
                viewCamera = host.AddComponent<Camera>();
                host.AddComponent<AudioListener>();
                host.transform.SetPositionAndRotation(MainMenuStage.CameraPosition,
                    Quaternion.Euler(-MainMenuStage.CameraPitchUp, 0f, 0f));
                viewCamera.fieldOfView = MainMenuStage.FieldOfView;
                viewCamera.nearClipPlane = MainMenuStage.NearClip;
                viewCamera.farClipPlane = MainMenuStage.FarClip;
                Undo.RegisterCreatedObjectUndo(host, DialogTitle);
                report.AppendLine("Added the menu camera: below chest height, looking slightly up, with a narrow field of view.");
            }

            UniversalAdditionalCameraData cameraData = viewCamera.GetUniversalAdditionalCameraData();
            if (cameraData != null && !cameraData.renderPostProcessing)
            {
                cameraData.renderPostProcessing = true;
                EditorUtility.SetDirty(cameraData);
            }

            return viewCamera;
        }

        /// <summary>
        /// The Mage as he is in the game, without anything that makes him playable: the generated visual
        /// (model, materials, Animator, which rests in the idle animation) and the bearing that breathes,
        /// blinks and lets hair and cloak settle.
        /// </summary>
        private static GameObject EnsureCharacter(Scene scene, StringBuilder report)
        {
            GameObject character = FindByName(scene, CharacterName);
            if (character == null)
            {
                character = new GameObject(CharacterName);
                character.transform.SetPositionAndRotation(MainMenuStage.CharacterPosition,
                    Quaternion.Euler(0f, MainMenuStage.CharacterYaw, 0f));
                Undo.RegisterCreatedObjectUndo(character, DialogTitle);
            }

            Transform visual = character.transform.Find(VisualName);
            if (visual == null)
            {
                GameObject prefab = MageCharacterSetup.EnsureVisualPrefab(report);
                if (prefab == null)
                {
                    report.AppendLine("WARNING: the Mage could not be placed in the main menu, because his model is not usable.");
                    return character;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, character.transform);
                instance.name = VisualName;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                Undo.RegisterCreatedObjectUndo(instance, DialogTitle);
                visual = instance.transform;
                report.AppendLine("Placed the Mage on the terrace, turned three-quarters towards the camera.");
            }

            Animator animator = visual.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                report.AppendLine("WARNING: the Mage's visual has no Animator, so he stands still in the main menu.");
                return character;
            }

            // Shown from head to toe at all times: never stop animating him.
            if (animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            {
                Undo.RecordObject(animator, DialogTitle);
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                MarkChanged(animator);
            }

            if (!character.TryGetComponent(out CharacterPersonality personality))
            {
                personality = Undo.AddComponent<CharacterPersonality>(character);
                report.AppendLine("Gave him his bearing: breathing, blinking, and hair and cloak that settle.");
            }

            Undo.RecordObject(personality, DialogTitle);
            if (personality.FillReferences(animator, null, null, null, null))
            {
                MarkChanged(personality);
            }

            return character;
        }

        // ------------------------------------------------------------ interface

        private static void EnsureInterface(Scene scene, StringBuilder report)
        {
            GraphicsMenu graphics = FindInScene<GraphicsMenu>(scene);
            string gameScene = System.IO.Path.GetFileNameWithoutExtension(FantasyPlaygroundSetup.ScenePath);

            MainMenuScreen screen = FindInScene<MainMenuScreen>(scene);
            if (screen != null)
            {
                Undo.RecordObject(screen, DialogTitle);
                screen.BindMissing(null, null, null, null, null, null, null, null, null, null, graphics, null);
                MarkChanged(screen);
                return;
            }

            // The Settings entry opens the graphics panel, so the panel's own button is not needed here; it
            // would also sit on the logo.
            GameObject graphicsButton = graphics != null ? FindByName(scene, GraphicsButtonName) : null;
            if (graphicsButton != null && graphicsButton.transform.IsChildOf(graphics.transform))
            {
                Undo.RecordObject(graphicsButton, DialogTitle);
                graphicsButton.SetActive(false);
            }

            var root = new GameObject(InterfaceName, typeof(Canvas));
            root.layer = UiLayer;
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90; // Under the style test panel (95) and the graphics panel (96).
            root.AddComponent<DensityCanvasScaler>();

            RectTransform namePlate = BuildNamePlate(root.transform);

            RectTransform safeArea = TouchControlsSetup.CreateRect("SafeArea", root.transform);
            TouchControlsSetup.Stretch(safeArea);
            safeArea.gameObject.AddComponent<SafeAreaFitter>();

            BuildLogo(safeArea);

            // The entries have a canvas of their own: the highlight changes every frame, and only this
            // canvas is then rebuilt.
            RectTransform entries = TouchControlsSetup.CreateRect("Entries", safeArea);
            TouchControlsSetup.Stretch(entries);
            entries.gameObject.AddComponent<Canvas>();

            var buttons = new TapButton[MainMenuModel.EntryCount];
            var highlights = new Image[MainMenuModel.EntryCount];
            var labels = new Text[MainMenuModel.EntryCount];
            for (int i = 0; i < MainMenuModel.EntryCount; i++)
            {
                var entry = (MainMenuEntry)i;
                buttons[i] = BuildEntry(entries, entry.ToString(), MainMenuLook.Caption(entry), MainMenuLayout.EntryTop(i),
                    out highlights[i], out labels[i]);
            }

            RectTransform confirm = TouchControlsSetup.CreateRect("Confirm", safeArea);
            TouchControlsSetup.Stretch(confirm);
            confirm.gameObject.AddComponent<Canvas>();

            RectTransform question = TouchControlsSetup.CreateRect("Question", confirm);
            TopLeft(question, MainMenuLayout.ColumnLeft, MainMenuLayout.EntryTop(0), MainMenuLayout.ColumnWidth,
                MainMenuLayout.ConfirmTextHeight);
            AddPlate(question);
            Text questionText = CreateText("Text", question, MainMenuLook.ConfirmQuestion, MainMenuLayout.ConfirmFontSize,
                MainMenuLook.Parchment, TextAnchor.MiddleCenter);
            Inset(questionText.rectTransform, 10f);
            questionText.horizontalOverflow = HorizontalWrapMode.Wrap;

            TapButton begin = BuildEntry(confirm, "Begin", MainMenuLook.ConfirmBegin, MainMenuLayout.ConfirmAnswerTop(0),
                out Image beginHighlight, out _);
            TapButton keep = BuildEntry(confirm, "Keep", MainMenuLook.ConfirmKeep, MainMenuLayout.ConfirmAnswerTop(1),
                out Image keepHighlight, out _);
            confirm.gameObject.SetActive(false);

            RectTransform noticeRect = TouchControlsSetup.CreateRect("Notice", safeArea);
            TopLeft(noticeRect, MainMenuLayout.ColumnLeft, MainMenuLayout.EntriesBottom + 6f, MainMenuLayout.ColumnWidth, 18f);
            Text notice = AddText(noticeRect, string.Empty, 12, MainMenuLook.Parchment, TextAnchor.MiddleLeft);

            screen = root.AddComponent<MainMenuScreen>();
            screen.SetGameScene(gameScene);
            screen.BindMissing(entries.gameObject, buttons, highlights, labels, confirm.gameObject, begin, keep, beginHighlight,
                keepHighlight, namePlate, graphics, notice);

            Undo.RegisterCreatedObjectUndo(root, DialogTitle);
            MarkChanged(screen);
            report.AppendLine("Added the menu: the title, Continue Adventure, New Game, Settings and Quit on the left, and the " +
                              $"name plate under the character. Continue and New Game load '{gameScene}'.");
            if (graphics == null)
            {
                report.AppendLine("The visual style is not in this scene, so the Settings entry is dimmed.");
            }
        }

        private static void BuildLogo(RectTransform parent)
        {
            RectTransform logo = TouchControlsSetup.CreateRect("Logo", parent);
            TopLeft(logo, MainMenuLayout.ColumnLeft, MainMenuLayout.LogoTop, MainMenuLayout.ColumnWidth, MainMenuLayout.LogoHeight);
            Text title = AddText(logo, MainMenuLook.GameTitle, MainMenuLayout.LogoFontSize, MainMenuLook.Parchment, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;

            // A soft dark edge keeps the letters readable over a bright sky.
            Shadow shadow = logo.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.05f, 0.04f, 0.03f, 0.7f);
            shadow.effectDistance = new Vector2(1.2f, -1.2f);

            // A thin gold rule with three marks: one for each of the three ways.
            RectTransform rule = TouchControlsSetup.CreateRect("Rule", parent);
            TopLeft(rule, MainMenuLayout.ColumnLeft, MainMenuLayout.RuleTop, MainMenuLayout.ColumnWidth, MainMenuLayout.RuleHeight);

            RectTransform line = TouchControlsSetup.CreateRect("Line", rule);
            line.anchorMin = new Vector2(0f, 0.5f);
            line.anchorMax = new Vector2(1f, 0.5f);
            line.sizeDelta = new Vector2(0f, 1f);
            AddImage(line, MainMenuLook.Gold);

            for (int i = 0; i < 3; i++)
            {
                Image mark = TouchControlsSetup.CreateShape("Mark" + (i + 1), rule, ProceduralShape.Diamond, MainMenuLook.Gold);
                RectTransform rect = mark.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(MainMenuLayout.RuleHeight * 1.6f, MainMenuLayout.RuleHeight * 1.6f);
                rect.anchoredPosition = new Vector2(8f + i * 13f, 0f);
                mark.raycastTarget = false;
            }
        }

        /// <summary>
        /// One entry: an aged bronze frame round a dark stone plate, two fine gold lines, a mark at each end,
        /// the warm highlight (hidden until the entry is chosen) and the caption in parchment.
        /// </summary>
        private static TapButton BuildEntry(RectTransform parent, string name, string caption, float top, out Image highlight,
            out Text label)
        {
            RectTransform rect = TouchControlsSetup.CreateRect(name, parent);
            TopLeft(rect, MainMenuLayout.ColumnLeft, top, MainMenuLayout.ColumnWidth, MainMenuLayout.EntryHeight);
            AddImage(rect, MainMenuLook.Bronze);
            TapButton button = rect.gameObject.AddComponent<TapButton>();

            RectTransform stone = TouchControlsSetup.CreateRect("Stone", rect);
            Inset(stone, 1.5f);
            AddImage(stone, MainMenuLook.Stone);

            RectTransform glow = TouchControlsSetup.CreateRect("Highlight", rect);
            Inset(glow, 1.5f);
            highlight = AddImage(glow, MainMenuLook.Highlight);
            highlight.enabled = false;

            Color fine = MainMenuLook.Gold;
            fine.a = 0.45f;
            for (int i = 0; i < 2; i++)
            {
                RectTransform line = TouchControlsSetup.CreateRect(i == 0 ? "LineTop" : "LineBottom", rect);
                line.anchorMin = new Vector2(0f, i == 0 ? 1f : 0f);
                line.anchorMax = new Vector2(1f, i == 0 ? 1f : 0f);
                line.pivot = new Vector2(0.5f, 0.5f);
                line.sizeDelta = new Vector2(-28f, 1f);
                line.anchoredPosition = new Vector2(0f, i == 0 ? -5.5f : 5.5f);
                AddImage(line, fine);
            }

            for (int i = 0; i < 2; i++)
            {
                Image mark = TouchControlsSetup.CreateShape(i == 0 ? "MarkLeft" : "MarkRight", rect, ProceduralShape.Diamond,
                    MainMenuLook.Gold);
                RectTransform markRect = mark.rectTransform;
                markRect.anchorMin = markRect.anchorMax = new Vector2(i, 0.5f);
                markRect.pivot = new Vector2(0.5f, 0.5f);
                markRect.sizeDelta = new Vector2(14f, 14f);
                markRect.anchoredPosition = new Vector2(i == 0 ? 13f : -13f, 0f);
                mark.raycastTarget = false;
            }

            label = CreateText("Label", rect, caption, MainMenuLayout.EntryFontSize, MainMenuLook.Parchment, TextAnchor.MiddleCenter);
            TouchControlsSetup.Stretch(label.rectTransform);
            return button;
        }

        /// <summary>
        /// The name plate. It sits directly on the canvas, not in the safe area, because the screen places
        /// it under the character's feet as the camera shows him.
        /// </summary>
        private static RectTransform BuildNamePlate(Transform parent)
        {
            RectTransform plate = TouchControlsSetup.CreateRect("NamePlate", parent);
            Vector2 feet = MainMenuStage.FeetOnScreen(16f / 9f);
            plate.anchorMin = plate.anchorMax = feet;
            plate.pivot = new Vector2(0.5f, 1f);
            plate.sizeDelta = new Vector2(MainMenuLayout.NamePlateWidth, MainMenuLayout.NamePlateHeight);
            plate.anchoredPosition = new Vector2(0f, -MainMenuLayout.NamePlateGap);

            Color stone = MainMenuLook.Stone;
            stone.a = 0.6f;
            AddImage(plate, stone);

            RectTransform line = TouchControlsSetup.CreateRect("Line", plate);
            line.anchorMin = new Vector2(0f, 1f);
            line.anchorMax = new Vector2(1f, 1f);
            line.pivot = new Vector2(0.5f, 1f);
            line.sizeDelta = new Vector2(0f, 1f);
            AddImage(line, MainMenuLook.Gold);

            Text name = CreateText("Name", plate, MainMenuLook.CharacterName, MainMenuLayout.NameFontSize, MainMenuLook.Parchment,
                TextAnchor.MiddleCenter);
            name.fontStyle = FontStyle.Bold;
            name.rectTransform.anchorMin = new Vector2(0f, 0.42f);
            name.rectTransform.anchorMax = new Vector2(1f, 1f);
            name.rectTransform.sizeDelta = Vector2.zero;

            Color soft = MainMenuLook.Gold;
            Text title = CreateText("Title", plate, MainMenuLook.CharacterTitle, MainMenuLayout.TitleFontSize, soft,
                TextAnchor.MiddleCenter);
            title.rectTransform.anchorMin = new Vector2(0f, 0f);
            title.rectTransform.anchorMax = new Vector2(1f, 0.44f);
            title.rectTransform.sizeDelta = Vector2.zero;

            return plate;
        }

        // -------------------------------------------------------------- helpers

        private static void AddPlate(RectTransform rect)
        {
            AddImage(rect, MainMenuLook.Bronze);
            RectTransform stone = TouchControlsSetup.CreateRect("Stone", rect);
            Inset(stone, 1.5f);
            AddImage(stone, MainMenuLook.Stone);
        }

        private static Image AddImage(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(string name, Transform parent, string content, int size, Color color, TextAnchor alignment)
        {
            return AddText(TouchControlsSetup.CreateRect(name, parent), content, size, color, alignment);
        }

        private static Text AddText(RectTransform rect, string content, int size, Color color, TextAnchor alignment)
        {
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = UiFont.Default;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Places a rectangle by its top-left corner, measured from the top-left corner of its parent.</summary>
        private static void TopLeft(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(left, -top);
        }

        private static void Inset(RectTransform rect, float margin)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
        }
    }
}
