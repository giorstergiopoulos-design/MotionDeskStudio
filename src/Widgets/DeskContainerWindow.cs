using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.UI;
// Βλ. αναλυτικό σχόλιο στο WidgetEngine.cs — ίδια ανακατεύθυνση προς το ανεξάρτητο θέμα widgets.
using UiTheme = MotionDesk.UI.WidgetTheme;

namespace MotionDesk.Widgets
{
    public enum ContainerSortMode { None, Name, Size, ItemType, DateModified, DateCreated, DateAdded, TimesOpened }

    public sealed class ContainerItem
    {
        public string Path { get; set; } = "";
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public int OpenCount { get; set; }
    }

    // Η ΠΡΑΓΜΑΤΙΚΗ έννοια των Stardock Fences: ένα container στο οποίο σέρνεις αρχεία/φακέλους/
    // συντομεύσεις της επιφάνειας εργασίας για να τα ομαδοποιήσεις οπτικά. Σκόπιμα ΔΕΝ αγγίζει
    // τα πραγματικά εικονίδια της επιφάνειας εργασίας — κρατάει τη ΔΙΚΗ του λίστα από paths και
    // τα εμφανίζει με πραγματικά system icons.
    //
    // Το μενού (ShowContainerMenu) αναδημιουργήθηκε ρητά ώστε να ταιριάζει στη διάρθρωση/
    // λειτουργίες του πραγματικού Stardock Fences (μετά από screenshots του χρήστη): Rename,
    // View (Roll-up/Exclude from quick-hide/Opacity/Copy&Edit color), Sort by (7 κριτήρια +
    // Organize > "Place new icons here by default"/"Manage sorting rules"), Configure…
    public sealed class DeskContainerWindow : Form
    {
        private const int TitleBarHeight = 28;
        private readonly string _containerId;
        private string _title;
        private List<ContainerItem> _items = new();
        private readonly FlowLayoutPanel _grid;
        private readonly System.Windows.Forms.Timer _saveTimer;
        private System.Windows.Forms.Timer? _rollUpAnimTimer;
        private System.Windows.Forms.Timer? _hoverPollTimer;
        private Label? _lockLabelRef;
        private int _expandedHeight;
        private ContainerSortMode _sortMode = ContainerSortMode.None;

        public string ContainerId => _containerId;
        public string ContainerTitle => _title;
        public bool IsLocked { get; set; }
        public bool IsRolledUp { get; private set; }
        public bool ExcludeFromQuickHide { get; set; }
        public Color AccentColor { get; set; } = Color.FromArgb(0, 170, 255);

        public DeskContainerWindow(string containerId, string title, int x, int y, int width, int height)
        {
            _containerId = containerId;
            _title = title;
            _expandedHeight = height;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(24, 24, 24);
            Size = new Size(width, height);
            AllowDrop = true;

            _saveTimer = new System.Windows.Forms.Timer { Interval = 350 };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveState(); };
            FormClosed += (_, _) =>
            {
                _saveTimer.Stop(); _saveTimer.Dispose();
                _rollUpAnimTimer?.Stop(); _rollUpAnimTimer?.Dispose();
                _hoverPollTimer?.Stop(); _hoverPollTimer?.Dispose();
                SaveState();
            };

            // ΔΙΟΡΘΩΣΗ πραγματικού bug (επιβεβαιώθηκε με screenshot): τα κουμπιά (✕/☰/🔒) ήταν
            // παιδιά του ΙΔΙΟΥ Form (με Location/Anchor) αντί για παιδιά του titleBar, και το
            // titleBar γινόταν SendToBack() ώστε να μην κλέβει τα κλικ τους — αλλά το SendToBack()
            // σε WinForms αλλάζει και τη σειρά επεξεργασίας του Dock layout, με αποτέλεσμα το
            // _grid (Dock=Fill) να υπολογίζει λάθος τα όριά του και να ξεκινάει ΚΑΤΩ από το
            // titleBar αντί για μετά από αυτό — τα εικονίδια εμφανίζονταν επικαλυπτόμενα από τη
            // γραμμή τίτλου. Ίδιο ακριβώς μοτίβο bug με αυτό που ήδη διορθώθηκε στο MainWindow
            // sidebar (βλ. σχόλιο στο BuildChrome). Λύση: τα κουμπιά γίνονται ΠΑΙΔΙΑ του titleBar
            // (Dock=Right/Left μέσα του), ίδιο μοτίβο με το CreateNativePanel των widgets — καμία
            // ανάγκη για SendToBack πλέον, αφού δεν ανταγωνίζονται πια το _grid στο ίδιο επίπεδο.
            var titleBar = new Panel { Dock = DockStyle.Top, Height = TitleBarHeight, BackColor = Color.Transparent };
            titleBar.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left && !IsLocked) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0); }
            };
            // Διπλό-κλικ στη γραμμή τίτλου = μετονομασία (ζητήθηκε ρητά, στυλ Fences) — το roll-up
            // είναι πλέον hover-driven (βλ. EnsureHoverPolling/SetHoverExpanded), οπότε το διπλό-
            // κλικ ήταν ελεύθερο να ξαναχρησιμοποιηθεί για κάτι πιο χρήσιμο από περιττό δεύτερο
            // τρόπο toggle roll-up.
            titleBar.DoubleClick += (_, _) => RenameContainer();
            titleBar.Paint += (_, e) =>
            {
                using var titleBrush = new SolidBrush(Color.White);
                using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
                var textRect = new RectangleF(TitleBarHeight + 4, 0, Math.Max(20, titleBar.Width - TitleBarHeight * 3 - 8), TitleBarHeight);
                using var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                e.Graphics.DrawString(_title, font, titleBrush, textRect, sf);
                using var separator = new Pen(Color.FromArgb(60, 255, 255, 255));
                e.Graphics.DrawLine(separator, 0, TitleBarHeight - 1, titleBar.Width, TitleBarHeight - 1);
            };

            var closeLabel = new Label { Text = "✕", AutoSize = false, Size = new Size(TitleBarHeight, TitleBarHeight), Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(200, 255, 255, 255), BackColor = Color.Transparent, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand };
            closeLabel.MouseEnter += (_, _) => closeLabel.ForeColor = Color.FromArgb(255, 231, 76, 60);
            closeLabel.MouseLeave += (_, _) => closeLabel.ForeColor = Color.FromArgb(200, 255, 255, 255);
            closeLabel.Click += (_, _) => Close();

            var lockLabel = new Label { Text = "🔓", AutoSize = false, Size = new Size(TitleBarHeight, TitleBarHeight), Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(200, 255, 255, 255), BackColor = Color.Transparent, Font = new Font("Segoe UI", 9), Cursor = Cursors.Hand };
            lockLabel.Click += (_, _) => { IsLocked = !IsLocked; lockLabel.Text = IsLocked ? "🔒" : "🔓"; QueueSave(); };
            _lockLabelRef = lockLabel;

            var menuLabel = new Label { Text = "☰", AutoSize = false, Size = new Size(TitleBarHeight, TitleBarHeight), Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(200, 255, 255, 255), BackColor = Color.Transparent, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand };
            menuLabel.MouseEnter += (_, _) => menuLabel.ForeColor = Color.White;
            menuLabel.MouseLeave += (_, _) => menuLabel.ForeColor = Color.FromArgb(200, 255, 255, 255);
            menuLabel.Click += (_, _) => ShowContainerMenu(menuLabel);

            // Ίδια σειρά προσθήκης με το ήδη-σωστό CreateNativePanel των widgets: lockLabel πριν
            // το closeLabel ώστε το ✕ να καταλήξει στην ακριανή δεξιά θέση.
            titleBar.Controls.Add(lockLabel);
            titleBar.Controls.Add(closeLabel);
            titleBar.Controls.Add(menuLabel);
            Controls.Add(titleBar);

            // Μεγαλύτερο πάνω padding (8 -> 12) ώστε να υπάρχει καθαρό, ορατό κενό κάτω από τη
            // γραμμή τίτλου (στυλ Fences) — ζητήθηκε ρητά ξανά ("για πολλοστή φορά"). Επίσης
            // ρητό PerformLayout() αμέσως μετά την προσθήκη titleBar+_grid: χωρίς αυτό, ένα
            // φρέσκο container (π.χ. μέσω "Create Folder Portal here") μπορεί να δείξει ΕΝΑ
            // πρώτο frame όπου το Dock=Fill του _grid υπολογίζεται πριν προλάβει να "κλειδώσει"
            // το ύψος του Dock=Top titleBar — φαινόμενο επικάλυψης ακριβώς στο πρώτο render.
            _grid = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoScroll = true, Padding = new Padding(8, 12, 8, 8), AllowDrop = true };
            _grid.DragEnter += Grid_DragEnter;
            _grid.DragDrop += Grid_DragDrop;
            Controls.Add(_grid);
            PerformLayout();

            DragEnter += Grid_DragEnter;
            DragDrop += Grid_DragDrop;

            LoadState(x, y);
            lockLabel.Text = IsLocked ? "🔒" : "🔓";
            RebuildGrid();
        }

        private void Grid_DragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void Grid_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
            AddPaths(files);
        }

        private void AddPaths(IEnumerable<string> files)
        {
            bool changed = false;
            foreach (var f in files)
            {
                if (!_items.Any(i => string.Equals(i.Path, f, StringComparison.OrdinalIgnoreCase)))
                {
                    _items.Add(new ContainerItem { Path = f, DateAdded = DateTime.Now });
                    changed = true;
                }
            }
            if (changed) { ApplySort(); QueueSave(); }
        }

        private void RebuildGrid()
        {
            _grid.SuspendLayout();
            _grid.Controls.Clear();
            foreach (var item in _items.ToArray())
            {
                string path = item.Path;
                var tile = new ContainerIconTile(path, RemovePath, () => IncrementOpenCount(path));
                _grid.Controls.Add(tile);
            }
            _grid.ResumeLayout();
        }

        private void RemovePath(string path)
        {
            if (_items.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                RebuildGrid();
                QueueSave();
            }
        }

        private void IncrementOpenCount(string path)
        {
            var item = _items.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            if (item != null) { item.OpenCount++; QueueSave(); }
        }

        // ---- Μενού: ίδια διάρθρωση/λειτουργίες με το πραγματικό Stardock Fences ----
        private void ShowContainerMenu(Control owner)
        {
            // ΟΧΙ "using": το Show() δεν μπλοκάρει — immediate Dispose μετά έκανε το μενού να
            // κλείνει πριν προλάβει ο χρήστης να διαλέξει κάτι (flash-and-vanish).
            var menu = new ContextMenuStrip();
            menu.Items.Add(LocalizationManager.T("DCMenu.Rename"), null, (_, _) => RenameContainer());

            var viewMenu = new ToolStripMenuItem(LocalizationManager.T("DCMenu.View"));
            var rollUpItem = new ToolStripMenuItem(LocalizationManager.T("DCMenu.RollUp")) { Checked = IsRolledUp };
            rollUpItem.Click += (_, _) => ToggleRollUp(save: true);
            viewMenu.DropDownItems.Add(rollUpItem);
            var excludeItem = new ToolStripMenuItem(LocalizationManager.T("DCMenu.ExcludeQuickHide")) { Checked = ExcludeFromQuickHide };
            excludeItem.Click += (_, _) => { ExcludeFromQuickHide = !ExcludeFromQuickHide; QueueSave(); };
            viewMenu.DropDownItems.Add(excludeItem);

            var opacityMenu = new ToolStripMenuItem(LocalizationManager.T("DCMenu.Opacity"));
            foreach (int pct in new[] { 100, 85, 70, 55, 40, 25 })
            {
                var opacityItem = new ToolStripMenuItem($"{pct}%") { Checked = Math.Abs(Opacity * 100 - pct) < 1 };
                opacityItem.Click += (_, _) => { Opacity = pct / 100.0; QueueSave(); };
                opacityMenu.DropDownItems.Add(opacityItem);
            }
            viewMenu.DropDownItems.Add(opacityMenu);
            viewMenu.DropDownItems.Add(new ToolStripSeparator());
            viewMenu.DropDownItems.Add(LocalizationManager.T("DCMenu.CopyColor"), null, (_, _) => { try { Clipboard.SetText(ColorTranslator.ToHtml(AccentColor)); } catch { } });
            viewMenu.DropDownItems.Add(LocalizationManager.T("DCMenu.EditColor"), null, (_, _) => EditColor());
            menu.Items.Add(viewMenu);

            var sortMenu = new ToolStripMenuItem(LocalizationManager.T("DCMenu.SortBy"));
            void AddSortOption(string label, ContainerSortMode mode)
            {
                var item = new ToolStripMenuItem(label) { Checked = _sortMode == mode };
                item.Click += (_, _) => { _sortMode = mode; ApplySort(); QueueSave(); };
                sortMenu.DropDownItems.Add(item);
            }
            AddSortOption(LocalizationManager.T("DCMenu.SortNone"), ContainerSortMode.None);
            AddSortOption(LocalizationManager.T("DCMenu.SortName"), ContainerSortMode.Name);
            AddSortOption(LocalizationManager.T("DCMenu.SortSize"), ContainerSortMode.Size);
            AddSortOption(LocalizationManager.T("DCMenu.SortType"), ContainerSortMode.ItemType);
            AddSortOption(LocalizationManager.T("DCMenu.SortModified"), ContainerSortMode.DateModified);
            AddSortOption(LocalizationManager.T("DCMenu.SortCreated"), ContainerSortMode.DateCreated);
            AddSortOption(LocalizationManager.T("DCMenu.SortAdded"), ContainerSortMode.DateAdded);
            AddSortOption(LocalizationManager.T("DCMenu.SortOpened"), ContainerSortMode.TimesOpened);
            sortMenu.DropDownItems.Add(new ToolStripSeparator());
            var organizeMenu = new ToolStripMenuItem(LocalizationManager.T("DCMenu.Organize"));
            var defaultItem = new ToolStripMenuItem(LocalizationManager.T("DCMenu.DefaultContainer")) { Checked = DeskContainerHostEngine.Instance.DefaultContainerId == _containerId };
            defaultItem.Click += (_, _) =>
            {
                DeskContainerHostEngine.Instance.DefaultContainerId = defaultItem.Checked ? null : _containerId;
                QueueSave();
            };
            organizeMenu.DropDownItems.Add(defaultItem);
            organizeMenu.DropDownItems.Add(LocalizationManager.T("DCMenu.ManageRules"), null, (_, _) => ShowSortingRulesInfo());
            sortMenu.DropDownItems.Add(organizeMenu);
            menu.Items.Add(sortMenu);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(LocalizationManager.T("DCMenu.AddFiles"), null, (_, _) => AddViaDialog());
            var lockItem = new ToolStripMenuItem(IsLocked ? LocalizationManager.T("DCMenu.Unlock") : LocalizationManager.T("DCMenu.Lock"));
            lockItem.Click += (_, _) => { IsLocked = !IsLocked; if (_lockLabelRef != null) _lockLabelRef.Text = IsLocked ? "🔒" : "🔓"; QueueSave(); };
            menu.Items.Add(lockItem);
            menu.Items.Add(LocalizationManager.T("DCMenu.Configure"), null, (_, _) => ShowContainerSettings());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(LocalizationManager.T("DCMenu.Close"), null, (_, _) => Close());
            menu.Show(owner, new Point(0, owner.Height));
        }

        // ΔΙΟΡΘΩΣΗ συμπεριφοράς (ζητήθηκε ρητά "να γίνεται με hover"): το "Roll-up container" δεν
        // είναι πια ένα χειροκίνητο toggle (διπλό-κλικ = μόνιμη εναλλαγή) — είναι μια ΛΕΙΤΟΥΡΓΙΑ
        // που ενεργοποιείς/απενεργοποιείς, ίδια με το πραγματικό Stardock Fences: όσο είναι
        // ενεργή, το container μαζεύεται μόνο του στη γραμμή τίτλου όταν το ποντίκι φύγει, και
        // ξανανοίγει μόνο του όταν περάσεις το ποντίκι από πάνω. IsRolledUp = η ΛΕΙΤΟΥΡΓΙΑ είναι
        // ενεργή (αποθηκεύεται)· _hoverExpanded = παροδική κατάσταση "αυτή τη στιγμή ανοιχτό".
        private bool _hoverExpanded = true;

        private void ToggleRollUp(bool save)
        {
            IsRolledUp = !IsRolledUp;
            if (IsRolledUp)
            {
                EnsureHoverPolling();
                bool hoveredNow = Bounds.Contains(Cursor.Position);
                SetHoverExpanded(hoveredNow, animate: true);
            }
            else
            {
                _hoverPollTimer?.Stop();
                SetHoverExpanded(true, animate: true);
            }
            if (save) QueueSave();
        }

        // Ελέγχει κάθε 150ms αν ο κέρσορας βρίσκεται μέσα στα όρια του container — ΟΧΙ
        // MouseEnter/MouseLeave του ίδιου του Form, γιατί κάθε παιδί control (titleBar, _grid,
        // κάθε ContainerIconTile) έχει το ΔΙΚΟ του native handle στα WinForms· μετακίνηση του
        // ποντικιού ΑΝΑΜΕΣΑ σε παιδιά προκαλεί ψευδή, στιγμιαία Leave+Enter στο ίδιο το Form —
        // ακριβώς η αιτία του αναφερόμενου "δεν γίνεται σωστά". Ο έλεγχος με Cursor.Position
        // πάνω στο πραγματικό ορθογώνιο του παραθύρου δεν έχει αυτό το πρόβλημα.
        private void EnsureHoverPolling()
        {
            if (_hoverPollTimer != null) { _hoverPollTimer.Start(); return; }
            _hoverPollTimer = new System.Windows.Forms.Timer { Interval = 150 };
            _hoverPollTimer.Tick += (_, _) =>
            {
                if (!IsRolledUp) { _hoverPollTimer?.Stop(); return; }
                bool hovering = Bounds.Contains(Cursor.Position);
                if (hovering != _hoverExpanded) SetHoverExpanded(hovering, animate: true);
            };
            _hoverPollTimer.Start();
        }

        private void SetHoverExpanded(bool expanded, bool animate)
        {
            _hoverExpanded = expanded;
            if (expanded && Height > TitleBarHeight) _expandedHeight = Height;
            int targetHeight = expanded ? (_expandedHeight > TitleBarHeight ? _expandedHeight : 220) : TitleBarHeight;

            // Το _grid εμφανίζεται ΠΡΙΝ την επέκταση (ώστε το περιεχόμενο να "μεγαλώνει" μαζί με
            // το ύψος) αλλά κρύβεται ΜΕΤΑ την κύλιση προς τα πάνω (ώστε να φαίνεται να γλιστράει
            // κάτω από τη γραμμή τίτλου αντί να εξαφανίζεται απότομα πριν καν κινηθεί το παράθυρο).
            if (expanded) _grid.Visible = true;
            AnimateToHeight(targetHeight, animate, onComplete: () =>
            {
                if (!expanded) _grid.Visible = false;
            });
        }

        // Ομαλή κύλιση ύψους (ζητήθηκε ρητά — πριν γινόταν στιγμιαίο άλμα) αντί για απότομη. Ίδια
        // τεχνική (ease-out κύβος, 10 βήματα/12ms) με το AnimateSidebarWidth του MainWindow.
        private void AnimateToHeight(int targetHeight, bool animate, Action? onComplete = null)
        {
            if (!animate || !IsHandleCreated)
            {
                Height = targetHeight;
                onComplete?.Invoke();
                return;
            }

            _rollUpAnimTimer?.Stop();
            _rollUpAnimTimer?.Dispose();
            int startHeight = Height;
            const int totalSteps = 10;
            int step = 0;
            _rollUpAnimTimer = new System.Windows.Forms.Timer { Interval = 12 };
            _rollUpAnimTimer.Tick += (_, _) =>
            {
                step++;
                double t = Math.Min(1.0, step / (double)totalSteps);
                double eased = 1 - Math.Pow(1 - t, 3);
                Height = (int)(startHeight + (targetHeight - startHeight) * eased);
                if (t >= 1.0)
                {
                    Height = targetHeight;
                    _rollUpAnimTimer?.Stop();
                    onComplete?.Invoke();
                }
            };
            _rollUpAnimTimer.Start();
        }

        private void ApplySort()
        {
            if (_sortMode != ContainerSortMode.None)
            {
                IEnumerable<ContainerItem> sorted = _sortMode switch
                {
                    ContainerSortMode.Name => _items.OrderBy(i => Path.GetFileName(i.Path), StringComparer.OrdinalIgnoreCase),
                    ContainerSortMode.Size => _items.OrderBy(i => SafeFileSize(i.Path)),
                    ContainerSortMode.ItemType => _items.OrderBy(i => Path.GetExtension(i.Path), StringComparer.OrdinalIgnoreCase),
                    ContainerSortMode.DateModified => _items.OrderBy(i => SafeLastWrite(i.Path)),
                    ContainerSortMode.DateCreated => _items.OrderBy(i => SafeCreated(i.Path)),
                    ContainerSortMode.DateAdded => _items.OrderBy(i => i.DateAdded),
                    ContainerSortMode.TimesOpened => _items.OrderByDescending(i => i.OpenCount),
                    _ => _items,
                };
                _items = sorted.ToList();
            }
            RebuildGrid();
        }

        private static long SafeFileSize(string path) { try { return File.Exists(path) ? new FileInfo(path).Length : 0; } catch { return 0; } }
        private static DateTime SafeLastWrite(string path) { try { return File.GetLastWriteTime(path); } catch { return DateTime.MinValue; } }
        private static DateTime SafeCreated(string path) { try { return File.GetCreationTime(path); } catch { return DateTime.MinValue; } }

        private void EditColor()
        {
            using var dlg = new ColorDialog { Color = AccentColor, FullOpen = true };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                AccentColor = dlg.Color;
                ApplyGlassBlur();
                Invalidate(true);
                QueueSave();
            }
        }

        private void ShowSortingRulesInfo()
        {
            MessageBox.Show(this,
                LocalizationManager.T("DeskContainer.SortingRulesInfo"),
                LocalizationManager.T("DeskContainer.SortingRulesTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowContainerSettings()
        {
            using var dialog = new Form
            {
                Text = LocalizationManager.T("DeskContainer.SettingsTitle"),
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(360, 200),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false
            };
            var info = new Label { Text = string.Format(LocalizationManager.T("DeskContainer.SettingsInfoFormat"), _title, _items.Count, _sortMode, (int)(Opacity * 100)), AutoSize = true, Location = new Point(16, 16) };
            dialog.Controls.Add(info);
            var resetColorBtn = new Button { Text = LocalizationManager.T("DeskContainer.ResetColor"), AutoSize = true, Location = new Point(16, 100) };
            resetColorBtn.Click += (_, _) => { AccentColor = Color.FromArgb(0, 170, 255); ApplyGlassBlur(); Invalidate(true); QueueSave(); };
            dialog.Controls.Add(resetColorBtn);
            var ok = new Button { Text = LocalizationManager.T("Common.Close"), DialogResult = DialogResult.OK, Location = new Point(16, 140) };
            dialog.Controls.Add(ok);
            dialog.AcceptButton = ok;
            dialog.ShowDialog(this);
        }

        private void AddViaDialog()
        {
            using var dialog = new OpenFileDialog { Multiselect = true, Title = "Add files to DeskContainer", CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                AddPaths(dialog.FileNames);
        }

        private void RenameContainer()
        {
            using var dialog = new Form { Text = LocalizationManager.T("DCMenu.RenameTitle"), StartPosition = FormStartPosition.CenterParent, Size = new Size(420, 150), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
            var box = new TextBox { Text = _title, Dock = DockStyle.Top, Margin = new Padding(12) };
            var ok = new Button { Text = LocalizationManager.T("Common.OK"), DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 34 };
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
            ApplyRoundedCorners();
        }

        // Στρογγυλεμένες γωνίες στο container — αισθητική κατεύθυνση από το Portals: Desktop
        // Organization (ζητήθηκε ρητά "σαν μπουσούλα"), το οποίο υποστηρίζει ρυθμιζόμενη
        // ακτίνα στρογγυλέματος ανά container.
        private void ApplyRoundedCorners()
        {
            if (Width > 0 && Height > 0) UiTheme.ApplyRoundedRegion(this, 10);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Το χειροκίνητο reposition του ✕/🔒 δεν χρειάζεται πια — είναι πλέον Dock=Right
            // παιδιά του titleBar, αυτο-τοποθετούνται μόνα τους. Το Region όμως ΠΡΕΠΕΙ να
            // ξαναϋπολογίζεται σε κάθε αλλαγή μεγέθους, αλλιώς οι στρογγυλεμένες γωνίες μένουν
            // στο αρχικό μέγεθος και το container "κόβεται" λάθος μετά από resize.
            ApplyRoundedCorners();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            QueueSave();
        }

        // Το AccentColor αναμειγνύεται ελαφρά μέσα στο σκούρο γυάλινο φόντο (DWM blur) — ίδια
        // ιδέα με το accent-color tinting των πραγματικών Fences.
        private void ApplyGlassBlur()
        {
            var baseColor = Color.FromArgb(0x20, 0x20, 0x20);
            const double t = 0.16;
            int r = (int)(baseColor.R + (AccentColor.R - baseColor.R) * t);
            int gCh = (int)(baseColor.G + (AccentColor.G - baseColor.G) * t);
            int b = (int)(baseColor.B + (AccentColor.B - baseColor.B) * t);
            int gradientColor = unchecked((int)0x99000000) | (r << 16) | (gCh << 8) | b;

            var accent = new ACCENT_POLICY { AccentState = 3, GradientColor = gradientColor };
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
                var state = new DeskContainerState
                {
                    X = Location.X,
                    Y = Location.Y,
                    Width = Width,
                    Height = IsRolledUp ? _expandedHeight : Height,
                    Title = _title,
                    Items = _items,
                    IsLocked = IsLocked,
                    IsRolledUp = IsRolledUp,
                    ExcludeFromQuickHide = ExcludeFromQuickHide,
                    PlaceNewIconsHereByDefault = DeskContainerHostEngine.Instance.DefaultContainerId == _containerId,
                    AccentColorArgb = AccentColor.ToArgb(),
                    WindowOpacity = Opacity,
                    SortMode = _sortMode.ToString()
                };
                MotionDesk.Services.AtomicFile.WriteAllText(path, JsonSerializer.Serialize(state));
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
                        _expandedHeight = Height;
                        Location = new Point(state.X, state.Y);
                        _title = state.Title ?? _title;
                        IsLocked = state.IsLocked;
                        ExcludeFromQuickHide = state.ExcludeFromQuickHide;
                        AccentColor = Color.FromArgb(state.AccentColorArgb == 0 ? Color.FromArgb(0, 170, 255).ToArgb() : state.AccentColorArgb);
                        Opacity = state.WindowOpacity is > 0 and <= 1 ? state.WindowOpacity : 1.0;
                        Enum.TryParse(state.SortMode, out _sortMode);
                        if (state.Items != null) _items = state.Items.Where(i => File.Exists(i.Path) || Directory.Exists(i.Path)).ToList();
                        else if (state.Paths != null) _items = state.Paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(p => new ContainerItem { Path = p }).ToList();
                        if (state.PlaceNewIconsHereByDefault) DeskContainerHostEngine.Instance.DefaultContainerId = _containerId;
                        if (state.IsRolledUp)
                        {
                            // Ξεκινάει μαζεμένο (όπως τα πραγματικά Fences) — θα ανοίξει μόνο
                            // του μόλις περάσει το ποντίκι από πάνω (EnsureHoverPolling το
                            // ελέγχει κάθε 150ms). Καμία κύλιση/animation εδώ: το container
                            // δεν είναι καν ορατό ακόμα κατά τη φόρτωση.
                            IsRolledUp = true;
                            _hoverExpanded = false;
                            Height = TitleBarHeight;
                            _grid.Visible = false;
                            EnsureHoverPolling();
                        }
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
        private readonly Action _onOpen;
        private readonly Icon? _icon;
        private bool _hover;

        public ContainerIconTile(string path, Action<string> onRemove, Action onOpen)
        {
            _path = path;
            _onRemove = onRemove;
            _onOpen = onOpen;
            // Μεγαλύτερο πλακίδιο (ζητήθηκε ρητά — τα εικονίδια ήταν πολύ μικρά): πλήρες 48x48
            // εικονίδιο (όχι το 32x32 του Icon.ExtractAssociatedIcon) + χώρος για 2 γραμμές κειμένου.
            Size = new Size(92, 100);
            Margin = new Padding(4);
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            _icon = LoadLargeIcon(path);
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
                    menu.Items.Add(LocalizationManager.T("DCMenu.Open"), null, (_, _) => LaunchSelf());
                    menu.Items.Add(LocalizationManager.T("DCMenu.QuickLook"), null, (_, _) => QuickLookPreview.Show(_path));
                    menu.Items.Add(LocalizationManager.T("DCMenu.Remove"), null, (_, _) => _onRemove(_path));
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
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_path) { UseShellExecute = true });
                _onOpen();
            }
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
            if (_icon != null) g.DrawIcon(_icon, new Rectangle((Width - 48) / 2, 8, 48, 48));

            string name = Path.GetFileName(_path);
            using var font = new Font("Segoe UI", 8f);
            using var textBrush = new SolidBrush(Color.White);
            var textRect = new RectangleF(2, 60, Width - 4, Height - 62);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit };
            g.DrawString(name, font, textBrush, textRect, sf);
        }

        // Icon.ExtractAssociatedIcon γυρνάει πάντα 32x32 — μικρό, θολωμένο αν το τεντώσεις σε
        // μεγαλύτερο μέγεθος. SHGetFileInfo με SHGFI_LARGEICON δίνει το πραγματικό "μεγάλο"
        // εικονίδιο του συστήματος (48x48 στις περισσότερες ρυθμίσεις DPI), ίδιο μέγεθος με τα
        // κανονικά εικονίδια της επιφάνειας εργασίας των Windows.
        private static Icon? LoadLargeIcon(string path)
        {
            try
            {
                var shinfo = new SHFILEINFO();
                IntPtr result = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
                if (result != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    using var handleIcon = Icon.FromHandle(shinfo.hIcon);
                    var cloned = (Icon)handleIcon.Clone();
                    DestroyIcon(shinfo.hIcon);
                    return cloned;
                }
            }
            catch { }
            try { return Icon.ExtractAssociatedIcon(path); } catch { return null; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }
        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_LARGEICON = 0x0;
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);

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
        // "Paths" διατηρείται μόνο για ανάγνωση παλιότερων αποθηκευμένων αρχείων (πριν προστεθεί
        // per-item metadata) — νέες αποθηκεύσεις γράφουν πάντα στο "Items".
        public string[]? Paths { get; set; }
        public List<ContainerItem>? Items { get; set; }
        public bool IsLocked { get; set; }
        public bool IsRolledUp { get; set; }
        public bool ExcludeFromQuickHide { get; set; }
        public bool PlaceNewIconsHereByDefault { get; set; }
        public int AccentColorArgb { get; set; }
        public double WindowOpacity { get; set; } = 1.0;
        public string SortMode { get; set; } = "None";
    }

    // Snapshot ενός DeskContainer για αποθήκευση μέσα σε ένα WorkspaceProfile (π.χ. "Last
    // Session") — ίδιο μοτίβο με το WidgetSnapshot, ώστε τα DeskContainers να ξανανοίγουν
    // αυτόματα στο επόμενο άνοιγμα της εφαρμογής, ακριβώς όπως ήδη συμβαίνει με τα widgets.
    public sealed class DeskContainerSnapshot
    {
        public string ContainerId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class DeskContainerHostEngine
    {
        private static DeskContainerHostEngine? _instance;
        public static DeskContainerHostEngine Instance => _instance ??= new DeskContainerHostEngine();

        private readonly List<DeskContainerWindow> _active = new();
        public IReadOnlyCollection<DeskContainerWindow> GetActiveContainers() => _active;

        // Ποιο container είναι σημειωμένο ως "προεπιλογή για νέα εικονίδια" (μενού Organize) —
        // ένα μόνο container μπορεί να είναι προεπιλογή τη φορά, όπως στα πραγματικά Fences.
        public string? DefaultContainerId { get; set; }

        public IReadOnlyList<DeskContainerSnapshot> GetSnapshots() => _active
            .Where(c => !c.IsDisposed)
            .Select(c => new DeskContainerSnapshot { ContainerId = c.ContainerId, Title = c.ContainerTitle, X = c.Location.X, Y = c.Location.Y, Width = c.Width, Height = c.Height })
            .ToArray();

        public void RestoreSnapshots(IEnumerable<DeskContainerSnapshot> snapshots)
        {
            CloseAll();
            foreach (var snapshot in snapshots.Where(s => !string.IsNullOrWhiteSpace(s.ContainerId)))
                SpawnContainer(snapshot.ContainerId, snapshot.Title, snapshot.X, snapshot.Y, Math.Max(200, snapshot.Width), Math.Max(140, snapshot.Height));
        }

        public void CloseAll()
        {
            foreach (var c in new List<DeskContainerWindow>(_active))
                if (!c.IsDisposed) c.Close();
            _active.Clear();
        }

        // Καλείται από το DesktopIconVisibilityEngine όταν ο χρήστης κάνει διπλό-κλικ σε κενό
        // σημείο της επιφάνειας εργασίας — τα πραγματικά Fences κρύβουν/ξαναδείχνουν επίσης όλα
        // τα δικά τους containers μαζί με τα εικονίδια σε αυτή τη χειρονομία, εκτός από όσα
        // container έχουν σημειωθεί "Exclude from quick-hide".
        public void SetQuickHidden(bool hidden)
        {
            foreach (var c in _active)
            {
                if (c.IsDisposed || c.ExcludeFromQuickHide) continue;
                c.Visible = !hidden;
            }
        }

        public DeskContainerWindow SpawnContainer(string containerId, string title, int x, int y, int width, int height)
        {
            var existing = _active.Find(c => c.Tag as string == containerId);
            if (existing != null && !existing.IsDisposed) { existing.BringToFront(); return existing; }

            // Ίδια λογική με τα widgets/wallpaper — ζητήθηκε ρητά.
            if (!StartupManager.IsStartupEnabled()) StartupManager.SetStartup(true);

            var container = new DeskContainerWindow(containerId, title, x, y, width, height) { Tag = containerId };
            _active.Add(container);
            container.FormClosed += (s, e) => _active.Remove(container);
            container.Show();
            return container;
        }
    }
}
