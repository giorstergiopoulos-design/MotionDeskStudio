using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MotionDesk.Services;

/// <summary>
/// ΕΝΑ κοινό WASAPI loopback capture για όλη την εφαρμογή (ref-counted). Πριν, κάθε
/// AudioSpectrumService (κάθε Audio widget + η σελίδα Audio Enhancement) άνοιγε ΔΙΚΟ ΤΟΥ capture
/// στη συσκευή εξόδου — πολλαπλές ροές, πολλαπλά buffers και callbacks για τα ίδια ακριβώς δείγματα.
/// Επίσης ακολουθεί την αλλαγή προεπιλεγμένης συσκευής εξόδου (π.χ. ακουστικά/Bluetooth): το
/// RecordingStopped επανεκκινεί το capture στη νέα συσκευή αντί να μένει νεκρό.
/// </summary>
internal static class SharedLoopbackCapture
{
    public const int RingLength = 1024;
    private static readonly object Sync = new();
    private static readonly float[] Ring = new float[RingLength];
    private static int _ringPos;
    private static long _lastDataTick;
    private static int _refCount;
    private static WasapiLoopbackCapture? _capture;
    private static System.Threading.Timer? _restartTimer;

    public static bool IsAvailable { get { lock (Sync) return _capture != null; } }

    public static void Acquire()
    {
        lock (Sync)
        {
            if (_refCount++ == 0) StartLocked();
        }
    }

    public static void Release()
    {
        lock (Sync)
        {
            if (_refCount == 0) return;
            if (--_refCount == 0) StopLocked();
        }
    }

    // Αντιγράφει τα τελευταία RingLength δείγματα (παλαιότερο → νεότερο) στο dest. Σε σιωπή (καθόλου
    // δεδομένα >250ms — το loopback δεν στέλνει τίποτα όταν δεν παίζει ήχος) γεμίζει μηδενικά.
    public static void CopyLatest(float[] dest)
    {
        lock (Sync)
        {
            bool silent = Environment.TickCount64 - _lastDataTick > 250;
            for (int i = 0; i < RingLength; i++)
                dest[i] = silent ? 0f : Ring[(_ringPos + i) % RingLength];
        }
    }

    private static void StartLocked()
    {
        try
        {
            var capture = new WasapiLoopbackCapture();
            capture.DataAvailable += OnData;
            capture.RecordingStopped += OnStopped;
            capture.StartRecording();
            _capture = capture;
        }
        catch { _capture = null; ScheduleRestartLocked(); }
    }

    private static void StopLocked()
    {
        _restartTimer?.Dispose(); _restartTimer = null;
        var c = _capture; _capture = null;
        if (c == null) return;
        c.DataAvailable -= OnData;
        c.RecordingStopped -= OnStopped;
        try { c.StopRecording(); } catch { }
        c.Dispose();
    }

    // Η συσκευή άλλαξε/αφαιρέθηκε (ή σφάλμα): ξαναδοκιμάζουμε σε λίγο στη νέα προεπιλεγμένη.
    private static void OnStopped(object? sender, StoppedEventArgs e)
    {
        lock (Sync)
        {
            if (_refCount == 0) return;
            var c = _capture; _capture = null;
            if (c != null) { c.DataAvailable -= OnData; c.RecordingStopped -= OnStopped; try { c.Dispose(); } catch { } }
            ScheduleRestartLocked();
        }
    }

    private static void ScheduleRestartLocked()
    {
        _restartTimer?.Dispose();
        _restartTimer = new System.Threading.Timer(_ =>
        {
            lock (Sync)
            {
                if (_refCount == 0 || _capture != null) return;
                StartLocked();
            }
        }, null, 2000, System.Threading.Timeout.Infinite);
    }

    private static void OnData(object? sender, WaveInEventArgs e)
    {
        var capture = sender as WasapiLoopbackCapture;
        if (capture == null) return;
        int bytesPerSample = capture.WaveFormat.BitsPerSample / 8;
        int channels = Math.Max(1, capture.WaveFormat.Channels);
        int frameSize = bytesPerSample * channels;
        if (frameSize <= 0) return;
        int frames = e.BytesRecorded / frameSize;

        lock (Sync)
        {
            _lastDataTick = Environment.TickCount64;
            for (int i = 0; i < frames; i++)
            {
                float sample = 0;
                for (int c = 0; c < channels; c++)
                {
                    int offset = i * frameSize + c * bytesPerSample;
                    if (offset + 4 > e.BytesRecorded) continue;
                    sample += BitConverter.ToSingle(e.Buffer, offset);
                }
                Ring[_ringPos] = sample / channels;
                _ringPos = (_ringPos + 1) % RingLength;
            }
        }
    }
}
