using System;
using System.Globalization;
using System.Text;

namespace Threefold.DevTools
{
    /// <summary>
    /// Which build this is: the commit it was made from, the build run that made it, the branch and the time.
    /// The build writes it as a small text file into the game (PrototypeBuild), and the development
    /// diagnostics show it, so a tester can tell which build is installed. Plain C#: no Unity calls.
    ///
    /// Every part may be missing: a build made by hand on a computer has no run number, and a copy of the
    /// project without its Git folder has no commit. A missing part is said in words, never left blank or guessed.
    /// </summary>
    public readonly struct BuildStamp
    {
        /// <summary>The name of the text file in a Resources folder, without its extension.</summary>
        public const string ResourceName = "ThreefoldBuildInfo";

        private const int ShortCommitLength = 7;
        private const string TimeFormat = "yyyy-MM-dd HH:mm";

        public BuildStamp(string commit, string run, string runAttempt, string branch, string builtUtc)
        {
            Commit = Clean(commit);
            Run = Clean(run);
            RunAttempt = Clean(runAttempt);
            Branch = Clean(branch);
            BuiltUtc = Clean(builtUtc);
        }

        /// <summary>The full commit identifier, or empty if it is not known.</summary>
        public string Commit { get; }

        /// <summary>The number of the build run (GitHub Actions), or empty for a build made by hand.</summary>
        public string Run { get; }

        /// <summary>The attempt of that run, or empty.</summary>
        public string RunAttempt { get; }

        public string Branch { get; }

        /// <summary>When the build was made, in UTC, as text.</summary>
        public string BuiltUtc { get; }

        public static BuildStamp Create(string commit, string run, string runAttempt, string branch, DateTime builtUtc)
        {
            return new BuildStamp(commit, run, runAttempt, branch, builtUtc.ToString(TimeFormat, CultureInfo.InvariantCulture));
        }

        /// <summary>The first characters of a commit identifier, as GitHub shows it; empty for nothing usable.</summary>
        public static string ShortCommit(string commit)
        {
            string clean = Clean(commit);
            return clean.Length > ShortCommitLength ? clean.Substring(0, ShortCommitLength) : clean;
        }

        /// <summary>One line for the diagnostics, for example "commit 7776a69, run 17, branch main, built 2026-11-15 20:03 UTC".</summary>
        public string Describe()
        {
            var text = new StringBuilder(96);
            text.Append(Commit.Length > 0 ? "commit " + ShortCommit(Commit) : "commit unknown");
            text.Append(Run.Length > 0 ? ", run " + Run : ", built by hand (no run number)");
            if (Run.Length > 0 && RunAttempt.Length > 0 && RunAttempt != "1")
            {
                text.Append(" attempt ").Append(RunAttempt);
            }

            if (Branch.Length > 0)
            {
                text.Append(", branch ").Append(Branch);
            }

            if (BuiltUtc.Length > 0)
            {
                text.Append(", built ").Append(BuiltUtc).Append(" UTC");
            }

            return text.ToString();
        }

        /// <summary>The text of the file the build writes: one "name=value" per line.</summary>
        public string Serialize()
        {
            return "commit=" + Commit + "\nrun=" + Run + "\nattempt=" + RunAttempt + "\nbranch=" + Branch + "\nbuilt=" + BuiltUtc + "\n";
        }

        /// <summary>Reads what <see cref="Serialize"/> wrote. False for empty text or text with none of the names.</summary>
        public static bool TryParse(string text, out BuildStamp stamp)
        {
            stamp = default;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string commit = null, run = null, attempt = null, branch = null, built = null;
            bool any = false;
            foreach (string line in text.Split('\n'))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string value = line.Substring(separator + 1);
                switch (line.Substring(0, separator).Trim())
                {
                    case "commit": commit = value; any = true; break;
                    case "run": run = value; any = true; break;
                    case "attempt": attempt = value; any = true; break;
                    case "branch": branch = value; any = true; break;
                    case "built": built = value; any = true; break;
                }
            }

            if (!any)
            {
                return false;
            }

            stamp = new BuildStamp(commit, run, attempt, branch, built);
            return true;
        }

        /// <summary>
        /// The line the diagnostics show. In the Editor nothing was built, and a file left by an earlier build
        /// would name the wrong commit, so it is not read there.
        /// </summary>
        public static string DescribeForDiagnostics(bool inEditor, string resourceText)
        {
            if (inEditor)
            {
                return "Editor session (not a build)";
            }

            return TryParse(resourceText, out BuildStamp stamp)
                ? stamp.Describe()
                : "not stamped (this build carries no build information)";
        }

        /// <summary>
        /// The commit a Git folder has checked out, read from its files without running Git: HEAD holds either
        /// the commit itself or the name of a branch, whose commit is in a file of that name or in packed-refs.
        /// Empty if it cannot be told. <paramref name="readFile"/> returns a file's text by its path inside the
        /// Git folder, or null if there is no such file.
        /// </summary>
        public static string CommitFromGitFiles(Func<string, string> readFile)
        {
            if (readFile == null)
            {
                return string.Empty;
            }

            string head = Clean(readFile("HEAD"));
            const string prefix = "ref:";
            if (!head.StartsWith(prefix, StringComparison.Ordinal))
            {
                return IsCommit(head) ? head : string.Empty;
            }

            string reference = head.Substring(prefix.Length).Trim();
            string direct = Clean(readFile(reference));
            if (IsCommit(direct))
            {
                return direct;
            }

            string packed = readFile("packed-refs");
            if (packed != null)
            {
                foreach (string line in packed.Split('\n'))
                {
                    string entry = line.Trim();
                    int space = entry.IndexOf(' ');
                    if (space > 0 && entry.Substring(space + 1).Trim() == reference && IsCommit(entry.Substring(0, space)))
                    {
                        return entry.Substring(0, space);
                    }
                }
            }

            return string.Empty;
        }

        private static bool IsCommit(string text)
        {
            if (text == null || text.Length < ShortCommitLength || text.Length > 64)
            {
                return false;
            }

            foreach (char character in text)
            {
                bool hex = (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') ||
                           (character >= 'A' && character <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        // One line, no separators of the file format, never null.
        private static string Clean(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
    }
}
