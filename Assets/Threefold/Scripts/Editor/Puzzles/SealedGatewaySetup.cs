using System.Text;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.Puzzles;
using Threefold.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Builds the example puzzle: an ancient sealed gateway in the south-east corner of the Fantasy
    /// Playground. It opens after three actions, in order:
    ///   1. reach the lookout, a dark stone block that must be climbed;
    ///   2. from the lookout, strike the crystal above the gateway with Arcane Bolt;
    ///   3. step onto the pressure plate in front of the gateway before the crystal's charge fades.
    /// Behind the gateway, a reliquary can be opened by hand.
    ///
    /// The command creates the geometry with its collision, the puzzle elements and every
    /// reference between them, the detection zones, and the visual and audio feedback. It is safe
    /// to run repeatedly: it adds only what is missing, fills only empty references, and keeps
    /// existing objects, positions and tuned values. Fantasy Playground runs it as well.
    /// </summary>
    public static class SealedGatewaySetup
    {
        private const string MenuPath = "Threefold/Setup/Sealed Gateway Puzzle";
        private const string DialogTitle = "Sealed Gateway Puzzle";

        internal const string SiteId = "FantasyPlayground.SealedGateway";
        internal const string RootName = "SealedGateway Puzzle";
        private const string KeyPrefix = SiteId + ".";

        /// <summary>Where the foot of the gateway's front face stands in the playground. The gateway faces north.</summary>
        internal static readonly Vector3 SitePosition = new Vector3(14.5f, 0f, -12.9f);
        internal static readonly Quaternion SiteRotation = Quaternion.identity;

        // Layout, in the site's space: origin at the foot of the gateway's front face, +Z out of
        // the gateway, +X to its right. Distances were checked by calculation against the
        // playground's existing objects (see Docs/Puzzles.md).
        internal static readonly Vector3 LookoutPosition = new Vector3(-7.7f, 0f, -1.8f);
        internal const float LookoutHeight = 2.6f;
        private static readonly Vector3 CratePosition = new Vector3(-9.7f, 0f, -1.8f);
        private static readonly Vector3 CrateSize = new Vector3(1.4f, 1.1f, 1.4f);
        internal static readonly Vector3 PlatePosition = new Vector3(0f, 0f, 2.4f);
        internal static readonly Vector3 CrystalPosition = new Vector3(0f, 5.3f, 0.5f);
        internal static readonly Vector3 ReliquaryPosition = new Vector3(0f, 0f, -2.4f);
        internal const float TimerSeconds = 6f;
        private const int TimerStones = 8;

        /// <summary>Areas, in the site's space, that the playground keeps free of grass and moss.</summary>
        private static readonly Rect[] ReservedAreas =
        {
            Rect.MinMaxRect(-4f, -5.2f, 4f, 5f),
            Rect.MinMaxRect(-11.6f, -4.4f, -5.2f, 0.8f),
        };

        private static readonly Color ArcaneLight = new Color(0.55f, 0.75f, 1f);
        private static readonly Color RelicLight = new Color(1f, 0.78f, 0.5f);

        private static int addedCount;

        [MenuItem(MenuPath, false, 5)]
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
                    "The open scene has no Mage yet. Run Threefold > Setup > Fantasy Playground instead; it builds " +
                    "the level, the Mage and this puzzle together.", "OK");
                return;
            }

            Undo.SetCurrentGroupName(DialogTitle);
            int undoGroup = Undo.GetCurrentGroup();
            var report = new StringBuilder();

            InteractionSetup.Ensure(scene, motor.gameObject, report);
            EnvironmentAssets environment = EnvironmentAssets.Ensure(false, report);
            PuzzleSite site = Ensure(scene, environment, false, report);
            MagePrototypeSetup.AppendValidation(scene, report);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = site.gameObject;

            string summary = report.ToString().TrimEnd();
            Debug.Log($"[Threefold] {DialogTitle} finished.\n{summary}");

            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    summary + "\n\nThis scene has never been saved. Save it with File > Save As.", "OK");
                return;
            }

            if (EditorUtility.DisplayDialog(DialogTitle, summary + "\n\nPress Play to try it.", "Save scene", "Not now")
                && !EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError($"[Threefold] The scene '{scene.path}' could not be saved. Save it manually.");
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>True if a world position lies where the puzzle stands, so decoration is kept away.</summary>
        internal static bool IsReserved(Vector3 worldPosition)
        {
            Vector3 local = Quaternion.Inverse(SiteRotation) * (worldPosition - SitePosition);
            var point = new Vector2(local.x, local.z);
            for (int i = 0; i < ReservedAreas.Length; i++)
            {
                if (ReservedAreas[i].Contains(point))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The puzzle's root object in the scene, or null.</summary>
        internal static PuzzleSite FindSite(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PuzzleSite site in root.GetComponentsInChildren<PuzzleSite>(true))
                {
                    if (site.SiteId == SiteId)
                    {
                        return site;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Builds whatever is missing of the puzzle and connects it. The player's interactor and
        /// prompt are added by <see cref="InteractionSetup"/>, which the rig setup runs.
        /// </summary>
        internal static PuzzleSite Ensure(Scene scene, EnvironmentAssets environment, bool rebuildAssets, StringBuilder report)
        {
            addedCount = 0;
            PuzzleAssets assets = PuzzleAssets.Ensure(environment, rebuildAssets, report);

            PuzzleSite site = FindSite(scene);
            bool newSite = site == null;
            if (newSite)
            {
                var rootObject = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(rootObject, scene);
                rootObject.transform.SetPositionAndRotation(SitePosition, SiteRotation);
                site = rootObject.AddComponent<PuzzleSite>();
                site.SiteId = SiteId;
                Undo.RegisterCreatedObjectUndo(rootObject, DialogTitle);
            }

            Transform root = site.transform;

            PresenceZone lookout = BuildLookout(root, assets, out Transform beamAnchor);
            PressurePlate plate = BuildPlate(root, assets);
            Transform gateway = BuildSanctum(root, assets);
            SpellActivator crystal = BuildCrystal(gateway, assets, lookout, out Transform crystalBody);
            BuildMechanism(root, lookout, crystal, plate, out PuzzleSequence sequence, out TimedMechanism timer);
            BuildSeal(gateway, assets, sequence);
            BuildLamps(gateway, assets, sequence);
            BuildTimerStones(plate, assets, timer);
            BuildBeam(root, assets, lookout, beamAnchor, crystalBody);
            BuildReliquary(gateway, assets);
            EnsureAudioListener(scene, report);

            report.AppendLine(newSite
                ? "Built the Sealed Gateway puzzle in the south-east corner: lookout, crystal, timer, pressure plate, " +
                  "gateway with its seal and lamps, and the reliquary behind it."
                : addedCount > 0
                    ? $"Kept the existing Sealed Gateway puzzle and added {addedCount} missing part(s)."
                    : "Kept the existing Sealed Gateway puzzle; nothing was missing.");

            return site;
        }

        // ------------------------------------------------------------- lookout

        /// <summary>
        /// A dark stone block 2.6 m high, the colour of the traversal course, with a climbing crate
        /// beside it. A ring of runes on top and a standing stone at its corner glow while the
        /// player stands there.
        /// </summary>
        private static PresenceZone BuildLookout(Transform root, PuzzleAssets assets, out Transform beamAnchor)
        {
            Material[] darkAndRune = { assets.DarkStoneMaterial, assets.RuneMaterial };
            Transform lookout = Child(root, "Lookout", LookoutPosition).transform;

            MeshChild(lookout, "Block", assets.Block(Vector3.one * LookoutHeight), new[] { assets.DarkStoneMaterial },
                Vector3.zero, true, true);
            GameObject ring = MeshChild(lookout, "RuneRing", assets.LookoutRing, darkAndRune,
                new Vector3(0f, LookoutHeight, 0f), false, false);
            // On the corner facing the start of the level, and never between the player and the crystal.
            GameObject stele = MeshChild(lookout, "Stele", assets.Stele, darkAndRune,
                new Vector3(-0.95f, LookoutHeight, 0.95f), true, false);
            beamAnchor = Child(stele.transform, "BeamAnchor", new Vector3(0f, 1.5f, 0f)).transform;

            MeshChild(root, "LookoutCrate", assets.Block(CrateSize), new[] { assets.DarkStoneMaterial }, CratePosition, true, true);

            PresenceZone zone = GetOrAdd<PresenceZone>(lookout.gameObject, out bool added);
            if (added)
            {
                // Starts just above head height of someone on the crate, so only the top counts.
                zone.ConfigureDetection(new Vector3(0f, LookoutHeight + 0.85f, 0f), new Vector3(2f, 1f, 2f), 0.1f, 0.25f);
                zone.ConfigureFilter(true, false, false, 0f);
            }

            PuzzleGlow glow = GetOrAdd<PuzzleGlow>(lookout.gameObject, out added);
            if (added || glow.Source == null)
            {
                Undo.RecordObject(glow, DialogTitle);
                glow.Bind(zone, null, new Renderer[] { ring.GetComponent<MeshRenderer>(), stele.GetComponent<MeshRenderer>() },
                    PuzzleAssets.AccentSlot, null);
                MarkChanged(glow);
            }

            return zone;
        }

        // --------------------------------------------------------------- plate

        private static PressurePlate BuildPlate(Transform root, PuzzleAssets assets)
        {
            Transform plateTransform = Child(root, "PressurePlate", PlatePosition).transform;
            MeshChild(plateTransform, "Base", assets.PlateBase, new[] { assets.StoneMaterial }, Vector3.zero, true, true);
            GameObject top = MeshChild(plateTransform, "Top", assets.PlateTop,
                new[] { assets.StoneMaterial, assets.RuneMaterial }, new Vector3(0f, 0.08f, 0f), false, false);

            PressurePlate plate = GetOrAdd<PressurePlate>(plateTransform.gameObject, out bool added);
            if (added)
            {
                plate.ConfigureDetection(new Vector3(0f, 0.55f, 0f), new Vector3(1.3f, 0.9f, 1.3f), 0f, 0.15f);
                plate.ConfigureFilter(true, false, true, 5f);
            }

            Undo.RecordObject(plate, DialogTitle);
            if (plate.PlateTop == null)
            {
                plate.PlateTop = top.transform;
            }

            SetKeyIfEmpty(plate, "Plate");
            MarkChanged(plate);

            EnsureAudio(plateTransform.gameObject, plate, PuzzleTone.Click, PuzzleTone.None, PuzzleTone.None);
            return plate;
        }

        /// <summary>
        /// Eight stones in a ring around the plate. They light when the crystal is charged and go
        /// out one by one as the time runs, with a tick each.
        /// </summary>
        private static void BuildTimerStones(PressurePlate plate, PuzzleAssets assets, TimedMechanism timer)
        {
            Transform ring = Child(plate.transform, "TimerStones", Vector3.zero).transform;
            var stones = new Renderer[TimerStones];
            for (int i = 0; i < TimerStones; i++)
            {
                // Clockwise from the side facing away from the gateway.
                float angle = (90f - i * 360f / TimerStones) * Mathf.Deg2Rad;
                var position = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.15f;
                Quaternion rotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg + 90f, 0f);
                stones[i] = MeshChild(ring, "Stone " + (i + 1), assets.TimerStone, new[] { assets.RuneMaterial }, position,
                    false, false, rotation).GetComponent<MeshRenderer>();
            }

            AudioSource tickSource = EnsureAudioSource(ring.gameObject);
            TimerIndicator indicator = GetOrAdd<TimerIndicator>(ring.gameObject, out bool added);
            if (added || indicator.Timer == null)
            {
                Undo.RecordObject(indicator, DialogTitle);
                indicator.Bind(timer, stones, tickSource);
                MarkChanged(indicator);
            }

            PuzzleGlow glow = GetOrAdd<PuzzleGlow>(plate.gameObject, out added);
            if (added || glow.Source == null)
            {
                Undo.RecordObject(glow, DialogTitle);
                // The plate beckons while the timer runs.
                Transform top = plate.PlateTop;
                glow.Bind(plate, timer, new Renderer[] { top != null ? top.GetComponent<MeshRenderer>() : null },
                    PuzzleAssets.AccentSlot, null);
                MarkChanged(glow);
            }
        }

        // ------------------------------------------------------------- gateway

        /// <summary>
        /// The sanctum: a roofed stone chamber whose only way in is the gateway in its north face.
        /// It is roofed so it cannot be climbed into, and its walls stand 3.6 m high.
        /// </summary>
        private static Transform BuildSanctum(Transform root, PuzzleAssets assets)
        {
            Transform gateway = Child(root, "Gateway", Vector3.zero).transform;
            Material[] stone = { assets.StoneMaterial };

            MeshChild(gateway, "Frame", assets.GatewayFrame, new[] { assets.StoneMaterial, assets.DarkStoneMaterial },
                Vector3.zero, true, true);

            Transform walls = Child(gateway, "Sanctum", Vector3.zero).transform;
            MeshChild(walls, "FrontLeft", assets.Block(new Vector3(1.4f, 3.6f, 0.8f)), stone, new Vector3(-1.9f, 0f, -0.4f), true, true);
            MeshChild(walls, "FrontRight", assets.Block(new Vector3(1.4f, 3.6f, 0.8f)), stone, new Vector3(1.9f, 0f, -0.4f), true, true);
            MeshChild(walls, "Lintel", assets.Block(new Vector3(2.4f, 0.6f, 0.8f)), stone, new Vector3(0f, 3f, -0.4f), true, true);
            MeshChild(walls, "WallWest", assets.Block(new Vector3(0.6f, 3.6f, 3.4f)), stone, new Vector3(-2.3f, 0f, -2.5f), true, true);
            MeshChild(walls, "WallEast", assets.Block(new Vector3(0.6f, 3.6f, 3.4f)), stone, new Vector3(2.3f, 0f, -2.5f), true, true);
            MeshChild(walls, "WallBack", assets.Block(new Vector3(5.2f, 3.6f, 0.6f)), stone, new Vector3(0f, 0f, -3.9f), true, true);
            MeshChild(walls, "Roof", assets.Block(new Vector3(5.2f, 0.35f, 4.2f)), stone, new Vector3(0f, 3.6f, -2.1f), true, true);
            return gateway;
        }

        /// <summary>
        /// The crystal floating on its bracket above the gateway. It answers Arcane Bolt only while
        /// the player stands on the lookout; otherwise the bolt fizzles.
        /// </summary>
        private static SpellActivator BuildCrystal(Transform gateway, PuzzleAssets assets, PresenceZone lookout,
            out Transform body)
        {
            GameObject crystalObject = Child(gateway, "Crystal", CrystalPosition);
            GameObject bodyObject = MeshChild(crystalObject.transform, "Body", assets.Crystal, new[] { assets.RuneMaterial },
                Vector3.zero, false, false);
            body = bodyObject.transform;
            GetOrAdd<FloatingMotion>(bodyObject, out _);

            MeshRenderer bodyRenderer = bodyObject.GetComponent<MeshRenderer>();
            if (bodyRenderer.shadowCastingMode != ShadowCastingMode.Off)
            {
                Undo.RecordObject(bodyRenderer, DialogTitle);
                bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            Light light = EnsurePointLight(crystalObject.transform, "Glow", new Vector3(0f, 0.2f, 0f), ArcaneLight, 7f);

            if (!crystalObject.TryGetComponent(out Collider _))
            {
                // A solid collider: spells ignore triggers.
                SphereCollider sphere = Undo.AddComponent<SphereCollider>(crystalObject);
                sphere.center = new Vector3(0f, 0.2f, 0f);
                sphere.radius = 0.5f;
                addedCount++;
            }

            SpellActivator activator = GetOrAdd<SpellActivator>(crystalObject, out bool added);
            Undo.RecordObject(activator, DialogTitle);
            if (added)
            {
                activator.AcceptedElements = SpellElement.Arcane;
                activator.Mode = ActivatorMode.Latch;
            }

            if (activator.ReceptiveWhile == null)
            {
                activator.ReceptiveWhile = lookout;
            }

            SetKeyIfEmpty(activator, "Crystal");
            MarkChanged(activator);

            PuzzleGlow glow = GetOrAdd<PuzzleGlow>(crystalObject, out added);
            if (added || glow.Source == null)
            {
                Undo.RecordObject(glow, DialogTitle);
                glow.Bind(activator, null, new Renderer[] { bodyRenderer }, -1, light);
                glow.SetLightLevels(0f, 0.9f, 3f);
                MarkChanged(glow);
            }

            // Its fading is announced by the sequence, so the crystal itself stays quiet then.
            EnsureAudio(crystalObject, activator, PuzzleTone.Chime, PuzzleTone.None, PuzzleTone.Fizzle);
            return activator;
        }

        /// <summary>
        /// The rules: a sequence of lookout (held until the crystal is struck), crystal (held until
        /// the plate is pressed) and plate; and a timer that the crystal starts, which puts the
        /// crystal out again when it runs out.
        /// </summary>
        private static void BuildMechanism(Transform root, PresenceZone lookout, SpellActivator crystal, PressurePlate plate,
            out PuzzleSequence sequence, out TimedMechanism timer)
        {
            GameObject mechanism = Child(root, "Mechanism", Vector3.zero);

            sequence = GetOrAdd<PuzzleSequence>(mechanism, out bool sequenceAdded);
            timer = GetOrAdd<TimedMechanism>(mechanism, out bool timerAdded);

            Undo.RecordObject(sequence, DialogTitle);
            if (sequence.StepCount == 0)
            {
                sequence.SetSteps(new[]
                {
                    new SequenceStep(lookout, StepRule.HoldUntilNextStep),
                    new SequenceStep(crystal, StepRule.HoldUntilNextStep),
                    new SequenceStep(plate, StepRule.Latch),
                });
            }

            if (sequenceAdded)
            {
                sequence.AddLockWhenSolved(timer);
            }

            SetKeyIfEmpty(sequence, "Sequence");
            MarkChanged(sequence);

            Undo.RecordObject(timer, DialogTitle);
            if (timerAdded)
            {
                timer.Duration = TimerSeconds;
                timer.AddResetTarget(crystal);
            }

            if (timer.Trigger == null)
            {
                timer.Trigger = crystal;
            }

            SetKeyIfEmpty(timer, "Timer");
            MarkChanged(timer);
        }

        /// <summary>The sealing slab in the gateway. It sinks into the ground when the sequence is solved.</summary>
        private static void BuildSeal(Transform gateway, PuzzleAssets assets, PuzzleSequence sequence)
        {
            GameObject seal = Child(gateway, "Seal", new Vector3(0f, 0f, -0.4f));
            GameObject slab = MeshChild(seal.transform, "Slab", assets.SealSlab,
                new[] { assets.DarkStoneMaterial, assets.RuneMaterial }, Vector3.zero, false, false);

            if (!slab.TryGetComponent(out Collider _))
            {
                BoxCollider box = Undo.AddComponent<BoxCollider>(slab);
                box.center = new Vector3(0f, 1.5f, 0f);
                box.size = new Vector3(2.36f, 3f, 0.4f);
                addedCount++;
            }

            // A moving collider is marked kinematic so physics treats it as moving, not as static scenery.
            Rigidbody body = GetOrAdd<Rigidbody>(slab, out bool added);
            if (added)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }

            PuzzleDoor door = GetOrAdd<PuzzleDoor>(seal, out added);
            Undo.RecordObject(door, DialogTitle);
            if (added)
            {
                door.Control = DoorControl.Input;
                door.ConfigureMotion(slab.transform, new Vector3(0f, -3.05f, 0f), Vector3.zero, 3.2f, 1.5f);
            }

            if (door.Leaf.target == null)
            {
                door.Leaf.target = slab.transform;
            }

            if (door.InputElement == null)
            {
                door.InputElement = sequence;
            }

            MarkChanged(door);

            PuzzleGlow glow = GetOrAdd<PuzzleGlow>(seal, out added);
            if (added || glow.Source == null)
            {
                Undo.RecordObject(glow, DialogTitle);
                // The seal's runes pulse while it is closed and flare when it is broken.
                glow.Bind(sequence, null, new Renderer[] { slab.GetComponent<MeshRenderer>() }, PuzzleAssets.AccentSlot, null);
                MarkChanged(glow);
            }

            EnsureAudio(seal, door, PuzzleTone.Rumble, PuzzleTone.Rumble, PuzzleTone.None);
        }

        /// <summary>Three rune gems on the lintel, one per step, lit as the steps are done.</summary>
        private static void BuildLamps(Transform gateway, PuzzleAssets assets, PuzzleSequence sequence)
        {
            Transform lamps = Child(gateway, "StepLamps", Vector3.zero).transform;
            var renderers = new Renderer[3];
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i] = MeshChild(lamps, "Lamp " + (i + 1), assets.Lamp, new[] { assets.RuneMaterial },
                    new Vector3((i - 1) * 0.6f, 3.3f, 0.04f), false, false).GetComponent<MeshRenderer>();
            }

            SequenceIndicator indicator = GetOrAdd<SequenceIndicator>(lamps.gameObject, out bool added);
            if (added || indicator.Sequence == null)
            {
                Undo.RecordObject(indicator, DialogTitle);
                indicator.Bind(sequence, renderers);
                MarkChanged(indicator);
            }

            // Step chimes, the fall when progress is lost, and the fanfare when solved.
            EnsureAudio(lamps.gameObject, sequence, PuzzleTone.None, PuzzleTone.None, PuzzleTone.None);
        }

        /// <summary>A thread of light from the lookout's stele to the crystal, shown while the player is on the lookout.</summary>
        private static void BuildBeam(Transform root, PuzzleAssets assets, PresenceZone lookout, Transform from, Transform to)
        {
            GameObject beamObject = Child(root, "LookoutBeam", Vector3.zero);
            LineRenderer line = GetOrAdd<LineRenderer>(beamObject, out bool added);
            if (added)
            {
                line.sharedMaterial = assets.BeamMaterial;
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.SetPosition(0, from.position);
                line.SetPosition(1, to.position);
                line.widthMultiplier = 0.08f;
                line.startColor = new Color(0.55f, 0.75f, 1f, 0.9f);
                line.endColor = new Color(0.8f, 0.92f, 1f, 0.9f);
                line.textureMode = LineTextureMode.Stretch;
                line.numCapVertices = 2;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
            }

            PuzzleBeam beam = GetOrAdd<PuzzleBeam>(beamObject, out added);
            if (added || beam.Source == null || beam.From == null || beam.To == null)
            {
                Undo.RecordObject(beam, DialogTitle);
                beam.Bind(lookout, from, to);
                MarkChanged(beam);
            }
        }

        /// <summary>A stone chest inside the sanctum that the player opens by hand, lighting the room.</summary>
        private static void BuildReliquary(Transform gateway, PuzzleAssets assets)
        {
            GameObject relic = Child(gateway, "Reliquary", ReliquaryPosition);
            GameObject body = MeshChild(relic.transform, "Body", assets.ReliquaryBody,
                new[] { assets.StoneMaterial, assets.DarkStoneMaterial }, Vector3.zero, false, true);
            if (!body.TryGetComponent(out Collider _))
            {
                BoxCollider box = Undo.AddComponent<BoxCollider>(body);
                box.center = new Vector3(0f, 0.33f, 0f);
                box.size = new Vector3(1.2f, 0.66f, 0.8f);
                addedCount++;
            }

            GameObject lid = MeshChild(relic.transform, "Lid", assets.ReliquaryLid,
                new[] { assets.StoneMaterial, assets.RuneMaterial }, new Vector3(0f, 0.66f, 0f), false, false);
            GameObject inner = MeshChild(relic.transform, "Glow", assets.ReliquaryGlow, new[] { assets.RuneMaterial },
                Vector3.zero, false, false);
            Light light = EnsurePointLight(relic.transform, "SanctumLight", new Vector3(0f, 1.7f, 0.3f), RelicLight, 5f);

            InteractiveSwitch chest = GetOrAdd<InteractiveSwitch>(relic, out bool added);
            Undo.RecordObject(chest, DialogTitle);
            if (added)
            {
                chest.Configure(SwitchMode.OneShot, "Open", new Vector3(0f, 0.9f, 0.4f), lid.transform,
                    new Vector3(0f, 0.08f, -0.55f), new Vector3(-14f, 0f, 0f), 1.2f);
            }

            if (chest.MovingPart.target == null)
            {
                chest.MovingPart.target = lid.transform;
            }

            SetKeyIfEmpty(chest, "Reliquary");
            MarkChanged(chest);

            PuzzleGlow glow = GetOrAdd<PuzzleGlow>(relic, out added);
            if (added || glow.Source == null)
            {
                Undo.RecordObject(glow, DialogTitle);
                glow.Bind(chest, null, new Renderer[] { inner.GetComponent<MeshRenderer>() }, -1, light);
                glow.SetColors(new Color(0.05f, 0.04f, 0.02f), new Color(0.6f, 0.45f, 0.15f), new Color(3f, 2.2f, 0.8f));
                glow.SetLightLevels(0f, 0.35f, 1.8f);
                MarkChanged(glow);
            }

            EnsureAudio(relic, chest, PuzzleTone.Chime, PuzzleTone.None, PuzzleTone.Fizzle);
        }

        // ------------------------------------------------------------- helpers

        /// <summary>Returns the direct child with the given name, creating it if it is missing.</summary>
        private static GameObject Child(Transform parent, string name, Vector3 localPosition, Quaternion? localRotation = null)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                return existing.gameObject;
            }

            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            child.transform.localRotation = localRotation ?? Quaternion.identity;
            Undo.RegisterCreatedObjectUndo(child, DialogTitle);
            addedCount++;
            return child;
        }

        /// <summary>
        /// Returns a child that shows a mesh, creating what is missing. An existing child keeps its
        /// position, materials and colliders; only an empty mesh or material is filled.
        /// </summary>
        private static GameObject MeshChild(Transform parent, string name, Mesh mesh, Material[] materials,
            Vector3 localPosition, bool collide, bool isStatic, Quaternion? localRotation = null)
        {
            bool existed = parent.Find(name) != null;
            GameObject child = Child(parent, name, localPosition, localRotation);

            MeshFilter filter = GetOrAdd<MeshFilter>(child, out _);
            if (filter.sharedMesh == null)
            {
                Undo.RecordObject(filter, DialogTitle);
                filter.sharedMesh = mesh;
                MarkChanged(filter);
            }

            MeshRenderer meshRenderer = GetOrAdd<MeshRenderer>(child, out bool rendererAdded);
            if (rendererAdded || meshRenderer.sharedMaterial == null)
            {
                Undo.RecordObject(meshRenderer, DialogTitle);
                meshRenderer.sharedMaterials = materials;
                MarkChanged(meshRenderer);
            }

            if (collide && !child.TryGetComponent(out Collider _))
            {
                MeshCollider meshCollider = Undo.AddComponent<MeshCollider>(child);
                meshCollider.sharedMesh = filter.sharedMesh;
                addedCount++;
            }

            if (!existed)
            {
                child.isStatic = isStatic;
            }

            return child;
        }

        private static T GetOrAdd<T>(GameObject target, out bool added) where T : Component
        {
            if (target.TryGetComponent(out T existing))
            {
                added = false;
                return existing;
            }

            added = true;
            addedCount++;
            return Undo.AddComponent<T>(target);
        }

        private static void SetKeyIfEmpty(PuzzleElement element, string name)
        {
            if (string.IsNullOrEmpty(element.PersistenceKey))
            {
                element.PersistenceKey = KeyPrefix + name;
            }
        }

        private static AudioSource EnsureAudioSource(GameObject target)
        {
            AudioSource source = GetOrAdd<AudioSource>(target, out bool added);
            if (added)
            {
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 3f;
                source.maxDistance = 40f;
                source.dopplerLevel = 0f;
            }

            return source;
        }

        private static void EnsureAudio(GameObject target, PuzzleElement source, PuzzleTone activated, PuzzleTone deactivated,
            PuzzleTone rejected)
        {
            // The audio source is set up first, so the one PuzzleAudio requires is not made with default settings.
            EnsureAudioSource(target);
            PuzzleAudio audio = GetOrAdd<PuzzleAudio>(target, out bool added);
            if (added || audio.Source == null)
            {
                Undo.RecordObject(audio, DialogTitle);
                audio.Bind(source, activated, deactivated, rejected);
                MarkChanged(audio);
            }
        }

        private static Light EnsurePointLight(Transform parent, string name, Vector3 localPosition, Color color, float range)
        {
            GameObject lightObject = Child(parent, name, localPosition);
            Light light = GetOrAdd<Light>(lightObject, out bool added);
            if (added)
            {
                light.type = LightType.Point;
                light.color = color;
                light.range = range;
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                light.enabled = false;
            }

            return light;
        }

        /// <summary>The puzzle's sounds need a listener; a camera made by hand may not have one.</summary>
        private static void EnsureAudioListener(Scene scene, StringBuilder report)
        {
            if (FindInScene<AudioListener>(scene) != null)
            {
                return;
            }

            Camera viewCamera = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera candidate in root.GetComponentsInChildren<Camera>(true))
                {
                    if (candidate.CompareTag("MainCamera"))
                    {
                        viewCamera = candidate;
                        break;
                    }
                }

                if (viewCamera != null)
                {
                    break;
                }
            }

            if (viewCamera == null)
            {
                report.AppendLine("WARNING: the scene has no main camera with an Audio Listener, so puzzle sounds are not heard.");
                return;
            }

            Undo.AddComponent<AudioListener>(viewCamera.gameObject);
            report.AppendLine($"Added an Audio Listener to '{viewCamera.name}' so puzzle sounds can be heard.");
        }
    }
}
