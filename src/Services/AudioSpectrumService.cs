using System;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace MotionDesk.Services
{
    // Πραγματικός multi-band equalizer (όχι προσομοίωση από ένα peak value): καταγράφει το
    // system audio output μέσω WASAPI loopback, τρέχει FFT σε κάθε buffer και ομαδοποιεί τα
    // bins σε N λογαριθμικές ζώνες συχνότητας (έτσι ακριβώς δουλεύουν οι περισσότεροι equalizers
    // — η μουσική ενέργεια κατανέμεται λογαριθμικά, όχι γραμμικά). Το NAudio.Dsp.FastFourierTransform
    // υπάρχει ήδη μέσα στο NAudio.Core (ήδη transitive dependency του NAudio.Wasapi που έχουμε) —
    // δεν χρειάστηκε νέο πακέτο.
    public sealed class AudioSpectrumService : IDisposable
    {
        private const int FftLength = 1024;
        private readonly int _m = (int)Math.Log(FftLength, 2.0);

        private WasapiLoopbackCapture? _capture;
        private readonly float[] _ring = new float[FftLength];
        private int _ringPos;
        private readonly object _lock = new();
        private readonly Complex[] _fftBuffer = new Complex[FftLength];

        public int BandCount { get; }
        private readonly float[] _bands;
        private readonly int[] _bandBinEdges;
        public bool IsAvailable => _capture != null;

        public AudioSpectrumService(int bandCount = 20)
        {
            BandCount = bandCount;
            _bands = new float[bandCount];
            _bandBinEdges = BuildLogBandEdges(bandCount, FftLength / 2);

            try
            {
                _capture = new WasapiLoopbackCapture();
                _capture.DataAvailable += OnDataAvailable;
                _capture.StartRecording();
            }
            catch (Exception)
            {
                _capture = null;
            }
        }

        private static int[] BuildLogBandEdges(int bandCount, int totalBins)
        {
            var edges = new int[bandCount + 1];
            double logMin = Math.Log(2);
            double logMax = Math.Log(totalBins);
            for (int i = 0; i <= bandCount; i++)
            {
                double t = (double)i / bandCount;
                edges[i] = (int)Math.Round(Math.Exp(logMin + t * (logMax - logMin)));
            }
            for (int i = 1; i <= bandCount; i++)
                if (edges[i] <= edges[i - 1]) edges[i] = edges[i - 1] + 1;
            return edges;
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_capture == null) return;
            int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
            int channels = Math.Max(1, _capture.WaveFormat.Channels);
            int frameSize = bytesPerSample * channels;
            if (frameSize <= 0) return;
            int frames = e.BytesRecorded / frameSize;

            lock (_lock)
            {
                for (int i = 0; i < frames; i++)
                {
                    float sample = 0;
                    for (int c = 0; c < channels; c++)
                    {
                        int offset = i * frameSize + c * bytesPerSample;
                        if (offset + 4 > e.BytesRecorded) continue;
                        sample += BitConverter.ToSingle(e.Buffer, offset);
                    }
                    sample /= channels;
                    _ring[_ringPos] = sample;
                    _ringPos = (_ringPos + 1) % FftLength;
                }
            }
        }

        // Επιστρέφει BandCount τιμές 0..1 (dB-scaled). Αν το loopback capture απέτυχε να
        // αρχικοποιηθεί (π.χ. καμία συσκευή ήχου), επιστρέφει όλα μηδέν αντί να πετάξει exception
        // — ο caller απλά βλέπει επίπεδες μπάρες, όχι crash.
        public float[] GetBands()
        {
            if (_capture == null) return _bands;

            lock (_lock)
            {
                for (int i = 0; i < FftLength; i++)
                {
                    int idx = (_ringPos + i) % FftLength;
                    double window = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (FftLength - 1)); // Hamming
                    _fftBuffer[i].X = (float)(_ring[idx] * window);
                    _fftBuffer[i].Y = 0;
                }
            }

            FastFourierTransform.FFT(true, _m, _fftBuffer);

            for (int b = 0; b < BandCount; b++)
            {
                int startBin = Math.Max(1, _bandBinEdges[b]);
                int endBin = Math.Min(FftLength / 2, _bandBinEdges[b + 1]);
                double sumMag = 0;
                int count = 0;
                for (int bin = startBin; bin < endBin; bin++)
                {
                    float re = _fftBuffer[bin].X, im = _fftBuffer[bin].Y;
                    sumMag += Math.Sqrt(re * re + im * im);
                    count++;
                }
                double mag = count > 0 ? sumMag / count : 0;

                // dB-scale mapping: -60dB..0dB -> 0..1, τυπική αναλογία για audio visualizers.
                double db = 20 * Math.Log10(mag + 1e-6);
                double level = (db + 60) / 60.0;
                _bands[b] = (float)Math.Clamp(level, 0.0, 1.0);
            }

            return _bands;
        }

        public void Dispose()
        {
            try { _capture?.StopRecording(); } catch (Exception) { }
            _capture?.Dispose();
            _capture = null;
        }
    }
}
