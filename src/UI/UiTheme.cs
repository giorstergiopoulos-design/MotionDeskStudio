using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MotionDesk.UI
{
    // Κοινό οπτικό σύστημα (χρώματα, τυπογραφία, rounded-corner helpers) ώστε όλα τα
    // παράθυρα/σελίδες της εφαρμογής (κύριο παράθυρο, dialogs, κάρτες, κουμπιά) να
    // μοιράζονται την ίδια αισθητική αντί για ad-hoc χρώματα σε κάθε αρχείο.
    // Τα χρώματα διαβάζονται ζωντανά από το ThemeManager.Current — μια αλλαγή Light/Dark
    // Ισχύει αμέσως για κάθε owner-draw στοιχείο (NavButton/StatCard/κ.λπ.) στο επόμενο paint.
    public static class UiTheme
    {
        public static Color Background => ThemeManager.Current.Background;
        public static Color Sidebar => ThemeManager.Current.Sidebar;
        public static Color Surface => ThemeManager.Current.Surface;
        public static Color SurfaceHover => ThemeManager.Current.SurfaceHover;
        public static Color Border => ThemeManager.Current.Border;
        public static Color TextPrimary => ThemeManager.Current.TextPrimary;
        public static Color TextSecondary => ThemeManager.Current.TextSecondary;
        public static Color TextMuted => ThemeManager.Current.TextMuted;

        public static Color AccentCyan => ThemeManager.Current.AccentCyan;
        public static Color AccentBlue => ThemeManager.Current.AccentBlue;
        public static Color AccentSoft => Color.FromArgb(40, AccentCyan.R, AccentCyan.G, AccentCyan.B);

        public static readonly Font FontHeading = new("Segoe UI", 20f, FontStyle.Bold);
        public static readonly Font FontSubheading = new("Segoe UI", 12f, FontStyle.Bold);
        public static readonly Font FontBody = new("Segoe UI", 10f);
        public static readonly Font FontIcon16 = new("Segoe Fluent Icons", 15f);

        public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void ApplyRoundedRegion(Control control, int radius)
        {
            var bounds = new Rectangle(0, 0, control.Width, control.Height);
            using var path = RoundedPath(bounds, radius);
            control.Region = new Region(path);
        }

        // Σωστά ελληνικά κεφαλαία: η ορθογραφική σύμβαση είναι να ΜΗΝ τονίζονται τα κεφαλαία
        // γράμματα (π.χ. "ΕΞΑΤΟΜΙΚΕΥΣΗ", όχι "ΕΞΑΤΟΜΙΚΕΥΣΉ"). Το απλό ToUpperInvariant() ΔΕΝ
        // αφαιρεί τους τόνους (μετατρέπει "διάταξη" -> "ΔΙΆΤΑΞΗ", με τόνο) — ζητήθηκε ρητά να
        // διορθωθεί σε όλα τα σημεία που εμφανίζουν κείμενο σε κεφαλαία (τίτλοι ενοτήτων, κάρτες).
        public static string UpperNoAccents(string text)
        {
            string upper = text.ToUpperInvariant();
            var sb = new System.Text.StringBuilder(upper.Length);
            foreach (char c in upper)
            {
                sb.Append(c switch
                {
                    'Ά' => 'Α',
                    'Έ' => 'Ε',
                    'Ή' => 'Η',
                    'Ί' => 'Ι',
                    'Ό' => 'Ο',
                    'Ύ' => 'Υ',
                    'Ώ' => 'Ω',
                    'Ϊ' => 'Ι',
                    'Ϋ' => 'Υ',
                    _ => c
                });
            }
            return sb.ToString();
        }
    }
}
