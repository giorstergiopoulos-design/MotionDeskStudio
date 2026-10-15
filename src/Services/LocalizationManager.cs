using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace MotionDesk.Services
{
    // Κεντρικό σύστημα μεταφράσεων - 14 γλώσσες (ίδιες με το GearWin), ή "Follow" = γλώσσα Windows.
    // Μία αλλαγή γλώσσας ενημερώνει άμεσα ό,τι διαβάζει από εδώ μέσω του Changed event.
    // Ένα κλειδί που λείπει από την τρέχουσα γλώσσα πέφτει πρώτα στα Αγγλικά (ΠΟΤΕ στα Ελληνικά - να μη
    // μπερδεύονται οι γλώσσες) και μετά στο ίδιο το όνομα του κλειδιού.
    public static class LocalizationManager
    {
        // Σειρά = σειρά εμφάνισης στον επιλογέα γλώσσας (μετά το "Follow Windows").
        public static readonly (string Code, string File, string Native)[] Supported =
        {
            ("el", "el-GR.json", "Ελληνικά"), ("en", "en-US.json", "English"), ("de", "de-DE.json", "Deutsch"),
            ("fr", "fr-FR.json", "Français"), ("es", "es-ES.json", "Español"), ("ko", "ko-KR.json", "한국어"),
            ("zh", "zh-CN.json", "中文"), ("it", "it-IT.json", "Italiano"), ("ru", "ru-RU.json", "Русский"),
            ("ja", "ja-JP.json", "日本語"), ("pt", "pt-PT.json", "Português"), ("tr", "tr-TR.json", "Türkçe"),
            ("ar", "ar-SA.json", "العربية"), ("hi", "hi-IN.json", "हिन्दी"),
        };

        private static Dictionary<string, string> _strings = new();
        private static Dictionary<string, string> _fallback = new();
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

        // Κωδικός γλώσσας -> υποστηριζόμενος κωδικός (άγνωστο = "en"). Δημόσιο/καθαρό για έλεγχο.
        public static string Resolve(string code, string windowsTwoLetter)
        {
            var wanted = code == "Follow" ? windowsTwoLetter : code;
            foreach (var s in Supported) if (s.Code == wanted) return wanted;
            return "en";
        }

        private static void Apply(string code)
        {
            var resolved = Resolve(code, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
            CurrentLanguage = resolved;
            _fallback = resolved == "en" ? new() : LoadDictionary("en");
            _strings = LoadDictionary(resolved);
        }

        private static Dictionary<string, string> LoadDictionary(string code)
        {
            try
            {
                var fileName = "en-US.json";
                foreach (var s in Supported) if (s.Code == code) { fileName = s.File; break; }
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

        public static string T(string key) =>
            _strings.TryGetValue(key, out var value) ? value : _fallback.TryGetValue(key, out var fb) ? fb : key;
    }
}
