using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace MotionDesk.Services
{
    // Checks the project's GitHub releases for a newer version (one small request, at most once a day, never blocks the UI).
    // The app never downloads or installs anything by itself: it only tells the user and opens the release page.
    public static class UpdateCheckService
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/giorstergiopoulos-design/MotionDeskStudio/releases/latest";
        private static readonly HttpClient Http = CreateClient();

        public sealed record UpdateInfo(Version Latest, string Tag, string Url);
        public sealed record CheckResult(bool Succeeded, UpdateInfo? Update);

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("MotionDeskStudio-UpdateCheck");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        public static async Task<CheckResult> CheckAsync()
        {
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(LatestReleaseApi));
                var root = doc.RootElement;
                string tag = root.GetProperty("tag_name").GetString() ?? "";
                string url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
                if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return new CheckResult(true, null);
                // compare major.minor.build only (the assembly version has a 4th component that releases do not)
                var cur = CurrentVersion;
                var curCmp = new Version(cur.Major, cur.Minor, Math.Max(0, cur.Build));
                var latCmp = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
                return new CheckResult(true, latCmp > curCmp ? new UpdateInfo(latest, tag, url) : null);
            }
            catch (Exception) { return new CheckResult(false, null); }   // offline, rate-limited, no releases yet…
        }

        // Daily check used by the tray: returns the update when one is available and the daily limit allows a request
        public static async Task<UpdateInfo?> CheckIfDueAsync()
        {
            var settings = AppSettings.Load();
            if (!settings.UpdateCheckEnabled) return null;
            if (DateTime.UtcNow - settings.LastUpdateCheckUtc < TimeSpan.FromHours(24)) return null;
            var result = await CheckAsync();
            if (result.Succeeded)
            {
                var s = AppSettings.Load();
                s.LastUpdateCheckUtc = DateTime.UtcNow;
                s.Save();
            }
            return result.Update;
        }
    }
}
