using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
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
    // The middleware writes the LK scroll wheel mode for the base profile.
    private const byte LkScrollProfileId = 0x01;

    private readonly RazerHidDeviceCatalog _catalog;
    private readonly IRazerFeatureTransport _transport;
    private readonly LocalDiagnosticLog? _diagnosticLog;
    private readonly ConcurrentDictionary<string, string> _endpointCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly RazerHidRequestExecutor _executor;
    private readonly ConcurrentDictionary<(string DevicePath, ushort ProductId, string Probe), DateTimeOffset>
        _middlewareProbeCache = new();
    // A control channel that answers nothing costs five HID retries per probe, and the
    // whole pass holds the device write gate. Repeating that every rescan is what makes
    // profile switches and lighting feel a beat behind, so a slow failure is remembered
    // briefly instead of being paid again on the next pass.
    private readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt, string? Error)>
        _unresolvedEndpointCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan UnresolvedEndpointTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SlowUnresolvedProbe = TimeSpan.FromSeconds(1);
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
        var wirelessLinkDown = false;

        async Task ReadAsync<T>(RazerHidBackendCapability capability, string key, Func<Task<T>> read, Action<T> assign)
        {
            // One link-down answer covers the whole pass: every remaining read would only
            // burn its retry budget against a device that is not on the air.
            if (wirelessLinkDown ||
                !connection.Capabilities.Contains(capability) || excludedCapabilities?.Contains(capability) == true)
            {
                return;
            }
            try
            {
                assign(await read().ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or COMException or
                InvalidOperationException or NotSupportedException or InvalidDataException or ArgumentException)
            {
                if (IsWirelessLinkDownFailure(exception))
                {
                    wirelessLinkDown = true;
                    errors[capability.ToString()] = exception.Message;
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"state read '{key}' stopped: the wireless link is down");
                    return;
                }

                if (exception is RazerDeviceTimeoutException { IsRetryableBusy: true })
                {
                    // Busy is transient. Keep the capability enabled so the next refresh retries it.
                    errors[capability.ToString()] = exception.Message;
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"state read '{key}' is busy and will retry: {exception.Message}");
                    return;
                }

                if (exception is RazerDeviceTimeoutException or RazerReportMismatchException)
                {
                    // A silent or stale busy reply is transient; retry on the next poll
                    // instead of hiding the capability until the next scan.
                    WriteDiagnostic(connection.Definition.ProductId,
                        $"state read '{key}' was unanswered and will retry: {exception.Message}");
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
                    InvalidOperationException or NotSupportedException or InvalidDataException or ArgumentException)
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
                $"razer-hid:{connection.InstanceId}", connection.Definition.DisplayName, battery, charging));
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

    /// <summary>Null when the firmware does not expose the edition (most pre-2021 devices).</summary>
    public async Task<int?> GetEditionIdAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await ExecuteAsync(connection,
                RazerHidStandardProtocol.GetEditionInformation(connection.Definition), cancellationToken)
                .ConfigureAwait(false);
            return RazerHidStandardProtocol.ParseEdition(response).EditionId;
        }
        catch (Exception exception) when (exception is RazerDeviceTimeoutException or
            RazerCommandRejectedException or RazerReportMismatchException or NotSupportedException or
            InvalidDataException)
        {
            WriteDiagnostic(connection.Definition.ProductId,
                $"edition read unavailable: {DescribeProbeFailure(exception)}");
            return null;
        }
    }

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

        var before = await TryReadDeviceModeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (before is not { } mode)
        {
            return false;
        }
        if (mode != RazerHidStandardProtocol.NormalDeviceMode)
        {
            // 0x03 is already correct; 0x02 and anything else is not ours to overwrite.
            return mode == RazerHidStandardProtocol.DriverDeviceMode;
        }

        WriteDiagnostic(connection.Definition.ProductId,
            $"device mode was 0x{mode:X2}; writing driver mode to restore the 2.4 GHz link");
        await ExecuteWithoutResultAsync(connection,
            RazerHidStandardProtocol.SetDeviceMode(
                connection.Definition, RazerHidStandardProtocol.DriverDeviceMode),
            cancellationToken)
            .ConfigureAwait(false);

        var after = await TryReadDeviceModeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (after is not { } restored)
        {
            return false;
        }
        WriteDiagnostic(connection.Definition.ProductId, $"device mode is now 0x{restored:X2}");
        return restored == RazerHidStandardProtocol.DriverDeviceMode;
    }

    /// <summary>
    /// A keyboard or keypad left in driver mode by an interrupted mapping session
    /// reports its roller as a mouse wheel and stops sending media keys. Only driver
    /// mode is repaired; an unknown mode is left alone rather than overwritten blind.
    /// </summary>
    public async Task<bool> EnsureNormalModeAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var before = await TryReadDeviceModeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (before is not { } mode)
        {
            return false;
        }
        if (mode != RazerHidStandardProtocol.DriverDeviceMode)
        {
            return mode == RazerHidStandardProtocol.NormalDeviceMode;
        }

        WriteDiagnostic(connection.Definition.ProductId,
            $"device mode was 0x{mode:X2}; restoring normal mode");
        try
        {
            await ExecuteWithoutResultAsync(connection,
                RazerHidStandardProtocol.SetDeviceMode(
                    connection.Definition, RazerHidStandardProtocol.NormalDeviceMode),
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is RazerCommandRejectedException or RazerDeviceTimeoutException or
                NotSupportedException)
        {
            WriteDiagnostic(connection.Definition.ProductId,
                $"normal mode restore rejected: {exception.Message}");
            return false;
        }

        var after = await TryReadDeviceModeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (after is not { } restored)
        {
            return false;
        }
        WriteDiagnostic(connection.Definition.ProductId, $"device mode is now 0x{restored:X2}");
        return restored == RazerHidStandardProtocol.NormalDeviceMode;
    }

    /// <summary>
    /// A device that cannot answer the mode read is left in whatever mode it is in.
    /// Letting this parse error escape aborted the whole refresh, hiding every other
    /// device and leaving the Blade telemetry unread.
    /// </summary>
    private async Task<byte?> TryReadDeviceModeAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            return RazerHidStandardProtocol.ParseDeviceMode(await ExecuteAsync(connection,
                RazerHidStandardProtocol.GetDeviceMode(connection.Definition), cancellationToken)
                .ConfigureAwait(false));
        }
        catch (Exception exception) when (
            exception is System.IO.InvalidDataException or NotSupportedException or
            RazerCommandRejectedException or RazerDeviceTimeoutException)
        {
            WriteDiagnostic(connection.Definition.ProductId,
                $"device mode unavailable: {exception.Message}");
            return null;
        }
    }

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
        await ExecuteWithoutResultAsync(connection, command1, cancellationToken).ConfigureAwait(false);
        await ExecuteWithoutResultAsync(connection, command2, cancellationToken).ConfigureAwait(false);
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
