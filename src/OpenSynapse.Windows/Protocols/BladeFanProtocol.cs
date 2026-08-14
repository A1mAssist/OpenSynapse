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
    public const int MinimumRpm = 2000;
    public const int MaximumRpm = 5000;
    public const int StepRpm = 100;

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
        return RazerFeatureReport.CreateRequest(
            TransactionId,
            DataSize,
            CommandClass,
            SetTargetCommandId,
            new byte[] { 0x00, ValidateZone(zone), checked((byte)(rpm / StepRpm)) });
    }

    public static int ParseTarget(ReadOnlySpan<byte> response, byte expectedZone) =>
        ParseTarget(response, expectedZone, CreateGetTargetRequest(expectedZone));

    internal static int ParseTarget(
        ReadOnlySpan<byte> response,
        byte expectedZone,
        ReadOnlySpan<byte> request)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, DataSize))
        {
            throw new InvalidOperationException("Blade 风扇目标返回了无效或错序的 feature report。");
        }

        if (response[RazerFeatureReport.ArgumentsOffset + 1] != expectedZone)
        {
            throw new InvalidOperationException(
                $"Blade 返回了错误的风扇分区 0x{response[RazerFeatureReport.ArgumentsOffset + 1]:X2}，期望 0x{expectedZone:X2}。");
        }

        var rpm = response[RazerFeatureReport.ArgumentsOffset + 2] * StepRpm;
        if (rpm is < MinimumRpm or > MaximumRpm || (rpm - MinimumRpm) % StepRpm != 0)
        {
            throw new InvalidOperationException(
                $"Blade 返回了不支持的固定风扇转速 {rpm} RPM；允许范围为 {MinimumRpm}..{MaximumRpm} RPM，步进 {StepRpm} RPM。");
        }
        return rpm;
    }

    private static byte ValidateZone(byte zone) => zone switch
    {
        ZoneCpu or ZoneGpu => zone,
        _ => throw new ArgumentOutOfRangeException(nameof(zone), "Blade 风扇分区必须为 CPU(0x01) 或 GPU(0x02)。"),
    };

    public static void ValidateTargetRpm(int rpm)
    {
        if (rpm is < MinimumRpm or > MaximumRpm || (rpm - MinimumRpm) % StepRpm != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rpm),
                $"Blade 固定风扇转速必须为 {MinimumRpm}..{MaximumRpm} RPM，步进 {StepRpm} RPM。");
        }
    }
}
