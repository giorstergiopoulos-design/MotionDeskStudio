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

        private readonly float[] _samples = new float[FftLength];
        private bool _acquired;
        private readonly Complex[] _fftBuffer = new Complex[FftLength];

        public int BandCount { get; }
        private readonly float[] _bands;
        private readonly int[] _bandBinEdges;
        public bool IsAvailable => _acquired && SharedLoopbackCapture.IsAvailable;

        // "Audio Enhancement" — ζητήθηκε ρητά, εμπνευσμένο από τη ΛΟΓΙΚΗ του FXSound (github.com/
        // fxsound2/fxsound-app), όχι αντιγραφή: το FXSound τρέχει σαν system-wide Audio Processing
        // Object (APO) καταχωρημένο στον driver graph των Windows — πραγματική αλλαγή του ήχου
        // ΟΛΩΝ των εφαρμογών απαιτεί ακριβώς αυτόν τον μηχανισμό (ή εικονική κάρτα ήχου), κανένα
        // από τα δύο δεν είναι εφικτό μέσα σε μια συνεδρία .NET/WinForms χωρίς signed driver
        // component — ΔΕΝ προσποιούμαστε ότι το κάνουμε. Αυτό που είναι πραγματικό και δουλεύει:
        // configurable per-band κέρδος (ίδια ιδέα με τα presets ενός equalizer) εφαρμοσμένο στην
        // ΗΔΗ ζωντανή ανάλυση φάσματος (WASAPI loopback) — αλλάζει πραγματικά το πώς αποδίδεται/
        // απεικονίζεται ο ήχος στον visualizer, με πραγματικά δεδομένα, όχι fake.
        public float[] BandGains { get; }
        public static readonly (string Name, string Description)[] Presets =
        {
            ("Flat", "Χωρίς ενίσχυση — ουδέτερη απεικόνιση."),
            ("Bass Boost", "Έμφαση στις χαμηλές συχνότητες (μπάσα)."),
            ("Treble Boost", "Έμφαση στις υψηλές συχνότητες (πρίμα)."),
            ("Vocal Boost", "Έμφαση στις μεσαίες συχνότητες (φωνή)."),
            ("Loudness", "Ενίσχυση μπάσων ΚΑΙ πρίμων μαζί (καμπύλη Fletcher-Munson, στυλ FXSound \"Loudness\")."),
        };

        public AudioSpectrumService(int bandCount = 20)
        {
            BandCount = bandCount;
            _bands = new float[bandCount];
            BandGains = new float[bandCount];
            Array.Fill(BandGains, 1f);
            _bandBinEdges = BuildLogBandEdges(bandCount, FftLength / 2);

            SharedLoopbackCapture.Acquire(); // κοινό capture — βλ. SharedLoopbackCapture
            _acquired = true;
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

        // Επιστρέφει BandCount τιμές 0..1 (dB-scaled). Αν το loopback capture απέτυχε να
        // αρχικοποιηθεί (π.χ. καμία συσκευή ήχου), επιστρέφει όλα μηδέν αντί να πετάξει exception
        // — ο caller απλά βλέπει επίπεδες μπάρες, όχι crash.
        public float[] GetBands()
        {
            if (!_acquired) return _bands;

            SharedLoopbackCapture.CopyLatest(_samples);
            {
                for (int i = 0; i < FftLength; i++)
                {
                    double window = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (FftLength - 1)); // Hamming
                    _fftBuffer[i].X = (float)(_samples[i] * window);
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
                double level = (db + 60) / 60.0 * BandGains[b];
                _bands[b] = (float)Math.Clamp(level, 0.0, 1.0);
            }

            return _bands;
        }

        // Υπολογίζει την καμπύλη κέρδους ανά ζώνη για ένα preset — οι χαμηλές ζώνες (index 0) =
        // μπάσα, οι υψηλές (index BandCount-1) = πρίμα, λογαριθμικά κατανεμημένες (ίδια σύμβαση
        // με το BuildLogBandEdges παραπάνω).
        public void ApplyPreset(string presetName)
        {
            for (int b = 0; b < BandCount; b++)
            {
                double t = b / (double)Math.Max(1, BandCount - 1); // 0 = πιο μπάσο, 1 = πιο πρίμο
                BandGains[b] = presetName switch
                {
                    "Bass Boost" => (float)(1.0 + 0.9 * Math.Max(0, 1 - t * 2.2)),
                    "Treble Boost" => (float)(1.0 + 0.9 * Math.Max(0, (t - 0.45) * 1.8)),
                    "Vocal Boost" => (float)(1.0 + 0.7 * Math.Exp(-Math.Pow((t - 0.5) * 3.2, 2))),
                    "Loudness" => (float)(1.0 + 0.55 * Math.Max(0, 1 - t * 2.0) + 0.45 * Math.Max(0, (t - 0.55) * 2.2)),
                    _ => 1f, // "Flat"
                };
            }
        }

        public void Dispose()
        {
            if (!_acquired) return;
            _acquired = false;
            SharedLoopbackCapture.Release();
        }
    }
}
