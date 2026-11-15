using Threefold.CombatAnimation;
using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Attachment points for discipline effects on the Mage: hands, forearms, feet and chest. Prefers the
    /// Humanoid bones when an Animator is present, so effects follow the animation; otherwise uses offsets
    /// from the character root, so effects still sit at roughly the right place on a capsule or a model
    /// without bones.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DisciplineVfxSockets : MonoBehaviour
    {
        private const int SocketCount = 7;

        [SerializeField] private Animator animator;

        private bool searchedForAnimator;

        [Tooltip("Used when no left-hand bone is found.")]
        [SerializeField] private Transform leftHand;

        [Tooltip("Used when no right-hand bone is found.")]
        [SerializeField] private Transform rightHand;

        [Tooltip("Fallback local position of the left hand relative to this object.")]
        [SerializeField] private Vector3 leftHandFallback = new Vector3(-0.28f, 1.2f, 0.35f);

        [Tooltip("Fallback local position of the right hand relative to this object.")]
        [SerializeField] private Vector3 rightHandFallback = new Vector3(0.28f, 1.2f, 0.35f);

        [Tooltip("Fallback local positions of the left and right forearm, the feet and the chest, used without bones.")]
        [SerializeField] private Vector3 leftForearmFallback = new Vector3(-0.3f, 1.2f, 0.15f);

        [SerializeField] private Vector3 rightForearmFallback = new Vector3(0.3f, 1.2f, 0.15f);
        [SerializeField] private Vector3 leftFootFallback = new Vector3(-0.12f, 0.08f, 0.05f);
        [SerializeField] private Vector3 rightFootFallback = new Vector3(0.12f, 0.08f, 0.05f);
        [SerializeField] private Vector3 chestFallback = new Vector3(0f, 1.3f, 0.1f);

        private static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightLowerArm, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.Chest,
        };

        private static readonly string[] FallbackNames =
        {
            "Vfx_Hand_L", "Vfx_Hand_R", "Vfx_Forearm_L", "Vfx_Forearm_R", "Vfx_Foot_L", "Vfx_Foot_R", "Vfx_Chest",
        };

        private readonly Transform[] resolved = new Transform[SocketCount];

        /// <summary>Connects the Animator. Called by the setup tooling.</summary>
        public void Bind(Animator source)
        {
            animator = source;

            // Bones found for another Animator no longer apply. Fallback points are found again by name.
            for (int i = 0; i < resolved.Length; i++)
            {
                resolved[i] = null;
            }
        }

        /// <summary>Uses these transforms for the hands instead of the bones. Called by setup tooling or by hand.</summary>
        public void BindHands(Transform left, Transform right)
        {
            leftHand = left;
            rightHand = right;
        }

        public Transform LeftHand => leftHand != null ? leftHand : Get(0);

        public Transform RightHand => rightHand != null ? rightHand : Get(1);

        public Transform LeftForearm => Get(2);

        public Transform RightForearm => Get(3);

        public Transform LeftFoot => Get(4);

        public Transform RightFoot => Get(5);

        public Transform Chest => Get(6);

        /// <summary>A point between the hands, used when a single charge glow is enough.</summary>
        public Vector3 HandsMidpoint
        {
            get
            {
                Transform left = LeftHand;
                Transform right = RightHand;
                return (left.position + right.position) * 0.5f;
            }
        }

        /// <summary>
        /// The places an effect on a limb belongs: one for a hand or a foot, two for both hands, the chest for none.
        /// </summary>
        /// <returns>How many of <paramref name="first"/> and <paramref name="second"/> are set: 1 or 2.</returns>
        public int Resolve(CombatEffector effector, out Transform first, out Transform second)
        {
            second = null;
            switch (effector)
            {
                case CombatEffector.LeftHand:
                    first = LeftHand;
                    return 1;
                case CombatEffector.RightHand:
                    first = RightHand;
                    return 1;
                case CombatEffector.BothHands:
                    first = LeftHand;
                    second = RightHand;
                    return 2;
                case CombatEffector.LeftFoot:
                    first = LeftFoot;
                    return 1;
                case CombatEffector.RightFoot:
                    first = RightFoot;
                    return 1;
                default:
                    first = Chest;
                    return 1;
            }
        }

        private Transform Get(int index)
        {
            if (resolved[index] != null)
            {
                return resolved[index];
            }

            if (index == 0 && leftHand != null)
            {
                return leftHand;
            }

            if (index == 1 && rightHand != null)
            {
                return rightHand;
            }

            // A visual model replaced after this was connected (a change of the Mage's look) leaves the
            // reference empty until the level's setup runs again; the model is a child, so it is found once.
            if (animator == null && !searchedForAnimator)
            {
                searchedForAnimator = true;
                animator = GetComponentInChildren<Animator>(true);
            }

            if (animator != null && animator.isHuman)
            {
                Transform bone = animator.GetBoneTransform(Bones[index]);
                if (bone == null && Bones[index] == HumanBodyBones.Chest)
                {
                    bone = animator.GetBoneTransform(HumanBodyBones.Spine);
                }

                if (bone != null)
                {
                    resolved[index] = bone;
                    return bone;
                }
            }

            Transform existing = transform.Find(FallbackNames[index]);
            if (existing == null)
            {
                var point = new GameObject(FallbackNames[index]);
                point.transform.SetParent(transform, false);
                point.transform.localPosition = FallbackPosition(index);
                existing = point.transform;
            }

            resolved[index] = existing;
            return existing;
        }

        private Vector3 FallbackPosition(int index)
        {
            switch (index)
            {
                case 0:
                    return leftHandFallback;
                case 1:
                    return rightHandFallback;
                case 2:
                    return leftForearmFallback;
                case 3:
                    return rightForearmFallback;
                case 4:
                    return leftFootFallback;
                case 5:
                    return rightFootFallback;
                default:
                    return chestFallback;
            }
        }
    }
}
