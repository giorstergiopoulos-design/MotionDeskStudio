using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace MotionDesk.Widgets
{
    // "DeskStrip" — πλωτό dock συντομεύσεων στο κάτω-κέντρο της κύριας οθόνης, στο πνεύμα του
    // RocketDock/Winstep Nexus/ObjectDock. Ίδιο βασικό μοτίβο owner-draw Form χωρίς περίγραμμα με
    // το DeskContainerWindow (ίδιο glass-blur effect, ίδιο DragDrop pattern, ίδιο JSON persistence
    // στο %AppData%). v1: πάντα ορατό (χωρίς edge-triggered auto-hide ακόμα — σκόπιμα εκτός scope
    // αυτού του περάσματος, βλ. roadmap), με μεγέθυνση εικονιδίου στο hover και live ένδειξη αν η
    // καρφιτσωμένη εφαρμογή τρέχει αυτή τη στιγμή.
    public sealed class DeskStripWindow : Form
    {
        private const int TileSize = 48;
        private const int TileGap = 10;
        private const int BarHeight = 72;

        private readonly List<string> _paths = new();
        private readonly FlowLayoutPanel _tray;
        private readonly System.Windows.Forms.Timer _saveTimer;
        private readonly System.Windows.Forms.Timer _runningCheckTimer;

        public DeskStripWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(20, 20, 20);
            Height = BarHeight;
            AllowDrop = true;

            _tray = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                Padding = new Padding(10, 8, 10, 8),
                AllowDrop = true
            };
            _tray.DragEnter += Tray_DragEnter;
            _tray.DragDrop += Tray_DragDrop;
            Controls.Add(_tray);
            DragEnter += Tray_DragEnter;
            DragDrop += Tray_DragDrop;

            _saveTimer = new System.Windows.Forms.Timer { Interval = 350 };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveState(); };

            _runningCheckTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            _runningCheckTimer.Tick += (_, _) => RefreshRunningIndicators();
            _runningCheckTimer.Start();

            FormClosed += (_, _) => { _saveTimer.Stop(); _saveTimer.Dispose(); _runningCheckTimer.Stop(); _runningCheckTimer.Dispose(); SaveState(); };

            LoadState();
            RebuildTray();
            RepositionToBottomCenter();
        }

        private void RepositionToBottomCenter()
        {
            var area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
            int width = Math.Max(160, _paths.Count * (TileSize + TileGap) + 40);
            Width = width;
            Location = new Point(area.X + (area.Width - Width) / 2, area.Bottom - Height - 8);
        }

        private void Tray_DragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void Tray_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
            bool changed = false;
            foreach (var f in files)
                if (!_paths.Contains(f, StringComparer.OrdinalIgnoreCase)) { _paths.Add(f); changed = true; }
            if (changed) { RebuildTray(); RepositionToBottomCenter(); QueueSave(); }
        }

        private void RebuildTray()
        {
            _tray.SuspendLayout();
            _tray.Controls.Clear();
            foreach (var path in _paths.ToArray())
                _tray.Controls.Add(new DockTile(path, RemovePath));
            _tray.ResumeLayout();
        }

        private void RemovePath(string path)
        {
            if (_paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                RebuildTray();
                RepositionToBottomCenter();
                QueueSave();
            }
        }

        public void PinApp(string path)
        {
            if (_paths.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
            _paths.Add(path);
            RebuildTray();
            RepositionToBottomCenter();
            QueueSave();
        }

        public void UnpinApp(string path) => RemovePath(path);

        public IReadOnlyList<string> PinnedApps => _paths;

        private void RefreshRunningIndicators()
        {
            foreach (Control c in _tray.Controls)
                if (c is DockTile tile) tile.RefreshRunningState();
        }

        private void QueueSave() { _saveTimer.Stop(); _saveTimer.Start(); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyGlassBlur();
        }

        private void ApplyGlassBlur()
        {
            var accent = new ACCENT_POLICY { AccentState = 3, GradientColor = unchecked((int)0x99202020) };
            int size = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = 19, SizeOfData = size, Data = accentPtr };
            SetWindowCompositionAttribute(Handle, ref data);
            Marshal.FreeHGlobal(accentPtr);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var path = MotionDesk.UI.UiTheme.RoundedPath(ClientRectangle, 16);
            using var pen = new Pen(Color.FromArgb(60, 255, 255, 255));
            e.Graphics.DrawPath(pen, path);
        }

        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MotionDeskStudio", "deskstrip", "config.json");

        private sealed class DeskStripState { public List<string> Paths { get; set; } = new(); }

        private void SaveState()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new DeskStripState { Paths = _paths }));
            }
            catch (IOException) { }
        }

        private void LoadState()
        {
            if (!File.Exists(ConfigPath)) return;
            try
            {
                var state = JsonSerializer.Deserialize<DeskStripState>(File.ReadAllText(ConfigPath));
                if (state?.Paths != null) _paths.AddRange(state.Paths.Where(p => File.Exists(p)));
            }
            catch (JsonException) { }
            catch (IOException) { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ACCENT_POLICY { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWCOMPOSITIONATTRIBDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);
    }

    // Owner-draw πλακίδιο dock: μεγεθύνεται ελαφρά στο hover (ObjectDock-style), δείχνει τελεία
    // από κάτω αν η καρφιτσωμένη εφαρμογή τρέχει αυτή τη στιγμή. Διπλό-κλικ = εκκίνηση/BringToFront
    // αν ήδη τρέχει, δεξί-κλικ = αφαίρεση.
    internal sealed class DockTile : Panel
    {
        private readonly string _path;
        private readonly Action<string> _onRemove;
        private readonly Icon? _icon;
        private bool _hover;
        private bool _running;

        public DockTile(string path, Action<string> onRemove)
        {
            _path = path;
            _onRemove = onRemove;
            Size = new Size(56, 64);
            Margin = new Padding(2, 0, 2, 0);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            try { _icon = Icon.ExtractAssociatedIcon(path); } catch (Exception) { _icon = null; }

            var tip = new ToolTip { InitialDelay = 300, AutoPopDelay = 4000 };
            tip.SetToolTip(this, Path.GetFileNameWithoutExtension(path));

            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; Invalidate(); };
            DoubleClick += (_, _) => Launch();
            MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    var menu = new ContextMenuStrip();
                    menu.Items.Add("Open", null, (_, _) => Launch());
                    menu.Items.Add("Unpin from DeskStrip", null, (_, _) => _onRemove(_path));
                    menu.Show(this, e.Location);
                }
            };
            RefreshRunningState();
        }

        public void RefreshRunningState()
        {
            bool wasRunning = _running;
            _running = IsProcessRunningForPath(_path);
            if (_running != wasRunning) Invalidate();
        }

        private static bool IsProcessRunningForPath(string path)
        {
            try
            {
                string target = Path.GetFullPath(path);
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        if (proc.MainModule != null &&
                            string.Equals(proc.MainModule.FileName, target, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch (Exception) { /* access denied σε process άλλου χρήστη/elevated — αγνοείται */ }
                }
            }
            catch (Exception) { }
            return false;
        }

        private void Launch()
        {
            try { Process.Start(new ProcessStartInfo(_path) { UseShellExecute = true }); }
            catch (Exception) { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int iconSize = _hover ? 36 : 30;
            int iconX = (Width - iconSize) / 2;
            int iconY = _hover ? 2 : 6;

            if (_hover)
            {
                using var bg = new SolidBrush(Color.FromArgb(50, 255, 255, 255));
                using var bgPath = MotionDesk.UI.UiTheme.RoundedPath(new Rectangle(4, 0, Width - 8, Width - 8), 10);
                g.FillPath(bg, bgPath);
            }

            if (_icon != null) g.DrawIcon(_icon, new Rectangle(iconX, iconY, iconSize, iconSize));

            if (_running)
            {
                using var dot = new SolidBrush(Color.FromArgb(230, 90, 220, 140));
                g.FillEllipse(dot, Width / 2 - 3, Height - 8, 6, 6);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _icon?.Dispose();
            base.Dispose(disposing);
        }
    }

    public sealed class DeskStripHostEngine
    {
        private static DeskStripHostEngine? _instance;
        public static DeskStripHostEngine Instance => _instance ??= new DeskStripHostEngine();

        private DeskStripWindow? _window;
        public bool IsEnabled => _window is { IsDisposed: false };

        public void Enable()
        {
            if (IsEnabled) return;
            _window = new DeskStripWindow();
            _window.FormClosed += (_, _) => _window = null;
            _window.Show();
        }

        public void Disable()
        {
            if (_window is { IsDisposed: false }) _window.Close();
            _window = null;
        }

        public void PinApp(string path)
        {
            if (!IsEnabled) Enable();
            _window?.PinApp(path);
        }

        public void UnpinApp(string path) => _window?.UnpinApp(path);

        public IReadOnlyList<string> GetPinnedApps() => _window?.PinnedApps ?? Array.Empty<string>();
    }
}
