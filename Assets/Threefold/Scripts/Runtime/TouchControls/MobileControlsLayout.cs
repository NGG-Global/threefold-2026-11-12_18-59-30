using System.Collections.Generic;
using UnityEngine;

namespace Threefold.TouchControls
{
    public enum ScreenCorner
    {
        BottomLeft,
        BottomRight,
    }

    /// <summary>A round control placed relative to a corner of the safe area. All values in dp.</summary>
    public readonly struct ControlPlacement
    {
        public readonly string Name;
        public readonly ScreenCorner Corner;

        /// <summary>Centre measured from the corner. X is negative from the right-hand corner.</summary>
        public readonly Vector2 Position;

        public readonly float Diameter;

        /// <summary>Extra touchable margin around the drawn circle.</summary>
        public readonly float HitPadding;

        public ControlPlacement(string name, ScreenCorner corner, Vector2 position, float diameter, float hitPadding)
        {
            Name = name;
            Corner = corner;
            Position = position;
            Diameter = diameter;
            HitPadding = hitPadding;
        }

        public float Radius => Diameter * 0.5f;

        /// <summary>Diameter of the area that responds to touch.</summary>
        public float TouchDiameter => Diameter + HitPadding * 2f;

        /// <summary>The anchor point as a fraction of the parent rectangle, for a RectTransform.</summary>
        public Vector2 Anchor => Corner == ScreenCorner.BottomLeft ? Vector2.zero : new Vector2(1f, 0f);

        /// <summary>Centre in a safe area of the given size, measured from its bottom-left corner.</summary>
        public Vector2 Resolve(Vector2 safeAreaSize)
        {
            return Corner == ScreenCorner.BottomLeft ? Position : new Vector2(safeAreaSize.x + Position.x, Position.y);
        }
    }

    /// <summary>
    /// The default arrangement of the mobile controls, in density-independent pixels (dp), and the
    /// rules it must satisfy on every supported screen. The setup command builds the controls from
    /// these numbers; the tests check them against small phones and large tablets.
    ///
    /// Left: the fixed joystick. Right: ATTACK (largest) with the discipline indicator on its rim,
    /// BLOCK above-left, JUMP left, and the contextual interact button further up-left. The
    /// discipline fan opens in an arc above ATTACK; the cancel zone for a charged attack appears
    /// above the press point. Plain C#, so it is tested outside Unity.
    /// </summary>
    public sealed class MobileControlsLayout
    {
        /// <summary>Smallest touch area, in dp. Android's accessibility guidance recommends 48 dp.</summary>
        public const float MinimumTouchTarget = 48f;

        /// <summary>The smallest safe area the layout is designed for: a 16:9 phone at the canvas's minimum height.</summary>
        public static readonly Vector2 MinimumSafeArea = new Vector2(560f, 316f);

        public static readonly MobileControlsLayout Default = new MobileControlsLayout();

        public ControlPlacement Joystick = new ControlPlacement("Joystick", ScreenCorner.BottomLeft, new Vector2(132f, 118f), 150f, 0f);
        public float JoystickKnobDiameter = 62f;
        public float JoystickTravel = 55f;
        public float JoystickActivationRadius = 105f;

        public ControlPlacement Attack = new ControlPlacement("Attack", ScreenCorner.BottomRight, new Vector2(-104f, 100f), 108f, 10f);
        public ControlPlacement Block = new ControlPlacement("Block", ScreenCorner.BottomRight, new Vector2(-222f, 160f), 72f, 10f);
        public ControlPlacement Jump = new ControlPlacement("Jump", ScreenCorner.BottomRight, new Vector2(-236f, 58f), 80f, 10f);
        public ControlPlacement Interact = new ControlPlacement("Interact", ScreenCorner.BottomRight, new Vector2(-300f, 232f), 72f, 10f);

        /// <summary>Direction of the discipline indicator from the attack centre, in degrees (0 is right, 90 is up).</summary>
        public float IndicatorAngle = 40f;
        public float IndicatorDistance = 76f;
        public float IndicatorDiameter = 40f;
        public float IndicatorHitPadding = 8f;

        public float FanRadius = 124f;
        public float FanCentreAngle = 90f;
        public float FanStepAngle = 30f;
        public float SlotDiameter = 52f;
        public float SlotHitPadding = 6f;

        /// <summary>Where the cancel zone appears, relative to the point where the attack was pressed.</summary>
        public Vector2 CancelOffset = new Vector2(0f, 150f);
        public float CancelDiameter = 72f;

        /// <summary>Space kept between the cancel zone and the edge of the safe area.</summary>
        public float CancelEdgeMargin = 8f;

        /// <summary>Finger travel that gives full aim during a charge. Matches the attack gesture default.</summary>
        public float AimRadius = 64f;

        /// <summary>The indicator's centre relative to the attack button's centre.</summary>
        public Vector2 IndicatorOffset => Polar(IndicatorAngle, IndicatorDistance);

        public ControlPlacement Indicator => new ControlPlacement("DisciplineIndicator", Attack.Corner,
            Attack.Position + IndicatorOffset, IndicatorDiameter, IndicatorHitPadding);

        /// <summary>Slot centres relative to the attack centre. Slot 0 is the leftmost.</summary>
        public Vector2[] FanSlotOffsetsFromAttack(int count)
        {
            var offsets = new Vector2[Mathf.Max(0, count)];
            float first = FanCentreAngle + FanStepAngle * (count - 1) * 0.5f;
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = Polar(first - FanStepAngle * i, FanRadius);
            }

            return offsets;
        }

        /// <summary>Slot centres relative to the indicator centre, as the selector model expects them.</summary>
        public Vector2[] FanSlotOffsetsFromIndicator(int count)
        {
            Vector2[] offsets = FanSlotOffsetsFromAttack(count);
            Vector2 indicator = IndicatorOffset;
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] -= indicator;
            }

            return offsets;
        }

        /// <summary>
        /// Centre of the cancel zone for a press at <paramref name="press"/>: the press point plus the
        /// offset, moved inside the bounds if it would stick out. Shared by the runtime and the tests.
        /// </summary>
        public static Vector2 PlaceCancelZone(Vector2 press, Vector2 offset, float radius, float margin, Rect bounds)
        {
            Vector2 centre = press + offset;
            float inset = radius + margin;

            if (bounds.width > inset * 2f)
            {
                centre.x = Mathf.Clamp(centre.x, bounds.xMin + inset, bounds.xMax - inset);
            }

            if (bounds.height > inset * 2f)
            {
                centre.y = Mathf.Clamp(centre.y, bounds.yMin + inset, bounds.yMax - inset);
            }

            return centre;
        }

        /// <summary>
        /// Checks the layout in a safe area of the given size and adds a sentence for every rule it breaks.
        /// </summary>
        public void Validate(Vector2 safeArea, int disciplineCount, List<string> problems)
        {
            var bounds = new Rect(Vector2.zero, safeArea);
            ControlPlacement indicator = Indicator;
            ControlPlacement[] persistent = { Joystick, Attack, indicator, Block, Jump, Interact };

            foreach (ControlPlacement control in persistent)
            {
                RequireInside(control.Name, control.Resolve(safeArea), control.Radius, bounds, problems);

                if (control.TouchDiameter < MinimumTouchTarget)
                {
                    problems.Add($"{control.Name}: touch area {control.TouchDiameter:0} dp is below {MinimumTouchTarget:0} dp.");
                }
            }

            for (int a = 0; a < persistent.Length; a++)
            {
                for (int b = a + 1; b < persistent.Length; b++)
                {
                    RequireApart(persistent[a], persistent[b], safeArea, problems);
                }
            }

            foreach (ControlPlacement button in new[] { indicator, Block, Jump, Interact })
            {
                if (button.Diameter >= Attack.Diameter)
                {
                    problems.Add($"{button.Name} is not smaller than Attack, which must be the most prominent button.");
                }
            }

            float attachedDistance = Attack.Radius + indicator.Radius + 8f;
            if (IndicatorOffset.magnitude > attachedDistance)
            {
                problems.Add("The discipline indicator is not attached to the attack button.");
            }

            // The joystick is taken only within its activation radius; it must not reach any button.
            Vector2 joystickCentre = Joystick.Resolve(safeArea);
            foreach (ControlPlacement button in new[] { Attack, indicator, Block, Jump, Interact })
            {
                float gap = Vector2.Distance(joystickCentre, button.Resolve(safeArea)) - JoystickActivationRadius -
                            button.TouchDiameter * 0.5f;
                if (gap < 0f)
                {
                    problems.Add($"The joystick's activation area overlaps {button.Name} by {-gap:0.#} dp.");
                }
            }

            Vector2 attackCentre = Attack.Resolve(safeArea);
            Vector2[] slots = FanSlotOffsetsFromAttack(disciplineCount);
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = new ControlPlacement($"Slot {i}", Attack.Corner, Attack.Position + slots[i], SlotDiameter,
                    SlotHitPadding);
                RequireInside(slot.Name, slot.Resolve(safeArea), slot.Radius, bounds, problems);
                if (slot.TouchDiameter < MinimumTouchTarget)
                {
                    problems.Add($"{slot.Name}: touch area {slot.TouchDiameter:0} dp is below {MinimumTouchTarget:0} dp.");
                }

                foreach (ControlPlacement control in persistent)
                {
                    RequireApart(slot, control, safeArea, problems);
                }

                for (int j = i + 1; j < slots.Length; j++)
                {
                    if (Vector2.Distance(slots[i], slots[j]) < SlotDiameter)
                    {
                        problems.Add($"Slots {i} and {j} overlap.");
                    }
                }
            }

            // Wherever on the attack button the press lands, a full aim must not reach the cancel zone.
            float cancelRadius = CancelDiameter * 0.5f;
            float reach = Attack.Radius + Attack.HitPadding;
            Vector2[] presses =
            {
                attackCentre, attackCentre + Vector2.up * reach, attackCentre + Vector2.left * reach,
                attackCentre + Vector2.right * reach, attackCentre + Vector2.down * reach,
            };

            foreach (Vector2 press in presses)
            {
                Vector2 cancel = PlaceCancelZone(press, CancelOffset, cancelRadius, CancelEdgeMargin, bounds);
                RequireInside("Cancel zone", cancel, cancelRadius, bounds, problems);
                float gap = Vector2.Distance(press, cancel) - cancelRadius;
                if (gap < AimRadius)
                {
                    problems.Add($"A full aim from {press} reaches the cancel zone (gap {gap:0.#} dp, aim {AimRadius:0} dp).");
                }
            }
        }

        private static void RequireInside(string name, Vector2 centre, float radius, Rect bounds, List<string> problems)
        {
            if (centre.x - radius < bounds.xMin - 0.01f || centre.x + radius > bounds.xMax + 0.01f ||
                centre.y - radius < bounds.yMin - 0.01f || centre.y + radius > bounds.yMax + 0.01f)
            {
                problems.Add($"{name} extends outside the safe area ({bounds.width:0}x{bounds.height:0} dp).");
            }
        }

        private static void RequireApart(ControlPlacement a, ControlPlacement b, Vector2 safeArea, List<string> problems)
        {
            float gap = Vector2.Distance(a.Resolve(safeArea), b.Resolve(safeArea)) - a.Radius - b.Radius;
            if (gap < -0.01f)
            {
                problems.Add($"{a.Name} and {b.Name} overlap by {-gap:0.#} dp ({safeArea.x:0}x{safeArea.y:0} dp).");
            }
        }

        private static Vector2 Polar(float degrees, float distance)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * distance;
        }
    }
}
