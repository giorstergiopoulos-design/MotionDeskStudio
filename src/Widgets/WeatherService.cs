using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MotionDesk.Services
{
    public enum WeatherCondition { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Thunderstorm }

    public class WeatherService
    {
        private static readonly HttpClient _httpClient = new();

        public static async Task<string> GetWeatherJsonAsync(double latitude = 37.9838, double longitude = 23.7275)
        {
            try
            {
                string url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current_weather=true";
                return await _httpClient.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "motiondesk_weather_debug.log"), $"{DateTime.Now}: {ex}\n"); } catch { }
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        // Open-Meteo geocoding API (ίδιο δωρεάν provider με τον καιρό, χωρίς API key) — επιτρέπει
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
                    string name = first.GetProperty("name").GetString() ?? cityName;
                    if (first.TryGetProperty("country", out var country))
                        name = $"{name}, {country.GetString()}";
                    return (lat, lon, name);
                }
            }
            catch (Exception) { }
            return null;
        }

        // WMO weather codes (χρησιμοποιούνται από το Open-Meteo current_weather.weathercode) ->
        // ομαδοποιημένη κατάσταση, ώστε το widget να διαλέξει σωστό εικονίδιο/animation.
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
    }
}
