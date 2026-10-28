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
            await ReadOptionalAsync(OpenRazerBackendCapability.SmartReelThresholdRead,
                async () => SmartReelThreshold = await _service.GetSmartReelThresholdAsync(Connection, cancellationToken));
            await ReadOptionalAsync(OpenRazerBackendCapability.ScrollFreeNotchedModeRead,
                async () => ScrollFreeNotchedModeIndex =
                    (int)await _service.GetScrollFreeNotchedModeAsync(Connection, cancellationToken));
            await ReadOptionalAsync(OpenRazerBackendCapability.ScrollAccelerationLevelRead,
                async () => ScrollAccelerationLevel =
                    await _service.GetScrollVirtualAccelerationLevelAsync(Connection, cancellationToken));
        }
        finally
        {
            IsScrollLoading = false;
        }
    }

    public Task ApplyScrollFreeNotchedModeAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.ScrollFreeNotchedModeWrite,
            () => IsScrollFreeNotchedBusy, value => IsScrollFreeNotchedBusy = value,
            async () =>
            {
                await _service.SetScrollFreeNotchedModeAsync(Connection,
                    (RazerScrollFreeNotchedMode)ScrollFreeNotchedModeIndex, cancellationToken);
                if (Has(OpenRazerBackendCapability.ScrollFreeNotchedModeRead))
                    ScrollFreeNotchedModeIndex =
                        (int)await _service.GetScrollFreeNotchedModeAsync(Connection, cancellationToken);
            }, cancellationToken);

    public Task ApplyScrollAccelerationLevelAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.ScrollAccelerationLevelWrite,
            () => IsScrollAccelerationLevelBusy, value => IsScrollAccelerationLevelBusy = value,
            async () =>
            {
                await _service.SetScrollVirtualAccelerationLevelAsync(
                    Connection, ScrollAccelerationLevel, cancellationToken);
                if (Has(OpenRazerBackendCapability.ScrollAccelerationLevelRead))
                    ScrollAccelerationLevel =
                        await _service.GetScrollVirtualAccelerationLevelAsync(Connection, cancellationToken);
            }, cancellationToken);

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

    public Task ApplySmartReelThresholdAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.SmartReelThresholdWrite,
            () => IsSmartReelThresholdBusy, value => IsSmartReelThresholdBusy = value,
            async () =>
            {
                await _service.SetSmartReelThresholdAsync(Connection, SmartReelThreshold, cancellationToken);
                if (Has(OpenRazerBackendCapability.SmartReelThresholdRead))
                    SmartReelThreshold = await _service.GetSmartReelThresholdAsync(Connection, cancellationToken);
            }, cancellationToken);

    public async Task LoadOnboardProfilesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsReady || RequiresRescan || IsOnboardProfileBusy ||
            !CanUse(OpenRazerBackendCapability.OnboardProfileRead))
        {
            return;
        }
        IsOnboardProfileBusy = true;
        try
        {
            OnboardProfileCount =
                await _service.GetOnboardProfileCountAsync(Connection, cancellationToken);
            OnboardMaxProfiles =
                await _service.GetMaxOnboardProfilesAsync(Connection, cancellationToken);
            ActiveOnboardProfileId =
                await _service.GetActiveOnboardProfileIdAsync(Connection, cancellationToken);
            if (OnboardMaxProfiles > 0)
            {
                SelectedOnboardProfileId = ActiveOnboardProfileId;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            OnboardProfileStatusText = exception.Message;
        }
        finally
        {
            IsOnboardProfileBusy = false;
        }
    }

    public Task ApplyActiveOnboardProfileAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.OnboardProfileWrite,
            () => IsOnboardProfileBusy, value => IsOnboardProfileBusy = value,
            async () =>
            {
                await _service.SetActiveOnboardProfileIdAsync(
                    Connection, SelectedOnboardProfileId, cancellationToken);
                await RefreshOnboardProfilesAsync(cancellationToken);
            }, cancellationToken);

    public Task CreateOnboardProfileAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.OnboardProfileWrite,
            () => IsOnboardProfileBusy, value => IsOnboardProfileBusy = value,
            async () =>
            {
                await _service.CreateOnboardProfileAsync(
                    Connection, SelectedOnboardProfileId, cancellationToken);
                await RefreshOnboardProfilesAsync(cancellationToken);
            }, cancellationToken);

    public Task DeleteOnboardProfileAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.OnboardProfileWrite,
            () => IsOnboardProfileBusy, value => IsOnboardProfileBusy = value,
            async () =>
            {
                await _service.DeleteOnboardProfileAsync(
                    Connection, SelectedOnboardProfileId, cancellationToken);
                await RefreshOnboardProfilesAsync(cancellationToken);
            }, cancellationToken);

    public Task ResetOnboardProfileAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.OnboardProfileWrite,
            () => IsOnboardProfileBusy, value => IsOnboardProfileBusy = value,
            async () =>
            {
                await _service.ResetOnboardProfileAsync(
                    Connection, SelectedOnboardProfileId, cancellationToken);
                await RefreshOnboardProfilesAsync(cancellationToken);
            }, cancellationToken);

    private async Task RefreshOnboardProfilesAsync(CancellationToken cancellationToken)
    {
        OnboardProfileCount =
            await _service.GetOnboardProfileCountAsync(Connection, cancellationToken);
        ActiveOnboardProfileId =
            await _service.GetActiveOnboardProfileIdAsync(Connection, cancellationToken);
    }

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


    /// <summary>
    /// Sweeps the read-only command space and writes the accepted commands to a file, so an
    /// unlisted device can be mapped without a packet capture.
    /// </summary>
    public async Task<string> ExportCommandScanAsync(CancellationToken cancellationToken = default)
    {
        if (!IsReady)
        {
            throw new InvalidOperationException("The device feature endpoint is not resolved.");
        }

        IsScrollLoading = true;
        try
        {
            var scan = await _service.ScanCommandSpaceAsync(Connection, cancellationToken);
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "OpenSynapse");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(
                directory,
                $"hid-scan-{Connection.Definition.ProductId:X4}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await File.WriteAllTextAsync(path, scan, cancellationToken);
            return path;
        }
        finally
        {
            IsScrollLoading = false;
        }
    }
}
