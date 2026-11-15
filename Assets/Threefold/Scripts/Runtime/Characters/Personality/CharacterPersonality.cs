using Threefold.Combat;
using Threefold.Magic;
using Threefold.VisualStyle;
using UnityEngine;

namespace Threefold.Characters
{
    /// <summary>
    /// Gives the character the bearing of its selected discipline and lets its hair and cloak follow through. After
    /// the Animator has posed the skeleton each frame, it adds the small rotations <see cref="PersonalityPoseModel"/>
    /// decides to the spine, chest, neck, head, shoulders, arms and hands, blinks and moves the brows on a model that
    /// has the two face bones, and turns the model's secondary bones (those named "sec_...") as
    /// <see cref="SecondaryMotionModel"/> decides. It only carries those decisions out.
    ///
    /// It reads gameplay (the discipline, movement, combat, the guard, health) and writes nothing gameplay reads:
    /// only bone rotations of the visual model. Legs, hips and the root are never touched. During a strike, a charge,
    /// an aerial ability or a climb the arms get nothing, so authored motion is shown as it is. A model without
    /// secondary bones, or without some of the Humanoid bones, simply gets less.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(130)] // After the animation driver (100), the combat animation director (110) and the body effects (120).
    public sealed class CharacterPersonality : MonoBehaviour
    {
        /// <summary>How much of the secondary motion is computed.</summary>
        public enum SecondaryQuality
        {
            Off,

            /// <summary>Every other frame.</summary>
            Reduced,
            Full,
        }

        private const int PoseBones = 10;
        private const float MovingSpeed = 0.3f;
        private const float StaggerSeconds = 0.35f;
        private const float DefaultBoneLength = 0.2f;
        private const float ClosedEyeScale = 0.1f;

        /// <summary>The bone that carries the drawn eyes; squashed along the head's height to blink. Optional.</summary>
        public const string EyesBone = "face_blink";

        /// <summary>The bone that carries the drawn brows; moved up and down the head's height. Optional.</summary>
        public const string BrowsBone = "face_brows";

        private static readonly HumanBodyBones[] PoseBoneIds =
        {
            HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        };

        [Tooltip("The Animator on the visual model.")]
        [SerializeField] private Animator animator;

        [SerializeField] private PlayerDisciplines disciplines;
        [SerializeField] private ThirdPersonMotor motor;

        [Tooltip("Optional. Found on this object if empty.")]
        [SerializeField] private DisciplineCombat combat;

        [Tooltip("Optional. Found on this object if empty.")]
        [SerializeField] private Health health;

        [Header("Bearing")]
        [Tooltip("Posture, breathing, hands and looks of each discipline, and the reactions.")]
        [SerializeField] private PersonalityTuning tuning = new PersonalityTuning();

        [Tooltip("Switches the bearing off; the animation then plays as authored.")]
        [SerializeField] private bool bearingEnabled = true;

        [Header("Hair and cloak")]
        [SerializeField] private SecondaryMotionTuning secondary = new SecondaryMotionTuning();

        [Tooltip("Follows the graphics preset: every other frame on Performance, every frame otherwise.")]
        [SerializeField] private bool secondaryFollowsGraphicsPreset = true;

        [Tooltip("Used when it does not follow the graphics preset.")]
        [SerializeField] private SecondaryQuality secondaryQuality = SecondaryQuality.Full;

        private readonly Transform[] poseBones = new Transform[PoseBones];
        private readonly Quaternion[] poseBase = new Quaternion[PoseBones];
        private readonly Quaternion[] poseWritten = new Quaternion[PoseBones];

        private readonly Transform[] secondaryBones = new Transform[SecondaryMotionModel.Capacity];
        private readonly Vector3[] secondaryTip = new Vector3[SecondaryMotionModel.Capacity];
        private readonly Quaternion[] secondaryBase = new Quaternion[SecondaryMotionModel.Capacity];
        private readonly Quaternion[] secondaryWritten = new Quaternion[SecondaryMotionModel.Capacity];

        private Transform eyes;
        private Transform brows;
        private Vector3 eyesRestScale;
        private Vector3 browsRestPosition;
        private Vector3 browsUp;   // one metre up the head, in the brow bone's parent space
        private int eyesAxis;

        private PersonalityPoseModel poseModel;
        private SecondaryMotionModel secondaryModel;
        private Animator boundAnimator;
        private Renderer[] visibility;
        private PlayerGuard guard;
        private TraversalController traversal;
        private IAerialMoveSource aerial;
        private Health subscribedHealth;
        private DisciplineCombat subscribedCombat;
        private float staggerLeft;
        private float secondaryPending;
        private bool secondarySkipped;
        private bool wasVisible;

        public Animator Animator => animator;

        public PersonalityTuning Tuning => tuning;

        /// <summary>The rules that decide the bearing, for diagnostics and tests. Null until enabled.</summary>
        public PersonalityPoseModel PoseModel => poseModel;

        /// <summary>How many secondary bones were found on the model.</summary>
        public int SecondaryBoneCount => secondaryModel != null ? secondaryModel.Count : 0;

        public bool BearingEnabled
        {
            get => bearingEnabled;
            set => bearingEnabled = value;
        }

        /// <summary>Sets how much secondary motion is computed, and stops it following the graphics preset.</summary>
        public void SetSecondaryQuality(SecondaryQuality quality)
        {
            secondaryFollowsGraphicsPreset = false;
            secondaryQuality = quality;
        }

        /// <summary>Connects only the references that are empty. Returns true if anything changed. Called by the setup tooling.</summary>
        public bool FillReferences(Animator visualAnimator, PlayerDisciplines playerDisciplines, ThirdPersonMotor characterMotor,
            DisciplineCombat disciplineCombat, Health characterHealth)
        {
            bool changed = (animator == null && visualAnimator != null) || (disciplines == null && playerDisciplines != null) ||
                           (motor == null && characterMotor != null) || (combat == null && disciplineCombat != null) ||
                           (health == null && characterHealth != null);
            if (changed)
            {
                Bind(animator != null ? animator : visualAnimator, disciplines != null ? disciplines : playerDisciplines,
                    motor != null ? motor : characterMotor, combat != null ? combat : disciplineCombat,
                    health != null ? health : characterHealth);
            }

            return changed;
        }

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(Animator visualAnimator, PlayerDisciplines playerDisciplines, ThirdPersonMotor characterMotor,
            DisciplineCombat disciplineCombat, Health characterHealth)
        {
            // Only while playing: in the Editor nothing would ever undo the subscriptions again.
            bool wasEnabled = Application.isPlaying && isActiveAndEnabled;
            if (wasEnabled)
            {
                OnDisable();
            }

            animator = visualAnimator;
            disciplines = playerDisciplines;
            motor = characterMotor;
            combat = disciplineCombat;
            health = characterHealth;
            boundAnimator = null;

            if (wasEnabled)
            {
                OnEnable();
            }
        }

        private void OnEnable()
        {
            // A visual model replaced after this was connected (a change of look) leaves the reference empty
            // until the level's setup runs again; the model is always a child.
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (disciplines == null) disciplines = GetComponent<PlayerDisciplines>();
            if (motor == null) motor = GetComponent<ThirdPersonMotor>();
            if (combat == null) combat = GetComponent<DisciplineCombat>();
            if (health == null) health = GetComponent<Health>();
            guard = GetComponent<PlayerGuard>();
            traversal = GetComponent<TraversalController>();
            aerial = GetComponent<IAerialMoveSource>();

            if (tuning == null) tuning = new PersonalityTuning();
            if (secondary == null) secondary = new SecondaryMotionTuning();
            poseModel = new PersonalityPoseModel(tuning);
            poseModel.Reset(disciplines != null ? disciplines.Current : MagicDiscipline.Arcane);
            secondaryModel = new SecondaryMotionModel(secondary);
            boundAnimator = null;
            staggerLeft = 0f;

            subscribedHealth = health;
            if (subscribedHealth != null)
            {
                subscribedHealth.Damaged += HandleDamaged;
            }

            subscribedCombat = combat;
            if (subscribedCombat != null)
            {
                subscribedCombat.ChargeEnded += HandleChargeEnded;
            }
        }

        private void OnDisable()
        {
            if (subscribedHealth != null)
            {
                subscribedHealth.Damaged -= HandleDamaged;
                subscribedHealth = null;
            }

            if (subscribedCombat != null)
            {
                subscribedCombat.ChargeEnded -= HandleChargeEnded;
                subscribedCombat = null;
            }

            RestoreBones();
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (animator == null || poseModel == null || dt <= 0f)
            {
                return;
            }

            if (boundAnimator != animator)
            {
                FindBones();
            }

            // Nothing to show: no work, and the hair and cloak start at rest when the character is seen again.
            bool visible = AnyRendererVisible();
            if (!visible || !animator.isActiveAndEnabled)
            {
                wasVisible = false;
                return;
            }

            if (!wasVisible)
            {
                secondaryModel.ResetAll();
                secondaryPending = 0f;
                wasVisible = true;
            }

            staggerLeft = Mathf.Max(0f, staggerLeft - dt);
            MagicDiscipline discipline = disciplines != null ? disciplines.Current : MagicDiscipline.Arcane;
            PersonalityPose pose = poseModel.Advance(dt, discipline, CurrentActivity());

            ApplyPose(bearingEnabled ? pose : default);
            ApplyFace(bearingEnabled ? pose : default);
            ApplySecondary(dt);
        }

        /// <summary>True if any part of the model is on screen, or if the model has no renderer to ask.</summary>
        private bool AnyRendererVisible()
        {
            if (visibility == null || visibility.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < visibility.Length; i++)
            {
                if (visibility[i] != null && visibility[i].isVisible)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------- gameplay state (read only)

        private PersonalityActivity CurrentActivity()
        {
            if (health != null && !health.IsAlive)
            {
                return PersonalityActivity.Defeated;
            }

            if (staggerLeft > 0f)
            {
                return PersonalityActivity.Staggered;
            }

            if (guard != null && guard.IsBlocking)
            {
                return PersonalityActivity.Blocking;
            }

            bool acting = (combat != null && combat.isActiveAndEnabled && (combat.IsCharging || combat.Model.Combo.IsSwinging)) ||
                          (traversal != null && (traversal.IsHanging || traversal.IsMoving)) ||
                          (aerial != null && aerial.AerialMove != 0);
            if (acting)
            {
                return PersonalityActivity.Action;
            }

            if (motor != null && !motor.IsGrounded)
            {
                return PersonalityActivity.Airborne;
            }

            return motor != null && motor.HorizontalSpeed > MovingSpeed ? PersonalityActivity.Moving : PersonalityActivity.Idle;
        }

        private void HandleDamaged(DamageInfo damage)
        {
            if (poseModel == null || damage.Amount <= 0f)
            {
                return;
            }

            float full = tuning != null && tuning.reactions != null ? Mathf.Max(1f, tuning.reactions.fullImpactDamage) : 25f;
            poseModel.NotifyImpact(damage.Amount / full, transform.InverseTransformDirection(damage.Direction));
            staggerLeft = StaggerSeconds;
        }

        private void HandleChargeEnded(bool released)
        {
            if (released && poseModel != null)
            {
                poseModel.NotifyPowerfulCast(1f);
            }
        }

        // ------------------------------------------------------------- bones

        private void FindBones()
        {
            RestoreBones();
            boundAnimator = animator;
            visibility = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            wasVisible = false;

            for (int i = 0; i < PoseBones; i++)
            {
                poseBones[i] = animator.isHuman ? animator.GetBoneTransform(PoseBoneIds[i]) : null;
                if (poseBones[i] != null)
                {
                    poseBase[i] = poseWritten[i] = poseBones[i].localRotation;
                }
            }

            Transform[] all = animator.GetComponentsInChildren<Transform>(true);
            FindFace(all);

            // Secondary bones in hierarchy order, so a parent always comes before its children.
            secondaryModel.Clear();
            for (int i = 0; i < all.Length && secondaryModel.Count < SecondaryMotionModel.Capacity; i++)
            {
                Transform bone = all[i];
                if (!SecondaryMotionModel.IsSecondary(bone.name))
                {
                    continue;
                }

                // Which way the bone runs is measured, never assumed from an axis convention. It points at its
                // first secondary child. The last bone of a chain continues the line from its parent bone. A single
                // bone runs along whichever of its own axes points most nearly down the character now (the model's
                // secondary bones all hang straight down at rest).
                Vector3 tip = Vector3.zero;
                for (int c = 0; c < bone.childCount; c++)
                {
                    Transform child = bone.GetChild(c);
                    if (SecondaryMotionModel.IsSecondary(child.name) && child.localPosition.sqrMagnitude > 1e-8f)
                    {
                        tip = child.localPosition;
                        break;
                    }
                }

                if (tip == Vector3.zero)
                {
                    float own = bone.localPosition.magnitude;
                    bool continues = bone.parent != null && SecondaryMotionModel.IsSecondary(bone.parent.name) && own > 1e-4f;
                    tip = continues
                        ? Quaternion.Inverse(bone.localRotation) * bone.localPosition
                        : NearestAxis(Quaternion.Inverse(bone.rotation) * -transform.up) * DefaultBoneLength;
                }

                float length = Vector3.Scale(tip, bone.lossyScale).magnitude;
                int index = secondaryModel.Add(SecondaryMotionModel.KindOf(bone.name), length);
                if (index < 0)
                {
                    break;
                }

                secondaryBones[index] = bone;
                secondaryTip[index] = tip.normalized;
                secondaryBase[index] = secondaryWritten[index] = bone.localRotation;
            }
        }

        /// <summary>The bone's own axis (plus or minus x, y or z) nearest to a direction given in its space.</summary>
        private static Vector3 NearestAxis(Vector3 local)
        {
            float x = Mathf.Abs(local.x), y = Mathf.Abs(local.y), z = Mathf.Abs(local.z);
            if (x >= y && x >= z)
            {
                return new Vector3(Mathf.Sign(local.x), 0f, 0f);
            }

            return y >= z ? new Vector3(0f, Mathf.Sign(local.y), 0f) : new Vector3(0f, 0f, Mathf.Sign(local.z));
        }

        /// <summary>
        /// The face bones, if the model has them: each moves only a few drawn marks. "Up" is measured from the neck
        /// to the head, not taken from an axis convention.
        /// </summary>
        private void FindFace(Transform[] all)
        {
            eyes = null;
            brows = null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == EyesBone) eyes = all[i];
                else if (all[i].name == BrowsBone) brows = all[i];
            }

            Transform head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            Transform neck = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Neck) : null;
            Vector3 up = head != null && neck != null && (head.position - neck.position).sqrMagnitude > 1e-8f
                ? (head.position - neck.position).normalized
                : transform.up;

            if (eyes != null)
            {
                eyesRestScale = eyes.localScale;
                Vector3 axis = NearestAxis(Quaternion.Inverse(eyes.rotation) * up);
                eyesAxis = axis.x != 0f ? 0 : axis.y != 0f ? 1 : 2;
            }

            if (brows != null)
            {
                browsRestPosition = brows.localPosition;
                Transform parent = brows.parent;
                Vector3 local = parent != null ? parent.InverseTransformVector(up) : up; // metres to the parent's units
                browsUp = local;
            }
        }

        private void ApplyFace(in PersonalityPose pose)
        {
            if (eyes != null)
            {
                Vector3 scale = eyesRestScale;
                scale[eyesAxis] *= Mathf.Lerp(1f, ClosedEyeScale, Mathf.Clamp01(pose.EyeClosed));
                eyes.localScale = scale;
            }

            if (brows != null)
            {
                brows.localPosition = browsRestPosition + browsUp * (pose.BrowMillimetres * 0.001f);
            }
        }

        /// <summary>
        /// The rotation the animation gave a bone this frame. If the bone still holds exactly what this component
        /// wrote, the Animator has not posed it since (it is paused, or the bone has no animation), and the rotation
        /// it had before is used again, so the additions never pile up.
        /// </summary>
        private static Quaternion Animated(Transform bone, ref Quaternion basis, in Quaternion written)
        {
            Quaternion local = bone.localRotation;
            if (local.x == written.x && local.y == written.y && local.z == written.z && local.w == written.w)
            {
                return basis;
            }

            basis = local;
            return local;
        }

        private void ApplyPose(in PersonalityPose pose)
        {
            Quaternion frame = transform.rotation;
            Quaternion inverse = Quaternion.Inverse(frame);
            for (int i = 0; i < PoseBones; i++)
            {
                Transform bone = poseBones[i];
                if (bone == null)
                {
                    continue;
                }

                Quaternion local = Animated(bone, ref poseBase[i], poseWritten[i]);
                bone.localRotation = local;
                Vector3 degrees = Channel(pose, i);
                if (degrees.x != 0f || degrees.y != 0f || degrees.z != 0f)
                {
                    // In the character's axes, whatever the bone's own axes are.
                    bone.rotation = frame * Quaternion.Euler(degrees) * inverse * bone.rotation;
                }

                poseWritten[i] = bone.localRotation;
            }
        }

        private static Vector3 Channel(in PersonalityPose pose, int index)
        {
            switch (index)
            {
                case 0: return pose.Spine;
                case 1: return pose.Chest;
                case 2: return pose.Neck;
                case 3: return pose.Head;
                case 4: return pose.LeftShoulder;
                case 5: return pose.RightShoulder;
                case 6: return pose.LeftArm;
                case 7: return pose.RightArm;
                case 8: return pose.LeftHand;
                default: return pose.RightHand;
            }
        }

        private void ApplySecondary(float dt)
        {
            int count = secondaryModel.Count;
            if (count == 0)
            {
                return;
            }

            SecondaryQuality quality = secondaryQuality;
            if (secondaryFollowsGraphicsPreset)
            {
                VisualStyleController style = VisualStyleController.Active;
                quality = style != null && style.QualityLevelCount > 1 && style.QualityLevel == GraphicsPresets.Performance
                    ? SecondaryQuality.Reduced
                    : SecondaryQuality.Full;
            }

            if (quality == SecondaryQuality.Off)
            {
                for (int i = 0; i < count; i++)
                {
                    Transform bone = secondaryBones[i];
                    if (bone != null)
                    {
                        bone.localRotation = Animated(bone, ref secondaryBase[i], secondaryWritten[i]);
                        secondaryWritten[i] = bone.localRotation;
                    }
                }

                secondaryModel.ResetAll();
                return;
            }

            // Reduced: the springs are stepped every other frame with both frames' time; the bones keep their
            // last offset from the animation in between.
            secondaryPending += dt;
            bool step = quality == SecondaryQuality.Full || secondarySkipped;
            secondarySkipped = !step;
            float stepTime = step ? secondaryPending : 0f;
            if (step)
            {
                secondaryPending = 0f;
            }

            for (int i = 0; i < count; i++)
            {
                Transform bone = secondaryBones[i];
                if (bone == null)
                {
                    continue;
                }

                bone.localRotation = Animated(bone, ref secondaryBase[i], secondaryWritten[i]);
                Quaternion world = bone.rotation;
                Vector3 rest = world * secondaryTip[i];
                Vector3 direction = secondaryModel.Step(i, bone.position, rest, stepTime);
                bone.rotation = Quaternion.FromToRotation(rest, direction) * world;
                secondaryWritten[i] = bone.localRotation;
            }
        }

        /// <summary>Puts every bone back as the animation had it, when the component stops or is bound to another model.</summary>
        private void RestoreBones()
        {
            for (int i = 0; i < PoseBones; i++)
            {
                if (poseBones[i] != null)
                {
                    poseBones[i].localRotation = Animated(poseBones[i], ref poseBase[i], poseWritten[i]);
                    poseBones[i] = null;
                }
            }

            int count = secondaryModel != null ? secondaryModel.Count : 0;
            for (int i = 0; i < count; i++)
            {
                if (secondaryBones[i] != null)
                {
                    secondaryBones[i].localRotation = Animated(secondaryBones[i], ref secondaryBase[i], secondaryWritten[i]);
                    secondaryBones[i] = null;
                }
            }

            if (eyes != null)
            {
                eyes.localScale = eyesRestScale;
                eyes = null;
            }

            if (brows != null)
            {
                brows.localPosition = browsRestPosition;
                brows = null;
            }

            secondaryModel?.Clear();
            boundAnimator = null;
        }
    }
}
