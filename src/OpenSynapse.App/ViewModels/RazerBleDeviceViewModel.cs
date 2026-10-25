using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerBleDeviceRowViewModel(RazerBleDevice device) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public RazerBleDevice Device { get; } = device;
    public string Name => string.IsNullOrWhiteSpace(Device.Name) ? "Razer BLE" : Device.Name;
    public string Identity => "BLE";
    public string IconGlyph => "\uE962";
    public string Status => AppStrings.Text("BlackSharkStatusConnected");
    // shortcut: protocol 30 is the middleware's mouse class; revisit when a BLE keyboard appears.
    public OpenSynapse.Core.Devices.DeviceCategory HardwareCategory => OpenSynapse.Core.Devices.DeviceCategory.Mouse;
    public Microsoft.UI.Xaml.Media.Brush StatusBrush { get; } =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 153, 221, 114));

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// <summary>
/// Battery, firmware, polling rate and DPI for one protocol 30 device. Every write is
/// verified by the service with a read-back and a restore of the previous value.
/// </summary>
public sealed class RazerBleDeviceViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    public static readonly IReadOnlyList<int> PollingRates = [1_000, 500, 333, 250, 200, 125, 100];

    private readonly RazerBleDevice _device;
    private readonly Action<string>? _diagnostic;
    private RazerBleDeviceService? _service;
    private bool _isBusy;
    private string _errorText = string.Empty;
    private string _operationStatusText = string.Empty;
    private string? _firmware;
    private string? _serial;
    private int? _batteryPercent;
    private byte? _chargingStatus;
    private int _selectedPollingIndex = PollingRates.Count - 1;
    private double _dpiX;
    private double _dpiY;

    public RazerBleDeviceViewModel(RazerBleDevice device, Action<string>? diagnostic = null)
    {
        _device = device;
        _diagnostic = diagnostic;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => string.IsNullOrWhiteSpace(_device.Name) ? "Razer BLE" : _device.Name;
    public string Identity => AppStrings.Text("BleIdentity");
    public string DeviceId => _device.Id;
    public bool CanEdit => !IsBusy && _service is not null;

    public string FirmwareText => _firmware ?? "—";
    public string SerialText => string.IsNullOrWhiteSpace(_serial) ? "—" : _serial;
    public string BatteryText => _batteryPercent is { } percent ? $"{percent}%" : "—";
    public string ChargingText => _chargingStatus switch
    {
        1 => AppStrings.Text("BlackSharkCharging"),
        2 => AppStrings.Text("BleChargingFailed"),
        6 => AppStrings.Text("BleChargingLimit"),
        0 => AppStrings.Text("OpenRazerZoneFullyCharged"),
        _ => string.Empty,
    };

    public IReadOnlyList<string> PollingRateOptions => PollingRates.Select(hertz => $"{hertz} Hz").ToArray();

    public int SelectedPollingIndex
    {
        get => _selectedPollingIndex;
        set => SetField(ref _selectedPollingIndex, value);
    }

    public double DpiX
    {
        get => _dpiX;
        set => SetField(ref _dpiX, value);
    }

    public double DpiY
    {
        get => _dpiY;
        set => SetField(ref _dpiY, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value)) OnPropertyChanged(nameof(CanEdit));
        }
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetField(ref _errorText, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string OperationStatusText
    {
        get => _operationStatusText;
        private set => SetField(ref _operationStatusText, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null)
        {
            if (!BeginOperation()) return;
            try
            {
                _service = await RazerBleDeviceService.OpenAsync(_device, _diagnostic, cancellationToken);
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                ErrorText = exception.Message;
                EndOperation();
                return;
            }
            OnPropertyChanged(nameof(CanEdit));
        }

        if (!BeginOperation()) return;
        try
        {
            Apply(await _service.ReadStateAsync(cancellationToken));
            OperationStatusText = string.Empty;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
        }
        finally
        {
            EndOperation();
        }
    }

    public Task ApplyPollingRateAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetPollingRateAsync(PollingRates[SelectedPollingIndex], cancellationToken),
            applied => SelectedPollingIndex = Math.Max(0, PollingRates.ToList().IndexOf(applied)));

    public Task ApplyDpiAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetDpiAsync((int)Math.Round(DpiX), (int)Math.Round(DpiY), cancellationToken),
            applied =>
            {
                DpiX = applied.X;
                DpiY = applied.Y;
            });

    public async ValueTask DisposeAsync()
    {
        if (_service is null) return;
        await Task.Run(_service.Dispose).ConfigureAwait(false);
        _service = null;
    }

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    private async Task RunWriteAsync<T>(
        Func<RazerBleDeviceService, Task<T>> write,
        Action<T> apply)
    {
        if (_service is null || !BeginOperation()) return;
        try
        {
            apply(await write(_service));
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
            ErrorText = string.Empty;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            EndOperation();
        }
    }

    private void Apply(RazerBleDeviceState state)
    {
        _firmware = state.Firmware?.ToVersionString();
        _serial = state.SerialNumber;
        _batteryPercent = state.BatteryPercent;
        _chargingStatus = state.ChargingStatus;
        if (state.PollingHertz is { } hertz)
        {
            _selectedPollingIndex = Math.Max(0, PollingRates.ToList().IndexOf(hertz));
        }
        if (state.Dpi is { } dpi)
        {
            _dpiX = dpi.X;
            _dpiY = dpi.Y;
        }

        OnPropertyChanged(nameof(FirmwareText));
        OnPropertyChanged(nameof(SerialText));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(ChargingText));
        OnPropertyChanged(nameof(SelectedPollingIndex));
        OnPropertyChanged(nameof(DpiX));
        OnPropertyChanged(nameof(DpiY));
    }

    private bool BeginOperation()
    {
        if (IsBusy) return false;
        ErrorText = string.Empty;
        OperationStatusText = string.Empty;
        IsBusy = true;
        return true;
    }

    private void EndOperation() => IsBusy = false;

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or InvalidOperationException or ArgumentOutOfRangeException or
            System.ComponentModel.Win32Exception or UnauthorizedAccessException or
            TimeoutException or OperationCanceledException;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

