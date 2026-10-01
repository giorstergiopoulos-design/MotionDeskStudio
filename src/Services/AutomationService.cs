using System.Diagnostics;
using System.Text.Json;

namespace MotionDesk.Services;

public sealed class AutomationRule
{
    public string Name { get; set; } = "Gaming Mode";
    public string TriggerProcess { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed class AutomationService : IDisposable
{
    private static AutomationService? _instance;
    public static AutomationService Instance => _instance ??= new AutomationService();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10000 };
    private readonly Dictionary<string, DateTime> _lastApplied = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MotionDeskStudio", "automation.json");
    public event EventHandler<string>? StatusChanged;
    public List<AutomationRule> Rules { get; } = new();

    private AutomationService() { Load(); _timer.Tick += (_, _) => Evaluate(); _timer.Start(); }
    public void Load()
    {
        try { if (File.Exists(_path)) Rules.AddRange(JsonSerializer.Deserialize<List<AutomationRule>>(File.ReadAllText(_path)) ?? new()); } catch { }
        if (Rules.Count == 0) Rules.Add(new AutomationRule { Name = "Gaming Mode", TriggerProcess = "steamwebhelper", Profile = "Gaming", Enabled = false });
    }
    public void Save() { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); MotionDesk.Services.AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(Rules, new JsonSerializerOptions { WriteIndented = true })); }
    private readonly HashSet<string> _wasRunning = new(StringComparer.OrdinalIgnoreCase);

    // Εφαρμόζει το προφίλ ΜΟΝΟ τη στιγμή που ξεκινά η διεργασία (μετάβαση "δεν τρέχει" → "τρέχει"). Πριν,
    // όσο έτρεχε π.χ. ένα παιχνίδι το προφίλ ξαναφορτωνόταν κάθε 30s — κάθε φορά έκλεινε και ξανάνοιγε ΟΛΑ
    // τα widgets/containers (flicker) και έσβηνε τις χειροκίνητες αλλαγές του χρήστη.
    private void Evaluate()
    {
        foreach (var r in Rules.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.TriggerProcess) && !string.IsNullOrWhiteSpace(x.Profile)).ToList())
        {
            bool running = RunningProcessCache.IsRunningByName(Path.GetFileNameWithoutExtension(r.TriggerProcess));
            if (!running) { _wasRunning.Remove(r.Name); continue; }
            if (!_wasRunning.Add(r.Name)) continue; // ήδη τρέχει και το έχουμε εφαρμόσει
            if (WorkspaceProfileService.Load(r.Profile)) { _lastApplied[r.Name] = DateTime.UtcNow; StatusChanged?.Invoke(this, $"Automation: loaded {r.Profile}"); }
        }
    }
    public void Dispose() { _timer.Stop(); _timer.Dispose(); }
}
