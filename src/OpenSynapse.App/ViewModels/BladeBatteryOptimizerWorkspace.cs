using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Battery-optimizer card state. The firmware exposes this as the existing charge
/// limiter plus the one-time full-charge override; no separate report exists.
/// </summary>
public sealed class BladeBatteryOptimizerWorkspace : INotifyPropertyChanged
{
    private readonly BladeBatteryOptimizerService _service;
    private readonly Func<IReadOnlyList<DeviceDescriptor>> _devices;
    private readonly Func<string, Func<Task>, CancellationToken, Action?, string?, string?, Task> _runDeviceOperation;
    private bool _supported;
    private bool _supportsOverride;
    private bool _enabled;
    private int _thresholdIndex;
    private bool _oneTimeOverride;
    private bool _busy;
    private string _statusText = string.Empty;

    internal BladeBatteryOptimizerWorkspace(
        BladeBatteryOptimizerService service,
        Func<IReadOnlyList<DeviceDescriptor>> devices,
        Func<string, Func<Task>, CancellationToken, Action?, string?, string?, Task> runDeviceOperation)
    {
        _service = service;
        _devices = devices;
        _runDeviceOperation = runDeviceOperation;
        _thresholdIndex = Array.IndexOf(BladeBatteryOptimizerProtocol.SupportedThresholds.ToArray(), BladeBatteryOptimizerProtocol.DefaultThresholdPercent);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Visibility CardVisibility => _supported ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OverrideVisibility =>
        _supported && _supportsOverride ? Visibility.Visible : Visibility.Collapsed;
    public IReadOnlyList<string> ThresholdOptions => AppStrings.Texts("50%", "55%", "60%", "65%", "70%", "75%", "80%");
    public bool IsEnabled { get => _enabled; set => SetField(ref _enabled, value); }
    public int ThresholdIndex { get => _thresholdIndex; set => SetField(ref _thresholdIndex, value); }
    public bool OneTimeOverrideEnabled { get => _oneTimeOverride; set => SetField(ref _oneTimeOverride, value); }
    public bool IsBusy { get => _busy; private set { if (SetField(ref _busy, value)) OnPropertyChanged(nameof(CanEdit)); } }
    public bool CanEdit => _supported && !_busy;
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }

    internal void Update(RazerDeviceTelemetry? telemetry)
    {
        var descriptors = _devices();
        _supported = _service.IsSupported(descriptors) &&
            telemetry?.BladeChargeLimitPercent is not null;
        _supportsOverride = _supported && _service.SupportsChargingOverride(descriptors);
        OnPropertyChanged(nameof(CardVisibility));
        OnPropertyChanged(nameof(OverrideVisibility));
        OnPropertyChanged(nameof(CanEdit));

        if (!_supported || telemetry?.BladeChargeLimitPercent is not int percent)
        {
            return;
        }

        IsEnabled = percent != 100;
        ThresholdIndex = ThresholdIndexFor(
            IsEnabled ? percent : BladeBatteryOptimizerProtocol.DefaultThresholdPercent);
        OneTimeOverrideEnabled = _supportsOverride && telemetry.BladeOneTimeFullChargeEnabled == true;
    }

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEdit)
        {
            return;
        }

        var threshold = BladeBatteryOptimizerProtocol.SupportedThresholds[ThresholdIndex];
        IsBusy = true;
        StatusText = string.Empty;
        try
        {
            await _runDeviceOperation(
                AppStrings.Text("BladeBatteryOptimizerOperation"),
                async () =>
                {
                    var state = await _service.ApplyAsync(
                        _devices(), IsEnabled, threshold, OneTimeOverrideEnabled, cancellationToken);
                    _enabled = state.IsEnabled;
                    _thresholdIndex = ThresholdIndexFor(state.ThresholdPercent);
                    _oneTimeOverride = state.OneTimeOverrideEnabled;
                    OnPropertyChanged(nameof(IsEnabled));
                    OnPropertyChanged(nameof(ThresholdIndex));
                    OnPropertyChanged(nameof(OneTimeOverrideEnabled));
                    StatusText = AppStrings.Text("BladeBatteryOptimizerApplied");
                },
                cancellationToken,
                null,
                null,
                null);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static int ThresholdIndexFor(int percent)
    {
        var index = Array.IndexOf(BladeBatteryOptimizerProtocol.SupportedThresholds.ToArray(), percent);
        return index >= 0
            ? index
            : Array.IndexOf(
                BladeBatteryOptimizerProtocol.SupportedThresholds.ToArray(),
                BladeBatteryOptimizerProtocol.DefaultThresholdPercent);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
