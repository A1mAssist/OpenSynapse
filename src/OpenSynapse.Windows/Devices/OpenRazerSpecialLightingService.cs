using System.Collections.Concurrent;
using System.Collections.Frozen;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public enum OpenRazerSpecialLightingKind
{
    Argb320,
    Kraken37,
    KrakenV4,
}

public sealed class OpenRazerSpecialLightingConnection
{
    internal OpenRazerSpecialLightingConnection(
        ushort productId,
        string displayName,
        OpenRazerSpecialLightingKind kind,
        string instanceId,
        string? devicePath,
        string? controlDevicePath,
        IReadOnlySet<OpenRazerLightingEffect> effects,
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
        SupportedEffects = effects;
        SupportsStaticColor = supportsStaticColor;
        IsMergedIntoAudioPage = isMergedIntoAudioPage;
        Error = error;
    }

    public ushort ProductId { get; }
    public string DisplayName { get; }
    public OpenRazerSpecialLightingKind Kind { get; }
    public string InstanceId { get; }
    public IReadOnlySet<OpenRazerLightingEffect> SupportedEffects { get; }
    public bool SupportsStaticColor { get; }
    /// <summary>
    /// True when the audio page owns this headset and shows the lighting card there.
    /// The audio page only discovers a headset that exposes the 91-byte feature
    /// interface, so a headset without one keeps its own lighting row.
    /// </summary>
    public bool IsMergedIntoAudioPage { get; }
    public string? Error { get; }
    public bool IsReady => DevicePath is not null || ControlDevicePath is not null;
    internal string? DevicePath { get; }
    internal string? ControlDevicePath { get; }
}

public sealed class OpenRazerSpecialLightingService
{
    public Task<string?> TryReadSerialAsync(
        OpenRazerSpecialLightingConnection connection,
        CancellationToken cancellationToken = default) => connection.DevicePath is { } path
            ? WindowsHidDiscovery.TryReadSerialNumberAsync(path, cancellationToken)
            : Task.FromResult<string?>(null);

    private const ushort ArgbProductId = 0x0F1F;
    private static readonly FrozenDictionary<ushort, KrakenDefinition> KrakenDevices =
        new Dictionary<ushort, KrakenDefinition>
        {
            [0x0501] = new("Razer Kraken 7.1", OpenRazerKrakenLayout.Rainie,
                Effects(OpenRazerLightingEffect.Off, OpenRazerLightingEffect.Static), false),
            [0x0504] = new("Razer Kraken 7.1 Chroma", OpenRazerKrakenLayout.Rainie,
                Effects(OpenRazerLightingEffect.Off, OpenRazerLightingEffect.Static,
                    OpenRazerLightingEffect.Spectrum, OpenRazerLightingEffect.BreathingSingle,
                    OpenRazerLightingEffect.Custom), true),
            [0x0506] = new("Razer Kraken 7.1", OpenRazerKrakenLayout.Rainie,
                Effects(OpenRazerLightingEffect.Off, OpenRazerLightingEffect.Static), false),
            [0x0510] = Kylie("Razer Kraken 7.1 V2"),
            [0x0520] = Kylie("Razer Kraken Tournament Edition"),
            [0x0527] = Kylie("Razer Kraken Ultimate"),
            [0x0560] = Kylie("Razer Kraken Kitty V2"),
            // OpenRGB's merged wired implementation validates the same 64-byte,
            // report-0x02 protocol on the Kraken V4 cable. The 2.4 GHz receiver is
            // deliberately not exposed until that path is validated.
            [0x056B] = new(
                "Razer Kraken V4 (Wired)",
                OpenRazerKrakenLayout.Kylie,
                Effects(OpenRazerLightingEffect.Off, OpenRazerLightingEffect.Static, OpenRazerLightingEffect.Wave),
                true,
                OpenRazerSpecialLightingKind.KrakenV4),
        }.ToFrozenDictionary();

    /// <summary>
    /// Headsets that already own an audio page keep their 37-byte lighting there, so the
    /// lighting discovery must not emit a second, lighting-only row for the same device.
    /// </summary>
    /// <remarks>
    /// The audio page only discovers a headset that exposes the 91-byte feature
    /// interface, so the lighting card may only move there for a headset that has one.
    /// A headset without that interface keeps its own lighting row; otherwise it would
    /// vanish from the app, because neither page could show it.
    /// </remarks>
    public static bool IsMergedIntoAudioPage(ushort productId, bool hasAudioFeatureInterface) =>
        hasAudioFeatureInterface && RazerHeadsetAudioService.IsSupported(productId);

    private readonly OpenRazerHidReportTransport _transport = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<OpenRazerSpecialLightingConnection>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorInterfacesAsync(
            OpenRazerDeviceDefinition.VendorId, cancellationToken).ConfigureAwait(false);
        var results = new List<OpenRazerSpecialLightingConnection>();
        foreach (var group in interfaces
            .Where(item => item.ProductId == ArgbProductId || KrakenDevices.ContainsKey(item.ProductId))
            .GroupBy(item => (item.ProductId, item.PhysicalDeviceKey)))
        {
            var argb = group.Key.ProductId == ArgbProductId;
            var definition = argb ? null : KrakenDevices[group.Key.ProductId];
            var krakenV4 = definition?.Kind == OpenRazerSpecialLightingKind.KrakenV4;
            var endpoint = group.FirstOrDefault(item => item.Access == DeviceAccessState.Available &&
                (krakenV4
                    ? item.UsagePage == 0xFF14 && item.Usage == 0x0001 &&
                      item.OutputReportByteLength == OpenRazerKrakenV4Protocol.ReportLength
                    : !argb &&
                      item.DevicePath.Contains("&MI_03", StringComparison.OrdinalIgnoreCase) &&
                      item.OutputReportByteLength == OpenRazerKrakenProtocol.ReportLength));
            var name = argb
                ? "Razer Chroma Addressable RGB Controller"
                : definition!.Name;
            var controlDevicePath = argb
                ? await RazerFilterEndpointDiscovery.DiscoverAsync(ArgbProductId, cancellationToken)
                    .ConfigureAwait(false)
                : null;
            var error = argb && controlDevicePath is null
                ? "ARGB needs Razer's 0F1F control driver (PID0f1fDrv) to send the 320-byte SET_FEATURE frame; its RZCONTROL endpoint is not installed."
                : !argb && endpoint is null
                    ? krakenV4
                        ? "No available FF14:0001 64-byte Output Report collection was found."
                        : "No available MI_03 37-byte Output Report collection was found."
                    : null;
            // The audio page only finds a headset that exposes the 91-byte feature
            // interface. The Kraken V4 exposes only a 64-byte FF14 collection, so the
            // audio page never selects it and the lighting card has to stay on its
            // own row, otherwise the headset disappears from the app entirely.
            var mergedIntoAudioPage = !argb && IsMergedIntoAudioPage(
                group.Key.ProductId,
                group.Any(item => item.FeatureReportByteLength == RazerFeatureReport.Length));
            results.Add(new OpenRazerSpecialLightingConnection(
                group.Key.ProductId,
                name,
                argb ? OpenRazerSpecialLightingKind.Argb320 : definition!.Kind,
                group.Key.PhysicalDeviceKey,
                endpoint?.DevicePath,
                controlDevicePath,
                argb ? Effects(OpenRazerLightingEffect.Custom) : definition!.Effects,
                !argb && definition!.WritesColor,
                mergedIntoAudioPage,
                error));
        }
        return results;
    }

    public Task SetArgbFrameAsync(
        OpenRazerSpecialLightingConnection connection,
        byte channel,
        IReadOnlyList<OpenRazerColor> colors,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection, OpenRazerSpecialLightingKind.Argb320);
        var frame = OpenRazerArgbFrame.Create(channel, colors);
        return RazerFilterControlTransport
            .SetFeatureAsync(connection.ControlDevicePath!, frame, cancellationToken)
            .AsTask();
    }

    public async Task SetKrakenLightingAsync(
        OpenRazerSpecialLightingConnection connection,
        OpenRazerLightingSettings settings,
        byte? intensity = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (connection.Kind == OpenRazerSpecialLightingKind.KrakenV4)
        {
            await SetKrakenV4LightingAsync(connection, settings, intensity, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        RequireReady(connection, OpenRazerSpecialLightingKind.Kraken37);
        if (!connection.SupportedEffects.Contains(settings.Effect))
        {
            throw new NotSupportedException(
                $"OpenRazer Kraken 1532:{connection.ProductId:X4} does not support {settings.Effect}.");
        }

        var device = KrakenDevices[connection.ProductId];
        var reports = settings.Effect switch
        {
            OpenRazerLightingEffect.Off => OpenRazerKrakenProtocol.CreateOff(device.Layout),
            OpenRazerLightingEffect.Static => OpenRazerKrakenProtocol.CreateStatic(
                device.Layout, settings.Primary, intensity, device.WritesColor),
            OpenRazerLightingEffect.Spectrum => OpenRazerKrakenProtocol.CreateSpectrum(device.Layout),
            OpenRazerLightingEffect.BreathingSingle => OpenRazerKrakenProtocol.CreateBreathing(
                device.Layout, [settings.Primary]),
            OpenRazerLightingEffect.BreathingDual => OpenRazerKrakenProtocol.CreateBreathing(
                device.Layout, [settings.Primary, settings.Secondary]),
            OpenRazerLightingEffect.BreathingTriple => OpenRazerKrakenProtocol.CreateBreathing(
                device.Layout, [settings.Primary, settings.Secondary, settings.Tertiary]),
            OpenRazerLightingEffect.Custom => OpenRazerKrakenProtocol.CreateCustom(
                device.Layout, settings.Primary, intensity),
            _ => throw new NotSupportedException(),
        };

        var gate = _gates.GetOrAdd(connection.InstanceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var report in reports)
            {
                await _transport.SendOutputAsync(connection.DevicePath!, report, cancellationToken).ConfigureAwait(false);
                if (settings.Effect != OpenRazerLightingEffect.Custom)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(report[2] * 15), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SetKrakenV4LightingAsync(
        OpenRazerSpecialLightingConnection connection,
        OpenRazerLightingSettings settings,
        byte? intensity,
        CancellationToken cancellationToken)
    {
        RequireReady(connection, OpenRazerSpecialLightingKind.KrakenV4);
        if (!connection.SupportedEffects.Contains(settings.Effect))
        {
            throw new NotSupportedException(
                $"OpenRazer Kraken V4 1532:{connection.ProductId:X4} does not support {settings.Effect}.");
        }

        IReadOnlyList<byte[]> reports = settings.Effect switch
        {
            OpenRazerLightingEffect.Off => OpenRazerKrakenV4Protocol.CreateOff(),
            OpenRazerLightingEffect.Static => OpenRazerKrakenV4Protocol.CreateStatic(settings.Primary, intensity),
            OpenRazerLightingEffect.Wave => OpenRazerKrakenV4Protocol.CreateWave(intensity),
            _ => throw new NotSupportedException(),
        };

        var gate = _gates.GetOrAdd(connection.InstanceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var report in reports)
            {
                await _transport.SendOutputAsync(connection.DevicePath!, report, cancellationToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static void RequireReady(
        OpenRazerSpecialLightingConnection connection,
        OpenRazerSpecialLightingKind expectedKind)
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

    private static FrozenSet<OpenRazerLightingEffect> Effects(params OpenRazerLightingEffect[] effects) =>
        effects.ToFrozenSet();

    private static KrakenDefinition Kylie(string name) => new(name, OpenRazerKrakenLayout.Kylie,
        Effects(OpenRazerLightingEffect.Off, OpenRazerLightingEffect.Static,
            OpenRazerLightingEffect.Spectrum, OpenRazerLightingEffect.BreathingSingle,
            OpenRazerLightingEffect.BreathingDual, OpenRazerLightingEffect.BreathingTriple,
            OpenRazerLightingEffect.Custom), true);

    private sealed record KrakenDefinition(
        string Name,
        OpenRazerKrakenLayout Layout,
        FrozenSet<OpenRazerLightingEffect> Effects,
        bool WritesColor,
        OpenRazerSpecialLightingKind Kind = OpenRazerSpecialLightingKind.Kraken37);
}
