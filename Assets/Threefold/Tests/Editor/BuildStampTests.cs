using System;
using System.Collections.Generic;
using NUnit.Framework;
using Threefold.DevTools;

namespace Threefold.Tests
{
    /// <summary>The build's identity as the development diagnostics show it. Plain C#: these run outside Unity too.</summary>
    public sealed class BuildStampTests
    {
        private const string Commit = "7776a69c4e28c40916d0ac4c5ad988d3eecc8ed5";
        private static readonly DateTime Built = new DateTime(2026, 11, 15, 20, 3, 53, DateTimeKind.Utc);

        [Test]
        public void ABuildFromGitHubActionsNamesItsCommitRunBranchAndTime()
        {
            BuildStamp stamp = BuildStamp.Create(Commit, "17", "1", "main", Built);
            Assert.AreEqual("commit 7776a69, run 17, branch main, built 2026-11-15 20:03 UTC", stamp.Describe());
        }

        [Test]
        public void ALaterAttemptOfARunIsNamed()
        {
            StringAssert.Contains("run 11 attempt 3", BuildStamp.Create(Commit, "11", "3", "main", Built).Describe());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void ABuildWithoutGitHubActionsSaysItWasBuiltByHand(string missing)
        {
            string text = BuildStamp.Create(Commit, missing, missing, missing, Built).Describe();
            Assert.AreEqual("commit 7776a69, built by hand (no run number), built 2026-11-15 20:03 UTC", text);
        }

        [Test]
        public void AnUnknownCommitIsSaidNotGuessed()
        {
            StringAssert.StartsWith("commit unknown, run 17", BuildStamp.Create(null, "17", "1", "main", Built).Describe());
        }

        [Test]
        public void WhatTheBuildWritesIsReadBackUnchanged()
        {
            BuildStamp written = BuildStamp.Create(Commit, "17", "2", "feature/look", Built);
            Assert.IsTrue(BuildStamp.TryParse(written.Serialize(), out BuildStamp read));
            Assert.AreEqual(Commit, read.Commit);
            Assert.AreEqual("17", read.Run);
            Assert.AreEqual("2", read.RunAttempt);
            Assert.AreEqual("feature/look", read.Branch);
            Assert.AreEqual(written.Describe(), read.Describe());

            Assert.IsTrue(BuildStamp.TryParse(written.Serialize().Replace("\n", "\r\n"), out BuildStamp windows));
            Assert.AreEqual(written.Describe(), windows.Describe());
        }

        [Test]
        public void AValueCannotBreakTheFile()
        {
            var stamp = new BuildStamp(Commit, "17\nrun=99", "1", "main", "now");
            Assert.IsTrue(BuildStamp.TryParse(stamp.Serialize(), out BuildStamp read));
            Assert.AreEqual("17 run=99", read.Run);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("nothing useful here")]
        public void ABuildWithoutTheFileSaysItIsNotStamped(string text)
        {
            Assert.IsFalse(BuildStamp.TryParse(text, out BuildStamp _));
            StringAssert.StartsWith("not stamped", BuildStamp.DescribeForDiagnostics(false, text));
        }

        [Test]
        public void TheEditorNeverShowsAStampLeftByAnEarlierBuild()
        {
            string left = BuildStamp.Create(Commit, "17", "1", "main", Built).Serialize();
            Assert.AreEqual("Editor session (not a build)", BuildStamp.DescribeForDiagnostics(true, left));
            StringAssert.Contains("run 17", BuildStamp.DescribeForDiagnostics(false, left));
        }

        [Test]
        public void TheCommitIsReadFromAGitFolder()
        {
            var detached = new Dictionary<string, string> { { "HEAD", Commit + "\n" } };
            Assert.AreEqual(Commit, BuildStamp.CommitFromGitFiles(Reader(detached)));

            var branch = new Dictionary<string, string>
            {
                { "HEAD", "ref: refs/heads/main\n" },
                { "refs/heads/main", Commit + "\n" },
            };
            Assert.AreEqual(Commit, BuildStamp.CommitFromGitFiles(Reader(branch)));

            var packed = new Dictionary<string, string>
            {
                { "HEAD", "ref: refs/heads/main\n" },
                { "packed-refs", "# pack-refs with: peeled fully-peeled sorted\n1111111111111111111111111111111111111111 refs/heads/other\n" + Commit + " refs/heads/main\n" },
            };
            Assert.AreEqual(Commit, BuildStamp.CommitFromGitFiles(Reader(packed)));
        }

        [Test]
        public void AMissingOrUnreadableGitFolderGivesNoCommit()
        {
            Assert.AreEqual(string.Empty, BuildStamp.CommitFromGitFiles(null));
            Assert.AreEqual(string.Empty, BuildStamp.CommitFromGitFiles(Reader(new Dictionary<string, string>())));
            Assert.AreEqual(string.Empty, BuildStamp.CommitFromGitFiles(Reader(new Dictionary<string, string> { { "HEAD", "ref: refs/heads/gone\n" } })));
            Assert.AreEqual(string.Empty, BuildStamp.CommitFromGitFiles(Reader(new Dictionary<string, string> { { "HEAD", "not a commit\n" } })));
        }

        [Test]
        public void TheCommitIsShortenedAsGitHubShowsIt()
        {
            Assert.AreEqual("7776a69", BuildStamp.ShortCommit(Commit));
            Assert.AreEqual("abc", BuildStamp.ShortCommit("abc"));
            Assert.AreEqual(string.Empty, BuildStamp.ShortCommit(null));
        }

        private static Func<string, string> Reader(Dictionary<string, string> files)
        {
            return path => files.TryGetValue(path, out string text) ? text : null;
        }
    }
}
