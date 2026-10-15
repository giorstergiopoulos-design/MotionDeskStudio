using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace MotionDesk.Widgets
{
    /// <summary>Creates one shared WebView2 environment for all rich-content surfaces.</summary>
    public static class WebView2Support
    {
        private static readonly Lazy<Task<CoreWebView2Environment>> _environment =
            new(CreateEnvironmentAsyncCore);

        public static Task<CoreWebView2Environment> CreateEnvironmentAsync() => _environment.Value;

        // Σκληραγώγηση των WebView2 που φιλοξενούν ΜΟΝΟ τοπικές σελίδες της εφαρμογής (wallpaper/widgets) και
        // εκθέτουν host objects (WallpaperBridge/WidgetBridge): κανένα link/σφάλμα δεν πρέπει να μπορεί να
        // φορτώσει εξωτερικό περιεχόμενο στο ίδιο WebView (θα είχε πρόσβαση στα host objects), ούτε να ανοίξει
        // νέο παράθυρο ή DevTools/μενού περιβάλλοντος πάνω στην επιφάνεια εργασίας.
        public static void Harden(CoreWebView2 core)
        {
            var settings = core.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsBuiltInErrorPageEnabled = false;
            core.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
        }

        private static async Task<CoreWebView2Environment> CreateEnvironmentAsyncCore()
        {
            // (LocalApplicationData, όχι ApplicationData/AppPaths.AppDataFolder: cache δεδομένα του
            // WebView2 runtime, όχι ρυθμίσεις χρήστη — σωστά τοπικό/μη-roaming, ξαναδημιουργείται
            // αβλαβώς αν χαθεί, οπότε δεν χρειάζεται μετάβαση από το παλιό όνομα.)
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Opsis", "WebView2");
            Directory.CreateDirectory(userDataFolder);
            return await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder).ConfigureAwait(true);
        }
    }
}
