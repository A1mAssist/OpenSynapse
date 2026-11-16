using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Sensa HD body page shared by Freyja and Soma Sensa HD Haptics.
/// </summary>
public sealed class RazerHapticsAccessoryViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ushort _productId;
    private readonly Action<string>? _diagnostic;
    private RazerHapticsAccessoryService? _service;
    private bool _isConnected;
    private bool _isBusy;
    private bool _enabled;
    private double _intensity;
    private double _gain;
    private double _perception;
    private double _transient;
    private double _signalGain;
    private string _statusText = string.Empty;
    private string _errorText = string.Empty;

    internal RazerHapticsAccessoryViewModel(ushort productId, Action<string>? diagnostic = null)
    {
        _productId = productId;
        _diagnostic = diagnostic;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsConnected { get => _isConnected; private set => SetField(ref _isConnected, value); }
    public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }
    public bool Enabled { get => _enabled; set => SetField(ref _enabled, value); }
    public double Intensity { get => _intensity; set => SetField(ref _intensity, Math.Clamp(value, 0, 100)); }
    public double Gain { get => _gain; set => SetField(ref _gain, Math.Clamp(value, 0, 100)); }
    public double Perception { get => _perception; set => SetField(ref _perception, Math.Clamp(value, 0, 100)); }
    public double Transient { get => _transient; set => SetField(ref _transient, Math.Clamp(value, 0, 100)); }
    public double SignalGain { get => _signalGain; set => SetField(ref _signalGain, Math.Clamp(value, 0, 100)); }
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
    public bool CanEdit => IsConnected && !IsBusy;

    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _service ??= await RazerHapticsAccessoryService.ConnectAsync(
                _productId, _diagnostic, cancellationToken).ConfigureAwait(false);
            if (_service is null)
            {
                IsConnected = false;
                ErrorText = $"No 91-byte haptics channel found for {_productId:X4}.";
                return;
            }

            var state = await _service.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            _enabled = state.Mode != 0 || state.IntensityEnabled;
            _intensity = state.Intensity;
            _gain = state.Gain;
            _perception = state.Perception;
            _transient = state.Transient;
            _signalGain = state.SignalGain;
            OnPropertyChanged(string.Empty);
            IsConnected = true;
            ErrorText = state.Error ?? string.Empty;
            StatusText = state.Mode == 0
                ? AppStrings.Text("CoolingPadModeOff")
                : AppStrings.Text("CoolingPadStatusConnected");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            IsConnected = false;
            ErrorText = exception.Message;
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
            await _service.SetModeAsync(Enabled ? 1 : 0, cancellationToken).ConfigureAwait(false);
            await _service.SetIntensityAsync(Enabled, (int)Math.Round(Intensity), cancellationToken)
                .ConfigureAwait(false);
            await _service.SetGainAsync((int)Math.Round(Gain), cancellationToken).ConfigureAwait(false);
            await _service.SetPerceptionAsync((int)Math.Round(Perception), cancellationToken)
                .ConfigureAwait(false);
            await _service.SetTransientAsync((int)Math.Round(Transient), cancellationToken)
                .ConfigureAwait(false);
            await _service.SetSignalGainAsync((int)Math.Round(SignalGain), cancellationToken)
                .ConfigureAwait(false);
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
        PropertyChanged?.Invoke(this, new(propertyName));
        return true;
    }
}
