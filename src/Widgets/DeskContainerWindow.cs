using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace MotionDesk.Widgets
{
    // Η ΠΡΑΓΜΑΤΙΚΗ έννοια των Stardock Fences: ένα container στο οποίο σέρνεις αρχεία/φακέλους/
    // συντομεύσεις της επιφάνειας εργασίας για να τα ομαδοποιήσεις οπτικά. Σκόπιμα ΔΕΝ αγγίζει
    // τα πραγματικά εικονίδια της επιφάνειας εργασίας (θα απαιτούσε cross-process χειρισμό του
    // κρυφού SysListView32 της Explorer μέσω LVM_SETITEMPOSITION — εύθραυστο ανάμεσα σε Windows
    // εκδόσεις και ρίσκο να χαλάσει πραγματικά τη διάταξη των εικονιδίων του χρήστη). Αντ' αυτού
    // κρατάει τη ΔΙΚΗ του λίστα από paths και τα εμφανίζει με πραγματικά system icons.
    public sealed class DeskContainerWindow : Form
    {
        private const int TitleBarHeight = 28;
        private readonly string _containerId;
        private string _title;
        private readonly List<string> _paths = new();
        private readonly FlowLayoutPanel _grid;
        private readonly System.Windows.Forms.Timer _saveTimer;

        public string ContainerId => _containerId;
        public string ContainerTitle => _title;

        public DeskContainerWindow(string containerId, string title, int x, int y, int width, int height)
        {
            _containerId = containerId;
            _title = title;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(24, 24, 24);
            Size = new Size(width, height);
            AllowDrop = true;

            _saveTimer = new System.Windows.Forms.Timer { Interval = 350 };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveState(); };
            FormClosed += (_, _) => { _saveTimer.Stop(); _saveTimer.Dispose(); SaveState(); };
            MouseLeave += (s, e) => { };

            var closeLabel = new Label { Text = "✕", AutoSize = false, Size = new Size(TitleBarHeight, TitleBarHeight), Location = new Point(width - TitleBarHeight, 0), Anchor = AnchorStyles.Top | AnchorStyles.Right, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(200, 255, 255, 255), BackColor = Color.Transparent, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand };
            closeLabel.MouseEnter += (_, _) => closeLabel.ForeColor = Color.FromArgb(255, 231, 76, 60);
            closeLabel.MouseLeave += (_, _) => closeLabel.ForeColor = Color.FromArgb(200, 255, 255, 255);
            closeLabel.Click += (_, _) => Close();
            Controls.Add(closeLabel);

            var menuLabel = new Label { Text = "☰", AutoSize = false, Size = new Size(TitleBarHeight, TitleBarHeight), Location = new Point(0, 0), Anchor = AnchorStyles.Top | AnchorStyles.Left, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(200, 255, 255, 255), BackColor = Color.Transparent, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand };
            menuLabel.MouseEnter += (_, _) => menuLabel.ForeColor = Color.White;
            menuLabel.MouseLeave += (_, _) => menuLabel.ForeColor = Color.FromArgb(200, 255, 255, 255);
            menuLabel.Click += (_, _) => ShowContainerMenu(menuLabel);
            Controls.Add(menuLabel);

            var titleBar = new Panel { Dock = DockStyle.Top, Height = TitleBarHeight, BackColor = Color.Transparent };
            titleBar.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0); }
            };
            titleBar.Paint += (_, e) =>
            {
                using var titleBrush = new SolidBrush(Color.White);
                using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
                e.Graphics.DrawString(_title, font, titleBrush, new PointF(TitleBarHeight + 2, 6));
                using var separator = new Pen(Color.FromArgb(60, 255, 255, 255));
                e.Graphics.DrawLine(separator, 0, TitleBarHeight - 1, Width, TitleBarHeight - 1);
            };
            Controls.Add(titleBar);
            titleBar.SendToBack();

            _grid = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoScroll = true, Padding = new Padding(8, 4, 8, 8), AllowDrop = true };
            _grid.DragEnter += Grid_DragEnter;
            _grid.DragDrop += Grid_DragDrop;
            Controls.Add(_grid);

            DragEnter += Grid_DragEnter;
            DragDrop += Grid_DragDrop;

            LoadState(x, y);
            RebuildGrid();
        }

        private void Grid_DragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void Grid_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
            bool changed = false;
            foreach (var f in files)
            {
                if (!_paths.Contains(f, StringComparer.OrdinalIgnoreCase)) { _paths.Add(f); changed = true; }
            }
            if (changed) { RebuildGrid(); QueueSave(); }
        }

        private void RebuildGrid()
        {
            _grid.SuspendLayout();
            _grid.Controls.Clear();
            foreach (var path in _paths.ToArray())
            {
                var tile = new ContainerIconTile(path, RemovePath);
                _grid.Controls.Add(tile);
            }
            _grid.ResumeLayout();
        }

        private void RemovePath(string path)
        {
            if (_paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                RebuildGrid();
                QueueSave();
            }
        }

        private void ShowContainerMenu(Control owner)
        {
            // ΟΧΙ "using": το Show() δεν μπλοκάρει — immediate Dispose μετά έκανε το μενού να
            // κλείνει πριν προλάβει ο χρήστης να διαλέξει κάτι (flash-and-vanish).
            var menu = new ContextMenuStrip();
            menu.Items.Add("Rename container…", null, (_, _) => RenameContainer());
            menu.Items.Add("Add files/folder…", null, (_, _) => AddViaDialog());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Close container", null, (_, _) => Close());
            menu.Show(owner, new Point(0, owner.Height));
        }

        private void AddViaDialog()
        {
            using var dialog = new OpenFileDialog { Multiselect = true, Title = "Add files to DeskContainer", CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                bool changed = false;
                foreach (var f in dialog.FileNames)
                    if (!_paths.Contains(f, StringComparer.OrdinalIgnoreCase)) { _paths.Add(f); changed = true; }
                if (changed) { RebuildGrid(); QueueSave(); }
            }
        }

        private void RenameContainer()
        {
            using var dialog = new Form { Text = "Rename DeskContainer", StartPosition = FormStartPosition.CenterParent, Size = new Size(420, 150), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
            var box = new TextBox { Text = _title, Dock = DockStyle.Top, Margin = new Padding(12) };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 34 };
            dialog.Controls.Add(box);
            dialog.Controls.Add(ok);
            dialog.AcceptButton = ok;
            if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(box.Text))
            {
                _title = box.Text.Trim();
                Invalidate(true);
                QueueSave();
            }
        }

        private void QueueSave() { _saveTimer.Stop(); _saveTimer.Start(); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyGlassBlur();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            foreach (Control c in Controls)
                if (c is Label l && l.Text == "✕") l.Location = new Point(Width - TitleBarHeight, 0);
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            QueueSave();
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

        private string GetConfigFilePath() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MotionDeskStudio", "deskcontainers", $"{_containerId}_config.json");

        private void SaveState()
        {
            try
            {
                var path = GetConfigFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var state = new DeskContainerState { X = Location.X, Y = Location.Y, Width = Width, Height = Height, Title = _title, Paths = _paths.ToArray() };
                File.WriteAllText(path, JsonSerializer.Serialize(state));
            }
            catch (IOException) { }
        }

        private void LoadState(int defaultX, int defaultY)
        {
            var path = GetConfigFilePath();
            if (File.Exists(path))
            {
                try
                {
                    var state = JsonSerializer.Deserialize<DeskContainerState>(File.ReadAllText(path));
                    if (state != null)
                    {
                        if (state.Width > 160 && state.Height > 120)
                            Size = new Size(Math.Clamp(state.Width, 200, 1600), Math.Clamp(state.Height, 140, 1000));
                        Location = new Point(state.X, state.Y);
                        _title = state.Title ?? _title;
                        if (state.Paths != null) _paths.AddRange(state.Paths.Where(p => File.Exists(p) || Directory.Exists(p)));
                        return;
                    }
                }
                catch (JsonException) { }
                catch (IOException) { }
            }
            Location = new Point(defaultX, defaultY);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ACCENT_POLICY { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWCOMPOSITIONATTRIBDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);
    }

    // Owner-draw πλακίδιο: πραγματικό system icon (μέσω Icon.ExtractAssociatedIcon) + όνομα από
    // κάτω, σαν κανονικό εικονίδιο επιφάνειας εργασίας. Διπλό-κλικ = άνοιγμα, δεξί-κλικ = αφαίρεση.
    internal sealed class ContainerIconTile : Panel
    {
        private readonly string _path;
        private readonly Action<string> _onRemove;
        private readonly Icon? _icon;
        private bool _hover;

        public ContainerIconTile(string path, Action<string> onRemove)
        {
            _path = path;
            _onRemove = onRemove;
            Size = new Size(76, 84);
            Margin = new Padding(4);
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            try { _icon = Icon.ExtractAssociatedIcon(path); } catch { _icon = null; }
            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; Invalidate(); };
            DoubleClick += (_, _) => LaunchSelf();
            MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) Focus();
                if (e.Button == MouseButtons.Right)
                {
                    Focus();
                    var menu = new ContextMenuStrip();
                    menu.Items.Add("Open", null, (_, _) => LaunchSelf());
                    menu.Items.Add("Quick Look (Space)", null, (_, _) => QuickLookPreview.Show(_path));
                    menu.Items.Add("Remove from container", null, (_, _) => _onRemove(_path));
                    menu.Show(this, e.Location);
                }
            };
            // Space = Quick Look, στο πνεύμα των macOS/PowerToys Peek — απαιτεί IsInputKey ώστε το
            // Space να φτάσει σε KeyDown αντί να καταναλωθεί ως "πάτημα κουμπιού" από τον Panel/Selectable στυλ.
            KeyDown += (_, e) => { if (e.KeyCode == Keys.Space) { QuickLookPreview.Show(_path); e.Handled = true; } };
        }

        protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);

        private void LaunchSelf()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_path) { UseShellExecute = true }); }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (_hover)
            {
                using var bg = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
                g.FillRectangle(bg, 0, 0, Width, Height);
            }
            if (_icon != null) g.DrawIcon(_icon, new Rectangle(Width / 2 - 16, 6, 32, 32));

            string name = Path.GetFileName(_path);
            using var font = new Font("Segoe UI", 8f);
            using var textBrush = new SolidBrush(Color.White);
            var textRect = new RectangleF(2, 42, Width - 4, Height - 44);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit };
            g.DrawString(name, font, textBrush, textRect, sf);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _icon?.Dispose();
            base.Dispose(disposing);
        }
    }

    public class DeskContainerState
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string? Title { get; set; }
        public string[]? Paths { get; set; }
    }

    public class DeskContainerHostEngine
    {
        private static DeskContainerHostEngine? _instance;
        public static DeskContainerHostEngine Instance => _instance ??= new DeskContainerHostEngine();

        private readonly List<DeskContainerWindow> _active = new();
        public IReadOnlyCollection<DeskContainerWindow> GetActiveContainers() => _active;

        public void CloseAll()
        {
            foreach (var c in new List<DeskContainerWindow>(_active))
                if (!c.IsDisposed) c.Close();
            _active.Clear();
        }

        public DeskContainerWindow SpawnContainer(string containerId, string title, int x, int y, int width, int height)
        {
            var existing = _active.Find(c => c.Tag as string == containerId);
            if (existing != null && !existing.IsDisposed) { existing.BringToFront(); return existing; }
            var container = new DeskContainerWindow(containerId, title, x, y, width, height) { Tag = containerId };
            _active.Add(container);
            container.FormClosed += (s, e) => _active.Remove(container);
            container.Show();
            return container;
        }
    }
}
