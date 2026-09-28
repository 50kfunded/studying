using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace NowAndDoing
{
    internal sealed class FocusSession
    {
        public string Task { get; set; }
        public long StartedUtcTicks { get; set; }
        public long FinishedUtcTicks { get; set; }

        [ScriptIgnore]
        public DateTime StartedUtc { get { return new DateTime(StartedUtcTicks, DateTimeKind.Utc); } }
        [ScriptIgnore]
        public DateTime? FinishedUtc
        {
            get { return FinishedUtcTicks == 0 ? (DateTime?)null : new DateTime(FinishedUtcTicks, DateTimeKind.Utc); }
        }
    }

    internal sealed class FocusHistory
    {
        private readonly string path;
        private readonly List<FocusSession> sessions;

        private FocusHistory(string filePath, List<FocusSession> entries)
        {
            path = filePath;
            sessions = entries;
        }

        public IList<FocusSession> Sessions { get { return sessions.AsReadOnly(); } }

        public static string DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NowAndDoing", "focus-history.json"); }
        }

        public static FocusHistory Load(string filePath)
        {
            if (!File.Exists(filePath)) return new FocusHistory(filePath, new List<FocusSession>());
            List<FocusSession> entries = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.Deserialize<List<FocusSession>>(File.ReadAllText(filePath));
            if (entries == null) throw new InvalidDataException("The focus history file is empty or invalid.");
            foreach (FocusSession entry in entries)
            {
                if (entry == null || String.IsNullOrWhiteSpace(entry.Task) ||
                    entry.StartedUtcTicks < DateTime.MinValue.Ticks || entry.StartedUtcTicks > DateTime.MaxValue.Ticks ||
                    (entry.FinishedUtcTicks != 0 &&
                    (entry.FinishedUtcTicks < entry.StartedUtcTicks || entry.FinishedUtcTicks > DateTime.MaxValue.Ticks)))
                    throw new InvalidDataException("The focus history file contains an invalid session.");
            }
            return new FocusHistory(filePath, entries);
        }

        public static FocusHistory Empty(string filePath)
        {
            return new FocusHistory(filePath, new List<FocusSession>());
        }

        public FocusSession Start(string task, DateTime startedUtc)
        {
            FocusSession session = new FocusSession { Task = task, StartedUtcTicks = startedUtc.Ticks };
            sessions.Add(session);
            return session;
        }

        public void Finish(FocusSession session, DateTime finishedUtc)
        {
            if (session == null || session.FinishedUtcTicks != 0) return;
            session.FinishedUtcTicks = Math.Max(session.StartedUtcTicks, finishedUtc.Ticks);
        }

        public void Save()
        {
            // save the log without replacing the old one until the new one is ready
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            string json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.Serialize(sessions);
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
