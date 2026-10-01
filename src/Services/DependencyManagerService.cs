using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MotionDesk.Services
{
    // Κεντρικό σημείο για τα εξωτερικά dependencies της εφαρμογής — ζητήθηκε ρητά "να εμφανιζεται
    // η δυνατοτητα εγκαταστασης dependencies οπως το ffmpeg... δες ποια dependencies υπαρχουν και
    // βαλε και αυτες στον installer αλλα και στις ρυθμισεις της εφαρμογης". Ο installer (.iss) τα
    // εγκαθιστά μία φορά κατά την εγκατάσταση· αυτή η κλάση επιτρέπει ΚΑΙ μια δεύτερη, χειροκίνητη
    // εγκατάσταση/επανεγκατάσταση αργότερα μέσα από τη σελίδα Ρυθμίσεις, χωρίς επανεκτέλεση του
    // installer (π.χ. αν ο χρήστης παρέλειψε το FFmpeg την πρώτη φορά, ή το WebView2 Runtime
    // ενημερώθηκε/αφαιρέθηκε από τα Windows μετά την εγκατάσταση).
    public enum DependencyStatus { Installed, Missing, Checking }

    public sealed class DependencyInfo
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string DescriptionKey { get; init; } = "";
        // Υπολογίζεται σε κάθε ανάγνωση — το All είναι static και αρχικοποιείται μία φορά, οπότε ένα σταθερό
        // string θα έμενε στη γλώσσα της πρώτης προσπέλασης ακόμη κι αν ο χρήστης άλλαζε γλώσσα.
        public string Description => LocalizationManager.T(DescriptionKey);
        public Func<bool> IsInstalled { get; init; } = () => false;

        // WinGet package id για αυτόματη εγκατάσταση/ενημέρωση, ή null αν δεν υποστηρίζεται.
        public string? WingetId { get; init; }
    }

    public static class DependencyManagerService
    {
        // Οι δύο εξωτερικές εξαρτήσεις που ήδη ελέγχει/εγκαθιστά ο installer (installer.iss) —
        // εδώ αναπαράγονται τα ίδια ακριβώς κριτήρια ανίχνευσης ώστε η σελίδα Ρυθμίσεις να δείχνει
        // την ΠΡΑΓΜΑΤΙΚΗ τρέχουσα κατάσταση του συστήματος, όχι μόνο τη στιγμή της εγκατάστασης.
        public static readonly DependencyInfo[] All =
        {
            new DependencyInfo
            {
                Id = "webview2",
                Name = "Microsoft Edge WebView2 Runtime",
                DescriptionKey = "Dependency.WebView2Desc",
                IsInstalled = IsWebView2Installed,
                WingetId = "Microsoft.EdgeWebView2Runtime"
            },
            new DependencyInfo
            {
                Id = "ffmpeg",
                Name = "FFmpeg",
                DescriptionKey = "Dependency.FfmpegDesc",
                IsInstalled = () => WmvConversionService.IsFfmpegAvailable,
                WingetId = "Gyan.FFmpeg"
            }
        };

        // Ίδιος ακριβώς έλεγχος με τη συνάρτηση IsWebView2Installed του installer.iss (Pascal
        // script) — το ίδιο registry key που γράφει το Evergreen bootstrapper.
        public static bool IsWebView2Installed()
        {
            try
            {
                const string keyPath = @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
                using var hklm = Registry.LocalMachine.OpenSubKey(keyPath);
                if (hklm != null) return true;
                using var hkcu = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
                return hkcu != null;
            }
            catch (System.Security.SecurityException) { return false; }
        }

        public static bool IsWingetAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo("winget", "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                return p != null && p.WaitForExit(3000) && p.ExitCode == 0;
            }
            catch { return false; }
        }

        // Τρέχει ένα "winget install" σιωπηλά για το δοσμένο package id. Επιστρέφει true μόνο αν
        // η διεργασία τερμάτισε με ExitCode 0. Ζητά αυτόματα αποδοχή των συμφωνιών πηγής/πακέτου
        // ώστε να μην κολλήσει περιμένοντας διαδραστική επιβεβαίωση.
        public static async Task<(bool Success, string Output)> InstallViaWingetAsync(string wingetId)
        {
            if (!IsWingetAvailable())
                return (false, LocalizationManager.T("Dependency.WingetMissing"));

            var psi = new ProcessStartInfo("winget",
                $"install --id {wingetId} -e --silent --accept-package-agreements --accept-source-agreements")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null) return (false, LocalizationManager.T("Dependency.WingetStartFailed"));
                // Ταυτόχρονη ανάγνωση: η διαδοχική (πρώτα stdout, μετά stderr) μπλόκαρε αν γέμιζε πρώτα το stderr.
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                string stdout = await stdoutTask;
                string stderr = await stderrTask;
                await proc.WaitForExitAsync();
                WmvConversionService.ResetDetection();
                return (proc.ExitCode == 0, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
            }
            catch (Exception ex) { return (false, ex.Message); }
        }
    }
}
