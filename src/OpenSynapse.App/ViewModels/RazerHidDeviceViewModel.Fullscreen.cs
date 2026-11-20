using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private int _selectedFullscreenPollingRate;
    private bool _fullscreenPollingEnabled;

    public Visibility FullscreenPollingVisibility => VisibleWhen(
        Has(RazerHidBackendCapability.PollingRateWrite) && PollingOptions.Count > 1);

    public bool FullscreenPollingEnabled
    {
        get => _fullscreenPollingEnabled;
        private set
        {
            if (SetField(ref _fullscreenPollingEnabled, value))
            {
                OnPropertyChanged(nameof(CanEditFullscreenPolling));
            }
        }
    }

    public bool CanEditFullscreenPolling =>
        CanUse(RazerHidBackendCapability.PollingRateWrite) && !IsPollingBusy;
    public IReadOnlyList<string> FullscreenPollingOptionTexts => PollingOptionTexts;

    public int SelectedFullscreenPollingRateIndex
    {
        get => IndexOf(
            PollingOptions,
            _selectedFullscreenPollingRate > 0
                ? _selectedFullscreenPollingRate
                : PollingOptions.Count > 0 ? PollingOptions.Max() : 0);
        set
        {
            if (value >= 0 && value < PollingOptions.Count)
            {
                SetField(ref _selectedFullscreenPollingRate, PollingOptions[value]);
            }
        }
    }

    private void LoadFullscreenPollingState()
    {
        var rate = ReadProfile(settings => IsKeyboard
            ? settings.Keyboard.FullscreenPollingRateHertz
            : settings.Viper.FullscreenPollingRateHertz);
        _fullscreenPollingEnabled = rate is not null;
        _selectedFullscreenPollingRate = rate is int value && PollingOptions.Contains(value)
            ? value
            : PollingOptions.Count > 0
                ? PollingOptions.Max()
                : 0;
        OnPropertyChanged(nameof(FullscreenPollingEnabled));
        OnPropertyChanged(nameof(SelectedFullscreenPollingRateIndex));
    }

    [ProfileSetting("input.fullscreen-polling", DeviceCategory.Unknown, powerScoped: true)]
    public Task ApplyFullscreenPollingAsync(
        bool enabled,
        int rateIndex,
        CancellationToken cancellationToken = default)
    {
        if (rateIndex < 0 || rateIndex >= PollingOptions.Count)
        {
            return Task.CompletedTask;
        }

        var rate = enabled ? PollingOptions[rateIndex] : (int?)null;
        var previousEnabled = FullscreenPollingEnabled;
        var previousRate = _selectedFullscreenPollingRate;
        var previousProfile = ReadProfile(settings => IsKeyboard
            ? settings.Keyboard.FullscreenPollingRateHertz
            : settings.Viper.FullscreenPollingRateHertz);
        return RunWriteAsync(
            RazerHidBackendCapability.PollingRateWrite,
            () => IsPollingBusy,
            value => IsPollingBusy = value,
            async () =>
            {
                FullscreenPollingEnabled = enabled;
                _selectedFullscreenPollingRate = PollingOptions[rateIndex];
                OnPropertyChanged(nameof(SelectedFullscreenPollingRateIndex));
                await SaveProfileAsync(
                    settings =>
                    {
                        if (IsKeyboard) settings.Keyboard.FullscreenPollingRateHertz = rate;
                        else settings.Viper.FullscreenPollingRateHertz = rate;
                    },
                    settings =>
                    {
                        if (IsKeyboard) settings.Keyboard.FullscreenPollingRateHertz = previousProfile;
                        else settings.Viper.FullscreenPollingRateHertz = previousProfile;
                    },
                    () =>
                    {
                        FullscreenPollingEnabled = previousEnabled;
                        _selectedFullscreenPollingRate = previousRate;
                        OnPropertyChanged(nameof(SelectedFullscreenPollingRateIndex));
                    },
                    null,
                    cancellationToken);
            },
            cancellationToken);
    }
}
