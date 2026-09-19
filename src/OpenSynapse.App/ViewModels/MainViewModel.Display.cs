using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    public async Task ApplyInternalDisplayRefreshRateAsync(CancellationToken cancellationToken = default)
    {
        if (!_canSetInternalDisplayRefreshRate || _internalDisplayController is null)
        {
            return;
        }

        if (!IsSelectedRefreshRatePowerActive)
        {
            var previous = EditableRefreshRateBladeProfile.RefreshRateHertz;
            EditableRefreshRateBladeProfile.RefreshRateHertz = InternalDisplayRefreshRateHertz;
            if (!await SaveProfileAsync(cancellationToken))
            {
                EditableRefreshRateBladeProfile.RefreshRateHertz = previous;
            }
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_8423206D"), async () =>
        {
            var snapshot = _internalDisplayController.SetRefreshRate(InternalDisplayRefreshRateHertz);
            ApplyInternalDisplaySnapshot(snapshot);
            EditableRefreshRateBladeProfile.RefreshRateHertz = snapshot.RefreshRateHertz;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () =>
            InternalDisplayRefreshRateHertz = _confirmedInternalDisplayRefreshRateHertz);
    }

    public string InternalDisplayResolutionText { get => _internalDisplayResolutionText; private set => SetField(ref _internalDisplayResolutionText, value); }
    public string InternalDisplayRefreshRateText { get => _internalDisplayRefreshRateText; private set => SetField(ref _internalDisplayRefreshRateText, value); }
    public IReadOnlyList<int> InternalDisplayRefreshRates
    {
        get => _internalDisplayRefreshRates;
        private set
        {
            if (SetField(ref _internalDisplayRefreshRates, value))
            {
                OnPropertyChanged(nameof(InternalDisplayRefreshRateOptions));
                OnPropertyChanged(nameof(InternalDisplayRefreshRateIndex));
            }
        }
    }
    public IReadOnlyList<string> InternalDisplayRefreshRateOptions => InternalDisplayRefreshRates
        .Select(rate => $"{rate} Hz")
        .ToArray();
    public int InternalDisplayRefreshRateHertz
    {
        get => _internalDisplayRefreshRateHertz;
        set
        {
            if (SetField(ref _internalDisplayRefreshRateHertz, value))
            {
                OnPropertyChanged(nameof(InternalDisplayRefreshRateIndex));
            }
        }
    }
    public int InternalDisplayRefreshRateIndex
    {
        get => Array.IndexOf(InternalDisplayRefreshRates.ToArray(), InternalDisplayRefreshRateHertz);
        set
        {
            if (value >= 0 && value < InternalDisplayRefreshRates.Count)
            {
                InternalDisplayRefreshRateHertz = InternalDisplayRefreshRates[value];
            }
        }
    }
    public bool CanSetInternalDisplayRefreshRate { get => _canSetInternalDisplayRefreshRate; private set => SetField(ref _canSetInternalDisplayRefreshRate, value); }
}
