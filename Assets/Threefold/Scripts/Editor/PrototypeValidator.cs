using System.Text;
using Threefold.Cameras;
using Threefold.Characters;
using Threefold.Combat;
using Threefold.CombatFeedback;
using Threefold.Enemies;
using Threefold.InputHandling;
using Threefold.Objectives;
using Threefold.Pooling;
using Threefold.TouchControls;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using static Threefold.EditorTools.SceneSetupUtility;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Checks that a prototype scene is complete and correctly connected: no missing scripts, and
    /// every reference that the playable loop depends on is assigned. It runs at the end of the
    /// setup commands, in the automated build, and on demand from the menu.
    /// </summary>
    public static class PrototypeValidator
    {
        private const string MenuPath = "Threefold/Validate/Open Scene";
        private const string DialogTitle = "Scene Validation";

        [MenuItem(MenuPath, false, 40)]
        public static void ValidateOpenScene()
        {
            var problems = new StringBuilder();
            int count = Validate(SceneManager.GetActiveScene(), problems);

            string message = count == 0
                ? "No problems found. Every part of the playable loop is present and connected."
                : $"{count} problem(s) found:\n\n{problems.ToString().TrimEnd()}";

            if (count == 0)
            {
                Debug.Log($"[Threefold] {DialogTitle}: {message}");
            }
            else
            {
                Debug.LogError($"[Threefold] {DialogTitle}: {message}");
            }

            EditorUtility.DisplayDialog(DialogTitle, message, "OK");
        }

        /// <summary>Appends one line per problem to <paramref name="problems"/>.</summary>
        /// <returns>The number of problems found.</returns>
        internal static int Validate(Scene scene, StringBuilder problems)
        {
            int count = 0;

            void Problem(string text)
            {
                count++;
                problems.AppendLine("- " + text);
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
                    if (missing > 0)
                    {
                        Problem($"'{child.name}' has {missing} missing script(s).");
                    }
                }
            }

            ThirdPersonMotor motor = FindInScene<ThirdPersonMotor>(scene);
            if (motor == null)
            {
                Problem("There is no Mage (no ThirdPersonMotor) in the scene.");
                return count;
            }

            GameObject mage = motor.gameObject;
            if (motor.InputSource == null) Problem("The Mage's movement script has no input source.");
            if (motor.CameraTransform == null) Problem("The Mage's movement script has no camera.");
            if (mage.GetComponent<CharacterController>() == null) Problem("The Mage has no CharacterController.");

            ThirdPersonCamera cameraRig = FindInScene<ThirdPersonCamera>(scene);
            if (cameraRig == null)
            {
                Problem("There is no third-person camera.");
            }
            else
            {
                if (cameraRig.Target == null) Problem("The camera has no target to follow.");
                if (cameraRig.InputSource == null) Problem("The camera has no input source.");
                if (cameraRig.GetComponent<Camera>() == null) Problem("The camera rig is not on a Camera.");
            }

            CombinedCharacterInput combined = mage.GetComponent<CombinedCharacterInput>();
            if (combined == null)
            {
                Problem("The Mage has no combined input, so touch and desktop input are not merged.");
            }
            else
            {
                if (combined.Sources.Count < 2) Problem("The combined input has fewer than two sources (expected desktop and touch).");
                foreach (CharacterInputSource source in combined.Sources)
                {
                    if (source == null) Problem("The combined input contains an empty source.");
                }

                if (motor.InputSource != combined) Problem("The Mage does not read the combined input.");
                if (cameraRig != null && cameraRig.InputSource != combined) Problem("The camera does not read the combined input.");
            }

            TouchControlsInput touch = FindInScene<TouchControlsInput>(scene);
            if (touch == null)
            {
                Problem("There are no touch controls.");
            }
            else
            {
                if (touch.Joystick == null) Problem("The touch controls have no joystick.");
                if (touch.LookArea == null) Problem("The touch controls have no camera look area.");
                if (touch.JumpButton == null) Problem("The touch controls have no jump button.");
                if (touch.AttackGestureButton == null) Problem("The touch controls have no ATTACK gesture button (run the setup command to upgrade).");
                if (touch.BlockButton == null) Problem("The touch controls have no block button.");
                if (touch.DisciplineSelector == null) Problem("The touch controls have no discipline selector.");
                if (touch.LayoutVersion < TouchControlsInput.CurrentLayoutVersion) Problem("The touch controls use an earlier layout (run the setup command to upgrade).");
                if (touch.Disciplines == null) Problem("The touch controls do not show the Mage's discipline.");
            }

            if (FindInScene<ControlSchemeSelector>(scene) == null) Problem("Nothing selects between touch and desktop controls.");

            PuzzleValidator.Validate(scene, mage, Problem);

            if (mage.GetComponent<Health>() == null) Problem("The Mage has no Health.");
            if (mage.GetComponent<Mana>() == null) Problem("The Mage has no Mana.");
            if (mage.GetComponent<PlayerAbilityInput>() == null) Problem("The Mage's attack input is not connected.");
            if (mage.GetComponent<PlayerGuard>() == null) Problem("The Mage has no block (PlayerGuard).");
            if (mage.GetComponent<Threefold.Magic.PlayerDisciplines>() == null) Problem("The Mage has no discipline selection.");
            if (mage.TryGetComponent(out AbilityCaster mageCaster) && mageCaster.GetAbility(PlayerAbilityInput.DefaultMeleeSlot) == null)
            {
                Problem("The Mage has no tap (melee) ability.");
            }

            AbilityCaster caster = mage.GetComponent<AbilityCaster>();
            if (caster == null)
            {
                Problem("The Mage has no ability caster.");
            }
            else
            {
                AbilityDefinition primary = caster.GetAbility(0);
                if (primary == null)
                {
                    Problem("The Mage has no primary attack assigned.");
                }
                else if (primary is ProjectileSpellDefinition spell)
                {
                    if (spell.ProjectilePrefab == null)
                    {
                        Problem($"The spell '{spell.name}' has no projectile prefab.");
                    }
                    else if (spell.ProjectilePrefab.GetComponent<PooledObject>() == null)
                    {
                        Problem($"The projectile prefab of '{spell.name}' cannot be pooled.");
                    }
                }
            }

            if (FindInScene<PlayerHud>(scene) == null) Problem("There is no combat HUD.");

            // The character model is optional; if its driver is present, it must be able to animate.
            if (mage.TryGetComponent(out CharacterAnimationDriver driver))
            {
                if (driver.Animator == null)
                {
                    Problem("The Mage's animation driver has no Animator.");
                }
                else if (driver.Animator.runtimeAnimatorController == null)
                {
                    Problem("The Mage's Animator has no Animator Controller.");
                }
                else if (driver.Animator.avatar == null || !driver.Animator.avatar.isHuman)
                {
                    Problem("The Mage's Animator has no Humanoid avatar.");
                }
            }

            DefeatTargetsObjective objective = FindInScene<DefeatTargetsObjective>(scene);
            if (objective == null)
            {
                Problem("There is no objective.");
            }
            else
            {
                if (objective.TotalCount == 0) Problem("The objective has no targets, so it cannot be completed.");
                foreach (Health target in objective.Targets)
                {
                    if (target == null) Problem("The objective lists a target that no longer exists.");
                }
            }

            if (FindInScene<PrototypeSession>(scene) == null) Problem("There is no session, so the level cannot be restarted.");

            ObjectiveHud objectiveHud = FindInScene<ObjectiveHud>(scene);
            if (objectiveHud == null)
            {
                Problem("The objective is not shown on screen.");
            }
            else if (!objectiveHud.HasDefeatPanel)
            {
                Problem("The HUD has no defeat panel, so a defeated player cannot restart by touch.");
            }

            ValidateEnemies(scene, mage, Problem);
            VisualStyleSetup.Validate(scene, Problem);
            return count;
        }

        /// <summary>Checks the enemies, if the scene has any: their parts, the director, and navigation.</summary>
        private static void ValidateEnemies(Scene scene, GameObject mage, System.Action<string> problem)
        {
            var enemies = new System.Collections.Generic.List<EnemyController>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                enemies.AddRange(root.GetComponentsInChildren<EnemyController>(true));
            }

            if (enemies.Count == 0)
            {
                return;
            }

            if (mage.TryGetComponent(out Health mageHealth) && mageHealth.Team != CombatTeam.Player)
            {
                problem("The Mage is not on the Player team.");
            }

            EnemyDirector director = FindInScene<EnemyDirector>(scene);
            if (director == null)
            {
                problem("There are enemies but no enemy director, so their attacks are not coordinated.");
            }
            else
            {
                if (!director.HasPlayer) problem("The enemy director has no player assigned.");
                if (director.Difficulty == null) problem("The enemy director has no difficulty assigned.");
                if (!director.HasSession) problem("The enemy director has no session, so enemies do not return on restart.");
            }

            NavMeshSurface surface = FindInScene<NavMeshSurface>(scene);
            bool navigation = surface != null && surface.navMeshData != null;
            if (!navigation)
            {
                problem("There is no baked navigation mesh, so enemies cannot move.");
            }

            foreach (EnemyController enemy in enemies)
            {
                string label = $"Enemy '{enemy.name}'";
                if (!enemy.HasDefinition)
                {
                    problem($"{label} has no Enemy Definition.");
                    continue;
                }

                if (enemy.GetComponent<NavMeshAgent>() == null) problem($"{label} has no NavMeshAgent.");
                if (!enemy.HasTelegraph) problem($"{label} has no telegraph, so its attacks give no warning.");
                if (enemy.Health.Team != CombatTeam.Enemy) problem($"{label} is not on the Enemy team.");

                if (!enemy.HasCaster)
                {
                    problem($"{label} has no Ability Caster.");
                }
                else
                {
                    foreach (EnemyAttackProfile attack in enemy.Definition.Tuning.attacks)
                    {
                        if (attack == null)
                        {
                            continue;
                        }

                        AbilityDefinition ability = enemy.Caster.GetAbility(attack.abilitySlot);
                        if (ability == null)
                        {
                            problem($"{label} has no ability in slot {attack.abilitySlot} for its attack '{attack.name}'.");
                        }
                        else if (ability is ProjectileSpellDefinition spell && spell.ProjectilePrefab == null)
                        {
                            problem($"{label}: the ability '{spell.name}' has no projectile prefab.");
                        }
                    }
                }

                if (navigation && !NavMesh.SamplePosition(enemy.transform.position, out NavMeshHit _, 1.5f, NavMesh.AllAreas))
                {
                    problem($"{label} does not stand on the navigation mesh.");
                }
            }
        }
    }
}
