using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>
    /// Animates an enemy built from simple shapes, from its behaviour alone: breathing or
    /// hovering at rest, arms raised and body drawn back during a wind-up, a lunge on the strike,
    /// a shudder when staggered, a hop and a marker when it notices the player, and a collapse
    /// when defeated. It only moves transforms, so it costs almost nothing and needs no
    /// animation assets. A rigged model with an Animator could replace it later without any
    /// change to the behaviour code.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyPresentation : MonoBehaviour
    {
        public enum MotionStyle
        {
            /// <summary>Stands on the ground and sinks when defeated.</summary>
            Heavy,

            /// <summary>Hovers and fades away when defeated.</summary>
            Floating,
        }

        [SerializeField] private EnemyController controller;
        [SerializeField] private MotionStyle style = MotionStyle.Heavy;

        [Tooltip("Hidden once the defeat has played; shown again on restore.")]
        [SerializeField] private GameObject visualRoot;

        [Tooltip("Leans, bobs and collapses. Its pivot should be at the feet.")]
        [SerializeField] private Transform body;

        [Tooltip("Optional. Pivots at the shoulders, raised forward during a wind-up.")]
        [SerializeField] private Transform leftArm;

        [SerializeField] private Transform rightArm;

        [Tooltip("Optional. Turns continuously, faster while charging.")]
        [SerializeField] private Transform orbit;

        [Tooltip("Optional. Shown for a moment when the enemy notices the player.")]
        [SerializeField] private GameObject alertCue;

        [Min(0f)] [SerializeField] private float bobHeight = 0.04f;
        [Min(0f)] [SerializeField] private float bobFrequency = 0.8f;
        [SerializeField] private float orbitSpeed = 70f;
        [Range(0f, 180f)] [SerializeField] private float armRaiseDegrees = 125f;
        [Range(0f, 45f)] [SerializeField] private float leanDegrees = 12f;
        [Min(0f)] [SerializeField] private float lungeDistance = 0.3f;
        [Min(0.1f)] [SerializeField] private float defeatSeconds = 1.2f;
        [Min(0f)] [SerializeField] private float alertCueSeconds = 1f;

        private Vector3 bodyRestPosition;
        private Quaternion bodyRestRotation = Quaternion.identity;
        private Vector3 bodyRestScale = Vector3.one;
        private Quaternion leftRest = Quaternion.identity;
        private Quaternion rightRest = Quaternion.identity;
        private float phase;
        private float alertRemaining;
        private bool hidden;

        /// <summary>Connects the parts. Called by the setup tooling.</summary>
        public void Bind(EnemyController owner, MotionStyle motion, GameObject visuals, Transform bodyPart,
            Transform leftArmPivot, Transform rightArmPivot, Transform orbiting, GameObject alertMarker)
        {
            controller = owner;
            style = motion;
            visualRoot = visuals;
            body = bodyPart;
            leftArm = leftArmPivot;
            rightArm = rightArmPivot;
            orbit = orbiting;
            alertCue = alertMarker;
        }

        private void Awake()
        {
            if (body != null)
            {
                bodyRestPosition = body.localPosition;
                bodyRestRotation = body.localRotation;
                bodyRestScale = body.localScale;
            }

            if (leftArm != null)
            {
                leftRest = leftArm.localRotation;
            }

            if (rightArm != null)
            {
                rightRest = rightArm.localRotation;
            }

            phase = Random.Range(0f, 10f);

            if (alertCue != null)
            {
                alertCue.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.StateChanged += HandleStateChanged;
                controller.Restored += HandleRestored;
            }
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.StateChanged -= HandleStateChanged;
                controller.Restored -= HandleRestored;
            }
        }

        private void LateUpdate()
        {
            if (controller == null || body == null || hidden)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            float t = controller.StateTime;
            float lift = 0f;
            float forward = 0f;
            float lean = 0f;
            float roll = 0f;
            float arms = 0f;
            float scale = 1f;
            float spin = orbitSpeed;

            switch (controller.State)
            {
                case EnemyStateId.Dead:
                {
                    float p = Mathf.Clamp01(t / defeatSeconds);
                    if (style == MotionStyle.Heavy)
                    {
                        lift = -1.4f * p * p;
                        lean = 25f * p;
                        arms = -20f * p;
                    }
                    else
                    {
                        scale = 1f - p;
                        lift = -0.8f * p;
                        spin = orbitSpeed * 4f * (1f - p);
                    }

                    if (p >= 1f && visualRoot != null)
                    {
                        visualRoot.SetActive(false);
                        hidden = true;
                        return;
                    }

                    break;
                }

                case EnemyStateId.Attack:
                {
                    EnemyAttackProfile attack = controller.CurrentAttackProfile;
                    if (attack == null)
                    {
                        break;
                    }

                    float windup = Mathf.Max(0.05f, attack.windupSeconds);
                    if (t < windup)
                    {
                        float p = t / windup;
                        float eased = p * p * (3f - 2f * p);
                        arms = armRaiseDegrees * eased;
                        lean = -leanDegrees * eased;
                        spin = orbitSpeed * (1f + 4f * p);
                        if (style == MotionStyle.Floating)
                        {
                            lift = 0.15f * eased;
                        }
                    }
                    else
                    {
                        float since = t - windup;
                        float strike = Mathf.Clamp01(since / 0.1f);
                        float settle = Mathf.Clamp01((since - 0.1f) / Mathf.Max(0.1f, attack.recoverySeconds));
                        arms = Mathf.Lerp(Mathf.Lerp(armRaiseDegrees, -25f, strike), 0f, settle);
                        lean = Mathf.Lerp(Mathf.Lerp(-leanDegrees, leanDegrees * 1.5f, strike), 0f, settle);
                        forward = lungeDistance * strike * (1f - settle);
                    }

                    break;
                }

                case EnemyStateId.Stagger:
                {
                    float fade = 1f - Mathf.Clamp01(t / 0.6f);
                    roll = Mathf.Sin(t * 45f) * 9f * fade;
                    lean = -10f * fade;
                    break;
                }

                case EnemyStateId.Alerted:
                    lift = 0.12f * Mathf.Sin(Mathf.Clamp01(t / 0.25f) * Mathf.PI);
                    break;
            }

            if (controller.State != EnemyStateId.Dead)
            {
                lift += Mathf.Sin((Time.time + phase) * bobFrequency * Mathf.PI * 2f) * bobHeight;
            }

            body.localPosition = bodyRestPosition + new Vector3(0f, lift, forward);
            body.localRotation = bodyRestRotation * Quaternion.Euler(lean, 0f, roll);
            body.localScale = bodyRestScale * Mathf.Max(0.001f, scale);

            if (leftArm != null)
            {
                leftArm.localRotation = leftRest * Quaternion.Euler(-arms, 0f, 0f);
            }

            if (rightArm != null)
            {
                rightArm.localRotation = rightRest * Quaternion.Euler(-arms, 0f, 0f);
            }

            if (orbit != null)
            {
                orbit.Rotate(0f, spin * deltaTime, 0f, Space.Self);
            }

            if (alertRemaining > 0f)
            {
                alertRemaining -= deltaTime;
                if (alertRemaining <= 0f && alertCue != null)
                {
                    alertCue.SetActive(false);
                }
            }
        }

        private void HandleStateChanged(EnemyStateId previous, EnemyStateId next)
        {
            if (next == EnemyStateId.Alerted && alertCue != null)
            {
                alertRemaining = alertCueSeconds;
                alertCue.SetActive(true);
            }
            else if (next == EnemyStateId.Dead && alertCue != null)
            {
                alertRemaining = 0f;
                alertCue.SetActive(false);
            }
        }

        private void HandleRestored(EnemyController owner)
        {
            hidden = false;
            alertRemaining = 0f;

            if (visualRoot != null)
            {
                visualRoot.SetActive(true);
            }

            if (alertCue != null)
            {
                alertCue.SetActive(false);
            }

            if (body != null)
            {
                body.localPosition = bodyRestPosition;
                body.localRotation = bodyRestRotation;
                body.localScale = bodyRestScale;
            }

            if (leftArm != null)
            {
                leftArm.localRotation = leftRest;
            }

            if (rightArm != null)
            {
                rightArm.localRotation = rightRest;
            }
        }
    }
}
