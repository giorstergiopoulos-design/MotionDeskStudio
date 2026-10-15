using System.Text;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// Ατομική εγγραφή αρχείου ρυθμίσεων: γράφει σε προσωρινό αρχείο και μετά το μετονομάζει πάνω στο
/// τελικό. Το απλό File.WriteAllText πρώτα "κόβει" το αρχείο σε 0 bytes — ένα ταυτόχρονο Load()
/// (πολλά σημεία της εφαρμογής διαβάζουν settings συνεχώς) έβλεπε μισο-γραμμένο JSON, έπεφτε στα
/// προεπιλεγμένα και το επόμενο Save() έσβηνε τις πραγματικές ρυθμίσεις του χρήστη. Το ίδιο
/// συνέβαινε και σε διακοπή ρεύματος/crash στη μέση της εγγραφής.
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, contents, new UTF8Encoding(false));
        KeepBackup(path);
        File.Move(tmp, path, overwrite: true);
    }

    // Keeps the previous GOOD file as "<name>.bak" (refreshed at most every 10 minutes so frequent saves do not rewrite it constantly,
    // and never overwritten with a file that is not valid JSON). ReadAllText restores it automatically if the main file is damaged.
    private static void KeepBackup(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            string bak = path + ".bak";
            if (File.Exists(bak) && DateTime.UtcNow - File.GetLastWriteTimeUtc(bak) < TimeSpan.FromMinutes(10)) return;
            if (!IsValidJson(File.ReadAllText(path))) return;
            File.Copy(path, bak, overwrite: true);
        }
        catch (Exception) { /* a backup is a convenience, never a reason to fail a save */ }
    }

    private static bool IsValidJson(string text)
    {
        try { using var _ = JsonDocument.Parse(text); return true; }
        catch (JsonException) { return false; }
    }

    // Ανάγνωση με λίγες επαναλήψεις: ένα στιγμιαίο sharing violation (το rename της εγγραφής,
    // antivirus, OneDrive) δεν πρέπει να μετατρέπεται σε "χάθηκαν οι ρυθμίσεις → προεπιλογές".
    public static string ReadAllText(string path)
    {
        string text = ReadWithRetry(path);
        if (IsValidJson(text)) return text;

        // The main file is empty/corrupt (crash mid-write on an old version, disk problem, manual edit): restore the last good copy.
        string bak = path + ".bak";
        try
        {
            if (File.Exists(bak))
            {
                string good = File.ReadAllText(bak);
                if (IsValidJson(good)) { File.Copy(bak, path, overwrite: true); return good; }
            }
        }
        catch (Exception) { /* fall through with what we have */ }
        return text;
    }

    private static string ReadWithRetry(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (attempt < 4) { Thread.Sleep(25); }
        }
    }
}
