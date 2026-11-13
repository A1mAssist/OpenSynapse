using System.Collections.Frozen;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Razer's MXIC audio channel, used by the 2023 BlackShark V2 Pro (1532:0555)
/// and the Barracuda Pro identities (0539/053B, report id 1 / short envelope).
/// Requests are 64-byte output reports with a 50 41 sync word and a little-endian
/// payload length. Responses use 50 49 sync words and may carry several sub-responses.
/// </summary>
public static class RazerMxicHeadsetProtocol
{
    public const ushort ProductId = 0x0555;
    public const byte ReportId = 2;
    /// <summary>
    /// Mabel T1 (0539/053B) drops the two zero bytes before the 50 41 sync word and
    /// sends report id 1, so its command starts at 8 instead of 10. Its response
    /// scanner already tolerates the extra length byte, so only the request changes.
    /// </summary>
    public const byte MabelT1ReportId = 1;
    public const int ReportLength = 64;
    public const byte StatusOk = 0x4F;
    public const byte RazerCommand = 8;
    public const byte RemoteCommand = 14;
    public const int TenBandCount = 10;

    public static readonly byte[] GetFirmware = [0x02, 0x00, 0x00];
    public static readonly byte[] FirmwareHeader = [0x02, 0x01, 0x18];

    public static readonly byte[] GetBattery = [0x21, 0x00, 0x00];
    public static readonly byte[] BatteryHeader = [0x21, 0x01, 0x01];

    public static readonly byte[] GetWirelessConnectionStatus = [0x20, 0x00, 0x00];
    public static readonly byte[] WirelessConnectionStatusHeader = [0x20, 0x01, 0x01];

    public static readonly byte[] GetCharging = [0x2A, 0x00, 0x00];
    public static readonly byte[] ChargingHeader = [0x2A, 0x01, 0x01];

    public static readonly byte[] GetEqPreset = [0x13, 0x00, 0x00];
    public static readonly byte[] EqPresetHeader = [0x13, 0x01, 0x01];
    public static readonly byte[] SetEqPresetCommand = [0x93, 0x00, 0x01];
    public static readonly byte[] SetEqPresetHeader = [0x93, 0x01, 0x01];

    public static readonly byte[] GetEqBands = [0x15, 0x00, 0x00];
    public static readonly byte[] EqBandsHeader = [0x15, 0x01, 0x0A];
    public static readonly byte[] SetEqBandsCommand = [0x95, 0x00, 0x0A];
    public static readonly byte[] SetEqBandsHeader = [0x95, 0x01, 0x01];

    public static readonly byte[] GetMicMute = [0x55, 0x00, 0x00];
    public static readonly byte[] MicMuteHeader = [0x55, 0x01, 0x01];
    public static readonly byte[] SetMicMuteCommand = [0xE9, 0x00, 0x01];
    public static readonly byte[] SetMicMuteHeader = [0xE9, 0x01, 0x01];

    // AudioMxicDevice: getSidetone [18,0,0], setSidetone [98,0,1].
    public static readonly byte[] GetSidetone = [0x18, 0x00, 0x00];
    public static readonly byte[] SidetoneHeader = [0x18, 0x01, 0x01];
    public static readonly byte[] SetSidetoneCommand = [0x98, 0x00, 0x01];
    public static readonly byte[] SetSidetoneHeader = [0x98, 0x01, 0x01];

    // getANCStatusAndLevel and setANCStatusAndLevel use a two-byte state/level payload.
    public static readonly byte[] GetAnc = [0x12, 0x00, 0x00];
    public static readonly byte[] AncHeader = [0x12, 0x01, 0x02];
    public static readonly byte[] SetAncCommand = [0x92, 0x00, 0x02];
    public static readonly byte[] SetAncHeader = [0x92, 0x01, 0x02];

    public static readonly byte[] GetAudioDnd = [0x27, 0x00, 0x00];
    public static readonly byte[] AudioDndHeader = [0x27, 0x01, 0x01];
    public static readonly byte[] SetAudioDndCommand = [0xA7, 0x00, 0x01];
    public static readonly byte[] SetAudioDndHeader = [0xA7, 0x01, 0x01];

    /// <summary>
    /// Products whose audio channel uses this 64-byte MXIC envelope. The
    /// Barracuda Pro/2.4 identities (0539, 053B, 0583, 0584) select AudioMxicDevice,
    /// whose legacy triplets are the ones below. The
    /// BlackShark V3 Xbox/PlayStation identities select AudioValkyrie, which uses a
    /// different 64-byte frame and is handled by <c>RazerValkyrieHeadsetService</c>.
    /// The Hammerhead V3 and BlackShark V3 X families use their own 0x18 dialects
    /// but share this envelope, so their IDs are still listed for transport dispatch.
    /// </summary>
    private static readonly FrozenSet<ushort> ProductIds = new ushort[]
    {
        ProductId,
        0x0539,
        0x053A,
        0x053B,
        0x0583,
        0x0584,
        0x0A61,
        0x0A64,
        0x0A68,
        0x0A6C,
        0x0A81,
        0x0A54,
        0x0A5B,
        0x057C,
        0x057D,
        0x05B6,
        0x1021,
        0x1022,
        0x05AB,
        0x05AD,
        0x059F,
        0x05B9,
        0x05B3,
    }.ToFrozenSet();

    public static bool IsSupported(ushort productId) => ProductIds.Contains(productId);

    // 0584 (Barracuda 2.4) derives its own power state from getWirelessConnectionStatus,
    // so the reply is authoritative there. 0583 gets the same command from the
    // AudioMxicDevice base but hardcodes isPowerOn true, so it stays out. The MabelT1
    // (0539/053A/053B) and Class18/Protocol40 dialects use a different report envelope
    // and stay out until their reply is measured.
    private static readonly FrozenSet<ushort> WirelessConnectionStatusProductIds =
        new ushort[] { ProductId, 0x0584 }.ToFrozenSet();

    public static bool SupportsWirelessConnectionStatus(ushort productId) =>
        WirelessConnectionStatusProductIds.Contains(productId);

    // AudioMxicDevice proves sidetone in its bundle (get 18 00 00 -> header 18 01 01,
    // set 98 00 01 + state -> header 98 01 01) for the six Mxic64 identities, and
    // rzDevice25ValkyrieT3 exposes the same feature on class 0x18 source 3.
    // ANC is still only measured on 0555, so it stays out of every other identity.
    private static readonly FrozenSet<ushort> SidetoneProductIds = new ushort[]
    {
        0x0539,
        0x053A,
        0x053B,
        ProductId,
        0x0583,
        0x0584,
        0x057C,
        0x057D,
        0x05B6,
        0x05B3,
        0x0A54,
        0x0A5B,
        0x1021,
        0x1022,
    }.ToFrozenSet();

    private static readonly FrozenSet<ushort> AncProductIds = new ushort[] { ProductId }.ToFrozenSet();

    public static bool SupportsSidetone(ushort productId) => SidetoneProductIds.Contains(productId);

    public static bool SupportsAnc(ushort productId) => AncProductIds.Contains(productId);

    private static readonly FrozenSet<ushort> MabelT1ProductIds = new ushort[]
    {
        0x0539,
        0x053A,
        0x053B,
    }.ToFrozenSet();

    public static bool UsesMabelT1(ushort productId) => MabelT1ProductIds.Contains(productId);

    /// <summary>
    /// The Hammerhead V3 dongles select the rzDevice40 audio chunk. Its command tuple is
    /// [dataSize, class, id] where this 2023 class uses [class, id, dataSize], EQ and mute
    /// moved to class 0x18, and every response echoes the request payload instead of the
    /// command bytes. The 64-byte 50 41 envelope is unchanged. 059F (BillieT3H) and
    /// 05B9 (Hammerhead V3 Chroma) inherit the same channel; 059F only exposes sidetone
    /// and haptics in its bundle, so unsupported probes surface as partial reads.
    /// </summary>
    private static readonly FrozenSet<ushort> Class18ProductIds = new ushort[]
    {
        0x0A61,
        0x0A64,
        0x0A68,
        0x0A6C,
        0x0A81,
        0x059F,
        0x05B9,
    }.ToFrozenSet();

    public static bool UsesClass18Commands(ushort productId) => Class18ProductIds.Contains(productId);

    /// <summary>
    /// BlackShark V3 X identities (0A54, 1022) select rzDevice25ValkyrieT3: the same
    /// [dataSize, class, id] tuples on class 0x18, sent as 64-byte report id 7 instead
    /// of 2. Its EQ band getter only carries the two prefix bytes.
    /// </summary>
    private static readonly FrozenSet<ushort> ValkyrieT3ProductIds = new ushort[]
    {
        0x057C,
        0x057D,
        0x05B6,
        0x05B3,
        0x0A54,
        0x0A5B,
        0x1021,
        0x1022,
    }.ToFrozenSet();

    public static bool UsesValkyrieT3Commands(ushort productId) => ValkyrieT3ProductIds.Contains(productId);

    /// <summary>
    /// Razer Mako X (05AD) selects rzDeviceJolie. It uses the same 64-byte report-id-7
    /// feature channel as rzDevice25ValkyrieT3, but its ten-band setter carries the
    /// group and preset before the bands.
    /// </summary>
    private static readonly FrozenSet<ushort> JolietProductIds = new ushort[]
    {
        0x05AB,
        0x05AD,
    }.ToFrozenSet();

    public static bool UsesJolietCommands(ushort productId) => JolietProductIds.Contains(productId);

    // rzDevice40 constants: He=[2,24,128] Be=[4,24,0] [12,24,129] Ve=[12,24,1]
    // Ye=[2,24,132] Xe=[3,24,4]. TD={Speaker:1,Mic:2}; the bundle reads and writes the
    // live profile 0 (getEQPreset(0), setEQPreset(0,...)), so the readback matches.
    private const byte Class18 = 0x18;
    public const byte ValkyrieReportId = 7;
    private const byte LiveProfile = 0x00;
    private const byte SpeakerSource = 0x01;
    private const byte MicrophoneSource = 0x02;

    public static readonly byte[] EqPresetRequestClass18 = [2, Class18, 0x80];
    public static readonly byte[] EqPresetHeaderClass18 = [LiveProfile, SpeakerSource];
    public static readonly byte[] EqBandsRequestClass18 = [12, Class18, 0x81];
    public static readonly byte[] EqBandsHeaderClass18 = [LiveProfile, SpeakerSource];
    public static readonly byte[] MicMuteRequestClass18 = [2, Class18, 0x84];
    public static readonly byte[] MicMuteHeaderClass18 = [LiveProfile, MicrophoneSource];
    public static readonly byte[] AudioDndRequestClass18 = [1, Class18, 0x85];
    public static readonly byte[] AudioDndRequestValkyrie = [2, Class18, 0x85];
    public static readonly byte[] AudioDndHeaderClass18 = [LiveProfile];
    public static readonly byte[] SetAudioDndCommandClass18 = [2, Class18, 0x05];
    public static readonly byte[] SetAudioDndCommandValkyrie = [2, Class18, 0x05];

    public static byte[] CreateGetEqPresetClass18() =>
        Create(RazerCommand, EqPresetRequestClass18, [LiveProfile, 0x00]);

    public static byte[] CreateSetEqPresetClass18(byte preset) =>
        Create(RazerCommand, [4, Class18, 0x00], [LiveProfile, SpeakerSource, 0x00, preset]);

    public static byte[] CreateGetEqBandsClass18()
    {
        // The bundle allocates the full 12-byte payload and only sets the profile byte.
        var payload = new byte[EqBandsRequestClass18[0]];
        payload[0] = LiveProfile;
        return Create(RazerCommand, EqBandsRequestClass18, payload);
    }

    public static byte[] CreateSetEqBandsClass18(ReadOnlySpan<byte> bands)
    {
        if (bands.Length != TenBandCount)
        {
            throw new ArgumentException("Equalizer data must contain 10 bands.", nameof(bands));
        }

        var payload = new byte[EqBandsRequestClass18[0]];
        payload[0] = LiveProfile;
        payload[1] = SpeakerSource;
        bands.CopyTo(payload.AsSpan(2));
        return Create(RazerCommand, [12, Class18, 0x01], payload);
    }

    public static byte[] CreateGetMicMuteClass18(byte reportId = ReportId) =>
        Create(reportId, RazerCommand, MicMuteRequestClass18, [LiveProfile, MicrophoneSource]);

    public static byte[] CreateSetMicMuteClass18(bool muted, byte reportId = ReportId) =>
        Create(reportId, RazerCommand, [3, Class18, 0x04], [LiveProfile, MicrophoneSource, muted ? (byte)1 : (byte)0]);

    public static byte[] CreateGetAudioDnd(ushort productId) =>
        UsesValkyrieT3Commands(productId)
            ? RazerValkyrieT3Protocol.Create(0x85, [LiveProfile, 0x00])
            : UsesClass18Commands(productId)
                ? Create(RazerCommand, AudioDndRequestClass18, [LiveProfile])
                : CreateRequest(
                    GetAudioDnd,
                    reportId: UsesMabelT1(productId) ? MabelT1ReportId : ReportId);

    public static byte[] CreateSetAudioDnd(ushort productId, bool enabled)
    {
        var state = enabled ? (byte)1 : (byte)0;
        return UsesValkyrieT3Commands(productId)
            ? RazerValkyrieT3Protocol.Create(0x05, [LiveProfile, state])
            : UsesClass18Commands(productId)
                ? Create(RazerCommand, SetAudioDndCommandClass18, [LiveProfile, state])
                : CreateRequest(
                    SetAudioDndCommand,
                    [state],
                    UsesMabelT1(productId) ? MabelT1ReportId : ReportId);
    }

    public static byte[] CreateGetSidetone(ushort productId) =>
        UsesValkyrieT3Commands(productId)
            ? RazerValkyrieT3Protocol.Create(0x84, [LiveProfile, 0x03, 0x00])
            : CreateRequest(
                GetSidetone,
                reportId: UsesMabelT1(productId) ? MabelT1ReportId : ReportId);

    public static byte[] CreateSetSidetone(ushort productId, bool enabled)
    {
        var state = enabled ? (byte)1 : (byte)0;
        return UsesValkyrieT3Commands(productId)
            ? RazerValkyrieT3Protocol.Create(0x04, [LiveProfile, 0x03, state])
            : CreateRequest(
                SetSidetoneCommand,
                [state],
                UsesMabelT1(productId) ? MabelT1ReportId : ReportId);
    }

    // rzDevice25ValkyrieT3: S=[2,24,128] A=[4,24,0] U=[2,24,129] E=[12,24,1]. The second
    // payload byte is the preset group (0/1, validated by setPresetEQIndex), not a source.
    public static readonly byte[] EqHeaderValkyrie = [LiveProfile, 0x01];

    // getSidetone/setSidetone keep class 0x18 and switch to source 3, so the getter
    // prefix is [profile, source] and the setter is command 0x04.
    public static readonly byte[] SidetoneHeaderValkyrie = [LiveProfile, 0x03];

    public static byte[] CreateGetEqPresetValkyrie() =>
        RazerValkyrieT3Protocol.Create(0x80, [LiveProfile, 0x01]);

    public static byte[] CreateSetEqPresetValkyrie(byte preset) =>
        RazerValkyrieT3Protocol.Create(0x00, [LiveProfile, 0x01, 0x00, preset]);

    public static byte[] CreateGetEqBandsValkyrie() =>
        RazerValkyrieT3Protocol.Create(0x81, [LiveProfile, 0x01]);

    public static byte[] CreateSetEqBandsValkyrie(ReadOnlySpan<byte> bands)
    {
        if (bands.Length != TenBandCount)
        {
            throw new ArgumentException("Equalizer data must contain 10 bands.", nameof(bands));
        }

        var payload = new byte[12];
        payload[0] = LiveProfile;
        payload[1] = 0x01;
        bands.CopyTo(payload.AsSpan(2));
        return RazerValkyrieT3Protocol.Create(0x01, payload);
    }

    // rzDeviceJolie.setEQSetting allocates 14 bytes:
    // [profile, speaker, group, preset, ten unsigned/0x80|abs band levels].
    public static readonly byte[] JolietBassHeader = [LiveProfile];

    // rzDevice40.getEQPreset(profile, speaker, group) sends all three bytes and
    // replies [profile, speaker, group, preset].
    public static byte[] CreateGetEqPresetJoliet() =>
        RazerValkyrieT3Protocol.Create(0x80, [LiveProfile, SpeakerSource, 0x00]);

    public static byte[] CreateSetEqPresetJoliet(byte preset) =>
        RazerValkyrieT3Protocol.Create(0x00, [LiveProfile, SpeakerSource, 0x00, preset]);

    // 05AD answers the staged twelve-byte getter. The two-byte
    // [profile, speaker] form returns no matching ACK on the same unit.
    public static byte[] CreateGetEqBandsJoliet()
    {
        var payload = new byte[EqBandsRequestClass18[0]];
        payload[0] = LiveProfile;
        payload[1] = SpeakerSource;
        return RazerValkyrieT3Protocol.Create(0x81, payload);
    }

    public static byte[] CreateSetEqBandsJoliet(byte preset, ReadOnlySpan<byte> bands)
    {
        if (bands.Length != TenBandCount)
        {
            throw new ArgumentException("Equalizer data must contain 10 bands.", nameof(bands));
        }

        var payload = new byte[14];
        payload[0] = LiveProfile;
        payload[1] = SpeakerSource;
        payload[2] = 0x00;
        payload[3] = preset;
        bands.CopyTo(payload.AsSpan(4));
        return RazerValkyrieT3Protocol.Create(0x01, payload);
    }

    // Joliet inherits rzDevice40's bass commands, which live on class 0x08.
    public static byte[] CreateGetBassJoliet() =>
        RazerValkyrieT3Protocol.Create(0x08, 0x87, [LiveProfile]);

    public static byte[] CreateSetBassJoliet(byte level)
    {
        if (level > 100) throw new ArgumentOutOfRangeException(nameof(level));
        return RazerValkyrieT3Protocol.Create(0x08, 0x07, [LiveProfile, level]);
    }

    public static byte ReadBass(ReadOnlySpan<byte> value)
    {
        Require(value, 1);
        return value[0];
    }

    /// <summary>The class 0x18 EQ-preset reply carries [profile, source, group, preset].</summary>
    public static byte ReadEqPresetClass18(ReadOnlySpan<byte> value)
    {
        Require(value, 2);
        return value[1];
    }

    public static byte[] CreateRemoteMode(bool enabled, byte reportId = ReportId) =>
        Create(reportId, RemoteCommand, [0xE1], [enabled ? (byte)1 : (byte)0]);

    public static byte[] CreateRequest(
        ReadOnlySpan<byte> command,
        ReadOnlySpan<byte> data = default,
        byte reportId = ReportId)
    {
        if (command.IsEmpty)
        {
            throw new ArgumentException("An MXIC command is required.", nameof(command));
        }

        return Create(reportId, RazerCommand, command, data);
    }

    public static byte[] CreateSetEqPreset(byte preset, byte reportId = ReportId) =>
        CreateRequest(SetEqPresetCommand, [preset], reportId);

    public static byte[] CreateSetEqBands(ReadOnlySpan<byte> bands, byte reportId = ReportId)
    {
        if (bands.Length != TenBandCount)
        {
            throw new ArgumentException("Equalizer data must contain 10 bands.", nameof(bands));
        }

        return CreateRequest(SetEqBandsCommand, bands, reportId);
    }

    public static byte[] CreateSetMicMute(bool muted, byte reportId = ReportId) =>
        CreateRequest(SetMicMuteCommand, [muted ? (byte)1 : (byte)0], reportId);

    public static bool TryReadValue(
        ReadOnlySpan<byte> report,
        ReadOnlySpan<byte> expectedHeader,
        out byte[] value) =>
        TryReadValue(report, ReportId, expectedHeader, out value);

    public static bool TryReadValue(
        ReadOnlySpan<byte> report,
        byte reportId,
        ReadOnlySpan<byte> expectedHeader,
        out byte[] value)
    {
        value = [];
        var offset = report.Length > 0 && report[0] == StatusOk ? 1 : 0;
        if (report.Length < offset + 2 || report[offset] != reportId)
        {
            return false;
        }

        var payload = report[(offset + 2)..];
        for (var index = 0; index + 10 <= payload.Length;)
        {
            if (payload[index] != 0x50 || payload[index + 1] != 0x49)
            {
                index++;
                continue;
            }

            var length = payload[index + 8] | (payload[index + 9] << 8);
            if (index + 10 + length > payload.Length)
            {
                return false;
            }

            var data = payload.Slice(index + 10, length);
            if (data.Length >= expectedHeader.Length &&
                data[..expectedHeader.Length].SequenceEqual(expectedHeader))
            {
                value = data[expectedHeader.Length..].ToArray();
                return true;
            }

            index += 10 + length;
        }

        return false;
    }

    public static Version ReadFirmware(ReadOnlySpan<byte> value)
    {
        Require(value, 4);
        return new Version(value[0], value[1], value[2], value[3]);
    }

    public static byte ReadBatteryPercent(ReadOnlySpan<byte> value)
    {
        Require(value, 1);
        return Math.Min(value[0], (byte)100);
    }

    public static bool ReadChargingStatus(ReadOnlySpan<byte> value)
    {
        Require(value, 1);
        return value[0] == 1;
    }

    public static bool ReadWirelessConnectionStatus(ReadOnlySpan<byte> value) =>
        value.Length >= 1 && value[0] == 1;

    public static byte ReadEqPreset(ReadOnlySpan<byte> value)
    {
        Require(value, 1);
        return value[0];
    }

    public static byte[] ReadEqBands(ReadOnlySpan<byte> value)
    {
        Require(value, TenBandCount);
        return value[..TenBandCount].ToArray();
    }

    public static bool ReadMicMute(ReadOnlySpan<byte> value)
    {
        Require(value, 1);
        return value[0] == 1;
    }

    public static bool ReadAudioDnd(ushort productId, ReadOnlySpan<byte> value)
    {
        // The class 0x18 reply is [profile, state]; transports that strip the
        // profile byte hand back only the state.
        if (UsesClass18Commands(productId) || UsesValkyrieT3Commands(productId))
        {
            Require(value, 1);
            return value.Length >= 2 ? value[1] == 1 : value[0] == 1;
        }

        Require(value, 1);
        return value[0] == 1;
    }

    private static byte[] Create(
        byte commandType,
        ReadOnlySpan<byte> command,
        ReadOnlySpan<byte> data) =>
        Create(ReportId, commandType, command, data);

    private static byte[] Create(
        byte reportId,
        byte commandType,
        ReadOnlySpan<byte> command,
        ReadOnlySpan<byte> data)
    {
        var total = command.Length + data.Length;
        if (total + 10 > ReportLength)
        {
            throw new ArgumentException("MXIC command and data exceed the 64-byte report.");
        }

        var report = new byte[ReportLength];
        report[0] = reportId;
        report[1] = 0x80;
        report[2] = (byte)(total + 5);
        var offset = 10;
        if (reportId == MabelT1ReportId)
        {
            report[3] = 0x50;
            report[4] = 0x41;
            report[5] = commandType;
            report[6] = 8;
            report[7] = (byte)total;
            offset = 8;
        }
        else
        {
            report[5] = 0x50;
            report[6] = 0x41;
            report[7] = commandType;
            report[9] = (byte)total;
        }
        command.CopyTo(report.AsSpan(offset));
        data.CopyTo(report.AsSpan(offset + command.Length));
        return report;
    }

    private static void Require(ReadOnlySpan<byte> value, int length)
    {
        if (value.Length < length)
        {
            throw new InvalidDataException(
                $"MXIC response has {value.Length} data bytes; {length} required.");
        }
    }
}
