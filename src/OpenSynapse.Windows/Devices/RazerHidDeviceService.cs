using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.ComponentModel;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public enum RazerHidKeyswitchOptimization
{
    Typing,
    Gaming,
}

public sealed partial class RazerHidDeviceService
{
    // The middleware drives GPU card detection on LED class 0 (All_Region).
    private const byte GpuCardLedClassId = 0x00;
    // The middleware writes the LK scroll wheel mode for the base profile.
    private const byte LkScrollProfileId = 0x01;

    private readonly RazerHidDeviceCatalog _catalog;
    private readonly IRazerFeatureTransport _transport;
    private readonly LocalDiagnosticLog? _diagnosticLog;
    private readonly ConcurrentDictionary<string, string> _endpointCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly RazerHidRequestExecutor _executor;
    private readonly ConcurrentDictionary<(string DevicePath, string Probe), DateTimeOffset>
        _middlewareProbeCache = new();
    private static readonly TimeSpan MiddlewareProbePositiveTtl = TimeSpan.FromHours(12);
    // Middleware bundles increment the transaction id per exchange; the firmware only
    // matches it against the response, so one fixed value works for a single request.
    private const byte MiddlewareTransactionId = 0x1F;
    private static readonly IReadOnlySet<RazerHidBackendCapability> MappingCapabilities =
        new HashSet<RazerHidBackendCapability>
        {
            RazerHidBackendCapability.MappingRead,
            RazerHidBackendCapability.MappingWrite,
        }.ToFrozenSet();
    private static readonly IReadOnlySet<RazerHidBackendCapability> ScrollWheelCapabilities =
        new HashSet<RazerHidBackendCapability>
        {
            RazerHidBackendCapability.ScrollFreeNotchedModeRead,
            RazerHidBackendCapability.ScrollFreeNotchedModeWrite,
            RazerHidBackendCapability.ScrollAccelerationLevelRead,
            RazerHidBackendCapability.ScrollAccelerationLevelWrite,
        }.ToFrozenSet();

    public event Action<DeviceBatteryReading>? BatteryObserved;

    public RazerHidDeviceService(LocalDiagnosticLog? diagnosticLog = null)
        : this(RazerHidDeviceCatalog.BuiltIn, new RazerFeatureTransport(diagnosticLog), diagnosticLog)
    {
    }

    internal RazerHidDeviceService(
        RazerHidDeviceCatalog catalog,
        IRazerFeatureTransport transport,
        LocalDiagnosticLog? diagnosticLog = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _diagnosticLog = diagnosticLog;
        _executor = new RazerHidRequestExecutor(transport, _endpointCache, WriteDiagnostic);
    }

    public LeviathanV2XAudioService? CreateLeviathanV2XAudio(RazerHidDeviceConnection connection)
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

    public async Task<RazerHidBasicState> ReadBasicStateAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default,
        IReadOnlySet<RazerHidBackendCapability>? excludedCapabilities = null)
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
        RazerHidSensorRotation? sensorRotation = null;
        int? idle = null;
        int? threshold = null;
        bool? autoSleep = null;
        byte? chargeLimiterLevel = null;
        bool? chargeLimiterEnabled = null;
        bool? timeToDimEnabled = null;
        int? timeToDimSeconds = null;
        byte? timeToDimPercent = null;
        byte? indicatorLedConfiguration = null;
        RazerHidIndicatorLedConfigurationV2? indicatorLedConfigurationV2 = null;
        bool? gpuCardDetection = null;
        bool? forceChromaCtrl = null;
        RazerHidProfileDebounce? profileDebounce = null;
        RazerHidChromaTap? chromaTap = null;
        byte? batteryDischargeCurve = null;
        RazerHidMacroStorageInfo? macroStorageInfo = null;
        byte? lkScrollWheelModeMask = null;
        byte? brightness = null;
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var unsupported = new HashSet<RazerHidBackendCapability>();

        async Task ReadAsync<T>(RazerHidBackendCapability capability, string key, Func<Task<T>> read, Action<T> assign)
        {
            if (!connection.Capabilities.Contains(capability) || excludedCapabilities?.Contains(capability) == true)
            {
                return;
            }
            try
            {
                assign(await read().ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or COMException or
                InvalidOperationException or NotSupportedException or InvalidDataException)
            {
                if (exception is RazerDeviceTimeoutException { IsRetryableBusy: true })
                {
                    // Busy is transient. Keep the capability enabled so the next refresh retries it.
                    errors[capability.ToString()] = exception.Message;
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"state read '{key}' is busy and will retry: {exception.Message}");
                    return;
                }

                if (exception is RazerDeviceTimeoutException)
                {
                    // A silent device is transient; retry on the next poll instead of hiding the
                    // capability until the next scan.
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"state read '{key}' timed out and will retry: {exception.Message}");
                    return;
                }

                if (exception is NotSupportedException or InvalidDataException or COMException)
                {
                    // The firmware rejected the transaction; do not offer that capability.
                    unsupported.Add(capability);
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"optional state read '{key}' is unavailable: {exception.GetType().Name}: {exception.Message}");
                    return;
                }
                // Key the failure by capability so it lines up with the protocol detail row.
                errors[capability.ToString()] = exception.Message;
                WriteDiagnostic(connection.Definition.ProductId,
                    $"state read '{key}' failed: {exception.GetType().Name}: {exception.Message}");
            }
        }

        await ReadAsync(RazerHidBackendCapability.FirmwareRead, "firmware",
            () => GetFirmwareAsync(connection, cancellationToken), value => firmware = value);
        if (excludedCapabilities?.Contains(RazerHidBackendCapability.SerialRead) != true)
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
                catch (Exception exception) when (exception is Win32Exception or IOException or COMException or
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
        await ReadAsync(RazerHidBackendCapability.DeviceModeRead, "deviceMode",
            () => GetSoftwareModeAsync(connection, cancellationToken), value => softwareMode = value);
        await ReadAsync(RazerHidBackendCapability.BatteryRead, "battery",
            () => GetBatteryPercentAsync(connection, cancellationToken), value => battery = value);
        await ReadAsync(RazerHidBackendCapability.ChargingRead, "charging",
            () => GetChargingAsync(connection, cancellationToken), value => charging = value);
        await ReadAsync(RazerHidBackendCapability.PollingRateRead, "pollingRate",
            () => GetPollingRateAsync(connection, cancellationToken), value => pollingRate = value);
        await ReadAsync(RazerHidBackendCapability.DpiRead, "dpi",
            () => GetDpiAsync(connection, cancellationToken), value => (dpiX, dpiY) = value);
        await ReadAsync(RazerHidBackendCapability.SensorRotationRead, "sensorRotation",
            () => GetSensorRotationAsync(connection, cancellationToken), value => sensorRotation = value);
        if (unsupported.Contains(RazerHidBackendCapability.SensorRotationRead))
        {
            // The write only makes sense once the same profile answered a read.
            unsupported.Add(RazerHidBackendCapability.SensorRotationWrite);
        }
        await ReadAsync(RazerHidBackendCapability.IdleTimeoutRead, "idleTimeout",
            () => GetIdleTimeoutAsync(connection, cancellationToken), value => idle = value);
        await ReadAsync(RazerHidBackendCapability.LowBatteryThresholdRead, "lowBatteryThreshold",
            () => GetLowBatteryThresholdAsync(connection, cancellationToken), value => threshold = value);
        await ReadAsync(RazerHidBackendCapability.AutoSleepRead, "autoSleep",
            () => GetAutoSleepAsync(connection, cancellationToken), value => autoSleep = value);
        await ReadAsync(RazerHidBackendCapability.BatteryChargeLimiterRead, "batteryChargeLimiter",
            () => GetBatteryChargeLimiterAsync(connection, cancellationToken), value => (chargeLimiterLevel, chargeLimiterEnabled) = value);
        await ReadAsync(RazerHidBackendCapability.TimeToDimRead, "timeToDim",
            () => GetTimeToDimAsync(connection, cancellationToken), value =>
            {
                timeToDimEnabled = value.Enabled;
                timeToDimSeconds = value.Seconds;
                timeToDimPercent = value.Percentage;
            });
        await ReadAsync(RazerHidBackendCapability.IndicatorLedConfigurationRead, "indicatorLedConfiguration",
            () => GetIndicatorLedConfigurationAsync(connection, cancellationToken), value => indicatorLedConfiguration = value);
        await ReadAsync(RazerHidBackendCapability.IndicatorLedConfigurationV2Read, "indicatorLedConfigurationV2",
            () => GetIndicatorLedConfigurationV2Async(connection, cancellationToken), value => indicatorLedConfigurationV2 = value);
        await ReadAsync(RazerHidBackendCapability.GpuCardDetectionRead, "gpuCardDetection",
            () => GetGpuCardDetectionAsync(connection, cancellationToken), value => gpuCardDetection = value.Enabled);
        await ReadAsync(RazerHidBackendCapability.ForceChromaCtrlRead, "forceChromaCtrl",
            () => GetForceChromaCtrlAsync(connection, cancellationToken), value => forceChromaCtrl = value);
        await ReadAsync(RazerHidBackendCapability.ProfileDebounceTimeRead, "profileDebounce",
            () => GetProfileDebounceTimeAsync(connection, cancellationToken), value => profileDebounce = value);
        await ReadAsync(RazerHidBackendCapability.ChromaTapRead, "chromaTap",
            () => GetChromaTapAsync(connection, cancellationToken), value => chromaTap = value);
        await ReadAsync(RazerHidBackendCapability.BatteryDischargeCurveRead, "batteryDischargeCurve",
            () => GetBatteryDischargeCurveAsync(connection, cancellationToken), value => batteryDischargeCurve = value);
        await ReadAsync(RazerHidBackendCapability.MacroStorageInfoRead, "macroStorageInfo",
            () => GetMacroStorageInfoAsync(connection, cancellationToken), value => macroStorageInfo = value);
        await ReadAsync(RazerHidBackendCapability.LkScrollWheelModeRead, "lkScrollWheelMode",
            () => GetLkScrollWheelModeAsync(connection, cancellationToken), value => lkScrollWheelModeMask = value.ModeMask);
        await ReadAsync(RazerHidBackendCapability.BrightnessRead, "brightness",
            () => GetBrightnessAsync(connection, cancellationToken: cancellationToken), value => brightness = value);

        if (charging is not null ||
            (battery is not null && !errors.ContainsKey(nameof(RazerHidBackendCapability.ChargingRead))))
        {
            // A charging read that answered is useful without a battery percentage.
            // A failed charging read must not be reported as discharging.
            BatteryObserved?.Invoke(new DeviceBatteryReading(
                $"openrazer:{connection.InstanceId}", connection.Definition.DisplayName, battery, charging));
        }
        return new RazerHidBasicState(firmware, serial, softwareMode, battery, charging,
            pollingRate, dpiX, dpiY, idle, threshold, autoSleep,
            chargeLimiterEnabled, chargeLimiterLevel,
            timeToDimEnabled, timeToDimSeconds, timeToDimPercent,
            indicatorLedConfiguration, gpuCardDetection, forceChromaCtrl,
            profileDebounce, chromaTap, batteryDischargeCurve, macroStorageInfo,
            lkScrollWheelModeMask,
            brightness, errors,
            unsupported.ToFrozenSet(), indicatorLedConfigurationV2, sensorRotation);
    }

    /// <summary>
    /// Sweeps the read half of the HID feature command space for commands this catalog does not describe.
    /// The firmware answers 0x02 for a supported command and 0x05 for an unsupported one, so
    /// the sweep reveals a device's real capability table without sending state-changing writes.
    /// </summary>
    public async Task<string> ScanCommandSpaceAsync(
        RazerHidDeviceConnection connection,
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

    public async Task<Version> GetFirmwareAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            return RazerHidStandardProtocol.ParseFirmware(await ExecuteAsync(connection,
                RazerHidStandardProtocol.GetFirmware(connection.Definition), cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is RazerDeviceTimeoutException or RazerCommandRejectedException or
            RazerReportMismatchException or NotSupportedException)
        {
            WriteDiagnostic(connection.Definition.ProductId,
                $"firmware fallback after {DescribeProbeFailure(exception)}");
            return RazerHidStandardProtocol.ParseFirmware(await ExecuteAsync(connection,
                RazerHidStandardProtocol.GetFirmwareFallback(connection.Definition), cancellationToken).ConfigureAwait(false));
        }
    }

    public async Task<string> GetSerialAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidStandardProtocol.ParseSerial(await ExecuteAsync(connection,
            RazerHidStandardProtocol.GetSerial(connection.Definition), cancellationToken).ConfigureAwait(false));

    public async Task<bool> GetSoftwareModeAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(connection,
            RazerHidStandardProtocol.GetDeviceMode(connection.Definition), cancellationToken).ConfigureAwait(false);
        return RazerHidStandardProtocol.Arguments(response, 2)[0] == 0x03;
    }

    /// <summary>
    /// Puts a device that is in normal mode back into driver mode. A mouse left in normal
    /// mode never joins its 2.4 GHz link, so the wireless connection stays dead until the
    /// mode is written back. Only normal mode is repaired: an unknown mode is left alone
    /// rather than overwritten blind.
    /// </summary>
    public async Task<bool> EnsureDriverModeAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        if (!connection.Capabilities.Contains(RazerHidBackendCapability.DeviceModeWrite))
        {
            return false;
        }

        var before = RazerHidStandardProtocol.ParseDeviceMode(await ExecuteAsync(connection,
            RazerHidStandardProtocol.GetDeviceMode(connection.Definition), cancellationToken).ConfigureAwait(false));
        if (before != RazerHidStandardProtocol.NormalDeviceMode)
        {
            // 0x03 is already correct; 0x02 and anything else is not ours to overwrite.
            return before == RazerHidStandardProtocol.DriverDeviceMode;
        }

        WriteDiagnostic(connection.Definition.ProductId,
            $"device mode was 0x{before:X2}; writing driver mode to restore the 2.4 GHz link");
        await ExecuteWithoutResultAsync(connection,
            RazerHidStandardProtocol.SetDriverDeviceMode(connection.Definition), cancellationToken)
            .ConfigureAwait(false);

        var after = RazerHidStandardProtocol.ParseDeviceMode(await ExecuteAsync(connection,
            RazerHidStandardProtocol.GetDeviceMode(connection.Definition), cancellationToken).ConfigureAwait(false));
        WriteDiagnostic(connection.Definition.ProductId, $"device mode is now 0x{after:X2}");
        return after == RazerHidStandardProtocol.DriverDeviceMode;
    }

    public async Task<int> GetBatteryPercentAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseBatteryPercent(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetBattery(connection.Definition), cancellationToken).ConfigureAwait(false));

    public async Task<bool> GetChargingAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseCharging(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetCharging(connection.Definition), cancellationToken).ConfigureAwait(false));

    public async Task<int> GetPollingRateAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        var highRate = connection.Definition.Transactions.ContainsKey("razer_chroma_misc_get_polling_rate2");
        var profileId = await ReadActiveProfileIdAsync(connection, cancellationToken).ConfigureAwait(false) ?? 0x00;
        return RazerHidMouseProtocol.ParsePollingRate(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetPollingRate(connection.Definition, profileId), cancellationToken).ConfigureAwait(false), highRate);
    }

    /// <summary>
    /// Writes the polling rate and reports the rate the firmware holds afterwards. The
    /// device answers a successful write without echoing the payload, so the value read
    /// back is the only evidence that the setting landed.
    /// </summary>
    /// <returns>The reported rate, or null when the device cannot be read back.</returns>
    public async Task<int?> SetPollingRateAsync(RazerHidDeviceConnection connection, int hertz, CancellationToken cancellationToken = default)
    {
        var profileId = await ReadActiveProfileIdAsync(connection, cancellationToken).ConfigureAwait(false) ?? 0x00;
        foreach (var request in RazerHidMouseProtocol.SetPollingRate(connection.Definition, hertz, profileId))
        {
            await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }

        if (!connection.Capabilities.Contains(RazerHidBackendCapability.PollingRateRead))
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

    private async Task<byte?> ReadActiveProfileIdAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        var request = RazerHidMouseProtocol.GetActiveProfile(connection.Definition);
        if (request is null)
        {
            return null;
        }

        try
        {
            var response = await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
            return response[RazerFeatureReport.ArgumentsOffset];
        }
        catch (Exception exception) when (exception is RazerDeviceTimeoutException or RazerCommandRejectedException or NotSupportedException or InvalidDataException)
        {
            // The write can still be attempted with the default profile; a read failure
            // must not turn a working polling-rate write into a no-op.
            return null;
        }
    }

    public async Task<(int X, int Y)> GetDpiAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        var definition = connection.Definition;
        var response = await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetDpi(definition), cancellationToken).ConfigureAwait(false);
        var dpi = definition.UsesP1DpiScale
            ? RazerHidStandardProtocol.ParseP1Dpi(response)
            : RazerHidMouseProtocol.ParseDpi(response,
                definition.Transactions.ContainsKey("razer_chroma_misc_get_dpi_xy_byte"));
        return dpi.X > 0 && dpi.Y > 0 ? dpi : throw new InvalidDataException("The DPI response contained no values.");
    }

    /// <summary>
    /// Writes the DPI and reports the values the firmware holds afterwards, so a caller
    /// can tell a stored setting from a write the device ignored.
    /// </summary>
    /// <returns>The reported DPI, or null when the device cannot be read back.</returns>
    public async Task<(int X, int Y)?> SetDpiAsync(RazerHidDeviceConnection connection, int x, int y, CancellationToken cancellationToken = default)
    {
        if (!connection.Definition.SupportsIndependentDpi)
        {
            y = x;
        }
        await ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetDpi(connection.Definition, x, y), cancellationToken).ConfigureAwait(false);

        if (!connection.Capabilities.Contains(RazerHidBackendCapability.DpiRead))
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

    public Task SetDpiStagesAsync(RazerHidDeviceConnection connection, RazerHidDpiStages state, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetDpiStages(connection.Definition, state), cancellationToken);

    public async Task<RazerHidDpiStages> GetDpiStagesAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        var request = RazerHidMouseProtocol.GetDpiStages(connection.Definition);
        var response = await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
        try
        {
            return RazerHidMouseProtocol.ParseDpiStages(connection.Definition, response);
        }
        catch (InvalidDataException exception)
        {
            WriteDiagnostic(connection.Definition.ProductId,
                $"DPI stages response rejected: {exception.Message}; report={Convert.ToHexString(response)}");
            throw;
        }
    }

    public async Task<int> GetIdleTimeoutAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseIdleSeconds(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetIdleTime(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetIdleTimeoutAsync(RazerHidDeviceConnection connection, int seconds, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetIdleTime(connection.Definition, seconds), cancellationToken);

    public async Task<int> GetLowBatteryThresholdAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseLowBatteryThreshold(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetLowBatteryThreshold(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetLowBatteryThresholdAsync(RazerHidDeviceConnection connection, int percent, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetLowBatteryThreshold(connection.Definition, percent), cancellationToken);

    public async Task<bool> GetAutoSleepAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseAutoSleepEnabled(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetAutoSleep(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetAutoSleepAsync(RazerHidDeviceConnection connection, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetAutoSleep(connection.Definition, enabled), cancellationToken);

    public async Task<(byte Level, bool Enabled)> GetBatteryChargeLimiterAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseBatteryChargeLimiter(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetBatteryChargeLimiter(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetBatteryChargeLimiterAsync(
        RazerHidDeviceConnection connection, byte level, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetBatteryChargeLimiter(connection.Definition, level, enabled), cancellationToken);

    public async Task<RazerHidTimeToDim> GetTimeToDimAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseTimeToDim(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetTimeToDim(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetTimeToDimAsync(
        RazerHidDeviceConnection connection,
        bool enabled,
        int seconds,
        byte percentage,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetTimeToDim(connection.Definition, 0, enabled, seconds, percentage),
            cancellationToken);

    public async Task<byte> GetIndicatorLedConfigurationAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseIndicatorLedConfiguration(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetIndicatorLedConfiguration(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetIndicatorLedConfigurationAsync(
        RazerHidDeviceConnection connection, byte configuration, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetIndicatorLedConfiguration(connection.Definition, configuration), cancellationToken);

    public async Task<RazerHidIndicatorLedConfigurationV2> GetIndicatorLedConfigurationV2Async(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidDongleIndicatorProtocol.Parse(await ExecuteAsync(connection,
            RazerHidDongleIndicatorProtocol.Get(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetIndicatorLedConfigurationV2Async(
        RazerHidDeviceConnection connection,
        RazerHidIndicatorLedConfigurationV2 configuration,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidDongleIndicatorProtocol.Set(connection.Definition, configuration), cancellationToken);

    public async Task<RazerHidGpuCardDetection> GetGpuCardDetectionAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseGpuCardDetection(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetGpuCardDetection(connection.Definition, GpuCardLedClassId), cancellationToken).ConfigureAwait(false));

    public Task SetGpuCardDetectionAsync(
        RazerHidDeviceConnection connection, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetGpuCardDetection(connection.Definition, GpuCardLedClassId, enabled ? (byte)1 : (byte)0),
            cancellationToken);

    public async Task<bool> GetForceChromaCtrlAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseForceChromaCtrl(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetForceChromaCtrl(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetForceChromaCtrlAsync(
        RazerHidDeviceConnection connection, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetForceChromaCtrl(connection.Definition, enabled ? (byte)1 : (byte)0),
            cancellationToken);

    public async Task<RazerHidProfileDebounce> GetProfileDebounceTimeAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseProfileDebounceTime(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetProfileDebounceTime(connection.Definition, 0), cancellationToken).ConfigureAwait(false));

    public Task SetProfileDebounceTimeAsync(
        RazerHidDeviceConnection connection,
        byte makeDebounceTimeMs,
        byte makeMaskTimeMs,
        byte breakDebounceTimeMs,
        byte breakMaskTimeMs,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetProfileDebounceTime(connection.Definition, 0,
                makeDebounceTimeMs, makeMaskTimeMs, breakDebounceTimeMs, breakMaskTimeMs),
            cancellationToken);

    public async Task<RazerHidChromaTap> GetChromaTapAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseChromaTap(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetChromaTap(connection.Definition, 0), cancellationToken).ConfigureAwait(false));

    public Task SetChromaTapAsync(
        RazerHidDeviceConnection connection,
        byte status,
        byte tapMode,
        byte functionId,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetChromaTap(connection.Definition, 0, status, tapMode, functionId),
            cancellationToken);

    public async Task<byte> GetBatteryDischargeCurveAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseBatteryDischargeCurve(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetBatteryDischargeCurve(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetBatteryDischargeCurveAsync(
        RazerHidDeviceConnection connection, byte chemistry, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetBatteryDischargeCurve(connection.Definition, chemistry), cancellationToken);

    public async Task<RazerHidMacroStorageInfo> GetMacroStorageInfoAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseMacroStorageInfo(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetMacroStorageInfo(connection.Definition), cancellationToken).ConfigureAwait(false));

    public async Task<RazerHidLkScrollWheelMode> GetLkScrollWheelModeAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseLkScrollWheelMode(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetLkScrollWheelMode(connection.Definition, LkScrollProfileId), cancellationToken).ConfigureAwait(false));

    public Task SetLkScrollWheelModeAsync(
        RazerHidDeviceConnection connection, byte modeMask, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetLkScrollWheelMode(connection.Definition, modeMask, LkScrollProfileId), cancellationToken);

    public async Task<IReadOnlyList<byte>> GetMappingButtonIdsAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return RazerHidMappingProtocol.ParseButtonList(await ExecuteAsync(connection,
                RazerHidMappingProtocol.GetButtonList(), cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is NotSupportedException or RazerDeviceTimeoutException)
        {
            // Razer's own UI never asks this GET for every product; some firmware only
            // answers the assignment table. The caller falls back to the product bundle ids.
            return [];
        }
    }

    public async Task<byte> GetBrightnessAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage? storage = null,
        RazerHidLedZone? ledId = null,
        CancellationToken cancellationToken = default)
    {
        var selectedZone = ledId ?? (storage is null || storage == connection.Definition.DefaultStorage
            ? GetDefaultBrightnessZone(connection, write: false) : null);
        var zone = RazerHidLightingProtocol.ResolveBrightnessZone(connection.Definition, write: false,
            storage, selectedZone);
        RequireLightingZone(connection, zone, capability => capability.CanReadBrightness, "brightness read");
        if (connection.Definition.ProductId == LeviathanV2XProtocol.ProductId)
        {
            var region = await GetLeviathanRegionAsync(connection, cancellationToken).ConfigureAwait(false);
            var leviathanRequest = RazerHidStandardProtocol.Create(connection.Definition,
                "razer_chroma_extended_matrix_get_brightness", 0x03, 0x0F, 0x84,
                [(byte)RazerHidStorage.Temporary, region]);
            var leviathanArguments = RazerHidStandardProtocol.Arguments(await ExecuteAsync(connection,
                leviathanRequest, cancellationToken).ConfigureAwait(false), 3);
            return leviathanArguments[2];
        }

        const string extendedBrightnessRead = "razer_chroma_extended_matrix_get_brightness";
        var request = RazerHidLightingProtocol.GetBrightness(connection.Definition, storage, zone);
        byte[] response;
        try
        {
            response = await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            (exception is NotSupportedException or RazerDeviceTimeoutException) &&
            zone == RazerHidLedZone.All &&
            request.BuilderName == extendedBrightnessRead)
        {
            // Some firmware answers the all-LED selector with busy/silent; a concrete zone still answers.
            var fallback = connection.LightingZones.Values
                .Where(item => item.Zone != RazerHidLedZone.All && item.CanReadBrightness)
                .MinBy(item => item.Zone);
            if (fallback is null) throw;
            request = RazerHidLightingProtocol.GetBrightness(connection.Definition, storage, fallback.Zone);
            response = await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }
        var arguments = RazerHidStandardProtocol.Arguments(response,
            request.BuilderName == "razer_chroma_misc_get_dock_brightness" ? (byte)1 : (byte)2);
        return request.BuilderName switch
        {
            "razer_chroma_misc_get_dock_brightness" => arguments[0],
            "razer_chroma_misc_get_blade_brightness" => arguments[1],
            _ => arguments[2],
        };
    }

    public async Task SetBrightnessAsync(
        RazerHidDeviceConnection connection,
        byte brightness,
        RazerHidStorage? storage = null,
        RazerHidLedZone? ledId = null,
        CancellationToken cancellationToken = default)
    {
        var selectedZone = ledId ?? (storage is null || storage == connection.Definition.DefaultStorage
            ? GetDefaultBrightnessZone(connection, write: true) : null);
        var zone = RazerHidLightingProtocol.ResolveBrightnessZone(connection.Definition, write: true,
            storage, selectedZone);
        RequireLightingZone(connection, zone, capability => capability.CanWriteBrightness, "brightness write");
        if (connection.Definition.ProductId == LeviathanV2XProtocol.ProductId)
        {
            var region = await GetLeviathanRegionAsync(connection, cancellationToken).ConfigureAwait(false);
            await ExecuteWithoutResultAsync(connection, RazerHidStandardProtocol.Create(connection.Definition,
                "razer_chroma_extended_matrix_brightness", 0x03, 0x0F, 0x04,
                [(byte)RazerHidStorage.Temporary, region, brightness]), cancellationToken).ConfigureAwait(false);
            return;
        }

        await ExecuteWithoutResultAsync(connection,
            RazerHidLightingProtocol.SetBrightness(connection.Definition, storage, zone, brightness), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetLightingAsync(
        RazerHidDeviceConnection connection,
        RazerHidLightingSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (connection.Definition.ProductId == LeviathanV2XProtocol.ProductId)
        {
            // The soundbar's effect write needs the region it reports, not the enum's All
            // placeholder, so it bypasses the RazerHid zone resolution below.
            var region = await GetLeviathanRegionAsync(connection, cancellationToken).ConfigureAwait(false);
            await ExecuteWithoutResultAsync(connection,
                new RazerHidRequest("razer_chroma_extended_matrix_effect_leviathan",
                    LeviathanV2XProtocol.CreateEffect(region, settings.Effect, settings.Direction,
                        settings.Primary, settings.Secondary)),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var zone = RazerHidLightingProtocol.ResolveEffectZone(
            connection.Definition, settings.Effect, settings.Storage, settings.Zone);
        RequireLightingZone(connection, zone,
            capability => capability.LightingEffects.Contains(settings.Effect), "lighting effect");
        foreach (var request in RazerHidLightingProtocol.CreateEffectRequests(connection.Definition, settings))
        {
            await ExecuteWithoutResultAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task SetCustomRowAsync(
        RazerHidDeviceConnection connection,
        byte row,
        byte startColumn,
        IReadOnlyList<RazerHidColor> colors,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidLightingProtocol.SetCustomRow(connection.Definition, row, startColumn, colors), cancellationToken);

    public async Task SetCustomFrameAsync(
        RazerHidDeviceConnection connection,
        IReadOnlyList<RazerHidColor> frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var dimensions = connection.Definition.MatrixDimensions ??
            throw new NotSupportedException("The RazerHid device does not expose matrix dimensions.");
        if (!connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite) ||
            frame.Count != dimensions.Rows * dimensions.Columns)
        {
            throw new NotSupportedException(
                $"RazerHid device 1532:{connection.Definition.ProductId:X4} does not support this matrix frame.");
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

    /// <summary>
    /// Matrix rows only display while the device is on its custom-frame effect.
    /// A firmware effect otherwise keeps running and the row writes are ignored.
    /// </summary>
    public async Task SetCustomFrameEffectAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!connection.Definition.HasSourceCapability("set_custom_effect") ||
            !connection.LightingZones.Values.Any(zone =>
                zone.LightingEffects.Contains(RazerHidLightingEffect.Custom)))
        {
            return;
        }

        await SetLightingAsync(
            connection,
            new RazerHidLightingSettings(RazerHidLightingEffect.Custom),
            cancellationToken).ConfigureAwait(false);
    }


    public Task SetLedStateAsync(RazerHidDeviceConnection connection, RazerHidStorage storage, RazerHidLedZone ledId, bool enabled, CancellationToken cancellationToken = default)
    {
        RequireLightingZone(connection, ledId, capability => capability.CanWriteState, "LED state write");
        if (!SupportsStorageAndZone(connection.Definition,
            "razer_chroma_standard_set_led_state", storage, ledId))
        {
            throw new NotSupportedException(
                $"RazerHid device 1532:{connection.Definition.ProductId:X4} does not support LED state storage {storage} on zone {ledId}.");
        }
        return ExecuteWithoutResultAsync(connection,
            RazerHidStandardProtocol.SetLedState(connection.Definition, (byte)storage, (byte)ledId, enabled), cancellationToken);
    }

    public async Task<bool> GetLedStateAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone ledId,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanReadState, "LED state read",
            "razer_chroma_standard_get_led_state");
        return RazerHidStandardProtocol.ParseLedState(await ExecuteAsync(connection,
            RazerHidStandardProtocol.GetLedState(connection.Definition, (byte)storage, (byte)ledId),
            cancellationToken).ConfigureAwait(false), (byte)storage, (byte)ledId);
    }

    public async Task<RazerHidLightingEffect?> GetLedEffectAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone ledId,
        CancellationToken cancellationToken = default)
    {
        if (connection.Definition.ProductId == LeviathanV2XProtocol.ProductId)
        {
            var region = await GetLeviathanRegionAsync(connection, cancellationToken).ConfigureAwait(false);
            var effect = LeviathanV2XProtocol.ReadEffect(await ExecuteAsync(connection,
                new RazerHidRequest("razer_chroma_extended_matrix_effect_leviathan_read",
                    LeviathanV2XProtocol.CreateGetEffect(LeviathanV2XProtocol.ReadProfile, region)),
                cancellationToken).ConfigureAwait(false), out _, out _);
            return effect switch
            {
                0x00 => RazerHidLightingEffect.Off,
                0x01 => RazerHidLightingEffect.Static,
                0x02 => RazerHidLightingEffect.BreathingDual,
                0x03 => RazerHidLightingEffect.Spectrum,
                0x04 => RazerHidLightingEffect.Wave,
                _ => null,
            };
        }

        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanReadEffect, "classic LED effect read",
            "razer_chroma_standard_get_led_effect");
        return RazerHidStandardProtocol.ParseLedEffect(await ExecuteAsync(connection,
            RazerHidStandardProtocol.GetLedEffect(connection.Definition, (byte)storage, (byte)ledId),
            cancellationToken).ConfigureAwait(false), (byte)storage, (byte)ledId) switch
        {
            RazerHidClassicLedEffect.Static => RazerHidLightingEffect.Static,
            RazerHidClassicLedEffect.Blinking => RazerHidLightingEffect.Blinking,
            RazerHidClassicLedEffect.Breathing => RazerHidLightingEffect.BreathingSingle,
            RazerHidClassicLedEffect.Spectrum => RazerHidLightingEffect.Spectrum,
            _ => null,
        };
    }

    public Task SetLedEffectAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone ledId,
        RazerHidClassicLedEffect effect,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanWriteEffect, "classic LED effect write",
            "razer_chroma_standard_set_led_effect");
        return ExecuteWithoutResultAsync(connection,
            RazerHidStandardProtocol.SetLedEffect(connection.Definition, (byte)storage, (byte)ledId, effect),
            cancellationToken);
    }

    public async Task<RazerHidColor> GetLedColorAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone ledId,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanReadColor, "LED color read",
            "razer_chroma_standard_get_led_rgb");
        return RazerHidStandardProtocol.ParseLedColor(await ExecuteAsync(connection,
            RazerHidStandardProtocol.GetLedColor(connection.Definition, (byte)storage, (byte)ledId),
            cancellationToken).ConfigureAwait(false), (byte)storage, (byte)ledId);
    }

    public Task SetLedColorAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone ledId,
        RazerHidColor color,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanWriteColor, "LED color write",
            "razer_chroma_standard_set_led_rgb");
        return ExecuteWithoutResultAsync(connection,
            RazerHidStandardProtocol.SetLedColor(connection.Definition, (byte)storage, (byte)ledId, color),
            cancellationToken);
    }

    public Task SetLedBlinkingAsync(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone ledId,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanWriteBlinking, "LED blinking write",
            "razer_chroma_standard_set_led_blinking");
        return ExecuteWithoutResultAsync(connection,
            RazerHidStandardProtocol.SetLedBlinking(connection.Definition, (byte)storage, (byte)ledId),
            cancellationToken);
    }

    public Task TriggerReactiveAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection, RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_matrix_reactive_trigger", 0x05, 0x03, 0x0A,
            [0x02, 0x00, 0x00, 0x00, 0x00]), cancellationToken);

    public async Task<byte> GetScrollModeAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseSecondArgument(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetScrollMode(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetScrollModeAsync(RazerHidDeviceConnection connection, byte mode, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetScrollMode(connection.Definition, mode), cancellationToken);

    public async Task<bool> GetScrollAccelerationAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseBooleanSecondArgument(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetScrollAcceleration(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetScrollAccelerationAsync(RazerHidDeviceConnection connection, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetScrollAcceleration(connection.Definition, enabled), cancellationToken);

    public async Task<bool> GetSmartReelAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseBooleanSecondArgument(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetSmartReel(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetSmartReelAsync(RazerHidDeviceConnection connection, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetSmartReel(connection.Definition, enabled), cancellationToken);

    public async Task<byte> GetSmartReelThresholdAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseSecondArgument(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetSmartReelThreshold(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetSmartReelThresholdAsync(RazerHidDeviceConnection connection, byte level, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetSmartReelThreshold(connection.Definition, level), cancellationToken);

    public async Task<RazerScrollFreeNotchedMode> GetScrollFreeNotchedModeAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        var request = RazerScrollWheelProtocol.CreateGetScrollFreeNotchedModeRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_scroll_get_free_notched_mode", request), cancellationToken).ConfigureAwait(false);
        return RazerScrollWheelProtocol.ParseScrollFreeNotchedMode(response, request).Mode;
    }

    public Task SetScrollFreeNotchedModeAsync(
        RazerHidDeviceConnection connection,
        RazerScrollFreeNotchedMode mode,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_scroll_set_free_notched_mode",
                RazerScrollWheelProtocol.CreateSetScrollFreeNotchedModeRequest(MiddlewareTransactionId, mode)),
            cancellationToken);

    public async Task<byte> GetScrollVirtualAccelerationLevelAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        var request =
            RazerScrollWheelProtocol.CreateGetScrollVirtualAccelerationLevelRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_scroll_get_acceleration_level", request), cancellationToken).ConfigureAwait(false);
        return RazerScrollWheelProtocol.ParseScrollVirtualAccelerationLevel(response, request).Level;
    }

    public Task SetScrollVirtualAccelerationLevelAsync(
        RazerHidDeviceConnection connection,
        byte level,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_scroll_set_acceleration_level",
                RazerScrollWheelProtocol.CreateSetScrollVirtualAccelerationLevelRequest(
                    MiddlewareTransactionId, level)),
            cancellationToken);

    public Task SetFnPrimaryAsync(RazerHidDeviceConnection connection, bool fnFunctionsArePrimary, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection, RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_fn_key_toggle", 0x02, 0x02, 0x06,
            [0x00, fnFunctionsArePrimary ? (byte)1 : (byte)0]), cancellationToken);

    public async Task<RazerHidKeyswitchOptimization> GetKeyswitchOptimizationAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(connection, RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_get_keyswitch_optimization", 0x04, 0x02, 0x82, []), cancellationToken).ConfigureAwait(false);
        var arguments = RazerHidStandardProtocol.Arguments(response, 4);
        return (arguments[1], arguments[3]) switch
        {
            (0x14, 0x28) => RazerHidKeyswitchOptimization.Typing,
            (0x00, 0x00) => RazerHidKeyswitchOptimization.Gaming,
            _ => throw new InvalidDataException("Device returned an unknown keyswitch optimization state."),
        };
    }

    public async Task SetKeyswitchOptimizationAsync(
        RazerHidDeviceConnection connection,
        RazerHidKeyswitchOptimization optimization,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(optimization))
        {
            throw new ArgumentOutOfRangeException(nameof(optimization));
        }
        var typing = optimization == RazerHidKeyswitchOptimization.Typing;
        var command1 = RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_keyswitch_optimization_command1", 0x04, 0x02, 0x02,
            typing ? [0x00, 0x14, 0x00, 0x28, 0x00] : [0x00, 0x00, 0x00, 0x00],
            allowArgumentsBeyondDeclaredSize: typing);
        var command2 = RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_keyswitch_optimization_command2", 0x05, 0x02, 0x15,
            typing ? [0x01, 0x00, 0x14, 0x00, 0x28, 0x00] : [0x01, 0x00, 0x00, 0x00, 0x00],
            allowArgumentsBeyondDeclaredSize: typing);
        await ExecuteAsync(connection, command1, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, command2, cancellationToken).ConfigureAwait(false);
    }

    public Task SetHyperPollingIndicatorAsync(
        RazerHidDeviceConnection connection,
        byte mode,
        CancellationToken cancellationToken = default)
    {
        if (mode is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        return ExecuteWithoutResultAsync(connection, RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_indicator_led_mode",
            0x01, 0x07, 0x10, [mode]), cancellationToken);
    }

    public async Task PairHyperPollingAsync(
        RazerHidDeviceConnection connection,
        ushort mouseProductId,
        CancellationToken cancellationToken = default)
    {
        var step1 = RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step1",
            0x01, 0x00, 0x46, [0x01]);
        var step2 = RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step2",
            0x03, 0x00, 0x41, [0x01, checked((byte)(mouseProductId >> 8)), checked((byte)mouseProductId)]);
        await ExecuteAsync(connection, step1, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, step2, cancellationToken).ConfigureAwait(false);
    }

    public Task UnpairHyperPollingAsync(
        RazerHidDeviceConnection connection,
        ushort mouseProductId,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection, RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_unpair",
            0x02, 0x00, 0x42,
            [checked((byte)(mouseProductId >> 8)), checked((byte)mouseProductId)]), cancellationToken);

    /// <summary>
    /// Sends the middleware-only GETs once per device and caches which command families
    /// answer, so a control appears only when the firmware really understands it. The cache
    /// keeps device-watch rescans from re-sending a HID request every few seconds.
    /// </summary>
    internal async Task<IReadOnlySet<RazerHidBackendCapability>> ProbeMiddlewareCapabilitiesAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken = default)
    {
        if (definition.Category is not (DeviceCategory.Mouse or DeviceCategory.Keyboard))
        {
            return FrozenSet<RazerHidBackendCapability>.Empty;
        }

        var result = new HashSet<RazerHidBackendCapability>();
        var isProbeDefinition =
            definition.SourceCapabilities.Count == 0 && definition.Transactions.Count == 0;
        var buttonList = RazerHidMappingProtocol.GetButtonList();
        if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                "mapping", buttonList.Report,
                response => RazerHidMappingProtocol.ParseButtonList(response).Any(id => id != 0),
                cancellationToken).ConfigureAwait(false))
        {
            result.UnionWith(MappingCapabilities);
        }
        else
        {
            // 0083 and other OBM mice answer the assignment table even when the button
            // list GET is silent, so read one known assignment as the capability probe.
            var assignment = RazerHidMappingProtocol.GetAssignment(
                0x01, RazerHidMappingProtocol.NormalMode);
            if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                    "mapping.assignment", assignment.Report,
                    response =>
                    {
                        _ = RazerHidMappingProtocol.ParseAssignment(response);
                        return true;
                    }, cancellationToken).ConfigureAwait(false))
            {
                result.UnionWith(MappingCapabilities);
            }
        }

        if (isProbeDefinition)
        {
            return result.ToFrozenSet();
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
                result.Add(RazerHidBackendCapability.ScrollFreeNotchedModeRead);
                result.Add(RazerHidBackendCapability.ScrollFreeNotchedModeWrite);
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
                result.Add(RazerHidBackendCapability.ScrollAccelerationLevelRead);
                result.Add(RazerHidBackendCapability.ScrollAccelerationLevelWrite);
            }
        }

        await ProbeOnboardProfileAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeWirelessPairingAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeKnobAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeAnalogKeyAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeOledAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeHapticScrollAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);

        if (RazerHidDeviceCatalog.IsMiddlewareMouseReadOnly(definition.ProductId))
            result.RemoveWhere(capability => capability.ToString().EndsWith("Write", StringComparison.Ordinal));
        return result.ToFrozenSet();
    }

    /// <summary>
    /// Sends one middleware GET and caches whether the firmware answered, so device-watch
    /// rescans do not re-send it. Shared by every protocol family that has no RazerHid
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
        var key = (devicePath.ToUpperInvariant(), probe);
        if (_middlewareProbeCache.TryGetValue(key, out var expiresAt) && expiresAt > now)
        {
            return true;
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

        if (supported)
        {
            _middlewareProbeCache[key] = now + MiddlewareProbePositiveTtl;
        }
        WriteDiagnostic(productId, $"{probe} probe: {supported}");
        return supported;
    }

    private void InvalidateMiddlewareProbeCache(string devicePath)
    {
        var normalizedPath = devicePath.ToUpperInvariant();
        foreach (var key in _middlewareProbeCache.Keys.Where(key => key.DevicePath == normalizedPath))
        {
            _middlewareProbeCache.TryRemove(key, out _);
        }
    }

    private enum EndpointProbeOutcome { Answered, Unsupported, Failed }

    private readonly record struct EndpointProbeResult(EndpointProbeOutcome Outcome, string? Error);

    private async Task<EndpointProbeResult> ProbeEndpointAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        var probes = CreateSafeProbesWithDiagnostics(
            definition, message => WriteDiagnostic(definition.ProductId, message));
        // An uncatalogued device has no identity reads to try; the mapping button list
        // below is the only side-effect-free probe that can admit it.
        string? lastError = probes.Count == 0
            ? "No catalog GET is declared; trying the mapping button list."
            : null;
        foreach (var probe in probes)
        {
            WriteDiagnostic(definition.ProductId,
                $"safe probe builder={probe.BuilderName}, declared-txn=0x{probe.Report[2]:X2}, class=0x{probe.Report[7]:X2}, command=0x{probe.Report[8]:X2}");
            try
            {
                await _transport.QueryPreparedAsync(devicePath, probe.Report, definition.DeviceWait,
                    cancellationToken).ConfigureAwait(false);
                return new(EndpointProbeOutcome.Answered, null);
            }
            catch (NotSupportedException exception)
            {
                // A matched status 0x05 reply is a well-formed answer: the path is the
                // control channel even when this particular GET is not implemented.
                WriteDiagnostic(definition.ProductId,
                    $"safe probe builder={probe.BuilderName}, declared-txn=0x{probe.Report[2]:X2}, class=0x{probe.Report[7]:X2}, command=0x{probe.Report[8]:X2}: 0x05 unsupported, control channel answered");
                return new(EndpointProbeOutcome.Unsupported, exception.Message);
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or COMException or
                InvalidOperationException or NotSupportedException or InvalidDataException or TimeoutException)
            {
                // One rejected or unavailable probe must not hide the later reads that
                // would still open this endpoint. Keep the raw reason for the connection
                // error and the classified reason in the diagnostic log.
                lastError = exception.Message;
                WriteDiagnostic(definition.ProductId,
                    $"safe probe failed builder={probe.BuilderName}, declared-txn=0x{probe.Report[2]:X2}, class=0x{probe.Report[7]:X2}, command=0x{probe.Report[8]:X2}: {DescribeProbeFailure(exception)}");
            }
        }

        if (definition.Category is DeviceCategory.Mouse or DeviceCategory.Keyboard)
        {
            // This read-only GET is the last-resort endpoint probe. Some mapping-only
            // firmware answers it when the catalog's identity probes time out, so the
            // runtime button list, not a static PID list, decides whether the page opens.
            var mapping = RazerHidMappingProtocol.GetButtonList();
            try
            {
                await _transport.QueryPreparedAsync(
                    devicePath,
                    mapping.Report,
                    definition.DeviceWait,
                    cancellationToken).ConfigureAwait(false);
                return new(EndpointProbeOutcome.Answered, null);
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or
                COMException or InvalidOperationException or NotSupportedException or
                InvalidDataException or TimeoutException)
            {
                lastError = exception.Message;
                WriteDiagnostic(definition.ProductId,
                    $"mapping fallback probe class=0x{mapping.Report[7]:X2}, command=0x{mapping.Report[8]:X2}: {DescribeProbeFailure(exception)}");
            }
        }

        return new(EndpointProbeOutcome.Failed,
            lastError ?? "No side-effect-free GET answered on this endpoint.");
    }

    private static string DescribeProbeFailure(Exception exception) => exception switch
    {
        RazerCommandRejectedException => $"0x03 rejected: {exception.Message}",
        RazerDeviceTimeoutException { IsRetryableBusy: true } => $"busy/timeout: {exception.Message}",
        RazerDeviceTimeoutException => $"timeout: {exception.Message}",
        RazerReportMismatchException => $"txn/report mismatch: {exception.Message}",
        COMException => $"COM 0x{exception.HResult:X8}: {exception.Message}",
        Win32Exception win32 => $"Win32 {win32.NativeErrorCode}: {exception.Message}",
        _ => $"{exception.GetType().Name}: {exception.Message}",
    };

    internal async Task<(string? Path, string? Error)> ResolveEndpointPathAsync(
        RazerHidDeviceDefinition definition,
        IEnumerable<string> orderedPaths,
        CancellationToken cancellationToken = default)
    {
        string? lastError = null;
        string? unsupportedPath = null;
        foreach (var devicePath in orderedPaths)
        {
            WriteDiagnostic(definition.ProductId,
                $"probing path={RazerFeatureTransport.DescribeDevicePath(devicePath)}");
            try
            {
                var probe = await ProbeEndpointAsync(definition, devicePath, cancellationToken).ConfigureAwait(false);
                if (probe.Outcome == EndpointProbeOutcome.Answered)
                {
                    WriteDiagnostic(definition.ProductId,
                        $"resolved path={RazerFeatureTransport.DescribeDevicePath(devicePath)}");
                    return (devicePath, null);
                }

                lastError = probe.Error;
                if (probe.Outcome == EndpointProbeOutcome.Unsupported)
                {
                    // Keep looking for an endpoint that answers a real GET; a 0x05-only
                    // collection is still a usable control channel if nothing better exists.
                    unsupportedPath ??= devicePath;
                    WriteDiagnostic(definition.ProductId,
                        $"path={RazerFeatureTransport.DescribeDevicePath(devicePath)} answered only 0x05: {probe.Error}");
                    continue;
                }
                WriteDiagnostic(definition.ProductId,
                    $"no answer on path={RazerFeatureTransport.DescribeDevicePath(devicePath)}: {probe.Error}");
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or
                InvalidOperationException or NotSupportedException or InvalidDataException)
            {
                lastError = exception.Message;
                WriteDiagnostic(definition.ProductId,
                    $"probe failed path={RazerFeatureTransport.DescribeDevicePath(devicePath)}: {exception.GetType().Name}: {exception.Message}");
            }
        }
        if (unsupportedPath is not null)
        {
            WriteDiagnostic(definition.ProductId,
                $"resolved path={RazerFeatureTransport.DescribeDevicePath(unsupportedPath)} via 0x05");
            return (unsupportedPath, null);
        }
        return (null, lastError);
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
            $"usage=0x{candidate.UsagePage:X4}:0x{candidate.Usage:X4} " +
            $"access={candidate.Access}"));

    private static IReadOnlyList<RazerHidRequest> CreateSafeProbes(RazerHidDeviceDefinition definition) =>
        CreateSafeProbesWithDiagnostics(definition, null);

    private static IReadOnlyList<RazerHidRequest> CreateSafeProbesWithDiagnostics(
        RazerHidDeviceDefinition definition,
        Action<string>? diagnostic)
    {
        var probes = new List<RazerHidRequest>();
        AddIfResolved("razer_chroma_standard_get_firmware_version", () => RazerHidStandardProtocol.GetFirmware(definition));
        if (definition.Transactions.ContainsKey("razer_chroma_standard_get_firmware_version"))
        {
            try
            {
                var fallback = RazerHidStandardProtocol.GetFirmwareFallback(definition);
                probes.Add(new RazerHidRequest(
                    "razer_chroma_standard_get_firmware_version_fallback", fallback.Report));
            }
            catch (Exception exception) when (exception is NotSupportedException or ArgumentException)
            {
                diagnostic?.Invoke(
                    $"safe probe builder=razer_chroma_standard_get_firmware_version_fallback could not be constructed: {exception.GetType().Name}: {exception.Message}");
            }
        }
        AddIfResolved("razer_chroma_standard_get_serial", () => RazerHidStandardProtocol.GetSerial(definition));
        AddIfResolved("razer_chroma_standard_get_device_mode", () => RazerHidStandardProtocol.GetDeviceMode(definition));
        AddIfResolved("razer_chroma_misc_get_battery_level", () => RazerHidMouseProtocol.GetBattery(definition));
        AddIfResolved("razer_chroma_misc_get_dpi_xy", () => RazerHidMouseProtocol.GetDpi(definition));
        AddIfResolved("razer_chroma_misc_get_dpi_xy_byte", () => RazerHidMouseProtocol.GetDpi(definition));
        AddIfResolved("razer_chroma_misc_get_polling_rate2", () => RazerHidMouseProtocol.GetPollingRate(definition));
        AddIfResolved("razer_chroma_misc_get_polling_rate", () => RazerHidMouseProtocol.GetPollingRate(definition));
        // The soundbar has no All region: the firmware only answers a real region id, so
        // probing with region 0 fails on every unit. Its brightness path reads the region
        // list first, and the firmware probe above is enough to open the channel.
        if (!RazerHidDeviceCatalog.IsLeviathanV2X(definition.ProductId))
        {
            AddIfResolved("razer_chroma_extended_matrix_get_brightness", () =>
                RazerHidLightingProtocol.GetBrightness(definition, null, null));
        }
        AddIfResolved("razer_chroma_standard_get_led_brightness", () =>
            RazerHidLightingProtocol.GetBrightness(definition, null, null));
        AddIfResolved("razer_chroma_misc_get_blade_brightness", () =>
            RazerHidLightingProtocol.GetBrightness(definition, null, null));
        AddIfResolved("razer_chroma_misc_get_dock_brightness", () =>
            RazerHidLightingProtocol.GetBrightness(definition, null, null));
        return probes;

        void AddIfResolved(string builder, Func<RazerHidRequest> create)
        {
            if (!definition.Transactions.ContainsKey(builder) || probes.Any(item => item.BuilderName == builder))
            {
                return;
            }

            try
            {
                probes.Add(create());
            }
            catch (Exception exception) when (exception is NotSupportedException or ArgumentException)
            {
                // One builder that cannot be constructed must not hide the other safe
                // probes. Keep the reason in the log so an empty probe list is not
                // mistaken for a device that declared no reads at all.
                diagnostic?.Invoke(
                    $"safe probe builder={builder} could not be constructed: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private RazerHidDeviceConnection CreateConnection(
        RazerHidDeviceDefinition definition,
        string? path,
        string? physicalDeviceKey,
        RazerHidEndpointState state,
        string? error,
        IReadOnlySet<RazerHidBackendCapability>? extraCapabilities = null,
        IReadOnlySet<RazerHidBackendCapability>? excludedCapabilities = null)
    {
        var resolved = state == RazerHidEndpointState.Resolved;
        var capabilities = resolved ? GetCapabilities(definition) : FrozenSet<RazerHidBackendCapability>.Empty;
        if (resolved && excludedCapabilities is not null)
        {
            capabilities = capabilities.Except(excludedCapabilities).ToFrozenSet();
        }
        if (resolved && extraCapabilities is not null)
        {
            capabilities = capabilities.Union(extraCapabilities).ToFrozenSet();
        }
        return new(definition, path, physicalDeviceKey, state,
            capabilities,
            resolved
                ? GetLightingZoneCapabilities(definition)
                : FrozenDictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>.Empty,
            error);
    }

    internal static IReadOnlySet<RazerHidLedZone> GetSupportedLedZones(RazerHidDeviceDefinition device)
    {
        return GetLightingZoneCapabilities(device).Keys.ToFrozenSet();
    }

    internal static IReadOnlyDictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities> GetLightingZoneCapabilities(
        RazerHidDeviceDefinition device)
    {
        var zones = new HashSet<RazerHidLedZone> { device.DefaultLedZone };
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
                if (prefix.Length >= 2 && Enum.IsDefined((RazerHidLedZone)prefix.Span[1]))
                {
                    zones.Add((RazerHidLedZone)prefix.Span[1]);
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

        var output = new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>();
        var effectsByZone = GetLightingEffectsByZone(device);
        foreach (var zone in zones.Order())
        {
            var effects = effectsByZone.GetValueOrDefault(zone) ?? FrozenSet<RazerHidLightingEffect>.Empty;
            var canReadBrightness = HasSourceBrightness(device, zone, write: false) &&
                CanBuild(() => RazerHidLightingProtocol.GetBrightness(
                device, device.DefaultStorage, zone));
            var canWriteBrightness = HasSourceBrightness(device, zone, write: true) &&
                CanBuild(() => RazerHidLightingProtocol.SetBrightness(
                device, device.DefaultStorage, zone, 0));
            var canReadState = device.HasSourceCapability("get_logo_active") && zone == RazerHidLedZone.Logo &&
                CanUseClassicBuilder(device, "razer_chroma_standard_get_led_state", zone);
            var canWriteState = device.Transactions.ContainsKey("razer_chroma_standard_set_led_state") &&
                HasSourceStateWrite(device, zone) &&
                SupportsStorageAndZone(device, "razer_chroma_standard_set_led_state",
                    device.DefaultStorage, zone);
            var canReadEffect = zone == RazerHidLedZone.Macro &&
                device.HasSourceCapability("get_macro_effect") &&
                CanUseClassicBuilder(device, "razer_chroma_standard_get_led_effect", zone);
            var canWriteEffect = effects.Count > 0 &&
                CanUseClassicBuilder(device, "razer_chroma_standard_set_led_effect", zone);
            var canReadColor = false;
            var canWriteColor = effects.Any(effect => effect is RazerHidLightingEffect.Static or
                RazerHidLightingEffect.BreathingSingle or RazerHidLightingEffect.Blinking) &&
                CanUseClassicBuilder(device, "razer_chroma_standard_set_led_rgb", zone);
            var canWriteBlinking = effects.Contains(RazerHidLightingEffect.Blinking) &&
                CanUseClassicBuilder(device, "razer_chroma_standard_set_led_blinking", zone);
            if (canReadBrightness || canWriteBrightness || canReadState || canWriteState ||
                canReadEffect || canWriteEffect || canReadColor || canWriteColor || canWriteBlinking ||
                effects.Count > 0)
            {
                output.Add(zone, new RazerHidLightingZoneCapabilities(
                    zone, canReadBrightness, canWriteBrightness, canReadState, canWriteState,
                    canReadEffect, canWriteEffect, canReadColor, canWriteColor, canWriteBlinking,
                    effects));
            }
        }
        return output.ToFrozenDictionary();
    }

    private static IReadOnlyDictionary<RazerHidLedZone, IReadOnlySet<RazerHidLightingEffect>> GetLightingEffectsByZone(
        RazerHidDeviceDefinition device)
    {
        var effects = new Dictionary<RazerHidLedZone, HashSet<RazerHidLightingEffect>>();
        foreach (var effect in Enum.GetValues<RazerHidLightingEffect>())
        {
            if (effect is RazerHidLightingEffect.SoftwareSpectrum or
                RazerHidLightingEffect.SoftwareWave or
                RazerHidLightingEffect.SoftwareFire or
                RazerHidLightingEffect.SoftwareAudioMeter or
                RazerHidLightingEffect.SoftwareWheel)
            {
                continue;
            }
            try
            {
                var zone = RazerHidLightingProtocol.ResolveEffectZone(device, effect);
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
                !CanBuild(() => RazerHidLightingProtocol.CreateEffectRequests(device,
                    new RazerHidLightingSettings(effect, Zone: zone))))
            {
                continue;
            }
            Add(zone, effect);
        }

        return effects.ToDictionary(
            item => item.Key,
            item => (IReadOnlySet<RazerHidLightingEffect>)item.Value.ToFrozenSet())
            .ToFrozenDictionary();

        void Add(RazerHidLedZone zone, RazerHidLightingEffect effect)
        {
            if (!effects.TryGetValue(zone, out var zoneEffects))
            {
                zoneEffects = [];
                effects.Add(zone, zoneEffects);
            }
            zoneEffects.Add(effect);
        }
    }

    private static bool TryGetSourceEffect(string capability, out RazerHidLightingEffect effect)
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
                "none" => RazerHidLightingEffect.Off,
                "static" => RazerHidLightingEffect.Static,
                "spectrum" => RazerHidLightingEffect.Spectrum,
                "wave" => RazerHidLightingEffect.Wave,
                "reactive" => RazerHidLightingEffect.Reactive,
                "breath_random" => RazerHidLightingEffect.BreathingRandom,
                "breath_mono" or "breath_single" => RazerHidLightingEffect.BreathingSingle,
                "breath_dual" => RazerHidLightingEffect.BreathingDual,
                _ => (RazerHidLightingEffect?)null,
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

    private static bool TryGetSourceZone(string capability, out RazerHidLedZone zone)
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

    private static bool HasSourceStateWrite(RazerHidDeviceDefinition device, RazerHidLedZone zone)
    {
        var name = SourceZones.SingleOrDefault(candidate => candidate.Zone == zone).Name;
        if (name is null)
        {
            return false;
        }
        return device.HasSourceCapability($"set_{name}_active") ||
            device.HasSourceCapability($"set_{name}_on");
    }

    private static bool HasSourceBrightness(RazerHidDeviceDefinition device, RazerHidLedZone zone, bool write)
    {
        var operation = write ? "set" : "get";
        // RazerHid adds get/set_brightness at runtime and routes them to the backlight zone.
        if (zone == device.DefaultLedZone &&
            (device.HasSourceCapability($"{operation}_brightness") ||
             device.Category == DeviceCategory.Keyboard && zone == RazerHidLedZone.Backlight))
        {
            // Keyboards get get/set_brightness injected at runtime; mice must not
            // inherit a backlight zone they never declared.
            return true;
        }
        var name = SourceZones.SingleOrDefault(candidate => candidate.Zone == zone).Name;
        return name is not null && device.HasSourceCapability($"{operation}_{name}_brightness");
    }

    private static bool SupportsStorageAndZone(
        RazerHidDeviceDefinition device,
        string builder,
        RazerHidStorage storage,
        RazerHidLedZone zone)
    {
        return device.BuilderArgumentPrefixes.TryGetValue(builder, out var prefixes) &&
            prefixes.Any(prefix =>
                prefix.Length >= 2 && prefix.Span[0] == (byte)storage && prefix.Span[1] == (byte)zone ||
                prefix.Length == 1 && prefix.Span[0] == (byte)storage);
    }

    private static bool CanUseClassicBuilder(
        RazerHidDeviceDefinition device,
        string builder,
        RazerHidLedZone zone) =>
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

    private static readonly (string Name, RazerHidLedZone Zone)[] SourceZones =
    [
        ("logo", RazerHidLedZone.Logo),
        ("scroll", RazerHidLedZone.ScrollWheel),
        ("backlight", RazerHidLedZone.Backlight),
        ("left", RazerHidLedZone.LeftSide),
        ("right", RazerHidLedZone.RightSide),
        ("charging", RazerHidLedZone.Charging),
        ("fast_charging", RazerHidLedZone.FastCharging),
        ("fully_charged", RazerHidLedZone.FullyCharged),
    ];

    private Task<byte[]> ExecuteAsync(
        RazerHidDeviceConnection connection,
        RazerHidRequest request,
        CancellationToken cancellationToken) =>
        _executor.ExecuteAsync(connection, request, cancellationToken);

    private Task ExecuteWithoutResultAsync(
        RazerHidDeviceConnection connection,
        RazerHidRequest request,
        CancellationToken cancellationToken) =>
        _executor.ExecuteWithoutResultAsync(connection, request, cancellationToken);

    private static void RequireReady(RazerHidDeviceConnection connection) =>
        RazerHidRequestExecutor.RequireReady(connection);

    private static void RequireLightingZone(
        RazerHidDeviceConnection connection,
        RazerHidLedZone zone,
        Func<RazerHidLightingZoneCapabilities, bool> predicate,
        string operation)
    {
        RequireReady(connection);
        if (!connection.LightingZones.TryGetValue(zone, out var capabilities) || !predicate(capabilities))
        {
            throw new NotSupportedException(
                $"RazerHid device 1532:{connection.Definition.ProductId:X4} does not support {operation} on zone {zone}.");
        }
    }

    private static RazerHidLedZone GetDefaultBrightnessZone(RazerHidDeviceConnection connection, bool write)
    {
        var defaultZone = connection.Definition.DefaultLedZone;
        bool Supports(RazerHidLightingZoneCapabilities capabilities) =>
            write ? capabilities.CanWriteBrightness : capabilities.CanReadBrightness;
        if (connection.LightingZones.TryGetValue(defaultZone, out var capabilities) && Supports(capabilities))
        {
            return defaultZone;
        }
        return connection.LightingZones.Values.Where(Supports).Select(item => item.Zone)
            .Order().FirstOrDefault(defaultZone);
    }

    private async Task<byte> GetLeviathanRegionAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        var request = RazerHidStandardProtocol.Create(connection.Definition,
            "razer_chroma_extended_matrix_get_region_ids", 0x50, 0x0F, 0x80, []);
        return LeviathanV2XProtocol.ReadFirstRegion(
            await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false));
    }

    private static void RequireClassicLedOperation(
        RazerHidDeviceConnection connection,
        RazerHidStorage storage,
        RazerHidLedZone zone,
        Func<RazerHidLightingZoneCapabilities, bool> predicate,
        string operation,
        string builder)
    {
        RequireLightingZone(connection, zone, predicate, operation);
        if (!SupportsStorageAndZone(connection.Definition, builder, storage, zone))
        {
            throw new NotSupportedException(
                $"RazerHid device 1532:{connection.Definition.ProductId:X4} does not support {operation} with storage {storage} on zone {zone}.");
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
