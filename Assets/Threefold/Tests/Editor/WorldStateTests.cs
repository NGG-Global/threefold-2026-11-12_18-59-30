using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Threefold.Persistence;
using Threefold.Puzzles;

namespace Threefold.Tests
{
    /// <summary>
    /// The world state: what the store remembers, how it is written and read back, what happens when
    /// a save is damaged, and the checkpoint, reload and new-game operations. No Unity object is
    /// involved; files are written to a temporary folder that each test removes again.
    /// </summary>
    public sealed class WorldStateTests
    {
        private string folder;

        [SetUp]
        public void SetUp()
        {
            WorldState.ResetSession();
            PersistentIdRegistry.Clear();
            folder = Path.Combine(Path.GetTempPath(), "threefold-world-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            WorldState.ResetSession();
            PersistentIdRegistry.Clear();
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        private string SavePath => Path.Combine(folder, "world-state.txt");

        private static ObjectRecord Destroyed()
        {
            return new ObjectRecord { Saved = PersistedState.Destruction };
        }

        private static ObjectRecord Placed(float z)
        {
            return new ObjectRecord { Saved = PersistedState.Position, OffsetZ = z };
        }

        private static WorldStateStore SampleStore()
        {
            var store = new WorldStateStore();
            store.SetPuzzle("gateway/seal", new PuzzleRecord(true, true));
            store.SetPuzzle("gateway/lever", new PuzzleRecord(true, false));
            store.SetObject("range/barricade", Destroyed());
            store.SetObject("range/platform", Placed(4.25f));
            store.SetObject("range/door", new ObjectRecord
            {
                Saved = PersistedState.Damage | PersistedState.Activation, Integrity = 90f, Activated = true,
            });
            return store;
        }

        // ---------------------------------------------------------------- store

        [Test]
        public void TheStoreRemembersPuzzlesAndObjectsUnderTheirIdentifiers()
        {
            WorldStateStore store = SampleStore();

            Assert.AreEqual(2, store.PuzzleCount);
            Assert.AreEqual(3, store.ObjectCount);
            Assert.IsTrue(store.TryGetPuzzle("gateway/seal", out PuzzleRecord seal));
            Assert.IsTrue(seal.Active && seal.Locked);
            Assert.IsTrue(store.TryGetObject("range/platform", out ObjectRecord platform));
            Assert.AreEqual(4.25f, platform.OffsetZ);
            Assert.IsFalse(store.TryGetObject("range/unknown", out _));
            Assert.IsFalse(store.TryGetObject(string.Empty, out _));
            Assert.IsFalse(store.TryGetPuzzle(null, out _));
        }

        [Test]
        public void PuzzleKeysAndObjectIdentifiersCannotOverwriteEachOther()
        {
            var store = new WorldStateStore();
            store.SetPuzzle("door", new PuzzleRecord(true, false));
            store.SetObject("door", Destroyed());

            Assert.IsTrue(store.TryGetPuzzle("door", out PuzzleRecord puzzle));
            Assert.IsTrue(puzzle.Active);
            Assert.IsTrue(store.TryGetObject("door", out ObjectRecord record));
            Assert.AreEqual(PersistedState.Destruction, record.Saved);

            store.RemoveObject("door");
            Assert.IsTrue(store.TryGetPuzzle("door", out _), "Removing the object leaves the puzzle record.");
        }

        [Test]
        public void OnlyARealChangeRaisesTheRevision()
        {
            var store = new WorldStateStore();
            int changes = 0;
            store.Changed += () => changes++;

            store.SetObject("a", Destroyed());
            int revision = store.Revision;
            Assert.AreEqual(1, changes);

            store.SetObject("a", Destroyed());
            store.SetPuzzle(string.Empty, new PuzzleRecord(true, true));
            store.RemoveObject("missing");
            Assert.AreEqual(revision, store.Revision, "Writing what is already there, or nothing, is not a change.");
            Assert.AreEqual(1, changes);

            store.SetObject("a", Placed(1f));
            Assert.Greater(store.Revision, revision);
        }

        [Test]
        public void AnObjectInItsStartingStateNeedsNoEntry()
        {
            var store = new WorldStateStore();
            store.SetObject("a", Destroyed());
            store.SetObject("a", default);

            Assert.AreEqual(0, store.ObjectCount, "An empty record removes the entry.");
        }

        [Test]
        public void ASnapshotCanBeReturnedToAndIsNotChangedByWhatFollows()
        {
            WorldStateStore store = SampleStore();
            WorldStateSnapshot snapshot = store.Capture();

            store.SetObject("range/stone", Destroyed());
            store.RemovePuzzle("gateway/seal");
            store.SetObject("range/platform", Placed(6f));
            Assert.AreEqual(5, snapshot.Count, "The snapshot is a copy.");

            store.Restore(snapshot);
            Assert.AreEqual(WorldStateText.Write(SampleStore()), WorldStateText.Write(store));

            // And it can be returned to again.
            store.Clear();
            store.Restore(snapshot);
            Assert.AreEqual(5, store.Count);
        }

        // ----------------------------------------------------------------- text

        [Test]
        public void WhatIsWrittenIsReadBackExactly()
        {
            WorldStateStore store = SampleStore();
            var read = new WorldStateStore();

            WorldStateReadResult result = WorldStateText.Read(WorldStateText.Write(store), read);

            Assert.IsTrue(result.Ok);
            Assert.IsTrue(result.Complete);
            Assert.AreEqual(2, result.Puzzles);
            Assert.AreEqual(3, result.Objects);
            Assert.AreEqual(0, result.SkippedLines);
            Assert.AreEqual(WorldStateText.Write(store), WorldStateText.Write(read));

            Assert.IsTrue(read.TryGetObject("range/door", out ObjectRecord door));
            Assert.AreEqual(PersistedState.Damage | PersistedState.Activation, door.Saved);
            Assert.AreEqual(90f, door.Integrity);
            Assert.IsTrue(door.Activated);
        }

        [Test]
        public void TheSameStateAlwaysGivesTheSameText()
        {
            var first = new WorldStateStore();
            first.SetObject("b", Destroyed());
            first.SetObject("a", Placed(2f));
            first.SetPuzzle("z", new PuzzleRecord(true, true));
            first.SetPuzzle("y", new PuzzleRecord(false, true));

            // The same records, entered the other way round.
            var second = new WorldStateStore();
            second.SetPuzzle("y", new PuzzleRecord(false, true));
            second.SetPuzzle("z", new PuzzleRecord(true, true));
            second.SetObject("a", Placed(2f));
            second.SetObject("b", Destroyed());

            Assert.AreEqual(WorldStateText.Write(first), WorldStateText.Write(second));
        }

        [Test]
        public void AwkwardIdentifiersAndNumbersSurvive()
        {
            var store = new WorldStateStore();
            string id = "Scene One/Barricade 100% (copy)\nlineé";
            store.SetObject(id, new ObjectRecord
            {
                Saved = PersistedState.Position | PersistedState.Heat, OffsetX = -0.1f, OffsetY = 1e-7f, OffsetZ = 123456.79f,
                Heat = 33.333332f,
            });
            store.SetPuzzle("key with spaces", new PuzzleRecord(true, false));

            var read = new WorldStateStore();
            Assert.IsTrue(WorldStateText.Read(WorldStateText.Write(store), read).Complete);

            Assert.IsTrue(read.TryGetObject(id, out ObjectRecord record));
            Assert.AreEqual(-0.1f, record.OffsetX);
            Assert.AreEqual(1e-7f, record.OffsetY);
            Assert.AreEqual(123456.79f, record.OffsetZ);
            Assert.AreEqual(33.333332f, record.Heat);
            Assert.IsTrue(read.TryGetPuzzle("key with spaces", out _));
        }

        [Test]
        public void ATextThatIsNotAWorldStateIsRefusedAndTheStoreIsLeftAlone()
        {
            WorldStateStore store = SampleStore();
            string before = WorldStateText.Write(store);

            Assert.IsFalse(WorldStateText.Read("{ \"json\": true }", store).Ok);
            Assert.IsFalse(WorldStateText.Read(string.Empty, store).Ok);
            Assert.IsFalse(WorldStateText.Read(null, store).Ok);

            // A save from a later version is not guessed at, and not overwritten in memory either.
            string newer = WorldStateText.Header + " " + (WorldStateText.Version + 1) + "\nP a 1 1\nEND 1\n";
            WorldStateReadResult result = WorldStateText.Read(newer, store);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("version", result.Problem);

            Assert.AreEqual(before, WorldStateText.Write(store));
        }

        [Test]
        public void DamagedLinesAreLeftOutAndTheRestIsKept()
        {
            string text = WorldStateText.Header + " 1\n" +
                          "P gateway/seal 1 1\n" +
                          "P broken\n" +
                          "O range/barricade 1 0 0 0 0 0 0 0 0\n" +
                          "O range/nan 8 0 0 0 0 NaN 0 0 0\n" +
                          "O range/short 1 0 0\n" +
                          "X something 1\n" +
                          "O range/platform 8 0 0 0 0 0 0 4 0\n" +
                          "END 3\n";

            var store = new WorldStateStore();
            WorldStateReadResult result = WorldStateText.Read(text, store);

            Assert.IsTrue(result.Ok);
            Assert.IsFalse(result.Complete, "A save with damaged lines is not whole.");
            Assert.AreEqual(4, result.SkippedLines);
            Assert.AreEqual(1, store.PuzzleCount);
            Assert.AreEqual(2, store.ObjectCount);
            Assert.IsFalse(store.TryGetObject("range/nan", out _), "A number that is not a number is damage, not data.");
            Assert.IsTrue(store.TryGetObject("range/platform", out ObjectRecord platform));
            Assert.AreEqual(4f, platform.OffsetZ);
        }

        [Test]
        public void ATextThatWasCutShortIsNoticed()
        {
            string whole = WorldStateText.Write(SampleStore());
            string cut = whole.Substring(0, whole.LastIndexOf("END", StringComparison.Ordinal));

            var store = new WorldStateStore();
            WorldStateReadResult result = WorldStateText.Read(cut, store);
            Assert.IsTrue(result.Ok);
            Assert.IsFalse(result.Complete);

            // Cut in the middle of a record: the count at the end no longer matches either.
            string[] lines = whole.Split('\n');
            string missingOne = string.Join("\n", lines[0], lines[1], lines[2], lines[4], lines[5], lines[6]) + "\n";
            Assert.IsFalse(WorldStateText.Read(missingOne, new WorldStateStore()).Complete);
        }

        // ----------------------------------------------------------------- file

        [Test]
        public void ASaveOnDiskIsLoadedAgain()
        {
            Assert.IsTrue(WorldStateFile.TrySave(SavePath, SampleStore(), out string problem), problem);

            var loaded = new WorldStateStore();
            WorldStateLoadReport report = WorldStateFile.Load(SavePath, loaded);

            Assert.IsTrue(report.Loaded);
            Assert.IsFalse(report.UsedBackup);
            Assert.IsEmpty(report.Problem);
            Assert.AreEqual(WorldStateText.Write(SampleStore()), WorldStateText.Write(loaded));
        }

        [Test]
        public void WithNothingSavedNothingIsLoadedAndNothingIsWrong()
        {
            var store = new WorldStateStore();
            store.SetObject("kept", Destroyed());

            WorldStateLoadReport report = WorldStateFile.Load(SavePath, store);

            Assert.IsFalse(report.Loaded);
            Assert.IsEmpty(report.Problem);
            Assert.AreEqual(1, store.ObjectCount, "The store is left as it was.");
            Assert.IsFalse(WorldStateFile.Load(string.Empty, store).Loaded);
        }

        [Test]
        public void ADamagedSaveFallsBackToThePreviousOne()
        {
            var first = new WorldStateStore();
            first.SetObject("range/barricade", Destroyed());
            Assert.IsTrue(WorldStateFile.TrySave(SavePath, first, out _));

            // A second save keeps the first as the backup.
            WorldStateStore second = SampleStore();
            Assert.IsTrue(WorldStateFile.TrySave(SavePath, second, out _));
            Assert.IsTrue(File.Exists(SavePath + WorldStateFile.BackupSuffix));

            // The newer file is then cut short, as by a write that never finished.
            string text = File.ReadAllText(SavePath);
            File.WriteAllText(SavePath, text.Substring(0, text.Length / 2));

            var loaded = new WorldStateStore();
            WorldStateLoadReport report = WorldStateFile.Load(SavePath, loaded);

            Assert.IsTrue(report.Loaded);
            Assert.IsTrue(report.UsedBackup);
            Assert.IsNotEmpty(report.Problem, "The player's side is told that the save was damaged.");
            Assert.AreEqual(WorldStateText.Write(first), WorldStateText.Write(loaded), "The previous, whole save is used.");
        }

        [Test]
        public void AMissingMainFileIsReplacedByTheBackup()
        {
            Assert.IsTrue(WorldStateFile.TrySave(SavePath, SampleStore(), out _));
            File.Move(SavePath, SavePath + WorldStateFile.BackupSuffix);

            var loaded = new WorldStateStore();
            WorldStateLoadReport report = WorldStateFile.Load(SavePath, loaded);

            Assert.IsTrue(report.Loaded && report.UsedBackup);
            Assert.AreEqual(5, loaded.Count);
        }

        [Test]
        public void SavingAndLoadingAgainAndAgainChangesNothing()
        {
            WorldStateStore store = SampleStore();
            string expected = WorldStateText.Write(store);

            for (int round = 0; round < 6; round++)
            {
                Assert.IsTrue(WorldStateFile.TrySave(SavePath, store, out _));
                var next = new WorldStateStore();
                Assert.IsTrue(WorldStateFile.Load(SavePath, next).Loaded);
                Assert.AreEqual(expected, WorldStateText.Write(next), $"Round {round}.");
                store = next;
            }
        }

        [Test]
        public void ASaveThatCannotBeWrittenIsReportedNotThrown()
        {
            // A file where the folder should be.
            Directory.CreateDirectory(folder);
            string blocker = Path.Combine(folder, "blocker");
            File.WriteAllText(blocker, "x");

            bool saved = WorldStateFile.TrySave(Path.Combine(blocker, "world-state.txt"), SampleStore(), out string problem);

            Assert.IsFalse(saved);
            Assert.IsNotEmpty(problem);
            Assert.IsFalse(WorldStateFile.TrySave(string.Empty, SampleStore(), out _));
        }

        // ------------------------------------------------------------ the session

        [Test]
        public void PuzzleMemoryAndTheWorldStateAreOneStore()
        {
            PuzzleSessionState.Set("gateway/seal", new PuzzleRecord(true, true));

            Assert.AreEqual(1, WorldState.Current.PuzzleCount);
            Assert.IsTrue(WorldState.Current.TryGetPuzzle("gateway/seal", out PuzzleRecord record));
            Assert.IsTrue(record.Locked);

            WorldState.Current.SetObject("range/barricade", Destroyed());
            PuzzleSessionState.Clear();
            Assert.AreEqual(0, PuzzleSessionState.Count);
            Assert.AreEqual(1, WorldState.Current.ObjectCount, "Clearing puzzle memory leaves the objects alone.");
        }

        [Test]
        public void ARestartKeepsTheStoreAndTellsTheWorld()
        {
            var reloads = new List<WorldReload>();
            Action<WorldReload> listener = reloads.Add;
            WorldState.Reloaded += listener;
            try
            {
                WorldState.Current.SetObject("range/barricade", Destroyed());
                int revision = WorldState.Current.Revision;

                WorldState.ReloadWorld();

                CollectionAssert.AreEqual(new[] { WorldReload.Restart }, reloads);
                Assert.AreEqual(revision, WorldState.Current.Revision, "A restart changes nothing that was saved.");
                Assert.IsTrue(WorldState.Current.TryGetObject("range/barricade", out _));
            }
            finally
            {
                WorldState.Reloaded -= listener;
            }
        }

        [Test]
        public void ACheckpointCanBeReturnedTo()
        {
            var reloads = new List<WorldReload>();
            Action<WorldReload> listener = reloads.Add;
            WorldState.Reloaded += listener;
            try
            {
                Assert.IsFalse(WorldState.RevertToCheckpoint(), "There is nothing to return to yet.");
                Assert.IsEmpty(reloads);

                WorldState.Current.SetObject("range/barricade", Destroyed());
                WorldState.SaveCheckpoint();
                Assert.IsTrue(WorldState.HasCheckpoint);

                WorldState.Current.SetObject("range/stone", Destroyed());
                WorldState.Current.RemoveObject("range/barricade");
                PuzzleSessionState.Set("gateway/seal", new PuzzleRecord(true, true));

                Assert.IsTrue(WorldState.RevertToCheckpoint());

                CollectionAssert.AreEqual(new[] { WorldReload.StateReplaced }, reloads);
                Assert.IsTrue(WorldState.Current.TryGetObject("range/barricade", out _));
                Assert.IsFalse(WorldState.Current.TryGetObject("range/stone", out _));
                Assert.AreEqual(0, PuzzleSessionState.Count);

                // The checkpoint stays: it can be returned to as often as needed.
                WorldState.Current.Clear();
                Assert.IsTrue(WorldState.RevertToCheckpoint());
                Assert.AreEqual(1, WorldState.Current.Count);
            }
            finally
            {
                WorldState.Reloaded -= listener;
            }
        }

        [Test]
        public void TheSessionSavesOnlyWhenSomethingChangedAndLoadsItBack()
        {
            Assert.IsFalse(WorldState.SavesToDisk, "A session keeps its state in memory unless it is given a file.");
            WorldState.Current.SetObject("range/barricade", Destroyed());
            Assert.IsFalse(WorldState.IsDirty, "Without a file there is nothing to write.");
            Assert.IsTrue(WorldState.Save());

            WorldState.FilePath = SavePath;
            Assert.IsTrue(WorldState.IsDirty);
            Assert.IsTrue(WorldState.Save());
            Assert.IsFalse(WorldState.IsDirty);
            DateTime written = File.GetLastWriteTimeUtc(SavePath);

            // Nothing changed: nothing is written, and there is no backup of an identical file.
            Assert.IsTrue(WorldState.Save());
            Assert.AreEqual(written, File.GetLastWriteTimeUtc(SavePath));
            Assert.IsFalse(File.Exists(SavePath + WorldStateFile.BackupSuffix));

            // A later session finds it.
            WorldState.ResetSession();
            WorldState.FilePath = SavePath;
            WorldStateLoadReport report = WorldState.Load();
            Assert.IsTrue(report.Loaded);
            Assert.IsTrue(WorldState.Current.TryGetObject("range/barricade", out _));
            Assert.IsFalse(WorldState.IsDirty, "What was just loaded does not need saving.");
        }

        [Test]
        public void ANewGameForgetsEverythingAlsoOnDisk()
        {
            WorldState.FilePath = SavePath;
            WorldState.Current.SetObject("range/barricade", Destroyed());
            PuzzleSessionState.Set("gateway/seal", new PuzzleRecord(true, true));
            WorldState.SaveCheckpoint();
            Assert.IsTrue(WorldState.Save());
            WorldState.Current.SetObject("range/stone", Destroyed());
            Assert.IsTrue(WorldState.Save());

            var reloads = new List<WorldReload>();
            Action<WorldReload> listener = reloads.Add;
            WorldState.Reloaded += listener;
            try
            {
                WorldState.NewGame();
            }
            finally
            {
                WorldState.Reloaded -= listener;
            }

            Assert.AreEqual(0, WorldState.Current.Count);
            Assert.IsFalse(WorldState.HasCheckpoint);
            Assert.IsFalse(File.Exists(SavePath));
            Assert.IsFalse(File.Exists(SavePath + WorldStateFile.BackupSuffix));
            Assert.IsFalse(WorldState.IsDirty);
            CollectionAssert.AreEqual(new[] { WorldReload.StateReplaced }, reloads);

            // Nothing comes back from disk afterwards.
            Assert.IsFalse(WorldState.Load().Loaded);
        }

        // ---------------------------------------------------------- identifiers

        [Test]
        public void TwoObjectsCannotShareAnIdentifier()
        {
            var first = new object();
            var second = new object();

            Assert.IsTrue(PersistentIdRegistry.TryRegister("range/barricade", first));
            Assert.IsTrue(PersistentIdRegistry.TryRegister("range/barricade", first), "Asking again for one's own id is fine.");
            Assert.IsFalse(PersistentIdRegistry.TryRegister("range/barricade", second), "The second object is refused.");
            Assert.AreEqual(1, PersistentIdRegistry.DuplicateCount);
            Assert.IsFalse(PersistentIdRegistry.TryRegister(string.Empty, second));

            // The intruder cannot release the owner's claim.
            Assert.IsFalse(PersistentIdRegistry.Unregister("range/barricade", second));
            Assert.IsTrue(PersistentIdRegistry.IsRegistered("range/barricade"));

            // Once the owner is gone, the id is free again.
            Assert.IsTrue(PersistentIdRegistry.Unregister("range/barricade", first));
            Assert.IsTrue(PersistentIdRegistry.TryRegister("range/barricade", second));
        }

        [Test]
        public void DuplicateIdentifiersAreFoundBeforeTheGameRuns()
        {
            var duplicates = new List<string>();
            var ids = new[] { "a", "b", string.Empty, "a", "c", "b", "a", null, string.Empty };

            Assert.AreEqual(2, PersistentIdRegistry.FindDuplicates(ids, duplicates));
            CollectionAssert.AreEqual(new[] { "a", "b" }, duplicates, "Each once, in the order found; empty ids are not duplicates.");

            Assert.AreEqual(0, PersistentIdRegistry.FindDuplicates(new[] { "x", "y" }, duplicates));
            Assert.IsEmpty(duplicates);
        }
    }
}
