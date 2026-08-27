using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public enum OpenRazerKeyswitchOptimization
{
    Typing,
    Gaming,
}

public sealed class OpenRazerDeviceService
{
    private readonly OpenRazerDeviceCatalog _catalog;
    private readonly IRazerFeatureTransport _transport;
    private readonly ConcurrentDictionary<string, string> _endpointCache = new(StringComparer.OrdinalIgnoreCase);

    public OpenRazerDeviceService()
        : this(OpenRazerDeviceCatalog.BuiltIn, new RazerFeatureTransport())
    {
    }

    internal OpenRazerDeviceService(OpenRazerDeviceCatalog catalog, IRazerFeatureTransport transport)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<IReadOnlyList<OpenRazerDeviceConnection>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorFeatureInterfacesAsync(
            OpenRazerDeviceDefinition.VendorId,
            RazerFeatureReport.Length,
            cancellationToken).ConfigureAwait(false);
        var results = new List<OpenRazerDeviceConnection>();
        foreach (var group in interfaces
            .Where(item => _catalog.Find(item.VendorId, item.ProductId) is not null)
            .GroupBy(item => (item.ProductId, item.PhysicalDeviceKey))
            .OrderBy(group => group.Key.ProductId)
            .ThenBy(group => group.Key.PhysicalDeviceKey, StringComparer.OrdinalIgnoreCase))
        {
            var definition = _catalog.Find(OpenRazerDeviceDefinition.VendorId, group.Key.ProductId)!;
            var candidates = group.ToArray();
            var available = candidates.Where(candidate => candidate.Access == DeviceAccessState.Available).ToArray();
            if (available.Length == 0)
            {
                results.Add(CreateConnection(definition, null, group.Key.PhysicalDeviceKey,
                    OpenRazerEndpointState.BusyOrUnavailable,
                    "All matching 91-byte HID collections are busy or unavailable."));
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
                try
                {
                    if (await ProbeEndpointAsync(definition, candidate.DevicePath, cancellationToken).ConfigureAwait(false))
                    {
                        resolvedPath = candidate.DevicePath;
                        break;
                    }
                    lastError = "No side-effect-free GET with a known transaction is available for endpoint probing.";
                }
                catch (Exception exception) when (exception is Win32Exception or IOException or
                    InvalidOperationException or NotSupportedException)
                {
                    lastError = exception.Message;
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
                results.Add(CreateConnection(definition, resolvedPath, group.Key.PhysicalDeviceKey,
                    OpenRazerEndpointState.Resolved, null));
            }
        }
        return results;
    }

    public async Task<OpenRazerBasicState> ReadBasicStateAsync(
        OpenRazerDeviceConnection connection,
        CancellationToken cancellationToken = default)
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
        byte? brightness = null;
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        async Task ReadAsync<T>(OpenRazerBackendCapability capability, string key, Func<Task<T>> read, Action<T> assign)
        {
            if (!connection.Capabilities.Contains(capability))
            {
                return;
            }
            try
            {
                assign(await read().ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or
                InvalidOperationException or NotSupportedException)
            {
                errors[key] = exception.Message;
            }
        }

        await ReadAsync(OpenRazerBackendCapability.FirmwareRead, "firmware",
            () => GetFirmwareAsync(connection, cancellationToken), value => firmware = value);
        await ReadAsync(OpenRazerBackendCapability.SerialRead, "serial",
            () => GetSerialAsync(connection, cancellationToken), value => serial = value);
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
        await ReadAsync(OpenRazerBackendCapability.BrightnessRead, "brightness",
            () => GetBrightnessAsync(connection, cancellationToken: cancellationToken), value => brightness = value);

        return new OpenRazerBasicState(firmware, serial, softwareMode, battery, charging,
            pollingRate, dpiX, dpiY, idle, threshold, brightness, errors);
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

    public async Task SetPollingRateAsync(OpenRazerDeviceConnection connection, int hertz, CancellationToken cancellationToken = default)
    {
        foreach (var request in OpenRazerMouseProtocol.SetPollingRate(connection.Definition, hertz))
        {
            await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<(int X, int Y)> GetDpiAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default)
    {
        var byteEncoding = connection.Definition.Transactions.ContainsKey("razer_chroma_misc_get_dpi_xy_byte");
        return OpenRazerMouseProtocol.ParseDpi(await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetDpi(connection.Definition), cancellationToken).ConfigureAwait(false), byteEncoding);
    }

    public Task SetDpiAsync(OpenRazerDeviceConnection connection, int x, int y, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetDpi(connection.Definition, x, y), cancellationToken);

    public Task SetDpiStagesAsync(OpenRazerDeviceConnection connection, OpenRazerDpiStages state, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            OpenRazerMouseProtocol.SetDpiStages(connection.Definition, state), cancellationToken);

    public async Task<OpenRazerDpiStages> GetDpiStagesAsync(OpenRazerDeviceConnection connection, CancellationToken cancellationToken = default) =>
        OpenRazerMouseProtocol.ParseDpiStages(connection.Definition, await ExecuteAsync(connection,
            OpenRazerMouseProtocol.GetDpiStages(connection.Definition), cancellationToken).ConfigureAwait(false));

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

    public async Task<byte> GetBrightnessAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerStorage? storage = null,
        OpenRazerLedZone? ledId = null,
        CancellationToken cancellationToken = default)
    {
        var zone = OpenRazerLightingProtocol.ResolveBrightnessZone(connection.Definition, write: false, storage, ledId);
        RequireLightingZone(connection, zone, capability => capability.CanReadBrightness, "brightness read");
        var request = OpenRazerLightingProtocol.GetBrightness(connection.Definition, storage, zone);
        var arguments = OpenRazerStandardProtocol.Arguments(await ExecuteAsync(connection,
            request, cancellationToken).ConfigureAwait(false),
            request.BuilderName == "razer_chroma_misc_get_dock_brightness" ? (byte)1 : (byte)2);
        return request.BuilderName switch
        {
            "razer_chroma_misc_get_dock_brightness" => arguments[0],
            "razer_chroma_misc_get_blade_brightness" => arguments[1],
            _ => arguments[2],
        };
    }

    public Task SetBrightnessAsync(
        OpenRazerDeviceConnection connection,
        byte brightness,
        OpenRazerStorage? storage = null,
        OpenRazerLedZone? ledId = null,
        CancellationToken cancellationToken = default)
    {
        var zone = OpenRazerLightingProtocol.ResolveBrightnessZone(connection.Definition, write: true, storage, ledId);
        RequireLightingZone(connection, zone, capability => capability.CanWriteBrightness, "brightness write");
        return ExecuteWithoutResultAsync(connection,
            OpenRazerLightingProtocol.SetBrightness(connection.Definition, storage, zone, brightness), cancellationToken);
    }

    public async Task SetLightingAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerLightingSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
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

    public async Task<OpenRazerClassicLedEffect> GetLedEffectAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerStorage storage,
        OpenRazerLedZone ledId,
        CancellationToken cancellationToken = default)
    {
        RequireClassicLedOperation(connection, storage, ledId,
            capability => capability.CanReadEffect, "classic LED effect read",
            "razer_chroma_standard_get_led_effect");
        return OpenRazerStandardProtocol.ParseLedEffect(await ExecuteAsync(connection,
            OpenRazerStandardProtocol.GetLedEffect(connection.Definition, (byte)storage, (byte)ledId),
            cancellationToken).ConfigureAwait(false), (byte)storage, (byte)ledId);
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
        AddIfResolved("razer_chroma_extended_matrix_get_brightness", () =>
            OpenRazerLightingProtocol.GetBrightness(definition, null, null));
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
        string? error)
    {
        var resolved = state == OpenRazerEndpointState.Resolved;
        return new(definition, path, physicalDeviceKey, state,
            resolved ? GetCapabilities(definition) : FrozenSet<OpenRazerBackendCapability>.Empty,
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
            var canReadBrightness = CanBuild(() => OpenRazerLightingProtocol.GetBrightness(
                device, device.DefaultStorage, zone));
            var canWriteBrightness = CanBuild(() => OpenRazerLightingProtocol.SetBrightness(
                device, device.DefaultStorage, zone, 0));
            var canReadState = CanUseClassicBuilder(device, "razer_chroma_standard_get_led_state", zone);
            var canWriteState = device.Transactions.ContainsKey("razer_chroma_standard_set_led_state") &&
                HasSourceStateWrite(device, zone) &&
                SupportsStorageAndZone(device, "razer_chroma_standard_set_led_state",
                    device.DefaultStorage, zone);
            var canReadEffect = CanUseClassicBuilder(device, "razer_chroma_standard_get_led_effect", zone);
            var canWriteEffect = CanUseClassicBuilder(device, "razer_chroma_standard_set_led_effect", zone);
            var canReadColor = CanUseClassicBuilder(device, "razer_chroma_standard_get_led_rgb", zone);
            var canWriteColor = CanUseClassicBuilder(device, "razer_chroma_standard_set_led_rgb", zone);
            var canWriteBlinking = CanUseClassicBuilder(device, "razer_chroma_standard_set_led_blinking", zone);
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
        Add(OpenRazerBackendCapability.FirmwareRead, "razer_chroma_standard_get_firmware_version");
        Add(OpenRazerBackendCapability.SerialRead, "razer_chroma_standard_get_serial");
        Add(OpenRazerBackendCapability.DeviceModeRead, "razer_chroma_standard_get_device_mode");
        Add(OpenRazerBackendCapability.BatteryRead, "razer_chroma_misc_get_battery_level");
        Add(OpenRazerBackendCapability.ChargingRead, "razer_chroma_misc_get_charging_status");
        AddAny(OpenRazerBackendCapability.PollingRateRead,
            "razer_chroma_misc_get_polling_rate", "razer_chroma_misc_get_polling_rate2");
        AddAny(OpenRazerBackendCapability.PollingRateWrite,
            "razer_chroma_misc_set_polling_rate", "razer_chroma_misc_set_polling_rate2");
        AddAny(OpenRazerBackendCapability.DpiRead,
            "razer_chroma_misc_get_dpi_xy", "razer_chroma_misc_get_dpi_xy_byte");
        AddAny(OpenRazerBackendCapability.DpiWrite,
            "razer_chroma_misc_set_dpi_xy", "razer_chroma_misc_set_dpi_xy_byte");
        Add(OpenRazerBackendCapability.DpiStagesRead, "razer_chroma_misc_get_dpi_stages");
        Add(OpenRazerBackendCapability.DpiStagesWrite, "razer_chroma_misc_set_dpi_stages");
        Add(OpenRazerBackendCapability.IdleTimeoutRead, "razer_chroma_misc_get_idle_time");
        Add(OpenRazerBackendCapability.IdleTimeoutWrite, "razer_chroma_misc_set_idle_time");
        Add(OpenRazerBackendCapability.LowBatteryThresholdRead, "razer_chroma_misc_get_low_battery_threshold");
        Add(OpenRazerBackendCapability.LowBatteryThresholdWrite, "razer_chroma_misc_set_low_battery_threshold");
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
        AddAny(OpenRazerBackendCapability.MatrixFrameWrite,
            "razer_chroma_standard_matrix_set_custom_frame",
            "razer_chroma_extended_matrix_set_custom_frame",
            "razer_chroma_extended_matrix_set_custom_frame2");
        Add(OpenRazerBackendCapability.ReactiveTriggerWrite, "razer_chroma_misc_matrix_reactive_trigger");
        Add(OpenRazerBackendCapability.ScrollModeRead, "razer_chroma_misc_get_scroll_mode");
        Add(OpenRazerBackendCapability.ScrollModeWrite, "razer_chroma_misc_set_scroll_mode");
        Add(OpenRazerBackendCapability.ScrollAccelerationRead, "razer_chroma_misc_get_scroll_acceleration");
        Add(OpenRazerBackendCapability.ScrollAccelerationWrite, "razer_chroma_misc_set_scroll_acceleration");
        Add(OpenRazerBackendCapability.SmartReelRead, "razer_chroma_misc_get_scroll_smart_reel");
        Add(OpenRazerBackendCapability.SmartReelWrite, "razer_chroma_misc_set_scroll_smart_reel");
        Add(OpenRazerBackendCapability.FnPrimaryWrite, "razer_chroma_misc_fn_key_toggle");
        Add(OpenRazerBackendCapability.KeyswitchOptimizationRead, "razer_chroma_misc_get_keyswitch_optimization");
        if (Has("razer_chroma_misc_set_keyswitch_optimization_command1") &&
            Has("razer_chroma_misc_set_keyswitch_optimization_command2"))
            result.Add(OpenRazerBackendCapability.KeyswitchOptimizationWrite);
        Add(OpenRazerBackendCapability.HyperPollingIndicatorWrite,
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_indicator_led_mode");
        if (Has("razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step1") &&
            Has("razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step2"))
            result.Add(OpenRazerBackendCapability.HyperPollingPairWrite);
        Add(OpenRazerBackendCapability.HyperPollingUnpairWrite,
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_unpair");
        return result.ToFrozenSet();

        bool Has(string builder) => device.Transactions.ContainsKey(builder) || device.CommandSequences.ContainsKey(builder);
        void Add(OpenRazerBackendCapability capability, string builder)
        {
            if (Has(builder)) result.Add(capability);
        }
        void AddAny(OpenRazerBackendCapability capability, params string[] builders)
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
        try
        {
            return await _transport.QueryPreparedAsync(
                connection.DevicePath!, request.Report, connection.Definition.DeviceWait,
                cancellationToken).ConfigureAwait(false);
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
