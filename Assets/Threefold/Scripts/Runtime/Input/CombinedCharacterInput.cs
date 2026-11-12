using System.Collections.Generic;
using UnityEngine;

namespace Threefold.InputHandling
{
    /// <summary>
    /// Merges several input sources into one, so the character can be driven by keyboard and
    /// mouse, gamepad and touch at the same time. Movement and look are summed, buttons are
    /// combined with a logical OR. Gameplay code keeps reading a single
    /// <see cref="CharacterInputSource"/> and does not know how many schemes exist.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombinedCharacterInput : CharacterInputSource
    {
        [Tooltip("Input sources to merge. Disabled or missing sources are ignored.")]
        [SerializeField] private List<CharacterInputSource> sources = new List<CharacterInputSource>();

        public IReadOnlyList<CharacterInputSource> Sources => sources;

        public override Vector2 Move
        {
            get
            {
                Vector2 total = Vector2.zero;
                for (int i = 0; i < sources.Count; i++)
                {
                    if (IsUsable(sources[i]))
                    {
                        total += sources[i].Move;
                    }
                }

                return Vector2.ClampMagnitude(total, 1f);
            }
        }

        public override Vector2 LookDelta
        {
            get
            {
                Vector2 total = Vector2.zero;
                for (int i = 0; i < sources.Count; i++)
                {
                    if (IsUsable(sources[i]))
                    {
                        total += sources[i].LookDelta;
                    }
                }

                return total;
            }
        }

        public override bool SprintHeld
        {
            get
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    if (IsUsable(sources[i]) && sources[i].SprintHeld)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public override bool JumpHeld
        {
            get
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    if (IsUsable(sources[i]) && sources[i].JumpHeld)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public override bool JumpPressedThisFrame
        {
            get
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    if (IsUsable(sources[i]) && sources[i].JumpPressedThisFrame)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public override bool AttackHeld
        {
            get
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    if (IsUsable(sources[i]) && sources[i].AttackHeld)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public override bool AttackPressedThisFrame
        {
            get
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    if (IsUsable(sources[i]) && sources[i].AttackPressedThisFrame)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Adds a source unless it is missing, already listed, or this component itself.</summary>
        /// <returns>True if the source was added.</returns>
        public bool AddSource(CharacterInputSource source)
        {
            if (source == null || source == this || sources.Contains(source))
            {
                return false;
            }

            sources.Add(source);
            return true;
        }

        public bool RemoveSource(CharacterInputSource source)
        {
            return sources.Remove(source);
        }

        private bool IsUsable(CharacterInputSource source)
        {
            return source != null && source != this && source.enabled && source.gameObject.activeInHierarchy;
        }
    }
}
