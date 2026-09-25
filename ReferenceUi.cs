using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MediaPlayer = System.Windows.Media.MediaPlayer;

namespace NowAndDoing
{
    internal sealed class ReferenceForm : Form
    {
        private static readonly Color Back = Color.FromArgb(24, 26, 29);
        private static readonly Color CardTop = Color.FromArgb(35, 38, 42);
        private static readonly Color CardBottom = Color.FromArgb(32, 35, 39);
        private static readonly Color Line = Color.FromArgb(48, 51, 54);
        private static readonly Color White = Color.FromArgb(233, 235, 231);
        private static readonly Color Muted = Color.FromArgb(149, 157, 160);
        private static readonly Color Green = Color.FromArgb(149, 195, 173);
        private readonly Settings settings;
        private readonly List<Track> tracks = new List<Track>();
        private readonly Random shuffleRandom = new Random();
        private readonly MediaPlayer player = new MediaPlayer();
        private readonly DiscordRpc rpc;
        private readonly System.Windows.Forms.Timer tick;
        private readonly System.Windows.Forms.Timer libraryRefresh;
        private FileSystemWatcher musicWatcher;
        private readonly TextBox taskEditor;
        private readonly Font brandFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        private readonly Font sectionFont = new Font("Segoe UI Semibold", 7.7f, FontStyle.Regular);
        private readonly Font taskFont = new Font("Segoe UI", 14f, FontStyle.Bold);
        private readonly Font regularFont = new Font("Segoe UI Semibold", 8.5f, FontStyle.Regular);
        private readonly Font smallFont = new Font("Segoe UI Semibold", 7.4f, FontStyle.Regular);
        private readonly Font boldFont = new Font("Segoe UI", 9.2f, FontStyle.Bold);
        private readonly Image logo;
        private string taskDraft = "";
        private string discordStatus = "Connecting…";
        private bool focusActive;
        private DateTime focusStart;
        private bool playing;
        private int current = -1;
        private TimeSpan duration = TimeSpan.Zero;
        private bool draggingProgress;
        private bool draggingVolume;
        private bool closing;
        private double dragProgress;
#if VISUAL_QA
        private TimeSpan? previewPosition;
#endif

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private struct UiLayout
        {
            public int Width, Height, HeaderLine, FocusLabel, FocusCard, FocusHeight;
            public int MusicLabel, MusicCard, MusicHeight, Footer;
            public bool Active, FocusStarted;

            public Rectangle FocusRect { get { return Active ? new Rectangle(18, FocusCard, 396, FocusHeight) : new Rectangle(16, FocusCard, 376, FocusHeight); } }
            public Rectangle MusicRect { get { return Active ? new Rectangle(18, MusicCard, 396, MusicHeight) : new Rectangle(16, MusicCard, 376, MusicHeight); } }
            public Rectangle FocusButton { get { return Active ? (FocusStarted ? new Rectangle(305, FocusCard + 58, 91, 33) : new Rectangle(284, FocusCard + 58, 112, 33)) : new Rectangle(263, FocusCard + 63, 109, 32); } }
            public Rectangle ShuffleButton { get { return new Rectangle(Width - 105, MusicLabel - 8, 90, 24); } }
            public Rectangle PlayButton { get { return new Rectangle(Width / 2 - 20, MusicCard + 110, 40, 40); } }
            public Rectangle PrevButton { get { return new Rectangle(Width / 2 - 65, MusicCard + 111, 32, 38); } }
            public Rectangle NextButton { get { return new Rectangle(Width / 2 + 33, MusicCard + 111, 32, 38); } }
            public Rectangle ExitButton { get { return new Rectangle(Width - 50, 13, 29, 29); } }
            public Rectangle FooterButton { get { return Active ? new Rectangle(348, Footer + 16, 66, 26) : new Rectangle(329, Footer + 19, 63, 25); } }
            public Rectangle TrackCount { get { return new Rectangle(Width - 91, MusicCard + 12, 55, 24); } }
            public Rectangle MusicProgress { get { return Active ? new Rectangle(35, MusicCard + 77, 363, 4) : new Rectangle(38, MusicCard + 79, 335, 4); } }
            public Rectangle VolumeProgress { get { return Active ? new Rectangle(346, MusicCard + 126, 52, 4) : new Rectangle(325, MusicCard + 129, 49, 4); } }
        }

        private UiLayout CurrentLayout()
        {
            bool active = focusActive || tracks.Count > 0;
            if (active)
                return new UiLayout { Width = 436, Height = 514, HeaderLine = 58, FocusLabel = 77, FocusCard = 103, FocusHeight = 112, MusicLabel = 242, MusicCard = 274, MusicHeight = 160, Footer = 454, Active = true, FocusStarted = focusActive };
            return new UiLayout { Width = 408, Height = 536, HeaderLine = 60, FocusLabel = 82, FocusCard = 109, FocusHeight = 117, MusicLabel = 259, MusicCard = 291, MusicHeight = 164, Footer = 477, Active = false, FocusStarted = focusActive };
        }

        public ReferenceForm()
        {
            settings = Settings.Load();
            Text = "now / doing";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Back;
            ForeColor = White;
            KeyPreview = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("StudyingPhoto"))
            {
                if (stream != null) { using (Image image = Image.FromStream(stream)) logo = new Bitmap(image); }
            }
            UiLayout layout = CurrentLayout();
            ClientSize = new Size(layout.Width, layout.Height);
            UpdateWindowRegion();

            taskEditor = new TextBox();
            taskEditor.BorderStyle = BorderStyle.None;
            taskEditor.BackColor = CardTop;
            taskEditor.ForeColor = White;
            taskEditor.Font = taskFont;
            taskEditor.Visible = false;
            taskEditor.TabStop = false;
            taskEditor.KeyDown += TaskEditorKeyDown;
            taskEditor.LostFocus += delegate { CommitTaskEditor(); };
            Controls.Add(taskEditor);

            foreach (string path in MusicLibrary.FindFiles()) tracks.Add(Track.Read(path));
            if (settings.Shuffle) ShuffleTracks(tracks);
            if (tracks.Count > 0) current = 0;
            RefreshSize();
            ApplyVolume();
            player.MediaOpened += delegate { ApplyVolume(); duration = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan : TimeSpan.Zero; UpdatePresence(); Invalidate(); };
            player.MediaEnded += delegate { MediaEnded(); };
            player.MediaFailed += (sender, args) => { MediaFailed(args.ErrorException == null ? "This audio file could not be played." : args.ErrorException.Message); };
            rpc = new DiscordRpc(SetDiscordStatus);
            rpc.Set(new PresenceState());
            tick = new System.Windows.Forms.Timer();
            tick.Interval = 500;
            tick.Tick += delegate { Invalidate(); };
            tick.Start();
            libraryRefresh = new System.Windows.Forms.Timer();
            libraryRefresh.Interval = 700;
            libraryRefresh.Tick += delegate { libraryRefresh.Stop(); RefreshLibrary(); };
            StartMusicWatcher();
            FormClosing += delegate
            {
                closing = true;
                tick.Stop();
                if (musicWatcher != null) musicWatcher.Dispose();
                libraryRefresh.Stop();
                libraryRefresh.Dispose();
                player.Close();
                rpc.Dispose();
                if (logo != null) logo.Dispose();
                brandFont.Dispose(); sectionFont.Dispose(); taskFont.Dispose(); regularFont.Dispose(); smallFont.Dispose(); boldFont.Dispose();
            };
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ClassStyle |= 0x20000; return p; }
        }

        private static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float size = radius * 2;
            path.AddArc(rect.X, rect.Y, size, size, 180, 90);
            path.AddArc(rect.Right - size, rect.Y, size, size, 270, 90);
            path.AddArc(rect.Right - size, rect.Bottom - size, size, size, 0, 90);
            path.AddArc(rect.X, rect.Bottom - size, size, size, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void UpdateWindowRegion()
        {
            if (Region != null) Region.Dispose();
            using (GraphicsPath path = Rounded(new RectangleF(0, 0, Width, Height), 16)) Region = new Region(path);
        }

        private void RefreshSize()
        {
            UiLayout layout = CurrentLayout();
            if (ClientSize != new Size(layout.Width, layout.Height))
            {
                int dx = (layout.Width - ClientSize.Width) / 2;
                int dy = (layout.Height - ClientSize.Height) / 2;
                Location = new Point(Left - dx, Top - dy);
                ClientSize = new Size(layout.Width, layout.Height);
                UpdateWindowRegion();
            }
            taskEditor.Location = new Point(36, layout.FocusCard + (layout.Active ? 10 : 12));
            taskEditor.Size = new Size(layout.Active ? 350 : 330, 34);
            Invalidate();
        }

        private static void FillRounded(Graphics g, Rectangle rect, float radius, Color top, Color bottom, Color border)
        {
            using (GraphicsPath path = Rounded(new RectangleF(rect.X + .5f, rect.Y + .5f, rect.Width - 1, rect.Height - 1), radius))
            using (LinearGradientBrush fill = new LinearGradientBrush(rect, top, bottom, LinearGradientMode.Vertical))
            using (Pen line = new Pen(border, 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(line, path);
            }
        }

        private static void DrawText(Graphics g, string value, Font font, Color color, RectangleF area, StringAlignment alignment)
        {
            TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis |
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            if (alignment == StringAlignment.Far) flags |= TextFormatFlags.Right;
            else if (alignment == StringAlignment.Center) flags |= TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(g, value, font, Rectangle.Round(area), color, flags);
        }

        private void Label(Graphics g, string value, Font font, Color color, float x, float y, float width, float height)
        {
            DrawText(g, value, font, color, new RectangleF(x, y, width, height), StringAlignment.Near);
        }

        private void RightLabel(Graphics g, string value, Font font, Color color, float x, float y, float width, float height)
        {
            DrawText(g, value, font, color, new RectangleF(x, y, width, height), StringAlignment.Far);
        }

        private void TrackedLabel(Graphics g, string value, float x, float y)
        {
            float cursor = x;
            foreach (char c in value)
            {
                if (c == ' ') { cursor += 8f; continue; }
                string s = c.ToString();
                Label(g, s, sectionFont, Muted, cursor, y, 14, 15);
                cursor += g.MeasureString(s, sectionFont, PointF.Empty, StringFormat.GenericTypographic).Width + 1.05f;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            UiLayout layout = CurrentLayout();
            g.Clear(Back);
            using (Pen border = new Pen(Color.FromArgb(48, 51, 54), 1f))
            using (Pen divider = new Pen(Line, 1f))
            {
                g.DrawRectangle(border, .5f, .5f, layout.Width - 1, layout.Height - 1);
                g.DrawLine(divider, 0, layout.HeaderLine, layout.Width, layout.HeaderLine);
                g.DrawLine(divider, 0, layout.Footer, layout.Width, layout.Footer);
            }
            DrawHeader(g, layout);
            DrawFocus(g, layout);
            DrawMusic(g, layout);
            DrawFooter(g, layout);
        }

        private void DrawHeader(Graphics g, UiLayout layout)
        {
            Rectangle iconRect = new Rectangle(18, 16, 23, 23);
            if (logo != null)
            {
                GraphicsState saved = g.Save();
                using (GraphicsPath clip = Rounded(iconRect, 6)) g.SetClip(clip);
                g.DrawImage(logo, iconRect);
                g.Restore(saved);
            }
            using (Pen outline = new Pen(Color.FromArgb(177, 183, 180), 1f))
            using (GraphicsPath border = Rounded(new RectangleF(iconRect.X + .5f, iconRect.Y + .5f, iconRect.Width - 1, iconRect.Height - 1), 6)) g.DrawPath(outline, border);
            Label(g, "now", brandFont, White, 49, 18, 29, 22);
            Label(g, "/", brandFont, Muted, 78, 18, 10, 22);
            Label(g, "doing", brandFont, White, 85, 18, 50, 22);
            float cx = layout.ExitButton.X + layout.ExitButton.Width / 2f;
            float cy = layout.ExitButton.Y + layout.ExitButton.Height / 2f;
            using (Pen pen = new Pen(Muted, 1.5f))
            {
                g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
            }
        }

        private void DrawFocus(Graphics g, UiLayout layout)
        {
            TrackedLabel(g, "CURRENT FOCUS", layout.Active ? 18 : 16, layout.FocusLabel);
            string elapsed = focusActive ? FormatTime(DateTime.UtcNow - focusStart) : "00:00";
            RightLabel(g, elapsed, smallFont, Muted, layout.Width - 76, layout.FocusLabel - 1, 58, 18);
            FillRounded(g, layout.FocusRect, 15, CardTop, CardBottom, Color.FromArgb(53, 57, 60));
            if (!taskEditor.Visible)
                Label(g, taskDraft.Length == 0 ? "What are you working on?" : taskDraft, taskFont,
                    taskDraft.Length == 0 ? Color.FromArgb(166, 174, 176) : Color.White,
                    36, layout.FocusCard + (layout.Active ? 9 : 11), layout.Width - 72, 36);
            using (SolidBrush dot = new SolidBrush(focusActive ? Green : Muted))
                g.FillEllipse(dot, 36, layout.FocusCard + (layout.Active ? 72 : 75), 6, 6);
            Label(g, focusActive ? "Focus in progress" : "One thing at a time", regularFont, Muted,
                49, layout.FocusCard + (layout.Active ? 65 : 68), 200, 22);
            Rectangle action = layout.FocusButton;
            FillRounded(g, action, 9, Color.FromArgb(247, 249, 246), Color.FromArgb(241, 244, 241), Color.FromArgb(240, 243, 241));
            Label(g, focusActive ? "Finish" : "Start focus", boldFont, Color.FromArgb(23, 26, 28),
                action.X + (focusActive ? 19 : 12), action.Y + 5, action.Width - (focusActive ? 30 : 37), 23);
            using (Pen arrow = new Pen(Color.FromArgb(42, 47, 49), 1.5f))
            {
                float x = action.Right - 18, y = action.Y + action.Height / 2f;
                g.DrawLine(arrow, x - 4, y, x + 3, y);
                g.DrawLine(arrow, x, y - 3, x + 3, y);
                g.DrawLine(arrow, x, y + 3, x + 3, y);
            }
        }

        private void DrawMusic(Graphics g, UiLayout layout)
        {
            TrackedLabel(g, "LOCAL MUSIC", layout.Active ? 18 : 16, layout.MusicLabel);
            int shuffleX = layout.Width - 93;
            Color shuffleColor = settings.Shuffle ? Green : White;
            using (Pen arrows = new Pen(shuffleColor, 1.25f))
            {
                float y = layout.MusicLabel + 7;
                g.DrawLine(arrows, shuffleX, y - 4, shuffleX + 3, y - 4);
                g.DrawLine(arrows, shuffleX + 3, y - 4, shuffleX + 10, y + 4);
                g.DrawLine(arrows, shuffleX, y + 4, shuffleX + 3, y + 4);
                g.DrawLine(arrows, shuffleX + 3, y + 4, shuffleX + 10, y - 4);
                g.DrawLine(arrows, shuffleX + 8, y - 6, shuffleX + 10, y - 4);
                g.DrawLine(arrows, shuffleX + 8, y - 2, shuffleX + 10, y - 4);
                g.DrawLine(arrows, shuffleX + 8, y + 2, shuffleX + 10, y + 4);
                g.DrawLine(arrows, shuffleX + 8, y + 6, shuffleX + 10, y + 4);
            }
            Label(g, "Shuffle", regularFont, shuffleColor, shuffleX + 15, layout.MusicLabel - 1, 62, 20);
            Rectangle card = layout.MusicRect;
            FillRounded(g, card, 15, CardTop, CardBottom, Color.FromArgb(53, 57, 60));
            Rectangle art = layout.Active ? new Rectangle(34, card.Y + 14, 47, 48) : new Rectangle(34, card.Y + 16, 45, 46);
            FillRounded(g, art, 10, Color.FromArgb(61, 68, 72), Color.FromArgb(45, 51, 55), Color.FromArgb(72, 81, 85));
            using (Pen bars = new Pen(Color.FromArgb(180, 188, 187), 1.5f))
            {
                float center = art.X + art.Width / 2f, middle = art.Y + art.Height / 2f;
                g.DrawLine(bars, center - 6, middle - 3, center - 6, middle + 3);
                g.DrawLine(bars, center - 2, middle - 6, center - 2, middle + 6);
                g.DrawLine(bars, center + 2, middle - 4, center + 2, middle + 4);
                g.DrawLine(bars, center + 6, middle - 2, center + 6, middle + 2);
            }
            string title = current >= 0 && current < tracks.Count ? tracks[current].Title : "No song selected";
            string subtitle = current >= 0 && current < tracks.Count ? "Desktop\\music · " + Path.GetExtension(tracks[current].PathName).TrimStart('.').ToUpperInvariant() :
                Directory.Exists(MusicLibrary.FolderPath) ? "No audio files in Desktop\\music" : "Desktop\\music folder not found";
            Label(g, title, boldFont, White, 92, card.Y + (layout.Active ? 17 : 18), layout.TrackCount.X - 101, 22);
            Label(g, subtitle, regularFont, Muted, 92, card.Y + (layout.Active ? 40 : 42), layout.Width - 178, 20);
            string number = tracks.Count == 0 ? "— / —" : String.Format("{0:00} / {1:00}", current + 1, tracks.Count);
            RightLabel(g, number, smallFont, Muted, layout.TrackCount.X, layout.TrackCount.Y, layout.TrackCount.Width, layout.TrackCount.Height);
            Rectangle bar = layout.MusicProgress;
#if VISUAL_QA
            TimeSpan position = previewPosition.HasValue ? previewPosition.Value : player.Position;
#else
            TimeSpan position = player.Position;
#endif
            using (Pen line = new Pen(Color.FromArgb(67, 73, 76), 2.5f))
            using (Pen played = new Pen(Color.FromArgb(214, 218, 216), 2.5f))
            using (Pen thumbEdge = new Pen(Color.FromArgb(155, 165, 166), 1.4f))
            using (SolidBrush thumbFill = new SolidBrush(CardBottom))
            {
                g.DrawLine(line, bar.Left, bar.Y, bar.Right, bar.Y);
                double fraction = draggingProgress ? dragProgress : duration.TotalSeconds > 0 ? position.TotalSeconds / duration.TotalSeconds : 0;
                fraction = Math.Max(0, Math.Min(1, fraction));
                float x = bar.Left + (float)(fraction * bar.Width);
                if (fraction > 0) g.DrawLine(played, bar.Left, bar.Y, x, bar.Y);
                g.FillEllipse(thumbFill, x - 5, bar.Y - 5, 10, 10);
                g.DrawEllipse(thumbEdge, x - 5, bar.Y - 5, 10, 10);
                g.FillEllipse(played.Brush, x - 1.5f, bar.Y - 1.5f, 3, 3);
            }
            string elapsed = current >= 0 ? FormatTime(position) : "0:00";
            string total = duration.TotalSeconds > 0 ? FormatTime(duration) : "0:00";
            Label(g, elapsed, smallFont, Muted, bar.X, bar.Y + 10, 80, 18);
            RightLabel(g, total, smallFont, Muted, bar.Right - 80, bar.Y + 10, 80, 18);
            DrawPlaybackControls(g, layout);
        }

        private void DrawPlaybackControls(Graphics g, UiLayout layout)
        {
            int center = layout.Width / 2;
            int cy = layout.Active ? layout.MusicCard + 128 : layout.MusicCard + 132;
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(120, 128, 130)))
            using (SolidBrush button = new SolidBrush(playing ? Color.FromArgb(247, 249, 246) : Color.FromArgb(170, 175, 175)))
            using (SolidBrush ink = new SolidBrush(Color.FromArgb(31, 35, 37)))
            {
                // keep these buttons small like the rest of the player
                g.FillRectangle(dim, center - 49, cy - 5, 2, 10);
                g.FillPolygon(dim, new PointF[] { new PointF(center - 47, cy), new PointF(center - 41, cy - 5), new PointF(center - 41, cy + 5) });
                g.FillRectangle(dim, center + 47, cy - 5, 2, 10);
                g.FillPolygon(dim, new PointF[] { new PointF(center + 47, cy), new PointF(center + 41, cy - 5), new PointF(center + 41, cy + 5) });
                g.FillEllipse(button, center - 18, cy - 18, 36, 36);
                if (playing)
                {
                    g.FillRectangle(ink, center - 5, cy - 5, 3, 10);
                    g.FillRectangle(ink, center + 2, cy - 5, 3, 10);
                }
                else g.FillPolygon(ink, new PointF[] { new PointF(center - 3, cy - 6), new PointF(center + 5, cy), new PointF(center - 3, cy + 6) });
            }
            Rectangle volume = layout.VolumeProgress;
            float speakerX = volume.X - 19, speakerY = volume.Y;
            using (Pen pen = new Pen(Muted, 1.2f))
            using (SolidBrush brush = new SolidBrush(Muted))
            {
                g.FillPolygon(brush, new PointF[] { new PointF(speakerX, speakerY - 2), new PointF(speakerX + 3, speakerY - 2), new PointF(speakerX + 6, speakerY - 5), new PointF(speakerX + 6, speakerY + 5), new PointF(speakerX + 3, speakerY + 2), new PointF(speakerX, speakerY + 2) });
                g.DrawArc(pen, speakerX + 3, speakerY - 4, 8, 8, -55, 110);
                g.DrawLine(pen, volume.Left, volume.Y, volume.Right, volume.Y);
                float knob = volume.Left + volume.Width * (settings.Volume / 100f);
                g.DrawEllipse(pen, knob - 4, volume.Y - 4, 8, 8);
                g.FillEllipse(brush, knob - 1.5f, volume.Y - 1.5f, 3, 3);
            }
        }

        private void DrawFooter(Graphics g, UiLayout layout)
        {
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(25, 27, 30)))
                g.FillRectangle(fill, 1, layout.Footer + 1, layout.Width - 2, layout.Height - layout.Footer - 2);
            Rectangle mark = layout.Active ? new Rectangle(18, layout.Footer + 17, 23, 23) : new Rectangle(17, layout.Footer + 19, 23, 23);
            FillRounded(g, mark, 6, Color.FromArgb(43, 47, 51), Color.FromArgb(39, 43, 46), Color.FromArgb(43, 47, 50));
            using (SolidBrush eyes = new SolidBrush(Color.FromArgb(211, 217, 213)))
            using (Pen mouth = new Pen(Color.FromArgb(211, 217, 213), 1.3f))
            {
                g.FillEllipse(eyes, mark.X + 6, mark.Y + 8, 3, 3);
                g.FillEllipse(eyes, mark.X + 14, mark.Y + 8, 3, 3);
                g.DrawArc(mouth, mark.X + 6, mark.Y + 9, 11, 6, 5, 170);
            }
            Label(g, "Discord activity", boldFont, White, 50, layout.Footer + (layout.Active ? 12 : 14), 180, 18);
            string sub = focusActive && playing ? "Sharing task + current song" : focusActive ? "Sharing current task" : playing ? "Sharing current song" : "Task + song status";
            Label(g, sub, smallFont, Muted, 50, layout.Footer + (layout.Active ? 29 : 31), 220, 17);
            Rectangle pill = layout.FooterButton;
            using (GraphicsPath shape = Rounded(pill, pill.Height / 2f))
            using (Pen edge = new Pen(Color.FromArgb(58, 62, 66), 1f)) g.DrawPath(edge, shape);
            string pillText = !layout.Active ? "UI preview" : discordStatus == "Connected to Discord" ? "Connected" : "Connect";
            DrawText(g, pillText, smallFont, Muted, pill, StringAlignment.Center);
        }

        private static string FormatTime(TimeSpan value)
        {
            if (value.TotalSeconds < 0) value = TimeSpan.Zero;
            return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
        }

        private void BeginTaskEditor()
        {
            taskEditor.Text = taskDraft;
            taskEditor.Visible = true;
            taskEditor.Focus();
            taskEditor.SelectionStart = taskEditor.TextLength;
            Invalidate();
        }

        private void CommitTaskEditor()
        {
            if (!taskEditor.Visible) return;
            taskDraft = taskEditor.Text.Trim();
            taskEditor.Visible = false;
            if (focusActive && taskDraft.Length == 0) focusActive = false;
            RefreshSize();
            UpdatePresence();
        }

        private void TaskEditorKeyDown(object sender, KeyEventArgs args)
        {
            if (args.KeyCode == Keys.Enter) { CommitTaskEditor(); args.SuppressKeyPress = true; }
            else if (args.KeyCode == Keys.Escape) { taskEditor.Visible = false; Invalidate(); args.SuppressKeyPress = true; }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            UiLayout layout = CurrentLayout();
            if (e.Button != MouseButtons.Left) return;
            if (e.Y < layout.HeaderLine && !layout.ExitButton.Contains(e.Location))
            {
                ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0);
                return;
            }
            Rectangle bar = layout.MusicProgress;
            if (new Rectangle(bar.X - 6, bar.Y - 10, bar.Width + 12, 22).Contains(e.Location) && current >= 0 && duration.TotalSeconds > 0)
            {
                draggingProgress = true;
                dragProgress = Math.Max(0, Math.Min(1, (e.X - bar.Left) / (double)bar.Width));
                Invalidate();
            }
            Rectangle volume = layout.VolumeProgress;
            if (new Rectangle(volume.X - 8, volume.Y - 12, volume.Width + 16, 24).Contains(e.Location))
            {
                draggingVolume = true;
                SetVolume(e.X, volume);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UiLayout layout = CurrentLayout();
            if (draggingProgress)
            {
                Rectangle bar = layout.MusicProgress;
                dragProgress = Math.Max(0, Math.Min(1, (e.X - bar.Left) / (double)bar.Width));
                Invalidate();
            }
            else if (draggingVolume) SetVolume(e.X, layout.VolumeProgress);
            else
            {
                Point p = e.Location;
                bool clickable = layout.FocusButton.Contains(p) || layout.ShuffleButton.Contains(p) || layout.PlayButton.Contains(p) || layout.PrevButton.Contains(p) || layout.NextButton.Contains(p) || layout.ExitButton.Contains(p) || layout.FooterButton.Contains(p) || layout.TrackCount.Contains(p) || new Rectangle(32, layout.FocusCard + 17, layout.Width - 64, 42).Contains(p);
                Cursor = clickable ? Cursors.Hand : Cursors.Default;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            UiLayout layout = CurrentLayout();
            if (draggingProgress)
            {
                draggingProgress = false;
                player.Position = TimeSpan.FromSeconds(duration.TotalSeconds * dragProgress);
                UpdatePresence();
                Invalidate();
                return;
            }
            if (draggingVolume) { draggingVolume = false; settings.Save(); return; }
            if (e.Button == MouseButtons.Right && e.Y >= layout.Footer) { ShowInfoMenu(); return; }
            if (e.Button != MouseButtons.Left) return;
            Point p = e.Location;
            if (layout.ExitButton.Contains(p)) { Close(); return; }
            if (layout.FocusButton.Contains(p)) { ToggleFocus(); return; }
            if (new Rectangle(32, layout.FocusCard + 17, layout.Width - 64, 42).Contains(p)) { BeginTaskEditor(); return; }
            if (layout.ShuffleButton.Contains(p)) { ToggleShuffle(); return; }
            if (layout.TrackCount.Contains(p)) { ShowQueue(); return; }
            if (layout.PrevButton.Contains(p)) { Previous(); return; }
            if (layout.PlayButton.Contains(p)) { TogglePlay(); return; }
            if (layout.NextButton.Contains(p)) { Next(); return; }
            if (layout.FooterButton.Contains(p)) { if (discordStatus != "Connected to Discord") ShowDiscordStatus(); else ShowDiscordPreview(); }
        }

        private void SetVolume(int mouseX, Rectangle bar)
        {
            settings.Volume = Math.Max(0, Math.Min(100, (int)Math.Round((mouseX - bar.Left) * 100.0 / bar.Width)));
            ApplyVolume();
            Invalidate();
        }

        private void ApplyVolume()
        {
            // closing a song resets the volume so set it again for the next one
            player.Volume = settings.Volume / 100.0;
        }

        private void ToggleFocus()
        {
            CommitTaskEditor();
            if (focusActive) focusActive = false;
            else
            {
                if (taskDraft.Length == 0) { BeginTaskEditor(); return; }
                focusActive = true;
                focusStart = DateTime.UtcNow;
            }
            RefreshSize();
            UpdatePresence();
        }

        private void ShuffleTracks(List<Track> items)
        {
            MusicLibrary.Shuffle(items, shuffleRandom);
        }

        private void ToggleShuffle()
        {
            // keep the song thats playing when i change the order
            string selected = current >= 0 && current < tracks.Count ? tracks[current].PathName : null;
            settings.Shuffle = !settings.Shuffle;
            if (settings.Shuffle)
            {
                if (selected != null && player.Source != null)
                {
                    Track selectedTrack = tracks[current];
                    List<Track> remaining = tracks.Where((track, index) => index != current).ToList();
                    ShuffleTracks(remaining);
                    tracks.Clear(); tracks.Add(selectedTrack); tracks.AddRange(remaining);
                }
                else ShuffleTracks(tracks);
                current = tracks.Count > 0 ? 0 : -1;
            }
            else
            {
                tracks.Sort((first, second) => MusicLibrary.ComparePaths(first.PathName, second.PathName));
                current = selected == null ? (tracks.Count > 0 ? 0 : -1) :
                    tracks.FindIndex(track => String.Equals(track.PathName, selected, StringComparison.OrdinalIgnoreCase));
            }
            settings.Save();
            Invalidate();
        }

        private void RefreshLibrary()
        {
            // pick up new songs without restarting the app
            List<string> paths = MusicLibrary.FindFiles();
            HashSet<string> found = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            HashSet<string> known = new HashSet<string>(tracks.Select(track => track.PathName), StringComparer.OrdinalIgnoreCase);
            if (found.SetEquals(known))
            {
                if (musicWatcher == null && Directory.Exists(MusicLibrary.FolderPath)) StartMusicWatcher();
                Invalidate();
                return;
            }
            string selected = current >= 0 && current < tracks.Count ? tracks[current].PathName : null;
            bool continuePlayback = playing;
            bool removedCurrent = selected != null && !found.Contains(selected);
            List<Track> updated;
            if (settings.Shuffle)
            {
                updated = tracks.Where(track => found.Contains(track.PathName)).ToList();
                List<Track> additions = paths.Where(path => !known.Contains(path)).Select(Track.Read).ToList();
                int afterCurrent = selected == null ? 0 :
                    updated.FindIndex(track => String.Equals(track.PathName, selected, StringComparison.OrdinalIgnoreCase)) + 1;
                if (afterCurrent < 0) afterCurrent = 0;
                foreach (Track addition in additions)
                    updated.Insert(shuffleRandom.Next(afterCurrent, updated.Count + 1), addition);
            }
            else updated = paths.Select(Track.Read).ToList();
            tracks.Clear(); tracks.AddRange(updated);
            current = player.Source != null && selected != null && !removedCurrent ?
                tracks.FindIndex(track => String.Equals(track.PathName, selected, StringComparison.OrdinalIgnoreCase)) :
                (tracks.Count > 0 ? 0 : -1);
            if (removedCurrent && player.Source != null)
            {
                player.Close(); playing = false; duration = TimeSpan.Zero;
                if (continuePlayback && tracks.Count > 0) PlayCurrent();
                else UpdatePresence();
            }
            RefreshSize();
            if (musicWatcher == null && Directory.Exists(MusicLibrary.FolderPath)) StartMusicWatcher();
        }

        private void StartMusicWatcher()
        {
            if (musicWatcher != null) { musicWatcher.Dispose(); musicWatcher = null; }
            if (!Directory.Exists(MusicLibrary.FolderPath)) return;
            musicWatcher = new FileSystemWatcher(MusicLibrary.FolderPath);
            musicWatcher.IncludeSubdirectories = true;
            musicWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName;
            musicWatcher.Created += delegate { QueueLibraryRefresh(); };
            musicWatcher.Deleted += delegate { QueueLibraryRefresh(); };
            musicWatcher.Renamed += delegate { QueueLibraryRefresh(); };
            musicWatcher.Error += delegate { QueueLibraryRefresh(); };
            musicWatcher.EnableRaisingEvents = true;
        }

        private void QueueLibraryRefresh()
        {
            if (closing || IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (closing || IsDisposed) return;
                    libraryRefresh.Stop(); libraryRefresh.Start();
                }));
            }
            catch (InvalidOperationException) { }
        }

        private void PlayCurrent()
        {
            if (current < 0 || current >= tracks.Count) return;
            try
            {
                player.Close();
                duration = TimeSpan.Zero;
                player.Open(new Uri(tracks[current].PathName));
                ApplyVolume();
                player.Play();
                playing = true;
                UpdatePresence();
                Invalidate();
            }
            catch (Exception error) { MediaFailed(error.Message); }
        }

        private void TogglePlay()
        {
            if (tracks.Count == 0)
            {
                RefreshLibrary();
                if (tracks.Count == 0)
                {
                    MessageBox.Show(this, "Put MP3, M4A, AAC, WAV, or WMA files in " + MusicLibrary.FolderPath + ".", "Music folder is empty", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            if (current < 0) current = 0;
            if (playing) { player.Pause(); playing = false; }
            else if (player.Source == null) { PlayCurrent(); return; }
            else { player.Play(); playing = true; }
            UpdatePresence();
            Invalidate();
        }

        private void Next()
        {
            if (tracks.Count == 0) return;
            current = (current + 1) % tracks.Count;
            PlayCurrent();
        }

        private void Previous()
        {
            if (tracks.Count == 0) return;
            if (player.Position.TotalSeconds > 3) { player.Position = TimeSpan.Zero; UpdatePresence(); Invalidate(); return; }
            current = (current - 1 + tracks.Count) % tracks.Count;
            PlayCurrent();
        }

        private void MediaEnded()
        {
            if (current + 1 < tracks.Count) { current++; PlayCurrent(); }
            else { playing = false; player.Stop(); UpdatePresence(); Invalidate(); }
        }

        private void MediaFailed(string reason)
        {
            playing = false;
            UpdatePresence();
            Invalidate();
            MessageBox.Show(this, reason, "Audio playback error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ShowQueue()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = CardBottom;
            menu.ForeColor = White;
            if (tracks.Count == 0) menu.Items.Add("No songs in queue").Enabled = false;
            for (int i = 0; i < tracks.Count; i++)
            {
                int selected = i;
                ToolStripItem item = menu.Items.Add((i == current ? "▶  " : "     ") + tracks[i].Title);
                item.Click += delegate { current = selected; PlayCurrent(); };
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Refresh music folder", null, delegate { RefreshLibrary(); StartMusicWatcher(); });
            UiLayout layout = CurrentLayout();
            menu.Show(this, new Point(layout.Width - 140, layout.MusicCard + 30));
        }

        private void ShowInfoMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = CardBottom;
            menu.ForeColor = White;
            menu.Items.Add("Song queue", null, delegate { ShowQueue(); });
            ToolStripItem status = menu.Items.Add(discordStatus);
            status.Enabled = false;
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit studying", null, delegate { Close(); });
            UiLayout layout = CurrentLayout();
            menu.Show(this, new Point(layout.Width - 165, layout.Footer - menu.PreferredSize.Height - 4));
        }

        private void ShowDiscordStatus()
        {
            MessageBox.Show(this, discordStatus, "Discord activity", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowDiscordPreview()
        {
            string text = "Playing\n" + DiscordRpc.StudyName(focusActive ? taskDraft : "");
            if (playing && current >= 0)
                text += "\nlistening to " + tracks[current].Title;
            if (focusActive) text += "\nStudy time: " + FormatTime(DateTime.UtcNow - focusStart);
            MessageBox.Show(this, text, "Discord activity preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SetDiscordStatus(string value)
        {
            discordStatus = value;
            if (IsHandleCreated && !IsDisposed)
            {
                try { BeginInvoke((Action)(() => Invalidate())); }
                catch (InvalidOperationException) { }
            }
        }

        private void UpdatePresence()
        {
            // update discord when i start a task or change songs
            if (rpc == null) return;
            PresenceState state = new PresenceState();
            state.Task = focusActive ? taskDraft : "";
            if (focusActive)
                state.FocusStart = (long)(focusStart - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            state.Playing = playing && current >= 0 && current < tracks.Count;
            state.Title = state.Playing ? tracks[current].Title : "";
            rpc.Set(state);
        }

#if VISUAL_QA
        internal void PreviewIdle()
        {
            tracks.Clear();
            current = -1;
            duration = TimeSpan.Zero;
            previewPosition = null;
            playing = false;
            taskDraft = "";
            focusActive = false;
            RefreshSize();
        }

        internal void PreviewMusicOnly()
        {
            tracks.Clear();
            tracks.Add(new Track { Title = "no other heart", PathName = "no other heart.mp3", Artist = "" });
            current = 0;
            duration = TimeSpan.FromSeconds(163);
            previewPosition = TimeSpan.FromSeconds(20);
            playing = false;
            discordStatus = "Connected to Discord";
            RefreshSize();
        }

        internal void PreviewActive()
        {
            taskDraft = "Finish the design draft";
            focusActive = true;
            focusStart = DateTime.UtcNow.AddMinutes(-23).AddSeconds(-41);
            tracks.Clear();
            for (int i = 0; i < 12; i++) tracks.Add(new Track { Title = i == 2 ? "Slow Motion" : "Track " + (i + 1), PathName = "Track.mp3", Artist = "" });
            current = 2;
            duration = TimeSpan.FromSeconds(198);
            previewPosition = TimeSpan.FromSeconds(102);
            playing = true;
            discordStatus = "Connected to Discord";
            RefreshSize();
        }
#endif
    }
}
