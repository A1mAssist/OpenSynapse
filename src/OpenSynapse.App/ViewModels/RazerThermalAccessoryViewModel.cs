using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerThermalChannelRowViewModel : INotifyPropertyChanged
{
    private readonly Func<RazerThermalChannelRowViewModel, CancellationToken, Task> _apply;
    private double _target;
    private int _currentRpm;
    private int _actualDuty;
    private int _mode;
    private bool _connected;
    private bool _isBusy;
    private string _statusText = string.Empty;

    internal RazerThermalChannelRowViewModel(
        RazerThermalChannel channel,
        bool isPwm,
        Func<RazerThermalChannelRowViewModel, CancellationToken, Task> apply)
    {
        _apply = apply;
        Index = channel.Index;
        Name = channel.Name;
        Kind = channel.Kind;
        IsPwm = isPwm;
        Minimum = isPwm ? 0 : 0;
        Maximum = isPwm ? RazerThermalAccessoryProtocol.PwmMaximumRpm : 100;
        Step = isPwm ? RazerThermalAccessoryProtocol.PwmRpmPerUnit : 1;
        Unit = isPwm ? "RPM" : "%";
        Update(channel);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public int Index { get; }
    public string Name { get; }
    internal RazerThermalChannelKind Kind { get; }
    public bool IsPwm { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public double Step { get; }
    public string Unit { get; }
    public string CurrentRpmText => $"{_currentRpm} RPM";
    public string ActualDutyText => $"{_actualDuty}%";
    public string ModeText => _mode switch
    {
        1 => AppStrings.Text("CoolingPadModeQuiet"),
        2 => AppStrings.Text("CoolingPadModeBalanced"),
        3 => AppStrings.Text("CoolingPadModePerformance"),
        _ => AppStrings.Text("CoolingPadCurveManual"),
    };

    public double Target
    {
        get => _target;
        set
        {
            if (!SetField(ref _target, Math.Clamp(value, Minimum, Maximum))) return;
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public bool CanApply => _connected && !_isBusy;
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }

    internal void Update(RazerThermalChannel channel)
    {
        _currentRpm = channel.CurrentRpm;
        _actualDuty = channel.ActualDuty;
        _mode = channel.Mode;
        _connected = channel.Connected;
        _target = IsPwm ? channel.TargetRpm ?? channel.CurrentRpm : channel.TargetDuty;
        OnPropertyChanged(string.Empty);
    }

    [NotProfileScoped]
    internal async Task ApplyAsync(CancellationToken cancellationToken)
    {
        if (!CanApply) return;
        IsBusy = true;
        try
        {
            StatusText = string.Empty;
            await _apply(this, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal void ApplyStatus(string text) => StatusText = text;

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

/// <summary>
/// Standalone thermal accessory page for the Hanbo AIO (fan + pump) and the PWM PC fan
/// controller (one row per detected channel). Writes are verified by a readback.
/// </summary>
public sealed class RazerThermalAccessoryViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ushort _productId;
    private readonly Action<string>? _diagnostic;
    private RazerThermalAccessoryService? _service;
    private bool _isConnected;
    private bool _isBusy;
    private string _name = string.Empty;
    private string _statusText = string.Empty;
    private string _errorText = string.Empty;
    private string _cpuTemperatureText = "--";
    private string _gpuTemperatureText = "--";
    private int _selectedModeIndex;

    internal RazerThermalAccessoryViewModel(ushort productId, Action<string>? diagnostic = null)
    {
        _productId = productId;
        _diagnostic = diagnostic;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<RazerThermalChannelRowViewModel> Channels { get; } = new();
    public string Name { get => _name; private set => SetField(ref _name, value); }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (!SetField(ref _errorText, value)) return;
            PropertyChanged?.Invoke(this, new(nameof(HasError)));
        }
    }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool IsConnected { get => _isConnected; private set => SetField(ref _isConnected, value); }
    public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }
    public string CpuTemperatureText { get => _cpuTemperatureText; private set => SetField(ref _cpuTemperatureText, value); }
    public string GpuTemperatureText { get => _gpuTemperatureText; private set => SetField(ref _gpuTemperatureText, value); }
    public bool IsHanbo => _productId == RazerThermalAccessoryProtocol.HanboProductId;
    public bool IsPwm => _productId == RazerThermalAccessoryProtocol.PwmProductId;
    public bool ShowTemperatures => false;
    public bool ShowModes => IsHanbo;
    public IReadOnlyList<string> ModeOptions =>
    [
        AppStrings.Text("CoolingPadCurveManual"),
        AppStrings.Text("CoolingPadModeQuiet"),
        AppStrings.Text("CoolingPadModeBalanced"),
        AppStrings.Text("CoolingPadModePerformance"),
    ];
    public int SelectedModeIndex
    {
        get => _selectedModeIndex;
        set => SetField(ref _selectedModeIndex, Math.Clamp(value, 0, 3));
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _service ??= await RazerThermalAccessoryService.ConnectAsync(
                _productId, _diagnostic, cancellationToken).ConfigureAwait(false);
            if (_service is null)
            {
                IsConnected = false;
                StatusText = AppStrings.Text("CoolingPadStatusNotConnected");
                ErrorText = $"No control channel found for {_productId:X4}.";
                return;
            }

            var state = await _service.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            Name = state.Name;
            IsConnected = true;
            StatusText = AppStrings.Text("CoolingPadStatusConnected");
            CpuTemperatureText = state.CpuTemperature is { } cpu ? $"{cpu:0} °C" : "--";
            GpuTemperatureText = state.GpuTemperature is { } gpu ? $"{gpu:0} °C" : "--";
            ErrorText = state.Error ?? string.Empty;
            SyncChannels(state.Channels);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            IsConnected = false;
            ErrorText = exception.Message;
            StatusText = AppStrings.Text("CoolingPadStatusNotConnected");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [NotProfileScoped]
    internal async Task ApplyChannelAsync(
        RazerThermalChannelRowViewModel row,
        CancellationToken cancellationToken)
    {
        if (_service is null) return;
        try
        {
            if (IsPwm)
            {
                await _service.SetPwmSpeedAsync(
                    checked((byte)row.Index),
                    (int)Math.Round(row.Target),
                    cancellationToken).ConfigureAwait(false);
                row.ApplyStatus(AppStrings.Text("BlackSharkSaved"));
            }
            else
            {
                var mode = SelectedModeIndex;
                var speed = mode switch
                {
                    1 => 20,
                    2 => 50,
                    3 => 80,
                    _ => (int)Math.Round(row.Target),
                };
                await _service.SetHanboModeAsync(
                    row.Kind,
                    mode,
                    speed,
                    cancellationToken).ConfigureAwait(false);
                row.ApplyStatus(AppStrings.Text("BlackSharkSaved"));
            }

            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            row.ApplyStatus(exception.Message);
            ErrorText = exception.Message;
        }
    }

    private void SyncChannels(IReadOnlyList<RazerThermalChannel> channels)
    {
        var existing = Channels.ToDictionary(row => (row.Index, row.Kind));
        Channels.Clear();
        foreach (var channel in channels)
        {
            if (existing.TryGetValue((channel.Index, channel.Kind), out var row))
            {
                row.Update(channel);
                Channels.Add(row);
            }
            else
            {
                Channels.Add(new RazerThermalChannelRowViewModel(
                    channel,
                    IsPwm,
                    ApplyChannelAsync));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
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
        PropertyChanged?.Invoke(this, new(propertyName));
        return true;
    }
}
