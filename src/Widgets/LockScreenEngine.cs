using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace MotionDesk.Widgets
{
    // Φόντο οθόνης κλειδώματος — επίσημος μηχανισμός Personalization CSP
    // (HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP), ο ίδιος που
    // χρησιμοποιούν εργαλεία MDM/Intune για να ορίσουν lock screen image. Απαιτεί εγγραφή σε
    // HKLM, άρα δικαιώματα διαχειριστή — αντί να απαιτεί ολόκληρη η εφαρμογή elevated εκκίνηση,
    // ανοίγει έναν ελάχιστο elevated "helper" (η ίδια η exe, με κρυφό όρισμα γραμμής εντολών) ΜΟΝΟ
    // για αυτή τη μία εγγραφή registry — βλ. Program.cs. Σε Windows Home edition αυτό το κλειδί
    // μπορεί να αγνοηθεί από το shell (η CSP/policy μηχανή είναι πιο περιορισμένη εκεί)· η οθόνη
    // το αναφέρει καθαρά αντί να προσποιείται εγγύηση.
    internal static class LockScreenEngine
    {
        private const string PersonalizationCspKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";

        public static string GetWindowsEdition()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                return key?.GetValue("EditionID") as string ?? "Unknown";
            }
            catch (Exception) { return "Unknown"; }
        }

        public static bool IsHomeEdition() =>
            GetWindowsEdition().Contains("Core", StringComparison.OrdinalIgnoreCase);

        public static string? GetCurrentLockScreenImagePath()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(PersonalizationCspKey);
                return key?.GetValue("LockScreenImagePath") as string;
            }
            catch (Exception) { return null; }
        }

        // Καλείται ΜΟΝΟ από τον ελάχιστο elevated helper process (Program.cs --set-lockscreen) —
        // ποτέ απευθείας από το κύριο, μη-elevated UI thread.
        public static void WriteLockScreenRegistry(string imagePath)
        {
            using var key = Registry.LocalMachine.CreateSubKey(PersonalizationCspKey);
            key?.SetValue("LockScreenImagePath", imagePath, RegistryValueKind.String);
            key?.SetValue("LockScreenImageUrl", imagePath, RegistryValueKind.String);
            key?.SetValue("LockScreenImageStatus", 1, RegistryValueKind.DWord);
        }

        // Καλείται από το κύριο UI — ανοίγει elevated instance της ίδιας exe μόνο για αυτή την
        // ενέργεια (εμφανίζει το πραγματικό UAC prompt των Windows), περιμένει να τελειώσει, μετά
        // διαβάζει πίσω την τιμή για να επιβεβαιώσει αν πέτυχε.
        public static bool TrySetLockScreenImageElevated(string imagePath, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                string exePath = Environment.ProcessPath!;
                var psi = new ProcessStartInfo(exePath, $"--set-lockscreen \"{imagePath}\"")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(15000);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                errorMessage = "cancelled"; // ο χρήστης αρνήθηκε το UAC prompt
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }

            return string.Equals(GetCurrentLockScreenImagePath(), imagePath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
