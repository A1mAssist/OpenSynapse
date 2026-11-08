using System.Globalization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Applies one profile to a protocol 30 BLE device. The BLE service verifies every
/// write with a read-back, so an item that returns without throwing is accepted.
/// The service resolver owns the session lifetime; this applier never disposes it.
/// </summary>
public sealed partial class RazerBleProfileApplier : IDeviceProfileApplier
{
    private static readonly DeviceCategory[] SupportedCategories = [DeviceCategory.Mouse];
    private readonly Func<string, RazerBleDevice?> _resolveDevice;
    private readonly Func<RazerBleDevice, CancellationToken, Task<RazerBleDeviceService?>> _resolveService;

    public RazerBleProfileApplier(
        Func<string, RazerBleDevice?> resolveDevice,
        Func<RazerBleDevice, CancellationToken, Task<RazerBleDeviceService?>> resolveService)
    {
        _resolveDevice = resolveDevice ?? throw new ArgumentNullException(nameof(resolveDevice));
        _resolveService = resolveService ?? throw new ArgumentNullException(nameof(resolveService));
    }

    public IReadOnlyCollection<DeviceCategory> Categories => SupportedCategories;

    public bool Accepts(DeviceDescriptor device) =>
        device.Id.StartsWith("ble:", StringComparison.OrdinalIgnoreCase);

    public async Task<ProfileDeviceApplyResult> ApplyAsync(
        DeviceProfileApplyContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Device.Category != DeviceCategory.Mouse)
        {
            return new ProfileDeviceApplyResult(context.Device.Id, ProfileDeviceApplyStatus.Skipped, []);
        }

        var key = BleKey(context.Device.Id);
        var device = _resolveDevice(key);
        if (device is null)
        {
            return Failed(context.Device.Id, "The BLE device is not connected.");
        }

        var service = await _resolveService(device, cancellationToken);
        if (service is null)
        {
            return Failed(context.Device.Id, "The BLE session could not be opened.");
        }

        context.Document.Devices.TryGetValue(key, out var deviceSettings);
        var powerSettings = context.PowerSource switch
        {
            PowerSourceKind.FullPowerAdapter => context.Document.PluggedIn,
            PowerSourceKind.LowPowerAdapter => context.Document.LowPowerAdapter,
            PowerSourceKind.Battery => context.Document.OnBattery,
            _ => null,
        };
        var settings = ResolveMouse(
            context.Document.Global.Viper,
            deviceSettings?.Viper,
            powerSettings?.Viper);
        var lighting = powerSettings?.Lighting ?? deviceSettings?.Lighting ?? context.Document.Global.Lighting;

        var results = new List<ProfileSettingApplyResult>();
        await ApplyDpiAsync(service, settings, results, cancellationToken);
        await ApplyPollingAsync(service, settings, results, cancellationToken);
        await ApplyPowerAsync(service, settings, results, cancellationToken);
        await ApplyScrollAsync(service, settings, results, cancellationToken);
        await ApplyPowerPlanAsync(service, settings, results, cancellationToken);
        await ApplyHapticAsync(service, settings, results, cancellationToken);
        await ApplyBatteryChemistryAsync(service, settings, results, cancellationToken);
        await ApplySmartIlluminationAsync(service, settings, results, cancellationToken);
        await ApplySnapTapAsync(service, settings, results, cancellationToken);
        await ApplyDebounceAsync(service, settings, results, cancellationToken);
        await ApplyLightingAsync(service, lighting, results, cancellationToken);

        var status = results.Any(item => item.Status == ProfileSettingApplyStatus.Failed)
            ? ProfileDeviceApplyStatus.Failed
            : results.Any(item => item.Status is ProfileSettingApplyStatus.Applied or
                ProfileSettingApplyStatus.AppliedUnverified)
                ? ProfileDeviceApplyStatus.Applied
                : ProfileDeviceApplyStatus.Skipped;
        return new ProfileDeviceApplyResult(context.Device.Id, status, results);
    }

    private static async Task ApplyDpiAsync(
        RazerBleDeviceService service,
        MouseProfileSettings settings,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (settings.DpiStages is { Stages.Count: > 0 } stages)
        {
            var desired = new RazerBleDpiStages(
                stages.ActiveStage,
                stages.Stages
                    .Select(stage => new RazerBleDpiStage(stage.Number, stage.X, stage.Y, 0))
                    .ToArray());
            await ApplySettingAsync(
                results, "ble.dpi-stages",
                token => service.ReadDpiStagesAsync(token),
                (value, token) => service.SetDpiStagesAsync(value, token),
                desired, DpiStagesEqual, cancellationToken);
            return;
        }

        if (settings.DpiX is { } x && settings.DpiY is { } y)
        {
            (int X, int Y) desired = (x, y);
            await ApplySettingAsync(
                results, "ble.dpi",
                async token => (await service.ReadStateAsync(token)).Dpi is { } dpi
                    ? (dpi.X, dpi.Y)
                    : throw new InvalidOperationException("The device did not report DPI."),
                async (value, token) => _ = await service.SetDpiAsync(value.X, value.Y, token),
                desired, (left, right) => left == right, cancellationToken);
        }
    }

    private static Task ApplyPollingAsync(
        RazerBleDeviceService service,
        MouseProfileSettings settings,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (settings.PollingRateHertz is not { } polling)
        {
            return Task.CompletedTask;
        }

        return ApplySettingAsync(
            results, "ble.polling-rate",
            async token => (await service.ReadStateAsync(token)).PollingHertz
                ?? throw new InvalidOperationException("The device did not report a polling rate."),
            async (value, token) => _ = await service.SetPollingRateAsync(value, token),
            polling, (left, right) => left == right, cancellationToken);
    }

    private static async Task ApplyPowerAsync(
        RazerBleDeviceService service,
        MouseProfileSettings settings,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (settings.LowBatteryThresholdPercent is { } lowBattery)
        {
            await ApplySettingAsync(
                results, "ble.low-battery",
                async token => (await service.ReadPowerAsync(token)).LowBatteryPercent
                    ?? throw new InvalidOperationException("The device did not report the low-battery threshold."),
                (value, token) => service.SetPowerAsync(value, null, null, null, token),
                lowBattery, (left, right) => left == right, cancellationToken);
        }

        if (settings.AutoSleepSeconds is { } autoSleepSeconds)
        {
            var enabled = autoSleepSeconds > 0;
            await ApplySettingAsync(
                results, "ble.auto-sleep",
                async token => (await service.ReadPowerAsync(token)).AutoSleep
                    ?? throw new InvalidOperationException("The device did not report auto-sleep."),
                (value, token) => service.SetPowerAsync(null, value, null, null, token),
                enabled, (left, right) => left == right, cancellationToken);
        }

        if (settings.SleepTimeoutSeconds is { } sleepSeconds && sleepSeconds > 0)
        {
            var minutes = Math.Clamp(sleepSeconds / 60, 1, 65_535);
            await ApplySettingAsync(
                results, "ble.sleep-timeout",
                async token => (await service.ReadPowerAsync(token)).SleepMinutes
                    ?? throw new InvalidOperationException("The device did not report the sleep timeout."),
                (value, token) => service.SetPowerAsync(null, null, value, null, token),
                minutes, (left, right) => left == right, cancellationToken);
        }
    }

    private static async Task ApplyScrollAsync(
        RazerBleDeviceService service,
        MouseProfileSettings settings,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (TryParseScrollMode(settings.ScrollMode, out var mode))
        {
            await ApplySettingAsync(
                results, "ble.scroll-mode",
                async token => (await service.ReadScrollAsync(token)).Mode
                    ?? throw new InvalidOperationException("The device did not report the scroll mode."),
                (value, token) => service.SetScrollAsync(value, null, null, null, token),
                mode, (left, right) => left == right, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(settings.ScrollMode))
        {
            results.Add(new ProfileSettingApplyResult(
                "ble.scroll-mode", ProfileSettingApplyStatus.Failed, "Unknown scroll mode."));
        }

        if (settings.ScrollAccelerationEnabled is { } acceleration)
        {
            await ApplySettingAsync(
                results, "ble.scroll-acceleration",
                async token => (await service.ReadScrollAsync(token)).Acceleration
                    ?? throw new InvalidOperationException("The device did not report scroll acceleration."),
                (value, token) => service.SetScrollAsync(null, null, null, value, token),
                acceleration, (left, right) => left == right, cancellationToken);
        }

        if (settings.SmartReelEnabled is { } smartReel)
        {
            await ApplySettingAsync(
                results, "ble.smart-reel",
                async token => (await service.ReadScrollAsync(token)).SmartReel
                    ?? throw new InvalidOperationException("The device did not report smart reel."),
                (value, token) => service.SetScrollAsync(null, value, null, null, token),
                smartReel, (left, right) => left == right, cancellationToken);
        }

        if (settings.SmartReelThreshold is { } threshold)
        {
            await ApplySettingAsync(
                results, "ble.smart-reel-threshold",
                async token => (await service.ReadScrollAsync(token)).SmartReelThreshold
                    ?? throw new InvalidOperationException("The device did not report the smart reel threshold."),
                (value, token) => service.SetScrollAsync(null, null, value, null, token),
                (byte)threshold, (left, right) => left == right, cancellationToken);
        }
    }

    private static Task ApplyPowerPlanAsync(
        RazerBleDeviceService service,
        MouseProfileSettings settings,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (!TryParsePowerPlan(settings.PowerPlan, out var plan))
        {
            if (string.IsNullOrWhiteSpace(settings.PowerPlan))
            {
                return Task.CompletedTask;
            }

            results.Add(new ProfileSettingApplyResult(
                "ble.power-plan", ProfileSettingApplyStatus.Failed, "Unknown power plan."));
            return Task.CompletedTask;
        }

        return ApplySettingAsync(
            results, "ble.power-plan",
            token => service.ReadPowerPlanAsync(cancellationToken: token),
            (value, token) => service.SetPowerPlanAsync(value, cancellationToken: token),
            plan, (left, right) => left == right, cancellationToken);
    }

    private static async Task ApplyHapticAsync(
        RazerBleDeviceService service,
        MouseProfileSettings settings,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (settings.HapticMode is { } hapticMode and >= 0 and <= 2)
        {
            await ApplySettingAsync(
                results, "ble.haptic-mode",
                token => service.ReadScrollTactileModeAsync(token),
                (value, token) => service.SetScrollTactileModeAsync(value, token),
                (RazerBleScrollTactileMode)hapticMode, (left, right) => left == right, cancellationToken);
        }

        if (settings.HapticAccelerationLevel is { } acceleration)
        {
            await ApplySettingAsync(
                results, "ble.haptic-acceleration",
                token => service.ReadScrollAccelerationLevelAsync(token),
                (value, token) => service.SetScrollAccelerationLevelAsync(value, token),
                (byte)acceleration, (left, right) => left == right, cancellationToken);
        }

        if (settings.HapticMaskTime is { } masking)
        {
            await ApplySettingAsync(
                results, "ble.haptic-mask-time",
                token => service.ReadScrollMaskingTimeAsync(token),
                (value, token) => service.SetScrollMaskingTimeAsync(value, token),
                (byte)masking, (left, right) => left == right, cancellationToken);
        }

        if (settings.HapticLevel is { } hapticLevel and >= 1 and <= 6)
        {
            await ApplySettingAsync(
                results, "ble.haptic-level",
                async token => (await service.ReadHapticStageAsync(cancellationToken: token)).Active,
                (value, token) => service.SetHapticStageAsync(value, cancellationToken: token),
                (byte)hapticLevel, (left, right) => left == right, cancellationToken);
        }
    }

    private static Task ApplyBatteryChemistryAsync(
        RazerBleDeviceService service,
        MouseProfileSettings settings,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (settings.BatteryChemistry is not { } chemistry || chemistry > 2)
        {
            return Task.CompletedTask;
        }

        return ApplySettingAsync(
            results, "ble.battery-chemistry",
            token => service.ReadBatteryChemistryAsync(token),
            (value, token) => service.SetBatteryChemistryAsync(value, token),
            (RazerBleBatteryChemistry)chemistry, (left, right) => left == right, cancellationToken);
    }

    private static async Task ApplySmartIlluminationAsync(
        RazerBleDeviceService service,
        MouseProfileSettings settings,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (settings.SmartIlluminationEnabled is not { } enabled)
        {
            return;
        }

        const string key = "ble.smart-illumination";
        try
        {
            var current = await service.ReadSmartIlluminationAsync(cancellationToken: cancellationToken);
            if ((current.Status != 0) == enabled)
            {
                results.Add(Applied(key));
                return;
            }

            var applied = await service.SetSmartIlluminationAsync(
                enabled ? (byte)1 : (byte)0,
                current.Mode,
                cancellationToken: cancellationToken);
            results.Add((applied.Status != 0) == enabled
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

    private static Task ApplySnapTapAsync(
        RazerBleDeviceService service,
        MouseProfileSettings settings,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (settings.SnapTapEnabled is not { } enabled)
        {
            return Task.CompletedTask;
        }

        return ApplySettingAsync(
            results, "ble.snap-tap",
            async token => (await service.ReadSnapTapAsync(cancellationToken: token)).Enabled,
            (value, token) => service.SetSnapTapAsync(value, cancellationToken: token),
            enabled, (left, right) => left == right, cancellationToken);
    }

    private static async Task ApplyDebounceAsync(
        RazerBleDeviceService service,
        MouseProfileSettings settings,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (settings.DebounceMilliseconds is not { } milliseconds)
        {
            return;
        }

        const string key = "ble.debounce";
        try
        {
            var current = await service.ReadDebounceAsync(cancellationToken);
            var desired = new RazerBleDebounceState(
                (byte)milliseconds,
                current.MakeMask,
                (byte)milliseconds,
                current.BreakMask);
            if (current == desired)
            {
                results.Add(Applied(key));
                return;
            }

            var applied = await service.SetDebounceAsync(desired, cancellationToken);
            results.Add(applied == desired
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

    private static async Task ApplyLightingAsync(
        RazerBleDeviceService service,
        LightingProfile? lighting,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (lighting is null || !TryBuildLighting(lighting, out var desired))
        {
            return;
        }

        const string key = "ble.lighting";
        try
        {
            var current = await service.ReadLightingAsync(cancellationToken);
            if (LightingEquals(current, desired))
            {
                results.Add(Applied(key));
                return;
            }

            await service.SetBrightnessAsync(desired.Brightness, cancellationToken);
            var applied = await service.SetEffectAsync(
                desired.Effect,
                desired.Flags,
                desired.Rate,
                desired.Colors,
                cancellationToken);
            results.Add(LightingEquals(applied, desired)
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

    private static Task ApplySettingAsync<T>(
        List<ProfileSettingApplyResult> results,
        string key,
        Func<CancellationToken, Task<T>> read,
        Func<T, CancellationToken, Task> write,
        T desired,
        Func<T, T, bool> equals,
        CancellationToken cancellationToken) =>
        ApplySettingCoreAsync(results, key, read, write, desired, equals, cancellationToken);

    private static async Task ApplySettingCoreAsync<T>(
        List<ProfileSettingApplyResult> results,
        string key,
        Func<CancellationToken, Task<T>> read,
        Func<T, CancellationToken, Task> write,
        T desired,
        Func<T, T, bool> equals,
        CancellationToken cancellationToken)
    {
        try
        {
            var readOk = false;
            T current = default!;
            try
            {
                current = await read(cancellationToken);
                readOk = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A device that refuses the read is still written; the service verifies the write.
            }

            if (readOk && equals(current, desired))
            {
                results.Add(Applied(key));
                return;
            }

            await write(desired, cancellationToken);
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

    private static bool DpiStagesEqual(RazerBleDpiStages left, RazerBleDpiStages right) =>
        left.ActiveStage == right.ActiveStage &&
        left.Stages.Count == right.Stages.Count &&
        left.Stages.Zip(right.Stages).All(pair =>
            pair.First.Index == pair.Second.Index &&
            pair.First.X == pair.Second.X &&
            pair.First.Y == pair.Second.Y);

    private static string BleKey(string id) =>
        id.StartsWith("ble:", StringComparison.OrdinalIgnoreCase) ? id : "ble:" + id;

    private static ProfileDeviceApplyResult Failed(string deviceId, string error) =>
        new(deviceId, ProfileDeviceApplyStatus.Failed,
            [new ProfileSettingApplyResult("device", ProfileSettingApplyStatus.Failed, error)]);

    private static ProfileSettingApplyResult Applied(string key) =>
        new(key, ProfileSettingApplyStatus.Applied);

    private static ProfileSettingApplyResult Mismatch(string key, string detail) =>
        new(key, ProfileSettingApplyStatus.Failed, detail);

    private static ProfileSettingApplyResult Failed(string key, Exception exception) =>
        new(key, ProfileSettingApplyStatus.Failed, exception.Message);
}
