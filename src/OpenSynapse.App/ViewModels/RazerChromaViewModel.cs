using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class ArgbLedViewModel : INotifyPropertyChanged
{
    private Color _color;

    public ArgbLedViewModel(byte index, Color color)
    {
        Index = index;
        _color = color;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public byte Index { get; }
    public string Label => AppStrings.FormatText("RazerArgbLedLabel", Index + 1);

    public Color Color
    {
        get => _color;
        set
        {
            if (_color == value) return;
            _color = value;
            PropertyChanged?.Invoke(this, new(nameof(Color)));
            PropertyChanged?.Invoke(this, new(nameof(Brush)));
        }
    }

    public Brush Brush => new SolidColorBrush(Color);
    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}

public sealed class RazerChromaDeviceRowViewModel : INotifyPropertyChanged
{
    public RazerChromaDeviceRowViewModel(RazerChromaDeviceConnection connection)
    {
        Connection = connection;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RazerChromaDeviceConnection Connection { get; }
    public DeviceCategory HardwareCategory => Connection.Category;
    public string Name => Connection.Name;
    public string Category => Connection.Category switch
    {
        DeviceCategory.Mouse => AppStrings.Text("Text_4B32CEE8"),
        DeviceCategory.Keyboard => AppStrings.Text("Text_7D4E2D8B"),
        DeviceCategory.Monitor => AppStrings.Text("Text_2E486BCB"),
        _ => AppStrings.Text("Text_71E692AA"),
    };
    public string Identity => $"VID_1532 / PID_{Connection.ProductId:X4}";
    public string IconGlyph => Connection.Category == DeviceCategory.Mouse ? "\uE962" : "\uE7F4";
    public string Status => AppStrings.Text("BlackSharkStatusConnected");
    public Brush StatusBrush { get; } = new SolidColorBrush(Color.FromArgb(255, 153, 221, 114));
    public string Error => string.Empty;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public sealed class RazerChromaViewModel : INotifyPropertyChanged
{
    private static readonly RazerChromaEffect[] Effects =
        [RazerChromaEffect.Off, RazerChromaEffect.Static, RazerChromaEffect.Breathing, RazerChromaEffect.Spectrum];

    private const ushort ArgbProductId = 0x0F1F;
    private static readonly string[] ArgbPortNumbers = ["1", "2", "3", "4", "5", "6"];

    private readonly RazerChromaDeviceConnection _connection;
    private readonly Action<string>? _diagnostic;
    private readonly OpenRazerSpecialLightingService? _specialLighting;
    private readonly Color[][] _argbPortColors = CreateArgbBuffers();
    private readonly int[] _argbLedCounts = ArgbStripSettings.Load();
    private OpenRazerSpecialLightingConnection? _argbConnection;
    private int _selectedArgbPortIndex;
    private int _argbLedCount;
    private bool _isBusy;
    private string _errorText = string.Empty;
    private string _operationStatusText = string.Empty;
    private double _brightness = 100;
    private int _selectedEffectIndex;
    private Color _primaryColor = Color.FromArgb(255, 0, 255, 0);
    private byte? _batteryPercent;
    private bool? _isCharging;
    private Version? _firmware;
    private string? _serial;
    private Color _secondaryColor = Color.FromArgb(255, 255, 0, 0);

    public RazerChromaViewModel(
        RazerChromaDeviceConnection connection,
        Action<string>? diagnostic = null,
        OpenRazerSpecialLightingService? specialLighting = null)
    {
        _connection = connection;
        _diagnostic = diagnostic;
        _specialLighting = specialLighting;
        _argbLedCount = _argbLedCounts[0];
    }

    private static Color[][] CreateArgbBuffers()
    {
        var buffers = new Color[ArgbStripSettings.Ports][];
        for (var port = 0; port < buffers.Length; port++)
        {
            buffers[port] = new Color[ArgbStripSettings.MaxLeds];
            Array.Fill(buffers[port], Color.FromArgb(255, 0, 0, 0));
        }

        return buffers;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => _connection.Name;
    public string CategoryText => _connection.Category switch
    {
        DeviceCategory.Mouse => AppStrings.Text("Text_4B32CEE8"),
        DeviceCategory.Monitor => AppStrings.Text("Text_2E486BCB"),
        _ => AppStrings.Text("Text_71E692AA"),
    };
    public string Identity => $"VID_1532 / PID_{_connection.ProductId:X4}";
    public string DevicePath => _connection.DevicePath;
    public string StatusText => AppStrings.Text("BlackSharkStatusConnected");

    public string BatterySummary => _batteryPercent is { } percent
        ? _isCharging == true
            ? $"{percent}% · {AppStrings.Text("BlackSharkCharging")}"
            : $"{percent}%"
        : string.Empty;

    public Visibility BatteryVisibility => _batteryPercent is null ? Visibility.Collapsed : Visibility.Visible;
    public string HardwareSummary => string.Join(
        " · ",
        new[]
        {
            string.IsNullOrWhiteSpace(_serial) ? null : $"SN {_serial}",
            _firmware is null ? null : $"FW {_firmware}",
        }.Where(part => part is not null));

    public Visibility HardwareVisibility =>
        string.IsNullOrEmpty(HardwareSummary) ? Visibility.Collapsed : Visibility.Visible;
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

    public Visibility ArgbVisibility => _argbConnection is null ? Visibility.Collapsed : Visibility.Visible;
    public IReadOnlyList<string> ArgbPortOptions => ArgbPortNumbers;
    public ObservableCollection<ArgbLedViewModel> ArgbLeds { get; } = new();

    public int SelectedArgbPortIndex
    {
        get => _selectedArgbPortIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, ArgbStripSettings.Ports - 1);
            if (_selectedArgbPortIndex == clamped) return;
            _selectedArgbPortIndex = clamped;
            _argbLedCount = _argbLedCounts[clamped];
            OnPropertyChanged(nameof(ArgbLedCount));
            RebuildArgbLeds();
            OnPropertyChanged();
        }
    }

    public double ArgbLedCount
    {
        get => _argbLedCount;
        set
        {
            if (double.IsNaN(value)) return;
            var clamped = (int)Math.Clamp(Math.Round(value), 1, ArgbStripSettings.MaxLeds);
            if (_argbLedCount == clamped) return;
            _argbLedCount = clamped;
            _argbLedCounts[_selectedArgbPortIndex] = clamped;
            ArgbStripSettings.Save(_argbLedCounts);
            RebuildArgbLeds();
            OnPropertyChanged();
        }
    }

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
            var state = await OpenService().ReadStateAsync(cancellationToken);
            if (state.BrightnessPercent is byte brightness) Brightness = brightness;
            if (state.Effect is RazerChromaEffect effect)
            {
                var index = Array.IndexOf(Effects, effect);
                if (index >= 0) SelectedLightingEffectIndex = index;
                if (state.Colors.Count >= 3)
                {
                    PrimaryColor = Color.FromArgb(255, state.Colors[0], state.Colors[1], state.Colors[2]);
                }
                if (state.Colors.Count >= 6)
                {
                    SecondaryColor = Color.FromArgb(255, state.Colors[3], state.Colors[4], state.Colors[5]);
                }
            }

            _batteryPercent = state.BatteryPercent;
            _isCharging = state.IsCharging;
            OnPropertyChanged(nameof(BatterySummary));
            _firmware = state.Firmware;
            _serial = state.SerialNumber;
            OnPropertyChanged(nameof(HardwareSummary));
            OnPropertyChanged(nameof(HardwareVisibility));
            OnPropertyChanged(nameof(BatteryVisibility));

            ErrorText = state.BrightnessPercent is null && state.Effect is null
                ? AppStrings.Text("RazerHeadsetReadFailed")
                : string.Empty;
            await LoadArgbAsync(cancellationToken);
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

    public Task ApplyBrightnessAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            (service, token) => service.SetBrightnessAsync((byte)Math.Round(Brightness), token),
            cancellationToken);

    public Task ApplyEffectAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(async (service, token) =>
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

    public void PaintArgbLed(ArgbLedViewModel led)
    {
        ArgumentNullException.ThrowIfNull(led);
        if (led.Index >= _argbLedCount) return;
        _argbPortColors[_selectedArgbPortIndex][led.Index] = PrimaryColor;
        led.Color = PrimaryColor;
    }

    public void FillArgbLeds()
    {
        var port = _argbPortColors[_selectedArgbPortIndex];
        for (var index = 0; index < _argbLedCount; index++) port[index] = PrimaryColor;
        RebuildArgbLeds();
    }

    public void ClearArgbLeds()
    {
        Array.Fill(_argbPortColors[_selectedArgbPortIndex], Color.FromArgb(255, 0, 0, 0));
        RebuildArgbLeds();
    }

    public async Task ApplyArgbFrameAsync(CancellationToken cancellationToken = default)
    {
        if (_specialLighting is null || _argbConnection is not { } connection) return;
        if (!BeginOperation()) return;
        try
        {
            try
            {
                await OpenService().SetCustomFrameModeAsync(cancellationToken);
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                // The frame is the operation that matters; firmware that refuses the mode
                // echo still accepts frames, and the diagnostic log records the refusal.
                _diagnostic?.Invoke($"chroma custom frame mode: {exception.Message}");
            }

            var colors = _argbPortColors[_selectedArgbPortIndex][.._argbLedCount]
                .Select(color => new OpenRazerColor(color.R, color.G, color.B))
                .ToArray();
            await _specialLighting.SetArgbFrameAsync(
                connection,
                (byte)_selectedArgbPortIndex,
                colors,
                cancellationToken);
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

    public void RefreshLocalization()
    {
        OnPropertyChanged(string.Empty);
        foreach (var led in ArgbLeds) led.RefreshLocalization();
    }

    private async Task LoadArgbAsync(CancellationToken cancellationToken)
    {
        if (_connection.ProductId != ArgbProductId || _specialLighting is null) return;

        var connections = await _specialLighting.DiscoverAsync(cancellationToken);
        var argb = connections.FirstOrDefault(item => item.ProductId == ArgbProductId);
        _argbConnection = argb is { IsReady: true } ? argb : null;
        if (argb is { IsReady: false, Error: { } error } && string.IsNullOrEmpty(ErrorText))
        {
            ErrorText = error;
        }

        OnPropertyChanged(nameof(ArgbVisibility));
        RebuildArgbLeds();
    }

    private void RebuildArgbLeds()
    {
        ArgbLeds.Clear();
        if (_argbConnection is null) return;

        var port = _argbPortColors[_selectedArgbPortIndex];
        for (var index = 0; index < _argbLedCount; index++)
        {
            ArgbLeds.Add(new ArgbLedViewModel((byte)index, port[index]));
        }
    }

    private RazerChromaStandardService OpenService() =>
        new(new RazerFeatureTransport(), _connection.DevicePath, TimeSpan.FromMilliseconds(20), _diagnostic);

    private async Task RunWriteAsync(
        Func<RazerChromaStandardService, CancellationToken, Task> write,
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
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
