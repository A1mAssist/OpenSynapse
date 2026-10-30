using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Razer camera page. Values are read from the matching RzNative DLL; brightness, contrast and
/// saturation have no bundle-proven numeric range, so their writes stay in Razer's
/// native settings dialog. White balance has an explicit auto flag and is applied here.
/// </summary>
public sealed class RazerKiyoViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Func<CancellationToken, Task<RazerKiyoService?>> _connect;
    private RazerKiyoService? _service;
    private RazerKiyoValue? _brightness;
    private RazerKiyoValue? _contrast;
    private RazerKiyoValue? _saturation;
    private RazerKiyoValue? _whiteBalance;
    private bool _isBusy;
    private bool _whiteBalanceAuto;
    private string _firmwareText = "--";
    private string _serialText = "--";
    private string _statusText = string.Empty;
    private string _errorText = string.Empty;

    public RazerKiyoViewModel(Action<string>? diagnostic = null)
        : this(cancellationToken => RazerKiyoService.ConnectAsync(diagnostic, cancellationToken))
    {
    }

    internal RazerKiyoViewModel(Func<CancellationToken, Task<RazerKiyoService?>> connect)
    {
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsConnected => _service is not null;
    public string Name => _service?.Name ?? RazerKiyoService.DeviceName;
    public string CategoryText => AppStrings.Text("KiyoCategory");
    public string Identity => _service?.Identity ?? $"VID_1532 / PID_{RazerKiyoDevices.DefaultProductId:X4}";
    public string IconGlyph => "\uE722";
    public DeviceCategory HardwareCategory => DeviceCategory.Accessory;
    public string StatusText => !string.IsNullOrWhiteSpace(_statusText)
        ? _statusText
        : AppStrings.Text(
            _service is { IsAvailable: true } ? "KiyoStatusConnected" : "KiyoStatusNotConnected");
    public string FirmwareText => _firmwareText;
    public string SerialText => _serialText;
    public string BrightnessText => FormatValue(_brightness);
    public string ContrastText => FormatValue(_contrast);
    public string SaturationText => FormatValue(_saturation);
    public string WhiteBalanceText => _whiteBalance is { } value
        ? $"{value.Value.ToString(CultureInfo.InvariantCulture)}" +
          (value.IsAuto ? $" ({AppStrings.Text("KiyoAuto")})" : string.Empty)
        : "--";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanApplyWhiteBalance));
        }
    }

    public bool CanEdit => _service is { IsAvailable: true } && !IsBusy;
    public bool CanApplyWhiteBalance =>
        CanEdit && _whiteBalance is not null &&
        (_service?.Api.HasFlag(RazerKiyoApi.WriteWhiteBalance) ?? false);
    public bool IsWhiteBalanceAuto
    {
        get => _whiteBalanceAuto;
        set => SetField(ref _whiteBalanceAuto, value);
    }

    public string ErrorText => _errorText;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public Task RefreshAsync(CancellationToken cancellationToken = default) => LoadAsync(cancellationToken);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (_service is { IsAvailable: false })
            {
                _service.Dispose();
                _service = null;
            }

            _service ??= await _connect(cancellationToken).ConfigureAwait(false);
            if (_service is null)
            {
                return;
            }

            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Identity));
            var snapshot = await Task.Run(_service.ReadSnapshot, cancellationToken).ConfigureAwait(false);
            Apply(snapshot);
            SetError(string.Empty);
            _statusText = AppStrings.Text("KiyoStatusRead");
            OnPropertyChanged(nameof(StatusText));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            // Keep the service so a transient read failure does not remove the device page.
            SetError(AppStrings.FormatText("KiyoErrorRead", exception.Message));
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanApplyWhiteBalance));
        }
    }

    public async Task ApplyWhiteBalanceAsync(CancellationToken cancellationToken = default)
    {
        var service = _service;
        if (service is null || IsBusy || _whiteBalance is null ||
            !service.Api.HasFlag(RazerKiyoApi.WriteWhiteBalance))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var updated = await Task.Run(
                    () => service.SetWhiteBalanceAuto(_whiteBalanceAuto),
                    cancellationToken)
                .ConfigureAwait(false);
            _whiteBalance = updated;
            _whiteBalanceAuto = updated.IsAuto;
            OnPropertyChanged(nameof(WhiteBalanceText));
            OnPropertyChanged(nameof(IsWhiteBalanceAuto));
            _statusText = AppStrings.Text("KiyoWhiteBalanceApplied");
            OnPropertyChanged(nameof(StatusText));
            SetError(string.Empty);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetError(AppStrings.FormatText("KiyoWhiteBalanceError", exception.Message));
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanApplyWhiteBalance));
        }
    }

    public async Task OpenSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_service is not { IsAvailable: true } || IsBusy)
        {
            return;
        }

        try
        {
            var opened = await Task.Run(_service.OpenSettings, cancellationToken).ConfigureAwait(false);
            if (!opened)
            {
                SetError(AppStrings.Text("KiyoSettingsError"));
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetError(AppStrings.FormatText("KiyoSettingsErrorText", exception.Message));
        }
    }

    public void RefreshLocalization() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public void Dispose()
    {
        _service?.Dispose();
        _service = null;
    }

    private void Apply(RazerKiyoSnapshot snapshot)
    {
        _brightness = snapshot.Brightness;
        _contrast = snapshot.Contrast;
        _saturation = snapshot.Saturation;
        _whiteBalance = snapshot.WhiteBalance;
        _whiteBalanceAuto = snapshot.WhiteBalance?.IsAuto ?? false;
        _firmwareText = string.IsNullOrWhiteSpace(snapshot.FirmwareVersion) ? "--" : snapshot.FirmwareVersion;
        _serialText = string.IsNullOrWhiteSpace(snapshot.SerialNumber) ? "--" : snapshot.SerialNumber;
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Identity));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(FirmwareText));
        OnPropertyChanged(nameof(SerialText));
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(ContrastText));
        OnPropertyChanged(nameof(SaturationText));
        OnPropertyChanged(nameof(WhiteBalanceText));
        OnPropertyChanged(nameof(IsWhiteBalanceAuto));
        OnPropertyChanged(nameof(CanApplyWhiteBalance));
    }

    private static string FormatValue(RazerKiyoValue? value) =>
        value is { } reading ? reading.Value.ToString(CultureInfo.InvariantCulture) : "--";

    private void SetError(string error)
    {
        _errorText = error;
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasError));
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or UnauthorizedAccessException or ArgumentException or
            BadImageFormatException or DllNotFoundException or EntryPointNotFoundException or Win32Exception;

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
        PropertyChanged?.Invoke(this, new(propertyName));
}
