using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Threefold.Puzzles;

namespace Threefold.Persistence
{
    /// <summary>What reading a saved world state found.</summary>
    public struct WorldStateReadResult
    {
        /// <summary>The text was a world state this version can read, and the store now holds it.</summary>
        public bool Ok;

        /// <summary>Every record that was written was read: the text was not cut short.</summary>
        public bool Complete;

        public int Puzzles;
        public int Objects;

        /// <summary>Lines that could not be understood and were left out.</summary>
        public int SkippedLines;

        /// <summary>Why the text was refused, when <see cref="Ok"/> is false.</summary>
        public string Problem;
    }

    /// <summary>
    /// Writes the world state as text and reads it back. One record per line, numbers in a fixed
    /// culture, records in a fixed order: the same state always gives the same text.
    ///
    /// Reading is cautious. A text with the wrong header, or from a newer version, is refused and the
    /// store is left as it was. A line that cannot be understood is skipped and counted; the rest is
    /// kept, because each record stands on its own. The last line states how many records were
    /// written, which shows whether the text was cut short. Plain C#, tested outside Unity.
    /// </summary>
    public static class WorldStateText
    {
        public const string Header = "THREEFOLD-WORLD-STATE";
        public const int Version = 1;

        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        public static string Write(WorldStateStore store)
        {
            var text = new StringBuilder(256);
            text.Append(Header).Append(' ').Append(Version.ToString(Culture)).Append('\n');

            int count = 0;
            foreach (string key in store.GetSortedPuzzleKeys())
            {
                store.TryGetPuzzle(key, out PuzzleRecord record);
                text.Append("P ").Append(Escape(key)).Append(' ').Append(record.Active ? '1' : '0').Append(' ')
                    .Append(record.Locked ? '1' : '0').Append('\n');
                count++;
            }

            foreach (string id in store.GetSortedObjectIds())
            {
                store.TryGetObject(id, out ObjectRecord record);
                text.Append("O ").Append(Escape(id)).Append(' ').Append(((int)record.Saved).ToString(Culture)).Append(' ')
                    .Append(Number(record.Integrity)).Append(' ').Append(record.Activated ? '1' : '0').Append(' ')
                    .Append(Number(record.Charge)).Append(' ').Append(record.Collapsed ? '1' : '0').Append(' ')
                    .Append(Number(record.OffsetX)).Append(' ').Append(Number(record.OffsetY)).Append(' ')
                    .Append(Number(record.OffsetZ)).Append(' ').Append(Number(record.Heat)).Append('\n');
                count++;
            }

            text.Append("END ").Append(count.ToString(Culture)).Append('\n');
            return text.ToString();
        }

        /// <summary>
        /// Reads a world state into <paramref name="into"/>, replacing what it held. If the text is
        /// refused, the store is not touched.
        /// </summary>
        public static WorldStateReadResult Read(string text, WorldStateStore into)
        {
            var result = new WorldStateReadResult();
            if (string.IsNullOrEmpty(text))
            {
                result.Problem = "The saved world state is empty.";
                return result;
            }

            string[] lines = text.Split('\n');
            string[] head = lines[0].Trim().Split(' ');
            if (head.Length != 2 || head[0] != Header || !int.TryParse(head[1], NumberStyles.Integer, Culture, out int version))
            {
                result.Problem = "The file is not a Threefold world state.";
                return result;
            }

            if (version > Version)
            {
                result.Problem = $"The saved world state is version {version}; this build reads up to version {Version}.";
                return result;
            }

            // Read into a store of its own first, so a refusal part of the way cannot leave half a state behind.
            var read = new WorldStateStore();
            int declared = -1;

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                string[] parts = line.Split(' ');
                switch (parts[0])
                {
                    case "P":
                        if (parts.Length == 4 && TryUnescape(parts[1], out string key) && TryFlag(parts[2], out bool active) &&
                            TryFlag(parts[3], out bool locked))
                        {
                            read.SetPuzzle(key, new PuzzleRecord(active, locked));
                        }
                        else
                        {
                            result.SkippedLines++;
                        }

                        break;

                    case "O":
                        if (parts.Length == 11 && TryUnescape(parts[1], out string id) && TryObject(parts, out ObjectRecord record))
                        {
                            read.SetObject(id, record);
                        }
                        else
                        {
                            result.SkippedLines++;
                        }

                        break;

                    case "END":
                        if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.Integer, Culture, out declared))
                        {
                            declared = -1;
                        }

                        break;

                    default:
                        // A kind of line this version does not know. Written by a later version of the same format.
                        result.SkippedLines++;
                        break;
                }
            }

            result.Ok = true;
            result.Puzzles = read.PuzzleCount;
            result.Objects = read.ObjectCount;
            result.Complete = declared == read.Count && result.SkippedLines == 0;
            into.CopyFrom(read);
            return result;
        }

        private static bool TryObject(string[] parts, out ObjectRecord record)
        {
            record = default;
            if (!int.TryParse(parts[2], NumberStyles.Integer, Culture, out int saved) || saved < 0 ||
                !TryNumber(parts[3], out record.Integrity) || !TryFlag(parts[4], out record.Activated) ||
                !TryNumber(parts[5], out record.Charge) || !TryFlag(parts[6], out record.Collapsed) ||
                !TryNumber(parts[7], out record.OffsetX) || !TryNumber(parts[8], out record.OffsetY) ||
                !TryNumber(parts[9], out record.OffsetZ) || !TryNumber(parts[10], out record.Heat))
            {
                return false;
            }

            record.Saved = (PersistedState)saved;
            return !record.IsEmpty;
        }

        private static string Number(float value)
        {
            return value.ToString("R", Culture);
        }

        /// <summary>A number that is not finite is damage, not data: the record is refused.</summary>
        private static bool TryNumber(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, Culture, out value) && !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static bool TryFlag(string text, out bool value)
        {
            value = text == "1";
            return text == "1" || text == "0";
        }

        /// <summary>Keeps letters, digits and a few signs; everything else (spaces, line ends, per cent) is written as %XXXX.</summary>
        public static string Escape(string value)
        {
            var text = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                bool plain = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' ||
                             c == '-' || c == '.' || c == '/' || c == ':';
                if (plain)
                {
                    text.Append(c);
                }
                else
                {
                    text.Append('%').Append(((int)c).ToString("X4", Culture));
                }
            }

            return text.ToString();
        }

        public static bool TryUnescape(string value, out string result)
        {
            var text = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '%')
                {
                    text.Append(c);
                    continue;
                }

                if (i + 4 >= value.Length ||
                    !int.TryParse(value.Substring(i + 1, 4), NumberStyles.AllowHexSpecifier, Culture, out int code))
                {
                    result = null;
                    return false;
                }

                text.Append((char)code);
                i += 4;
            }

            result = text.ToString();
            return result.Length > 0;
        }
    }

    /// <summary>What loading the world state from disk found.</summary>
    public struct WorldStateLoadReport
    {
        /// <summary>A saved state was found and is now in the store.</summary>
        public bool Loaded;

        /// <summary>The main file was missing or damaged and the previous save was used instead.</summary>
        public bool UsedBackup;

        public WorldStateReadResult Read;

        /// <summary>What went wrong, if anything did. Empty when there was simply nothing saved yet.</summary>
        public string Problem;

        /// <summary>A save that was written in full but never put in place (the app was ended in between) was used: it is the newest.</summary>
        public bool UsedPendingSave;

        /// <summary>How many files this version could not use were set aside, under a name ending in <see cref="WorldStateFile.UnreadableSuffix"/>.</summary>
        public int SetAside;

        /// <summary>
        /// A file that could not be used could not be set aside either. Saving to this place would
        /// overwrite it, so the session must not save there.
        /// </summary>
        public bool SaveBlocked;

        /// <summary>What was loaded did not come from a sound main file, so it should be written soon.</summary>
        public bool NeedsSave;
    }

    /// <summary>
    /// The world state on disk. A save is written to a new file first, flushed to the storage, and
    /// then put in the place of the old one, which is kept as a backup: an interrupted write (the
    /// app killed, the battery empty) cannot leave a half-written save as the only copy.
    ///
    /// Loading prefers a complete save that was written but never put in place (it is the newest),
    /// then a complete main file, then a complete backup, then whatever of the main file or the
    /// backup could be read.
    ///
    /// A file this version cannot use in full (damaged, cut short, from a newer version of the game,
    /// unreadable) is never overwritten or deleted by a later save: it is set aside under a name
    /// ending in <see cref="UnreadableSuffix"/>. If it cannot even be set aside, the load says that
    /// saving must stop. Plain C#.
    /// </summary>
    public static class WorldStateFile
    {
        public const string BackupSuffix = ".bak";
        public const string TempSuffix = ".tmp";
        public const string UnreadableSuffix = ".unreadable";
        private const int MaxSetAside = 20;

        public static bool TrySave(string path, WorldStateStore store, out string problem)
        {
            problem = string.Empty;
            if (string.IsNullOrEmpty(path))
            {
                problem = "No path to save to.";
                return false;
            }

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temp = path + TempSuffix;
                WriteThrough(temp, WorldStateText.Write(store));

                if (File.Exists(path))
                {
                    string backup = path + BackupSuffix;
                    if (File.Exists(backup))
                    {
                        File.Delete(backup);
                    }

                    File.Move(path, backup);
                }

                File.Move(temp, path);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                              exception is NotSupportedException || exception is ArgumentException)
            {
                problem = exception.Message;
                return false;
            }
        }

        public static WorldStateLoadReport Load(string path, WorldStateStore into)
        {
            var report = new WorldStateLoadReport { Problem = string.Empty };
            if (string.IsNullOrEmpty(path))
            {
                return report;
            }

            var main = new Candidate(path);
            var backup = new Candidate(path + BackupSuffix);
            var pending = new Candidate(path + TempSuffix);
            if (!main.Exists && !backup.Exists && !pending.Exists)
            {
                return report;
            }

            // The order of preference. A complete pending save is newer than the main file it was to replace.
            Candidate used = null;
            string problem = string.Empty;
            if (pending.Sound)
            {
                used = pending;
                report.UsedPendingSave = true;
                problem = "The last save was interrupted before it was put in place; it was complete and has been used.";
            }
            else if (main.Sound)
            {
                used = main;
            }
            else if (backup.Sound)
            {
                used = backup;
                report.UsedBackup = true;
                problem = main.Exists ? "The saved world state was damaged; the previous save was used." : string.Empty;
            }
            else if (main.Read.Ok)
            {
                used = main;
                problem = "The saved world state was damaged; part of it could not be read.";
            }
            else if (backup.Read.Ok)
            {
                used = backup;
                report.UsedBackup = true;
                problem = "The saved world state was damaged; part of the previous save was used.";
            }

            if (used != null)
            {
                into.CopyFrom(used.Store);
                report.Loaded = true;
                report.Read = used.Read;
                report.NeedsSave = used != main || !main.Sound;
            }
            else
            {
                report.Read = main.Exists ? main.Read : backup.Read;
                problem = FirstProblem(main, backup, pending);
            }

            // What this version could not use in full is kept aside, never overwritten by the next save.
            // A pending save that is ours but cut short is only an interrupted write and is simply replaced.
            string blocked = string.Empty;
            SetAsideIfUnsound(main, ref report, ref blocked);
            SetAsideIfUnsound(backup, ref report, ref blocked);
            if (pending.Exists && !pending.Sound && !pending.Read.Ok)
            {
                SetAsideIfUnsound(pending, ref report, ref blocked);
            }

            if (report.SetAside > 0)
            {
                problem = Join(problem, $"{report.SetAside} file(s) this version could not use were kept beside it, " +
                                        $"ending in '{UnreadableSuffix}'.");
            }

            if (report.SaveBlocked)
            {
                problem = Join(problem, $"'{blocked}' could not be used and could not be set aside, so nothing will be " +
                                        "saved in this session, to keep it from being overwritten.");
            }

            report.Problem = problem;
            return report;
        }

        /// <summary>Removes the save and its backup. Returns false if a file could not be removed.</summary>
        public static bool Delete(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }

            try
            {
                File.Delete(path);
                File.Delete(path + BackupSuffix);
                File.Delete(path + TempSuffix);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>One file that may hold the world state, read once.</summary>
        private sealed class Candidate
        {
            public readonly string Path;
            public readonly bool Exists;
            public readonly string Problem;
            public readonly WorldStateStore Store = new WorldStateStore();
            public readonly WorldStateReadResult Read;

            public Candidate(string path)
            {
                Path = path;
                bool readable = TryReadFile(path, out string text, out string problem);
                Exists = readable || !string.IsNullOrEmpty(problem);
                Problem = problem;
                Read = readable ? WorldStateText.Read(text, Store) : default;
            }

            /// <summary>Read in full, by this version.</summary>
            public bool Sound => Read.Ok && Read.Complete;
        }

        private static void SetAsideIfUnsound(Candidate file, ref WorldStateLoadReport report, ref string blocked)
        {
            if (!file.Exists || file.Sound)
            {
                return;
            }

            if (TrySetAside(file.Path))
            {
                report.SetAside++;
            }
            else
            {
                report.SaveBlocked = true;
                blocked = file.Path;
            }
        }

        /// <summary>Moves a file to a free name ending in <see cref="UnreadableSuffix"/>. Never replaces anything.</summary>
        private static bool TrySetAside(string path)
        {
            try
            {
                for (int i = 1; i <= MaxSetAside; i++)
                {
                    string target = i == 1 ? path + UnreadableSuffix : $"{path}{UnreadableSuffix}-{i}";
                    if (!File.Exists(target))
                    {
                        File.Move(path, target);
                        return true;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                              exception is NotSupportedException || exception is ArgumentException)
            {
                return false;
            }

            return false;
        }

        /// <summary>Writes the text and asks for it to reach the storage before the file is put in place.</summary>
        private static void WriteThrough(string path, string text)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static string FirstProblem(Candidate main, Candidate backup, Candidate pending)
        {
            foreach (Candidate file in new[] { main, backup, pending })
            {
                if (!string.IsNullOrEmpty(file.Problem)) return file.Problem;
                if (file.Exists && !string.IsNullOrEmpty(file.Read.Problem)) return file.Read.Problem;
            }

            return "The saved world state could not be read.";
        }

        private static string Join(string first, string second)
        {
            return string.IsNullOrEmpty(first) ? second : first + " " + second;
        }

        private static bool TryReadFile(string path, out string text, out string problem)
        {
            text = null;
            problem = string.Empty;
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                text = File.ReadAllText(path, Encoding.UTF8);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                              exception is NotSupportedException || exception is ArgumentException)
            {
                problem = exception.Message;
                return false;
            }
        }
    }
}
