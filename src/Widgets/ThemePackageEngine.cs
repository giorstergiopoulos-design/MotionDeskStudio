using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MotionDesk.Widgets
{
    // Πακέτα θέματος (.theme) — ο επίσημος μηχανισμός των ίδιων των Windows: ένα .theme αρχείο
    // δένει μαζί wallpaper + δείκτη ποντικιού + σχήμα ήχων + χρώμα έμφασης, και εφαρμόζεται με ένα
    // διπλό-κλικ μέσω του ήδη υπάρχοντος OS handler (rundll32 themecpl.dll) — καμία δική μας
    // λογική εφαρμογής θέματος, μόνο δημιουργία/απαρίθμηση αρχείων .theme. Σκόπιμα ΔΕΝ αγγίζει
    // την ενότητα [VisualStyles] πέρα από το ήδη υπογεγραμμένο προεπιλεγμένο styles αρχείο του
    // συστήματος — πλήρες visual-style engine εξετάστηκε και αποκλείστηκε ρητά (risky tier).
    internal static class ThemePackageEngine
    {
        public static string UserThemesFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Themes");

        private static string BuiltInThemesFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Resources", "Themes");

        public sealed record ThemeEntry(string Name, string Path, bool BuiltIn);

        public static List<ThemeEntry> ListAvailableThemes()
        {
            var list = new List<ThemeEntry>();
            try
            {
                if (Directory.Exists(BuiltInThemesFolder))
                    foreach (var f in Directory.EnumerateFiles(BuiltInThemesFolder, "*.theme"))
                        list.Add(new ThemeEntry(System.IO.Path.GetFileNameWithoutExtension(f), f, true));
            }
            catch (IOException) { }
            try
            {
                if (Directory.Exists(UserThemesFolder))
                    foreach (var f in Directory.EnumerateFiles(UserThemesFolder, "*.theme"))
                        list.Add(new ThemeEntry(System.IO.Path.GetFileNameWithoutExtension(f), f, false));
            }
            catch (IOException) { }
            return list.OrderBy(t => t.BuiltIn ? 0 : 1).ThenBy(t => t.Name).ToList();
        }

        // Άνοιγμα ενός .theme αρχείου μέσω του δικού του OS association — ό,τι ακριβώς συμβαίνει
        // όταν ο χρήστης κάνει διπλό-κλικ σε ένα .theme μέσα στο Explorer.
        public static void ApplyTheme(string themeFilePath)
        {
            Process.Start(new ProcessStartInfo(themeFilePath) { UseShellExecute = true });
        }

        public static string SaveCurrentAsTheme(string themeName)
        {
            Directory.CreateDirectory(UserThemesFolder);
            string safeName = string.Join("_", themeName.Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(UserThemesFolder, $"{safeName}.theme");

            var sb = new StringBuilder();
            sb.AppendLine("; Δημιουργήθηκε από το MotionDesk Studio — IconAtlas/DeskCursors/DeskSounds snapshot");
            sb.AppendLine("[Theme]");
            sb.AppendLine($"DisplayName={themeName}");
            sb.AppendLine();

            sb.AppendLine("[Control Panel\\Desktop]");
            string wallpaper = GetCurrentDesktopWallpaper();
            if (!string.IsNullOrEmpty(wallpaper)) sb.AppendLine($"Wallpaper={wallpaper}");
            sb.AppendLine("WallpaperStyle=10");
            sb.AppendLine("TileWallpaper=0");
            sb.AppendLine();

            sb.AppendLine("[Control Panel\\Cursors]");
            using (var cursorsKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors"))
            {
                if (cursorsKey != null)
                {
                    foreach (var name in cursorsKey.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        if (cursorsKey.GetValue(name) is string v && !string.IsNullOrEmpty(v))
                            sb.AppendLine($"{name}={v}");
                    }
                }
            }
            sb.AppendLine();

            sb.AppendLine("[Sounds]");
            string? soundScheme = GetCurrentSoundSchemeName();
            if (!string.IsNullOrEmpty(soundScheme)) sb.AppendLine($"SchemeName={soundScheme}");
            sb.AppendLine();

            sb.AppendLine("[VisualStyles]");
            sb.AppendLine(@"Path=%SystemRoot%\resources\Themes\aero\aero.msstyles");
            sb.AppendLine("ColorStyle=NormalColor");
            sb.AppendLine("Size=NormalSize");

            File.WriteAllText(path, sb.ToString(), Encoding.Unicode);
            return path;
        }

        private static string? GetCurrentSoundSchemeName()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes");
            return key?.GetValue(null) as string;
        }

        private static string GetCurrentDesktopWallpaper()
        {
            var sb = new StringBuilder(260);
            SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0);
            return sb.ToString();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, int uiParam, StringBuilder pvParam, uint fWinIni);
        private const uint SPI_GETDESKWALLPAPER = 0x0073;
    }
}
