using System;
using System.Collections.Generic;
using UnityEngine;

namespace Threefold.TouchControls
{
    /// <summary>Something on screen that one pointer (finger or mouse) at a time can own.</summary>
    public interface IPointerTarget
    {
        /// <summary>False while hidden or disabled. Such a target takes no new pointer.</summary>
        bool AcceptsPointers { get; }

        /// <summary>True if a pointer starting at this screen position should belong to this target.</summary>
        bool Contains(Vector2 screenPosition);

        bool IsCaptured { get; }

        int PointerId { get; }

        void Capture(int pointerId, Vector2 screenPosition);

        void Drag(Vector2 screenPosition);

        /// <summary>Lets the pointer go. Cancelled means the system aborted it: nothing should fire.</summary>
        void Release(bool cancelled);
    }

    /// <summary>
    /// Decides which control owns which pointer. A pointer belongs to the first target, in priority
    /// order, that contains it when it starts and is free, and it keeps that owner until it ends.
    /// This is what lets the joystick, the camera and the buttons be used at the same time, and it
    /// keeps a drag that starts on a button from turning the camera.
    ///
    /// Plain C#, so the ownership rules are tested outside Unity.
    /// </summary>
    public sealed class PointerRouter
    {
        private readonly List<IPointerTarget> targets = new List<IPointerTarget>(12);
        private readonly List<bool> seen = new List<bool>(12);

        /// <summary>
        /// Raised for every pointer that starts, with the target that took it (null if none). Used to
        /// close a menu when the player touches elsewhere, without taking that touch away.
        /// </summary>
        public event Action<int, Vector2, IPointerTarget> PointerBegan;

        public int TargetCount => targets.Count;

        /// <summary>Replaces the targets. The first has the highest priority. Captured pointers are released.</summary>
        public void SetTargets(IReadOnlyList<IPointerTarget> priorityOrder)
        {
            ReleaseAll(true);
            targets.Clear();
            seen.Clear();

            if (priorityOrder == null)
            {
                return;
            }

            for (int i = 0; i < priorityOrder.Count; i++)
            {
                if (priorityOrder[i] != null && !targets.Contains(priorityOrder[i]))
                {
                    targets.Add(priorityOrder[i]);
                    seen.Add(false);
                }
            }
        }

        /// <summary>Call once per frame before reporting pointers.</summary>
        public void BeginFrame()
        {
            for (int i = 0; i < seen.Count; i++)
            {
                seen[i] = false;
            }
        }

        /// <summary>A pointer started. Returns the target that took it, or null.</summary>
        public IPointerTarget Begin(int pointerId, Vector2 screenPosition)
        {
            // An identifier reused without an end event: the old owner must not stay stuck.
            int stale = IndexOfOwner(pointerId);
            if (stale >= 0)
            {
                targets[stale].Release(true);
            }

            IPointerTarget owner = null;
            for (int i = 0; i < targets.Count; i++)
            {
                IPointerTarget target = targets[i];
                if (!target.IsCaptured && target.AcceptsPointers && target.Contains(screenPosition))
                {
                    target.Capture(pointerId, screenPosition);
                    seen[i] = true;
                    owner = target;
                    break;
                }
            }

            PointerBegan?.Invoke(pointerId, screenPosition, owner);
            return owner;
        }

        /// <summary>A pointer moved, or is still down without moving.</summary>
        public void Move(int pointerId, Vector2 screenPosition)
        {
            int index = IndexOfOwner(pointerId);
            if (index < 0)
            {
                return;
            }

            targets[index].Drag(screenPosition);
            seen[index] = true;
        }

        /// <summary>A pointer lifted (cancelled false) or was aborted by the system (cancelled true).</summary>
        public void End(int pointerId, Vector2 screenPosition, bool cancelled)
        {
            int index = IndexOfOwner(pointerId);
            if (index < 0)
            {
                return;
            }

            targets[index].Drag(screenPosition);
            targets[index].Release(cancelled);
            seen[index] = true;
        }

        /// <summary>
        /// Call once per frame after reporting pointers. A target whose pointer was not reported this
        /// frame, or that stopped accepting pointers, is released as cancelled, so no control stays stuck.
        /// </summary>
        public void EndFrame()
        {
            for (int i = 0; i < targets.Count; i++)
            {
                IPointerTarget target = targets[i];
                if (target.IsCaptured && (!seen[i] || !target.AcceptsPointers))
                {
                    target.Release(true);
                }
            }
        }

        public void ReleaseAll(bool cancelled)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].IsCaptured)
                {
                    targets[i].Release(cancelled);
                }
            }
        }

        /// <summary>The target that owns the pointer, or null.</summary>
        public IPointerTarget OwnerOf(int pointerId)
        {
            int index = IndexOfOwner(pointerId);
            return index >= 0 ? targets[index] : null;
        }

        private int IndexOfOwner(int pointerId)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].IsCaptured && targets[i].PointerId == pointerId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
