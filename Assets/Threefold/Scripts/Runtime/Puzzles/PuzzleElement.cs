using System;
using Threefold.Persistence;
using UnityEngine;
using UnityEngine.Events;

namespace Threefold.Puzzles
{
    /// <summary>
    /// One part of a puzzle that is either active or not: a pressed plate, a lit crystal, a running
    /// timer, a solved sequence, an open door. Elements are joined by referencing each other (a door
    /// opens while its input is active, a timer starts when its trigger activates), so a puzzle is
    /// built by connecting parts rather than by writing code.
    ///
    /// Every element offers the same feedback hooks: C# events for code and Unity Events for
    /// sounds and effects connected in the Inspector. A locked element keeps its state for good,
    /// which is how a solved puzzle stays solved. With a persistence key, the state is remembered
    /// for the rest of the play session (see <see cref="PuzzleSessionState"/>).
    /// </summary>
    public abstract class PuzzleElement : MonoBehaviour
    {
        [Header("Puzzle element")]
        [Tooltip("Optional. When set, this element's state is remembered for the rest of the play session, " +
                 "also when the scene is left and loaded again. Must be unique in the game.")]
        [SerializeField] private string persistenceKey = string.Empty;

        [Header("Feedback hooks")]
        [SerializeField] private UnityEvent onActivated = new UnityEvent();
        [SerializeField] private UnityEvent onDeactivated = new UnityEvent();

        [Tooltip("An attempt to use the element was refused, for example the wrong spell.")]
        [SerializeField] private UnityEvent onRejected = new UnityEvent();

        private bool active;
        private bool locked;
        private bool listensToWorld;

        /// <summary>Raised when the element becomes active or inactive.</summary>
        public event Action<PuzzleElement> StateChanged;

        /// <summary>Raised when an attempt to use the element is refused.</summary>
        public event Action<PuzzleElement> Rejected;

        public bool IsActive => active;
        public bool IsLocked => locked;

        /// <summary>
        /// Whether the element can be activated at the moment. A crystal that only listens while
        /// the player stands on the lookout is not ready until then. Feedback shows ready elements
        /// as waiting.
        /// </summary>
        public virtual bool IsReady => !locked;

        public string PersistenceKey
        {
            get => persistenceKey;
            set => persistenceKey = value ?? string.Empty;
        }

        /// <summary>
        /// Whether a state that is not locked is worth remembering. True for a lever the player
        /// left switched on; false for a plate, whose state comes from whatever stands on it.
        /// </summary>
        protected virtual bool PersistsUnlockedState => true;

        protected virtual void Awake()
        {
            RestoreFromSession();

            // An element with a key follows the saved world when it is put back as a whole.
            if (!string.IsNullOrEmpty(persistenceKey) && Application.isPlaying)
            {
                WorldState.Reloaded += HandleWorldReloaded;
                listensToWorld = true;
            }
        }

        protected virtual void OnDestroy()
        {
            if (listensToWorld)
            {
                WorldState.Reloaded -= HandleWorldReloaded;
                listensToWorld = false;
            }
        }

        /// <summary>
        /// The world state was put back as a whole (a checkpoint, a new game, a load from disk). The
        /// element takes what is remembered for it; with nothing remembered it goes back to how it
        /// starts. A plain restart changes nothing here.
        /// </summary>
        private void HandleWorldReloaded(WorldReload reload)
        {
            // A restart leaves puzzle elements as they are, as it always has.
            if (reload != WorldReload.StateReplaced)
            {
                return;
            }

            if (PuzzleSessionState.TryGet(persistenceKey, out PuzzleRecord record))
            {
                if (!record.Locked && !PersistsUnlockedState)
                {
                    return;
                }

                if (active == record.Active && locked == record.Locked)
                {
                    return;
                }

                active = record.Active;
                locked = record.Locked;
            }
            else
            {
                if (!active && !locked)
                {
                    return;
                }

                active = false;
                locked = false;
            }

            OnRestored();
            StateChanged?.Invoke(this);
        }

        /// <summary>
        /// Takes over the state remembered for this element's persistence key, without any
        /// feedback. Runs when the element is loaded.
        /// </summary>
        /// <returns>True if a remembered state was applied.</returns>
        public bool RestoreFromSession()
        {
            if (!PuzzleSessionState.TryGet(persistenceKey, out PuzzleRecord record))
            {
                return false;
            }

            if (!record.Locked && !PersistsUnlockedState)
            {
                return false;
            }

            active = record.Active;
            locked = record.Locked;
            OnRestored();
            return true;
        }

        /// <summary>Freezes the current state for good. Further changes and resets are ignored.</summary>
        public void Lock()
        {
            if (locked)
            {
                return;
            }

            locked = true;
            OnLocked();
            Remember();
        }

        public void Unlock()
        {
            if (!locked)
            {
                return;
            }

            locked = false;
            Remember();
        }

        /// <summary>Returns the element to inactive, unless it is locked. Used by timers that run out.</summary>
        public virtual void ResetState()
        {
            SetElementActive(false);
        }

        /// <summary>Changes the state and announces it, unless the element is locked.</summary>
        /// <returns>True if the state changed.</returns>
        protected bool SetElementActive(bool value)
        {
            if (locked || active == value)
            {
                return false;
            }

            active = value;
            Remember();
            OnStateChanged(value);
            StateChanged?.Invoke(this);

            if (value)
            {
                onActivated.Invoke();
            }
            else
            {
                onDeactivated.Invoke();
            }

            return true;
        }

        /// <summary>
        /// Changes the state without announcing it, for showing the state a scene starts in: a gate
        /// that is already open when the level loads must not rumble open again.
        /// </summary>
        protected void SetElementActiveSilently(bool value)
        {
            if (locked || active == value)
            {
                return;
            }

            active = value;
            Remember();
            OnStateChanged(value);
        }

        /// <summary>Announces a refused attempt.</summary>
        protected void RaiseRejected()
        {
            Rejected?.Invoke(this);
            onRejected.Invoke();
        }

        /// <summary>Called after the state changed, before it is announced.</summary>
        protected virtual void OnStateChanged(bool isActive)
        {
        }

        protected virtual void OnLocked()
        {
        }

        /// <summary>Called after a remembered state was applied.</summary>
        protected virtual void OnRestored()
        {
        }

        private void Remember()
        {
            if (string.IsNullOrEmpty(persistenceKey))
            {
                return;
            }

            if (locked || PersistsUnlockedState)
            {
                PuzzleSessionState.Set(persistenceKey, new PuzzleRecord(active, locked));
            }
            else
            {
                PuzzleSessionState.Remove(persistenceKey);
            }
        }
    }
}
