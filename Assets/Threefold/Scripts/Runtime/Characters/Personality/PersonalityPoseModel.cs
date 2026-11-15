using Threefold.Magic;
using UnityEngine;

namespace Threefold.Characters
{
    /// <summary>What the character is doing, as far as its bearing is concerned. Chosen by the component from gameplay state.</summary>
    public enum PersonalityActivity
    {
        /// <summary>Standing, in the idle or a stance.</summary>
        Idle,
        Moving,
        Airborne,

        /// <summary>A strike, a charge, an aerial ability, climbing: the authored motion owns the arms.</summary>
        Action,
        Blocking,
        Staggered,
        Defeated,
    }

    /// <summary>
    /// Small rotations to add to the animated pose, in degrees, in the character's own axes whatever the rig's bone
    /// axes are: x is pitch (positive forwards, chin down), y is yaw (positive to the character's right), z is roll
    /// (positive lifts the character's right side). Feet, legs, hips and the root are never part of it.
    /// </summary>
    public struct PersonalityPose
    {
        public Vector3 Spine;
        public Vector3 Chest;
        public Vector3 Neck;
        public Vector3 Head;
        public Vector3 LeftShoulder;
        public Vector3 RightShoulder;
        public Vector3 LeftArm;
        public Vector3 RightArm;
        public Vector3 LeftHand;
        public Vector3 RightHand;

        /// <summary>How far the eyes are closed, 0 (open) to 1 (closed). Only a model with an eye bone shows it.</summary>
        public float EyeClosed;

        /// <summary>Brows up (positive) or down, in millimetres. Only a model with a brow bone shows it.</summary>
        public float BrowMillimetres;

        /// <summary>The largest angle of any channel, for checks.</summary>
        public float Largest
        {
            get
            {
                float largest = 0f;
                Largest3(Spine, ref largest);
                Largest3(Chest, ref largest);
                Largest3(Neck, ref largest);
                Largest3(Head, ref largest);
                Largest3(LeftShoulder, ref largest);
                Largest3(RightShoulder, ref largest);
                Largest3(LeftArm, ref largest);
                Largest3(RightArm, ref largest);
                Largest3(LeftHand, ref largest);
                Largest3(RightHand, ref largest);
                return largest;
            }
        }

        /// <summary>The largest difference to another pose on any channel, for checks.</summary>
        public float LargestDifference(in PersonalityPose other)
        {
            float largest = 0f;
            Largest3(Spine - other.Spine, ref largest);
            Largest3(Chest - other.Chest, ref largest);
            Largest3(Neck - other.Neck, ref largest);
            Largest3(Head - other.Head, ref largest);
            Largest3(LeftShoulder - other.LeftShoulder, ref largest);
            Largest3(RightShoulder - other.RightShoulder, ref largest);
            Largest3(LeftArm - other.LeftArm, ref largest);
            Largest3(RightArm - other.RightArm, ref largest);
            Largest3(LeftHand - other.LeftHand, ref largest);
            Largest3(RightHand - other.RightHand, ref largest);
            return largest;
        }

        private static void Largest3(Vector3 value, ref float largest)
        {
            largest = Mathf.Max(largest, Mathf.Max(Mathf.Abs(value.x), Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z))));
        }
    }

    /// <summary>
    /// Decides the character's bearing: the posture, breathing, hand and shoulder movement, weight shift and looks of
    /// the selected discipline, blended smoothly when the discipline changes, scaled by what the character is doing,
    /// plus two restrained reactions (a hit taken, a powerful cast). Plain C#: it touches no Unity object, reads no
    /// clock and allocates nothing after construction, so it runs outside Unity (PersonalityTests).
    ///
    /// It only presents. Nothing it returns is read by gameplay, and during an action the arms get nothing from it,
    /// so the authored strike and the pose its contact was measured on are shown as they are.
    /// </summary>
    public sealed class PersonalityPoseModel
    {
        // Hard limits of every channel, in degrees per axis, whatever the data says.
        public const float MaxSpineDegrees = 8f;
        public const float MaxChestDegrees = 10f;
        public const float MaxNeckDegrees = 10f;
        public const float MaxHeadDegrees = 16f;
        public const float MaxShoulderDegrees = 4f;
        public const float MaxArmDegrees = 3f;
        public const float MaxHandDegrees = 2f;
        public const float MaxBrowMillimetres = 4f;

        /// <summary>The arms are free of this layer this long after an action begins: shorter than any strike's start-up.</summary>
        public const float ArmsReleaseSeconds = 0.06f;

        private const int DisciplineCount = 3;
        private const float MaxStep = 0.1f;
        private const float SpringStep = 1f / 120f;
        private const float TwoPi = Mathf.PI * 2f;

        /// <summary>How much of each part of the bearing shows during an activity.</summary>
        private readonly struct Shares
        {
            public readonly float Posture;
            public readonly float Breath;
            public readonly float Arms;
            public readonly float Look;
            public readonly float Sway;
            public readonly float Reactions;

            public Shares(float posture, float breath, float arms, float look, float sway, float reactions)
            {
                Posture = posture;
                Breath = breath;
                Arms = arms;
                Look = look;
                Sway = sway;
                Reactions = reactions;
            }
        }

        private static readonly Shares[] ActivityShares =
        {
            new Shares(1f, 1f, 1f, 1f, 1f, 1f),          // Idle
            new Shares(0.5f, 1f, 0.25f, 0.35f, 0f, 1f),  // Moving
            new Shares(0.25f, 0.5f, 0f, 0f, 0f, 0.6f),   // Airborne
            new Shares(0.35f, 0.5f, 0f, 0f, 0f, 0.6f),   // Action
            new Shares(0.5f, 0.7f, 0f, 0f, 0.3f, 0.5f),  // Blocking: the block has its own impact animation
            new Shares(0.2f, 0.5f, 0f, 0f, 0f, 1f),      // Staggered
            new Shares(0f, 0f, 0f, 0f, 0f, 0f),          // Defeated
        };

        private enum LookPhase
        {
            Waiting,
            Turning,
            Returning,
        }

        private readonly PersonalityTuning tuning;
        private readonly float[] weights = new float[DisciplineCount];

        private float postureShare = 1f;
        private float breathShare = 1f;
        private float armsShare = 1f;
        private float lookShare = 1f;
        private float swayShare = 1f;
        private float reactionShare = 1f;

        private float breathPhase;
        private float circlePhase;
        private float restlessPhase;
        private float swayPhase;

        private LookPhase lookPhase;
        private float lookTimer;
        private float lookYaw;
        private float lookPitch;
        private float lookYawVelocity;
        private float lookPitchVelocity;
        private float lookTargetYaw;
        private float lookTargetPitch;
        private uint random;
        private uint faceRandom; // its own sequence, so a blink forced by a hit never changes when the head looks

        private float impactPitch;
        private float impactRoll;
        private float impactPitchVelocity;
        private float impactRollVelocity;
        private float brace;
        private float braceVelocity;

        private float blinkTimer;
        private float blinkProgress = -1f; // below 0: not blinking; else 0 to 1 through the blink
        private float blinkLength = 0.15f;
        private float surprise;
        private float focusShare;
        private float faceShare = 1f;

        private PersonalityPose pose;

        public PersonalityPoseModel(PersonalityTuning tuning, uint seed = 0x2F6E2B1u)
        {
            this.tuning = tuning ?? PersonalityDefaults.Create();
            random = seed == 0u ? 1u : seed;
            faceRandom = random ^ 0x9E3779B9u;
            if (faceRandom == 0u) faceRandom = 1u;
            Reset(MagicDiscipline.Arcane);
        }

        /// <summary>The last pose returned by <see cref="Advance"/>.</summary>
        public PersonalityPose Pose => pose;

        /// <summary>How much of a discipline's bearing shows now, 0 to 1. The three always add up to 1.</summary>
        public float Weight(MagicDiscipline discipline)
        {
            int slot = (int)discipline;
            return slot >= 0 && slot < DisciplineCount ? weights[slot] : 0f;
        }

        /// <summary>How much of the hand and shoulder movement shows now, 0 to 1. 0 during an action.</summary>
        public float ArmsShare => armsShare;

        /// <summary>Starts again in a discipline's bearing, at rest, with no reaction in progress.</summary>
        public void Reset(MagicDiscipline discipline)
        {
            int slot = Slot(discipline);
            for (int i = 0; i < DisciplineCount; i++)
            {
                weights[i] = i == slot ? 1f : 0f;
            }

            postureShare = breathShare = armsShare = lookShare = swayShare = reactionShare = 1f;
            breathPhase = circlePhase = restlessPhase = swayPhase = 0f;
            lookPhase = LookPhase.Waiting;
            lookYaw = lookPitch = lookYawVelocity = lookPitchVelocity = lookTargetYaw = lookTargetPitch = 0f;
            PersonalityProfile profile = tuning.Get(slot);
            lookTimer = Mathf.Lerp(profile.lookIntervalMin, Mathf.Max(profile.lookIntervalMin, profile.lookIntervalMax), 0.5f);
            impactPitch = impactRoll = impactPitchVelocity = impactRollVelocity = brace = braceVelocity = 0f;
            blinkTimer = Mathf.Lerp(profile.blinkIntervalMin, Mathf.Max(profile.blinkIntervalMin, profile.blinkIntervalMax), 0.5f);
            blinkProgress = -1f;
            surprise = focusShare = 0f;
            faceShare = 1f;
            pose = default;
        }

        /// <summary>
        /// A hit was taken. Strength 0 to 1; the direction is the way the hit travelled, in the character's own axes
        /// (z forwards, x to its right). The chest and head give way in that direction and swing back.
        /// </summary>
        public void NotifyImpact(float strength, Vector3 localDirection)
        {
            strength = Clamp01(strength);
            if (strength <= 0f)
            {
                return;
            }

            float planar = Mathf.Sqrt(localDirection.x * localDirection.x + localDirection.z * localDirection.z);
            float forward = planar > 1e-4f ? localDirection.z / planar : -1f; // unknown: as if struck from the front
            float right = planar > 1e-4f ? localDirection.x / planar : 0f;

            PersonalityReactionTuning reactions = tuning.reactions;
            float kick = strength * reactions.impactDegrees * TwoPi * reactions.impactFrequency * 1.5f;
            impactPitchVelocity += forward * kick;   // travelling forwards pushes the chest forwards
            impactRollVelocity += -right * kick;     // travelling to the right tips the body to the right

            surprise = Mathf.Max(surprise, strength);
            if (strength >= reactions.blinkOnImpactStrength && blinkProgress < 0f)
            {
                blinkProgress = 0f;
            }
        }

        /// <summary>A powerful (charged) attack left the hands. Strength 0 to 1. The chest braces back and settles.</summary>
        public void NotifyPowerfulCast(float strength)
        {
            strength = Clamp01(strength);
            if (strength <= 0f)
            {
                return;
            }

            PersonalityReactionTuning reactions = tuning.reactions;
            braceVelocity += -strength * reactions.castBraceDegrees * TwoPi * reactions.castFrequency * 1.5f;
        }

        /// <summary>Advances by a frame and returns the pose to add to the animation.</summary>
        public PersonalityPose Advance(float deltaTime, MagicDiscipline discipline, PersonalityActivity activity)
        {
            float dt = float.IsNaN(deltaTime) ? 0f : Mathf.Clamp(deltaTime, 0f, MaxStep);
            int slot = Slot(discipline);

            BlendDisciplines(slot, dt);
            MoveShares(activity, dt);

            // The discipline's numbers as they are blended now.
            float lean = 0f, open = 0f, headPitch = 0f, breathRate = 0f, breathDepth = 0f, circle = 0f, circleRate = 0f;
            float restless = 0f, restlessRate = 0f, shift = 0f, shiftRate = 0f;
            float lookMin = 0f, lookMax = 0f, lookYawRange = 0f, lookPitchRange = 0f, lookTurn = 0f, lookHold = 0f;
            float blinkMin = 0f, blinkMax = 0f, blinkSeconds = 0f, brow = 0f;
            for (int i = 0; i < DisciplineCount; i++)
            {
                float w = weights[i];
                if (w <= 0f)
                {
                    continue;
                }

                PersonalityProfile p = tuning.Get(i);
                lean += w * p.forwardLeanDegrees;
                open += w * p.chestOpenDegrees;
                headPitch += w * p.headPitchDegrees;
                breathRate += w * p.breathsPerMinute;
                breathDepth += w * p.breathDepthDegrees;
                circle += w * p.handCircleDegrees;
                circleRate += w * p.handCirclesPerMinute;
                restless += w * p.restlessDegrees;
                restlessRate += w * p.restlessPerMinute;
                shift += w * p.weightShiftDegrees;
                shiftRate += w * p.weightShiftsPerMinute;
                lookMin += w * p.lookIntervalMin;
                lookMax += w * p.lookIntervalMax;
                lookYawRange += w * p.lookYawDegrees;
                lookPitchRange += w * p.lookPitchDegrees;
                lookTurn += w * p.lookTurnSeconds;
                lookHold += w * p.lookHoldSeconds;
                blinkMin += w * p.blinkIntervalMin;
                blinkMax += w * p.blinkIntervalMax;
                blinkSeconds += w * p.blinkSeconds;
                brow += w * p.browMillimetres;
            }

            // Phases advance at the blended rates, so a change of rate never moves a phase.
            breathPhase = Wrap(breathPhase + dt * breathRate / 60f);
            circlePhase = Wrap(circlePhase + dt * circleRate / 60f);
            restlessPhase = Wrap(restlessPhase + dt * restlessRate / 60f);
            swayPhase = Wrap(swayPhase + dt * shiftRate / 60f);

            AdvanceLook(dt, lookMin, lookMax, lookYawRange, lookPitchRange, lookTurn, lookHold);
            AdvanceReactions(dt);
            AdvanceFace(dt, activity, blinkMin, blinkMax, blinkSeconds);

            float breath = Mathf.Sin(breathPhase * TwoPi) * breathDepth * breathShare;
            float sway = Mathf.Sin(swayPhase * TwoPi) * shift * swayShare;
            float posture = postureShare;
            float react = reactionShare;

            // Two uneven waves for each side, so the shoulders and hands never move together or repeat soon.
            float restlessLeft = (Mathf.Sin(restlessPhase * TwoPi) * 0.65f + Mathf.Sin(restlessPhase * TwoPi * 2.7f + 1.3f) * 0.35f) * restless;
            float restlessRight = (Mathf.Sin(restlessPhase * TwoPi * 1.3f + 2.1f) * 0.65f + Mathf.Sin(restlessPhase * TwoPi * 3.1f + 0.4f) * 0.35f) * restless;

            float circleCos = Mathf.Cos(circlePhase * TwoPi) * circle;
            float circleSin = Mathf.Sin(circlePhase * TwoPi) * circle;

            var result = new PersonalityPose();

            result.Spine = new Vector3(
                posture * lean * 0.5f + react * impactPitch * 0.4f,
                0f,
                sway + react * impactRoll * 0.4f);

            result.Chest = new Vector3(
                posture * (lean * 0.5f - open * 0.5f) - breath + react * (impactPitch * 0.6f + brace),
                0f,
                -sway * 0.5f + react * impactRoll * 0.6f); // the chest stays nearer upright than the hips' sway

            // The head keeps level through the breath and takes half as much again of a hit.
            result.Neck = new Vector3(
                posture * headPitch * 0.35f + lookPitch * lookShare * 0.35f + breath * 0.3f + react * impactPitch * 0.5f,
                lookYaw * lookShare * 0.35f,
                react * impactRoll * 0.3f);

            result.Head = new Vector3(
                posture * headPitch * 0.65f + lookPitch * lookShare * 0.65f + breath * 0.3f + react * (impactPitch - brace * 0.5f),
                lookYaw * lookShare * 0.65f,
                -sway * 0.5f + react * impactRoll * 0.4f);

            // Shoulders: lifted by the breath (left up is negative roll, right up positive), drawn back when the
            // chest is open (left back is negative yaw, right back positive), and restless.
            float lift = Mathf.Max(0f, breath) * 0.33f;
            float back = posture * open * 0.5f;
            result.LeftShoulder = new Vector3(0f, -back, -(lift + restlessLeft * armsShare));
            result.RightShoulder = new Vector3(0f, back, lift + restlessRight * armsShare);

            // Arms: each traces a small circle, the right a quarter turn behind the left and mirrored.
            result.LeftArm = new Vector3(circleCos, circleSin, 0f) * armsShare;
            result.RightArm = new Vector3(circleSin, -circleCos, 0f) * armsShare;

            result.LeftHand = new Vector3(restlessLeft * 0.8f, circleSin * 0.5f, 0f) * armsShare;
            result.RightHand = new Vector3(restlessRight * 0.8f, -circleCos * 0.5f, 0f) * armsShare;

            result.Spine = Limit(result.Spine, MaxSpineDegrees);
            result.Chest = Limit(result.Chest, MaxChestDegrees);
            result.Neck = Limit(result.Neck, MaxNeckDegrees);
            result.Head = Limit(result.Head, MaxHeadDegrees);
            result.LeftShoulder = Limit(result.LeftShoulder, MaxShoulderDegrees);
            result.RightShoulder = Limit(result.RightShoulder, MaxShoulderDegrees);
            result.LeftArm = Limit(result.LeftArm, MaxArmDegrees);
            result.RightArm = Limit(result.RightArm, MaxArmDegrees);
            result.LeftHand = Limit(result.LeftHand, MaxHandDegrees);
            result.RightHand = Limit(result.RightHand, MaxHandDegrees);

            // The face: a blink closes quickly and opens more slowly; the brows rest where the discipline has them,
            // lower in concentration during an action and rise for a moment when a hit lands.
            if (blinkProgress >= 0f)
            {
                float closing = blinkProgress < 0.4f ? blinkProgress / 0.4f : 1f - (blinkProgress - 0.4f) / 0.6f;
                closing = Mathf.Clamp01(closing);
                result.EyeClosed = closing * closing * (3f - 2f * closing) * faceShare;
            }

            PersonalityReactionTuning face = tuning.reactions;
            result.BrowMillimetres = Limit(
                faceShare * (brow * (1f - focusShare) + Mathf.Min(brow, -face.browFocusMillimetres) * focusShare +
                             surprise * face.browRaiseMillimetres),
                MaxBrowMillimetres);

            pose = result;
            return result;
        }

        // ------------------------------------------------------------- parts

        private void BlendDisciplines(int slot, float dt)
        {
            if (weights[slot] >= 1f)
            {
                return;
            }

            float seconds = Mathf.Max(0.05f, tuning.Get(slot).blendSeconds);
            float target = Mathf.Min(1f, weights[slot] + dt / seconds);
            float others = 0f;
            for (int i = 0; i < DisciplineCount; i++)
            {
                if (i != slot)
                {
                    others += weights[i];
                }
            }

            float scale = others > 1e-6f ? (1f - target) / others : 0f;
            for (int i = 0; i < DisciplineCount; i++)
            {
                weights[i] = i == slot ? target : weights[i] * scale;
            }
        }

        private void MoveShares(PersonalityActivity activity, float dt)
        {
            int index = (int)activity;
            if (index < 0 || index >= ActivityShares.Length)
            {
                index = 0;
            }

            Shares target = ActivityShares[index];
            const float settle = 0.3f; // seconds for a share to come or go

            postureShare = Mathf.MoveTowards(postureShare, target.Posture, dt / settle);
            breathShare = Mathf.MoveTowards(breathShare, target.Breath, dt / settle);
            lookShare = Mathf.MoveTowards(lookShare, target.Look, dt / settle);
            swayShare = Mathf.MoveTowards(swayShare, target.Sway, dt / settle);
            reactionShare = Mathf.MoveTowards(reactionShare, target.Reactions, dt / settle);

            // The arms are given up quickly, before a strike's contact, and taken back slowly.
            float armsSeconds = target.Arms < armsShare ? ArmsReleaseSeconds : settle;
            armsShare = Mathf.MoveTowards(armsShare, target.Arms, dt / armsSeconds);
        }

        private void AdvanceLook(float dt, float min, float max, float yawRange, float pitchRange, float turnSeconds, float holdSeconds)
        {
            lookTimer -= dt;
            if (lookTimer <= 0f)
            {
                switch (lookPhase)
                {
                    case LookPhase.Waiting:
                        // Never a look straight ahead: at least a third of the range to one side.
                        float side = NextFloat() < 0.5f ? -1f : 1f;
                        lookTargetYaw = side * yawRange * (0.35f + 0.65f * NextFloat());
                        lookTargetPitch = pitchRange * (NextFloat() * 2f - 1f);
                        lookPhase = LookPhase.Turning;
                        lookTimer = turnSeconds + holdSeconds;
                        break;
                    case LookPhase.Turning:
                        lookTargetYaw = 0f;
                        lookTargetPitch = 0f;
                        lookPhase = LookPhase.Returning;
                        lookTimer = turnSeconds;
                        break;
                    default:
                        lookPhase = LookPhase.Waiting;
                        lookTimer = Mathf.Lerp(min, Mathf.Max(min, max), NextFloat());
                        break;
                }
            }

            lookYaw = Ease(lookYaw, lookTargetYaw, ref lookYawVelocity, turnSeconds, dt);
            lookPitch = Ease(lookPitch, lookTargetPitch, ref lookPitchVelocity, turnSeconds, dt);
        }

        private void AdvanceFace(float dt, PersonalityActivity activity, float min, float max, float seconds)
        {
            bool defeated = activity == PersonalityActivity.Defeated;
            faceShare = Mathf.MoveTowards(faceShare, defeated ? 0f : 1f, dt / 0.3f);
            focusShare = Mathf.MoveTowards(focusShare, activity == PersonalityActivity.Action ? 1f : 0f, dt / 0.15f);
            surprise = Mathf.Max(0f, surprise - dt / 0.35f);

            if (blinkProgress >= 0f)
            {
                if (blinkProgress == 0f)
                {
                    blinkLength = Mathf.Max(0.05f, seconds); // a blink keeps the length it began with
                }

                blinkProgress += dt / blinkLength;
                if (blinkProgress >= 1f)
                {
                    blinkProgress = -1f;
                    blinkTimer = Mathf.Lerp(min, Mathf.Max(min, max), Next(ref faceRandom));
                }

                return;
            }

            blinkTimer -= dt;
            if (blinkTimer <= 0f && !defeated)
            {
                blinkProgress = 0f;
            }
        }

        private void AdvanceReactions(float dt)
        {
            PersonalityReactionTuning reactions = tuning.reactions;
            float remaining = dt;
            while (remaining > 1e-6f)
            {
                float step = Mathf.Min(SpringStep, remaining);
                remaining -= step;
                Spring(ref impactPitch, ref impactPitchVelocity, reactions.impactFrequency, reactions.impactDamping, reactions.impactDegrees, step);
                Spring(ref impactRoll, ref impactRollVelocity, reactions.impactFrequency, reactions.impactDamping, reactions.impactDegrees, step);
                Spring(ref brace, ref braceVelocity, reactions.castFrequency, reactions.castDamping, reactions.castBraceDegrees, step);
            }
        }

        /// <summary>A damped spring towards 0, one small step. The value never leaves ±limit.</summary>
        private static void Spring(ref float value, ref float velocity, float frequency, float damping, float limit, float step)
        {
            float omega = TwoPi * Mathf.Clamp(frequency, 0.5f, 8f);
            float zeta = Mathf.Clamp(damping, 0.1f, 1.5f);
            velocity += (-omega * omega * value - 2f * zeta * omega * velocity) * step;
            value += velocity * step;

            limit = Mathf.Max(0f, limit);
            if (value > limit)
            {
                value = limit;
                velocity = Mathf.Min(0f, velocity);
            }
            else if (value < -limit)
            {
                value = -limit;
                velocity = Mathf.Max(0f, velocity);
            }

            if (float.IsNaN(value) || float.IsNaN(velocity))
            {
                value = 0f;
                velocity = 0f;
            }
        }

        /// <summary>Moves a value towards a target without overshoot, arriving in about the given time. Stable for any step.</summary>
        private static float Ease(float current, float target, ref float velocity, float seconds, float dt)
        {
            if (dt <= 0f)
            {
                return current;
            }

            float omega = 4f / Mathf.Max(0.05f, seconds);
            float x = omega * dt;
            float decay = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            float change = current - target;
            float temp = (velocity + omega * change) * dt;
            velocity = (velocity - omega * temp) * decay;
            return target + (change + temp) * decay;
        }

        // ------------------------------------------------------------- helpers

        private static int Slot(MagicDiscipline discipline)
        {
            int slot = (int)discipline;
            return slot >= 0 && slot < DisciplineCount ? slot : 0;
        }

        private static float Wrap(float phase)
        {
            // Phases with the factors used above (1.3, 2.7, 3.1) repeat exactly every 10 turns.
            return phase >= 10f ? phase - 10f : phase;
        }

        private static float Clamp01(float value)
        {
            return float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
        }

        private static Vector3 Limit(Vector3 value, float limit)
        {
            return new Vector3(Limit(value.x, limit), Limit(value.y, limit), Limit(value.z, limit));
        }

        private static float Limit(float value, float limit)
        {
            return float.IsNaN(value) ? 0f : Mathf.Clamp(value, -limit, limit);
        }

        /// <summary>A number from 0 to 1 from the model's own generator (xorshift), so runs repeat exactly.</summary>
        private float NextFloat()
        {
            return Next(ref random);
        }

        private static float Next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFFu) / (float)0x1000000u;
        }
    }
}
