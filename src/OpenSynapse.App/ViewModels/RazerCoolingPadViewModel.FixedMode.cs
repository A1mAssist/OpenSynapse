using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerCoolingPadViewModel
{
    private bool _hasFixedMode;
    private int _selectedFixedModeIndex = 1;
    private string _fixedModeStatusText = string.Empty;

    public Visibility FixedModeVisibility =>
        _hasFixedMode ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<string> FixedModeOptions =>
    [
        AppStrings.Text("CoolingPadFixedLow"),
        AppStrings.Text("CoolingPadFixedMid"),
        AppStrings.Text("CoolingPadFixedHigh"),
    ];

    public int SelectedFixedModeIndex
    {
        get => _selectedFixedModeIndex;
        set
        {
            if (SetField(ref _selectedFixedModeIndex, Math.Clamp(value, 0, 2)))
            {
                OnPropertyChanged(nameof(CanApplyFixedMode));
            }
        }
    }

    public string FixedModeStatusText => _fixedModeStatusText;

    public bool CanApplyFixedMode => IsConnected && !IsBusy && _hasFixedMode;

    public async Task ApplyFixedModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyFixedMode || _service is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var mode = (RazerCoolingPadFixedMode)SelectedFixedModeIndex;
            var actual = await _service.SetFixedModeAsync(mode, cancellationToken).ConfigureAwait(false);
            _fixedModeStatusText = AppStrings.FormatText(
                "CoolingPadFixedModeStatus",
                FixedModeOptions[(int)mode],
                actual.Rpm);
            SetError(string.Empty);
            OnPropertyChanged(nameof(FixedModeStatusText));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetError(AppStrings.FormatText("CoolingPadFixedModeError", exception.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadFixedModeCoreAsync(CancellationToken cancellationToken)
    {
        var service = _service ?? throw new InvalidOperationException("The cooling pad is not connected.");
        try
        {
            var state = await service.ReadCoolingModeAsync(cancellationToken).ConfigureAwait(false);
            _hasFixedMode = true;
            if (state.ModeId <= 2)
            {
                _selectedFixedModeIndex = state.ModeId;
            }

            _fixedModeStatusText = AppStrings.FormatText(
                "CoolingPadFixedModeCurrent",
                DescribeMode(state.ModeId),
                state.Rpm);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _hasFixedMode = false;
            _fixedModeStatusText = string.Empty;
        }

        OnPropertyChanged(nameof(FixedModeVisibility));
        OnPropertyChanged(nameof(SelectedFixedModeIndex));
        OnPropertyChanged(nameof(FixedModeStatusText));
        OnPropertyChanged(nameof(CanApplyFixedMode));
    }

    private static string DescribeMode(byte mode) => mode switch
    {
        0 => AppStrings.Text("CoolingPadFixedLow"),
        1 => AppStrings.Text("CoolingPadFixedMid"),
        2 => AppStrings.Text("CoolingPadFixedHigh"),
        3 => AppStrings.Text("CoolingPadModeQuiet"),
        4 => AppStrings.Text("CoolingPadModeBalanced"),
        5 => AppStrings.Text("CoolingPadModePerformance"),
        6 => AppStrings.Text("CoolingPadModeOff"),
        _ => AppStrings.FormatText("CoolingPadModeUnknown", mode),
    };
}
