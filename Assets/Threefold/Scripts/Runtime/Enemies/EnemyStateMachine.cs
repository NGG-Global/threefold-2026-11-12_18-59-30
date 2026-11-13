using System;

namespace Threefold.Enemies
{
    /// <summary>The behaviours an enemy can be in.</summary>
    public enum EnemyStateId
    {
        /// <summary>Standing at its post, unaware.</summary>
        Idle,

        /// <summary>Walking its route, unaware.</summary>
        Patrol,

        /// <summary>Has just noticed the player and reacts before moving: the player's warning.</summary>
        Alerted,

        /// <summary>Going to look at something suspicious: a glimpse, a noise, then searching around.</summary>
        Investigate,

        /// <summary>Engaged: pursuing and positioning, deciding when to attack.</summary>
        Chase,

        /// <summary>Winding up (telegraph), striking and recovering.</summary>
        Attack,

        /// <summary>Interrupted by damage. Any attack in progress is cancelled.</summary>
        Stagger,

        /// <summary>Walking back to its post after losing the player.</summary>
        Return,

        /// <summary>Defeated.</summary>
        Dead,
    }

    /// <summary>One behaviour of an enemy. Each enemy has its own instances, so a state may keep data.</summary>
    public abstract class EnemyState
    {
        public abstract EnemyStateId Id { get; }

        /// <summary>Called when the state starts. Do not change state from here.</summary>
        public virtual void Enter(EnemyBrain brain)
        {
        }

        /// <summary>Called every update while the state is active. May change state.</summary>
        public virtual void Tick(EnemyBrain brain, float deltaTime)
        {
        }

        /// <summary>Called when the state ends, before the next one starts.</summary>
        public virtual void Exit(EnemyBrain brain)
        {
        }
    }

    /// <summary>
    /// A flat state machine: exactly one state is active; a change exits the old state, then
    /// enters the new one. Transitions are decided inside the states and, for events that can
    /// interrupt anything (damage, defeat), by the brain. Plain C#, no Unity lifecycle.
    /// </summary>
    public sealed class EnemyStateMachine
    {
        public const int StateCount = (int)EnemyStateId.Dead + 1;

        private readonly EnemyState[] states = new EnemyState[StateCount];
        private EnemyState current;

        public EnemyStateId CurrentId { get; private set; }

        /// <summary>Seconds since the current state was entered.</summary>
        public float TimeInState { get; private set; }

        public bool IsStarted => current != null;

        /// <summary>Raised after every change. Arguments: previous state, new state.</summary>
        public event Action<EnemyStateId, EnemyStateId> Changed;

        public void Add(EnemyState state)
        {
            if (state != null)
            {
                states[(int)state.Id] = state;
            }
        }

        public EnemyState Get(EnemyStateId id)
        {
            return states[(int)id];
        }

        /// <summary>Enters a state, leaving the current one first. Entering the current state again restarts it.</summary>
        public void ChangeTo(EnemyStateId id, EnemyBrain owner)
        {
            EnemyState next = states[(int)id];
            if (next == null)
            {
                return;
            }

            EnemyStateId previous = CurrentId;
            current?.Exit(owner);

            current = next;
            CurrentId = id;
            TimeInState = 0f;
            current.Enter(owner);

            Changed?.Invoke(previous, id);
        }

        public void Tick(EnemyBrain owner, float deltaTime)
        {
            if (current == null)
            {
                return;
            }

            TimeInState += deltaTime;
            current.Tick(owner, deltaTime);
        }
    }
}
