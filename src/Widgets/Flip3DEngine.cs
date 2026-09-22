using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MotionDesk.UI;

namespace MotionDesk.Widgets
{
    // Αναβίωση του Windows Flip 3D (Win+Tab / Alt+Tab στα Vista): στοίβα από ζωντανά DWM
    // thumbnails των ανοιχτών παραθύρων, με πλοήγηση πληκτρολογίου (Tab/βελάκια/Enter) και
    // κλικ-για-ενεργοποίηση. Ενεργοποιείται με Ctrl+Alt+F (ΟΧΙ το ίδιο το system Alt+Tab —
    // η υποκλοπή του πραγματικού Alt+Tab θα απαιτούσε global keyboard hook που να καταπιέζει
    // μόνιμα τον system window-switcher· ένα bug εκεί θα άφηνε τον χρήστη χωρίς κανονικό
    // Alt+Tab, οπότε προτιμήθηκε ένα ξεχωριστό, ασφαλές hotkey με την ίδια αισθητική/λειτουργία).
    public class Flip3DOverlay : Form
    {
        private const int CenterWidth = 320;
        private const int CenterHeight = 200;
        private const int SideWidth = 220;
        private const int SideHeight = 140;
        private const int Spacing = 40;

        private readonly List<(IntPtr Hwnd, string Title, IntPtr ThumbId, Rectangle Bounds, float Angle, float Opacity)> _entries = new();
        private int _selectedIndex;

        public Flip3DOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Bounds = SystemInformation.VirtualScreen;
            BackColor = Color.FromArgb(16, 16, 22);
            Opacity = 0.001; // αρχικά αόρατο μέχρι να γίνει set το πραγματικό opacity μετά τα thumbnails
            DoubleBuffered = true;

            KeyPreview = true;
            KeyDown += OnOverlayKeyDown;
            Click += (s, e) => ActivateSelected();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Opacity = 0.90;
            BuildThumbnails();
            Focus();
        }

        private void OnOverlayKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Escape:
                    Close();
                    break;
                case Keys.Tab when e.Shift:
                case Keys.Left:
                    Select(_selectedIndex - 1);
                    break;
                case Keys.Tab:
                case Keys.Right:
                    Select(_selectedIndex + 1);
                    break;
                case Keys.Enter:
                case Keys.Space:
                    ActivateSelected();
                    break;
            }
            e.Handled = true;
        }

        private void Select(int index)
        {
            if (_entries.Count == 0) return;
            _selectedIndex = ((index % _entries.Count) + _entries.Count) % _entries.Count;
            LayoutThumbnails();
            Invalidate();
        }

        private void ActivateSelected()
        {
            if (_selectedIndex >= 0 && _selectedIndex < _entries.Count)
                SetForegroundWindow(_entries[_selectedIndex].Hwnd);
            Close();
        }

        private void BuildThumbnails()
        {
            var windows = EnumerateCandidateWindows();
            foreach (var hwnd in windows)
            {
                if (DwmRegisterThumbnail(Handle, hwnd, out IntPtr thumbId) != 0) continue;
                _entries.Add((hwnd, GetWindowTitle(hwnd), thumbId, Rectangle.Empty, 0f, 1f));
            }

            // Προεπιλεγμένη επιλογή: το ΔΕΥΤΕΡΟ παράθυρο (index 1) αν υπάρχει — ίδια λογική με
            // το κλασικό Alt+Tab, όπου το πρώτο πάτημα επιλέγει το "προηγούμενο" παράθυρο, όχι
            // αυτό που είσαι ήδη πάνω του.
            _selectedIndex = _entries.Count > 1 ? 1 : 0;
            LayoutThumbnails();
            Invalidate();
        }

        // "Υπό γωνία" στοίβα, σαν fanned card deck: το επιλεγμένο παράθυρο στο κέντρο, μεγάλο
        // και επίπεδο· τα υπόλοιπα υποχωρούν αριστερά/δεξιά, μικρότερα, πιο διάφανα, με γωνία
        // (η γωνία υλοποιείται σαν skewed πλαίσιο γύρω από το επίπεδο live DWM thumbnail — το
        // δημόσιο DWM API δεν υποστηρίζει περιστροφή/προοπτική στο ίδιο το περιεχόμενο).
        private void LayoutThumbnails()
        {
            if (_entries.Count == 0) return;

            int centerX = Width / 2;
            int centerY = Height / 2;

            for (int i = 0; i < _entries.Count; i++)
            {
                int offset = i - _selectedIndex;
                bool isSelected = offset == 0;
                int w = isSelected ? CenterWidth : SideWidth;
                int h = isSelected ? CenterHeight : SideHeight;

                int x = centerX + offset * (SideWidth / 2 + Spacing) - w / 2;
                if (offset != 0) x += Math.Sign(offset) * (CenterWidth / 2 - SideWidth / 2);
                int y = centerY - h / 2;

                float angle = isSelected ? 0f : Math.Sign(offset) * Math.Min(1f, Math.Abs(offset) * 0.35f + 0.35f);
                float opacity = isSelected ? 1f : Math.Max(0.35f, 1f - Math.Abs(offset) * 0.22f);

                var entry = _entries[i];
                entry.Bounds = new Rectangle(x, y, w, h);
                entry.Angle = angle;
                entry.Opacity = opacity;
                _entries[i] = entry;

                var props = new DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY,
                    rcDestination = new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h },
                    fVisible = true,
                    opacity = (byte)(opacity * 255)
                };
                DwmUpdateThumbnailProperties(entry.ThumbId, ref props);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var font = new Font("Segoe UI", 9.5f);
            using var titleFont = new Font("Segoe UI", 11f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            using var mutedBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));

            // Draw furthest-from-selected first ώστε το κεντρικό (πάνω από όλα) να ζωγραφίζεται
            // τελευταίο — σωστό z-order για την "στοίβα".
            var order = new List<int>();
            for (int i = 0; i < _entries.Count; i++) order.Add(i);
            order.Sort((a, b) => Math.Abs(b - _selectedIndex).CompareTo(Math.Abs(a - _selectedIndex)));

            foreach (var i in order)
            {
                var entry = _entries[i];
                bool isSelected = i == _selectedIndex;

                // Skewed backdrop "κάρτα" πίσω/γύρω από το επίπεδο live thumbnail — αυτό δίνει
                // την αίσθηση γωνίας/κλίσης που το ίδιο το DWM thumbnail δεν μπορεί να έχει.
                DrawTiltedFrame(g, entry.Bounds, entry.Angle, isSelected ? UiTheme.AccentCyan : Color.FromArgb((int)(entry.Opacity * 160), 255, 255, 255), isSelected ? 2.4f : 1.2f);

                if (isSelected)
                {
                    var titleSize = g.MeasureString(entry.Title, titleFont);
                    g.DrawString(entry.Title, titleFont, brush, entry.Bounds.X + (entry.Bounds.Width - titleSize.Width) / 2f, entry.Bounds.Bottom + 10);
                }
            }

            using var hint = new Font("Segoe UI", 9.5f);
            g.DrawString("←/→ ή Tab για περιήγηση  •  Enter για ενεργοποίηση  •  Esc για έξοδο", hint, mutedBrush, 24, 24);
        }

        // Σχεδιάζει ένα λεπτό, "γερμένο" περίγραμμα γύρω από το ορθογώνιο ενός thumbnail —
        // shear μέσω parallelogram (τα δύο κατακόρυφα άκρα ίδιου ύψους αλλά μετατοπισμένα
        // οριζόντια), η πιο κοντινή προσέγγιση σε "γωνία" που επιτρέπει το GDI+ χωρίς πλήρη
        // προοπτική μετασχηματισμό (δεν υποστηρίζεται εγγενώς, ούτε από το δημόσιο DWM API).
        private static void DrawTiltedFrame(Graphics g, Rectangle bounds, float angle, Color color, float thickness)
        {
            float skew = bounds.Height * 0.12f * angle;
            var pts = new[]
            {
                new PointF(bounds.Left + skew, bounds.Top),
                new PointF(bounds.Right + skew, bounds.Top),
                new PointF(bounds.Right - skew, bounds.Bottom),
                new PointF(bounds.Left - skew, bounds.Bottom),
            };
            using var pen = new Pen(color, thickness) { LineJoin = LineJoin.Round };
            g.DrawPolygon(pen, pts);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            foreach (var entry in _entries)
                DwmUnregisterThumbnail(entry.ThumbId);
            base.OnFormClosing(e);
        }

        private List<IntPtr> EnumerateCandidateWindows()
        {
            var result = new List<IntPtr>();
            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                if (GetWindowTextLength(hwnd) == 0) return true;
                if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return true;

                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

                long style = GetWindowLong(hwnd, GWL_STYLE);
                if ((style & WS_CAPTION) == 0) return true;

                result.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            int len = GetWindowTextLength(hwnd);
            var sb = new System.Text.StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DWM_THUMBNAIL_PROPERTIES
        {
            public int dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            public bool fVisible;
            public bool fSourceClientAreaOnly;
        }

        private const int DWM_TNP_RECTDESTINATION = 0x1;
        private const int DWM_TNP_OPACITY = 0x4;
        private const int DWM_TNP_VISIBLE = 0x8;

        private const int GWL_EXSTYLE = -20;
        private const int GWL_STYLE = -16;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const long WS_CAPTION = 0x00C00000;
        private const int GW_OWNER = 4;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES props);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    }

    public static class Flip3DEngine
    {
        public static void Show()
        {
            new Flip3DOverlay().Show();
        }
    }
}
