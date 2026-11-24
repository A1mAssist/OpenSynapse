namespace OpenSynapse.Core.Lighting;

/// <summary>Audio geometry primitives; music.bars is the shared bar baseline.</summary>
internal static class MusicRenderModules
{
    public static IReadOnlyList<IRenderModuleFactory> All { get; } = Array.AsReadOnly<IRenderModuleFactory>(
    [
        new MusicBarsModule(),
        new MusicSpectrumModule(), new MusicFieldModule(), new MusicRadialModule(),
        new MusicHeartModule(), new MusicTraceModule(), new MusicParticlesModule(),
        new MusicContourModule(), new MusicLatticeModule(), new MusicArrowsModule(),
        new MusicLightningModule(),
    ]);
}

internal readonly record struct MusicSettings(double Gain, double ColorFlow)
{
    public static MusicSettings Read(RenderModuleParams p) => new(
        p.Number("gain", 1, 0, 4), p.Number("colorFlow", 0, -2, 2));
}

internal readonly record struct MusicLight(double Amount, double Color);

internal abstract class MusicLayer(MusicSettings settings) : IRenderModule
{
    private sealed class Clock { public double Seconds; }

    public void Render(in RenderModuleContext context)
    {
        Array.Clear(context.Output);
        var audio = context.Audio;
        var activity = audio is null ? 0 : MusicMath.Activity(audio);
        if (audio is null || activity <= 0.00001 || settings.Gain == 0)
        {
            context.State.Clear();
            return;
        }

        var seconds = double.IsFinite(context.Seconds) ? context.Seconds : 0;
        var clock = context.State.Tag as Clock;
        if (clock is null || seconds < clock.Seconds)
        {
            context.State.Clear();
            clock = new Clock { Seconds = seconds - 1d / 30 };
            context.State.Tag = clock;
        }
        var dt = Math.Clamp(seconds - clock.Seconds, 0, 0.25);
        clock.Seconds = seconds;
        Prepare(context, audio, dt);
        for (var row = 0; row < context.Rows; row++)
        for (var column = 0; column < context.Columns; column++)
        {
            var light = Shade(context, audio, (column + 0.5) / context.Columns,
                (row + 0.5) / context.Rows, activity);
            var position = settings.ColorFlow == 0 ? MusicMath.Clamp(light.Color)
                : MusicMath.Frac(light.Color + seconds * settings.ColorFlow);
            context.Output[row * context.Columns + column] = StudioRgb.Lerp(StudioRgb.Black,
                context.Palette.Sample(position), MusicMath.Clamp(light.Amount * settings.Gain));
        }
    }

    protected virtual void Prepare(in RenderModuleContext c, StudioAudioFrame a, double dt) { }
    protected abstract MusicLight Shade(in RenderModuleContext c, StudioAudioFrame a,
        double x, double y, double activity);
}

internal static class MusicMath
{
    public const double Tau = Math.PI * 2;
    public static double Clamp(double v) => Math.Clamp(v, 0, 1);
    public static double Frac(double v) => v - Math.Floor(v);
    public static double Length(double x, double y) => Math.Sqrt(x * x + y * y);
    public static double Line(double distance, double width) => Math.Exp(-Math.Pow(distance / width, 2));
    public static double Pulse(StudioAudioFrame a, double decay = 5) =>
        a.BeatCount > 0 || a.Beat || a.BeatHit > 0 ? a.Pulse(decay) : 0;
    public static double Kick(StudioAudioFrame a) => a.KickCount > 0 || a.KickHit > 0 ? a.KickPulse() : 0;
    public static double Energy(StudioAudioFrame a) => Math.Max(a.Rms, Math.Max(a.Low, Math.Max(a.Mid, a.High)));

    public static double Activity(StudioAudioFrame a)
    {
        var value = Math.Max(Energy(a), Math.Max(Pulse(a), Kick(a)));
        foreach (var v in a.Columns) value = Math.Max(value, v);
        foreach (var v in a.Bands) value = Math.Max(value, v);
        foreach (var v in a.Waveform) value = Math.Max(value, v);
        foreach (var history in a.History)
        foreach (var v in history) value = Math.Max(value, v);
        for (var i = 0; i < a.Beats.Count; i++)
            if (a.Beats[i].Age is >= 0 and < 1.6)
                value = Math.Max(value, a.Beats[i].Hit * (1 - a.Beats[i].Age / 1.6));
        return Clamp(value);
    }

    public static double Sample(float[] values, double u)
    {
        if (values.Length == 0) return 0;
        var p = Clamp(u) * (values.Length - 1);
        var i = (int)p;
        return values[i] + (values[Math.Min(i + 1, values.Length - 1)] - values[i]) * (p - i);
    }

    public static double History(StudioAudioFrame a, double age, double band)
    {
        if (a.History.Length == 0) return 0;
        var p = Clamp(age) * (a.History.Length - 1);
        var i = (int)p;
        return Sample(a.History[i], band) * (1 - (p - i)) +
            Sample(a.History[Math.Min(i + 1, a.History.Length - 1)], band) * (p - i);
    }

    public static double Noise(int x, int y)
    {
        unchecked
        {
            var v = (uint)(x * 374761393 + y * 668265263);
            v = (v ^ (v >> 13)) * 1274126177;
            return ((v ^ (v >> 16)) & 0xFFFFFF) / 16777216d;
        }
    }

    public static string Choice(RenderModuleParams p, string name, string fallback, params string[] choices) =>
        p.Choice(name, fallback, choices);

    public static double Segment(double x, double y, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var t = Clamp(((x - ax) * dx + (y - ay) * dy) / Math.Max(0.000001, dx * dx + dy * dy));
        return Length(x - ax - t * dx, y - ay - t * dy);
    }
}
