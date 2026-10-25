using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerKeyboardDeviceRowViewModel : INotifyPropertyChanged
{
    public RazerKeyboardDeviceRowViewModel(RazerKeyboardConnection connection)
    {
        Connection = connection;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RazerKeyboardConnection Connection { get; }
    public DeviceCategory HardwareCategory => DeviceCategory.Keyboard;
    public string Name => Connection.Name;
    public string Category => AppStrings.Text("RazerKeyboardCategory");
    public string Identity => $"VID_1532 / PID_{Connection.ProductId:X4}";
    public string IconGlyph => "\uE765";
    public string Status => AppStrings.Text("BlackSharkStatusConnected");
    public Brush StatusBrush { get; } = new SolidColorBrush(Color.FromArgb(255, 153, 221, 114));
    public string Error => string.Empty;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public sealed class RazerKeyboardViewModel : INotifyPropertyChanged
{
    private static readonly int[] PollingRates = [8_000, 4_000, 2_000, 1_000, 500, 250, 125];

    private readonly RazerKeyboardConnection _connection;
    private readonly Action<string>? _diagnostic;
    private bool _isBusy;
    private string _errorText = string.Empty;
    private string _operationStatusText = string.Empty;
    private string _firmwareText = "--";
    private string _serialText = "--";
    private string _batteryText = "--";
    private string _chargingText = "--";
    private bool _gameModeEnabled;
    private int _selectedPollingIndex;
    private bool _hasGameMode;
    private bool _hasPolling;
    private bool _hasBattery;

    public RazerKeyboardViewModel(
        RazerKeyboardConnection connection,
        Action<string>? diagnostic = null)
    {
        _connection = connection;
        _diagnostic = diagnostic;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => _connection.Name;
    public string CategoryText => AppStrings.Text("RazerKeyboardCategory");
    public string Identity => $"VID_1532 / PID_{_connection.ProductId:X4}";
    public string DevicePath => _connection.DevicePath;
    public string StatusText => AppStrings.Text("BlackSharkStatusConnected");

    public string FirmwareText { get => _firmwareText; private set => SetField(ref _firmwareText, value); }
    public string SerialText { get => _serialText; private set => SetField(ref _serialText, value); }
    public string BatteryText { get => _batteryText; private set => SetField(ref _batteryText, value); }
    public string ChargingText { get => _chargingText; private set => SetField(ref _chargingText, value); }

    public bool GameModeEnabled
    {
        get => _gameModeEnabled;
        set => SetField(ref _gameModeEnabled, value);
    }

    public int SelectedPollingIndex
    {
        get => _selectedPollingIndex;
        set => SetField(ref _selectedPollingIndex, Math.Clamp(value, 0, PollingRates.Length - 1));
    }

    public string PollingRateText => $"{PollingRates[SelectedPollingIndex]} Hz";
    public IReadOnlyList<string> PollingOptions => PollingRates.Select(rate => $"{rate} Hz").ToArray();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanEdit));
        }
    }

    public bool CanEdit => !IsBusy;
    public string ErrorText { get => _errorText; private set => SetField(ref _errorText, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string OperationStatusText
    {
        get => _operationStatusText;
        private set => SetField(ref _operationStatusText, value);
    }

    public Microsoft.UI.Xaml.Visibility GameModeVisibility =>
        _hasGameMode ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility PollingVisibility =>
        _hasPolling ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility BatteryVisibility =>
        _hasBattery ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        try
        {
            var state = await OpenService().ReadStateAsync(cancellationToken);
            ApplyState(state);
            ErrorText = state.Firmware is null && state.Serial is null && state.BatteryPercent is null &&
                state.GameModeEnabled is null && state.PollingRate is null
                    ? AppStrings.Text("RazerHeadsetReadFailed")
                    : string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task ApplyGameModeAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync((service, token) => service.SetGameModeAsync(GameModeEnabled, token), cancellationToken);

    public Task ApplyPollingAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            (service, token) => service.SetPollingRateAsync(PollingRates[SelectedPollingIndex], token),
            cancellationToken);

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    private RazerKeyboardService OpenService() =>
        new(new RazerFeatureTransport(), _connection.DevicePath, TimeSpan.FromMilliseconds(20), _diagnostic);

    private async Task RunWriteAsync(
        Func<RazerKeyboardService, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (!BeginOperation()) return;
        try
        {
            await write(OpenService(), cancellationToken);
            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyState(RazerKeyboardState state)
    {
        FirmwareText = state.Firmware?.ToString() ?? "--";
        SerialText = string.IsNullOrWhiteSpace(state.Serial) ? "--" : state.Serial;
        if (state.BatteryPercent is byte battery)
        {
            BatteryText = $"{battery}%";
            _hasBattery = true;
            OnPropertyChanged(nameof(BatteryVisibility));
        }

        if (state.Charging is bool charging)
        {
            ChargingText = AppStrings.Text(charging ? "BlackSharkCharging" : "BlackSharkNotCharging");
        }

        if (state.GameModeEnabled is bool gameMode)
        {
            GameModeEnabled = gameMode;
            _hasGameMode = true;
            OnPropertyChanged(nameof(GameModeVisibility));
        }

        if (state.PollingRate is int polling)
        {
            var index = Array.IndexOf(PollingRates, polling);
            if (index >= 0)
            {
                SelectedPollingIndex = index;
                OnPropertyChanged(nameof(PollingRateText));
            }

            _hasPolling = true;
            OnPropertyChanged(nameof(PollingVisibility));
        }
    }

    private bool BeginOperation()
    {
        if (IsBusy) return false;
        ErrorText = string.Empty;
        OperationStatusText = string.Empty;
        IsBusy = true;
        return true;
    }

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or InvalidOperationException or ArgumentOutOfRangeException or
            System.ComponentModel.Win32Exception or UnauthorizedAccessException;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(ErrorText)) OnPropertyChanged(nameof(HasError));
        if (propertyName == nameof(SelectedPollingIndex)) OnPropertyChanged(nameof(PollingRateText));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
