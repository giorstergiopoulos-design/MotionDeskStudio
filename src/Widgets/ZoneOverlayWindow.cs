using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MotionDesk.UI;

namespace MotionDesk.Widgets
{
    // Ημιδιάφανο overlay που δείχνει τις ζώνες μιας οθόνης — εμφανίζεται ΜΟΝΟ όσο ο χρήστης κρατάει
    // Shift ενώ σέρνει ένα παράθυρο (βλ. ZoneSnapEngine), ακριβώς όπως το πραγματικό FancyZones
    // Editor overlay. Click-through (WS_EX_TRANSPARENT) + WS_EX_NOACTIVATE: δεν πρέπει ΠΟΤΕ να
    // κλέψει το mouse capture ή το focus από το drag operation που ήδη τρέχει σε άλλη εφαρμογή.
    public sealed class ZoneOverlayWindow : Form
    {
        private ZoneLayoutData _layout = new();
        private Rectangle _workingArea;
        private int _highlightIndex = -1;

        public ZoneOverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            TransparencyKey = Color.Black; // πραγματική διαφάνεια φόντου, μένουν μόνο τα σχήματα ζωνών
            TopMost = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        // Το ShowWithoutActivation=true (built-in WinForms hook) εγγυάται ότι το Show() παρακάτω
        // ΔΕΝ θα πάρει focus/activation — το drag operation του χρήστη σε ΑΛΛΟ παράθυρο συνεχίζει
        // απρόσκοπτο.
        protected override bool ShowWithoutActivation => true;

        public void ShowForScreen(Screen screen, ZoneLayoutData layout)
        {
            _layout = layout;
            _workingArea = screen.WorkingArea;
            Bounds = _workingArea;
            _highlightIndex = -1;
            if (!Visible) Show();
            Invalidate();
        }

        public void UpdateHighlight(int zoneIndex)
        {
            if (_highlightIndex == zoneIndex) return;
            _highlightIndex = zoneIndex;
            Invalidate();
        }

        public void HideOverlay()
        {
            if (Visible) Hide();
        }

        // Δίνει το απόλυτο ορθογώνιο (screen coords) της ζώνης που είναι αυτή τη στιγμή highlighted,
        // ή null αν ο δείκτης δεν είναι πάνω σε καμία ζώνη — αυτό είναι το σημείο "κουμπώματος".
        public Rectangle? GetHighlightedZoneBounds() =>
            _highlightIndex >= 0 && _highlightIndex < _layout.Zones.Count
                ? _layout.Zones[_highlightIndex].ToAbsolute(_workingArea)
                : null;

        public int FindZoneIndexAt(Point screenPt)
        {
            for (int i = 0; i < _layout.Zones.Count; i++)
                if (_layout.Zones[i].ToAbsolute(_workingArea).Contains(screenPt)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (int i = 0; i < _layout.Zones.Count; i++)
            {
                var rect = _layout.Zones[i].ToAbsolute(_workingArea);
                rect.Offset(-_workingArea.X, -_workingArea.Y); // σε client-local συντεταγμένες
                bool highlighted = i == _highlightIndex;

                using var fill = new SolidBrush(highlighted
                    ? Color.FromArgb(120, UiTheme.AccentCyan)
                    : Color.FromArgb(45, UiTheme.AccentCyan));
                using var border = new Pen(highlighted ? UiTheme.AccentCyan : Color.FromArgb(160, UiTheme.AccentCyan), highlighted ? 3f : 1.6f);
                using var path = UiTheme.RoundedPath(rect, 10);
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
        }
    }
}
