using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Sensors;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// The system telemetry page: the performance sampler, the battery readouts, and the
/// poll that keeps them live. Device telemetry stays with the shell because it drives the
/// Blade and Viper controls.
/// </summary>
public sealed class TelemetryWorkspace : INotifyPropertyChanged
{
    private readonly IPerformanceMonitor _performanceMonitor;
    private readonly IPowerSourceProvider _powerSourceProvider;
    private readonly Func<bool> _isDisplayAvailable;
    private readonly Func<PowerSourceKind> _currentPowerSource;
    private readonly Action<string> _setPerformanceError;
    private readonly SystemTelemetryViewModel _system = new();
    private readonly SemaphoreSlim _performanceSamplingSignal = new(0, 1);
    private int _performanceSamplingEnabled = 1;

    public TelemetryWorkspace(
        IPerformanceMonitor performanceMonitor,
        IPowerSourceProvider powerSourceProvider,
        Func<bool> isDisplayAvailable,
        Func<PowerSourceKind> currentPowerSource,
        Action<string> setPerformanceError)
    {
        _performanceMonitor = performanceMonitor;
        _powerSourceProvider = powerSourceProvider;
        _isDisplayAvailable = isDisplayAvailable;
        _currentPowerSource = currentPowerSource;
        _setPerformanceError = setPerformanceError;
        _system.PropertyChanged += (_, args) => OnPropertyChanged(args.PropertyName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<DeviceBatteryReading>? BatteryObserved;

    public string TelemetryTimeText => _system.TelemetryTimeText;

    public string CpuName => _system.CpuName;
    public string CpuValue => _system.CpuValue;
    public double CpuPercent => _system.CpuPercent;
    public string CpuTemperatureText => _system.CpuTemperatureText;
    public string CpuPowerText => _system.CpuPowerText;
    public string CpuClockText => _system.CpuClockText;
    public string CpuTemperaturePeakText => _system.CpuTemperaturePeakText;
    public string CpuPowerPeakText => _system.CpuPowerPeakText;
    public string CpuClockPeakText => _system.CpuClockPeakText;
    public string GpuName => _system.GpuName;
    public string GpuValue => _system.GpuValue;
    public double GpuPercent => _system.GpuPercent;
    public string GpuTemperatureText => _system.GpuTemperatureText;
    public string GpuPowerText => _system.GpuPowerText;
    public string GpuClockText => _system.GpuClockText;
    public string GpuTemperaturePeakText => _system.GpuTemperaturePeakText;
    public string GpuPowerPeakText => _system.GpuPowerPeakText;
    public string GpuClockPeakText => _system.GpuClockPeakText;
    public string GpuMemoryLabel => _system.GpuMemoryLabel;
    public string GpuMemoryText => _system.GpuMemoryText;
    public string MemoryValue => _system.MemoryValue;
    public string MemoryDetail => _system.MemoryDetail;
    public double MemoryPercent => _system.MemoryPercent;
    public string StorageValue => _system.StorageValue;
    public string StorageDetail => _system.StorageDetail;
    public double StoragePercent => _system.StoragePercent;
    public string BatteryHealthValue => _system.BatteryHealthValue;
    public string BatteryHealthDetail => _system.BatteryHealthDetail;
    public string BatteryChargeValue { get; private set; } = "--";
    public string BatteryChargeState { get; private set; } = string.Empty;

    internal async Task RefreshPerformanceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await Task.Run(
                () => _performanceMonitor.SampleAsync(cancellationToken).AsTask(),
                cancellationToken);
            _system.Apply(snapshot);
            RefreshBatteryCharge();
            _setPerformanceError(snapshot.ErrorMessage ?? string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
        {
            SetPerformanceUnavailable(exception.Message);
        }
    }

    internal async Task RefreshBatteryHealthAsync(CancellationToken cancellationToken)
    {
        var snapshot = await Task.Run(WindowsBatteryHealthReader.TryRead, cancellationToken);
        _system.ApplyBatteryHealth(snapshot);
        OnPropertyChanged(nameof(BatteryHealthValue));
        OnPropertyChanged(nameof(BatteryHealthDetail));
        RefreshBatteryCharge();
    }

    internal void RefreshBatteryCharge()
    {
        var percent = _powerSourceProvider.BatteryPercent;
        var charging = _powerSourceProvider.IsCharging;
        BatteryChargeValue = percent is { } value ? $"{value}%" : "--";
        BatteryChargeState = percent is null
            ? AppStrings.Text("BatteryChargeUnavailable")
            : charging == true
                ? AppStrings.Text("BatteryChargeCharging")
                : _currentPowerSource() == PowerSourceKind.Battery
                    ? AppStrings.Text("BatteryChargeDischarging")
                    : AppStrings.Text("BatteryChargeIdle");
        OnPropertyChanged(nameof(BatteryChargeValue));
        OnPropertyChanged(nameof(BatteryChargeState));
    }

    internal void RefreshLocalization() => _system.RefreshLocalization();

    internal void NotifyBatteryObserved(DeviceBatteryReading reading) =>
        BatteryObserved?.Invoke(reading);

    internal void SetPerformanceSamplingEnabled(bool enabled)
    {
        if (Interlocked.Exchange(ref _performanceSamplingEnabled, enabled ? 1 : 0) !=
            (enabled ? 1 : 0))
        {
            NotifySamplingStateChanged();
        }
    }

    internal async Task RunPerformanceLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                while (!IsPerformanceSamplingActive)
                {
                    await _performanceSamplingSignal.WaitAsync(cancellationToken);
                }

                await RefreshPerformanceAsync(cancellationToken);
                await _performanceSamplingSignal.WaitAsync(
                    TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    internal void NotifySamplingStateChanged()
    {
        try
        {
            _performanceSamplingSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private bool IsPerformanceSamplingActive =>
        Volatile.Read(ref _performanceSamplingEnabled) != 0 &&
        _isDisplayAvailable();

    internal bool IsSamplingEnabled => Volatile.Read(ref _performanceSamplingEnabled) != 0;

    private void SetPerformanceUnavailable(string error)
    {
        _system.MarkUnavailable();
        _setPerformanceError(AppStrings.FormatText("PerformanceSamplingError", error));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
