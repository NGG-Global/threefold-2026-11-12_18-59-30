using NUnit.Framework;
using Threefold.MainMenu;
using UnityEngine;

namespace Threefold.Tests
{
    /// <summary>
    /// The main menu (Docs/MainMenu.md): its rules (<see cref="MainMenuModel"/>), the measurements of its
    /// interface (<see cref="MainMenuLayout"/>) and the framing of the scene behind it
    /// (<see cref="MainMenuStage"/>). Plain C#, run outside Unity by Tools/ci/compile_check.sh.
    /// </summary>
    public sealed class MainMenuTests
    {
        /// <summary>Phones and tablets in landscape, from 3:2 to 21:9, on the smallest canvas the scaler allows.</summary>
        private static readonly float[] Aspects = { 3f / 2f, 16f / 10f, 16f / 9f, 2f, 19.5f / 9f, 21f / 9f };

        /// <summary>
        /// A 4:3 screen is a tablet. The smallest one considered is 600 canvas units high; this is an
        /// assumption about devices, not a measurement.
        /// </summary>
        private const float TabletAspect = 4f / 3f;
        private const float TabletCanvasHeight = 600f;

        private static MainMenuModel Ready(bool saved, bool settings = true)
        {
            var model = new MainMenuModel(saved, settings);
            model.Tick(false, false);
            return model;
        }

        // ---------------------------------------------------------------- rules

        [Test]
        public void WithoutASavedGameContinueCannotBeChosenAndNewGameIsHighlighted()
        {
            MainMenuModel model = Ready(false);

            Assert.IsFalse(model.IsEnabled(MainMenuEntry.Continue));
            Assert.AreEqual(MainMenuEntry.NewGame, model.Selected);
            Assert.AreEqual(MainMenuCommand.None, model.Activate(MainMenuEntry.Continue));
            Assert.AreEqual(MainMenuState.Main, model.State);
        }

        [Test]
        public void WithASavedGameContinueIsHighlightedAndLoadsTheGame()
        {
            MainMenuModel model = Ready(true);

            Assert.AreEqual(MainMenuEntry.Continue, model.Selected);
            Assert.AreEqual(MainMenuCommand.ContinueGame, model.Activate(MainMenuEntry.Continue));
            Assert.AreEqual(MainMenuState.Leaving, model.State);
        }

        [Test]
        public void NothingIsAcceptedBeforeThePointerHasBeenSeenReleased()
        {
            var model = new MainMenuModel(false, true);
            Assert.AreEqual(MainMenuCommand.None, model.Activate(MainMenuEntry.NewGame), "before the first frame");

            model.Tick(false, true);
            Assert.AreEqual(MainMenuCommand.None, model.Activate(MainMenuEntry.NewGame), "a finger carried over from the last scene");

            model.Tick(false, false);
            Assert.AreEqual(MainMenuCommand.StartNewGame, model.Activate(MainMenuEntry.NewGame));
        }

        [Test]
        public void ANewGameWithoutASavedGameStartsAtOnceAndOnlyOnce()
        {
            MainMenuModel model = Ready(false);

            Assert.AreEqual(MainMenuCommand.StartNewGame, model.Activate(MainMenuEntry.NewGame));
            model.Tick(false, false);
            Assert.AreEqual(MainMenuCommand.None, model.Activate(MainMenuEntry.NewGame), "a second tap while the level loads");
            Assert.AreEqual(MainMenuCommand.None, model.ActivateSelected());
        }

        [Test]
        public void ANewGameOverASavedGameAsksFirst()
        {
            MainMenuModel model = Ready(true);

            Assert.AreEqual(MainMenuCommand.None, model.Activate(MainMenuEntry.NewGame));
            Assert.AreEqual(MainMenuState.ConfirmNewGame, model.State);

            model.Tick(false, false);
            Assert.AreEqual(MainMenuCommand.StartNewGame, model.AnswerConfirm(true));
            Assert.AreEqual(MainMenuState.Leaving, model.State);
        }

        [Test]
        public void TheTouchThatOpenedTheQuestionCannotAnswerIt()
        {
            MainMenuModel model = Ready(true);
            model.Activate(MainMenuEntry.NewGame);

            // The answers appear where the entries were, under the same finger.
            Assert.AreEqual(MainMenuCommand.None, model.AnswerConfirm(true), "in the same frame");
            model.Tick(false, true);
            Assert.AreEqual(MainMenuCommand.None, model.AnswerConfirm(true), "while the finger is still down");
            Assert.AreEqual(MainMenuState.ConfirmNewGame, model.State);

            model.Tick(false, false);
            Assert.AreEqual(MainMenuCommand.StartNewGame, model.AnswerConfirm(true));
        }

        [Test]
        public void KeepingTheSavedGameReturnsToTheEntriesAndTheSameTouchChoosesNothing()
        {
            MainMenuModel model = Ready(true);
            model.Activate(MainMenuEntry.NewGame);
            model.Tick(false, false);

            Assert.AreEqual(MainMenuCommand.None, model.AnswerConfirm(false));
            Assert.AreEqual(MainMenuState.Main, model.State);
            Assert.AreEqual(MainMenuCommand.None, model.Activate(MainMenuEntry.Quit), "the entry under the 'keep' answer");

            model.Tick(false, false);
            Assert.AreEqual(MainMenuCommand.QuitGame, model.Activate(MainMenuEntry.Quit));
        }

        [Test]
        public void TheConfirmKeyKeepsTheSavedGameUnlessTheHighlightWasMoved()
        {
            MainMenuModel model = Ready(true);
            model.MoveSelection(1);
            Assert.AreEqual(MainMenuEntry.NewGame, model.Selected);

            model.ActivateSelected();
            model.Tick(false, false);
            Assert.IsFalse(model.ConfirmSelected, "the safe answer is highlighted first");
            Assert.AreEqual(MainMenuCommand.None, model.ActivateSelected());
            Assert.AreEqual(MainMenuState.Main, model.State);

            model.Tick(false, false);
            model.ActivateSelected();
            model.Tick(false, false);
            model.MoveSelection(-1);
            Assert.IsTrue(model.ConfirmSelected);
            Assert.AreEqual(MainMenuCommand.StartNewGame, model.ActivateSelected());
        }

        [Test]
        public void BackLeavesTheQuestionAndDoesNothingElsewhere()
        {
            MainMenuModel model = Ready(true);
            Assert.IsFalse(model.Back());

            model.Activate(MainMenuEntry.NewGame);
            Assert.IsTrue(model.Back());
            Assert.AreEqual(MainMenuState.Main, model.State);
        }

        [Test]
        public void SettingsReturnToTheEntriesWhenThePanelClosesAndTheClosingTouchChoosesNothing()
        {
            MainMenuModel model = Ready(true);

            Assert.AreEqual(MainMenuCommand.OpenSettings, model.Activate(MainMenuEntry.Settings));
            model.Tick(true, false);
            Assert.AreEqual(MainMenuState.Settings, model.State);
            Assert.AreEqual(MainMenuCommand.None, model.Activate(MainMenuEntry.Continue), "while the panel is open");

            // The panel closed under a finger that is still on the screen, over an entry.
            model.Tick(false, true);
            Assert.AreEqual(MainMenuState.Main, model.State);
            Assert.AreEqual(MainMenuCommand.None, model.Activate(MainMenuEntry.Continue));

            model.Tick(false, false);
            Assert.AreEqual(MainMenuCommand.ContinueGame, model.Activate(MainMenuEntry.Continue));
        }

        [Test]
        public void WithoutSettingsTheEntryCannotBeChosenAndTheHighlightSkipsIt()
        {
            MainMenuModel model = Ready(false, false);

            Assert.AreEqual(MainMenuCommand.None, model.Activate(MainMenuEntry.Settings));
            Assert.AreEqual(MainMenuEntry.NewGame, model.Selected);

            model.MoveSelection(1);
            Assert.AreEqual(MainMenuEntry.Quit, model.Selected, "past Settings");
            model.MoveSelection(1);
            Assert.AreEqual(MainMenuEntry.NewGame, model.Selected, "round to the top, past Continue");
            model.MoveSelection(-1);
            Assert.AreEqual(MainMenuEntry.Quit, model.Selected);
        }

        [Test]
        public void QuitLeavesTheMenuUsableWhereQuittingDoesNothing()
        {
            MainMenuModel model = Ready(false);

            Assert.AreEqual(MainMenuCommand.QuitGame, model.Activate(MainMenuEntry.Quit));
            Assert.AreEqual(MainMenuState.Main, model.State);
            Assert.AreEqual(MainMenuCommand.StartNewGame, model.Activate(MainMenuEntry.NewGame));
        }

        [Test]
        public void ALevelThatCannotBeLoadedGivesTheMenuBack()
        {
            MainMenuModel model = Ready(true);
            model.Activate(MainMenuEntry.Continue);

            model.LoadFailed();
            Assert.AreEqual(MainMenuState.Main, model.State);
            model.Tick(false, false);
            Assert.AreEqual(MainMenuCommand.ContinueGame, model.Activate(MainMenuEntry.Continue));
        }

        [Test]
        public void EveryVisibleChangeRaisesTheRevision()
        {
            MainMenuModel model = Ready(true);
            int revision = model.Revision;

            model.MoveSelection(1);
            Assert.Greater(model.Revision, revision);
            revision = model.Revision;

            model.MoveSelection(0);
            model.Tick(false, false);
            Assert.AreEqual(revision, model.Revision, "nothing changed");

            model.Activate(MainMenuEntry.NewGame);
            Assert.Greater(model.Revision, revision);
        }

        // --------------------------------------------------------------- layout

        [Test]
        public void EveryEntryIsLargeEnoughToTouchAndNoneOverlap()
        {
            Assert.GreaterOrEqual(MainMenuLayout.EntryHeight, MainMenuLayout.SmallestTouchTarget);
            Assert.GreaterOrEqual(MainMenuLayout.ColumnWidth, MainMenuLayout.SmallestTouchTarget);
            Assert.Greater(MainMenuLayout.EntryGap, 0f);

            for (int i = 1; i < MainMenuModel.EntryCount; i++)
            {
                Assert.GreaterOrEqual(MainMenuLayout.EntryTop(i), MainMenuLayout.EntryTop(i - 1) + MainMenuLayout.EntryHeight);
            }
        }

        [Test]
        public void TheColumnFitsTheSmallestCanvasFromTitleToNotice()
        {
            Assert.GreaterOrEqual(MainMenuLayout.RuleTop, MainMenuLayout.LogoTop + MainMenuLayout.LogoHeight);
            Assert.GreaterOrEqual(MainMenuLayout.FirstEntryTop, MainMenuLayout.RuleTop + MainMenuLayout.RuleHeight);

            // The notice is 18 units high, 6 under the last entry.
            Assert.LessOrEqual(MainMenuLayout.EntriesBottom + 6f + 18f, MainMenuLayout.SmallestCanvasHeight - 8f);
        }

        [Test]
        public void TheQuestionAndItsAnswersStandInThePlaceOfTheEntries()
        {
            Assert.LessOrEqual(MainMenuLayout.EntryTop(0) + MainMenuLayout.ConfirmTextHeight, MainMenuLayout.ConfirmAnswerTop(0));
            Assert.AreEqual(MainMenuLayout.EntryTop(2), MainMenuLayout.ConfirmAnswerTop(0));
            Assert.AreEqual(MainMenuLayout.EntriesBottom, MainMenuLayout.ConfirmAnswerTop(1) + MainMenuLayout.EntryHeight);
        }

        [Test]
        public void EveryEntryHasACaptionAndTheHighlightStaysGentle()
        {
            for (int i = 0; i < MainMenuModel.EntryCount; i++)
            {
                Assert.IsNotEmpty(MainMenuLook.Caption((MainMenuEntry)i));
            }

            for (float seconds = 0f; seconds < 6f; seconds += 0.05f)
            {
                float alpha = MainMenuLook.HighlightAlpha(seconds);
                Assert.GreaterOrEqual(alpha, MainMenuLook.HighlightLow - 0.0001f);
                Assert.LessOrEqual(alpha, MainMenuLook.HighlightHigh + 0.0001f);
            }

            Assert.AreEqual(MainMenuLook.HighlightAlpha(0.4f), MainMenuLook.HighlightAlpha(0.4f + MainMenuLook.HighlightPeriod), 0.0005f);
            Assert.AreEqual(MainMenuLook.HighlightHigh, MainMenuLook.HighlightAlpha(MainMenuLook.HighlightPeriod * 0.5f), 0.0005f);
        }

        // -------------------------------------------------------------- framing

        [Test]
        public void TheCharacterFillsAboutSeventyPercentOfTheScreenHeightFromHeadToToe()
        {
            foreach (float aspect in Aspects)
            {
                float feet = MainMenuStage.FeetOnScreen(aspect).y;
                float head = MainMenuStage.HeadOnScreen(aspect).y;

                Assert.That(head - feet, Is.InRange(0.67f, 0.73f), $"{aspect:0.00}: share of the screen height");
                Assert.Greater(feet, 0.17f, $"{aspect:0.00}: room under the feet for the name plate");
                Assert.Less(head, 0.93f, $"{aspect:0.00}: room above the head");
            }
        }

        [Test]
        public void TheCharacterStandsRightOfCentreAndWhollyOnScreen()
        {
            Assert.That(MainMenuStage.FeetOnScreen(16f / 9f).x, Is.InRange(0.62f, 0.72f), "16:9");

            foreach (float aspect in new[] { TabletAspect, 3f / 2f, 16f / 9f, 21f / 9f })
            {
                float centre = MainMenuStage.FeetOnScreen(aspect).x;
                float left = MainMenuStage.CharacterLeftOnScreen(aspect);
                Assert.Greater(centre, 0.58f, $"{aspect:0.00}");
                Assert.Less(centre + (centre - left), 0.95f, $"{aspect:0.00}: his right side");
            }
        }

        [Test]
        public void TheMenuColumnNeverReachesTheCharacter()
        {
            foreach (float aspect in Aspects)
            {
                AssertColumnClear(aspect, MainMenuLayout.SmallestCanvasHeight);
            }

            AssertColumnClear(TabletAspect, TabletCanvasHeight);
        }

        private static void AssertColumnClear(float aspect, float canvasHeight)
        {
            float canvasWidth = canvasHeight * aspect;
            float columnRight = MainMenuLayout.ColumnRight / canvasWidth;
            Assert.Less(columnRight + 0.01f, MainMenuStage.CharacterLeftOnScreen(aspect),
                $"{aspect:0.00} at {canvasHeight} units: the column's right edge against his left side");
        }

        [Test]
        public void TheNamePlateFitsUnderTheFeetAndBesideTheColumn()
        {
            foreach (float aspect in Aspects)
            {
                AssertNamePlateClear(aspect, MainMenuLayout.SmallestCanvasHeight);
            }

            AssertNamePlateClear(TabletAspect, TabletCanvasHeight);
        }

        private static void AssertNamePlateClear(float aspect, float canvasHeight)
        {
            Vector2 feet = MainMenuStage.FeetOnScreen(aspect);
            float top = feet.y * canvasHeight - MainMenuLayout.NamePlateGap;
            Assert.GreaterOrEqual(top - MainMenuLayout.NamePlateHeight, 6f, $"{aspect:0.00}: the plate's lower edge is on screen");

            float canvasWidth = canvasHeight * aspect;
            float left = feet.x * canvasWidth - MainMenuLayout.NamePlateWidth * 0.5f;
            Assert.Greater(left, MainMenuLayout.ColumnRight + 4f, $"{aspect:0.00}: the plate against the column");
            Assert.Less(left + MainMenuLayout.NamePlateWidth, canvasWidth - 4f, $"{aspect:0.00}: the plate's right edge");
        }

        [Test]
        public void TheCameraIsBelowHisChestAndInFrontOfHim()
        {
            Assert.Less(MainMenuStage.CameraPosition.y, MainMenuStage.CharacterHeight * 0.6f);
            Assert.Greater(MainMenuStage.CameraPosition.y, MainMenuStage.NearClip);
            Assert.Greater(MainMenuStage.CameraPitchUp, 0f);
            Assert.Less(MainMenuStage.CameraPosition.z, MainMenuStage.CharacterPosition.z);
            Assert.Greater(MainMenuStage.CameraPosition.z, MainMenuStage.TerraceBack + 1f, "the camera stands on the terrace");
        }

        // ----------------------------------------------------------------- land

        [Test]
        public void TheLandLiesUnderTheTerraceAtItsEdgeAndTheCharacterStandsOnTheFloor()
        {
            for (float x = -MainMenuStage.TerraceHalfWidth; x <= MainMenuStage.TerraceHalfWidth; x += 0.5f)
            {
                Assert.Less(MainMenuStage.ValleyHeight(x, MainMenuStage.TerraceEdge), 0f, $"x {x}");
                Assert.Greater(MainMenuStage.ValleyHeight(x, MainMenuStage.TerraceEdge), -MainMenuStage.TerraceThickness, $"x {x}");
            }

            Assert.AreEqual(0f, MainMenuStage.CharacterPosition.y);
            Assert.Less(MainMenuStage.CharacterPosition.z, MainMenuStage.TerraceEdge - 1f);
            Assert.Less(Mathf.Abs(MainMenuStage.CharacterPosition.x), MainMenuStage.TerraceHalfWidth - 1f);
        }

        [Test]
        public void TheValleyFloorComesIntoViewBeforeTheFarHillsRise()
        {
            float distance = MainMenuStage.FirstVisibleFloorDistance();
            Assert.Greater(distance, 30f);
            Assert.Less(distance + MainMenuStage.CameraPosition.z, 140f, "the floor is seen before the hills begin at 140 m");
        }

        [Test]
        public void TheLandInFrontNeverHidesTheSpireOrTheSky()
        {
            // From the camera over the open edge to the spire's base, the land along the middle stays under the line of sight.
            Vector3 eye = MainMenuStage.CameraPosition;
            Vector3 target = MainMenuStage.SpirePosition + new Vector3(0f, 8f, 0f);
            for (float t = 0.05f; t < 0.98f; t += 0.01f)
            {
                Vector3 point = Vector3.Lerp(eye, target, t);
                if (point.z <= MainMenuStage.TerraceEdge)
                {
                    continue;
                }

                Assert.Less(MainMenuStage.ValleyHeight(point.x, point.z), point.y, $"at {point.z:0} m");
            }
        }

        [Test]
        public void TheFarHillsRiseToTheSpire()
        {
            Vector3 spire = MainMenuStage.SpirePosition;
            Assert.AreEqual(spire.y, MainMenuStage.ValleyHeight(spire.x, spire.z), 3f, "the spire neither floats nor is buried");
            Assert.Less(spire.z, MainMenuStage.ValleyLength);
            Assert.Less(spire.z, MainMenuStage.FarClip + MainMenuStage.CameraPosition.z);
        }

        [Test]
        public void TheLandIsSmoothEnoughForItsGrid()
        {
            // The far grid cells are 8 m wide; a step of more than 5 m between neighbours would show as a cliff.
            for (float z = 70f; z < MainMenuStage.ValleyLength - 8f; z += 8f)
            {
                for (float x = -MainMenuStage.ValleyHalfWidth + 8f; x < MainMenuStage.ValleyHalfWidth - 8f; x += 8f)
                {
                    float here = MainMenuStage.ValleyHeight(x, z);
                    Assert.Less(Mathf.Abs(MainMenuStage.ValleyHeight(x + 8f, z) - here), 5f, $"({x}, {z}) along x");
                    Assert.Less(Mathf.Abs(MainMenuStage.ValleyHeight(x, z + 8f) - here), 5f, $"({x}, {z}) along z");
                }
            }
        }
    }
}
