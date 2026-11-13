using System;
using UnityEngine;

namespace Threefold.TouchControls
{
    public enum DisciplineSelectorState
    {
        /// <summary>Only the small indicator beside the attack button is shown.</summary>
        Closed,

        /// <summary>The finger that opened the selector is still down: dragging highlights a slot, lifting confirms.</summary>
        Held,

        /// <summary>Opened by a tap and waiting for a tap on a slot.</summary>
        Open,
    }

    /// <summary>
    /// Behaviour of the compact discipline selector attached to the attack button, in the
    /// indicator's own coordinates (canvas units, origin at the indicator's centre).
    ///
    /// - Pressing the indicator opens the fan at once. Dragging onto a slot and lifting selects it.
    /// - Lifting on the indicator itself (a tap) leaves the fan open; tapping a slot then selects it.
    /// - Cancelling keeps the current discipline: lifting away from every slot after dragging,
    ///   tapping the indicator again, touching elsewhere, the system cancelling the touch, or waiting.
    ///
    /// Plain C#, so it is tested outside Unity.
    /// </summary>
    public sealed class DisciplineSelectorModel
    {
        private Vector2[] slotOffsets = Array.Empty<Vector2>();
        private float slotRadius = 26f;
        private float indicatorRadius = 28f;
        private float openTimeout = 3f;

        private float openedTime;
        private bool closingPress;
        private bool fanPointerDown;

        public DisciplineSelectorState State { get; private set; } = DisciplineSelectorState.Closed;

        public bool IsOpen => State != DisciplineSelectorState.Closed;

        /// <summary>The slot under the finger, or -1.</summary>
        public int Highlighted { get; private set; } = -1;

        /// <summary>The slot chosen this frame, or -1.</summary>
        public int SelectedThisFrame { get; private set; } = -1;

        public bool OpenedThisFrame { get; private set; }

        /// <summary>True on the frame the selector closed without a choice.</summary>
        public bool CancelledThisFrame { get; private set; }

        public int SlotCount => slotOffsets.Length;

        /// <summary>Touch radius of each slot, including any padding.</summary>
        public float SlotRadius
        {
            get => slotRadius;
            set => slotRadius = Mathf.Max(1f, value);
        }

        /// <summary>Touch radius of the indicator, including any padding.</summary>
        public float IndicatorRadius
        {
            get => indicatorRadius;
            set => indicatorRadius = Mathf.Max(1f, value);
        }

        /// <summary>Seconds a tap-opened selector waits for a choice before closing by itself. 0 waits forever.</summary>
        public float OpenTimeout
        {
            get => openTimeout;
            set => openTimeout = Mathf.Max(0f, value);
        }

        /// <summary>Sets where the slots are, relative to the indicator's centre. Called again whenever the layout may have changed.</summary>
        public void SetSlots(Vector2[] offsets)
        {
            slotOffsets = offsets ?? Array.Empty<Vector2>();
            if (Highlighted >= slotOffsets.Length)
            {
                Highlighted = -1;
            }
        }

        public Vector2 GetSlotOffset(int slot)
        {
            return slot >= 0 && slot < slotOffsets.Length ? slotOffsets[slot] : Vector2.zero;
        }

        /// <summary>The nearest slot within its touch radius, or -1.</summary>
        public int SlotAt(Vector2 position)
        {
            int best = -1;
            float bestDistance = slotRadius * slotRadius;

            for (int i = 0; i < slotOffsets.Length; i++)
            {
                float distance = (position - slotOffsets[i]).sqrMagnitude;
                if (distance <= bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best;
        }

        public bool IsOverIndicator(Vector2 position)
        {
            return position.sqrMagnitude <= indicatorRadius * indicatorRadius;
        }

        /// <summary>Clears the one-frame results. Call once per frame before feeding events.</summary>
        public void BeginFrame()
        {
            SelectedThisFrame = -1;
            OpenedThisFrame = false;
            CancelledThisFrame = false;
        }

        public void PressIndicator(float time, Vector2 position)
        {
            if (State == DisciplineSelectorState.Open)
            {
                // A second tap on the indicator closes the fan; its release must not reopen it.
                closingPress = true;
                Close(true);
                return;
            }

            closingPress = false;
            State = DisciplineSelectorState.Held;
            openedTime = time;
            Highlighted = SlotAt(position);
            OpenedThisFrame = true;
        }

        public void MoveIndicator(Vector2 position)
        {
            if (State == DisciplineSelectorState.Held)
            {
                Highlighted = SlotAt(position);
            }
        }

        public void ReleaseIndicator(float time, Vector2 position, bool cancelled)
        {
            if (closingPress)
            {
                closingPress = false;
                return;
            }

            if (State != DisciplineSelectorState.Held)
            {
                return;
            }

            if (cancelled)
            {
                Close(true);
                return;
            }

            int slot = SlotAt(position);
            if (slot >= 0)
            {
                Select(slot);
            }
            else if (IsOverIndicator(position))
            {
                State = DisciplineSelectorState.Open;
                openedTime = time;
                Highlighted = -1;
            }
            else
            {
                Close(true);
            }
        }

        /// <summary>A finger went down on the open fan.</summary>
        public void PressFan(Vector2 position)
        {
            if (State != DisciplineSelectorState.Open)
            {
                return;
            }

            fanPointerDown = true;
            Highlighted = SlotAt(position);
        }

        public void MoveFan(Vector2 position)
        {
            if (fanPointerDown && State == DisciplineSelectorState.Open)
            {
                Highlighted = SlotAt(position);
            }
        }

        public void ReleaseFan(float time, Vector2 position, bool cancelled)
        {
            if (!fanPointerDown)
            {
                return;
            }

            fanPointerDown = false;

            if (State != DisciplineSelectorState.Open)
            {
                return;
            }

            int slot = cancelled ? -1 : SlotAt(position);
            if (slot >= 0)
            {
                Select(slot);
                return;
            }

            // Slid off every slot: stay open for another try; the timeout still applies.
            Highlighted = -1;
            openedTime = time;
        }

        /// <summary>A touch started somewhere else. An open fan closes without a choice; the touch is not taken.</summary>
        public void PressOutside()
        {
            if (State == DisciplineSelectorState.Open && !fanPointerDown)
            {
                Close(true);
            }
        }

        /// <summary>Closes without a choice, for example when the controls are hidden.</summary>
        public void CancelAll()
        {
            fanPointerDown = false;
            closingPress = false;
            if (State != DisciplineSelectorState.Closed)
            {
                Close(true);
            }
        }

        public void Update(float time)
        {
            if (State == DisciplineSelectorState.Open && !fanPointerDown && openTimeout > 0f &&
                time - openedTime >= openTimeout)
            {
                Close(true);
            }
        }

        private void Select(int slot)
        {
            SelectedThisFrame = slot;
            Close(false);
        }

        private void Close(bool cancelled)
        {
            State = DisciplineSelectorState.Closed;
            Highlighted = -1;
            fanPointerDown = false;
            CancelledThisFrame |= cancelled;
        }
    }
}
