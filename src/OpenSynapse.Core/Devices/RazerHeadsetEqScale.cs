namespace OpenSynapse.Core.Devices;

/// <summary>
/// Maps the MXIC headset equalizer between the UI decibel range and the firmware's
/// 0-10 steps. The Valkyrie/Billie channels use sign-magnitude bytes instead.
/// </summary>
public static class RazerHeadsetEqScale
{
    public const double MinimumDb = -12;
    public const double MaximumDb = 12;
    public const byte MinimumStep = 0;
    public const byte MaximumStep = 10;

    /// <summary>10 steps cover 24 dB, so one firmware step is 2.4 dB.</summary>
    public const double StepDb = (MaximumDb - MinimumDb) / MaximumStep;

    public static byte ToStep(double decibels)
    {
        var value = double.IsFinite(decibels) ? Math.Clamp(decibels, MinimumDb, MaximumDb) : 0;
        return (byte)Math.Round((value - MinimumDb) / StepDb);
    }

    public static double ToDecibels(byte step) =>
        Math.Min(step, MaximumStep) * StepDb + MinimumDb;

    public static double Snap(double decibels) => ToDecibels(ToStep(decibels));
}