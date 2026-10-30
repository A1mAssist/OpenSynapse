using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// The standalone cooling-pad page. Loading only reads the current speed; the target
/// is written only when the user presses Apply, and the service verifies the read-back.
/// </summary>
public sealed partial class RazerCoolingPadViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Func<CancellationToken, Task<RazerCoolingPadService?>> _connect;
    private readonly IPerformanceMonitor? _performanceMonitor;
    private RazerCoolingPadService? _service;
    private bool _isConnected;
    private bool _isBusy;
    private bool _hasInitialTarget;
    private double _targetRpm = RazerCoolingPadProtocol.MinimumRpm;
    private string _currentRpmText = "--";
    private string _operationStatusText = string.Empty;
    private string _errorText = string.Empty;

    public RazerCoolingPadViewModel(
        Action<string>? diagnostic = null,
        IPerformanceMonitor? performanceMonitor = null)
        : this(cancellationToken => RazerCoolingPadService.ConnectAsync(diagnostic, cancellationToken), performanceMonitor)
    {
    }

    internal RazerCoolingPadViewModel(
        Func<CancellationToken, Task<RazerCoolingPadService?>> connect,
        IPerformanceMonitor? performanceMonitor = null)
    {
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
        _performanceMonitor = performanceMonitor;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name => RazerCoolingPadService.DeviceName;
    public string CategoryText => AppStrings.Text("CoolingPadCategory");
    public string Identity => _service is null ? string.Empty : $"VID_1532 / PID_{_service.ProductId:X4}";
    public string IconGlyph => "\uE7F8";
    public string StatusText => AppStrings.Text(IsConnected ? "CoolingPadStatusConnected" : "CoolingPadStatusNotConnected");
    public Brush StatusBrush { get; } = new SolidColorBrush(Color.FromArgb(255, 153, 221, 114));
    public string CapabilityText => IsConnected ? AppStrings.Text("CoolingPadCategory") : string.Empty;
    public string ErrorText => _errorText;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool IsConnected => _isConnected;
    public int MinimumRpm => RazerCoolingPadProtocol.MinimumRpm;
    public int MaximumRpm => RazerCoolingPadProtocol.MaximumRpm;
    public int RpmStep => RazerCoolingPadProtocol.RpmPerUnit;
    public string CurrentRpmText => _currentRpmText;
    public string OperationStatusText => _operationStatusText;

    public double TargetRpm
    {
        get => _targetRpm;
        set
        {
            if (SetField(ref _targetRpm, NormalizeTarget((int)Math.Round(value))))
            {
                OnPropertyChanged(nameof(CanApply));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(CanRefresh));
            OnPropertyChanged(nameof(CanApplyLighting));
            OnPropertyChanged(nameof(CanApplyFixedMode));
            OnPropertyChanged(nameof(CanApplyCurve));
        }
    }

    public bool CanApply => IsConnected && !IsBusy;
    public bool CanRefresh => IsConnected && !IsBusy;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (_service is null)
            {
                await ConnectAndReadAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                // A cached path can be stale after a replug; re-discover once before
                // reporting the read failure.
                await ConnectAndReadAsync(cancellationToken, exception.Message).ConfigureAwait(false);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConnectAndReadAsync(
        CancellationToken cancellationToken,
        string? previousError = null)
    {
        RazerCoolingPadService? service;
        string? connectionError = previousError;
        try
        {
            service = await _connect(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            service = null;
            connectionError = exception.Message;
        }

        if (service is null)
        {
            _service = null;
            _hasInitialTarget = false;
            SetConnected(false);
            _currentRpmText = "--";
            _operationStatusText = string.Empty;
            SetError(connectionError is null
                ? string.Empty
                : AppStrings.FormatText("CoolingPadErrorRead", connectionError));
            OnPropertyChanged(nameof(CurrentRpmText));
            OnPropertyChanged(nameof(OperationStatusText));
            return;
        }

        _service = service;
        SetConnected(true);
        try
        {
            await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetError(AppStrings.FormatText("CoolingPadErrorRead", exception.Message));
        }
    }

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApply || _service is null)
        {
            return;
        }

        StopCurveAndSelectManual();
        IsBusy = true;
        try
        {
            var target = (int)Math.Round(TargetRpm);
            var actual = await _service.SetSpeedRpmAsync(target, cancellationToken).ConfigureAwait(false);
            _currentRpmText = $"{actual} RPM";
            _operationStatusText = AppStrings.FormatText("CoolingPadStatusApplied", target, actual);
            SetError(string.Empty);
            OnPropertyChanged(nameof(CurrentRpmText));
            OnPropertyChanged(nameof(OperationStatusText));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            SetError(AppStrings.FormatText("CoolingPadErrorApply", exception.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    public void Dispose() => StopCurveTimer();

    private async Task ReadCurrentAsync(CancellationToken cancellationToken)
    {
        var service = _service ?? throw new InvalidOperationException("The cooling pad is not connected.");
        var rpm = await service.ReadSpeedRpmAsync(cancellationToken).ConfigureAwait(false);
        await LoadLightingCoreAsync(cancellationToken).ConfigureAwait(false);
        await LoadFixedModeCoreAsync(cancellationToken).ConfigureAwait(false);
        _currentRpmText = $"{rpm} RPM";
        _operationStatusText = AppStrings.FormatText("CoolingPadStatusRead", rpm);
        if (!_hasInitialTarget)
        {
            _targetRpm = NormalizeTarget(rpm);
            _hasInitialTarget = true;
            OnPropertyChanged(nameof(TargetRpm));
        }

        SetError(string.Empty);
        OnPropertyChanged(nameof(CurrentRpmText));
        OnPropertyChanged(nameof(OperationStatusText));
    }

    private void SetConnected(bool connected)
    {
        if (!SetField(ref _isConnected, connected, nameof(IsConnected)))
        {
            return;
        }

        if (!connected)
        {
            StopCurveTimer();
        }

        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Identity));
        OnPropertyChanged(nameof(CapabilityText));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanApplyLighting));
        OnPropertyChanged(nameof(CanApplyFixedMode));
        OnPropertyChanged(nameof(CanApplyCurve));
        OnPropertyChanged(nameof(CurveVisibility));
    }

    private void SetError(string error)
    {
        if (StringComparer.Ordinal.Equals(_errorText, error))
        {
            return;
        }

        _errorText = error;
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasError));
    }

    private static int NormalizeTarget(int rpm)
    {
        var clamped = Math.Clamp(
            rpm,
            RazerCoolingPadProtocol.MinimumRpm,
            RazerCoolingPadProtocol.MaximumRpm);
        return (int)Math.Round(
            clamped / (double)RazerCoolingPadProtocol.RpmPerUnit,
            MidpointRounding.AwayFromZero) * RazerCoolingPadProtocol.RpmPerUnit;
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException or ArgumentException;

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
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
