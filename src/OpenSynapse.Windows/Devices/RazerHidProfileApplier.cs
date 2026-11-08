using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Applies the persisted mouse and keyboard settings through the same RazerHid
/// commands the device page uses. Lighting and mapping stay with their owners.
/// </summary>
public sealed partial class RazerHidProfileApplier : IDeviceProfileApplier
{
    private static readonly DeviceCategory[] SupportedCategories =
        [DeviceCategory.Mouse, DeviceCategory.Keyboard];
    private readonly Func<DeviceDescriptor, RazerHidDeviceConnection?> _resolveConnection;
    private readonly RazerHidDeviceService _service;

    public RazerHidProfileApplier(
        RazerHidDeviceService service,
        Func<DeviceDescriptor, RazerHidDeviceConnection?> resolveConnection)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _resolveConnection = resolveConnection ?? throw new ArgumentNullException(nameof(resolveConnection));
    }

    public IReadOnlyCollection<DeviceCategory> Categories => SupportedCategories;

    public bool Accepts(DeviceDescriptor device) =>
        !device.Id.StartsWith("ble:", StringComparison.OrdinalIgnoreCase);

    public async Task<ProfileDeviceApplyResult> ApplyAsync(
        DeviceProfileApplyContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Device.Category is not (DeviceCategory.Mouse or DeviceCategory.Keyboard))
        {
            return new ProfileDeviceApplyResult(context.Device.Id, ProfileDeviceApplyStatus.Skipped, []);
        }

        var connection = _resolveConnection(context.Device);
        if (connection is null || !connection.IsReady)
        {
            return new ProfileDeviceApplyResult(
                context.Device.Id,
                ProfileDeviceApplyStatus.Failed,
                [new ProfileSettingApplyResult("device", ProfileSettingApplyStatus.Failed,
                    "The RazerHid connection is not available.")]);
        }

        var results = new List<ProfileSettingApplyResult>();
        if (context.Device.Category == DeviceCategory.Mouse)
        {
            await ApplyMouseAsync(context.Profile.Viper, connection, results, cancellationToken);
        }
        else
        {
            await ApplyKeyboardAsync(
                context.Profile.Keyboard, context.Profile.Lighting, connection, results, cancellationToken);
        }

        var status = results.Any(item => item.Status == ProfileSettingApplyStatus.Failed)
            ? ProfileDeviceApplyStatus.Failed
            : results.Any(item => item.Status is ProfileSettingApplyStatus.Applied or
                ProfileSettingApplyStatus.AppliedUnverified)
                ? ProfileDeviceApplyStatus.Applied
                : ProfileDeviceApplyStatus.Skipped;
        return new ProfileDeviceApplyResult(context.Device.Id, status, results);
    }

    private async Task ApplyMouseAsync(
        MouseProfileSettings profile,
        RazerHidDeviceConnection connection,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (profile.DpiStages is { } stages)
        {
            var desired = new RazerHidDpiStages(
                stages.ActiveStage,
                stages.Stages.Select(stage => new RazerHidDpiStage(
                    stage.Number, stage.X, stage.Y)).ToArray());
            await ApplySettingAsync(
                results, "mouse.dpi-stages", connection,
                RazerHidBackendCapability.DpiStagesRead,
                RazerHidBackendCapability.DpiStagesWrite,
                token => _service.GetDpiStagesAsync(connection, token),
                (value, token) => _service.SetDpiStagesAsync(connection, value, token),
                desired, SameDpiStages, cancellationToken);
        }
        else if (profile.DpiX is { } x && profile.DpiY is { } y)
        {
            await ApplySettingAsync(
                results, "mouse.dpi", connection,
                RazerHidBackendCapability.DpiRead,
                RazerHidBackendCapability.DpiWrite,
                token => _service.GetDpiAsync(connection, token),
                async (value, token) => _ = await _service.SetDpiAsync(connection, value.X, value.Y, token),
                (X: x, Y: y), (left, right) => left == right, cancellationToken);
        }

        if (profile.PollingRateHertz is { } polling)
        {
            await ApplySettingAsync(
                results, "mouse.polling-rate", connection,
                RazerHidBackendCapability.PollingRateRead,
                RazerHidBackendCapability.PollingRateWrite,
                token => _service.GetPollingRateAsync(connection, token),
                async (value, token) => _ = await _service.SetPollingRateAsync(connection, value, token),
                polling, (left, right) => left == right, cancellationToken);
        }

        if (profile.IdleSeconds is { } idle)
        {
            await ApplySettingAsync(
                results, "mouse.idle-timeout", connection,
                RazerHidBackendCapability.IdleTimeoutRead,
                RazerHidBackendCapability.IdleTimeoutWrite,
                token => _service.GetIdleTimeoutAsync(connection, token),
                (value, token) => _service.SetIdleTimeoutAsync(connection, value, token),
                idle, (left, right) => left == right, cancellationToken);
        }

        if (profile.LowBatteryThresholdPercent is { } lowBattery)
        {
            await ApplySettingAsync(
                results, "mouse.low-battery", connection,
                RazerHidBackendCapability.LowBatteryThresholdRead,
                RazerHidBackendCapability.LowBatteryThresholdWrite,
                token => _service.GetLowBatteryThresholdAsync(connection, token),
                (value, token) => _service.SetLowBatteryThresholdAsync(connection, value, token),
                lowBattery, (left, right) => left == right, cancellationToken);
        }

        if (profile.AutoSleepSeconds is { } autoSleep)
        {
            await ApplySettingAsync(
                results, "mouse.auto-sleep", connection,
                RazerHidBackendCapability.AutoSleepRead,
                RazerHidBackendCapability.AutoSleepWrite,
                token => _service.GetAutoSleepAsync(connection, token),
                (value, token) => _service.SetAutoSleepAsync(connection, value, token),
                autoSleep > 0, (left, right) => left == right, cancellationToken);
        }

        if (profile.ChargeLimitPercent is { } chargeLimit)
        {
            await ApplySettingAsync(
                results, "mouse.charge-limit", connection,
                RazerHidBackendCapability.BatteryChargeLimiterRead,
                RazerHidBackendCapability.BatteryChargeLimiterWrite,
                token => _service.GetBatteryChargeLimiterAsync(connection, token),
                (value, token) => _service.SetBatteryChargeLimiterAsync(
                    connection, value.Level, value.Enabled, token),
                (Level: (byte)chargeLimit, Enabled: chargeLimit > 0),
                (left, right) => left == right, cancellationToken);
        }

        if (TryParseScrollMode(profile.ScrollMode, out var scrollMode))
        {
            await ApplySettingAsync(
                results, "mouse.scroll-mode", connection,
                RazerHidBackendCapability.ScrollModeRead,
                RazerHidBackendCapability.ScrollModeWrite,
                token => _service.GetScrollModeAsync(connection, token),
                (value, token) => _service.SetScrollModeAsync(connection, value, token),
                scrollMode, (left, right) => left == right, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(profile.ScrollMode))
        {
            results.Add(new ProfileSettingApplyResult(
                "mouse.scroll-mode", ProfileSettingApplyStatus.Failed, "Unknown scroll mode."));
        }

        if (profile.ScrollFreeNotched is { } freeNotched)
        {
            await ApplySettingAsync(
                results, "mouse.scroll-free-notched", connection,
                RazerHidBackendCapability.ScrollFreeNotchedModeRead,
                RazerHidBackendCapability.ScrollFreeNotchedModeWrite,
                token => _service.GetScrollFreeNotchedModeAsync(connection, token),
                (value, token) => _service.SetScrollFreeNotchedModeAsync(connection, value, token),
                freeNotched ? RazerScrollFreeNotchedMode.FreeSpinMode : RazerScrollFreeNotchedMode.NotchedMode,
                (left, right) => left == right, cancellationToken);
        }

        if (profile.ScrollAccelerationEnabled is { } acceleration)
        {
            await ApplySettingAsync(
                results, "mouse.scroll-acceleration", connection,
                RazerHidBackendCapability.ScrollAccelerationRead,
                RazerHidBackendCapability.ScrollAccelerationWrite,
                token => _service.GetScrollAccelerationAsync(connection, token),
                (value, token) => _service.SetScrollAccelerationAsync(connection, value, token),
                acceleration, (left, right) => left == right, cancellationToken);
        }

        if (profile.ScrollAccelerationLevel is { } accelerationLevel)
        {
            await ApplySettingAsync(
                results, "mouse.scroll-acceleration-level", connection,
                RazerHidBackendCapability.ScrollAccelerationLevelRead,
                RazerHidBackendCapability.ScrollAccelerationLevelWrite,
                token => _service.GetScrollVirtualAccelerationLevelAsync(connection, token),
                (value, token) => _service.SetScrollVirtualAccelerationLevelAsync(connection, value, token),
                (byte)accelerationLevel, (left, right) => left == right, cancellationToken);
        }

        if (profile.SmartReelEnabled is { } smartReel)
        {
            await ApplySettingAsync(
                results, "mouse.smart-reel", connection,
                RazerHidBackendCapability.SmartReelRead,
                RazerHidBackendCapability.SmartReelWrite,
                token => _service.GetSmartReelAsync(connection, token),
                (value, token) => _service.SetSmartReelAsync(connection, value, token),
                smartReel, (left, right) => left == right, cancellationToken);
        }

        if (profile.SmartReelThreshold is { } smartReelThreshold)
        {
            await ApplySettingAsync(
                results, "mouse.smart-reel-threshold", connection,
                RazerHidBackendCapability.SmartReelThresholdRead,
                RazerHidBackendCapability.SmartReelThresholdWrite,
                token => _service.GetSmartReelThresholdAsync(connection, token),
                (value, token) => _service.SetSmartReelThresholdAsync(connection, value, token),
                (byte)smartReelThreshold, (left, right) => left == right, cancellationToken);
        }

        if (profile.ActiveOnboardProfileId is { } onboardProfile)
        {
            await ApplySettingAsync(
                results, "mouse.onboard-profile", connection,
                RazerHidBackendCapability.OnboardProfileRead,
                RazerHidBackendCapability.OnboardProfileWrite,
                token => _service.GetActiveOnboardProfileIdAsync(connection, token),
                (value, token) => _service.SetActiveOnboardProfileIdAsync(connection, value, token),
                (byte)onboardProfile, (left, right) => left == right, cancellationToken);
        }

        if (profile.HapticScrollLevel is { } hapticLevel)
        {
            await ApplyHapticScrollAsync(connection, (byte)hapticLevel, results, cancellationToken);
        }

        if (profile.ButtonMappings is { Count: > 0 } mappings)
        {
            await ApplyButtonMappingsAsync(connection, mappings, results, cancellationToken);
        }
    }

    private async Task ApplyButtonMappingsAsync(
        RazerHidDeviceConnection connection,
        IReadOnlyCollection<ButtonMappingProfile> mappings,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (!Has(connection, RazerHidBackendCapability.MappingRead))
        {
            results.Add(Unsupported("mapping", RazerHidBackendCapability.MappingRead));
            return;
        }
        if (!Has(connection, RazerHidBackendCapability.MappingWrite))
        {
            results.Add(Unsupported("mapping", RazerHidBackendCapability.MappingWrite));
            return;
        }

        foreach (var mapping in mappings)
        {
            var mode = mapping.Mode ?? 0;
            var key = $"mapping.{mode}.{mapping.ButtonId}";
            if (mapping.FunctionId is not { } functionId)
            {
                results.Add(new ProfileSettingApplyResult(
                    key, ProfileSettingApplyStatus.Skipped,
                    "The mapping has no raw assignment to apply."));
                continue;
            }

            try
            {
                var current = await _service.GetButtonAssignmentAsync(
                    connection, mapping.ButtonId, mode, cancellationToken);
                var data = mapping.FunctionData?.ToArray() ?? [];
                if (current.FunctionId == functionId && current.Data.SequenceEqual(data))
                {
                    results.Add(Applied(key));
                    continue;
                }

                await _service.SetButtonAssignmentAsync(
                    connection, mapping.ButtonId, mode, functionId, data, cancellationToken);
                results.Add(Applied(key));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(Failed(key, exception));
            }
        }
    }

    private async Task ApplyHapticScrollAsync(
        RazerHidDeviceConnection connection,
        byte level,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        const string key = "mouse.haptic-scroll-level";
        if (!Has(connection, RazerHidBackendCapability.HapticScrollWrite))
        {
            results.Add(Unsupported(key, RazerHidBackendCapability.HapticScrollWrite));
            return;
        }
        if (!Has(connection, RazerHidBackendCapability.HapticScrollRead))
        {
            results.Add(new ProfileSettingApplyResult(key, ProfileSettingApplyStatus.Skipped,
                "Reading the current haptic stage list is required before changing its active level."));
            return;
        }

        try
        {
            var current = await _service.GetHapticScrollStagesAsync(connection, ProfileId, cancellationToken);
            if (current.ActiveStage == level)
            {
                results.Add(Applied(key));
                return;
            }

            var stages = current.StageTypes.Select(stage => (byte)stage).ToArray();
            await _service.SetHapticScrollStagesAsync(connection, ProfileId, level, stages, cancellationToken);
            var applied = await _service.GetHapticScrollStagesAsync(connection, ProfileId, cancellationToken);
            results.Add(applied.ActiveStage == level
                ? Applied(key)
                : Mismatch(key, $"expected {level}, got {applied.ActiveStage}"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            results.Add(Failed(key, exception));
        }
    }

    private static async Task ApplySettingAsync<T>(
        List<ProfileSettingApplyResult> results,
        string key,
        RazerHidDeviceConnection connection,
        RazerHidBackendCapability readCapability,
        RazerHidBackendCapability writeCapability,
        Func<CancellationToken, Task<T>> readAsync,
        Func<T, CancellationToken, Task> writeAsync,
        T desired,
        Func<T, T, bool> equals,
        CancellationToken cancellationToken,
        bool hasRead = true)
    {
        if (!Has(connection, writeCapability))
        {
            results.Add(Unsupported(key, writeCapability));
            return;
        }

        var canRead = hasRead && Has(connection, readCapability);
        if (canRead)
        {
            try
            {
                if (equals(await readAsync(cancellationToken), desired))
                {
                    results.Add(Applied(key));
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // The setting may still be writable when its read is unavailable.
            }
        }

        try
        {
            await writeAsync(desired, cancellationToken);
            if (!canRead)
            {
                results.Add(new ProfileSettingApplyResult(key,
                    ProfileSettingApplyStatus.AppliedUnverified));
                return;
            }

            var applied = await readAsync(cancellationToken);
            results.Add(equals(applied, desired)
                ? Applied(key)
                : Mismatch(key, "read-back did not match the requested value"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            results.Add(Failed(key, exception));
        }
    }

    private static bool Has(RazerHidDeviceConnection connection, RazerHidBackendCapability capability) =>
        connection.Capabilities.Contains(capability);

    private static bool SameDpiStages(RazerHidDpiStages left, RazerHidDpiStages right) =>
        left.ActiveStage == right.ActiveStage &&
        left.Stages.Count == right.Stages.Count &&
        left.Stages.Zip(right.Stages).All(pair =>
            pair.First.Number == pair.Second.Number &&
            pair.First.X == pair.Second.X &&
            pair.First.Y == pair.Second.Y);

    private static bool TryParseScrollMode(string? value, out byte mode)
    {
        mode = 0;
        if (string.IsNullOrWhiteSpace(value) ||
            !Enum.TryParse<MouseScrollMode>(value, ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            return false;
        }

        mode = parsed switch
        {
            MouseScrollMode.Tactile => 0,
            MouseScrollMode.FreeSpin => 1,
            MouseScrollMode.MicroTactile => 2,
            _ => 0,
        };
        return true;
    }

    private static ProfileSettingApplyResult Applied(string key) =>
        new(key, ProfileSettingApplyStatus.Applied);

    private static ProfileSettingApplyResult Unsupported(string key, RazerHidBackendCapability capability) =>
        new(key, ProfileSettingApplyStatus.Skipped, $"Device does not support {capability}.");

    private static ProfileSettingApplyResult Mismatch(string key, string detail) =>
        new(key, ProfileSettingApplyStatus.Failed, detail);

    private static ProfileSettingApplyResult Failed(string key, Exception exception) =>
        new(key, ProfileSettingApplyStatus.Failed, exception.Message);

    private const byte ProfileId = 0x01;
}

