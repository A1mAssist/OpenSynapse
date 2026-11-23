namespace OpenSynapse.Core.Lighting;

public static partial class StudioEffectRenderer
{
    /// <summary>
    /// Native music visuals. Every style reads the same live spectrum frame, so a
    /// beat or a bass hit changes the picture on the next rendered frame.
    /// </summary>
    private static StudioRgb[] RenderMusicStyle(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time,
        StudioAudioFrame audio)
    {
        var frame = new StudioRgb[rows * columns];
        var style = parameters.AudioStyle;
        for (var row = 0; row < rows; row++)
        {
            var ny = (row + 0.5) / rows;
            var height = 1 - ny;
            for (var column = 0; column < columns; column++)
            {
                var nx = (column + 0.5) / columns;
                frame[row * columns + column] = style switch
                {
                    StudioAudioStyle.Bars => MusicBars(parameters, audio, nx, height, rows),
                    StudioAudioStyle.BeatFlash => MusicBeatFlash(parameters, audio, nx, height),
                    StudioAudioStyle.Equalizer => MusicEqualizer(audio, nx, height, rows),
                    StudioAudioStyle.Mirror => MusicMirror(parameters, audio, nx, height, time),
                    StudioAudioStyle.Waterfall => MusicWaterfall(audio, nx, ny),
                    StudioAudioStyle.Ring => MusicRing(parameters, audio, nx, height, time),
                    StudioAudioStyle.Core => MusicCore(parameters, audio, nx, height, time),
                    StudioAudioStyle.Wave => MusicWave(parameters, audio, nx, height, time),
                    StudioAudioStyle.Fire => MusicFire(audio, nx, height),
                    StudioAudioStyle.Stardust => MusicStardust(audio, nx, height, time),
                    StudioAudioStyle.Volume => MusicVolume(parameters, audio, nx, time),
                    StudioAudioStyle.BeatRipple => MusicBeatRipple(parameters, audio, nx, height),
                    _ => StudioRgb.Black,
                };
            }
        }

        return frame;
    }

    private static StudioRgb MusicBars(
        StudioEffectParams parameters,
        StudioAudioFrame audio,
        double nx,
        double height,
        int rows)
    {
        var value = audio.Column(nx);
        var peak = audio.ColumnPeak(nx);
        if (peak > 0.1 && Math.Abs(height - peak) < 0.5 / rows)
        {
            return StudioRgb.Lerp(parameters.Resolve(nx), StudioRgb.White, 0.45);
        }

        return height < value
            ? Scale(parameters.Resolve(nx), 0.55 + 0.45 * value)
            : StudioRgb.Black;
    }

    private static StudioRgb MusicBeatFlash(
        StudioEffectParams parameters,
        StudioAudioFrame audio,
        double nx,
        double height)
    {
        var pulse = audio.Pulse(2.6);
        var distance = Hypot((nx - 0.5) * 2.2, height - 0.5);
        var amount = Math.Min(
            1,
            0.25 * audio.Bass + 0.85 * pulse * Math.Max(0, 1 - distance * (1 - pulse) * 0.9));
        return Scale(parameters.Resolve(Frac(audio.BeatCount * 0.13)), amount);
    }

    private static StudioRgb MusicEqualizer(
        StudioAudioFrame audio,
        double nx,
        double height,
        int rows)
    {
        var peak = audio.ColumnPeak(nx);
        if (peak > 0.1 && Math.Abs(height - peak) < 0.5 / rows)
        {
            return new StudioRgb(230, 230, 230);
        }

        return height < audio.Column(nx)
            ? StudioRgb.FromHsv(0.33 * (1 - height), 1, 1)
            : StudioRgb.Black;
    }

    private static StudioRgb MusicMirror(
        StudioEffectParams parameters,
        StudioAudioFrame audio,
        double nx,
        double height,
        double time)
    {
        var mirrored = Math.Abs(nx - 0.5) * 2;
        var value = audio.Column(mirrored);
        return height < value
            ? Scale(parameters.Resolve(Frac(mirrored * 0.75 + time * 0.03)), 0.55 + 0.45 * value)
            : StudioRgb.Black;
    }

    private static StudioRgb MusicWaterfall(StudioAudioFrame audio, double nx, double ny)
    {
        var depth = Math.Clamp((int)(ny * StudioAudioFrame.HistoryDepth), 0, StudioAudioFrame.HistoryDepth - 1);
        var history = audio.History[StudioAudioFrame.HistoryDepth - 1 - depth];
        return HeatColor(BandAt(history, nx) * 0.9);
    }

    private static StudioRgb MusicRing(
        StudioEffectParams parameters,
        StudioAudioFrame audio,
        double nx,
        double height,
        double time)
    {
        var distance = Hypot((nx - 0.45) * 2.2, height - 0.5) / 1.35;
        var value = Math.Pow(audio.Column(distance), 1.3);
        return Scale(parameters.Resolve(Frac(distance * 0.8 - time * 0.05)), value);
    }

    private static StudioRgb MusicCore(
        StudioEffectParams parameters,
        StudioAudioFrame audio,
        double nx,
        double height,
        double time)
    {
        var distance = Hypot((nx - 0.45) * 2.6, height - 0.5);
        var radius = 0.12 + 1.1 * audio.Bass + 0.2 * audio.Pulse(6);
        var body = Clamp01((radius - distance) * 3);
        var halo = Math.Exp(-Math.Pow((distance - radius) / 0.08, 2)) * 0.35;
        var color = Scale(parameters.Resolve(Frac(0.92 - distance * 0.35 + time * 0.02)), body);
        return StudioRgb.Lerp(color, new StudioRgb(255, 191, 255), halo);
    }

    private static StudioRgb MusicWave(
        StudioEffectParams parameters,
        StudioAudioFrame audio,
        double nx,
        double height,
        double time)
    {
        var amplitude = 0.06 + 0.22 * audio.Rms + 0.3 * audio.Column(nx) + 0.2 * audio.Pulse();
        var phase = nx * Math.PI * 2 * 1.4 - time * 3.5;
        var first = 0.5 + amplitude * Math.Sin(phase);
        var second = 0.5 - amplitude * 0.7 * Math.Sin(phase + 0.9);
        var sigma = 0.12 + 0.18 * audio.Bass;
        var value = Math.Max(
            Math.Exp(-Math.Pow((height - first) / sigma, 2)),
            0.45 * Math.Exp(-Math.Pow((height - second) / sigma, 2)));
        return Scale(parameters.Resolve(Frac(nx * 0.7 - time * 0.08)), value);
    }

    private static StudioRgb MusicFire(StudioAudioFrame audio, double nx, double height)
    {
        // height is 1 at the top row and 0 at the bottom, so flames rise from the bottom.
        var value = audio.Column(nx) * 1.15 - height * 0.95;
        return FireColor(Clamp01(value * 3) * (1 - height * 0.4));
    }

    private static StudioRgb MusicStardust(
        StudioAudioFrame audio,
        double nx,
        double height,
        double time)
    {
        var noise = Noise((long)(nx * 997), (long)(height * 991), 0);
        var sparkle = 0.0;
        if (noise < 0.35)
        {
            sparkle = Math.Pow(Math.Max(0, Math.Sin(time * (1 + noise * 3) + noise * 50)), 8) *
                (0.3 + 0.9 * audio.Treble);
            if (noise < 0.12) sparkle += audio.Pulse(4) * 0.8;
        }

        return Scale(new StudioRgb(217, 230, 255), Math.Min(1, sparkle));
    }

    private static StudioRgb MusicVolume(
        StudioEffectParams parameters,
        StudioAudioFrame audio,
        double nx,
        double time) =>
        Scale(parameters.Resolve(Frac(nx * 0.6 + time * 0.04)), Math.Pow(audio.Rms, 0.9));

    private static StudioRgb MusicBeatRipple(
        StudioEffectParams parameters,
        StudioAudioFrame audio,
        double nx,
        double height)
    {
        var result = StudioRgb.Black;
        foreach (var beat in audio.Beats)
        {
            if (beat.Age >= 1.5) continue;
            var centerX = 0.05 + Noise(beat.Index, 1, 0) * 0.9;
            var centerY = 0.1 + Noise(beat.Index, 2, 0) * 0.8;
            var distance = Hypot((nx - centerX) * 3.2, height - centerY);
            var value = Math.Exp(-Math.Pow((distance - beat.Age * 2.6) / 0.2, 2)) *
                (1 - beat.Age / 1.5) *
                (0.5 + 0.5 * beat.Hit);
            var color = Scale(parameters.Resolve(Frac(beat.Index * 0.21)), value);
            result = new StudioRgb(
                Math.Max(result.R, color.R),
                Math.Max(result.G, color.G),
                Math.Max(result.B, color.B));
        }

        return result;
    }

    private static double BandAt(float[] source, double u)
    {
        var position = Math.Clamp(u, 0, 1) * (StudioAudioFrame.BandCount - 1);
        var lower = (int)position;
        if (lower >= StudioAudioFrame.BandCount - 1) return source[StudioAudioFrame.BandCount - 1];
        var weight = position - lower;
        return source[lower] * (1 - weight) + source[lower + 1] * weight;
    }

    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static double Frac(double value) =>
        double.IsFinite(value) ? value - Math.Floor(value) : 0;

    private static double Noise(long x, long y, long salt)
    {
        unchecked
        {
            var value = (ulong)(x * 374761393 + y * 668265263 + salt * 2246822519);
            value = (value ^ (value >> 13)) * 1274126177;
            value ^= value >> 16;
            return (value & 0xFFFFFF) / (double)0x1000000;
        }
    }

    private static readonly StudioRgb[] HeatStops =
    [
        new(0, 0, 0),
        new(90, 0, 178),
        new(255, 20, 64),
        new(255, 140, 0),
        new(255, 242, 128),
        new(255, 255, 255),
    ];

    private static readonly StudioRgb[] FireStops =
    [
        new(0, 0, 0),
        new(191, 10, 0),
        new(255, 89, 0),
        new(255, 191, 20),
        new(255, 255, 140),
    ];

    private static StudioRgb HeatColor(double value) => Ramp(HeatStops, value);

    private static StudioRgb FireColor(double value) => Ramp(FireStops, value);

    private static StudioRgb Ramp(StudioRgb[] stops, double value)
    {
        var scaled = Clamp01(value) * (stops.Length - 1);
        var index = Math.Min(stops.Length - 2, (int)scaled);
        return StudioRgb.Lerp(stops[index], stops[index + 1], scaled - index);
    }
}
