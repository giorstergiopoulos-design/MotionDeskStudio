using System.Media;

namespace MotionDesk.Services
{
    // Απλά, ενσωματωμένα ηχητικά cues των ίδιων των Windows (SystemSounds) — όχι custom .wav
    // αρχεία (θα ήταν copyrighted "Vista sounds" κ.λπ.). Ενεργοποιείται/απενεργοποιείται από τις
    // Ρυθμίσεις (AppSettings.UiSoundsEnabled).
    public static class UiSounds
    {
        public static void PlayClick() { if (AppSettings.Load().UiSoundsEnabled) SystemSounds.Asterisk.Play(); }
        public static void PlaySuccess() { if (AppSettings.Load().UiSoundsEnabled) SystemSounds.Exclamation.Play(); }
        public static void PlayError() { if (AppSettings.Load().UiSoundsEnabled) SystemSounds.Hand.Play(); }
    }
}
