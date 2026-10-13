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
        private const int TitleBarHeight = 36;
        private readonly string _containerId;
        private string _title;
        private List<ContainerItem> _items = new();
        private readonly FlowLayoutPanel _grid;
        private readonly System.Windows.Forms.Timer _saveTimer;
        private System.Windows.Forms.Timer? _rollUpAnimTimer;
        private System.Windows.Forms.Timer? _hoverPollTimer;
        private Label? _lockLabelRef;
        private string _filter = "";
        private Panel? _titleBarRef;
        public string AutoCollectExtensions { get; set; } = "";
        private long _lastTitleDownTick;
        private Point _lastTitleDownPos;
        private int _expandedHeight;
        private ContainerSortMode _sortMode = ContainerSortMode.None;
        // ΔΙΟΡΘΩΣΗ "η διαφάνεια να αφορά το παράθυρο όχι το περιεχόμενο": δεν χρησιμοποιούμε πια
        // Form.Opacity (ξεθωριάζει μαζί και το κείμενο/εικονίδια) — βλ. MotionDesk.UI.GlassBlur.
        private double _backgroundOpacity = 1.0;

        public string ContainerId => _containerId;
        public string ContainerTitle => _title;
        public bool IsLocked { get; set; }
        public bool IsRolledUp { get; private set; }
        public bool ExcludeFromQuickHide { get; set; }
        public Color AccentColor { get; set; } = Color.FromArgb(0, 170, 255);
        // Ζητήθηκε ρητά "να μπορεί να αλλαχθεί το μέγεθος και η γραμματοσειρά του τίτλου".
        public string TitleFontFamily { get; set; } = "Segoe UI Semibold";
        public float TitleFontSize { get; set; } = 10.5f;

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
            MinimumSize = new Size(180, 140);
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
            _titleBarRef = titleBar;
            titleBar.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                // ΔΙΟΡΘΩΣΗ: το πρώτο κλικ ξεκινά το native move loop των Windows (WM_NCLBUTTONDOWN/HTCAPTION), που "καταπίνει" το
                // mouse-up — έτσι το WinForms DoubleClick event δεν εκπεμπόταν ποτέ και η μετονομασία με διπλό κλικ δεν δούλευε.
                // Ανιχνεύουμε το διπλό κλικ εδώ (χρόνος + απόσταση από το προηγούμενο πάτημα) πριν ξεκινήσει το drag.
                long now = Environment.TickCount64;
                bool isDouble = e.Clicks >= 2 ||
                    (now - _lastTitleDownTick <= SystemInformation.DoubleClickTime &&
                     Math.Abs(e.X - _lastTitleDownPos.X) <= SystemInformation.DoubleClickSize.Width &&
                     Math.Abs(e.Y - _lastTitleDownPos.Y) <= SystemInformation.DoubleClickSize.Height);
                if (isDouble)
                {
                    _lastTitleDownTick = 0;
                    RenameContainer();
                    return;
                }
                _lastTitleDownTick = now;
                _lastTitleDownPos = e.Location;
                if (!IsLocked) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0); }
            };
            // (Διπλό-κλικ στη γραμμή τίτλου = μετονομασία, στυλ Fences — βλ. MouseDown παραπάνω.)
            titleBar.Paint += (_, e) =>
            {
                // Γραμμή τίτλου με ΔΙΚΗ ΤΗΣ απόχρωση (σκούρο με τόνο του accent, ημιδιαφανές) ώστε να ξεχωρίζει από το υπόλοιπο
                // παράθυρο, όπως στα Fences.
                var header = Color.FromArgb(185,
                    (int)(AccentColor.R * 0.35 + 8), (int)(AccentColor.G * 0.35 + 10), (int)(AccentColor.B * 0.35 + 16));
                using (var headerBrush = new SolidBrush(header)) e.Graphics.FillRectangle(headerBrush, 0, 0, titleBar.Width, TitleBarHeight);
                using var titleBrush = new SolidBrush(Color.White);
                using var font = new Font(TitleFontFamily, TitleFontSize, FontStyle.Bold);
                // Κεντραρισμένος τίτλος: συμμετρική δέσμευση χώρου 2 κουμπιών αριστερά/δεξιά ώστε το κέντρο να είναι το κέντρο του παραθύρου.
                var textRect = new RectangleF(TitleBarHeight * 2, 0, Math.Max(20, titleBar.Width - TitleBarHeight * 4), TitleBarHeight);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                // title + item count ("Projects  12", or "3 / 12" while a search is active) — the count in a quieter colour
                string count = _filter.Length > 0 ? $"{_grid.Controls.Count} / {_items.Count}" : _items.Count.ToString();
                var titleSize = e.Graphics.MeasureString(_title, font, (int)textRect.Width, sf);
                using var countFont = new Font("Segoe UI", 9f);
                var countSize = e.Graphics.MeasureString(count, countFont);
                float totalW = Math.Min(textRect.Width, titleSize.Width + 10 + countSize.Width);
                float startX = textRect.X + (textRect.Width - totalW) / 2f;
                var titleRect = new RectangleF(startX, textRect.Y, Math.Max(10, totalW - 10 - countSize.Width), textRect.Height);
                using var leftSf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                e.Graphics.DrawString(_title, font, titleBrush, titleRect, leftSf);
                using var countBrush = new SolidBrush(Color.FromArgb(150, 255, 255, 255));
                e.Graphics.DrawString(count, countFont, countBrush, new RectangleF(titleRect.Right + 10, textRect.Y, countSize.Width + 2, textRect.Height), leftSf);
                using var separator = new Pen(Color.FromArgb(160, AccentColor), 2f);
                e.Graphics.DrawLine(separator, 0, TitleBarHeight - 1, titleBar.Width, TitleBarHeight - 1);
            };
            titleBar.Resize += (_, _) => titleBar.Invalidate();

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
            menuLabel.Click += (_, _) => ShowContainerSettingsDialog();

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
            // Ζητήθηκε ξανά ρητά ("εξακολουθεί να επικαλύπτει"): ο κώδικας Dock/Z-order ήταν ήδη
            // σωστός (titleBar πριν το _grid, Dock=Top/Fill, PerformLayout() αμέσως μετά) — το
            // προηγούμενο πάνω padding (12px) απλώς δεν άφηνε αρκετό οπτικό κενό σε όλες τις
            // αναλύσεις/DPI. Αυξήθηκε σε 18px για καθαρό, αδιαμφισβήτητο κενό κάτω από τη γραμμή τίτλου.
            _grid = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoScroll = true, Padding = new Padding(8, 18, 8, 8), AllowDrop = true };
            // Τα DeskContainers είναι πάντα σκούρο γυαλί (dark glass), ανεξάρτητα από το θέμα της
            // εφαρμογής — το scrollbar ακολουθεί πάντα το σκούρο στυλ.
            GlassBlur.ApplyScrollbarTheme(_grid, dark: true);
            _grid.DragEnter += Grid_DragEnter;
            _grid.DragDrop += Grid_DragDrop;
            Controls.Add(_grid);
            PerformLayout();

            DragEnter += Grid_DragEnter;
            DragDrop += Grid_DragDrop;

            // ΔΙΟΡΘΩΣΗ "δεν λειτουργεί η προσαρμογή μεγέθους τραβώντας τις άκρες": το container
            // ΔΕΝ είχε ΚΑΝΕΝΑ μηχανισμό αλλαγής μεγέθους (FormBorderStyle=None αφαιρεί και τη
            // σχετική λαβή των Windows) — όχι bug σε υπάρχον μηχανισμό, απλά δεν υπήρχε καθόλου.
            // Ίδιο ακριβώς "κόλπο" (WM_NCLBUTTONDOWN/HTBOTTOMRIGHT) με το ήδη αποδεδειγμένο
            // BeginNativeResize των widgets (WidgetEngine.cs) — αναθέτει το resize στον ίδιο τον
            // Windows compositor αντί για χειροκίνητο MouseMove tracking.
            var resizeGrip = new Label
            {
                Text = "⤡", AutoSize = false, Size = new Size(18, 18), Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(140, 255, 255, 255), BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9), Cursor = Cursors.SizeNWSE
            };
            resizeGrip.Location = new Point(Width - resizeGrip.Width - 2, Height - resizeGrip.Height - 2);
            resizeGrip.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left && !IsLocked) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HTBOTTOMRIGHT, 0); } };
            Controls.Add(resizeGrip);
            resizeGrip.BringToFront();
            Resize += (_, _) => { if (!IsRolledUp) { _expandedHeight = Height; QueueSave(); } };

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
            // ΔΙΟΡΘΩΣΗ scroll-reset (ίδιο μοτίβο με RefreshActiveList στο MainWindow): το _grid είναι
            // το ίδιο το AutoScroll FlowLayoutPanel, οπότε το Controls.Clear()+rebuild που τρέχει σε
            // κάθε προσθήκη/αφαίρεση/ταξινόμηση επαναφέρει σιωπηλά την κύλιση στην κορυφή — αισθητό σε
            // containers με πολλά εικονίδια όταν ο χρήστης είναι κυλισμένος κάτω.
            var savedScroll = _grid.AutoScrollPosition;
            _grid.SuspendLayout();
            // Controls.Clear() ΔΕΝ κάνει Dispose — κάθε tile κρατά native Icon (HICON clone) + handle, και
            // η RebuildGrid τρέχει σε κάθε προσθήκη/αφαίρεση/ταξινόμηση. Ρητό Dispose για να μη διαρρέουν.
            foreach (Control old in _grid.Controls.Cast<Control>().ToList()) old.Dispose();
            _grid.Controls.Clear();
            foreach (var item in _items.ToArray())
            {
                string path = item.Path;
                if (_filter.Length > 0 && Path.GetFileName(path).IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var tile = new ContainerIconTile(path, RemovePath, () => IncrementOpenCount(path));
                // ΔΙΟΡΘΩΣΗ "δεν λειτουργεί το drag&drop": οι χειριστές ήταν μόνο στο _grid/στο
                // Form, όχι στα ίδια τα ContainerIconTile — αφού τα πλακίδια γεμίζουν όλο το grid,
                // ΚΑΘΕ drop πάνω σε ήδη-υπάρχον εικονίδιο (δηλ. σχεδόν πάντα, αφού αυτό είναι το
                // βασικό σενάριο χρήσης ενός container) πήγαινε στο tile, όχι στο γονικό
                // FlowLayoutPanel, και αγνοούνταν σιωπηλά (το Panel δεν είχε AllowDrop/handlers).
                tile.AllowDrop = true;
                tile.DragEnter += Grid_DragEnter;
                tile.DragDrop += Grid_DragDrop;
                _grid.Controls.Add(tile);
            }
            _grid.ResumeLayout();
            _grid.AutoScrollPosition = new Point(-savedScroll.X, -savedScroll.Y);
            _titleBarRef?.Invalidate();       // the item count in the title follows the list
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

        // ΔΙΟΡΘΩΣΗ ζητήματος "να μην θυμίζει Fences": το παλιό ContextMenuStrip flyout (το οποίο
        // ΑΥΤΟ ΤΟ ΙΔΙΟ έδειχνε στο χρήστη screenshots Stardock Fences σαν αναφορά) αντικαταστάθηκε
        // από ένα κανονικό pop-up παράθυρο με όλες τις ρυθμίσεις οργανωμένες σε tabs. Οι ίδιες
        // ακριβώς ενέργειες/μέθοδοι (RenameContainer, ToggleRollUp, ApplyGlassBlur, EditColor,
        // ApplySort, EditAutoCollectRule, AddViaDialog, FindInContainer) παραμένουν ως έχουν.
        // ΔΙΟΡΘΩΣΗ "επικαλυπτόμενα κουμπιά": η προηγούμενη έκδοση τοποθετούσε κουμπιά/dropdown με
        // χειροκίνητο Location (π.χ. δεύτερο κουμπί σε x=150 υποθέτοντας ότι το πρώτο χωράει σε
        // 134px) — ελληνικό κείμενο σε AutoSize κουμπί είναι συχνά φαρδύτερο απ' όσο υπολογίστηκε,
        // οπότε τα κουμπιά επικαλύπτονταν οπτικά. Ο χρήστης ζήτησε να μελετηθεί πώς το αδελφό
        // project GearWin λύνει το ίδιο πρόβλημα· εκεί (WPF) κάθε γραμμή είναι ένα Grid/StackPanel
        // που κατανέμει χώρο αυτόματα, ΠΟΤΕ απόλυτες συντεταγμένες pixel. Το ακριβές WinForms
        // αντίστοιχο: κάθε "γραμμή" εδώ είναι πλέον ένα οριζόντιο FlowLayoutPanel (AutoSize, τα
        // παιδιά παίρνουν το δικό τους φυσικό πλάτος και σειριακά ο ένας μετά τον άλλο με Margin —
        // ΠΟΤΕ δεν μπορούν να επικαλυφθούν, όσο μεγάλο κι αν είναι το κείμενο/η γλώσσα), και κάθε
        // tab είναι ένα κάθετο FlowLayoutPanel από τέτοιες γραμμές. Ίδιο μοτίβο να χρησιμοποιείται
        // σε ΚΑΘΕ μελλοντικό dialog αντί για Location-based layout.
        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            foreach (var c in controls) { c.Margin = new Padding(0, 0, 10, 0); row.Controls.Add(c); }
            return row;
        }

        private void ShowContainerSettingsDialog()
        {
            using var dialog = new Form
            {
                Text = LocalizationManager.T("DeskContainer.SettingsTitle"),
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(460, 380),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary
            };

            var tabs = new TabControl { Dock = DockStyle.Fill };

            TabPage NewTab(string titleKey)
            {
                var page = new TabPage(LocalizationManager.T(titleKey)) { BackColor = UiTheme.Surface };
                var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14) };
                MotionDesk.UI.GlassBlur.ApplyScrollbarTheme(stack, UiTheme.Surface.GetBrightness() < 0.5f);
                page.Controls.Add(stack);
                return page;
            }
            FlowLayoutPanel StackOf(TabPage page) => (FlowLayoutPanel)page.Controls[0];

            // --- Γενικά ---
            var tabGeneral = NewTab("DeskContainer.TabGeneral");
            var nameBox = new TextBox { Text = _title, Width = 220 };
            var renameBtn = new Button { Text = LocalizationManager.T("Common.Save"), AutoSize = true };
            renameBtn.Click += (_, _) => { var t = nameBox.Text.Trim(); if (t.Length > 0 && t != _title) { _title = t; _titleBarRef?.Invalidate(); QueueSave(); } };

            var lockCheck = new FlatCheckBox { Text = LocalizationManager.T("DCMenu.Lock"), Checked = IsLocked, AutoSize = true, BackColor = UiTheme.Surface };
            lockCheck.CheckedChanged += (_, _) => { IsLocked = lockCheck.Checked; if (_lockLabelRef != null) _lockLabelRef.Text = IsLocked ? "🔒" : "🔓"; QueueSave(); };

            var excludeCheck = new FlatCheckBox { Text = LocalizationManager.T("DCMenu.ExcludeQuickHide"), Checked = ExcludeFromQuickHide, AutoSize = true, BackColor = UiTheme.Surface };
            excludeCheck.CheckedChanged += (_, _) => { ExcludeFromQuickHide = excludeCheck.Checked; QueueSave(); };

            var defaultCheck = new FlatCheckBox { Text = LocalizationManager.T("DCMenu.DefaultContainer"), Checked = DeskContainerHostEngine.Instance.DefaultContainerId == _containerId, AutoSize = true, BackColor = UiTheme.Surface };
            defaultCheck.CheckedChanged += (_, _) => { DeskContainerHostEngine.Instance.DefaultContainerId = defaultCheck.Checked ? _containerId : null; QueueSave(); };

            var closeContainerBtn = new Button { Text = LocalizationManager.T("DCMenu.Close"), AutoSize = true };
            closeContainerBtn.Click += (_, _) => { dialog.Close(); Close(); };

            var generalStack = StackOf(tabGeneral);
            generalStack.Controls.Add(Row(new Label { Text = LocalizationManager.T("Common.NameLabel"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 6, 10, 0) }, nameBox, renameBtn));
            generalStack.Controls.Add(Row(lockCheck));
            generalStack.Controls.Add(Row(excludeCheck));
            generalStack.Controls.Add(Row(defaultCheck));
            generalStack.Controls.Add(Row(closeContainerBtn));

            // --- Εμφάνιση ---
            var tabAppearance = NewTab("DeskContainer.TabAppearance");
            var rollUpCheck = new FlatCheckBox { Text = LocalizationManager.T("DCMenu.RollUp"), Checked = IsRolledUp, AutoSize = true, BackColor = UiTheme.Surface };
            rollUpCheck.CheckedChanged += (_, _) => { if (rollUpCheck.Checked != IsRolledUp) ToggleRollUp(save: true); };

            var opacityTrack = new FlatSlider { Minimum = 25, Maximum = 100, SmallChange = 5, LargeChange = 15, Value = Math.Clamp((int)Math.Round(_backgroundOpacity * 100), 25, 100), Width = 260 };
            var opacityValueLabel = new Label { Text = $"{opacityTrack.Value}%", AutoSize = true, ForeColor = UiTheme.TextPrimary, Margin = new Padding(0, 6, 0, 0) };
            opacityTrack.Scroll += (_, _) => { _backgroundOpacity = opacityTrack.Value / 100.0; opacityValueLabel.Text = $"{opacityTrack.Value}%"; ApplyGlassBlur(); QueueSave(); };

            var editColorBtn = new Button { Text = LocalizationManager.T("DCMenu.EditColor"), AutoSize = true };
            editColorBtn.Click += (_, _) => EditColor();
            var copyColorBtn = new Button { Text = LocalizationManager.T("DCMenu.CopyColor"), AutoSize = true };
            copyColorBtn.Click += (_, _) => { try { Clipboard.SetText(ColorTranslator.ToHtml(AccentColor)); } catch { } };
            var resetColorBtn = new Button { Text = LocalizationManager.T("DeskContainer.ResetColor"), AutoSize = true };
            resetColorBtn.Click += (_, _) => { AccentColor = Color.FromArgb(0, 170, 255); ApplyGlassBlur(); Invalidate(true); QueueSave(); };

            // Ζητήθηκε ρητά "να μπορεί να αλλαχθεί το μέγεθος και η γραμματοσειρά του τίτλου".
            var titleFontCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
            var titleFontChoices = new[] { "Segoe UI Semibold", "Segoe UI", "Consolas", "Arial", "Georgia", "Verdana" };
            titleFontCombo.Items.AddRange(titleFontChoices);
            titleFontCombo.SelectedIndex = Math.Max(0, Array.IndexOf(titleFontChoices, TitleFontFamily));
            titleFontCombo.SelectedIndexChanged += (_, _) => { TitleFontFamily = titleFontChoices[titleFontCombo.SelectedIndex]; _titleBarRef?.Invalidate(); QueueSave(); };
            var titleSizeUpDown = new NumericUpDown { Width = 64, Minimum = 7, Maximum = 20, DecimalPlaces = 1, Increment = 0.5m, Value = (decimal)TitleFontSize };
            titleSizeUpDown.ValueChanged += (_, _) => { TitleFontSize = (float)titleSizeUpDown.Value; _titleBarRef?.Invalidate(); QueueSave(); };

            var appearanceStack = StackOf(tabAppearance);
            appearanceStack.Controls.Add(Row(rollUpCheck));
            appearanceStack.Controls.Add(Row(new Label { Text = LocalizationManager.T("DCMenu.Opacity"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 6, 10, 0) }));
            appearanceStack.Controls.Add(Row(opacityTrack, opacityValueLabel));
            appearanceStack.Controls.Add(Row(editColorBtn, copyColorBtn));
            appearanceStack.Controls.Add(Row(resetColorBtn));
            appearanceStack.Controls.Add(Row(new Label { Text = LocalizationManager.T("DeskContainer.TitleFont"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 6, 10, 0) }));
            appearanceStack.Controls.Add(Row(titleFontCombo, titleSizeUpDown));

            // --- Ταξινόμηση ---
            var tabSorting = NewTab("DeskContainer.TabSorting");
            var sortCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
            var sortOptions = new (string Label, ContainerSortMode Mode)[]
            {
                (LocalizationManager.T("DCMenu.SortNone"), ContainerSortMode.None),
                (LocalizationManager.T("DCMenu.SortName"), ContainerSortMode.Name),
                (LocalizationManager.T("DCMenu.SortSize"), ContainerSortMode.Size),
                (LocalizationManager.T("DCMenu.SortType"), ContainerSortMode.ItemType),
                (LocalizationManager.T("DCMenu.SortModified"), ContainerSortMode.DateModified),
                (LocalizationManager.T("DCMenu.SortCreated"), ContainerSortMode.DateCreated),
                (LocalizationManager.T("DCMenu.SortAdded"), ContainerSortMode.DateAdded),
                (LocalizationManager.T("DCMenu.SortOpened"), ContainerSortMode.TimesOpened),
            };
            foreach (var opt in sortOptions) sortCombo.Items.Add(opt.Label);
            sortCombo.SelectedIndex = Math.Max(0, Array.FindIndex(sortOptions, o => o.Mode == _sortMode));
            sortCombo.SelectedIndexChanged += (_, _) => { _sortMode = sortOptions[sortCombo.SelectedIndex].Mode; ApplySort(); QueueSave(); };

            var defaultCheck2 = new FlatCheckBox { Text = LocalizationManager.T("DCMenu.DefaultContainer"), Checked = DeskContainerHostEngine.Instance.DefaultContainerId == _containerId, AutoSize = true, BackColor = UiTheme.Surface };
            defaultCheck2.CheckedChanged += (_, _) => { DeskContainerHostEngine.Instance.DefaultContainerId = defaultCheck2.Checked ? _containerId : null; defaultCheck.Checked = defaultCheck2.Checked; QueueSave(); };
            var autoCollectBtn = new Button { Text = LocalizationManager.T("DCMenu.AutoCollect"), AutoSize = true };
            autoCollectBtn.Click += (_, _) => EditAutoCollectRule();

            var sortingStack = StackOf(tabSorting);
            sortingStack.Controls.Add(Row(new Label { Text = LocalizationManager.T("DCMenu.SortBy"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 6, 10, 0) }));
            sortingStack.Controls.Add(Row(sortCombo));
            sortingStack.Controls.Add(Row(new Label { Text = LocalizationManager.T("DCMenu.Organize"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 6, 10, 0) }));
            sortingStack.Controls.Add(Row(defaultCheck2));
            sortingStack.Controls.Add(Row(autoCollectBtn));

            // --- Αρχεία ---
            var tabFiles = NewTab("DeskContainer.TabFiles");
            var addFilesBtn = new Button { Text = LocalizationManager.T("DCMenu.AddFiles"), AutoSize = true };
            addFilesBtn.Click += (_, _) => AddViaDialog();
            var findBtn = new Button { Text = LocalizationManager.T("DCMenu.Find"), AutoSize = true };
            findBtn.Click += (_, _) => FindInContainer();
            var clearFindBtn = new Button { Text = LocalizationManager.T("DCMenu.ClearFind"), AutoSize = true, Enabled = _filter.Length > 0 };
            clearFindBtn.Click += (_, _) => { _filter = ""; RebuildGrid(); clearFindBtn.Enabled = false; };

            var filesStack = StackOf(tabFiles);
            filesStack.Controls.Add(Row(addFilesBtn));
            filesStack.Controls.Add(Row(findBtn, clearFindBtn));

            tabs.TabPages.AddRange(new[] { tabGeneral, tabAppearance, tabSorting, tabFiles });
            dialog.Controls.Add(tabs);
            // Ζητήθηκε ρητά "σκιασμένα popup" (MediaPlayerPro) — native DWM drop-shadow.
            dialog.HandleCreated += (_, _) => GlassBlur.ApplyWindowDropShadow(dialog.Handle);
            dialog.ShowDialog(this);
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
            // Ζητήθηκε ρητά "πιο smooth animation" — 10 βήματα/12ms (120ms σύνολο) ήταν αισθητά
            // απότομο. 20 βήματα/10ms (~200ms) με το ίδιο ease-out cubic είναι αισθητά πιο ομαλό
            // χωρίς να γίνεται αργό/κολλημένο.
            const int totalSteps = 20;
            int step = 0;
            // ΔΙΟΡΘΩΣΗ "bug εμφάνισης scrollbar χωρίς να υπάρχει ανάγκη": το _grid είναι
            // AutoScroll — κατά τη διάρκεια της γρήγορης αλλαγής ύψους το WinForms υπολογίζει
            // στιγμιαία (λάθος) ότι το περιεχόμενο ξεπερνάει το ορατό ύψος σε ενδιάμεσα frames,
            // εμφανίζοντας/κρύβοντας scrollbar κάθε λίγα ms. Απενεργοποίηση του AutoScroll ΜΟΝΟ
            // κατά τη διάρκεια του animation (όχι ανά frame) το αποτρέπει τελείως.
            bool wasAutoScroll = _grid.AutoScroll;
            _grid.AutoScroll = false;
            _rollUpAnimTimer = new System.Windows.Forms.Timer { Interval = 10 };
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
                    _grid.AutoScroll = wasAutoScroll;
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

        // Small single-line input dialog (same look as the rename dialog)
        private string? PromptText(string title, string prompt, string initial)
        {
            using var dialog = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(440, 150), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary };
            var label = new Label { Text = prompt, Location = new Point(14, 12), Size = new Size(412, 50), ForeColor = UiTheme.TextSecondary };
            var box = new TextBox { Text = initial, Location = new Point(14, 66), Width = 412 };
            var ok = new Button { Text = LocalizationManager.T("Common.OK"), DialogResult = DialogResult.OK, Location = new Point(250, 104), Size = new Size(86, 30) };
            var cancel = new Button { Text = LocalizationManager.T("Common.Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(342, 104), Size = new Size(86, 30) };
            dialog.Controls.AddRange(new Control[] { label, box, ok, cancel });
            dialog.AcceptButton = ok; dialog.CancelButton = cancel;
            dialog.HandleCreated += (_, _) => GlassBlur.ApplyWindowDropShadow(dialog.Handle);
            return dialog.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
        }

        private void FindInContainer()
        {
            var text = PromptText(LocalizationManager.T("DeskContainer.FindTitle"), LocalizationManager.T("DeskContainer.FindPrompt"), _filter);
            if (text == null) return;
            _filter = text;
            RebuildGrid();
        }

        private void EditAutoCollectRule()
        {
            var text = PromptText(LocalizationManager.T("DeskContainer.AutoCollectTitle"), LocalizationManager.T("DeskContainer.AutoCollectPrompt"), AutoCollectExtensions);
            if (text == null) return;
            AutoCollectExtensions = NormalizeExtensions(text);
            QueueSave();
            if (AutoCollectExtensions.Length > 0) CollectFromDesktop();
        }

        private static string NormalizeExtensions(string raw) =>
            string.Join(", ", raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant())
                .Where(e => e.Length > 0 && e.All(char.IsLetterOrDigit)).Distinct());

        public bool MatchesAutoCollect(string path)
        {
            if (AutoCollectExtensions.Length == 0) return false;
            var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            return ext.Length > 0 && AutoCollectExtensions.Split(',').Any(e => e.Trim() == ext);
        }

        // Adds a desktop file when it matches this container's rule (called by the desktop watcher on the UI thread)
        public void TryAutoCollect(string path)
        {
            if (!IsDisposed && MatchesAutoCollect(path) && File.Exists(path)) AddPaths(new[] { path });
        }

        // Scans the current desktop folders for files that already match the rule
        public void CollectFromDesktop()
        {
            var found = new List<string>();
            foreach (var dir in DeskContainerHostEngine.DesktopFolders())
            {
                try { found.AddRange(Directory.EnumerateFiles(dir).Where(MatchesAutoCollect)); }
                catch (Exception) { /* inaccessible folder */ }
            }
            if (found.Count > 0) AddPaths(found);
        }

        private void AddViaDialog()
        {
            using var dialog = new OpenFileDialog { Multiselect = true, Title = LocalizationManager.T("DeskContainer.AddFilesTitle"), CheckFileExists = true };
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
            dialog.HandleCreated += (_, _) => GlassBlur.ApplyWindowDropShadow(dialog.Handle);
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
            GlassBlur.DisableNativeBorderAccent(Handle);
            GlassBlur.AllowDragDropAcrossUipi(Handle);
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
        private void ApplyGlassBlur() => GlassBlur.Apply(Handle, AccentColor, _backgroundOpacity);

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
                    WindowOpacity = _backgroundOpacity,
                    SortMode = _sortMode.ToString(),
                    AutoCollectExtensions = AutoCollectExtensions,
                    TitleFontFamily = TitleFontFamily,
                    TitleFontSize = TitleFontSize
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
                    var state = JsonSerializer.Deserialize<DeskContainerState>(MotionDesk.Services.AtomicFile.ReadAllText(path));
                    if (state != null)
                    {
                        if (state.Width > 160 && state.Height > 120)
                            Size = new Size(Math.Clamp(state.Width, 200, 1600), Math.Clamp(state.Height, 140, 1000));
                        _expandedHeight = Height;
                        // Αποθηκευμένη θέση σε οθόνη που δεν είναι πια συνδεδεμένη (π.χ. αποσύνδεση εξωτερικής
                        // οθόνης) = container αόρατο για πάντα. Αν δεν τέμνει καμία οθόνη, πάει στην κύρια.
                        var wanted = new Rectangle(state.X, state.Y, Width, Height);
                        Location = Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(wanted))
                            ? new Point(state.X, state.Y)
                            : new Point((Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea.X + 80, (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea.Y + 80);
                        _title = state.Title ?? _title;
                        IsLocked = state.IsLocked;
                        ExcludeFromQuickHide = state.ExcludeFromQuickHide;
                        TitleFontFamily = string.IsNullOrWhiteSpace(state.TitleFontFamily) ? "Segoe UI Semibold" : state.TitleFontFamily;
                        TitleFontSize = state.TitleFontSize is >= 7f and <= 20f ? state.TitleFontSize : 10.5f;
                        AccentColor = Color.FromArgb(state.AccentColorArgb == 0 ? Color.FromArgb(0, 170, 255).ToArgb() : state.AccentColorArgb);
                        _backgroundOpacity = state.WindowOpacity is > 0 and <= 1 ? state.WindowOpacity : 1.0;
                        Enum.TryParse(state.SortMode, out _sortMode);
                        AutoCollectExtensions = state.AutoCollectExtensions ?? "";
                        // ΔΕΝ φιλτράρουμε τα στοιχεία που λείπουν: ένας offline δίσκος/USB/δικτυακός πόρος εξαφάνιζε μόνιμα τα στοιχεία του
                        // (το επόμενο save τα έσβηνε). Μένουν ως εικονίδιο που ο χρήστης αφαιρεί χειροκίνητα.
                        if (state.Items != null) _items = state.Items.ToList();
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

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
        private const int HTBOTTOMRIGHT = 0x11;
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
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
        // File types (extensions, comma-separated) that are collected automatically from the desktop into this container
        public string AutoCollectExtensions { get; set; } = "";
        public string TitleFontFamily { get; set; } = "Segoe UI Semibold";
        public float TitleFontSize { get; set; } = 10.5f;
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

        // ---- desktop watcher: new files matching a container's "auto-collect" rule are added to it automatically
        public static IEnumerable<string> DesktopFolders()
        {
            var user = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.IsNullOrEmpty(user) && Directory.Exists(user)) yield return user;
            if (!string.IsNullOrEmpty(common) && Directory.Exists(common)) yield return common;
        }

        private readonly List<FileSystemWatcher> _desktopWatchers = new();
        private System.Threading.SynchronizationContext? _ui;

        private void EnsureDesktopWatchers()
        {
            if (_desktopWatchers.Count > 0) return;
            _ui = System.Threading.SynchronizationContext.Current;
            foreach (var dir in DesktopFolders())
            {
                try
                {
                    var w = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime, IncludeSubdirectories = false, EnableRaisingEvents = true };
                    w.Created += (_, e) => OnDesktopFile(e.FullPath);
                    w.Renamed += (_, e) => OnDesktopFile(e.FullPath);
                    _desktopWatchers.Add(w);
                }
                catch (Exception) { /* a watcher is a convenience, never fatal */ }
            }
        }

        private void OnDesktopFile(string path)
        {
            // files that are still being written can be locked for a moment: marshal to the UI thread after a short delay
            System.Threading.Tasks.Task.Delay(800).ContinueWith(_ =>
            {
                void Run() { foreach (var c in _active.ToArray()) c.TryAutoCollect(path); }
                if (_ui != null) _ui.Post(_ => Run(), null); else Run();
            });
        }

        public DeskContainerWindow SpawnContainer(string containerId, string title, int x, int y, int width, int height)
        {
            var existing = _active.Find(c => c.Tag as string == containerId);
            if (existing != null && !existing.IsDisposed) { existing.BringToFront(); return existing; }

            // Ίδια λογική με τα widgets/wallpaper — ζητήθηκε ρητά.
            if (!StartupManager.IsStartupEnabled()) StartupManager.SetStartup(true);

            var container = new DeskContainerWindow(containerId, title, x, y, width, height) { Tag = containerId };
            _active.Add(container);
            EnsureDesktopWatchers();
            container.FormClosed += (s, e) => _active.Remove(container);
            container.Show();
            return container;
        }
    }
}
