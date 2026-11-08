using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed class BlackSharkHeadsetViewModel : INotifyPropertyChanged
{
    private static readonly BlackSharkAncMode[] AncModes =
        [BlackSharkAncMode.Off, BlackSharkAncMode.Anc, BlackSharkAncMode.Ambient];

    private readonly BlackSharkHeadsetConnection _connection;
    private readonly Action<string>? _diagnostic;
    private readonly Func<ProfileDocument>? _getProfile;
    private readonly Func<CancellationToken, Task<bool>>? _saveProfile;
    private bool _isBusy;
    private string _errorText = string.Empty;
    private string _operationStatusText = string.Empty;
    private string? _serial;
    private int? _batteryPercent;
    private bool? _charging;
    private bool _hasBattery;
    private bool _hasCharging;
    private bool _hasSidetone;
    private bool _hasThx;
    private bool _hasAnc;
    private bool _hasGameChat;
    private double _sidetoneLevel;
    private bool _thxEnabled;
    private int _selectedAncModeIndex;
    private double _ancLevel = 1;
    private double _gameChatBalance = 10;

    public BlackSharkHeadsetViewModel(
        BlackSharkHeadsetConnection connection,
        Action<string>? diagnostic = null,
        Func<ProfileDocument>? getProfile = null,
        Func<CancellationToken, Task<bool>>? saveProfile = null)
    {
        _connection = connection;
        _diagnostic = diagnostic;
        _getProfile = getProfile;
        _saveProfile = saveProfile;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => _connection.Name;
    public string CategoryText => AppStrings.Text("BlackSharkHeadsetCategory");
    public string Identity => $"VID_1532 / PID_{_connection.ProductId:X4}";
    public string DevicePath => _connection.DevicePath;
    public string StatusText => AppStrings.Text("BlackSharkStatusConnected");
    public int ProtocolTotalCount => 6;
    public int ProtocolAvailableCount =>
        (_hasBattery ? 1 : 0) +
        (_hasCharging ? 1 : 0) +
        (_hasSidetone ? 1 : 0) +
        (_hasThx ? 1 : 0) +
        (_hasAnc ? 1 : 0) +
        (_hasGameChat ? 1 : 0);
    public string CapabilityText =>
        AppStrings.FormatText("ProtocolAvailableCount", ProtocolAvailableCount, ProtocolTotalCount);
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
                _connection, _diagnostic, cancellationToken);
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
            await ApplyPersistedProfileAsync(service, PowerSourceKind.Unknown, cancellationToken);
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
            OnPropertyChanged(nameof(ProtocolAvailableCount));
            OnPropertyChanged(nameof(CapabilityText));
        }
    }

    private string ProfileDeviceKey => string.IsNullOrWhiteSpace(Serial)
        ? $"1532:{_connection.ProductId:X4}"
        : Serial;

    [NotProfileScoped]
    public async Task ApplyPersistedProfileAsync(
        PowerSourceKind powerSource,
        CancellationToken cancellationToken = default)
    {
        await using var service = await BlackSharkHeadsetService.OpenAsync(
            _connection, _diagnostic, cancellationToken);
        await ApplyPersistedProfileAsync(service, powerSource, cancellationToken);
    }

    [NotProfileScoped]
    private async Task ApplyPersistedProfileAsync(
        BlackSharkHeadsetService service,
        PowerSourceKind powerSource,
        CancellationToken cancellationToken)
    {
        if (_getProfile?.Invoke() is not { } document)
        {
            return;
        }

        var descriptor = new DeviceDescriptor(
            ProfileDeviceKey,
            Name,
            0x1532,
            _connection.ProductId,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            "blackshark",
            DeviceCategory.Headset);
        var applier = new HeadsetPeripheralProfileApplier(
            resolveBlackShark: (_, _) => Task.FromResult<BlackSharkHeadsetService?>(service));
        await applier.ApplyAsync(
            new DeviceProfileApplyContext(
                document,
                descriptor,
                powerSource,
                ProfileResolver.Resolve(document, descriptor, powerSource)),
            cancellationToken);
    }

    [ProfileSetting("headset.sidetone", DeviceCategory.Headset, powerScoped: true)]
    public Task SetSidetoneAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(async (service, token) =>
        {
            var previous = await service.ReadStateAsync(token);
            var desired = (int)Math.Round(SidetoneLevel);
            await service.SetSidetoneAsync(desired, token);
            if (!await PersistHeadsetAsync(headset => headset.SideTone = desired, token))
            {
                if (previous.Sidetone is { } old)
                {
                    await service.SetSidetoneAsync(old, token);
                    SidetoneLevel = old;
                }

                throw new InvalidOperationException(
                    AppStrings.Text("RazerHidSettingProfileSaveFailed"));
            }
        }, cancellationToken);

    [ProfileSetting("headset.thx", DeviceCategory.Headset, powerScoped: true)]
    public Task SetThxAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(async (service, token) =>
        {
            var previous = await service.ReadStateAsync(token);
            var desired = ThxEnabled;
            await service.SetThxAsync(desired, token);
            if (!await PersistHeadsetAsync(headset => headset.ThxEnabled = desired, token))
            {
                if (previous.Thx is { } old)
                {
                    await service.SetThxAsync(old, token);
                    ThxEnabled = old;
                }

                throw new InvalidOperationException(
                    AppStrings.Text("RazerHidSettingProfileSaveFailed"));
            }
        }, cancellationToken);

    [ProfileSetting("headset.anc", DeviceCategory.Headset, powerScoped: true)]
    public Task SetAncAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(async (service, token) =>
        {
            var previous = await service.ReadStateAsync(token);
            var mode = AncModes[SelectedAncModeIndex];
            var level = (int)Math.Round(AncLevel);
            await service.SetAncAsync(mode, level, token);
            if (!await PersistHeadsetAsync(
                    headset =>
                    {
                        headset.AncEnabled = mode != BlackSharkAncMode.Off;
                        headset.AncModeIndex = SelectedAncModeIndex;
                        headset.AncLevel = level;
                    },
                    token))
            {
                if (previous.AncMode is { } old)
                {
                    await service.SetAncAsync(old, previous.AncLevel ?? 1, token);
                    SelectedAncModeIndex = Array.IndexOf(AncModes, old);
                    AncLevel = previous.AncLevel ?? AncLevel;
                }

                throw new InvalidOperationException(
                    AppStrings.Text("RazerHidSettingProfileSaveFailed"));
            }
        }, cancellationToken);

    [ProfileSetting("headset.game-chat", DeviceCategory.Headset, powerScoped: true)]
    public Task SetGameChatAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(async (service, token) =>
        {
            var previous = await service.ReadStateAsync(token);
            var desired = (int)Math.Round(GameChatBalance);
            await service.SetGameChatAsync(desired, token);
            if (!await PersistHeadsetAsync(headset => headset.GameChatBalance = desired, token))
            {
                if (previous.GameChatBalance is { } old)
                {
                    await service.SetGameChatAsync(old, token);
                    GameChatBalance = old;
                }

                throw new InvalidOperationException(
                    AppStrings.Text("RazerHidSettingProfileSaveFailed"));
            }
        }, cancellationToken);

    private Task<bool> PersistHeadsetAsync(
        Action<HeadsetProfileSettings> mutate,
        CancellationToken cancellationToken) =>
        HeadsetProfilePersistence.SaveAsync(
            _getProfile?.Invoke(),
            _saveProfile,
            ProfileDeviceKey,
            mutate,
            cancellationToken);

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    private async Task RunWriteAsync(
        Func<BlackSharkHeadsetService, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (!BeginOperation()) return;
        try
        {
            await using var service = await BlackSharkHeadsetService.OpenAsync(
                _connection, _diagnostic, cancellationToken);
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
            _hasBattery = true;
            OnPropertyChanged(nameof(BatteryText));
        }

        if (state.Charging is bool charging)
        {
            _charging = charging;
            _hasCharging = true;
            OnPropertyChanged(nameof(ChargingText));
        }

        if (state.Sidetone is byte sidetone) { SidetoneLevel = sidetone; _hasSidetone = true; }
        if (state.Thx is bool thx) { ThxEnabled = thx; _hasThx = true; }
        if (state.AncMode is BlackSharkAncMode mode)
        {
            SelectedAncModeIndex = Array.IndexOf(AncModes, mode);
            _hasAnc = true;
        }
        if (state.AncLevel is int ancLevel) AncLevel = ancLevel;
        if (state.GameChatBalance is int balance) { GameChatBalance = balance; _hasGameChat = true; }
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
