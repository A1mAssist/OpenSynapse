using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerCoolingPadViewModel
{
    private DispatcherTimer? _curveTimer;
    private bool _curveTickRunning;
    private bool _disposed;
    private int _curveGeneration;
    private CancellationTokenSource? _curveCancellation;
    private Task _curveTask = Task.CompletedTask;
    private RazerCoolingPadCurveMode _activeCurveMode;
    private RazerCoolingPadTemperatureSource _activeCurveSource;
    private int _selectedCurveModeIndex;
    private int _selectedCurveSourceIndex;
    private int? _lastCurveUnits;
    private string _curveStatusText = string.Empty;

    public Visibility CurveVisibility =>
        IsConnected && _performanceMonitor is not null ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<string> CurveModeOptions =>
    [
        AppStrings.Text("CoolingPadCurveManual"),
        AppStrings.Text("CoolingPadCurveQuiet"),
        AppStrings.Text("CoolingPadCurveBalanced"),
        AppStrings.Text("CoolingPadCurvePerformance"),
    ];

    public IReadOnlyList<string> CurveSourceOptions =>
    [
        AppStrings.Text("CoolingPadCurveSourceCpu"),
        AppStrings.Text("CoolingPadCurveSourceGpu"),
        AppStrings.Text("CoolingPadCurveSourceBoth"),
    ];

    public int SelectedCurveModeIndex
    {
        get => _selectedCurveModeIndex;
        set
        {
            if (SetField(ref _selectedCurveModeIndex, Math.Clamp(value, 0, 3)))
            {
                OnPropertyChanged(nameof(CanApplyCurve));
            }
        }
    }

    public int SelectedCurveSourceIndex
    {
        get => _selectedCurveSourceIndex;
        set
        {
            if (SetField(ref _selectedCurveSourceIndex, Math.Clamp(value, 0, 2)))
            {
                _lastCurveUnits = null;
            }
        }
    }

    public string CurveStatusText => _curveStatusText;

    public bool CanApplyCurve => IsConnected && !IsBusy && !_disposed;

    public async Task ApplyCurveAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyCurve)
        {
            return;
        }

        if (SelectedCurveModeIndex == 0)
        {
            StopCurveAndSelectManual();
            await _curveTask;
            return;
        }

        if (_performanceMonitor is null || _service is null)
        {
            SetCurveStatus(AppStrings.Text("CoolingPadCurveTemperatureUnavailable"));
            return;
        }

        IsBusy = true;
        StopCurveTimer();
        try
        {
            await _curveTask;
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || !IsConnected) return;
            _activeCurveMode = (RazerCoolingPadCurveMode)(SelectedCurveModeIndex - 1);
            _activeCurveSource = (RazerCoolingPadTemperatureSource)SelectedCurveSourceIndex;
            await _service.SetSmartModeAsync(
                (RazerCoolingPadSmartMode)(SelectedCurveModeIndex + 2),
                cancellationToken);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetCurveStatus(AppStrings.FormatText("CoolingPadCurveError", exception.Message));
            return;
        }

        finally { IsBusy = false; }

        if (_disposed || !IsConnected || cancellationToken.IsCancellationRequested) return;
        _curveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _lastCurveUnits = null;
        EnsureCurveTimer();
        _curveTimer!.Start();
        SetCurveStatus(AppStrings.FormatText(
            "CoolingPadCurveApplied",
            CurveModeOptions[SelectedCurveModeIndex]));
        await CurveTickAsync(cancellationToken);
    }

    private void EnsureCurveTimer()
    {
        if (_curveTimer is not null)
        {
            return;
        }

        _curveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _curveTimer.Tick += async (_, _) => await CurveTickAsync(CancellationToken.None);
    }

    private Task CurveTickAsync(CancellationToken cancellationToken)
    {
        if (_curveTickRunning) return Task.CompletedTask;
        return _curveTask = RunCurveTickAsync(cancellationToken);
    }

    private async Task RunCurveTickAsync(CancellationToken cancellationToken)
    {
        if (_disposed || _curveCancellation is null || _curveCancellation.IsCancellationRequested ||
            IsBusy ||
            _service is null ||
            _performanceMonitor is null ||
            _curveTimer is not { IsEnabled: true })
        {
            return;
        }

        _curveTickRunning = true;
        var generation = _curveGeneration;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _curveCancellation.Token);
        cancellationToken = linked.Token;
        try
        {
            var source = _activeCurveSource;
            var (cpu, gpu) = await _performanceMonitor.SampleTemperaturesAsync(
                includeCpu: source != RazerCoolingPadTemperatureSource.Gpu,
                includeGpu: source != RazerCoolingPadTemperatureSource.Cpu,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || generation != _curveGeneration || !IsConnected) return;
            var target = RazerCoolingPadCurve.TargetRpm(
                _activeCurveMode,
                source,
                cpu,
                gpu);
            if (target is not { } rpm)
            {
                SetCurveStatus(AppStrings.Text("CoolingPadCurveTemperatureUnavailable"));
                return;
            }

            var units = rpm / RazerCoolingPadProtocol.RpmPerUnit;
            if (_lastCurveUnits == units)
            {
                return;
            }

            await _service.SetCurveSpeedAsync(rpm, cancellationToken);
            if (_disposed || generation != _curveGeneration) return;
            _lastCurveUnits = units;
            SetCurveStatus(AppStrings.FormatText(
                "CoolingPadCurveActive",
                CurveModeOptions[SelectedCurveModeIndex],
                rpm));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetCurveStatus(AppStrings.FormatText("CoolingPadCurveError", exception.Message));
        }
        catch (Exception exception)
        {
            // A background sampler must never take the app down.
            SetCurveStatus(AppStrings.FormatText("CoolingPadCurveError", exception.Message));
        }
        finally
        {
            _curveTickRunning = false;
        }
    }

    private void StopCurveTimer()
    {
        ++_curveGeneration;
        _curveTimer?.Stop();
        _curveCancellation?.Cancel();
        _curveCancellation?.Dispose();
        _curveCancellation = null;
    }

    private void StopCurveAndSelectManual()
    {
        StopCurveTimer();
        _selectedCurveModeIndex = 0;
        _lastCurveUnits = null;
        OnPropertyChanged(nameof(SelectedCurveModeIndex));
        OnPropertyChanged(nameof(CanApplyCurve));
        SetCurveStatus(AppStrings.Text("CoolingPadCurveManualStatus"));
    }

    private void SetCurveStatus(string text)
    {
        _curveStatusText = text;
        OnPropertyChanged(nameof(CurveStatusText));
    }
}
