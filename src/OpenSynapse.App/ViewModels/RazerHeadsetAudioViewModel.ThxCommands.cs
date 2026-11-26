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
            SetThxSpatialDraft(_thxState?.SpatialEnabled ?? previous);
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => settings.ThxSpatialEnabled = ThxSpatialEnabled,
            settings => settings.ThxSpatialEnabled = previous,
            () => SetThxSpatialDraft(previous),
            async token => ApplyThxStatus(await ThxController.SetSpatialAsync(
                ThxAudioEndpoint.Headphones, previous, token)),
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
        var previousIndex = _thxState.Presets.ToList().FindIndex(item => item.Id == _thxState.SelectedPresetId);
        var previousPreset = _thxState.Presets.ElementAtOrDefault(previousIndex);
        _diagnostic?.Invoke($"thx select preset requested={preset.Id} current={_thxState.SelectedPresetId ?? "-"}");
        if (!await RunThxAsync(
                (controller, token) => SelectThxPresetAsync(controller, preset.Id, token),
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
                    ApplyThxStatus(await SelectThxPresetAsync(ThxController, previousPreset.Id, token));
                }
            },
            cancellationToken);
    }

    [ProfileSetting("headset.thx.bands", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxBandsAsync(CancellationToken cancellationToken = default)
    {
        var gains = ThxBands.Select(band => band.Level).ToArray();
        if (_thxState?.SelectedPresetId is not { Length: > 0 } presetId)
        {
            return;
        }

        // THX keeps one live gain set; selecting a named preset reloads its factory
        // curve. Save this preset's edit in the profile and replay it on switch.
        var previous = _thxState.EqualizerBands.Select(band => band.GainDb).ToArray();
        if (!await RunThxAsync(
                (controller, token) => controller.SetGainsAsync(
                    ThxAudioEndpoint.Headphones, gains, token),
                cancellationToken))
        {
            return;
        }

        var previousStored = ReadStoredThxBands(presetId);
        var previousSavedPreset = ResolveHeadsetProfileSettings()?.ThxPreset;
        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.ThxPreset = presetId;
                settings.ThxBands = ToThxBandMap(gains);
                var map = settings.ThxBandsByPreset ??=
                    new Dictionary<string, Dictionary<string, double>>(
                        StringComparer.OrdinalIgnoreCase);
                map[presetId] = ToThxBandMap(gains);
            },
            settings =>
            {
                settings.ThxBands = ToThxBandMap(previous);
                settings.ThxPreset = previousSavedPreset;
                if (previousStored is null)
                {
                    settings.ThxBandsByPreset?.Remove(presetId);
                }
                else if (settings.ThxBandsByPreset is { } map)
                {
                    map[presetId] = ToThxBandMap(previousStored);
                }
            },
            () =>
            {
                for (var index = 0; index < previous.Length && index < ThxBands.Count; index++)
                {
                    ThxBands[index].Level = previous[index];
                }
            },
            async token => ApplyThxStatus(await ThxController.SetGainsAsync(
                ThxAudioEndpoint.Headphones, previous, token)),
            cancellationToken);
    }

    /// <summary>
    /// Fills the band sliders from a Synapse 4 EQ export.
    /// </summary>
    public bool TryLoadSynapseEq(string json)
    {
        var result = SynapseEqProfile.Read(json, ThxBands.Count);
        if (!result.Success)
        {
            _thxErrorText = result.Error == "bandCount"
                ? AppStrings.FormatText(
                    "RazerHeadsetThxImportBandCount", result.SourceBands, ThxBands.Count)
                : AppStrings.Text("RazerHeadsetThxImportInvalid");
            NotifyThxStatus();
            return false;
        }

        for (var index = 0; index < ThxBands.Count; index++)
        {
            ThxBands[index].Level = result.Gains[index];
        }

        _thxErrorText = string.Empty;
        NotifyThxStatus();
        return true;
    }

    private string? ThxCustomPresetId =>
        _thxState?.Presets
            .FirstOrDefault(preset =>
                preset.Id.Equals("Custom", StringComparison.OrdinalIgnoreCase) ||
                preset.DisplayName.Contains("Custom", StringComparison.OrdinalIgnoreCase))
            ?.Id;

    private Task<ThxAudioStatus> SelectThxPresetAsync(
        WindowsThxHeadsetController controller,
        string presetId,
        CancellationToken cancellationToken) =>
        ReadStoredThxBands(presetId) is { } gains
            ? controller.SelectPresetAsync(
                ThxAudioEndpoint.Headphones, presetId, gains, cancellationToken)
            : controller.SelectPresetOnlyAsync(
                ThxAudioEndpoint.Headphones, presetId, cancellationToken);

    private double[]? ReadStoredThxBands(string presetId)
    {
        if (ResolveHeadsetProfileSettings()?.ThxBandsByPreset is not { } map ||
            !map.TryGetValue(presetId, out var bands) ||
            bands.Count == 0)
        {
            return null;
        }

        var gains = new double[bands.Count];
        for (var index = 0; index < gains.Length; index++)
        {
            if (!bands.TryGetValue(index.ToString(), out var value))
            {
                return null;
            }
            gains[index] = value;
        }
        return gains;
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

        var customPresetId = ThxCustomPresetId ?? "Custom";
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

    /// <summary>
    /// Reads the live preset while the THX card is visible. The headset's own
    /// controls change the preset outside this app, so a single page-load read
    /// leaves the combo showing the old value.
    /// </summary>
    private async Task PollThxAsync(Func<bool> isCurrent, CancellationToken cancellationToken)
    {
        if (!CanApplyThx || _thxState is not { } current ||
            _thxReadback.Begin(ThxDraftPreset, ThxBands.Select(band => band.Level)) is not { } revision)
        {
            return;
        }

        try
        {
            var status = await ThxController.ReadAsync(
                ThxAudioEndpoint.Headphones,
                cancellationToken).ConfigureAwait(true);
            if (!isCurrent() || !CanApplyThx ||
                !_thxReadback.CanApply(revision, ThxDraftPreset, ThxBands.Select(band => band.Level)) ||
                status.State is not { } state ||
                (StringComparer.Ordinal.Equals(state.SelectedPresetId, current.SelectedPresetId) &&
                    state.EqualizerBands.SequenceEqual(current.EqualizerBands)))
            {
                return;
            }

            _diagnostic?.Invoke(
                $"thx EQ changed externally: {current.SelectedPresetId ?? "-"} -> " +
                $"{state.SelectedPresetId ?? "-"}");
            // An EQ read must not replace drafts in the other THX controls.
            if (!current.Presets.Any(preset => preset.Id == state.SelectedPresetId)) return;
            _thxState = current with { SelectedPresetId = state.SelectedPresetId, EqualizerBands = state.EqualizerBands };
            ApplyThxEqualizerState(_thxState);
            OnPropertyChanged(nameof(SelectedThxPresetIndex));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Polling must not replace the last good card with a transient error.
            _diagnostic?.Invoke(
                $"thx preset poll failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private async Task RefreshThxAsync(CancellationToken cancellationToken)
    {
        if (!ThxHeadsetAdmission.IsSupported(_connection.ProductId))
        {
            return;
        }

        if (ThxHeadsetAdmission.Check(_connection.ProductId) is
            { RebootRequired: true } rebootStatus)
        {
            // The marker exists to hide controls until the native stack answers.
            // Probe once here: a working APO clears the marker, a still-inactive
            // one keeps the reboot message.
            if (ThxHeadsetAdmission.CanProbe(_connection.ProductId, _connection.ContainerId) &&
                ThxAudioAdmission.HasRunningService(ThxHeadsetAdmission.ServiceName))
            {
                try
                {
                    var probe = await ThxController.ProbeAsync(
                        ThxAudioEndpoint.Headphones,
                        cancellationToken).ConfigureAwait(true);
                    _diagnostic?.Invoke(
                        $"thx probe availability={probe.Availability} error={probe.Error ?? "-"}");
                    if (probe.State is not null)
                    {
                        ApplyThxStatus(probe);
                        return;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsExpectedException(exception))
                {
                    _diagnostic?.Invoke($"thx probe failed error={exception.Message}");
                }
            }

            _thxStatus = rebootStatus;
            _thxState = null;
            _thxErrorText = ThxMessage(rebootStatus.ErrorCode);
            NotifyThxStatus();
            return;
        }

        _thxStatus = await ThxHeadsetAdmission
            .CheckAsync(_connection.ProductId, cancellationToken)
            .ConfigureAwait(true);
        _diagnostic?.Invoke(
            $"thx status availability={_thxStatus.Availability} error={_thxStatus.ErrorCode ?? "-"} " +
            $"service={ThxAudioAdmission.HasService(ThxHeadsetAdmission.ServiceName)} " +
            $"running={ThxAudioAdmission.HasRunningService(ThxHeadsetAdmission.ServiceName)} " +
            $"health={ThxServiceControl.ReadServiceHealth(ThxHeadsetAdmission.ServiceName, _diagnostic)}");
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
                _diagnostic?.Invoke(
                    $"thx operation failed availability={status.Availability} error={status.Error ?? "-"}");
                ApplyThxStatus(status);
                OperationStatusText = string.Empty;
                return false;
            }
            ApplyThxStatus(status);
            if (!status.IsAvailable)
            {
                _diagnostic?.Invoke($"thx write rejected error={status.Error} current={status.State.SelectedPresetId}");
                _thxErrorText = ThxMessage(status.Error);
                OperationStatusText = string.Empty;
                return false;
            }
            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
            return true;
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
            ThxHeadsetAdmission.ClearRebootMarker(_connection.ProductId);
            // A missing enhancement is a failed read, not a missing capability.
            // Preserve known values and disable settings until their read succeeds.
            _thxErrorText = state.SoundNormalization is null || state.VoiceClarity is null || state.BassBoost is null ||
                state.SelectedPresetId is null || state.EqualizerBands.Count != 10
                ? AppStrings.Text("RazerHeadsetThxPartialRead")
                : string.Empty;
        }
        else
        {
            _thxState = null;
            _thxStatus = new ThxHeadsetStatus(
                status.Error switch
                {
                    "ThxServiceMissing" or "ThxServiceBroken" =>
                        ThxHeadsetAvailability.ServiceMissing,
                    "ThxServiceStopped" => ThxHeadsetAvailability.InstalledButInactive,
                    "ThxNativeMissing" => ThxHeadsetAvailability.NativeMissing,
                    // The helper got past the file checks but the native stack refused
                    // to initialize or write. The components are already installed;
                    // showing the installer again only starts an install loop.
                    "ThxNativeFailure" or "ThxOperationRejected" =>
                        ThxHeadsetAvailability.InstalledButInactive,
                    _ when status.Error?.StartsWith(
                        "ThxOperationRejected:",
                        StringComparison.Ordinal) == true =>
                        ThxHeadsetAvailability.InstalledButInactive,
                    _ => ThxHeadsetAvailability.Available,
                },
                status.Error);
            _thxErrorText = status.Error is "ThxNativeFailure" or "ThxOperationRejected" ||
                status.Error?.StartsWith("ThxOperationRejected:", StringComparison.Ordinal) == true
                ? AppStrings.Text("RazerHeadsetThxRejectedInstalled")
                : ThxMessage(status.Error);
        }
        NotifyThxStatus();
    }

    private void ApplyThxEqualizerState(ThxAudioState state)
    {
        _thxSelectedPresetIndex = state.SelectedPresetId is null
            ? -1 : state.Presets.ToList().FindIndex(preset => preset.Id == state.SelectedPresetId);
        if (ThxBands.Count == state.EqualizerBands.Count &&
            ThxBands.Select(band => band.FrequencyHertz).SequenceEqual(state.EqualizerBands.Select(band => band.FrequencyHertz)))
        {
            for (var index = 0; index < ThxBands.Count; index++)
                ThxBands[index].Level = state.EqualizerBands[index].GainDb;
        }
        else
        {
            ThxBands.Clear();
            foreach (var band in state.EqualizerBands)
                ThxBands.Add(new RazerHeadsetThxBandViewModel(band.FrequencyHertz) { Level = band.GainDb });
        }
        _thxReadback.Accept(ThxDraftPreset, ThxBands.Select(band => band.Level));
        if (state.SelectedPresetId is { } presetId)
            ConfirmAudioSetting(HeadsetAudioSetting.ThxPreset, presetId);
        else
            _confirmedAudioSettings.Remove(HeadsetAudioSetting.ThxPreset);
        ConfirmAudioSetting(HeadsetAudioSetting.ThxBands, CaptureThxBands());
    }

    private void ApplyThxState(ThxAudioState state)
    {
        _thxState = state;
        _thxSpatialEnabled = state.SpatialEnabled;
        _thxEqualizerEnabled = state.EqualizerEnabled;
        ApplyThxEqualizerState(state);

        (_thxNormalizationEnabled, _thxNormalizationLevel) = state.SoundNormalization is { } normalization
            ? (normalization.Enabled, normalization.LevelPercent)
            : ConfirmedAudioValue(HeadsetAudioSetting.ThxNormalization, (_thxNormalizationEnabled, _thxNormalizationLevel));
        (_thxVoiceClarityEnabled, _thxVoiceClarityLevel) = state.VoiceClarity is { } voiceClarity
            ? (voiceClarity.Enabled, voiceClarity.LevelPercent)
            : ConfirmedAudioValue(HeadsetAudioSetting.ThxVoiceClarity, (_thxVoiceClarityEnabled, _thxVoiceClarityLevel));
        (_thxBassBoostEnabled, _thxBassBoostLevel) = state.BassBoost is { } bassBoost
            ? (bassBoost.Enabled, bassBoost.LevelPercent)
            : ConfirmedAudioValue(HeadsetAudioSetting.ThxBassBoost, (_thxBassBoostEnabled, _thxBassBoostLevel));
        (_thxSidetoneEnabled, _thxSidetoneLevel) = state.Sidetone is { } sidetone
            ? (sidetone.Enabled, sidetone.LevelPercent)
            : ConfirmedAudioValue(HeadsetAudioSetting.ThxSidetone, (_thxSidetoneEnabled, _thxSidetoneLevel));
        (_thxCaptureNormalizationEnabled, _thxCaptureNormalizationLevel) = state.CaptureNormalization is { } captureNormalization
            ? (captureNormalization.Enabled, captureNormalization.LevelPercent)
            : ConfirmedAudioValue(HeadsetAudioSetting.ThxCaptureNormalization, (_thxCaptureNormalizationEnabled, _thxCaptureNormalizationLevel));
        (_thxCaptureVocalClarityEnabled, _thxCaptureVocalClarityLevel) = state.CaptureVocalClarity is { } captureVocalClarity
            ? (captureVocalClarity.Enabled, captureVocalClarity.LevelPercent)
            : ConfirmedAudioValue(HeadsetAudioSetting.ThxCaptureVocalClarity, (_thxCaptureVocalClarityEnabled, _thxCaptureVocalClarityLevel));
        (_thxCaptureNoiseReductionEnabled, _thxCaptureNoiseReductionLevel) = state.CaptureNoiseReduction is { } captureNoiseReduction
            ? (captureNoiseReduction.Enabled, captureNoiseReduction.LevelPercent)
            : ConfirmedAudioValue(HeadsetAudioSetting.ThxCaptureNoiseReduction, (_thxCaptureNoiseReductionEnabled, _thxCaptureNoiseReductionLevel));
        (_thxCaptureVoiceGateEnabled, _thxCaptureVoiceGateLevel) = state.CaptureVoiceGate is { } captureVoiceGate
            ? (captureVoiceGate.Enabled, captureVoiceGate.LevelPercent)
            : ConfirmedAudioValue(HeadsetAudioSetting.ThxCaptureVoiceGate, (_thxCaptureVoiceGateEnabled, _thxCaptureVoiceGateLevel));
        AcceptThxAutoApplyReadback(state);
        OnPropertyChanged(string.Empty);
    }

    private static string ThxMessage(string? error) => error switch
    {
        "ThxServiceMissing" => AppStrings.Text("RazerHeadsetThxServiceMissing"),
        "ThxNativeMissing" => AppStrings.Text("RazerHeadsetThxNativeMissing"),
        "ThxContainerMissing" => AppStrings.Text("RazerHeadsetThxContainerMissing"),
        "ThxHelperMissing" or "ThxHelperStartFailed" or "ThxHelperExited" =>
            AppStrings.Text("RazerHeadsetThxHelperFailed"),
        "ThxNativeFailure" => AppStrings.Text("RazerHeadsetThxRejectedInstalled"),
        "ThxOperationRejected" or "ThxInvalidRequest" =>
            AppStrings.Text("RazerHeadsetThxOperationRejected"),
        _ when error?.StartsWith("ThxOperationRejected:", StringComparison.Ordinal) == true =>
            AppStrings.Text("RazerHeadsetThxOperationRejected"),
        "ThxRebootRequired" => AppStrings.Text("RazerHeadsetThxRebootRequired"),
        "ThxServiceBroken" => AppStrings.Text("RazerHeadsetThxServiceBroken"),
        "ThxServiceStopped" => AppStrings.Text("RazerHeadsetThxServiceMissing"),
        _ when error?.StartsWith("ThxHelperFailed", StringComparison.Ordinal) == true =>
            AppStrings.Text("RazerHeadsetThxHelperFailed"),
        _ => AppStrings.Text("RazerHeadsetThxFailed"),
    };

    private void NotifyThxEnhancementAvailability()
    {
        OnPropertyChanged(nameof(CanApplyThxEqualizer));
        OnPropertyChanged(nameof(CanApplyThxNormalization));
        OnPropertyChanged(nameof(CanApplyThxVoiceClarity));
        OnPropertyChanged(nameof(CanApplyThxBassBoost));
        OnPropertyChanged(nameof(CanApplyThxSidetone));
        OnPropertyChanged(nameof(CanApplyThxCaptureNormalization));
        OnPropertyChanged(nameof(CanApplyThxCaptureVocalClarity));
        OnPropertyChanged(nameof(CanApplyThxCaptureNoiseReduction));
        OnPropertyChanged(nameof(CanApplyThxCaptureVoiceGate));
    }

    private void NotifyThxStatus()
    {
        OnPropertyChanged(nameof(ThxStatusText));
        OnPropertyChanged(nameof(ThxInstallerVisibility));
        OnPropertyChanged(nameof(ThxControlsVisibility));
        OnPropertyChanged(nameof(CanInstallThx));
        OnPropertyChanged(nameof(CanApplyThx));
        NotifyThxEnhancementAvailability();
        OnPropertyChanged(nameof(ThxPresetOptions));
        OnPropertyChanged(nameof(SelectedThxPresetIndex));
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
