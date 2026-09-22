using Microsoft.Win32;

namespace MotionDesk.Services
{
    // Ανιχνεύει το τρέχον Windows Light/Dark app theme (Settings > Personalization > Colors)
    // ώστε η MotionDesk να μπορεί να ακολουθεί αυτόματα το θέμα των Windows ("Follow Windows").
    public static class ThemeService
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static bool IsLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                var value = key?.GetValue("AppsUseLightTheme");
                return value is int i && i != 0;
            }
            catch (System.Security.SecurityException) { return false; }
        }
    }
}
