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
    private bool _hasLighting;
    private bool _hasBrightness;
    private double _brightness = 100;
    private int _selectedEffectIndex;
    private Color _primaryColor = Color.FromArgb(255, 0, 255, 0);
    private Color _secondaryColor = Color.FromArgb(255, 255, 0, 0);
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

    public Task ApplyPollingAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            (service, token) => service.SetPollingRateAsync(PollingRates[SelectedPollingIndex], token),
            cancellationToken);

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    private RazerChromaStandardService OpenChromaService() =>
        new(
            new RazerFeatureTransport(),
            _connection.DevicePath,
            TimeSpan.FromMilliseconds(20),
            _diagnostic,
            _connection.UsesLegacyChroma ? RazerChromaChannel.Legacy : RazerChromaChannel.Standard);

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
