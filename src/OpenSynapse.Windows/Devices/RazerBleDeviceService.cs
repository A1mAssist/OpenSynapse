using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerBleDeviceState(
    RazerBleFirmware? Firmware,
    string? SerialNumber,
    int? BatteryPercent,
    byte? ChargingStatus,
    int? PollingHertz,
    (int X, int Y, int Z)? Dpi,
    IReadOnlyList<string> Errors);

/// <summary>
/// Serialises protocol 30 commands for one BLE device, mirroring the middleware's
/// per-device lock, 2 attempts and 30 ms retry delay.
/// </summary>
public sealed class RazerBleSession : IDisposable
{
    private readonly IRazerBleTransport _transport;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _retryDelay;
    private readonly Action<string>? _diagnostic;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte _transactionId;

    public RazerBleSession(
        IRazerBleTransport transport,
        TimeSpan? timeout = null,
        TimeSpan? retryDelay = null,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _timeout = timeout ?? TimeSpan.FromMilliseconds(500);
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(30);
        _diagnostic = diagnostic;
    }

    public async Task<RazerBleResponse> SendAsync(
        Func<byte, byte[]> createFrame,
        string description,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createFrame);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? lastError = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
                }

                var frame = createFrame(NextTransactionId());
                var reply = await _transport.ExchangeAsync(frame, _timeout, cancellationToken).ConfigureAwait(false);
                if (reply is null)
                {
                    lastError = "timeout";
                    _diagnostic?.Invoke($"ble {description}: no notification");
                    continue;
                }

                RazerBleResponse response;
                try
                {
                    response = RazerBleProtocol30.ParseResponse(reply);
                }
                catch (InvalidOperationException exception)
                {
                    lastError = exception.Message;
                    _diagnostic?.Invoke($"ble {description}: {exception.Message}");
                    continue;
                }

                if (response.Status is RazerBleProtocol30.StatusBusy or RazerBleProtocol30.StatusFailure)
                {
                    lastError = $"status {response.Status}";
                    _diagnostic?.Invoke($"ble {description}: status {response.Status}, retrying");
                    continue;
                }

                return response;
            }

            throw new InvalidOperationException($"Protocol 30 command '{description}' failed: {lastError}.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _transport.Dispose();
        _gate.Dispose();
    }

    private byte NextTransactionId()
    {
        if (_transactionId > RazerBleProtocol30.MaximumTransactionId)
        {
            _transactionId = 0;
        }

        return _transactionId++;
    }
}

/// <summary>
/// Battery, polling rate, DPI and identity for a protocol 30 BLE device. Writes follow the
/// house rule: read the current value, write, read back, restore the old value on mismatch.
/// </summary>
public sealed class RazerBleDeviceService : IDisposable
{
    private const byte BleProfileId = 0;
    private const byte MappingClassId = 0;
    private const byte ScrollClassId = 0;
    private const byte BatteryId = 0;
    private readonly RazerBleSession _session;
    private readonly Action<string>? _diagnostic;
    private byte? _regionId;

    public RazerBleDeviceService(
        RazerBleSession session,
        string name,
        Action<string>? diagnostic = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Name = name;
        _diagnostic = diagnostic;
    }

    public string Name { get; }

    public static async Task<RazerBleDeviceService> OpenAsync(
        RazerBleDevice device,
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        var transport = await WindowsRazerBleTransport.ConnectAsync(device.Id, cancellationToken).ConfigureAwait(false);
        return new RazerBleDeviceService(new RazerBleSession(transport, diagnostic: diagnostic), device.Name, diagnostic);
    }

    public async Task<RazerBleDeviceState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        var firmware = await TryAsync(
            "firmware",
            async () => RazerBleProtocol30.ReadFirmware(await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateGetFirmware(transaction), "get firmware", cancellationToken).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var serial = await TryAsync(
            "serial",
            async () => RazerBleProtocol30.ReadSerialNumber(await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateGetSerialNumber(transaction), "get serial", cancellationToken).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var battery = await TryStructAsync(
            "battery",
            async () => RazerBleProtocol30.ReadBatteryPercent(await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateGetBatteryLevel(transaction), "get battery", cancellationToken).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var charging = await TryStructAsync(
            "charging",
            async () => RazerBleProtocol30.ReadChargingStatus(await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateGetChargingStatus(transaction), "get charging", cancellationToken).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var polling = await TryStructAsync(
            "polling",
            async () => RazerBleProtocol30.ReadPollingRate(await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateGetPollingRate(transaction), "get polling", cancellationToken).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var dpi = await TryStructAsync(
            "dpi",
            async () => RazerBleProtocol30.ReadDpi(await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateGetDpi(transaction), "get dpi", cancellationToken).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        return new RazerBleDeviceState(firmware, serial, battery, charging, polling, dpi, errors);
    }

    public async Task<RazerBleLightingState> ReadLightingAsync(CancellationToken cancellationToken = default)
    {
        var regionId = await GetRegionIdAsync(cancellationToken).ConfigureAwait(false);
        var brightnessResponse = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetBrightness(transaction, BleProfileId, regionId),
            "get brightness", cancellationToken).ConfigureAwait(false);
        EnsureSuccess(brightnessResponse, "get brightness");

        var effectResponse = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetEffect(transaction, BleProfileId, regionId),
            "get effect", cancellationToken).ConfigureAwait(false);
        EnsureSuccess(effectResponse, "get effect");

        return RazerBleProtocol30.ReadEffect(effectResponse, regionId) with
        {
            BrightnessPercent = RazerBleProtocol30.ReadBrightnessPercent(brightnessResponse),
        };
    }

    public async Task<int> SetBrightnessAsync(int percent, CancellationToken cancellationToken = default)
    {
        var regionId = await GetRegionIdAsync(cancellationToken).ConfigureAwait(false);
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetBrightness(transaction, BleProfileId, regionId, percent),
            "set brightness", cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "set brightness");

        var applied = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetBrightness(transaction, BleProfileId, regionId),
            "verify brightness", cancellationToken).ConfigureAwait(false);
        EnsureSuccess(applied, "verify brightness");
        return RazerBleProtocol30.ReadBrightnessPercent(applied);
    }

    public async Task<RazerBleLightingState> SetEffectAsync(
        RazerBleLightingEffect effect,
        byte flags,
        byte rate,
        ReadOnlyMemory<byte> colors,
        CancellationToken cancellationToken = default)
    {
        var regionId = await GetRegionIdAsync(cancellationToken).ConfigureAwait(false);
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetEffect(
                transaction, BleProfileId, regionId, effect, flags, rate, colors.Span),
            "set effect", cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "set effect");
        return await ReadLightingAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<byte>> GetMappingButtonIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetButtonList(transaction, MappingClassId),
            "get button list", cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "get button list");
        return RazerBleProtocol30.ReadButtonList(response);
    }

    public async Task<RazerBleButtonAssignment> GetButtonAssignmentAsync(
        byte buttonId,
        byte mode = 0,
        CancellationToken cancellationToken = default)
    {
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetButtonAssignment(
                transaction, MappingClassId, buttonId),
            "get button assignment", cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "get button assignment");
        return RazerBleProtocol30.ReadButtonAssignment(response, mode);
    }

    public async Task SetButtonAssignmentAsync(
        byte buttonId,
        byte mode,
        byte functionId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetButtonAssignment(
                transaction, MappingClassId, buttonId, mode, functionId, data.Span),
            "set button assignment", cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "set button assignment");

        var applied = await GetButtonAssignmentAsync(buttonId, mode, cancellationToken).ConfigureAwait(false);
        if (applied.FunctionId != functionId || !applied.Data.SequenceEqual(data.ToArray()))
        {
            throw new InvalidOperationException("The BLE device kept a different button assignment.");
        }
    }

    private async Task<byte> GetRegionIdAsync(CancellationToken cancellationToken)
    {
        if (_regionId is { } cached)
        {
            return cached;
        }

        byte regionId;
        try
        {
            var response = await _session.SendAsync(
                RazerBleProtocol30.CreateGetRegionIds,
                "get Chroma regions", cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, "get Chroma regions");
            regionId = RazerBleProtocol30.ReadRegionIds(response).FirstOrDefault();
            if (regionId == 0)
            {
                regionId = 1;
            }
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            regionId = 1;
        }

        _regionId = regionId;
        return regionId;
    }

    private static void EnsureSuccess(RazerBleResponse response, string operation)
    {
        if (response.IsSuccess)
        {
            return;
        }

        if (response.IsUnsupported)
        {
            throw new NotSupportedException($"The BLE device does not support {operation}.");
        }

        throw new InvalidOperationException($"BLE {operation} failed with status 0x{response.Status:X2}.");
    }

    public async Task<int> SetPollingRateAsync(int hertz, CancellationToken cancellationToken = default)
    {
        RazerBleProtocol30.PollingCode(hertz);

        var current = RazerBleProtocol30.ReadPollingRate(await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetPollingRate(transaction), "get polling", cancellationToken).ConfigureAwait(false));
        if (current == hertz)
        {
            return current;
        }

        await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetPollingRate(transaction, hertz),
            "set polling",
            cancellationToken).ConfigureAwait(false);

        var applied = RazerBleProtocol30.ReadPollingRate(await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetPollingRate(transaction), "verify polling", cancellationToken).ConfigureAwait(false));
        if (applied == hertz)
        {
            return applied;
        }

        _diagnostic?.Invoke($"ble polling read back {applied} Hz after writing {hertz} Hz, restoring {current} Hz");
        await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetPollingRate(transaction, current),
            "restore polling",
            cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException($"The device kept {applied} Hz instead of {hertz} Hz; the previous rate was restored.");
    }

    public async Task<(int X, int Y)> SetDpiAsync(int x, int y, CancellationToken cancellationToken = default)
    {
        var current = RazerBleProtocol30.ReadDpi(await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetDpi(transaction), "get dpi", cancellationToken).ConfigureAwait(false));

        await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetDpi(transaction, x, y),
            "set dpi",
            cancellationToken).ConfigureAwait(false);

        var applied = RazerBleProtocol30.ReadDpi(await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetDpi(transaction), "verify dpi", cancellationToken).ConfigureAwait(false));
        if (applied.X == x && applied.Y == y)
        {
            return (applied.X, applied.Y);
        }

        _diagnostic?.Invoke(
            $"ble dpi read back {applied.X}x{applied.Y} after writing {x}x{y}, restoring {current.X}x{current.Y}");
        await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetDpi(transaction, current.X, current.Y),
            "restore dpi",
            cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"The device kept {applied.X}x{applied.Y} instead of {x}x{y}; the previous DPI was restored.");
    }

    public async Task<RazerBleDpiStages> ReadDpiStagesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetDpiStages(transaction, BleProfileId),
            "get dpi stages",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "get dpi stages");
        return RazerBleProtocol30.ReadDpiStages(response);
    }

    public async Task<RazerBleDpiStages> SetDpiStagesAsync(
        RazerBleDpiStages state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var current = await ReadDpiStagesAsync(cancellationToken).ConfigureAwait(false);

        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetDpiStages(transaction, BleProfileId, state),
            "set dpi stages",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "set dpi stages");

        var applied = await ReadDpiStagesAsync(cancellationToken).ConfigureAwait(false);
        if (DpiStagesEqual(applied, state))
        {
            return applied;
        }

        _diagnostic?.Invoke("ble dpi stages did not read back as written; restoring the previous stages");
        await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetDpiStages(transaction, BleProfileId, current),
            "restore dpi stages",
            cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(
            "The device kept a different DPI stage table; the previous stages were restored.");
    }

    public async Task<RazerBleScrollState> ReadScrollAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var mode = await TryStructAsync(
            "scroll mode",
            async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateGetScrollFreeNotchedMode(transaction, ScrollClassId),
                    "get scroll mode",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "get scroll mode");
                return ReadFirstByte(response, "get scroll mode");
            },
            errors,
            cancellationToken).ConfigureAwait(false);

        var smartReel = await TryStructAsync(
            "scroll smart reel",
            async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateGetScrollSmartReel(transaction, ScrollClassId),
                    "get scroll smart reel",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "get scroll smart reel");
                return RazerBleProtocol30.ReadBoolean(response);
            },
            errors,
            cancellationToken).ConfigureAwait(false);

        var threshold = await TryStructAsync(
            "scroll smart reel threshold",
            async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateGetScrollSmartReelThreshold(transaction, ScrollClassId),
                    "get scroll smart reel threshold",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "get scroll smart reel threshold");
                return ReadFirstByte(response, "get scroll smart reel threshold");
            },
            errors,
            cancellationToken).ConfigureAwait(false);

        var acceleration = await TryStructAsync(
            "scroll acceleration",
            async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateGetScrollAcceleration(transaction, ScrollClassId),
                    "get scroll acceleration",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "get scroll acceleration");
                return RazerBleProtocol30.ReadBoolean(response);
            },
            errors,
            cancellationToken).ConfigureAwait(false);

        return new RazerBleScrollState(mode, smartReel, threshold, acceleration, errors);
    }

    public async Task<RazerBleScrollState> SetScrollAsync(
        byte? mode,
        bool? smartReel,
        byte? smartReelThreshold,
        bool? acceleration,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadScrollAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (mode is { } requestedMode)
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetScrollFreeNotchedMode(
                        transaction, requestedMode, ScrollClassId),
                    "set scroll mode",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "set scroll mode");
            }
            if (smartReel is { } requestedSmartReel)
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetScrollSmartReel(
                        transaction, requestedSmartReel, ScrollClassId),
                    "set scroll smart reel",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "set scroll smart reel");
            }
            if (smartReelThreshold is { } requestedThreshold)
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetScrollSmartReelThreshold(
                        transaction, requestedThreshold, ScrollClassId),
                    "set scroll smart reel threshold",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "set scroll smart reel threshold");
            }
            if (acceleration is { } requestedAcceleration)
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetScrollAcceleration(
                        transaction, requestedAcceleration, ScrollClassId),
                    "set scroll acceleration",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "set scroll acceleration");
            }

            var applied = await ReadScrollAsync(cancellationToken).ConfigureAwait(false);
            if ((mode is null || applied.Mode == mode) &&
                (smartReel is null || applied.SmartReel == smartReel) &&
                (smartReelThreshold is null || applied.SmartReelThreshold == smartReelThreshold) &&
                (acceleration is null || applied.Acceleration == acceleration))
            {
                return applied;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _diagnostic?.Invoke($"ble scroll write failed: {exception.Message}; restoring previous settings");
            await RestoreScrollAsync(current, cancellationToken).ConfigureAwait(false);
            throw;
        }

        _diagnostic?.Invoke("ble scroll settings did not read back as written; restoring the previous settings");
        await RestoreScrollAsync(current, cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(
            "The device kept different scroll settings; the previous settings were restored.");
    }

    public async Task<RazerBlePowerState> ReadPowerAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var lowBattery = await TryStructAsync(
            "low battery warning",
            async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateGetBatteryCriticalLevel(transaction, BatteryId),
                    "get low battery warning",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "get low battery warning");
                return RazerBleProtocol30.ReadBatteryCriticalPercent(response);
            },
            errors,
            cancellationToken).ConfigureAwait(false);

        var autoSleep = await TryStructAsync(
            "auto sleep",
            async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateGetAutoSleep(transaction, BleProfileId, BatteryId),
                    "get auto sleep",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "get auto sleep");
                return RazerBleProtocol30.ReadBoolean(response);
            },
            errors,
            cancellationToken).ConfigureAwait(false);

        var sleepTime = await TryStructAsync(
            "sleep time",
            async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateGetSleepTime(transaction, BleProfileId, BatteryId),
                    "get sleep time",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "get sleep time");
                return RazerBleProtocol30.ReadSleepMinutes(response);
            },
            errors,
            cancellationToken).ConfigureAwait(false);

        var timeToDim = await TryStructAsync(
            "time to dim",
            async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateGetTimeToDim(transaction, BleProfileId, BatteryId),
                    "get time to dim",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "get time to dim");
                return RazerBleProtocol30.ReadTimeToDim(response);
            },
            errors,
            cancellationToken).ConfigureAwait(false);

        return new RazerBlePowerState(lowBattery, autoSleep, sleepTime, timeToDim, errors);
    }

    public async Task<RazerBlePowerState> SetPowerAsync(
        int? lowBatteryPercent,
        bool? autoSleep,
        int? sleepMinutes,
        RazerBleTimeToDimState? timeToDim = null,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadPowerAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (lowBatteryPercent is { } requestedLowBattery)
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetBatteryCriticalLevel(
                        transaction, requestedLowBattery, BatteryId),
                    "set low battery warning",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "set low battery warning");
            }
            if (autoSleep is { } requestedAutoSleep)
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetAutoSleep(
                        transaction, requestedAutoSleep, BleProfileId, BatteryId),
                    "set auto sleep",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "set auto sleep");
            }
            if (sleepMinutes is { } requestedSleepMinutes)
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetSleepTime(transaction, requestedSleepMinutes),
                    "set sleep time",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "set sleep time");
            }
            if (timeToDim is { } requestedTimeToDim)
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetTimeToDim(
                        transaction, requestedTimeToDim, BleProfileId, BatteryId),
                    "set time to dim",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "set time to dim");
            }

            var applied = await ReadPowerAsync(cancellationToken).ConfigureAwait(false);
            if ((lowBatteryPercent is null || applied.LowBatteryPercent == lowBatteryPercent) &&
                (autoSleep is null || applied.AutoSleep == autoSleep) &&
                (sleepMinutes is null || applied.SleepMinutes == sleepMinutes) &&
                (timeToDim is null || applied.TimeToDim == timeToDim))
            {
                return applied;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _diagnostic?.Invoke($"ble power write failed: {exception.Message}; restoring previous settings");
            await RestorePowerAsync(current, cancellationToken).ConfigureAwait(false);
            throw;
        }

        _diagnostic?.Invoke("ble power settings did not read back as written; restoring the previous settings");
        await RestorePowerAsync(current, cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(
            "The device kept different power settings; the previous settings were restored.");
    }

    public async Task<RazerBleProfileState> ReadProfilesAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var ids = await TryAsync(
            "profiles",
            async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateGetProfileIds(transaction),
                    "get profile ids",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "get profile ids");
                return (IReadOnlyList<byte>)RazerBleProtocol30.ReadProfileIds(response);
            },
            errors,
            cancellationToken).ConfigureAwait(false);

        var active = await TryStructAsync(
            "active profile",
            async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateGetActiveProfileId(transaction),
                    "get active profile",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "get active profile");
                return RazerBleProtocol30.ReadActiveProfileId(response);
            },
            errors,
            cancellationToken).ConfigureAwait(false);

        return new RazerBleProfileState(ids ?? [], active);
    }

    public async Task SetActiveProfileAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        var current = await ReadProfilesAsync(cancellationToken).ConfigureAwait(false);
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetActiveProfileId(transaction, profileId),
            "set active profile",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "set active profile");

        var applied = await ReadProfilesAsync(cancellationToken).ConfigureAwait(false);
        if (applied.ActiveId == profileId)
        {
            return;
        }

        if (current.ActiveId is { } previous)
        {
            _diagnostic?.Invoke($"ble active profile read back {applied.ActiveId}, restoring {previous}");
            await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateSetActiveProfileId(transaction, previous),
                "restore active profile",
                cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException(
            $"The device kept profile {applied.ActiveId?.ToString() ?? "unknown"} instead of {profileId}.");
    }

    private async Task RestoreScrollAsync(RazerBleScrollState state, CancellationToken cancellationToken)
    {
        if (state.Mode is { } mode)
        {
            await TryRestoreAsync("restore scroll mode", async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetScrollFreeNotchedMode(
                        transaction, mode, ScrollClassId),
                    "restore scroll mode",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "restore scroll mode");
            }).ConfigureAwait(false);
        }
        if (state.SmartReel is { } smartReel)
        {
            await TryRestoreAsync("restore scroll smart reel", async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetScrollSmartReel(
                        transaction, smartReel, ScrollClassId),
                    "restore scroll smart reel",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "restore scroll smart reel");
            }).ConfigureAwait(false);
        }
        if (state.SmartReelThreshold is { } threshold)
        {
            await TryRestoreAsync("restore scroll smart reel threshold", async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetScrollSmartReelThreshold(
                        transaction, threshold, ScrollClassId),
                    "restore scroll smart reel threshold",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "restore scroll smart reel threshold");
            }).ConfigureAwait(false);
        }
        if (state.Acceleration is { } acceleration)
        {
            await TryRestoreAsync("restore scroll acceleration", async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetScrollAcceleration(
                        transaction, acceleration, ScrollClassId),
                    "restore scroll acceleration",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "restore scroll acceleration");
            }).ConfigureAwait(false);
        }
    }

    private async Task RestorePowerAsync(RazerBlePowerState state, CancellationToken cancellationToken)
    {
        if (state.LowBatteryPercent is { } lowBattery)
        {
            await TryRestoreAsync("restore low battery warning", async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetBatteryCriticalLevel(
                        transaction, lowBattery, BatteryId),
                    "restore low battery warning",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "restore low battery warning");
            }).ConfigureAwait(false);
        }
        if (state.AutoSleep is { } autoSleep)
        {
            await TryRestoreAsync("restore auto sleep", async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetAutoSleep(
                        transaction, autoSleep, BleProfileId, BatteryId),
                    "restore auto sleep",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "restore auto sleep");
            }).ConfigureAwait(false);
        }
        if (state.SleepMinutes is { } sleepMinutes)
        {
            await TryRestoreAsync("restore sleep time", async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetSleepTime(transaction, sleepMinutes),
                    "restore sleep time",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "restore sleep time");
            }).ConfigureAwait(false);
        }
        if (state.TimeToDim is { } timeToDim)
        {
            await TryRestoreAsync("restore time to dim", async () =>
            {
                var response = await _session.SendAsync(
                    transaction => RazerBleProtocol30.CreateSetTimeToDim(
                        transaction, timeToDim, BleProfileId, BatteryId),
                    "restore time to dim",
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccess(response, "restore time to dim");
            }).ConfigureAwait(false);
        }
    }

    private async Task TryRestoreAsync(string label, Func<Task> restore)
    {
        try
        {
            await restore().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _diagnostic?.Invoke($"ble {label} failed: {exception.Message}");
        }
    }

    private static byte ReadFirstByte(RazerBleResponse response, string operation)
    {
        if (response.Payload.Count == 0)
        {
            throw new InvalidOperationException($"BLE {operation} returned no payload.");
        }

        return response.Payload[0];
    }

    private static bool DpiStagesEqual(RazerBleDpiStages left, RazerBleDpiStages right) =>
        left.ActiveStage == right.ActiveStage &&
        left.Stages.Count == right.Stages.Count &&
        left.Stages.Zip(right.Stages).All(pair => pair.First == pair.Second);

    public void Dispose() => _session.Dispose();

    private static async Task<T?> TryStructAsync<T>(
        string label,
        Func<Task<T>> read,
        List<string> errors,
        CancellationToken cancellationToken)
        where T : struct
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException)
        {
            // A device that ignores this command class hides the field instead of failing the page.
            errors.Add(label);
            return null;
        }
    }

    private static async Task<T?> TryAsync<T>(
        string label,
        Func<Task<T>> read,
        List<string> errors,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException)
        {
            errors.Add(label);
            return null;
        }
    }
}


