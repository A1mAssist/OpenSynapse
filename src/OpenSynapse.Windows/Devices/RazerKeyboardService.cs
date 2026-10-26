using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerKeyboardConnection(
    string DevicePath,
    ushort ProductId,
    string Name,
    ushort FeatureReportByteLength,
    bool UsesLegacyChroma);

public sealed record RazerKeyboardState(
    Version? Firmware,
    string? Serial,
    byte? BatteryPercent,
    bool? Charging,
    bool? GameModeEnabled,
    int? PollingRate,
    IReadOnlyList<string> Errors);

/// <summary>
/// Settings the middleware exposes for the Huntsman V3 and Pro Type keyboards, which the
/// OpenRazer catalogue does not cover: game mode, polling period, battery and identity.
/// Each value is probed, so a model that does not answer a command simply hides that row.
/// </summary>
public sealed class RazerKeyboardService
{
    public static readonly IReadOnlyDictionary<ushort, string> Products =
        new Dictionary<ushort, string>
        {
            [0x02D0] = "Razer Huntsman V3 Pro Tenkeyless 8KHz",
            [0x02D1] = "Razer Huntsman V3 Pro Mini 8KHz",
            [0x02D8] = "Razer Huntsman Signature Edition",
            [0x02E4] = "Razer Huntsman V3 HE Magnetic Mini 65% 8KHz",
            [0x02E5] = "Razer Huntsman V3 Tenkeyless 8KHz",
            [0x02EA] = "Razer Huntsman V3 HE Magnetic Tenkeyless 8KHz",
            [0x0249] = "Razer Pro Type",
            [0x0251] = "Razer Pikachu Limited Edition Backlit Keyboard",
            [0x0277] = "Razer Pro Type Ultra",
            [0x028E] = "Razer Cynosapro",
            [0x02F0] = "Razer BlackWidow V4 75% Xbox 25th Anniversary Edition",
            [0x023E] = "Razer Turret Keyboard Xbox One Edition",
            [0x024F] = "Razer BlackWidow X Tenkeyless",
            [0x0250] = "Razer Huntsman Essential",
            [0x02B1] = "Razer Huntsman V3 X Tenkeyless",
            [0x02B3] = "Razer BlackWidow V4 Pro 75%",
            [0x02C4] = "Razer Pro Type Ergo",
            [0x02CC] = "Razer BlackWidow V4 Low-profile HyperSpeed",
            [0x02D4] = "Razer BlackWidow V4 Low-profile Tenkeyless HyperSpeed",
            [0x02E3] = "Razer Reclusa X Mini 65%",
            [0x02E6] = "Razer Huntsman V3 Pro Low-Profile Tenkeyless 8KHz",
            [0x02EB] = "Razer Tartarus V2 Pro",
            [0x02F4] = "Razer Reclusa Mini 65%",
        };

    /// <summary>
    /// Keyboards whose lighting runs on the older class 0x0F effect commands, which carry
    /// no brightness command. The rest of the catalogue uses class 0x10.
    /// </summary>
    public static readonly IReadOnlySet<ushort> LegacyChromaProducts = new HashSet<ushort>
    {
        0x023E, 0x024F, 0x0250, 0x02B1, 0x02B3, 0x02CC, 0x02D0, 0x02D1,
        0x02D4, 0x02D8, 0x02E3, 0x02E4, 0x02E5, 0x02EA, 0x02EB, 0x02F4,
    };

    public static bool UsesLegacyChroma(ushort productId) => LegacyChromaProducts.Contains(productId);

    private readonly IRazerFeatureTransport _transport;
    private readonly string _devicePath;
    private readonly TimeSpan _deviceWait;
    private readonly Action<string>? _diagnostic;
    private byte _transactionId = 0x1F;

    internal RazerKeyboardService(
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

    public static bool IsSupported(ushort productId) => Products.ContainsKey(productId);

    public static async Task<RazerKeyboardConnection?> FindAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorFeatureInterfacesAsync(
            0x1532,
            RazerKeyboardProtocol.ReportLength,
            cancellationToken).ConfigureAwait(false);

        var candidates = interfaces
            .Where(item => Products.ContainsKey(item.ProductId))
            // The OpenRazer catalogue already owns these PIDs and shows its own page.
            .Where(item => OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, item.ProductId) is null)
            .OrderByDescending(item => item.Access == DeviceAccessState.Available)
            .ThenBy(item => item.ProductId)
            .ToArray();
        if (candidates.Length == 0)
        {
            diagnostic?.Invoke(
                "keyboard found no admitted 91-byte interface; " +
                $"enumerated={string.Join(",", interfaces.Select(item => $"{item.ProductId:X4}"))}");
            return null;
        }

        var chosen = candidates[0];
        diagnostic?.Invoke(
            $"keyboard selected pid={chosen.ProductId:X4} path={RazerFeatureTransport.DescribeDevicePath(chosen.DevicePath)} " +
            $"usage=0x{chosen.UsagePage:X4}:0x{chosen.Usage:X4} access={chosen.Access}");
        return new RazerKeyboardConnection(
            chosen.DevicePath,
            chosen.ProductId,
            Products[chosen.ProductId],
            chosen.FeatureReportByteLength,
            UsesLegacyChroma(chosen.ProductId));
    }

    public async Task<RazerKeyboardState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var firmware = await ProbeAsync(
            "firmware", RazerKeyboardProtocol.GetExtendedFirmware, RazerKeyboardProtocol.ReadFirmware,
            errors, cancellationToken).ConfigureAwait(false);

        string? serial = null;
        try
        {
            serial = RazerKeyboardProtocol.ReadSerialNumber(
                await QueryAsync(RazerKeyboardProtocol.GetSerialNumber, "read serial", cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            try
            {
                serial = RazerKeyboardProtocol.ReadSerialNumber(
                    await QueryAsync(RazerKeyboardProtocol.GetSerialNumberAlt, "read serial (alt)", cancellationToken)
                        .ConfigureAwait(false));
            }
            catch (Exception fallback) when (IsExpected(fallback))
            {
                errors.Add($"serial: {exception.Message}; alt: {fallback.Message}");
            }
        }

        var battery = await ProbeAsync(
            "battery", RazerKeyboardProtocol.GetBatteryLevel, RazerKeyboardProtocol.ReadBatteryPercent,
            errors, cancellationToken).ConfigureAwait(false);
        var charging = await ProbeAsync(
            "charging", RazerKeyboardProtocol.GetChargingStatus, RazerKeyboardProtocol.ReadChargingStatus,
            errors, cancellationToken).ConfigureAwait(false);
        var gameMode = await ProbeAsync(
            "game mode", RazerKeyboardProtocol.GetGameModeState,
            response => RazerKeyboardProtocol.ReadGameModeState(response).GameMode != 0,
            errors, cancellationToken).ConfigureAwait(false);
        var polling = await ProbeAsync(
            "polling rate", RazerKeyboardProtocol.GetPollingRate, RazerKeyboardProtocol.ReadPollingRate,
            errors, cancellationToken).ConfigureAwait(false);

        return new RazerKeyboardState(
            firmware.Ok ? firmware.Value : null,
            string.IsNullOrWhiteSpace(serial) ? null : serial,
            battery.Ok ? battery.Value : null,
            charging.Ok ? charging.Value : null,
            gameMode.Ok ? gameMode.Value : null,
            polling.Ok ? polling.Value : null,
            errors);
    }

    public async Task SetGameModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var previous = RazerKeyboardProtocol.ReadGameModeState(
            await QueryAsync(RazerKeyboardProtocol.GetGameModeState, "read game mode", cancellationToken)
                .ConfigureAwait(false));
        var desired = enabled ? (byte)1 : (byte)0;
        if (previous.GameMode == desired)
        {
            return;
        }

        try
        {
            await QueryAsync(
                transaction => RazerKeyboardProtocol.SetGameModeState(
                    transaction, desired, previous.KeyCover, previous.Lifted),
                "set game mode",
                cancellationToken).ConfigureAwait(false);
            var actual = RazerKeyboardProtocol.ReadGameModeState(
                await QueryAsync(RazerKeyboardProtocol.GetGameModeState, "read back game mode", cancellationToken)
                    .ConfigureAwait(false));
            if (actual.GameMode != desired)
            {
                throw new InvalidOperationException(
                    $"Game mode readback was {actual.GameMode}, expected {desired}.");
            }
        }
        catch
        {
            await TryRestoreAsync(
                transaction => RazerKeyboardProtocol.SetGameModeState(
                    transaction, previous.GameMode, previous.KeyCover, previous.Lifted),
                "restore game mode").ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetPollingRateAsync(int hertz, CancellationToken cancellationToken = default)
    {
        var code = RazerKeyboardProtocol.PollingCode(hertz)
            ?? throw new ArgumentOutOfRangeException(nameof(hertz), "Unsupported polling rate.");
        var previous = RazerKeyboardProtocol.ReadPollingRate(
            await QueryAsync(RazerKeyboardProtocol.GetPollingRate, "read polling rate", cancellationToken)
                .ConfigureAwait(false));
        if (previous == hertz)
        {
            return;
        }

        try
        {
            await QueryAsync(
                transaction => RazerKeyboardProtocol.SetPollingRate(transaction, code),
                "set polling rate",
                cancellationToken).ConfigureAwait(false);
            var actual = RazerKeyboardProtocol.ReadPollingRate(
                await QueryAsync(RazerKeyboardProtocol.GetPollingRate, "read back polling rate", cancellationToken)
                    .ConfigureAwait(false));
            if (actual != hertz)
            {
                throw new InvalidOperationException($"Polling readback was {actual?.ToString() ?? "unknown"}, expected {hertz}.");
            }
        }
        catch
        {
            if (previous is int restore && RazerKeyboardProtocol.PollingCode(restore) is byte restoreCode)
            {
                await TryRestoreAsync(
                    transaction => RazerKeyboardProtocol.SetPollingRate(transaction, restoreCode),
                    "restore polling rate").ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task<(bool Ok, T Value)> ProbeAsync<T>(
        string label,
        Func<byte, byte[]> request,
        Func<ReadOnlySpan<byte>, T> parse,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            return (true, parse(await QueryAsync(request, $"read {label}", cancellationToken).ConfigureAwait(false)));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"{label}: {exception.Message}");
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
            _diagnostic?.Invoke($"keyboard {label} failed: {exception.Message}");
        }
    }

    private async Task<byte[]> QueryAsync(
        Func<byte, byte[]> request,
        string label,
        CancellationToken cancellationToken)
    {
        var report = request(NextTransactionId());
        _diagnostic?.Invoke(
            $"keyboard {label}: size={report[6]}, class=0x{report[7]:X2}, command=0x{report[8]:X2}");
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
