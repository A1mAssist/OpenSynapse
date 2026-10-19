using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed class BlackSharkHeadsetViewModel : INotifyPropertyChanged
{
    private static readonly BlackSharkAncMode[] AncModes =
        [BlackSharkAncMode.Off, BlackSharkAncMode.Anc, BlackSharkAncMode.Ambient];

    private readonly BlackSharkHeadsetConnection _connection;
    private bool _isBusy;
    private string _errorText = string.Empty;
    private string _operationStatusText = string.Empty;
    private string? _serial;
    private int? _batteryPercent;
    private bool? _charging;
    private double _sidetoneLevel;
    private bool _thxEnabled;
    private int _selectedAncModeIndex;
    private double _ancLevel = 1;
    private double _gameChatBalance = 10;

    public BlackSharkHeadsetViewModel(BlackSharkHeadsetConnection connection)
    {
        _connection = connection;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => _connection.Name;
    public string CategoryText => AppStrings.Text("BlackSharkHeadsetCategory");
    public string Identity => $"VID_1532 / PID_{_connection.ProductId:X4}";
    public string DevicePath => _connection.DevicePath;
    public string StatusText => AppStrings.Text("BlackSharkStatusConnected");
    public string? Serial { get => _serial; private set => SetField(ref _serial, value); }
    public Visibility SerialVisibility => string.IsNullOrWhiteSpace(Serial)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string BatteryText => _batteryPercent is int battery ? $"{battery}%" : "--";
    public string ChargingText => _charging switch
    {
        true => AppStrings.Text("BlackSharkCharging"),
        false => AppStrings.Text("BlackSharkNotCharging"),
        _ => "--",
    };

    public double SidetoneLevel
    {
        get => _sidetoneLevel;
        set
        {
            if (!SetField(ref _sidetoneLevel, Math.Clamp(value, 0, 15))) return;
            OnPropertyChanged(nameof(SidetoneText));
        }
    }

    public string SidetoneText => $"{Math.Round(SidetoneLevel)} / 15";

    public bool ThxEnabled
    {
        get => _thxEnabled;
        set => SetField(ref _thxEnabled, value);
    }

    public IReadOnlyList<string> AncModeOptions =>
    [
        AppStrings.Text("BlackSharkAncOff"),
        AppStrings.Text("BlackSharkAncOn"),
        AppStrings.Text("BlackSharkAncAmbient"),
    ];

    public int SelectedAncModeIndex
    {
        get => _selectedAncModeIndex;
        set
        {
            var normalized = Math.Clamp(value, 0, AncModes.Length - 1);
            if (!SetField(ref _selectedAncModeIndex, normalized)) return;
            OnPropertyChanged(nameof(AncLevelVisibility));
        }
    }

    public double AncLevel
    {
        get => _ancLevel;
        set => SetField(ref _ancLevel, Math.Clamp(value, 1, 4));
    }

    public Visibility AncLevelVisibility =>
        _selectedAncModeIndex == 1 ? Visibility.Visible : Visibility.Collapsed;

    public double GameChatBalance
    {
        get => _gameChatBalance;
        set
        {
            if (!SetField(ref _gameChatBalance, Math.Clamp(value, 0, 20))) return;
            OnPropertyChanged(nameof(GameChatBalanceText));
        }
    }

    public string GameChatBalanceText => $"{Math.Round(GameChatBalance)} / 20";

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

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        try
        {
            await using var service = await BlackSharkHeadsetService.OpenAsync(
                _connection, cancellationToken: cancellationToken);
            var state = await service.ReadStateAsync(cancellationToken);
            ApplyState(state);
            if (state.BatteryPercent is null && state.Charging is null && state.Sidetone is null &&
                state.Thx is null && state.AncMode is null && state.GameChatBalance is null)
            {
                ErrorText = AppStrings.Text("BlackSharkReadFailed");
            }
            else
            {
                ErrorText = string.Empty;
            }

            Serial ??= await WindowsHidDiscovery.TryReadSerialNumberAsync(
                _connection.DevicePath, cancellationToken);
            OnPropertyChanged(nameof(SerialVisibility));
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

    public Task SetSidetoneAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(async (service, token) =>
        {
            await service.SetSidetoneAsync((int)Math.Round(SidetoneLevel), token);
        }, cancellationToken);

    public Task SetThxAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync((service, token) => service.SetThxAsync(ThxEnabled, token), cancellationToken);

    public Task SetAncAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(async (service, token) =>
        {
            var mode = AncModes[SelectedAncModeIndex];
            await service.SetAncAsync(mode, (int)Math.Round(AncLevel), token);
        }, cancellationToken);

    public Task SetGameChatAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(async (service, token) =>
        {
            await service.SetGameChatAsync((int)Math.Round(GameChatBalance), token);
        }, cancellationToken);

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    private async Task RunWriteAsync(
        Func<BlackSharkHeadsetService, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (!BeginOperation()) return;
        try
        {
            await using var service = await BlackSharkHeadsetService.OpenAsync(
                _connection, cancellationToken: cancellationToken);
            await write(service, cancellationToken);
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

    private void ApplyState(BlackSharkHeadsetState state)
    {
        if (state.BatteryPercent is byte battery)
        {
            _batteryPercent = battery;
            OnPropertyChanged(nameof(BatteryText));
        }

        if (state.Charging is bool charging)
        {
            _charging = charging;
            OnPropertyChanged(nameof(ChargingText));
        }

        if (state.Sidetone is byte sidetone) SidetoneLevel = sidetone;
        if (state.Thx is bool thx) ThxEnabled = thx;
        if (state.AncMode is BlackSharkAncMode mode)
        {
            SelectedAncModeIndex = Array.IndexOf(AncModes, mode);
        }
        if (state.AncLevel is int ancLevel) AncLevel = ancLevel;
        if (state.GameChatBalance is int balance) GameChatBalance = balance;
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
        if (propertyName == nameof(Serial)) OnPropertyChanged(nameof(SerialVisibility));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
