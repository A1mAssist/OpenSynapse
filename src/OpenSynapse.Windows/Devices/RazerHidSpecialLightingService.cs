using System.Collections.Concurrent;
using System.Collections.Frozen;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public enum RazerHidSpecialLightingKind
{
    Argb320,
    Kraken37,
    KrakenV4,
    KrystalT2,
    AvneraChroma,
    Protocol40Chroma,
}

public sealed class RazerHidSpecialLightingConnection
{
    internal RazerHidSpecialLightingConnection(
        ushort productId,
        string displayName,
        RazerHidSpecialLightingKind kind,
        string instanceId,
        string? devicePath,
        string? controlDevicePath,
        string? featureDevicePath,
        IReadOnlySet<RazerHidLightingEffect> effects,
        bool supportsStaticColor,
        bool isMergedIntoAudioPage,
        string? error)
    {
        ProductId = productId;
        DisplayName = displayName;
        Kind = kind;
        InstanceId = instanceId;
        DevicePath = devicePath;
        ControlDevicePath = controlDevicePath;
        FeatureDevicePath = featureDevicePath;
        SupportedEffects = effects;
        SupportsStaticColor = supportsStaticColor;
        IsMergedIntoAudioPage = isMergedIntoAudioPage;
        Error = error;
    }

    public ushort ProductId { get; }
    public string DisplayName { get; }
    public RazerHidSpecialLightingKind Kind { get; }
    public string InstanceId { get; }
    public IReadOnlySet<RazerHidLightingEffect> SupportedEffects { get; }
    public bool SupportsStaticColor { get; }
    /// <summary>
    /// True when the audio page owns this headset and shows the lighting card there.
    /// The audio page only discovers a headset whose bundle channel has a matching
    /// interface, so a headset without one keeps its own lighting row.
    /// </summary>
    public bool IsMergedIntoAudioPage { get; }
    public string? Error { get; }
    public bool IsReady => DevicePath is not null || ControlDevicePath is not null;
    internal string? DevicePath { get; }
    internal string? ControlDevicePath { get; }
    internal string? FeatureDevicePath { get; }
}

public sealed partial class RazerHidSpecialLightingService
{
    public Task<string?> TryReadSerialAsync(
        RazerHidSpecialLightingConnection connection,
        CancellationToken cancellationToken = default) => connection.DevicePath is { } path
            ? WindowsHidDiscovery.TryReadSerialNumberAsync(path, cancellationToken)
            : Task.FromResult<string?>(null);

    private const ushort ArgbProductId = 0x0F1F;
    private static readonly FrozenDictionary<ushort, KrakenDefinition> KrakenDevices = BuildKrakenDevices();

    private static FrozenDictionary<ushort, KrakenDefinition> BuildKrakenDevices()
    {
        var devices = new Dictionary<ushort, KrakenDefinition>
        {
            [0x0501] = new("Razer Kraken 7.1", RazerHidKrakenLayout.Rainie,
                Effects(RazerHidLightingEffect.Off, RazerHidLightingEffect.Static), false),
            [0x0504] = new("Razer Kraken 7.1 Chroma", RazerHidKrakenLayout.Rainie,
                Effects(RazerHidLightingEffect.Off, RazerHidLightingEffect.Static,
                    RazerHidLightingEffect.Spectrum, RazerHidLightingEffect.BreathingSingle,
                    RazerHidLightingEffect.Custom), true),
            [0x0506] = new("Razer Kraken 7.1", RazerHidKrakenLayout.Rainie,
                Effects(RazerHidLightingEffect.Off, RazerHidLightingEffect.Static), false),
            [0x0510] = Kylie("Razer Kraken 7.1 V2"),
            [0x0520] = Kylie("Razer Kraken Tournament Edition"),
            [0x0521] = Kylie("Razer Kraken Kitty Edition"),
            [0x0526] = Kylie("Razer Kraken X USB"),
            [0x0527] = Kylie("Razer Kraken Ultimate"),
            [0x0560] = Kylie("Razer Kraken Kitty V2"),
            [0x0554] = Kylie("Razer Kraken Kitty V2 Pro"),
            // The Kraken V4 exposes a 64-byte FF14 output report on the USB-C cable.
            // The 2.4 GHz receiver (056C) inherits the same collection and lighting
            // methods from AudioBillieT2Dongle; its report byte 9 is 0x80.
            [0x056B] = new(
                "Razer Kraken V4 (Wired)",
                RazerHidKrakenLayout.Kylie,
                Effects(RazerHidLightingEffect.Off, RazerHidLightingEffect.Static,
                    RazerHidLightingEffect.BreathingSingle, RazerHidLightingEffect.Spectrum,
                    RazerHidLightingEffect.Wave, RazerHidLightingEffect.StarlightRandom,
                    RazerHidLightingEffect.Fire, RazerHidLightingEffect.AudioMeter,
                    RazerHidLightingEffect.Wheel),
                true,
                RazerHidSpecialLightingKind.KrakenV4,
                LedCount: 9),
            [0x056C] = new(
                "Razer Kraken V4 (2.4 GHz)",
                RazerHidKrakenLayout.Kylie,
                Effects(RazerHidLightingEffect.Off, RazerHidLightingEffect.Static,
                    RazerHidLightingEffect.BreathingSingle, RazerHidLightingEffect.Spectrum,
                    RazerHidLightingEffect.Wave, RazerHidLightingEffect.StarlightRandom,
                    RazerHidLightingEffect.Fire, RazerHidLightingEffect.AudioMeter,
                    RazerHidLightingEffect.Wheel),
                true,
                RazerHidSpecialLightingKind.KrakenV4,
                LedCount: 9),
            // AudioMabelT3C (Barracuda X Chroma) inherits the AudioBillieT2 frame and
            // adds Breathing, SpectrumCycling and CustomFrame; it declares NUM_LEDS=6,
            // so a custom frame fills six LEDs instead of the Billie T2 ring's nine.
            [0x0574] = new(
                "Razer Barracuda X Chroma",
                RazerHidKrakenLayout.Kylie,
                Effects(RazerHidLightingEffect.Off, RazerHidLightingEffect.Static,
                    RazerHidLightingEffect.BreathingSingle, RazerHidLightingEffect.Spectrum,
                    RazerHidLightingEffect.Wave, RazerHidLightingEffect.Custom),
                true,
                RazerHidSpecialLightingKind.KrakenV4,
                LedCount: 6),
            [0x0533] = Krystal("Razer Kraken V3 HyperSense"),
            [0x0549] = Krystal("Razer Kraken V3"),
            // AudioAvneraDevice: 64-byte feature frame, report id 255.
            [0x051A] = new("Razer Nari Ultimate", RazerHidKrakenLayout.Kylie,
                Effects(RazerHidLightingEffect.Off, RazerHidLightingEffect.Static,
                    RazerHidLightingEffect.Custom),
                true, RazerHidSpecialLightingKind.AvneraChroma, LedCount: 1),
            // rzDevice40 Chroma family 1: 64-byte feature frame, report id 2.
            [0x05B9] = new("Razer Hammerhead V3 Chroma", RazerHidKrakenLayout.Kylie,
                Effects(RazerHidLightingEffect.Off, RazerHidLightingEffect.Static,
                    RazerHidLightingEffect.BreathingSingle, RazerHidLightingEffect.Spectrum),
                true, RazerHidSpecialLightingKind.Protocol40Chroma, LedCount: 1),
        };
        AddBillieDevices(devices);
        AddKrystalT3Devices(devices);
        return devices.ToFrozenDictionary();
    }

    /// <summary>
    /// Headsets that already own an audio page keep their 37-byte lighting there, so the
    /// lighting discovery must not emit a second, lighting-only row for the same device.
    /// </summary>
    /// <remarks>
    /// The audio page only discovers a headset whose bundle channel has a matching
    /// interface, so the lighting card may only move there for a headset that has one.
    /// A headset without that interface keeps its own lighting row; otherwise it would
    /// vanish from the app, because neither page could show it.
    /// </remarks>
    public static bool IsMergedIntoAudioPage(ushort productId, bool hasAudioChannelInterface) =>
        hasAudioChannelInterface && RazerHeadsetAudioService.IsSupported(productId);

    /// <summary>
    /// True when the audio page can discover this headset on the channel its bundle declares.
    /// </summary>
    internal static bool HasAudioChannelInterface(
        ushort productId,
        IEnumerable<WindowsHidDiscovery.HidInterfaceDescriptor> interfaces)
    {
        var channel = RazerHeadsetChannelCatalog.Resolve(productId);
        // Mabel T3C carries audio and lighting on the same 64-byte FF14 collection.
        return (channel is RazerHeadsetChannel.Feature91 or RazerHeadsetChannel.KrakenBase37 ||
                (productId == 0x0574 && channel == RazerHeadsetChannel.Billie64)) &&
            interfaces.Any(item => RazerHeadsetAudioService.MatchesChannel(channel, item));
    }

    /// <summary>
    /// AudioMabelT3C (Barracuda X Chroma) uses hid.write; HidD_SetOutputReport stalls on 0574.
    /// </summary>
    internal static bool KrakenV4UsesSetOutputReport(ushort productId) => productId != 0x0574;

    private readonly Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> _sendOutput;
    private readonly Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> _sendFeature;
    private readonly Func<string, IReadOnlyList<byte[]>, bool, bool, CancellationToken, Task> _sendKrakenV4;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _krakenV4Armed =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _krakenV4Brightness =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RazerMxicHeadsetTransport> _krakenV4Transports =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string>? _diagnostic;

    public RazerHidSpecialLightingService(Action<string>? diagnostic = null)
        : this(new RazerHidHidReportTransport().SendOutputAsync, null, null, diagnostic) { }

    internal RazerHidSpecialLightingService(
        Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> sendOutput, Action<string>? diagnostic = null)
        : this(sendOutput,
            (path, reports, _, _, cancellationToken) => SendAllAsync(sendOutput, path, reports, cancellationToken),
            null,
            diagnostic)
    {
    }

    internal RazerHidSpecialLightingService(
        Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> sendOutput,
        Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> sendFeature,
        Action<string>? diagnostic = null)
        : this(sendOutput,
            (path, reports, _, _, cancellationToken) => SendAllAsync(sendOutput, path, reports, cancellationToken),
            sendFeature,
            diagnostic)
    {
    }

    private RazerHidSpecialLightingService(
        Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> sendOutput,
        Func<string, IReadOnlyList<byte[]>, bool, bool, CancellationToken, Task>? sendKrakenV4,
        Func<string, ReadOnlyMemory<byte>, CancellationToken, Task>? sendFeature,
        Action<string>? diagnostic)
    {
        _sendOutput = sendOutput;
        _sendFeature = sendFeature ?? new RazerHidHidReportTransport().SendFeatureAsync;
        _sendKrakenV4 = sendKrakenV4 ?? SendKrakenV4Async;
        _diagnostic = diagnostic;
    }

    private static async Task SendAllAsync(
        Func<string, ReadOnlyMemory<byte>, CancellationToken, Task> sendOutput,
        string devicePath,
        IReadOnlyList<byte[]> reports,
        CancellationToken cancellationToken)
    {
        foreach (var report in reports)
        {
            await sendOutput(devicePath, report, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<RazerHidSpecialLightingConnection>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorInterfacesAsync(
            RazerHidDeviceDefinition.VendorId, cancellationToken).ConfigureAwait(false);
        var results = new List<RazerHidSpecialLightingConnection>();
        foreach (var group in interfaces
            .Where(item => item.ProductId == ArgbProductId || KrakenDevices.ContainsKey(item.ProductId))
            .GroupBy(item => (item.ProductId, item.PhysicalDeviceKey)))
        {
            var argb = group.Key.ProductId == ArgbProductId;
            var definition = argb ? null : KrakenDevices[group.Key.ProductId];
            var krakenV4 = definition?.Kind == RazerHidSpecialLightingKind.KrakenV4;
            var avnera = definition?.Kind == RazerHidSpecialLightingKind.AvneraChroma;
            var protocol40 = definition?.Kind == RazerHidSpecialLightingKind.Protocol40Chroma;
            var endpoint = group.FirstOrDefault(item => item.Access == DeviceAccessState.Available &&
                (krakenV4
                    ? item.UsagePage == 0xFF14 && item.Usage == 0x0001 &&
                      item.OutputReportByteLength == RazerHidKrakenV4Protocol.ReportLength
                    : avnera
                        ? item.FeatureReportByteLength == RazerAvnera65Protocol.ReadLength
                    : protocol40
                        ? item.FeatureReportByteLength == RazerProtocol40HeadsetProtocol.ReportLength
                    : !argb &&
                      item.DevicePath.Contains("&MI_03", StringComparison.OrdinalIgnoreCase) &&
                      item.OutputReportByteLength == (definition!.Kind ==
                          RazerHidSpecialLightingKind.KrystalT2
                              ? RazerKrystalT2LightingProtocol.OutputReportLength
                              : RazerHidKrakenProtocol.ReportLength)));
            var name = argb
                ? "Razer Chroma Addressable RGB Controller"
                : definition!.Name;
            var controlDevicePath = argb
                ? await RazerFilterEndpointDiscovery.DiscoverAsync(ArgbProductId, cancellationToken)
                    .ConfigureAwait(false)
                : null;
            var featureDevicePath = argb
                ? group.FirstOrDefault(item =>
                    item.Access == DeviceAccessState.Available &&
                    item.FeatureReportByteLength == RazerFeatureReport.Length)?.DevicePath
                : null;
            var error = argb && controlDevicePath is null
                ? "ARGB needs Razer's 0F1F control driver (PID0f1fDrv) to send the 320-byte SET_FEATURE frame; its RZCONTROL endpoint is not installed."
                : !argb && endpoint is null
                    ? krakenV4
                        ? "No available FF14:0001 64-byte Output Report collection was found."
                        : avnera
                            ? "No available 65-byte Avnera feature collection was found."
                        : protocol40
                            ? "No available 64-byte rzDevice40 feature collection was found."
                            : $"No available MI_03 {(definition!.Kind == RazerHidSpecialLightingKind.KrystalT2 ? 62 : 37)}-byte Output Report collection was found."
                    : null;
            // The audio page only finds a headset whose bundle channel has a matching
            // interface. The Kraken V4 channel is not handled there, so its lighting
            // card has to stay on its own row or the headset disappears entirely.
            var mergedIntoAudioPage = !argb && IsMergedIntoAudioPage(
                group.Key.ProductId,
                HasAudioChannelInterface(group.Key.ProductId, group));
            results.Add(new RazerHidSpecialLightingConnection(
                group.Key.ProductId,
                name,
                argb ? RazerHidSpecialLightingKind.Argb320 : definition!.Kind,
                group.Key.PhysicalDeviceKey,
                endpoint?.DevicePath,
                controlDevicePath,
                featureDevicePath,
                argb ? Effects(RazerHidLightingEffect.Custom) : definition!.Effects,
                !argb && definition!.WritesColor,
                mergedIntoAudioPage,
                error));
        }
        return results;
    }

    public Task SetArgbFrameAsync(
        RazerHidSpecialLightingConnection connection,
        byte channel,
        IReadOnlyList<RazerHidColor> colors,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection, RazerHidSpecialLightingKind.Argb320);
        var frame = RazerHidArgbFrame.Create(channel, colors);
        return RazerFilterControlTransport
            .SetFeatureAsync(connection.ControlDevicePath!, frame, cancellationToken)
            .AsTask();
    }

    /// <summary>
    /// Switches the controller to the custom-frame effect on its 91-byte feature
    /// endpoint before the router starts writing 320-byte channel frames.
    /// </summary>
    public async Task SetArgbCustomFrameModeAsync(
        RazerHidSpecialLightingConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection, RazerHidSpecialLightingKind.Argb320);
        if (connection.FeatureDevicePath is not { } path)
        {
            throw new InvalidOperationException(
                "The ARGB controller did not expose its 91-byte Chroma feature interface.");
        }

        var service = new RazerChromaStandardService(
            new RazerFeatureTransport(),
            path,
            TimeSpan.FromMilliseconds(20),
            _diagnostic);
        await service.SetCustomFrameModeAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SetKrakenV4LightingAsync(
        RazerHidSpecialLightingConnection connection,
        RazerHidLightingSettings settings,
        byte? intensity,
        byte? brightness,
        CancellationToken cancellationToken)
    {
        RequireReady(connection, RazerHidSpecialLightingKind.KrakenV4);
        if (!connection.SupportedEffects.Contains(settings.Effect))
        {
            throw new NotSupportedException(
                $"RazerHid Kraken V4 1532:{connection.ProductId:X4} does not support {settings.Effect}.");
        }

        // The Billie/Mabel dongle classes set isDongleCommand=false in the bundle,
        // so their lighting frames carry the 0x80 direction byte. The wired classes
        // and AudioValkyrieDongle keep it at zero.
        var isDongle = connection.ProductId is 0x056C or 0x0574 or 0x0588;
        var device = KrakenDevices[connection.ProductId];
        IReadOnlyList<byte[]> reports = settings.Effect switch
        {
            RazerHidLightingEffect.Off => RazerHidKrakenV4Protocol.CreateOff(
                isDongle, device.LedCount),
            RazerHidLightingEffect.Static => RazerHidKrakenV4Protocol.CreateStatic(
                settings.Primary, intensity, isDongle, device.LedCount),
            RazerHidLightingEffect.Wave => RazerHidKrakenV4Protocol.CreateWave(intensity, isDongle),
            RazerHidLightingEffect.BreathingSingle => RazerHidKrakenV4Protocol.CreateBreathing(
                settings.Primary, intensity, isDongle),
            RazerHidLightingEffect.Spectrum => RazerHidKrakenV4Protocol.CreateSpectrum(isDongle),
            RazerHidLightingEffect.StarlightRandom => RazerHidKrakenV4Protocol.CreateStarlight(isDongle),
            RazerHidLightingEffect.Fire => RazerHidKrakenV4Protocol.CreateFire(isDongle),
            RazerHidLightingEffect.AudioMeter => RazerHidKrakenV4Protocol.CreateAudioMeter(isDongle),
            RazerHidLightingEffect.Wheel => RazerHidKrakenV4Protocol.CreateWheel(isDongle),
            RazerHidLightingEffect.Custom => RazerHidKrakenV4Protocol.CreateCustomFrame(
                settings.Primary, device.LedCount, intensity, isDongle),
            _ => throw new NotSupportedException(),
        };

        var gate = _gates.GetOrAdd(connection.InstanceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var devicePath = connection.DevicePath!;
            var arm = !_krakenV4Armed.ContainsKey(devicePath);
            var percent = brightness ??
                (_krakenV4Brightness.TryGetValue(devicePath, out var known) ? known : (byte)100);
            var resendBrightness = !arm &&
                _krakenV4Brightness.TryGetValue(devicePath, out var sent) && sent != percent;
            var reportsToSend = new List<byte[]>(reports.Count + (arm ? 3 : resendBrightness ? 1 : 0));
            if (arm)
            {
                // Re-taking Chroma control on every click makes 056B fade to the
                // old colour, so the handshake only runs once per device path.
                // AudioMabelT3CDongle (0574) has no 02 00 00 connect step in its
                // bundle; sending it makes the headset answer 0x05 and drop.
                if (connection.ProductId != 0x0574)
                {
                    reportsToSend.Add(RazerHidKrakenV4Protocol.CreateConnect(isDongle));
                }
                reportsToSend.Add(RazerHidKrakenV4Protocol.CreateForceChroma(enabled: true, isDongle));
                reportsToSend.Add(RazerHidKrakenV4Protocol.CreateBrightness(percent, isDongle));
            }
            else if (resendBrightness)
            {
                reportsToSend.Add(RazerHidKrakenV4Protocol.CreateBrightness(percent, isDongle));
            }

            reportsToSend.AddRange(reports);
            foreach (var report in reportsToSend)
            {
                _diagnostic?.Invoke(
                    $"kraken lighting 1532:{connection.ProductId:X4} effect={settings.Effect} " +
                    $"report=[{Convert.ToHexString(report)}]");
            }

            try
            {
                await _sendKrakenV4(
                        devicePath,
                        reportsToSend,
                        arm,
                        KrakenV4UsesSetOutputReport(connection.ProductId),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // A failed or cancelled send leaves the takeover state unknown.
                _krakenV4Armed.TryRemove(devicePath, out _);
                throw;
            }

            if (arm)
            {
                _krakenV4Armed.TryAdd(devicePath, 0);
            }
            _krakenV4Brightness[devicePath] = percent;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Billie T2 writes through HidD_SetOutputReport and answers every general
    /// command. The old WriteFile-only path never drained those replies, so the
    /// firmware stopped accepting the static frame. AudioMabelT3C instead uses hid.write.
    /// </summary>
    private async Task SendKrakenV4Async(
        string devicePath,
        IReadOnlyList<byte[]> reports,
        bool sendHandshake,
        bool useSetOutputReport,
        CancellationToken cancellationToken)
    {
        // Synapse keeps one HID handle open for the lighting session. 056B drops
        // host control when that handle closes, so a handle per click makes later
        // effects no-ops even though the firmware still ACKs them.
        if (!_krakenV4Transports.TryGetValue(devicePath, out var transport))
        {
            var created = new RazerMxicHeadsetTransport(
                devicePath,
                RazerHidKrakenV4Protocol.ReportLength,
                useSetOutputReport,
                useControlStatusReport: false);
            if (_krakenV4Transports.TryAdd(devicePath, created))
            {
                transport = created;
                _diagnostic?.Invoke("kraken lighting session=open");
            }
            else
            {
                await created.DisposeAsync().ConfigureAwait(false);
                transport = _krakenV4Transports[devicePath];
                _diagnostic?.Invoke("kraken lighting session=reuse");
            }
        }
        else
        {
            _diagnostic?.Invoke("kraken lighting session=reuse");
        }

        try
        {
            // AudioMabelT3CDongle answers the force-chroma command but not the
            // brightness or effect frames; waiting on those only stalls the write.
            var waitForEffectResponse =
                !devicePath.Contains("pid_0574", StringComparison.OrdinalIgnoreCase);
            // 0574 has no connect frame, so its first report is force-chroma and
            // must still have its reply drained.
            var firstIsConnect = reports.Count > 0 &&
                reports[0].Length == RazerHidKrakenV4Protocol.ReportLength &&
                reports[0][7] == 0x00 && reports[0][8] == 0x00 &&
                reports[0][10] == 0x02 && reports[0][11] == 0x00 && reports[0][12] == 0x00;
            for (var index = 0; index < reports.Count; index++)
            {
                var report = reports[index];
                if (sendHandshake && index == 0 && firstIsConnect)
                {
                    await transport.SendAsync(report, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await SendKrakenV4ReportAsync(
                        transport, report, waitForEffectResponse, cancellationToken)
                    .ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
            }

            if (sendHandshake)
            {
                // The takeover animation drops the first effect; repeat the frame
                // and effect pair on the same session so the first click sticks.
                await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken).ConfigureAwait(false);
                _diagnostic?.Invoke("kraken lighting retry=1");
                foreach (var report in reports.Skip(Math.Max(0, reports.Count - 2)))
                {
                    await SendKrakenV4ReportAsync(
                            transport, report, waitForEffectResponse, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch
        {
            if (_krakenV4Transports.TryRemove(devicePath, out var stale))
            {
                _diagnostic?.Invoke("kraken lighting session=reset");
                await stale.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task SendKrakenV4ReportAsync(
        RazerMxicHeadsetTransport transport,
        byte[] report,
        bool waitForEffectResponse,
        CancellationToken cancellationToken)
    {
        if (RazerHidKrakenV4Protocol.ExpectsResponse(report) &&
            (waitForEffectResponse || report[10] == 0xE8))
        {
            var response = await transport.QueryRawAsync(
                report, data => RazerHidKrakenV4Protocol.IsCommandResponse(report, data), cancellationToken)
                .ConfigureAwait(false);
            _diagnostic?.Invoke(response is null
                ? "kraken lighting response=none"
                : $"kraken lighting response status=0x{response[1]:X2} report=[{Convert.ToHexString(response)}]");
            return;
        }

        await transport.SendAsync(report, cancellationToken).ConfigureAwait(false);
    }

    private static void RequireReady(
        RazerHidSpecialLightingConnection connection,
        RazerHidSpecialLightingKind expectedKind)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.Kind != expectedKind)
        {
            throw new ArgumentException("The special lighting connection uses a different transport family.", nameof(connection));
        }
        if (!connection.IsReady)
        {
            throw new InvalidOperationException(connection.Error ?? "The special lighting endpoint is not resolved.");
        }
    }

    private sealed record KrakenDefinition(
        string Name,
        RazerHidKrakenLayout Layout,
        FrozenSet<RazerHidLightingEffect> Effects,
        bool WritesColor,
        RazerHidSpecialLightingKind Kind = RazerHidSpecialLightingKind.Kraken37,
        int LedCount = 0);
}
