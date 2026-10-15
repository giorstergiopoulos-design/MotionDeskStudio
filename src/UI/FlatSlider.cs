using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MotionDesk.UI
{
    // Custom-σχεδιασμένο slider (λεπτή στρογγυλεμένη διαδρομή + κυκλικός δρομέας) αντί για το
    // κλασικό, παλιάς αισθητικής TrackBar των Windows — ζητήθηκε ρητά "ενσωμάτωσε την παρουσίαση
    // ενός media player" (MediaPlayerPro, αδελφό project· εκεί ο seek/volume slider είναι ένα
    // retemplated WPF Slider: λεπτή 4px διαδρομή, accent-χρωματισμένο γέμισμα, κυκλικός δρομέας).
    // Ίδιο αποτέλεσμα εδώ με GDI+. Υποστηρίζει το ελάχιστο υποσύνολο API που χρησιμοποιεί ήδη η
    // εφαρμογή (Minimum/Maximum/Value/SmallChange/Scroll) ώστε να αντικαθιστά το TrackBar σε κάθε
    // σημείο κλήσης με ελάχιστες αλλαγές.
    public class FlatSlider : Control
    {
        private int _minimum;
        private int _maximum = 100;
        private int _value;
        private bool _dragging;

        public event EventHandler? Scroll;
        public event EventHandler? ValueChanged;

        public int Minimum { get => _minimum; set { _minimum = value; if (_value < _minimum) Value = _minimum; Invalidate(); } }
        public int Maximum { get => _maximum; set { _maximum = value; if (_value > _maximum) Value = _maximum; Invalidate(); } }
        public int SmallChange { get; set; } = 1;
        public int LargeChange { get; set; } = 10;
        public int TickFrequency { get; set; } // kept for TrackBar API parity; not drawn (MediaPlayerPro's slider has no tick marks)

        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Clamp(value, _minimum, _maximum);
                if (clamped == _value) return;
                _value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public FlatSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = 24;
            Cursor = Cursors.Hand;
        }

        private float ThumbX
        {
            get
            {
                float usable = Width - ThumbRadius * 2;
                double frac = _maximum > _minimum ? (double)(_value - _minimum) / (_maximum - _minimum) : 0;
                return ThumbRadius + (float)(frac * usable);
            }
        }
        private const float ThumbRadius = 7f;
        private const float TrackHeight = 4f;

        private int ValueFromX(int x)
        {
            float usable = Width - ThumbRadius * 2;
            if (usable <= 0) return _minimum;
            double frac = Math.Clamp((x - ThumbRadius) / usable, 0, 1);
            return _minimum + (int)Math.Round(frac * (_maximum - _minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            Value = ValueFromX(e.X);
            Scroll?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            Value = ValueFromX(e.X);
            Scroll?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right ? true : base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) { Value -= SmallChange; Scroll?.Invoke(this, EventArgs.Empty); e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { Value += SmallChange; Scroll?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? UiTheme.Surface); // Clear, not an antialiased FillRectangle: that leaves a faint line on the edge pixels

            float midY = Height / 2f;
            var trackRect = new RectangleF(ThumbRadius, midY - TrackHeight / 2f, Math.Max(0, Width - ThumbRadius * 2), TrackHeight);
            using (var trackPath = UiTheme.RoundedPath(Rectangle.Round(trackRect), (int)(TrackHeight / 2)))
            using (var trackBrush = new SolidBrush(UiTheme.Border))
                g.FillPath(trackBrush, trackPath);

            float thumbX = ThumbX;
            var fillRect = new RectangleF(ThumbRadius, midY - TrackHeight / 2f, Math.Max(0, thumbX - ThumbRadius), TrackHeight);
            if (fillRect.Width > 0.5f)
                using (var fillPath = UiTheme.RoundedPath(Rectangle.Round(fillRect), (int)(TrackHeight / 2)))
                using (var fillBrush = new SolidBrush(Enabled ? UiTheme.AccentCyan : UiTheme.TextMuted))
                    g.FillPath(fillBrush, fillPath);

            using (var thumbBrush = new SolidBrush(UiTheme.Surface))
                g.FillEllipse(thumbBrush, thumbX - ThumbRadius, midY - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);
            using (var thumbPen = new Pen(Enabled ? UiTheme.AccentCyan : UiTheme.TextMuted, 2f))
                g.DrawEllipse(thumbPen, thumbX - ThumbRadius + 1, midY - ThumbRadius + 1, ThumbRadius * 2 - 2, ThumbRadius * 2 - 2);
        }
    }
}
