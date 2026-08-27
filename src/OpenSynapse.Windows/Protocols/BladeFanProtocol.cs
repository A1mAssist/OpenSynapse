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

    internal static byte[] CreateSetCurveTargetRequest(byte zone, int rpm)
    {
        ValidateCurveTargetRpm(rpm);
        return CreateSetTargetRequestCore(zone, rpm);
    }

    private static byte[] CreateSetTargetRequestCore(byte zone, int rpm)
    {
        return RazerFeatureReport.CreateRequest(
            TransactionId,
            DataSize,
            CommandClass,
            SetTargetCommandId,
            new byte[] { 0x00, ValidateZone(zone), checked((byte)(rpm / StepRpm)) });
    }

    public static int ParseTarget(ReadOnlySpan<byte> response, byte expectedZone) =>
        ParseTargetCore(response, expectedZone, CreateGetTargetRequest(expectedZone), MinimumRpm);

    internal static int ParseTarget(
        ReadOnlySpan<byte> response,
        byte expectedZone,
        ReadOnlySpan<byte> request) =>
        ParseTargetCore(response, expectedZone, request, MinimumRpm);

    internal static int ParseCurveTarget(
        ReadOnlySpan<byte> response,
        byte expectedZone,
        ReadOnlySpan<byte> request) =>
        ParseTargetCore(response, expectedZone, request, MinimumCurveRpm);

    private static int ParseTargetCore(
        ReadOnlySpan<byte> response,
        byte expectedZone,
        ReadOnlySpan<byte> request,
        int minimumRpm)
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
        if (rpm < minimumRpm || rpm > MaximumRpm || rpm % StepRpm != 0)
        {
            var kind = minimumRpm == MinimumRpm ? "fixed fan speed" : "fan-curve speed";
            throw new InvalidOperationException(
                $"Blade returned an unsupported {kind} of {rpm} RPM; allowed range is {minimumRpm}..{MaximumRpm} RPM in {StepRpm} RPM steps.");
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

    internal static void ValidateCurveTargetRpm(int rpm)
    {
        if (rpm is < MinimumCurveRpm or > MaximumRpm || rpm % StepRpm != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rpm),
                $"Blade curve target must be {MinimumCurveRpm}..{MaximumRpm} RPM in {StepRpm} RPM steps.");
        }
    }
}
