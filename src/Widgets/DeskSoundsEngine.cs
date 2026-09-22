using System.Media;
using Microsoft.Win32;

namespace MotionDesk.Widgets
{
    // "DeskSounds" — πλήρες σχήμα ήχων συστήματος πέρα από τα stock SystemSounds cues που ήδη
    // χρησιμοποιεί το Services.UiSounds. Γράφει απευθείας στο επίσημο registry μηχανισμό των
    // Windows (HKCU\AppEvents\Schemes\Apps\.Default\{event}\.Current) — καμία επιπλέον κλήση API
    // χρειάζεται, το PlaySound-based σύστημα διαβάζει αυτή την τιμή τη στιγμή που συμβαίνει το
    // event. Τα ονόματα events εδώ επαληθεύτηκαν έναντι πραγματικού registry enumeration στη
    // μηχανή-στόχο (π.χ. το "EmptyRecycleBin" ΔΕΝ υπάρχει πλέον σε σύγχρονα Windows — αφαιρέθηκε
    // από τη λίστα αντί να προστεθεί ένα event που σιωπηλά δεν θα έκανε τίποτα).
    internal static class DeskSoundsEngine
    {
        public static readonly (string EventLabel, string LabelKey)[] Events =
        {
            ("WindowsLogon", "Sounds.EventLogon"),
            ("WindowsLogoff", "Sounds.EventLogoff"),
            ("Notification.Default", "Sounds.EventNotification"),
            ("SystemExclamation", "Sounds.EventWarning"),
            ("SystemHand", "Sounds.EventCriticalStop"),
            ("MailBeep", "Sounds.EventMail"),
        };

        private const string SchemeBase = @"AppEvents\Schemes\Apps\.Default";

        public static string? GetSound(string eventLabel)
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{SchemeBase}\{eventLabel}\.Current");
            var v = key?.GetValue(null) as string;
            return string.IsNullOrEmpty(v) ? null : v;
        }

        public static void SetSound(string eventLabel, string? wavPath)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"{SchemeBase}\{eventLabel}\.Current");
            key?.SetValue(null, wavPath ?? string.Empty, RegistryValueKind.ExpandString);
        }

        public static void Preview(string wavPath)
        {
            try { using var player = new SoundPlayer(wavPath); player.Play(); } catch (System.Exception) { }
        }
    }
}
