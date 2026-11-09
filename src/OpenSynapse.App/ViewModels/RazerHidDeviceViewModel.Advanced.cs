using System.ComponentModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// The class 0x03 profile dialect (DeathAdder V4 Pro) has no reset command, so the
    /// button would always fail with a device rejection.
    /// </summary>
    public async Task LoadScrollAsync(CancellationToken cancellationToken = default)
    {
        if (!IsReady || RequiresRescan || IsScrollLoading)
        {
            return;
        }
        IsScrollLoading = true;
        try
        {
            await ReadOptionalAsync(RazerHidBackendCapability.ScrollModeRead,
                async () => ScrollMode = await _service.GetScrollModeAsync(Connection, cancellationToken));
            await ReadOptionalAsync(RazerHidBackendCapability.ScrollAccelerationRead,
                async () => ScrollAccelerationEnabled = await _service.GetScrollAccelerationAsync(Connection, cancellationToken));
            await ReadOptionalAsync(RazerHidBackendCapability.SmartReelRead,
                async () => SmartReelEnabled = await _service.GetSmartReelAsync(Connection, cancellationToken));
            await ReadOptionalAsync(RazerHidBackendCapability.SmartReelThresholdRead,
                async () => SmartReelThreshold = await _service.GetSmartReelThresholdAsync(Connection, cancellationToken));
            await ReadOptionalAsync(RazerHidBackendCapability.ScrollFreeNotchedModeRead,
                async () => ScrollFreeNotchedModeIndex =
                    (int)await _service.GetScrollFreeNotchedModeAsync(Connection, cancellationToken));
            await ReadOptionalAsync(RazerHidBackendCapability.ScrollAccelerationLevelRead,
                async () => ScrollAccelerationLevel =
                    await _service.GetScrollVirtualAccelerationLevelAsync(Connection, cancellationToken));
            RefreshScrollWheelModeSelection();
        }
        finally
        {
            IsScrollLoading = false;
        }
    }

    [ProfileSetting("mouse.scroll-free-notched", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplyScrollFreeNotchedModeAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.ScrollFreeNotchedModeWrite,
            () => IsScrollFreeNotchedBusy, value => IsScrollFreeNotchedBusy = value,
            async () =>
            {
                var previous = ScrollFreeNotchedModeIndex;
                var previousProfile = ReadProfile(settings => settings.Viper.ScrollFreeNotched);
                await _service.SetScrollFreeNotchedModeAsync(Connection,
                    (RazerScrollFreeNotchedMode)ScrollFreeNotchedModeIndex, cancellationToken);
                if (Has(RazerHidBackendCapability.ScrollFreeNotchedModeRead))
                    ScrollFreeNotchedModeIndex =
                        (int)await _service.GetScrollFreeNotchedModeAsync(Connection, cancellationToken);
                await SaveProfileAsync(
                    settings => settings.Viper.ScrollFreeNotched =
                        (RazerScrollFreeNotchedMode)ScrollFreeNotchedModeIndex ==
                        RazerScrollFreeNotchedMode.FreeSpinMode,
                    settings => settings.Viper.ScrollFreeNotched = previousProfile,
                    () => ScrollFreeNotchedModeIndex = previous,
                    token => _service.SetScrollFreeNotchedModeAsync(
                        Connection, (RazerScrollFreeNotchedMode)previous, token),
                    cancellationToken);
            }, cancellationToken);

    [ProfileSetting("mouse.scroll-acceleration-level", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplyScrollAccelerationLevelAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.ScrollAccelerationLevelWrite,
            () => IsScrollAccelerationLevelBusy, value => IsScrollAccelerationLevelBusy = value,
            async () =>
            {
                var previous = ScrollAccelerationLevel;
                var previousProfile = ReadProfile(settings => settings.Viper.ScrollAccelerationLevel);
                await _service.SetScrollVirtualAccelerationLevelAsync(
                    Connection, ScrollAccelerationLevel, cancellationToken);
                if (Has(RazerHidBackendCapability.ScrollAccelerationLevelRead))
                    ScrollAccelerationLevel =
                        await _service.GetScrollVirtualAccelerationLevelAsync(Connection, cancellationToken);
                await SaveProfileAsync(
                    settings => settings.Viper.ScrollAccelerationLevel = ScrollAccelerationLevel,
                    settings => settings.Viper.ScrollAccelerationLevel = previousProfile,
                    () => ScrollAccelerationLevel = previous,
                    token => _service.SetScrollVirtualAccelerationLevelAsync(Connection, previous, token),
                    cancellationToken);
            }, cancellationToken);

    [ProfileSetting("mouse.scroll-mode", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplyScrollModeAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.ScrollModeWrite,
            () => IsScrollModeBusy, value => IsScrollModeBusy = value,
            async () =>
            {
                var previous = ScrollMode;
                var previousProfile = ReadProfile(settings => settings.Viper.ScrollMode);
                await _service.SetScrollModeAsync(Connection, ScrollMode, cancellationToken);
                if (Has(RazerHidBackendCapability.ScrollModeRead))
                    ScrollMode = await _service.GetScrollModeAsync(Connection, cancellationToken);
                await SaveProfileAsync(
                    settings => settings.Viper.ScrollMode = ToProfileScrollMode(ScrollMode),
                    settings => settings.Viper.ScrollMode = previousProfile,
                    () => ScrollMode = previous,
                    token => _service.SetScrollModeAsync(Connection, previous, token),
                    cancellationToken);
            }, cancellationToken);

    [ProfileSetting("mouse.scroll-acceleration", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplyScrollAccelerationAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.ScrollAccelerationWrite,
            () => IsScrollAccelerationBusy, value => IsScrollAccelerationBusy = value,
            async () =>
            {
                var previous = ScrollAccelerationEnabled;
                var previousProfile = ReadProfile(settings => settings.Viper.ScrollAccelerationEnabled);
                await _service.SetScrollAccelerationAsync(Connection, ScrollAccelerationEnabled, cancellationToken);
                if (Has(RazerHidBackendCapability.ScrollAccelerationRead))
                    ScrollAccelerationEnabled = await _service.GetScrollAccelerationAsync(Connection, cancellationToken);
                await SaveProfileAsync(
                    settings => settings.Viper.ScrollAccelerationEnabled = ScrollAccelerationEnabled,
                    settings => settings.Viper.ScrollAccelerationEnabled = previousProfile,
                    () => ScrollAccelerationEnabled = previous,
                    token => _service.SetScrollAccelerationAsync(Connection, previous, token),
                    cancellationToken);
            }, cancellationToken);

    [ProfileSetting("mouse.smart-reel", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplySmartReelAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.SmartReelWrite,
            () => IsSmartReelBusy, value => IsSmartReelBusy = value,
            async () =>
            {
                var previous = SmartReelEnabled;
                var previousProfile = ReadProfile(settings => settings.Viper.SmartReelEnabled);
                await _service.SetSmartReelAsync(Connection, SmartReelEnabled, cancellationToken);
                if (Has(RazerHidBackendCapability.SmartReelRead))
                    SmartReelEnabled = await _service.GetSmartReelAsync(Connection, cancellationToken);
                await SaveProfileAsync(
                    settings => settings.Viper.SmartReelEnabled = SmartReelEnabled,
                    settings => settings.Viper.SmartReelEnabled = previousProfile,
                    () => SmartReelEnabled = previous,
                    token => _service.SetSmartReelAsync(Connection, previous, token),
                    cancellationToken);
            }, cancellationToken);

    [ProfileSetting("mouse.smart-reel-threshold", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplySmartReelThresholdAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.SmartReelThresholdWrite,
            () => IsSmartReelThresholdBusy, value => IsSmartReelThresholdBusy = value,
            async () =>
            {
                var previous = SmartReelThreshold;
                var previousProfile = ReadProfile(settings => settings.Viper.SmartReelThreshold);
                await _service.SetSmartReelThresholdAsync(Connection, SmartReelThreshold, cancellationToken);
                if (Has(RazerHidBackendCapability.SmartReelThresholdRead))
                    SmartReelThreshold = await _service.GetSmartReelThresholdAsync(Connection, cancellationToken);
                await SaveProfileAsync(
                    settings => settings.Viper.SmartReelThreshold = SmartReelThreshold,
                    settings => settings.Viper.SmartReelThreshold = previousProfile,
                    () => SmartReelThreshold = previous,
                    token => _service.SetSmartReelThresholdAsync(Connection, previous, token),
                    cancellationToken);
            }, cancellationToken);

    public async Task LoadKeyswitchAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUse(RazerHidBackendCapability.KeyswitchOptimizationRead) || IsKeyswitchLoading)
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
            HandleFailure(exception, RazerHidBackendCapability.KeyswitchOptimizationRead);
        }
        finally
        {
            IsKeyswitchLoading = false;
        }
    }

    [ProfileSetting("keyboard.keyswitch", DeviceCategory.Keyboard)]
    public Task ApplyKeyswitchAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.KeyswitchOptimizationWrite,
            () => IsKeyswitchBusy, value => IsKeyswitchBusy = value,
            async () =>
            {
                var previous = KeyswitchOptimization;
                var previousProfile = ReadProfile(settings => settings.Keyboard.SwitchType);
                await _service.SetKeyswitchOptimizationAsync(Connection, KeyswitchOptimization, cancellationToken);
                if (Has(RazerHidBackendCapability.KeyswitchOptimizationRead))
                    KeyswitchOptimization = await _service.GetKeyswitchOptimizationAsync(Connection, cancellationToken);
                await SaveProfileAsync(
                    settings => settings.Keyboard.SwitchType = KeyswitchOptimization.ToString(),
                    settings => settings.Keyboard.SwitchType = previousProfile,
                    () => KeyswitchOptimization = previous,
                    token => _service.SetKeyswitchOptimizationAsync(Connection, previous, token),
                    cancellationToken);
            }, cancellationToken);

    [NotProfileScoped]
    public Task SetFnPrimaryAsync(bool fnFunctionsArePrimary, CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.FnPrimaryWrite,
            () => IsFnBusy, value => IsFnBusy = value,
            async () =>
            {
                await _service.SetFnPrimaryAsync(Connection, fnFunctionsArePrimary, cancellationToken);
                var settings = _getMachineSettings?.Invoke();
                if (settings is null || _saveMachineSettingsAsync is null)
                {
                    return;
                }

                var previous = settings.FnKeyPrimary;
                settings.FnKeyPrimary = fnFunctionsArePrimary;
                try
                {
                    if (!await _saveMachineSettingsAsync(cancellationToken))
                    {
                        throw new InvalidOperationException(
                            AppStrings.Text("RazerHidSettingProfileSaveFailed"));
                    }
                }
                catch
                {
                    settings.FnKeyPrimary = previous;
                    try
                    {
                        await _service.SetFnPrimaryAsync(
                            Connection,
                            previous ?? !fnFunctionsArePrimary,
                            CancellationToken.None);
                    }
                    catch
                    {
                        // The save failure is the error the user must see.
                    }
                    throw;
                }
            },
            cancellationToken);

    public Task SetHyperPollingIndicatorAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.HyperPollingIndicatorWrite,
            () => IsHyperPollingBusy, value => IsHyperPollingBusy = value,
            () => _service.SetHyperPollingIndicatorAsync(Connection, HyperPollingIndicatorMode, cancellationToken),
            cancellationToken);

    public Task PairHyperPollingAsync(ushort mouseProductId, CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.HyperPollingPairWrite,
            () => IsHyperPollingBusy, value => IsHyperPollingBusy = value,
            () => _service.PairHyperPollingAsync(Connection, mouseProductId, cancellationToken),
            cancellationToken);

    public Task UnpairHyperPollingAsync(ushort mouseProductId, CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.HyperPollingUnpairWrite,
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
