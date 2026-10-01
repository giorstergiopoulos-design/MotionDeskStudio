using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MotionDesk.Services
{
    // Πραγματική λύση για το .wmv (όχι μόνο προειδοποίηση): μετατρέπει αυτόματα σε .mp4 (H.264/
    // AAC) μέσω FFmpeg πριν προστεθεί στο Wallpaper Studio, αφού το ενσωματωμένο player (WebView2/
    // Chromium) δεν έχει decoder για τον παλιό codec WMV3/VC-1. Επιλέχθηκε FFmpeg (εξωτερική
    // διεργασία, όχι COM) αντί για το legacy Windows Media Player ActiveX control ή για χειροποίητο
    // Media Foundation COM interop — και τα δύο θα απαιτούσαν εύθραυστο, μη-επαληθεύσιμο κώδικα
    // (ακριβές vtable/typelib interop) χωρίς πραγματική επιφάνεια εργασίας Windows διαθέσιμη εδώ
    // για ζωντανό έλεγχο· το FFmpeg είναι ο βιομηχανικός κανόνας γι' ακριβώς αυτή τη μετατροπή,
    // με σταθερό, καλά τεκμηριωμένο command-line interface.
    public static class WmvConversionService
    {
        private static string DefaultCacheDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MotionDeskStudio", "converted");

        // Ο χρήστης μπορεί να επιλέξει δικό του φάκελο αποθήκευσης για ΟΛΑ τα μετατρεπόμενα
        // βίντεο (ζητήθηκε ρητά "να έχει επιλογή ο χρήστης το path αποθήκευσης") — προεπιλογή
        // παραμένει ο φάκελος της εφαρμογής στο AppData.
        public static string CacheDir
        {
            get
            {
                var custom = AppSettings.Load().WmvConversionOutputDir;
                return !string.IsNullOrWhiteSpace(custom) && Directory.Exists(Path.GetPathRoot(custom) ?? custom)
                    ? custom
                    : DefaultCacheDir;
            }
        }

        private static string? _cachedFfmpegPath;

        public static string? FindFfmpeg()
        {
            if (_cachedFfmpegPath != null) return _cachedFfmpegPath;

            // 1) Ήδη στο PATH του συστήματος (π.χ. εγκαταστάθηκε από τον χρήστη ή μέσω winget).
            if (TryProbe("ffmpeg")) { _cachedFfmpegPath = "ffmpeg"; return _cachedFfmpegPath; }

            // 2) Προαιρετικό τοπικό αντίγραφο δίπλα στην εφαρμογή (ο χρήστης το τοποθέτησε ο
            // ίδιος, ή μελλοντικός installer bundle) — ΔΕΝ κατεβάζουμε τίποτα αυτόματα εμείς.
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe");
            if (File.Exists(local)) { _cachedFfmpegPath = local; return _cachedFfmpegPath; }

            // 3) WinGet εγκατάσταση (π.χ. "winget install Gyan.FFmpeg") — δεν βασιζόμαστε στο ΝΑ
            // έχει ήδη ανανεωθεί το PATH της τρέχουσας διεργασίας (οι μεταβλητές περιβάλλοντος
            // ΔΕΝ ανανεώνονται αυτόματα σε ήδη-τρέχουσες διεργασίες μετά από winget install, μόνο
            // σε νέες που ξεκινούν αφού το Explorer λάβει το WM_SETTINGCHANGE broadcast) — ψάχνουμε
            // απευθείας τους δύο γνωστούς φακέλους: το "shim" του WinGet (Links) και τον πραγματικό
            // φάκελο πακέτου (Packages\Gyan.FFmpeg_...\...\bin\ffmpeg.exe).
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string wingetLink = Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe");
            if (File.Exists(wingetLink)) { _cachedFfmpegPath = wingetLink; return _cachedFfmpegPath; }

            string wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(wingetPackages))
            {
                try
                {
                    string? found = Directory.EnumerateFiles(wingetPackages, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (found != null) { _cachedFfmpegPath = found; return _cachedFfmpegPath; }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            return null;
        }

        private static bool TryProbe(string exe)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, "-version")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                if (p == null) return false;
                return p.WaitForExit(2000) && p.ExitCode == 0;
            }
            catch { return false; }
        }

        public static bool IsFfmpegAvailable => FindFfmpeg() != null;

        // Ανοίγει τη σελίδα λήψης σε browser — ΔΕΝ κατεβάζουμε/εκτελούμε τίποτα εμείς οι ίδιοι,
        // ο χρήστης εγκαθιστά το FFmpeg με δική του ρητή ενέργεια.
        // Κοινός, τοπικοποιημένος διάλογος "λείπει το FFmpeg" για όλα τα σημεία εισαγωγής .wmv.
        public static void PromptInstallFfmpeg(int wmvCount)
        {
            var choice = MessageBox.Show(
                string.Format(LocalizationManager.T("Dialog.FfmpegMissingFormat"), wmvCount),
                LocalizationManager.T("Dialog.FfmpegTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (choice == DialogResult.Yes) OpenFfmpegDownloadPage();
        }

        public static void OpenFfmpegDownloadPage()
        {
            try { Process.Start(new ProcessStartInfo("https://www.gyan.dev/ffmpeg/builds/") { UseShellExecute = true }); }
            catch { }
        }

        // Μετατρέπει ένα .wmv σε .mp4, με cache βάσει μεγέθους+ημερομηνίας αρχείου ώστε να μην
        // ξαναμετατρέπεται το ίδιο αρχείο σε κάθε επανεκκίνηση της εφαρμογής.
        public static async Task<string?> ConvertToMp4Async(string wmvPath)
        {
            string? ffmpeg = FindFfmpeg();
            if (ffmpeg == null || !File.Exists(wmvPath)) return null;

            Directory.CreateDirectory(CacheDir);
            string outputPath = Path.Combine(CacheDir, $"{Path.GetFileNameWithoutExtension(wmvPath)}_{QuickHash(wmvPath)}.mp4");
            if (File.Exists(outputPath)) return outputPath;

            // Γράφουμε πρώτα σε προσωρινό αρχείο και μετονομάζουμε μόνο σε επιτυχία — αλλιώς μια
            // διακοπή/αποτυχία άφηνε μισό .mp4 που το cache (File.Exists) θα επέστρεφε για πάντα
            // ως "έτοιμο". Το -pix_fmt yuv420p και το scale σε ζυγές διαστάσεις είναι απαραίτητα
            // για libx264 (αλλιώς αποτυγχάνει σε WMV με περιττό πλάτος/ύψος ή μη-4:2:0 pixel format).
            string tempPath = outputPath + ".partial.mp4";
            var psi = new ProcessStartInfo(ffmpeg,
                $"-y -i \"{wmvPath}\" -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" -pix_fmt yuv420p " +
                $"-c:v libx264 -preset veryfast -crf 23 -c:a aac -movflags +faststart \"{tempPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                // ΚΡΙΣΙΜΟ: το FFmpeg γράφει συνεχώς πρόοδο στο stderr. Αν το pipe δεν διαβάζεται, γεμίζει
                // (~4KB) και το FFmpeg μπλοκάρει για πάντα — τα μεγάλα .wmv "κόλλαγαν" χωρίς σφάλμα.
                var errTask = proc.StandardError.ReadToEndAsync();
                var outTask = proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                await Task.WhenAll(errTask, outTask);

                if (proc.ExitCode == 0 && File.Exists(tempPath))
                {
                    File.Move(tempPath, outputPath, overwrite: true);
                    return outputPath;
                }
                return null;
            }
            catch { return null; }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        private static string QuickHash(string path)
        {
            try
            {
                var info = new FileInfo(path);
                string raw = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                using var sha = System.Security.Cryptography.SHA1.Create();
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return Convert.ToHexString(bytes)[..10];
            }
            catch { return Guid.NewGuid().ToString("N")[..10]; }
        }
    }
}
