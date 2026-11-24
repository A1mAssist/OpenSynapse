namespace OpenSynapse.Core.Lighting;

/// <summary>Deterministic stand-ins for music, screen, key and system inputs in the editor.</summary>
public static class StudioPresetPreviewSimulation
{
    public static StudioEffectInput Create(
        double seconds,
        IReadOnlyList<StudioPresetLayer> layers,
        StudioKeyState? keys)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var time = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        var music = layers.Any(layer => layer.ModuleId.StartsWith("music.", StringComparison.Ordinal));
        var screen = layers.Any(layer => layer.ModuleId.StartsWith("screen.", StringComparison.Ordinal));
        var system = layers.Any(layer => layer.ModuleId.StartsWith("system.", StringComparison.Ordinal));
        return new StudioEffectInput(
            Audio: music ? Audio(time) : null,
            Keys: keys,
            ScreenSamplerUv: screen ? ScreenSampler(time) : null,
            System: system ? System(time) : null);
    }

    private static StudioAudioFrame Audio(double time)
    {
        var bands = new float[StudioAudioFrame.BandCount];
        for (var index = 0; index < bands.Length; index++)
        {
            var wave = Math.Sin(time * (1.1 + index * 0.13) + index * 0.7) *
                Math.Cos(time * 0.37 + index * 0.21);
            bands[index] = (float)Math.Clamp(0.24 + 0.62 * Math.Abs(wave), 0, 1);
        }

        var waveform = new float[StudioAudioFrame.WaveformCount];
        for (var index = 0; index < waveform.Length; index++)
        {
            waveform[index] = bands[index % bands.Length];
        }

        var peaks = bands.Select(value => Math.Min(1f, value + 0.08f)).ToArray();
        var columns = new float[StudioAudioFrame.ColumnCount];
        var columnPeaks = new float[StudioAudioFrame.ColumnCount];
        for (var index = 0; index < columns.Length; index++)
        {
            columns[index] = BandAt(bands, index / (double)(columns.Length - 1));
            columnPeaks[index] = BandAt(peaks, index / (double)(peaks.Length - 1));
        }

        var history = new float[StudioAudioFrame.HistoryDepth][];
        for (var row = 0; row < history.Length; row++)
        {
            var past = time - row * 0.09;
            history[row] = new float[StudioAudioFrame.BandCount];
            for (var index = 0; index < history[row].Length; index++)
            {
                history[row][index] = (float)Math.Clamp(
                    0.24 + 0.62 * Math.Abs(Math.Sin(past * (1.1 + index * 0.13) + index * 0.7) *
                        Math.Cos(past * 0.37 + index * 0.21)), 0, 1);
            }
        }

        const double beatPeriod = 0.5;
        var beatAge = time % beatPeriod;
        var beat = beatAge < 0.06;
        var beatHit = beat ? 1 : Math.Exp(-beatAge * 6);
        var beats = Enumerable.Range(0, 8)
            .Select(index => new StudioBeat(beatAge + index * beatPeriod, index, beatHit))
            .ToArray();
        var low = bands.Take(6).Max();
        var mid = bands.Skip(6).Take(6).Max();
        var high = bands.Skip(12).Max();
        var kickAge = time % 1.0;
        return new StudioAudioFrame(
            bands.Average(),
            bands.Max(),
            bands,
            waveform,
            low,
            mid,
            high,
            beat,
            120,
            peaks,
            columns,
            columnPeaks,
            history,
            beatAge,
            beatHit,
            (int)(time / beatPeriod),
            beats,
            kickAge,
            Math.Max(0, 1 - kickAge),
            (int)time);
    }

    private static Func<double, double, StudioRgb> ScreenSampler(double time) =>
        (u, v) =>
        {
            var sweep = Math.Abs((u + time * 0.08) % 1 - 0.5);
            var hue = (u * 0.65 + v * 0.2 + time * 0.02) % 1;
            var color = StudioRgb.FromHsv(hue, 0.55, 0.8);
            return sweep < 0.04
                ? StudioRgb.Lerp(color, StudioRgb.White, 0.65)
                : color;
        };

    private static StudioSystemInput System(double time) => new(
        DateTimeOffset.Now,
        CpuLoad: 0.45 + 0.35 * Math.Sin(time * 0.7),
        GpuLoad: 0.5 + 0.3 * Math.Cos(time * 0.53),
        BatteryPercent: 0.8,
        ForegroundProcess: "OpenSynapse")
    {
        MemoryLoad = 0.55 + 0.2 * Math.Sin(time * 0.31),
    };

    private static float BandAt(float[] source, double position)
    {
        var index = Math.Clamp((int)Math.Round(position * (source.Length - 1)), 0, source.Length - 1);
        return source[index];
    }
}
