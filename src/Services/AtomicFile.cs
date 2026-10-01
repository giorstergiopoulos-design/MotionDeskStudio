using System.Text;

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
        File.Move(tmp, path, overwrite: true);
    }

    // Ανάγνωση με λίγες επαναλήψεις: ένα στιγμιαίο sharing violation (το rename της εγγραφής,
    // antivirus, OneDrive) δεν πρέπει να μετατρέπεται σε "χάθηκαν οι ρυθμίσεις → προεπιλογές".
    public static string ReadAllText(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (attempt < 4) { Thread.Sleep(25); }
        }
    }
}
