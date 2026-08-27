using System.Collections.Concurrent;
using System.Collections.Frozen;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public enum OpenRazerSpecialLightingKind
{
    Argb320,
    Kraken37,
}

public sealed class OpenRazerSpecialLightingConnection
{
    internal OpenRazerSpecialLightingConnection(
        ushort productId,
        string displayName,
        OpenRazerSpecialLightingKind kind,
        string instanceId,
        string? devicePath,
        IReadOnlySet<OpenRazerLightingEffect> effects,
        string? error)
    {
        ProductId = productId;
        DisplayName = displayName;
        Kind = kind;
        InstanceId = instanceId;
        DevicePath = devicePath;
        SupportedEffects = effects;
        Error = error;
    }

    public ushort ProductId { get; }
    public string DisplayName { get; }
    public OpenRazerSpecialLightingKind Kind { get; }
    public string InstanceId { get; }
    public IReadOnlySet<OpenRazerLightingEffect> SupportedEffects { get; }
    public string? Error { get; }
    public bool IsReady => DevicePath is not null;
    internal string? DevicePath { get; }
}

public sealed class OpenRazerSpecialLightingService
{
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
        }.ToFrozenDictionary();

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
            var endpoint = group.FirstOrDefault(item => item.Access == DeviceAccessState.Available &&
                !argb &&
                item.DevicePath.Contains("&MI_03", StringComparison.OrdinalIgnoreCase) &&
                item.OutputReportByteLength == OpenRazerKrakenProtocol.ReportLength);
            var name = argb
                ? "Razer Chroma Addressable RGB Controller"
                : KrakenDevices[group.Key.ProductId].Name;
            results.Add(new OpenRazerSpecialLightingConnection(
                group.Key.ProductId,
                name,
                argb ? OpenRazerSpecialLightingKind.Argb320 : OpenRazerSpecialLightingKind.Kraken37,
                group.Key.PhysicalDeviceKey,
                endpoint?.DevicePath,
                argb ? Effects(OpenRazerLightingEffect.Custom) : KrakenDevices[group.Key.ProductId].Effects,
                endpoint is null
                    ? argb
                        ? "ARGB requires a USB control transfer with wValue 0x0300 and wIndex 1; Windows HID transport is not supported."
                        : "No available MI_03 37-byte Output Report collection was found."
                    : null));
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
        _ = channel;
        _ = colors;
        _ = cancellationToken;
        throw new PlatformNotSupportedException(
            "ARGB requires an arbitrary USB control transfer and cannot be sent through HidD_SetFeature.");
    }

    public async Task SetKrakenLightingAsync(
        OpenRazerSpecialLightingConnection connection,
        OpenRazerLightingSettings settings,
        byte? intensity = null,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection, OpenRazerSpecialLightingKind.Kraken37);
        ArgumentNullException.ThrowIfNull(settings);
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
        bool WritesColor);
}
