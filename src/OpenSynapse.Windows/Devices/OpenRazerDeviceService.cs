using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.ComponentModel;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public enum OpenRazerKeyswitchOptimization
{
    Typing,
    Gaming,
}

public sealed partial class OpenRazerDeviceService
{
    // The middleware drives GPU card detection on LED class 0 (All_Region).
    private const byte GpuCardLedClassId = 0x00;
    // The middleware writes the LK scroll wheel mode for the base profile.
    private const byte LkScrollProfileId = 0x01;

    private readonly OpenRazerDeviceCatalog _catalog;
    private readonly IRazerFeatureTransport _transport;
    private readonly LocalDiagnosticLog? _diagnosticLog;
    private readonly ConcurrentDictionary<string, string> _endpointCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(ushort ProductId, string Probe), (bool Supported, DateTimeOffset ExpiresAt)>
        _middlewareProbeCache = new();
    private static readonly TimeSpan MiddlewareProbePositiveTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan MiddlewareProbeNegativeTtl = TimeSpan.FromMinutes(5);
    // Middleware bundles increment the transaction id per exchange; the firmware only
    // matches it against the response, so one fixed value works for a single request.
    private const byte MiddlewareTransactionId = 0x1F;
    private static readonly IReadOnlySet<OpenRazerBackendCapability> MappingCapabilities =
        new HashSet<OpenRazerBackendCapability>
        {
            OpenRazerBackendCapability.MappingRead,
            OpenRazerBackendCapability.MappingWrite,
        }.ToFrozenSet();
    private static readonly IReadOnlySet<OpenRazerBackendCapability> ScrollWheelCapabilities =
        new HashSet<OpenRazerBackendCapability>
        {
            OpenRazerBackendCapability.ScrollFreeNotchedModeRead,
            OpenRazerBackendCapability.ScrollFreeNotchedModeWrite,
            OpenRazerBackendCapability.ScrollAccelerationLevelRead,
            OpenRazerBackendCapability.ScrollAccelerationLevelWrite,
        }.ToFrozenSet();

    public event Action<DeviceBatteryReading>? BatteryObserved;

    public OpenRazerDeviceService(LocalDiagnosticLog? diagnosticLog = null)
        : this(OpenRazerDeviceCatalog.BuiltIn, new RazerFeatureTransport(diagnosticLog), diagnosticLog)
    {
    }

    internal OpenRazerDeviceService(
        OpenRazerDeviceCatalog catalog,
        IRazerFeatureTransport transport,
        LocalDiagnosticLog? diagnosticLog = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _diagnosticLog = diagnosticLog;
    }

    public LeviathanV2XAudioService? CreateLeviathanV2XAudio(OpenRazerDeviceConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection.Definition.ProductId == LeviathanV2XProtocol.ProductId &&
               connection.IsReady &&
               !string.IsNullOrWhiteSpace(connection.DevicePath)
            ? new LeviathanV2XAudioService(
                _transport,
                connection.DevicePath!,
                connection.Definition.DeviceWait,
                message => _diagnosticLog?.TryWrite("leviathan-054A", message))
            : null;
    }

    private async Task<OpenRazerDeviceDefinition> ResolveLeviathanDefinitionAsync(
        OpenRazerDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        if (definition.ProductId != LeviathanV2XProtocol.ProductId)
        {
            return definition;
        }

        try
        {
            var request = OpenRazerStandardProtocol.Create(definition,
                "razer_standard_get_edition_info", 0x03, 0x00, 0x86, []);
            var response = await _transport.QueryPreparedAsync(
                devicePath, request.Report, definition.DeviceWait, cancellationToken).ConfigureAwait(false);
            var edition = LeviathanV2XProtocol.ReadEditionInfo(response).Edition;
            return definition.WithDisplayName(LeviathanV2XProtocol.GetEditionDisplayName(edition));
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or
            InvalidOperationException or NotSupportedException or InvalidDataException or UnauthorizedAccessException)
        {
            WriteDiagnostic(definition.ProductId,
                $"optional edition read failed: {exception.GetType().Name}: {exception.Message}");
            return definition;
        }
    }

    public async Task<IReadOnlyList<OpenRazerDeviceConnection>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorFeatureInterfacesAsync(
            OpenRazerDeviceDefinition.VendorId,
            RazerFeatureReport.Length,
            cancellationToken,
            LogObservedInterface).ConfigureAwait(false);
        var results = new List<OpenRazerDeviceConnection>();
        foreach (var group in interfaces
            .Where(item => _catalog.Find(item.VendorId, item.ProductId) is not null)
            // A locally admitted Blade manifest owns the laptop controls and matrix path;
            // discovering it twice would show one physical device as two cards.
            .Where(item => RazerDeviceRegistry.BuiltIn.Find(item.VendorId, item.ProductId) is null)
            .GroupBy(item => (item.ProductId, item.PhysicalDeviceKey))
            .OrderBy(group => group.Key.ProductId)
            .ThenBy(group => group.Key.PhysicalDeviceKey, StringComparer.OrdinalIgnoreCase))
        {
            var definition = _catalog.Find(OpenRazerDeviceDefinition.VendorId, group.Key.ProductId)!;
            var candidates = group.ToArray();
            WriteDiagnostic(definition.ProductId,
                $"scan found {candidates.Length} matching 91-byte HID collection(s)");
            var available = candidates.Where(candidate => candidate.Access == DeviceAccessState.Available).ToArray();
            if (available.Length == 0)
            {
                results.Add(CreateConnection(definition, null, group.Key.PhysicalDeviceKey,
                    OpenRazerEndpointState.BusyOrUnavailable,
                    // Name the rejected collections: a wireless mouse often leaves a wired
                    // collection enumerated whose descriptor does not match, and "busy" hides why.
                    $"No usable 91-byte HID control collection ({DescribeRejected(candidates)})."));
                continue;
            }

            var ordered = _endpointCache.TryGetValue(group.Key.PhysicalDeviceKey, out var cachedPath)
                ? available.OrderByDescending(candidate =>
                    string.Equals(candidate.DevicePath, cachedPath, StringComparison.OrdinalIgnoreCase)).ToArray()
                : available;
            string? lastError = null;
            string? resolvedPath = null;
            foreach (var candidate in ordered)
            {
                WriteDiagnostic(definition.ProductId,
                    $"probing path={RazerFeatureTransport.DescribeDevicePath(candidate.DevicePath)}, access={candidate.Access}, usage=0x{candidate.UsagePage:X4}:0x{candidate.Usage:X4}");
                try
                {
                    if (await ProbeEndpointAsync(definition, candidate.DevicePath, cancellationToken).ConfigureAwait(false))
                    {
                        resolvedPath = candidate.DevicePath;
                        WriteDiagnostic(definition.ProductId,
                            $"resolved path={RazerFeatureTransport.DescribeDevicePath(candidate.DevicePath)}");
                        break;
                    }
                    lastError = "No side-effect-free GET with a known transaction is available for endpoint probing.";
                }
                catch (Exception exception) when (exception is Win32Exception or IOException or
                    InvalidOperationException or NotSupportedException or InvalidDataException)
                {
                    lastError = exception.Message;
                    WriteDiagnostic(definition.ProductId,
                        $"probe failed path={RazerFeatureTransport.DescribeDevicePath(candidate.DevicePath)}: {exception.GetType().Name}: {exception.Message}");
                }
            }

            if (resolvedPath is null)
            {
                _endpointCache.TryRemove(group.Key.PhysicalDeviceKey, out _);
                results.Add(CreateConnection(definition, null, group.Key.PhysicalDeviceKey,
                    OpenRazerEndpointState.RecognizedButUnresolved, lastError));
            }
            else
            {
                _endpointCache[group.Key.PhysicalDeviceKey] = resolvedPath;
                var resolvedDefinition = await ResolveLeviathanDefinitionAsync(
                    definition, resolvedPath, cancellationToken).ConfigureAwait(false);
                var probedCapabilities = await ProbeMiddlewareCapabilitiesAsync(
                    resolvedDefinition, resolvedPath, cancellationToken).ConfigureAwait(false);
                results.Add(CreateConnection(resolvedDefinition, resolvedPath, group.Key.PhysicalDeviceKey,
                    OpenRazerEndpointState.Resolved, null,
                    probedCapabilities.Count > 0 ? probedCapabilities : null));
            }
        }
        return results;
    }

    public async Task<OpenRazerBasicState> ReadBasicStateAsync(
        OpenRazerDeviceConnection connection,
        CancellationToken cancellationToken = default,
        IReadOnlySet<OpenRazerBackendCapability>? excludedCapabilities = null)
    {
        RequireReady(connection);
        Version? firmware = null;
        string? serial = null;
        bool? softwareMode = null;
        int? battery = null;
        bool? charging = null;
        int? pollingRate = null;
        int? dpiX = null;
        int? dpiY = null;
        int? idle = null;
        int? threshold = null;
        bool? autoSleep = null;
        byte? chargeLimiterLevel = null;
        bool? chargeLimiterEnabled = null;
        bool? timeToDimEnabled = null;
        int? timeToDimSeconds = null;
        byte? timeToDimPercent = null;
        byte? indicatorLedConfiguration = null;
        bool? gpuCardDetection = null;
        bool? forceChromaCtrl = null;
        OpenRazerProfileDebounce? profileDebounce = null;
        OpenRazerChromaTap? chromaTap = null;
        byte? batteryDischargeCurve = null;
        OpenRazerMacroStorageInfo? macroStorageInfo = null;
        byte? lkScrollWheelModeMask = null;
        byte? brightness = null;
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var unsupported = new HashSet<OpenRazerBackendCapability>();

        async Task ReadAsync<T>(OpenRazerBackendCapability capability, string key, Func<Task<T>> read, Action<T> assign)
        {
            if (!connection.Capabilities.Contains(capability) || excludedCapabilities?.Contains(capability) == true)
            {
                return;
            }
            try
            {
                assign(await read().ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or
                InvalidOperationException or NotSupportedException or InvalidDataException)
            {
                if (exception is NotSupportedException or InvalidDataException or RazerDeviceTimeoutException)
                {
                    // A manifest can advertise an optional transaction that a particular firmware
                    // rejects, returns malformed data for, or simply never answers. Keep the rest of
                    // the device state usable and let the UI render that field as unknown.
                    unsupported.Add(capability);
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"optional state read '{key}' is unavailable: {exception.GetType().Name}: {exception.Message}");
                    return;
                }

                // Key the failure by capability, not by the internal field name, so the UI can
                // line it up with the protocol detail row that reports the same capability.
                errors[capability.ToString()] = exception.Message;
                WriteDiagnostic(connection.Definition.ProductId,
                    $"state read '{key}' failed: {exception.GetType().Name}: {exception.Message}");
            }
        }

        await ReadAsync(OpenRazerBackendCapability.FirmwareRead, "firmware",
            () => GetFirmwareAsync(connection, cancellationToken), value => firmware = value);
        if (excludedCapabilities?.Contains(OpenRazerBackendCapability.SerialRead) != true)
        {
            if (connection.Definition.Transactions.ContainsKey("razer_chroma_standard_get_serial"))
            {
                try
                {
                    serial = await GetSerialAsync(connection, cancellationToken).ConfigureAwait(false);
                    if (!WindowsHidDiscovery.IsUsableSerial(serial))
                    {
                        serial = null;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is Win32Exception or IOException or
                    InvalidOperationException or NotSupportedException or InvalidDataException)
                {
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"optional serial read failed: {exception.GetType().Name}: {exception.Message}");
                }
            }
            if (string.IsNullOrWhiteSpace(serial))
            {
                serial = await WindowsHidDiscovery.TryReadSerialNumberAsync(
                    connection.DevicePath!, cancellationToken).ConfigureAwait(false);
            }
        }
        await ReadAsync(OpenRazerBackendCapability.DeviceModeRead, "deviceMode",
            () => GetSoftwareModeAsync(connection, cancellationToken), value => softwareMode = value);
        await ReadAsync(OpenRazerBackendCapability.BatteryRead, "battery",
            () => GetBatteryPercentAsync(connection, cancellationToken), value => battery = value);
        await ReadAsync(OpenRazerBackendCapability.ChargingRead, "charging",
            () => GetChargingAsync(connection, cancellationToken), value => charging = value);
        await ReadAsync(OpenRazerBackendCapability.PollingRateRead, "pollingRate",
            () => GetPollingRateAsync(connection, cancellationToken), value => pollingRate = value);
        await ReadAsync(OpenRazerBackendCapability.DpiRead, "dpi",
            () => GetDpiAsync(connection, cancellationToken), value => (dpiX, dpiY) = value);
        await ReadAsync(OpenRazerBackendCapability.IdleTimeoutRead, "idleTimeout",
            () => GetIdleTimeoutAsync(connection, cancellationToken), value => idle = value);
        await ReadAsync(OpenRazerBackendCapability.LowBatteryThresholdRead, "lowBatteryThreshold",
            () => GetLowBatteryThresholdAsync(connection, cancellationToken), value => threshold = value);
        await ReadAsync(OpenRazerBackendCapability.AutoSleepRead, "autoSleep",
            () => GetAutoSleepAsync(connection, cancellationToken), value => autoSleep = value);
        await ReadAsync(OpenRazerBackendCapability.BatteryChargeLimiterRead, "batteryChargeLimiter",
            () => GetBatteryChargeLimiterAsync(connection, cancellationToken), value => (chargeLimiterLevel, chargeLimiterEnabled) = value);
        await ReadAsync(OpenRazerBackendCapability.TimeToDimRead, "timeToDim",
            () => GetTimeToDimAsync(connection, cancellationToken), value =>
            {
                timeToDimEnabled = value.Enabled;
                timeToDimSeconds = value.Seconds;
                timeToDimPercent = value.Percentage;
            });
        await ReadAsync(OpenRazerBackendCapability.IndicatorLedConfigurationRead, "indicatorLedConfiguration",
            () => GetIndicatorLedConfigurationAsync(connection, cancellationToken), value => indicatorLedConfiguration = value);
        await ReadAsync(OpenRazerBackendCapability.GpuCardDetectionRead, "gpuCardDetection",
            () => GetGpuCardDetectionAsync(connection, cancellationToken), value => gpuCardDetection = value.Enabled);
        await ReadAsync(OpenRazerBackendCapability.ForceChromaCtrlRead, "forceChromaCtrl",
            () => GetForceChromaCtrlAsync(connection, cancellationToken), value => forceChromaCtrl = value);
        await ReadAsync(OpenRazerBackendCapability.ProfileDebounceTimeRead, "profileDebounce",
            () => GetProfileDebounceTimeAsync(connection, cancellationToken), value => profileDebounce = value);
        await ReadAsync(OpenRazerBackendCapability.ChromaTapRead, "chromaTap",
            () => GetChromaTapAsync(connection, cancellationToken), value => chromaTap = value);
        await ReadAsync(OpenRazerBackendCapability.BatteryDischargeCurveRead, "batteryDischargeCurve",
            () => GetBatteryDischargeCurveAsync(connection, cancellationToken), value => batteryDischargeCurve = value);
        await ReadAsync(OpenRazerBackendCapability.MacroStorageInfoRead, "macroStorageInfo",
            () => GetMacroStorageInfoAsync(connection, cancellationToken), value => macroStorageInfo = value);
        await ReadAsync(OpenRazerBackendCapability.LkScrollWheelModeRead, "lkScrollWheelMode",
            () => GetLkScrollWheelModeAsync(connection, cancellationToken), value => lkScrollWheelModeMask = value.ModeMask);
        await ReadAsync(OpenRazerBackendCapability.BrightnessRead, "brightness",
            () => GetBrightnessAsync(connection, cancellationToken: cancellationToken), value => brightness = value);

        if (battery is not null && !errors.ContainsKey(nameof(OpenRazerBackendCapability.ChargingRead)))
        {
            BatteryObserved?.Invoke(new DeviceBatteryReading(
                $"openrazer:{connection.InstanceId}", connection.Definition.DisplayName, battery, charging));
        }
        return new OpenRazerBasicState(firmware, serial, softwareMode, battery, charging,
            pollingRate, dpiX, dpiY, idle, threshold, autoSleep,
            chargeLimiterEnabled, chargeLimiterLevel,
            timeToDimEnabled, timeToDimSeconds, timeToDimPercent,
            indicatorLedConfiguration, gpuCardDetection, forceChromaCtrl,
            profileDebounce, chromaTap, batteryDischargeCurve, macroStorageInfo,
            lkScrollWheelModeMask,
            brightness, errors,
            unsupported.ToFrozenSet());
    }

    /// <summary>
    /// Sweeps the read half of the HID feature command space for commands this catalog does not describe.
    /// The firmware answers 0x02 for a supported command and 0x05 for an unsupported one, so
    /// the sweep reveals a device's real capability table without sending state-changing writes.
    /// </summary>
    public async Task<string> ScanCommandSpaceAsync(
        OpenRazerDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        // Mice are scannable now that the sweep is read-only. The old guard existed when
        // the sweep still covered command 00:04, which puts a wireless mouse into a mode
        // where it never rejoins the 2.4 GHz link.
        var report = new StringBuilder();
        report.AppendLine($"generated: {DateTimeOffset.Now:O}");
        report.Append(HidCommandScan.FormatHeader(
            connection.Definition.DisplayName, connection.Definition.ProductId));

        var accepted = 0;
        foreach (var commandClass in HidCommandScan.ScanClasses)
        {
            // The high bit marks a device-to-host read. Never sweep the write half:
            // class 00 command 04 alone can change the device mode.
            for (var commandId = 0x80; commandId <= 0xFF; commandId++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = RazerFeatureReport.CreateRequest(
                    0x1F, 0x00, commandClass, (byte)commandId, ReadOnlySpan<byte>.Empty);
                byte[]? response;
                try
                {
                    response = await _transport.QueryPreparedAsync(
                        connection.DevicePath!, request, connection.Definition.DeviceWait,
                        cancellationToken, allowRemainingPacketsMismatch: true).ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is NotSupportedException or RazerCommandRejectedException)
                {
                    continue;
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or Win32Exception or IOException)
                {
                    continue;
                }

                var status = response![1];
                if (status != 0x05)
                {
                    accepted++;
                }

                report.AppendLine(HidCommandScan.FormatRow(commandClass, (byte)commandId, response));

            }
        }

        report.AppendLine();
        report.Append(CultureInfo.InvariantCulture, $"total accepted: {accepted}");
        report.AppendLine();
        report.AppendLine("## builders already in the bundled catalog");
        foreach (var builder in connection.Definition.Transactions.Keys
                     .OrderBy(static name => name, StringComparer.Ordinal))
        {
            report.AppendLine(builder);
        }

        return report.ToString();
    }


    public async Task<Version> GetFirmwareAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerStandardProtocol.ParseFirmware(await ExecuteAsync(connection,
            OpenRazerStandardProtocol.GetFirmware(connection.Definition), cancellationToken).ConfigureAwait(false));

    public async Task<string> GetSerialAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerStandardProtocol.ParseSerial(await ExecuteAsync(connection,
            OpenRazerStandardProtocol.GetSerial(connection.Definition), cancellationToken).ConfigureAwait(false));

    public async Task<bool> GetSoftwareModeAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(connection,
            OpenRazerStandardProtocol.GetDeviceMode(connection.Definition), cancellationToken).ConfigureAwait(false);
        return OpenRazerStandardProtocol.Arguments(response, 2)[0] == 0x03;
    }

    /// <summary>
    /// Puts a device that is in normal mode back into driver mode. A mouse left in normal
    /// mode never joins its 2.4 GHz link, so the wireless connection stays dead until the
    /// mode is written back. Only normal mode is repaired: an unknown mode is left alone
    /// rather than overwritten blind.
    /// </summary>
    public async Task<bool> EnsureDriverModeAsync(
        OpenRazerDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        if (!connection.Capabilities.Contains(OpenRazerBackendCapability.DeviceModeWrite))
        {
            return false;
        }

        var before = OpenRazerStandardProtocol.ParseDeviceMode(await ExecuteAsync(connection,
            OpenRazerStandardProtocol.GetDeviceMode(connection.Definition), cancellationToken).ConfigureAwait(false));
        if (before != OpenRazerStandardProtocol.NormalDeviceMode)
        {
            // 0x03 is already correct; 0x02 and anything else is not ours to overwrite.
            return before == OpenRazerStandardProtocol.DriverDeviceMode;
        }

        WriteDiagnostic(connection.Definition.ProductId,
            $"device mode was 0x{before:X2}; writing driver mode to restore the 2.4 GHz link");
        await ExecuteWithoutResultAsync(connection,
            OpenRazerStandardProtocol.SetDriverDeviceMode(connection.Definition), cancellationToken)
            .ConfigureAwait(false);

        var after = OpenRazerStandardProtocol.ParseDeviceMode(await ExecuteAsync(connection,
            OpenRazerStandardProtocol.GetDeviceMode(connection.Definition), cancellationToken).ConfigureAwait(false));
        WriteDiagnostic(connection.Definition.ProductId, $"device mode is now 0x{after:X2}");
        return after == OpenRazerStandardProtocol.DriverDeviceMode;
    }

    public async Task<int> GetBatteryPercentAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseBatteryPercent(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetBattery(connection.Definition), cancellationToken).ConfigureAwait(false));

    public async Task<bool> GetChargingAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseCharging(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetCharging(connection.Definition), cancellationToken).ConfigureAwait(false));

    public async Task<int> GetPollingRateAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        var highRate = connection.Definition.Transactions.ContainsKey("razer_chroma_misc_get_polling_rate2");
        return OpenRazerMouseProtocol.ParsePollingRate(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetPollingRate(connection.Definition), cancellationToken).ConfigureAwait(false), highRate);
    }

    /// <summary>
    /// Writes the polling rate and reports the rate the firmware holds afterwards. The
    /// device answers a successful write without echoing the payload, so the value read
    /// back is the only evidence that the setting landed.
    /// </summary>
    /// <returns>The reported rate, or null when the device cannot be read back.</returns>
    public async Task<int?> SetPollingRateAsync(OpenRazerDeviceConnection connection, int hertz, CancellationToken cancellationToken = default)
    {
        foreach (var request in OpenRazerMouseProtocol.SetPollingRate(connection.Definition, hertz))
        {
            await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }

        if (!connection.Capabilities.Contains(OpenRazerBackendCapability.PollingRateRead))
        {
            return null;
        }

        try
        {
            return await GetPollingRateAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is RazerDeviceTimeoutException or InvalidDataException)
        {
            // The write already landed. A device that cannot answer the read-back must not
            // turn a completed write into a failure.
            return null;
        }
    }

    public async Task<(int X, int Y)> GetDpiAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        var byteEncoding = connection.Definition.Transactions.ContainsKey("razer_chroma_misc_get_dpi_xy_byte");
        return OpenRazerMouseProtocol.ParseDpi(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetDpi(connection.Definition), cancellationToken).ConfigureAwait(false), byteEncoding);
    }

    /// <summary>
    /// Writes the DPI and reports the values the firmware holds afterwards, so a caller
    /// can tell a stored setting from a write the device ignored.
    /// </summary>
    /// <returns>The reported DPI, or null when the device cannot be read back.</returns>
    public async Task<(int X, int Y)?> SetDpiAsync(OpenRazerDeviceConnection connection, int x, int y, CancellationToken cancellationToken = default)
    {
        await ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetDpi(connection.Definition, x, y), cancellationToken).ConfigureAwait(false);

        if (!connection.Capabilities.Contains(OpenRazerBackendCapability.DpiRead))
        {
            return null;
        }

        try
        {
            return await GetDpiAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is RazerDeviceTimeoutException or InvalidDataException)
        {
            // The write already landed. A device that cannot answer the read-back must not
            // turn a completed write into a failure.
            return null;
        }
    }

    public Task SetDpiStagesAsync(OpenRazerDeviceConnection connection, OpenRazerDpiStages state, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetDpiStages(connection.Definition, state), cancellationToken);

    public async Task<OpenRazerDpiStages> GetDpiStagesAsync(
        OpenRazerDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        var request = OpenRazerMouseProtocol.GetDpiStages(connection.Definition);
        var response = await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
        try
        {
            return OpenRazerMouseProtocol.ParseDpiStages(connection.Definition, response);
        }
        catch (InvalidDataException exception)
        {
            WriteDiagnostic(connection.Definition.ProductId,
                $"DPI stages response rejected: {exception.Message}; report={Convert.ToHexString(response)}");
            throw;
        }
    }

    public async Task<int> GetIdleTimeoutAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseIdleSeconds(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetIdleTime(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetIdleTimeoutAsync(OpenRazerDeviceConnection connection, int seconds, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetIdleTime(connection.Definition, seconds), cancellationToken);

    public async Task<int> GetLowBatteryThresholdAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseLowBatteryThreshold(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetLowBatteryThreshold(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetLowBatteryThresholdAsync(OpenRazerDeviceConnection connection, int percent, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetLowBatteryThreshold(connection.Definition, percent), cancellationToken);

    public async Task<bool> GetAutoSleepAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseAutoSleepEnabled(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetAutoSleep(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetAutoSleepAsync(OpenRazerDeviceConnection connection, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetAutoSleep(connection.Definition, enabled), cancellationToken);

    public async Task<(byte Level, bool Enabled)> GetBatteryChargeLimiterAsync(
        OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseBatteryChargeLimiter(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetBatteryChargeLimiter(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetBatteryChargeLimiterAsync(
        OpenRazerDeviceConnection connection, byte level, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetBatteryChargeLimiter(connection.Definition, level, enabled), cancellationToken);

    public async Task<OpenRazerTimeToDim> GetTimeToDimAsync(
        OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseTimeToDim(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetTimeToDim(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetTimeToDimAsync(
        OpenRazerDeviceConnection connection,
        bool enabled,
        int seconds,
        byte percentage,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetTimeToDim(connection.Definition, 0, enabled, seconds, percentage),
            cancellationToken);

    public async Task<byte> GetIndicatorLedConfigurationAsync(
        OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseIndicatorLedConfiguration(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetIndicatorLedConfiguration(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetIndicatorLedConfigurationAsync(
        OpenRazerDeviceConnection connection, byte configuration, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetIndicatorLedConfiguration(connection.Definition, configuration), cancellationToken);

    public async Task<OpenRazerGpuCardDetection> GetGpuCardDetectionAsync(
        OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseGpuCardDetection(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetGpuCardDetection(connection.Definition, GpuCardLedClassId), cancellationToken).ConfigureAwait(false));

    public Task SetGpuCardDetectionAsync(
        OpenRazerDeviceConnection connection, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetGpuCardDetection(connection.Definition, GpuCardLedClassId, enabled ? (byte)1 : (byte)0),
            cancellationToken);

    public async Task<bool> GetForceChromaCtrlAsync(
        OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseForceChromaCtrl(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetForceChromaCtrl(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetForceChromaCtrlAsync(
        OpenRazerDeviceConnection connection, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetForceChromaCtrl(connection.Definition, enabled ? (byte)1 : (byte)0),
            cancellationToken);

    public async Task<OpenRazerProfileDebounce> GetProfileDebounceTimeAsync(
        OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseProfileDebounceTime(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetProfileDebounceTime(connection.Definition, 0), cancellationToken).ConfigureAwait(false));

    public Task SetProfileDebounceTimeAsync(
        OpenRazerDeviceConnection connection,
        byte makeDebounceTimeMs,
        byte makeMaskTimeMs,
        byte breakDebounceTimeMs,
        byte breakMaskTimeMs,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetProfileDebounceTime(connection.Definition, 0,
                makeDebounceTimeMs, makeMaskTimeMs, breakDebounceTimeMs, breakMaskTimeMs),
            cancellationToken);

    public async Task<OpenRazerChromaTap> GetChromaTapAsync(
        OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseChromaTap(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetChromaTap(connection.Definition, 0), cancellationToken).ConfigureAwait(false));

    public Task SetChromaTapAsync(
        OpenRazerDeviceConnection connection,
        byte status,
        byte tapMode,
        byte functionId,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetChromaTap(connection.Definition, 0, status, tapMode, functionId),
            cancellationToken);

    public async Task<byte> GetBatteryDischargeCurveAsync(
        OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseBatteryDischargeCurve(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetBatteryDischargeCurve(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetBatteryDischargeCurveAsync(
        OpenRazerDeviceConnection connection, byte chemistry, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetBatteryDischargeCurve(connection.Definition, chemistry), cancellationToken);

    public async Task<OpenRazerMacroStorageInfo> GetMacroStorageInfoAsync(
        OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseMacroStorageInfo(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetMacroStorageInfo(connection.Definition), cancellationToken).ConfigureAwait(false));

    public async Task<OpenRazerLkScrollWheelMode> GetLkScrollWheelModeAsync(
        OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseLkScrollWheelMode(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetLkScrollWheelMode(connection.Definition, LkScrollProfileId), cancellationToken).ConfigureAwait(false));

    public Task SetLkScrollWheelModeAsync(
        OpenRazerDeviceConnection connection, byte modeMask, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetLkScrollWheelMode(connection.Definition, modeMask, LkScrollProfileId), cancellationToken);

    public async Task<IReadOnlyList<byte>> GetMappingButtonIdsAsync(
        OpenRazerDeviceConnection connection,
        CancellationToken cancellationToken = default) =>
        OpenRazerMappingProtocol.ParseButtonList(await ExecuteAsync(connection,
            OpenRazerMappingProtocol.GetButtonList(), cancellationToken).ConfigureAwait(false));

    public async Task<OpenRazerButtonAssignment> GetButtonAssignmentAsync(
        OpenRazerDeviceConnection connection,
        byte buttonId,
        byte mode,
        CancellationToken cancellationToken = default) =>
        OpenRazerMappingProtocol.ParseAssignment(await ExecuteAsync(connection,
            OpenRazerMappingProtocol.GetAssignment(buttonId, mode), cancellationToken).ConfigureAwait(false));

    public async Task SetButtonAssignmentAsync(
        OpenRazerDeviceConnection connection,
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithoutResultAsync(connection,
            OpenRazerMappingProtocol.SetAssignment(buttonId, mode, functionId, data.ToArray()),
            cancellationToken).ConfigureAwait(false);
        var applied = await GetButtonAssignmentAsync(connection, buttonId, mode, cancellationToken)
            .ConfigureAwait(false);
        if (applied.FunctionId != functionId || !applied.Data.SequenceEqual(data))
        {
            throw new InvalidOperationException("The device kept a different button assignment.");
        }
    }

    public async Task<byte> GetBrightnessAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerStorage? storage = null,
        OpenRazerLedZone? ledId = null,
        CancellationToken cancellationToken = default)
    {
        var selectedZone = ledId ?? (storage is null || storage == connection.Definition.DefaultStorage
            ? GetDefaultBrightnessZone(connection, write: false) : null);
        var zone = OpenRazerLightingProtocol.ResolveBrightnessZone(connection.Definition, write: false,
            storage, selectedZone);
        RequireLightingZone(connection, zone, capability => capability.CanReadBrightness, "brightness read");
        if (connection.Definition.ProductId == LeviathanV2XProtocol.ProductId)
        {
            var region = await GetLeviathanRegionAsync(connection, cancellationToken).ConfigureAwait(false);
            var leviathanRequest = OpenRazerStandardProtocol.Create(connection.Definition,
                "razer_chroma_extended_matrix_get_brightness", 0x03, 0x0F, 0x84,
                [(byte)OpenRazerStorage.Temporary, region]);
            var leviathanArguments = OpenRazerStandardProtocol.Arguments(await ExecuteAsync(connection,
                leviathanRequest, cancellationToken).ConfigureAwait(false), 3);
            return leviathanArguments[2];
        }

        var request = OpenRazerLightingProtocol.GetBrightness(connection.Definition, storage, zone);
        byte[] response;
        try
        {
            response = await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }
        catch (NotSupportedException) when (
            zone == OpenRazerLedZone.All &&
            request.BuilderName == "razer_chroma_extended_matrix_get_brightness")
        {
            // shortcut: use the first readable zone instead of querying the official region list;
            // upgrade if a device rejects zone 0 but exposes more than one matrix region.
            var fallback = connection.LightingZones.Values
                .Where(item => item.Zone != OpenRazerLedZone.All && item.CanReadBrightness)
                .OrderBy(item => item.Zone)
                .FirstOrDefault();
            if (fallback is null) throw;
            request = OpenRazerLightingProtocol.GetBrightness(connection.Definition, storage, fallback.Zone);
            response = await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }
        var arguments = OpenRazerStandardProtocol.Arguments(response,
            request.BuilderName == "razer_chroma_misc_get_dock_brightness" ? (byte)1 : (byte)2);
        return request.BuilderName switch
        {
            "razer_chroma_misc_get_dock_brightness" => arguments[0],
            "razer_chroma_misc_get_blade_brightness" => arguments[1],
            _ => arguments[2],
        };
    }

    public async Task SetBrightnessAsync(
        OpenRazerDeviceConnection connection,
        byte brightness,
        OpenRazerStorage? storage = null,
        OpenRazerLedZone? ledId = null,
        CancellationToken cancellationToken = default)
    {
        var selectedZone = ledId ?? (storage is null || storage == connection.Definition.DefaultStorage
            ? GetDefaultBrightnessZone(connection, write: true) : null);
        var zone = OpenRazerLightingProtocol.ResolveBrightnessZone(connection.Definition, write: true,
            storage, selectedZone);
        RequireLightingZone(connection, zone, capability => capability.CanWriteBrightness, "brightness write");
        if (connection.Definition.ProductId == LeviathanV2XProtocol.ProductId)
        {
            var region = await GetLeviathanRegionAsync(connection, cancellationToken).ConfigureAwait(false);
            await ExecuteWithoutResultAsync(connection, OpenRazerStandardProtocol.Create(connection.Definition,
                "razer_chroma_extended_matrix_brightness", 0x03, 0x0F, 0x04,
                [(byte)OpenRazerStorage.Temporary, region, brightness]), cancellationToken).ConfigureAwait(false);
            return;
        }

        await ExecuteWithoutResultAsync(connection,
            OpenRazerLightingProtocol.SetBrightness(connection.Definition, storage, zone, brightness), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetLightingAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerLightingSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (connection.Definition.ProductId == LeviathanV2XProtocol.ProductId)
        {
            // The soundbar's effect write needs the region it reports, not the enum's All
            // placeholder, so it bypasses the OpenRazer zone resolution below.
            var region = await GetLeviathanRegionAsync(connection, cancellationToken).ConfigureAwait(false);
            await ExecuteWithoutResultAsync(connection,
                new OpenRazerRequest("razer_chroma_extended_matrix_effect_leviathan",
                    LeviathanV2XProtocol.CreateEffect(region, settings.Effect, settings.Direction,
                        settings.Primary, settings.Secondary)),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var zone = OpenRazerLightingProtocol.ResolveEffectZone(
            connection.Definition, settings.Effect, settings.Storage, settings.Zone);
        RequireLightingZone(connection, zone,
            capability => capability.LightingEffects.Contains(settings.Effect), "lighting effect");
        foreach (var request in OpenRazerLightingProtocol.CreateEffectRequests(connection.Definition, settings))
        {
            await ExecuteWithoutResultAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task SetCustomRowAsync(
        OpenRazerDeviceConnection connection,
        byte row,
        byte startColumn,
        IReadOnlyList<OpenRazerColor> colors,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerLightingProtocol.SetCustomRow(connection.Definition, row, startColumn, colors), cancellationToken);

    public async Task SetCustomFrameAsync(
        OpenRazerDeviceConnection connection,
        IReadOnlyList<OpenRazerColor> frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var dimensions = connection.Definition.MatrixDimensions ??
            throw new NotSupportedException("The OpenRazer device does not expose matrix dimensions.");
        if (!connection.Capabilities.Contains(OpenRazerBackendCapability.MatrixFrameWrite) ||
            frame.Count != dimensions.Rows * dimensions.Columns)
        {
            throw new NotSupportedException(
                $"OpenRazer device 1532:{connection.Definition.ProductId:X4} does not support this matrix frame.");
        }

        for (var row = 0; row < dimensions.Rows; row++)
        {
            await SetCustomRowAsync(
                connection,
                checked((byte)row),
                0,
                frame.Skip(row * dimensions.Columns).Take(dimensions.Columns).ToArray(),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public Task SetLedStateAsync(OpenRazerDeviceConnection connection, OpenRazerStorage storage, OpenRazerLedZone ledId, bool enabled, CancellationToken cancellationToken = default)
    {
        RequireLightingZone(connection, ledId, capability => capability.CanWriteState, "LED state write");
        if (!SupportsStorageAndZone(connection.Definition,
            "razer_chroma_standard_set_led_state", storage, ledId))
        {
            throw new NotSupportedException(
                $"OpenRazer device 1532:{connection.Definition.ProductId:X4} does not support LED state storage {storage} on zone {ledId}.");
        }
        return ExecuteWithoutResultAsync(connection,
            OpenRazerStandardProtocol.SetLedState(connection.Definition, (byte)storage, (byte)ledId, enabled), cancellationToken);
    }

    public async Task<bool> GetLedStateAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerStorage storage,
        OpenRazerLedZone ledId,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanReadState, "LED state read",
            "razer_chroma_standard_get_led_state");
        return OpenRazerStandardProtocol.ParseLedState(await ExecuteAsync(connection,
            OpenRazerStandardProtocol.GetLedState(connection.Definition, (byte)storage, (byte)ledId),
            cancellationToken).ConfigureAwait(false), (byte)storage, (byte)ledId);
    }

    public async Task<OpenRazerLightingEffect?> GetLedEffectAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerStorage storage,
        OpenRazerLedZone ledId,
        CancellationToken cancellationToken = default)
    {
        if (connection.Definition.ProductId == LeviathanV2XProtocol.ProductId)
        {
            var region = await GetLeviathanRegionAsync(connection, cancellationToken).ConfigureAwait(false);
            var effect = LeviathanV2XProtocol.ReadEffect(await ExecuteAsync(connection,
                new OpenRazerRequest("razer_chroma_extended_matrix_effect_leviathan_read",
                    LeviathanV2XProtocol.CreateGetEffect(LeviathanV2XProtocol.ReadProfile, region)),
                cancellationToken).ConfigureAwait(false), out _, out _);
            return effect switch
            {
                0x00 => OpenRazerLightingEffect.Off,
                0x01 => OpenRazerLightingEffect.Static,
                0x02 => OpenRazerLightingEffect.BreathingDual,
                0x03 => OpenRazerLightingEffect.Spectrum,
                0x04 => OpenRazerLightingEffect.Wave,
                _ => null,
            };
        }

        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanReadEffect, "classic LED effect read",
            "razer_chroma_standard_get_led_effect");
        return OpenRazerStandardProtocol.ParseLedEffect(await ExecuteAsync(connection,
            OpenRazerStandardProtocol.GetLedEffect(connection.Definition, (byte)storage, (byte)ledId),
            cancellationToken).ConfigureAwait(false), (byte)storage, (byte)ledId) switch
        {
            OpenRazerClassicLedEffect.Static => OpenRazerLightingEffect.Static,
            OpenRazerClassicLedEffect.Blinking => OpenRazerLightingEffect.Blinking,
            OpenRazerClassicLedEffect.Breathing => OpenRazerLightingEffect.BreathingSingle,
            OpenRazerClassicLedEffect.Spectrum => OpenRazerLightingEffect.Spectrum,
            _ => null,
        };
    }

    public Task SetLedEffectAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerStorage storage,
        OpenRazerLedZone ledId,
        OpenRazerClassicLedEffect effect,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanWriteEffect, "classic LED effect write",
            "razer_chroma_standard_set_led_effect");
        return ExecuteWithoutResultAsync(connection,
            OpenRazerStandardProtocol.SetLedEffect(connection.Definition, (byte)storage, (byte)ledId, effect),
            cancellationToken);
    }

    public async Task<OpenRazerColor> GetLedColorAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerStorage storage,
        OpenRazerLedZone ledId,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanReadColor, "LED color read",
            "razer_chroma_standard_get_led_rgb");
        return OpenRazerStandardProtocol.ParseLedColor(await ExecuteAsync(connection,
            OpenRazerStandardProtocol.GetLedColor(connection.Definition, (byte)storage, (byte)ledId),
            cancellationToken).ConfigureAwait(false), (byte)storage, (byte)ledId);
    }

    public Task SetLedColorAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerStorage storage,
        OpenRazerLedZone ledId,
        OpenRazerColor color,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanWriteColor, "LED color write",
            "razer_chroma_standard_set_led_rgb");
        return ExecuteWithoutResultAsync(connection,
            OpenRazerStandardProtocol.SetLedColor(connection.Definition, (byte)storage, (byte)ledId, color),
            cancellationToken);
    }

    public Task SetLedBlinkingAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerStorage storage,
        OpenRazerLedZone ledId,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanWriteBlinking, "LED blinking write",
            "razer_chroma_standard_set_led_blinking");
        return ExecuteWithoutResultAsync(connection,
            OpenRazerStandardProtocol.SetLedBlinking(connection.Definition, (byte)storage, (byte)ledId),
            cancellationToken);
    }

    public Task TriggerReactiveAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection, OpenRazerStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_matrix_reactive_trigger", 0x05, 0x03, 0x0A,
            [0x02, 0x00, 0x00, 0x00, 0x00]), cancellationToken);

    public async Task<byte> GetScrollModeAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseSecondArgument(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetScrollMode(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetScrollModeAsync(OpenRazerDeviceConnection connection, byte mode, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetScrollMode(connection.Definition, mode), cancellationToken);

    public async Task<bool> GetScrollAccelerationAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseBooleanSecondArgument(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetScrollAcceleration(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetScrollAccelerationAsync(OpenRazerDeviceConnection connection, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetScrollAcceleration(connection.Definition, enabled), cancellationToken);

    public async Task<bool> GetSmartReelAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseBooleanSecondArgument(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetSmartReel(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetSmartReelAsync(OpenRazerDeviceConnection connection, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetSmartReel(connection.Definition, enabled), cancellationToken);

    public async Task<byte> GetSmartReelThresholdAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseSecondArgument(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetSmartReelThreshold(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetSmartReelThresholdAsync(OpenRazerDeviceConnection connection, byte level, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetSmartReelThreshold(connection.Definition, level), cancellationToken);

    public async Task<RazerScrollFreeNotchedMode> GetScrollFreeNotchedModeAsync(
        OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        var request = RazerScrollWheelProtocol.CreateGetScrollFreeNotchedModeRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_scroll_get_free_notched_mode", request), cancellationToken).ConfigureAwait(false);
        return RazerScrollWheelProtocol.ParseScrollFreeNotchedMode(response, request).Mode;
    }

    public Task SetScrollFreeNotchedModeAsync(
        OpenRazerDeviceConnection connection,
        RazerScrollFreeNotchedMode mode,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_scroll_set_free_notched_mode",
                RazerScrollWheelProtocol.CreateSetScrollFreeNotchedModeRequest(MiddlewareTransactionId, mode)),
            cancellationToken);

    public async Task<byte> GetScrollVirtualAccelerationLevelAsync(
        OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        var request =
            RazerScrollWheelProtocol.CreateGetScrollVirtualAccelerationLevelRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_scroll_get_acceleration_level", request), cancellationToken).ConfigureAwait(false);
        return RazerScrollWheelProtocol.ParseScrollVirtualAccelerationLevel(response, request).Level;
    }

    public Task SetScrollVirtualAccelerationLevelAsync(
        OpenRazerDeviceConnection connection,
        byte level,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_scroll_set_acceleration_level",
                RazerScrollWheelProtocol.CreateSetScrollVirtualAccelerationLevelRequest(
                    MiddlewareTransactionId, level)),
            cancellationToken);

    public Task SetFnPrimaryAsync(OpenRazerDeviceConnection connection, bool fnFunctionsArePrimary, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection, OpenRazerStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_fn_key_toggle", 0x02, 0x02, 0x06,
            [0x00, fnFunctionsArePrimary ? (byte)1 : (byte)0]), cancellationToken);

    public async Task<OpenRazerKeyswitchOptimization> GetKeyswitchOptimizationAsync(
        OpenRazerDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(connection, OpenRazerStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_get_keyswitch_optimization", 0x04, 0x02, 0x82, []), cancellationToken).ConfigureAwait(false);
        var arguments = OpenRazerStandardProtocol.Arguments(response, 4);
        return (arguments[1], arguments[3]) switch
        {
            (0x14, 0x28) => OpenRazerKeyswitchOptimization.Typing,
            (0x00, 0x00) => OpenRazerKeyswitchOptimization.Gaming,
            _ => throw new InvalidDataException("Device returned an unknown keyswitch optimization state."),
        };
    }

    public async Task SetKeyswitchOptimizationAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerKeyswitchOptimization optimization,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(optimization))
        {
            throw new ArgumentOutOfRangeException(nameof(optimization));
        }
        var typing = optimization == OpenRazerKeyswitchOptimization.Typing;
        var command1 = OpenRazerStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_keyswitch_optimization_command1", 0x04, 0x02, 0x02,
            typing ? [0x00, 0x14, 0x00, 0x28, 0x00] : [0x00, 0x00, 0x00, 0x00],
            allowArgumentsBeyondDeclaredSize: typing);
        var command2 = OpenRazerStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_keyswitch_optimization_command2", 0x05, 0x02, 0x15,
            typing ? [0x01, 0x00, 0x14, 0x00, 0x28, 0x00] : [0x01, 0x00, 0x00, 0x00, 0x00],
            allowArgumentsBeyondDeclaredSize: typing);
        await ExecuteAsync(connection, command1, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, command2, cancellationToken).ConfigureAwait(false);
    }

    public Task SetHyperPollingIndicatorAsync(
        OpenRazerDeviceConnection connection,
        byte mode,
        CancellationToken cancellationToken = default)
    {
        if (mode is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        return ExecuteWithoutResultAsync(connection, OpenRazerStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_indicator_led_mode",
            0x01, 0x07, 0x10, [mode]), cancellationToken);
    }

    public async Task PairHyperPollingAsync(
        OpenRazerDeviceConnection connection,
        ushort mouseProductId,
        CancellationToken cancellationToken = default)
    {
        var step1 = OpenRazerStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step1",
            0x01, 0x00, 0x46, [0x01]);
        var step2 = OpenRazerStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step2",
            0x03, 0x00, 0x41, [0x01, checked((byte)(mouseProductId >> 8)), checked((byte)mouseProductId)]);
        await ExecuteAsync(connection, step1, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, step2, cancellationToken).ConfigureAwait(false);
    }

    public Task UnpairHyperPollingAsync(
        OpenRazerDeviceConnection connection,
        ushort mouseProductId,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection, OpenRazerStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_unpair",
            0x02, 0x00, 0x42,
            [checked((byte)(mouseProductId >> 8)), checked((byte)mouseProductId)]), cancellationToken);

    /// <summary>
    /// Sends the middleware-only GETs once per device and caches which command families
    /// answer, so a control appears only when the firmware really understands it. The cache
    /// keeps device-watch rescans from re-sending a HID request every few seconds.
    /// </summary>
    internal async Task<IReadOnlySet<OpenRazerBackendCapability>> ProbeMiddlewareCapabilitiesAsync(
        OpenRazerDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken = default)
    {
        if (definition.Category is not (DeviceCategory.Mouse or DeviceCategory.Keyboard))
        {
            return FrozenSet<OpenRazerBackendCapability>.Empty;
        }

        var result = new HashSet<OpenRazerBackendCapability>();
        var buttonList = OpenRazerMappingProtocol.GetButtonList();
        if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                "mapping", buttonList.Report,
                response => OpenRazerMappingProtocol.ParseButtonList(response).Count > 0,
                cancellationToken).ConfigureAwait(false))
        {
            result.UnionWith(MappingCapabilities);
        }

        if (definition.Category == DeviceCategory.Mouse)
        {
            var freeNotched = RazerScrollWheelProtocol.CreateGetScrollFreeNotchedModeRequest(MiddlewareTransactionId);
            if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                    "scroll.freeNotched", freeNotched, response =>
                {
                    _ = RazerScrollWheelProtocol.ParseScrollFreeNotchedMode(response, freeNotched);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
            {
                result.Add(OpenRazerBackendCapability.ScrollFreeNotchedModeRead);
                result.Add(OpenRazerBackendCapability.ScrollFreeNotchedModeWrite);
            }

            var accelerationLevel =
                RazerScrollWheelProtocol.CreateGetScrollVirtualAccelerationLevelRequest(MiddlewareTransactionId);
            if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                    "scroll.accelerationLevel", accelerationLevel, response =>
                {
                    _ = RazerScrollWheelProtocol.ParseScrollVirtualAccelerationLevel(response, accelerationLevel);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
            {
                result.Add(OpenRazerBackendCapability.ScrollAccelerationLevelRead);
                result.Add(OpenRazerBackendCapability.ScrollAccelerationLevelWrite);
            }
        }

        await ProbeOnboardProfileAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeWirelessPairingAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeKnobAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeAnalogKeyAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeOledAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeHapticScrollAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);

        return result.ToFrozenSet();
    }

    /// <summary>
    /// Sends one middleware GET and caches whether the firmware answered, so device-watch
    /// rescans do not re-send it. Shared by every protocol family that has no OpenRazer
    /// capability of its own to gate on.
    /// </summary>
    private async Task<bool> ProbeMiddlewareAsync(
        ushort productId,
        string devicePath,
        TimeSpan deviceWait,
        string probe,
        byte[] request,
        Func<byte[], bool> accepts,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var key = (productId, probe);
        if (_middlewareProbeCache.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Supported;
        }

        var supported = false;
        try
        {
            var response = await _transport.QueryPreparedAsync(
                devicePath, request, deviceWait, cancellationToken).ConfigureAwait(false);
            supported = accepts(response);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or
            InvalidOperationException or NotSupportedException or InvalidDataException or
            UnauthorizedAccessException or TimeoutException)
        {
            WriteDiagnostic(productId, $"{probe} probe failed: {exception.GetType().Name}: {exception.Message}");
        }

        _middlewareProbeCache[key] = (
            supported,
            now + (supported ? MiddlewareProbePositiveTtl : MiddlewareProbeNegativeTtl));
        WriteDiagnostic(productId, $"{probe} probe: {supported}");
        return supported;
    }

    private async Task<bool> ProbeEndpointAsync(
        OpenRazerDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        var probes = CreateSafeProbes(definition);
        if (probes.Count == 0)
        {
            return false;
        }
        foreach (var probe in probes)
        {
            WriteDiagnostic(definition.ProductId,
                $"safe probe builder={probe.BuilderName}, txn=0x{probe.Report[2]:X2}, class=0x{probe.Report[7]:X2}, command=0x{probe.Report[8]:X2}");
            try
            {
                await _transport.QueryPreparedAsync(devicePath, probe.Report, definition.DeviceWait,
                    cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (NotSupportedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
        return false;
    }

    private void LogObservedInterface(WindowsHidDiscovery.HidInterfaceDescriptor item)
    {
        if (_catalog.Find(item.VendorId, item.ProductId) is null)
        {
            return;
        }
        WriteDiagnostic(item.ProductId,
            $"interface path={RazerFeatureTransport.DescribeDevicePath(item.DevicePath)}, input={item.InputReportByteLength}, output={item.OutputReportByteLength}, feature={item.FeatureReportByteLength}, usage=0x{item.UsagePage:X4}:0x{item.Usage:X4}, access={item.Access}");
    }

    private void WriteDiagnostic(ushort productId, string message) =>
        _diagnosticLog?.TryWrite($"openrazer-{productId:X4}", message);

    /// <summary>
    /// Records why a control was skipped before it reached the device, so a write that
    /// never leaves the app is distinguishable from one the firmware refused.
    /// </summary>
    public void LogSkippedControl(ushort productId, string message) =>
        _diagnosticLog?.TryWrite($"openrazer-{productId:X4}", message);

    internal static string DescribeRejected(IReadOnlyList<WindowsHidDiscovery.HidInterfaceDescriptor> candidates) =>
        string.Join("; ", candidates.Select(candidate =>
            $"{RazerFeatureTransport.DescribeDevicePath(candidate.DevicePath)} " +
            $"feature={candidate.FeatureReportByteLength} " +
            $"usage=0x{candidate.UsagePage:X4}:0x{candidate.Usage:X4}"));

    private static IReadOnlyList<OpenRazerRequest> CreateSafeProbes(OpenRazerDeviceDefinition definition)
    {
        var probes = new List<OpenRazerRequest>();
        AddIfResolved("razer_chroma_standard_get_firmware_version", () => OpenRazerStandardProtocol.GetFirmware(definition));
        AddIfResolved("razer_chroma_standard_get_serial", () => OpenRazerStandardProtocol.GetSerial(definition));
        AddIfResolved("razer_chroma_standard_get_device_mode", () => OpenRazerStandardProtocol.GetDeviceMode(definition));
        AddIfResolved("razer_chroma_misc_get_battery_level", () => OpenRazerMouseProtocol.GetBattery(definition));
        AddIfResolved("razer_chroma_misc_get_dpi_xy", () => OpenRazerMouseProtocol.GetDpi(definition));
        AddIfResolved("razer_chroma_misc_get_dpi_xy_byte", () => OpenRazerMouseProtocol.GetDpi(definition));
        AddIfResolved("razer_chroma_misc_get_polling_rate2", () => OpenRazerMouseProtocol.GetPollingRate(definition));
        AddIfResolved("razer_chroma_misc_get_polling_rate", () => OpenRazerMouseProtocol.GetPollingRate(definition));
        // The soundbar has no All region: the firmware only answers a real region id, so
        // probing with region 0 fails on every unit. Its brightness path reads the region
        // list first, and the firmware probe above is enough to open the channel.
        if (!OpenRazerDeviceCatalog.IsLeviathanV2X(definition.ProductId))
        {
            AddIfResolved("razer_chroma_extended_matrix_get_brightness", () =>
                OpenRazerLightingProtocol.GetBrightness(definition, null, null));
        }
        AddIfResolved("razer_chroma_standard_get_led_brightness", () =>
            OpenRazerLightingProtocol.GetBrightness(definition, null, null));
        AddIfResolved("razer_chroma_misc_get_blade_brightness", () =>
            OpenRazerLightingProtocol.GetBrightness(definition, null, null));
        AddIfResolved("razer_chroma_misc_get_dock_brightness", () =>
            OpenRazerLightingProtocol.GetBrightness(definition, null, null));
        return probes;

        void AddIfResolved(string builder, Func<OpenRazerRequest> create)
        {
            if (definition.Transactions.ContainsKey(builder) && !probes.Any(item => item.BuilderName == builder))
            {
                probes.Add(create());
            }
        }
    }

    private OpenRazerDeviceConnection CreateConnection(
        OpenRazerDeviceDefinition definition,
        string? path,
        string? physicalDeviceKey,
        OpenRazerEndpointState state,
        string? error,
        IReadOnlySet<OpenRazerBackendCapability>? extraCapabilities = null)
    {
        var resolved = state == OpenRazerEndpointState.Resolved;
        var capabilities = resolved ? GetCapabilities(definition) : FrozenSet<OpenRazerBackendCapability>.Empty;
        if (resolved && extraCapabilities is not null)
        {
            capabilities = capabilities.Union(extraCapabilities).ToFrozenSet();
        }
        return new(definition, path, physicalDeviceKey, state,
            capabilities,
            resolved
                ? GetLightingZoneCapabilities(definition)
                : FrozenDictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>.Empty,
            error);
    }

    internal static IReadOnlySet<OpenRazerLedZone> GetSupportedLedZones(OpenRazerDeviceDefinition device)
    {
        return GetLightingZoneCapabilities(device).Keys.ToFrozenSet();
    }

    internal static IReadOnlyDictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities> GetLightingZoneCapabilities(
        OpenRazerDeviceDefinition device)
    {
        var zones = new HashSet<OpenRazerLedZone> { device.DefaultLedZone };
        foreach (var (builder, prefixes) in device.BuilderArgumentPrefixes)
        {
            if (!builder.Contains("matrix_effect", StringComparison.Ordinal) &&
                !builder.Contains("brightness", StringComparison.Ordinal) &&
                !builder.Contains("_led_", StringComparison.Ordinal))
            {
                continue;
            }
            foreach (var prefix in prefixes)
            {
                if (prefix.Length >= 2 && Enum.IsDefined((OpenRazerLedZone)prefix.Span[1]))
                {
                    zones.Add((OpenRazerLedZone)prefix.Span[1]);
                }
            }
        }

        foreach (var capability in device.SourceCapabilities)
        {
            if (TryGetSourceZone(capability, out var zone))
            {
                zones.Add(zone);
            }
        }

        var output = new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>();
        var effectsByZone = GetLightingEffectsByZone(device);
        foreach (var zone in zones.Order())
        {
            var effects = effectsByZone.GetValueOrDefault(zone) ?? FrozenSet<OpenRazerLightingEffect>.Empty;
            var canReadBrightness = HasSourceBrightness(device, zone, write: false) &&
                CanBuild(() => OpenRazerLightingProtocol.GetBrightness(
                device, device.DefaultStorage, zone));
            var canWriteBrightness = HasSourceBrightness(device, zone, write: true) &&
                CanBuild(() => OpenRazerLightingProtocol.SetBrightness(
                device, device.DefaultStorage, zone, 0));
            var canReadState = device.HasSourceCapability("get_logo_active") && zone == OpenRazerLedZone.Logo &&
                CanUseClassicBuilder(device, "razer_chroma_standard_get_led_state", zone);
            var canWriteState = device.Transactions.ContainsKey("razer_chroma_standard_set_led_state") &&
                HasSourceStateWrite(device, zone) &&
                SupportsStorageAndZone(device, "razer_chroma_standard_set_led_state",
                    device.DefaultStorage, zone);
            var canReadEffect = zone == OpenRazerLedZone.Macro &&
                device.HasSourceCapability("get_macro_effect") &&
                CanUseClassicBuilder(device, "razer_chroma_standard_get_led_effect", zone);
            var canWriteEffect = effects.Count > 0 &&
                CanUseClassicBuilder(device, "razer_chroma_standard_set_led_effect", zone);
            var canReadColor = false;
            var canWriteColor = effects.Any(effect => effect is OpenRazerLightingEffect.Static or
                OpenRazerLightingEffect.BreathingSingle or OpenRazerLightingEffect.Blinking) &&
                CanUseClassicBuilder(device, "razer_chroma_standard_set_led_rgb", zone);
            var canWriteBlinking = effects.Contains(OpenRazerLightingEffect.Blinking) &&
                CanUseClassicBuilder(device, "razer_chroma_standard_set_led_blinking", zone);
            if (canReadBrightness || canWriteBrightness || canReadState || canWriteState ||
                canReadEffect || canWriteEffect || canReadColor || canWriteColor || canWriteBlinking ||
                effects.Count > 0)
            {
                output.Add(zone, new OpenRazerLightingZoneCapabilities(
                    zone, canReadBrightness, canWriteBrightness, canReadState, canWriteState,
                    canReadEffect, canWriteEffect, canReadColor, canWriteColor, canWriteBlinking,
                    effects));
            }
        }
        return output.ToFrozenDictionary();
    }

    private static IReadOnlyDictionary<OpenRazerLedZone, IReadOnlySet<OpenRazerLightingEffect>> GetLightingEffectsByZone(
        OpenRazerDeviceDefinition device)
    {
        var effects = new Dictionary<OpenRazerLedZone, HashSet<OpenRazerLightingEffect>>();
        foreach (var effect in Enum.GetValues<OpenRazerLightingEffect>())
        {
            if (effect is OpenRazerLightingEffect.SoftwareSpectrum or
                OpenRazerLightingEffect.SoftwareWave or
                OpenRazerLightingEffect.SoftwareFire or
                OpenRazerLightingEffect.SoftwareAudioMeter or
                OpenRazerLightingEffect.SoftwareWheel)
            {
                continue;
            }
            try
            {
                var zone = OpenRazerLightingProtocol.ResolveEffectZone(device, effect);
                Add(zone, effect);
            }
            catch (NotSupportedException)
            {
            }
        }

        foreach (var capability in device.SourceCapabilities)
        {
            if (!TryGetSourceZone(capability, out var zone) ||
                !TryGetSourceEffect(capability, out var effect) ||
                !CanBuild(() => OpenRazerLightingProtocol.CreateEffectRequests(device,
                    new OpenRazerLightingSettings(effect, Zone: zone))))
            {
                continue;
            }
            Add(zone, effect);
        }

        return effects.ToDictionary(
            item => item.Key,
            item => (IReadOnlySet<OpenRazerLightingEffect>)item.Value.ToFrozenSet())
            .ToFrozenDictionary();

        void Add(OpenRazerLedZone zone, OpenRazerLightingEffect effect)
        {
            if (!effects.TryGetValue(zone, out var zoneEffects))
            {
                zoneEffects = [];
                effects.Add(zone, zoneEffects);
            }
            zoneEffects.Add(effect);
        }
    }

    private static bool TryGetSourceEffect(string capability, out OpenRazerLightingEffect effect)
    {
        foreach (var sourceZone in SourceZones)
        {
            var prefix = $"set_{sourceZone.Name}_";
            if (!capability.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
            var suffix = capability[prefix.Length..];
            var parsed = suffix switch
            {
                "none" => OpenRazerLightingEffect.Off,
                "static" => OpenRazerLightingEffect.Static,
                "spectrum" => OpenRazerLightingEffect.Spectrum,
                "wave" => OpenRazerLightingEffect.Wave,
                "reactive" => OpenRazerLightingEffect.Reactive,
                "breath_random" => OpenRazerLightingEffect.BreathingRandom,
                "breath_mono" or "breath_single" => OpenRazerLightingEffect.BreathingSingle,
                "breath_dual" => OpenRazerLightingEffect.BreathingDual,
                _ => (OpenRazerLightingEffect?)null,
            };
            if (parsed is { } value)
            {
                effect = value;
                return true;
            }
        }
        effect = default;
        return false;
    }

    private static bool TryGetSourceZone(string capability, out OpenRazerLedZone zone)
    {
        foreach (var candidate in SourceZones)
        {
            if (capability.StartsWith($"get_{candidate.Name}_", StringComparison.Ordinal) ||
                capability.StartsWith($"set_{candidate.Name}_", StringComparison.Ordinal))
            {
                zone = candidate.Zone;
                return true;
            }
        }
        zone = default;
        return false;
    }

    private static bool HasSourceStateWrite(OpenRazerDeviceDefinition device, OpenRazerLedZone zone)
    {
        var name = SourceZones.SingleOrDefault(candidate => candidate.Zone == zone).Name;
        if (name is null)
        {
            return false;
        }
        return device.HasSourceCapability($"set_{name}_active") ||
            device.HasSourceCapability($"set_{name}_on");
    }

    private static bool HasSourceBrightness(OpenRazerDeviceDefinition device, OpenRazerLedZone zone, bool write)
    {
        var operation = write ? "set" : "get";
        if (zone == device.DefaultLedZone && device.HasSourceCapability($"{operation}_brightness"))
        {
            return true;
        }
        var name = SourceZones.SingleOrDefault(candidate => candidate.Zone == zone).Name;
        return name is not null && device.HasSourceCapability($"{operation}_{name}_brightness");
    }

    private static bool SupportsStorageAndZone(
        OpenRazerDeviceDefinition device,
        string builder,
        OpenRazerStorage storage,
        OpenRazerLedZone zone)
    {
        return device.BuilderArgumentPrefixes.TryGetValue(builder, out var prefixes) &&
            prefixes.Any(prefix =>
                prefix.Length >= 2 && prefix.Span[0] == (byte)storage && prefix.Span[1] == (byte)zone ||
                prefix.Length == 1 && prefix.Span[0] == (byte)storage);
    }

    private static bool CanUseClassicBuilder(
        OpenRazerDeviceDefinition device,
        string builder,
        OpenRazerLedZone zone) =>
        device.Transactions.ContainsKey(builder) &&
        device.BuilderArgumentPrefixes.TryGetValue(builder, out var prefixes) &&
        prefixes.Any(prefix => prefix.Length == 1 || prefix.Length >= 2 && prefix.Span[1] == (byte)zone);

    private static bool CanBuild(Func<object> create)
    {
        try
        {
            _ = create();
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static readonly (string Name, OpenRazerLedZone Zone)[] SourceZones =
    [
        ("logo", OpenRazerLedZone.Logo),
        ("scroll", OpenRazerLedZone.ScrollWheel),
        ("backlight", OpenRazerLedZone.Backlight),
        ("left", OpenRazerLedZone.LeftSide),
        ("right", OpenRazerLedZone.RightSide),
        ("charging", OpenRazerLedZone.Charging),
        ("fast_charging", OpenRazerLedZone.FastCharging),
        ("fully_charged", OpenRazerLedZone.FullyCharged),
    ];

    internal static IReadOnlySet<OpenRazerBackendCapability> GetCapabilities(OpenRazerDeviceDefinition device)
    {
        var result = new HashSet<OpenRazerBackendCapability>();
        var lightingZones = GetLightingZoneCapabilities(device).Values;
        Add(OpenRazerBackendCapability.FirmwareRead, "get_firmware", "razer_chroma_standard_get_firmware_version");
        // Only the two DeathAdder V4 Pro entries declare set_device_mode, so only they
        // publish a write. DeviceModeRead stays unpublished to avoid a new UI row on
        // every mouse that merely declares the GET.
        AddAnyBuilder(OpenRazerBackendCapability.DeviceModeWrite, "razer_chroma_standard_set_device_mode");
        Add(OpenRazerBackendCapability.BatteryRead, "get_battery", "razer_chroma_misc_get_battery_level");
        Add(OpenRazerBackendCapability.ChargingRead, "is_charging", "razer_chroma_misc_get_charging_status");
        AddAnyDeclared(OpenRazerBackendCapability.PollingRateRead, "get_poll_rate",
            "razer_chroma_misc_get_polling_rate", "razer_chroma_misc_get_polling_rate2");
        AddAnyDeclared(OpenRazerBackendCapability.PollingRateWrite, "set_poll_rate",
            "razer_chroma_misc_set_polling_rate", "razer_chroma_misc_set_polling_rate2");
        AddAnySource(OpenRazerBackendCapability.DpiRead, ["get_dpi_xy", "get_dpi_xy_byte"],
            "razer_chroma_misc_get_dpi_xy", "razer_chroma_misc_get_dpi_xy_byte");
        AddAnySource(OpenRazerBackendCapability.DpiWrite, ["set_dpi_xy", "set_dpi_xy_byte"],
            "razer_chroma_misc_set_dpi_xy", "razer_chroma_misc_set_dpi_xy_byte");
        Add(OpenRazerBackendCapability.DpiStagesRead, "get_dpi_stages", "razer_chroma_misc_get_dpi_stages");
        Add(OpenRazerBackendCapability.DpiStagesWrite, "set_dpi_stages", "razer_chroma_misc_set_dpi_stages");
        Add(OpenRazerBackendCapability.IdleTimeoutRead, "get_idle_time", "razer_chroma_misc_get_idle_time");
        Add(OpenRazerBackendCapability.IdleTimeoutWrite, "set_idle_time", "razer_chroma_misc_set_idle_time");
        Add(OpenRazerBackendCapability.LowBatteryThresholdRead, "get_low_battery_threshold", "razer_chroma_misc_get_low_battery_threshold");
        Add(OpenRazerBackendCapability.LowBatteryThresholdWrite, "set_low_battery_threshold", "razer_chroma_misc_set_low_battery_threshold");
        Add(OpenRazerBackendCapability.AutoSleepRead, "get_auto_sleep", "razer_chroma_misc_get_auto_sleep");
        Add(OpenRazerBackendCapability.AutoSleepWrite, "set_auto_sleep", "razer_chroma_misc_set_auto_sleep");
        Add(OpenRazerBackendCapability.BatteryChargeLimiterRead, "get_battery_charge_limiter", "razer_chroma_misc_get_battery_charge_limiter");
        Add(OpenRazerBackendCapability.BatteryChargeLimiterWrite, "set_battery_charge_limiter", "razer_chroma_misc_set_battery_charge_limiter");
        Add(OpenRazerBackendCapability.TimeToDimRead, "get_time_to_dim", "razer_chroma_misc_get_time_to_dim");
        Add(OpenRazerBackendCapability.TimeToDimWrite, "set_time_to_dim", "razer_chroma_misc_set_time_to_dim");
        Add(OpenRazerBackendCapability.IndicatorLedConfigurationRead, "get_indicator_led_configuration", "razer_chroma_misc_get_indicator_led_configuration");
        Add(OpenRazerBackendCapability.IndicatorLedConfigurationWrite, "set_indicator_led_configuration", "razer_chroma_misc_set_indicator_led_configuration");
        Add(OpenRazerBackendCapability.GpuCardDetectionRead, "get_gpu_card_detection", "razer_chroma_misc_get_gpu_card_detection");
        Add(OpenRazerBackendCapability.GpuCardDetectionWrite, "set_gpu_card_detection", "razer_chroma_misc_set_gpu_card_detection");
        Add(OpenRazerBackendCapability.ForceChromaCtrlRead, "get_force_chroma_ctrl", "razer_chroma_misc_get_force_chroma_ctrl");
        Add(OpenRazerBackendCapability.ForceChromaCtrlWrite, "set_force_chroma_ctrl", "razer_chroma_misc_set_force_chroma_ctrl");
        Add(OpenRazerBackendCapability.ProfileDebounceTimeRead, "get_profile_debounce_time", "razer_chroma_misc_get_profile_debounce_time");
        Add(OpenRazerBackendCapability.ProfileDebounceTimeWrite, "set_profile_debounce_time", "razer_chroma_misc_set_profile_debounce_time");
        Add(OpenRazerBackendCapability.ChromaTapRead, "get_chroma_tap", "razer_chroma_misc_get_chroma_tap");
        Add(OpenRazerBackendCapability.ChromaTapWrite, "set_chroma_tap", "razer_chroma_misc_set_chroma_tap");
        Add(OpenRazerBackendCapability.BatteryDischargeCurveRead, "get_battery_discharge_curve", "razer_chroma_misc_get_battery_discharge_curve");
        Add(OpenRazerBackendCapability.BatteryDischargeCurveWrite, "set_battery_discharge_curve", "razer_chroma_misc_set_battery_discharge_curve");
        Add(OpenRazerBackendCapability.MacroStorageInfoRead, "get_macro_storage_info", "razer_chroma_misc_get_macro_storage_info");
        Add(OpenRazerBackendCapability.LkScrollWheelModeRead, "get_lk_scroll_wheel_mode", "razer_chroma_misc_get_lk_scroll_wheel_mode");
        Add(OpenRazerBackendCapability.LkScrollWheelModeWrite, "set_lk_scroll_wheel_mode", "razer_chroma_misc_set_lk_scroll_wheel_mode");
        if (lightingZones.Any(zone => zone.CanReadBrightness)) result.Add(OpenRazerBackendCapability.BrightnessRead);
        if (lightingZones.Any(zone => zone.CanWriteBrightness)) result.Add(OpenRazerBackendCapability.BrightnessWrite);
        if (lightingZones.Any(zone => zone.CanReadState)) result.Add(OpenRazerBackendCapability.LedStateRead);
        if (lightingZones.Any(zone => zone.CanWriteState)) result.Add(OpenRazerBackendCapability.LedStateWrite);
        if (lightingZones.Any(zone => zone.CanReadEffect)) result.Add(OpenRazerBackendCapability.LedEffectRead);
        if (lightingZones.Any(zone => zone.CanWriteEffect)) result.Add(OpenRazerBackendCapability.LedEffectWrite);
        if (lightingZones.Any(zone => zone.CanWriteColor)) result.Add(OpenRazerBackendCapability.LedColorWrite);
        if (lightingZones.Any(zone => zone.CanWriteBlinking)) result.Add(OpenRazerBackendCapability.LedBlinkingWrite);
        if (lightingZones.Any(zone => zone.LightingEffects.Count > 0))
            result.Add(OpenRazerBackendCapability.LightingEffectWrite);
        if (device.MatrixDimensions is not null && device.HasSourceCapability("set_key_row"))
            AddAnyBuilder(OpenRazerBackendCapability.MatrixFrameWrite,
            "razer_chroma_standard_matrix_set_custom_frame",
            "razer_chroma_extended_matrix_set_custom_frame",
            "razer_chroma_extended_matrix_set_custom_frame2");
        Add(OpenRazerBackendCapability.ReactiveTriggerWrite, "trigger_reactive_effect", "razer_chroma_misc_matrix_reactive_trigger");
        Add(OpenRazerBackendCapability.ScrollModeRead, "get_scroll_mode", "razer_chroma_misc_get_scroll_mode");
        Add(OpenRazerBackendCapability.ScrollModeWrite, "set_scroll_mode", "razer_chroma_misc_set_scroll_mode");
        Add(OpenRazerBackendCapability.ScrollAccelerationRead, "get_scroll_acceleration", "razer_chroma_misc_get_scroll_acceleration");
        Add(OpenRazerBackendCapability.ScrollAccelerationWrite, "set_scroll_acceleration", "razer_chroma_misc_set_scroll_acceleration");
        Add(OpenRazerBackendCapability.SmartReelRead, "get_scroll_smart_reel", "razer_chroma_misc_get_scroll_smart_reel");
        Add(OpenRazerBackendCapability.SmartReelWrite, "set_scroll_smart_reel", "razer_chroma_misc_set_scroll_smart_reel");
        Add(OpenRazerBackendCapability.SmartReelThresholdRead, "get_scroll_smart_reel_threshold", "razer_chroma_misc_get_scroll_smart_reel_threshold");
        Add(OpenRazerBackendCapability.SmartReelThresholdWrite, "set_scroll_smart_reel_threshold", "razer_chroma_misc_set_scroll_smart_reel_threshold");
        Add(OpenRazerBackendCapability.KeyswitchOptimizationRead, "get_keyswitch_optimization", "razer_chroma_misc_get_keyswitch_optimization");
        if (device.HasSourceCapability("set_keyswitch_optimization") &&
            Has("razer_chroma_misc_set_keyswitch_optimization_command1") &&
            Has("razer_chroma_misc_set_keyswitch_optimization_command2"))
            result.Add(OpenRazerBackendCapability.KeyswitchOptimizationWrite);
        Add(OpenRazerBackendCapability.HyperPollingIndicatorWrite, "set_hyperpolling_wireless_dongle_indicator_led_mode",
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_indicator_led_mode");
        if (device.HasSourceCapability("set_hyperpolling_wireless_dongle_pair") &&
            Has("razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step1") &&
            Has("razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step2"))
            result.Add(OpenRazerBackendCapability.HyperPollingPairWrite);
        Add(OpenRazerBackendCapability.HyperPollingUnpairWrite, "set_hyperpolling_wireless_dongle_unpair",
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_unpair");
        return result.ToFrozenSet();

        bool Has(string builder) => device.Transactions.ContainsKey(builder) || device.CommandSequences.ContainsKey(builder);
        void Add(OpenRazerBackendCapability capability, string sourceCapability, string builder)
        {
            if (device.HasSourceCapability(sourceCapability) && Has(builder)) result.Add(capability);
        }
        void AddAnyDeclared(OpenRazerBackendCapability capability, string sourceCapability, params string[] builders)
        {
            if (device.HasSourceCapability(sourceCapability) && builders.Any(Has)) result.Add(capability);
        }
        void AddAnySource(OpenRazerBackendCapability capability, string[] sourceCapabilities, params string[] builders)
        {
            if (sourceCapabilities.Any(device.HasSourceCapability) && builders.Any(Has)) result.Add(capability);
        }
        void AddAnyBuilder(OpenRazerBackendCapability capability, params string[] builders)
        {
            if (builders.Any(Has)) result.Add(capability);
        }
    }

    private async Task<byte[]> ExecuteAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerRequest request,
        CancellationToken cancellationToken)
    {
        RequireReady(connection);
        // The transport log only records a request once it reaches the HID layer, so a
        // command dropped before that point would be invisible. Record the application
        // side as well to tell "never sent" apart from "sent and refused".
        WriteDiagnostic(connection.Definition.ProductId,
            $"send {request.BuilderName}: txn=0x{request.Report[2]:X2}, size={request.Report[6]}, class=0x{request.Report[7]:X2}, command=0x{request.Report[8]:X2}, args={Convert.ToHexString(request.Report.AsSpan(RazerFeatureReport.ArgumentsOffset, request.Report[6]))}");
        try
        {
            return await _transport.QueryPreparedAsync(
                connection.DevicePath!, request.Report, connection.Definition.DeviceWait,
                cancellationToken).ConfigureAwait(false);
        }
        catch (RazerCommandRejectedException exception)
        {
            throw new NotSupportedException(exception.Message, exception);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            _endpointCache.TryRemove(connection.InstanceId, out _);
            throw;
        }
    }

    private async Task ExecuteWithoutResultAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerRequest request,
        CancellationToken cancellationToken) =>
        _ = await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);

    private static void RequireReady(OpenRazerDeviceConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!connection.IsReady || string.IsNullOrWhiteSpace(connection.DevicePath))
        {
            throw new InvalidOperationException("The OpenRazer Windows feature endpoint is not resolved.");
        }
    }

    private static void RequireLightingZone(
        OpenRazerDeviceConnection connection,
        OpenRazerLedZone zone,
        Func<OpenRazerLightingZoneCapabilities, bool> predicate,
        string operation)
    {
        RequireReady(connection);
        if (!connection.LightingZones.TryGetValue(zone, out var capabilities) || !predicate(capabilities))
        {
            throw new NotSupportedException(
                $"OpenRazer device 1532:{connection.Definition.ProductId:X4} does not support {operation} on zone {zone}.");
        }
    }

    private static OpenRazerLedZone GetDefaultBrightnessZone(OpenRazerDeviceConnection connection, bool write)
    {
        var defaultZone = connection.Definition.DefaultLedZone;
        bool Supports(OpenRazerLightingZoneCapabilities capabilities) =>
            write ? capabilities.CanWriteBrightness : capabilities.CanReadBrightness;
        if (connection.LightingZones.TryGetValue(defaultZone, out var capabilities) && Supports(capabilities))
        {
            return defaultZone;
        }
        return connection.LightingZones.Values.Where(Supports).Select(item => item.Zone)
            .Order().FirstOrDefault(defaultZone);
    }

    private async Task<byte> GetLeviathanRegionAsync(
        OpenRazerDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        var request = OpenRazerStandardProtocol.Create(connection.Definition,
            "razer_chroma_extended_matrix_get_region_ids", 0x50, 0x0F, 0x80, []);
        return LeviathanV2XProtocol.ReadFirstRegion(
            await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false));
    }

    private static void RequireClassicLedOperation(
        OpenRazerDeviceConnection connection,
        OpenRazerStorage storage,
        OpenRazerLedZone zone,
        Func<OpenRazerLightingZoneCapabilities, bool> predicate,
        string operation,
        string builder)
    {
        RequireLightingZone(connection, zone, predicate, operation);
        if (!SupportsStorageAndZone(connection.Definition, builder, storage, zone))
        {
            throw new NotSupportedException(
                $"OpenRazer device 1532:{connection.Definition.ProductId:X4} does not support {operation} with storage {storage} on zone {zone}.");
        }
    }
}

/// <summary>
/// Formats one row of the HID command scan. The status byte is the whole contract: the
/// firmware answers 0x02 for a command it supports and 0x05 for one it does not.
/// </summary>
internal static class HidCommandScan
{
    internal static readonly byte[] ScanClasses = [0x00, 0x03, 0x07, 0x0D, 0x0C, 0x15, 0x1D];

    internal const string StatusLegend =
        "status legend: 0x02 success, 0x01 busy, 0x03 rejected, 0x05 not supported";

    internal static string FormatHeader(string displayName, ushort productId) =>
        $"# OpenSynapse HID command scan{Environment.NewLine}" +
        $"device: {displayName}{Environment.NewLine}" +
        $"pid: 1532:{productId:X4}{Environment.NewLine}" +
        "mode: read-only (device-to-host commands)" + Environment.NewLine +
        $"{StatusLegend}{Environment.NewLine}" +
        $"columns: class command status args{Environment.NewLine}";

    internal static string FormatRow(byte commandClass, byte commandId, byte[] response) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{commandClass:x2} {commandId:x2} {response[1]:x2} {Hex(response.AsSpan(RazerFeatureReport.ArgumentsOffset))}");

    internal static string Hex(ReadOnlySpan<byte> payload) =>
        Convert.ToHexString(payload[..Math.Min(payload.Length, 20)]).ToLowerInvariant();
}
