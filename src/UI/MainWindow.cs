using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using MotionDesk.Services;
using MotionDesk.Widgets;
using System.Diagnostics;

namespace MotionDesk.UI
{
    /// <summary>
    /// The real application shell. Feature windows/widgets remain independent,
    /// while this window provides a single navigation point for the desktop manager.
    /// </summary>
    public sealed class MainWindow : Form
    {
        private Panel _sidebar = null!;
        private TableLayoutPanel _sidebarLayout = null!;
        private Panel _brandPanel = null!;
        private FlowLayoutPanel _nav = null!;
        private PulsingLogoPanel _brandDot = null!;
        private Label _brandLabel = null!;
        private Label _brandSubLabel = null!;
        private SidebarToggleButton _collapseToggle = null!;
        private System.Windows.Forms.Timer? _sidebarAnimTimer;
        private bool _sidebarCollapsed;
        private const int SidebarExpandedWidth = 232;
        private const int SidebarCollapsedWidth = 80;
        private HeaderWavePanel _header = null!;
        private double _headerWaveT;
        private System.Windows.Forms.Timer? _headerWaveTimer;
        private Panel _headerRule = null!;
        private Panel _content = null!;
        private Label _pageTitle = null!;
        private StatusStrip _status = null!;
        private readonly ToolStripStatusLabel _statusLabel = new("MotionDesk ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly System.Windows.Forms.Timer _statusTimer;
        private readonly System.Windows.Forms.Timer _brandPulseTimer;
        private double _brandPulseT;
        private NavButton? _activeButton;
        private readonly System.Collections.Generic.Dictionary<string, NavButton> _navButtons = new();
        private readonly System.Collections.Generic.Dictionary<string, Action> _pageBuilders = new();
        private string _currentPageKey = "Dashboard";
        private readonly HotkeyManager _hotkeys;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(System.IntPtr hwnd, int attr, ref int value, int size);

        public MainWindow()
        {
            var appSettings = AppSettings.Load();
            WidgetSnapEngine.EnableGridSnap = appSettings.GridSnap;
            WidgetSnapEngine.SnapThreshold = Math.Clamp(appSettings.SnapThreshold, 5, 50);
            WidgetSnapEngine.GridSize = Math.Clamp(appSettings.GridSize, 5, 100);
            Text = "MotionDesk Studio";
            Icon = LoadApplicationIcon();
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(960, 600);
            Size = new Size(1120, 700);
            Opacity = Math.Clamp(appSettings.WindowOpacity, 0.6, 1.0);

            _pageBuilders["Dashboard"] = ShowDashboard;
            _pageBuilders["Widgets"] = ShowWidgets;
            _pageBuilders["DeskZones"] = ShowDeskZones;
            _pageBuilders["Wallpaper"] = ShowWallpaper;
            _pageBuilders["Settings"] = ShowSettings;
            _pageBuilders["Profiles"] = ShowProfiles;
            _pageBuilders["Performance"] = ShowPerformance;
            _pageBuilders["Automation"] = ShowAutomation;
            _pageBuilders["Personalization"] = ShowPersonalization;
            _pageBuilders["About"] = ShowAbout;

            BuildChrome();
            ApplyChromeColors();

            if (appSettings.SidebarCollapsed)
            {
                _sidebarCollapsed = true;
                _sidebar.Width = SidebarCollapsedWidth;
                ApplySidebarCollapsedVisuals(true);
            }

            // "Αναπνέουσα" λάμψη στο λογότυπο του sidebar (όχι πλέον στο tray icon).
            _brandPulseTimer = new System.Windows.Forms.Timer { Interval = 60 };
            _brandPulseTimer.Tick += (_, _) => { _brandPulseT += 0.08; _brandDot.Invalidate(); };
            _brandPulseTimer.Start();

            _statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _statusTimer.Tick += (_, _) => UpdateStatus();
            _statusTimer.Start();
            _hotkeys = new HotkeyManager();
            _hotkeys.RegisterCtrlAlt('G', ToggleGamingMode);
            _hotkeys.RegisterCtrlAlt('M', ShowAndActivate);

            ThemeManager.Changed += OnThemeOrLanguageChanged;
            ThemeManager.Repainted += OnThemeRepainted;
            LocalizationManager.Changed += OnThemeOrLanguageChanged;
            SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;

            // Χαμηλού-κόστους hook, τρέχει σε όλη τη διάρκεια ζωής της εφαρμογής — δεν κάνει
            // τίποτα εκτός αν ο χρήστης κρατάει Shift ενώ σέρνει ένα παράθυρο (βλ. ZoneSnapEngine).
            ZoneSnapEngine.Start();

            FormClosed += (_, _) =>
            {
                try { WorkspaceProfileService.Save("Last Session"); } catch { }
                ThemeManager.Changed -= OnThemeOrLanguageChanged;
                ThemeManager.Repainted -= OnThemeRepainted;
                LocalizationManager.Changed -= OnThemeOrLanguageChanged;
                SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged;
                ZoneSnapEngine.Stop();
                _statusTimer.Dispose();
                _brandPulseTimer.Dispose();
                _sidebarAnimTimer?.Dispose();
                _headerWaveTimer?.Dispose();
                _hotkeys.Dispose();
            };

            ShowDashboard();
            var saved = AppSettings.Load();
            if (saved.RestoreLastSession && WorkspaceProfileService.ListProfiles().Contains("Last Session", StringComparer.OrdinalIgnoreCase))
            {
                try { WorkspaceProfileService.Load("Last Session"); } catch { }
            }
            else if (saved.ShowSystemMonitor && !WidgetHostEngine.Instance.GetActiveWidgets().Any(w => w.WidgetId == "sysmon"))
            {
                WidgetHostEngine.Instance.SpawnWidget("sysmon", 100, 100, 300, 200);
            }
            if (_navButtons.TryGetValue("Dashboard", out var dashboardButton))
                SetActiveButton(dashboardButton);
        }

        private void OnWindowsPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General) ThemeManager.RefreshFromWindows();
        }

        // ΠΑΝΤΑ μέσω BeginInvoke (όχι μόνο όταν InvokeRequired) — ζητήθηκε bug fix: όταν η
        // εφαρμογή άνοιγε σε Dark και ο χρήστης διάλεγε "Light" από το FlatComboBox της σελίδας
        // Ρυθμίσεων, η εφαρμογή "δεν αποκρινόταν". Αιτία: το SelectedIndexChanged του combo
        // (μέσα στο click handler του δικού του popup ContextMenuStrip) καλούσε συγχρονισμένα
        // ThemeManager.SetMode -> Changed -> εδώ -> πλήρη ανακατασκευή της τρέχουσας σελίδας
        // (dispose ΟΛΩΝ των controls, ΣΥΜΠΕΡΙΛΑΜΒΑΝΟΜΕΝΟΥ του ίδιου του combo) ΕΝΩ το popup ήταν
        // ακόμη στη μέση του δικού του closing/click-handling — κλασικό re-entrancy hazard, ίδιας
        // οικογένειας με το ήδη τεκμηριωμένο "ΟΧΙ using στο ContextMenuStrip" bug pattern αυτού
        // του project. Το BeginInvoke αναβάλλει την ανακατασκευή μέχρι να ξετυλιχτεί πλήρως το
        // τρέχον event πρώτα.
        private void OnThemeOrLanguageChanged()
        {
            if (IsDisposed) return;
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                ApplyChromeColors();
                RebuildSidebarNav();
                if (_pageBuilders.TryGetValue(_currentPageKey, out var builder)) builder();
            }));
        }

        // Φτηνιά ζωντανή προεπισκόπηση ενώ σέρνεις το slider σκουρότητας — μόνο Invalidate,
        // καμία ανακατασκευή control tree. Βλ. σχόλιο στο ThemeManager.Repainted.
        private void OnThemeRepainted()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(OnThemeRepainted)); return; }
            Invalidate(true);
        }

        private void BuildChrome()
        {
            _sidebar = new Panel { Dock = DockStyle.Left, Width = SidebarExpandedWidth, Padding = new Padding(16, 20, 16, 12) };

            // TableLayoutPanel αντί για Dock=Top (brand) + Dock=Fill (nav) απευθείας μέσα στο
            // padded _sidebar: σε αυτόν τον συνδυασμό το Fill rectangle του _nav υπολογιζόταν
            // λανθασμένα σαν να μην υπήρχε καθόλου δεσμευμένος χώρος από το _brandPanel (bug
            // που ΔΕΝ διορθωνόταν ούτε με ρητό PerformLayout/Invalidate μετά το Shown), με
            // αποτέλεσμα το _nav να ξεκινάει επικαλυπτόμενο με το brand και να κρύβει εντελώς
            // το πρώτο κουμπί πλοήγησης (Dashboard). Οι σταθερές δύο σειρές ενός
            // TableLayoutPanel δεν έχουν αυτή την ασάφεια.
            _sidebarLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            _sidebarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _sidebar.Controls.Add(_sidebarLayout);

            _brandPanel = new Panel { Dock = DockStyle.Fill };
            // Το ίδιο σχέδιο με το MotionDesk.ico (τετράγωνο, στρογγυλεμένο, gradient, δύο
            // κυματιστές γραμμές) αντί για απλό κύκλο. Οι γραμμές μετατοπίζουν φάση με τον
            // χρόνο (πραγματική κίνηση κύματος) — το halo έχει ΣΤΑΘΕΡΟ μέγεθος (μόνο η
            // διαφάνειά του παλλεται) ώστε να μη δίνει εντύπωση "μεγέθυνσης/σμίκρυνσης".
            // Μεγαλύτερο λογότυπο + τίτλος (ζητήθηκε ρητά) — όλες οι συντεταγμένες σχεδίασης
            // παραμένουν στο αρχικό 36x36 grid και κλιμακώνονται μέσω ScaleTransform, ώστε το
            // μέγεθος να ρυθμίζεται μόνο αλλάζοντας το Size παρακάτω.
            const int logoSize = 48;
            _brandDot = new PulsingLogoPanel { Size = new Size(logoSize, logoSize), Location = new Point(0, 2) };
            _brandDot.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // Δυναμική κλιμάκωση βάσει του ΤΡΕΧΟΝΤΟΣ Width του control (όχι της σταθεράς
                // logoSize) — έτσι το λογότυπο μπορεί να ξαναμεγεθυνθεί όταν συμπτύσσεται το
                // sidebar, χωρίς να "κόβεται" σε ένα στενότερο compact μενού.
                float scale = ((Control)s!).Width / 36f;
                g.ScaleTransform(scale, scale);
                double glowAlpha = Math.Sin(_brandPulseT) * 0.5 + 0.5;
                double wave = _brandPulseT * 1.6;

                using (var halo = new SolidBrush(Color.FromArgb((int)(25 + glowAlpha * 55), UiTheme.AccentCyan)))
                    g.FillEllipse(halo, 18 - 20, 18 - 20, 40, 40);

                var rect = new Rectangle(2, 2, 32, 32);
                using (var bgBrush = new LinearGradientBrush(rect, UiTheme.AccentCyan, UiTheme.AccentBlue, 45f))
                using (var path = UiTheme.RoundedPath(rect, 8))
                    g.FillPath(bgBrush, path);

                // Glossy highlight (ζητήθηκε ρητά): απαλή λευκή λάμψη στο πάνω μισό, σαν
                // γυαλιστερό κουμπί — clipped στο ίδιο rounded-rect ώστε να μη ξεχειλίζει.
                using (var glossPath = UiTheme.RoundedPath(rect, 8))
                {
                    var oldClip = g.Clip;
                    g.SetClip(glossPath, System.Drawing.Drawing2D.CombineMode.Intersect);
                    var glossRect = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height / 2 + 4);
                    using (var glossBrush = new LinearGradientBrush(glossRect, Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                        g.FillRectangle(glossBrush, glossRect);
                    g.Clip = oldClip;
                }

                float o1 = (float)(Math.Sin(wave) * 2.5);
                float o2 = (float)(Math.Sin(wave + 1.4) * 2.5);
                using (var pen1 = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawBezier(pen1, 8, 20 + o1, 13, 13 + o2, 21, 27 - o2, 26, 20 - o1);

                float o3 = (float)(Math.Sin(wave + 0.8) * 2.2);
                float o4 = (float)(Math.Sin(wave + 2.2) * 2.2);
                using (var pen2 = new Pen(Color.FromArgb(215, 255, 255, 255), 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawBezier(pen2, 8, 13 + o3, 13, 7 + o4, 21, 19 - o4, 26, 13 - o3);
            };
            _brandPanel.Controls.Add(_brandDot);
            const int brandTextX = logoSize + 10;
            _brandLabel = new Label { AutoSize = true, Location = new Point(brandTextX, 3), Text = "MotionDesk", Font = new Font("Segoe UI", 15.5f, FontStyle.Bold), AutoEllipsis = true };
            _brandPanel.Controls.Add(_brandLabel);
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            _brandSubLabel = new Label { AutoSize = true, Location = new Point(brandTextX, 32), Text = $"STUDIO   v{version?.ToString(3) ?? "1.0.0"}", Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            _brandPanel.Controls.Add(_brandSubLabel);
            _sidebarLayout.Controls.Add(_brandPanel, 0, 0);

            // ΟΧΙ AutoScroll: μια κάθετη scrollbar (όταν τα 9 nav items δεν χωρούν καθ' ύψος)
            // "έτρωγε" πλάτος από το ήδη στενό, συμπτυγμένο μενού (64px), προκαλώντας ΚΑΙ
            // οριζόντια scrollbar σε καταρράκτη — αφού τα τετράγωνα πλακίδια είχαν υπολογιστεί
            // να γεμίζουν ΑΚΡΙΒΩΣ το διαθέσιμο πλάτος, χωρίς περιθώριο. Ζητήθηκε ρητά καμία
            // scrollbar· καλύτερα να κόβεται καθαρά ένα τελευταίο στοιχείο παρά να εμφανίζεται.
            _nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false, Padding = new Padding(0, 14, 0, 0) };
            _sidebarLayout.Controls.Add(_nav, 0, 1);
            RebuildSidebarNav();

            _collapseToggle = new SidebarToggleButton();
            _collapseToggle.Click += (_, _) => ToggleSidebarCollapsed();
            _sidebarLayout.Controls.Add(_collapseToggle, 0, 2);

            _content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(32, 24, 32, 24) };

            // Απαλό, κινούμενο κύμα-φόντο πίσω από τον τίτλο κάθε σελίδας (ζητήθηκε ρητά) — ίδια
            // "γλώσσα" με το wallpaper/λογότυπο, αλλά πολύ χαμηλής έντασης ώστε να μην αποσπά.
            _header = new HeaderWavePanel(() => _headerWaveT) { Dock = DockStyle.Top, Height = 64 };
            _pageTitle = new Label { Dock = DockStyle.Fill, Text = "Dashboard", Font = UiTheme.FontHeading, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
            _header.Controls.Add(_pageTitle);
            _headerRule = new Panel { Dock = DockStyle.Bottom, Height = 1 };
            _header.Controls.Add(_headerRule);
            _headerWaveTimer = new System.Windows.Forms.Timer { Interval = 45 };
            _headerWaveTimer.Tick += (_, _) => { _headerWaveT += 0.03; _header.Invalidate(); };
            _headerWaveTimer.Start();

            _status = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = false };
            _status.Items.Add(_statusLabel);

            Controls.Add(_content);
            Controls.Add(_header);
            Controls.Add(_sidebar);
            Controls.Add(_status);
        }

        private void RebuildSidebarNav()
        {
            _nav.Controls.Clear();
            _navButtons.Clear();
            _activeButton = null;
            AddNavButton(_nav, LocalizationManager.T("Nav.Dashboard"), "Dashboard", ShowDashboard, 1);
            AddNavButton(_nav, LocalizationManager.T("Nav.Widgets"), "Widgets", ShowWidgets, 2);
            AddNavButton(_nav, LocalizationManager.T("Nav.DeskZones"), "DeskZones", ShowDeskZones, 3);
            AddNavButton(_nav, LocalizationManager.T("Nav.Wallpaper"), "Wallpaper", ShowWallpaper, 4);
            AddNavButton(_nav, LocalizationManager.T("Nav.Performance"), "Performance", ShowPerformance, 5);
            AddNavButton(_nav, LocalizationManager.T("Nav.Settings"), "Settings", ShowSettings, 6);
            AddNavButton(_nav, LocalizationManager.T("Nav.Profiles"), "Profiles", ShowProfiles, 7);
            AddNavButton(_nav, LocalizationManager.T("Nav.Automation"), "Automation", ShowAutomation, 8);
            AddNavButton(_nav, LocalizationManager.T("Nav.Personalization"), "Personalization", ShowPersonalization, 0);
            AddNavButton(_nav, LocalizationManager.T("Nav.About"), "About", ShowAbout, 9);

            if (_sidebarCollapsed) foreach (var btn in _navButtons.Values) btn.Collapsed = true;
            if (_navButtons.TryGetValue(_currentPageKey, out var active)) SetActiveButton(active);
        }

        private void ApplyChromeColors()
        {
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.TextPrimary;
            _sidebar.BackColor = UiTheme.Sidebar;
            _sidebarLayout.BackColor = UiTheme.Sidebar;
            _brandPanel.BackColor = UiTheme.Sidebar;
            _brandDot.BackColor = UiTheme.Sidebar;
            _content.BackColor = UiTheme.Background;
            _header.BackColor = UiTheme.Background;
            _headerRule.BackColor = UiTheme.Border;
            _pageTitle.ForeColor = UiTheme.TextPrimary;
            _status.BackColor = UiTheme.Sidebar;
            _statusLabel.ForeColor = UiTheme.TextSecondary;
            _brandLabel.ForeColor = UiTheme.TextPrimary;
            _brandSubLabel.ForeColor = UiTheme.TextMuted;

            int useDark = UiTheme.Background.GetBrightness() < 0.5f ? 1 : 0;
            try { if (IsHandleCreated) DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)); }
            catch (DllNotFoundException) { }

            Invalidate(true);
        }

        private NavButton AddNavButton(Control parent, string text, string key, Action action, int shortcutIndex)
        {
            var button = new NavButton(key, text, $"Ctrl+{shortcutIndex}") { Width = 184 };
            button.Click += (_, _) => { _currentPageKey = key; action(); };
            button.Click += (_, _) => SetActiveButton(button);
            button.Click += (_, _) => Services.UiSounds.PlayClick();
            parent.Controls.Add(button);
            _navButtons[key] = button;
            return button;
        }

        /// <summary>Επιτρέπει σε εξωτερικό caller (π.χ. το tray context menu) να ανοίξει το
        /// κύριο παράθυρο κατευθείαν σε συγκεκριμένη σελίδα αντί για το Dashboard.</summary>
        public void NavigateTo(string pageKey)
        {
            if (_navButtons.TryGetValue(pageKey, out var button))
                button.PerformClick();
        }

        private void SetActiveButton(NavButton button)
        {
            if (_activeButton != null)
            {
                _activeButton.IsActive = false;
                _activeButton.Invalidate();
            }

            _activeButton = button;
            _activeButton.IsActive = true;
            _activeButton.Invalidate();
        }

        // Απλό double-buffered Panel για το owner-draw λογότυπο — χωρίς αυτό, οι συχνές
        // Invalidate() κλήσεις του animation timer προκαλούσαν εμφανές flicker.
        private sealed class PulsingLogoPanel : Panel
        {
            public PulsingLogoPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
        }

        // Header background: 3 πολύ χαμηλής έντασης κυματιστές γραμμές που ταξιδεύουν αργά,
        // πίσω από τον τίτλο κάθε σελίδας — ζητήθηκε ρητά "κινούμενο φόντο με κύματα".
        private sealed class HeaderWavePanel : Panel
        {
            private readonly Func<double> _getT;
            public HeaderWavePanel(Func<double> getT)
            {
                _getT = getT;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bg = new SolidBrush(UiTheme.Background))
                    g.FillRectangle(bg, ClientRectangle);

                double t = _getT();
                var colors = new[] { UiTheme.AccentCyan, UiTheme.AccentBlue, UiTheme.AccentCyan };
                for (int i = 0; i < 3; i++)
                {
                    double phase = t * (0.5 + i * 0.15) + i * 1.7;
                    double amp = 6 + i * 3;
                    double baseline = Height * (0.35 + i * 0.28);
                    var pts = new System.Collections.Generic.List<PointF>();
                    for (int x = 0; x <= Width; x += 16)
                        pts.Add(new PointF(x, (float)(baseline + Math.Sin(phase + x * 0.012) * amp)));
                    if (pts.Count < 2) continue;
                    using var pen = new Pen(Color.FromArgb(38 - i * 6, colors[i]), 1.6f) { LineJoin = LineJoin.Round };
                    g.DrawLines(pen, pts.ToArray());
                }
            }
        }

        // Owner-draw nav item: εικονίδιο (Segoe Fluent Icons) + ετικέτα, με accent bar και
        // hover/active state. Βασισμένο σε Panel (όχι Button) — ένα ButtonBase-specific quirk
        // (η αυτόματη "default button" ειδοποίηση του Form προς το πρώτο Button ενός container)
        // εμπόδιζε ολοκληρωτικά το πρώτο owner-draw Button του sidebar να ζωγραφιστεί ποτέ.
        private sealed class NavButton : Panel
        {
            private readonly string _iconKey;
            private bool _hover;
            private bool _collapsed;
            public bool IsActive { get; set; }
            public new event EventHandler? Click;

            // Σε "συμπτυγμένη" λειτουργία δείχνει ΜΟΝΟ το (μεγαλύτερο, κεντραρισμένο) εικονίδιο —
            // ζητήθηκε ρητά: το πλευρικό μενού να μπορεί να συμπτύσσεται με μεγαλύτερα εικονίδια.
            public bool Collapsed
            {
                get => _collapsed;
                set
                {
                    if (_collapsed == value) return;
                    _collapsed = value;
                    // 56 (όχι ίσο με το πλήρες πλάτος του container, 64) — σκόπιμο περιθώριο
                    // ασφαλείας ώστε τίποτα να μην ακουμπάει/κόβεται στο δεξί άκρο, ό,τι κι αν
                    // προκαλούσε το προηγούμενο ζήτημα ("κόβονται τα εικονίδια").
                    Width = value ? 56 : 184;
                    Invalidate();
                }
            }

            private readonly ToolTip _tip = new() { InitialDelay = 300, AutoPopDelay = 4000 };
            private readonly string? _shortcutHint;

            // Tooltip με τη συντόμευση πληκτρολογίου (π.χ. "Widgets (Ctrl+2)") — πάντα ορατό,
            // όχι μόνο στη συμπτυγμένη λειτουργία, ώστε οι συντομεύσεις να είναι ανακαλύψιμες
            // μέσα στο ίδιο το UI (ζητήθηκε ρητά στο roadmap).
            public NavButton(string iconKey, string text, string? shortcutHint = null)
            {
                _iconKey = iconKey;
                Text = text;
                _shortcutHint = shortcutHint;
                Height = 42;
                Margin = new Padding(0, 0, 0, 4);
                TabStop = false;
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
                MouseEnter += (_, _) => { _hover = true; Invalidate(); };
                MouseLeave += (_, _) => { _hover = false; Invalidate(); };
                MouseUp += (_, e) => { if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) Click?.Invoke(this, EventArgs.Empty); };
                if (_shortcutHint != null) _tip.SetToolTip(this, $"{Text} ({_shortcutHint})");
            }

            public void PerformClick() => Click?.Invoke(this, EventArgs.Empty);

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var iconColor = IsActive ? UiTheme.AccentCyan : UiTheme.TextSecondary;

                // Συμπτυγμένη λειτουργία: ξεχωριστό, τετράγωνο πλακίδιο-κουμπί (όχι το ίδιο
                // επίμηκες pill του πλήρους sidebar) με μεγαλύτερο εικονίδιο — ζητήθηκε ρητά
                // "τετράγωνα κουμπιά" + μεγαλύτερα εικονίδια στη σύμπτυξη.
                if (_collapsed)
                {
                    const int tile = 42;
                    var tileRect = new Rectangle((Width - tile) / 2, (Height - tile) / 2, tile, tile);
                    var bg = IsActive ? UiTheme.SurfaceHover : (_hover ? UiTheme.Surface : UiTheme.Sidebar);
                    using (var bgBrush = new SolidBrush(bg))
                    using (var path = UiTheme.RoundedPath(tileRect, 6))
                        g.FillPath(bgBrush, path);
                    using (var borderPen = new Pen(IsActive ? UiTheme.AccentCyan : UiTheme.Border, IsActive ? 1.6f : 1f))
                    using (var path = UiTheme.RoundedPath(tileRect, 6))
                        g.DrawPath(borderPen, path);

                    const int iconSize = 26;
                    IconRenderer.Draw(g, _iconKey, new RectangleF(tileRect.X + (tile - iconSize) / 2f, tileRect.Y + (tile - iconSize) / 2f, iconSize, iconSize), iconColor);
                    return;
                }

                var expandedBg = IsActive ? UiTheme.SurfaceHover : (_hover ? UiTheme.Surface : UiTheme.Sidebar);
                using (var bgBrush = new SolidBrush(expandedBg))
                using (var path = UiTheme.RoundedPath(new Rectangle(4, 2, Width - 8, Height - 4), 8))
                    g.FillPath(bgBrush, path);

                if (IsActive)
                {
                    using var accentBrush = new SolidBrush(UiTheme.AccentCyan);
                    g.FillRectangle(accentBrush, 0, 8, 3, Height - 16);
                }

                IconRenderer.Draw(g, _iconKey, new RectangleF(14, (Height - 20) / 2f, 20, 20), iconColor);

                var textColor = IsActive ? UiTheme.TextPrimary : UiTheme.TextSecondary;
                using var textFont = new Font("Segoe UI", 9.5f, IsActive ? FontStyle.Bold : FontStyle.Regular);
                using (var textBrush = new SolidBrush(textColor))
                    g.DrawString(Text, textFont, textBrush, 42, (Height - textFont.Height) / 2f);
            }
        }

        // Μικρό κουμπί σύμπτυξης/επέκτασης στο κάτω μέρος του sidebar — το βέλος αλλάζει
        // κατεύθυνση ανάλογα με την τρέχουσα κατάσταση.
        private sealed class SidebarToggleButton : Panel
        {
            private bool _hover;
            private bool _collapsed;
            public new event EventHandler? Click;

            public bool Collapsed { get => _collapsed; set { _collapsed = value; Invalidate(); } }

            public SidebarToggleButton()
            {
                Dock = DockStyle.Fill;
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
                MouseEnter += (_, _) => { _hover = true; Invalidate(); };
                MouseLeave += (_, _) => { _hover = false; Invalidate(); };
                MouseUp += (_, e) => { if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) Click?.Invoke(this, EventArgs.Empty); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (_hover)
                {
                    using var bg = new SolidBrush(UiTheme.Surface);
                    using var path = UiTheme.RoundedPath(new Rectangle(4, 2, Width - 8, Height - 4), 8);
                    g.FillPath(bg, path);
                }
                using var pen = new Pen(UiTheme.TextSecondary, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                float cx = Width / 2f, cy = Height / 2f;
                float dir = _collapsed ? 1 : -1;
                g.DrawLines(pen, new[] { new PointF(cx + 3 * dir, cy - 5), new PointF(cx - 3 * dir, cy), new PointF(cx + 3 * dir, cy + 5) });
            }
        }

        // Ζωντανή animation (ease-out) για το πλάτος του sidebar κατά τη σύμπτυξη/επέκταση —
        // ζητήθηκε ρητά "εφέ κύλισης" αντί για στιγμιαία αλλαγή μεγέθους.
        private void ToggleSidebarCollapsed()
        {
            _sidebarCollapsed = !_sidebarCollapsed;
            var appSettings = AppSettings.Load();
            appSettings.SidebarCollapsed = _sidebarCollapsed;
            appSettings.Save();

            // Η σειρά έχει σημασία (πριν όχι — αυτό έδινε ένα "άκυρο" απότομο snap): όταν
            // ΣΥΜΠΤΥΣΣΟΥΜΕ, πρώτα ζωγραφίζουμε πλήρες μενού που απλά στενεύει/κόβεται καθώς
            // τρέχει το animation, και ΜΟΝΟ όταν φτάσει στο τελικό στενό πλάτος αλλάζουμε σε
            // τετράγωνα πλακίδια/μεγάλα εικονίδια. Όταν ΕΠΕΚΤΕΙΝΟΥΜΕ, κάνουμε το αντίστροφο:
            // πρώτα ετοιμάζουμε την πλήρη διάταξη, μετά φαρδαίνει το sidebar να την αποκαλύψει.
            if (_sidebarCollapsed)
                AnimateSidebarWidth(SidebarCollapsedWidth, () => ApplySidebarCollapsedVisuals(true));
            else
            {
                ApplySidebarCollapsedVisuals(false);
                AnimateSidebarWidth(SidebarExpandedWidth);
            }
        }

        // Με πλάτος sidebar 64px στη συμπτυγμένη λειτουργία και το ΙΔΙΟ Padding(16,...,16,...)
        // του πλήρους sidebar, το χρησιμοποιήσιμο πλάτος έπεφτε στα 32px — μικρότερο από το
        // λογότυπο (48px) και τα τετράγωνα πλακίδια των κουμπιών (48px), δηλαδή "δεν χωρούσαν".
        // Εδώ μειώνουμε το πλευρικό padding ΚΑΙ ξαναμεγεθύνουμε/κεντράρουμε το λογότυπο ώστε να
        // χωράει σωστά στο πραγματικά διαθέσιμο πλάτος.
        private void ApplySidebarCollapsedVisuals(bool collapsed)
        {
            _sidebar.Padding = collapsed ? new Padding(8, 20, 8, 12) : new Padding(16, 20, 16, 12);

            const int expandedLogoSize = 48;
            const int collapsedLogoSize = 40;
            int usableWidth = (collapsed ? SidebarCollapsedWidth : SidebarExpandedWidth) - _sidebar.Padding.Horizontal;
            if (collapsed)
            {
                _brandDot.Size = new Size(collapsedLogoSize, collapsedLogoSize);
                _brandDot.Location = new Point(Math.Max(0, (usableWidth - collapsedLogoSize) / 2), 6);
            }
            else
            {
                _brandDot.Size = new Size(expandedLogoSize, expandedLogoSize);
                _brandDot.Location = new Point(0, 2);
            }

            _collapseToggle.Collapsed = collapsed;
            _brandLabel.Visible = !collapsed;
            _brandSubLabel.Visible = !collapsed;
            foreach (var btn in _navButtons.Values) { btn.Collapsed = collapsed; btn.Refresh(); }
            _nav.Refresh();
        }

        private void AnimateSidebarWidth(int targetWidth, Action? onComplete = null)
        {
            _sidebarAnimTimer?.Stop();
            _sidebarAnimTimer?.Dispose();
            int startWidth = _sidebar.Width;
            const int totalSteps = 10;
            int step = 0;
            _sidebarAnimTimer = new System.Windows.Forms.Timer { Interval = 12 };
            _sidebarAnimTimer.Tick += (_, _) =>
            {
                step++;
                double t = Math.Min(1.0, step / (double)totalSteps);
                double eased = 1 - Math.Pow(1 - t, 3);
                _sidebar.Width = (int)(startWidth + (targetWidth - startWidth) * eased);
                if (t >= 1.0)
                {
                    _sidebar.Width = targetWidth;
                    _sidebarAnimTimer?.Stop();
                    onComplete?.Invoke();
                }
            };
            _sidebarAnimTimer.Start();
        }

        private void SetPage(string titleKey, Control control)
        {
            _pageTitle.Text = LocalizationManager.T(titleKey);
            _content.SuspendLayout();
            _content.Controls.Clear();
            control.Dock = DockStyle.Fill;
            _content.Controls.Add(control);
            _content.ResumeLayout();
        }

        private void ShowAndActivate()
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Show();
            BringToFront();
            Activate();
        }

        // Συντομεύσεις πληκτρολογίου για κάθε βασική λειτουργία της εφαρμογής (ζητήθηκε ρητά).
        // ProcessCmdKey αντί για απλό KeyDown ώστε να δουλεύουν ΟΠΟΥΔΗΠΟΤΕ μέσα στο παράθυρο,
        // ακόμη κι όταν ένα child control (π.χ. TextBox) έχει focus.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData is >= (Keys.Control | Keys.D1) and <= (Keys.Control | Keys.D9))
            {
                int index = (int)(keyData & Keys.KeyCode) - (int)Keys.D1;
                var order = new[] { "Dashboard", "Widgets", "DeskZones", "Wallpaper", "Performance", "Settings", "Profiles", "Automation", "About" };
                if (index >= 0 && index < order.Length) { NavigateTo(order[index]); return true; }
            }

            switch (keyData)
            {
                case Keys.Control | Keys.D0:
                    NavigateTo("Personalization");
                    return true;
                case Keys.Control | Keys.K:
                    ShowCommandPalette();
                    return true;
                case Keys.Control | Keys.B:
                    ToggleSidebarCollapsed();
                    return true;
                case Keys.Control | Keys.E:
                    if (WallpaperHostEngine.Instance.IsEnabled) WallpaperHostEngine.Instance.Disable();
                    else WallpaperHostEngine.Instance.Enable();
                    if (_currentPageKey == "Wallpaper" || _currentPageKey == "Dashboard") { if (_pageBuilders.TryGetValue(_currentPageKey, out var b)) b(); }
                    return true;
                case Keys.Control | Keys.Shift | Keys.N:
                    using (var editor = new ZoneLayoutEditorForm())
                        if (editor.ShowDialog(this) == DialogResult.OK) _statusLabel.Text = "DeskZone layout updated";
                    if (_currentPageKey == "DeskZones" && _pageBuilders.TryGetValue("DeskZones", out var dzBuilder)) dzBuilder();
                    return true;
                case Keys.Control | Keys.Oemcomma:
                    NavigateTo("Settings");
                    return true;
                case Keys.F1:
                    NavigateTo("About");
                    return true;
                case Keys.F5:
                    if (_pageBuilders.TryGetValue(_currentPageKey, out var builder)) builder();
                    return true;
                case Keys.Control | Keys.Alt | Keys.F:
                    Flip3DEngine.Show();
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowDashboard()
        {
            _currentPageKey = "Dashboard";
            var panel = CreatePagePanel();
            AddHeading(panel, "MotionDesk Studio");
            AddText(panel, LocalizationManager.T("Dashboard.Intro"));

            var metrics = SystemMonitorService.Instance.GetSnapshot();
            // "System" card = ζωντανή σύνοψη + συντόμευση στο πλήρες System Monitor (εκεί μένει η λεπτομέρεια:
            // CPU/RAM/δίκτυο/processes). Η γραμμή κατάστασης κάτω-κάτω δείχνει μόνο ένα ambient CPU/RAM glance.
            var systemCard = AddCard(panel, "System (κλικ για λεπτομέρειες →)",
                $"CPU {metrics.CpuPercent:0.0}%   •   RAM {metrics.AvailableMemoryMb:0} / {metrics.TotalMemoryMb:0} MB free",
                onClick: () => NavigateTo("Performance"));
            AddCard(panel, "Desktop", $"{Screen.AllScreens.Length} monitor(s)   •   {WidgetHostEngine.Instance.GetActiveWidgets().Count} active widget(s)   •   {Screen.AllScreens.Sum(s => ZoneLayoutStore.GetLayout(s.DeviceName).Zones.Count)} zone(s)");
            AddCard(panel, "Wallpaper", $"{(WallpaperHostEngine.Instance.IsEnabled ? "ON" : "OFF")}   •   {WallpaperSettings.Load().Mode}   •   {WallpaperSettings.Load().PerformanceMode}");

            var dashboardRefreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            dashboardRefreshTimer.Tick += (_, _) =>
            {
                var m = SystemMonitorService.Instance.GetSnapshot();
                systemCard.Text = $"CPU {m.CpuPercent:0.0}%   •   RAM {m.AvailableMemoryMb:0} / {m.TotalMemoryMb:0} MB free";
            };
            dashboardRefreshTimer.Start();
            panel.Disposed += (_, _) => dashboardRefreshTimer.Dispose();

            AddSection(panel, LocalizationManager.T("Dashboard.SectionQuickLaunch"));
            AddIconButtonGrid(panel,
                ("Widgets", "Open Widget Gallery", (_, _) => NavigateTo("Widgets")),
                ("DeskZones", "Edit DeskZone layout", (_, _) => { using var editor = new ZoneLayoutEditorForm(); editor.ShowDialog(this); }),
                ("Wallpaper", "Enable Wallpaper", (_, _) => { WallpaperHostEngine.Instance.Enable(); _statusLabel.Text = "Wallpaper enabled"; }),
                ("Command", "Command Palette", (_, _) => ShowCommandPalette()));

            AddSection(panel, LocalizationManager.T("Dashboard.SectionWorkspace"));
            AddIconButtonGrid(panel,
                ("Work", "Work Profile", (_, _) => LoadProfileFromQuickButton("Work")),
                ("Gaming", "Gaming Profile", (_, _) => LoadProfileFromQuickButton("Gaming")),
                ("Focus", "Focus Profile", (_, _) => LoadProfileFromQuickButton("Focus")));
            SetPage("Page.Dashboard.Title", panel);
        }

        private void ShowWidgets()
        {
            _currentPageKey = "Widgets";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Widgets.Intro"));
            AddSection(panel, LocalizationManager.T("Widgets.SectionDesktopWidgets"));
            AddIconButtonGrid(panel,
                ("SystemMonitor", "System Monitor", (_, _) => WidgetHostEngine.Instance.SpawnWidget("sysmon", 100, 100, 320, 220)),
                ("Clock", "Clock", (_, _) => WidgetHostEngine.Instance.SpawnWidget("clock", 450, 100, 300, 220)),
                ("Network", "Network", (_, _) => WidgetHostEngine.Instance.SpawnWidget("network", 100, 350, 320, 220)),
                ("AudioVisualizer", "Audio Visualizer", (_, _) => WidgetHostEngine.Instance.SpawnWidget("audio", 450, 350, 300, 220)),
                ("Weather", "Weather", (_, _) => WidgetHostEngine.Instance.SpawnWidget("weather", 800, 100, 300, 220)));
            AddSection(panel, LocalizationManager.T("Widgets.SectionWorkspaceActions"));
            AddIconButtonGrid(panel,
                ("Save", "Save current widget layout", (_, _) => { WorkspaceProfileService.Save("Last Session"); _statusLabel.Text = "Widget layout saved"; }),
                ("Restore", "Restore saved widget layout", (_, _) => { WorkspaceProfileService.Load("Last Session"); _statusLabel.Text = "Workspace restored"; }),
                ("Close", "Close all widgets", (_, _) => { WidgetHostEngine.Instance.CloseAll(); }));

            AddSection(panel, LocalizationManager.T("Widgets.SectionActiveWidgets"));
            var activeList = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            panel.Controls.Add(activeList);

            void RefreshActiveList()
            {
                activeList.SuspendLayout();
                activeList.Controls.Clear();
                var active = WidgetHostEngine.Instance.GetActiveWidgets();
                if (active.Count == 0)
                {
                    activeList.Controls.Add(new Label { AutoSize = true, Text = LocalizationManager.T("Common.NoActiveWidgets"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody });
                }
                foreach (var w in active)
                {
                    var row = new Panel { Width = 500, Height = 40, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 6) };
                    UiTheme.ApplyRoundedRegion(row, 6);
                    row.Controls.Add(new Label { Text = w.WidgetId, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, Location = new Point(14, 11), AutoSize = true });
                    var lockBtn = new HoverButton { Text = w.IsLocked ? "Unlock" : "Lock", Width = 80, Height = 28, Location = new Point(310, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    lockBtn.FlatAppearance.BorderSize = 0;
                    lockBtn.Click += (_, _) => { w.IsLocked = !w.IsLocked; RefreshActiveList(); };
                    row.Controls.Add(lockBtn);
                    var closeBtn = new HoverButton { Text = "Close", Width = 80, Height = 28, Location = new Point(400, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    closeBtn.FlatAppearance.BorderSize = 0;
                    closeBtn.Click += (_, _) => { w.Close(); RefreshActiveList(); };
                    row.Controls.Add(closeBtn);
                    activeList.Controls.Add(row);
                }
                activeList.ResumeLayout();
            }
            RefreshActiveList();
            var widgetsRefreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            widgetsRefreshTimer.Tick += (_, _) => RefreshActiveList();
            widgetsRefreshTimer.Start();
            panel.Disposed += (_, _) => widgetsRefreshTimer.Dispose();

            SetPage("Page.Widgets.Title", panel);
        }

        private void ShowDeskZones()
        {
            _currentPageKey = "DeskZones";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("DeskZones.Intro"));
            AddSection(panel, LocalizationManager.T("DeskZones.SectionLayouts"));
            AddText(panel, LocalizationManager.T("DeskZones.ShiftDragHint"));

            foreach (var screen in Screen.AllScreens)
            {
                var row = new Panel { Width = 720, Height = 130, Margin = new Padding(0, 0, 0, 10) };
                var preview = new ZonePreviewPanel { Location = new Point(0, 0), Size = new Size(220, 124) };
                var layout = ZoneLayoutStore.GetLayout(screen.DeviceName);
                preview.SetLayout(layout);
                row.Controls.Add(preview);

                string screenLabel = Screen.AllScreens.Length > 1
                    ? string.Format(LocalizationManager.T(screen.Primary ? "Wallpaper.ScreenLabelPrimary" : "Wallpaper.ScreenLabel"), Array.IndexOf(Screen.AllScreens, screen) + 1)
                    : LocalizationManager.T("DeskZones.EditorSingleScreen");
                row.Controls.Add(new Label { Text = $"{screenLabel}  ({screen.Bounds.Width}×{screen.Bounds.Height})", ForeColor = UiTheme.TextPrimary, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Location = new Point(236, 8), AutoSize = true });
                row.Controls.Add(new Label { Text = LocalizationManager.T("DeskZones.Template." + layout.Template.Replace(" ", "")), ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Location = new Point(236, 34), AutoSize = true, Name = "templateLabel" });

                var editBtn = new PillButton { Text = LocalizationManager.T("DeskZones.EditorApply2"), Width = 160, Height = 32, Location = new Point(236, 64), BackColor = UiTheme.Surface, ForeColor = UiTheme.AccentCyan };
                var capturedScreen = screen;
                editBtn.Click += (_, _) =>
                {
                    using var editor = new ZoneLayoutEditorForm(capturedScreen);
                    if (editor.ShowDialog(this) == DialogResult.OK)
                    {
                        var updated = ZoneLayoutStore.GetLayout(capturedScreen.DeviceName);
                        preview.SetLayout(updated);
                        foreach (Control c in row.Controls) if (c.Name == "templateLabel") c.Text = LocalizationManager.T("DeskZones.Template." + updated.Template.Replace(" ", ""));
                    }
                };
                row.Controls.Add(editBtn);
                panel.Controls.Add(row);
            }

            // Ξεχωριστή, δεύτερη λειτουργία στην ίδια σελίδα: DeskContainers — η "πραγματική"
            // έννοια των Fences (ομαδοποίηση αρχείων/φακέλων/συντομεύσεων, όχι παραθύρων).
            AddSection(panel, LocalizationManager.T("DeskZones.SectionContainers"));
            AddText(panel, LocalizationManager.T("DeskZones.ContainersIntro"));
            AddIconButtonGrid(panel,
                ("Container", "New DeskContainer", (_, _) => DeskContainerHostEngine.Instance.SpawnContainer($"container{DateTime.Now.Ticks}", "New Container", 360, 200, 360, 260)),
                ("Close", "Close all containers", (_, _) => DeskContainerHostEngine.Instance.CloseAll()));

            var activeContainersList = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            panel.Controls.Add(activeContainersList);

            void RefreshActiveContainers()
            {
                activeContainersList.SuspendLayout();
                activeContainersList.Controls.Clear();
                var active = DeskContainerHostEngine.Instance.GetActiveContainers();
                if (active.Count == 0)
                {
                    activeContainersList.Controls.Add(new Label { AutoSize = true, Text = LocalizationManager.T("Common.NoActiveContainers"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody });
                }
                foreach (var c in active)
                {
                    var row = new Panel { Width = 500, Height = 40, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 6) };
                    UiTheme.ApplyRoundedRegion(row, 6);
                    row.Controls.Add(new Label { Text = c.ContainerTitle, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, Location = new Point(14, 11), AutoSize = true });
                    var closeBtn = new HoverButton { Text = "Close", Width = 80, Height = 28, Location = new Point(400, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    closeBtn.FlatAppearance.BorderSize = 0;
                    closeBtn.Click += (_, _) => { c.Close(); RefreshActiveContainers(); };
                    row.Controls.Add(closeBtn);
                    activeContainersList.Controls.Add(row);
                }
                activeContainersList.ResumeLayout();
            }
            RefreshActiveContainers();
            var containersRefreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            containersRefreshTimer.Tick += (_, _) => RefreshActiveContainers();
            containersRefreshTimer.Start();
            panel.Disposed += (_, _) => containersRefreshTimer.Dispose();

            SetPage("Page.DeskZones.Title", panel);
        }

        private void ShowWallpaper()
        {
            _currentPageKey = "Wallpaper";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Wallpaper.Intro"));

            var settings = WallpaperSettings.Load();
            var statusCard = AddCard(panel, LocalizationManager.T("Wallpaper.StatusLabel"), DescribeWallpaperState(settings));
            var playlistCard = AddCard(panel, "Playlist", DescribePlaylist(settings));

            void RefreshStatus()
            {
                var s = WallpaperSettings.Load();
                statusCard.Text = DescribeWallpaperState(s);
                playlistCard.Text = DescribePlaylist(s);
            }

            var enableButtons = AddToggleButtonGrid(panel,
                ("Enable", (_, _) => { WallpaperHostEngine.Instance.Enable(); RefreshStatus(); }),
                ("Disable", (_, _) => { WallpaperHostEngine.Instance.Disable(); RefreshStatus(); }));
            MarkActive(enableButtons, WallpaperHostEngine.Instance.IsEnabled ? "Enable" : "Disable");

            AddSection(panel, LocalizationManager.T("Wallpaper.SectionMode"));
            var modeCombo = new FlatComboBox { Width = 200, Margin = new Padding(0, 0, 0, 10) };
            modeCombo.SetItems(new[] { "Waves", "Video", "Particles" }, settings.Mode);
            // Ξαναχτίζει ΟΛΟΚΛΗΡΗ τη σελίδα κάθε φορά που αλλάζει το Mode — ζητήθηκε ρητά bug fix:
            // πριν, οι ενότητες "Video Library"/"Theme & Colors"/"Wave Tuning" ήταν ΠΑΝΤΑ ορατές
            // ανεξάρτητα από το επιλεγμένο mode, οπότε η επιλογή βίντεο έμενε στην οθόνη ακόμη κι
            // όταν ο χρήστης διάλεγε "Particles" — φαινόταν σαν να "ανακατευθύνει" σε βίντεο ενώ
            // απλά ποτέ δεν κρυβόταν.
            modeCombo.SelectedIndexChanged += (_, _) => { WallpaperHostEngine.Instance.SetMode(modeCombo.SelectedItem!); ShowWallpaper(); };
            panel.Controls.Add(modeCombo);

            if (settings.Mode == "Video")
            {
            AddSection(panel, LocalizationManager.T("Wallpaper.SectionVideoLibrary"));
            AddIconButtonGrid(panel,
                ("Add", "Προσθήκη βίντεο…", (_, _) => { ChooseWallpaperVideoFiles(); RefreshStatus(); }),
                ("Add", "Προσθήκη φακέλου…", (_, _) => { ChooseWallpaperFolder(); RefreshStatus(); }),
                ("Delete", "Καθαρισμός playlist", (_, _) => { WallpaperHostEngine.Instance.ClearVideo(); RefreshStatus(); }));

            var shuffle = new CheckBox { Text = "Shuffle", AutoSize = true, Checked = settings.Shuffle, ForeColor = UiTheme.TextPrimary, Margin = new Padding(0, 4, 0, 10) };
            shuffle.CheckedChanged += (_, _) => WallpaperHostEngine.Instance.SetShuffle(shuffle.Checked);
            panel.Controls.Add(shuffle);

            AddText(panel, LocalizationManager.T("Wallpaper.SupportedFormats"));
            }

            // Ζητήθηκε ρητά "διαφορετικό βίντεο ανά οθόνη" — εμφανίζεται μόνο όταν υπάρχουν
            // πράγματι πολλαπλές οθόνες ΚΑΙ το mode είναι Video (άσχετο για Waves/Particles).
            // Κάθε οθόνη μπορεί να "καρφιτσωθεί" σε ένα συγκεκριμένο βίντεο της βιβλιοθήκης, ή να
            // μείνει στο κοινό μοιρασμένο playlist (προεπιλογή).
            if (Screen.AllScreens.Length > 1 && settings.Mode == "Video")
            {
                AddSection(panel, LocalizationManager.T("Wallpaper.SectionPerScreenVideo"));
                string sharedOption = LocalizationManager.T("Wallpaper.SharedPlaylistOption");
                for (int screenIndex = 0; screenIndex < Screen.AllScreens.Length; screenIndex++)
                {
                    var targetScreen = Screen.AllScreens[screenIndex];
                    var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 6) };
                    string screenLabel = string.Format(LocalizationManager.T(targetScreen.Primary ? "Wallpaper.ScreenLabelPrimary" : "Wallpaper.ScreenLabel"), screenIndex + 1);
                    row.Controls.Add(new Label { Text = screenLabel + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0), Width = 140 });

                    var screenCombo = new FlatComboBox { Width = 260 };
                    var videoNames = settings.VideoPaths.Where(File.Exists).ToList();
                    var options = new List<string> { sharedOption };
                    options.AddRange(videoNames.Select(Path.GetFileName)!);
                    string? currentOverride = WallpaperHostEngine.Instance.GetScreenVideoOverride(targetScreen.DeviceName);
                    string selected = currentOverride != null && File.Exists(currentOverride) ? Path.GetFileName(currentOverride)! : sharedOption;
                    screenCombo.SetItems(options, selected);
                    screenCombo.SelectedIndexChanged += (_, _) =>
                    {
                        var pick = screenCombo.SelectedItem;
                        if (pick == null || pick == sharedOption)
                            WallpaperHostEngine.Instance.SetScreenVideoOverride(targetScreen.DeviceName, null);
                        else
                        {
                            var match = videoNames.FirstOrDefault(v => Path.GetFileName(v) == pick);
                            if (match != null) WallpaperHostEngine.Instance.SetScreenVideoOverride(targetScreen.DeviceName, match);
                        }
                    };
                    row.Controls.Add(screenCombo);
                    panel.Controls.Add(row);
                }
                AddText(panel, LocalizationManager.T("Wallpaper.PerScreenNote"));
            }

            // Theme/Palette και Wave Tuning ισχύουν για Waves ΚΑΙ Particles (μοιράζονται το ίδιο
            // cfg.speed/glowIntensity/palette στο JS engine) — άσχετα μόνο σε Video mode.
            if (settings.Mode != "Video")
            {
            AddSection(panel, LocalizationManager.T("Wallpaper.SectionThemeColors"));
            var themeCombo = new FlatComboBox { Width = 200, Margin = new Padding(0, 0, 0, 8) };
            themeCombo.SetItems(new[] { "Follow", "Light", "Dark" }, settings.ThemeMode);
            panel.Controls.Add(themeCombo);

            var styleCombo = new FlatComboBox { Width = 200, Margin = new Padding(0, 0, 0, 8) };
            styleCombo.SetItems(new[] { "Ribbons", "Aurora" }, settings.WaveStyle);
            styleCombo.SelectedIndexChanged += (_, _) => WallpaperHostEngine.Instance.SetWaveStyle(styleCombo.SelectedItem!);
            panel.Controls.Add(styleCombo);

            var paletteCombo = new FlatComboBox { Width = 200, Margin = new Padding(0, 0, 0, 10) };
            void RepopulatePalettes()
            {
                bool light = themeCombo.SelectedItem switch
                {
                    "Light" => true,
                    "Dark" => false,
                    _ => ThemeService.IsLightTheme(),
                };
                var names = (light ? WavePalettes.Light : WavePalettes.Dark).Select(p => p.Name).ToArray();
                var current = WallpaperSettings.Load().PaletteName;
                paletteCombo.SetItems(names, names.Contains(current) ? current : names.FirstOrDefault());
            }
            RepopulatePalettes();
            themeCombo.SelectedIndexChanged += (_, _) => { WallpaperHostEngine.Instance.SetThemeMode(themeCombo.SelectedItem!); RepopulatePalettes(); };
            paletteCombo.SelectedIndexChanged += (_, _) => { if (paletteCombo.SelectedItem is { } p) WallpaperHostEngine.Instance.SetPalette(p); };
            panel.Controls.Add(paletteCombo);

            AddSection(panel, LocalizationManager.T("Wallpaper.SectionWaveTuning"));
            panel.Controls.Add(BuildWaveSlider(LocalizationManager.T("Wallpaper.Speed"), (int)(settings.WaveSpeed * 10), 3, 20,
                v => WallpaperHostEngine.Instance.SetWaveTuning(v / 10.0, WallpaperSettings.Load().GlowIntensity, WallpaperSettings.Load().LineThickness)));
            panel.Controls.Add(BuildWaveSlider(LocalizationManager.T("Wallpaper.GlowIntensity"), (int)(settings.GlowIntensity * 10), 0, 20,
                v => WallpaperHostEngine.Instance.SetWaveTuning(WallpaperSettings.Load().WaveSpeed, v / 10.0, WallpaperSettings.Load().LineThickness)));
            panel.Controls.Add(BuildWaveSlider(LocalizationManager.T("Wallpaper.LineThickness"), (int)(settings.LineThickness * 10), 5, 20,
                v => WallpaperHostEngine.Instance.SetWaveTuning(WallpaperSettings.Load().WaveSpeed, WallpaperSettings.Load().GlowIntensity, v / 10.0)));
            }

            AddSection(panel, LocalizationManager.T("Wallpaper.SectionPerformance"));
            Dictionary<string, HoverButton>? perfButtons = null;
            perfButtons = AddToggleButtonGrid(panel,
                ("High", (_, _) => { WallpaperHostEngine.Instance.SetPerformanceMode("High"); RefreshStatus(); MarkActive(perfButtons!, "High"); }),
                ("Balanced", (_, _) => { WallpaperHostEngine.Instance.SetPerformanceMode("Balanced"); RefreshStatus(); MarkActive(perfButtons!, "Balanced"); }),
                ("Low Power", (_, _) => { WallpaperHostEngine.Instance.SetPerformanceMode("Low Power"); RefreshStatus(); MarkActive(perfButtons!, "Low Power"); }),
                ("Battery", (_, _) => { WallpaperHostEngine.Instance.SetPerformanceMode("Battery"); RefreshStatus(); MarkActive(perfButtons!, "Battery"); }));
            MarkActive(perfButtons, settings.PerformanceMode);

            SetPage("Page.Wallpaper.Title", panel);
        }

        private static string DescribeWallpaperState(WallpaperSettings s) =>
            $"{(WallpaperHostEngine.Instance.IsEnabled ? "Ενεργό" : "Ανενεργό")}  •  Mode: {s.Mode}  •  Performance: {s.PerformanceMode}";

        private static string DescribePlaylist(WallpaperSettings s)
        {
            var playable = s.VideoPaths.Where(File.Exists).ToList();
            if (playable.Count == 0) return "Κενή — εμφανίζεται το MotionDesk Waves φόντο.";
            return $"{playable.Count} βίντεο{(s.Shuffle ? " (shuffle)" : "")} — τρέχον: {Path.GetFileName(s.CurrentPlaylistFile() ?? "")}";
        }

        private static Panel BuildWaveSlider(string label, int value, int min, int max, Action<int> onChange)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = UiTheme.TextSecondary, Width = 110, Padding = new Padding(0, 6, 8, 0) });
            var track = new TrackBar { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 220, TickStyle = TickStyle.None };
            track.ValueChanged += (_, _) => onChange(track.Value);
            row.Controls.Add(track);
            return row;
        }

        private static void ChooseWallpaperVideoFiles()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Video (*.mp4;*.m4v;*.webm;*.mov;*.ogv;*.ogg;*.avi;*.mkv;*.wmv;*.mpeg;*.mpg;*.m2ts;*.ts)|*.mp4;*.m4v;*.webm;*.mov;*.ogv;*.ogg;*.avi;*.mkv;*.wmv;*.mpeg;*.mpg;*.m2ts;*.ts|All files (*.*)|*.*",
                Title = "Προσθήκη βίντεο στο Wallpaper Library",
                Multiselect = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                var settings = WallpaperSettings.Load();
                settings.AddVideoFiles(dialog.FileNames);
                settings.Mode = "Video";
                settings.Save();
                WallpaperHostEngine.Instance.Enable();
            }
        }

        private static void ChooseWallpaperFolder()
        {
            using var dialog = new FolderBrowserDialog { Description = "Επιλογή φακέλου video library" };
            if (dialog.ShowDialog() == DialogResult.OK)
                WallpaperHostEngine.Instance.AddVideoFolder(dialog.SelectedPath);
        }


        // "Εξατομίκευση" — τα ασφαλή (registry/native API μόνο, καμία τροποποίηση αρχείων
        // συστήματος) κομμάτια του Customization Vision: IconAtlas, DeskCursors, DeskStrip,
        // DeskSounds. Όλα τα ονόματα ελέγχθηκαν ρητά για συγκρούσεις επωνυμίας πριν επιλεγούν.
        private void ShowPersonalization()
        {
            _currentPageKey = "Personalization";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Personalization.Intro"));

            // ---------- IconAtlas ----------
            AddSection(panel, LocalizationManager.T("Personalization.SectionIconAtlas"));
            AddText(panel, LocalizationManager.T("Personalization.IconAtlasIntro"));

            Control BuildIconSlotRow(string labelKey, Func<string?> get, Action<string?> set)
            {
                var row = new Panel { Width = 720, Height = 44, Margin = new Padding(0, 0, 0, 8) };
                var preview = new PictureBox { Location = new Point(0, 4), Size = new Size(32, 32), SizeMode = PictureBoxSizeMode.StretchImage, BackColor = Color.Transparent };

                void RefreshPreview()
                {
                    preview.Image?.Dispose();
                    preview.Image = null;
                    var p = get();
                    if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    {
                        try { using var ic = Icon.ExtractAssociatedIcon(p); if (ic != null) preview.Image = ic.ToBitmap(); }
                        catch (Exception) { }
                    }
                }
                RefreshPreview();
                row.Controls.Add(preview);

                row.Controls.Add(new Label { Text = LocalizationManager.T(labelKey), ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, AutoSize = true, Location = new Point(42, 13) });

                var chooseBtn = new HoverButton { Text = LocalizationManager.T("Personalization.ChooseIcon"), Width = 160, Height = 30, Location = new Point(320, 7), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand };
                chooseBtn.FlatAppearance.BorderSize = 0;
                chooseBtn.Click += (_, _) =>
                {
                    using var dlg = new OpenFileDialog { Filter = "Icon files (*.ico)|*.ico", Title = LocalizationManager.T("Personalization.ChooseIcon") };
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        set(dlg.FileName);
                        RefreshPreview();
                        _statusLabel.Text = "Icon applied";
                    }
                };
                row.Controls.Add(chooseBtn);

                var resetBtn = new HoverButton { Text = LocalizationManager.T("Personalization.Reset"), Width = 90, Height = 30, Location = new Point(490, 7), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextSecondary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                resetBtn.FlatAppearance.BorderSize = 0;
                resetBtn.Click += (_, _) => { set(null); RefreshPreview(); _statusLabel.Text = "Icon reset"; };
                row.Controls.Add(resetBtn);

                return row;
            }

            panel.Controls.Add(BuildIconSlotRow("Personalization.ThisPcIcon", IconAtlasEngine.GetThisPcIcon, IconAtlasEngine.SetThisPcIcon));
            panel.Controls.Add(BuildIconSlotRow("Personalization.RecycleBinEmpty", IconAtlasEngine.GetRecycleBinEmptyIcon, IconAtlasEngine.SetRecycleBinEmptyIcon));
            panel.Controls.Add(BuildIconSlotRow("Personalization.RecycleBinFull", IconAtlasEngine.GetRecycleBinFullIcon, IconAtlasEngine.SetRecycleBinFullIcon));

            AddButtonGrid(panel, (LocalizationManager.T("Personalization.FolderIcon"), (_, _) =>
            {
                using var folderDlg = new FolderBrowserDialog { Description = LocalizationManager.T("Personalization.FolderIcon") };
                if (folderDlg.ShowDialog() != DialogResult.OK) return;
                using var iconDlg = new OpenFileDialog { Filter = "Icon files (*.ico)|*.ico", Title = LocalizationManager.T("Personalization.ChooseIcon") };
                if (iconDlg.ShowDialog(this) == DialogResult.OK)
                {
                    IconAtlasEngine.SetFolderIcon(folderDlg.SelectedPath, iconDlg.FileName);
                    _statusLabel.Text = "Folder icon applied";
                }
            }));

            // ---------- DeskCursors ----------
            AddSection(panel, LocalizationManager.T("Personalization.SectionDeskCursors"));
            AddText(panel, LocalizationManager.T("Personalization.DeskCursorsIntro"));

            foreach (var (registryName, labelKey) in DeskCursorsEngine.Roles)
            {
                var row = new Panel { Width = 720, Height = 36, Margin = new Padding(0, 0, 0, 6) };
                row.Controls.Add(new Label { Text = LocalizationManager.T(labelKey), ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(0, 6), Size = new Size(185, 24) });
                var pathLabel = new Label { Text = Path.GetFileName(DeskCursorsEngine.GetCursor(registryName) ?? "") is { Length: > 0 } fn ? fn : LocalizationManager.T("Personalization.NotSet"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(190, 6), Size = new Size(230, 24) };
                row.Controls.Add(pathLabel);

                var browseBtn = new HoverButton { Text = LocalizationManager.T("Personalization.Browse"), Width = 110, Height = 28, Location = new Point(430, 4), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand };
                browseBtn.FlatAppearance.BorderSize = 0;
                string capturedRole = registryName;
                browseBtn.Click += (_, _) =>
                {
                    using var dlg = new OpenFileDialog { Filter = "Cursor files (*.cur;*.ani)|*.cur;*.ani", Title = LocalizationManager.T("Personalization.Browse") };
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        DeskCursorsEngine.SetCursor(capturedRole, dlg.FileName);
                        pathLabel.Text = Path.GetFileName(dlg.FileName);
                    }
                };
                row.Controls.Add(browseBtn);
                panel.Controls.Add(row);
            }

            AddButtonGrid(panel,
                (LocalizationManager.T("Personalization.ApplyCursors"), (_, _) => { DeskCursorsEngine.ApplyNow(); _statusLabel.Text = "Cursors applied"; }),
                (LocalizationManager.T("Personalization.ResetCursors"), (_, _) => { DeskCursorsEngine.ResetAllToWindowsDefault(); if (_pageBuilders.TryGetValue("Personalization", out var rebuild)) rebuild(); }));

            // ---------- DeskStrip ----------
            AddSection(panel, LocalizationManager.T("Personalization.SectionDeskStrip"));
            AddText(panel, LocalizationManager.T("Personalization.DeskStripIntro"));

            var deskStripToggle = new CheckBox { Text = LocalizationManager.T("Personalization.EnableDeskStrip"), AutoSize = true, Checked = DeskStripHostEngine.Instance.IsEnabled, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, Margin = new Padding(0, 4, 0, 10) };
            panel.Controls.Add(deskStripToggle);

            var pinnedList = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            panel.Controls.Add(pinnedList);

            void RefreshPinnedApps()
            {
                // Bug fix: αυτή η συνάρτηση καλείται κάθε 2 δευτερόλεπτα από timer (βλ.
                // deskStripRefreshTimer παρακάτω) ενώ ο χρήστης μπορεί να έχει ήδη κυλήσει
                // ΠΟΛΥ πιο κάτω στη σελίδα (π.χ. στο "Φόντο οθόνης κλειδώματος"). Το
                // Controls.Clear()+rebuild ενός FlowLayoutPanel μέσα σε ένα AutoScroll container
                // επαναφέρει σιωπηλά τη θέση κύλισης — αναφέρθηκε ως "autoscrolling πίσω στα
                // DeskSounds". Λύση: αποθήκευση/επαναφορά της θέσης κύλισης της ΣΕΛΙΔΑΣ γύρω από
                // την ανακατασκευή (το AutoScrollPosition GET επιστρέφει αρνητικές τιμές, το SET
                // περιμένει θετικές — WinForms ιδιαιτερότητα).
                var savedScroll = panel.AutoScrollPosition;
                pinnedList.SuspendLayout();
                pinnedList.Controls.Clear();
                var pinned = DeskStripHostEngine.Instance.GetPinnedApps();
                if (pinned.Count == 0)
                {
                    pinnedList.Controls.Add(new Label { AutoSize = true, Text = LocalizationManager.T("Common.NoPinnedApps"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody });
                }
                foreach (var appPath in pinned)
                {
                    var row = new Panel { Width = 500, Height = 40, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 6) };
                    UiTheme.ApplyRoundedRegion(row, 6);
                    row.Controls.Add(new Label { Text = Path.GetFileNameWithoutExtension(appPath), ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, Location = new Point(14, 11), AutoSize = true });
                    var removeBtn = new HoverButton { Text = "✕", Width = 36, Height = 28, Location = new Point(444, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    removeBtn.FlatAppearance.BorderSize = 0;
                    string capturedPath = appPath;
                    removeBtn.Click += (_, _) => { DeskStripHostEngine.Instance.UnpinApp(capturedPath); RefreshPinnedApps(); };
                    row.Controls.Add(removeBtn);
                    pinnedList.Controls.Add(row);
                }
                pinnedList.ResumeLayout();
                panel.AutoScrollPosition = new Point(-savedScroll.X, -savedScroll.Y);
            }

            deskStripToggle.CheckedChanged += (_, _) =>
            {
                if (deskStripToggle.Checked) DeskStripHostEngine.Instance.Enable(); else DeskStripHostEngine.Instance.Disable();
                RefreshPinnedApps();
            };

            AddButtonGrid(panel, (LocalizationManager.T("Personalization.PinApp"), (_, _) =>
            {
                using var dlg = new OpenFileDialog { Filter = "Applications (*.exe;*.lnk)|*.exe;*.lnk", Title = LocalizationManager.T("Personalization.PinApp") };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    DeskStripHostEngine.Instance.PinApp(dlg.FileName);
                    deskStripToggle.Checked = true;
                    RefreshPinnedApps();
                }
            }));

            RefreshPinnedApps();
            var deskStripRefreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            deskStripRefreshTimer.Tick += (_, _) => RefreshPinnedApps();
            deskStripRefreshTimer.Start();
            panel.Disposed += (_, _) => deskStripRefreshTimer.Dispose();

            // ---------- DeskSounds ----------
            AddSection(panel, LocalizationManager.T("Personalization.SectionDeskSounds"));
            AddText(panel, LocalizationManager.T("Personalization.DeskSoundsIntro"));

            foreach (var (eventLabel, labelKey) in DeskSoundsEngine.Events)
            {
                // Bug fix: τα κουμπιά ήταν πολύ στενά/πολύ αριστερά για το πραγματικό μήκος των
                // ελληνικών ετικετών (π.χ. "Προεπισκόπηση ▶") — φαρδύτερα κουμπιά + μετατόπιση
                // δεξιά ώστε να χωράει όλο το κείμενο χωρίς να κόβεται.
                var row = new Panel { Width = 760, Height = 36, Margin = new Padding(0, 0, 0, 6) };
                row.Controls.Add(new Label { Text = LocalizationManager.T(labelKey), ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(0, 6), Size = new Size(150, 24) });
                var pathLabel = new Label { Text = Path.GetFileName(DeskSoundsEngine.GetSound(eventLabel) ?? "") is { Length: > 0 } sfn ? sfn : LocalizationManager.T("Personalization.NotSet"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(156, 6), Size = new Size(170, 24) };
                row.Controls.Add(pathLabel);

                string capturedEvent = eventLabel;

                var browseBtn = new HoverButton { Text = LocalizationManager.T("Personalization.Browse"), Width = 116, Height = 28, Location = new Point(336, 4), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand };
                browseBtn.FlatAppearance.BorderSize = 0;
                browseBtn.Click += (_, _) =>
                {
                    using var dlg = new OpenFileDialog { Filter = "WAV files (*.wav)|*.wav", Title = LocalizationManager.T("Personalization.Browse") };
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        DeskSoundsEngine.SetSound(capturedEvent, dlg.FileName);
                        pathLabel.Text = Path.GetFileName(dlg.FileName);
                    }
                };
                row.Controls.Add(browseBtn);

                var previewBtn = new HoverButton { Text = LocalizationManager.T("Personalization.Preview"), Width = 150, Height = 28, Location = new Point(460, 4), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                previewBtn.FlatAppearance.BorderSize = 0;
                previewBtn.Click += (_, _) => { var s = DeskSoundsEngine.GetSound(capturedEvent); if (!string.IsNullOrEmpty(s) && File.Exists(s)) DeskSoundsEngine.Preview(s); };
                row.Controls.Add(previewBtn);

                var clearBtn = new HoverButton { Text = LocalizationManager.T("Personalization.Clear"), Width = 110, Height = 28, Location = new Point(618, 4), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextSecondary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                clearBtn.FlatAppearance.BorderSize = 0;
                clearBtn.Click += (_, _) => { DeskSoundsEngine.SetSound(capturedEvent, null); pathLabel.Text = LocalizationManager.T("Personalization.NotSet"); };
                row.Controls.Add(clearBtn);

                panel.Controls.Add(row);
            }

            // ---------- .theme Packages ----------
            AddSection(panel, LocalizationManager.T("Personalization.SectionThemePackages"));
            AddText(panel, LocalizationManager.T("Personalization.ThemePackagesIntro"));

            var themeList = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            panel.Controls.Add(themeList);

            void RefreshThemeList()
            {
                themeList.SuspendLayout();
                themeList.Controls.Clear();
                var themes = ThemePackageEngine.ListAvailableThemes();
                if (themes.Count == 0)
                {
                    themeList.Controls.Add(new Label { AutoSize = true, Text = LocalizationManager.T("Personalization.NoThemesFound"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody });
                }
                foreach (var theme in themes)
                {
                    var row = new Panel { Width = 500, Height = 40, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 6) };
                    UiTheme.ApplyRoundedRegion(row, 6);
                    string label = theme.Name + (theme.BuiltIn ? "  ·  Windows" : "");
                    row.Controls.Add(new Label { Text = label, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, Location = new Point(14, 11), AutoSize = true });
                    var applyBtn = new HoverButton { Text = LocalizationManager.T("Personalization.ApplyThemeBtn"), Width = 90, Height = 28, Location = new Point(394, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    applyBtn.FlatAppearance.BorderSize = 0;
                    string capturedPath = theme.Path;
                    applyBtn.Click += (_, _) => { ThemePackageEngine.ApplyTheme(capturedPath); _statusLabel.Text = "Theme applied"; };
                    row.Controls.Add(applyBtn);
                    themeList.Controls.Add(row);
                }
                themeList.ResumeLayout();
            }
            RefreshThemeList();

            AddButtonGrid(panel, (LocalizationManager.T("Personalization.SaveCurrentTheme"), (_, _) =>
            {
                using var dialog = new Form { Text = LocalizationManager.T("Personalization.ThemeNamePrompt"), StartPosition = FormStartPosition.CenterParent, Size = new Size(420, 150), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
                var box = new TextBox { Text = "MotionDesk Custom", Dock = DockStyle.Top, Margin = new Padding(12) };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 34 };
                dialog.Controls.Add(box);
                dialog.Controls.Add(ok);
                dialog.AcceptButton = ok;
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(box.Text))
                {
                    ThemePackageEngine.SaveCurrentAsTheme(box.Text.Trim());
                    RefreshThemeList();
                    _statusLabel.Text = "Theme saved";
                }
            }));

            // ---------- Lock Screen Background ----------
            AddSection(panel, LocalizationManager.T("Personalization.SectionLockScreen"));
            AddText(panel, LocalizationManager.T("Personalization.LockScreenIntro"));
            AddText(panel, string.Format(LocalizationManager.T("Personalization.LockScreenEditionNote"), LockScreenEngine.GetWindowsEdition()));

            AddButtonGrid(panel, (LocalizationManager.T("Personalization.SetLockScreenImage"), (_, _) =>
            {
                using var dlg = new OpenFileDialog { Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp", Title = LocalizationManager.T("Personalization.SetLockScreenImage") };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                bool ok = LockScreenEngine.TrySetLockScreenImageElevated(dlg.FileName, out var error);
                if (ok) _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenSuccess");
                else if (error == "cancelled") _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenCancelled");
                else _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenFailed");
            }));

            SetPage("Page.Personalization.Title", panel);
        }

        private void ShowSettings()
        {
            _currentPageKey = "Settings";
            var settings = new SettingsView(() => NavigateTo("Wallpaper"));
            SetPage("Page.Settings.Title", settings);
        }

        public void OpenCommandPalette() => ShowCommandPalette();

        private void ShowCommandPalette()
        {
            using var dialog = new Form
            {
                Text = "MotionDesk Command Palette",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(560, 430),
                BackColor = UiTheme.Background,
                ForeColor = UiTheme.TextPrimary,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            dialog.HandleCreated += (_, _) => { int d = UiTheme.Background.GetBrightness() < 0.5f ? 1 : 0; try { DwmSetWindowAttribute(dialog.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int)); } catch (DllNotFoundException) { } };

            var box = new TextBox
            {
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 14),
                PlaceholderText = "Πληκτρολόγησε μια εντολή…",
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };
            var list = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 11),
                BackColor = UiTheme.Background,
                ForeColor = UiTheme.TextPrimary,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 30
            };
            list.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using var bg = new SolidBrush(selected ? UiTheme.SurfaceHover : UiTheme.Background);
                e.Graphics.FillRectangle(bg, e.Bounds);
                using var textBrush = new SolidBrush(selected ? UiTheme.AccentCyan : UiTheme.TextPrimary);
                e.Graphics.DrawString(list.Items[e.Index].ToString(), e.Font!, textBrush, e.Bounds.X + 10, e.Bounds.Y + 5);
            };

            var commands = new[] { "Dashboard", "Widget Gallery", "DeskZones", "Wallpaper Studio", "Profiles", "Performance", "Automation", "Personalization", "Settings", "Enable Wallpaper", "Disable Wallpaper", "Work Profile", "Gaming Profile", "Focus Profile" };
            list.Items.AddRange(commands);
            if (list.Items.Count > 0) list.SelectedIndex = 0;

            void Execute()
            {
                if (list.SelectedItem is not string c) return;
                switch (c) {
                    case "Dashboard": NavigateTo("Dashboard"); break; case "Widget Gallery": NavigateTo("Widgets"); break; case "DeskZones": NavigateTo("DeskZones"); break;
                    case "Wallpaper Studio": NavigateTo("Wallpaper"); break; case "Profiles": NavigateTo("Profiles"); break;
                    case "Performance": NavigateTo("Performance"); break; case "Automation": NavigateTo("Automation"); break;
                    case "Personalization": NavigateTo("Personalization"); break; case "Settings": NavigateTo("Settings"); break;
                    case "Enable Wallpaper": WallpaperHostEngine.Instance.Enable(); break;
                    case "Disable Wallpaper": WallpaperHostEngine.Instance.Disable(); break; case "Work Profile": LoadProfileFromQuickButton("Work"); break;
                    case "Gaming Profile": LoadProfileFromQuickButton("Gaming"); break; case "Focus Profile": LoadProfileFromQuickButton("Focus"); break;
                }
                dialog.Close();
            }
            box.TextChanged += (_, _) =>
            {
                list.Items.Clear();
                list.Items.AddRange(commands.Where(c => c.Contains(box.Text, StringComparison.OrdinalIgnoreCase)).ToArray());
                if (list.Items.Count > 0) list.SelectedIndex = 0;
            };
            list.DoubleClick += (_, _) => Execute();
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter) { Execute(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down && list.Items.Count > 0) { list.SelectedIndex = Math.Min(list.SelectedIndex + 1, list.Items.Count - 1); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up && list.Items.Count > 0) { list.SelectedIndex = Math.Max(list.SelectedIndex - 1, 0); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) dialog.Close();
            };
            dialog.Controls.Add(list); dialog.Controls.Add(box); box.Focus(); dialog.ShowDialog(this);
        }

        private void ShowProfiles()
        {
            _currentPageKey = "Profiles";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Profiles.Intro"));
            var list = new ListBox { Width = 440, Height = 180, Font = new Font("Segoe UI", 10) };
            void Refresh() { list.Items.Clear(); foreach (var p in WorkspaceProfileService.ListProfiles()) list.Items.Add(p); }
            Refresh(); panel.Controls.Add(list);
            var name = new TextBox { Width = 240, Text = "Work" }; panel.Controls.Add(name);
            AddIconButtonGrid(panel,
                ("Save", "Save current as profile", (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) { WorkspaceProfileService.Save(name.Text.Trim()); Refresh(); } }),
                ("Restore", "Load selected", (_, _) => { if (list.SelectedItem is string p && WorkspaceProfileService.Load(p)) { NavigateTo("Dashboard"); } }),
                ("Add", "Create / update Work, Gaming, Focus", (_, _) => { foreach (var p in new[] { "Work", "Gaming", "Focus" }) WorkspaceProfileService.Save(p); Refresh(); }),
                ("Delete", "Delete selected profile", (_, _) => { if (list.SelectedItem is string p && !p.Equals("Last Session", StringComparison.OrdinalIgnoreCase)) { WorkspaceProfileService.Delete(p); Refresh(); } }));
            SetPage("Page.Profiles.Title", panel);
        }

        // Ενοποιημένη καρτέλα: πρώην ξεχωριστά "System Monitor" + "Performance Center" (το
        // δεύτερο ήταν σχεδόν πανομοιότυπο με το πρώτο) -> τώρα ΜΙΑ καρτέλα, με το ίδιο live
        // hardware περιεχόμενο, κάτω από τον τίτλο/εικονίδιο "Απόδοση".
        private void ShowPerformance()
        {
            _currentPageKey = "Performance";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Performance.Intro"));
            var cpu = AddMeter(panel, "CPU", 0);
            var ram = AddMeter(panel, "Memory", 0);
            var net = new SparklineCard("NETWORK", 2, 1024) { Width = 500, Height = 110, Margin = new Padding(0, 0, 0, 10) };
            panel.Controls.Add(net);
            var procs = new SparklineCard("PROCESSES", 1, 400) { Width = 500, Height = 110, Margin = new Padding(0, 0, 0, 10) };
            panel.Controls.Add(procs);
            var powerCard = AddCard(panel, "Power", "");

            var timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += (_, _) => {
                var m = AdvancedSystemMonitorService.Instance.GetSnapshot();
                cpu.SetValue(m.CpuPercent, $"{m.CpuPercent:0.0}%");
                double ramPercent = m.TotalMemoryMb > 0 ? (m.TotalMemoryMb - m.AvailableMemoryMb) / m.TotalMemoryMb * 100.0 : 0;
                ram.SetValue(ramPercent, $"{m.AvailableMemoryMb:0} MB free / {m.TotalMemoryMb:0} MB");
                net.Push(m.NetworkDownKbps, m.NetworkUpKbps, $"↓ {m.NetworkDownKbps:0.0} KB/s   ↑ {m.NetworkUpKbps:0.0} KB/s");
                procs.Push(m.ProcessCount, null, $"{m.ProcessCount} processes");
                powerCard.Text = $"Recommended: {m.BatteryMode}";
            };
            timer.Start();
            panel.Disposed += (_, _) => timer.Dispose();
            SetPage("Page.Performance.Title", panel);
        }

        private void ShowAutomation()
        {
            _currentPageKey = "Automation";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Automation.Intro"));
            foreach (var rule in AutomationService.Instance.Rules)
            {
                var row = new CheckBox { Text = $"{rule.Name}  ·  {rule.TriggerProcess}  →  {rule.Profile}", Checked = rule.Enabled, AutoSize = true, ForeColor = UiTheme.TextPrimary };
                row.CheckedChanged += (_, _) => { rule.Enabled = row.Checked; AutomationService.Instance.Save(); };
                panel.Controls.Add(row);
            }
            AddButton(panel, "Add Gaming rule", (_, _) => { AutomationService.Instance.Rules.Add(new AutomationRule { Name = "Gaming Mode", TriggerProcess = "steam.exe", Profile = "Gaming" }); AutomationService.Instance.Save(); ShowAutomation(); });
            SetPage("Page.Automation.Title", panel);
        }

        private bool _gamingModeActive;
        private void ToggleGamingMode() => LoadProfileFromQuickButton(_gamingModeActive ? "Work" : "Gaming", true);
        private void LoadProfileFromQuickButton(string profile, bool toggle = false)
        {
            if (WorkspaceProfileService.Load(profile)) { if (toggle) _gamingModeActive = !_gamingModeActive; _statusLabel.Text = $"Profile loaded: {profile}"; }
            else _statusLabel.Text = $"Profile not found: {profile}";
        }

        // Ιστορικό εκδόσεων — απλή, in-code λίστα· ανοίγει σε ξεχωριστό (δευτερεύον) παράθυρο.
        private static readonly (string Version, string Date, string Notes)[] VersionHistory =
        {
            ("1.2.9", "2026-09", "Κρίσιμη διόρθωση του κινούμενου wallpaper (Waves/Particles/Video), δανεισμένη από τον τρόπο που το κάνει το Lively Wallpaper: το WinForms παράθυρο του wallpaper δημιουργείται πάντα με native style WS_POPUP, ακόμη κι όταν είναι FormBorderStyle.None. Το SetParent προς το WorkerW/Progman άλλαζε μόνο τον λογικό parent — ΔΕΝ μετέτρεπε αυτόματα το WS_POPUP σε WS_CHILD, με αποτέλεσμα σε πολλά builds Windows 10/11 το παράθυρο να μην συμμετέχει σωστά στο compositing/z-order του νέου parent (να παραμένει αόρατο ή να συμπεριφέρεται σαν προστασία οθόνης πάνω από την επιφάνεια εργασίας), ακόμη κι όταν το ίδιο το SetParent \"πετύχαινε\" τυπικά. Προστέθηκε ρητή μετατροπή WS_POPUP→WS_CHILD (SetWindowLongPtr + SWP_FRAMECHANGED) πριν από κάθε SetParent. Επιβεβαιώθηκε ζωντανά ότι το παράθυρο πλέον γίνεται πραγματικό child του Progman στο σωστό μέγεθος οθόνης."),
            ("1.2.8", "2026-09", "UI polish πέρασμα: (1) Τα ελληνικά κεφαλαία σε τίτλους ενοτήτων/καρτών δεν έχουν πλέον τόνους (σωστή ορθογραφική σύμβαση — π.χ. \"ΕΞΑΤΟΜΙΚΕΥΣΗ\" όχι \"ΕΞΑΤΟΜΙΚΕΥΣΉ\"). (2) Όλα τα HoverButton της εφαρμογής έγιναν πλήρως στρογγυλεμένα (\"pills\") καθολικά, με τον ίδιο μηχανισμό (Selectable=false) που ήδη διόρθωσε το ορατό focus-rectangle bug στο PillButton του DeskZones editor. (3) Τα κουμπιά DeskSounds (Αναζήτηση/Προεπισκόπηση/Καθαρισμός) έκοβαν κείμενο — φαρδύτερα + μετατοπισμένα δεξιά. (4) Bug fix: όσο ο χρήστης ήταν στο \"Φόντο οθόνης κλειδώματος\", η σελίδα \"autoscroll-άριζε\" πίσω στα DeskSounds κάθε 2 δευτερόλεπτα — αιτία ήταν ο περιοδικός timer του DeskStrip που ανακατασκεύαζε τη δική του λίστα μέσα σε ΑΥΤΟ-scroll container, επαναφέροντας σιωπηλά τη θέση κύλισης της ΣΕΛΙΔΑΣ. Διορθώθηκε με αποθήκευση/επαναφορά της θέσης κύλισης γύρω από κάθε ανανέωση."),
            ("1.2.7", "2026-09", "Τα DeskZones έγιναν ΠΡΑΓΜΑΤΙΚΑ FancyZones-style (μετά από screenshots του πραγματικού PowerToys Editor) — όχι πια μόνιμα ορατά bordered παράθυρα στην επιφάνεια εργασίας. Κράτα Shift ενώ σέρνεις ένα παράθυρο για να δεις τις ζώνες και να κουμπώσεις (χωρίς Shift, το σύρσιμο είναι απολύτως κανονικό). Νέος επεξεργαστής διάταξης (Ctrl+Shift+N) με 7 templates (No layout/Focus/Columns/Rows/Grid/Priority Grid/Custom) — ρυθμιζόμενο πλήθος στηλών/σειρών ΜΟΝΟ στο Custom, τα presets κρατούν σταθερό σχήμα. ΣΟΒΑΡΟ bug βρέθηκε και διορθώθηκε πριν προλάβει να κυκλοφορήσει: ένα self-referential Click handler (this.Click καλούσε το OnClick() που ακριβώς πυροδοτεί το ίδιο το Click) προκαλούσε άπειρη αναδρομή σε κάθε κλικ πάνω σε template card — StackOverflowException, μη-πιάσιμο από το .NET, τερμάτιζε αμέσως όλη την εφαρμογή χωρίς κανένα exception log. Εντοπίστηκε με προσωρινό debug logging που αποκάλυψε ότι ο handler δεν πρόλαβε καν να τρέξει μία φορά. Επίσης διορθώθηκε ξεχωριστό bug στη χαρτογράφηση παραμέτρων του template \"Rows\" (διάβαζε λάθος μεταβλητή για το πλήθος σειρών) και οπτικό bug όπου το keyboard-focus-rectangle ενός στρογγυλεμένου (\"pill\") κουμπιού πρόβαλλε έξω από το στρογγυλεμένο περίγραμμά του."),
            ("1.2.6", "2026-09", "Bugfix πέρασμα κατόπιν αναφορών: (1) Τα DeskZones \"άνοιγαν μόνα τους\" στην εκκίνηση — αιτία ήταν ρυπασμένο saved \"Last Session\" προφίλ από εσωτερικά test runs, καθαρίστηκε· το smoke test script τώρα καθαρίζει μόνο του τα test zones. (2) Αφαιρέθηκε η Fences-style λειτουργία roll-up/hover-collapse από τα DeskZones — η πραγματική τους λειτουργία (snap-on-drop, στυλ FancyZones) παρέμενε ήδη σωστή, το roll-up ήταν το πιο \"Fences\" κομμάτι. (3) Διορθώθηκε ΣΟΒΑΡΟ bug όπου η ενεργοποίηση wallpaper (ειδικά η πρώτη σε φρέσκια συνεδρία) μπλόκαρε συγχρονισμένα το UI thread έως ~8.5 δευτερόλεπτα ανά οθόνη (WorkerW attach retry loop) — \"κολλούσε\" όλη η εφαρμογή. Τρέχει πλέον σε background thread. (4) Οι ενότητες Video Library/Theme&Colors/Wave Tuning στη σελίδα Wallpaper ήταν ΠΑΝΤΑ ορατές ανεξάρτητα από το επιλεγμένο Mode — τώρα εμφανίζονται μόνο όταν είναι σχετικές. (5) Διορθώθηκε bug όπου η εναλλαγή Dark→Light \"δεν αποκρινόταν\" (re-entrancy στο popup του theme dropdown). (6) Διορθώθηκε \"τρεμόπαιγμα\" στο slider σκουρότητας θέματος — ζωντανή προεπισκόπηση χωρίς ανακατασκευή σελίδας, πλήρης εφαρμογή μόνο στην αποδέσμευση του slider. (7) Το ιστορικό εκδόσεων έκοβε κείμενο σε μεγάλες καταχωρήσεις — τα cards παίρνουν πλέον το πραγματικό ύψος που χρειάζονται. (8) Η πιο πρόσφατη έκδοση στο ιστορικό ξεχωρίζει τώρα με χρωματιστό πλαίσιο."),
            ("1.2.5", "2026-09", "Νέο slider \"Σκουρότητα σκούρου θέματος\" στις Ρυθμίσεις (0-100%, 50% = η αρχική βάση, αμετάβλητη) — ζωντανή προεπισκόπηση καθώς σέρνεις, όπως το ήδη υπάρχον Window Opacity. Βρέθηκε και διορθώθηκε bug πριν προλάβει να κυκλοφορήσει: η πρώτη υλοποίηση έκανε όλα τα επίπεδα (φόντο/sidebar/κάρτες/hover/περιγράμματα) να συγκλίνουν σε πανομοιότυπο καθαρό μαύρο στο 100% — τώρα κάθε επίπεδο έχει δικό του ρητό \"floor\" χρώμα, διατηρώντας οπτικό διαχωρισμό σε όλο το εύρος."),
            ("1.2.4", "2026-09", "Ολοκληρώθηκαν τα \"μερικώς εφικτά\" στοιχεία του Customization Vision, στη σελίδα Εξατομίκευση: πακέτα .theme (gallery έτοιμων θεμάτων Windows + αποθήκευση τρέχουσας ρύθμισης ως νέο .theme, μέσω του ίδιου του OS handler) και φόντο οθόνης κλειδώματος (Personalization CSP, με elevation μόνο για τη μία ενέργεια — καμία ολόκληρη elevated εκκίνηση της εφαρμογής). Ο installer μεταγλωττίστηκε για πρώτη φορά ρητά κατόπιν αιτήματος."),
            ("1.2.3", "2026-09", "Νέα σελίδα \"Εξατομίκευση\" (Ctrl+0) με τα ασφαλή (registry/native API μόνο) κομμάτια του Customization Vision — όλα τα ονόματα ελέγχθηκαν για συγκρούσεις επωνυμίας πριν επιλεγούν: IconAtlas (εικονίδιο This PC / Κάδος Ανακύκλωσης άδειος-γεμάτος / ανά φάκελο), DeskCursors (προσαρμοσμένο σχήμα δείκτη ποντικιού), DeskStrip (πλωτό dock καρφιτσωμένων εφαρμογών με live ένδειξη \"τρέχει τώρα\"), DeskSounds (πλήρες σχήμα ήχων συστήματος πέρα από τα stock cues). Ένα visual-style engine και η επαναφορά classic Start Menu/taskbar εξετάστηκαν και αποκλείστηκαν ρητά — και τα δύο θα απαιτούσαν patch προστατευμένων αρχείων συστήματος."),
            ("1.2.2", "2026-09", "Sidebar nav tooltips now always show the keyboard shortcut (e.g. \"Widgets (Ctrl+2)\"), not just in collapsed mode. New Quick Look preview for DeskContainers — press Space or right-click → Quick Look on a file icon to instantly preview images and text files, or browse a folder's contents, without opening any external app."),
            ("1.2.1", "2026-09", "Flip 3D: real keyboard cycling (←/→/Tab/Enter), angled tilted-card layout with the selected window centered. Fixed the sidebar logo showing a stray gray box in compact mode and added a glossy highlight. DeskZones layouts now cover every connected screen, not just the primary one. Wallpaper auto-pauses when another app goes fullscreen. Keyboard shortcuts for every major function (Ctrl+1…9 navigate pages, Ctrl+K palette, Ctrl+B collapse sidebar, Ctrl+E toggle wallpaper, Ctrl+Shift+N new DeskZone — full list in About). Uninstaller now offers to remove saved settings. Installer explicitly declared x64-only."),
            ("1.2.0", "2026-09", "DeskZones reworked as FancyZones-style window snapping (drag any window into a zone to fill it); new DeskContainers feature for grouping files/folders/shortcuts on the desktop. Fixed the wallpaper hiding desktop icons when the WorkerW attach silently failed. Custom rounded dropdowns everywhere (replacing classic ComboBoxes) — and fixed a real crash where opening one closed instantly (context-menu disposal bug affecting every popup menu in the app). Collapsible sidebar (icon-only, animated, square tiles). Window opacity setting. Animated wave header background. Sparkline charts for Network/Processes. Nav reorder, bigger logo/title with version number, active-state highlighting on toggle buttons, more icons app-wide."),
            ("1.1.2", "2026-09", "Fixed a real crash where creating any DeskZone layout threw a NullReferenceException. Vector icons (no font-glyph dependency) across the sidebar and Widget Gallery. Removed duplicate page titles app-wide. Merged Performance/System Monitor into one tab. Widgets and DeskZones now both show visible ✕ close and ☰ settings-menu buttons instead of relying on right-click."),
            ("1.1.1", "2026-09", "Fixed Dashboard sidebar entry being invisible (Home icon added), richer gradient/glow wallpaper wave rendering, Performance page redesigned as a distinct wallpaper-performance control center (no longer duplicates System Monitor's live stats)."),
            ("1.1.0", "2026-09", "Theme system (Light/Dark/Follow Windows), localization (EL/EN), colored System Monitor meters, DeskZones/Profiles two-column layouts with layout icons, visible widget Close/Lock buttons, animated sidebar logo, fixed invisible-text rendering bugs (NavButton, StatCard)."),
            ("1.0.0", "2026-09", "Initial unified build: Dashboard, Widget Gallery, DeskZones, Wallpaper Studio (video playlist + MotionDesk Waves), System Monitor, Profiles, Performance, Automation, Command Palette, tray integration, global hotkeys."),
        };

        private void ShowAbout()
        {
            _currentPageKey = "About";
            var panel = CreatePagePanel();
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            AddText(panel, $"Version {version?.ToString(3) ?? "1.0.0"}   •   .NET 8 Windows desktop workspace manager.");
            AddText(panel, LocalizationManager.T("About.Body"));
            AddSection(panel, LocalizationManager.T("About.SectionCapabilities"));
            AddText(panel, LocalizationManager.T("About.CapabilitiesList"));
            AddText(panel, $"Runtime: .NET 8 / WinForms / win-x64   •   WebView2: lazy-loaded   •   Active widgets: {WidgetHostEngine.Instance.GetActiveWidgets().Count}");
            AddButton(panel, "Version History…", (_, _) => ShowVersionHistory());

            AddSection(panel, LocalizationManager.T("About.SectionShortcuts"));
            AddText(panel, LocalizationManager.T("About.ShortcutsList"));

            SetPage("Page.About.Title", panel);
        }

        private void ShowVersionHistory()
        {
            using var dialog = new Form
            {
                Text = "Version History",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(560, 460),
                BackColor = UiTheme.Background,
                ForeColor = UiTheme.TextPrimary,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            dialog.HandleCreated += (_, _) => { int d = UiTheme.Background.GetBrightness() < 0.5f ? 1 : 0; try { DwmSetWindowAttribute(dialog.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int)); } catch (DllNotFoundException) { } };

            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16), BackColor = UiTheme.Background };
            bool isLatest = true;
            foreach (var entry in VersionHistory)
            {
                // Bug fix: το card είχε ΣΤΑΘΕΡΟ Height=110 ανεξάρτητα από το πόσο κείμενο είχε το
                // κάθε entry — καθώς οι σημειώσεις έκδοσης μεγάλωναν, το κείμενο έκοβε στο κάτω
                // άκρο. Τώρα μετριέται το πραγματικό ύψος που χρειάζεται το κείμενο (στο ίδιο
                // πλάτος 470px που ήδη όριζε το MaximumSize του label) και το card παίρνει ακριβώς
                // όσο ύψος χρειάζεται, όχι ένα άκαμπτο νούμερο.
                var notesFont = UiTheme.FontBody;
                var notesSize = TextRenderer.MeasureText(entry.Notes, notesFont, new Size(470, int.MaxValue), TextFormatFlags.WordBreak);
                int cardHeight = Math.Max(80, 34 + notesSize.Height + 18);

                var card = new Panel { Width = 500, Height = cardHeight, BackColor = UiTheme.Surface, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 10) };
                UiTheme.ApplyRoundedRegion(card, 8);

                // Ζητήθηκε ρητά: η πιο πρόσφατη έκδοση (πάντα το πρώτο entry — νέα προστίθενται
                // στην κορυφή) να ξεχωρίζει οπτικά από τις προηγούμενες με χρωματιστό πλαίσιο.
                if (isLatest)
                {
                    card.Paint += (_, e) =>
                    {
                        using var pen = new Pen(UiTheme.AccentCyan, 2f);
                        e.Graphics.DrawRectangle(pen, 1, 1, card.Width - 3, card.Height - 3);
                    };
                }

                string header = $"v{entry.Version}   •   {entry.Date}" + (isLatest ? "   •   τρέχουσα" : "");
                card.Controls.Add(new Label { Text = header, Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = UiTheme.AccentCyan, AutoSize = true, Location = new Point(14, 10) });
                card.Controls.Add(new Label { Text = entry.Notes, Font = notesFont, ForeColor = UiTheme.TextSecondary, MaximumSize = new Size(470, 0), AutoSize = true, Location = new Point(14, 34) });
                list.Controls.Add(card);
                isLatest = false;
            }
            dialog.Controls.Add(list);
            dialog.ShowDialog(this);
        }

        private FlowLayoutPanel CreatePagePanel() => new()
        {
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(4),
            BackColor = UiTheme.Background
        };

        private static void AddHeading(Control parent, string text)
        {
            parent.Controls.Add(new Label
            {
                AutoSize = true,
                Width = 760,
                Text = text,
                Font = UiTheme.FontHeading,
                ForeColor = UiTheme.TextPrimary,
                Margin = new Padding(0, 0, 0, 4)
            });
        }

        private static void AddText(Control parent, string text)
        {
            parent.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(780, 0),
                Text = text,
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, 0, 0, 14)
            });
        }

        private static void AddSection(Control parent, string text)
        {
            parent.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiTheme.UpperNoAccents(text),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = UiTheme.AccentCyan,
                Margin = new Padding(0, 14, 0, 6)
            });
        }

        private static StatCard AddCard(Control parent, string title, string value, Action? onClick = null)
        {
            var card = new StatCard(title, value) { Width = 760, Height = 80, Margin = new Padding(0, 0, 0, 10) };
            if (onClick != null)
            {
                card.Cursor = Cursors.Hand;
                card.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) onClick(); };
            }
            parent.Controls.Add(card);
            return card;
        }

        private static MeterCard AddMeter(Control parent, string title, double initial)
        {
            var card = new MeterCard(title, initial) { Width = 760, Height = 90, Margin = new Padding(0, 0, 0, 10) };
            parent.Controls.Add(card);
            return card;
        }

        // Πλήρως owner-draw στατιστική κάρτα (τίτλος + τιμή) — τα παιδιά-Label μέσα σε
        // Panel/FlowLayoutPanel εμφάνιζαν σποραδικά αόρατο κείμενο σε αυτό το build/runtime
        // (ίδιας οικογένειας quirk με το NavButton), οπότε ζωγραφίζουμε το κείμενο απευθείας.
        private sealed class StatCard : Panel
        {
            private readonly string _title;
            private string _value;

            public StatCard(string title, string value)
            {
                _title = UiTheme.UpperNoAccents(title);
                _value = value;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            public new string Text
            {
                get => _value;
                set { _value = value; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                using (var bg = new SolidBrush(UiTheme.Surface))
                    g.FillRectangle(bg, ClientRectangle);

                using (var accent = new SolidBrush(UiTheme.AccentCyan))
                    g.FillRectangle(accent, 0, 0, 3, Height);

                using var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                using (var titleBrush = new SolidBrush(UiTheme.TextMuted))
                    g.DrawString(_title, titleFont, titleBrush, 18, 12);

                using var valueFont = new Font("Segoe UI", 13f);
                using (var valueBrush = new SolidBrush(UiTheme.TextPrimary))
                    g.DrawString(_value, valueFont, valueBrush, 18, 36);
            }
        }

        // Κάρτα μέτρησης με δυναμική, χρωματιστή μπάρα (πράσινο → κίτρινο → κόκκινο ανάλογα με %).
        // Πραγματικό sparkline chart (area fill + γραμμή + τελευταίο σημείο τονισμένο) αντί για
        // απλό κείμενο — ζητήθηκε ρητά "όμορφα γραφήματα" για Network/Processes/Power. Στηρίζεται
        // αυτόματα στη μεγαλύτερη τιμή του ιστορικού (min floor) ώστε να μη χρειάζεται fixed cap.
        private sealed class SparklineCard : Panel
        {
            private readonly string _title;
            private readonly double _minScaleFloor;
            private readonly int _maxPoints;
            private readonly Queue<double> _series1 = new();
            private readonly Queue<double>? _series2;
            private readonly bool _hasSeries2;
            private string _valueText = "";

            public SparklineCard(string title, int seriesCount, double minScaleFloor, int maxPoints = 30)
            {
                _title = title;
                _minScaleFloor = minScaleFloor;
                _maxPoints = maxPoints;
                _hasSeries2 = seriesCount >= 2;
                if (_hasSeries2) _series2 = new Queue<double>();
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            public void Push(double value1, double? value2, string valueText)
            {
                _series1.Enqueue(value1);
                while (_series1.Count > _maxPoints) _series1.Dequeue();
                if (_hasSeries2 && value2.HasValue)
                {
                    _series2!.Enqueue(value2.Value);
                    while (_series2.Count > _maxPoints) _series2.Dequeue();
                }
                _valueText = valueText;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bg = new SolidBrush(UiTheme.Surface))
                    g.FillRectangle(bg, ClientRectangle);
                using (var accent = new SolidBrush(UiTheme.AccentCyan))
                    g.FillRectangle(accent, 0, 0, 3, Height);

                using var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                using (var titleBrush = new SolidBrush(UiTheme.TextMuted))
                    g.DrawString(_title, titleFont, titleBrush, 18, 10);
                using var valueFont = new Font("Segoe UI", 11.5f);
                using (var valueBrush = new SolidBrush(UiTheme.TextPrimary))
                    g.DrawString(_valueText, valueFont, valueBrush, 18, 28);

                if (_series1.Count < 2) return;
                int chartX = 18, chartY = 54, chartW = Math.Max(10, Width - 36), chartH = Math.Max(10, Height - 66);

                using (var gridPen = new Pen(UiTheme.Border))
                    for (int i = 0; i <= 2; i++)
                    {
                        int y = chartY + chartH * i / 2;
                        g.DrawLine(gridPen, chartX, y, chartX + chartW, y);
                    }

                double scaleMax = Math.Max(_minScaleFloor, Math.Max(MaxOf(_series1), _hasSeries2 ? MaxOf(_series2!) : 0));
                DrawSeries(g, _series1, chartX, chartY, chartW, chartH, scaleMax, UiTheme.AccentCyan, true);
                if (_hasSeries2) DrawSeries(g, _series2!, chartX, chartY, chartW, chartH, scaleMax, UiTheme.AccentBlue, false);
            }

            private static double MaxOf(Queue<double> q) { double m = 0; foreach (var v in q) if (v > m) m = v; return m; }

            private void DrawSeries(Graphics g, Queue<double> series, int chartX, int chartY, int chartW, int chartH, double scaleMax, Color color, bool fillArea)
            {
                var values = series.ToArray();
                var pts = new PointF[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    float x = chartX + chartW * i / (float)Math.Max(1, _maxPoints - 1);
                    double v = scaleMax > 0 ? Math.Clamp(values[i] / scaleMax, 0, 1) : 0;
                    pts[i] = new PointF(x, chartY + chartH - (float)(v * chartH));
                }
                if (pts.Length < 2) return;

                if (fillArea)
                {
                    var areaPts = new PointF[pts.Length + 2];
                    Array.Copy(pts, areaPts, pts.Length);
                    areaPts[pts.Length] = new PointF(pts[^1].X, chartY + chartH);
                    areaPts[pts.Length + 1] = new PointF(pts[0].X, chartY + chartH);
                    using var areaBrush = new LinearGradientBrush(new RectangleF(chartX, chartY, chartW, chartH), Color.FromArgb(70, color), Color.FromArgb(0, color), 90f);
                    g.FillPolygon(areaBrush, areaPts);
                }

                using (var linePen = new Pen(color, 1.8f) { LineJoin = LineJoin.Round })
                    g.DrawLines(linePen, pts);

                var last = pts[^1];
                using var dotBrush = new SolidBrush(color);
                g.FillEllipse(dotBrush, last.X - 3, last.Y - 3, 6, 6);
            }
        }

        private sealed class MeterCard : Panel
        {
            private readonly string _title;
            private double _percent;
            private string _valueText;

            public MeterCard(string title, double percent)
            {
                _title = UiTheme.UpperNoAccents(title);
                _percent = percent;
                _valueText = "";
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            public void SetValue(double percent, string valueText)
            {
                _percent = Math.Clamp(percent, 0, 100);
                _valueText = valueText;
                Invalidate();
            }

            private static Color BarColor(double percent)
            {
                if (percent < 50) return Color.FromArgb(46, 204, 113);   // green
                if (percent < 80) return Color.FromArgb(241, 196, 15);   // yellow
                return Color.FromArgb(231, 76, 60);                      // red
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                using (var bg = new SolidBrush(UiTheme.Surface))
                    g.FillRectangle(bg, ClientRectangle);
                using (var accent = new SolidBrush(UiTheme.AccentCyan))
                    g.FillRectangle(accent, 0, 0, 3, Height);

                using var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                using (var titleBrush = new SolidBrush(UiTheme.TextMuted))
                    g.DrawString(_title, titleFont, titleBrush, 18, 12);

                using var valueFont = new Font("Segoe UI", 13f);
                using (var valueBrush = new SolidBrush(UiTheme.TextPrimary))
                    g.DrawString(_valueText, valueFont, valueBrush, 18, 34);

                var barRect = new Rectangle(18, Height - 22, Width - 36, 8);
                using (var track = new SolidBrush(UiTheme.SurfaceHover))
                using (var trackPath = UiTheme.RoundedPath(barRect, 4))
                    g.FillPath(track, trackPath);

                int fillWidth = (int)(barRect.Width * (_percent / 100.0));
                if (fillWidth > 4)
                {
                    var fillRect = new Rectangle(barRect.X, barRect.Y, fillWidth, barRect.Height);
                    using var fillBrush = new SolidBrush(BarColor(_percent));
                    using var fillPath = UiTheme.RoundedPath(fillRect, 4);
                    g.FillPath(fillBrush, fillPath);
                }
            }
        }

        // Δύο-στηλη σειρά κουμπιών (π.χ. Quick Launch / Workspace) για εξοικονόμηση κάθετου χώρου.
        private static void AddButtonGrid(Control parent, params (string Text, EventHandler Action)[] items)
        {
            var grid = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0)
            };
            foreach (var (text, action) in items)
            {
                var button = new HoverButton
                {
                    Text = text,
                    Width = 372,
                    Height = 40,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = UiTheme.TextPrimary,
                    BackColor = UiTheme.Surface,
                    Margin = new Padding(0, 3, 8, 3),
                    TabStop = false,
                    Cursor = Cursors.Hand
                };
                button.FlatAppearance.BorderSize = 0;
                UiTheme.ApplyRoundedRegion(button, 8);
                button.Click += (sender, e) => action(sender, e);
                grid.Controls.Add(button);
            }
            parent.Controls.Add(grid);
        }

        // Ίδιο με το AddButtonGrid, αλλά επιστρέφει τα ίδια τα κουμπιά ώστε ο caller να μπορεί να
        // "σημαδέψει" ποιο/ποια είναι αυτή τη στιγμή ενεργά (π.χ. το τρέχον performance mode) —
        // ζητήθηκε ρητά: ενεργές επιλογές πρέπει να έχουν αντίστοιχη οπτική σήμανση στο κουμπί.
        private static Dictionary<string, HoverButton> AddToggleButtonGrid(Control parent, params (string Text, EventHandler Action)[] items)
        {
            var map = new Dictionary<string, HoverButton>();
            var grid = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0) };
            foreach (var (text, action) in items)
            {
                var button = new HoverButton
                {
                    Text = text,
                    Width = 180,
                    Height = 38,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = UiTheme.TextPrimary,
                    BackColor = UiTheme.Surface,
                    Margin = new Padding(0, 3, 8, 3),
                    TabStop = false,
                    Cursor = Cursors.Hand
                };
                button.FlatAppearance.BorderSize = 0;
                UiTheme.ApplyRoundedRegion(button, 8);
                button.Click += (sender, e) => action(sender, e);
                grid.Controls.Add(button);
                map[text] = button;
            }
            parent.Controls.Add(grid);
            return map;
        }

        // Εφαρμόζει οπτική σήμανση (accent χρώμα + ✓) στο ενεργό κουμπί ενός AddToggleButtonGrid.
        private static void MarkActive(Dictionary<string, HoverButton> buttons, string? activeKey)
        {
            foreach (var (key, btn) in buttons)
            {
                bool active = key == activeKey;
                btn.BackColor = active ? UiTheme.AccentSoft : UiTheme.Surface;
                btn.ForeColor = active ? UiTheme.AccentCyan : UiTheme.TextPrimary;
                btn.FlatAppearance.BorderSize = active ? 1 : 0;
                btn.FlatAppearance.BorderColor = UiTheme.AccentCyan;
                btn.Text = active ? "✓  " + key : key;
            }
        }

        // Ίδια διάταξη δύο-στηλών με το AddButtonGrid, αλλά με διανυσματικό εικονίδιο (IconRenderer)
        // αντί για μόνο κείμενο — π.χ. τα κουμπιά "+ System Monitor" της Widget Gallery δεν είχαν
        // κανένα πραγματικό εικονίδιο, μόνο τον χαρακτήρα "＋".
        private static void AddIconButtonGrid(Control parent, params (string IconKey, string Text, EventHandler Action)[] items)
        {
            var grid = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0)
            };
            foreach (var (iconKey, text, action) in items)
            {
                var button = new IconButton(iconKey, text) { Width = 372, Height = 44, Margin = new Padding(0, 3, 8, 3) };
                button.Click += (_, _) => action(button, EventArgs.Empty);
                grid.Controls.Add(button);
            }
            parent.Controls.Add(grid);
        }

        private sealed class IconButton : Panel
        {
            private readonly string _iconKey;
            private bool _hover;
            public new event EventHandler? Click;

            public IconButton(string iconKey, string text)
            {
                _iconKey = iconKey;
                Text = text;
                Cursor = Cursors.Hand;
                TabStop = false;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
                MouseEnter += (_, _) => { _hover = true; Invalidate(); };
                MouseLeave += (_, _) => { _hover = false; Invalidate(); };
                MouseUp += (_, e) => { if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) Click?.Invoke(this, EventArgs.Empty); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bgBrush = new SolidBrush(_hover ? UiTheme.SurfaceHover : UiTheme.Surface))
                using (var path = UiTheme.RoundedPath(new Rectangle(0, 0, Width, Height), 8))
                    g.FillPath(bgBrush, path);

                IconRenderer.Draw(g, _iconKey, new RectangleF(14, (Height - 22) / 2f, 22, 22), UiTheme.AccentCyan);

                using var textFont = new Font("Segoe UI", 10f);
                using var textBrush = new SolidBrush(UiTheme.TextPrimary);
                var textSize = g.MeasureString(Text, textFont);
                g.DrawString(Text, textFont, textBrush, 46, (Height - textSize.Height) / 2f);
            }
        }

        private static void AddButton(Control parent, string text, EventHandler action)
        {
            var button = new HoverButton
            {
                Text = text,
                Width = 300,
                Height = 40,
                FlatStyle = FlatStyle.Flat,
                ForeColor = UiTheme.TextPrimary,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0, 3, 0, 3),
                TabStop = false,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            UiTheme.ApplyRoundedRegion(button, 8);
            button.Click += (sender, e) => action(sender, e);
            parent.Controls.Add(button);
        }

        // Απλό flat button με ελαφρύ hover recolor, ώστε τα κουμπιά να μη νιώθουν στατικά.
        // Ζητήθηκε ρητά: όσα κουμπιά είναι ορθογώνια να γίνουν "pills" (πλήρως στρογγυλεμένα άκρα)
        // — εφαρμόζεται καθολικά εδώ αντί σε κάθε σημείο κλήσης ξεχωριστά, αφού το HoverButton
        // χρησιμοποιείται σε δεκάδες σημεία σε όλη την εφαρμογή. Selectable=false αποτρέπει το
        // ίδιο bug που βρέθηκε στο PillButton του ZoneLayoutEditor: το τετράγωνο keyboard-focus
        // rectangle ενός Button προεξέχει έξω από ένα στρογγυλεμένο Region αν δεν απενεργοποιηθεί.
        private sealed class HoverButton : Button
        {
            public HoverButton()
            {
                MouseEnter += (_, _) => BackColor = UiTheme.SurfaceHover;
                MouseLeave += (_, _) => BackColor = UiTheme.Surface;
                SetStyle(ControlStyles.Selectable, false);
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                if (Width > 0 && Height > 0) UiTheme.ApplyRoundedRegion(this, Height / 2);
            }
        }

        private static void SaveAppSettings()
        {
            var a = AppSettings.Load();
            a.GridSnap = WidgetSnapEngine.EnableGridSnap;
            a.SnapThreshold = WidgetSnapEngine.SnapThreshold;
            a.GridSize = WidgetSnapEngine.GridSize;
            a.Save();
        }

        private void UpdateStatus()
        {
            var m = SystemMonitorService.Instance.GetSnapshot();
            _statusLabel.Text = $"CPU {m.CpuPercent:0.0}%  •  RAM {m.AvailableMemoryMb:0} MB free";
        }

        private sealed class SettingsView : Panel
        {
            private readonly Action _openWallpaperStudio;

            public SettingsView(Action openWallpaperStudio)
            {
                _openWallpaperStudio = openWallpaperStudio;
                BackColor = UiTheme.Background;
                AutoScroll = true;
                var stack = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    Padding = new Padding(4),
                    BackColor = UiTheme.Background
                };
                Controls.Add(stack);

                AddText(stack, "Καθολικές ρυθμίσεις για εμφάνιση, γλώσσα, snap, εκκίνηση και συμπεριφορά της εφαρμογής.");

                AddSection(stack, LocalizationManager.T("Settings.Appearance"));
                stack.Controls.Add(BuildThemeRow());
                stack.Controls.Add(BuildLanguageRow());
                stack.Controls.Add(BuildOpacityRow());
                stack.Controls.Add(BuildDarkIntensityRow());
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.UiSoundCues"), AppSettings.Load().UiSoundsEnabled,
                    v => { var a = AppSettings.Load(); a.UiSoundsEnabled = v; a.Save(); }));

                AddSection(stack, LocalizationManager.T("Settings.SectionSnapping"));
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.EnableGridSnap"), AppSettings.Load().GridSnap, v => { WidgetSnapEngine.EnableGridSnap = v; SaveAppSettings(); }));
                var snapRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
                snapRow.Controls.Add(NumericRow(LocalizationManager.T("Settings.SnapThreshold"), AppSettings.Load().SnapThreshold, 5, 50,
                    v => { WidgetSnapEngine.SnapThreshold = v; SaveAppSettings(); }));
                snapRow.Controls.Add(NumericRow(LocalizationManager.T("Settings.GridSize"), Math.Clamp(AppSettings.Load().GridSize, 5, 100), 5, 100,
                    v => { WidgetSnapEngine.GridSize = v; SaveAppSettings(); }, marginLeft: 24));
                stack.Controls.Add(snapRow);

                AddSection(stack, LocalizationManager.T("Settings.SectionWallpaper"));
                var wallpaperCard = new Panel { Width = 700, Height = 64, BackColor = UiTheme.Surface, Padding = new Padding(16, 10, 14, 10), Margin = new Padding(0, 0, 0, 4) };
                UiTheme.ApplyRoundedRegion(wallpaperCard, 10);
                wallpaperCard.Controls.Add(new Label { Dock = DockStyle.Fill, Text = LocalizationManager.T("Settings.WallpaperNote"), ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, TextAlign = ContentAlignment.MiddleLeft });
                stack.Controls.Add(wallpaperCard);
                AddButton(stack, LocalizationManager.T("Settings.OpenWallpaperStudio"), (_, _) => _openWallpaperStudio());
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.AutoPerformanceMode"), AppSettings.Load().AutoPerformanceMode,
                    v => { var a = AppSettings.Load(); a.AutoPerformanceMode = v; a.Save(); }));
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.StartWallpaperWithApp"), AppSettings.Load().StartWallpaperWithWindows,
                    v => { var a = AppSettings.Load(); a.StartWallpaperWithWindows = v; a.Save(); }));

                AddSection(stack, LocalizationManager.T("Settings.SectionWidgetsStartup"));
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.ShowSystemMonitorWidget"),
                    AppSettings.Load().ShowSystemMonitor || WidgetHostEngine.Instance.GetActiveWidgets().Any(w => w.WidgetId == "sysmon"),
                    v =>
                    {
                        if (v) WidgetHostEngine.Instance.SpawnWidget("sysmon", 100, 100, 300, 200);
                        else WidgetHostEngine.Instance.GetActiveWidgets().FirstOrDefault(w => w.WidgetId == "sysmon")?.Close();
                        var a = AppSettings.Load(); a.ShowSystemMonitor = v; a.Save();
                    }));
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.StartWithWindows"), StartupManager.IsStartupEnabled(), StartupManager.SetStartup));
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.RestoreLastSession"), AppSettings.Load().RestoreLastSession,
                    v => { var a = AppSettings.Load(); a.RestoreLastSession = v; a.Save(); }));
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.EnableAnimations"), AppSettings.Load().EnableAnimations,
                    v => { var a = AppSettings.Load(); a.EnableAnimations = v; a.Save(); }));
            }

            private static Panel BuildThemeRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.Theme") + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                var combo = new FlatComboBox { Width = 200 };
                var current = AppSettings.Load().ThemeMode;
                combo.SetItems(new[] { LocalizationManager.T("Settings.ThemeFollow"), LocalizationManager.T("Settings.ThemeLight"), LocalizationManager.T("Settings.ThemeDark") },
                    current switch { "Light" => LocalizationManager.T("Settings.ThemeLight"), "Dark" => LocalizationManager.T("Settings.ThemeDark"), _ => LocalizationManager.T("Settings.ThemeFollow") });
                combo.SelectedIndexChanged += (_, _) =>
                {
                    var mode = combo.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "Follow" };
                    ThemeManager.SetMode(mode);
                };
                row.Controls.Add(combo);
                return row;
            }

            // Πραγματικές emoji σημαίες: χρειάζονται τη γραμματοσειρά "Segoe UI Emoji" για να
            // αποδοθούν ως έγχρωμα εικονίδια αντί για γράμματα περιφερειακού δείκτη (GR/GB) —
            // αυτό ήταν το ζητούμενο πρόβλημα ("δεν έχεις βάλει τις σημαίες στο dropdown").
            private static readonly string[] LanguageCodes = { "Follow", "el", "en" };

            private static Panel BuildLanguageRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.Language") + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                var combo = new FlatComboBox { Width = 220, ItemFont = new Font("Segoe UI Emoji", 9.5f) };
                var labels = new[]
                {
                    "🌐  " + LocalizationManager.T("Settings.LanguageFollow"),
                    "🇬🇷  " + LocalizationManager.T("Settings.LanguageGreek"),
                    "🇬🇧  " + LocalizationManager.T("Settings.LanguageEnglish")
                };
                var currentLang = AppSettings.Load().Language;
                combo.SetItems(labels, labels[currentLang switch { "el" => 1, "en" => 2, _ => 0 }]);
                combo.SelectedIndexChanged += (_, _) => LocalizationManager.SetLanguage(LanguageCodes[combo.SelectedIndex]);
                row.Controls.Add(combo);
                return row;
            }

            private static CheckBox Checkbox(string text, bool initial, Action<bool> onChange)
            {
                var box = new CheckBox
                {
                    Text = text,
                    AutoSize = true,
                    Checked = initial,
                    ForeColor = UiTheme.TextPrimary,
                    Font = UiTheme.FontBody,
                    Margin = new Padding(0, 4, 0, 4)
                };
                box.CheckedChanged += (_, _) => onChange(box.Checked);
                return box;
            }

            // "Διαφάνεια εφαρμογής" ανάλογη της γνωστής επιλογής διαφάνειας παραθύρων των Windows —
            // εδώ εφαρμόζεται στο ΚΥΡΙΟ παράθυρο μέσω Form.Opacity (πραγματική λειτουργία, όχι
            // απλή προσομοίωση), με ζωντανό preview καθώς σέρνεις το slider.
            private FlowLayoutPanel BuildOpacityRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
                row.Controls.Add(new Label { Text = "Window opacity:", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                int initial = Math.Clamp((int)Math.Round(AppSettings.Load().WindowOpacity * 100), 60, 100);
                var track = new TrackBar { Minimum = 60, Maximum = 100, Value = initial, Width = 200, TickFrequency = 10, SmallChange = 1, LargeChange = 5 };
                var valueLabel = new Label { Text = $"{initial}%", AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Padding = new Padding(8, 6, 0, 0) };
                track.Scroll += (_, _) =>
                {
                    valueLabel.Text = $"{track.Value}%";
                    double opacity = track.Value / 100.0;
                    var a = AppSettings.Load(); a.WindowOpacity = opacity; a.Save();
                    if (FindForm() is { } form) form.Opacity = opacity;
                };
                row.Controls.Add(track);
                row.Controls.Add(valueLabel);
                return row;
            }

            // Πόσο σκούρο είναι το dark theme — ζητήθηκε ρητά. 0 = πιο ανοιχτό σκούρο, 100 = σχεδόν
            // μαύρο, 50 = η αρχική, αμετάβλητη βάση.
            //
            // Bug fix: πριν, κάθε Scroll tick (δεκάδες/δευτερόλεπτο ενώ σέρνεις) καλούσε απευθείας
            // το ΑΚΡΙΒΟ SetDarkIntensity (save σε δίσκο + πλήρης ανακατασκευή σελίδας/sidebar) —
            // ανέφερε "τρεμόπαιγμα" και "πηγαίνει κάθε 5%" επειδή το ίδιο το TrackBar καταστρεφόταν
            // και ξαναφτιαχνόταν ενώ ο χρήστης ακόμη το έσερνε. Τώρα: Scroll κάνει μόνο ΦΤΗΝΗ
            // προεπισκόπηση (PreviewDarkIntensity — Invalidate, καμία ανακατασκευή), και η ακριβή
            // εφαρμογή/αποθήκευση γίνεται ΜΙΑ φορά στο MouseUp (όταν αφήνει το slider).
            private FlowLayoutPanel BuildDarkIntensityRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.DarkIntensity") + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                int initial = Math.Clamp((int)Math.Round(AppSettings.Load().DarkIntensity * 100), 0, 100);
                var track = new TrackBar { Minimum = 0, Maximum = 100, Value = initial, Width = 200, TickFrequency = 10, SmallChange = 1, LargeChange = 5 };
                var valueLabel = new Label { Text = $"{initial}%", AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Padding = new Padding(8, 6, 0, 0) };
                track.Scroll += (_, _) =>
                {
                    valueLabel.Text = $"{track.Value}%";
                    ThemeManager.PreviewDarkIntensity(track.Value / 100.0);
                };
                track.MouseUp += (_, _) => ThemeManager.SetDarkIntensity(track.Value / 100.0);
                track.KeyUp += (_, _) => ThemeManager.SetDarkIntensity(track.Value / 100.0); // πληκτρολόγιο/βελάκια: δεν έχουν MouseUp
                row.Controls.Add(track);
                row.Controls.Add(valueLabel);
                return row;
            }

            private static FlowLayoutPanel NumericRow(string label, int value, int min, int max, Action<int> onChange, int marginLeft = 0)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(marginLeft, 2, 0, 4) };
                row.Controls.Add(new Label { Text = label + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                var numeric = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 80 };
                numeric.ValueChanged += (_, _) => onChange((int)numeric.Value);
                row.Controls.Add(numeric);
                row.Controls.Add(new Label { Text = "px", AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Padding = new Padding(8, 6, 0, 0) });
                return row;
            }
        }

        private static Icon LoadApplicationIcon()
        {
            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "MotionDesk.ico");
            return File.Exists(path) ? new Icon(path) : SystemIcons.Application;
        }
    }
}
