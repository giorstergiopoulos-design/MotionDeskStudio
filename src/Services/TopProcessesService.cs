using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace MotionDesk.Services
{
    // Processes grouped by name and ranked by memory (working set) — for the Performance page and the high-usage alert.
    public static class TopProcessesService
    {
        public static List<(string Name, double Mb)> TopByMemory(int count)
        {
            var groups = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try { groups[p.ProcessName] = (groups.TryGetValue(p.ProcessName, out var v) ? v : 0) + p.WorkingSet64; }
                catch (Exception) { /* access denied / exited */ }
                finally { p.Dispose(); }
            }
            return groups.OrderByDescending(kv => kv.Value).Take(count).Select(kv => (kv.Key, kv.Value / 1048576.0)).ToList();
        }
    }
}
