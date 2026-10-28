using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Exact Blade fan-target protocol. The 2026 generation passes class 1; the older
/// 710/671/02C6 reports keep the shared default class 0.
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
    internal const byte DefaultClassId = 0x00;

    private const byte TransactionId = 0x1F;
    private const byte DataSize = 0x03;
    private const byte CommandClass = 0x0D;
    private const byte SetTargetCommandId = 0x01;
    private const byte GetTargetCommandId = 0x81;

    public static byte[] CreateGetTargetRequest(byte zone) =>
        CreateGetTargetRequest(zone, DefaultClassId);

    internal static byte[] CreateGetTargetRequest(byte zone, byte classId) =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            DataSize,
            CommandClass,
            GetTargetCommandId,
            new byte[] { classId, ValidateZone(zone), 0x00 });

    public static byte[] CreateSetTargetRequest(byte zone, int rpm)
    {
        ValidateTargetRpm(rpm);
        return CreateSetTargetRequestCore(zone, rpm, DefaultClassId);
    }

    internal static byte[] CreateSetTargetRequest(byte zone, int rpm, int maximumRpm)
    {
        ValidateTargetRpm(rpm, maximumRpm);
        return CreateSetTargetRequestCore(zone, rpm, DefaultClassId);
    }

    internal static byte[] CreateSetTargetRequest(byte zone, int rpm, byte classId)
    {
        ValidateTargetRpm(rpm);
        return CreateSetTargetRequestCore(zone, rpm, classId);
    }

    internal static byte[] CreateSetTargetRequest(byte zone, int rpm, int maximumRpm, byte classId)
    {
        ValidateTargetRpm(rpm, maximumRpm);
        return CreateSetTargetRequestCore(zone, rpm, classId);
    }

    internal static byte[] CreateSetCurveTargetRequest(byte zone, int rpm)
    {
        ValidateCurveTargetRpm(rpm);
        return CreateSetTargetRequestCore(zone, rpm, DefaultClassId);
    }

    internal static byte[] CreateSetCurveTargetRequest(byte zone, int rpm, int maximumRpm)
    {
        ValidateCurveTargetRpm(rpm, maximumRpm);
        return CreateSetTargetRequestCore(zone, rpm, DefaultClassId);
    }

    internal static byte[] CreateSetCurveTargetRequest(byte zone, int rpm, int maximumRpm, byte classId)
    {
        ValidateCurveTargetRpm(rpm, maximumRpm);
        return CreateSetTargetRequestCore(zone, rpm, classId);
    }

    private static byte[] CreateSetTargetRequestCore(byte zone, int rpm, byte classId)
    {
        return RazerFeatureReport.CreateRequest(
            TransactionId,
            DataSize,
            CommandClass,
            SetTargetCommandId,
            new byte[] { classId, ValidateZone(zone), checked((byte)(rpm / StepRpm)) });
    }

    public static int ParseTarget(ReadOnlySpan<byte> response, byte expectedZone) =>
        ParseTarget(response, expectedZone, MaximumRpm);

    public static int ParseTarget(
        ReadOnlySpan<byte> response,
        byte expectedZone,
        int maximumRpm) =>
        ParseTargetCore(
            response,
            expectedZone,
            CreateGetTargetRequest(expectedZone),
            MinimumRpm,
            maximumRpm,
            DefaultClassId);

    internal static int ParseTarget(
        ReadOnlySpan<byte> response,
        byte expectedZone,
        ReadOnlySpan<byte> request,
        int maximumRpm = MaximumRpm) =>
        ParseTargetCore(response, expectedZone, request, MinimumRpm, maximumRpm, DefaultClassId);

    internal static int ParseTarget(
        ReadOnlySpan<byte> response,
        byte expectedZone,
        ReadOnlySpan<byte> request,
        int maximumRpm,
        byte expectedClassId) =>
        ParseTargetCore(response, expectedZone, request, MinimumRpm, maximumRpm, expectedClassId);

    internal static int ParseCurveTarget(
        ReadOnlySpan<byte> response,
        byte expectedZone,
        ReadOnlySpan<byte> request,
        int maximumRpm = MaximumRpm) =>
        ParseTargetCore(response, expectedZone, request, MinimumCurveRpm, maximumRpm, DefaultClassId);

    internal static int ParseCurveTarget(
        ReadOnlySpan<byte> response,
        byte expectedZone,
        ReadOnlySpan<byte> request,
        int maximumRpm,
        byte expectedClassId) =>
        ParseTargetCore(response, expectedZone, request, MinimumCurveRpm, maximumRpm, expectedClassId);

    private static int ParseTargetCore(
        ReadOnlySpan<byte> response,
        byte expectedZone,
        ReadOnlySpan<byte> request,
        int minimumRpm,
        int maximumRpm,
        byte expectedClassId)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(
                request, response, DataSize, allowRemainingPacketsMismatch: true))
        {
            throw new InvalidOperationException("Blade fan target returned an invalid or out-of-order feature report.");
        }

        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[0] != expectedClassId || arguments[1] != expectedZone)
        {
            throw new InvalidOperationException(
                $"Blade returned an incorrect fan object: 0x{arguments[0]:X2}/0x{arguments[1]:X2}; " +
                $"expected 0x{expectedClassId:X2}/0x{expectedZone:X2}.");
        }

        var rpm = arguments[2] * StepRpm;
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
