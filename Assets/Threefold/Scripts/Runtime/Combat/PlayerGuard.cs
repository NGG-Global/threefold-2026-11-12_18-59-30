using System;
using Threefold.Characters;
using Threefold.InputHandling;
using UnityEngine;

namespace Threefold.Combat
{
    /// <summary>
    /// The player's block, as press, hold and release. This sprint provides the input semantics and
    /// the hooks only: <see cref="BlockStarted"/>, <see cref="BlockEnded"/> and <see cref="IsBlocking"/>.
    /// It does not yet reduce damage or change movement; that is connected in a later sprint.
    ///
    /// Blocking takes priority over attacking: the input sources cancel an attack press or charge
    /// when block is pressed (see <see cref="CombatInputRules"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerGuard : MonoBehaviour
    {
        [SerializeField] private CharacterInputSource inputSource;

        private TraversalController traversal;
        private float blockStartTime;

        /// <summary>Raised when the player starts blocking.</summary>
        public event Action BlockStarted;

        /// <summary>Raised when the player stops blocking, with how long the block lasted in seconds.</summary>
        public event Action<float> BlockEnded;

        public bool IsBlocking { get; private set; }

        /// <summary>Seconds the current block has lasted, or 0.</summary>
        public float BlockSeconds => IsBlocking ? Time.time - blockStartTime : 0f;

        public CharacterInputSource InputSource
        {
            get => inputSource;
            set => inputSource = value;
        }

        /// <summary>Connects the input. Called by the setup tooling.</summary>
        public void Bind(CharacterInputSource input)
        {
            inputSource = input;
        }

        private void Awake()
        {
            traversal = GetComponent<TraversalController>();
        }

        private void OnDisable()
        {
            EndBlock();
        }

        private void Update()
        {
            // Hands are busy while hanging, climbing or vaulting.
            bool handsFree = traversal == null || !(traversal.IsHanging || traversal.IsMoving);
            bool wantsBlock = inputSource != null && inputSource.BlockHeld && handsFree;

            if (wantsBlock && !IsBlocking)
            {
                IsBlocking = true;
                blockStartTime = Time.time;
                BlockStarted?.Invoke();
            }
            else if (!wantsBlock)
            {
                EndBlock();
            }
        }

        private void EndBlock()
        {
            if (!IsBlocking)
            {
                return;
            }

            float seconds = BlockSeconds;
            IsBlocking = false;
            BlockEnded?.Invoke(seconds);
        }
    }
}
