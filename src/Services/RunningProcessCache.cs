using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MotionDesk.Services;

/// <summary>
/// Κοινή, χρονικά cached εικόνα των τρεχουσών διεργασιών (ονόματα + διαδρομές εκτελέσιμων).
/// Πριν, το DeskStrip (ανά εικονίδιο, κάθε 2.5s) και το Automation (ανά κανόνα, κάθε 10s) έκαναν
/// ΞΕΧΩΡΙΣΤΟ Process.GetProcesses()/MainModule χωρίς Dispose — εκατοντάδες Process objects και
/// handles ανά κύκλο, με διαρροή πόρων και αισθητό CPU. Εδώ γίνεται ΕΝΑ snapshot ανά TTL,
/// με Dispose κάθε Process και φθηνό QueryFullProcessImageName (χωρίς φόρτωση module list).
/// Καλείται μόνο από το UI thread.
/// </summary>
public static class RunningProcessCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(2);
    private static DateTime _stamp = DateTime.MinValue;
    private static HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsRunningByName(string processName)
    {
        Refresh();
        return _names.Contains(processName);
    }

    public static bool IsRunningByPath(string fullPath)
    {
        Refresh();
        return _paths.Contains(fullPath);
    }

    private static void Refresh()
    {
        var now = DateTime.UtcNow;
        if (_stamp != DateTime.MinValue && now - _stamp < Ttl) return;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Process[] all;
        try { all = Process.GetProcesses(); } catch { return; }
        foreach (var p in all)
        {
            try
            {
                names.Add(p.ProcessName);
                string? path = QueryImagePath(p.Id);
                if (path != null) paths.Add(path);
            }
            catch { /* η διεργασία τερματίστηκε στο μεταξύ */ }
            finally { p.Dispose(); }
        }
        _names = names;
        _paths = paths;
        _stamp = now;
    }

    private static string? QueryImagePath(int pid)
    {
        IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder name, ref int size);
}
