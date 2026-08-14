using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.ProtocolProbe;

public static class ProbeCatalog
{
    private static readonly ProbeCommand[] Commands =
    {
        new(0x02C6, "blade.keyboard-brightness", ProbeEvidenceLevel.Verified,
            0xFF, 0x02, 0x0E, 0x84, new byte[] { 0x01, 0x00 }, 1),

        new(0x02C6, "blade.thermal-zone-1", ProbeEvidenceLevel.Verified,
            0x1F, 0x04, 0x0D, 0x82, new byte[] { 0x00, 0x01, 0x00, 0x00 }, 2),
        new(0x02C6, "blade.thermal-zone-2", ProbeEvidenceLevel.Verified,
            0x1F, 0x04, 0x0D, 0x82, new byte[] { 0x00, 0x02, 0x00, 0x00 }, 2),
        new(0x02C6, "blade.fan-target-zone-1", ProbeEvidenceLevel.SourceBacked,
            0x1F, 0x03, 0x0D, 0x81, new byte[] { 0x00, 0x01, 0x00 }, 2),
        new(0x02C6, "blade.fan-target-zone-2", ProbeEvidenceLevel.SourceBacked,
            0x1F, 0x03, 0x0D, 0x81, new byte[] { 0x00, 0x02, 0x00 }, 2),
        FromRequest(0x02C6, "blade.fan-id-list", ProbeEvidenceLevel.SourceBacked,
            BladeThermalProtocol.CreateGetFanIdListRequest(), 2, 0),
        FromRequest(0x02C6, "blade.current-fan-speed-cpu", ProbeEvidenceLevel.SourceBacked,
            BladeThermalProtocol.CreateGetCurrentSpeedRequest(BladeThermalProtocol.CpuFanId), 2, 2),
        FromRequest(0x02C6, "blade.current-fan-speed-gpu", ProbeEvidenceLevel.SourceBacked,
            BladeThermalProtocol.CreateGetCurrentSpeedRequest(BladeThermalProtocol.GpuFanId), 2, 2),
        FromRequest(0x02C6, "blade.advanced-fan-cpu", ProbeEvidenceLevel.SourceBacked,
            BladeThermalProtocol.CreateGetAdvancedFanModeRequest(BladeThermalProtocol.CpuFanId), 2, 2),
        FromRequest(0x02C6, "blade.advanced-fan-gpu", ProbeEvidenceLevel.SourceBacked,
            BladeThermalProtocol.CreateGetAdvancedFanModeRequest(BladeThermalProtocol.GpuFanId), 2, 2),
        new(0x02C6, "blade.charge-limit", ProbeEvidenceLevel.Verified,
            0x1F, 0x01, 0x07, 0x92, new byte[] { 0x00 }, 2, true),
        new(0x02C6, "blade.cpu-boost", ProbeEvidenceLevel.Verified,
            0x1F, 0x03, 0x0D, 0x87, new byte[] { 0x00, 0x01, 0x00 }, 2),
        new(0x02C6, "blade.gpu-boost", ProbeEvidenceLevel.Verified,
            0x1F, 0x03, 0x0D, 0x87, new byte[] { 0x00, 0x02, 0x00 }, 2),
        new(0x02C6, "blade.max-fan-speed-mode", ProbeEvidenceLevel.SourceBacked,
            0x1F, 0x01, 0x07, 0x8F, new byte[] { 0x00 }, 2, true),
        new(0x02C6, "blade.logo-power", ProbeEvidenceLevel.SourceBacked,
            0xFF, 0x03, 0x03, 0x80, new byte[] { 0x01, 0x04, 0x00 }, 2),
        new(0x02C6, "blade.logo-mode", ProbeEvidenceLevel.SourceBacked,
            0xFF, 0x03, 0x03, 0x82, new byte[] { 0x01, 0x04, 0x00 }, 2),
        FromRequest(0x02C6, "blade.battery-level", ProbeEvidenceLevel.SourceBacked,
            BladeProduct710Protocol.CreateGetBatteryLevelRequest(), 2, 1),
        FromRequest(0x02C6, "blade.charging-status", ProbeEvidenceLevel.SourceBacked,
            BladeProduct710Protocol.CreateGetChargingStatusRequest(), 2, 1),
        FromRequest(0x02C6, "blade.auto-sleep", ProbeEvidenceLevel.SourceBacked,
            BladeProduct710Protocol.CreateGetAutoSleepRequest(), 2, 1),
        FromRequest(0x02C6, "blade.time-to-sleep", ProbeEvidenceLevel.SourceBacked,
            BladeProduct710Protocol.CreateGetTimeToSleepRequest(), 2, 0),

        new(0x00B8, "viper.battery", ProbeEvidenceLevel.Verified,
            0x1F, 0x02, 0x07, 0x80, Array.Empty<byte>(), 60),
        new(0x00B8, "viper.polling-rate", ProbeEvidenceLevel.Verified,
            0x1F, 0x01, 0x00, 0x85, Array.Empty<byte>(), 60),
        new(0x00B8, "viper.current-dpi", ProbeEvidenceLevel.Verified,
            0x1F, 0x07, 0x04, 0x85, new byte[] { 0x00 }, 60),
        new(0x00B8, "viper.idle-timeout", ProbeEvidenceLevel.Verified,
            0x1F, 0x02, 0x07, 0x83, Array.Empty<byte>(), 60),
        new(0x00B8, "viper.low-battery-threshold", ProbeEvidenceLevel.SourceBacked,
            0x1F, 0x01, 0x07, 0x81, Array.Empty<byte>(), 60),
        new(0x00B8, "viper.dpi-stages", ProbeEvidenceLevel.SourceBacked,
            0x1F, 0x26, 0x04, 0x86, new byte[] { 0x01 }, 60),
    };

    public static IReadOnlyList<ProbeCommand> Get(bool includeSourceBacked) =>
        Commands
            .Where(command => includeSourceBacked || command.Evidence == ProbeEvidenceLevel.Verified)
            .ToArray();

    private static ProbeCommand FromRequest(
        ushort productId,
        string name,
        ProbeEvidenceLevel evidence,
        byte[] request,
        int waitMilliseconds,
        int argumentLength) =>
        new(productId, name, evidence, request[2], request[6], request[7], request[8],
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, argumentLength), waitMilliseconds);
}
