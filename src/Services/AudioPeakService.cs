using NAudio.CoreAudioApi;

namespace MotionDesk.Services
{
    // Wraps the WASAPI master peak meter of the default render (output) device,
    // so wallpapers/widgets can react to whatever audio the system is currently playing.
    // Ξαναδιαβάζει την προεπιλεγμένη συσκευή εξόδου ανά ~5s: πριν κρατούσε για πάντα τη συσκευή της
    // στιγμής δημιουργίας, οπότε μετά από αλλαγή εξόδου (ακουστικά/Bluetooth/HDMI) ο δείκτης έμενε στο 0.
    public class AudioPeakService : System.IDisposable
    {
        private static readonly System.TimeSpan RefreshEvery = System.TimeSpan.FromSeconds(5);
        private readonly MMDeviceEnumerator? _enumerator;
        private MMDevice? _device;
        private string? _deviceId;
        private System.DateTime _lastResolve = System.DateTime.MinValue;

        public AudioPeakService()
        {
            try { _enumerator = new MMDeviceEnumerator(); }
            catch (System.Runtime.InteropServices.COMException) { _enumerator = null; }
            ResolveDevice();
        }

        private void ResolveDevice()
        {
            _lastResolve = System.DateTime.UtcNow;
            if (_enumerator == null) return;
            try
            {
                var current = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (current.ID == _deviceId) { current.Dispose(); return; }
                _device?.Dispose();
                _device = current;
                _deviceId = current.ID;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                _device?.Dispose();
                _device = null;
                _deviceId = null;
            }
        }

        public void Dispose()
        {
            _device?.Dispose();
            _enumerator?.Dispose();
        }

        public float GetPeak()
        {
            if (System.DateTime.UtcNow - _lastResolve > RefreshEvery) ResolveDevice();
            try
            {
                return _device?.AudioMeterInformation?.MasterPeakValue ?? 0f;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Η συσκευή αφαιρέθηκε/άλλαξε — ξαναδοκιμάζουμε στο επόμενο poll.
                _lastResolve = System.DateTime.MinValue;
                return 0f;
            }
        }
    }
}
