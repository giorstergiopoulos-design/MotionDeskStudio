using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MotionDesk.UI;

namespace MotionDesk.Widgets
{
    // Αναβίωση του κλασικού window-switcher της εποχής Windows Vista (Ctrl+Alt+F, ΟΧΙ το
    // πραγματικό system Alt+Tab/Win+Tab): στοίβα ανοιχτών παραθύρων σε γωνία, υποχωρώντας σε
    // βάθος. Το όνομα άλλαξε από το προηγούμενο "Flip3D/Flip 3D" σε "DeskFlip" (ίδια σύμβαση
    // ονοματολογίας με τα υπόλοιπα DeskZones/DeskContainers/DeskCursors/DeskStrip/DeskSounds του
    // project) — ζητήθηκε ρητά να μην αναφέρεται η ίδια η επίσημη ονομασία λειτουργίας της
    // Microsoft, για αποφυγή οποιουδήποτε ζητήματος επωνυμίας/trademark.
    //
    // ΣΗΜΑΝΤΙΚΟΣ ΤΕΧΝΙΚΟΣ ΠΕΡΙΟΡΙΣΜΟΣ (δεν είναι επιλογή σχεδίασης, είναι όριο του public Win32/DWM
    // API): το πραγματικό Flip3D των Vista ήταν built-in στον ίδιο τον DWM compositor της
    // Microsoft, με προνομιακή πρόσβαση σε αληθινό 3D perspective rendering ΖΩΝΤΑΝΟΥ περιεχομένου.
    // Η δημόσια `DwmRegisterThumbnail` API επιτρέπει σε 3rd-party εφαρμογές να τοποθετήσουν ζωντανά
    // thumbnails ΜΟΝΟ σε επίπεδα, ορθογώνια (axis-aligned) πλαίσια — καμία περιστροφή/προοπτική.
    // Για να πετύχουμε την πραγματική "γερμένη σελίδα βιβλίου" εμφάνιση της αυθεντικής εφαρμογής,
    // τα ΜΗ-επιλεγμένα (γερμένα) παράθυρα καταγράφονται εδώ ως ΣΤΑΤΙΚΟ στιγμιότυπο (PrintWindow)
    // και ζωγραφίζονται με πραγματικό parallelogram warp (Graphics.DrawImage σε 3 σημεία) — παύουν
    // να είναι "ζωντανά" όσο είναι γερμένα, αλλά αποκτούν πραγματική οπτική κλίση αντί για απλό
    // σχεδιασμένο πλαίσιο γύρω από ένα επίπεδο ορθογώνιο (όπως ήταν πριν). Το επιλεγμένο (μπροστινό,
    // χωρίς κλίση) παράθυρο παραμένει ΖΩΝΤΑΝΟ DWM thumbnail, αφού εκεί δεν χρειάζεται καθόλου
    // περιστροφή — άρα το πιο σημαντικό/μεγάλο κομμάτι της στοίβας παραμένει ζωντανό βίντεο.
    public class DeskFlipOverlay : Form
    {
        // Μεγέθη/βήματα υπολογίζονται στον constructor ως ποσοστό της οθόνης (όχι πια σταθερά
        // pixel) — ζητήθηκε ρητά "θέλω ακριβώς το ίδιο [με τα Vista]": τα πραγματικά παράθυρα στο
        // αυθεντικό Flip3D καταλάμβαναν μεγάλο μέρος της οθόνης, όχι μικρά thumbnails.
        private int FrontWidth;
        private int FrontHeight;
        private float StepScale = 0.90f;   // κάθε επόμενο προς τα πίσω είναι ~10% μικρότερο
        private int StepX;                  // οριζόντια μετατόπιση προς τα πίσω (υποχωρεί δεξιά)
        private int StepY;                  // κατακόρυφη μετατόπιση προς τα πίσω (ανεβαίνει)

        private sealed class Entry
        {
            public IntPtr Hwnd;
            public string Title = "";
            public IntPtr ThumbId;
            public Bitmap? Snapshot;
            public Rectangle Bounds;
        }

        private readonly List<Entry> _entries = new();
        private int _selectedIndex;
        private Bitmap? _background;

        public DeskFlipOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Bounds = SystemInformation.VirtualScreen;
            BackColor = Color.FromArgb(10, 10, 14);
            Opacity = 0.001;
            DoubleBuffered = true;

            FrontWidth = (int)(Bounds.Width * 0.30f);
            FrontHeight = (int)(FrontWidth * 0.66f);
            StepX = (int)(FrontWidth * 0.13f);
            StepY = -(int)(FrontHeight * 0.20f);

            // Το αυθεντικό Flip3D έδειχνε τη θολωμένη/σκουρυσμένη ζωντανή επιφάνεια εργασίας από
            // πίσω (Aero Glass), όχι ένα επίπεδο σχεδόν-μαύρο φόντο — καταγράφεται ΠΡΙΝ εμφανιστεί
            // το ίδιο το overlay (Opacity ακόμα 0.001), άρα δεν "φωτογραφίζει τον εαυτό του".
            _background = CaptureBlurredBackground();

            KeyPreview = true;
            KeyDown += OnOverlayKeyDown;
            MouseWheel += (s, e) => Select(_selectedIndex + (e.Delta < 0 ? 1 : -1));
            Click += (s, e) => ActivateSelected();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Opacity = 0.96;
            Build();
            Focus();
        }

        // Φτηνό, γρήγορο blur προσέγγισης: σμίκρυνση σε πολύ μικρό μέγεθος και μεγέθυνση πίσω με
        // bicubic interpolation (η ίδια "τέχνη" που χρησιμοποιούν πολλά real-time UI blur effects
        // όταν δεν υπάρχει διαθέσιμο πραγματικό Gaussian/box blur), + σκούρο επικάλυμμα από πάνω.
        private Bitmap? CaptureBlurredBackground()
        {
            try
            {
                var screenBounds = Bounds;
                if (screenBounds.Width <= 0 || screenBounds.Height <= 0) return null;

                using var full = new Bitmap(screenBounds.Width, screenBounds.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(full))
                    g.CopyFromScreen(screenBounds.Left, screenBounds.Top, 0, 0, screenBounds.Size);

                using var small = DownscaleUpscale(full, new Size(Math.Max(1, screenBounds.Width / 24), Math.Max(1, screenBounds.Height / 24)));
                var blurred = DownscaleUpscale(small, screenBounds.Size);

                using (var g2 = Graphics.FromImage(blurred))
                using (var dark = new SolidBrush(Color.FromArgb(160, 8, 10, 16)))
                    g2.FillRectangle(dark, 0, 0, blurred.Width, blurred.Height);

                return blurred;
            }
            catch { return null; }
        }

        private static Bitmap DownscaleUpscale(Bitmap src, Size target)
        {
            var result = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(result);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, new Rectangle(Point.Empty, target));
            return result;
        }

        private void OnOverlayKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Escape: Close(); break;
                case Keys.Tab when e.Shift:
                case Keys.Left: Select(_selectedIndex - 1); break;
                case Keys.Tab:
                case Keys.Right: Select(_selectedIndex + 1); break;
                case Keys.Enter:
                case Keys.Space: ActivateSelected(); break;
            }
            e.Handled = true;
        }

        private void Select(int index)
        {
            if (_entries.Count == 0) return;
            int newIndex = ((index % _entries.Count) + _entries.Count) % _entries.Count;
            if (newIndex == _selectedIndex) return;
            _selectedIndex = newIndex;
            RebuildLiveThumbnailForSelection();
            LayoutThumbnails();
            Invalidate();
        }

        private void ActivateSelected()
        {
            if (_selectedIndex >= 0 && _selectedIndex < _entries.Count)
                SetForegroundWindow(_entries[_selectedIndex].Hwnd);
            Close();
        }

        private void Build()
        {
            foreach (var hwnd in EnumerateCandidateWindows())
                _entries.Add(new Entry { Hwnd = hwnd, Title = GetWindowTitle(hwnd) });

            // Ίδια λογική με το κλασικό Alt+Tab: το πρώτο πάτημα επιλέγει το "προηγούμενο"
            // παράθυρο (index 1), όχι αυτό στο οποίο βρίσκεσαι ήδη.
            _selectedIndex = _entries.Count > 1 ? 1 : 0;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (i == _selectedIndex) RegisterLiveThumbnail(_entries[i]);
                else _entries[i].Snapshot = CaptureSnapshot(_entries[i].Hwnd);
            }

            LayoutThumbnails();
            Invalidate();
        }

        private void RegisterLiveThumbnail(Entry entry)
        {
            if (DwmRegisterThumbnail(Handle, entry.Hwnd, out IntPtr thumbId) == 0)
                entry.ThumbId = thumbId;
        }

        // Όταν αλλάζει η επιλογή, το ΝΕΟ επιλεγμένο παράθυρο χρειάζεται ζωντανό thumbnail (θα
        // γίνει επίπεδο/μπροστινό) και το ΠΑΛΙΟ επιλεγμένο πρέπει να πάρει ένα στατικό στιγμιότυπο
        // (θα γείρει πλέον προς τα πίσω) αφού απελευθερώσουμε το DWM thumbnail του.
        private void RebuildLiveThumbnailForSelection()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                bool shouldBeLive = i == _selectedIndex;
                bool isLive = entry.ThumbId != IntPtr.Zero;
                if (shouldBeLive && !isLive)
                {
                    entry.Snapshot?.Dispose(); entry.Snapshot = null;
                    RegisterLiveThumbnail(entry);
                }
                else if (!shouldBeLive && isLive)
                {
                    DwmUnregisterThumbnail(entry.ThumbId);
                    entry.ThumbId = IntPtr.Zero;
                    entry.Snapshot = CaptureSnapshot(entry.Hwnd);
                }
            }
        }

        // Στιγμιότυπο πραγματικού περιεχομένου παραθύρου — PW_RENDERFULLCONTENT (0x2) είναι
        // απαραίτητο για σύγχρονα GPU/DWM-composited παράθυρα (browsers, WebView2, κ.λπ.),
        // διαφορετικά το κλασικό PrintWindow γυρνάει συχνά κενή/μαύρη εικόνα σε αυτά.
        private static Bitmap? CaptureSnapshot(IntPtr hwnd)
        {
            if (!GetWindowRect(hwnd, out var rect)) return null;
            int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
            if (w <= 0 || h <= 0) return null;
            try
            {
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bmp);
                IntPtr hdc = g.GetHdc();
                try { PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT); }
                finally { g.ReleaseHdc(hdc); }
                return bmp;
            }
            catch { return null; }
        }

        // Μία διαγώνια σειρά που υποχωρεί προς τα πίσω-δεξιά — ίδια διάταξη με το αυθεντικό
        // Flip3D (όχι πλέον αριστερά/δεξιά fan όπως στην προηγούμενη υλοποίηση).
        private void LayoutThumbnails()
        {
            if (_entries.Count == 0) return;
            int anchorX = Width / 2 - FrontWidth / 2 - (_entries.Count - 1) * StepX / 2;
            int anchorY = Height / 2 - FrontHeight / 2 + (_entries.Count - 1) * Math.Abs(StepY) / 2;

            // Σειρά εμφάνισης: 0 = επιλεγμένο (μπροστά), μετά εναλλάξ τα υπόλοιπα προς τα πίσω.
            var order = new List<int> { _selectedIndex };
            for (int d = 1; d < _entries.Count; d++)
            {
                int after = _selectedIndex + d; if (after < _entries.Count) order.Add(after);
                int before = _selectedIndex - d; if (before >= 0) order.Add(before);
            }

            for (int depth = 0; depth < order.Count; depth++)
            {
                var entry = _entries[order[depth]];
                float scale = (float)Math.Pow(StepScale, depth);
                int w = Math.Max(60, (int)(FrontWidth * scale));
                int h = Math.Max(40, (int)(FrontHeight * scale));
                int x = anchorX + depth * StepX;
                int y = anchorY + depth * StepY;
                entry.Bounds = new Rectangle(x, y, w, h);

                if (entry.ThumbId != IntPtr.Zero)
                {
                    var props = new DWM_THUMBNAIL_PROPERTIES
                    {
                        dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY,
                        rcDestination = new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h },
                        fVisible = true,
                        opacity = 255
                    };
                    DwmUpdateThumbnailProperties(entry.ThumbId, ref props);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            if (_background != null) g.DrawImageUnscaled(_background, 0, 0);
            else using (var bg = new SolidBrush(Color.FromArgb(10, 10, 14))) g.FillRectangle(bg, ClientRectangle);

            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using var titleFont = new Font("Segoe UI", 12f, FontStyle.Bold);
            using var hintFont = new Font("Segoe UI", 9.5f);
            using var brush = new SolidBrush(Color.White);
            using var mutedBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));

            // Ζωγραφίζει από το πιο μακρινό προς το πιο κοντινό, ώστε η σωστή αλληλοεπικάλυψη
            // (το μπροστινό/επιλεγμένο πάνω από όλα) — αντίστροφη σειρά από αυτή που φτιάξαμε.
            var order = new List<Entry>(_entries);
            order.Sort((a, b) => DepthOf(b).CompareTo(DepthOf(a)));

            foreach (var entry in order)
            {
                bool isSelected = entry.ThumbId != IntPtr.Zero;
                int depth = DepthOf(entry);

                if (isSelected)
                {
                    // Ζωντανό DWM thumbnail: το ίδιο το DWM το ζωγραφίζει απευθείας στην οθόνη
                    // πάνω από αυτό το παράθυρο (compositor-level) — εδώ σχεδιάζουμε μόνο το
                    // περίγραμμα highlight γύρω του.
                    using var pen = new Pen(UiTheme.AccentCyan, 2.6f);
                    g.DrawRectangle(pen, entry.Bounds);
                }
                else if (entry.Snapshot != null)
                {
                    DrawTiltedSnapshot(g, entry.Snapshot, entry.Bounds, depth);
                }

                if (isSelected)
                {
                    var titleSize = g.MeasureString(entry.Title, titleFont);
                    g.DrawString(entry.Title, titleFont, brush, entry.Bounds.X + (entry.Bounds.Width - titleSize.Width) / 2f, entry.Bounds.Bottom + 12);
                }
            }

            g.DrawString(MotionDesk.Services.LocalizationManager.T("DeskFlip.Hint"),
                hintFont, mutedBrush, 24, 24);
        }

        private int DepthOf(Entry entry)
        {
            int idx = _entries.IndexOf(entry);
            int diff = idx - _selectedIndex;
            // ίδια λογική "εναλλάξ προς τα πίσω" με το LayoutThumbnails() — υπολογίζει το βάθος (0=μπροστά).
            if (diff == 0) return 0;
            int depth = 0;
            for (int d = 1; d < _entries.Count; d++)
            {
                int a = _selectedIndex + d, b = _selectedIndex - d;
                if (a < _entries.Count) { depth++; if (a == idx) return depth; }
                if (b >= 0) { depth++; if (b == idx) return depth; }
            }
            return _entries.Count;
        }

        // GDI+'s DrawImage(destPoints[3], ...) only maps a PARALLELOGRAM (affine) — it cannot bend
        // an image into a true trapezoid, which is what real 3D perspective (a window rotated away
        // around a vertical axis) actually looks like. A plain parallelogram warp just looks
        // "sheared", not "tilted in 3D", which was the core reason the previous version didn't read
        // as genuine Flip3D. Fix: slice the snapshot into thin vertical strips and warp each strip
        // with its OWN parallelogram whose height shrinks strip-by-strip toward the right edge —
        // stacking many thin affine slices approximates a real perspective trapezoid closely enough
        // to read correctly at normal viewing distance (the classic pre-shader "fake 3D" technique).
        private static void DrawTiltedSnapshot(Graphics g, Bitmap snapshot, Rectangle bounds, int depth)
        {
            float heightShrink = Math.Min(0.62f, depth * 0.22f);   // πόσο "μαζεύει" κάθετα η δεξιά άκρη
            float widthShrink = Math.Min(0.24f, depth * 0.05f);    // ελαφρύ μάζεμα και οριζόντια
            float alpha = Math.Max(0.35f, 1f - depth * 0.15f);
            float darken = Math.Min(0.45f, depth * 0.10f);         // πιο σκούρο όσο πάει πιο πίσω/πλάγια
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            // Overlapping-strip warps still left faint seams (GDI+ resamples each thin slice
            // independently, so adjacent strips never blend perfectly no matter how much they
            // overlap — confirmed live, the lines persisted even after removing per-strip shading).
            // Fix: render the whole trapezoid warp into an offscreen buffer at 2x resolution, then
            // draw THAT scaled back down to normal size — the downscale's own bicubic averaging
            // blends away the residual 1px seams between strips, the classic supersampling trick.
            const int strips = 24;
            const int supersample = 2;
            int bufW = bounds.Width * supersample, bufH = bounds.Height * supersample;

            using var buffer = new Bitmap(bufW, bufH, PixelFormat.Format32bppArgb);
            using (var bg = Graphics.FromImage(buffer))
            {
                bg.SmoothingMode = SmoothingMode.HighQuality;
                bg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                bg.PixelOffsetMode = PixelOffsetMode.HighQuality;

                float overlap = (1f / strips) * 0.6f;
                float centerY = bufH / 2f;
                float halfH = bufH / 2f;

                for (int s = 0; s < strips; s++)
                {
                    float t0 = s / (float)strips;
                    float t1 = (s + 1) / (float)strips;
                    float ot0 = s == 0 ? t0 : t0 - overlap;
                    float ot1 = s == strips - 1 ? t1 : t1 + overlap;

                    float srcX0 = snapshot.Width * ot0;
                    float srcX1 = snapshot.Width * ot1;

                    float dx0 = bufW * (ot0 * (1f - widthShrink));
                    float dx1 = bufW * (ot1 * (1f - widthShrink));

                    float h0 = halfH * (1f - heightShrink * ot0);
                    float h1 = halfH * (1f - heightShrink * ot1);

                    var topLeft = new PointF(dx0, centerY - h0);
                    var topRight = new PointF(dx1, centerY - h1);
                    var bottomLeft = new PointF(dx0, centerY + h0);

                    bg.DrawImage(snapshot, new[] { topLeft, topRight, bottomLeft },
                        new RectangleF(srcX0, 0, Math.Max(0.5f, srcX1 - srcX0), snapshot.Height), GraphicsUnit.Pixel);
                }
            }

            using var alphaAttrs = new ImageAttributes();
            alphaAttrs.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
            g.DrawImage(buffer, bounds, 0, 0, bufW, bufH, GraphicsUnit.Pixel, alphaAttrs);

            // Περίγραμμα + σκίαση γύρω/πάνω από τη συνολική (τραπεζοειδή) σιλουέτα, σε πραγματικές
            // (μη-supersampled) συντεταγμένες οθόνης.
            float endHFull = bounds.Height / 2f * (1f - heightShrink);
            float endXFull = bounds.Left + bounds.Width * (1f - widthShrink);
            float centerYFull = bounds.Top + bounds.Height / 2f;
            var farTop = new PointF(endXFull, centerYFull - endHFull);
            var farBottom = new PointF(endXFull, centerYFull + endHFull);

            if (darken > 0.01f)
            {
                using var trapezoidPath = new GraphicsPath();
                trapezoidPath.AddPolygon(new[]
                {
                    new PointF(bounds.Left, bounds.Top), farTop, farBottom, new PointF(bounds.Left, bounds.Bottom)
                });
                var oldClip = g.Clip;
                g.SetClip(trapezoidPath, CombineMode.Intersect);
                using var shadeBrush = new LinearGradientBrush(
                    new PointF(bounds.Left, centerYFull), new PointF(endXFull, centerYFull),
                    Color.FromArgb(0, 0, 0, 0), Color.FromArgb((int)(darken * 255), 0, 0, 0));
                g.FillPath(shadeBrush, trapezoidPath);
                g.Clip = oldClip;
            }

            using var pen = new Pen(Color.FromArgb((int)(alpha * 140), 255, 255, 255), 1.2f);
            g.DrawLine(pen, new PointF(bounds.Left, bounds.Top), farTop);
            g.DrawLine(pen, farTop, farBottom);
            g.DrawLine(pen, farBottom, new PointF(bounds.Left, bounds.Bottom));
            g.DrawLine(pen, new PointF(bounds.Left, bounds.Bottom), new PointF(bounds.Left, bounds.Top));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            foreach (var entry in _entries)
            {
                if (entry.ThumbId != IntPtr.Zero) DwmUnregisterThumbnail(entry.ThumbId);
                entry.Snapshot?.Dispose();
            }
            _background?.Dispose();
            _background = null;
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
        private const uint PW_RENDERFULLCONTENT = 0x00000002;

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
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    }

    public static class DeskFlipEngine
    {
        public static void Show()
        {
            new DeskFlipOverlay().Show();
        }
    }
}
