using System;
using System.IO;
using System.Text;

namespace MotionDesk.Services
{
    // Small rolling log of the wallpaper "attach behind the desktop icons" steps (%APPDATA%\MotionDeskStudio\wallpaper-attach.log).
    // Purpose: the intermittent "wallpaper/icons not always visible right after a Windows boot" report can only be diagnosed with data —
    // each line records the screen, the result, how long the attach took and the system uptime (to correlate with slow boots).
    public static class AttachLog
    {
        private const long MaxBytes = 96 * 1024;
        private static readonly object Gate = new();
        private static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MotionDeskStudio", "wallpaper-attach.log");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    var path = LogPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        // keep the newest half
                        var all = File.ReadAllText(path, Encoding.UTF8);
                        File.WriteAllText(path, all[(all.Length / 2)..], Encoding.UTF8);
                    }
                    File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  uptime={Environment.TickCount64 / 1000}s  {message}{Environment.NewLine}", Encoding.UTF8);
                }
            }
            catch (Exception) { /* diagnostics must never break the wallpaper */ }
        }
    }
}
