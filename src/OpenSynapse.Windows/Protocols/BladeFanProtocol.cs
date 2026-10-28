using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Exact Blade 16 2025 (02C6) stored fan-target protocol.
/// SET construction is source-backed only; production writes stay gated by the
/// controller until process-exit and sleep recovery are owned by the caller.
/// </summary>
public static class BladeFanProtocol
{
    public const byte ZoneCpu = 0x01;
    public const byte ZoneGpu = 0x02;
    public const int MinimumRpm = BladeFanLimits.MinimumRpm;
    public const int MaximumRpm = BladeFanLimits.MaximumRpm;
    public const int StepRpm = BladeFanLimits.StepRpm;
    internal const int MinimumCurveRpm = 1900;

    private const byte TransactionId = 0x1F;
    private const byte DataSize = 0x03;
    private const byte CommandClass = 0x0D;
    private const byte SetTargetCommandId = 0x01;
    private const byte GetTargetCommandId = 0x81;

    public static byte[] CreateGetTargetRequest(byte zone) =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            DataSize,
            CommandClass,
            GetTargetCommandId,
            new byte[] { 0x00, ValidateZone(zone), 0x00 });

    public static byte[] CreateSetTargetRequest(byte zone, int rpm)
    {
        ValidateTargetRpm(rpm);
        return CreateSetTargetRequestCore(zone, rpm);
    }

    internal static byte[] CreateSetTargetRequest(byte zone, int rpm, int maximumRpm)
    {
        ValidateTargetRpm(rpm, maximumRpm);
        return CreateSetTargetRequestCore(zone, rpm);
    }

    internal static byte[] CreateSetCurveTargetRequest(byte zone, int rpm)
    {
        ValidateCurveTargetRpm(rpm);
        return CreateSetTargetRequestCore(zone, rpm);
    }

    internal static byte[] CreateSetCurveTargetRequest(byte zone, int rpm, int maximumRpm)
    {
        ValidateCurveTargetRpm(rpm, maximumRpm);
        return CreateSetTargetRequestCore(zone, rpm);
    }

    private static byte[] CreateSetTargetRequestCore(byte zone, int rpm)
    {
        return RazerFeatureReport.CreateRequest(
            TransactionId,
            DataSize,
            CommandClass,
            SetTargetCommandId,
            // shortcut: the middleware sends classId 1 here; this build drives fans with 0x00,
            // so changing it needs one hardware readback.
            new byte[] { 0x00, ValidateZone(zone), checked((byte)(rpm / StepRpm)) });
    }

    public static int ParseTarget(ReadOnlySpan<byte> response, byte expectedZone) =>
        ParseTarget(response, expectedZone, MaximumRpm);

    public static int ParseTarget(
        ReadOnlySpan<byte> response,
        byte expectedZone,
        int maximumRpm) =>
        ParseTargetCore(response, expectedZone, CreateGetTargetRequest(expectedZone), MinimumRpm, maximumRpm);

    internal static int ParseTarget(
        ReadOnlySpan<byte> response,
        byte expectedZone,
        ReadOnlySpan<byte> request,
        int maximumRpm = MaximumRpm) =>
        ParseTargetCore(response, expectedZone, request, MinimumRpm, maximumRpm);

    internal static int ParseCurveTarget(
        ReadOnlySpan<byte> response,
        byte expectedZone,
        ReadOnlySpan<byte> request,
        int maximumRpm = MaximumRpm) =>
        ParseTargetCore(response, expectedZone, request, MinimumCurveRpm, maximumRpm);

    private static int ParseTargetCore(
        ReadOnlySpan<byte> response,
        byte expectedZone,
        ReadOnlySpan<byte> request,
        int minimumRpm,
        int maximumRpm)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, DataSize))
        {
            throw new InvalidOperationException("Blade fan target returned an invalid or out-of-order feature report.");
        }

        if (response[RazerFeatureReport.ArgumentsOffset + 1] != expectedZone)
        {
            throw new InvalidOperationException(
                $"Blade returned an incorrect fan zone: 0x{response[RazerFeatureReport.ArgumentsOffset + 1]:X2}; expected 0x{expectedZone:X2}.");
        }

        var rpm = response[RazerFeatureReport.ArgumentsOffset + 2] * StepRpm;
        if (rpm < minimumRpm || rpm > maximumRpm || rpm % StepRpm != 0)
        {
            var kind = minimumRpm == MinimumRpm ? "fixed fan speed" : "fan-curve speed";
            throw new InvalidOperationException(
                $"Blade returned an unsupported {kind} of {rpm} RPM; allowed range is {minimumRpm}..{maximumRpm} RPM in {StepRpm} RPM steps.");
        }
        return rpm;
    }

    private static byte ValidateZone(byte zone) => zone switch
    {
        ZoneCpu or ZoneGpu => zone,
        _ => throw new ArgumentOutOfRangeException(nameof(zone), "Blade fan zone must be CPU (0x01) or GPU (0x02)."),
    };

    public static void ValidateTargetRpm(int rpm)
        => BladeFanLimits.ValidateTargetRpm(rpm);

    internal static void ValidateTargetRpm(int rpm, int maximumRpm)
        => BladeFanLimits.ValidateTargetRpm(rpm, maximumRpm);

    internal static void ValidateCurveTargetRpm(int rpm)
    {
        ValidateCurveTargetRpm(rpm, MaximumRpm);
    }

    internal static void ValidateCurveTargetRpm(int rpm, int maximumRpm)
    {
        if (rpm is < MinimumCurveRpm || rpm > maximumRpm || rpm % StepRpm != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rpm),
                $"Blade curve target must be {MinimumCurveRpm}..{maximumRpm} RPM in {StepRpm} RPM steps.");
        }
    }
}
