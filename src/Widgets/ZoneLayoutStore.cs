using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace MotionDesk.Widgets
{
    // Ζώνη ως ΚΑΝΟΝΙΚΟΠΟΙΗΜΕΝΟ (0..1) ορθογώνιο πάνω στο working area μιας οθόνης — όχι απόλυτα
    // pixels, ώστε το ίδιο layout να έχει νόημα σε οποιαδήποτε ανάλυση/μέγεθος οθόνης.
    public sealed class ZoneRect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public Rectangle ToAbsolute(Rectangle workingArea) => new(
            workingArea.X + (int)(X * workingArea.Width),
            workingArea.Y + (int)(Y * workingArea.Height),
            (int)(Width * workingArea.Width),
            (int)(Height * workingArea.Height));
    }

    public sealed class ZoneLayoutData
    {
        // No layout | Focus | Columns | Rows | Grid | PriorityGrid | Custom
        public string Template { get; set; } = "Rows";
        public List<ZoneRect> Zones { get; set; } = new();
        // "WxH" of the monitor this layout was made for — lets a layout follow its monitor when Windows renumbers \\.\DISPLAYn after a reconnect
        public string? Resolution { get; set; }
    }

    // Πραγματική συμπεριφορά στυλ FancyZones (PowerToys), όχι Fences: τα layouts είναι ΔΕΔΟΜΕΝΑ
    // ανά οθόνη (κανένα μόνιμα ορατό παράθυρο-ζώνη στην επιφάνεια εργασίας) — ζητήθηκε ρητά μετά
    // από screenshots του πραγματικού FancyZones Editor. Ένα ελαφρύ, ημιδιάφανο overlay
    // (ZoneOverlayWindow) εμφανίζεται ΜΟΝΟ όσο κρατάς Shift ενώ σέρνεις ένα παράθυρο.
    public static class ZoneLayoutStore
    {
        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MotionDeskStudio", "zonelayouts.json");

        private static Dictionary<string, ZoneLayoutData>? _cache;

        private static Dictionary<string, ZoneLayoutData> Load()
        {
            if (_cache != null) return _cache;
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, ZoneLayoutData>>(File.ReadAllText(ConfigPath));
                    if (loaded != null) { _cache = loaded; return _cache; }
                }
            }
            catch (JsonException) { }
            catch (IOException) { }
            _cache = new Dictionary<string, ZoneLayoutData>();
            return _cache;
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                MotionDesk.Services.AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(_cache ?? new Dictionary<string, ZoneLayoutData>(), new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (IOException) { }
        }

        public static ZoneLayoutData GetLayout(string screenDeviceName)
        {
            var map = Load();
            if (map.TryGetValue(screenDeviceName, out var data)) return data;

            // No layout under this device name: when a monitor is reconnected Windows can hand it a different \\.\DISPLAYn name.
            // Adopt an ORPHANED layout (its key is not a currently connected screen) that was made for the same resolution.
            try
            {
                var screen = Screen.AllScreens.FirstOrDefault(sc => sc.DeviceName == screenDeviceName);
                if (screen != null)
                {
                    string res = $"{screen.Bounds.Width}x{screen.Bounds.Height}";
                    var connected = new HashSet<string>(Screen.AllScreens.Select(sc => sc.DeviceName));
                    var orphan = map.FirstOrDefault(kv => !connected.Contains(kv.Key) && kv.Value.Resolution == res);
                    if (orphan.Value != null)
                    {
                        map[screenDeviceName] = orphan.Value;
                        map.Remove(orphan.Key);
                        Save();
                        return orphan.Value;
                    }
                }
            }
            catch (Exception) { /* fall through to the default layout */ }
            // Προεπιλογή για μια οθόνη που δεν έχει ρυθμιστεί ακόμα: 3 στήλες, το πιο κοινό
            // πρώτο-run layout στο πραγματικό FancyZones.
            return BuildTemplate("Columns", 3, 2);
        }

        public static void SetLayout(string screenDeviceName, ZoneLayoutData data)
        {
            var map = Load();
            var scr = Screen.AllScreens.FirstOrDefault(sc => sc.DeviceName == screenDeviceName);
            if (scr != null) data.Resolution = $"{scr.Bounds.Width}x{scr.Bounds.Height}";
            map[screenDeviceName] = data;
            Save();
        }

        public static bool AnyLayoutActive() => Load().Values.Any(d => d.Zones.Count > 0);

        // Δημιουργεί ένα layout από ένα από τα 6 templates του πραγματικού FancyZones Editor.
        // n1/n2 σημαίνουν διαφορετικά πράγματα ανά template (στήλες/σειρές, ή cols×rows στο Grid).
        // ΔΙΟΡΘΩΣΗ πραγματικού bug (v1.5.0 request — "δεν γίνονται stretch τα παράθυρα στα
        // περιθώρια της οθόνης"): το gap/2 εφαρμοζόταν ΣΥΜΜΕΤΡΙΚΑ σε ΟΛΕΣ τις πλευρές κάθε ζώνης,
        // ΑΚΟΜΑ και στις πλευρές που ακουμπάνε την ίδια την άκρη της οθόνης (όπου δεν υπάρχει
        // διπλανή ζώνη να δικαιολογεί κενό). Έτσι ακόμα και με τέλεια αντιστάθμιση του αόρατου
        // περιθωρίου του DWM (βλ. ZoneSnapEngine.AdjustForInvisibleFrame), το ΠΑΡΑΘΥΡΟ κουμπώνει
        // σωστά στη ζώνη, αλλά η ίδια η ζώνη ποτέ δεν έφτανε μέχρι την άκρη — γι' αυτό ο χρήστης
        // έβλεπε ένα σταθερό κενό (~0.6% της οθόνης) ακόμα και μετά τη διόρθωση του DWM margin.
        // Λύση: το gap μπαίνει ΜΟΝΟ ανάμεσα σε δύο ζώνες (εσωτερικές γραμμές πλέγματος) — οι
        // πλευρές που ακουμπάνε X=0/Y=0/X+W=1/Y+H=1 (πραγματική άκρη οθόνης) δεν παίρνουν ΠΟΤΕ
        // padding. Το "Priority Grid" παρακάτω ήταν ήδη γραμμένο έτσι χειροκίνητα (0/1 anchors) —
        // δεν είχε ποτέ αυτό το bug, μόνο τα loop-based templates (Columns/Rows/Grid/Custom).
        public static ZoneLayoutData BuildTemplate(string template, int n1 = 3, int n2 = 2)
        {
            var zones = new List<ZoneRect>();
            const double gap = 0.012; // μικρό, ορατό κενό ανάμεσα σε ζώνες, όπως το πραγματικό FancyZones

            // Άκρα ενός κελιού [index, index+1) πάνω σε "count" ίσα κομμάτια του 0..1, με gap/2
            // ΜΟΝΟ στις εσωτερικές πλευρές (index>0 για την αρχή, index<count-1 για το τέλος).
            static (double start, double length) CellRange(int index, int count, double gap)
            {
                double rawStart = index * (1.0 / count);
                double rawEnd = (index + 1) * (1.0 / count);
                double start = index == 0 ? 0.0 : rawStart + gap / 2;
                double end = index == count - 1 ? 1.0 : rawEnd - gap / 2;
                return (start, end - start);
            }

            switch (template)
            {
                case "No layout":
                    break;

                case "Focus":
                    // Απλοποιημένο για v1: μία μεγάλη, κεντραρισμένη ζώνη (το πραγματικό Focus
                    // είναι μια στοίβα από ζώνες που κάνεις κύκλο με Ctrl+Alt+βελάκια — πολύ
                    // μεγαλύτερο scope για ένα πρώτο πέρασμα). Το 10% περιθώριο εδώ είναι σκόπιμο
                    // (κεντραρισμένη ζώνη, ΟΧΙ ακουμπισμένη στην άκρη) — δεν αφορά αυτό το fix.
                    zones.Add(new ZoneRect { X = 0.1, Y = 0.1, Width = 0.8, Height = 0.8 });
                    break;

                case "Columns":
                    for (int i = 0; i < n1; i++)
                    {
                        var (x, w) = CellRange(i, n1, gap);
                        zones.Add(new ZoneRect { X = x, Y = 0, Width = w, Height = 1.0 });
                    }
                    break;

                case "Rows":
                    // n2 = πλήθος σειρών — σταθερή σύμβαση σε όλα τα templates: n1 = στήλες, n2 =
                    // σειρές (ίδια με το Grid παρακάτω), ώστε το ίδιο ζευγάρι steppers στο editor
                    // να οδηγεί σωστά όποιο template κι αν είναι επιλεγμένο.
                    for (int i = 0; i < n2; i++)
                    {
                        var (y, h) = CellRange(i, n2, gap);
                        zones.Add(new ZoneRect { X = 0, Y = y, Width = 1.0, Height = h });
                    }
                    break;

                case "Grid":
                case "Custom":
                    for (int r = 0; r < n2; r++)
                        for (int c = 0; c < n1; c++)
                        {
                            var (x, w) = CellRange(c, n1, gap);
                            var (y, h) = CellRange(r, n2, gap);
                            zones.Add(new ZoneRect { X = x, Y = y, Width = w, Height = h });
                        }
                    break;

                case "Priority Grid":
                    // Μία μεγάλη ζώνη αριστερά (60%) + δύο μικρότερες στοιβαγμένες δεξιά (40%) —
                    // το κλασικό προεπιλεγμένο σχήμα του πραγματικού FancyZones Priority Grid.
                    zones.Add(new ZoneRect { X = 0, Y = 0, Width = 0.6 - gap / 2, Height = 1.0 });
                    zones.Add(new ZoneRect { X = 0.6 + gap / 2, Y = 0, Width = 0.4 - gap / 2, Height = 0.5 - gap / 2 });
                    zones.Add(new ZoneRect { X = 0.6 + gap / 2, Y = 0.5 + gap / 2, Width = 0.4 - gap / 2, Height = 0.5 - gap / 2 });
                    break;
            }

            return new ZoneLayoutData { Template = template, Zones = zones };
        }
    }
}
