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
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(_cache ?? new Dictionary<string, ZoneLayoutData>(), new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (IOException) { }
        }

        public static ZoneLayoutData GetLayout(string screenDeviceName)
        {
            var map = Load();
            if (map.TryGetValue(screenDeviceName, out var data)) return data;
            // Προεπιλογή για μια οθόνη που δεν έχει ρυθμιστεί ακόμα: 3 στήλες, το πιο κοινό
            // πρώτο-run layout στο πραγματικό FancyZones.
            return BuildTemplate("Columns", 3, 2);
        }

        public static void SetLayout(string screenDeviceName, ZoneLayoutData data)
        {
            var map = Load();
            map[screenDeviceName] = data;
            Save();
        }

        public static bool AnyLayoutActive() => Load().Values.Any(d => d.Zones.Count > 0);

        // Δημιουργεί ένα layout από ένα από τα 6 templates του πραγματικού FancyZones Editor.
        // n1/n2 σημαίνουν διαφορετικά πράγματα ανά template (στήλες/σειρές, ή cols×rows στο Grid).
        public static ZoneLayoutData BuildTemplate(string template, int n1 = 3, int n2 = 2)
        {
            var zones = new List<ZoneRect>();
            const double gap = 0.012; // μικρό, ορατό κενό ανάμεσα σε ζώνες, όπως το πραγματικό FancyZones

            switch (template)
            {
                case "No layout":
                    break;

                case "Focus":
                    // Απλοποιημένο για v1: μία μεγάλη, κεντραρισμένη ζώνη (το πραγματικό Focus
                    // είναι μια στοίβα από ζώνες που κάνεις κύκλο με Ctrl+Alt+βελάκια — πολύ
                    // μεγαλύτερο scope για ένα πρώτο πέρασμα).
                    zones.Add(new ZoneRect { X = 0.1, Y = 0.1, Width = 0.8, Height = 0.8 });
                    break;

                case "Columns":
                    for (int i = 0; i < n1; i++)
                        zones.Add(new ZoneRect { X = i * (1.0 / n1) + gap / 2, Y = 0, Width = 1.0 / n1 - gap, Height = 1.0 });
                    break;

                case "Rows":
                    // n2 = πλήθος σειρών — σταθερή σύμβαση σε όλα τα templates: n1 = στήλες, n2 =
                    // σειρές (ίδια με το Grid παρακάτω), ώστε το ίδιο ζευγάρι steppers στο editor
                    // να οδηγεί σωστά όποιο template κι αν είναι επιλεγμένο.
                    for (int i = 0; i < n2; i++)
                        zones.Add(new ZoneRect { X = 0, Y = i * (1.0 / n2) + gap / 2, Width = 1.0, Height = 1.0 / n2 - gap });
                    break;

                case "Grid":
                case "Custom":
                    for (int r = 0; r < n2; r++)
                        for (int c = 0; c < n1; c++)
                            zones.Add(new ZoneRect
                            {
                                X = c * (1.0 / n1) + gap / 2,
                                Y = r * (1.0 / n2) + gap / 2,
                                Width = 1.0 / n1 - gap,
                                Height = 1.0 / n2 - gap
                            });
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
