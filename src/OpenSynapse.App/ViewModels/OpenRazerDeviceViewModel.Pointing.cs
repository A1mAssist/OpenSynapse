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

public sealed partial class OpenRazerDeviceViewModel : INotifyPropertyChanged
{
    public Task ApplyPollingAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.PollingRateWrite,
            () => IsPollingBusy, value => IsPollingBusy = value,
            async () =>
            {
                if (!PollingOptions.Contains(SelectedPollingRate)) throw new ArgumentOutOfRangeException(nameof(SelectedPollingRate));
                await _service.SetPollingRateAsync(Connection, SelectedPollingRate, cancellationToken);
                if (Has(OpenRazerBackendCapability.PollingRateRead))
                    SelectedPollingRate = await _service.GetPollingRateAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { PollingRate = SelectedPollingRate });
            }, cancellationToken);

    public Task ApplyDpiAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.DpiWrite,
            () => IsDpiBusy, value => IsDpiBusy = value,
            async () =>
            {
                await _service.SetDpiAsync(Connection, DpiX, DpiY, cancellationToken);
                if (Has(OpenRazerBackendCapability.DpiRead))
                    (DpiX, DpiY) = await _service.GetDpiAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { DpiX = DpiX, DpiY = DpiY });
            }, cancellationToken);

    public async Task LoadDpiStagesAsync(CancellationToken cancellationToken = default)
    {
        if (!Has(OpenRazerBackendCapability.DpiStagesRead) || RequiresRescan || IsDpiStagesLoading) return;
        IsDpiStagesLoading = true;
        try
        {
            ReplaceDpiStages(await _service.GetDpiStagesAsync(Connection, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception, OpenRazerBackendCapability.DpiStagesRead);
        }
        finally
        {
            IsDpiStagesLoading = false;
        }
    }

    public Task ApplyDpiStagesAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.DpiStagesWrite,
            () => IsDpiStagesBusy, value => IsDpiStagesBusy = value,
            async () =>
            {
                var state = new OpenRazerDpiStages(ActiveDpiStage,
                    DpiStages.Select(stage => new OpenRazerDpiStage(stage.Number, stage.X, stage.Y)).ToArray());
                await _service.SetDpiStagesAsync(Connection, state, cancellationToken);
                if (Has(OpenRazerBackendCapability.DpiStagesRead))
                    ReplaceDpiStages(await _service.GetDpiStagesAsync(Connection, cancellationToken));
            }, cancellationToken);

    public Task ApplyIdleTimeoutAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.IdleTimeoutWrite,
            () => IsIdleBusy, value => IsIdleBusy = value,
            async () =>
            {
                await _service.SetIdleTimeoutAsync(Connection, IdleTimeoutSeconds, cancellationToken);
                if (Has(OpenRazerBackendCapability.IdleTimeoutRead))
                    IdleTimeoutSeconds = await _service.GetIdleTimeoutAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { IdleTimeoutSeconds = IdleTimeoutSeconds });
            }, cancellationToken);

    public Task ApplyLowBatteryThresholdAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.LowBatteryThresholdWrite,
            () => IsLowBatteryBusy, value => IsLowBatteryBusy = value,
            async () =>
            {
                await _service.SetLowBatteryThresholdAsync(Connection, LowBatteryThresholdPercent, cancellationToken);
                if (Has(OpenRazerBackendCapability.LowBatteryThresholdRead))
                    LowBatteryThresholdPercent = await _service.GetLowBatteryThresholdAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { LowBatteryThresholdPercent = LowBatteryThresholdPercent });
            }, cancellationToken);

}
