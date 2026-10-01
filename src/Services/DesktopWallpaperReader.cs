using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MotionDesk.Services
{
    // Reads the user's CURRENT Windows desktop wallpaper (static image, or the current slideshow frame) so the "Desktop" wallpaper mode
    // can show it and put the clock/date/temperature overlay on top — the overlay then also works over a normal static wallpaper.
    public static class DesktopWallpaperReader
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, StringBuilder pvParam, uint fWinIni);
        private const uint SPI_GETDESKWALLPAPER = 0x0073;

        public sealed record Info(string? Uri, string Style, string Color, long Stamp);

        public static Info Read()
        {
            string? uri = null; long stamp = 0;
            try
            {
                var sb = new StringBuilder(520);
                if (SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
                {
                    var path = sb.ToString();
                    // Slideshow / Spotlight: the API can return an empty path — Windows keeps the current frame in TranscodedWallpaper
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        var transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
                        if (File.Exists(transcoded)) path = transcoded;
                    }
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        stamp = File.GetLastWriteTimeUtc(path).Ticks;
                        uri = new Uri(path).AbsoluteUri + "?v=" + stamp;     // the stamp busts the cache when a slideshow changes the file
                    }
                }
            }
            catch (Exception) { /* no wallpaper / access problem -> solid colour below */ }

            string style = "Fill";
            try
            {
                using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                var ws = desktop?.GetValue("WallpaperStyle")?.ToString() ?? "10";
                var tile = desktop?.GetValue("TileWallpaper")?.ToString() ?? "0";
                style = ws switch { "10" => "Fill", "6" => "Fit", "2" => "Stretch", "22" => "Span", _ => tile == "1" ? "Tile" : "Center" };
            }
            catch (Exception) { }

            string color = "#101418";
            try
            {
                using var colors = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
                var parts = (colors?.GetValue("Background")?.ToString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 && int.TryParse(parts[0], out var r) && int.TryParse(parts[1], out var g) && int.TryParse(parts[2], out var b))
                    color = $"#{Math.Clamp(r, 0, 255):x2}{Math.Clamp(g, 0, 255):x2}{Math.Clamp(b, 0, 255):x2}";
            }
            catch (Exception) { }

            return new Info(uri, style, color, stamp);
        }
    }
}
