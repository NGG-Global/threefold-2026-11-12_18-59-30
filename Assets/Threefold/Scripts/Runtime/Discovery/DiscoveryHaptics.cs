using UnityEngine;
using UnityEngine.InputSystem;

namespace Threefold.Discovery
{
    /// <summary>
    /// Optional haptic pulses for the answers to what the player tries. Nothing depends on it.
    ///
    /// On a gamepad the pulse follows the answer's strength and length. On a phone or tablet only
    /// the device's standard vibration is available through Unity, which has one fixed strength and
    /// length; it is therefore off unless switched on, and used only for the two large answers
    /// (a change of state, a completion). Neither path has been tried on hardware.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DiscoveryHaptics : MonoBehaviour
    {
        [SerializeField] private DiscoveryDirector director;

        [Tooltip("Scales every pulse. 0 switches haptics off.")]
        [Range(0f, 1f)]
        [SerializeField] private float strength = 0.6f;

        [Tooltip("Also use the device's own vibration on a phone or tablet, for large answers only. It is a fixed, " +
                 "fairly strong buzz, so it is off by default.")]
        [SerializeField] private bool deviceVibration = false;

        private DiscoveryDirector subscribed;
        private float remaining;
        private bool rumbling;

        /// <summary>Connects the component. Called by the setup tooling.</summary>
        public void Bind(DiscoveryDirector discoveryDirector)
        {
            director = discoveryDirector;
        }

        private void Awake()
        {
            if (director == null)
            {
                director = GetComponent<DiscoveryDirector>();
            }
        }

        private void OnEnable()
        {
            subscribed = director;
            if (subscribed != null)
            {
                subscribed.AnswerPresented += HandleAnswer;
            }
        }

        private void OnDisable()
        {
            if (subscribed != null)
            {
                subscribed.AnswerPresented -= HandleAnswer;
                subscribed = null;
            }

            StopRumble();
        }

        private void Update()
        {
            if (!rumbling)
            {
                return;
            }

            // Real time, so a pulse also ends if the game is paused.
            remaining -= Time.unscaledDeltaTime;
            if (remaining <= 0f)
            {
                StopRumble();
            }
        }

        private void HandleAnswer(DiscoveryClues clues, FeedbackResult result, FeedbackCue cue)
        {
            float pulse = cue.Haptic * strength;
            if (pulse <= 0f)
            {
                return;
            }

            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                pad.SetMotorSpeeds(pulse * 0.6f, pulse);
                remaining = Mathf.Min(cue.Seconds, 0.6f);
                rumbling = true;
            }

#if UNITY_ANDROID || UNITY_IOS
            bool large = result.Category == FeedbackCategory.Transition || result.Category == FeedbackCategory.Completed;
            if (deviceVibration && large)
            {
                Handheld.Vibrate();
            }
#endif
        }

        /// <summary>Whether the device's own vibration is used as well. Only has an effect on a phone or tablet.</summary>
        public bool UsesDeviceVibration => deviceVibration;

        private void StopRumble()
        {
            if (!rumbling)
            {
                return;
            }

            rumbling = false;
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                pad.ResetHaptics();
            }
        }
    }
}
