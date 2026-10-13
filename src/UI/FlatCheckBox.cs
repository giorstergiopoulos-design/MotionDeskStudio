using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MotionDesk.UI
{
    // Custom-σχεδιασμένο checkbox (στρογγυλεμένο τετράγωνο + χειροποίητο checkmark) αντί για το
    // προεπιλεγμένο checkbox των Windows — ζητήθηκε ρητά "ενσωμάτωσε την παρουσίαση ενός
    // media player ... checkboxes" με αναφορά στο MediaPlayerPro (αδελφό project, WPF): εκεί το
    // checkbox είναι ένα 18x18 στρογγυλεμένο κουτί (ακτίνα 5) που γεμίζει πλήρως με το accent
    // χρώμα και εμφανίζει λευκό χειροποίητο checkmark όταν είναι τσεκαρισμένο, με πιο φωτεινό
    // περίγραμμα στο hover πριν καν τσεκαριστεί — η ίδια ιδέα εδώ, σε GDI+ αντί για WPF
    // ControlTemplate. Drop-in αντικατάσταση του System.Windows.Forms.CheckBox (ίδιο API/events),
    // ώστε να μπορεί να αντικαταστήσει το "new CheckBox" σε οποιοδήποτε σημείο της εφαρμογής
    // χωρίς αλλαγές στη γύρω λογική.
    public class FlatCheckBox : CheckBox
    {
        private const int BoxSize = 18;
        private bool _hot;

        public FlatCheckBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = UiTheme.Surface;
            ForeColor = UiTheme.TextPrimary;
            Font = UiTheme.FontBody;
            MinimumSize = new Size(BoxSize, BoxSize);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            if (string.IsNullOrEmpty(Text)) return new Size(BoxSize, BoxSize);
            using var g = CreateGraphics();
            var textSize = g.MeasureString(Text, Font);
            int h = Math.Max(BoxSize, (int)Math.Ceiling(textSize.Height));
            int w = BoxSize + 8 + (int)Math.Ceiling(textSize.Width);
            return new Size(w, h);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);

            var boxRect = new Rectangle(0, (Height - BoxSize) / 2, BoxSize, BoxSize);
            using var path = UiTheme.RoundedPath(boxRect, 5);

            if (Checked)
            {
                using var fillBrush = new SolidBrush(Enabled ? UiTheme.AccentCyan : UiTheme.TextMuted);
                g.FillPath(fillBrush, path);
                using var checkPen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                var p1 = new PointF(boxRect.X + BoxSize * 0.22f, boxRect.Y + BoxSize * 0.52f);
                var p2 = new PointF(boxRect.X + BoxSize * 0.42f, boxRect.Y + BoxSize * 0.74f);
                var p3 = new PointF(boxRect.X + BoxSize * 0.80f, boxRect.Y + BoxSize * 0.28f);
                g.DrawLines(checkPen, new[] { p1, p2, p3 });
            }
            else
            {
                using var fillBrush = new SolidBrush(UiTheme.Background);
                g.FillPath(fillBrush, path);
                using var borderPen = new Pen(_hot && Enabled ? UiTheme.AccentCyan : UiTheme.TextMuted, 1.5f);
                g.DrawPath(borderPen, path);
            }

            if (!string.IsNullOrEmpty(Text))
            {
                using var textBrush = new SolidBrush(Enabled ? ForeColor : UiTheme.TextMuted);
                using var sf = new StringFormat { LineAlignment = StringAlignment.Center };
                var textRect = new RectangleF(BoxSize + 8, 0, Math.Max(0, Width - BoxSize - 8), Height);
                g.DrawString(Text, Font, textBrush, textRect, sf);
            }
        }
    }
}
