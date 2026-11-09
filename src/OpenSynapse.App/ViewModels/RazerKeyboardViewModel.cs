using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerKeyboardDeviceRowViewModel : INotifyPropertyChanged
{
    private static readonly Brush ConnectedBrush =
        new SolidColorBrush(Color.FromArgb(255, 68, 214, 44));
    private static readonly Brush PartialBrush =
        new SolidColorBrush(Color.FromArgb(255, 240, 185, 90));
    private Task? _loadTask;

    public RazerKeyboardDeviceRowViewModel(
        RazerKeyboardConnection connection,
        Action<string>? diagnostic = null,
        Func<ProfileDocument>? getProfile = null,
        Func<CancellationToken, Task<bool>>? saveProfile = null)
    {
        Connection = connection;
        Detail = new RazerKeyboardViewModel(connection, diagnostic, getProfile, saveProfile);
        Detail.PropertyChanged += DetailPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RazerKeyboardConnection Connection { get; }
    public RazerKeyboardViewModel Detail { get; }
    public DeviceCategory HardwareCategory => DeviceCategory.Keyboard;
    public string Name => Connection.Name;
    public string Category => AppStrings.Text("RazerKeyboardCategory");
    public string Identity => $"VID_1532 / PID_{Connection.DisplayProductId:X4}";
    public string IconGlyph => "\uE765";
    public string Status => AppStrings.Text("BlackSharkStatusConnected");
    public string Capability => Detail.CapabilityText;
    public Brush StatusBrush => !Detail.HasCapabilityProbe ||
        Detail.ProtocolAvailableCount == Detail.ProtocolTotalCount
            ? ConnectedBrush
            : PartialBrush;
    public string Error => string.Empty;

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        _loadTask ??= Detail.LoadAsync(cancellationToken);

    private void DetailPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(RazerKeyboardViewModel.CapabilityText) or
            nameof(RazerKeyboardViewModel.ProtocolAvailableCount) or
            nameof(RazerKeyboardViewModel.HasCapabilityProbe))
        {
            PropertyChanged?.Invoke(this, new(nameof(Capability)));
            PropertyChanged?.Invoke(this, new(nameof(StatusBrush)));
        }
    }

    public void RefreshLocalization()
    {
        Detail.RefreshLocalization();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

public sealed partial class RazerKeyboardViewModel : INotifyPropertyChanged
{
    private static readonly int[] PollingRates = [8_000, 4_000, 2_000, 1_000, 500, 250, 125];

    private readonly RazerKeyboardConnection _connection;
    private readonly Action<string>? _diagnostic;
    private readonly Func<ProfileDocument>? _getProfile;
    private readonly Func<CancellationToken, Task<bool>>? _saveProfile;
    private bool _isBusy;
    private string _errorText = string.Empty;
    private string _operationStatusText = string.Empty;
    private string _firmwareText = "--";
    private string _serialText = "--";
    private string _batteryText = "--";
    private string _chargingText = "--";
    private bool _gameModeEnabled;
    private int _selectedPollingIndex;
    private bool _appliedGameMode;
    private int _appliedPollingIndex;
    private bool _hasGameMode;
    private bool _hasLighting;
    private bool _hasBrightness;
    private double _brightness = 100;
    private int _selectedEffectIndex;
    private Color _primaryColor = Color.FromArgb(255, 0, 255, 0);
    private Color _secondaryColor = Color.FromArgb(255, 255, 0, 0);
    private bool _hasPolling;
    private bool _hasBattery;
    private bool _hasFirmware;
    private bool _hasSerial;
    private bool _hasMapping;
    private bool _capabilityProbed;
    private RazerKeyboardService? _mappingService;
    private RazerHidMappingViewModel? _mapping;
    private Microsoft.UI.Xaml.Visibility _mappingVisibility = Microsoft.UI.Xaml.Visibility.Collapsed;

    public Func<string, OpenSynapse.Core.Macros.MacroInput,
        OpenSynapse.Core.Macros.MacroTriggerMode, bool, CancellationToken, Task<string?>>?
        SoftwareMacroBinder { get; set; }

    public RazerKeyboardViewModel(
        RazerKeyboardConnection connection,
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
    public string CategoryText => AppStrings.Text("RazerKeyboardCategory");
    public string Identity => $"VID_1532 / PID_{_connection.DisplayProductId:X4}";
    public string DevicePath => _connection.DevicePath;
    public string StatusText => AppStrings.Text("BlackSharkStatusConnected");
    public bool HasCapabilityProbe => _capabilityProbed;
    // Firmware, serial, game mode, polling, lighting, brightness and mapping.
    public int ProtocolTotalCount => 7;
    public int ProtocolAvailableCount =>
        (_hasFirmware ? 1 : 0) +
        (_hasSerial ? 1 : 0) +
        (_hasGameMode ? 1 : 0) +
        (_hasPolling ? 1 : 0) +
        (_hasLighting ? 1 : 0) +
        (_hasBrightness ? 1 : 0) +
        (_hasMapping ? 1 : 0);
    public string CapabilityText => !_capabilityProbed
        ? AppStrings.Text("BlackSharkStatusConnected")
        : AppStrings.FormatText("ProtocolAvailableCount", ProtocolAvailableCount, ProtocolTotalCount);

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

    private static readonly RazerChromaEffect[] Effects =
        [RazerChromaEffect.Off, RazerChromaEffect.Static, RazerChromaEffect.Breathing, RazerChromaEffect.Spectrum];

    public IReadOnlyList<string> LightingEffectOptions =>
    [
        AppStrings.Text("RazerLightingEffectOff"),
        AppStrings.Text("RazerLightingEffectStatic"),
        AppStrings.Text("RazerLightingEffectBreathing"),
        AppStrings.Text("RazerLightingEffectSpectrum"),
    ];

    public int SelectedLightingEffectIndex
    {
        get => _selectedEffectIndex;
        set
        {
            if (!SetField(ref _selectedEffectIndex, Math.Clamp(value, 0, Effects.Length - 1))) return;
            OnPropertyChanged(nameof(SecondaryColorVisibility));
        }
    }

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (!SetField(ref _brightness, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(BrightnessText));
        }
    }

    public string BrightnessText => $"{Math.Round(Brightness)}%";

    public Color PrimaryColor
    {
        get => _primaryColor;
        set => SetField(ref _primaryColor, value);
    }

    public Color SecondaryColor
    {
        get => _secondaryColor;
        set => SetField(ref _secondaryColor, value);
    }

    public Microsoft.UI.Xaml.Visibility SecondaryColorVisibility =>
        Effects[_selectedEffectIndex] == RazerChromaEffect.Breathing
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility LightingVisibility =>
        _hasLighting ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility BrightnessVisibility =>
        _hasBrightness ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
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
    public Microsoft.UI.Xaml.Visibility MappingVisibility
    {
        get => _mappingVisibility;
        private set
        {
            if (_mappingVisibility == value) return;
            _mappingVisibility = value;
            OnPropertyChanged();
        }
    }

    public RazerHidMappingViewModel Mapping =>
        _mapping ??= new RazerHidMappingViewModel(
            new RazerKeyboardMappingTransport(MappingService),
            canWrite: true,
            macroServiceFactory: CreateService,
            localMacros: () => Onboard.LocalMacroProvider?.Invoke() ?? [],
            actionCatalog: RazerKeyboardMappingActionCatalog.Instance,
            mappingProfileSaver: PersistMappingProfileAsync,
            productId: _connection.DisplayProductId,
            deviceKind: RazerMappingDeviceKind.Keyboard,
            canWriteOnboardMacros:
                RazerOnboardMacroDevices.Keyboards.Contains(_connection.ProductId),
            softwareTriggerResolver: buttonId =>
                RazerKeyboardMacroTriggerResolver.Resolve(buttonId, _connection.ProductId),
            softwareTriggerBinder: SoftwareMacroBinder);

    private RazerKeyboardService MappingService => _mappingService ??= OpenService();

    private string ProfileDeviceKey =>
        $"{0x1532:X4}:{RazerProductAliases.Canonical(_connection.ProductId):X4}";

    private async Task PersistMappingProfileAsync(
        IReadOnlyList<ButtonMappingProfile> mappings,
        CancellationToken cancellationToken)
    {
        if (mappings.Count == 0 || _getProfile is null || _saveProfile is null)
        {
            return;
        }

        var document = _getProfile();
        var created = !document.Devices.TryGetValue(ProfileDeviceKey, out var device);
        device ??= new DeviceProfileSettings();
        var previous = device.Keyboard.ButtonMappings;
        device.Keyboard.ButtonMappings = RazerHidMappingActions.MergeButtonMappings(
            previous, mappings, mappings[0].Mode ?? 0);
        if (created)
        {
            document.Devices[ProfileDeviceKey] = device;
        }
        if (await _saveProfile(cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        device.Keyboard.ButtonMappings = previous;
        if (created)
        {
            document.Devices.Remove(ProfileDeviceKey);
        }
        throw new IOException("Keyboard mapping could not be saved to the profile.");
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        try
        {
            var state = await OpenService().ReadStateAsync(cancellationToken);
            ApplyState(state);
            var lighting = await OpenChromaService().ReadStateAsync(cancellationToken);
            // Legacy keyboards expose the effect commands even when this firmware will not
            // answer the read, so the card stays available and starts from the defaults.
            if (_connection.UsesLegacyChroma)
            {
                _hasLighting = true;
            }

            ApplyLighting(lighting);
            await RefreshMappingAvailabilityAsync(cancellationToken).ConfigureAwait(true);
            if (MappingVisibility == Microsoft.UI.Xaml.Visibility.Visible && !Mapping.IsLoaded)
            {
                await Mapping.ReadAsync(cancellationToken).ConfigureAwait(true);
            }
            // Keep the transport's own reason in the report: "did not respond" alone hides
            // whether the device rejected, timed out or answered a different transaction.
            ErrorText = state.Firmware is null && state.Serial is null && state.BatteryPercent is null &&
                state.GameModeEnabled is null && state.PollingRate is null
                    ? state.Errors.Count > 0
                        ? $"{AppStrings.Text("RazerHeadsetReadFailed")} {state.Errors[0]}"
                        : AppStrings.Text("RazerHeadsetReadFailed")
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
            _capabilityProbed = true;
            OnPropertyChanged(nameof(HasCapabilityProbe));
            OnPropertyChanged(nameof(ProtocolAvailableCount));
            OnPropertyChanged(nameof(CapabilityText));
        }

        await ApplyPersistedProfileAsync(PowerSourceKind.Unknown, cancellationToken);
    }

    [ProfileSetting("keyboard.game-mode", DeviceCategory.Keyboard)]
    public async Task ApplyGameModeAsync(CancellationToken cancellationToken = default)
    {
        var previous = _appliedGameMode;
        if (!await RunWriteResultAsync(
                (service, token) => service.SetGameModeAsync(GameModeEnabled, token), cancellationToken))
        {
            return;
        }

        _appliedGameMode = GameModeEnabled;
        await SaveKeyboardSettingAsync(
            settings => settings.GamingModeEnabled = GameModeEnabled,
            settings => settings.GamingModeEnabled = previous,
            () =>
            {
                GameModeEnabled = previous;
                _appliedGameMode = previous;
            },
            token => RunWriteResultAsync(
                (service, rollbackToken) => service.SetGameModeAsync(previous, rollbackToken), token),
            cancellationToken);
    }

    public Task ApplyBrightnessAsync(CancellationToken cancellationToken = default) =>
        RunChromaWriteAsync(
            (service, token) => service.SetBrightnessAsync((byte)Math.Round(Brightness), token),
            cancellationToken);

    public Task ApplyLightingAsync(CancellationToken cancellationToken = default) =>
        RunChromaWriteAsync(async (service, token) =>
        {
            var effect = Effects[SelectedLightingEffectIndex];
            var colors = effect switch
            {
                RazerChromaEffect.Static => new byte[] { PrimaryColor.R, PrimaryColor.G, PrimaryColor.B },
                RazerChromaEffect.Breathing =>
                    [PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, SecondaryColor.R, SecondaryColor.G, SecondaryColor.B],
                _ => [],
            };
            await service.SetEffectAsync(effect, colors, token);
        }, cancellationToken);

    [ProfileSetting("keyboard.polling-rate", DeviceCategory.Keyboard)]
    public async Task ApplyPollingAsync(CancellationToken cancellationToken = default)
    {
        var previous = _appliedPollingIndex;
        var index = SelectedPollingIndex;
        var rate = PollingRates[index];
        if (!await RunWriteResultAsync(
                (service, token) => service.SetPollingRateAsync(rate, token), cancellationToken))
        {
            return;
        }

        _appliedPollingIndex = index;
        await SaveKeyboardSettingAsync(
            settings => settings.PollingRateHertz = rate,
            settings => settings.PollingRateHertz = PollingRates[previous],
            () =>
            {
                SelectedPollingIndex = previous;
                _appliedPollingIndex = previous;
            },
            token => RunWriteResultAsync(
                (service, rollbackToken) => service.SetPollingRateAsync(PollingRates[previous], rollbackToken),
                token),
            cancellationToken);
    }

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    private RazerChromaStandardService OpenChromaService() =>
        new(
            new RazerFeatureTransport(),
            _connection.DevicePath,
            TimeSpan.FromMilliseconds(20),
            _diagnostic,
            _connection.UsesLegacyChroma ? RazerChromaChannel.Legacy : RazerChromaChannel.Standard,
            _connection.ProductId);

    private async Task RunChromaWriteAsync(
        Func<RazerChromaStandardService, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (!BeginOperation()) return;
        try
        {
            await write(OpenChromaService(), cancellationToken);
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

    private void ApplyLighting(RazerChromaStandardState state)
    {
        if (state.BrightnessPercent is byte brightness)
        {
            Brightness = brightness;
            _hasLighting = true;
            _hasBrightness = true;
            OnPropertyChanged(nameof(BrightnessVisibility));
        }

        if (state.Effect is RazerChromaEffect effect)
        {
            var index = Array.IndexOf(Effects, effect);
            if (index >= 0)
            {
                SelectedLightingEffectIndex = index;
            }

            _hasLighting = true;
            if (state.Colors.Count >= 3)
            {
                PrimaryColor = Color.FromArgb(255, state.Colors[0], state.Colors[1], state.Colors[2]);
            }
            if (state.Colors.Count >= 6)
            {
                SecondaryColor = Color.FromArgb(255, state.Colors[3], state.Colors[4], state.Colors[5]);
            }
        }

        OnPropertyChanged(nameof(LightingVisibility));
    }

    private async Task RefreshMappingAvailabilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            var ids = await MappingService.GetMappingButtonIdsAsync(cancellationToken).ConfigureAwait(true);
            _hasMapping = ids.Any(id => id != 0);
            MappingVisibility = _hasMapping
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _diagnostic?.Invoke($"keyboard mapping unavailable: {exception.Message}");
            MappingVisibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }

    private RazerKeyboardService OpenService() =>
        new(new RazerFeatureTransport(), _connection.DevicePath, TimeSpan.FromMilliseconds(20), _diagnostic);

    private async Task RunWriteAsync(
        Func<RazerKeyboardService, CancellationToken, Task> write,
        CancellationToken cancellationToken) =>
        await RunWriteResultAsync(write, cancellationToken);

    private async Task<bool> RunWriteResultAsync(
        Func<RazerKeyboardService, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (!BeginOperation()) return false;
        try
        {
            await write(OpenService(), cancellationToken);
            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
            return true;
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
        return false;
    }

    private void ApplyState(RazerKeyboardState state)
    {
        FirmwareText = state.Firmware?.ToString() ?? "--";
        _hasFirmware = state.Firmware is not null;
        SerialText = string.IsNullOrWhiteSpace(state.Serial) ? "--" : state.Serial;
        _hasSerial = !string.IsNullOrWhiteSpace(state.Serial);
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
            _appliedGameMode = gameMode;
            _hasGameMode = true;
            OnPropertyChanged(nameof(GameModeVisibility));
        }

        if (state.PollingRate is int polling)
        {
            var index = Array.IndexOf(PollingRates, polling);
            if (index >= 0)
            {
                SelectedPollingIndex = index;
                _appliedPollingIndex = index;
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

