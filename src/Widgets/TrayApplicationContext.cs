using System;
using System.Drawing;
using System.IO;
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
            contextMenu.Items.Add("Άνοιγμα MotionDesk Studio", null, (s, e) => ShowMainWindow());
            contextMenu.Items.Add("Command Palette...", null, (s, e) => { ShowMainWindow(); _mainWindow?.OpenCommandPalette(); });
            contextMenu.Items.Add("Ρυθμίσεις...", null, (s, e) => ShowMainWindow("Settings"));
            contextMenu.Items.Add(new ToolStripSeparator());

            var profilesMenu = new ToolStripMenuItem("Γρήγορο Προφίλ");
            profilesMenu.DropDownItems.Add("Work", null, (s, e) => ApplyProfile("Work"));
            profilesMenu.DropDownItems.Add("Gaming", null, (s, e) => ApplyProfile("Gaming"));
            profilesMenu.DropDownItems.Add("Focus", null, (s, e) => ApplyProfile("Focus"));
            contextMenu.Items.Add(profilesMenu);
            contextMenu.Items.Add(new ToolStripSeparator());

            contextMenu.Items.Add("DeskZones — Επεξεργασία διάταξης…", null, (s, e) => AddDeskZone());
            contextMenu.Items.Add(new ToolStripSeparator());

            _wallpaperToggleItem = new ToolStripMenuItem("Ζωντανή Επιφάνεια Εργασίας (Wallpaper)");
            _wallpaperToggleItem.Click += (s, e) => ToggleWallpaper();
            contextMenu.Items.Add(_wallpaperToggleItem);
            contextMenu.Items.Add("Προσθήκη βίντεο στο Wallpaper...", null, (s, e) => ChooseWallpaperVideo());
            contextMenu.Items.Add("Προσθήκη φακέλου στο Wallpaper...", null, (s, e) => ChooseWallpaperFolder());
            contextMenu.Items.Add(new ToolStripSeparator());

            contextMenu.Items.Add("Flip 3D  (Ctrl+Alt+F)", null, (s, e) => Flip3DEngine.Show());

            ToolStripMenuItem gridSnapItem = new ToolStripMenuItem("Ενεργοποίηση Grid Snap");
            gridSnapItem.Checked = WidgetSnapEngine.EnableGridSnap;
            gridSnapItem.Click += (s, e) => {
                WidgetSnapEngine.EnableGridSnap = !gridSnapItem.Checked;
                gridSnapItem.Checked = WidgetSnapEngine.EnableGridSnap;
            };
            contextMenu.Items.Add(gridSnapItem);

            ToolStripMenuItem startupItem = new ToolStripMenuItem("Αυτόματη Εκκίνηση (Startup)");
            startupItem.Checked = StartupManager.IsStartupEnabled();
            startupItem.Click += (s, e) => {
                bool newState = !startupItem.Checked;
                StartupManager.SetStartup(newState);
                startupItem.Checked = newState;
            };
            contextMenu.Items.Add(startupItem);

            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Έξοδος", null, (s, e) => {
                _hotkeys.Dispose();
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

            _hotkeys.RegisterCtrlAlt('F', () => Flip3DEngine.Show());

            // Restore lightweight persistent preferences before showing the manager.
            var appSettings = AppSettings.Load();
            WidgetSnapEngine.EnableGridSnap = appSettings.GridSnap;
            WidgetSnapEngine.SnapThreshold = Math.Clamp(appSettings.SnapThreshold, 5, 50);
            WidgetSnapEngine.GridSize = Math.Clamp(appSettings.GridSize, 5, 100);
            _wallpaperEnabled = WallpaperHostEngine.Instance.IsEnabled;

            if (appSettings.StartWallpaperWithWindows)
            {
                WallpaperHostEngine.Instance.Enable();
                _wallpaperEnabled = true;
            }

            ShowMainWindow();
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
            _trayIcon.ShowBalloonTip(1500, "MotionDesk Studio", $"Εφαρμόστηκε το προφίλ: {name}", ToolTipIcon.Info);
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
                Filter = "Video (*.mp4;*.m4v;*.webm;*.mov;*.ogv;*.ogg;*.avi;*.mkv;*.wmv;*.mpeg;*.mpg;*.m2ts;*.ts)|*.mp4;*.m4v;*.webm;*.mov;*.ogv;*.ogg;*.avi;*.mkv;*.wmv;*.mpeg;*.mpg;*.m2ts;*.ts|Όλα τα αρχεία (*.*)|*.*",
                Title = "Προσθήκη βίντεο στο Wallpaper Library",
                Multiselect = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                var settings = WallpaperSettings.Load();
                settings.AddVideoFiles(dialog.FileNames);
                settings.Mode = "Video";
                settings.Save();
                if (!_wallpaperEnabled) ToggleWallpaper();
                WallpaperHostEngine.Instance.Enable();
            }
        }

        private void ChooseWallpaperFolder()
        {
            using var dialog = new FolderBrowserDialog { Description = "Επιλογή φακέλου video library" };
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
