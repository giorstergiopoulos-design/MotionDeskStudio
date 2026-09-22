using NAudio.CoreAudioApi;

namespace MotionDesk.Services
{
    // Wraps the WASAPI master peak meter of the default render (output) device,
    // so wallpapers/widgets can react to whatever audio the system is currently playing.
    public class AudioPeakService : System.IDisposable
    {
        private readonly MMDeviceEnumerator? _enumerator;
        private readonly MMDevice? _device;

        public AudioPeakService()
        {
            try
            {
                _enumerator = new MMDeviceEnumerator();
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                _device = null;
            }
        }

        public void Dispose()
        {
            _device?.Dispose();
            _enumerator?.Dispose();
        }

        public float GetPeak()
        {
            try
            {
                return _device?.AudioMeterInformation?.MasterPeakValue ?? 0f;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return 0f;
            }
        }
    }
}
