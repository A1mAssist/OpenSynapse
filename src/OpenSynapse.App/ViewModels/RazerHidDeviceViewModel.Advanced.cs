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
    /// <summary>
    /// The class 0x03 profile dialect (DeathAdder V4 Pro) has no reset command, so the
    /// button would always fail with a device rejection.
    /// </summary>
    public Visibility OnboardProfileResetVisibility =>
        VisibleWhen(Has(RazerHidBackendCapability.OnboardProfileWrite) &&
            RazerHidDeviceService.SupportsOnboardProfileReset(Connection.Definition.ProductId));
    public Visibility OnboardProfileVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.OnboardProfileRead, RazerHidBackendCapability.OnboardProfileWrite));
    public Visibility OnboardProfileWriteVisibility =>
        VisibleWhen(Has(RazerHidBackendCapability.OnboardProfileWrite));
    public byte OnboardProfileCount
    {
        get => _onboardProfileCount;
        set => SetOnboardProfileField(ref _onboardProfileCount, value, nameof(OnboardProfileCount),
            nameof(CanCreateOnboardProfile), nameof(CanDeleteOnboardProfile));
    }
    public byte OnboardMaxProfiles
    {
        get => _onboardMaxProfiles;
        set => SetOnboardProfileField(ref _onboardMaxProfiles, value, nameof(OnboardMaxProfiles),
            nameof(OnboardProfileMaximumId), nameof(CanCreateOnboardProfile),
            nameof(CanDeleteOnboardProfile));
    }
    public byte ActiveOnboardProfileId
    {
        get => _activeOnboardProfileId;
        set => SetOnboardProfileField(ref _activeOnboardProfileId, value, nameof(ActiveOnboardProfileId),
            nameof(CanApplyOnboardProfile));
    }
    public byte SelectedOnboardProfileId
    {
        get => _selectedOnboardProfileId;
        set => SetOnboardProfileField(ref _selectedOnboardProfileId, value, nameof(SelectedOnboardProfileId),
            nameof(CanApplyOnboardProfile));
    }
    public string OnboardProfileStatusText
    {
        get => _onboardProfileStatusText;
        set => SetField(ref _onboardProfileStatusText, value);
    }
    /// <summary>
    /// The highest profile id the device accepts. A device that reports a maximum of
    /// one profile only knows id 0, so the picker must not offer id 1.
    /// </summary>
    public int OnboardProfileMaximumId => Math.Max(0, OnboardMaxProfiles - 1);
    public bool CanApplyOnboardProfile => CanEditOnboardProfile &&
        SelectedOnboardProfileId != ActiveOnboardProfileId;
    public bool CanCreateOnboardProfile => CanEditOnboardProfile &&
        OnboardProfileCount < OnboardMaxProfiles;
    public bool CanDeleteOnboardProfile => CanEditOnboardProfile &&
        OnboardProfileCount > 1;
    public bool CanResetOnboardProfile => CanEditOnboardProfile &&
        RazerHidDeviceService.SupportsOnboardProfileReset(Connection.Definition.ProductId);

    /// <summary>
    /// The onboard profile card enables its buttons from the profile counters, so a
    /// changed counter has to refresh the dependent commands.
    /// </summary>
    private void SetOnboardProfileField<T>(
        ref T field,
        T value,
        string propertyName,
        params string[] dependentProperties)
    {
        if (!SetField(ref field, value, propertyName))
        {
            return;
        }

        foreach (var property in dependentProperties)
        {
            OnPropertyChanged(property);
        }
    }

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

    public async Task LoadOnboardProfilesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsReady || RequiresRescan || IsOnboardProfileBusy ||
            !CanUse(RazerHidBackendCapability.OnboardProfileRead))
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
            OnboardProfileStatusText = string.Empty;
            if (OnboardMaxProfiles > 0)
            {
                SelectedOnboardProfileId = ActiveOnboardProfileId;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (RazerCommandRejectedException)
        {
            OnboardProfileStatusText = AppStrings.Text("RazerHidOnboardProfileRejected");
        }
        catch (NotSupportedException)
        {
            OnboardProfileStatusText = AppStrings.Text("RazerHidOnboardProfileUnsupported");
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

    [ProfileSetting("mouse.onboard-profile", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplyActiveOnboardProfileAsync(CancellationToken cancellationToken = default) =>
        RunOnboardProfileWriteAsync(
            () => IsOnboardProfileBusy, value => IsOnboardProfileBusy = value,
            async () =>
            {
                var previous = ActiveOnboardProfileId;
                var previousProfile = ReadProfile(settings => settings.Viper.ActiveOnboardProfileId);
                await _service.SetActiveOnboardProfileIdAsync(
                    Connection, SelectedOnboardProfileId, cancellationToken);
                await RefreshOnboardProfilesAsync(cancellationToken);
                await SaveProfileAsync(
                    settings => settings.Viper.ActiveOnboardProfileId = ActiveOnboardProfileId,
                    settings => settings.Viper.ActiveOnboardProfileId = previousProfile,
                    () =>
                    {
                        SelectedOnboardProfileId = previous;
                        ActiveOnboardProfileId = previous;
                    },
                    token => _service.SetActiveOnboardProfileIdAsync(Connection, previous, token),
                    cancellationToken);
            }, cancellationToken);

    public Task CreateOnboardProfileAsync(CancellationToken cancellationToken = default) =>
        RunOnboardProfileWriteAsync(
            () => IsOnboardProfileBusy, value => IsOnboardProfileBusy = value,
            async () =>
            {
                await _service.CreateOnboardProfileAsync(
                    Connection, SelectedOnboardProfileId, cancellationToken);
                await RefreshOnboardProfilesAsync(cancellationToken);
            }, cancellationToken);

    public Task DeleteOnboardProfileAsync(CancellationToken cancellationToken = default) =>
        RunOnboardProfileWriteAsync(
            () => IsOnboardProfileBusy, value => IsOnboardProfileBusy = value,
            async () =>
            {
                await _service.DeleteOnboardProfileAsync(
                    Connection, SelectedOnboardProfileId, cancellationToken);
                await RefreshOnboardProfilesAsync(cancellationToken);
            }, cancellationToken);

    public Task ResetOnboardProfileAsync(CancellationToken cancellationToken = default) =>
        RunOnboardProfileWriteAsync(
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

    /// <summary>
    /// A profile command the device refuses is a user-facing answer, not a transport
    /// failure: the card keeps working and the reason is shown next to the buttons.
    /// </summary>
    private async Task RunOnboardProfileWriteAsync(
        Func<bool> isBusy,
        Action<bool> setBusy,
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        if (!CanUse(RazerHidBackendCapability.OnboardProfileWrite) || isBusy())
        {
            return;
        }

        setBusy(true);
        try
        {
            await operation();
            OnboardProfileStatusText = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (RazerCommandRejectedException)
        {
            OnboardProfileStatusText = AppStrings.Text("RazerHidOnboardProfileRejected");
        }
        catch (NotSupportedException)
        {
            OnboardProfileStatusText = AppStrings.Text("RazerHidOnboardProfileUnsupported");
        }
        catch (Exception exception)
        {
            OnboardProfileStatusText = exception.Message;
        }
        finally
        {
            setBusy(false);
        }
    }

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

    [ProfileSetting("keyboard.fn-primary", DeviceCategory.Keyboard)]
    public Task SetFnPrimaryAsync(bool fnFunctionsArePrimary, CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.FnPrimaryWrite,
            () => IsFnBusy, value => IsFnBusy = value,
            async () =>
            {
                var previous = ReadProfile(settings => settings.Keyboard.FnKeyPrimary);
                await _service.SetFnPrimaryAsync(Connection, fnFunctionsArePrimary, cancellationToken);
                await SaveProfileAsync(
                    settings => settings.Keyboard.FnKeyPrimary = fnFunctionsArePrimary,
                    settings => settings.Keyboard.FnKeyPrimary = previous,
                    null,
                    token => _service.SetFnPrimaryAsync(Connection, previous ?? !fnFunctionsArePrimary, token),
                    cancellationToken);
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
