using System;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace MotionDesk.Services
{
    public sealed class GpuSnapshot
    {
        public string Name { get; set; } = "—";
        public double? LoadPercent { get; set; }
        public double? TemperatureC { get; set; }
        public double? CoreVoltage { get; set; }
        public double? MemoryUsedMb { get; set; }
        public bool Available { get; set; }
    }

    // Πραγματικά sensor data GPU (φόρτος/θερμοκρασία/τάση πυρήνα) μέσω LibreHardwareMonitorLib
    // (ανοιχτού κώδικα, MPL-2.0, ο διάδοχος του OpenHardwareMonitor) — ζητήθηκε ρητά "voltage της
    // GPU" στο System Monitor widget. Οι στάνταρ Win32 performance counters (αυτό που ήδη
    // χρησιμοποιεί το SystemMonitorService για CPU/RAM) ΔΕΝ εκθέτουν καθόλου τάση — μόνο μια
    // πραγματική βιβλιοθήκη hardware sensors μπορεί να τη διαβάσει. Ενεργοποιείται ΜΟΝΟ το GPU
    // (όχι CPU/motherboard) ώστε να ΜΗΝ χρειάζεται ο πυρηνικός driver (WinRing0) που απαιτεί
    // Administrator — τα GPU sensors διαβάζονται μέσω NVAPI/ADL/Intel vendor APIs, διαθέσιμα
    // συνήθως και χωρίς elevation. Αν κάποιος sensor δεν είναι διαθέσιμος σε αυτό το σύστημα,
    // γυρνάει null αντί να πετάξει exception — το widget δείχνει "—" σε αυτή την περίπτωση.
    public sealed class GpuMonitorService : IDisposable
    {
        private static GpuMonitorService? _instance;
        public static GpuMonitorService Instance => _instance ??= new GpuMonitorService();

        private readonly Computer? _computer;
        private readonly UpdateVisitor _visitor = new();
        private readonly bool _opened;

        private GpuMonitorService()
        {
            try
            {
                _computer = new Computer { IsGpuEnabled = true };
                _computer.Open();
                _opened = true;
            }
            catch { _computer = null; _opened = false; }
        }

        public GpuSnapshot GetSnapshot()
        {
            var snap = new GpuSnapshot();
            if (!_opened || _computer == null) return snap;
            try
            {
                _computer.Accept(_visitor);
                var gpu = _computer.Hardware.FirstOrDefault(h =>
                    h.HardwareType == HardwareType.GpuNvidia ||
                    h.HardwareType == HardwareType.GpuAmd ||
                    h.HardwareType == HardwareType.GpuIntel);
                if (gpu == null) return snap;

                snap.Name = gpu.Name;
                snap.Available = true;
                foreach (var sensor in gpu.Sensors)
                {
                    if (sensor.Value == null) continue;
                    switch (sensor.SensorType)
                    {
                        case SensorType.Load when sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && snap.LoadPercent == null:
                            snap.LoadPercent = sensor.Value; break;
                        case SensorType.Temperature when snap.TemperatureC == null:
                            snap.TemperatureC = sensor.Value; break;
                        case SensorType.Voltage when snap.CoreVoltage == null:
                            snap.CoreVoltage = sensor.Value; break;
                        case SensorType.SmallData when sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase) && snap.MemoryUsedMb == null:
                            snap.MemoryUsedMb = sensor.Value; break;
                    }
                }
            }
            catch { }
            return snap;
        }

        public void Dispose()
        {
            try { if (_opened) _computer?.Close(); } catch { }
        }

        private sealed class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer) => computer.Traverse(this);
            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (var sub in hardware.SubHardware) sub.Accept(this);
            }
            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }
    }
}
