using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerKeyboardConnection(
    string DevicePath,
    ushort ProductId,
    string Name,
    ushort FeatureReportByteLength,
    bool UsesLegacyChroma,
    ushort ReportedProductId = 0)
{
    /// <summary>PID Windows enumerated; the dongle and BLE links use their own.</summary>
    public ushort DisplayProductId => ReportedProductId == 0 ? ProductId : ReportedProductId;
}

public sealed record RazerKeyboardState(
    Version? Firmware,
    string? Serial,
    byte? BatteryPercent,
    bool? Charging,
    bool? GameModeEnabled,
    int? PollingRate,
    IReadOnlyList<string> Errors);

public enum RazerKeyboardMappingProtocol
{
    Middleware02,
    ButtonAssignment08,
}

/// <summary>
/// Settings the middleware exposes for the Huntsman V3 and Pro Type keyboards, which the
/// RazerHid catalogue does not cover: game mode, polling period, battery and identity.
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

    public static bool UsesLegacyChroma(ushort productId) =>
        LegacyChromaProducts.Contains(RazerProductAliases.Canonical(productId));

    private readonly IRazerFeatureTransport _transport;
    private readonly string _devicePath;
    private readonly TimeSpan _deviceWait;
    private readonly Action<string>? _diagnostic;
    private byte _transactionId = 0x1F;
    private byte _profileId;

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

    public static bool IsSupported(ushort productId) =>
        Products.ContainsKey(RazerProductAliases.Canonical(productId));

    public static async Task<RazerKeyboardConnection?> FindAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default) =>
        (await FindAllAsync(diagnostic, cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    /// <summary>
    /// Every attached keyboard that belongs to this page. Several keyboards can be
    /// connected at once, including two units of the same model, so discovery must not
    /// stop at the first interface.
    /// </summary>
    public static async Task<IReadOnlyList<RazerKeyboardConnection>> FindAllAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorFeatureInterfacesAsync(
            0x1532,
            RazerKeyboardProtocol.ReportLength,
            cancellationToken).ConfigureAwait(false);

        var candidates = SelectCandidates(interfaces);
        if (candidates.Length == 0)
        {
            diagnostic?.Invoke(
                "keyboard found no admitted 91-byte interface; " +
                $"enumerated={string.Join(",", interfaces.Select(item => $"{item.ProductId:X4}"))}");
            return [];
        }

        diagnostic?.Invoke(
            "keyboard selected " + string.Join(", ", candidates.Select(item =>
                $"pid={item.ProductId:X4} model={RazerProductAliases.Canonical(item.ProductId):X4} " +
                $"path={RazerFeatureTransport.DescribeDevicePath(item.DevicePath)} " +
                $"usage=0x{item.UsagePage:X4}:0x{item.Usage:X4} access={item.Access}")));
        return candidates.Select(CreateConnection).ToArray();
    }

    /// <summary>
    /// Keeps one interface per physical keyboard. The dongle and BLE links enumerate
    /// their own PID, so admission goes through the wired alias. Available links come
    /// first, then wired ahead of wireless because a wired path is stable across reconnects.
    /// </summary>
    internal static WindowsHidDiscovery.HidInterfaceDescriptor[] SelectCandidates(
        IReadOnlyList<WindowsHidDiscovery.HidInterfaceDescriptor> interfaces) =>
        interfaces
            .Where(item => Products.ContainsKey(RazerProductAliases.Canonical(item.ProductId)))
            // The RazerHid catalogue already owns these PIDs and shows its own page.
            .Where(item => RazerHidDeviceCatalog.BuiltIn.Find(0x1532, item.ProductId) is null)
            .GroupBy(item => item.PhysicalDeviceKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Access == DeviceAccessState.Available)
                .ThenBy(item => RazerProductAliases.IsWirelessAlias(item.ProductId))
                .ThenBy(item => item.ProductId)
                .ThenBy(item => item.DevicePath, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(item => item.Access == DeviceAccessState.Available)
            .ThenBy(item => RazerProductAliases.IsWirelessAlias(item.ProductId))
            .ThenBy(item => item.ProductId)
            .ThenBy(item => item.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static IReadOnlyList<RazerKeyboardConnection> SelectConnections(
        IReadOnlyList<WindowsHidDiscovery.HidInterfaceDescriptor> interfaces) =>
        SelectCandidates(interfaces).Select(CreateConnection).ToArray();

    private static RazerKeyboardConnection CreateConnection(
        WindowsHidDiscovery.HidInterfaceDescriptor descriptor)
    {
        var canonical = RazerProductAliases.Canonical(descriptor.ProductId);
        return new RazerKeyboardConnection(
            descriptor.DevicePath,
            canonical,
            Products[canonical],
            descriptor.FeatureReportByteLength,
            UsesLegacyChroma(canonical),
            descriptor.ProductId);
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
        var profile = await ProbeAsync(
            "active profile",
            RazerKeyboardProtocol.GetActiveProfile,
            RazerKeyboardProtocol.ReadActiveProfileId,
            errors,
            cancellationToken).ConfigureAwait(false);
        if (profile.Ok)
        {
            _profileId = profile.Value;
        }

        var polling = await ProbeAsync(
            "polling rate",
            transaction => RazerKeyboardProtocol.GetPollingRate(transaction, _profileId),
            RazerKeyboardProtocol.ReadPollingRate,
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
                transaction => RazerKeyboardProtocol.SetGameModeState(transaction, desired),
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
                transaction => RazerKeyboardProtocol.SetGameModeState(transaction, previous.GameMode),
                "restore game mode").ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetPollingRateAsync(int hertz, CancellationToken cancellationToken = default)
    {
        var code = RazerKeyboardProtocol.PollingCode(hertz)
            ?? throw new ArgumentOutOfRangeException(nameof(hertz), "Unsupported polling rate.");
        _profileId = await TryReadActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var profileId = _profileId;
        var previous = RazerKeyboardProtocol.ReadPollingRate(
            await QueryAsync(
                transaction => RazerKeyboardProtocol.GetPollingRate(transaction, profileId),
                "read polling rate",
                cancellationToken).ConfigureAwait(false));
        if (previous == hertz)
        {
            return;
        }

        try
        {
            await QueryAsync(
                transaction => RazerKeyboardProtocol.SetPollingRate(transaction, profileId, code),
                "set polling rate",
                cancellationToken).ConfigureAwait(false);
            var actual = RazerKeyboardProtocol.ReadPollingRate(
                await QueryAsync(
                    transaction => RazerKeyboardProtocol.GetPollingRate(transaction, profileId),
                    "read back polling rate",
                    cancellationToken).ConfigureAwait(false));
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
                    transaction => RazerKeyboardProtocol.SetPollingRate(transaction, profileId, restoreCode),
                    "restore polling rate").ConfigureAwait(false);
            }

            throw;
        }
    }

    private RazerKeyboardMappingProtocol? _mappingProtocol;
    private IReadOnlyList<byte>? _mappingButtonIds;

    /// <summary>
    /// Probes the two middleware button-assignment families in order. Both are read-only
    /// GETs; the first non-empty list wins, and no product-id list decides admission here.
    /// The 0x02 family is the shared mouse/keyboard mapping path. Some firmware exposes
    /// the protocol-30 0x08 family instead, which uses the existing frame decoder.
    /// </summary>
    public async Task<IReadOnlyList<byte>> GetMappingButtonIdsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_mappingButtonIds is not null)
        {
            return _mappingButtonIds;
        }

        foreach (var protocol in new[]
        {
            RazerKeyboardMappingProtocol.Middleware02,
            RazerKeyboardMappingProtocol.ButtonAssignment08,
        })
        {
            try
            {
                var response = protocol switch
                {
                    RazerKeyboardMappingProtocol.Middleware02 =>
                        await QueryPreparedAsync(
                            RazerHidMappingProtocol.GetButtonList().Report,
                            "mapping 02:84",
                            cancellationToken).ConfigureAwait(false),
                    _ => await QueryPreparedAsync(
                        RazerButtonAssignmentProtocol.CreateGetButtonListRequest(
                            NextTransactionId(), 0x02),
                        "mapping 08:80",
                        cancellationToken).ConfigureAwait(false),
                };
                var ids = protocol switch
                {
                    RazerKeyboardMappingProtocol.Middleware02 =>
                        RazerHidMappingProtocol.ParseButtonList(response),
                    _ => RazerButtonAssignmentProtocol.ParseButtonList(response),
                };
                if (ids.Any(id => id != 0))
                {
                    _mappingProtocol = protocol;
                    _mappingButtonIds = ids;
                    return ids;
                }
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                _diagnostic?.Invoke($"keyboard mapping probe {protocol} failed: {exception.Message}");
            }
        }

        return [];
    }

    public async Task<RazerHidButtonAssignment> GetMappingAssignmentAsync(
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken = default)
    {
        if (_mappingProtocol is null)
        {
            _ = await GetMappingButtonIdsAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_mappingProtocol == RazerKeyboardMappingProtocol.Middleware02)
        {
            return RazerHidMappingProtocol.ParseAssignment(
                await QueryPreparedAsync(
                    RazerHidMappingProtocol.GetAssignment(buttonId, mode).Report,
                    "mapping 02:8C",
                    cancellationToken).ConfigureAwait(false));
        }

        if (_mappingProtocol == RazerKeyboardMappingProtocol.ButtonAssignment08)
        {
            var assignment = RazerButtonAssignmentProtocol.ParseSingleAssignment(
                await QueryPreparedAsync(
                    RazerButtonAssignmentProtocol.CreateGetSingleAssignmentRequest(
                        NextTransactionId(), 0x02, buttonId),
                    "mapping 08:84",
                    cancellationToken).ConfigureAwait(false),
                (RazerButtonAssignmentMode)mode);
            return new RazerHidButtonAssignment(
                assignment.ButtonId,
                (byte)assignment.Mode,
                (byte)assignment.FunctionId,
                assignment.Data.Take(assignment.DataSize).ToArray());
        }

        throw new InvalidOperationException("The keyboard did not report a button list.");
    }

    public async Task SetMappingAssignmentAsync(
        byte buttonId,
        byte mode,
        byte functionId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        if (_mappingProtocol is null)
        {
            _ = await GetMappingButtonIdsAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_mappingProtocol == RazerKeyboardMappingProtocol.Middleware02)
        {
            await QueryPreparedAsync(
                RazerHidMappingProtocol.SetAssignment(buttonId, mode, functionId, data.Span).Report,
                "mapping 02:0C",
                cancellationToken).ConfigureAwait(false);
        }
        else if (_mappingProtocol == RazerKeyboardMappingProtocol.ButtonAssignment08)
        {
            await QueryPreparedAsync(
                RazerButtonAssignmentProtocol.CreateSetSingleAssignmentRequest(
                    NextTransactionId(),
                    0x02,
                    buttonId,
                    (RazerButtonAssignmentMode)mode,
                    (RazerButtonFunctionType)functionId,
                    data.Span),
                "mapping 08:04",
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new InvalidOperationException("The keyboard did not report a button list.");
        }

        var applied = await GetMappingAssignmentAsync(buttonId, mode, cancellationToken)
            .ConfigureAwait(false);
        if (applied.FunctionId != functionId || !applied.Data.SequenceEqual(data.ToArray()))
        {
            throw new InvalidOperationException(
                "The keyboard kept a different button assignment.");
        }
    }

    private async Task<byte> TryReadActiveProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            return RazerKeyboardProtocol.ReadActiveProfileId(
                await QueryAsync(
                    RazerKeyboardProtocol.GetActiveProfile,
                    "read active profile",
                    cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"keyboard active profile unavailable: {exception.Message}");
            return _profileId;
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

    private Task<byte[]> QueryAsync(
        Func<byte, byte[]> request,
        string label,
        CancellationToken cancellationToken) =>
        QueryPreparedAsync(request(NextTransactionId()), label, cancellationToken);

    private async Task<byte[]> QueryPreparedAsync(
        byte[] report,
        string label,
        CancellationToken cancellationToken)
    {
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
