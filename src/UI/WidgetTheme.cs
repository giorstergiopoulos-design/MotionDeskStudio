using System.Drawing;
using MotionDesk.Services;

namespace MotionDesk.UI
{
    // Ανεξάρτητο θέμα για widgets/DeskContainers από αυτό της ίδιας της εφαρμογής — ζητήθηκε
    // ρητά "αν η εφαρμογή είναι σε φωτεινό θέμα, τα widgets/containers να μπορούν είτε να
    // ακολουθούν τα windows είτε να έχουν διαφορετικά χρώματα και από το σύστημα και από την
    // εφαρμογή" (AppSettings.WidgetsThemeMode: "App" | "Windows" | "Dark" | "Light").
    //
    // Ίδιο ακριβώς σύνολο ιδιοτήτων με το UiTheme, ώστε το WidgetEngine.cs/DeskContainerWindow.cs
    // να μπορούν να ανακατευθύνουν ΟΛΕΣ τις υπάρχουσες αναφορές "UiTheme.X" σε αυτή την κλάση με
    // ένα using-alias (using UiTheme = MotionDesk.UI.WidgetTheme;) αντί να αλλάξει το καθένα από
    // τα δεκάδες σημεία ξεχωριστά.
    public static class WidgetTheme
    {
        private static UiPalette Resolve()
        {
            string mode = AppSettings.Load().WidgetsThemeMode;
            return mode switch
            {
                "Windows" => ThemeManager.IsWindowsLightTheme() ? UiPalette.Light() : UiPalette.Dark(AppSettings.Load().DarkIntensity),
                "Dark" => UiPalette.Dark(AppSettings.Load().DarkIntensity),
                "Light" => UiPalette.Light(),
                _ => ThemeManager.Current, // "App" — ακολουθεί το θέμα της ίδιας της εφαρμογής (προεπιλογή, προηγούμενη συμπεριφορά)
            };
        }

        public static Color Background => Resolve().Background;
        public static Color Sidebar => Resolve().Sidebar;
        public static Color Surface => Resolve().Surface;
        public static Color SurfaceHover => Resolve().SurfaceHover;
        public static Color Border => Resolve().Border;
        public static Color TextPrimary => Resolve().TextPrimary;
        public static Color TextSecondary => Resolve().TextSecondary;
        public static Color TextMuted => Resolve().TextMuted;
        public static Color AccentCyan => Resolve().AccentCyan;
        public static Color AccentBlue => Resolve().AccentBlue;
        public static Color AccentSoft => Color.FromArgb(40, AccentCyan.R, AccentCyan.G, AccentCyan.B);

        public static readonly Font FontHeading = UiTheme.FontHeading;
        public static readonly Font FontSubheading = UiTheme.FontSubheading;
        public static readonly Font FontBody = UiTheme.FontBody;

        public static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle bounds, int radius) => UiTheme.RoundedPath(bounds, radius);
        public static void ApplyRoundedRegion(System.Windows.Forms.Control control, int radius) => UiTheme.ApplyRoundedRegion(control, radius);
    }
}
