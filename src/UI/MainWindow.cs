using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
            // Διορθώνει "λευκά τετραγωνάκια" κατά την αλλαγή σκουρότητας θέματος: η πλήρης
            // ανακατασκευή σελίδας/sidebar σε κάθε ThemeManager.Changed (OnThemeOrLanguageChanged)
            // κάνει Controls.Clear()+Add() σε πολλά containers — χωρίς DoubleBuffered στο ίδιο το
            // Form, τα Windows ζωγραφίζουν προσωρινά το προεπιλεγμένο φόντο (λευκό) στις περιοχές
            // που μόλις αδειάσανε πριν προλάβουν να ζωγραφιστούν τα νέα controls από πάνω.
            //
            // ΔΙΟΡΘΩΣΗ πραγματικού regression bug (επιβεβαιώθηκε ζωντανά): το αρχικό fix πρόσθεσε
            // ΚΑΙ ControlStyles.UserPaint — αυτό λέει στα WinForms "θα ζωγραφίζω μόνος μου ΟΛΟ το
            // φόντο", απενεργοποιώντας το κανονικό WM_ERASEBKGND· αφού το MainWindow ΔΕΝ έχει δικό
            // του πλήρες OnPaint που γεμίζει όλη την περιοχή πελάτη, παλιά pixels από προηγούμενο
            // paint pass έμεναν ορατά κάτω από τα νέα — φαινόταν σαν "διπλό", θολό/φαντασματικό
            // κείμενο (π.χ. "Πίνακας Ελέγχου" σε γιγάντια γράμματα πάνω από το κανονικό). Το απλό
            // DoubleBuffered = true (η καθιερωμένη τεχνική για Form, χωρίς UserPaint) διπλασιάζει
            // την απόδοση χωρίς να πειράξει το background-erase pipeline.
            DoubleBuffered = true;
            var appSettings = AppSettings.Load();
            WidgetSnapEngine.EnableGridSnap = appSettings.GridSnap;
            WidgetSnapEngine.SnapThreshold = Math.Clamp(appSettings.SnapThreshold, 5, 50);
            WidgetSnapEngine.GridSize = Math.Clamp(appSettings.GridSize, 5, 100);
            Text = "MotionDesk Studio";
            Icon = LoadApplicationIcon();
            StartPosition = FormStartPosition.CenterScreen;
            // Μεγαλύτερο προεπιλεγμένο μέγεθος — ζητήθηκε ρητά ώστε να χωράει άνετα η νέα σελίδα
            // "Audio Enhancement" (equalizer bands + πολλαπλές ρυθμίσεις + visualizer μαζί).
            MinimumSize = new Size(1040, 680);
            Size = new Size(1280, 800);
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
            _pageBuilders["AudioEnhancement"] = ShowAudioEnhancement;

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
            // ΝΕΟ, στα πρότυπα του FancyWM (ζητήθηκε ρητά): μετακινεί το τρέχον ενεργό παράθυρο
            // στη γειτονική DeskZone προς αυτή την κατεύθυνση, χωρίς ποντίκι. Ctrl+Alt+Shift (όχι
            // απλό Ctrl+Alt) — το απλό Ctrl+Alt+βελάκι βρέθηκε ήδη δεσμευμένο σε αυτό το μηχάνημα
            // από τον οδηγό γραφικών (περιστροφή οθόνης), επιβεβαιωμένο με αποτυχημένο RegisterHotKey.
            _hotkeys.RegisterCtrlAltShift(Keys.Left, () => ZoneSnapEngine.MoveForegroundWindowToZone(ZoneSnapEngine.ZoneDirection.Left));
            _hotkeys.RegisterCtrlAltShift(Keys.Right, () => ZoneSnapEngine.MoveForegroundWindowToZone(ZoneSnapEngine.ZoneDirection.Right));
            _hotkeys.RegisterCtrlAltShift(Keys.Up, () => ZoneSnapEngine.MoveForegroundWindowToZone(ZoneSnapEngine.ZoneDirection.Up));
            _hotkeys.RegisterCtrlAltShift(Keys.Down, () => ZoneSnapEngine.MoveForegroundWindowToZone(ZoneSnapEngine.ZoneDirection.Down));

            ThemeManager.UiContext = System.Threading.SynchronizationContext.Current;
            AnimationsSettingChanged += UpdateAnimationTimers;
            WorkspaceProfileService.ExitSaveDone = false;
            ThemeManager.Changed += OnThemeOrLanguageChanged;
            ThemeManager.Repainted += OnThemeRepainted;
            LocalizationManager.Changed += OnThemeOrLanguageChanged;
            SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;

            // Χαμηλού-κόστους hook, τρέχει σε όλη τη διάρκεια ζωής της εφαρμογής — δεν κάνει
            // τίποτα εκτός αν ο χρήστης κρατάει Shift ενώ σέρνει ένα παράθυρο (βλ. ZoneSnapEngine).
            ZoneSnapEngine.AllowOwnWindow = h => IsHandleCreated && h == Handle;
            ZoneSnapEngine.Start();

            FormClosed += (_, _) =>
            {
                if (!WorkspaceProfileService.ExitSaveDone) { try { WorkspaceProfileService.Save("Last Session"); } catch { } }
                AnimationsSettingChanged -= UpdateAnimationTimers;
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
            UpdateAnimationTimers(); // παράθυρο ακόμη αόρατο (π.χ. --background) → οι animations ξεκινούν μόνο όταν εμφανιστεί
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
                // SuspendLayout/ResumeLayout γύρω από ΟΛΗ την ανακατασκευή (χρώματα + sidebar +
                // τρέχουσα σελίδα) — χωρίς αυτό, κάθε ενδιάμεσο Controls.Clear()/Add() προκαλούσε
                // δικό του layout+repaint pass, ορατό ως στιγμιαία "λευκά τετραγωνάκια" στο σημείο
                // που μόλις άδειασε πριν γεμίσει ξανά.
                SuspendLayout();
                try
                {
                    ApplyChromeColors();
                    RebuildSidebarNav();
                    if (_pageBuilders.TryGetValue(_currentPageKey, out var builder)) builder();
                }
                finally { ResumeLayout(true); }
                Invalidate(true);
            }));
        }

        // Φτηνιά ζωντανή προεπισκόπηση ενώ σέρνεις το slider σκουρότητας — καμία ανακατασκευή
        // control tree (αυτό παραμένει αποκλειστικά στο MouseUp μέσω Changed/OnThemeOrLanguageChanged).
        //
        // Bug fix: πριν, εδώ γινόταν ΜΟΝΟ Invalidate(true) — αυτό ενημερώνει ζωντανά μόνο τα
        // owner-draw controls (NavButton/StatCard/PulsingLogoPanel κ.λπ., που διαβάζουν το
        // UiTheme.* ζωντανά μέσα στο δικό τους OnPaint), ενώ τα απλά Panel (_content/_header/
        // _status κ.λπ.) ζωγραφίζουν με το ΗΔΗ αποθηκευμένο BackColor property τους — που ΔΕΝ
        // αλλάζει μόνο του από ένα Invalidate. Αυτό ακριβώς εξηγούσε το αναφερόμενο σύμπτωμα "η
        // σκουρότητα εφαρμόζεται μόνο στο πλευρικό μενού και στην κορυφή (τα owner-draw κομμάτια),
        // όχι σε όλο το παράθυρο" όσο ο χρήστης έσερνε το slider. Το ApplyChromeColors() είναι
        // φτηνό (μόνο αναθέσεις χρωμάτων σε ήδη υπάρχοντα controls, καμία Controls.Clear()/Add()),
        // οπότε ασφαλές να τρέχει σε κάθε tick χωρίς να επαναφέρει το αρχικό flicker bug.
        private void OnThemeRepainted()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(OnThemeRepainted)); return; }
            ApplyChromeColors();
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

        // Οι διακοσμητικές animations (παλμός λογότυπου, κύμα header) τρέχαν συνεχώς σε 20Hz/16Hz
        // ακόμη κι όταν το παράθυρο ήταν κρυμμένο στο tray ή ελαχιστοποιημένο — άσκοπη κατανάλωση
        // CPU/GPU για μια εφαρμογή που ζει κυρίως στο παρασκήνιο. Τρέχουν μόνο όταν φαίνονται.
        // Καλείται και από τη ρύθμιση Animations ώστε η αλλαγή να ισχύει αμέσως.
        internal static event Action? AnimationsSettingChanged;

        private void UpdateAnimationTimers()
        {
            bool visible = Visible && WindowState != FormWindowState.Minimized && AppSettings.Load().EnableAnimations;
            if (_brandPulseTimer != null) _brandPulseTimer.Enabled = visible;
            if (_headerWaveTimer != null) _headerWaveTimer.Enabled = visible;
        }

        // ΔΙΟΡΘΩΣΗ πραγματικού bug: το κλείσιμο (X) του κύριου παραθύρου το ΚΑΤΕΣΤΡΕΦΕ — και το
        // FormClosed handler σταματούσε ZoneSnapEngine + όλα τα global hotkeys (Ctrl+Alt+G/M/Shift+βέλη)
        // ενώ η εφαρμογή συνέχιζε να ζει στο tray (DeskZones/συντομεύσεις "έσβηναν" σιωπηλά), και το
        // επόμενο άνοιγμα ξανάτρεχε όλον τον constructor (επαναφορά "Last Session" ξανά). Η ρύθμιση
        // MinimizeToTray (προεπιλογή true) υπήρχε αλλά ΔΕΝ χρησιμοποιούνταν πουθενά. Τώρα το X κρύβει
        // στο tray· η πραγματική έξοδος γίνεται μόνο από το "Έξοδος" του tray menu (CloseReason != UserClosing).
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && AppSettings.Load().MinimizeToTray)
            {
                e.Cancel = true;
                // Φεύγουμε από βαριές σελίδες (live equalizer: WASAPI capture + timer 25Hz) ώστε να μην
                // τρέχουν αόρατες στο παρασκήνιο.
                if (_currentPageKey is "AudioEnhancement" or "Performance") NavigateTo("Dashboard");
                Hide();
                return;
            }
            // Πραγματικό κλείσιμο (έξοδος/shutdown): αποθήκευση ΤΩΡΑ, όσο τα widgets ζουν ακόμα.
            if (!WorkspaceProfileService.ExitSaveDone)
            {
                try { WorkspaceProfileService.Save("Last Session"); WorkspaceProfileService.ExitSaveDone = true; } catch { }
            }
            base.OnFormClosing(e);
        }

        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateAnimationTimers(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateAnimationTimers(); }

        // Controls.Clear() ΜΟΝΟ αφαιρεί — δεν κάνει Dispose. Οι σελίδες Widgets/DeskZones/DeskStrip ξαναχτίζουν
        // τις λίστες τους κάθε 2s, άρα πριν διέρρεαν panels/labels/buttons/Regions συνεχώς όσο ήταν ανοιχτή η σελίδα.
        private static void DisposeChildren(Control parent)
        {
            foreach (Control child in parent.Controls.Cast<Control>().ToList()) child.Dispose();
            parent.Controls.Clear();
        }

        private void RebuildSidebarNav()
        {
            DisposeChildren(_nav);
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
            // Ζητήθηκε ρητά: αντί για απλό "Audio Visualizer", νέα καρτέλα "Διαχείριση Ήχου" με
            // presets ενίσχυσης + ζωντανό visualizer, εμπνευσμένη από τη λογική του FXSound.
            AddNavButton(_nav, LocalizationManager.T("Nav.AudioEnhancement"), "AudioEnhancement", ShowAudioEnhancement, (string?)null);
            // "Σχετικά" ζητήθηκε ρητά να είναι το ΤΕΛΕΥΤΑΙΟ στοιχείο του nav, κάτω από τη
            // "Διαχείριση Ήχου" (πριν ήταν ανάποδα).
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

        private NavButton AddNavButton(Control parent, string text, string key, Action action, int shortcutIndex) =>
            AddNavButton(parent, text, key, action, $"Ctrl+{shortcutIndex}");

        private NavButton AddNavButton(Control parent, string text, string key, Action action, string? shortcutLabel)
        {
            var button = new NavButton(key, text, shortcutLabel) { Width = 184 };
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
            // Η ρύθμιση "Animations" (Ρυθμίσεις → Εκκίνηση) δεν είχε καμία επίδραση — τώρα την τηρούμε.
            if (!AppSettings.Load().EnableAnimations)
            {
                _sidebar.Width = targetWidth;
                onComplete?.Invoke();
                return;
            }
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
            // ΔΙΟΡΘΩΣΗ πραγματικού bug: Controls.Clear() αφαιρεί τα child controls από τη συλλογή
            // αλλά ΔΕΝ τα κάνει Dispose — έτσι το παλιό page panel (π.χ. του Audio Enhancement, με
            // το δικό του System.Windows.Forms.Timer) έμενε "ορφανό" αλλά ζωντανό, ο Timer του
            // συνέχιζε να τρέχει επ' άπειρον, και το panel.Disposed cleanup ΠΟΤΕ δεν εκτελούνταν
            // ντετερμινιστικά στο UI thread — μόνο (ενδεχομένως) αργότερα από τον GC finalizer σε
            // ΔΙΑΦΟΡΕΤΙΚΟ thread, προκαλώντας διαλείπον NullReferenceException σε πεδία που
            // μοιράζονταν μεταξύ διαδοχικών επισκέψεων στην ίδια σελίδα. Ρητό Dispose εδώ κάνει το
            // cleanup άμεσο και στο σωστό thread.
            foreach (Control old in _content.Controls) old.Dispose();
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
                        if (editor.ShowDialog(this) == DialogResult.OK) _statusLabel.Text = LocalizationManager.T("Status.ZonesUpdated");
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
                    DeskFlipEngine.Show();
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
            var systemCard = AddCard(panel, LocalizationManager.T("Dashboard.SystemCardTitle"),
                string.Format(LocalizationManager.T("Dashboard.SystemCardFormat"), metrics.CpuPercent.ToString("0.0"), metrics.AvailableMemoryMb.ToString("0"), metrics.TotalMemoryMb.ToString("0"), RamUsedPercent(metrics.AvailableMemoryMb, metrics.TotalMemoryMb)),
                onClick: () => NavigateTo("Performance"));
            AddCard(panel, LocalizationManager.T("Dashboard.DesktopCardTitle"), string.Format(LocalizationManager.T("Dashboard.DesktopCardBodyFormat"),
                Screen.AllScreens.Length, WidgetHostEngine.Instance.GetActiveWidgets().Count, Screen.AllScreens.Sum(s => ZoneLayoutStore.GetLayout(s.DeviceName).Zones.Count)));
            AddCard(panel, LocalizationManager.T("Dashboard.WallpaperCardTitle"), $"{LocalizationManager.T(WallpaperHostEngine.Instance.IsEnabled ? "Common.On" : "Common.Off")}   •   {WallpaperSettings.Load().Mode}   •   {WallpaperSettings.Load().PerformanceMode}");

            var dashboardRefreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            dashboardRefreshTimer.Tick += (_, _) =>
            {
                var m = SystemMonitorService.Instance.GetSnapshot();
                systemCard.Text = string.Format(LocalizationManager.T("Dashboard.SystemCardFormat"), m.CpuPercent.ToString("0.0"), m.AvailableMemoryMb.ToString("0"), m.TotalMemoryMb.ToString("0"), RamUsedPercent(m.AvailableMemoryMb, m.TotalMemoryMb));
            };
            dashboardRefreshTimer.Start();
            panel.Disposed += (_, _) => dashboardRefreshTimer.Dispose();

            AddSection(panel, LocalizationManager.T("Dashboard.SectionQuickLaunch"));
            AddIconButtonGrid(panel,
                ("Widgets", LocalizationManager.T("Dashboard.QuickOpenWidgets"), (_, _) => NavigateTo("Widgets")),
                ("DeskZones", LocalizationManager.T("Dashboard.QuickEditZones"), (_, _) => { using var editor = new ZoneLayoutEditorForm(); editor.ShowDialog(this); }),
                ("Wallpaper", LocalizationManager.T("Dashboard.QuickEnableWallpaper"), (_, _) => { WallpaperHostEngine.Instance.Enable(); _statusLabel.Text = LocalizationManager.T("Dashboard.WallpaperEnabledStatus"); }),
                ("Command", LocalizationManager.T("Dashboard.QuickCommandPalette"), (_, _) => ShowCommandPalette()));

            AddSection(panel, LocalizationManager.T("Dashboard.SectionWorkspace"));
            // Τα "Work"/"Gaming"/"Focus" παραμένουν ως έχουν (ονόματα προφίλ/IconKey, βλ. σχόλιο
            // στο TrayApplicationContext) — μόνο η λέξη "Profile/Προφίλ" γύρω τους μεταφράζεται.
            string profileFmt = LocalizationManager.T("Dashboard.ProfileButtonFormat");
            AddIconButtonGrid(panel,
                ("Work", string.Format(profileFmt, "Work"), (_, _) => LoadProfileFromQuickButton("Work")),
                ("Gaming", string.Format(profileFmt, "Gaming"), (_, _) => LoadProfileFromQuickButton("Gaming")),
                ("Focus", string.Format(profileFmt, "Focus"), (_, _) => LoadProfileFromQuickButton("Focus")));
            SetPage("Page.Dashboard.Title", panel);
        }

        private void ShowWidgets()
        {
            _currentPageKey = "Widgets";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Widgets.Intro"));
            // Ζητήθηκε ρητά "όλα τα widgets να έχουν το ίδιο μέγεθος" — ενιαίο προεπιλεγμένο
            // μέγεθος από τις Ρυθμίσεις αντί για διαφορετικό ανά τύπο widget (ο χρήστης μπορεί να
            // το αλλάξει μετά ανά widget, μέσω της νέας λαβής αλλαγής μεγέθους στη γωνία).
            var wds = AppSettings.Load();
            int ww = wds.WidgetDefaultWidth, wh = wds.WidgetDefaultHeight;
            AddSection(panel, LocalizationManager.T("Widgets.SectionDesktopWidgets"));
            AddIconButtonGrid(panel,
                ("SystemMonitor", LocalizationManager.T("Widgets.SystemMonitor"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("sysmon", 100, 100, ww, wh)),
                ("Clock", LocalizationManager.T("Widgets.Clock"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("clock", 450, 100, ww, wh)),
                ("Network", LocalizationManager.T("Widgets.Network"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("network", 100, 350, ww, wh)),
                ("AudioVisualizer", LocalizationManager.T("Widgets.AudioVisualizer"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("audio", 450, 350, ww, wh)),
                ("Weather", LocalizationManager.T("Widgets.Weather"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("weather", 800, 100, ww, wh)),
                ("Disk", LocalizationManager.T("Widgets.Disk"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("disk", 800, 350, ww, wh)));
            AddSection(panel, LocalizationManager.T("Widgets.SectionWorkspaceActions"));
            AddIconButtonGrid(panel,
                ("Save", LocalizationManager.T("Widgets.SaveLayout"), (_, _) => { WorkspaceProfileService.Save("Last Session"); _statusLabel.Text = LocalizationManager.T("Widgets.LayoutSavedStatus"); }),
                ("Restore", LocalizationManager.T("Widgets.RestoreLayout"), (_, _) => { WorkspaceProfileService.Load("Last Session"); _statusLabel.Text = LocalizationManager.T("Widgets.WorkspaceRestoredStatus"); }),
                ("Close", LocalizationManager.T("Widgets.CloseAllWidgets"), (_, _) => { WidgetHostEngine.Instance.CloseAll(); }));

            AddSection(panel, LocalizationManager.T("Widgets.SectionActiveWidgets"));
            var activeList = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            panel.Controls.Add(activeList);

            void RefreshActiveList()
            {
                activeList.SuspendLayout();
                DisposeChildren(activeList);
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
                    var lockBtn = new HoverButton { Text = LocalizationManager.T(w.IsLocked ? "Common.Unlock" : "Common.Lock"), Width = 80, Height = 28, Location = new Point(310, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    lockBtn.FlatAppearance.BorderSize = 0;
                    lockBtn.Click += (_, _) => { w.IsLocked = !w.IsLocked; RefreshActiveList(); };
                    row.Controls.Add(lockBtn);
                    var closeBtn = new HoverButton { Text = LocalizationManager.T("Common.Close"), Width = 80, Height = 28, Location = new Point(400, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
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
                ("Container", LocalizationManager.T("DeskZones.NewContainerBtn"), (_, _) => DeskContainerHostEngine.Instance.SpawnContainer($"container{DateTime.Now.Ticks}", LocalizationManager.T("DeskZones.NewContainerDefaultTitle"), 360, 200, 360, 260)),
                ("Close", LocalizationManager.T("DeskZones.CloseAllContainers"), (_, _) => DeskContainerHostEngine.Instance.CloseAll()));

            var activeContainersList = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            panel.Controls.Add(activeContainersList);

            void RefreshActiveContainers()
            {
                activeContainersList.SuspendLayout();
                DisposeChildren(activeContainersList);
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
                    var closeBtn = new HoverButton { Text = LocalizationManager.T("Common.Close"), Width = 80, Height = 28, Location = new Point(400, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
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
            var playlistCard = AddCard(panel, LocalizationManager.T("Wallpaper.PlaylistLabel"), DescribePlaylist(settings));

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
            // Ξαναχτίζει ΟΛΟΚΛΗΡΗ τη σελίδα μετά από προσθήκη — ζητήθηκε ρητά "τόσο με την
            // προσθήκη video όσο και με την προσθήκη φακέλου η εφαρμογή να ενημερώνει τη λίστα
            // με τα βίντεο". Πριν, η λίστα (dropdown/κάρτα) δεν ανανεωνόταν καθόλου μετά την
            // προσθήκη — μόνο το στατιστικό κείμενο του playlistCard.
            AddIconButtonGrid(panel,
                ("Add", LocalizationManager.T("Wallpaper.AddVideoButton"), (_, _) => ChooseWallpaperVideoFiles(ShowWallpaper)),
                ("Add", LocalizationManager.T("Wallpaper.AddFolderButton"), (_, _) => { ChooseWallpaperFolder(); ShowWallpaper(); }),
                ("Delete", LocalizationManager.T("Wallpaper.ClearPlaylist"), (_, _) => { WallpaperHostEngine.Instance.ClearVideo(); ShowWallpaper(); }));

            // Επιλογή ποια από τα φορτωμένα βίντεο (αρχεία ή περιεχόμενο φακέλου) θα αναπαράγονται: όλα / κανένα με ένα κλικ,
            // και μετά τικάρισμα μεμονωμένων γραμμών παρακάτω (κλικ και πάνω στο όνομα).
            AddIconButtonGrid(panel,
                ("Restore", LocalizationManager.T("Wallpaper.SelectAll"), (_, _) => { WallpaperHostEngine.Instance.SetAllVideosEnabled(true); ShowWallpaper(); }),
                ("Close", LocalizationManager.T("Wallpaper.SelectNone"), (_, _) => { WallpaperHostEngine.Instance.SetAllVideosEnabled(false); ShowWallpaper(); }));

            // Πραγματική λίστα με ΟΛΑ τα φορτωμένα βίντεο (όχι μόνο ένα στατιστικό "N αρχεία") —
            // κάθε γραμμή έχει το όνομα αρχείου και ένα ✕ για αφαίρεση, ζητήθηκε ρητά "να μπορεί
            // να προσθαφαιρεί αρχεία βίντεο" απευθείας από τη βιβλιοθήκη.
            var videoListPanel = new Panel { Width = 720, Height = Math.Min(220, Math.Max(50, settings.VideoPaths.Count * 34 + 10)), Margin = new Padding(0, 0, 0, 10), AutoScroll = true, BackColor = UiTheme.Surface };
            int rowY = 4;
            foreach (var videoPath in settings.VideoPaths.ToArray())
            {
                bool exists = File.Exists(videoPath);
                var row = new Panel { Location = new Point(4, rowY), Size = new Size(700, 28) };
                // Ζητήθηκε ρητά "να επιλέγει ο χρήστης 1 ή περισσότερα βίντεο για να
                // αναπαράγονται" — ένα φορτωμένο βίντεο μπορεί να μείνει στη βιβλιοθήκη χωρίς να
                // συμμετέχει στην ενεργή αναπαραγωγή/shuffle (checkbox, όχι αφαίρεση).
                var enabledCheck = new CheckBox { Checked = !settings.DisabledVideoPaths.Contains(videoPath), Location = new Point(2, 4), Size = new Size(20, 20) };
                enabledCheck.CheckedChanged += (_, _) => WallpaperHostEngine.Instance.SetVideoEnabled(videoPath, enabledCheck.Checked);
                var nameLabel = new Label
                {
                    Text = Path.GetFileName(videoPath) + (exists ? "" : LocalizationManager.T("Wallpaper.MissingSuffix")),
                    AutoSize = false,
                    Size = new Size(596, 24),
                    Location = new Point(28, 2),
                    ForeColor = exists ? UiTheme.TextPrimary : UiTheme.TextMuted,
                    Font = UiTheme.FontBody,
                    AutoEllipsis = true
                };
                var removeBtn = new Label { Text = "✕", AutoSize = false, Size = new Size(24, 24), Location = new Point(670, 2), TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9) };
                removeBtn.MouseEnter += (_, _) => removeBtn.ForeColor = Color.FromArgb(231, 76, 60);
                removeBtn.MouseLeave += (_, _) => removeBtn.ForeColor = UiTheme.TextSecondary;
                removeBtn.Click += (_, _) => { WallpaperHostEngine.Instance.RemoveVideo(videoPath); ShowWallpaper(); };
                nameLabel.Click += (_, _) => enabledCheck.Checked = !enabledCheck.Checked;
                row.Controls.Add(enabledCheck);
                row.Controls.Add(nameLabel);
                row.Controls.Add(removeBtn);
                videoListPanel.Controls.Add(row);
                rowY += 32;
            }
            if (settings.VideoPaths.Count == 0)
                videoListPanel.Controls.Add(new Label { Text = LocalizationManager.T("Wallpaper.NoVideosYet"), AutoSize = true, Location = new Point(6, 6), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody });
            panel.Controls.Add(videoListPanel);

            var shuffle = new CheckBox { Text = LocalizationManager.T("Wallpaper.Shuffle"), AutoSize = true, Checked = settings.Shuffle, ForeColor = UiTheme.TextPrimary, Margin = new Padding(0, 4, 0, 10) };
            shuffle.CheckedChanged += (_, _) => WallpaperHostEngine.Instance.SetShuffle(shuffle.Checked);
            panel.Controls.Add(shuffle);

            AddText(panel, LocalizationManager.T("Wallpaper.SupportedFormats"));

            // Πραγματικό DSP (bass/mid/treble/volume) πάνω στον ΔΙΚΟ ΜΑΣ ήχο του wallpaper video,
            // μέσω Web Audio API στο wallpaper/index.html (ensureAudioGraph/applyAudioConfig) — όχι
            // system-wide, μόνο η αναπαραγωγή του MotionDesk. Προεπιλογή ΚΛΕΙΣΤΟ (AudioEnabled
            // false): το wallpaper ήταν πάντα σιωπηλό πριν, δεν αλλάζει συμπεριφορά χωρίς ρητή
            // ενεργοποίηση από τον χρήστη.
            AddSection(panel, LocalizationManager.T("Wallpaper.SectionAudio"));
            var audioEnabledCheck = new CheckBox { Text = LocalizationManager.T("Wallpaper.AudioEnabled"), AutoSize = true, Checked = settings.AudioEnabled, ForeColor = UiTheme.TextPrimary, Margin = new Padding(0, 4, 0, 8) };
            panel.Controls.Add(audioEnabledCheck);

            var volumeTrack = new TrackBar { Minimum = 0, Maximum = 100, Value = (int)Math.Clamp(settings.AudioVolume * 100, 0, 100), Width = 220, TickStyle = TickStyle.None };
            var bassTrack = new TrackBar { Minimum = -12, Maximum = 12, Value = (int)Math.Clamp(settings.AudioBassGain, -12, 12), Width = 220, TickStyle = TickStyle.None };
            var midTrack = new TrackBar { Minimum = -12, Maximum = 12, Value = (int)Math.Clamp(settings.AudioMidGain, -12, 12), Width = 220, TickStyle = TickStyle.None };
            var trebleTrack = new TrackBar { Minimum = -12, Maximum = 12, Value = (int)Math.Clamp(settings.AudioTrebleGain, -12, 12), Width = 220, TickStyle = TickStyle.None };

            void ApplyAudioSettings() => WallpaperHostEngine.Instance.SetAudioSettings(
                audioEnabledCheck.Checked, volumeTrack.Value / 100.0, bassTrack.Value, midTrack.Value, trebleTrack.Value);

            Panel AudioRow(string label, TrackBar track, string unit)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
                row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = UiTheme.TextSecondary, Width = 110, Padding = new Padding(0, 6, 8, 0) });
                var valueLabel = new Label { AutoSize = true, ForeColor = UiTheme.TextMuted, Padding = new Padding(8, 6, 0, 0) };
                void UpdateLabel() => valueLabel.Text = unit == "%" ? $"{track.Value}%" : $"{(track.Value > 0 ? "+" : "")}{track.Value} dB";
                UpdateLabel();
                track.ValueChanged += (_, _) => { UpdateLabel(); ApplyAudioSettings(); };
                row.Controls.Add(track);
                row.Controls.Add(valueLabel);
                return row;
            }

            audioEnabledCheck.CheckedChanged += (_, _) => ApplyAudioSettings();
            panel.Controls.Add(AudioRow(LocalizationManager.T("Wallpaper.AudioVolume"), volumeTrack, "%"));
            panel.Controls.Add(AudioRow(LocalizationManager.T("Wallpaper.AudioBass"), bassTrack, "dB"));
            panel.Controls.Add(AudioRow(LocalizationManager.T("Wallpaper.AudioMid"), midTrack, "dB"));
            panel.Controls.Add(AudioRow(LocalizationManager.T("Wallpaper.AudioTreble"), trebleTrack, "dB"));
            AddText(panel, LocalizationManager.T("Wallpaper.AudioNote"));
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
            styleCombo.SetItems(new[] { "Ribbons", "Aurora", "TechGrid" }, settings.WaveStyle);
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
            string.Format(LocalizationManager.T("Wallpaper.StateFormat"), WallpaperHostEngine.Instance.IsEnabled ? LocalizationManager.T("Wallpaper.EnabledShort") : LocalizationManager.T("Wallpaper.DisabledShort"), s.Mode, s.PerformanceMode);

        private static string DescribePlaylist(WallpaperSettings s)
        {
            var playable = s.VideoPaths.Where(File.Exists).ToList();
            if (playable.Count == 0) return LocalizationManager.T("Wallpaper.PlaylistEmpty");
            return string.Format(LocalizationManager.T("Wallpaper.PlaylistFormat"), playable.Count, s.Shuffle ? LocalizationManager.T("Wallpaper.ShuffleSuffix") : "", Path.GetFileName(s.CurrentPlaylistFile() ?? ""));
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

        private static void ChooseWallpaperVideoFiles(Action? onCompleted = null)
        {
            // Ζητήθηκε ρητά "δυνατότητα φόρτωσης video μεμονωμένων ή playlists με αρχεία
            // zip/7zip" — το ίδιο dialog δέχεται πλέον και αρχεία συμπίεσης· η εξαγωγή τους
            // γίνεται στο AddWallpaperVideosAsync.
            using var dialog = new OpenFileDialog
            {
                Filter = LocalizationManager.T("Filter.VideoPlaylist") + "|" + LocalizationManager.T("Filter.AllFiles"),
                Title = LocalizationManager.T("Wallpaper.AddVideoTitle"),
                Multiselect = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                _ = AddWallpaperVideosAsync(dialog.FileNames, onCompleted);
        }

        // Το Wallpaper Studio αποδίδει τα βίντεο μέσα σε WebView2 (Chromium <video>), το οποίο ΔΕΝ
        // περιλαμβάνει decoder για τον κλασικό WMV3/VC-1 codec (μόνο H.264/VP8/VP9/AV1/Theora).
        // Αντί για απλή προειδοποίηση, τα .wmv μετατρέπονται πλέον ΑΥΤΟΜΑΤΑ σε .mp4 μέσω FFmpeg
        // (WmvConversionService) πριν προστεθούν στη λίστα — βλ. αναλυτικό σχόλιο εκεί για το
        // γιατί επιλέχθηκε FFmpeg αντί για COM/ActiveX εναλλακτικές.
        private static async Task AddWallpaperVideosAsync(string[] fileNames, Action? onCompleted)
        {
            // Αρχεία .zip/.7z εξάγονται πρώτα — τα βίντεο που βρίσκονται μέσα τους μπαίνουν στην
            // ίδια ροή (ίδιος έλεγχος .wmv->mp4 παρακάτω) σαν να τα είχε επιλέξει ένα-ένα ο
            // χρήστης. Ένα .7z χωρίς εγκατεστημένο 7-Zip στο σύστημα παραλείπεται σιωπηλά εδώ,
            // με προειδοποίηση+σύνδεσμο λήψης μετά την επεξεργασία των υπολοίπων.
            var archiveFiles = fileNames.Where(ArchivePlaylistService.IsArchive).ToArray();
            var plainFiles = fileNames.Except(archiveFiles).ToList();
            bool anySevenZipMissing = false;

            foreach (var archive in archiveFiles)
            {
                if (string.Equals(Path.GetExtension(archive), ".7z", StringComparison.OrdinalIgnoreCase) && !ArchivePlaylistService.IsSevenZipAvailable)
                {
                    anySevenZipMissing = true;
                    continue;
                }
                plainFiles.AddRange(await Task.Run(() => ArchivePlaylistService.ExtractVideos(archive))); // εκτός UI thread — η εξαγωγή μεγάλου αρχείου πάγωνε το παράθυρο
            }

            if (anySevenZipMissing)
            {
var choice = MessageBox.Show(
                    LocalizationManager.T("Dialog.SevenZipMissing"),
                    LocalizationManager.T("Dialog.SevenZipTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice == DialogResult.Yes) ArchivePlaylistService.OpenSevenZipDownloadPage();
            }

            fileNames = plainFiles.ToArray();

            var wmvFiles = fileNames.Where(f => string.Equals(Path.GetExtension(f), ".wmv", StringComparison.OrdinalIgnoreCase)).ToArray();
            var finalPaths = fileNames.Except(wmvFiles).ToList();

            if (wmvFiles.Length > 0)
            {
                if (!WmvConversionService.IsFfmpegAvailable)
                {
WmvConversionService.PromptInstallFfmpeg(wmvFiles.Length);
                }
                else
                {
                    foreach (var wmv in wmvFiles)
                    {
                        string? mp4 = await WmvConversionService.ConvertToMp4Async(wmv);
                        if (mp4 != null) finalPaths.Add(mp4);
                    }
                    if (finalPaths.Count == 0)
                        MessageBox.Show(LocalizationManager.T("Dialog.ConvertFailed"), LocalizationManager.T("Dialog.ConvertFailedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            if (finalPaths.Count > 0)
            {
                var settings = WallpaperSettings.Load();
                settings.AddVideoFiles(finalPaths.ToArray());
                settings.Mode = "Video";
                settings.Save();
                WallpaperHostEngine.Instance.Enable();
                // ΚΡΙΣΙΜΟ: το Enable() από μόνο του ΔΕΝ ζητάει motionDeskRefresh() από ένα ήδη
                // ανοιχτό/ορατό wallpaper window (μόνο BringToFront/Show) — χωρίς αυτό, ένα ήδη
                // τρέχον WebView2 σε λειτουργία Waves δεν μαθαίνει ποτέ ότι το Mode έγινε Video,
                // το .mp4 μετατρέπεται επιτυχώς αλλά δεν παίζει ποτέ.
                _ = WallpaperHostEngine.Instance.RefreshAllAsync();
            }
            onCompleted?.Invoke();
        }

        private static void ChooseWallpaperFolder()
        {
            using var dialog = new FolderBrowserDialog { Description = LocalizationManager.T("Wallpaper.ChooseFolder") };
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
                    using var dlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Icons"), Title = LocalizationManager.T("Personalization.ChooseIcon") };
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        set(dlg.FileName);
                        RefreshPreview();
                        _statusLabel.Text = LocalizationManager.T("Personalization.IconAppliedStatus");
                    }
                };
                row.Controls.Add(chooseBtn);

                var resetBtn = new HoverButton { Text = LocalizationManager.T("Personalization.Reset"), Width = 90, Height = 30, Location = new Point(490, 7), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextSecondary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                resetBtn.FlatAppearance.BorderSize = 0;
                resetBtn.Click += (_, _) => { set(null); RefreshPreview(); _statusLabel.Text = LocalizationManager.T("Personalization.IconResetStatus"); };
                row.Controls.Add(resetBtn);

                return row;
            }

            panel.Controls.Add(BuildIconSlotRow("Personalization.ThisPcIcon", IconAtlasEngine.GetThisPcIcon, IconAtlasEngine.SetThisPcIcon));
            panel.Controls.Add(BuildIconSlotRow("Personalization.RecycleBinEmpty", IconAtlasEngine.GetRecycleBinEmptyIcon, IconAtlasEngine.SetRecycleBinEmptyIcon));
            panel.Controls.Add(BuildIconSlotRow("Personalization.RecycleBinFull", IconAtlasEngine.GetRecycleBinFullIcon, IconAtlasEngine.SetRecycleBinFullIcon));
            // Επέκταση κάλυψης (ζητήθηκε ρητά v1.5.0) — τα υπόλοιπα τυπικά εικονίδια επιφάνειας
            // εργασίας που υποστηρίζουν επίσημα custom icon μέσω registry.
            panel.Controls.Add(BuildIconSlotRow("Personalization.NetworkIcon", IconAtlasEngine.GetNetworkIcon, IconAtlasEngine.SetNetworkIcon));
            panel.Controls.Add(BuildIconSlotRow("Personalization.ControlPanelIcon", IconAtlasEngine.GetControlPanelIcon, IconAtlasEngine.SetControlPanelIcon));
            panel.Controls.Add(BuildIconSlotRow("Personalization.UsersFilesIcon", IconAtlasEngine.GetUsersFilesIcon, IconAtlasEngine.SetUsersFilesIcon));

            // "Εισαγωγή icon pack (depot)…" — σαρώνει έναν φάκελο για έτοιμα ζευγάρια εικονιδίων
            // Κάδου Ανακύκλωσης (σύμβαση "-empty"/"-full", π.χ. sdushantha/recycle-bin-themes στο
            // GitHub) και τα εφαρμόζει αυτόματα, αντί να χρειάζεται να επιλέξει ο χρήστης τα δύο
            // αρχεία ένα-ένα από τις παραπάνω γραμμές.
            AddButtonGrid(panel, (LocalizationManager.T("Personalization.IconPackImport"), (_, _) =>
            {
                using var folderDlg = new FolderBrowserDialog { Description = LocalizationManager.T("Personalization.IconPackFolderPrompt") };
                if (folderDlg.ShowDialog() != DialogResult.OK) return;
                var depot = IconAtlasEngine.ScanDepotFolder(folderDlg.SelectedPath);
                if (depot.EmptyIconPath != null || depot.FullIconPath != null)
                {
                    IconAtlasEngine.ApplyDepotToRecycleBin(depot);
                    _statusLabel.Text = string.Format(LocalizationManager.T("Personalization.IconPackAppliedFormat"),
                        LocalizationManager.T(depot.EmptyIconPath != null ? "Personalization.IconPackEmptyOk" : "Personalization.IconPackEmptyMissing"),
                        LocalizationManager.T(depot.FullIconPath != null ? "Personalization.IconPackFullOk" : "Personalization.IconPackFullMissing"));
                }
                else if (depot.SingleIconPath != null)
                {
                    IconAtlasEngine.SetThisPcIcon(depot.SingleIconPath);
                    _statusLabel.Text = LocalizationManager.T("Personalization.IconPackSingleApplied");
                }
                else
                {
                    MessageBox.Show(this, LocalizationManager.T("Personalization.IconPackNotFound"), LocalizationManager.T("Personalization.IconPackTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                ShowPersonalization();
            }));

            AddButtonGrid(panel, (LocalizationManager.T("Personalization.FolderIcon"), (_, _) =>
            {
                using var folderDlg = new FolderBrowserDialog { Description = LocalizationManager.T("Personalization.FolderIcon") };
                if (folderDlg.ShowDialog() != DialogResult.OK) return;
                using var iconDlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Icons"), Title = LocalizationManager.T("Personalization.ChooseIcon") };
                if (iconDlg.ShowDialog(this) == DialogResult.OK)
                {
                    IconAtlasEngine.SetFolderIcon(folderDlg.SelectedPath, iconDlg.FileName);
                    _statusLabel.Text = LocalizationManager.T("Personalization.FolderIconAppliedStatus");
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
                    using var dlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Cursors"), Title = LocalizationManager.T("Personalization.Browse") };
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
                (LocalizationManager.T("Personalization.ApplyCursors"), (_, _) => { DeskCursorsEngine.ApplyNow(); _statusLabel.Text = LocalizationManager.T("Personalization.CursorsAppliedStatus"); }),
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
                DisposeChildren(pinnedList);
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
                using var dlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Apps"), Title = LocalizationManager.T("Personalization.PinApp") };
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
                    using var dlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Wav"), Title = LocalizationManager.T("Personalization.Browse") };
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
                DisposeChildren(themeList);
                var themes = ThemePackageEngine.ListAvailableThemes();
                if (themes.Count == 0)
                {
                    themeList.Controls.Add(new Label { AutoSize = true, Text = LocalizationManager.T("Personalization.NoThemesFound"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody });
                }
                foreach (var theme in themes)
                {
                    var row = new Panel { Width = 500, Height = 40, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 6) };
                    UiTheme.ApplyRoundedRegion(row, 6);
                    string label = theme.Name + (theme.BuiltIn ? LocalizationManager.T("Personalization.BuiltInSuffix") : "");
                    row.Controls.Add(new Label { Text = label, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, Location = new Point(14, 11), AutoSize = true });
                    var applyBtn = new HoverButton { Text = LocalizationManager.T("Personalization.ApplyThemeBtn"), Width = 90, Height = 28, Location = new Point(394, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    applyBtn.FlatAppearance.BorderSize = 0;
                    string capturedPath = theme.Path;
                    applyBtn.Click += (_, _) => { ThemePackageEngine.ApplyTheme(capturedPath); _statusLabel.Text = LocalizationManager.T("Personalization.ThemeAppliedStatus"); };
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
                var ok = new Button { Text = LocalizationManager.T("Common.OK"), DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 34 };
                dialog.Controls.Add(box);
                dialog.Controls.Add(ok);
                dialog.AcceptButton = ok;
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(box.Text))
                {
                    ThemePackageEngine.SaveCurrentAsTheme(box.Text.Trim());
                    RefreshThemeList();
                    _statusLabel.Text = LocalizationManager.T("Personalization.ThemeSavedStatus");
                }
            }),
            // Έτοιμο, "τεχνολογικό" πακέτο θέματος — ζητήθηκε ρητά ένα νέο .theme με tech-themed
            // wallpaper. Χρησιμοποιεί ΤΟ ΙΔΙΟ οπτικό μοτίβο (circuit-grid) με το νέο ζωντανό
            // στυλ "TechGrid" του Wallpaper Studio, ως στατική εικόνα (απαίτηση του .theme format).
            (LocalizationManager.T("Personalization.CreateTechGridTheme"), (_, _) =>
            {
                string png = ThemePackageEngine.GenerateTechWallpaperPng();
                ThemePackageEngine.SaveCurrentAsTheme("MotionDesk Tech Grid", png);
                RefreshThemeList();
                _statusLabel.Text = LocalizationManager.T("Personalization.TechGridThemeCreatedStatus");
            }));

            // ---------- Lock Screen Background ----------
            AddSection(panel, LocalizationManager.T("Personalization.SectionLockScreen"));
            AddText(panel, LocalizationManager.T("Personalization.LockScreenIntro"));
            AddText(panel, string.Format(LocalizationManager.T("Personalization.LockScreenEditionNote"), LockScreenEngine.GetWindowsEdition()));

            AddButtonGrid(panel, (LocalizationManager.T("Personalization.SetLockScreenImage"), async (_, _) =>
            {
                string fileName;
                using (var dlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Images"), Title = LocalizationManager.T("Personalization.SetLockScreenImage") })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    fileName = dlg.FileName;
                }
                // Εκτός UI thread: το helper περιμένει έως 15s (UAC prompt) — στο UI thread πάγωνε όλο το
                // παράθυρο ΚΑΙ (λόγω του global mouse hook του quick-hide) το ποντίκι όλου του συστήματος.
                string error = string.Empty;
                bool ok = await Task.Run(() => LockScreenEngine.TrySetLockScreenImageElevated(fileName, out error));
                if (IsDisposed) return;
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
                Text = LocalizationManager.T("Command.PaletteTitle"),
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
                PlaceholderText = LocalizationManager.T("Command.PalettePlaceholder"),
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

            // ΔΙΟΡΘΩΣΗ REQ-008: οι εντολές ΗΤΑΝ ταυτόχρονα το εμφανιζόμενο κείμενο ΚΑΙ το κλειδί
            // αντιστοίχισης στο switch — πάντα αγγλικά, ανεξάρτητα από την επιλεγμένη γλώσσα.
            // Τώρα κάθε εντολή έχει ξεχωριστό σταθερό "id" (για το switch) από το μεταφρασμένο
            // label που βλέπει ο χρήστης (για εμφάνιση/αναζήτηση), μέσω ενός dictionary label->id.
            string profileFmt = LocalizationManager.T("Dashboard.ProfileButtonFormat");
            var commandDefs = new (string Id, string Label)[]
            {
                ("Dashboard", LocalizationManager.T("Nav.Dashboard")),
                ("WidgetGallery", LocalizationManager.T("Command.WidgetGallery")),
                ("DeskZones", LocalizationManager.T("Nav.DeskZones")),
                ("WallpaperStudio", LocalizationManager.T("Nav.Wallpaper")),
                ("Profiles", LocalizationManager.T("Nav.Profiles")),
                ("Performance", LocalizationManager.T("Nav.Performance")),
                ("Automation", LocalizationManager.T("Nav.Automation")),
                ("Personalization", LocalizationManager.T("Nav.Personalization")),
                ("Settings", LocalizationManager.T("Nav.Settings")),
                ("About", LocalizationManager.T("Nav.About")),
                ("AudioEnhancement", LocalizationManager.T("Nav.AudioEnhancement")),
                ("EnableWallpaper", LocalizationManager.T("Dashboard.QuickEnableWallpaper")),
                ("DisableWallpaper", LocalizationManager.T("Command.DisableWallpaper")),
                ("WorkProfile", string.Format(profileFmt, "Work")),
                ("GamingProfile", string.Format(profileFmt, "Gaming")),
                ("FocusProfile", string.Format(profileFmt, "Focus")),
            };
            var idByLabel = commandDefs.ToDictionary(c => c.Label, c => c.Id, StringComparer.OrdinalIgnoreCase);
            var allLabels = commandDefs.Select(c => c.Label).ToArray();
            list.Items.AddRange(allLabels);
            if (list.Items.Count > 0) list.SelectedIndex = 0;

            void Execute()
            {
                if (list.SelectedItem is not string label || !idByLabel.TryGetValue(label, out var id)) return;
                switch (id) {
                    case "Dashboard": NavigateTo("Dashboard"); break; case "WidgetGallery": NavigateTo("Widgets"); break; case "DeskZones": NavigateTo("DeskZones"); break;
                    case "WallpaperStudio": NavigateTo("Wallpaper"); break; case "Profiles": NavigateTo("Profiles"); break;
                    case "Performance": NavigateTo("Performance"); break; case "Automation": NavigateTo("Automation"); break;
                    case "Personalization": NavigateTo("Personalization"); break; case "Settings": NavigateTo("Settings"); break;
                    case "About": NavigateTo("About"); break;
                    case "AudioEnhancement": NavigateTo("AudioEnhancement"); break;
                    case "EnableWallpaper": WallpaperHostEngine.Instance.Enable(); break;
                    case "DisableWallpaper": WallpaperHostEngine.Instance.Disable(); break; case "WorkProfile": LoadProfileFromQuickButton("Work"); break;
                    case "GamingProfile": LoadProfileFromQuickButton("Gaming"); break; case "FocusProfile": LoadProfileFromQuickButton("Focus"); break;
                }
                dialog.Close();
            }
            box.TextChanged += (_, _) =>
            {
                list.Items.Clear();
                list.Items.AddRange(allLabels.Where(c => c.Contains(box.Text, StringComparison.OrdinalIgnoreCase)).ToArray());
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
                ("Save", LocalizationManager.T("Profiles.SaveCurrentAsProfile"), (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) { WorkspaceProfileService.Save(name.Text.Trim()); Refresh(); } }),
                ("Restore", LocalizationManager.T("Profiles.LoadSelected"), (_, _) => { if (list.SelectedItem is string p && WorkspaceProfileService.Load(p)) { NavigateTo("Dashboard"); } }),
                ("Add", LocalizationManager.T("Profiles.CreateUpdateDefaults"), (_, _) => { foreach (var p in new[] { "Work", "Gaming", "Focus" }) { if (!WorkspaceProfileService.ListProfiles().Contains(p, StringComparer.OrdinalIgnoreCase)) WorkspaceProfileService.Save(p); } Refresh(); }),
                ("Delete", LocalizationManager.T("Profiles.DeleteSelected"), (_, _) => { if (list.SelectedItem is string p && !p.Equals("Last Session", StringComparison.OrdinalIgnoreCase)) { WorkspaceProfileService.Delete(p); Refresh(); } }));
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
            var cpu = AddMeter(panel, LocalizationManager.T("Performance.CpuLabel"), 0);
            var ram = AddMeter(panel, LocalizationManager.T("Performance.MemoryLabel"), 0);
            // GPU (φόρτος) — ζητήθηκε ρητά "βελτιώσεις για ram cpu gpu usage": το Performance page
            // έδειχνε μόνο CPU/RAM, ενώ το System Monitor widget έχει ήδη GPU load/temp μέσω
            // GpuMonitorService. Ίδιο μοτίβο "ζέσταμα σε background thread" με το widget (βλ.
            // WidgetEngine.InitializeNativeSystemMonitor) ώστε να ΜΗΝ ξαναεισάγουμε το ίδιο
            // UI-thread hitch που διορθώθηκε εκεί.
            var gpu = AddMeter(panel, LocalizationManager.T("Performance.GpuLabel"), 0);
            gpu.SetValue(0, LocalizationManager.T("Performance.GpuLoading"));
            bool gpuReady = false;
            _ = System.Threading.Tasks.Task.Run(() => { GpuMonitorService.Instance.GetSnapshot(); gpuReady = true; });
            var net = new SparklineCard(LocalizationManager.T("Performance.NetworkLabel"), 2, 1024) { Width = 500, Height = 110, Margin = new Padding(0, 0, 0, 10) };
            panel.Controls.Add(net);
            var procs = new SparklineCard(LocalizationManager.T("Performance.ProcessesLabel"), 1, 400) { Width = 500, Height = 110, Margin = new Padding(0, 0, 0, 10) };
            panel.Controls.Add(procs);
            var powerCard = AddCard(panel, LocalizationManager.T("Performance.PowerLabel"), "");

            var timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += (_, _) => {
                var m = AdvancedSystemMonitorService.Instance.GetSnapshot();
                cpu.SetValue(m.CpuPercent, $"{m.CpuPercent:0.0}%");
                double ramPercent = m.TotalMemoryMb > 0 ? (m.TotalMemoryMb - m.AvailableMemoryMb) / m.TotalMemoryMb * 100.0 : 0;
                ram.SetValue(ramPercent, string.Format(LocalizationManager.T("Performance.MemoryValueFormat"), $"{m.AvailableMemoryMb:0}", $"{m.TotalMemoryMb:0}", RamUsedPercent(m.AvailableMemoryMb, m.TotalMemoryMb)));
                if (gpuReady)
                {
                    var g = GpuMonitorService.Instance.GetSnapshot();
                    gpu.SetValue(g.Available ? g.LoadPercent ?? 0 : 0,
                        g.Available ? $"{g.LoadPercent:0.0}%" + (g.TemperatureC.HasValue ? $"   {g.TemperatureC:0}°C" : "") : LocalizationManager.T("Performance.GpuUnavailable"));
                }
                net.Push(m.NetworkDownKbps, m.NetworkUpKbps, string.Format(LocalizationManager.T("Performance.NetworkValueFormat"), $"{m.NetworkDownKbps:0.0}", $"{m.NetworkUpKbps:0.0}"));
                procs.Push(m.ProcessCount, null, string.Format(LocalizationManager.T("Performance.ProcessesValueFormat"), m.ProcessCount));
                powerCard.Text = string.Format(LocalizationManager.T("Performance.RecommendedFormat"), m.BatteryMode);
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
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
                var check = new CheckBox { Text = $"{rule.Name}  ·  {rule.TriggerProcess}  →  {rule.Profile}", Checked = rule.Enabled, AutoSize = true, ForeColor = UiTheme.TextPrimary, Padding = new Padding(0, 4, 0, 0) };
                check.CheckedChanged += (_, _) => { rule.Enabled = check.Checked; AutomationService.Instance.Save(); };
                row.Controls.Add(check);

                var editBtn = new HoverButton { Text = LocalizationManager.T("Common.Edit"), Width = 70, Height = 26, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand, Margin = new Padding(10, 0, 0, 0) };
                editBtn.FlatAppearance.BorderSize = 0;
                editBtn.Click += (_, _) => { if (PromptAutomationRule(rule)) { AutomationService.Instance.Save(); ShowAutomation(); } };
                row.Controls.Add(editBtn);

                var deleteBtn = new HoverButton { Text = LocalizationManager.T("Common.Delete"), Width = 70, Height = 26, FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(231, 76, 60), BackColor = UiTheme.Surface, Cursor = Cursors.Hand, Margin = new Padding(6, 0, 0, 0) };
                deleteBtn.FlatAppearance.BorderSize = 0;
                deleteBtn.Click += (_, _) => { AutomationService.Instance.Rules.Remove(rule); AutomationService.Instance.Save(); ShowAutomation(); };
                row.Controls.Add(deleteBtn);

                panel.Controls.Add(row);
            }

            var addRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            addRow.Controls.Add(NewHoverButton(LocalizationManager.T("Automation.AddRule"), () =>
            {
                var newRule = new AutomationRule { Name = LocalizationManager.T("Automation.NewRuleDefaultName"), TriggerProcess = "", Profile = "" };
                if (PromptAutomationRule(newRule)) { AutomationService.Instance.Rules.Add(newRule); AutomationService.Instance.Save(); ShowAutomation(); }
            }));
            addRow.Controls.Add(NewHoverButton(LocalizationManager.T("Automation.AddGamingRule"), () =>
            {
                AutomationService.Instance.Rules.Add(new AutomationRule { Name = LocalizationManager.T("Automation.GamingRuleName"), TriggerProcess = "steam.exe", Profile = "Gaming" });
                AutomationService.Instance.Save();
                ShowAutomation();
            }));
            panel.Controls.Add(addRow);

            SetPage("Page.Automation.Title", panel);
        }

        private static HoverButton NewHoverButton(string text, Action onClick)
        {
            var btn = new HoverButton { Text = text, Width = 140, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (_, _) => onClick();
            return btn;
        }

        // Ζητήθηκε ρητά "εκτός από Add Gaming role να μπορώ να τον παραμετροποιώ & διαγράφω" —
        // ίδιο πρότυπο διαλόγου με το RenameContainer/PromptWeatherLocation, χρησιμοποιείται και
        // για Add (νέος, άδειος κανόνας) και για Edit (υπάρχων κανόνας) στο ίδιο dialog.
        private bool PromptAutomationRule(AutomationRule rule)
        {
            using var dialog = new Form
            {
                Text = LocalizationManager.T("Automation.RuleDialogTitle"),
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(420, 230),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                BackColor = UiTheme.Surface
            };
            var nameLabel = new Label { Text = LocalizationManager.T("Common.NameLabel"), Location = new Point(14, 14), AutoSize = true, ForeColor = UiTheme.TextPrimary };
            var nameBox = new TextBox { Location = new Point(14, 34), Width = 380, Text = rule.Name };
            var procLabel = new Label { Text = LocalizationManager.T("Automation.TriggerProcessLabel"), Location = new Point(14, 66), AutoSize = true, ForeColor = UiTheme.TextPrimary };
            var procBox = new TextBox { Location = new Point(14, 86), Width = 380, Text = rule.TriggerProcess };
            var profileLabel = new Label { Text = LocalizationManager.T("Automation.ProfileToLoadLabel"), Location = new Point(14, 118), AutoSize = true, ForeColor = UiTheme.TextPrimary };
            var profileBox = new TextBox { Location = new Point(14, 138), Width = 380, Text = rule.Profile };
            var okBtn = new Button { Text = LocalizationManager.T("Common.OK"), Location = new Point(228, 170), DialogResult = DialogResult.OK };
            var cancelBtn = new Button { Text = LocalizationManager.T("Common.Cancel"), Location = new Point(316, 170), DialogResult = DialogResult.Cancel };
            dialog.Controls.Add(nameLabel); dialog.Controls.Add(nameBox);
            dialog.Controls.Add(procLabel); dialog.Controls.Add(procBox);
            dialog.Controls.Add(profileLabel); dialog.Controls.Add(profileBox);
            dialog.Controls.Add(okBtn); dialog.Controls.Add(cancelBtn);
            dialog.AcceptButton = okBtn; dialog.CancelButton = cancelBtn;

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(nameBox.Text)) return false;
            rule.Name = nameBox.Text.Trim();
            rule.TriggerProcess = procBox.Text.Trim();
            rule.Profile = profileBox.Text.Trim();
            return true;
        }

        private bool _gamingModeActive;
        private void ToggleGamingMode() => LoadProfileFromQuickButton(_gamingModeActive ? "Work" : "Gaming", true);
        private void LoadProfileFromQuickButton(string profile, bool toggle = false)
        {
            if (WorkspaceProfileService.Load(profile)) { if (toggle) _gamingModeActive = !_gamingModeActive; _statusLabel.Text = string.Format(LocalizationManager.T("Automation.ProfileLoadedStatus"), profile); }
            else _statusLabel.Text = string.Format(LocalizationManager.T("Automation.ProfileNotFoundStatus"), profile);
        }

        // Ιστορικό εκδόσεων — απλή, in-code λίστα· ανοίγει σε ξεχωριστό (δευτερεύον) παράθυρο.
        // Οι σημειώσεις κάθε έκδοσης ζουν στα locale αρχεία (κλειδί "VersionNotes.<έκδοση>") ώστε να
        // ακολουθούν την επιλεγμένη γλώσσα — πριν ήταν hardcoded και μισές ελληνικά / μισές αγγλικά.
        private static readonly (string Version, string Date)[] VersionHistory =
        {
            ("1.6.8", "2026-10"), ("1.6.2", "2026-10"), ("1.6.1", "2026-10"), ("1.6.0", "2026-09"), ("1.5.0", "2026-09"), ("1.4.1", "2026-09"), ("1.4.0", "2026-09"), ("1.3.0", "2026-09"), ("1.2.9", "2026-09"), ("1.2.8", "2026-09"), ("1.2.7", "2026-09"), ("1.2.6", "2026-09"), ("1.2.5", "2026-09"), ("1.2.4", "2026-09"), ("1.2.3", "2026-09"), ("1.2.2", "2026-09"), ("1.2.1", "2026-09"), ("1.2.0", "2026-09"), ("1.1.2", "2026-09"), ("1.1.1", "2026-09"), ("1.1.0", "2026-09"), ("1.0.0", "2026-09")
        };


        // "Audio Enhancement" — ζητήθηκε ρητά, εμπνευσμένο από τη ΛΟΓΙΚΗ του FXSound (github.com/
        // fxsound2/fxsound-app), όχι αντιγραφή του κώδικά του. Έρευνα στο ίδιο το repo (WebFetch)
        // επιβεβαίωσε ότι ΑΚΟΜΑ ΚΙ ΕΚΕΙ η πραγματική system-wide επεξεργασία γίνεται μέσω
        // ξεχωριστού, ΚΛΕΙΣΤΟΥ virtual audio driver — δεν είναι καν μέρος του δικού τους open-
        // source κώδικα, χρειάζεται kernel-mode driver signing (πέρα από απλό code-signing cert).
        // Η ΠΡΑΓΜΑΤΙΚΗ, εφικτή λύση βρέθηκε ερευνώντας παρόμοια GitHub projects (π.χ.
        // github.com/psidex/EACS): το Equalizer APO (equalizerapo.sourceforge.io) είναι ένα ήδη
        // δωρεάν, ανοιχτού κώδικα, ήδη-υπογεγραμμένο, system-wide Windows Audio Processing Object
        // — ελέγχεται προγραμματιστικά γράφοντας το δικό του config.txt (EqualizerApoService, ίδιο
        // μοτίβο ενσωμάτωσης εξωτερικού εργαλείου με FFmpeg/7-Zip σε αυτό το project). Όταν είναι
        // εγκατεστημένο, τα presets ΕΔΩ αλλάζουν πραγματικά τον ήχο ΟΛΩΝ των εφαρμογών· χωρίς αυτό,
        // εξακολουθούν έστω να ενισχύουν πραγματικά τον live visualizer (WASAPI loopback).
        private AudioSpectrumService? _enhancementSpectrum;
        private void ShowAudioEnhancement()
        {
            _currentPageKey = "AudioEnhancement";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("AudioEnhancement.Intro"));

            // ΔΙΟΡΘΩΣΗ πραγματικού bug: Dock=Fill (κείμενο) + Dock=Right (κουμπί) στον ίδιο γονέα
            // δεν μείωνε σωστά το πλάτος του Fill label — το κουμπί απλά ζωγραφιζόταν ΠΑΝΩ από το
            // κείμενο (BringToFront), όχι δίπλα του, αφού το layout δεν αφαιρούσε ποτέ πραγματικά
            // τον χώρο του κουμπιού. Λύση: κάθετη στοίβα (FlowLayoutPanel, TopDown) — κείμενο πάνω,
            // κουμπί από κάτω, ΠΟΤΕ στο ίδιο ύψος, άρα αδύνατο να επικαλυφθούν.
            bool apoInstalled = EqualizerApoService.IsInstalled;
            var noteCard = new Panel { Width = 720, BackColor = UiTheme.Surface, Padding = new Padding(14, 12, 14, 12), Margin = new Padding(0, 0, 0, 10) };
            UiTheme.ApplyRoundedRegion(noteCard, 8);
            var noteStack = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent };
            var statusLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(690, 0),
                Text = apoInstalled
                    ? "✓ " + LocalizationManager.T("AudioEnhancement.ApoFound")
                    : LocalizationManager.T("AudioEnhancement.ApoMissing"),
                ForeColor = apoInstalled ? Color.FromArgb(60, 210, 140) : UiTheme.TextSecondary,
                Font = UiTheme.FontBody
            };
            noteStack.Controls.Add(statusLabel);
            if (!apoInstalled)
            {
                var installBtn = new HoverButton { Text = LocalizationManager.T("AudioEnhancement.ApoInstallBtn"), Width = 190, Height = 32, Margin = new Padding(0, 8, 0, 0), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.AccentBlue, BaseColor = UiTheme.AccentBlue, HoverBackColor = ControlPaint.Light(UiTheme.AccentBlue, 0.15f), Cursor = Cursors.Hand };
                installBtn.FlatAppearance.BorderSize = 0;
                installBtn.Click += (_, _) => EqualizerApoService.OpenDownloadPage();
                noteStack.Controls.Add(installBtn);
            }
            noteCard.Controls.Add(noteStack);
            noteCard.Height = noteStack.PreferredSize.Height + noteCard.Padding.Vertical;
            panel.Controls.Add(noteCard);

            AddSection(panel, LocalizationManager.T("AudioEnhancement.SectionPresets"));
            var settings = AppSettings.Load();
            Dictionary<string, HoverButton>? presetButtons = null;
            var presetItems = AudioSpectrumService.Presets.Select(p =>
                (p.Name, (EventHandler)((_, _) =>
                {
                    var a = AppSettings.Load(); a.AudioEnhancementPreset = p.Name; a.Save();
                    _enhancementSpectrum?.ApplyPreset(p.Name);
                    MarkActive(presetButtons!, p.Name);
                    if (apoInstalled)
                    {
                        var (ok, msg) = EqualizerApoService.ApplyPreset(p.Name);
                        _statusLabel.Text = ok ? string.Format(LocalizationManager.T("Status.AudioPresetSystemWideFormat"), p.Name) : $"Equalizer APO: {msg}";
                    }
                }))).ToArray();
            presetButtons = AddToggleButtonGrid(panel, presetItems);
            MarkActive(presetButtons, settings.AudioEnhancementPreset);
            AddText(panel, LocalizationManager.T("AudioEnhancement.PresetsNote"));

            AddSection(panel, LocalizationManager.T("AudioEnhancement.SectionVisualizer"));
            var equalizer = new EqualizerControl { Width = 700, Height = 200, Style = "WMP", Margin = new Padding(0, 0, 0, 8) };
            panel.Controls.Add(equalizer);

            var styleRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            var wmpBtn = new HoverButton { Text = "WMP Legacy", Width = 130, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
            var winampBtn = new HoverButton { Text = "Winamp", Width = 130, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand };
            wmpBtn.FlatAppearance.BorderSize = 0; winampBtn.FlatAppearance.BorderSize = 0;
            wmpBtn.Click += (_, _) => { equalizer.Style = "WMP"; wmpBtn.BackColor = wmpBtn.BaseColor = UiTheme.SurfaceHover; winampBtn.BackColor = winampBtn.BaseColor = UiTheme.Surface; };
            winampBtn.Click += (_, _) => { equalizer.Style = "Winamp"; winampBtn.BackColor = winampBtn.BaseColor = UiTheme.SurfaceHover; wmpBtn.BackColor = wmpBtn.BaseColor = UiTheme.Surface; };
            styleRow.Controls.Add(wmpBtn);
            styleRow.Controls.Add(winampBtn);
            panel.Controls.Add(styleRow);

            _enhancementSpectrum?.Dispose();
            var spectrum = new AudioSpectrumService(20);
            _enhancementSpectrum = spectrum;
            spectrum.ApplyPreset(settings.AudioEnhancementPreset);
            var timer = new System.Windows.Forms.Timer { Interval = 40 };
            // Το closure πιάνει το δικό του "spectrum" local (όχι το κοινόχρηστο πεδίο
            // _enhancementSpectrum) ώστε ΚΑΘΕ επίσκεψη στη σελίδα να έχει το δικό της, απομονωμένο
            // instance — ακόμη κι αν ο Timer μιας παλιότερης επίσκεψης καθυστερήσει να σταματήσει.
            timer.Tick += (_, _) => { if (!equalizer.IsDisposed) equalizer.PushBands(spectrum.GetBands()); };
            timer.Start();
            panel.Disposed += (_, _) =>
            {
                timer.Stop(); timer.Dispose(); spectrum.Dispose();
                if (ReferenceEquals(_enhancementSpectrum, spectrum)) _enhancementSpectrum = null;
            };

            SetPage("AudioEnhancement.Title", panel);
        }

        private void ShowAbout()
        {
            _currentPageKey = "About";
            var panel = CreatePagePanel();
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            AddText(panel, string.Format(LocalizationManager.T("About.VersionLine"), version?.ToString(3) ?? "1.0.0"));
            AddText(panel, LocalizationManager.T("About.Body"));

            // Κάρτες ανά θέμα (πρώην ξεχωριστή σελίδα "Βοήθεια") — ζητήθηκε ρητά να ενσωματωθεί
            // εδώ αφού είχαν ίδιες πληροφορίες με τη σελίδα Σχετικά (About.InstructionsList/
            // ShortcutsList παρακάτω ήδη καλύπτουν τα ίδια, αναλυτικά, με βήματα).
            Panel BuildTopicCard(string titleKey, string bodyKey)
            {
                string body = LocalizationManager.T(bodyKey);
                var font = UiTheme.FontBody;
                var bodySize = TextRenderer.MeasureText(body, font, new Size(650, int.MaxValue), TextFormatFlags.WordBreak);
                var card = new Panel { Width = 700, Height = 34 + bodySize.Height + 20, BackColor = UiTheme.Surface, Padding = new Padding(16, 12, 16, 12), Margin = new Padding(0, 0, 0, 10) };
                UiTheme.ApplyRoundedRegion(card, 8);
                card.Controls.Add(new Label { Text = LocalizationManager.T(titleKey), Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = UiTheme.AccentCyan, AutoSize = true, Location = new Point(16, 10) });
                card.Controls.Add(new Label { Text = body, Font = font, ForeColor = UiTheme.TextSecondary, MaximumSize = new Size(650, 0), AutoSize = true, Location = new Point(16, 34) });
                return card;
            }
            panel.Controls.Add(BuildTopicCard("Nav.Widgets", "Widgets.Intro"));
            panel.Controls.Add(BuildTopicCard("Nav.DeskZones", "DeskZones.Intro"));
            panel.Controls.Add(BuildTopicCard("DeskZones.SectionContainers", "DeskZones.ContainersIntro"));
            panel.Controls.Add(BuildTopicCard("Nav.Wallpaper", "Wallpaper.Intro"));
            panel.Controls.Add(BuildTopicCard("Nav.Personalization", "Personalization.Intro"));
            panel.Controls.Add(BuildTopicCard("Nav.Automation", "Automation.Intro"));

            AddSection(panel, LocalizationManager.T("About.SectionCapabilities"));
            AddText(panel, LocalizationManager.T("About.CapabilitiesList"));
            AddText(panel, string.Format(LocalizationManager.T("About.RuntimeLine"), WidgetHostEngine.Instance.GetActiveWidgets().Count));
            AddButton(panel, LocalizationManager.T("VersionHistory.Button"), (_, _) => ShowVersionHistory());

            AddSection(panel, LocalizationManager.T("About.SectionInstructions"));
            AddText(panel, LocalizationManager.T("About.InstructionsList"));

            AddSection(panel, LocalizationManager.T("About.SectionShortcuts"));
            AddText(panel, LocalizationManager.T("About.ShortcutsList"));

            AddSection(panel, LocalizationManager.T("About.SectionLicense"));
            AddText(panel, LocalizationManager.T("About.LicenseSummary"));
            // Ζητήθηκε ρητά "το κουμπί της άδειας χρήσης να εμφανίζεται όπως στο GearWin" — χωρίς
            // ακριβές screenshot αναφοράς του GearWin εδώ, τουλάχιστον διαφοροποιείται οπτικά από
            // τα γενικά full-width κουμπιά ενεργειών της σελίδας (accent-χρωματισμένο "pill", auto-
            // sized) αντί να μοιάζει με απλό κουμπί ενέργειας. Αν έχεις screenshot του πραγματικού
            // GearWin κουμπιού, πες μου να το ταιριάξω ακριβώς.
            // ΧΩΡΙΣ εικονίδιο emoji (📄): η γραμματοσειρά UI (Segoe UI) δεν έχει έγχρωμο glyph για
            // αυτό, οπότε αποδιδόταν σαν απλό μονόχρωμο περίγραμμα εγγράφου — αυτό ήταν το
            // "μοιάζει με εικονίδιο html, δεν φαίνεται καλά" που ανέφερε ο χρήστης.
            var licenseBtn = new HoverButton
            {
                Text = LocalizationManager.T("About.LicenseButton"),
                AutoSize = true,
                Padding = new Padding(18, 8, 18, 8),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = UiTheme.AccentBlue,
                BaseColor = UiTheme.AccentBlue,
                HoverBackColor = ControlPaint.Light(UiTheme.AccentBlue, 0.15f),
                Margin = new Padding(0, 4, 0, 3),
                TabStop = false,
                Cursor = Cursors.Hand,
                Font = new Font(UiTheme.FontBody.FontFamily, 9.5f, FontStyle.Bold)
            };
            licenseBtn.FlatAppearance.BorderSize = 0;
            // ΟΧΙ χειροκίνητο UiTheme.ApplyRoundedRegion εδώ: το HoverButton.OnSizeChanged το
            // κάνει ήδη αυτόματα με το ΤΕΛΙΚΟ μέγεθος μετά το AutoSize.
            // ΔΙΟΡΘΩΣΗ πραγματικού bug: το αρχείο LICENSE δεν έχει επέκταση, οπότε το
            // Process.Start(UseShellExecute=true) δεν έχει ΚΑΝΕΝΑΝ προεπιλεγμένο handler να
            // ανοίξει — τα Windows εμφανίζουν το δικό τους παράθυρο "Επιλέξτε μια εφαρμογή" αντί
            // για το αναμενόμενο popup με το κείμενο της άδειας (ακριβώς αυτό ανέφερε ο χρήστης
            // ως διαφορά από το GearWin). Το GearWin δείχνει το ΚΕΙΜΕΝΟ απευθείας σε δικό του
            // παράθυρο· τώρα κάνει το ίδιο εδώ, διαβάζοντας το αρχείο ο ίδιος αντί να το ανοίγει
            // με εξωτερικό πρόγραμμα.
            licenseBtn.Click += (_, _) => ShowLicenseDialog();
            panel.Controls.Add(licenseBtn);

            SetPage("Page.About.Title", panel);
        }

        private void ShowLicenseDialog()
        {
            var licensePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSE");
            string text;
            try { text = File.Exists(licensePath) ? File.ReadAllText(licensePath) : "LICENSE file not found."; }
            catch (IOException) { text = "Could not read the LICENSE file."; }

            using var dialog = new Form
            {
                Text = LocalizationManager.T("About.LicenseButton"),
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(620, 560),
                BackColor = UiTheme.Background,
                ForeColor = UiTheme.TextPrimary,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            dialog.HandleCreated += (_, _) => { int d = UiTheme.Background.GetBrightness() < 0.5f ? 1 : 0; try { DwmSetWindowAttribute(dialog.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int)); } catch (DllNotFoundException) { } };

            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9.5f),
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                BorderStyle = BorderStyle.None,
                Text = text.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n")
            };
            var closeBtn = new Button { Text = "OK", Dock = DockStyle.Bottom, Height = 36, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary };
            closeBtn.FlatAppearance.BorderSize = 0;

            dialog.Controls.Add(textBox);
            dialog.Controls.Add(closeBtn);
            dialog.AcceptButton = closeBtn;
            dialog.CancelButton = closeBtn;
            dialog.ShowDialog(this);
        }

        private void ShowVersionHistory()
        {
            using var dialog = new Form
            {
                Text = LocalizationManager.T("VersionHistory.Title"),
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
                string notes = LocalizationManager.T("VersionNotes." + entry.Version);
                var notesSize = TextRenderer.MeasureText(notes, notesFont, new Size(470, int.MaxValue), TextFormatFlags.WordBreak);
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

                string header = $"v{entry.Version}   •   {entry.Date}" + (isLatest ? "   •   " + LocalizationManager.T("VersionHistory.Current") : "");
                card.Controls.Add(new Label { Text = header, Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = UiTheme.AccentCyan, AutoSize = true, Location = new Point(14, 10) });
                card.Controls.Add(new Label { Text = notes, Font = notesFont, ForeColor = UiTheme.TextSecondary, MaximumSize = new Size(470, 0), AutoSize = true, Location = new Point(14, 34) });
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
            // ΔΙΟΡΘΩΣΗ πραγματικού bug: τα Mouse Enter/Leave έκαναν πάντα hardcoded reset σε
            // UiTheme.Surface/SurfaceHover — οποιοδήποτε κουμπί με ΔΙΑΦΟΡΕΤΙΚΟ, σκόπιμο BackColor
            // (π.χ. accent-χρωματισμένο CTA) γύριζε αμέσως πίσω σε γκρι με το πρώτο hover, χωρίς
            // κανένα τρόπο να παραμείνει το επιθυμητό χρώμα. Αυτό ήταν ακριβώς γιατί το κουμπί
            // "Άδεια χρήσης" φαινόταν "ξεθωριασμένο" γκρι αντί για μπλε — αρκούσε ένα πέρασμα του
            // δείκτη από πάνω του. Τώρα το βασικό/hover χρώμα είναι ρυθμιζόμενο ανά instance, με
            // προεπιλογή Surface/SurfaceHover (ίδια συμπεριφορά με πριν για όλα τα υπόλοιπα σημεία
            // κλήσης που δεν τα αλλάζουν ρητά).
            public Color BaseColor { get; set; } = UiTheme.Surface;
            public Color HoverBackColor { get; set; } = UiTheme.SurfaceHover;

            public HoverButton()
            {
                MouseEnter += (_, _) => BackColor = HoverBackColor;
                MouseLeave += (_, _) => BackColor = BaseColor;
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
            _statusLabel.Text = string.Format(LocalizationManager.T("Status.SystemFormat"), m.CpuPercent.ToString("0.0"), m.AvailableMemoryMb.ToString("0"), RamUsedPercent(m.AvailableMemoryMb, m.TotalMemoryMb));
        }

        // Ποσοστό χρήσης RAM (χρησιμοποιούμενη/συνολική) — εμφανίζεται δίπλα στα MB στην Αρχική, στη γραμμή κατάστασης, στο Performance και στο widget.
        private static string RamUsedPercent(double availableMb, double totalMb) =>
            totalMb > 0 ? Math.Clamp((totalMb - availableMb) / totalMb * 100.0, 0, 100).ToString("0") : "0";

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

                AddText(stack, LocalizationManager.T("Settings.Intro"));

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
                    v => { var a = AppSettings.Load(); a.EnableAnimations = v; a.Save(); AnimationsSettingChanged?.Invoke(); }));
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.MinimizeToTray"), AppSettings.Load().MinimizeToTray,
                    v => { var a = AppSettings.Load(); a.MinimizeToTray = v; a.Save(); }));

                // Ζητήθηκε ρητά: ανεξάρτητο θέμα widgets/DeskContainers από αυτό της εφαρμογής,
                // ενιαίο προεπιλεγμένο μέγεθος νέων widgets, και επιλέξιμος φάκελος αποθήκευσης
                // για τα μετατρεπόμενα .wmv->.mp4.
                AddSection(stack, LocalizationManager.T("Settings.SectionWidgetsContainers"));
                stack.Controls.Add(BuildWidgetsThemeRow());
                var sizeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                sizeRow.Controls.Add(NumericRow(LocalizationManager.T("Settings.WidgetDefaultWidthLabel"), AppSettings.Load().WidgetDefaultWidth, 160, 800,
                    v => { var a = AppSettings.Load(); a.WidgetDefaultWidth = v; a.Save(); }));
                sizeRow.Controls.Add(NumericRow(LocalizationManager.T("Settings.HeightLabel"), AppSettings.Load().WidgetDefaultHeight, 100, 800,
                    v => { var a = AppSettings.Load(); a.WidgetDefaultHeight = v; a.Save(); }, marginLeft: 24));
                stack.Controls.Add(sizeRow);
                stack.Controls.Add(BuildWmvOutputRow());

                // Ζητήθηκε ρητά: ο installer εγκαθιστά ήδη το WebView2 Runtime/.NET 8 Runtime μία
                // φορά, αλλά ο χρήστης πρέπει να μπορεί να δει/εγκαταστήσει ξανά τα εξωτερικά
                // dependencies (π.χ. FFmpeg για .wmv) και από μέσα από την εφαρμογή, χωρίς να
                // ξανατρέξει τον installer.
                AddSection(stack, LocalizationManager.T("Settings.SectionDependencies"));
                foreach (var dep in DependencyManagerService.All)
                    stack.Controls.Add(BuildDependencyRow(dep));
            }

            private static Panel BuildDependencyRow(DependencyInfo dep)
            {
                var row = new Panel { Width = 700, Height = 56, BackColor = UiTheme.Surface, Padding = new Padding(14, 8, 14, 8), Margin = new Padding(0, 0, 0, 8) };
                UiTheme.ApplyRoundedRegion(row, 10);

                bool installed = dep.IsInstalled();
                var nameLabel = new Label { Text = dep.Name, AutoSize = true, Location = new Point(0, 2), Font = new Font(UiTheme.FontBody.FontFamily, 10, FontStyle.Bold), ForeColor = UiTheme.TextPrimary };
                var descLabel = new Label { Text = dep.Description, AutoSize = true, Location = new Point(0, 22), Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary };
                var statusLabel = new Label
                {
                    Text = LocalizationManager.T(installed ? "Settings.DependencyInstalled" : "Settings.DependencyMissing"),
                    AutoSize = true,
                    Location = new Point(420, 8),
                    Font = new Font(UiTheme.FontBody.FontFamily, 9, FontStyle.Bold),
                    ForeColor = installed ? Color.FromArgb(60, 210, 140) : Color.FromArgb(235, 120, 66)
                };
                row.Controls.Add(nameLabel);
                row.Controls.Add(descLabel);
                row.Controls.Add(statusLabel);

                if (dep.WingetId != null)
                {
                    var installBtn = new HoverButton { Text = LocalizationManager.T(installed ? "Settings.DependencyReinstall" : "Settings.DependencyInstall"), Width = 140, Height = 30, Location = new Point(540, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    installBtn.FlatAppearance.BorderSize = 0;
                    installBtn.Click += async (_, _) =>
                    {
                        installBtn.Enabled = false;
                        statusLabel.Text = LocalizationManager.T("Settings.DependencyInstalling");
                        statusLabel.ForeColor = UiTheme.TextMuted;
                        var (success, output) = await DependencyManagerService.InstallViaWingetAsync(dep.WingetId);
                        bool nowInstalled = dep.IsInstalled();
                        statusLabel.Text = LocalizationManager.T(nowInstalled ? "Settings.DependencyInstalled" : "Settings.DependencyMissing");
                        statusLabel.ForeColor = nowInstalled ? Color.FromArgb(60, 210, 140) : Color.FromArgb(235, 120, 66);
                        installBtn.Text = LocalizationManager.T(nowInstalled ? "Settings.DependencyReinstall" : "Settings.DependencyInstall");
                        installBtn.Enabled = true;
                        if (!success && !nowInstalled)
                            MessageBox.Show(string.Format(LocalizationManager.T("Settings.DependencyInstallFailedFormat"), dep.Name, output), "MotionDesk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    };
                    row.Controls.Add(installBtn);
                }
                return row;
            }

            private static Panel BuildWidgetsThemeRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.WidgetsThemeLabel"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                var combo = new FlatComboBox { Width = 260 };
                string[] modes = { "App", "Windows", "Dark", "Light" };
                string[] labels = { LocalizationManager.T("Settings.WidgetsThemeFollowApp"), LocalizationManager.T("Settings.WidgetsThemeFollowWindows"), LocalizationManager.T("Settings.WidgetsThemeAlwaysDark"), LocalizationManager.T("Settings.WidgetsThemeAlwaysLight") };
                var current = AppSettings.Load().WidgetsThemeMode;
                int idx = Array.IndexOf(modes, current); if (idx < 0) idx = 0;
                combo.SetItems(labels, labels[idx]);
                combo.SelectedIndexChanged += (_, _) =>
                {
                    var a = AppSettings.Load(); a.WidgetsThemeMode = modes[Math.Max(0, combo.SelectedIndex)]; a.Save();
                    ThemeManager.NotifyChanged();
                };
                row.Controls.Add(combo);
                return row;
            }

            private static Panel BuildWmvOutputRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.WmvOutputLabel"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                var pathLabel = new Label { Text = WmvConversionService.CacheDir, AutoSize = false, Width = 380, Height = 24, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, AutoEllipsis = true, Padding = new Padding(0, 4, 0, 0) };
                row.Controls.Add(pathLabel);
                var changeBtn = new HoverButton { Text = LocalizationManager.T("Settings.ChangeFolder"), Width = 100, Height = 28, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand };
                changeBtn.FlatAppearance.BorderSize = 0;
                changeBtn.Click += (_, _) =>
                {
                    using var dlg = new FolderBrowserDialog { Description = LocalizationManager.T("Settings.WmvOutputFolderPrompt"), SelectedPath = WmvConversionService.CacheDir };
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        var a = AppSettings.Load(); a.WmvConversionOutputDir = dlg.SelectedPath; a.Save();
                        pathLabel.Text = WmvConversionService.CacheDir;
                    }
                };
                row.Controls.Add(changeBtn);
                return row;
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
                // ΔΙΟΡΘΩΣΗ πραγματικού bug ("θέλω σημαίες"): τα unicode emoji σημαίες (🇬🇷/🇬🇧) μέσα
                // στο κείμενο αποδίδονταν ως απλά γράμματα περιφερειακού δείκτη σε πλαισιάκι
                // ("GR"/"GB"), όχι ως έγχρωμες σημαίες — ούτε το GDI (ToolStripMenuItem text) ούτε
                // το GDI+ (Graphics.DrawString) υποστηρίζουν πραγματικά έγχρωμες (COLR/CPAL) emoji
                // γραμματοσειρές σε WinForms, ανεξάρτητα από τη γραμματοσειρά. Τώρα πραγματικά,
                // ζωγραφισμένα bitmap (βλ. FlagIcons.cs) μέσω του νέου Image-aware SetItems.
                var combo = new FlatComboBox { Width = 220 };
                var labels = new[]
                {
                    LocalizationManager.T("Settings.LanguageFollow"),
                    LocalizationManager.T("Settings.LanguageGreek"),
                    LocalizationManager.T("Settings.LanguageEnglish")
                };
                var icons = new Image[] { FlagIcons.Globe(), FlagIcons.Greece(), FlagIcons.UnitedKingdom() };
                var currentLang = AppSettings.Load().Language;
                combo.SetItems(labels, icons, labels[currentLang switch { "el" => 1, "en" => 2, _ => 0 }]);
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
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.WindowOpacityLabel"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                int initial = Math.Clamp((int)Math.Round(AppSettings.Load().WindowOpacity * 100), 60, 100);
                var track = new TrackBar { Minimum = 60, Maximum = 100, Value = initial, Width = 200, TickFrequency = 10, SmallChange = 1, LargeChange = 5 };
                var valueLabel = new Label { Text = $"{initial}%", AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Padding = new Padding(8, 6, 0, 0) };
                track.Scroll += (_, _) =>
                {
                    valueLabel.Text = $"{track.Value}%";
                    // Ζωντανή προεπισκόπηση μόνο — η αποθήκευση γίνεται στο άφημα (πριν: εγγραφή αρχείου σε κάθε tick).
                    if (FindForm() is { } form) form.Opacity = track.Value / 100.0;
                };
                void SaveOpacity() { var a = AppSettings.Load(); a.WindowOpacity = track.Value / 100.0; a.Save(); }
                track.MouseUp += (_, _) => SaveOpacity();
                track.KeyUp += (_, _) => SaveOpacity();
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
