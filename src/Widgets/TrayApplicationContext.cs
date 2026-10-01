using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets
{
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon = null!;
        private MainWindow? _mainWindow;
        private readonly HotkeyManager _hotkeys = new();
        private readonly ToolStripMenuItem _wallpaperToggleItem;
        private bool _wallpaperEnabled;

        public TrayApplicationContext()
        {
            // Δημιουργία Context Menu για το Tray Icon — ομαδοποιημένο σε: παράθυρο, DeskZones,
            // wallpaper, γρήγορα προφίλ, γρήγορες ρυθμίσεις, έξοδος.
            ContextMenuStrip contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(LocalizationManager.T("Tray.Open"), null, (s, e) => ShowMainWindow());
            contextMenu.Items.Add(LocalizationManager.T("Tray.CommandPalette"), null, (s, e) => { ShowMainWindow(); _mainWindow?.OpenCommandPalette(); });
            contextMenu.Items.Add(LocalizationManager.T("Tray.Settings"), null, (s, e) => ShowMainWindow("Settings"));
            contextMenu.Items.Add(new ToolStripSeparator());

            // Τα "Work"/"Gaming"/"Focus" παραμένουν ως έχουν σε κάθε γλώσσα — είναι τα ίδια τα
            // ονόματα των προεπιλεγμένων προφίλ (ταυτίζονται με το string ID που περνάει στο
            // ApplyProfile/WorkspaceProfileService), όχι γενικό κείμενο UI προς μετάφραση.
            var profilesMenu = new ToolStripMenuItem(LocalizationManager.T("Tray.QuickProfile"));
            profilesMenu.DropDownItems.Add("Work", null, (s, e) => ApplyProfile("Work"));
            profilesMenu.DropDownItems.Add("Gaming", null, (s, e) => ApplyProfile("Gaming"));
            profilesMenu.DropDownItems.Add("Focus", null, (s, e) => ApplyProfile("Focus"));
            contextMenu.Items.Add(profilesMenu);
            contextMenu.Items.Add(new ToolStripSeparator());

            contextMenu.Items.Add(LocalizationManager.T("Tray.DeskZonesEdit"), null, (s, e) => AddDeskZone());
            contextMenu.Items.Add(new ToolStripSeparator());

            _wallpaperToggleItem = new ToolStripMenuItem(LocalizationManager.T("Tray.WallpaperToggle"));
            _wallpaperToggleItem.Click += (s, e) => ToggleWallpaper();
            contextMenu.Items.Add(_wallpaperToggleItem);
            contextMenu.Items.Add(LocalizationManager.T("Tray.WallpaperAddVideo"), null, (s, e) => ChooseWallpaperVideo());
            contextMenu.Items.Add(LocalizationManager.T("Tray.WallpaperAddFolder"), null, (s, e) => ChooseWallpaperFolder());
            contextMenu.Items.Add(new ToolStripSeparator());

            contextMenu.Items.Add("DeskFlip  (Ctrl+Alt+F)", null, (s, e) => DeskFlipEngine.Show());

            ToolStripMenuItem gridSnapItem = new ToolStripMenuItem(LocalizationManager.T("Tray.GridSnap"));
            gridSnapItem.Checked = WidgetSnapEngine.EnableGridSnap;
            gridSnapItem.Click += (s, e) => {
                WidgetSnapEngine.EnableGridSnap = !gridSnapItem.Checked;
                gridSnapItem.Checked = WidgetSnapEngine.EnableGridSnap;
            };
            contextMenu.Items.Add(gridSnapItem);

            ToolStripMenuItem startupItem = new ToolStripMenuItem(LocalizationManager.T("Tray.Startup"));
            startupItem.Checked = StartupManager.IsStartupEnabled();
            startupItem.Click += (s, e) => {
                bool newState = !startupItem.Checked;
                StartupManager.SetStartup(newState);
                startupItem.Checked = newState;
            };
            contextMenu.Items.Add(startupItem);

            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(LocalizationManager.T("Common.Exit"), null, (s, e) => {
                try { WorkspaceProfileService.Save("Last Session"); WorkspaceProfileService.ExitSaveDone = true; } catch { }
                _hotkeys.Dispose();
                DesktopIconVisibilityEngine.Instance.Stop();
                _trayIcon.Visible = false;
                Application.Exit();
            });

            // Αρχικοποίηση NotifyIcon — στατικό εικονίδιο (η "ζωντανή" λάμψη μετακόμισε στο
            // λογότυπο του sidebar μέσα στο κύριο παράθυρο, όχι πλέον στο tray).
            _trayIcon = new NotifyIcon()
            {
                Icon = LoadTrayIcon(),
                ContextMenuStrip = contextMenu,
                Text = "MotionDesk Studio",
                Visible = true
            };

            _trayIcon.DoubleClick += (s, e) => ShowMainWindow();

            // ---- high-usage alerts + daily update check
            _uiContext = System.Threading.SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _alertTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _alertTimer.Tick += (_, _) => UsageAlertTick();
            _alertTimer.Start();
            _trayIcon.BalloonTipClicked += (_, _) => OpenPendingUpdate();
            _ = CheckForUpdateAsync();

            _hotkeys.RegisterCtrlAlt('F', () => DeskFlipEngine.Show());

            // Fences-style: διπλό-κλικ σε κενό σημείο της επιφάνειας εργασίας κρύβει/επαναφέρει
            // όλα τα εικονίδια εκτός του Ο Υπολογιστής μου/φάκελος χρήστη/Πίνακας Ελέγχου/Κάδος
            // Ανακύκλωσης — ζητήθηκε ρητά. Τρέχει σε όλη τη διάρκεια ζωής της εφαρμογής.
            DesktopIconVisibilityEngine.Instance.Start();
            AppActivity.Start();

            // Restore lightweight persistent preferences before showing the manager.
            var appSettings = AppSettings.Load();
            WidgetSnapEngine.EnableGridSnap = appSettings.GridSnap;
            WidgetSnapEngine.SnapThreshold = Math.Clamp(appSettings.SnapThreshold, 5, 50);
            WidgetSnapEngine.GridSize = Math.Clamp(appSettings.GridSize, 5, 100);
            _wallpaperEnabled = WallpaperHostEngine.Instance.IsEnabled;

            // Time-of-day schedule (may switch the wallpaper mode before the wallpaper starts)
            WallpaperHostEngine.Instance.ApplyScheduleNow();

            if (appSettings.StartWallpaperWithWindows)
            {
                WallpaperHostEngine.Instance.Enable();
                _wallpaperEnabled = true;
            }

            // "--background": η αυτόματη εκκίνηση με τα Windows (StartupManager.SetStartup) περνάει
            // αυτό το flag — ζητήθηκε ρητά ότι όταν υπάρχουν αποθηκευμένα widgets/DeskContainers, η
            // εφαρμογή πρέπει να ανοίγει στο παρασκήνιο (μόνο tray icon), όχι με το κύριο παράθυρο
            // διαχείρισης να αναδύεται κάθε φορά. Το MainWindow ΔΗΜΙΟΥΡΓΕΙΤΑΙ κανονικά (ώστε να
            // τρέξει η επαναφορά του "Last Session" — widgets/DeskContainers/wallpaper μέσα στον
            // constructor του), απλά δεν καλείται Show() πάνω του.
            bool launchedInBackground = Environment.GetCommandLineArgs().Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
            if (launchedInBackground)
            {
                _mainWindow = new MainWindow();
                _mainWindow.FormClosed += (s, e) => _mainWindow = null;
            }
            else
            {
                ShowMainWindow();
            }
        }

        private void ShowMainWindow(string? page = null)
        {
            if (_mainWindow == null || _mainWindow.IsDisposed)
            {
                _mainWindow = new MainWindow();
                _mainWindow.FormClosed += (s, e) => _mainWindow = null;
                _mainWindow.Show();
            }
            else
            {
                if (_mainWindow.WindowState == FormWindowState.Minimized)
                    _mainWindow.WindowState = FormWindowState.Normal;

                _mainWindow.Show();
                _mainWindow.BringToFront();
                _mainWindow.Activate();
            }

            if (page != null) _mainWindow.NavigateTo(page);
        }

        private void ApplyProfile(string name)
        {
            WorkspaceProfileService.Load(name);
            _trayIcon.ShowBalloonTip(1500, "MotionDesk Studio", string.Format(LocalizationManager.T("Tray.ProfileAppliedFormat"), name), ToolTipIcon.Info);
        }

        private void ToggleWallpaper()
        {
            _wallpaperEnabled = !_wallpaperEnabled;
            _wallpaperToggleItem.Checked = _wallpaperEnabled;

            if (_wallpaperEnabled)
                WallpaperHostEngine.Instance.Enable();
            else
                WallpaperHostEngine.Instance.Disable();
        }

        private void ChooseWallpaperVideo()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = LocalizationManager.T("Filter.Video") + "|" + LocalizationManager.T("Filter.AllFiles"),
                Title = LocalizationManager.T("Wallpaper.AddVideoTitleTray"),
                Multiselect = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                _ = AddVideosAsync(dialog.FileNames);
        }

        // Τα .wmv μετατρέπονται αυτόματα σε .mp4 μέσω FFmpeg (WmvConversionService) πριν
        // προστεθούν — ο ενσωματωμένος player (WebView2/Chromium) δεν έχει decoder για τον παλιό
        // codec WMV3/VC-1. Βλ. αναλυτικό σχόλιο στο WmvConversionService.
        private async Task AddVideosAsync(string[] fileNames)
        {
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
                }
            }

            if (finalPaths.Count == 0) return;
            var settings = WallpaperSettings.Load();
            settings.AddVideoFiles(finalPaths.ToArray());
            settings.Mode = "Video";
            settings.Save();
            if (!_wallpaperEnabled) ToggleWallpaper();
            WallpaperHostEngine.Instance.Enable();
            // Βλ. αναλυτικό σχόλιο στο MainWindow.AddWallpaperVideosAsync — χωρίς αυτό, ένα ήδη
            // ανοιχτό wallpaper window δεν μαθαίνει ποτέ ότι το Mode/playlist άλλαξε.
            _ = WallpaperHostEngine.Instance.RefreshAllAsync();
        }

        private System.Threading.SynchronizationContext _uiContext = null!;
        private System.Windows.Forms.Timer? _alertTimer;
        private int _cpuHighTicks, _ramHighTicks;
        private DateTime _lastAlertUtc = DateTime.MinValue;
        private string? _pendingUpdateUrl;

        // Called every 5 s: CPU or memory >= 90% for 12 consecutive ticks (one minute) -> one tray balloon, then a 10-minute pause
        private void UsageAlertTick()
        {
            try
            {
                if (!AppSettings.Load().UsageAlertsEnabled) { _cpuHighTicks = _ramHighTicks = 0; return; }
                var m = SystemMonitorService.Instance.GetSnapshot();
                double ramPct = m.TotalMemoryMb > 0 ? (m.TotalMemoryMb - m.AvailableMemoryMb) / m.TotalMemoryMb * 100.0 : 0;
                _cpuHighTicks = m.CpuPercent >= 90 ? _cpuHighTicks + 1 : 0;
                _ramHighTicks = ramPct >= 90 ? _ramHighTicks + 1 : 0;
                if (DateTime.UtcNow - _lastAlertUtc < TimeSpan.FromMinutes(10)) return;
                if (_ramHighTicks >= 12)
                {
                    _lastAlertUtc = DateTime.UtcNow; _ramHighTicks = 0;
                    var top = TopProcessesService.TopByMemory(1).FirstOrDefault();
                    _trayIcon.ShowBalloonTip(8000, LocalizationManager.T("Alert.Title"), string.Format(LocalizationManager.T("Alert.RamHigh"), top.Name ?? "?"), ToolTipIcon.Warning);
                }
                else if (_cpuHighTicks >= 12)
                {
                    _lastAlertUtc = DateTime.UtcNow; _cpuHighTicks = 0;
                    _trayIcon.ShowBalloonTip(8000, LocalizationManager.T("Alert.Title"), LocalizationManager.T("Alert.CpuHigh"), ToolTipIcon.Warning);
                }
            }
            catch (Exception) { /* an alert must never break the tray */ }
        }

        private async System.Threading.Tasks.Task CheckForUpdateAsync()
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(25));      // do not compete with the startup work
                var info = await UpdateCheckService.CheckIfDueAsync();
                if (info == null) return;
                _uiContext.Post(_ =>
                {
                    _pendingUpdateUrl = info.Url;
                    _trayIcon.ShowBalloonTip(10000, LocalizationManager.T("Update.Title"), string.Format(LocalizationManager.T("Update.Available"), info.Latest.ToString(3)), ToolTipIcon.Info);
                }, null);
            }
            catch (Exception) { /* offline etc. */ }
        }

        private void OpenPendingUpdate()
        {
            var url = _pendingUpdateUrl;
            if (url == null || !url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { }
            _pendingUpdateUrl = null;
        }

        private void ChooseWallpaperFolder()
        {
            using var dialog = new FolderBrowserDialog { Description = LocalizationManager.T("Wallpaper.ChooseFolder") };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                int added = WallpaperHostEngine.Instance.AddVideoFolder(dialog.SelectedPath);
                if (added > 0 && !_wallpaperEnabled) ToggleWallpaper();
            }
        }

        private static Icon LoadTrayIcon()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "MotionDesk.ico");
            return File.Exists(path) ? new Icon(path) : SystemIcons.Application;
        }

        // Το DeskZones έγινε πραγματικό FancyZones-style στυλ (κανένα μόνιμα ορατό
        // παράθυρο-ζώνη πλέον) — το tray μενού ανοίγει απευθείας τον editor διάταξης.
        private void AddDeskZone()
        {
            using var editor = new ZoneLayoutEditorForm();
            editor.ShowDialog();
        }
    }
}
