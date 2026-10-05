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
    public async Task LoadScrollAsync(CancellationToken cancellationToken = default)
    {
        if (!IsReady || RequiresRescan || IsScrollLoading)
        {
            return;
        }
        IsScrollLoading = true;
        try
        {
            await ReadOptionalAsync(OpenRazerBackendCapability.ScrollModeRead,
                async () => ScrollMode = await _service.GetScrollModeAsync(Connection, cancellationToken));
            await ReadOptionalAsync(OpenRazerBackendCapability.ScrollAccelerationRead,
                async () => ScrollAccelerationEnabled = await _service.GetScrollAccelerationAsync(Connection, cancellationToken));
            await ReadOptionalAsync(OpenRazerBackendCapability.SmartReelRead,
                async () => SmartReelEnabled = await _service.GetSmartReelAsync(Connection, cancellationToken));
        }
        finally
        {
            IsScrollLoading = false;
        }
    }

    public Task ApplyScrollModeAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.ScrollModeWrite,
            () => IsScrollModeBusy, value => IsScrollModeBusy = value,
            async () =>
            {
                await _service.SetScrollModeAsync(Connection, ScrollMode, cancellationToken);
                if (Has(OpenRazerBackendCapability.ScrollModeRead))
                    ScrollMode = await _service.GetScrollModeAsync(Connection, cancellationToken);
            }, cancellationToken);

    public Task ApplyScrollAccelerationAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.ScrollAccelerationWrite,
            () => IsScrollAccelerationBusy, value => IsScrollAccelerationBusy = value,
            async () =>
            {
                await _service.SetScrollAccelerationAsync(Connection, ScrollAccelerationEnabled, cancellationToken);
                if (Has(OpenRazerBackendCapability.ScrollAccelerationRead))
                    ScrollAccelerationEnabled = await _service.GetScrollAccelerationAsync(Connection, cancellationToken);
            }, cancellationToken);

    public Task ApplySmartReelAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.SmartReelWrite,
            () => IsSmartReelBusy, value => IsSmartReelBusy = value,
            async () =>
            {
                await _service.SetSmartReelAsync(Connection, SmartReelEnabled, cancellationToken);
                if (Has(OpenRazerBackendCapability.SmartReelRead))
                    SmartReelEnabled = await _service.GetSmartReelAsync(Connection, cancellationToken);
            }, cancellationToken);

    public async Task LoadKeyswitchAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUse(OpenRazerBackendCapability.KeyswitchOptimizationRead) || IsKeyswitchLoading)
        {
            return;
        }
        IsKeyswitchLoading = true;
        try
        {
            KeyswitchOptimization = await _service.GetKeyswitchOptimizationAsync(Connection, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception, OpenRazerBackendCapability.KeyswitchOptimizationRead);
        }
        finally
        {
            IsKeyswitchLoading = false;
        }
    }

    public Task ApplyKeyswitchAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.KeyswitchOptimizationWrite,
            () => IsKeyswitchBusy, value => IsKeyswitchBusy = value,
            async () =>
            {
                await _service.SetKeyswitchOptimizationAsync(Connection, KeyswitchOptimization, cancellationToken);
                if (Has(OpenRazerBackendCapability.KeyswitchOptimizationRead))
                    KeyswitchOptimization = await _service.GetKeyswitchOptimizationAsync(Connection, cancellationToken);
            }, cancellationToken);

    public Task SetFnPrimaryAsync(bool fnFunctionsArePrimary, CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.FnPrimaryWrite,
            () => IsFnBusy, value => IsFnBusy = value,
            () => _service.SetFnPrimaryAsync(Connection, fnFunctionsArePrimary, cancellationToken),
            cancellationToken);

    public Task SetHyperPollingIndicatorAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.HyperPollingIndicatorWrite,
            () => IsHyperPollingBusy, value => IsHyperPollingBusy = value,
            () => _service.SetHyperPollingIndicatorAsync(Connection, HyperPollingIndicatorMode, cancellationToken),
            cancellationToken);

    public Task PairHyperPollingAsync(ushort mouseProductId, CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.HyperPollingPairWrite,
            () => IsHyperPollingBusy, value => IsHyperPollingBusy = value,
            () => _service.PairHyperPollingAsync(Connection, mouseProductId, cancellationToken),
            cancellationToken);

    public Task UnpairHyperPollingAsync(ushort mouseProductId, CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.HyperPollingUnpairWrite,
            () => IsHyperPollingBusy, value => IsHyperPollingBusy = value,
            () => _service.UnpairHyperPollingAsync(Connection, mouseProductId, cancellationToken),
            cancellationToken);

}
