using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerKeyboardViewModel
{
    private bool _fullscreenPollingEnabled;
    private int _selectedFullscreenPollingIndex;

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
        FullscreenPollingEnabled = index >= 0;
        if (index >= 0)
        {
            SelectedFullscreenPollingIndex = index;
        }
    }

    [ProfileSetting("keyboard.fullscreen-polling", DeviceCategory.Keyboard, powerScoped: true)]
    public async Task SetFullscreenPollingAsync(
        bool enabled,
        int index,
        CancellationToken cancellationToken = default)
    {
        if (!_hasPolling || _getProfile is null || _saveProfile is null || !BeginOperation())
        {
            return;
        }

        index = Math.Clamp(index, 0, PollingRates.Length - 1);
        var previousEnabled = FullscreenPollingEnabled;
        var previousIndex = SelectedFullscreenPollingIndex;
        try
        {
            FullscreenPollingEnabled = enabled;
            SelectedFullscreenPollingIndex = index;
            await SaveKeyboardSettingAsync(
                settings => settings.FullscreenPollingRateHertz =
                    enabled ? PollingRates[index] : null,
                settings => settings.FullscreenPollingRateHertz =
                    previousEnabled ? PollingRates[previousIndex] : null,
                () =>
                {
                    FullscreenPollingEnabled = previousEnabled;
                    SelectedFullscreenPollingIndex = previousIndex;
                },
                null,
                cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
