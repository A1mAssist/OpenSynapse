using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Audio controls for Nommo speakers; unsupported fields stay disabled.
/// </summary>
public sealed partial class RazerSpeakerAccessoryViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ushort _productId;
    private readonly Action<string>? _diagnostic;
    private RazerSpeakerAccessoryService? _service;
    private bool _isConnected;
    private bool _isBusy;
    private bool _hasVolume;
    private bool _hasMute;
    private bool _hasBass;
    private bool _hasEq;
    private bool _hasInput;
    private bool _hasOutput;
    private bool _hasMicVolume;
    private bool _hasMicMute;
    private double _volume;
    private bool _isMuted;
    private double _bass;
    private int _eqPresetIndex;
    private int _inputSourceIndex;
    private int _outputSourceIndex;
    private double _micVolume;
    private bool _isMicMuted;
    private string _statusText = string.Empty;
    private string _errorText = string.Empty;

    internal RazerSpeakerAccessoryViewModel(ushort productId, Action<string>? diagnostic = null)
    {
        _productId = productId;
        _diagnostic = diagnostic;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Title => _productId switch
    {
        0x0517 => "Razer Nommo Chroma",
        0x0518 => "Razer Nommo Pro",
        0x0566 => "Razer Wireless Control Pod",
        _ => $"Razer Audio {_productId:X4}",
    };
    public bool IsConnected { get => _isConnected; private set => SetField(ref _isConnected, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanRefresh));
            OnPropertyChanged(nameof(CanEditVolume));
            OnPropertyChanged(nameof(CanEditMute));
            OnPropertyChanged(nameof(CanEditBass));
            OnPropertyChanged(nameof(CanEditEq));
            OnPropertyChanged(nameof(CanEditInput));
            OnPropertyChanged(nameof(CanEditOutput));
            OnPropertyChanged(nameof(CanEditMicVolume));
            OnPropertyChanged(nameof(CanEditMicMute));
            OnPropertyChanged(nameof(CanEditDolby));
            OnPropertyChanged(nameof(CanEditBands));
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
    public bool CanEdit => IsConnected && !IsBusy;
    public bool CanRefresh => !IsBusy;
    public bool CanEditVolume => CanEdit && _hasVolume;
    public bool CanEditMute => CanEdit && _hasMute;
    public bool CanEditBass => CanEdit && _hasBass;
    public bool CanEditEq => CanEdit && _hasEq;
    public bool CanEditInput => CanEdit && _hasInput;
    public bool CanEditOutput => CanEdit && _hasOutput;
    public bool CanEditMicVolume => CanEdit && _hasMicVolume;
    public bool CanEditMicMute => CanEdit && _hasMicMute;
    public bool ShowBass => _productId is 0x0517 or 0x0518;
    public bool ShowEq => _productId is 0x0517 or 0x0518;
    public bool ShowInputOutput => true;
    public bool ShowMicrophone => true;
    public Visibility BassVisibility => ShowBass ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EqVisibility => ShowEq ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MicrophoneVisibility =>
        ShowMicrophone ? Visibility.Visible : Visibility.Collapsed;

    public double Volume
    {
        get => _volume;
        set => SetField(ref _volume, Math.Clamp(value, 0, 100));
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
    }

    public double Bass
    {
        get => _bass;
        set => SetField(ref _bass, Math.Clamp(value, 0, 100));
    }

    public int EqPresetIndex
    {
        get => _eqPresetIndex;
        set
        {
            if (SetField(ref _eqPresetIndex, Math.Clamp(value, 0, EqPresetOptions.Count - 1)))
            {
                if (_nommoDescriptor is not null && _eqPresetIndex == 4) Dolby = false;
                OnPropertyChanged(nameof(CanEditBands));
            }
        }
    }

    public int InputSourceIndex
    {
        get => _inputSourceIndex;
        set => SetField(ref _inputSourceIndex, Math.Clamp(value, 0, InputSourceOptions.Count - 1));
    }

    public int OutputSourceIndex
    {
        get => _outputSourceIndex;
        set => SetField(ref _outputSourceIndex, Math.Clamp(value, 0, OutputSourceOptions.Count - 1));
    }

    public double MicVolume
    {
        get => _micVolume;
        set => SetField(ref _micVolume, Math.Clamp(value, 0, 100));
    }

    public bool IsMicMuted
    {
        get => _isMicMuted;
        set => SetField(ref _isMicMuted, value);
    }

    public IReadOnlyList<string> EqPresetOptions { get; } =
    [
        AppStrings.Text("AccessoryAudioEqDefault"),
        AppStrings.Text("AccessoryAudioEqGame"),
        AppStrings.Text("AccessoryAudioEqMusic"),
        AppStrings.Text("AccessoryAudioEqMovie"),
        AppStrings.Text("AccessoryAudioEqTreble"),
    ];

    public IReadOnlyList<string> InputSourceOptions { get; } =
    [
        AppStrings.Text("AccessoryAudioInputNone"),
        "SPDIF",
        "AUX",
        "Bluetooth",
        "USB",
    ];

    public IReadOnlyList<string> OutputSourceOptions { get; } =
    [
        AppStrings.Text("AccessoryAudioOutputSpeaker"),
        AppStrings.Text("AccessoryAudioOutputHeadset"),
    ];

    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _service ??= await RazerSpeakerAccessoryService.ConnectAsync(
                _productId, _diagnostic, cancellationToken);
            if (_service is null)
            {
                IsConnected = false;
                ErrorText = $"No 65/91-byte audio channel found for {_productId:X4}.";
                StatusText = string.Empty;
                return;
            }

            var state = await _service.ReadStateAsync(cancellationToken);
            _hasVolume = state.Volume is >= 0 and <= 100;
            _hasMute = state.Muted is not null;
            _hasBass = state.Bass is >= 0 and <= 100;
            _hasEq = _nommoDescriptor is null ? state.EqPreset is >= 0 and < 5 :
                state.EqPreset is >= 0 and < 4 || _productId == 0x0518 && state.EqPreset == 16;
            _hasInput = state.InputSource is >= 0 and < 5;
            _hasOutput = state.OutputSource is >= 0 and < 2;
            _hasMicVolume = state.MicVolume is >= 0 and <= 100;
            _hasMicMute = state.MicMuted is not null;
            _volume = state.Volume ?? _volume;
            _isMuted = state.Muted ?? _isMuted;
            _bass = state.Bass ?? _bass;
            if (_hasEq) _eqPresetIndex = state.EqPreset == 16 ? 4 : state.EqPreset!.Value;
            ApplyNommoState(state);
            if (_hasInput) _inputSourceIndex = state.InputSource!.Value;
            if (_hasOutput) _outputSourceIndex = state.OutputSource!.Value;
            _micVolume = state.MicVolume ?? _micVolume;
            _isMicMuted = state.MicMuted ?? _isMicMuted;
            IsConnected = true;
            ErrorText = state.Error ?? string.Empty;
            StatusText = AppStrings.Text("CoolingPadStatusConnected");
            OnPropertyChanged(string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (IsExpected(exception))
        {
            IsConnected = false;
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
            if (_nommoDescriptor is not null)
            {
                await ApplyNommoAndSaveAsync(cancellationToken);
                return;
            }
            if (_hasVolume)
            {
                await _service.SetVolumeAsync((int)Math.Round(Volume), cancellationToken).ConfigureAwait(false);
            }
            if (_hasMute)
            {
                await _service.SetMuteAsync(IsMuted, cancellationToken).ConfigureAwait(false);
            }
            if (_hasBass)
            {
                await _service.SetBassAsync((int)Math.Round(Bass), cancellationToken).ConfigureAwait(false);
            }
            if (_hasEq)
            {
                await _service.SetEqPresetAsync(EqPresetIndex, cancellationToken).ConfigureAwait(false);
            }
            if (_hasInput)
            {
                await _service.SetInputSourceAsync(InputSourceIndex, cancellationToken).ConfigureAwait(false);
            }
            if (_hasOutput)
            {
                await _service.SetOutputSourceAsync(OutputSourceIndex, cancellationToken).ConfigureAwait(false);
            }
            if (_hasMicVolume)
            {
                await _service.SetMicVolumeAsync((int)Math.Round(MicVolume), cancellationToken)
                    .ConfigureAwait(false);
            }
            if (_hasMicMute)
            {
                await _service.SetMicMuteAsync(IsMicMuted, cancellationToken).ConfigureAwait(false);
            }

            StatusText = AppStrings.Text("BlackSharkSaved");
            ErrorText = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
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
            System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or ArgumentException;

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
