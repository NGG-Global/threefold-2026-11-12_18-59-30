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

        public override AttackGestureFrame AttackGesture
        {
            get
            {
                AttackGestureFrame merged = default;
                for (int i = 0; i < sources.Count; i++)
                {
                    if (IsUsable(sources[i]))
                    {
                        merged = AttackGestureFrame.Merge(merged, sources[i].AttackGesture);
                    }
                }

                return merged;
            }
        }

        public override bool BlockHeld
        {
            get
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    if (IsUsable(sources[i]) && sources[i].BlockHeld)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public override bool BlockPressedThisFrame
        {
            get
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    if (IsUsable(sources[i]) && sources[i].BlockPressedThisFrame)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public override bool BlockReleasedThisFrame
        {
            get
            {
                // Released only when no other source still holds block.
                bool released = false;
                for (int i = 0; i < sources.Count; i++)
                {
                    if (!IsUsable(sources[i]))
                    {
                        continue;
                    }

                    if (sources[i].BlockHeld)
                    {
                        return false;
                    }

                    released |= sources[i].BlockReleasedThisFrame;
                }

                return released;
            }
        }

        /// <summary>The first source's request wins; two devices choosing in the same frame is not meaningful.</summary>
        public override int DisciplineSlotRequested
        {
            get
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    if (IsUsable(sources[i]) && sources[i].DisciplineSlotRequested >= 0)
                    {
                        return sources[i].DisciplineSlotRequested;
                    }
                }

                return -1;
            }
        }

        public override int DisciplineCycleRequested
        {
            get
            {
                int total = 0;
                for (int i = 0; i < sources.Count; i++)
                {
                    if (IsUsable(sources[i]))
                    {
                        total += sources[i].DisciplineCycleRequested;
                    }
                }

                return System.Math.Sign(total);
            }
        }

        public override bool InteractPressedThisFrame
        {
            get
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    if (IsUsable(sources[i]) && sources[i].InteractPressedThisFrame)
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
