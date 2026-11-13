using Threefold.Interface;
using UnityEngine;
using UnityEngine.UI;

namespace Threefold.Objectives
{
    /// <summary>
    /// Shows the objective: a title, one marker per target that lights up as targets fall, a
    /// completion panel with the time taken and a restart button, and a defeat panel with a
    /// restart button when the player falls. The markers carry the information on their own, so
    /// the display still works if no font is available.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveHud : MonoBehaviour
    {
        [SerializeField] private DefeatTargetsObjective objective;
        [SerializeField] private PrototypeSession session;

        [Header("Progress")]
        [Tooltip("Optional. Shows the objective's title and count.")]
        [SerializeField] private Text titleLabel;

        [Tooltip("One marker per target, in order.")]
        [SerializeField] private Graphic[] markers = new Graphic[0];

        [SerializeField] private Color pendingColor = new Color(1f, 0.96f, 0.86f, 0.3f);
        [SerializeField] private Color defeatedColor = new Color(0.55f, 0.85f, 1f, 1f);

        [Header("Completion")]
        [Tooltip("Shown only while the objective is complete.")]
        [SerializeField] private GameObject completionPanel;

        [Tooltip("Optional. Shows the time taken.")]
        [SerializeField] private Text timeLabel;

        [Tooltip("Optional. Restarts the level when tapped.")]
        [SerializeField] private TapButton restartButton;

        [Tooltip("Optional. A hint for keyboard users. Hidden on phones and tablets.")]
        [SerializeField] private GameObject keyboardHint;

        [Header("Defeat")]
        [Tooltip("Optional. Shown while the player is defeated.")]
        [SerializeField] private GameObject defeatPanel;

        [Tooltip("Optional. Restarts the level when tapped.")]
        [SerializeField] private TapButton defeatRestartButton;

        [Tooltip("Optional. A hint for keyboard users on the defeat panel. Hidden on phones and tablets.")]
        [SerializeField] private GameObject defeatKeyboardHint;

        private const float PunchSeconds = 0.25f;
        private const float PunchScale = 0.6f;

        private int punchIndex = -1;
        private float punchRemaining;

        /// <summary>Connects the display. Called by the setup tooling.</summary>
        public void Bind(DefeatTargetsObjective shownObjective, PrototypeSession owningSession, Text title,
            Graphic[] targetMarkers, GameObject panel, Text time, TapButton restart, GameObject hint)
        {
            objective = shownObjective;
            session = owningSession;
            titleLabel = title;
            markers = targetMarkers ?? new Graphic[0];
            completionPanel = panel;
            timeLabel = time;
            restartButton = restart;
            keyboardHint = hint;
        }

        public bool HasDefeatPanel => defeatPanel != null;

        /// <summary>Connects the defeat panel. Called by the setup tooling.</summary>
        public void BindDefeat(GameObject panel, TapButton restart, GameObject hint)
        {
            defeatKeyboardHint = hint;

            bool listening = isActiveAndEnabled;
            if (listening && defeatRestartButton != null)
            {
                defeatRestartButton.Pressed -= HandleRestartPressed;
            }

            defeatPanel = panel;
            defeatRestartButton = restart;

            if (listening && defeatRestartButton != null)
            {
                defeatRestartButton.Pressed += HandleRestartPressed;
            }
        }

        private void OnEnable()
        {
            if (session != null)
            {
                session.PlayerDefeated += RefreshDefeat;
                session.Restarted += RefreshDefeat;
            }

            if (defeatRestartButton != null)
            {
                defeatRestartButton.Pressed += HandleRestartPressed;
            }

            RefreshDefeat();

            if (objective != null)
            {
                objective.Progressed += HandleProgressed;
                objective.Completed += Refresh;
                objective.ProgressReset += Refresh;
            }

            if (restartButton != null)
            {
                restartButton.Pressed += HandleRestartPressed;
            }

            if (keyboardHint != null)
            {
                keyboardHint.SetActive(!Application.isMobilePlatform);
            }

            if (defeatKeyboardHint != null)
            {
                defeatKeyboardHint.SetActive(!Application.isMobilePlatform);
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (session != null)
            {
                session.PlayerDefeated -= RefreshDefeat;
                session.Restarted -= RefreshDefeat;
            }

            if (defeatRestartButton != null)
            {
                defeatRestartButton.Pressed -= HandleRestartPressed;
            }

            if (objective != null)
            {
                objective.Progressed -= HandleProgressed;
                objective.Completed -= Refresh;
                objective.ProgressReset -= Refresh;
            }

            if (restartButton != null)
            {
                restartButton.Pressed -= HandleRestartPressed;
            }
        }

        private void Update()
        {
            if (punchRemaining <= 0f || punchIndex < 0 || punchIndex >= markers.Length || markers[punchIndex] == null)
            {
                return;
            }

            punchRemaining = Mathf.Max(0f, punchRemaining - Time.unscaledDeltaTime);
            float scale = 1f + PunchScale * (punchRemaining / PunchSeconds);
            markers[punchIndex].rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        private void HandleProgressed(int defeated, int total)
        {
            // Make the marker that has just lit up jump, so the progress is noticed mid-fight.
            if (defeated > 0)
            {
                punchIndex = defeated - 1;
                punchRemaining = PunchSeconds;
            }

            Refresh();
        }

        private void HandleRestartPressed()
        {
            if (session != null)
            {
                session.Restart();
            }
        }

        private void RefreshDefeat()
        {
            bool show = session != null && session.IsPlayerDefeated;
            if (defeatPanel != null && defeatPanel.activeSelf != show)
            {
                defeatPanel.SetActive(show);
            }
        }

        private void Refresh()
        {
            if (objective == null)
            {
                return;
            }

            int defeated = objective.DefeatedCount;
            int total = objective.TotalCount;

            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] != null)
                {
                    markers[i].color = i < defeated ? defeatedColor : pendingColor;
                }
            }

            if (titleLabel != null)
            {
                titleLabel.text = objective.Title + "   " + defeated + " / " + total;
            }

            bool complete = objective.IsComplete;
            if (completionPanel != null && completionPanel.activeSelf != complete)
            {
                completionPanel.SetActive(complete);
            }

            if (complete && timeLabel != null)
            {
                float seconds = objective.ElapsedSeconds;
                int minutes = Mathf.FloorToInt(seconds / 60f);
                timeLabel.text = "Time  " + minutes + ":" + (seconds - minutes * 60f).ToString("00.0");
            }
        }
    }
}
