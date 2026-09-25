using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using MediaPlayer = System.Windows.Media.MediaPlayer;

namespace NowAndDoing
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ReferenceForm());
        }
    }

    internal sealed class Settings
    {
        public string AppId { get; set; }
        public List<string> Files { get; set; }
        public int Volume { get; set; }
        public bool Shuffle { get; set; }

        public Settings()
        {
            AppId = "";
            Files = new List<string>();
            Volume = 75;
        }

        private static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NowAndDoing", "settings.json"); }
        }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    Settings value = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath));
                    if (value != null)
                    {
                        if (value.Files == null) value.Files = new List<string>();
                        value.Files = value.Files.Where(File.Exists).ToList();
                        value.Volume = Math.Max(0, Math.Min(100, value.Volume));
                        return value;
                    }
                }
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this));
            }
            catch { }
        }
    }

    internal sealed class Track
    {
        public string PathName;
        public string Title;
        public string Artist;

        public static Track Read(string path)
        {
            // use the file name so discord shows what i saved the song as
            return new Track { PathName = path, Title = Path.GetFileNameWithoutExtension(path), Artist = "" };
        }
    }

    internal static class MusicLibrary
    {
        private static readonly HashSet<string> AudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".m4a", ".aac", ".wav", ".wma" };

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string first, string second);

        public static string FolderPath
        {
            // this is the music folder on the desktop
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "music"); }
        }

        public static List<string> FindFiles()
        {
            return FindFiles(FolderPath);
        }

        internal static List<string> FindFiles(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return new List<string>();
                List<string> files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(path => AudioExtensions.Contains(Path.GetExtension(path))).ToList();
                files.Sort(ComparePaths);
                return files;
            }
            catch (IOException) { return new List<string>(); }
            catch (UnauthorizedAccessException) { return new List<string>(); }
        }

        internal static int ComparePaths(string first, string second)
        {
            int order = StrCmpLogicalW(Path.GetFileName(first), Path.GetFileName(second));
            return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(first, second);
        }

        internal static void Shuffle(List<Track> items, Random random)
        {
            // shuffle the order and make sure it actually changes
            List<string> original = items.Select(track => track.PathName).ToList();
            for (int i = items.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                Track swap = items[i]; items[i] = items[other]; items[other] = swap;
            }
            if (items.Count > 1 && items.Select(track => track.PathName).SequenceEqual(original))
            {
                Track swap = items[items.Count - 1];
                items[items.Count - 1] = items[items.Count - 2];
                items[items.Count - 2] = swap;
            }
        }
    }

    internal sealed class PresenceState
    {
        public string Task;
        public string Title;
        public bool Playing;
        public long FocusStart;
    }

    internal sealed class DiscordRpc : IDisposable
    {
        private readonly object gate = new object();
        private readonly AutoResetEvent changed = new AutoResetEvent(false);
        private readonly Thread worker;
        private readonly Action<string> report;
        private string appId = "";
        private PresenceState presence = new PresenceState();
        private bool dirty = true;
        private bool stopping;
        private NamedPipeClientStream pipe;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly string pipePrefix;

        public DiscordRpc(Action<string> reportStatus) : this(reportStatus, "discord-ipc-") { }

        internal DiscordRpc(Action<string> reportStatus, string prefix)
        {
            report = reportStatus;
            pipePrefix = prefix;
            worker = new Thread(Run);
            worker.IsBackground = true;
            worker.Name = "Discord Rich Presence";
            worker.Start();
        }

        public void Set(string id, PresenceState state)
        {
            lock (gate)
            {
                appId = id == null ? "" : id.Trim();
                presence = state;
                dirty = true;
            }
            changed.Set();
        }

        private void Run()
        {
            string connectedId = "";
            DateTime lastSent = DateTime.MinValue;
            while (true)
            {
                string id;
                PresenceState state;
                bool send;
                lock (gate)
                {
                    if (stopping) break;
                    id = appId;
                    state = presence;
                    send = dirty;
                }
                if (!IsValidId(id))
                {
                    Disconnect();
                    connectedId = "";
                    report("Add a Discord app ID in settings");
                    changed.WaitOne(10000);
                    continue;
                }
                if (pipe != null && connectedId != id)
                {
                    try { SendActivity(null); } catch { }
                    Disconnect();
                }
                if (pipe == null)
                {
                    try
                    {
                        pipe = Connect(id);
                        connectedId = id;
                        lastSent = DateTime.MinValue;
                        lock (gate) dirty = true;
                    }
                    catch (Exception error)
                    {
                        Disconnect();
                        report(error.Message);
                        changed.WaitOne(5000);
                        continue;
                    }
                }
                if (send || (DateTime.UtcNow - lastSent).TotalSeconds >= 30)
                {
                    lock (gate)
                    {
                        state = presence;
                        dirty = false;
                    }
                    try
                    {
                        SendActivity(state);
                        string reply = ReadFrame(pipe);
                        Dictionary<string, object> parsed = json.Deserialize<Dictionary<string, object>>(reply);
                        if (parsed.ContainsKey("evt") && String.Equals(Convert.ToString(parsed["evt"]), "ERROR", StringComparison.OrdinalIgnoreCase))
                        {
                            string error = "Discord rejected the activity";
                            if (parsed.ContainsKey("data"))
                            {
                                Dictionary<string, object> data = parsed["data"] as Dictionary<string, object>;
                                if (data != null && data.ContainsKey("message")) error = Convert.ToString(data["message"]);
                            }
                            report(error);
                        }
                        else report("Connected to Discord");
                        lastSent = DateTime.UtcNow;
                    }
                    catch
                    {
                        Disconnect();
                        report("Discord connection lost — retrying");
                        changed.WaitOne(2500);
                        continue;
                    }
                }
                changed.WaitOne(2000);
            }
            try { if (pipe != null) SendActivity(null); } catch { }
            Disconnect();
        }

        public static bool IsValidId(string id)
        {
            if (String.IsNullOrWhiteSpace(id) || id.Length < 17 || id.Length > 21) return false;
            return id.All(Char.IsDigit);
        }

        private NamedPipeClientStream Connect(string id)
        {
            string handshakeError = null;
            for (int i = 0; i < 10; i++)
            {
                NamedPipeClientStream candidate = new NamedPipeClientStream(".", pipePrefix + i, PipeDirection.InOut, PipeOptions.None);
                try
                {
                    candidate.Connect(100);
                    WriteFrame(candidate, 0, json.Serialize(new Dictionary<string, object> { { "v", 1 }, { "client_id", id } }));
                    string ready = ReadFrame(candidate);
                    Dictionary<string, object> parsed = json.Deserialize<Dictionary<string, object>>(ready);
                    if (!parsed.ContainsKey("evt") || Convert.ToString(parsed["evt"]) != "READY") throw new IOException("Check the Discord Application ID");
                    return candidate;
                }
                catch (Exception error)
                {
                    if (candidate.IsConnected) handshakeError = error.Message;
                    candidate.Dispose();
                }
            }
            throw new IOException(handshakeError ?? "Waiting for Discord desktop");
        }

        private void SendActivity(PresenceState state)
        {
            // send the task and the song to discord
            object activity = null;
            if (state != null)
            {
                string task = (state.Task ?? "").Trim();
                string title = (state.Title ?? "").Trim();
                if (task.Length > 0 || (state.Playing && title.Length > 0))
                {
                    Dictionary<string, object> entry = new Dictionary<string, object>();
                    entry["type"] = 0;
                    entry["name"] = StudyName(task);
                    if (state.Playing && title.Length > 0)
                        entry["details"] = Shorten("listening to " + title, 128);
                    if (task.Length > 0 && state.FocusStart > 0)
                        entry["timestamps"] = new Dictionary<string, long> { { "start", state.FocusStart } };
                    entry["assets"] = new Dictionary<string, string> { { "large_image", "studying-photo" }, { "large_text", "studying" } };
                    activity = entry;
                }
            }
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["cmd"] = "SET_ACTIVITY";
            payload["args"] = new Dictionary<string, object> { { "pid", Process.GetCurrentProcess().Id }, { "activity", activity } };
            payload["nonce"] = Guid.NewGuid().ToString();
            WriteFrame(pipe, 1, json.Serialize(payload));
        }

        private static string Shorten(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }

        internal static string StudyName(string task)
        {
            if (String.IsNullOrWhiteSpace(task)) return "studying";
            string subject = task.Trim();
            if (subject.StartsWith("Studying ", StringComparison.OrdinalIgnoreCase)) subject = subject.Substring(9).Trim();
            else if (subject.StartsWith("Study ", StringComparison.OrdinalIgnoreCase)) subject = subject.Substring(6).Trim();
            return Shorten(subject.Length == 0 ? "studying" : "studying " + subject, 128);
        }

        private static void WriteFrame(Stream stream, int opcode, string payload)
        {
            byte[] content = Encoding.UTF8.GetBytes(payload);
            byte[] head = new byte[8];
            Array.Copy(BitConverter.GetBytes(opcode), 0, head, 0, 4);
            Array.Copy(BitConverter.GetBytes(content.Length), 0, head, 4, 4);
            stream.Write(head, 0, 8);
            stream.Write(content, 0, content.Length);
            stream.Flush();
        }

        private static string ReadFrame(Stream stream)
        {
            byte[] head = new byte[8];
            ReadExact(stream, head, 0, 8);
            int opcode = BitConverter.ToInt32(head, 0);
            int count = BitConverter.ToInt32(head, 4);
            if (count < 0 || count > 1024 * 1024) throw new IOException("Unexpected Discord frame");
            byte[] body = new byte[count];
            ReadExact(stream, body, 0, count);
            if (opcode == 3)
            {
                byte[] pong = new byte[8];
                Array.Copy(BitConverter.GetBytes(4), 0, pong, 0, 4);
                Array.Copy(BitConverter.GetBytes(count), 0, pong, 4, 4);
                stream.Write(pong, 0, pong.Length);
                stream.Write(body, 0, body.Length);
                stream.Flush();
                return ReadFrame(stream);
            }
            if (opcode == 2) throw new IOException("Check the Discord Application ID");
            if (opcode != 1) throw new IOException("Unexpected Discord frame");
            return Encoding.UTF8.GetString(body);
        }

        private static void ReadExact(Stream stream, byte[] bytes, int start, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(bytes, start, count);
                if (read <= 0) throw new EndOfStreamException();
                start += read;
                count -= read;
            }
        }

        private void Disconnect()
        {
            if (pipe != null)
            {
                try { pipe.Dispose(); } catch { }
                pipe = null;
            }
        }

        public void Dispose()
        {
            lock (gate) stopping = true;
            changed.Set();
            worker.Join(800);
        }
    }

}
