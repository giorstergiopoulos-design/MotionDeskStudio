using System;
using System.IO;
using System.Text.Json;

namespace MotionDesk.Services
{
    public sealed class AppSettings
    {
        public bool GridSnap { get; set; }
        public int SnapThreshold { get; set; } = 15;
        public int GridSize { get; set; } = 20;
        public bool AutoPerformanceMode { get; set; } = true;
        public string WallpaperPerformanceMode { get; set; } = "Balanced";
        public bool ShowSystemMonitor { get; set; }
        public bool RestoreLastSession { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        public bool StartWallpaperWithWindows { get; set; }
        public bool EnableAnimations { get; set; } = true;
        public string ThemeMode { get; set; } = "Follow"; // Follow | Light | Dark
        public string Language { get; set; } = "Follow";  // Follow | el | en
        public double WindowOpacity { get; set; } = 1.0;  // 0.6 - 1.0, εφαρμόζεται στο κύριο παράθυρο
        public double DarkIntensity { get; set; } = 0.5;  // 0 (πιο ανοιχτό σκούρο) - 1 (σχεδόν μαύρο), 0.5 = η αρχική βάση
        public bool UiSoundsEnabled { get; set; }
        public bool SidebarCollapsed { get; set; }

        private static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MotionDeskStudio", "settings.json");
        public static AppSettings Load()
        {
            try { if (File.Exists(ConfigPath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath)) ?? new AppSettings(); }
            catch (JsonException) { }
            catch (IOException) { }
            return new AppSettings();
        }
        public void Save()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!); File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
            catch (IOException) { }
        }
    }

    public static class PerformanceModeManager
    {
        public static string GetRecommendedMode()
        {
            var power = System.Windows.Forms.SystemInformation.PowerStatus;
            if (power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline)
                return "Battery";
            if (power.BatteryLifePercent >= 0 && power.BatteryLifePercent < 0.30f)
                return "Low Power";
            return "Balanced";
        }
    }
}
