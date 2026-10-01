using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MotionDesk.UI
{
    // Αντικαθιστά το πλαίσιο ComboBox σε ό,τι αφορά αισθητική — τετράγωνο, "κλασικό Windows"
    // περίγραμμα δεν ταιριάζει με το στρογγυλεμένο, σκούρο look της υπόλοιπης εφαρμογής.
    // Ιστορικά, το owner-draw ενός πραγματικού ComboBox σε αυτό το project είχε σοβαρά bugs
    // (βλ. "Language ComboBox rendered completely blank" — εγκαταλείφθηκε τότε), οπότε εδώ
    // αποφεύγουμε τελείως το ComboBox: ένα στρογγυλεμένο "κουμπί" ανοίγει ένα στυλιζαρισμένο
    // ContextMenuStrip με τις επιλογές — ασφαλές, ήδη αποδεδειγμένο pattern σε αυτή την εφαρμογή
    // (ίδιο με τα μενού widgets/DeskZones/DeskContainers).
    public sealed class FlatComboBox : Panel
    {
        private readonly List<string> _items = new();
        private IReadOnlyList<Image?>? _icons;
        private int _selectedIndex = -1;
        private bool _hover;
        private bool _open;

        public event EventHandler? SelectedIndexChanged;

        public IReadOnlyList<string> Items => _items;

        // Ρυθμίσιμη γραμματοσειρά — π.χ. "Segoe UI Emoji" όταν τα items περιέχουν πραγματικά
        // emoji σημαίες (η προεπιλεγμένη "Segoe UI" δεν τις αποδίδει ως έγχρωμα εικονίδια, απλά
        // ως γράμματα περιφερειακού δείκτη — αυτό ήταν το ζητούμενο πρόβλημα).
        public Font ItemFont { get; set; } = new Font("Segoe UI", 9.5f);

        public int SelectedIndex
        {
            get => _selectedIndex;
            set { if (value == _selectedIndex) return; _selectedIndex = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
        }

        public string? SelectedItem
        {
            get => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;
            set { var i = value == null ? -1 : _items.IndexOf(value); if (i >= 0) SelectedIndex = i; }
        }

        public FlatComboBox()
        {
            Height = 32;
            Width = 200;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; Invalidate(); };
            MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) OpenDropdown(); };
        }

        public void SetItems(IEnumerable<string> items, string? selected = null)
        {
            _items.Clear();
            _items.AddRange(items);
            _icons = null;
            _selectedIndex = selected != null ? _items.IndexOf(selected) : (_items.Count > 0 ? 0 : -1);
            Invalidate();
        }

        // Υπερφόρτωση με προαιρετικό μικρό εικονίδιο ανά αντικείμενο (π.χ. σημαίες γλώσσας) — ΟΧΙ
        // μέσω emoji-στο-κείμενο (βλ. FlagIcons.cs για το γιατί), αλλά μέσω πραγματικού Image,
        // ζωγραφισμένο εδώ ΚΑΙ στο ToolStripMenuItem.Image του dropdown.
        public void SetItems(IEnumerable<string> items, IReadOnlyList<Image?> icons, string? selected = null)
        {
            _items.Clear();
            _items.AddRange(items);
            _icons = icons;
            _selectedIndex = selected != null ? _items.IndexOf(selected) : (_items.Count > 0 ? 0 : -1);
            Invalidate();
        }

        private void OpenDropdown()
        {
            if (_items.Count == 0) return;
            _open = true;
            Invalidate();

            // ΟΧΙ "using": το ContextMenuStrip.Show() δεν μπλοκάρει — επιστρέφει αμέσως ενώ το
            // popup παραμένει ζωντανό ασύγχρονα. Ένα "using" εδώ το Dispose-άρει στο ΤΕΛΟΣ αυτής
            // της μεθόδου, δηλαδή αμέσως μετά το Show(), οπότε το μενού εμφανίζεται για ένα
            // frame και χάνεται πριν προλάβει ο χρήστης να κάνει κλικ σε κάποιο item. Το
            // απελευθερώνουμε σωστά μέσω Closed, αφού πραγματικά κλείσει.
            var menu = new ContextMenuStrip { Renderer = new FlatMenuRenderer(), BackColor = UiTheme.Surface, ShowImageMargin = _icons != null };
            menu.Font = ItemFont;
            // Ένα ToolStripMenuItem.Width δεν "πιάνει" όσο το ContextMenuStrip έχει AutoSize=true
            // (το layout ξαναϋπολογίζει το πλάτος από το κείμενο, αγνοώντας ρητή τιμή) — γι' αυτό
            // υπολογίζουμε ΕΜΕΙΣ το ύψος (ώστε να μη χρειαστεί να βασιστούμε στο δικό του AutoSize
            // για το ύψος) και θέτουμε ρητά Width/Height στο ίδιο το strip: ίδιο πλάτος με το
            // κουμπί (ζητήθηκε ρητά), σωστό ύψος βάσει αριθμού επιλογών.
            int itemHeight = TextRenderer.MeasureText("Ag", ItemFont).Height + 10;
            for (int i = 0; i < _items.Count; i++)
            {
                int idx = i;
                var item = new ToolStripMenuItem(_items[i]) { Checked = idx == _selectedIndex, CheckOnClick = false, ForeColor = UiTheme.TextPrimary, AutoSize = false, Size = new Size(Width, itemHeight) };
                if (_icons != null && i < _icons.Count && _icons[i] != null) { item.Image = _icons[i]; item.ImageScaling = ToolStripItemImageScaling.None; }
                item.Click += (_, _) => SelectedIndex = idx;
                menu.Items.Add(item);
            }
            menu.AutoSize = false;
            menu.Size = new Size(Width, itemHeight * _items.Count + 4);
            // ΟΧΙ Dispose() εδώ: τη στιγμή που πυροδοτείται το Closed, το ίδιο το ToolStripDropDown
            // βρίσκεται ΑΚΟΜΗ μέσα στη δική του εσωτερική διαδικασία κλεισίματος (SetVisibleCore) —
            // ένα re-entrant Dispose() εκεί προκαλεί ObjectDisposedException λίγο αργότερα στο ίδιο
            // call stack (επιβεβαιωμένο crash.log). Ένα μικρό, βραχύβιο ContextMenuStrip χωρίς
            // ρητό Dispose είναι αποδεκτό πρότυπο στο WinForms — το μαζεύει κανονικά ο GC.
            menu.Closed += (_, _) => { _open = false; Invalidate(); };
            menu.Show(this, new Point(0, Height));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bg = _open || _hover ? UiTheme.SurfaceHover : UiTheme.Surface;
            using (var bgBrush = new SolidBrush(bg))
            using (var path = UiTheme.RoundedPath(new Rectangle(0, 0, Width, Height), 7))
                g.FillPath(bgBrush, path);
            using (var borderPen = new Pen(UiTheme.Border))
            using (var path = UiTheme.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 7))
                g.DrawPath(borderPen, path);

            string text = SelectedItem ?? string.Empty;
            using var textBrush = new SolidBrush(UiTheme.TextPrimary);
            var textSize = g.MeasureString(text, ItemFont);
            float textX = 12;
            if (_icons != null && _selectedIndex >= 0 && _selectedIndex < _icons.Count && _icons[_selectedIndex] is { } icon)
            {
                g.DrawImage(icon, 12, (Height - icon.Height) / 2f, icon.Width, icon.Height);
                textX = 12 + icon.Width + 8;
            }
            g.DrawString(text, ItemFont, textBrush, textX, (Height - textSize.Height) / 2f);

            using var chevronPen = new Pen(UiTheme.TextSecondary, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float cx = Width - 18, cy = Height / 2f - 1.5f;
            g.DrawLines(chevronPen, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
        }
    }

    // Σκούρο, επίπεδο rendering για ContextMenuStrip/ToolStrip — χωρίς gradients και 3D
    // περιγράμματα των κλασικών Windows menus, με highlight στο χρώμα του θέματος.
    public sealed class FlatMenuRenderer : ToolStripProfessionalRenderer
    {
        public FlatMenuRenderer() : base(new FlatColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = (e.Item.Selected && e.Item is ToolStripMenuItem) ? UiTheme.AccentCyan : UiTheme.TextPrimary;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(UiTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(UiTheme.AccentCyan, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var r = e.ImageRectangle;
            g.DrawLines(pen, new[] { new PointF(r.Left + 2, r.Top + r.Height / 2f), new PointF(r.Left + r.Width / 2.5f, r.Bottom - 3), new PointF(r.Right - 2, r.Top + 2) });
        }
    }

    public sealed class FlatColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => UiTheme.SurfaceHover;
        public override Color MenuItemSelectedGradientBegin => UiTheme.SurfaceHover;
        public override Color MenuItemSelectedGradientEnd => UiTheme.SurfaceHover;
        public override Color MenuItemBorder => UiTheme.SurfaceHover;
        public override Color MenuBorder => UiTheme.Border;
        public override Color ToolStripDropDownBackground => UiTheme.Surface;
        public override Color ImageMarginGradientBegin => UiTheme.Surface;
        public override Color ImageMarginGradientMiddle => UiTheme.Surface;
        public override Color ImageMarginGradientEnd => UiTheme.Surface;
        public override Color SeparatorDark => UiTheme.Border;
        public override Color SeparatorLight => UiTheme.Border;
    }
}
