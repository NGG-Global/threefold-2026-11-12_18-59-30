using UnityEngine;

namespace Threefold.CombatFeedback
{
    /// <summary>
    /// Attachment points for discipline effects on the Mage. Prefers Humanoid hand bones when an
    /// Animator is present; otherwise uses authored offsets from the character root so charge and
    /// slash FX still sit at roughly hand height on a capsule or mannequin without bones.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DisciplineVfxSockets : MonoBehaviour
    {
        [SerializeField] private Animator animator;

        [Tooltip("Used when no left-hand bone is found.")]
        [SerializeField] private Transform leftHand;

        [Tooltip("Used when no right-hand bone is found.")]
        [SerializeField] private Transform rightHand;

        [Tooltip("Fallback local position of the left hand relative to this object.")]
        [SerializeField] private Vector3 leftHandFallback = new Vector3(-0.28f, 1.2f, 0.35f);

        [Tooltip("Fallback local position of the right hand relative to this object.")]
        [SerializeField] private Vector3 rightHandFallback = new Vector3(0.28f, 1.2f, 0.35f);

        private Transform leftFallback;
        private Transform rightFallback;

        /// <summary>Connects the Animator. Called by the setup tooling.</summary>
        public void Bind(Animator source)
        {
            animator = source;
        }

        public Transform LeftHand => Resolve(HumanBodyBones.LeftHand, ref leftHand, ref leftFallback, leftHandFallback);

        public Transform RightHand => Resolve(HumanBodyBones.RightHand, ref rightHand, ref rightFallback, rightHandFallback);

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

        private Transform Resolve(HumanBodyBones bone, ref Transform authored, ref Transform fallback, Vector3 local)
        {
            if (authored != null)
            {
                return authored;
            }

            if (animator != null && animator.isHuman)
            {
                Transform fromBone = animator.GetBoneTransform(bone);
                if (fromBone != null)
                {
                    authored = fromBone;
                    return authored;
                }
            }

            if (fallback == null)
            {
                var point = new GameObject(bone == HumanBodyBones.LeftHand ? "Vfx_Hand_L" : "Vfx_Hand_R");
                point.transform.SetParent(transform, false);
                point.transform.localPosition = local;
                fallback = point.transform;
            }

            return fallback;
        }
    }
}
