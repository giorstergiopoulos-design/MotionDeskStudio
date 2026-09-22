using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace MotionDesk.Services
{
    // Κεντρικό σύστημα μεταφράσεων (Ελληνικά/Αγγλικά, ή "Follow" = γλώσσα Windows).
    // Μία αλλαγή γλώσσας ενημερώνει άμεσα ό,τι διαβάζει από εδώ μέσω του Changed event.
    public static class LocalizationManager
    {
        private static Dictionary<string, string> _strings = new();
        public static string CurrentLanguage { get; private set; } = "en";
        public static event Action? Changed;

        static LocalizationManager()
        {
            Apply(AppSettings.Load().Language);
        }

        public static void SetLanguage(string code)
        {
            var settings = AppSettings.Load();
            settings.Language = code;
            settings.Save();
            Apply(code);
            Changed?.Invoke();
        }

        private static void Apply(string code)
        {
            string resolved = code == "Follow"
                ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en")
                : code;

            CurrentLanguage = resolved;
            _strings = LoadDictionary(resolved);
        }

        private static Dictionary<string, string> LoadDictionary(string code)
        {
            try
            {
                var fileName = code == "el" ? "el-GR.json" : "en-US.json";
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locales", fileName);
                if (File.Exists(path))
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                    if (loaded != null) return loaded;
                }
            }
            catch (JsonException) { }
            catch (IOException) { }
            return new Dictionary<string, string>();
        }

        public static string T(string key) => _strings.TryGetValue(key, out var value) ? value : key;
    }
}
