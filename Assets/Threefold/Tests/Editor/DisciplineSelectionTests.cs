using NUnit.Framework;
using Threefold.Magic;
using Threefold.TouchControls;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>The discipline selector on the ATTACK button, and the selected-discipline state.</summary>
    public sealed class DisciplineSelectionTests
    {
        private static readonly Vector2[] Slots = { new Vector2(-100f, 60f), new Vector2(-50f, 80f), new Vector2(0f, 90f) };

        private DisciplineSelectorModel selector;

        [SetUp]
        public void SetUp()
        {
            selector = new DisciplineSelectorModel { IndicatorRadius = 28f, SlotRadius = 24f, OpenTimeout = 3f };
            selector.SetSlots(Slots);
        }

        private void Frame(System.Action input)
        {
            selector.BeginFrame();
            input();
        }

        [Test]
        public void PressingTheIndicatorOpensTheFanAtOnce()
        {
            Frame(() => selector.PressIndicator(0f, Vector2.zero));
            Assert.AreEqual(DisciplineSelectorState.Held, selector.State);
            Assert.IsTrue(selector.OpenedThisFrame);
        }

        [Test]
        public void DragOntoASlotAndReleaseSelectsIt()
        {
            Frame(() => selector.PressIndicator(0f, Vector2.zero));
            Frame(() => selector.MoveIndicator(Slots[1] + new Vector2(5f, -3f)));
            Assert.AreEqual(1, selector.Highlighted);

            Frame(() => selector.ReleaseIndicator(0.4f, Slots[1], false));
            Assert.AreEqual(1, selector.SelectedThisFrame);
            Assert.AreEqual(DisciplineSelectorState.Closed, selector.State);
            Assert.IsFalse(selector.CancelledThisFrame);
        }

        [Test]
        public void ReleasingAwayFromEverySlotCancels()
        {
            Frame(() => selector.PressIndicator(0f, Vector2.zero));
            Frame(() => selector.MoveIndicator(new Vector2(-60f, -80f)));
            Frame(() => selector.ReleaseIndicator(0.3f, new Vector2(-60f, -80f), false));

            Assert.AreEqual(-1, selector.SelectedThisFrame);
            Assert.IsTrue(selector.CancelledThisFrame);
            Assert.AreEqual(DisciplineSelectorState.Closed, selector.State);
        }

        [Test]
        public void TappingTheIndicatorLeavesTheFanOpenForATapOnASlot()
        {
            Frame(() => selector.PressIndicator(0f, Vector2.zero));
            Frame(() => selector.ReleaseIndicator(0.1f, new Vector2(3f, 2f), false));
            Assert.AreEqual(DisciplineSelectorState.Open, selector.State);

            Frame(() => selector.PressFan(Slots[2]));
            Assert.AreEqual(2, selector.Highlighted);
            Frame(() => selector.ReleaseFan(0.5f, Slots[2], false));

            Assert.AreEqual(2, selector.SelectedThisFrame);
            Assert.AreEqual(DisciplineSelectorState.Closed, selector.State);
        }

        [Test]
        public void TappingTheIndicatorAgainClosesWithoutAChoice()
        {
            Frame(() => selector.PressIndicator(0f, Vector2.zero));
            Frame(() => selector.ReleaseIndicator(0.1f, Vector2.zero, false));
            Frame(() => selector.PressIndicator(0.5f, Vector2.zero));
            Assert.IsTrue(selector.CancelledThisFrame);
            Assert.AreEqual(DisciplineSelectorState.Closed, selector.State);

            // The release of that second tap must not reopen it.
            Frame(() => selector.ReleaseIndicator(0.6f, Vector2.zero, false));
            Assert.AreEqual(DisciplineSelectorState.Closed, selector.State);
        }

        [Test]
        public void ATouchElsewhereClosesAnOpenFan()
        {
            Frame(() => selector.PressIndicator(0f, Vector2.zero));
            Frame(() => selector.ReleaseIndicator(0.1f, Vector2.zero, false));
            Frame(() => selector.PressOutside());

            Assert.IsTrue(selector.CancelledThisFrame);
            Assert.AreEqual(DisciplineSelectorState.Closed, selector.State);
        }

        [Test]
        public void ATouchElsewhereDoesNotInterruptADragSelection()
        {
            Frame(() => selector.PressIndicator(0f, Vector2.zero));
            Frame(() => selector.PressOutside());
            Assert.AreEqual(DisciplineSelectorState.Held, selector.State);
        }

        [Test]
        public void ATapOpenedFanClosesByItselfAfterTheTimeout()
        {
            Frame(() => selector.PressIndicator(0f, Vector2.zero));
            Frame(() => selector.ReleaseIndicator(0.1f, Vector2.zero, false));
            Frame(() => selector.Update(2f));
            Assert.AreEqual(DisciplineSelectorState.Open, selector.State);

            Frame(() => selector.Update(3.2f));
            Assert.AreEqual(DisciplineSelectorState.Closed, selector.State);
            Assert.IsTrue(selector.CancelledThisFrame);
        }

        [Test]
        public void ASystemCancelledTouchClosesWithoutAChoice()
        {
            Frame(() => selector.PressIndicator(0f, Vector2.zero));
            Frame(() => selector.MoveIndicator(Slots[0]));
            Frame(() => selector.ReleaseIndicator(0.2f, Slots[0], true));

            Assert.AreEqual(-1, selector.SelectedThisFrame);
            Assert.IsTrue(selector.CancelledThisFrame);
        }

        [Test]
        public void SlidingOffASlotKeepsTheFanOpen()
        {
            Frame(() => selector.PressIndicator(0f, Vector2.zero));
            Frame(() => selector.ReleaseIndicator(0.1f, Vector2.zero, false));
            Frame(() => selector.PressFan(Slots[0]));
            Frame(() => selector.MoveFan(new Vector2(-200f, 200f)));
            Frame(() => selector.ReleaseFan(0.4f, new Vector2(-200f, 200f), false));

            Assert.AreEqual(-1, selector.SelectedThisFrame);
            Assert.AreEqual(DisciplineSelectorState.Open, selector.State);
        }

        [Test]
        public void TheNearestSlotWins()
        {
            Assert.AreEqual(0, selector.SlotAt(Slots[0]));
            Assert.AreEqual(1, selector.SlotAt(Vector2.Lerp(Slots[0], Slots[1], 0.6f)));
            Assert.AreEqual(-1, selector.SlotAt(new Vector2(300f, 300f)));
        }

        // ------------------------------------------------------------- state

        [Test]
        public void TheLoadoutStartsWithTheGivenDiscipline()
        {
            var loadout = new DisciplineLoadout(3, (int)MagicDiscipline.Flame);
            Assert.AreEqual((int)MagicDiscipline.Flame, loadout.Current);
        }

        [Test]
        public void SelectingRaisesChangedOnceAndIgnoresRepeats()
        {
            var loadout = new DisciplineLoadout(3);
            int changes = 0;
            loadout.Changed += (previous, current) => changes++;

            Assert.IsTrue(loadout.Select(2));
            Assert.IsFalse(loadout.Select(2));
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void UnknownSlotsAreIgnored()
        {
            var loadout = new DisciplineLoadout(3);
            Assert.IsFalse(loadout.Select(3));
            Assert.IsFalse(loadout.Select(-1));
            Assert.AreEqual(0, loadout.Current);
        }

        [Test]
        public void CyclingWrapsAroundBothWays()
        {
            var loadout = new DisciplineLoadout(3);
            loadout.Cycle(-1);
            Assert.AreEqual(2, loadout.Current);
            loadout.Cycle(1);
            Assert.AreEqual(0, loadout.Current);
        }

        [Test]
        public void TheCatalogueHasArcaneFlameAndAegisInSlotOrder()
        {
            Assert.AreEqual(3, DisciplineCatalog.Count);
            Assert.AreEqual("Arcane", DisciplineCatalog.Get(MagicDiscipline.Arcane).Name);
            Assert.AreEqual("Flame", DisciplineCatalog.Get(MagicDiscipline.Flame).Name);
            Assert.AreEqual("Aegis", DisciplineCatalog.Get(MagicDiscipline.Aegis).Name);
        }

        [Test]
        public void TheDisciplinesHaveDistinctIconsAndColours()
        {
            for (int a = 0; a < DisciplineCatalog.Count; a++)
            {
                for (int b = a + 1; b < DisciplineCatalog.Count; b++)
                {
                    Assert.AreNotEqual(DisciplineCatalog.Get(a).Icon, DisciplineCatalog.Get(b).Icon);
                    Assert.AreNotEqual(DisciplineCatalog.Get(a).Color, DisciplineCatalog.Get(b).Color);
                }
            }
        }
    }
}
