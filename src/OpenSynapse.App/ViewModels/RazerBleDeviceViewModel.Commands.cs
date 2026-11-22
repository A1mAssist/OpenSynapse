using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerBleDeviceViewModel
{
    private bool IsNaga => RazerNagaSideButtonResolver.IsNagaProduct(_device.ProductId, _device.Name);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        try
        {
            _service ??= await RazerBleDeviceService.OpenAsync(_device, _diagnostic, cancellationToken);
            Apply(await _service.ReadStateAsync(cancellationToken));
            OperationStatusText = string.Empty;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
        }
        finally
        {
            EndOperation();
        }

        await LoadLightingAsync(cancellationToken);
        await LoadProfilesAsync(cancellationToken);
        await LoadMappingAsync(cancellationToken);
        await LoadDpiStagesAsync(cancellationToken);
        await LoadScrollAsync(cancellationToken);
        await LoadPowerAsync(cancellationToken);
        await LoadExtrasAsync(cancellationToken);
    }

    [ProfileSetting("ble.polling-rate", DeviceCategory.Mouse)]
    public Task ApplyPollingRateAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetPollingRateAsync(PollingRates[SelectedPollingIndex], cancellationToken),
            applied => SelectedPollingIndex = Math.Max(0, PollingRates.ToList().IndexOf(applied)),
            (applied, token) => PersistMouseAsync(mouse => mouse.PollingRateHertz = applied, token),
            async token => (await _service!.ReadStateAsync(token)).PollingHertz
                ?? throw new InvalidOperationException("The device did not report a polling rate."),
            (previous, token) => _service!.SetPollingRateAsync(previous, token),
            cancellationToken);

    [ProfileSetting("ble.dpi", DeviceCategory.Mouse)]
    public Task ApplyDpiAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetDpiAsync((int)Math.Round(DpiX), (int)Math.Round(DpiY), cancellationToken),
            applied =>
            {
                DpiX = applied.X;
                DpiY = applied.Y;
            },
            (applied, token) => PersistMouseAsync(
                mouse =>
                {
                    mouse.DpiX = applied.X;
                    mouse.DpiY = applied.Y;
                },
                token),
            async token => (await _service!.ReadStateAsync(token)).Dpi is { } dpi
                ? (dpi.X, dpi.Y)
                : throw new InvalidOperationException("The device did not report DPI."),
            (previous, token) => _service!.SetDpiAsync(previous.X, previous.Y, token),
            cancellationToken);

    public async Task LoadLightingAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || !BeginOperation()) return;
        try
        {
            ApplyLighting(await _service.ReadLightingAsync(cancellationToken));
            LightingStatusText = string.Empty;
            LightingVisibility = Visibility.Visible;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            LightingVisibility = Visibility.Collapsed;
            LightingStatusText = exception.Message;
        }
        finally
        {
            EndOperation();
        }
    }

    [ProfileSetting("ble.lighting", DeviceCategory.Mouse)]
    public Task ApplyLightingAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            async service =>
            {
                var brightness = await service.SetBrightnessAsync(Brightness, cancellationToken);
                var (flags, rate, colors) = BuildLightingPayload(
                    SelectedLightingEffect, LightingColor, SelectedLightingDirectionIndex);
                var state = await service.SetEffectAsync(
                    SelectedLightingEffect, flags, rate, colors, cancellationToken);
                return state with { BrightnessPercent = brightness };
            },
            state =>
            {
                ApplyLighting(state);
            },
            async (state, token) =>
            {
                var saved = await PersistLightingAsync(BuildLightingProfile(), token);
                LightingStatusText = saved ? AppStrings.Text("BlackSharkSaved") : string.Empty;
                return saved;
            },
            token => _service!.ReadLightingAsync(token),
            RestoreLightingAsync,
            cancellationToken);

    public async Task LoadMappingAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || !BeginOperation()) return;
        try
        {
            if (_mapping is null)
            {
                _mapping = new RazerHidMappingViewModel(
                    new RazerBleMappingTransport(_service),
                    canWrite: true,
                    log: _diagnostic,
                    mappingProfileSaver: PersistMappingProfileAsync,
                    productId: RazerProductAliases.Canonical(_device.ProductId),
                    actionCatalog: IsNaga ? RazerNagaMappingActionCatalog.Instance : null,
                    softwareTriggerResolver: buttonId => RazerMouseMacroTriggerResolver.Resolve(
                        RazerProductAliases.Canonical(_device.ProductId), buttonId, IsNaga));
                OnPropertyChanged(nameof(Mapping));
            }

            await _mapping.ReadAsync(cancellationToken).ConfigureAwait(true);
            // A read failure means the device advertised mapping but did not answer this
            // time; keep the card visible so the error is actionable instead of hiding it.
            MappingVisibility = _mapping.Rows.Count > 0 || _mapping.HasError
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            MappingVisibility = Visibility.Collapsed;
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task PersistMappingProfileAsync(
        IReadOnlyList<ButtonMappingProfile> mappings,
        CancellationToken cancellationToken)
    {
        if (mappings.Count == 0)
        {
            return;
        }

        var mode = mappings[0].Mode ?? 0;
        var saved = await PersistMouseAsync(mouse =>
        {
            mouse.ButtonMappings = RazerHidMappingActions.MergeButtonMappings(
                mouse.ButtonMappings, mappings, mode);
        }, cancellationToken).ConfigureAwait(true);
        if (!saved)
        {
            throw new IOException("BLE mapping could not be saved to the profile.");
        }
    }

    [NotProfileScoped]
    public async Task ApplyMappingAsync(CancellationToken cancellationToken = default)
    {
        if (_mapping is null) return;
        await _mapping.ApplyAsync(cancellationToken).ConfigureAwait(true);
        if (_mapping.HasError)
        {
            ErrorText = _mapping.ErrorText;
        }
        else
        {
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
    }

    public async Task LoadDpiStagesAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || !BeginOperation()) return;
        try
        {
            ApplyDpiStages(await _service.ReadDpiStagesAsync(cancellationToken));
            DpiStagesStatusText = string.Empty;
            DpiStagesVisibility = Visibility.Visible;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            DpiStages.Clear();
            ActiveDpiStage = 1;
            DpiStagesVisibility = Visibility.Collapsed;
            DpiStagesStatusText = exception.Message;
        }
        finally
        {
            EndOperation();
        }
    }

    [ProfileSetting("ble.dpi-stages", DeviceCategory.Mouse)]
    public Task ApplyDpiStagesAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            async service =>
            {
                var state = new RazerBleDpiStages(
                    (byte)ActiveDpiStage,
                    DpiStages.Select(stage => new RazerBleDpiStage(
                        stage.Index,
                        (int)Math.Round(stage.X),
                        (int)Math.Round(stage.Y),
                        stage.Z)).ToArray());
                return await service.SetDpiStagesAsync(state, cancellationToken);
            },
            state =>
            {
                ApplyDpiStages(state);
            },
            async (state, token) =>
            {
                var saved = await PersistMouseAsync(
                    mouse => ApplyDpiStagesToProfile(mouse, state), token);
                DpiStagesStatusText = saved ? AppStrings.Text("BlackSharkSaved") : string.Empty;
                return saved;
            },
            token => _service!.ReadDpiStagesAsync(token),
            (previous, token) => _service!.SetDpiStagesAsync(previous, token),
            cancellationToken);

    public async Task LoadScrollAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || !BeginOperation()) return;
        try
        {
            ApplyScroll(await _service.ReadScrollAsync(cancellationToken));
            ScrollStatusText = string.Empty;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ScrollVisibility = Visibility.Collapsed;
            ScrollStatusText = exception.Message;
        }
        finally
        {
            EndOperation();
        }
    }

    private bool _scrollApplyFailed;
    [ProfileSetting("ble.scroll", DeviceCategory.Mouse)]
    public async Task ApplyScrollAsync(CancellationToken cancellationToken = default) =>
        _scrollApplyFailed = !await ApplyScrollCoreAsync(cancellationToken);

    private Task<bool> ApplyScrollCoreAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetScrollAsync(
                ScrollModeVisibility == Visibility.Visible ? (byte)SelectedScrollModeIndex : null,
                ScrollSmartReelVisibility == Visibility.Visible ? ScrollSmartReelEnabled : null,
                ScrollSmartReelThresholdVisibility == Visibility.Visible
                    ? (byte)Math.Round(ScrollSmartReelThreshold)
                    : null,
                ScrollAccelerationVisibility == Visibility.Visible
                    ? ScrollAccelerationEnabled
                    : null,
                cancellationToken),
            state =>
            {
                ApplyScroll(state);
            },
            async (state, token) =>
            {
                var saved = await PersistMouseAsync(
                    mouse => ApplyScrollToProfile(mouse, state), token);
                ScrollStatusText = saved ? AppStrings.Text("BlackSharkSaved") : string.Empty;
                return saved;
            },
            token => _service!.ReadScrollAsync(token),
            (previous, token) => _service!.SetScrollAsync(
                previous.Mode,
                previous.SmartReel,
                previous.SmartReelThreshold,
                previous.Acceleration,
                token),
            cancellationToken);

    public async Task LoadPowerAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || !BeginOperation()) return;
        try
        {
            ApplyPower(await _service.ReadPowerAsync(cancellationToken));
            PowerStatusText = string.Empty;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            PowerVisibility = Visibility.Collapsed;
            PowerStatusText = exception.Message;
        }
        finally
        {
            EndOperation();
        }
    }

    [ProfileSetting("ble.power", DeviceCategory.Mouse)]
    public Task ApplyPowerAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetPowerAsync(
                PowerLowBatteryVisibility == Visibility.Visible
                    ? (int)Math.Round(LowBatteryThresholdPercent)
                    : null,
                PowerAutoSleepVisibility == Visibility.Visible ? AutoSleepEnabled : null,
                PowerSleepTimeVisibility == Visibility.Visible
                    ? (int)Math.Round(SleepMinutes)
                    : null,
                PowerTimeToDimVisibility == Visibility.Visible
                    ? new RazerBleTimeToDimState(
                        TimeToDimEnabled,
                        (int)Math.Round(TimeToDimMinutes * 60d),
                        _timeToDimPercentage)
                    : null,
                cancellationToken),
            state =>
            {
                ApplyPower(state);
            },
            async (state, token) =>
            {
                var saved = await PersistMouseAsync(
                    mouse => ApplyPowerToProfile(mouse, state), token);
                PowerStatusText = saved ? AppStrings.Text("BlackSharkSaved") : string.Empty;
                return saved;
            },
            token => _service!.ReadPowerAsync(token),
            (previous, token) => _service!.SetPowerAsync(
                previous.LowBatteryPercent,
                previous.AutoSleep,
                previous.SleepMinutes,
                previous.TimeToDim,
                token),
            cancellationToken);

    public async Task LoadProfilesAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || !BeginOperation()) return;
        try
        {
            var state = await _service.ReadProfilesAsync(cancellationToken);
            _service.MappingProfileId = state.ActiveId ?? 1;
            _profileIds = state.Ids;
            SelectedProfileIndex = state.ActiveId is { } active
                ? Math.Max(0, state.Ids.ToList().IndexOf(active))
                : 0;
            ProfilesVisibility = state.Ids.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ProfileStatusText = state.ActiveId is { } id
                ? $"Active: Profile {id}"
                : string.Empty;
            OnPropertyChanged(nameof(ProfileOptions));
            OnPropertyChanged(nameof(SelectedProfileIndex));
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _profileIds = [];
            ProfilesVisibility = Visibility.Collapsed;
            ProfileStatusText = exception.Message;
            OnPropertyChanged(nameof(ProfileOptions));
            OnPropertyChanged(nameof(SelectedProfileIndex));
        }
        finally
        {
            EndOperation();
        }
    }

    [ProfileSetting("ble.onboard-profile", DeviceCategory.Mouse)]
    public async Task ApplyProfileAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || _profileIds.Count == 0 || !BeginOperation()) return;
        var profileId = _profileIds[Math.Clamp(SelectedProfileIndex, 0, _profileIds.Count - 1)];
        RazerBleProfileState? previous = null;
        try
        {
            previous = await _service.ReadProfilesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            EndOperation();
            return;
        }
        catch
        {
            // The active profile is still writable; only the rollback target is lost.
        }

        var applied = false;
        try
        {
            await _service.SetActiveProfileAsync(profileId, cancellationToken);
            _service.MappingProfileId = profileId;
            ProfileStatusText = AppStrings.Text("BlackSharkSaved");
            if (!await PersistMouseAsync(mouse => mouse.ActiveOnboardProfileId = profileId, cancellationToken))
            {
                await RestorePreviousProfileAsync();
                ProfileStatusText = string.Empty;
                ErrorText = AppStrings.Text("ViperSettingAppliedProfileSaveFailed");
                return;
            }

            applied = true;
        }
        catch (OperationCanceledException)
        {
            await RestorePreviousProfileAsync();
            ProfileStatusText = string.Empty;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            await RestorePreviousProfileAsync();
            ProfileStatusText = string.Empty;
            ErrorText = exception.Message;
        }
        finally
        {
            EndOperation();
        }

        if (!applied) return;
        await LoadDpiStagesAsync(cancellationToken);
        await LoadScrollAsync(cancellationToken);
        await LoadPowerAsync(cancellationToken);
        await LoadLightingAsync(cancellationToken);
        await LoadMappingAsync(cancellationToken);

        async Task RestorePreviousProfileAsync()
        {
            if (previous?.ActiveId is not { } previousId) return;
            SelectedProfileIndex = Math.Max(0, _profileIds.ToList().IndexOf(previousId));
            try
            {
                await _service.SetActiveProfileAsync(previousId, CancellationToken.None);
                _service.MappingProfileId = previousId;
            }
            catch
            {
                // The save failure is the error the user must see.
            }
        }
    }

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or InvalidOperationException or ArgumentOutOfRangeException or
            NotSupportedException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or
            TimeoutException or OperationCanceledException;
}
