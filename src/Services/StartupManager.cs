using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace MotionDesk.Services
{
    public static class StartupManager
    {
        private const string AppName = "MotionDeskStudio";

        public static void SetStartup(bool enable)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (enable)
                    {
                        string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                        if (key != null && exePath != null)
                        {
                            // "--background": ζητήθηκε ρητά ότι όταν η εφαρμογή έχει αποθηκευμένα
                            // widgets/DeskContainers, η αυτόματη εκκίνηση με τα Windows πρέπει να
                            // γίνεται στο παρασκήνιο (μόνο tray icon) αντί να αναδύεται πάντα το
                            // κύριο παράθυρο διαχείρισης — βλ. TrayApplicationContext.
                            key.SetValue(AppName, $"\"{exePath}\" --background");
                        }
                    }
                    else
                    {
                        key?.DeleteValue(AppName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Σφάλμα κατά τη ρύθμιση Startup: {ex.Message}");
            }
        }

        public static bool IsStartupEnabled()
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
            {
                return key?.GetValue(AppName) != null;
            }
        }
    }
}
