using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerChromaDeviceConnection(
    string DevicePath,
    ushort ProductId,
    string Name,
    DeviceCategory Category);

public sealed record RazerChromaStandardState(
    byte? BrightnessPercent,
    RazerChromaEffect? Effect,
    IReadOnlyList<byte> Colors,
    IReadOnlyList<string> Errors);

/// <summary>
/// Brightness and effect control over the class 0x10 Chroma channel shared by mice,
/// keyboards, lamps, ARGB controllers and docks that the OpenRazer catalogue does not cover.
/// </summary>
public sealed class RazerChromaStandardService
{
    private readonly IRazerFeatureTransport _transport;
    private readonly string _devicePath;
    private readonly TimeSpan _deviceWait;
    private readonly Action<string>? _diagnostic;
    private byte _transactionId = 0x1F;

    internal RazerChromaStandardService(
        IRazerFeatureTransport transport,
        string devicePath,
        TimeSpan deviceWait,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _devicePath = string.IsNullOrWhiteSpace(devicePath)
            ? throw new ArgumentException("A HID device path is required.", nameof(devicePath))
            : devicePath;
        _deviceWait = deviceWait;
        _diagnostic = diagnostic;
    }

    /// <summary>
    /// Devices whose only shared capability is the class 0x10 Chroma channel: mice, lamps,
    /// ARGB controllers, docks and stands. Keyboards, audio devices, the OpenRazer catalogue
    /// and the 320-byte ARGB controller are excluded because they own a richer page.
    /// </summary>
    public static readonly IReadOnlyDictionary<ushort, (string Name, DeviceCategory Category)> Products =
        new Dictionary<ushort, (string, DeviceCategory)>
        {
            [0x0063] = ("Razer Jugan", DeviceCategory.Mouse),
            [0x0069] = ("Razer Mamba Hyperflux", DeviceCategory.Mouse),
            [0x0074] = ("Razer Abyssus Lite", DeviceCategory.Mouse),
            [0x0075] = ("Razer Turret Mouse Xbox One Edition", DeviceCategory.Mouse),
            [0x0093] = ("Razer Naga Classic Edition", DeviceCategory.Mouse),
            [0x00B8] = ("Razer Viper V3 HyperSpeed", DeviceCategory.Mouse),
            [0x00CF] = ("Razer HyperFlux V2", DeviceCategory.Mouse),
            [0x00DD] = ("Razer Krakoff", DeviceCategory.Mouse),
            [0x00E0] = ("Razer Orochi V2", DeviceCategory.Mouse),
            [0x00E2] = ("Razer Basilisk V4 Pro", DeviceCategory.Mouse),
            [0x00E5] = ("Razer Viper V4 Pro", DeviceCategory.Mouse),
            [0x00E7] = ("Razer Naga V3 Pro", DeviceCategory.Mouse),
            [0x00EB] = ("Razer Basilisk V4 HyperSpeed", DeviceCategory.Mouse),
            [0x00EF] = ("Razer DeathAdder V4 Pro Carbon Fiber Edition", DeviceCategory.Mouse),
            [0x00F1] = ("Razer Mouse Dock V2 Pro", DeviceCategory.Accessory),
            [0x030A] = ("Razer Chroma Addressable RGB Controller", DeviceCategory.Accessory),
            [0x030C] = ("Razer Key Light Chroma", DeviceCategory.Accessory),
            [0x030D] = ("Razer Aether Lamp Pro", DeviceCategory.Accessory),
            [0x030E] = ("Razer Aether Lamp", DeviceCategory.Accessory),
            [0x030F] = ("Razer Aether Light Bulb", DeviceCategory.Accessory),
            [0x0310] = ("Razer Aether Light Strip", DeviceCategory.Accessory),
            [0x0316] = ("Razer Aether Monitor Light Bar", DeviceCategory.Accessory),
            [0x0317] = ("Razer Aether Standing Light Bars", DeviceCategory.Accessory),
            [0x0F27] = ("Tomahawk Gaming Desktop", DeviceCategory.Accessory),
            [0x0F28] = ("Razer Raptor 27", DeviceCategory.Monitor),
            [0x0F2C] = ("Razer Chroma Wireless ARGB Controller", DeviceCategory.Accessory),
            [0x0F35] = ("Razer Hanbo AIO", DeviceCategory.Accessory),
            [0x0F36] = ("Razer Head Cushion Chroma", DeviceCategory.Accessory),
            [0x0F3C] = ("Razer PWM PC Fan Controller", DeviceCategory.Accessory),
            [0x0F45] = ("Esther", DeviceCategory.Accessory),
            [0x0F51] = ("Razer Core X V2", DeviceCategory.Accessory),
            [0x0F52] = ("Razer Thunderbolt 5 Dock Chroma", DeviceCategory.Accessory),
            [0x0F59] = ("Razer Monitor Stand Chroma", DeviceCategory.Accessory),
            [0x0F5A] = ("Razer Laptop Monitor Stand Chroma", DeviceCategory.Accessory),
            [0x0F5C] = ("Razer Handheld Dock Chroma", DeviceCategory.Accessory),
            [0x0F64] = ("Razer Soma Chroma", DeviceCategory.Accessory),
            [0x0566] = ("Razer Wireless Control Pod", DeviceCategory.Accessory),
            [0x0F6A] = ("Razer Base Station V3 Chroma", DeviceCategory.Accessory),
            [0x0F6D] = ("MarciT2H", DeviceCategory.Accessory),
        };

    public static bool IsSupported(ushort productId) => Products.ContainsKey(productId);

    public static async Task<RazerChromaDeviceConnection?> FindAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorFeatureInterfacesAsync(
            0x1532,
            RazerFeatureReport.Length,
            cancellationToken).ConfigureAwait(false);

        var candidates = interfaces
            .Where(item => Products.ContainsKey(item.ProductId))
            .Where(item => OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, item.ProductId) is null)
            .Where(item => !RazerHeadsetAudioService.IsSupported(item.ProductId))
            .Where(item => !RazerKeyboardService.IsSupported(item.ProductId))
            .OrderByDescending(item => item.Access == DeviceAccessState.Available)
            .ThenBy(item => item.ProductId)
            .ToArray();
        if (candidates.Length == 0)
        {
            diagnostic?.Invoke(
                "chroma found no admitted 91-byte interface; " +
                $"enumerated={string.Join(",", interfaces.Select(item => $"{item.ProductId:X4}"))}");
            return null;
        }

        var chosen = candidates[0];
        var (name, category) = Products[chosen.ProductId];
        diagnostic?.Invoke(
            $"chroma selected pid={chosen.ProductId:X4} path={RazerFeatureTransport.DescribeDevicePath(chosen.DevicePath)} " +
            $"usage=0x{chosen.UsagePage:X4}:0x{chosen.Usage:X4} access={chosen.Access}");
        return new RazerChromaDeviceConnection(
            chosen.DevicePath, chosen.ProductId, name, category);
    }

    public async Task<RazerChromaStandardState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        byte? brightness = null;
        try
        {
            brightness = RazerChromaStandardProtocol.ReadBrightnessPercent(
                await QueryAsync(RazerChromaStandardProtocol.GetBrightness, "read brightness", cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"brightness: {exception.Message}");
        }

        RazerChromaEffect? effect = null;
        IReadOnlyList<byte> colors = [];
        try
        {
            var current = RazerChromaStandardProtocol.ReadEffect(
                await QueryAsync(RazerChromaStandardProtocol.GetEffect, "read effect", cancellationToken)
                    .ConfigureAwait(false));
            effect = current.Effect;
            colors = current.Colors;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"effect: {exception.Message}");
        }

        return new RazerChromaStandardState(brightness, effect, colors, errors);
    }

    public async Task SetBrightnessAsync(byte percent, CancellationToken cancellationToken = default)
    {
        if (percent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), "Brightness must be between 0 and 100.");
        }

        var previous = RazerChromaStandardProtocol.ReadBrightnessPercent(
            await QueryAsync(RazerChromaStandardProtocol.GetBrightness, "read brightness", cancellationToken)
                .ConfigureAwait(false));
        if (previous == percent)
        {
            return;
        }

        try
        {
            await QueryAsync(
                transaction => RazerChromaStandardProtocol.SetBrightness(
                    transaction, RazerChromaStandardProtocol.ToBrightnessLevel(percent)),
                "set brightness",
                cancellationToken).ConfigureAwait(false);
            var actual = RazerChromaStandardProtocol.ReadBrightnessPercent(
                await QueryAsync(RazerChromaStandardProtocol.GetBrightness, "read back brightness", cancellationToken)
                    .ConfigureAwait(false));
            if (Math.Abs(actual - percent) > 1)
            {
                throw new InvalidOperationException($"Brightness readback was {actual}%, expected {percent}%.");
            }
        }
        catch
        {
            await TryRestoreAsync(
                transaction => RazerChromaStandardProtocol.SetBrightness(
                    transaction, RazerChromaStandardProtocol.ToBrightnessLevel(previous)),
                "restore brightness").ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetEffectAsync(
        RazerChromaEffect effect,
        IReadOnlyList<byte> colors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (!Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }

        var payload = colors.ToArray();
        var previous = RazerChromaStandardProtocol.ReadEffect(
            await QueryAsync(RazerChromaStandardProtocol.GetEffect, "read effect", cancellationToken)
                .ConfigureAwait(false));
        if (previous.Effect == effect && previous.Colors.SequenceEqual(payload))
        {
            return;
        }

        try
        {
            await QueryAsync(
                transaction => RazerChromaStandardProtocol.SetEffect(transaction, effect, payload),
                "set effect",
                cancellationToken).ConfigureAwait(false);
            var actual = RazerChromaStandardProtocol.ReadEffect(
                await QueryAsync(RazerChromaStandardProtocol.GetEffect, "read back effect", cancellationToken)
                    .ConfigureAwait(false));
            if (actual.Effect != effect)
            {
                throw new InvalidOperationException(
                    $"Effect readback was {actual.Effect}, expected {effect}.");
            }
        }
        catch
        {
            await TryRestoreAsync(
                transaction => RazerChromaStandardProtocol.SetEffect(transaction, previous.Effect, previous.Colors.ToArray()),
                "restore effect").ConfigureAwait(false);
            throw;
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
            _diagnostic?.Invoke($"chroma {label} failed: {exception.Message}");
        }
    }

    private async Task<byte[]> QueryAsync(
        Func<byte, byte[]> request,
        string label,
        CancellationToken cancellationToken)
    {
        var report = request(NextTransactionId());
        _diagnostic?.Invoke(
            $"chroma {label}: size={report[6]}, class=0x{report[7]:X2}, command=0x{report[8]:X2}");
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
