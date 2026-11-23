using System.Numerics;

namespace OpenSynapse.Core.Lighting;

/// <summary>
/// One 1024-sample analysis of the loopback mix, shared by every audio-reactive
/// effect. Bands and waveform are normalized to 0..1; Beat is true only on the
/// frame where an onset is detected. The arrays are read-only by contract.
/// </summary>
public sealed class StudioAudioFrame
{
    public const int BandCount = 18;
    public const int WaveformCount = 24;

    public static StudioAudioFrame Silence { get; } = new(
        0, 0, new float[BandCount], new float[WaveformCount], 0, 0, 0, false, 120);

    internal StudioAudioFrame(
        double rms,
        double peak,
        float[] bands,
        float[] waveform,
        double low,
        double mid,
        double high,
        bool beat,
        double bpm)
    {
        Rms = rms;
        Peak = peak;
        Bands = bands;
        Waveform = waveform;
        Low = low;
        Mid = mid;
        High = high;
        Beat = beat;
        Bpm = bpm;
    }

    /// <summary>Normalized 0..1 RMS of the packet that produced this frame.</summary>
    public double Rms { get; }

    /// <summary>Normalized 0..1 peak of the packet that produced this frame.</summary>
    public double Peak { get; }

    /// <summary>18 log-spaced bands from 40 Hz to 16 kHz, normalized 0..1.</summary>
    public float[] Bands { get; }

    /// <summary>24 time slices of the latest 1024 samples, normalized 0..1.</summary>
    public float[] Waveform { get; }

    public double Low { get; }

    public double Mid { get; }

    public double High { get; }

    public bool Beat { get; }

    public double Bpm { get; }
}

/// <summary>
/// 1024-point FFT over a mono mix of the loopback stream. One instance per
/// capture session. Analyze returns a fresh frame so a consumer can compare
/// beat edges without the next packet mutating its input.
/// </summary>
public sealed class StudioAudioAnalyzer
{
    public const int FftSize = 1024;
    public const int BandCount = StudioAudioFrame.BandCount;
    public const int WaveformCount = StudioAudioFrame.WaveformCount;

    private const double MinFrequency = 40;
    private const double MaxFrequency = 16000;
    private const double BandReleaseSeconds = 0.12;
    private const double BeatRefractorySeconds = 0.18;
    private const double FloorDb = -60;

    private static readonly double[] HannWindow = BuildHannWindow();

    private readonly int _sampleRate;
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly double[] _magnitude = new double[FftSize / 2];
    private readonly float[] _ring = new float[FftSize];
    private readonly float[] _bands = new float[BandCount];
    private readonly double[] _edges = new double[BandCount + 1];
    private int _ringIndex;
    private double _lastSeconds;
    private double _beatAverage;
    private double _lastBeatSeconds = double.NegativeInfinity;
    private double _bpm = 120;

    public StudioAudioAnalyzer(int sampleRate)
    {
        if (sampleRate < 8000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        _sampleRate = sampleRate;
        var maximum = Math.Min(MaxFrequency, sampleRate / 2.0);
        for (var index = 0; index <= BandCount; index++)
        {
            _edges[index] = MinFrequency * Math.Pow(
                maximum / MinFrequency,
                index / (double)BandCount);
        }
    }

    /// <summary>Center frequency of one band; useful for labels and tests.</summary>
    public double BandCenterFrequency(int index)
    {
        if ((uint)index >= BandCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return Math.Sqrt(_edges[index] * _edges[index + 1]);
    }

    /// <summary>Appends mono samples to the analysis window.</summary>
    public void Push(ReadOnlySpan<float> mono)
    {
        foreach (var value in mono)
        {
            _ring[_ringIndex] = float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0;
            _ringIndex = (_ringIndex + 1) % FftSize;
        }
    }

    /// <summary>Appends silence so old audio drains out of the window.</summary>
    public void PushSilence(int sampleCount)
    {
        for (var index = 0; index < sampleCount; index++)
        {
            _ring[_ringIndex] = 0;
            _ringIndex = (_ringIndex + 1) % FftSize;
        }
    }

    /// <summary>
    /// Computes bands, waveform, low/mid/high energy and beat state at
    /// <paramref name="nowSeconds"/>. Rms/Peak come from the packet meter.
    /// </summary>
    public StudioAudioFrame Analyze(double nowSeconds, double rms, double peak)
    {
        var delta = _lastSeconds <= 0
            ? 0.02
            : Math.Clamp(nowSeconds - _lastSeconds, 0.001, 0.25);
        _lastSeconds = nowSeconds;

        FillFft();
        Fft(_fft);

        var scale = 2.0 / FftSize;
        for (var bin = 1; bin < _magnitude.Length; bin++)
        {
            _magnitude[bin] = _fft[bin].Magnitude * scale;
        }

        var release = (float)Math.Exp(-delta / BandReleaseSeconds);
        double low = 0, mid = 0, high = 0;
        for (var band = 0; band < BandCount; band++)
        {
            var lowBin = Math.Clamp(
                (int)Math.Ceiling(_edges[band] * FftSize / _sampleRate),
                1,
                _magnitude.Length - 1);
            var highBin = Math.Clamp(
                (int)Math.Floor(_edges[band + 1] * FftSize / _sampleRate),
                lowBin,
                _magnitude.Length - 1);

            var magnitude = 0d;
            for (var bin = lowBin; bin <= highBin; bin++)
            {
                magnitude = Math.Max(magnitude, _magnitude[bin]);
            }

            var decibels = 20 * Math.Log10(magnitude + 1e-9);
            var value = (float)Math.Clamp(
                (decibels - FloorDb) / -FloorDb,
                0,
                1);
            _bands[band] = Math.Max(value, _bands[band] * release);

            var center = Math.Sqrt(_edges[band] * _edges[band + 1]);
            if (center < 250)
            {
                low = Math.Max(low, _bands[band]);
            }
            else if (center < 2000)
            {
                mid = Math.Max(mid, _bands[band]);
            }
            else
            {
                high = Math.Max(high, _bands[band]);
            }
        }

        var beat = false;
        if (low > 0.35)
        {
            var threshold = _beatAverage <= 0 ? 0.45 : _beatAverage * 1.35 + 0.04;
            if (low > threshold &&
                nowSeconds - _lastBeatSeconds >= BeatRefractorySeconds)
            {
                beat = true;
                if (double.IsFinite(_lastBeatSeconds))
                {
                    var interval = nowSeconds - _lastBeatSeconds;
                    if (interval is > 0.25 and < 2.0)
                    {
                        _bpm = Math.Clamp(_bpm * 0.7 + 60 / interval * 0.3, 60, 200);
                    }
                }

                _lastBeatSeconds = nowSeconds;
            }

            _beatAverage = _beatAverage <= 0
                ? low
                : _beatAverage * 0.92 + low * 0.08;
        }
        else
        {
            _beatAverage *= 0.9;
        }

        var waveform = new float[WaveformCount];
        for (var index = 0; index < WaveformCount; index++)
        {
            var end = Math.Min(FftSize, (index + 1) * FftSize / WaveformCount);
            var peakValue = 0f;
            for (var sample = index * FftSize / WaveformCount; sample < end; sample++)
            {
                peakValue = Math.Max(
                    peakValue,
                    Math.Abs(_ring[(_ringIndex + sample) % FftSize]));
            }

            waveform[index] = peakValue;
        }

        return new StudioAudioFrame(
            Math.Clamp(rms, 0, 1),
            Math.Clamp(peak, 0, 1),
            (float[])_bands.Clone(),
            waveform,
            low,
            mid,
            high,
            beat,
            _bpm);
    }

    private void FillFft()
    {
        for (var index = 0; index < FftSize; index++)
        {
            _fft[index] = new Complex(
                _ring[(_ringIndex + index) % FftSize] * HannWindow[index],
                0);
        }
    }

    private static void Fft(Complex[] buffer)
    {
        var length = buffer.Length;
        for (int index = 1, reversed = 0; index < length; index++)
        {
            var bit = length >> 1;
            while ((reversed & bit) != 0)
            {
                reversed ^= bit;
                bit >>= 1;
            }

            reversed ^= bit;
            if (index < reversed)
            {
                (buffer[index], buffer[reversed]) = (buffer[reversed], buffer[index]);
            }
        }

        for (var size = 2; size <= length; size <<= 1)
        {
            var angle = -2 * Math.PI / size;
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var offset = 0; offset < length; offset += size)
            {
                var weight = Complex.One;
                for (var index = 0; index < size / 2; index++)
                {
                    var even = buffer[offset + index];
                    var odd = buffer[offset + index + size / 2] * weight;
                    buffer[offset + index] = even + odd;
                    buffer[offset + index + size / 2] = even - odd;
                    weight *= step;
                }
            }
        }
    }

    private static double[] BuildHannWindow()
    {
        var window = new double[FftSize];
        for (var index = 0; index < FftSize; index++)
        {
            window[index] = 0.5 * (1 - Math.Cos(2 * Math.PI * index / (FftSize - 1)));
        }

        return window;
    }
}
