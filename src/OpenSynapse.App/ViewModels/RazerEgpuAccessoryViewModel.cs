using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Core X V2 fan page. The stock bundle carries separate GPU and chassis curves and
/// evaluates the active one on the host every 2.5 seconds.
/// </summary>
public sealed class RazerEgpuAccessoryViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly TimeSpan CurveInterval = TimeSpan.FromMilliseconds(2500);

    private readonly ushort _productId;
    private readonly Action<string>? _diagnostic;
    private readonly IPerformanceMonitor? _performanceMonitor;
    private RazerEgpuFanService? _service;
    private DispatcherTimer? _curveTimer;
    private bool _curveTickRunning;
    private bool _disposed;
    private bool _isConnected;
    private bool _isBusy;
    private int _selectedCurveIndex;
    private int _selectedTemperatureSourceIndex;
    private int _currentSpeedRpm;
    private int _minimumRpm = RazerEgpuFanProtocol.MinimumCurveRpm;
    private int _maximumRpm = RazerEgpuFanProtocol.MaximumCurveRpm;
    private double? _temperature;
    private string _statusText = string.Empty;
    private string _operationStatusText = string.Empty;
    private string _errorText = string.Empty;
    private IReadOnlyList<RazerEgpuFanCurvePoint>? _activeCurve;

    internal RazerEgpuAccessoryViewModel(
        ushort productId,
        Action<string>? diagnostic = null,
        IPerformanceMonitor? performanceMonitor = null)
    {
        _productId = productId;
        _diagnostic = diagnostic;
        _performanceMonitor = performanceMonitor;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title => "Razer Core X V2";
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public string OperationStatusText
    {
        get => _operationStatusText;
        private set => SetField(ref _operationStatusText, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (!SetField(ref _errorText, value)) return;
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (!SetField(ref _isConnected, value)) return;
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(CanRefresh));
            OnPropertyChanged(nameof(CurrentSpeedText));
            OnPropertyChanged(nameof(TemperatureText));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(CanRefresh));
        }
    }

    public bool CanApply => IsConnected && !IsBusy && !_disposed;
    public bool CanRefresh => IsConnected && !IsBusy && !_disposed;
    public string CurrentSpeedText => IsConnected ? $"{_currentSpeedRpm} RPM" : "--";
    public string TemperatureText =>
        _temperature is { } temperature ? $"{temperature:0.#} °C" : "--";
    public string MinimumRpmText => $"{_minimumRpm} RPM";
    public string MaximumRpmText => $"{_maximumRpm} RPM";

    public IReadOnlyList<string> TemperatureSourceOptions =>
    [
        AppStrings.Text("AccessoryEgpuSourceGpu"),
        AppStrings.Text("AccessoryEgpuSourceChassis"),
    ];

    internal RazerEgpuTemperatureSource TemperatureSource =>
        _selectedTemperatureSourceIndex == 1
            ? RazerEgpuTemperatureSource.Chassis
            : RazerEgpuTemperatureSource.Gpu;

    public int SelectedTemperatureSourceIndex
    {
        get => _selectedTemperatureSourceIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (!SetField(ref _selectedTemperatureSourceIndex, clamped)) return;
            StopCurveLoop();
            _temperature = null;
            OnPropertyChanged(nameof(TemperatureText));
            OnPropertyChanged(nameof(CurveSummary));
        }
    }

    public IReadOnlyList<string> CurveOptions =>
    [
        AppStrings.Text("AccessoryEgpuFanAuto"),
        AppStrings.Text("CoolingPadModeQuiet"),
        AppStrings.Text("CoolingPadModeBalanced"),
        AppStrings.Text("CoolingPadModePerformance"),
    ];

    public int SelectedCurveIndex
    {
        get => _selectedCurveIndex;
        set
        {
            if (!SetField(ref _selectedCurveIndex, Math.Clamp(value, 0, 3))) return;
            OnPropertyChanged(nameof(CurveSummary));
        }
    }

    public string CurveSummary
    {
        get
        {
            if (RazerEgpuFanProtocol.CurveForPreset(SelectedCurveIndex, TemperatureSource) is not { } curve)
            {
                return AppStrings.Text("AccessoryEgpuFanAutoHint");
            }

            return string.Join(
                "   ",
                curve.Select(point => $"{point.TemperatureCelsius}°C → {point.Rpm} RPM"));
        }
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || _disposed) return;
        IsBusy = true;
        try
        {
            _service ??= await RazerEgpuFanService.ConnectAsync(
                _diagnostic,
                cancellationToken).ConfigureAwait(true);
            if (_service is null)
            {
                SetDisconnected($"No control channel found for {_productId:X4}.");
                return;
            }

            var state = await _service.ReadStateAsync(cancellationToken).ConfigureAwait(true);
            ApplyState(state);
            await RefreshDisplayedTemperatureAsync(state, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            var service = _service;
            _service = null;
            if (service is not null)
            {
                await service.DisposeAsync().ConfigureAwait(true);
            }
            SetDisconnected(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApply || _service is null) return;
        IsBusy = true;
        StopCurveLoop();
        try
        {
            ErrorText = string.Empty;
            OperationStatusText = string.Empty;
            if (SelectedCurveIndex == 0)
            {
                await _service.SetAutomaticAsync(cancellationToken).ConfigureAwait(true);
                _activeCurve = null;
                await RefreshDisplayedTemperatureAsync(null, cancellationToken).ConfigureAwait(true);
            }
            else
            {
                var source = TemperatureSource;
                var curve = RazerEgpuFanProtocol.CurveForPreset(
                    SelectedCurveIndex,
                    source)!;
                var temperature = await ReadTemperatureAsync(source, cancellationToken)
                    .ConfigureAwait(true);
                var target = await _service.ApplyCurveAsync(
                    curve,
                    temperature,
                    source,
                    _minimumRpm,
                    _maximumRpm,
                    cancellationToken).ConfigureAwait(true);
                _activeCurve = curve;
                _temperature = temperature;
                _currentSpeedRpm = target;
                OnPropertyChanged(nameof(CurrentSpeedText));
                OnPropertyChanged(nameof(TemperatureText));
                StartCurveLoop();
            }

            OperationStatusText = AppStrings.Text("BlackSharkSaved");
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            ErrorText = exception.Message;
            StopCurveLoop();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (_service is null) return;
        var state = await _service.ReadStateAsync(cancellationToken).ConfigureAwait(true);
        ApplyState(state);
        await RefreshDisplayedTemperatureAsync(state, cancellationToken).ConfigureAwait(true);
    }

    private void ApplyState(RazerEgpuFanState state)
    {
        IsConnected = true;
        StatusText = FormatConnectedStatus(state.Mode);
        ErrorText = state.Error ?? string.Empty;
        _minimumRpm = state.MinimumRpm;
        _maximumRpm = state.MaximumRpm;
        if (state.CurrentSpeedRpm is { } speed) _currentSpeedRpm = speed;

        OnPropertyChanged(nameof(CurrentSpeedText));
        OnPropertyChanged(nameof(TemperatureText));
        OnPropertyChanged(nameof(MinimumRpmText));
        OnPropertyChanged(nameof(MaximumRpmText));
    }

    private static string FormatConnectedStatus(RazerEgpuFanMode? mode)
    {
        var connected = AppStrings.Text("CoolingPadStatusConnected");
        return mode switch
        {
            RazerEgpuFanMode.Auto or RazerEgpuFanMode.ForceAuto =>
                $"{connected} · {AppStrings.Text("AccessoryEgpuFanAuto")}",
            RazerEgpuFanMode.Manual =>
                $"{connected} · {AppStrings.Text("CoolingPadCurveManual")}",
            _ => connected,
        };
    }

    private async Task RefreshDisplayedTemperatureAsync(
        RazerEgpuFanState? state,
        CancellationToken cancellationToken)
    {
        try
        {
            var source = TemperatureSource;
            var temperature = source == RazerEgpuTemperatureSource.Chassis
                ? state?.ChassisTemperatureCelsius ??
                    await _service!.ReadChassisTemperatureAsync(cancellationToken).ConfigureAwait(true)
                : await ReadGpuTemperatureAsync(cancellationToken).ConfigureAwait(true);
            if (source == TemperatureSource) _temperature = temperature;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _temperature = null;
            _diagnostic?.Invoke($"egpu {TemperatureSource} temperature unavailable: {exception.Message}");
        }

        OnPropertyChanged(nameof(TemperatureText));
    }

    private async Task<double> ReadTemperatureAsync(
        RazerEgpuTemperatureSource source,
        CancellationToken cancellationToken) =>
        source == RazerEgpuTemperatureSource.Chassis
            ? await _service!.ReadChassisTemperatureAsync(cancellationToken).ConfigureAwait(true)
                ?? throw new InvalidOperationException(
                    "Core X V2 chassis temperature is unavailable; refusing to guess a fan target.")
            : await ReadGpuTemperatureAsync(cancellationToken).ConfigureAwait(true);

    private async Task<double> ReadGpuTemperatureAsync(CancellationToken cancellationToken)
    {
        if (_performanceMonitor is null)
        {
            throw new InvalidOperationException(
                "GPU temperature monitoring is unavailable; select the chassis source instead.");
        }

        var temperatures = await _performanceMonitor
            .SampleTemperaturesAsync(includeCpu: false, includeGpu: true, cancellationToken)
            .ConfigureAwait(true);
        return temperatures.GpuTemperatureCelsius
            ?? throw new InvalidOperationException(
                "GPU temperature is unavailable; refusing to guess a fan target.");
    }

    private void SetDisconnected(string error)
    {
        IsConnected = false;
        StatusText = AppStrings.Text("CoolingPadStatusNotConnected");
        ErrorText = error;
        StopCurveLoop();
        OnPropertyChanged(nameof(CurrentSpeedText));
        OnPropertyChanged(nameof(TemperatureText));
    }

    private void StartCurveLoop()
    {
        if (_activeCurve is null || _disposed) return;
        if (_curveTimer is null)
        {
            _curveTimer = new DispatcherTimer { Interval = CurveInterval };
            _curveTimer.Tick += async (_, _) => await CurveTickAsync().ConfigureAwait(true);
        }

        _curveTimer.Start();
    }

    private void StopCurveLoop()
    {
        _activeCurve = null;
        _curveTimer?.Stop();
    }

    private async Task CurveTickAsync()
    {
        if (_curveTickRunning || IsBusy || _activeCurve is not { } curve ||
            _service is null || _disposed)
        {
            return;
        }

        _curveTickRunning = true;
        try
        {
            var source = TemperatureSource;
            var (temperature, rpm) = await _service.RefreshCurveAsync(
                curve,
                await ReadTemperatureAsync(source, CancellationToken.None).ConfigureAwait(true),
                source,
                _minimumRpm,
                _maximumRpm).ConfigureAwait(true);
            if (source == TemperatureSource) _temperature = temperature;
            _currentSpeedRpm = rpm;
            OnPropertyChanged(nameof(TemperatureText));
            OnPropertyChanged(nameof(CurrentSpeedText));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            ErrorText = exception.Message;
            StopCurveLoop();
        }
        finally
        {
            _curveTickRunning = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        StopCurveLoop();
        if (_service is { } service)
        {
            await service.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or ArgumentException;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));
}
