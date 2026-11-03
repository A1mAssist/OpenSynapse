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
                SelectedPollingRate = rate;
                UpdateBasic(state => state with { PollingRate = rate });
                // The firmware reports the stored rate back. A mismatch means the write did
                // not land, and saying "applied" alone would leave the owner guessing.
                if (rate != requested)
                {
                    _operationStatusOverride = AppStrings.FormatText("RazerHidPollingRateMismatch", requested, rate);
                    OnPropertyChanged(nameof(OperationStatusText));
                }
            }, cancellationToken);
    }

    public Task ApplyDpiAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.DpiWrite,
            () => IsDpiBusy, value => IsDpiBusy = value,
            async () =>
            {
                var requestedX = DpiX;
                var requestedY = DpiY;
                var reported = await _service.SetDpiAsync(Connection, DpiX, DpiY, cancellationToken);
                if (reported is not { } dpi)
                {
                    // The device could not be read back, so the state keeps the values it
                    // had. Claiming the requested DPI would state something unknown.
                    return;
                }
                (DpiX, DpiY) = (dpi.X, dpi.Y);
                UpdateBasic(state => state with { DpiX = dpi.X, DpiY = dpi.Y });
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

    public Task ApplyDpiStagesAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.DpiStagesWrite,
            () => IsDpiStagesBusy, value => IsDpiStagesBusy = value,
            async () =>
            {
                var state = new RazerHidDpiStages(ActiveDpiStage,
                    DpiStages.Select(stage => new RazerHidDpiStage(stage.Number, stage.X, stage.Y, stage.Z)).ToArray());
                await _service.SetDpiStagesAsync(Connection, state, cancellationToken);
                if (Has(RazerHidBackendCapability.DpiStagesRead))
                    ReplaceDpiStages(await _service.GetDpiStagesAsync(Connection, cancellationToken));
            }, cancellationToken);

    public Task ApplyIdleTimeoutAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.IdleTimeoutWrite,
            () => IsIdleBusy, value => IsIdleBusy = value,
            async () =>
            {
                await _service.SetIdleTimeoutAsync(Connection, IdleTimeoutSeconds, cancellationToken);
                if (Has(RazerHidBackendCapability.IdleTimeoutRead))
                    IdleTimeoutSeconds = await _service.GetIdleTimeoutAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { IdleTimeoutSeconds = IdleTimeoutSeconds });
            }, cancellationToken);

    public Task ApplyLowBatteryThresholdAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.LowBatteryThresholdWrite,
            () => IsLowBatteryBusy, value => IsLowBatteryBusy = value,
            async () =>
            {
                await _service.SetLowBatteryThresholdAsync(Connection, LowBatteryThresholdPercent, cancellationToken);
                if (Has(RazerHidBackendCapability.LowBatteryThresholdRead))
                    LowBatteryThresholdPercent = await _service.GetLowBatteryThresholdAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { LowBatteryThresholdPercent = LowBatteryThresholdPercent });
            }, cancellationToken);

    public Task ApplyAutoSleepAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.AutoSleepWrite,
            () => IsAutoSleepBusy, value => IsAutoSleepBusy = value,
            async () =>
            {
                await _service.SetAutoSleepAsync(Connection, AutoSleepEnabled, cancellationToken);
                if (Has(RazerHidBackendCapability.AutoSleepRead))
                    AutoSleepEnabled = await _service.GetAutoSleepAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { AutoSleepEnabled = AutoSleepEnabled });
            }, cancellationToken);

    public Task ApplyChargeLimiterAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.BatteryChargeLimiterWrite,
            () => IsChargeLimiterBusy, value => IsChargeLimiterBusy = value,
            async () =>
            {
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
            }, cancellationToken);

}
