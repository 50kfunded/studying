using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NowAndDoing
{
    internal sealed class FocusHistoryForm : Form
    {
        private static readonly Color Back = Color.FromArgb(24, 26, 29);
        private static readonly Color Card = Color.FromArgb(32, 35, 39);
        private static readonly Color White = Color.FromArgb(233, 235, 231);
        private static readonly Color Muted = Color.FromArgb(149, 157, 160);
        private static readonly Color Line = Color.FromArgb(48, 51, 54);
        private readonly DataGridView grid;
        private readonly Timer timer;
        private readonly FocusSession activeSession;
        private int activeRow = -1;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public FocusHistoryForm(FocusHistory history, FocusSession currentSession)
        {
            activeSession = currentSession;
            Text = "study log";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Back;
            ClientSize = new Size(620, 460);
            KeyDown += (sender, args) => { if (args.KeyCode == Keys.Escape) Close(); };

            Label title = new Label { Text = "study log", Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = White, BackColor = Back, Location = new Point(20, 17), Size = new Size(200, 30) };
            Label subtitle = new Label { Text = "your tasks, start and finish times", Font = new Font("Segoe UI", 8.5f),
                ForeColor = Muted, BackColor = Back, Location = new Point(21, 51), Size = new Size(360, 20) };
            Button close = new Button { Text = "×", Font = new Font("Segoe UI", 15f),
                ForeColor = Muted, BackColor = Back, FlatStyle = FlatStyle.Flat,
                Location = new Point(574, 14), Size = new Size(29, 29), TabStop = true };
            close.FlatAppearance.BorderSize = 0;
            close.Click += (sender, args) => Close();
            Controls.Add(title);
            Controls.Add(subtitle);
            Controls.Add(close);
            MouseDown += (sender, args) =>
            {
                if (args.Button == MouseButtons.Left && args.Y < 76)
                { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); }
            };

            grid = new DataGridView();
            grid.Location = new Point(18, 84);
            grid.Size = new Size(584, 356);
            grid.BackgroundColor = Card;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Line;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 32;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.RowHeadersVisible = false;
            grid.RowTemplate.Height = 32;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToOrderColumns = false;
            grid.ReadOnly = true;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.ScrollBars = ScrollBars.Vertical;
            grid.DefaultCellStyle.BackColor = Card;
            grid.DefaultCellStyle.ForeColor = White;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(51, 60, 61);
            grid.DefaultCellStyle.SelectionForeColor = White;
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 8.7f);
            grid.DefaultCellStyle.Padding = new Padding(5, 0, 2, 0);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Back;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8f);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(5, 0, 0, 0);
            grid.Columns.Add("task", "task");
            grid.Columns.Add("studied", "studied");
            grid.Columns.Add("started", "started");
            grid.Columns.Add("finished", "finished");
            grid.Columns[0].Width = 185;
            grid.Columns[1].Width = 76;
            grid.Columns[2].Width = 145;
            grid.Columns[3].Width = 145;
            Controls.Add(grid);

            foreach (FocusSession session in history.Sessions.OrderByDescending(item => item.StartedUtcTicks))
            {
                bool isActive = Object.ReferenceEquals(session, activeSession) && session.FinishedUtcTicks == 0;
                string studied = session.FinishedUtcTicks == 0 && !isActive ? "—" :
                    Duration((session.FinishedUtc ?? DateTime.UtcNow) - session.StartedUtc);
                string finished = session.FinishedUtc.HasValue ? LocalTime(session.FinishedUtc.Value) :
                    isActive ? "in progress" : "unfinished";
                int row = grid.Rows.Add(session.Task, studied, LocalTime(session.StartedUtc), finished);
                if (isActive) activeRow = row;
            }
            grid.ClearSelection();
            if (grid.Rows.Count == 0)
            {
                Label empty = new Label { Text = "no sessions yet. start a task and itll show here.",
                    Font = new Font("Segoe UI", 9f), ForeColor = Muted, BackColor = Card,
                    TextAlign = ContentAlignment.MiddleCenter, Location = new Point(28, 138), Size = new Size(564, 50) };
                Controls.Add(empty);
                empty.BringToFront();
            }

            timer = new Timer { Interval = 1000 };
            timer.Tick += (sender, args) =>
            {
                if (activeRow >= 0)
                    grid.Rows[activeRow].Cells[1].Value = Duration(DateTime.UtcNow - activeSession.StartedUtc);
            };
            timer.Start();
            FormClosed += (sender, args) => { timer.Stop(); timer.Dispose(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(Line))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                e.Graphics.DrawLine(pen, 0, 76, ClientSize.Width, 76);
            }
        }

        private static string LocalTime(DateTime utc)
        {
            return utc.ToLocalTime().ToString("dd MMM  HH:mm");
        }

        private static string Duration(TimeSpan time)
        {
            if (time < TimeSpan.Zero) time = TimeSpan.Zero;
            if (time.TotalHours >= 1)
                return ((long)time.TotalHours).ToString() + ":" + time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00");
            return time.Minutes + ":" + time.Seconds.ToString("00");
        }
    }
}
