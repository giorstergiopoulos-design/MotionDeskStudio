using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    public class WidgetBridge
    {
        private readonly WidgetWindow _window;

        public WidgetBridge(WidgetWindow window)
        {
            _window = window;
        }

        public float GetCpuUsage() => (float)SystemMonitorService.Instance.GetSnapshot().CpuPercent;
        public float GetAvailableRam() => (float)SystemMonitorService.Instance.GetSnapshot().AvailableMemoryMb;

        public string GetSystemStatsJson()
        {
            var metrics = SystemMonitorService.Instance.GetSnapshot();
            return JsonSerializer.Serialize(new
            {
                cpu = metrics.CpuPercent,
                ramAvailableMb = metrics.AvailableMemoryMb,
                ramTotalMb = metrics.TotalMemoryMb,
                timestamp = metrics.Timestamp.ToString("HH:mm:ss")
            });
        }

        public async System.Threading.Tasks.Task<string> GetWeatherDataJson(double lat, double lon)
        {
            return await WeatherService.GetWeatherJsonAsync(lat, lon);
        }

        public void StartDrag()
        {
            if (!_window.IsLocked) _window.BeginNativeDrag();
        }

        public bool ToggleLock()
        {
            _window.IsLocked = !_window.IsLocked;
            _window.SaveWidgetState();
            return _window.IsLocked;
        }

        public bool GetIsLocked() => _window.IsLocked;
    }

    // Διαφανές top-most overlay που σχεδιάζει τις μπλε γραμμές ευθυγράμμισης κατά το drag.
    public class SnapOverlayWindow : Form
    {
        private static SnapOverlayWindow? _instance;
        public static SnapOverlayWindow Instance => _instance ??= new SnapOverlayWindow();

        private int? _vertLineX;
        private int? _horizLineY;

        private SnapOverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            Bounds = SystemInformation.VirtualScreen;

            int exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            SetWindowLong(Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public void ShowGuides(int? x, int? y)
        {
            _vertLineX = x;
            _horizLineY = y;

            if (x.HasValue || y.HasValue)
            {
                if (!Visible) Show();
                Invalidate();
            }
            else if (Visible)
            {
                Hide();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.FromArgb(220, 0, 210, 255), 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };

            if (_vertLineX.HasValue) e.Graphics.DrawLine(pen, _vertLineX.Value, 0, _vertLineX.Value, Height);
            if (_horizLineY.HasValue) e.Graphics.DrawLine(pen, 0, _horizLineY.Value, Width, _horizLineY.Value);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }

    // Υπολογίζει snap-to-edge (οθόνη) και snap-to-widget (γειτονικά widgets), με προαιρετικό grid snap.
    public static class WidgetSnapEngine
    {
        public static int SnapThreshold { get; set; } = 15;
        public static int GridSize { get; set; } = 20;
        public static bool EnableGridSnap { get; set; } = false;

        public static void ApplySnap(WidgetWindow currentWidget, ref RECT targetRect, IReadOnlyCollection<WidgetWindow> activeWidgets)
        {
            int width = targetRect.Right - targetRect.Left;
            int height = targetRect.Bottom - targetRect.Top;

            Rectangle workArea = Screen.FromHandle(currentWidget.Handle).WorkingArea;

            int? snapX = null;
            int? snapY = null;

            if (Math.Abs(targetRect.Left - workArea.Left) < SnapThreshold)
            {
                targetRect.Left = workArea.Left;
                targetRect.Right = targetRect.Left + width;
                snapX = workArea.Left;
            }
            else if (Math.Abs(targetRect.Right - workArea.Right) < SnapThreshold)
            {
                targetRect.Right = workArea.Right;
                targetRect.Left = targetRect.Right - width;
                snapX = workArea.Right;
            }

            if (Math.Abs(targetRect.Top - workArea.Top) < SnapThreshold)
            {
                targetRect.Top = workArea.Top;
                targetRect.Bottom = targetRect.Top + height;
                snapY = workArea.Top;
            }
            else if (Math.Abs(targetRect.Bottom - workArea.Bottom) < SnapThreshold)
            {
                targetRect.Bottom = workArea.Bottom;
                targetRect.Top = targetRect.Bottom - height;
                snapY = workArea.Bottom;
            }

            foreach (var other in activeWidgets)
            {
                if (other == currentWidget || !other.Visible) continue;
                Rectangle b = other.Bounds;

                if (Math.Abs(targetRect.Left - b.Right) < SnapThreshold) { targetRect.Left = b.Right; targetRect.Right = targetRect.Left + width; snapX = b.Right; }
                else if (Math.Abs(targetRect.Right - b.Left) < SnapThreshold) { targetRect.Right = b.Left; targetRect.Left = targetRect.Right - width; snapX = b.Left; }
                else if (Math.Abs(targetRect.Left - b.Left) < SnapThreshold) { targetRect.Left = b.Left; targetRect.Right = targetRect.Left + width; snapX = b.Left; }

                if (Math.Abs(targetRect.Top - b.Bottom) < SnapThreshold) { targetRect.Top = b.Bottom; targetRect.Bottom = targetRect.Top + height; snapY = b.Bottom; }
                else if (Math.Abs(targetRect.Bottom - b.Top) < SnapThreshold) { targetRect.Bottom = b.Top; targetRect.Top = targetRect.Bottom - height; snapY = b.Top; }
                else if (Math.Abs(targetRect.Top - b.Top) < SnapThreshold) { targetRect.Top = b.Top; targetRect.Bottom = targetRect.Top + height; snapY = b.Top; }
            }

            if (EnableGridSnap && GridSize > 0)
            {
                targetRect.Left = (int)(Math.Round((double)targetRect.Left / GridSize) * GridSize);
                targetRect.Right = targetRect.Left + width;
                targetRect.Top = (int)(Math.Round((double)targetRect.Top / GridSize) * GridSize);
                targetRect.Bottom = targetRect.Top + height;
            }

            targetRect.Left = Math.Clamp(targetRect.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
            targetRect.Top = Math.Clamp(targetRect.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
            targetRect.Right = targetRect.Left + width;
            targetRect.Bottom = targetRect.Top + height;
            SnapOverlayWindow.Instance.ShowGuides(snapX, snapY);
        }
    }

    public class WidgetWindow : Form
    {
        private const int WM_MOVING = 0x0216;
        private const int WM_EXITSIZEMOVE = 0x0232;

        private WebView2? _webView;
        private System.Windows.Forms.Timer? _nativeMonitorTimer;
        public string WidgetId { get; }
        public bool IsLocked { get; set; } = false;
        public string Variant { get; set; } = "Digital";
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? LocationName { get; set; }
        private Panel? _themedPanel;
        private Action? _themeHandler;
        private Label? _titleLabelRef;

        public WidgetWindow(string widgetId, int defaultX, int defaultY, int width, int height)
        {
            WidgetId = widgetId;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(width, height);
            this.ShowInTaskbar = false;
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Black;

            LoadWidgetState(defaultX, defaultY);
            if (string.Equals(widgetId, "sysmon", StringComparison.OrdinalIgnoreCase))
                InitializeNativeSystemMonitor();
            else if (string.Equals(widgetId, "clock", StringComparison.OrdinalIgnoreCase))
                InitializeNativeClock();
            else if (string.Equals(widgetId, "network", StringComparison.OrdinalIgnoreCase))
                InitializeNativeNetwork();
            else if (string.Equals(widgetId, "audio", StringComparison.OrdinalIgnoreCase))
                InitializeNativeAudio();
            else if (string.Equals(widgetId, "weather", StringComparison.OrdinalIgnoreCase))
                InitializeNativeWeather();
            else
                _ = InitializeWebViewAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _nativeMonitorTimer?.Stop();
                _nativeMonitorTimer?.Dispose();
                _nativeMonitorTimer = null;
                _webView?.Dispose();
                _webView = null;
                if (_themeHandler != null) { ThemeManager.Changed -= _themeHandler; ThemeManager.Repainted -= _themeHandler; }
                _themeHandler = null;
            }
            base.Dispose(disposing);
        }

        public void BeginNativeDrag()
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOVING && !IsLocked)
            {
                RECT rect = Marshal.PtrToStructure<RECT>(m.LParam);
                WidgetSnapEngine.ApplySnap(this, ref rect, WidgetHostEngine.Instance.GetActiveWidgets());
                Marshal.StructureToPtr(rect, m.LParam, false);
            }
            else if (m.Msg == WM_EXITSIZEMOVE)
            {
                SnapOverlayWindow.Instance.ShowGuides(null, null);
                SaveWidgetState();
            }

            base.WndProc(ref m);
        }

        public void SaveWidgetState()
        {
            try
            {
                var configPath = GetConfigFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

                var state = new WidgetState
                {
                    WidgetId = WidgetId, X = Location.X, Y = Location.Y, Width = Width, Height = Height, IsLocked = IsLocked,
                    Variant = Variant, Latitude = Latitude, Longitude = Longitude, LocationName = LocationName
                };
                File.WriteAllText(configPath, JsonSerializer.Serialize(state));
            }
            catch (IOException) { }
        }

        private void LoadWidgetState(int defaultX, int defaultY)
        {
            var configPath = GetConfigFilePath();
            if (File.Exists(configPath))
            {
                try
                {
                    var state = JsonSerializer.Deserialize<WidgetState>(File.ReadAllText(configPath));
                    if (state != null)
                    {
                        if (state.Width > 120 && state.Height > 80) Size = new Size(Math.Clamp(state.Width, 160, 1200), Math.Clamp(state.Height, 100, 900));
                        Location = ClampToAvailableScreen(new Point(state.X, state.Y));
                        IsLocked = state.IsLocked;
                        Variant = string.IsNullOrWhiteSpace(state.Variant) ? "Digital" : state.Variant;
                        Latitude = state.Latitude;
                        Longitude = state.Longitude;
                        LocationName = state.LocationName;
                        return;
                    }
                }
                catch (JsonException) { }
                catch (IOException) { }
            }

            Location = ClampToAvailableScreen(new Point(defaultX, defaultY));
        }

        private Point ClampToAvailableScreen(Point location)
        {
            var screen = Screen.AllScreens.FirstOrDefault(s => s.WorkingArea.Contains(location)) ?? Screen.PrimaryScreen;
            if (screen == null) return location;
            var area = screen.WorkingArea;
            return new Point(Math.Clamp(location.X, area.Left, Math.Max(area.Left, area.Right - Width)), Math.Clamp(location.Y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        }

        private string GetConfigFilePath() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MotionDeskStudio", "widgets", $"{WidgetId}_config.json");

        private async System.Threading.Tasks.Task InitializeWebViewAsync()
        {
            try
            {
                if (IsDisposed) return;
                _webView = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
                Controls.Add(_webView);
                await _webView.EnsureCoreWebView2Async(await WebView2Support.CreateEnvironmentAsync());
                if (IsDisposed || _webView.CoreWebView2 == null) return;
                _webView.CoreWebView2.AddHostObjectToScript("motionDesk", new WidgetBridge(this));
                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "widgets", WidgetId, "index.html");
                if (File.Exists(htmlPath)) _webView.CoreWebView2.Navigate(htmlPath);
            }
            catch (Exception ex)
            {
                if (!IsDisposed) Text = $"MotionDesk - {WidgetId}";
                System.Diagnostics.Debug.WriteLine($"WebView2 widget initialization failed: {ex}");
            }
        }

        private void ShowWidgetMenu(Control owner)
        {
            // ΟΧΙ "using": το ContextMenuStrip.Show() δεν μπλοκάρει, οπότε ένα "using" εδώ θα το
            // Dispose-άρει αμέσως μετά το Show() -> flash-and-vanish πριν προλάβει κλικ ο χρήστης.
            var menu = new ContextMenuStrip();

            if (string.Equals(WidgetId, "clock", StringComparison.OrdinalIgnoreCase))
            {
                var digital = new ToolStripMenuItem("Digital", null, (_, _) => SetClockVariant("Digital")) { Checked = Variant == "Digital" };
                var analog = new ToolStripMenuItem("Analog", null, (_, _) => SetClockVariant("Analog")) { Checked = Variant == "Analog" };
                menu.Items.Add(digital);
                menu.Items.Add(analog);
                menu.Items.Add(new ToolStripSeparator());
            }
            else if (string.Equals(WidgetId, "weather", StringComparison.OrdinalIgnoreCase))
            {
                menu.Items.Add("Set location…", null, (_, _) => PromptWeatherLocation());
                menu.Items.Add(new ToolStripSeparator());
            }

            var lockItem = new ToolStripMenuItem(IsLocked ? "Unlock widget" : "Lock widget");
            lockItem.Click += (_, _) => { IsLocked = !IsLocked; SaveWidgetState(); };
            menu.Items.Add(lockItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Close widget", null, (_, _) => Close());
            menu.Show(owner, owner.PointToClient(Cursor.Position));
        }

        private Action? _variantChangedHandler;
        public void OnVariantChanged(Action handler) => _variantChangedHandler = handler;

        private void SetClockVariant(string variant)
        {
            Variant = variant;
            SaveWidgetState();
            _variantChangedHandler?.Invoke();
        }

        private void PromptWeatherLocation()
        {
            using var dlg = new Form
            {
                Text = "Set weather location",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(320, 120),
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = UiTheme.Surface
            };
            var label = new Label { Text = "City name:", Location = new Point(14, 14), AutoSize = true, ForeColor = UiTheme.TextPrimary };
            var textBox = new TextBox { Location = new Point(14, 36), Width = 290, Text = LocationName ?? "" };
            var okBtn = new Button { Text = "OK", Location = new Point(140, 74), DialogResult = DialogResult.OK };
            var cancelBtn = new Button { Text = "Cancel", Location = new Point(228, 74), DialogResult = DialogResult.Cancel };
            dlg.Controls.Add(label); dlg.Controls.Add(textBox); dlg.Controls.Add(okBtn); dlg.Controls.Add(cancelBtn);
            dlg.AcceptButton = okBtn; dlg.CancelButton = cancelBtn;

            if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                _ = ApplyGeocodedLocationAsync(textBox.Text.Trim());
            }
        }

        private Func<string, System.Threading.Tasks.Task>? _geocodeHandler;
        public void OnGeocodeRequested(Func<string, System.Threading.Tasks.Task> handler) => _geocodeHandler = handler;

        private async System.Threading.Tasks.Task ApplyGeocodedLocationAsync(string cityName)
        {
            if (_geocodeHandler != null) await _geocodeHandler(cityName);
        }

        // Κοινό, θεματισμένο "κέλυφος" widget: στρογγυλεμένες γωνίες + χρώματα από το UiTheme
        // αντί για μόνιμο σχεδόν-μαύρο φόντο (ζητήθηκε ρητά — "τα widgets είναι μόνο μαύρα").
        // Ξανασχεδιάζεται αυτόματα σε κάθε αλλαγή θέματος/σκουρότητας μέσω ThemeManager.Changed/Repainted.
        private Panel CreateNativePanel(string title, out Label values)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.Background;
            TransparencyKey = default;

            var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(14) };
            _themedPanel = panel;
            var titleBar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };

            var closeLabel = new Label { Text = "✕", AutoSize = false, Size = new Size(22, 22), Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand };
            closeLabel.MouseEnter += (_, _) => closeLabel.ForeColor = Color.FromArgb(231, 76, 60);
            closeLabel.MouseLeave += (_, _) => closeLabel.ForeColor = UiTheme.TextSecondary;
            closeLabel.Click += (_, _) => Close();

            var lockLabel = new Label { Text = IsLocked ? "🔒" : "🔓", AutoSize = false, Size = new Size(22, 22), Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Font = new Font("Segoe UI", 9), Cursor = Cursors.Hand };
            lockLabel.Click += (_, _) => { IsLocked = !IsLocked; lockLabel.Text = IsLocked ? "🔒" : "🔓"; SaveWidgetState(); };

            var menuLabel = new Label { Text = "☰", AutoSize = false, Size = new Size(22, 22), Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand };
            menuLabel.MouseEnter += (_, _) => menuLabel.ForeColor = UiTheme.TextPrimary;
            menuLabel.MouseLeave += (_, _) => menuLabel.ForeColor = UiTheme.TextSecondary;
            menuLabel.Click += (_, _) => ShowWidgetMenu(menuLabel);

            var titleLabel = new Label { Dock = DockStyle.Fill, Text = title, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, Cursor = Cursors.SizeAll, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };
            _titleLabelRef = titleLabel;
            titleLabel.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left && !IsLocked) BeginNativeDrag();
                else if (e.Button == MouseButtons.Right) ShowWidgetMenu(titleLabel);
            };

            titleBar.Controls.Add(titleLabel);
            titleBar.Controls.Add(lockLabel);
            titleBar.Controls.Add(closeLabel);
            titleBar.Controls.Add(menuLabel);

            panel.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left && !IsLocked) BeginNativeDrag(); };
            values = new Label { Dock = DockStyle.Top, Height = 52, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10.5f), ForeColor = UiTheme.TextPrimary, BackColor = Color.Transparent };
            var valuesRef = values;
            panel.Controls.Add(values); panel.Controls.Add(titleBar);
            Controls.Add(panel);

            _themeHandler = () =>
            {
                if (IsDisposed) return;
                panel.BackColor = UiTheme.Surface;
                BackColor = UiTheme.Background;
                closeLabel.ForeColor = UiTheme.TextSecondary;
                lockLabel.ForeColor = UiTheme.TextSecondary;
                menuLabel.ForeColor = UiTheme.TextSecondary;
                titleLabel.ForeColor = UiTheme.TextPrimary;
                valuesRef.ForeColor = UiTheme.TextPrimary;
                ApplyRoundedShell();
                Invalidate(true);
            };
            ThemeManager.Changed += _themeHandler;
            ThemeManager.Repainted += _themeHandler;

            ApplyRoundedShell();
            Resize += (_, _) => ApplyRoundedShell();
            return panel;
        }

        private void ApplyRoundedShell()
        {
            if (Width > 0 && Height > 0) UiTheme.ApplyRoundedRegion(this, 12);
        }

        private void InitializeNativeClock()
        {
            var panel = CreateNativePanel("Clock", out var values);
            values.TextAlign = ContentAlignment.MiddleCenter;
            values.Font = new Font("Segoe UI", 16, FontStyle.Bold);

            var analog = new AnalogClockControl { Dock = DockStyle.Fill, Visible = false };
            panel.Controls.Add(analog);
            analog.BringToFront();

            void ApplyVariant()
            {
                bool isAnalog = Variant == "Analog";
                analog.Visible = isAnalog;
                values.Visible = !isAnalog;
                values.Dock = isAnalog ? DockStyle.None : DockStyle.Top;
            }
            void Update()
            {
                values.Text = DateTime.Now.ToString("HH:mm:ss\ndddd, dd MMMM");
                analog.Invalidate();
            }
            OnVariantChanged(ApplyVariant);
            ApplyVariant();
            Update();
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _nativeMonitorTimer.Tick += (_, _) => Update();
            _nativeMonitorTimer.Start();
        }

        private void InitializeNativeNetwork()
        {
            var panel = CreateNativePanel("Network", out var values);
            values.Height = 40;
            var sparkline = new NetworkSparklineControl { Dock = DockStyle.Fill };
            panel.Controls.Add(sparkline);
            sparkline.BringToFront();

            void Update()
            {
                var m = AdvancedSystemMonitorService.Instance.GetSnapshot();
                values.Text = $"↓ {m.NetworkDownKbps:0.0} KB/s   ↑ {m.NetworkUpKbps:0.0} KB/s   ·   {m.ProcessCount} διεργασίες";
                sparkline.Push(m.NetworkDownKbps, m.NetworkUpKbps);
            }
            Update();
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _nativeMonitorTimer.Tick += (_, _) => Update();
            _nativeMonitorTimer.Start();
        }

        private void InitializeNativeAudio()
        {
            var panel = CreateNativePanel("Audio Visualizer", out var values);
            values.TextAlign = ContentAlignment.MiddleCenter;
            values.Height = 24;
            values.Font = new Font("Segoe UI", 9);

            var spectrum = new AudioSpectrumService(20);
            var equalizer = new EqualizerControl { Dock = DockStyle.Fill };
            panel.Controls.Add(equalizer);
            equalizer.BringToFront();

            void Update()
            {
                var bands = spectrum.GetBands();
                equalizer.PushBands(bands);
                values.Text = spectrum.IsAvailable ? "" : "Δεν εντοπίστηκε συσκευή ήχου εξόδου";
            }
            Update();
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _nativeMonitorTimer.Tick += (_, _) => Update();
            _nativeMonitorTimer.Start();
            FormClosed += (_, _) => spectrum.Dispose();
        }

        private void InitializeNativeWeather()
        {
            var panel = CreateNativePanel(LocationName != null ? $"Weather · {LocationName}" : "Weather · Athens (default)", out var values);
            values.TextAlign = ContentAlignment.MiddleCenter;
            values.Text = "Loading…";

            var icon = new WeatherIconControl { Dock = DockStyle.Fill };
            panel.Controls.Add(icon);
            icon.BringToFront();

            var animTimer = new System.Windows.Forms.Timer { Interval = 60 };
            animTimer.Tick += (_, _) => icon.AdvancePhase();
            animTimer.Start();
            FormClosed += (_, _) => animTimer.Dispose();

            async void Update()
            {
                try
                {
                    double lat = Latitude ?? 37.9838;
                    double lon = Longitude ?? 23.7275;
                    var json = await WeatherService.GetWeatherJsonAsync(lat, lon);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("current_weather", out var current))
                    {
                        var temp = current.GetProperty("temperature").GetDouble();
                        var wind = current.GetProperty("windspeed").GetDouble();
                        int code = current.TryGetProperty("weathercode", out var wc) ? wc.GetInt32() : 0;
                        icon.Condition = WeatherService.ClassifyWeatherCode(code);
                        values.Text = $"{temp:0.#} °C   ·   Άνεμος {wind:0.#} km/h";
                    }
                    else values.Text = "Weather unavailable";
                }
                catch (Exception ex) { values.Text = "Weather unavailable"; try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "motiondesk_weather_debug.log"), $"{DateTime.Now}: {ex}\n"); } catch { } }
            }
            OnGeocodeRequested(async cityName =>
            {
                var result = await WeatherService.GeocodeAsync(cityName);
                if (result.HasValue)
                {
                    Latitude = result.Value.Lat;
                    Longitude = result.Value.Lon;
                    LocationName = result.Value.Name;
                    SaveWidgetState();
                    if (_titleLabelRef != null) _titleLabelRef.Text = $"Weather · {LocationName}";
                    Update();
                }
            });
            Update();
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 900000 };
            _nativeMonitorTimer.Tick += (_, _) => Update();
            _nativeMonitorTimer.Start();
        }

        private void InitializeNativeSystemMonitor()
        {
            // Πρώην ξεχωριστό, χειροποίητο title bar χωρίς Χ/κλείδωμα/μενού — το μόνο widget
            // που δεν περνούσε από το κοινό CreateNativePanel, γι' αυτό δεν είχε ορατό κουμπί
            // κλεισίματος σε αντίθεση με τα υπόλοιπα (Clock/Network/Audio/Weather).
            CreateNativePanel("System Monitor", out var values);
            void Update()
            {
                var m = SystemMonitorService.Instance.GetSnapshot();
                values.Text = $"CPU     {m.CpuPercent:0.0}%\n\nRAM     {m.AvailableMemoryMb:0} MB free\n        {m.TotalMemoryMb:0} MB total";
            }
            Update();
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _nativeMonitorTimer.Tick += (_, _) => Update();
            _nativeMonitorTimer.Start();
        }

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    }

    // Αναλογικό ρολόι — εναλλακτικό στυλ στο Clock widget (Digital/Analog, ζητήθηκε ρητά).
    internal sealed class AnalogClockControl : Control
    {
        public AnalogClockControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(UiTheme.Surface)) g.FillRectangle(bg, ClientRectangle);

            int size = Math.Min(Width, Height) - 12;
            if (size < 20) return;
            var center = new PointF(Width / 2f, Height / 2f);
            float radius = size / 2f;

            using (var facePen = new Pen(UiTheme.Border, 2f))
                g.DrawEllipse(facePen, center.X - radius, center.Y - radius, radius * 2, radius * 2);

            using var tickPen = new Pen(UiTheme.TextMuted, 2f);
            for (int i = 0; i < 12; i++)
            {
                double angle = i * Math.PI / 6.0;
                float outer = radius - 3;
                float inner = radius - (i % 3 == 0 ? 11 : 6);
                var p1 = new PointF(center.X + (float)Math.Sin(angle) * outer, center.Y - (float)Math.Cos(angle) * outer);
                var p2 = new PointF(center.X + (float)Math.Sin(angle) * inner, center.Y - (float)Math.Cos(angle) * inner);
                g.DrawLine(tickPen, p1, p2);
            }

            var now = DateTime.Now;
            DrawHand(g, center, radius * 0.5f, 5f, (now.Hour % 12 + now.Minute / 60.0) * 30.0, UiTheme.TextPrimary);
            DrawHand(g, center, radius * 0.72f, 3.5f, now.Minute * 6.0, UiTheme.TextPrimary);
            DrawHand(g, center, radius * 0.82f, 1.5f, now.Second * 6.0, UiTheme.AccentCyan);

            using var hubBrush = new SolidBrush(UiTheme.AccentCyan);
            g.FillEllipse(hubBrush, center.X - 4, center.Y - 4, 8, 8);
        }

        private static void DrawHand(Graphics g, PointF center, float length, float width, double angleDegrees, Color color)
        {
            double rad = angleDegrees * Math.PI / 180.0;
            var tip = new PointF(center.X + (float)Math.Sin(rad) * length, center.Y - (float)Math.Cos(rad) * length);
            using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, center, tip);
        }
    }

    // Ζωντανό mini sparkline (Download/Upload) κάτω από το κειμενικό στατιστικό — ζητήθηκε ρητά
    // "κάποιο visual γραφικό για ομορφιά" πέρα από τις γυμνές πληροφορίες κειμένου.
    internal sealed class NetworkSparklineControl : Control
    {
        private readonly Queue<double> _down = new();
        private readonly Queue<double> _up = new();
        private const int MaxPoints = 40;

        public NetworkSparklineControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void Push(double downKbps, double upKbps)
        {
            _down.Enqueue(downKbps); while (_down.Count > MaxPoints) _down.Dequeue();
            _up.Enqueue(upKbps); while (_up.Count > MaxPoints) _up.Dequeue();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(UiTheme.Surface)) g.FillRectangle(bg, ClientRectangle);
            if (_down.Count < 2) return;

            double scaleMax = Math.Max(64, Math.Max(_down.Max(), _up.Max()));
            DrawSeries(g, _down, scaleMax, UiTheme.AccentCyan, true);
            DrawSeries(g, _up, scaleMax, UiTheme.AccentBlue, false);
        }

        private void DrawSeries(Graphics g, Queue<double> series, double scaleMax, Color color, bool fill)
        {
            var values = series.ToArray();
            var pts = new PointF[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                float x = i * Width / (float)Math.Max(1, MaxPoints - 1);
                double v = scaleMax > 0 ? Math.Clamp(values[i] / scaleMax, 0, 1) : 0;
                pts[i] = new PointF(x, Height - 4 - (float)(v * (Height - 8)));
            }
            if (pts.Length < 2) return;

            if (fill)
            {
                var poly = new PointF[pts.Length + 2];
                Array.Copy(pts, poly, pts.Length);
                poly[pts.Length] = new PointF(pts[^1].X, Height);
                poly[pts.Length + 1] = new PointF(pts[0].X, Height);
                using var fillBrush = new SolidBrush(Color.FromArgb(40, color));
                g.FillPolygon(fillBrush, poly);
            }
            using var pen = new Pen(color, 2f) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, pts);
        }
    }

    // WMP-Legacy-style equalizer: κάθετες, τμηματοποιημένες ("LED") μπάρες με peak-hold καπάκι
    // που πέφτει αργά, τροφοδοτούμενες από πραγματικά FFT bands (AudioSpectrumService) — όχι
    // πλέον ASCII κείμενο βασισμένο σε ένα μοναδικό peak value.
    internal sealed class EqualizerControl : Control
    {
        private float[] _display = Array.Empty<float>();
        private float[] _peaks = Array.Empty<float>();

        public EqualizerControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void PushBands(float[] bands)
        {
            if (_display.Length != bands.Length)
            {
                _display = new float[bands.Length];
                _peaks = new float[bands.Length];
            }
            for (int i = 0; i < bands.Length; i++)
            {
                // Γρήγορη άνοδος, πιο αργή πτώση — τυπική συμπεριφορά equalizer (attack/release).
                _display[i] = bands[i] > _display[i] ? bands[i] : _display[i] * 0.75f + bands[i] * 0.25f;
                if (_display[i] >= _peaks[i]) _peaks[i] = _display[i];
                else _peaks[i] = Math.Max(0, _peaks[i] - 0.02f);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            using (var bg = new SolidBrush(UiTheme.Surface)) g.FillRectangle(bg, ClientRectangle);
            if (_display.Length == 0) return;

            const int segments = 12;
            float barGap = 2f, segGap = 2f;
            float barWidth = (Width - barGap * (_display.Length - 1)) / _display.Length;
            float segHeight = (Height - segGap * (segments - 1)) / (float)segments;

            for (int b = 0; b < _display.Length; b++)
            {
                float x = b * (barWidth + barGap);
                int litSegments = (int)Math.Round(_display[b] * segments);
                int peakSegment = (int)Math.Round(_peaks[b] * segments);

                for (int s = 0; s < segments; s++)
                {
                    float y = Height - (s + 1) * (segHeight + segGap) + segGap;
                    Color segColor = s < litSegments
                        ? SegmentColor(s, segments)
                        : Color.FromArgb(40, UiTheme.Border);
                    using var brush = new SolidBrush(segColor);
                    g.FillRectangle(brush, x, y, barWidth, segHeight);
                }

                if (peakSegment > 0 && peakSegment <= segments)
                {
                    float py = Height - peakSegment * (segHeight + segGap) + segGap;
                    using var peakBrush = new SolidBrush(Color.White);
                    g.FillRectangle(peakBrush, x, py, barWidth, 2f);
                }
            }
        }

        private static Color SegmentColor(int segmentIndex, int totalSegments)
        {
            double t = (segmentIndex + 1) / (double)totalSegments;
            if (t > 0.85) return Color.FromArgb(235, 76, 66);
            if (t > 0.6) return Color.FromArgb(240, 200, 60);
            return Color.FromArgb(60, 210, 140);
        }
    }

    // Χειροποίητο, ζωντανό εικονίδιο καιρού (όχι στατικό emoji) — αλλάζει σχήμα ανάλογα με τη
    // συνθήκη καιρού (WMO code) ΚΑΙ κινείται (ζητήθηκε ρητά "κινούμενα εικονίδια ανάλογα με την
    // κατάσταση του καιρού"): ήλιος με περιστρεφόμενες ακτίνες, σύννεφο που μετατοπίζεται, βροχή/
    // χιόνι με σταγόνες/νιφάδες που πέφτουν, καταιγίδα με αναβοσβήνοντα κεραυνό.
    internal sealed class WeatherIconControl : Control
    {
        public WeatherCondition Condition { get; set; } = WeatherCondition.Clear;
        private int _phase;
        private readonly Random _rng = new();
        private readonly PointF[] _drops;

        public WeatherIconControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _drops = new PointF[10];
            for (int i = 0; i < _drops.Length; i++) _drops[i] = new PointF((float)_rng.NextDouble(), (float)_rng.NextDouble());
        }

        public void AdvancePhase() { _phase++; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(UiTheme.Surface)) g.FillRectangle(bg, ClientRectangle);

            float cx = Width / 2f, cy = Height / 2f - 4;
            float scale = Math.Min(Width, Height) / 110f;
            if (scale <= 0) return;

            switch (Condition)
            {
                case WeatherCondition.Clear: DrawSun(g, cx, cy, scale, 1f); break;
                case WeatherCondition.PartlyCloudy: DrawSun(g, cx - 14 * scale, cy - 6 * scale, scale * 0.7f, 0.8f); DrawCloud(g, cx + 6 * scale, cy + 8 * scale, scale); break;
                case WeatherCondition.Cloudy: case WeatherCondition.Fog: DrawCloud(g, cx, cy, scale * 1.15f); break;
                case WeatherCondition.Drizzle: case WeatherCondition.Rain: DrawCloud(g, cx, cy - 10 * scale, scale); DrawRain(g, cx, cy, scale); break;
                case WeatherCondition.Snow: DrawCloud(g, cx, cy - 10 * scale, scale); DrawSnow(g, cx, cy, scale); break;
                case WeatherCondition.Thunderstorm: DrawCloud(g, cx, cy - 10 * scale, scale); DrawBolt(g, cx, cy, scale); break;
            }
        }

        private void DrawSun(Graphics g, float cx, float cy, float scale, float alpha)
        {
            using var rayPen = new Pen(Color.FromArgb((int)(200 * alpha), 250, 190, 70), 3f * scale);
            double rot = _phase * 1.2 * Math.PI / 180.0;
            float r1 = 22 * scale, r2 = 30 * scale;
            for (int i = 0; i < 8; i++)
            {
                double a = rot + i * Math.PI / 4;
                g.DrawLine(rayPen,
                    cx + (float)Math.Cos(a) * r1, cy + (float)Math.Sin(a) * r1,
                    cx + (float)Math.Cos(a) * r2, cy + (float)Math.Sin(a) * r2);
            }
            using var sunBrush = new SolidBrush(Color.FromArgb((int)(255 * alpha), 255, 205, 90));
            g.FillEllipse(sunBrush, cx - 18 * scale, cy - 18 * scale, 36 * scale, 36 * scale);
        }

        private void DrawCloud(Graphics g, float cx, float cy, float scale)
        {
            float drift = (float)Math.Sin(_phase * 0.02) * 6f * scale;
            cx += drift;
            using var cloudBrush = new SolidBrush(UiTheme.TextMuted);
            g.FillEllipse(cloudBrush, cx - 26 * scale, cy - 6 * scale, 30 * scale, 24 * scale);
            g.FillEllipse(cloudBrush, cx - 6 * scale, cy - 16 * scale, 36 * scale, 32 * scale);
            g.FillEllipse(cloudBrush, cx + 16 * scale, cy - 2 * scale, 26 * scale, 22 * scale);
            g.FillRectangle(cloudBrush, cx - 24 * scale, cy + 6 * scale, 62 * scale, 12 * scale);
        }

        private void DrawRain(Graphics g, float cx, float cy, float scale)
        {
            using var dropPen = new Pen(Color.FromArgb(200, 90, 160, 230), 2.5f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (int i = 0; i < _drops.Length; i++)
            {
                float t = ((_phase * 0.03f + i * 0.37f) % 1f);
                float x = cx + (_drops[i].X - 0.5f) * 60 * scale;
                float y = cy + 10 * scale + t * 34 * scale;
                g.DrawLine(dropPen, x, y, x - 3 * scale, y + 8 * scale);
            }
        }

        private void DrawSnow(Graphics g, float cx, float cy, float scale)
        {
            using var flakeBrush = new SolidBrush(Color.White);
            for (int i = 0; i < _drops.Length; i++)
            {
                float t = ((_phase * 0.015f + i * 0.41f) % 1f);
                float x = cx + (_drops[i].X - 0.5f) * 60 * scale + (float)Math.Sin(_phase * 0.05 + i) * 4 * scale;
                float y = cy + 8 * scale + t * 36 * scale;
                g.FillEllipse(flakeBrush, x, y, 3.5f * scale, 3.5f * scale);
            }
        }

        private void DrawBolt(Graphics g, float cx, float cy, float scale)
        {
            bool flash = (_phase / 8) % 3 == 0;
            if (!flash) return;
            var pts = new[]
            {
                new PointF(cx - 2 * scale, cy + 6 * scale),
                new PointF(cx + 6 * scale, cy + 6 * scale),
                new PointF(cx - 2 * scale, cy + 24 * scale),
                new PointF(cx + 10 * scale, cy + 18 * scale),
                new PointF(cx + 2 * scale, cy + 18 * scale),
                new PointF(cx + 10 * scale, cy + 2 * scale),
            };
            using var boltBrush = new SolidBrush(Color.FromArgb(230, 255, 221, 87));
            g.FillPolygon(boltBrush, pts);
        }
    }

    public class WidgetState
    {
        public string WidgetId { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsLocked { get; set; }

        // Clock: "Digital" | "Analog".
        public string Variant { get; set; } = "Digital";

        // Weather: αποθηκευμένη τοποθεσία χρήστη — Αθήνα παραμένει το default όταν είναι κενό.
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? LocationName { get; set; }
    }

    public sealed class WidgetSnapshot
    {
        public string WidgetId { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsLocked { get; set; }
    }

    public class WidgetHostEngine
    {
        private static WidgetHostEngine? _instance;
        public static WidgetHostEngine Instance => _instance ??= new WidgetHostEngine();

        private readonly List<WidgetWindow> _activeWidgets = new();
        public IReadOnlyCollection<WidgetWindow> GetActiveWidgets() => _activeWidgets;

        public IReadOnlyList<WidgetSnapshot> GetSnapshots() => _activeWidgets
            .Where(w => !w.IsDisposed)
            .Select(w => new WidgetSnapshot { WidgetId = w.WidgetId, X = w.Location.X, Y = w.Location.Y, Width = w.Width, Height = w.Height, IsLocked = w.IsLocked })
            .ToArray();

        public void RestoreSnapshots(IEnumerable<WidgetSnapshot> snapshots)
        {
            CloseAll();
            foreach (var snapshot in snapshots.Where(s => !string.IsNullOrWhiteSpace(s.WidgetId)))
            {
                var widget = SpawnWidget(snapshot.WidgetId, snapshot.X, snapshot.Y, Math.Max(160, snapshot.Width), Math.Max(100, snapshot.Height));
                widget.IsLocked = snapshot.IsLocked;
                widget.SaveWidgetState();
            }
        }

        public void CloseAll()
        {
            foreach (var widget in new List<WidgetWindow>(_activeWidgets))
                if (!widget.IsDisposed) widget.Close();
            _activeWidgets.Clear();
        }

        public WidgetWindow SpawnWidget(string widgetId, int x, int y, int width, int height)
        {
            var existing = _activeWidgets.Find(w => w.WidgetId == widgetId);
            if (existing != null && !existing.IsDisposed)
            {
                existing.BringToFront();
                return existing;
            }

            var widgetWindow = new WidgetWindow(widgetId, x, y, width, height);
            _activeWidgets.Add(widgetWindow);
            widgetWindow.FormClosed += (s, e) => _activeWidgets.Remove(widgetWindow);
            widgetWindow.Show();
            return widgetWindow;
        }
    }
}
