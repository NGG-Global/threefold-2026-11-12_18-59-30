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
    }

    /// <summary>
    /// The world state on disk. A save is written to a new file first and then put in the place of
    /// the old one, and the old one is kept as a backup: an interrupted write (the app killed, the
    /// battery empty) cannot leave a half-written save as the only copy. Loading prefers a complete
    /// main file, then a complete backup, then whatever of the main file could be read. Plain C#.
    /// </summary>
    public static class WorldStateFile
    {
        public const string BackupSuffix = ".bak";
        private const string TempSuffix = ".tmp";

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
                File.WriteAllText(temp, WorldStateText.Write(store), new UTF8Encoding(false));

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

            bool hasMain = TryReadFile(path, out string mainText, out string mainProblem);
            bool hasBackup = TryReadFile(path + BackupSuffix, out string backupText, out _);
            if (!hasMain && !hasBackup)
            {
                report.Problem = mainProblem;
                return report;
            }

            var main = new WorldStateStore();
            WorldStateReadResult mainRead = hasMain ? WorldStateText.Read(mainText, main) : default;
            if (mainRead.Ok && mainRead.Complete)
            {
                return Accept(main, mainRead, false, into);
            }

            var backup = new WorldStateStore();
            WorldStateReadResult backupRead = hasBackup ? WorldStateText.Read(backupText, backup) : default;
            if (backupRead.Ok && backupRead.Complete)
            {
                WorldStateLoadReport fromBackup = Accept(backup, backupRead, true, into);
                fromBackup.Problem = hasMain ? "The saved world state was damaged; the previous save was used." : string.Empty;
                return fromBackup;
            }

            // Neither is whole. What could be read of the newer one is better than nothing.
            if (mainRead.Ok)
            {
                WorldStateLoadReport partial = Accept(main, mainRead, false, into);
                partial.Problem = "The saved world state was damaged; part of it could not be read.";
                return partial;
            }

            if (backupRead.Ok)
            {
                WorldStateLoadReport partial = Accept(backup, backupRead, true, into);
                partial.Problem = "The saved world state was damaged; part of the previous save was used.";
                return partial;
            }

            report.Read = mainRead;
            report.Problem = !string.IsNullOrEmpty(mainRead.Problem) ? mainRead.Problem : backupRead.Problem;
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

        private static WorldStateLoadReport Accept(WorldStateStore read, WorldStateReadResult result, bool usedBackup,
            WorldStateStore into)
        {
            into.CopyFrom(read);
            return new WorldStateLoadReport { Loaded = true, UsedBackup = usedBackup, Read = result, Problem = string.Empty };
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
