using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace MotionDesk.Services
{
    public static class StartupManager
    {
        private const string AppName = "Opsis";
        // Από τη μετονομασία "MotionDesk Studio" -> "Opsis" — καθαρίζεται αυτόματα ώστε να μην
        // μείνει για πάντα μια νεκρή εγγραφή Run που δείχνει στο παλιό MotionDesk.exe.
        private const string LegacyAppName = "MotionDeskStudio";

        public static void SetStartup(bool enable)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    key?.DeleteValue(LegacyAppName, false);
                    if (enable)
                    {
                        string? exePath = Environment.ProcessPath;
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
                return key?.GetValue(AppName) != null || key?.GetValue(LegacyAppName) != null;
            }
        }
    }
}
