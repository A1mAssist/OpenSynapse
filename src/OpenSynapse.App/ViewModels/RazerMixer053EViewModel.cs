using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// The 053E Audio Mixer card. It owns one HID service for the selected device path and
/// only exposes the bundle-aligned subset implemented by <see cref="RazerMixer053EService"/>.
/// Missing fields stay as "--"; controls are hidden until the report-9/feature path answers.
/// </summary>
public sealed class RazerMixer053EViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly string _devicePath;
    private readonly Action<string>? _diagnostic;
    private readonly Func<ProfileDocument>? _getProfile;
    private readonly Func<CancellationToken, Task<bool>>? _saveProfile;
    private RazerMixer053EService? _service;
    private bool _disposed;
    private bool _isBusy;
    private bool _hasState;
    private bool _hasBrightness;
    private string _firmwareText = "--";
    private string _serialNumberText = "--";
    private string _deviceModeText = "--";
    private string _errorText = string.Empty;
    private string _operationStatusText = string.Empty;
    private byte? _slider1;
    private byte? _slider2;
    private byte? _slider3;
    private byte? _slider4;
    private bool _mute1;
    private bool _mute2;
    private bool _mute3;
    private bool _mute4;
    private bool _micMuted;
    private bool _beep;
    private bool _phantomPower48V;
    private bool _micJack;
    private bool _lineIn;
    private double _brightness;

    public RazerMixer053EViewModel(
        string devicePath,
        Action<string>? diagnostic = null,
        Func<ProfileDocument>? getActiveProfile = null,
        Func<CancellationToken, Task<bool>>? saveProfileAsync = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        _devicePath = devicePath;
        _diagnostic = diagnostic;
        _getProfile = getActiveProfile;
        _saveProfile = saveProfileAsync;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasState => _hasState;
    public Visibility ControlsVisibility => _hasState ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BrightnessVisibility => _hasBrightness ? Visibility.Visible : Visibility.Collapsed;

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
            OnPropertyChanged(nameof(CanApplyBrightness));
        }
    }

    public bool CanEdit => (_hasState || _hasBrightness) && !IsBusy;
    public bool CanApplyBrightness => CanEdit && _hasBrightness;

    public string FirmwareText
    {
        get => _firmwareText;
        private set
        {
            if (SetField(ref _firmwareText, value))
            {
                OnPropertyChanged(nameof(InfoSummary));
            }
        }
    }

    public string SerialNumberText
    {
        get => _serialNumberText;
        private set
        {
            if (SetField(ref _serialNumberText, value))
            {
                OnPropertyChanged(nameof(InfoSummary));
            }
        }
    }

    public string DeviceModeText
    {
        get => _deviceModeText;
        private set
        {
            if (SetField(ref _deviceModeText, value))
            {
                OnPropertyChanged(nameof(InfoSummary));
            }
        }
    }

    public string InfoSummary => AppStrings.FormatText(
        "RazerMixer053EInfoFormat",
        FirmwareText,
        SerialNumberText,
        DeviceModeText);

    public string FadersSummary => AppStrings.FormatText(
        "RazerMixer053EFadersFormat",
        FormatSlider(_slider1),
        FormatSlider(_slider2),
        FormatSlider(_slider3),
        FormatSlider(_slider4));

    public string StatusSummary => AppStrings.FormatText(
        "RazerMixer053EStatusFormat",
        FormatFlag(Mute1),
        FormatFlag(Mute2),
        FormatFlag(Mute3),
        FormatFlag(Mute4),
        FormatFlag(_beep),
        FormatFlag(_micMuted),
        FormatFlag(_phantomPower48V),
        FormatFlag(_micJack),
        FormatFlag(_lineIn));

    public bool Mute1 => _mute1;
    public bool Mute2 => _mute2;
    public bool Mute3 => _mute3;
    public bool Mute4 => _mute4;
    public bool MicMuted => _micMuted;

    public string Mute1Label => FormatMuteLabel(1, Mute1);
    public string Mute2Label => FormatMuteLabel(2, Mute2);
    public string Mute3Label => FormatMuteLabel(3, Mute3);
    public string Mute4Label => FormatMuteLabel(4, Mute4);

    public string MicMuteLabel => AppStrings.FormatText(
        "RazerMixer053EMicMuteButton",
        FormatMuteState(MicMuted));

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (!SetField(ref _brightness, Math.Clamp(value, 0, 100)))
            {
                return;
            }

            OnPropertyChanged(nameof(BrightnessText));
        }
    }

    public string BrightnessText => _hasBrightness ? $"{Math.Round(Brightness)}%" : "--";

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetField(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string OperationStatusText
    {
        get => _operationStatusText;
        private set => SetField(ref _operationStatusText, value);
    }

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    [NotProfileScoped]
    public async Task ApplyPersistedProfileAsync(
        PowerSourceKind powerSource,
        CancellationToken cancellationToken = default)
    {
        if (_getProfile?.Invoke() is not { } document)
        {
            return;
        }

        var descriptor = new DeviceDescriptor(
            "1532:053E",
            "Razer Audio Mixer",
            0x1532,
            0x053E,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            64,
            1,
            2,
            "mixer053e",
            DeviceCategory.Accessory);

        // Reuse the card's own service: AccessoryProfileApplier rediscovers the mixer
        // and would open a second handle on a possibly different path while this one
        // is still live.
        RazerMixer053EState state;
        try
        {
            state = await GetService().ReadStateAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return;
        }

        if (!state.Available || state.Hardware is not { } hardware)
        {
            return;
        }

        var deviceKey = string.IsNullOrWhiteSpace(state.SerialNumber)
            ? ProfileResolver.GetDeviceKey(descriptor)
            : state.SerialNumber;
        var settings = ProfileResolver.ResolveWithDeviceKey(document, descriptor, powerSource, deviceKey).Mixer;

        async Task AttemptAsync(string key, Func<Task> write)
        {
            try
            {
                await write();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                _diagnostic?.Invoke($"profile {key}: {exception.Message}");
            }
        }

        if (settings.ChannelMutes is { Count: 4 } mutes)
        {
            for (var channel = 1; channel <= 4; channel++)
            {
                var target = channel;
                var desired = mutes[target - 1];
                if (hardware.IsMuted(target) == desired)
                {
                    continue;
                }

                await AttemptAsync(
                    $"mixer.channel-{target}-mute",
                    () => GetService().SetChannelMuteAsync(target, desired, cancellationToken));
            }
        }

        if (settings.MicMute is { } micMute && hardware.MicMute != micMute)
        {
            await AttemptAsync(
                "mixer.mic-mute",
                () => GetService().SetMicMuteAsync(micMute, cancellationToken));
        }

        if (settings.BrightnessPercent is { } brightness && state.Brightness != brightness)
        {
            await AttemptAsync(
                "mixer.brightness",
                () => GetService().SetBrightnessAsync((byte)brightness, cancellationToken: cancellationToken));
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await RefreshStateCoreAsync(cancellationToken);
            await ApplyPersistedProfileAsync(PowerSourceKind.Unknown, cancellationToken);
            OperationStatusText = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [ProfileSetting("mixer.channel-mutes", DeviceCategory.Accessory)]
    public Task ToggleMute1Async(CancellationToken cancellationToken = default) =>
        ToggleChannelAsync(1, !_mute1, cancellationToken);

    [ProfileSetting("mixer.channel-mutes", DeviceCategory.Accessory)]
    public Task ToggleMute2Async(CancellationToken cancellationToken = default) =>
        ToggleChannelAsync(2, !_mute2, cancellationToken);

    [ProfileSetting("mixer.channel-mutes", DeviceCategory.Accessory)]
    public Task ToggleMute3Async(CancellationToken cancellationToken = default) =>
        ToggleChannelAsync(3, !_mute3, cancellationToken);

    [ProfileSetting("mixer.channel-mutes", DeviceCategory.Accessory)]
    public Task ToggleMute4Async(CancellationToken cancellationToken = default) =>
        ToggleChannelAsync(4, !_mute4, cancellationToken);

    [ProfileSetting("mixer.mic-mute", DeviceCategory.Accessory)]
    public Task ToggleMicMuteAsync(CancellationToken cancellationToken = default)
    {
        var previous = _micMuted;
        var desired = !previous;
        return ExecuteWriteAsync(
            (service, token) => service.SetMicMuteAsync(desired, token),
            cancellationToken,
            settings =>
            {
                settings.Mixer ??= new MixerProfileSettings();
                settings.Mixer.MicMute = desired;
            },
            (service, token) => service.SetMicMuteAsync(previous, token));
    }

    [ProfileSetting("mixer.brightness", DeviceCategory.Accessory)]
    public async Task ApplyBrightnessAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEdit) return;
        var previous = (byte)Math.Round(Brightness);
        try
        {
            previous = await GetService().ReadBrightnessAsync(cancellationToken: cancellationToken) ?? previous;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
        }

        var desired = (int)Math.Round(Brightness);
        await ExecuteWriteAsync(
            (service, token) => service.SetBrightnessAsync((byte)desired, cancellationToken: token),
            cancellationToken,
            settings =>
            {
                settings.Mixer ??= new MixerProfileSettings();
                settings.Mixer.BrightnessPercent = desired;
            },
            (service, token) => service.SetBrightnessAsync(previous, cancellationToken: token));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_service is { } service)
        {
            await service.DisposeAsync();
        }
    }

    private Task ToggleChannelAsync(int channel, bool muted, CancellationToken cancellationToken)
    {
        var previous = channel switch
        {
            1 => _mute1,
            2 => _mute2,
            3 => _mute3,
            _ => _mute4,
        };
        return ExecuteWriteAsync(
            (service, token) => service.SetChannelMuteAsync(channel, muted, token),
            cancellationToken,
            settings =>
            {
                settings.Mixer ??= new MixerProfileSettings();
                var mutes = settings.Mixer.ChannelMutes is { Count: 4 } existing
                    ? existing
                    : Enumerable.Repeat(false, 4).ToList();
                mutes[channel - 1] = muted;
                settings.Mixer.ChannelMutes = mutes;
            },
            (service, token) => service.SetChannelMuteAsync(channel, previous, token));
    }

    private async Task ExecuteWriteAsync(
        Func<RazerMixer053EService, CancellationToken, Task> write,
        CancellationToken cancellationToken,
        Action<DeviceProfileSettings>? mutateProfile = null,
        Func<RazerMixer053EService, CancellationToken, Task>? rollbackHardware = null)
    {
        if (!CanEdit)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var service = GetService();
            await write(service, cancellationToken);
            if (mutateProfile is not null &&
                !await PeripheralProfileStore.SaveAsync(
                    _getProfile?.Invoke(),
                    _saveProfile,
                    ProfileDeviceKey,
                    mutateProfile,
                    cancellationToken))
            {
                if (rollbackHardware is not null)
                {
                    try
                    {
                        await rollbackHardware(service, CancellationToken.None);
                    }
                    catch (Exception exception) when (IsExpected(exception))
                    {
                        // The save failure is the actionable error; rollback is best effort.
                    }
                }

                throw new InvalidOperationException(
                    AppStrings.Text("RazerHidSettingProfileSaveFailed"));
            }

            await RefreshStateCoreAsync(cancellationToken);
            OperationStatusText = AppStrings.Text("RazerMixer053ESaved");
            ErrorText = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            var message = exception.Message;
            OperationStatusText = string.Empty;
            try
            {
                await RefreshStateCoreAsync(cancellationToken);
            }
            catch (Exception refreshException) when (IsExpected(refreshException))
            {
            }

            ErrorText = message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshStateCoreAsync(CancellationToken cancellationToken)
    {
        var state = await GetService().ReadStateAsync(cancellationToken);
        ApplyState(state);
        ErrorText = state.Available ? string.Empty : AppStrings.Text("RazerMixer053EUnavailable");
    }

    private RazerMixer053EService GetService()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _service ??= new RazerMixer053EService(_devicePath, _diagnostic);
    }

    private string ProfileDeviceKey => string.IsNullOrWhiteSpace(_serialNumberText) ||
        string.Equals(_serialNumberText, "--", StringComparison.Ordinal)
            ? "1532:053E"
            : _serialNumberText;

    private void ApplyState(RazerMixer053EState state)
    {
        var hardware = state.Hardware;
        _hasState = state.Available && hardware is not null;
        _hasBrightness = state.Brightness is not null;
        FirmwareText = state.Firmware?.ToString() ?? "--";
        SerialNumberText = string.IsNullOrWhiteSpace(state.SerialNumber) ? "--" : state.SerialNumber;
        DeviceModeText = state.DeviceMode?.ToString(CultureInfo.InvariantCulture) ?? "--";
        _slider1 = hardware?.Slider1;
        _slider2 = hardware?.Slider2;
        _slider3 = hardware?.Slider3;
        _slider4 = hardware?.Slider4;
        _mute1 = hardware?.Mute1 ?? false;
        _mute2 = hardware?.Mute2 ?? false;
        _mute3 = hardware?.Mute3 ?? false;
        _mute4 = hardware?.Mute4 ?? false;
        _micMuted = hardware?.MicMute ?? false;
        _beep = hardware?.Beep ?? false;
        _phantomPower48V = hardware?.PhantomPower48V ?? false;
        _micJack = hardware?.MicJack ?? false;
        _lineIn = hardware?.LineIn ?? false;
        _brightness = state.Brightness ?? 0;

        OnPropertyChanged(nameof(HasState));
        OnPropertyChanged(nameof(ControlsVisibility));
        OnPropertyChanged(nameof(BrightnessVisibility));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanApplyBrightness));
        OnPropertyChanged(nameof(FadersSummary));
        OnPropertyChanged(nameof(StatusSummary));
        OnPropertyChanged(nameof(Mute1));
        OnPropertyChanged(nameof(Mute2));
        OnPropertyChanged(nameof(Mute3));
        OnPropertyChanged(nameof(Mute4));
        OnPropertyChanged(nameof(MicMuted));
        OnPropertyChanged(nameof(Mute1Label));
        OnPropertyChanged(nameof(Mute2Label));
        OnPropertyChanged(nameof(Mute3Label));
        OnPropertyChanged(nameof(Mute4Label));
        OnPropertyChanged(nameof(MicMuteLabel));
        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(BrightnessText));
    }

    private string FormatFlag(bool value)
    {
        if (!_hasState)
        {
            return "--";
        }

        return AppStrings.Text(value ? "RazerMixer053EOn" : "RazerMixer053EOff");
    }

    private string FormatMuteLabel(int channel, bool muted) =>
        AppStrings.FormatText("RazerMixer053EMuteButton", channel, FormatMuteState(muted));

    private string FormatMuteState(bool muted)
    {
        if (!_hasState)
        {
            return "--";
        }

        return AppStrings.Text(muted ? "RazerMixer053EMuted" : "RazerMixer053EUnmuted");
    }

    private static string FormatSlider(byte? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "--";

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or TimeoutException;

    private bool SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
