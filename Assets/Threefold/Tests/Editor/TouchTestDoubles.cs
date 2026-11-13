using Threefold.InputHandling;
using Threefold.TouchControls;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// Pointer targets built on the same plain C# models as the real controls, so finger routing can
    /// be tested without Unity. Screen positions are used directly as canvas units (scale 1).
    /// </summary>
    internal abstract class FakeTarget : IPointerTarget
    {
        public bool Available = true;
        public int Captures;
        public int Releases;
        public bool LastReleaseCancelled;

        public virtual bool AcceptsPointers => Available;
        public bool IsCaptured => PointerId != TouchRegion.NoPointer;
        public int PointerId { get; private set; } = TouchRegion.NoPointer;

        public abstract bool Contains(Vector2 screenPosition);

        public void Capture(int pointerId, Vector2 screenPosition)
        {
            PointerId = pointerId;
            Captures++;
            OnBegan(screenPosition);
        }

        public void Drag(Vector2 screenPosition)
        {
            if (IsCaptured)
            {
                OnMoved(screenPosition);
            }
        }

        public void Release(bool cancelled)
        {
            if (!IsCaptured)
            {
                return;
            }

            PointerId = TouchRegion.NoPointer;
            Releases++;
            LastReleaseCancelled = cancelled;
            OnEnded(cancelled);
        }

        protected virtual void OnBegan(Vector2 position)
        {
        }

        protected virtual void OnMoved(Vector2 position)
        {
        }

        protected virtual void OnEnded(bool cancelled)
        {
        }
    }

    internal sealed class FakeJoystick : FakeTarget
    {
        public readonly FixedJoystickModel Model = new FixedJoystickModel();

        public FakeJoystick(Vector2 centre)
        {
            Model.Centre = centre;
            Model.Radius = 55f;
            Model.ActivationRadius = 105f;
            Model.DeadZone = 0.12f;
            Model.ResponseExponent = 1f;
        }

        public Vector2 Value => Model.Value;

        public override bool Contains(Vector2 screenPosition) => Model.CanActivate(screenPosition);
        protected override void OnBegan(Vector2 position) => Model.Begin(position);
        protected override void OnMoved(Vector2 position) => Model.Move(position);
        protected override void OnEnded(bool cancelled) => Model.End();
    }

    /// <summary>Takes any touch: the camera area covers the whole screen at the lowest priority.</summary>
    internal sealed class FakeLookArea : FakeTarget
    {
        private Vector2 last;
        public Vector2 Total;

        public override bool Contains(Vector2 screenPosition) => true;

        protected override void OnBegan(Vector2 position) => last = position;

        protected override void OnMoved(Vector2 position)
        {
            Total += position - last;
            last = position;
        }
    }

    internal class FakeButton : FakeTarget
    {
        private readonly Vector2 centre;
        private readonly float radius;

        public FakeButton(Vector2 centre, float radius)
        {
            this.centre = centre;
            this.radius = radius;
        }

        public bool Held => IsCaptured;

        public override bool Contains(Vector2 screenPosition) => (screenPosition - centre).sqrMagnitude <= radius * radius;
    }

    /// <summary>The ATTACK button: a round target driving an attack gesture recognizer.</summary>
    internal sealed class FakeAttackButton : FakeButton
    {
        public readonly AttackGestureRecognizer Gesture = new AttackGestureRecognizer(new AttackGestureSettings());
        public float Time;
        public bool PressAllowed = true;
        public System.Func<Vector2, bool> OverCancelZone = position => false;
        private Vector2 last;

        public FakeAttackButton(Vector2 centre, float radius) : base(centre, radius)
        {
        }

        protected override void OnBegan(Vector2 position)
        {
            last = position;
            if (PressAllowed)
            {
                Gesture.Press(Time, position);
            }
        }

        protected override void OnMoved(Vector2 position)
        {
            last = position;
            Gesture.Move(Time, position, OverCancelZone(position));
        }

        protected override void OnEnded(bool cancelled)
        {
            if (cancelled)
            {
                Gesture.Cancel();
            }
            else
            {
                Gesture.Release(Time, OverCancelZone(last));
            }
        }
    }

    /// <summary>The discipline indicator, with its centre at <see cref="Centre"/>.</summary>
    internal sealed class FakeSelector : FakeButton
    {
        public readonly DisciplineSelectorModel Model = new DisciplineSelectorModel();
        public readonly Vector2 Centre;
        public float Time;
        private Vector2 last;

        public FakeSelector(Vector2 centre, float radius, Vector2[] slotOffsets) : base(centre, radius)
        {
            Centre = centre;
            Model.IndicatorRadius = radius;
            Model.SlotRadius = 32f;
            Model.SetSlots(slotOffsets);
        }

        protected override void OnBegan(Vector2 position)
        {
            last = position;
            Model.PressIndicator(Time, position - Centre);
        }

        protected override void OnMoved(Vector2 position)
        {
            last = position;
            Model.MoveIndicator(position - Centre);
        }

        protected override void OnEnded(bool cancelled) => Model.ReleaseIndicator(Time, last - Centre, cancelled);
    }

    /// <summary>The open fan: takes a touch only on a slot, and only while open by a tap.</summary>
    internal sealed class FakeFan : FakeTarget
    {
        private readonly FakeSelector selector;
        private Vector2 last;

        public FakeFan(FakeSelector selector)
        {
            this.selector = selector;
        }

        public override bool AcceptsPointers => Available && selector.Model.State == DisciplineSelectorState.Open;

        public override bool Contains(Vector2 screenPosition) =>
            selector.Model.State == DisciplineSelectorState.Open && selector.Model.SlotAt(screenPosition - selector.Centre) >= 0;

        protected override void OnBegan(Vector2 position)
        {
            last = position;
            selector.Model.PressFan(position - selector.Centre);
        }

        protected override void OnMoved(Vector2 position)
        {
            last = position;
            selector.Model.MoveFan(position - selector.Centre);
        }

        protected override void OnEnded(bool cancelled) => selector.Model.ReleaseFan(selector.Time, last - selector.Centre, cancelled);
    }
}
