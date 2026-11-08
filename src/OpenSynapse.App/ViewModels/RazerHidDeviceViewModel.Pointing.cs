using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel : INotifyPropertyChanged
{
    [ProfileSetting("input.polling-rate", DeviceCategory.Unknown, powerScoped: true)]
    public Task ApplyPollingAsync(CancellationToken cancellationToken = default)
    {
        _service.LogSkippedControl(Connection.Definition.ProductId,
            $"apply polling rate requested: selected={SelectedPollingRate}, canWrite={CanWritePolling}, canEdit={CanEditPolling}, busy={IsPollingBusy}, options=[{string.Join(",", PollingOptions)}]");
        return RunWriteAsync(RazerHidBackendCapability.PollingRateWrite,
            () => IsPollingBusy, value => IsPollingBusy = value,
            async () =>
            {
                if (!PollingOptions.Contains(SelectedPollingRate)) throw new ArgumentOutOfRangeException(nameof(SelectedPollingRate));
                var requested = SelectedPollingRate;
                var reported = await _service.SetPollingRateAsync(Connection, requested, cancellationToken);
                if (reported is not { } rate)
                {
                    // The device could not be read back, so the state keeps the value it
                    // had. Claiming the requested rate would state something unknown.
                    return;
                }
                var previousRate = _basicState?.PollingRate ?? requested;
                SelectedPollingRate = rate;
                UpdateBasic(state => state with { PollingRate = rate });
                var previousProfile = ReadProfile(settings => IsKeyboard
                    ? settings.Keyboard.PollingRateHertz
                    : settings.Viper.PollingRateHertz);
                await SaveProfileAsync(
                    settings =>
                    {
                        if (IsKeyboard) settings.Keyboard.PollingRateHertz = rate;
                        else settings.Viper.PollingRateHertz = rate;
                    },
                    settings =>
                    {
                        if (IsKeyboard) settings.Keyboard.PollingRateHertz = previousProfile;
                        else settings.Viper.PollingRateHertz = previousProfile;
                    },
                    () =>
                    {
                        SelectedPollingRate = previousRate;
                        UpdateBasic(state => state with { PollingRate = previousRate });
                    },
                    async token => _ = await _service.SetPollingRateAsync(Connection, previousRate, token),
                    cancellationToken);
                // The firmware reports the stored rate back. A mismatch means the write did
                // not land, and saying "applied" alone would leave the owner guessing.
                if (rate != requested)
                {
                    _operationStatusOverride = AppStrings.FormatText("RazerHidPollingRateMismatch", requested, rate);
                    OnPropertyChanged(nameof(OperationStatusText));
                }
            }, cancellationToken);
    }

    [ProfileSetting("mouse.dpi", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplyDpiAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.DpiWrite,
            () => IsDpiBusy, value => IsDpiBusy = value,
            async () =>
            {
                var requestedX = DpiX;
                var requestedY = DpiY;
                var previousX = _basicState?.DpiX ?? requestedX;
                var previousY = _basicState?.DpiY ?? requestedY;
                var previousProfile = ReadProfile(settings => (settings.Viper.DpiX, settings.Viper.DpiY));
                var reported = await _service.SetDpiAsync(Connection, DpiX, DpiY, cancellationToken);
                if (reported is not { } dpi)
                {
                    // The device could not be read back, so the state keeps the values it
                    // had. Claiming the requested DPI would state something unknown.
                    return;
                }
                (DpiX, DpiY) = (dpi.X, dpi.Y);
                UpdateBasic(state => state with { DpiX = dpi.X, DpiY = dpi.Y });
                await SaveProfileAsync(
                    settings =>
                    {
                        settings.Viper.DpiX = dpi.X;
                        settings.Viper.DpiY = dpi.Y;
                    },
                    settings =>
                    {
                        settings.Viper.DpiX = previousProfile.DpiX;
                        settings.Viper.DpiY = previousProfile.DpiY;
                    },
                    () =>
                    {
                        (DpiX, DpiY) = (previousX, previousY);
                        UpdateBasic(state => state with { DpiX = previousX, DpiY = previousY });
                    },
                    async token => _ = await _service.SetDpiAsync(Connection, previousX, previousY, token),
                    cancellationToken);
                if (dpi.X != requestedX || dpi.Y != requestedY)
                {
                    _operationStatusOverride = AppStrings.FormatText("RazerHidDpiMismatch", requestedX, requestedY, dpi.X, dpi.Y);
                    OnPropertyChanged(nameof(OperationStatusText));
                }
            }, cancellationToken);

    public async Task LoadDpiStagesAsync(CancellationToken cancellationToken = default)
    {
        if (!Has(RazerHidBackendCapability.DpiStagesRead) || RequiresRescan || IsDpiStagesLoading) return;
        IsDpiStagesLoading = true;
        try
        {
            ReplaceDpiStages(await _service.GetDpiStagesAsync(Connection, cancellationToken));
            ErrorText = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception, RazerHidBackendCapability.DpiStagesRead);
        }
        finally
        {
            IsDpiStagesLoading = false;
        }
    }

    [ProfileSetting("mouse.dpi-stages", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplyDpiStagesAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.DpiStagesWrite,
            () => IsDpiStagesBusy, value => IsDpiStagesBusy = value,
            async () =>
            {
                var previousState = new RazerHidDpiStages(ActiveDpiStage,
                    DpiStages.Select(stage => new RazerHidDpiStage(stage.Number, stage.X, stage.Y, stage.Z)).ToArray());
                var previousProfile = ReadProfile(settings => settings.Viper.DpiStages);
                var state = new RazerHidDpiStages(ActiveDpiStage,
                    DpiStages.Select(stage => new RazerHidDpiStage(stage.Number, stage.X, stage.Y, stage.Z)).ToArray());
                await _service.SetDpiStagesAsync(Connection, state, cancellationToken);
                if (Has(RazerHidBackendCapability.DpiStagesRead))
                    ReplaceDpiStages(await _service.GetDpiStagesAsync(Connection, cancellationToken));
                var applied = new ViperDpiStagesProfile
                {
                    ActiveStage = ActiveDpiStage,
                    Stages = DpiStages.Select(stage => new ViperDpiStageProfile
                    {
                        Number = stage.Number,
                        X = stage.X,
                        Y = stage.Y,
                    }).ToList(),
                };
                await SaveProfileAsync(
                    settings => settings.Viper.DpiStages = applied,
                    settings => settings.Viper.DpiStages = previousProfile,
                    () => ReplaceDpiStages(previousState),
                    token => _service.SetDpiStagesAsync(Connection, previousState, token),
                    cancellationToken);
            }, cancellationToken);

    [ProfileSetting("mouse.idle-timeout", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplyIdleTimeoutAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.IdleTimeoutWrite,
            () => IsIdleBusy, value => IsIdleBusy = value,
            async () =>
            {
                var previous = _basicState?.IdleTimeoutSeconds ?? IdleTimeoutSeconds;
                var previousProfile = ReadProfile(settings => settings.Viper.IdleSeconds);
                await _service.SetIdleTimeoutAsync(Connection, IdleTimeoutSeconds, cancellationToken);
                if (Has(RazerHidBackendCapability.IdleTimeoutRead))
                    IdleTimeoutSeconds = await _service.GetIdleTimeoutAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { IdleTimeoutSeconds = IdleTimeoutSeconds });
                await SaveProfileAsync(
                    settings => settings.Viper.IdleSeconds = IdleTimeoutSeconds,
                    settings => settings.Viper.IdleSeconds = previousProfile,
                    () =>
                    {
                        IdleTimeoutSeconds = previous;
                        UpdateBasic(state => state with { IdleTimeoutSeconds = previous });
                    },
                    token => _service.SetIdleTimeoutAsync(Connection, previous, token),
                    cancellationToken);
            }, cancellationToken);

    [ProfileSetting("mouse.low-battery", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplyLowBatteryThresholdAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.LowBatteryThresholdWrite,
            () => IsLowBatteryBusy, value => IsLowBatteryBusy = value,
            async () =>
            {
                var previous = _basicState?.LowBatteryThresholdPercent ?? LowBatteryThresholdPercent;
                var previousProfile = ReadProfile(settings => settings.Viper.LowBatteryThresholdPercent);
                await _service.SetLowBatteryThresholdAsync(Connection, LowBatteryThresholdPercent, cancellationToken);
                if (Has(RazerHidBackendCapability.LowBatteryThresholdRead))
                    LowBatteryThresholdPercent = await _service.GetLowBatteryThresholdAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { LowBatteryThresholdPercent = LowBatteryThresholdPercent });
                await SaveProfileAsync(
                    settings => settings.Viper.LowBatteryThresholdPercent = LowBatteryThresholdPercent,
                    settings => settings.Viper.LowBatteryThresholdPercent = previousProfile,
                    () =>
                    {
                        LowBatteryThresholdPercent = previous;
                        UpdateBasic(state => state with { LowBatteryThresholdPercent = previous });
                    },
                    token => _service.SetLowBatteryThresholdAsync(Connection, previous, token),
                    cancellationToken);
            }, cancellationToken);

    [ProfileSetting("mouse.auto-sleep", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplyAutoSleepAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.AutoSleepWrite,
            () => IsAutoSleepBusy, value => IsAutoSleepBusy = value,
            async () =>
            {
                var previous = _basicState?.AutoSleepEnabled ?? AutoSleepEnabled;
                var previousProfile = ReadProfile(settings => settings.Viper.AutoSleepSeconds);
                await _service.SetAutoSleepAsync(Connection, AutoSleepEnabled, cancellationToken);
                if (Has(RazerHidBackendCapability.AutoSleepRead))
                    AutoSleepEnabled = await _service.GetAutoSleepAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { AutoSleepEnabled = AutoSleepEnabled });
                await SaveProfileAsync(
                    settings => settings.Viper.AutoSleepSeconds = AutoSleepEnabled ? 1 : 0,
                    settings => settings.Viper.AutoSleepSeconds = previousProfile,
                    () =>
                    {
                        AutoSleepEnabled = previous;
                        UpdateBasic(state => state with { AutoSleepEnabled = previous });
                    },
                    token => _service.SetAutoSleepAsync(Connection, previous, token),
                    cancellationToken);
            }, cancellationToken);

    [ProfileSetting("mouse.charge-limit", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplyChargeLimiterAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.BatteryChargeLimiterWrite,
            () => IsChargeLimiterBusy, value => IsChargeLimiterBusy = value,
            async () =>
            {
                var previousEnabled = _basicState?.BatteryChargeLimiterEnabled ?? ChargeLimiterEnabled;
                var previousPercent = _basicState?.BatteryChargeLimiterPercent ?? ChargeLimiterPercent;
                var previousProfile = ReadProfile(settings => settings.Viper.ChargeLimitPercent);
                await _service.SetBatteryChargeLimiterAsync(
                    Connection, checked((byte)ChargeLimiterPercent), ChargeLimiterEnabled, cancellationToken);
                if (Has(RazerHidBackendCapability.BatteryChargeLimiterRead))
                {
                    var (level, enabled) = await _service.GetBatteryChargeLimiterAsync(Connection, cancellationToken);
                    ChargeLimiterPercent = level;
                    ChargeLimiterEnabled = enabled;
                }
                UpdateBasic(state => state with
                {
                    BatteryChargeLimiterEnabled = ChargeLimiterEnabled,
                    BatteryChargeLimiterPercent = ChargeLimiterPercent,
                });
                await SaveProfileAsync(
                    settings => settings.Viper.ChargeLimitPercent =
                        ChargeLimiterEnabled ? ChargeLimiterPercent : 0,
                    settings => settings.Viper.ChargeLimitPercent = previousProfile,
                    () =>
                    {
                        ChargeLimiterEnabled = previousEnabled;
                        ChargeLimiterPercent = previousPercent;
                        UpdateBasic(state => state with
                        {
                            BatteryChargeLimiterEnabled = previousEnabled,
                            BatteryChargeLimiterPercent = previousPercent,
                        });
                    },
                    token => _service.SetBatteryChargeLimiterAsync(
                        Connection,
                        checked((byte)previousPercent),
                        previousEnabled,
                        token),
                    cancellationToken);
            }, cancellationToken);

}
