using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerGamepadViewModel
{
    private IReadOnlyList<byte> _profileIds = [];
    private int _selectedProfileIndex;
    private int _selectedPollingIndex;
    private bool _hasPollingProfile;
    private string _pollingProfileStatusText = string.Empty;

    public IReadOnlyList<int> PollingRates => RazerGamepadService.PollingRatesFor(_connection.ProductId);

    public IReadOnlyList<string> PollingRateOptions =>
        PollingRates.Select(hertz => $"{hertz} Hz").ToArray();

    public int SelectedPollingIndex
    {
        get => _selectedPollingIndex;
        set => SetField(ref _selectedPollingIndex, Math.Clamp(value, 0, Math.Max(0, PollingRates.Count - 1)));
    }

    public Visibility PollingVisibility =>
        _hasPollingProfile && PollingRates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<string> ProfileOptions =>
        _profileIds.Select(id => $"Profile {id}").ToArray();

    public int SelectedProfileIndex
    {
        get => _selectedProfileIndex;
        set => SetField(
            ref _selectedProfileIndex,
            Math.Clamp(value, 0, Math.Max(0, _profileIds.Count - 1)));
    }

    public Visibility ProfileVisibility =>
        _profileIds.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PollingProfileVisibility =>
        PollingVisibility == Visibility.Visible || ProfileVisibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string PollingProfileStatusText
    {
        get => _pollingProfileStatusText;
        private set => SetField(ref _pollingProfileStatusText, value);
    }

    public async Task LoadPollingProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        try
        {
            await LoadPollingProfileCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            PollingProfileStatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyPollingProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        try
        {
            if (ProfileVisibility == Visibility.Visible && _profileIds.Count > 0)
            {
                await _service.SetActiveProfileAsync(
                    _profileIds[SelectedProfileIndex], cancellationToken);
            }
            if (PollingVisibility == Visibility.Visible && PollingRates.Count > 0)
            {
                await _service.SetPollingRateAsync(
                    PollingRates[SelectedPollingIndex], cancellationToken);
            }

            await LoadPollingProfileCoreAsync(cancellationToken);
            PollingProfileStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            PollingProfileStatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadPollingProfileCoreAsync(CancellationToken cancellationToken)
    {
        var state = await _service.ReadPollingProfileAsync(cancellationToken);
        _profileIds = state.ProfileIds;
        _hasPollingProfile = state.PollingRateHertz is not null;
        _selectedProfileIndex = state.ActiveProfileId is { } active
            ? Math.Max(0, _profileIds.ToList().IndexOf(active))
            : 0;
        _selectedPollingIndex = state.PollingRateHertz is { } hertz
            ? Math.Max(0, PollingRates.ToList().IndexOf(hertz))
            : 0;
        PollingProfileStatusText = state.Errors.Count == 0
            ? string.Empty
            : string.Join(", ", state.Errors);
        OnPropertyChanged(nameof(PollingRateOptions));
        OnPropertyChanged(nameof(SelectedPollingIndex));
        OnPropertyChanged(nameof(PollingVisibility));
        OnPropertyChanged(nameof(ProfileOptions));
        OnPropertyChanged(nameof(SelectedProfileIndex));
        OnPropertyChanged(nameof(ProfileVisibility));
        OnPropertyChanged(nameof(PollingProfileVisibility));
    }
}
