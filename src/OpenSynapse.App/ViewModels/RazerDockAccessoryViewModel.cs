using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Mouse Dock V2 Pro body page. The dock sync and charging-effect switches are only
/// exposed after their read commands answer, so an unknown LED id never gets a write.
/// </summary>
public sealed class RazerDockAccessoryViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ushort _productId;
    private readonly Action<string>? _diagnostic;
    private RazerDockAccessoryService? _service;
    private bool _isConnected;
    private bool _isBusy;
    private bool _hasDockSync;
    private bool _hasChargingEffect;
    private bool _dockSync;
    private bool _chargingEffect;
    private string _statusText = string.Empty;
    private string _errorText = string.Empty;

    internal RazerDockAccessoryViewModel(ushort productId, Action<string>? diagnostic = null)
    {
        _productId = productId;
        _diagnostic = diagnostic;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Title => "Razer Mouse Dock V2 Pro";
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanEditDockSync));
            OnPropertyChanged(nameof(CanEditChargingEffect));
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
    public bool CanEditDockSync => CanEdit && _hasDockSync;
    public bool CanEditChargingEffect => CanEdit && _hasChargingEffect;
    public bool DockSync
    {
        get => _dockSync;
        set => SetField(ref _dockSync, value);
    }
    public bool ChargingEffect
    {
        get => _chargingEffect;
        set => SetField(ref _chargingEffect, value);
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _service ??= await RazerDockAccessoryService.ConnectAsync(
                _productId, _diagnostic, cancellationToken).ConfigureAwait(false);
            if (_service is null)
            {
                _isConnected = false;
                ErrorText = $"No 91-byte dock channel found for {_productId:X4}.";
                StatusText = string.Empty;
                return;
            }

            var state = await _service.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            _hasDockSync = state.HasDockSync;
            _hasChargingEffect = state.HasChargingEffect;
            if (_hasDockSync) _dockSync = state.DockSync;
            if (_hasChargingEffect) _chargingEffect = state.ChargingEffect;
            _isConnected = _hasDockSync || _hasChargingEffect;
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
            if (_hasDockSync)
            {
                await _service.SetDockSyncAsync(DockSync, cancellationToken).ConfigureAwait(false);
            }
            if (_hasChargingEffect)
            {
                await _service.SetChargingEffectAsync(ChargingEffect, cancellationToken)
                    .ConfigureAwait(false);
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
