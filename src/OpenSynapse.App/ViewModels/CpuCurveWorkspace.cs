using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// All-core PBO Curve Optimizer. The SMU accepts the shift but has no read-back, so the
/// card reports what this process wrote instead of presenting a firmware value.
/// </summary>
public sealed class CpuCurveWorkspace : INotifyPropertyChanged
{
    private static readonly int[] PresetSteps = [0, -5, -10, -15, -20, -25, -30];

    private readonly bool _isAmd;
    private readonly Func<string, Func<Task>, CancellationToken, Task> _runOperation;
    private int _steps;
    private int? _lastApplied;
    private bool _available;
    private bool _probed;
    private string _detail = string.Empty;

    public CpuCurveWorkspace(Func<string, Func<Task>, CancellationToken, Task> runOperation)
    {
        _runOperation = runOperation;
        _isAmd = CpuVendor.IsAmd(out _);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Visibility Visibility => _isAmd ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<string> Options => PresetSteps.Select(Format).ToArray();

    public int SelectedIndex
    {
        get => Math.Max(0, Array.IndexOf(PresetSteps, _steps));
        set
        {
            if (value < 0 || value >= PresetSteps.Length || PresetSteps[value] == _steps)
            {
                return;
            }

            _steps = PresetSteps[value];
            OnPropertyChanged();
            OnPropertyChanged(nameof(ValueText));
        }
    }

    public string ValueText => _steps == 0
        ? AppStrings.Text("CpuCurveOff")
        : AppStrings.FormatText("CpuCurveStepsValue", _steps);

    /// <summary>The shared operation gate already blocks a second write while busy.</summary>
    public bool CanEdit => _available;

    public string StatusText => !_probed
        ? AppStrings.Text("CpuCurveChecking")
        : _detail.Length > 0
            ? _detail
            : _lastApplied is { } applied
                ? AppStrings.FormatText("CpuCurveApplied", Format(applied))
                : AppStrings.Text("CpuCurveReady");

    public async Task RefreshSafelyAsync()
    {
        if (!_isAmd)
        {
            return;
        }

        DedicatedGpuCurveOptimizerState? state = null;
        try
        {
            state = await DedicatedGpuModeServiceClient.TryGetCurveOptimizerAsync();
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or TimeoutException or
                ObjectDisposedException or System.ComponentModel.Win32Exception)
        {
        }

        _available = state?.Available == true;
        _detail = _available
            ? string.Empty
            : state is null
                ? AppStrings.Text("CpuCurveServiceMissing")
                : state.Error is { Length: > 0 } error
                    ? error
                    : AppStrings.Text("CpuCurveUnavailable");
        _probed = true;
        NotifyAll();
    }

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEdit)
        {
            return;
        }

        var steps = _steps;
        await _runOperation(
            AppStrings.Text("CpuCurveTitle"),
            async () =>
            {
                var result = await DedicatedGpuModeServiceClient.TrySetCurveOptimizerAsync(
                    steps, cancellationToken);
                if (result is null || !result.Applied)
                {
                    throw new InvalidOperationException(
                        result?.Error ?? AppStrings.Text("CpuCurveUnavailable"));
                }

                _lastApplied = steps;
                _detail = string.Empty;
            },
            cancellationToken);
        NotifyAll();
    }

    private static string Format(int steps) => steps == 0
        ? AppStrings.Text("CpuCurveOff")
        : AppStrings.FormatText("CpuCurveStepsValue", steps);

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanEdit));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
