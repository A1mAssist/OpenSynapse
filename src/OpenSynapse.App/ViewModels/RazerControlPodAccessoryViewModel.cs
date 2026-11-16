using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// OLED settings that are safe to expose on the Wireless Control Pod. Brightness,
/// roller direction and the low-battery warning each use one-byte class 0x17 commands.
/// </summary>
public sealed class RazerControlPodAccessoryViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ushort _productId;
    private readonly Action<string>? _diagnostic;
    private RazerControlPodAccessoryService? _service;
    private bool _isConnected;
    private bool _isBusy;
    private bool _hasBrightness;
    private bool _hasDirection;
    private bool _hasLowBattery;
    private double _brightness;
    private int _rollerDirectionIndex;
    private double _lowBatteryPercent;
    private string _statusText = string.Empty;
    private string _errorText = string.Empty;

    internal RazerControlPodAccessoryViewModel(ushort productId, Action<string>? diagnostic = null)
    {
        _productId = productId;
        _diagnostic = diagnostic;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Title => "Razer Wireless Control Pod";
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanEditBrightness));
            OnPropertyChanged(nameof(CanEditDirection));
            OnPropertyChanged(nameof(CanEditLowBattery));
        }
    }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (!SetField(ref _errorText, value)) return;
            OnPropertyChanged(nameof(HasError));
        }
    }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool CanEdit => _isConnected && !IsBusy;
    public bool CanEditBrightness => CanEdit && _hasBrightness;
    public bool CanEditDirection => CanEdit && _hasDirection;
    public bool CanEditLowBattery => CanEdit && _hasLowBattery;
    public double Brightness
    {
        get => _brightness;
        set => SetField(ref _brightness, Math.Clamp(value, 0, 100));
    }
    public int RollerDirectionIndex
    {
        get => _rollerDirectionIndex;
        set => SetField(ref _rollerDirectionIndex, Math.Clamp(value, 0, 1));
    }
    public double LowBatteryPercent
    {
        get => _lowBatteryPercent;
        set => SetField(ref _lowBatteryPercent, Math.Clamp(value, 0, 100));
    }
    public IReadOnlyList<string> DirectionOptions { get; } =
    [
        AppStrings.Text("AccessoryControlPodDirectionNormal"),
        AppStrings.Text("AccessoryControlPodDirectionInverted"),
    ];

    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _service ??= await RazerControlPodAccessoryService.ConnectAsync(
                _productId, _diagnostic, cancellationToken).ConfigureAwait(false);
            if (_service is null)
            {
                _isConnected = false;
                ErrorText = $"No 91-byte Control Pod channel found for {_productId:X4}.";
                StatusText = string.Empty;
                return;
            }

            var state = await _service.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            _hasBrightness = state.Brightness is >= 0 and <= 100;
            _hasDirection = state.RollerDirection is 0 or 1;
            _hasLowBattery = state.LowBatteryPercent is >= 0 and <= 100;
            if (_hasBrightness) _brightness = state.Brightness!.Value;
            if (_hasDirection) _rollerDirectionIndex = state.RollerDirection!.Value;
            if (_hasLowBattery) _lowBatteryPercent = state.LowBatteryPercent!.Value;
            _isConnected = _hasBrightness || _hasDirection || _hasLowBattery;
            ErrorText = state.Error ?? string.Empty;
            StatusText = _isConnected ? AppStrings.Text("CoolingPadStatusConnected") : string.Empty;
            OnPropertyChanged(string.Empty);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _isConnected = false;
            ErrorText = exception.Message;
            StatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [NotProfileScoped]
    internal async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEdit || _service is null) return;
        IsBusy = true;
        try
        {
            if (_hasBrightness)
            {
                await _service.SetBrightnessAsync((int)Math.Round(Brightness), cancellationToken)
                    .ConfigureAwait(false);
            }
            if (_hasDirection)
            {
                await _service.SetRollerDirectionAsync(RollerDirectionIndex, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (_hasLowBattery)
            {
                await _service.SetLowBatteryPercentAsync(
                    (int)Math.Round(LowBatteryPercent), cancellationToken).ConfigureAwait(false);
            }
            StatusText = AppStrings.Text("BlackSharkSaved");
            ErrorText = string.Empty;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            ErrorText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_service is { } service) await service.DisposeAsync().ConfigureAwait(false);
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or ArgumentException;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
