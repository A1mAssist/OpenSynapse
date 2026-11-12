using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public enum RazerHeadsetAudioTransport
{
    Feature91,
    AvneraFeature65,
    Protocol25Feature65,
    Mxic64,
    Valkyrie64,
    Billie64, Mixer053E, ValkyrieT3Feature64, Jolie64, KrakenBase37, NativeDll,
}

public sealed record RazerHeadsetAudioConnection(
    string DevicePath,
    ushort ProductId,
    string Name,
    ushort FeatureReportByteLength,
    string? ContainerId = null,
    RazerHeadsetAudioTransport Transport = RazerHeadsetAudioTransport.Feature91,
    bool HasControlInterface = true);


/// <summary>
/// Audio controls for the Razer headset families (Nari, Kraken, Barracuda, BlackShark,
/// USB audio controllers). Discovery follows the bundle-declared channel in
/// <see cref="RazerHeadsetChannelCatalog"/>; this service implements the 91-byte feature
/// path and owns the EQ, bass, microphone, audio-source, ANC and noise-suppression
/// commands that RazerHid does not model.
/// </summary>
public sealed partial class RazerHeadsetAudioService
{
    // The Hammerhead V3 and BlackShark V3 X identities (057C/057D, 05B6, 05B9,
    // 0A54/0A5B, 0A61/0A64/0A68/0A6C/0A81, 1021/1022) are listed here for identity,
    // but RazerHeadsetChannelCatalog routes them to the 64-byte rzDevice40 or
    // rzDevice25ValkyrieT3 path instead of this table's 91-byte class 0x08 path.
    // The Xbox/PlayStation BlackShark V3 identities (0A4E/0A55, 0A51/0A58, 101E/101F)
    // route to RazerValkyrieHeadsetService. 0x101C keeps the dedicated BlackShark THX
    // panel instead: it is BlackSharkV3ProProtocol.UsbProductId.
    public static readonly IReadOnlyDictionary<ushort, string> Products =
        new Dictionary<ushort, string>
        {
            [0x051A] = "Razer Nari Ultimate",
            [0x051C] = "Razer Nari",
            [0x051E] = "Razer Nari Essential",
            [0x051F] = "Razer Nari Essential",
            [0x0520] = "Razer USB Audio Controller",
            [0x0521] = "Razer Kraken Kitty Edition",
            [0x0527] = "Razer Kraken Ultimate",
            [0x0528] = "Razer BlackShark V2 Pro",
            [0x0529] = "Razer USB Sound Card",
            [0x052D] = "Razer Kraken V3 Pro",
            [0x0533] = "Razer Kraken V3 HyperSense",
            [0x0539] = "Razer Barracuda Pro",
            [0x053A] = "Razer Barracuda Pro",
            [0x053B] = "Razer Barracuda 2.4",
            [0x053C] = "Razer Barracuda",
            [0x053E] = "Razer Audio Mixer",
            [0x0549] = "Razer Kraken V3",
            [0x0554] = "Razer Kraken Kitty V2 Pro",
            [0x0555] = "Razer BlackShark V2 Pro",
            [0x0565] = "Razer BlackShark V2 HyperSpeed",
            [0x0567] = "Razer Kraken V4 Pro",
            [0x0568] = "Razer Kraken V4 Pro",
            [0x056B] = "Razer Kraken V4",
            [0x056C] = "Razer Kraken V4",
            [0x056E] = "Razer BlackShark V2 HyperSpeed",
            [0x0575] = "Razer BlackShark V3 Pro",
            [0x0576] = "Razer BlackShark V3 Pro",
            [0x0578] = "Razer BlackShark V3",
            [0x0579] = "Razer BlackShark V3",
            [0x057A] = "Razer BlackShark V3",
            [0x057C] = "Razer BlackShark V3 X",
            [0x057D] = "Razer BlackShark V3 X",
            [0x0A4E] = "Razer BlackShark V3 Pro for Xbox",
            [0x0A4F] = "Razer BlackShark V3 Pro for Xbox",
            [0x0A51] = "Razer BlackShark V3 for Xbox",
            [0x0A52] = "Razer BlackShark V3 for Xbox",
            [0x0A54] = "Razer BlackShark V3 X for Xbox",
            [0x0A55] = "Razer BlackShark V3 Pro for Xbox",
            [0x0A56] = "Razer BlackShark V3 X for Xbox",
            [0x0A58] = "Razer BlackShark V3 for Xbox",
            [0x0A5B] = "Razer BlackShark V3 X for Xbox",
            [0x0A61] = "Razer Hammerhead V3 HyperSpeed",
            [0x0A64] = "Razer Hammerhead V3 X HyperSpeed",
            [0x0A68] = "Razer Hammerhead V3 X HyperSpeed for Xbox",
            [0x0A6C] = "Razer Hammerhead V3 X HyperSpeed for PlayStation",
            [0x0A81] = "Razer Hammerhead V3 X HyperSpeed Kuromi Edition",
            [0x1022] = "Razer BlackShark V3 X HyperSpeed for PlayStation",
            [0x1021] = "Razer BlackShark V3 X HyperSpeed for PlayStation",
            [0x101D] = "Razer BlackShark V3 Pro for PlayStation",
            [0x101E] = "Razer BlackShark V3 for PlayStation",
            [0x101F] = "Razer BlackShark V3 for PlayStation",
            [0x0583] = "Razer Barracuda Pro",
            [0x0584] = "Razer Barracuda 2.4",
            [0x0587] = "Razer Kraken Kitty V3 Pro",
            [0x0588] = "Razer Kraken Kitty V3 Pro",
            [0x0589] = "Razer Kraken Kitty V3 Pro",
            [0x0592] = "Razer Barracuda V2 Pro",
            [0x0593] = "Razer Barracuda V2 Pro",
            [0x059F] = "Razer Kraken V4 X Sensa HD Haptics",
            [0x0309] = "Razer Kraken BT Sanrio Limited Edition",
            [0x0526] = "Razer Kraken X USB",
            [0x0530] = "Razer Kraken BT Kitty Edition",
            [0x0537] = "Razer Kraken V3 X",
            [0x0534] = "Razer Kraken BT Sanrio Limited Edition",
            [0x0560] = "Razer Kraken Kitty V2",
            [0x0562] = "Razer Kraken Kitty V2 BT Quartz Edition",
            [0x0532] = "Razer Leviathan V2",
            [0x055A] = "Razer Nommo V2 Pro",
            [0x055C] = "Razer Nommo V2",
            [0x055E] = "Razer Nommo V2 X",
            [0x0542] = "Razer Seiren V2 Pro",
            [0x0543] = "Razer Seiren V2 X",
            [0x056A] = "Razer Seiren V3 Mini",
            [0x0548] = "Razer Leviathan V2 Pro",
            [0x056F] = "Razer Seiren V3 Chroma",
            [0x058E] = "Razer Seiren V3 Pro",
            [0x05A3] = "Razer Leviathan V2 ECR",
            [0x05A6] = "Razer Seiren V3 Chroma 32-Bit DSP",
            [0x058C] = "Razer Hammerhead V3",
            [0x0574] = "Razer Barracuda X Chroma",
            [0x0582] = "Razer Kraken V3 X",
            [0x056D] = "Razer Kraken V4 X",
            [0x0F66] = "Razer Marci",
            [0x05AB] = "Razer Mako",
            [0x05AD] = "Razer Mako X",
            [0x05B6] = "Razer Kraken Kitty V3",
            [0x05B9] = "Razer Hammerhead V3 Chroma",
            // The Seiren Emote shares the Camyt3 middleware bundle; its LED matrix uses the
            // class 0x10 Chroma commands, while EQ and mic volume match this family.
            [0x0F1B] = "Razer Seiren Emote",
        };

    private static readonly IReadOnlySet<ushort> SpeakerProductIds =
        new HashSet<ushort> { 0x0532, 0x055A, 0x055C, 0x055E, 0x0548, 0x05A3, 0x05AB, 0x05AD };

    private static readonly IReadOnlySet<ushort> MicrophoneProductIds =
        new HashSet<ushort> { 0x0542, 0x0543, 0x056A, 0x0F1B, 0x056F, 0x058E, 0x05A6 };

    private readonly IRazerFeatureTransport _transport;
    private readonly string _devicePath;
    private readonly TimeSpan _deviceWait;
    private readonly Action<string>? _diagnostic;
    private readonly RazerHeadsetAudioTransport _transportKind;
    private readonly ushort _productId;
    private readonly IRazerProtocol25FeatureTransport? _protocol25Transport;
    private readonly RazerHidHidReportTransport _hidReports = new();
    private byte _transactionId = 0x1F;

    internal RazerHeadsetAudioService(
        IRazerFeatureTransport transport,
        string devicePath,
        TimeSpan deviceWait,
        Action<string>? diagnostic = null,
        RazerHeadsetAudioTransport transportKind = RazerHeadsetAudioTransport.Feature91,
        ushort productId = 0,
        IRazerProtocol25FeatureTransport? protocol25Transport = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _devicePath = string.IsNullOrWhiteSpace(devicePath)
            ? throw new ArgumentException("A HID device path is required.", nameof(devicePath))
            : devicePath;
        _deviceWait = deviceWait;
        _diagnostic = diagnostic;
        _transportKind = transportKind;
        _productId = productId;
        // Both 64- and 65-byte protocol-25 channels resolve to this transport kind
        // (see ToTransport); the report length comes from the product id.
        _protocol25Transport = protocol25Transport ??
            (transportKind == RazerHeadsetAudioTransport.Protocol25Feature65
                ? new RazerProtocol25FeatureTransport(_devicePath)
                : null);
    }

    public static bool IsKnown(ushort productId) => Products.ContainsKey(productId);

    public static bool IsSupported(ushort productId) =>
        IsKnown(productId) && RazerHeadsetChannelCatalog.Resolve(productId) is not
            (RazerHeadsetChannel.Unsupported or RazerHeadsetChannel.NativeDll);

    private static string? ToContainerId(string physicalDeviceKey)
    {
        const string prefix = "container:";
        return physicalDeviceKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            physicalDeviceKey.Length > prefix.Length
                ? $"{{{physicalDeviceKey[prefix.Length..]}}}"
                : null;
    }

    public static DeviceCategory GetCategory(ushort productId) =>
        MicrophoneProductIds.Contains(productId) ? DeviceCategory.Microphone
        : SpeakerProductIds.Contains(productId) ? DeviceCategory.Speaker
        : DeviceCategory.Headset;

    // The bundle-declared channel decides which interface is usable; a 91-byte feature
    // collection is not a fallback for a 64-byte device.
    internal static bool UsesFeatureReportTransport(ushort productId) =>
        RazerHeadsetChannelCatalog.Resolve(productId) == RazerHeadsetChannel.Feature91;

    public static async Task<RazerHeadsetAudioConnection?> FindAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorInterfacesAsync(
            0x1532,
            cancellationToken,
            item => diagnostic?.Invoke(
                $"headset-audio interface pid={item.ProductId:X4} product='{item.ProductName}' " +
                $"usage=0x{item.UsagePage:X4}:0x{item.Usage:X4} in={item.InputReportByteLength} " +
                $"out={item.OutputReportByteLength} feature={item.FeatureReportByteLength} " +
                $"access={item.Access}"))
            .ConfigureAwait(false);

        var candidates = interfaces
            .Select(item => (Interface: item, Channel: RazerHeadsetChannelCatalog.Resolve(item.ProductId)))
            .Where(candidate => MatchesChannel(candidate.Channel, candidate.Interface))
            .OrderByDescending(candidate => candidate.Interface.Access == DeviceAccessState.Available)
            .ThenByDescending(candidate => candidate.Interface.UsagePage == 0xFF14)
            .ThenBy(candidate => candidate.Interface.ProductId)
            .ToArray();
        if (candidates.Length == 0)
        {
            // Access-denied and audio-only collections can lose the feature-report
            // metadata that normally identifies a usable channel. Keep the device
            // visible so its card can explain why controls are unavailable.
            var fallback = interfaces
                .Where(item => Products.ContainsKey(item.ProductId) &&
                    RazerHeadsetChannelCatalog.Resolve(item.ProductId) != RazerHeadsetChannel.Unsupported)
                .OrderByDescending(item => item.Access == DeviceAccessState.Available)
                .ThenByDescending(item => item.UsagePage == 0xFF14)
                .ThenBy(item => item.ProductId)
                .FirstOrDefault();
            if (fallback is not null)
            {
                var fallbackChannel = RazerHeadsetChannelCatalog.Resolve(fallback.ProductId);
                diagnostic?.Invoke(
                    $"headset-audio selected unresolved {fallbackChannel} pid={fallback.ProductId:X4} " +
                    $"product='{fallback.ProductName}' feature={fallback.FeatureReportByteLength} " +
                    $"access={fallback.Access}");
                return new RazerHeadsetAudioConnection(
                    fallback.DevicePath,
                    fallback.ProductId,
                    Products[fallback.ProductId],
                    fallback.FeatureReportByteLength,
                    ToContainerId(fallback.PhysicalDeviceKey),
                    ToTransport(fallbackChannel));
            }

            var usbDevices = await WindowsUsbDiscovery
                .FindPresentAsync(0x1532, cancellationToken)
                .ConfigureAwait(false);
            var usbDevice = usbDevices
                .Where(item => Products.ContainsKey(item.ProductId) && NeedsHidControlInterface(item.ProductId))
                .OrderBy(item => item.ProductId)
                .FirstOrDefault();
            if (usbDevice.ProductId != 0)
            {
                diagnostic?.Invoke(
                    $"headset-audio selected usb-only pid={usbDevice.ProductId:X4} " +
                    $"path={usbDevice.InstanceId}");
                return CreateUsbFallback(usbDevice);
            }

            diagnostic?.Invoke(
                "headset-audio found no interface for a supported channel; " +
                $"hid={string.Join(",", interfaces.Select(item => $"{item.ProductId:X4}:0x{item.UsagePage:X4}:{item.FeatureReportByteLength}/{item.OutputReportByteLength}"))}; " +
                $"usb={string.Join(",", usbDevices.Select(item => $"{item.ProductId:X4}"))}");
            return null;
        }

        var (chosen, channel) = candidates[0];
        diagnostic?.Invoke(
            $"headset-audio selected {channel} pid={chosen.ProductId:X4} product='{chosen.ProductName}' " +
            $"path={RazerFeatureTransport.DescribeDevicePath(chosen.DevicePath)} " +
            $"usage=0x{chosen.UsagePage:X4}:0x{chosen.Usage:X4} " +
            $"in={chosen.InputReportByteLength} out={chosen.OutputReportByteLength} " +
            $"feature={chosen.FeatureReportByteLength} access={chosen.Access}");
        return new RazerHeadsetAudioConnection(
            chosen.DevicePath,
            chosen.ProductId,
            Products[chosen.ProductId],
            chosen.FeatureReportByteLength,
            ToContainerId(chosen.PhysicalDeviceKey),
            ToTransport(channel));
    }

    internal static bool MatchesChannel(
        RazerHeadsetChannel channel,
        WindowsHidDiscovery.HidInterfaceDescriptor item) =>
        channel switch
        {
            RazerHeadsetChannel.Feature91 => item.FeatureReportByteLength == RazerFeatureReport.Length,
            RazerHeadsetChannel.AvneraFeature65 => item.FeatureReportByteLength == RazerAvnera65Protocol.ReadLength,
            RazerHeadsetChannel.Protocol25Feature65 => item.FeatureReportByteLength == 65,
            RazerHeadsetChannel.Protocol25Feature64 => item.FeatureReportByteLength == 64,
            RazerHeadsetChannel.KrakenBase37 =>
                item.OutputReportByteLength >= RazerKrakenBaseProtocol.ReportLength &&
                item.InputReportByteLength >= 33,
            RazerHeadsetChannel.ValkyrieT3Feature64 => item.FeatureReportByteLength == 64,
            RazerHeadsetChannel.Jolie64 => item.FeatureReportByteLength == 64,
            RazerHeadsetChannel.Mxic64 or RazerHeadsetChannel.Valkyrie64 or RazerHeadsetChannel.Billie64 => item.OutputReportByteLength == RazerMxicHeadsetProtocol.ReportLength && item.InputReportByteLength >= RazerMxicHeadsetProtocol.ReportLength,
            RazerHeadsetChannel.Mixer053E => item.FeatureReportByteLength == RazerMixer053EProtocol.FeatureReportLength && item.InputReportByteLength == RazerMixer053EProtocol.StatusReportLength && item.OutputReportByteLength == RazerMixer053EProtocol.OutputReportLength,
            _ => false,
        };

    private static RazerHeadsetAudioTransport ToTransport(RazerHeadsetChannel channel) =>
        channel switch
        {
            RazerHeadsetChannel.Feature91 => RazerHeadsetAudioTransport.Feature91,
            RazerHeadsetChannel.Mxic64 => RazerHeadsetAudioTransport.Mxic64,
            RazerHeadsetChannel.ValkyrieT3Feature64 => RazerHeadsetAudioTransport.ValkyrieT3Feature64,
            RazerHeadsetChannel.Jolie64 => RazerHeadsetAudioTransport.Jolie64,
            RazerHeadsetChannel.Valkyrie64 => RazerHeadsetAudioTransport.Valkyrie64,
            RazerHeadsetChannel.Billie64 => RazerHeadsetAudioTransport.Billie64,
            RazerHeadsetChannel.KrakenBase37 => RazerHeadsetAudioTransport.KrakenBase37,
            RazerHeadsetChannel.AvneraFeature65 => RazerHeadsetAudioTransport.AvneraFeature65,
            RazerHeadsetChannel.Protocol25Feature65 or RazerHeadsetChannel.Protocol25Feature64 =>
                RazerHeadsetAudioTransport.Protocol25Feature65,
            RazerHeadsetChannel.NativeDll => RazerHeadsetAudioTransport.NativeDll,
            RazerHeadsetChannel.Mixer053E => RazerHeadsetAudioTransport.Mixer053E,
            // Keep the dedicated BlackShark panel's historical fallback transport.
            RazerHeadsetChannel.DedicatedBlackShark => RazerHeadsetAudioTransport.Feature91,
            _ => throw new NotSupportedException(
                $"Headset channel {channel} has no audio transport."),
        };

    public async Task<RazerHeadsetAudioState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        if (_transportKind == RazerHeadsetAudioTransport.NativeDll)
        {
            return new RazerHeadsetAudioState(
                null, null, null, null, null, null, null, null, null, null, null, null,
                ["Razer audio component required."]);
        }

        if (_protocol25Transport is not null)
        {
            return await ReadProtocol25StateAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_transportKind == RazerHeadsetAudioTransport.KrakenBase37)
        {
            return await ReadKrakenBaseStateAsync(cancellationToken).ConfigureAwait(false);
        }

        var errors = new List<string>();
        var firmware = await ProbeAsync(
            "firmware", RazerHeadsetAudioProtocol.GetFirmware, RazerHeadsetAudioProtocol.ReadFirmware,
            errors, cancellationToken).ConfigureAwait(false);
        var battery = await ProbeAsync(
            "battery", RazerHeadsetAudioProtocol.GetBatteryLevel, RazerHeadsetAudioProtocol.ReadBatteryPercent,
            errors, cancellationToken).ConfigureAwait(false);
        var charging = await ProbeAsync(
            "charging", RazerHeadsetAudioProtocol.GetChargingStatus, RazerHeadsetAudioProtocol.ReadChargingStatus,
            errors, cancellationToken).ConfigureAwait(false);
        var preset = await ProbeAsync(
            "EQ preset", txn => RazerHeadsetAudioProtocol.GetEqPreset(txn), RazerHeadsetAudioProtocol.ReadEqPreset,
            errors, cancellationToken).ConfigureAwait(false);
        var bass = await ProbeAsync(
            "bass", txn => RazerHeadsetAudioProtocol.GetBass(txn), RazerHeadsetAudioProtocol.ReadBass,
            errors, cancellationToken).ConfigureAwait(false);
        var micMute = await ProbeAsync(
            "microphone mute", txn => RazerHeadsetAudioProtocol.GetMicMute(txn), RazerHeadsetAudioProtocol.ReadMicMute,
            errors, cancellationToken).ConfigureAwait(false);
        var micVolume = await ProbeAsync(
            "microphone volume", txn => RazerHeadsetAudioProtocol.GetMicVolume(txn), RazerHeadsetAudioProtocol.ReadMicVolume,
            errors, cancellationToken).ConfigureAwait(false);
        var source = await ProbeAsync(
            "audio source", txn => RazerHeadsetAudioProtocol.GetAudioSource(txn), RazerHeadsetAudioProtocol.ReadAudioSource,
            errors, cancellationToken).ConfigureAwait(false);
        var noise = await ProbeAsync(
            "noise suppression", txn => RazerHeadsetAudioProtocol.GetAudioInputControlState(txn),
            RazerHeadsetAudioProtocol.ReadAudioInputControlState, errors, cancellationToken).ConfigureAwait(false);
        var haptics = await ProbeAsync(
            "haptics",
            RazerHapticsProtocol.CreateGetHapticsControlRequest,
            ParseHapticsResponse,
            errors,
            cancellationToken,
            recordUnsupported: false).ConfigureAwait(false);

        IReadOnlyList<byte>? bands = null;
        try
        {
            bands = await ReadEqBandsRequiredAsync(true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            try
            {
                bands = await ReadEqBandsRequiredAsync(false, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception fallback) when (IsExpected(fallback))
            {
                errors.Add($"EQ bands: {exception.Message}; 8-band fallback: {fallback.Message}");
            }
        }

        RazerAncState? ancState = null;
        byte? ancLevel = null;
        try
        {
            (ancState, ancLevel) = RazerHeadsetAudioProtocol.ReadAnc(
                await QueryAsync(txn => RazerHeadsetAudioProtocol.GetAnc(txn), "read ANC", cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"ANC: {exception.Message}");
        }

        // The protocol has no default selector; band 0 is the first of its ten-band range.
        var parametricEq = await ProbeAsync(
            "parametric EQ",
            txn => RazerHeadsetAudioProtocol.GetParametricEqSetting(txn, band: 0),
            RazerHeadsetAudioProtocol.ReadParametricEqSetting,
            errors,
            cancellationToken,
            recordUnsupported: false).ConfigureAwait(false);
        var compressor = await ProbeAsync(
            "microphone compressor",
            txn => RazerHeadsetAudioProtocol.GetMicCompressor(txn),
            RazerHeadsetAudioProtocol.ReadMicCompressor,
            errors,
            cancellationToken,
            recordUnsupported: false).ConfigureAwait(false);
        var deEsser = await ProbeAsync(
            "microphone de-esser",
            txn => RazerHeadsetAudioProtocol.GetMicDeEsser(txn),
            RazerHeadsetAudioProtocol.ReadMicDeEsser,
            errors,
            cancellationToken,
            recordUnsupported: false).ConfigureAwait(false);
        var micMonitoring = await ProbeAsync(
            "mic monitoring",
            txn => RazerHeadsetAudioProtocol.GetMicReturnSwitch(txn),
            RazerHeadsetAudioProtocol.ReadMicReturnSwitch,
            errors,
            cancellationToken,
            recordUnsupported: false).ConfigureAwait(false);
        var reverb = await ProbeAsync(
            "microphone reverb",
            txn => RazerHeadsetAudioProtocol.GetMicReverb(txn),
            RazerHeadsetAudioProtocol.ReadMicReverb,
            errors,
            cancellationToken,
            recordUnsupported: false).ConfigureAwait(false);
        var limiter = await ProbeAsync(
            "microphone limiter",
            txn => RazerHeadsetAudioProtocol.GetMicLimiter(txn),
            RazerHeadsetAudioProtocol.ReadMicLimiter,
            errors,
            cancellationToken,
            recordUnsupported: false).ConfigureAwait(false);
        var dnd = await ProbeAsync(
            "audio DND",
            txn => RazerHeadsetAudioProtocol.GetAudioDnd(txn),
            RazerHeadsetAudioProtocol.ReadAudioDnd,
            errors,
            cancellationToken,
            recordUnsupported: false).ConfigureAwait(false);
        var voicePrompts = await ProbeAsync(
            "audio voice prompts",
            txn => RazerHeadsetAudioProtocol.GetAudioVoicePrompts(txn),
            RazerHeadsetAudioProtocol.ReadAudioVoicePrompts,
            errors,
            cancellationToken,
            recordUnsupported: false).ConfigureAwait(false);
        var autoPause = await ProbeAsync(
            "audio auto-pause",
            txn => RazerHeadsetAudioProtocol.GetAudioAutoPause(txn),
            RazerHeadsetAudioProtocol.ReadAudioAutoPause,
            errors,
            cancellationToken,
            recordUnsupported: false).ConfigureAwait(false);
        var gestureControl = await ProbeAsync(
            "audio gesture control",
            txn => RazerHeadsetAudioProtocol.GetAudioGestureControl(txn),
            RazerHeadsetAudioProtocol.ReadAudioGestureControl,
            errors,
            cancellationToken,
            recordUnsupported: false).ConfigureAwait(false);
        var dolby = await ProbeAsync(
            "Dolby state",
            txn => RazerHeadsetAudioProtocol.GetDolbyState(txn),
            RazerHeadsetAudioProtocol.ReadDolbyState,
            errors,
            cancellationToken,
            recordUnsupported: false).ConfigureAwait(false);
        var nightMode = await ProbeAsync(
            "EQ night mode",
            txn => RazerHeadsetAudioProtocol.GetEqNightMode(txn),
            RazerHeadsetAudioProtocol.ReadEqNightMode,
            errors,
            cancellationToken,
            recordUnsupported: false).ConfigureAwait(false);

        return new RazerHeadsetAudioState(
            firmware.Ok ? firmware.Value : null,
            battery.Ok ? battery.Value : null,
            charging.Ok ? charging.Value : null,
            preset.Ok ? preset.Value : null,
            bands,
            bass.Ok ? bass.Value : null,
            micMute.Ok ? micMute.Value : null,
            micVolume.Ok ? micVolume.Value : null,
            source.Ok ? source.Value : null,
            noise.Ok ? noise.Value : null,
            ancState,
            ancLevel,
            errors)
        {
            ParametricEq = parametricEq.Ok ? parametricEq.Value : null,
            MicCompressor = compressor.Ok ? compressor.Value : null,
            MicReturnSwitch = micMonitoring.Ok ? micMonitoring.Value : null,
            MicDeEsser = deEsser.Ok ? deEsser.Value : null,
            MicReverb = reverb.Ok ? reverb.Value : null,
            MicLimiter = limiter.Ok ? limiter.Value : null,
            Dnd = dnd.Ok ? dnd.Value : null,
            VoicePrompts = voicePrompts.Ok ? voicePrompts.Value : null,
            AutoPause = autoPause.Ok ? autoPause.Value : null,
            GestureControl = gestureControl.Ok ? gestureControl.Value : null,
            Dolby = dolby.Ok ? dolby.Value : null,
            NightMode = nightMode.Ok ? nightMode.Value : null,
            Haptics = haptics.Ok ? haptics.Value : null,
        };
    }

    public Task SetHapticsAsync(bool enabled, ushort strength, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException(
            "Headset haptics writes require independent per-product evidence for the 91-byte feature envelope."));

    public async Task<bool> GetMicReturnSwitchAsync(CancellationToken cancellationToken = default) =>
        RazerHeadsetAudioProtocol.ReadMicReturnSwitch(
            await QueryAsync(
                txn => RazerHeadsetAudioProtocol.GetMicReturnSwitch(txn),
                "read mic monitoring",
                cancellationToken).ConfigureAwait(false));

    public Task SetMicReturnSwitchAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetMicReturnSwitch(txn),
            RazerHeadsetAudioProtocol.ReadMicReturnSwitch,
            enabled,
            (value, txn) => RazerHeadsetAudioProtocol.SetMicReturnSwitch(txn, value),
            value => value == enabled,
            "mic monitoring",
            cancellationToken);

    public async Task<RazerParametricEqBand> GetParametricEqAsync(
        byte band,
        CancellationToken cancellationToken = default) =>
        RazerHeadsetAudioProtocol.ReadParametricEqSetting(
            await QueryAsync(
                txn => RazerHeadsetAudioProtocol.GetParametricEqSetting(txn, band),
                "read parametric EQ",
                cancellationToken).ConfigureAwait(false));

    public Task SetAudioSourceAsync(RazerAudioSource source, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        return WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetAudioSource(txn),
            RazerHeadsetAudioProtocol.ReadAudioSource,
            source,
            (value, txn) => RazerHeadsetAudioProtocol.SetAudioSource(txn, value),
            value => value == source,
            "audio source",
            cancellationToken);
    }

    public Task SetNoiseSuppressionAsync(bool enabled, CancellationToken cancellationToken = default) =>
        SetNoiseSuppressionCoreAsync(enabled, cancellationToken);

    public async Task SetAncAsync(
        RazerAncState state,
        byte level,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }
        if (level > RazerHeadsetAudioProtocol.TenBandCount)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "ANC level must be between 0 and 10.");
        }

        if (_transportKind == RazerHeadsetAudioTransport.KrakenBase37)
        {
            var enabled = state != RazerAncState.Off;
            await QueryKrakenBaseAsync(
                    RazerKrakenBaseProtocol.CreateAncSetRequest(enabled), cancellationToken)
                .ConfigureAwait(false);
            var actual = RazerKrakenBaseProtocol.ParseAncEnabled(
                await QueryKrakenBaseAsync(
                        RazerKrakenBaseProtocol.CreateAncGetRequest(), cancellationToken)
                    .ConfigureAwait(false));
            if (actual is null || actual.Value != enabled)
            {
                throw new InvalidOperationException(
                    "Razer ANC read-back did not match the requested state.");
            }

            return;
        }

        var previous = RazerHeadsetAudioProtocol.ReadAnc(
            await QueryAsync(txn => RazerHeadsetAudioProtocol.GetAnc(txn), "read ANC", cancellationToken)
                .ConfigureAwait(false));
        if (previous.State == state && previous.Level == level)
        {
            return;
        }

        try
        {
            await QueryAsync(
                txn => RazerHeadsetAudioProtocol.SetAnc(txn, state, level),
                "set ANC",
                cancellationToken).ConfigureAwait(false);
            var actual = RazerHeadsetAudioProtocol.ReadAnc(
                await QueryAsync(txn => RazerHeadsetAudioProtocol.GetAnc(txn), "read back ANC", cancellationToken)
                    .ConfigureAwait(false));
            if (actual.State != state || actual.Level != level)
            {
                throw new InvalidOperationException(
                    $"ANC readback was {actual.State}/{actual.Level}, expected {state}/{level}.");
            }
        }
        catch
        {
            await TryRestoreAsync(
                txn => RazerHeadsetAudioProtocol.SetAnc(txn, previous.State, previous.Level),
                "restore ANC").ConfigureAwait(false);
            throw;
        }
    }

    public Task SetParametricEqAsync(
        RazerParametricEqBand setting,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetParametricEqSetting(
                txn, setting.Index, setting.Direction, setting.Profile),
            RazerHeadsetAudioProtocol.ReadParametricEqSetting,
            setting,
            (value, txn) => RazerHeadsetAudioProtocol.SetParametricEqSetting(txn, value),
            value => value == setting,
            "parametric EQ",
            cancellationToken);

    public Task SetMicCompressorAsync(
        RazerMicCompressor compressor,
        CancellationToken cancellationToken = default)
    {
        if (compressor.Db > 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(compressor), "Razer mic compressor db must be zero or negative.");
        }

        return WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetMicCompressor(txn, compressor.Profile),
            RazerHeadsetAudioProtocol.ReadMicCompressor,
            compressor,
            (value, txn) => RazerHeadsetAudioProtocol.SetMicCompressor(txn, value),
            value => value == compressor,
            "microphone compressor",
            cancellationToken);
    }

    public Task SetMicDeEsserAsync(byte value, CancellationToken cancellationToken = default)
    {
        if (value > RazerHeadsetAudioProtocol.MicDeEsserMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Razer mic de-esser value must be between 0 and {RazerHeadsetAudioProtocol.MicDeEsserMax}.");
        }

        return WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetMicDeEsser(txn),
            RazerHeadsetAudioProtocol.ReadMicDeEsser,
            value,
            (setting, txn) => RazerHeadsetAudioProtocol.SetMicDeEsser(txn, setting),
            actual => actual == value,
            "microphone de-esser",
            cancellationToken);
    }

    public Task SetMicReverbAsync(
        RazerMicReverbRoomMode roomMode,
        byte intensity,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(roomMode))
        {
            throw new ArgumentOutOfRangeException(nameof(roomMode));
        }

        if (intensity > RazerHeadsetAudioProtocol.MicReverbIntensityMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intensity),
                $"Razer mic reverb intensity must be between 0 and {RazerHeadsetAudioProtocol.MicReverbIntensityMax}.");
        }

        return WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetMicReverb(txn),
            RazerHeadsetAudioProtocol.ReadMicReverb,
            (roomMode, intensity),
            (value, txn) => RazerHeadsetAudioProtocol.SetMicReverb(txn, value.Item1, value.Item2),
            value => value.Item1 == roomMode && value.Item2 == intensity,
            "microphone reverb",
            cancellationToken);
    }

    public Task SetMicLimiterAsync(sbyte thresholdDb, CancellationToken cancellationToken = default)
    {
        if (thresholdDb is < RazerHeadsetAudioProtocol.MicLimiterMinDb or > 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(thresholdDb),
                $"Razer mic limiter threshold must be between {RazerHeadsetAudioProtocol.MicLimiterMinDb} and 0 dB.");
        }

        return WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetMicLimiter(txn),
            RazerHeadsetAudioProtocol.ReadMicLimiter,
            thresholdDb,
            (value, txn) => RazerHeadsetAudioProtocol.SetMicLimiter(txn, value),
            actual => actual == thresholdDb,
            "microphone limiter",
            cancellationToken);
    }

    public Task SetAudioDndAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetAudioDnd(txn),
            RazerHeadsetAudioProtocol.ReadAudioDnd,
            enabled,
            (value, txn) => RazerHeadsetAudioProtocol.SetAudioDnd(txn, value ? (byte)1 : (byte)0),
            value => value == enabled,
            "audio DND",
            cancellationToken);

    public Task SetAudioVoicePromptsAsync(
        bool enabled,
        byte promptIndex = RazerHeadsetAudioProtocol.AudioPromptAll,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetAudioVoicePrompts(txn, promptIndex),
            RazerHeadsetAudioProtocol.ReadAudioVoicePrompts,
            (promptIndex, enabled),
            (value, txn) => RazerHeadsetAudioProtocol.SetAudioVoicePrompts(
                txn, value.Item2 ? (byte)1 : (byte)0, promptIndex),
            value => value.Item2 == enabled &&
                (promptIndex == RazerHeadsetAudioProtocol.AudioPromptAll || value.Item1 == promptIndex),
            "audio voice prompts",
            cancellationToken);

    public Task SetAudioAutoPauseAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetAudioAutoPause(txn),
            RazerHeadsetAudioProtocol.ReadAudioAutoPause,
            enabled,
            (value, txn) => RazerHeadsetAudioProtocol.SetAudioAutoPause(txn, value ? (byte)1 : (byte)0),
            value => value == enabled,
            "audio auto-pause",
            cancellationToken);

    public Task SetAudioGestureControlAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetAudioGestureControl(txn),
            RazerHeadsetAudioProtocol.ReadAudioGestureControl,
            enabled,
            (value, txn) => RazerHeadsetAudioProtocol.SetAudioGestureControl(txn, value ? (byte)1 : (byte)0),
            value => value == enabled,
            "audio gesture control",
            cancellationToken);

    public Task SetDolbyStateAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetDolbyState(txn),
            RazerHeadsetAudioProtocol.ReadDolbyState,
            enabled,
            (value, txn) => RazerHeadsetAudioProtocol.SetDolbyState(txn, value ? (byte)1 : (byte)0),
            value => value == enabled,
            "Dolby state",
            cancellationToken);

    public Task SetEqNightModeAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAsync(
            txn => RazerHeadsetAudioProtocol.GetEqNightMode(txn),
            RazerHeadsetAudioProtocol.ReadEqNightMode,
            enabled,
            (value, txn) => RazerHeadsetAudioProtocol.SetEqNightMode(txn, value ? (byte)1 : (byte)0),
            value => value == enabled,
            "EQ night mode",
            cancellationToken);

    private async Task WriteAsync<T>(
        Func<byte, byte[]> readRequest,
        Func<ReadOnlySpan<byte>, T> parse,
        T value,
        Func<T, byte, byte[]> writeRequest,
        Func<T, bool> matches,
        string label,
        CancellationToken cancellationToken)
    {
        var previous = parse(await QueryAsync(readRequest, $"read {label}", cancellationToken).ConfigureAwait(false));
        if (matches(previous))
        {
            return;
        }

        try
        {
            await QueryAsync(txn => writeRequest(value, txn), $"set {label}", cancellationToken)
                .ConfigureAwait(false);
            var actual = parse(
                await QueryAsync(readRequest, $"read back {label}", cancellationToken).ConfigureAwait(false));
            if (!matches(actual))
            {
                throw new InvalidOperationException($"{label} readback did not match the requested value.");
            }
        }
        catch
        {
            await TryRestoreAsync(txn => writeRequest(previous, txn), $"restore {label}").ConfigureAwait(false);
            throw;
        }
    }

    private async Task<byte[]> ReadEqBandsRequiredAsync(bool tenBand, CancellationToken cancellationToken) =>
        RazerHeadsetAudioProtocol.ReadEqBands(
            await QueryAsync(
                txn => RazerHeadsetAudioProtocol.GetEqBands(txn, tenBand),
                tenBand ? "read 10-band EQ" : "read 8-band EQ",
                cancellationToken).ConfigureAwait(false),
            tenBand);

    // ProbeAsync exposes only the response; rebuild the getter request used for its echo check.
    private static RazerHapticsControlState ParseHapticsResponse(ReadOnlySpan<byte> response) =>
        RazerHapticsProtocol.ParseHapticsControl(
            response,
            RazerHapticsProtocol.CreateGetHapticsControlRequest(
                response.Length > 2 ? response[2] : (byte)0));

    private Task<(bool Ok, T Value)> ProbeAsync<T>(
        string label,
        Func<byte, byte[]> request,
        Func<ReadOnlySpan<byte>, T> parse,
        List<string> errors,
        CancellationToken cancellationToken)
        => ProbeAsync(label, request, parse, errors, cancellationToken, recordUnsupported: true);

    private async Task<(bool Ok, T Value)> ProbeAsync<T>(
        string label,
        Func<byte, byte[]> request,
        Func<ReadOnlySpan<byte>, T> parse,
        List<string> errors,
        CancellationToken cancellationToken,
        bool recordUnsupported)
    {
        try
        {
            return (true, parse(await QueryAsync(request, $"read {label}", cancellationToken).ConfigureAwait(false)));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            // Advanced audio commands are optional across this device family.
            if (recordUnsupported || exception is not NotSupportedException)
            {
                errors.Add($"{label}: {exception.Message}");
            }

            return (false, default!);
        }
    }

    private async Task TryRestoreAsync(Func<byte, byte[]> request, string label)
    {
        try
        {
            await QueryAsync(request, label, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"headset {label} failed: {exception.Message}");
        }
    }

    private async Task<byte[]> QueryAsync(
        Func<byte, byte[]> request,
        string label,
        CancellationToken cancellationToken)
    {
        var report = request(NextTransactionId());
        _diagnostic?.Invoke(
            $"headset {label}: size={report[6]}, class=0x{report[7]:X2}, command=0x{report[8]:X2}");
        return await _transport.QueryPreparedAsync(
            _devicePath, report, _deviceWait, cancellationToken).ConfigureAwait(false);
    }

    private byte NextTransactionId()
    {
        if (_transactionId >= 0x1F)
        {
            _transactionId = 0;
        }
        else
        {
            _transactionId++;
        }

        return _transactionId;
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException;
}
