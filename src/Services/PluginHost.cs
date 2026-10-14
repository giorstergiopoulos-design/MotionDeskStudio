using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Win32;
using MotionDesk.UI;
using OpsisNewsCrawler.Plugin.Abstractions;

namespace MotionDesk.Services
{
    // Μηχανισμός plugin του Opsis - Desktop Customization.
    //
    // Ένα plugin είναι ένας φάκελος μέσα σε έναν από τους φακέλους "plugins" με ένα *Plugin.dll που περιέχει μια κλάση
    // που υλοποιεί IOpsisNewsCrawlerPlugin (το contract βρίσκεται στο libs\OpsisNewsCrawler.Plugin.Abstractions.dll).
    // Φάκελοι αναζήτησης (με αυτή τη σειρά):
    //   1. <φάκελος Opsis>\plugins\*\
    //   2. %AppData%\Opsis\plugins\*\
    //   3. κάθε διαδρομή στο HKCU\Software\Opsis\PluginPaths (την γράφει ο installer του plugin)
    // Κάθε plugin φορτώνεται σε δικό του AssemblyLoadContext ώστε οι εξαρτήσεις του να μη μπλέκονται με του Opsis·
    // μόνο το contract (και το framework) μοιράζονται. Ένα plugin που αποτυγχάνει ΠΟΤΕ δεν ρίχνει το Opsis —
    // το σφάλμα γράφεται στο plugins.log και το plugin παραλείπεται.
    public sealed class PluginHost
    {
        public static PluginHost Instance { get; } = new();

        private sealed class Loaded
        {
            public IOpsisNewsCrawlerPlugin Plugin = null!;
            public string Folder = "";
        }

        private readonly List<Loaded> _plugins = new();
        private bool _started;

        public IReadOnlyList<IOpsisNewsCrawlerPlugin> Plugins => _plugins.Select(p => p.Plugin).ToList();

        /// <summary>Ένα plugin ζήτησε να εμφανιστεί μία από τις σελίδες του (π.χ. δεξί κλικ στη μπάρα ειδήσεων).</summary>
        public event Action<string>? NavigationRequested;

        /// <summary>Η λίστα στοιχείων του tray menu ή του sidebar μπορεί να έχει αλλάξει.</summary>
        public event Action? Changed;

        /// <summary>Φορτώνει και ξεκινά όλα τα plugin. Πρέπει να κληθεί στο UI thread.</summary>
        public void StartAll()
        {
            if (_started) return;
            _started = true;
            if (!AppSettings.Load().PluginsEnabled) return;

            foreach (var folder in EnumerateCandidateFolders())
            {
                try { LoadFolder(folder); }
                catch (Exception ex) { Log($"Failed to load '{folder}': {ex}"); }
            }

            foreach (var p in _plugins.ToList())
            {
                try
                {
                    p.Plugin.NavigationRequested += key => NavigationRequested?.Invoke(key);
                    PushLanguage(p.Plugin);
                    PushTheme(p.Plugin);
                    p.Plugin.Start();
                }
                catch (Exception ex)
                {
                    Log($"Plugin '{p.Plugin.Name}' failed to start: {ex}");
                    _plugins.Remove(p);
                }
            }

            ThemeManager.Changed += () => { foreach (var p in _plugins.ToList()) PushTheme(p.Plugin); };
            // Η γλώσσα του Opsis ισχύει και για τα plugin: αλλάζει ΚΑΙ στα δύο ταυτόχρονα.
            LocalizationManager.Changed += () => { foreach (var p in _plugins.ToList()) PushLanguage(p.Plugin); };
            Changed?.Invoke();
        }

        public void StopAll()
        {
            foreach (var p in _plugins.ToList())
            {
                try { p.Plugin.Stop(); }
                catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' failed to stop: {ex.Message}"); }
            }
        }

        /// <summary>Σελίδες των plugin για το sidebar, ταξινομημένες κατά Order.</summary>
        public IReadOnlyList<PluginNavigationItem> GetNavigationItems()
        {
            var items = new List<PluginNavigationItem>();
            foreach (var p in _plugins)
            {
                try { items.AddRange(p.Plugin.GetNavigationItems()); }
                catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' navigation failed: {ex.Message}"); }
            }
            return items.OrderBy(i => i.Order).ToList();
        }

        public IReadOnlyList<PluginMenuItem> GetTrayMenuItems()
        {
            var items = new List<PluginMenuItem>();
            foreach (var p in _plugins)
            {
                try { items.AddRange(p.Plugin.GetTrayMenuItems()); }
                catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' tray items failed: {ex.Message}"); }
            }
            return items;
        }

        /// <summary>Πληροφορίες "Σχετικά" όλων των plugin (ιδιότητες, βοήθεια, οδηγίες, ιστορικό, άδεια) για τη σελίδα About.</summary>
        public IReadOnlyList<PluginAboutInfo> GetAboutInfos()
        {
            var infos = new List<PluginAboutInfo>();
            foreach (var p in _plugins)
            {
                try { infos.Add(p.Plugin.GetAboutInfo()); }
                catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' about info failed: {ex.Message}"); }
            }
            return infos;
        }

        public static string TitleOf(PluginNavigationItem item) =>
            LocalizationManager.CurrentLanguage == "el" ? item.TitleEl : item.TitleEn;

        private void PushLanguage(IOpsisNewsCrawlerPlugin plugin)
        {
            try { plugin.SetLanguage(LocalizationManager.CurrentLanguage); }
            catch (Exception ex) { Log($"Plugin '{plugin.Name}' SetLanguage failed: {ex.Message}"); }
        }

        private void PushTheme(IOpsisNewsCrawlerPlugin plugin)
        {
            try
            {
                var t = ThemeManager.Current;
                var opacity = Math.Clamp(AppSettings.Load().WindowOpacity, 0.3, 1.0);
                plugin.SetTheme(t.IsDark, t.Background.ToArgb(), t.Surface.ToArgb(), t.Border.ToArgb(),
                    t.TextPrimary.ToArgb(), t.TextMuted.ToArgb(), t.AccentCyan.ToArgb(), opacity);
            }
            catch (Exception ex) { Log($"Plugin '{plugin.Name}' SetTheme failed: {ex.Message}"); }
        }

        // ---- discovery & loading ---------------------------------------------------------------

        private static IEnumerable<string> EnumerateCandidateFolders()
        {
            var roots = new List<string>
            {
                Path.Combine(AppContext.BaseDirectory, "plugins"),
                Path.Combine(AppPaths.AppDataFolder, "plugins")
            };
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Opsis\PluginPaths");
                if (key != null)
                    foreach (var name in key.GetValueNames())
                        if (key.GetValue(name) is string path && path.Length > 0) roots.Add(path);
            }
            catch (Exception) { }

            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root)) continue;
                foreach (var dir in Directory.EnumerateDirectories(root)) yield return dir;
            }
        }

        private void LoadFolder(string folder)
        {
            foreach (var dll in Directory.EnumerateFiles(folder, "*Plugin.dll"))
            {
                var context = new PluginLoadContext(dll);
                var assembly = context.LoadFromAssemblyPath(dll);
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || !typeof(IOpsisNewsCrawlerPlugin).IsAssignableFrom(type)) continue;
                    if (Activator.CreateInstance(type) is not IOpsisNewsCrawlerPlugin plugin) continue;
                    if (_plugins.Any(p => p.Plugin.Id == plugin.Id)) continue; // ίδιο plugin από δύο φακέλους
                    _plugins.Add(new Loaded { Plugin = plugin, Folder = folder });
                }
            }
        }

        private sealed class PluginLoadContext : AssemblyLoadContext
        {
            private readonly AssemblyDependencyResolver _resolver;

            public PluginLoadContext(string pluginPath) : base(Path.GetFileNameWithoutExtension(pluginPath), isCollectible: false)
            {
                _resolver = new AssemblyDependencyResolver(pluginPath);
            }

            protected override Assembly? Load(AssemblyName name)
            {
                // Το contract πρέπει να είναι ΤΟ ΙΔΙΟ assembly με του Opsis, αλλιώς τα casts αποτυγχάνουν.
                if (name.Name == typeof(IOpsisNewsCrawlerPlugin).Assembly.GetName().Name) return null;
                var path = _resolver.ResolveAssemblyToPath(name);
                return path != null ? LoadFromAssemblyPath(path) : null;
            }

            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
            }
        }

        private static void Log(string message)
        {
            try
            {
                var path = Path.Combine(AppPaths.AppDataFolder, "plugins.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"[{DateTime.Now:O}] {message}\n");
            }
            catch (Exception) { }
        }
    }
}
