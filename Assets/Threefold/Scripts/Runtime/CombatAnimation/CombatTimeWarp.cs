using UnityEngine;

namespace Threefold.CombatAnimation
{
    /// <summary>
    /// How a clip is played so that it agrees with the combat clock: where it starts, and how fast it
    /// plays before and after the contact frame. Times in game seconds, positions in normalised clip time.
    /// </summary>
    public readonly struct CombatTimeFit
    {
        /// <summary>Where the clip starts. Later than the slot's entry when the wind-up had to be shortened.</summary>
        public readonly float Entry;

        public readonly float Contact;
        public readonly float RecoveryEnd;

        /// <summary>Playback speed from the start to the contact frame.</summary>
        public readonly float WindupSpeed;

        /// <summary>Playback speed from the contact frame to the end of recovery.</summary>
        public readonly float RecoverySpeed;

        /// <summary>Game seconds from the start until the clip shows its contact frame.</summary>
        public readonly float WindupSeconds;

        /// <summary>Game seconds from the contact frame until the clip reaches its recovery end.</summary>
        public readonly float RecoverySeconds;

        /// <summary>When the clip shows contact, minus when the rules resolve it. Negative: early; positive: late.</summary>
        public readonly float ContactError;

        /// <summary>When the clip ends its recovery, minus when the rules allow the next action.</summary>
        public readonly float RecoveryError;

        private readonly float clipSeconds;

        public CombatTimeFit(float entry, float contact, float recoveryEnd, float windupSpeed, float recoverySpeed,
            float windupSeconds, float recoverySeconds, float contactError, float recoveryError, float clipSeconds)
        {
            Entry = entry;
            Contact = contact;
            RecoveryEnd = recoveryEnd;
            WindupSpeed = windupSpeed;
            RecoverySpeed = recoverySpeed;
            WindupSeconds = windupSeconds;
            RecoverySeconds = recoverySeconds;
            ContactError = contactError;
            RecoveryError = recoveryError;
            this.clipSeconds = clipSeconds;
        }

        /// <summary>True when the wind-up start was moved later to fit.</summary>
        public bool EntryMoved(float slotEntry)
        {
            return Entry > slotEntry + 0.0001f;
        }

        /// <summary>
        /// Game seconds after the start at which the clip reaches a moment before contact. A moment the clip
        /// starts after is reached at once; a moment after contact is reached at contact.
        /// </summary>
        public float SecondsToBeforeContact(float marker)
        {
            if (marker <= Entry || WindupSeconds <= 0f)
            {
                return 0f;
            }

            float clamped = Mathf.Min(marker, Contact);
            return (clamped - Entry) * clipSeconds / WindupSpeed;
        }

        /// <summary>
        /// Game seconds after contact at which the clip reaches a moment after contact. A moment before contact
        /// is reached at once; a moment after the recovery end is reached at the recovery end.
        /// </summary>
        public float SecondsAfterContact(float marker)
        {
            if (marker <= Contact || RecoverySeconds <= 0f)
            {
                return 0f;
            }

            float clamped = Mathf.Min(marker, RecoveryEnd);
            return (clamped - Contact) * clipSeconds / RecoverySpeed;
        }
    }

    /// <summary>
    /// Fits a clip to the combat clock. The combat rules decide when a hit lands (the step's start-up) and
    /// when the next action may begin (its recovery); the clip only shows it. Its wind-up, from entry to
    /// contact, is played at the speed that makes the contact frame appear when the hit lands; its recovery,
    /// from contact to the recovery end, at the speed that ends it when the recovery ends.
    ///
    /// Speeds are kept between a minimum and a maximum so a clip never crawls or blurs. When a wind-up would
    /// need more than the maximum, its start is skipped instead (the entry moves later), so the contact still
    /// lands on time. When it would need less than the minimum, it plays at the minimum and shows contact
    /// early; <see cref="CombatTimeFit.ContactError"/> says by how much. The rules are never changed to suit
    /// a clip. Plain C#, tested outside Unity.
    /// </summary>
    public static class CombatTimeWarp
    {
        private const float Epsilon = 0.0001f;

        public static CombatTimeFit Fit(CombatAnimationSlot slot, float startupSeconds, float recoverySeconds,
            float minSpeed, float maxSpeed)
        {
            float length = slot != null ? slot.Seconds : 1f;
            float contact = slot != null ? Mathf.Clamp01(slot.contact) : 0f;
            float entry = slot != null ? Mathf.Clamp(slot.entry, 0f, contact) : 0f;
            float recoveryEnd = slot != null ? Mathf.Clamp(slot.recoveryEnd, contact, 1f) : contact;
            float min = Mathf.Max(0.01f, minSpeed);
            float max = Mathf.Max(min, maxSpeed);
            startupSeconds = Mathf.Max(0f, startupSeconds);
            recoverySeconds = Mathf.Max(0f, recoverySeconds);

            // Wind-up: entry to contact.
            float windupSpeed = 1f;
            float windupSeconds = 0f;
            float clipWindup = (contact - entry) * length;

            if (startupSeconds <= Epsilon || clipWindup <= Epsilon)
            {
                // Contact is immediate: start at the contact frame.
                entry = contact;
            }
            else
            {
                windupSpeed = clipWindup / startupSeconds;
                if (windupSpeed > max)
                {
                    windupSpeed = max;
                    entry = contact - max * startupSeconds / length;
                    windupSeconds = startupSeconds;
                }
                else if (windupSpeed < min)
                {
                    windupSpeed = min;
                    windupSeconds = clipWindup / min;
                }
                else
                {
                    windupSeconds = startupSeconds;
                }
            }

            // Recovery: contact to the recovery end.
            float recoverySpeed = 1f;
            float shownRecovery = 0f;
            float clipRecovery = (recoveryEnd - contact) * length;

            if (clipRecovery > Epsilon)
            {
                recoverySpeed = recoverySeconds > Epsilon ? Mathf.Clamp(clipRecovery / recoverySeconds, min, max) : max;
                shownRecovery = clipRecovery / recoverySpeed;
            }

            return new CombatTimeFit(entry, contact, recoveryEnd, windupSpeed, recoverySpeed, windupSeconds, shownRecovery,
                windupSeconds - startupSeconds, shownRecovery - recoverySeconds, length);
        }

        /// <summary>
        /// The fit for a moment that happens at once, such as a charged release: the clip starts at its contact
        /// frame (the charge hold was the wind-up) and plays its recovery at normal speed.
        /// </summary>
        public static CombatTimeFit FitImmediate(CombatAnimationSlot slot)
        {
            float length = slot != null ? slot.Seconds : 1f;
            float recoverySeconds = slot != null ? (Mathf.Clamp(slot.recoveryEnd, slot.contact, 1f) - Mathf.Clamp01(slot.contact)) * length : 0f;
            return Fit(slot, 0f, recoverySeconds, 1f, 1f);
        }

        /// <summary>
        /// The fit for a clip played as it is, from its entry, at normal speed: reactions, charge starts and
        /// cancels. Its contact is reached when the clip reaches it.
        /// </summary>
        public static CombatTimeFit FitNatural(CombatAnimationSlot slot)
        {
            float length = slot != null ? slot.Seconds : 1f;
            float contact = slot != null ? Mathf.Clamp01(slot.contact) : 0f;
            float entry = slot != null ? Mathf.Clamp(slot.entry, 0f, contact) : 0f;
            float recoveryEnd = slot != null ? Mathf.Clamp(slot.recoveryEnd, contact, 1f) : contact;
            return Fit(slot, (contact - entry) * length, (recoveryEnd - contact) * length, 1f, 1f);
        }
    }
}
