using OpenSynapse.Core.Audio;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Audio;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    [ProfileSetting("headset.thx.spatial", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxSpatialAsync(CancellationToken cancellationToken = default)
    {
        var desired = ThxSpatialEnabled;
        var previous = _thxState?.SpatialEnabled ?? !desired;
        if (!await RunThxAsync(
                (controller, token) => controller.SetSpatialAsync(
                    ThxAudioEndpoint.Headphones, desired, token),
                cancellationToken))
        {
            SetThxSpatialDraft(previous);
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => settings.ThxSpatialEnabled = ThxSpatialEnabled,
            settings => settings.ThxSpatialEnabled = previous,
            () => SetThxSpatialDraft(previous),
            async token => await ThxController.SetSpatialAsync(
                ThxAudioEndpoint.Headphones, previous, token),
            cancellationToken);
    }

    private void SetThxSpatialDraft(bool value)
    {
        if (_thxSpatialEnabled == value)
        {
            return;
        }

        _thxSpatialEnabled = value;
        OnPropertyChanged(nameof(ThxSpatialEnabled));
    }

    [ProfileSetting("headset.thx.preset", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxPresetAsync(CancellationToken cancellationToken = default)
    {
        if (_thxState?.Presets.ElementAtOrDefault(SelectedThxPresetIndex) is not { } preset)
        {
            return;
        }
        var previousIndex = SelectedThxPresetIndex;
        var previousPreset = _thxState.Presets.ElementAtOrDefault(previousIndex);
        var previousGains = ThxBands.Select(band => band.Level).ToArray();
        var gains = preset.Id == "default"
            ? Enumerable.Repeat(0d, ThxBands.Count).ToArray()
            : ThxBands.Select(band => band.Level).ToArray();
        if (!await RunThxAsync(
                (controller, token) => controller.SelectPresetAsync(
                    ThxAudioEndpoint.Headphones, preset.Id, gains, token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => settings.ThxPreset = preset.Id,
            settings => settings.ThxPreset = previousPreset?.Id,
            () => SelectedThxPresetIndex = previousIndex,
            async token =>
            {
                if (previousPreset is not null)
                {
                    await ThxController.SelectPresetAsync(
                        ThxAudioEndpoint.Headphones, previousPreset.Id, previousGains, token);
                }
            },
            cancellationToken);
    }

    [ProfileSetting("headset.thx.bands", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxBandsAsync(CancellationToken cancellationToken = default)
    {
        var previous = ThxBands.Select(band => band.Level).ToArray();
        var gains = ThxBands.Select(band => band.Level).ToArray();
        if (!await RunThxAsync(
                (controller, token) => controller.SetGainsAsync(
                    ThxAudioEndpoint.Headphones, gains, token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => settings.ThxBands = ToBandMap(ThxBands.Select(band => band.Level)),
            settings => settings.ThxBands = ToBandMap(previous),
            () =>
            {
                for (var index = 0; index < previous.Length && index < ThxBands.Count; index++)
                {
                    ThxBands[index].Level = previous[index];
                }
            },
            async token => await ThxController.SetGainsAsync(
                ThxAudioEndpoint.Headphones, previous, token),
            cancellationToken);
    }

    /// <summary>
    /// Synapse 3 ran these headsets with VirtualSurround off and its Game EQ on
    /// the headset firmware. Synapse 4 keeps spatial on for the Kitty and uses a
    /// different Game curve, so restore both halves of the old baseline.
    /// </summary>
    [NotProfileScoped]
    public async Task ApplyS3BaselineAsync(CancellationToken cancellationToken = default)
    {
        if (!SupportsS3Baseline(_connection.ProductId) || !CanApplyThx) return;

        SetThxSpatialDraft(false);
        await ApplyThxSpatialAsync(cancellationToken);
        if (!CanApplyThx || ThxSpatialEnabled) return;

        var customPresetId = _thxState?.Presets
            .FirstOrDefault(preset =>
                preset.Id.Equals("Custom", StringComparison.OrdinalIgnoreCase) ||
                preset.DisplayName.Contains("Custom", StringComparison.OrdinalIgnoreCase))
            ?.Id ?? "Custom";
        await RunThxAsync(
            (controller, token) => controller.SelectPresetAsync(
                ThxAudioEndpoint.Headphones, customPresetId, FlatThxBands, token),
            cancellationToken);

        // 0549 and the other 91-byte THX headsets expose the ten-band firmware EQ.
        // 0521 (KrakenBase37) has no EQ writer here; its Synapse 3 fix is spatial off.
        if (IsFeature91 && CanApplyThx)
        {
            var customIndex = Array.FindIndex(
                ActivePresets, preset => preset.Key == "RazerHeadsetPresetCustom");
            if (customIndex >= 0)
            {
                var customPreset = ActivePresets[customIndex].Value;
                await RunWriteAsync(
                    (service, token) => service.SetCustomEqAsync(
                        customPreset, Synapse3GameBands, token),
                    cancellationToken);
            }
        }
    }

    private WindowsThxHeadsetController ThxController =>
        _thxController ??= new WindowsThxHeadsetController(
            _connection.ProductId,
            _connection.ContainerId);

    private async Task RefreshThxAsync(CancellationToken cancellationToken)
    {
        if (!ThxHeadsetAdmission.IsSupported(_connection.ProductId))
        {
            return;
        }

        if (ThxHeadsetAdmission.Check(_connection.ProductId) is
            { RebootRequired: true } rebootStatus)
        {
            _thxStatus = rebootStatus;
            _thxState = null;
            _thxErrorText = ThxMessage(rebootStatus.ErrorCode);
            NotifyThxStatus();
            return;
        }

        // VSSrv is demand-start. A machine that already has the THX stack must
        // not look like a fresh install just because the service is stopped.
        if (ThxAudioAdmission.HasService(ThxHeadsetAdmission.ServiceName) &&
            !ThxAudioAdmission.HasRunningService(ThxHeadsetAdmission.ServiceName))
        {
            using var start = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            start.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await ThxAudioAdmission
                    .EnsureServiceRunningAsync(ThxHeadsetAdmission.ServiceName, start.Token)
                    .ConfigureAwait(true);
            }
            catch (Exception exception) when (
                exception is OperationCanceledException or InvalidOperationException or
                    System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
            }
        }

        _thxStatus = ThxHeadsetAdmission.Check(_connection.ProductId);
        _diagnostic?.Invoke(
            $"thx status availability={_thxStatus.Availability} error={_thxStatus.ErrorCode ?? "-"} " +
            $"service={ThxAudioAdmission.HasService(ThxHeadsetAdmission.ServiceName)} " +
            $"running={ThxAudioAdmission.HasRunningService(ThxHeadsetAdmission.ServiceName)}");
        if (_thxStatus.Availability != ThxHeadsetAvailability.Available)
        {
            _thxState = null;
            _thxErrorText = ThxMessage(_thxStatus.ErrorCode);
            NotifyThxStatus();
            return;
        }

        try
        {
            ApplyThxStatus(await ThxController.ReadAsync(
                ThxAudioEndpoint.Headphones,
                cancellationToken).ConfigureAwait(true));
            if (_thxState is null)
            {
                OperationStatusText = string.Empty;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _thxState = null;
            _diagnostic?.Invoke($"thx read failed error={exception.Message}");
            _thxErrorText = AppStrings.Text("RazerHeadsetThxRejectedInstalled");
            OperationStatusText = string.Empty;
            NotifyThxStatus();
        }
    }

    private async Task<bool> RunThxAsync(
        Func<WindowsThxHeadsetController, CancellationToken, Task<ThxAudioStatus>> action,
        CancellationToken cancellationToken)
    {
        if (!CanApplyThx || !BeginOperation()) return false;
        try
        {
            var status = await action(ThxController, cancellationToken).ConfigureAwait(true);
            if (status.State is null)
            {
                // A refused write does not mean the components went away; keep the
                // panel the last admission check proved and report the reason.
                _diagnostic?.Invoke(
                    $"thx operation failed availability={status.Availability} error={status.Error ?? "-"}");
                _thxErrorText = status.Error?.StartsWith(
                    "ThxOperationRejected",
                    StringComparison.Ordinal) == true
                    ? AppStrings.Text("RazerHeadsetThxRejectedInstalled")
                    : ThxMessage(status.Error);
                if (status.Error?.StartsWith("ThxOperationRejected", StringComparison.Ordinal) == true ||
                    status.Error == "ThxNativeFailure")
                {
                    _thxStatus = new ThxHeadsetStatus(
                        ThxHeadsetAvailability.NativeMissing,
                        status.Error);
                }
                OperationStatusText = string.Empty;
                return false;
            }
            ApplyThxStatus(status);
            ErrorText = string.Empty;
            OperationStatusText = status.IsAvailable
                ? AppStrings.Text("BlackSharkSaved")
                : string.Empty;
            return status.IsAvailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _diagnostic?.Invoke($"thx operation failed error={exception.Message}");
            _thxErrorText = AppStrings.Text("RazerHeadsetThxRejectedInstalled");
            OperationStatusText = string.Empty;
            return false;
        }
        finally
        {
            IsBusy = false;
            NotifyThxStatus();
        }
    }

    private void ApplyThxStatus(ThxAudioStatus status)
    {
        _diagnostic?.Invoke(
            $"thx result availability={status.Availability} error={status.Error ?? "-"}");
        if (status.State is { } state)
        {
            ApplyThxState(state);
            _thxStatus = new ThxHeadsetStatus(ThxHeadsetAvailability.Available, null);
            // A missing enhancement is a failed read, not a missing capability.
            // Keep the controls visible with defaults and say so.
            _thxErrorText = state.SoundNormalization is null || state.VoiceClarity is null
                ? AppStrings.Text("RazerHeadsetThxPartialRead")
                : string.Empty;
        }
        else
        {
            _thxState = null;
            _thxStatus = new ThxHeadsetStatus(
                status.Error switch
                {
                    "ThxServiceMissing" => ThxHeadsetAvailability.ServiceMissing,
                    "ThxNativeMissing" => ThxHeadsetAvailability.NativeMissing,
                    // The helper got past the file checks but the native stack refused
                    // to initialize or write. Keep the install entry available: the
                    // APO for this model is usually the missing piece.
                    "ThxNativeFailure" or "ThxOperationRejected" =>
                        ThxHeadsetAvailability.NativeMissing,
                    _ when status.Error?.StartsWith(
                        "ThxOperationRejected:",
                        StringComparison.Ordinal) == true =>
                        ThxHeadsetAvailability.NativeMissing,
                    _ => ThxHeadsetAvailability.Available,
                },
                status.Error);
            _thxErrorText = ThxMessage(status.Error);
        }
        NotifyThxStatus();
    }

    private void ApplyThxState(ThxAudioState state)
    {
        _thxState = state;
        _thxSpatialEnabled = state.SpatialEnabled;
        _thxEqualizerEnabled = state.EqualizerEnabled;
        _thxSelectedPresetIndex = Math.Max(0, state.Presets
            .Select((preset, index) => (preset, index))
            .FirstOrDefault(item => item.preset.Id == state.SelectedPresetId).index);

        ThxBands.Clear();
        foreach (var band in state.EqualizerBands)
        {
            ThxBands.Add(new RazerHeadsetThxBandViewModel(band.FrequencyHertz)
            {
                Level = band.GainDb,
            });
        }

        _thxNormalizationEnabled = state.SoundNormalization?.Enabled ?? false;
        _thxNormalizationLevel = state.SoundNormalization?.LevelPercent ?? 0;
        _thxVoiceClarityEnabled = state.VoiceClarity?.Enabled ?? false;
        _thxVoiceClarityLevel = state.VoiceClarity?.LevelPercent ?? 0;
        _thxBassBoostEnabled = state.BassBoost?.Enabled ?? false;
        _thxBassBoostLevel = state.BassBoost?.LevelPercent ?? 0;
        _thxSidetoneEnabled = state.Sidetone?.Enabled ?? false;
        _thxSidetoneLevel = state.Sidetone?.LevelPercent ?? 0;
        _thxCaptureNormalizationEnabled = state.CaptureNormalization?.Enabled ?? false;
        _thxCaptureNormalizationLevel = state.CaptureNormalization?.LevelPercent ?? 0;
        _thxCaptureVocalClarityEnabled = state.CaptureVocalClarity?.Enabled ?? false;
        _thxCaptureVocalClarityLevel = state.CaptureVocalClarity?.LevelPercent ?? 0;
        _thxCaptureNoiseReductionEnabled = state.CaptureNoiseReduction?.Enabled ?? false;
        _thxCaptureNoiseReductionLevel = state.CaptureNoiseReduction?.LevelPercent ?? 0;
        _thxCaptureVoiceGateEnabled = state.CaptureVoiceGate?.Enabled ?? false;
        _thxCaptureVoiceGateLevel = state.CaptureVoiceGate?.LevelPercent ?? 0;
        OnPropertyChanged(string.Empty);
    }

    private static string ThxMessage(string? error) => error switch
    {
        "ThxServiceMissing" => AppStrings.Text("RazerHeadsetThxServiceMissing"),
        "ThxNativeMissing" => AppStrings.Text("RazerHeadsetThxNativeMissing"),
        "ThxContainerMissing" => AppStrings.Text("RazerHeadsetThxContainerMissing"),
        "ThxHelperMissing" or "ThxHelperStartFailed" or "ThxHelperExited" =>
            AppStrings.Text("RazerHeadsetThxHelperFailed"),
        "ThxOperationRejected" or "ThxNativeFailure" or "ThxInvalidRequest" =>
            AppStrings.Text("RazerHeadsetThxOperationRejected"),
        _ when error?.StartsWith("ThxOperationRejected:", StringComparison.Ordinal) == true =>
            AppStrings.Text("RazerHeadsetThxOperationRejected"),
        "ThxRebootRequired" => AppStrings.Text("RazerHeadsetThxRebootRequired"),
        _ when error?.StartsWith("ThxHelperFailed", StringComparison.Ordinal) == true =>
            AppStrings.Text("RazerHeadsetThxHelperFailed"),
        _ => AppStrings.Text("RazerHeadsetThxFailed"),
    };

    private void NotifyThxStatus()
    {
        OnPropertyChanged(nameof(ThxStatusText));
        OnPropertyChanged(nameof(ThxInstallerVisibility));
        OnPropertyChanged(nameof(ThxInstallerLinkVisibility));
        OnPropertyChanged(nameof(ThxControlsVisibility));
        OnPropertyChanged(nameof(CanInstallThx));
        OnPropertyChanged(nameof(CanApplyThx));
        OnPropertyChanged(nameof(ThxPresetOptions));
        OnPropertyChanged(nameof(ThxBandsVisibility));
        OnPropertyChanged(nameof(ThxNormalizationVisibility));
        OnPropertyChanged(nameof(ThxVoiceClarityVisibility));
        OnPropertyChanged(nameof(ThxBassBoostVisibility));
        OnPropertyChanged(nameof(ThxSidetoneVisibility));
        OnPropertyChanged(nameof(ThxCaptureVisibility));
        OnPropertyChanged(nameof(ThxCaptureNormalizationVisibility));
        OnPropertyChanged(nameof(ThxCaptureVocalClarityVisibility));
        OnPropertyChanged(nameof(ThxCaptureNoiseReductionVisibility));
        OnPropertyChanged(nameof(ThxCaptureVoiceGateVisibility));
    }
}
