using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerCoolingPadViewModel
{
    private DispatcherTimer? _curveTimer;
    private bool _curveTickRunning;
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

    public bool CanApplyCurve => IsConnected && !IsBusy;

    public async Task ApplyCurveAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyCurve)
        {
            return;
        }

        if (SelectedCurveModeIndex == 0)
        {
            StopCurveAndSelectManual();
            return;
        }

        if (_performanceMonitor is null || _service is null)
        {
            SetCurveStatus(AppStrings.Text("CoolingPadCurveTemperatureUnavailable"));
            return;
        }

        try
        {
            await _service.SetSmartModeAsync(
                (RazerCoolingPadSmartMode)(SelectedCurveModeIndex + 2),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetCurveStatus(AppStrings.FormatText("CoolingPadCurveError", exception.Message));
            return;
        }

        _lastCurveUnits = null;
        EnsureCurveTimer();
        _curveTimer!.Start();
        SetCurveStatus(AppStrings.FormatText(
            "CoolingPadCurveApplied",
            CurveModeOptions[SelectedCurveModeIndex]));
        await CurveTickAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EnsureCurveTimer()
    {
        if (_curveTimer is not null)
        {
            return;
        }

        _curveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _curveTimer.Tick += async (_, _) => await CurveTickAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task CurveTickAsync(CancellationToken cancellationToken)
    {
        if (_curveTickRunning ||
            IsBusy ||
            _service is null ||
            _performanceMonitor is null ||
            _curveTimer is not { IsEnabled: true })
        {
            return;
        }

        _curveTickRunning = true;
        try
        {
            var source = (RazerCoolingPadTemperatureSource)SelectedCurveSourceIndex;
            var (cpu, gpu) = await _performanceMonitor.SampleTemperaturesAsync(
                includeCpu: source != RazerCoolingPadTemperatureSource.Gpu,
                includeGpu: source != RazerCoolingPadTemperatureSource.Cpu,
                cancellationToken).ConfigureAwait(false);

            var target = RazerCoolingPadCurve.TargetRpm(
                (RazerCoolingPadCurveMode)(SelectedCurveModeIndex - 1),
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

            await _service.SetCurveSpeedAsync(rpm, cancellationToken).ConfigureAwait(false);
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

    private void StopCurveTimer() => _curveTimer?.Stop();

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
