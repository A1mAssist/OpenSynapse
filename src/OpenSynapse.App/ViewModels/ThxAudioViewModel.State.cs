using OpenSynapse.Core.Audio;

namespace OpenSynapse.App.ViewModels;

public sealed partial class ThxAudioViewModel
{
    private int _requestGeneration;
    private int _pendingRequests;

    private async Task ExecuteAsync(
        Func<Task<ThxAudioStatus>> operation,
        string failureCode = "ThxHelperFailed")
    {
        var endpoint = SelectedEndpoint.Endpoint;
        var generation = ++_requestGeneration;
        ++_pendingRequests;
        IsBusy = true;
        try
        {
            var status = await operation();
            if (generation == _requestGeneration && endpoint == SelectedEndpoint.Endpoint &&
                (status.State is null || status.State.Endpoint == endpoint))
                ApplyStatus(status);
        }
        catch (Exception exception)
        {
            if (generation == _requestGeneration && endpoint == SelectedEndpoint.Endpoint)
                ApplyStatus(new ThxAudioStatus(
                ThxAudioAvailability.ComponentsUnavailable,
                $"{failureCode}:{exception.Message}",
                null));
        }
        finally
        {
            IsBusy = --_pendingRequests > 0;
        }
    }

    private void ApplyStatus(ThxAudioStatus status)
    {
        _availability = status.Availability;
        _errorCode = status.Error;
        _state = status.IsAvailable || status.Availability == ThxAudioAvailability.OperationRejected
            ? status.State : null;
        OnPropertyChanged(nameof(ControlsVisibility));
        OnPropertyChanged(nameof(UnavailableVisibility));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(UnavailableHint));
        OnPropertyChanged(nameof(InstallVisibility));
        if (_state is not { } state)
        {
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(BassBoostVisibility));
            OnPropertyChanged(nameof(HasEqualizerChanges));
            OnPropertyChanged(nameof(CanApplyEqualizer));
            OnPropertyChanged(nameof(CanResetEqualizer));
            StatusText = LocalizeError(status);
            NotifyStateChanged();
            return;
        }

        _applying = true;
        try
        {
            SpatialEnabled = state.SpatialEnabled;
            EqualizerEnabled = state.EqualizerEnabled;
            VoiceClarityEnabled = state.VoiceClarity?.Enabled ?? false;
            if (state.VoiceClarity is { } voiceClarity)
                VoiceClarityLevel = voiceClarity.LevelPercent;
            SoundNormalizationEnabled = state.SoundNormalization?.Enabled ?? false;
            if (state.SoundNormalization is { } normalization)
                SoundNormalizationLevel = normalization.LevelPercent;
            BassBoostEnabled = state.BassBoost?.Enabled ?? false;
            if (state.BassBoost is { } bassBoost)
                BassBoostLevel = bassBoost.LevelPercent;

            Presets.Clear();
            foreach (var preset in state.Presets)
            {
                Presets.Add(new ThxPresetOption(preset.Id, LocalizePreset(preset)));
            }
            var endpoint = state.Endpoint;
            if (!_presetCurves.TryGetValue(endpoint, out var curves))
            {
                curves = new Dictionary<string, double[]>(StringComparer.Ordinal);
                _presetCurves[endpoint] = curves;
            }
            if (!_factoryCurves.TryGetValue(endpoint, out var factory))
            {
                factory = new Dictionary<string, double[]>(StringComparer.Ordinal);
                _factoryCurves[endpoint] = factory;
            }
            foreach (var (id, gains) in state.FactoryCurves)
            {
                factory[id] = gains.ToArray();
                curves.TryAdd(id, gains.ToArray());
            }
            if (state.SelectedPresetId is { } selectedId)
            {
                curves[selectedId] = state.EqualizerBands.Select(band => band.GainDb).ToArray();
            }
            var selectedIndex = state.SelectedPresetId is null
                ? -1
                : state.Presets.ToList().FindIndex(preset => preset.Id == state.SelectedPresetId);
            _selectedPresetIndex = selectedIndex;
            OnPropertyChanged(nameof(SelectedPresetIndex));
            OnPropertyChanged(nameof(IsEditablePreset));
            OnPropertyChanged(nameof(EqualizerEditVisibility));
            OnPropertyChanged(nameof(EqualizerHint));

            Bands.Clear();
            foreach (var band in state.EqualizerBands)
            {
                var item = new ThxBandViewModel(band.FrequencyHertz, band.GainDb);
                item.PropertyChanged += BandPropertyChanged;
                Bands.Add(item);
            }
        }
        finally
        {
            _applying = false;
        }

        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(BassBoostVisibility));
        OnPropertyChanged(nameof(HasEqualizerChanges));
        OnPropertyChanged(nameof(CanApplyEqualizer));
        OnPropertyChanged(nameof(CanResetEqualizer));
        StatusText = status.IsAvailable ? AppStrings.Text("ThxStatusReady") : LocalizeError(status);
        NotifyStateChanged();
    }

}
