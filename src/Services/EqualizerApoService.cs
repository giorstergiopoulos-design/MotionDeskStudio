using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MotionDesk.Services
{
    // ΠΡΑΓΜΑΤΙΚΗ, system-wide ενίσχυση ήχου — ζητήθηκε ρητά μετά από έρευνα στο πραγματικό FXSound
    // repo (github.com/fxsound2/fxsound-app): επιβεβαιώθηκε (WebFetch στο ίδιο το repo) ότι ΑΚΟΜΑ
    // ΚΙ ΕΚΕΙ η πραγματική επεξεργασία system-wide ήχου γίνεται μέσω ΞΕΧΩΡΙΣΤΟΥ, ΚΛΕΙΣΤΟΥ virtual
    // audio driver ("FxSound Audio Enhancer virtual audio driver") — ΔΕΝ περιλαμβάνεται καν στο
    // ίδιο το open-source repo τους. Ένα τέτοιο driver χρειάζεται kernel-mode driver signing
    // (ακόμα πιο απαιτητικό από απλό code-signing certificate) — εκτός εμβέλειας εδώ.
    //
    // Η ΠΡΑΓΜΑΤΙΚΗ, εφικτή λύση (βρέθηκε μέσω έρευνας παρόμοιων GitHub projects, π.χ.
    // github.com/psidex/EACS): το Equalizer APO (equalizerapo.sourceforge.io) είναι ένα ΗΔΗ
    // δωρεάν, ανοιχτού κώδικα, ήδη-υπογεγραμμένο, ευρέως-εμπιστευμένο (εκατομμύρια downloads)
    // πραγματικό Windows Audio Processing Object — τρέχει ΜΕΣΑ στο audiodg.exe του ίδιου του
    // Windows, system-wide, ΧΩΡΙΣ κανένα virtual driver. Ελέγχεται απλά γράφοντας το δικό του
    // config.txt (ίδιο ακριβώς μοτίβο ενσωμάτωσης με FFmpeg/7-Zip σε αυτό το project: ανίχνευση
    // εξωτερικού, ήδη-λυμένου εργαλείου αντί να ξαναγραφτεί ο τροχός). Δεν αγγίζουμε το υπόλοιπο
    // config.txt του χρήστη — προσθέτουμε ΜΟΝΟ μία γραμμή "Include: MotionDesk.txt" (αν λείπει)
    // και γράφουμε το δικό μας preset σε ΞΕΧΩΡΙΣΤΟ αρχείο.
    public static class EqualizerApoService
    {
        private static string ConfigDir => @"C:\Program Files\EqualizerAPO\config";
        private static string ConfigFile => Path.Combine(ConfigDir, "config.txt");
        private static string MotionDeskIncludeFile => Path.Combine(ConfigDir, "MotionDesk.txt");
        private const string IncludeLine = "Include: MotionDesk.txt";

        public static bool IsInstalled => Directory.Exists(ConfigDir) && File.Exists(ConfigFile);

        public static void OpenDownloadPage()
        {
            try { Process.Start(new ProcessStartInfo("https://equalizerapo.sourceforge.io/") { UseShellExecute = true }); }
            catch { }
        }

        // Γράφει το preset ως πραγματικά parametric EQ filters (Equalizer APO syntax) στο δικό
        // μας MotionDesk.txt, και εγγυάται ότι το config.txt το συμπεριλαμβάνει (Include) — δεν
        // πειράζει τίποτα άλλο που έχει ήδη ο χρήστης στο config.txt του.
        public static (bool Success, string Message) ApplyPreset(string presetName)
        {
            if (!IsInstalled) return (false, "Equalizer APO δεν εντοπίστηκε.");

            string filters = presetName switch
            {
                "Bass Boost" => "Preamp: -3 dB\nFilter 1: ON LSC Fc 120 Hz Gain 7.0 dB Q 0.70\n",
                "Treble Boost" => "Preamp: -3 dB\nFilter 1: ON HSC Fc 6500 Hz Gain 6.0 dB Q 0.70\n",
                "Vocal Boost" => "Preamp: -2 dB\nFilter 1: ON PK Fc 2500 Hz Gain 5.0 dB Q 1.0\n",
                "Loudness" => "Preamp: -4 dB\nFilter 1: ON LSC Fc 100 Hz Gain 6.0 dB Q 0.70\nFilter 2: ON HSC Fc 9000 Hz Gain 5.0 dB Q 0.70\n",
                _ => "Preamp: 0 dB\n", // "Flat"
            };

            try
            {
                File.WriteAllText(MotionDeskIncludeFile,
                    "// Δημιουργήθηκε από το MotionDesk Studio (Audio Enhancement) — μη το επεξεργάζεστε χειροκίνητα, αντικαθίσταται σε κάθε αλλαγή preset.\n" + filters);

                string existing = File.ReadAllText(ConfigFile);
                if (!existing.Split('\n').Select(l => l.Trim()).Contains(IncludeLine))
                {
                    string sep = existing.Length > 0 && !existing.EndsWith("\n") ? "\n" : "";
                    File.AppendAllText(ConfigFile, sep + IncludeLine + "\n");
                }
                return (true, "OK");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "Χωρίς δικαίωμα εγγραφής στον φάκελο του Equalizer APO — δοκιμάστε εκτέλεση ως διαχειριστής μία φορά, ή ελέγξτε τα δικαιώματα του φακέλου.");
            }
            catch (IOException ex) { return (false, ex.Message); }
        }
    }
}
