using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MotionDesk.Services
{
    public enum WeatherCondition { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Thunderstorm }

    public sealed class WeatherResult
    {
        public double TemperatureC { get; set; }
        public double WindKmh { get; set; }
        public double? HumidityPercent { get; set; }
        public WeatherCondition Condition { get; set; }
        public string Source { get; set; } = "";
    }

    public class WeatherService
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

        // ΚΡΙΣΙΜΟ bug-fix: η παλιά έκδοση έχτιζε το URL με $"...{latitude}..." χωρίς ρητό
        // CultureInfo — σε οποιαδήποτε κουλτούρα χρησιμοποιεί κόμμα ως δεκαδικό διαχωριστικό
        // (π.χ. el-GR), το 37.9838 γινόταν "37,9838" μέσα στο URL. Το Open-Meteo δεν πετούσε
        // HTTP error γι' αυτό (επέστρεφε κανονικό 200 με JSON σφάλματος), οπότε ΔΕΝ πιανόταν από
        // το catch — το widget έδειχνε σιωπηλά "Weather unavailable" χωρίς κανένα log. Λύση:
        // CultureInfo.InvariantCulture ρητά σε κάθε αριθμό που μπαίνει σε URL.
        private static string BuildOpenMeteoUrl(double lat, double lon) =>
            $"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString(CultureInfo.InvariantCulture)}&longitude={lon.ToString(CultureInfo.InvariantCulture)}&current_weather=true";

        // Η legacy "current_weather=true" παράμετρος του Open-Meteo ΔΕΝ περιλαμβάνει υγρασία —
        // χρειάζεται η νεότερη "current=" παράμετρος (ζητήθηκε ρητά "ποσοστό υγρασίας" στο widget).
        private static string BuildOpenMeteoCurrentUrl(double lat, double lon) =>
            $"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString(CultureInfo.InvariantCulture)}&longitude={lon.ToString(CultureInfo.InvariantCulture)}&current=temperature_2m,relative_humidity_2m,wind_speed_10m,weather_code";

        // Κλίμακα Μποφόρ (0-12) από ταχύτητα ανέμου σε km/h — τυπικά όρια της κλίμακας.
        public static int ToBeaufort(double windKmh) => windKmh switch
        {
            < 1 => 0,
            < 6 => 1,
            < 12 => 2,
            < 20 => 3,
            < 29 => 4,
            < 39 => 5,
            < 50 => 6,
            < 62 => 7,
            < 75 => 8,
            < 89 => 9,
            < 103 => 10,
            < 118 => 11,
            _ => 12,
        };

        public static async Task<string> GetWeatherJsonAsync(double latitude = 37.9838, double longitude = 23.7275)
        {
            try
            {
                return await _httpClient.GetStringAsync(BuildOpenMeteoUrl(latitude, longitude));
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        // Ενιαίο, ομαλοποιημένο αποτέλεσμα καιρού με αυτόματο fallback σε δεύτερο δωρεάν πάροχο
        // (wttr.in, χωρίς API key) αν ο πρώτος (Open-Meteo) αποτύχει — ζητήθηκε ρητά "κάποιος
        // πάροχος/ιστοσελίδα δωρεάν πρόσβασης ή διάφοροι providers/sites" ώστε το widget να μην
        // μένει μόνιμα "Weather unavailable" αν ο ένας πάροχος έχει πρόβλημα.
        public static async Task<WeatherResult?> GetNormalizedWeatherAsync(double latitude, double longitude)
        {
            var fromOpenMeteo = await TryOpenMeteoAsync(latitude, longitude);
            if (fromOpenMeteo != null) return fromOpenMeteo;

            return await TryWttrInAsync(latitude, longitude);
        }

        private static async Task<WeatherResult?> TryOpenMeteoAsync(double lat, double lon)
        {
            try
            {
                string json = await _httpClient.GetStringAsync(BuildOpenMeteoCurrentUrl(lat, lon));
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("current", out var current)) return null;
                double temp = current.GetProperty("temperature_2m").GetDouble();
                double wind = current.GetProperty("wind_speed_10m").GetDouble();
                double? humidity = current.TryGetProperty("relative_humidity_2m", out var rh) ? rh.GetDouble() : null;
                int code = current.TryGetProperty("weather_code", out var wc) ? wc.GetInt32() : 0;
                return new WeatherResult { TemperatureC = temp, WindKmh = wind, HumidityPercent = humidity, Condition = ClassifyWeatherCode(code), Source = "Open-Meteo" };
            }
            catch (Exception) { return null; }
        }

        // wttr.in: δωρεάν, χωρίς εγγραφή/API key, δέχεται απευθείας συντεταγμένες στο URL path.
        // https://github.com/chubin/wttr.in — χρησιμοποιείται εδώ μόνο ως εφεδρικός (fallback)
        // πάροχος όταν το Open-Meteo δεν απαντήσει.
        private static async Task<WeatherResult?> TryWttrInAsync(double lat, double lon)
        {
            try
            {
                string coord = $"{lat.ToString(CultureInfo.InvariantCulture)},{lon.ToString(CultureInfo.InvariantCulture)}";
                string url = $"https://wttr.in/{coord}?format=j1";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd("curl/8.0");
                using var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;
                string json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var current = doc.RootElement.GetProperty("current_condition")[0];
                double temp = double.Parse(current.GetProperty("temp_C").GetString()!, CultureInfo.InvariantCulture);
                double wind = double.Parse(current.GetProperty("windspeedKmph").GetString()!, CultureInfo.InvariantCulture);
                double? humidity = current.TryGetProperty("humidity", out var hum) ? double.Parse(hum.GetString()!, CultureInfo.InvariantCulture) : null;
                int wttrCode = int.Parse(current.GetProperty("weatherCode").GetString()!, CultureInfo.InvariantCulture);
                return new WeatherResult { TemperatureC = temp, WindKmh = wind, HumidityPercent = humidity, Condition = ClassifyWttrCode(wttrCode), Source = "wttr.in" };
            }
            catch (Exception) { return null; }
        }

        // ---- Wallpaper "Weather" mode: richer data than the widget (cloud cover, wind direction, precipitation, UTC offset).
        // Cached for 10 minutes per location (shared by every screen's bridge); on failure the last good payload (even if stale)
        // is returned, otherwise {"ok":false} and the wallpaper keeps a clear sky with the real time of day.
        private static string? _wpCacheKey, _wpCacheJson;
        private static DateTime _wpCacheAt = DateTime.MinValue;
        private static readonly object _wpLock = new();

        private static string BuildWallpaperUrl(double lat, double lon) =>
            $"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString(CultureInfo.InvariantCulture)}&longitude={lon.ToString(CultureInfo.InvariantCulture)}" +
            "&current=temperature_2m,relative_humidity_2m,precipitation,weather_code,cloud_cover,wind_speed_10m,wind_direction_10m,is_day&timezone=auto";

        private static int CodeForCondition(WeatherCondition c) => c switch
        {
            WeatherCondition.Clear => 0, WeatherCondition.PartlyCloudy => 2, WeatherCondition.Cloudy => 3, WeatherCondition.Fog => 45,
            WeatherCondition.Drizzle => 51, WeatherCondition.Rain => 63, WeatherCondition.Snow => 73, WeatherCondition.Thunderstorm => 95, _ => 0
        };

        public static async Task<string> GetWallpaperWeatherJsonAsync(double lat, double lon)
        {
            string key = $"{lat.ToString(CultureInfo.InvariantCulture)},{lon.ToString(CultureInfo.InvariantCulture)}";
            lock (_wpLock)
            {
                if (_wpCacheKey == key && _wpCacheJson != null && DateTime.UtcNow - _wpCacheAt < TimeSpan.FromMinutes(10)) return _wpCacheJson;
            }

            string? json = null;
            try
            {
                string raw = await _httpClient.GetStringAsync(BuildWallpaperUrl(lat, lon));
                using var doc = JsonDocument.Parse(raw);
                var cur = doc.RootElement.GetProperty("current");
                double Num(string n, double d = 0) => cur.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : d;
                json = JsonSerializer.Serialize(new
                {
                    ok = true,
                    code = (int)Num("weather_code"),
                    temp = Num("temperature_2m"),
                    humidity = Num("relative_humidity_2m", -1),
                    precip = Num("precipitation"),
                    cloud = Num("cloud_cover", -1),
                    wind = Num("wind_speed_10m"),
                    windDir = Num("wind_direction_10m", 270),
                    isDay = Num("is_day", 1) > 0,
                    utcOffsetSec = doc.RootElement.TryGetProperty("utc_offset_seconds", out var off) && off.ValueKind == JsonValueKind.Number ? off.GetInt32() : (int?)null,
                    source = "Open-Meteo"
                });
            }
            catch (Exception) { /* fall through to the second provider */ }

            if (json == null)
            {
                var fallback = await GetNormalizedWeatherAsync(lat, lon);
                if (fallback != null)
                    json = JsonSerializer.Serialize(new
                    {
                        ok = true, code = CodeForCondition(fallback.Condition), temp = fallback.TemperatureC, humidity = fallback.HumidityPercent ?? -1,
                        precip = 0.0, cloud = -1, wind = fallback.WindKmh, windDir = 270, isDay = true, utcOffsetSec = (int?)null, source = fallback.Source
                    });
            }

            lock (_wpLock)
            {
                if (json != null) { _wpCacheKey = key; _wpCacheJson = json; _wpCacheAt = DateTime.UtcNow; return json; }
                if (_wpCacheKey == key && _wpCacheJson != null) return _wpCacheJson;   // stale but better than nothing
            }
            return "{\"ok\":false}";
        }

        // Open-Meteo geocoding API (ίδιος δωρεάν πάροχος με τον καιρό, χωρίς API key) — επιτρέπει
        // στο widget να έχει πραγματική επιλογή τοποθεσίας αντί για μόνιμα κλειδωμένη Αθήνα.
        public static async Task<(double Lat, double Lon, string Name)?> GeocodeAsync(string cityName)
        {
            try
            {
                string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(cityName)}&count=1&language=en&format=json";
                string json = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                {
                    var first = results[0];
                    double lat = first.GetProperty("latitude").GetDouble();
                    double lon = first.GetProperty("longitude").GetDouble();
                    // Μόνο το όνομα πόλης (όχι + χώρα) — ζητήθηκε να μη κόβεται ο τίτλος του widget.
                    string name = first.GetProperty("name").GetString() ?? cityName;
                    return (lat, lon, name);
                }
            }
            catch (Exception) { }
            return null;
        }

        // WMO weather codes (Open-Meteo current_weather.weathercode) -> ομαδοποιημένη συνθήκη.
        // https://open-meteo.com/en/docs (πίνακας WMO Weather interpretation codes)
        public static WeatherCondition ClassifyWeatherCode(int code) => code switch
        {
            0 => WeatherCondition.Clear,
            1 or 2 => WeatherCondition.PartlyCloudy,
            3 => WeatherCondition.Cloudy,
            45 or 48 => WeatherCondition.Fog,
            51 or 53 or 55 or 56 or 57 => WeatherCondition.Drizzle,
            61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => WeatherCondition.Rain,
            71 or 73 or 75 or 77 or 85 or 86 => WeatherCondition.Snow,
            95 or 96 or 99 => WeatherCondition.Thunderstorm,
            _ => WeatherCondition.PartlyCloudy,
        };

        // wttr.in χρησιμοποιεί δικούς του (worldweatheronline-style) κωδικούς, διαφορετικούς
        // από τους WMO codes του Open-Meteo — ξεχωριστή χαρτογράφηση.
        private static WeatherCondition ClassifyWttrCode(int code) => code switch
        {
            113 => WeatherCondition.Clear,
            116 => WeatherCondition.PartlyCloudy,
            119 or 122 => WeatherCondition.Cloudy,
            143 or 248 or 260 => WeatherCondition.Fog,
            176 or 263 or 266 or 293 or 296 or 353 => WeatherCondition.Drizzle,
            185 or 281 or 284 or 299 or 302 or 305 or 308 or 311 or 314 or 317 or 320 or 356 or 359 or 362 or 365 => WeatherCondition.Rain,
            179 or 182 or 227 or 230 or 323 or 326 or 329 or 332 or 335 or 338 or 368 or 371 or 374 or 377 => WeatherCondition.Snow,
            200 or 386 or 389 or 392 or 395 => WeatherCondition.Thunderstorm,
            _ => WeatherCondition.PartlyCloudy,
        };
    }
}
