using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerKeyboardViewModel
{
    private bool _fullscreenPollingEnabled;
    private int _selectedFullscreenPollingIndex;
    private bool _fullscreenPollingLoaded;
    private int? _confirmedFullscreenPollingRate;

    public bool FullscreenPollingEnabled
    {
        get => _fullscreenPollingEnabled;
        set => SetField(ref _fullscreenPollingEnabled, value);
    }

    public int SelectedFullscreenPollingIndex
    {
        get => _selectedFullscreenPollingIndex;
        set
        {
            if (SetField(ref _selectedFullscreenPollingIndex, Math.Clamp(value, 0, PollingRates.Length - 1)))
            {
                OnPropertyChanged(nameof(FullscreenPollingRateText));
            }
        }
    }

    public string FullscreenPollingRateText => $"{PollingRates[SelectedFullscreenPollingIndex]} Hz";
    public IReadOnlyList<string> FullscreenPollingOptions => PollingOptions;

    private void LoadFullscreenPollingState(KeyboardProfileSettings keyboard)
    {
        if (!_hasPolling)
        {
            return;
        }

        var index = keyboard.FullscreenPollingRateHertz is int fullscreen
            ? Array.IndexOf(PollingRates, fullscreen)
            : -1;
        _confirmedFullscreenPollingRate = index >= 0 ? keyboard.FullscreenPollingRateHertz : null;
        FullscreenPollingEnabled = index >= 0;
        if (index >= 0)
        {
            SelectedFullscreenPollingIndex = index;
        }
        _fullscreenPollingLoaded = true;
    }

    internal bool HasFullscreenPollingChange(bool enabled, int index) =>
        _fullscreenPollingLoaded && !_restoringProfile && index >= 0 && index < PollingRates.Length &&
        (enabled ? PollingRates[index] : (int?)null) != _confirmedFullscreenPollingRate;

    private int? SavedFullscreenPollingRate =>
        _getProfile?.Invoke().Devices.GetValueOrDefault(ProfileDeviceKey)?.Keyboard.FullscreenPollingRateHertz;

    [ProfileSetting("keyboard.fullscreen-polling", DeviceCategory.Keyboard, powerScoped: true)]
    public async Task SetFullscreenPollingAsync(
        bool enabled,
        int index,
        CancellationToken cancellationToken = default)
    {
        if (!_hasPolling || _getProfile is null || _saveProfile is null || !BeginOperation())
        {
            NotifyFullscreenPollingSelection();
            return;
        }

        index = Math.Clamp(index, 0, PollingRates.Length - 1);
        var previousRate = SavedFullscreenPollingRate;
        var previousEnabled = _confirmedFullscreenPollingRate is not null;
        var savedIndex = _confirmedFullscreenPollingRate is int value ? Array.IndexOf(PollingRates, value) : -1;
        var previousIndex = savedIndex >= 0 ? savedIndex : SelectedFullscreenPollingIndex;
        try
        {
            FullscreenPollingEnabled = enabled;
            SelectedFullscreenPollingIndex = index;
            await SaveKeyboardSettingAsync(
                settings => settings.FullscreenPollingRateHertz =
                    enabled ? PollingRates[index] : null,
                settings => settings.FullscreenPollingRateHertz = previousRate,
                () =>
                {
                    FullscreenPollingEnabled = previousEnabled;
                    SelectedFullscreenPollingIndex = previousIndex;
                },
                null,
                cancellationToken);
            if (string.IsNullOrEmpty(ErrorText))
                _confirmedFullscreenPollingRate = enabled ? PollingRates[index] : null;
        }
        finally
        {
            IsBusy = false;
            NotifyFullscreenPollingSelection();
        }
    }

    private void NotifyFullscreenPollingSelection()
    {
        OnPropertyChanged(nameof(FullscreenPollingEnabled));
        OnPropertyChanged(nameof(SelectedFullscreenPollingIndex));
    }
}
