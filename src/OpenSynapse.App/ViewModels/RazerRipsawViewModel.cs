using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerRipsawChannelViewModel(RazerRipsawChannel channel, string label) : INotifyPropertyChanged
{
    private double _volume;
    private bool _muted;
    public event PropertyChangedEventHandler? PropertyChanged;
    public RazerRipsawChannel Channel { get; } = channel;
    public string Label => Channel is RazerRipsawChannel.Pc or RazerRipsawChannel.Hdmi ? label : AppStrings.Text(label);
    public string MuteLabel => AppStrings.Text("MacroMediaMute");
    public string ApplyLabel => AppStrings.Text("RazerGamepadApply");
    public string VolumeText => $"{Volume:0}%";
    public double Volume
    {
        get => _volume;
        set
        {
            if (!double.IsFinite(value)) return;
            _volume = Math.Round(Math.Clamp(value, 0, 100));
            PropertyChanged?.Invoke(this, new(nameof(Volume)));
            PropertyChanged?.Invoke(this, new(nameof(VolumeText)));
        }
    }
    public bool Muted
    {
        get => _muted;
        set { _muted = value; PropertyChanged?.Invoke(this, new(nameof(Muted))); }
    }
    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}

public sealed class RazerRipsawViewModel : INotifyPropertyChanged
{
    static RazerRipsawViewModel() => ProfileSettingCatalog.Register(
        new ProfileSettingDescriptor("ripsaw.channel", DeviceCategory.Accessory, PowerScoped: false));
    private readonly IRazerRipsawService _service;
    private readonly string _deviceKey;
    private readonly Func<ProfileDocument>? _getProfile;
    private readonly Func<CancellationToken, Task<bool>>? _saveProfile;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _isBusy;
    private bool _hasState;
    private string _errorText = string.Empty;
    private string _statusText = string.Empty;
    private string _hdmiText = "HDMI —";
    private string _infoText = string.Empty;

    public RazerRipsawViewModel(IRazerRipsawService service, string deviceKey,
        Func<ProfileDocument>? getProfile = null, Func<CancellationToken, Task<bool>>? saveProfile = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        _deviceKey = deviceKey;
        _getProfile = getProfile;
        _saveProfile = saveProfile;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<RazerRipsawChannelViewModel> Channels { get; } =
    [
        new(RazerRipsawChannel.Speaker, "AccessoryAudioVolume/Text"),
        new(RazerRipsawChannel.Pc, "PC"),
        new(RazerRipsawChannel.Hdmi, "HDMI"),
        new(RazerRipsawChannel.Mic, "RazerMicrophoneCategory"),
    ];
    public string RefreshLabel => AppStrings.Text("RazerGamepadRefresh");
    public bool IsBusy { get => _isBusy; private set { Set(ref _isBusy, value); Notify(nameof(CanEdit)); Notify(nameof(CanRefresh)); } }
    public bool HasState { get => _hasState; private set { Set(ref _hasState, value); Notify(nameof(CanEdit)); } }
    public bool CanEdit => HasState && !IsBusy;
    public bool CanRefresh => !IsBusy;
    public bool HasError => ErrorText.Length != 0;
    public string ErrorText { get => _errorText; private set { Set(ref _errorText, value); Notify(nameof(HasError)); } }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string HdmiText { get => _hdmiText; private set => Set(ref _hdmiText, value); }
    public string InfoText { get => _infoText; private set => Set(ref _infoText, value); }

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        ApplyPersistedProfileAsync(PowerSourceKind.Unknown, cancellationToken);

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async () => ApplyState(await _service.ReadStateAsync(cancellationToken)), cancellationToken);

    [NotProfileScoped]
    public Task ApplyPersistedProfileAsync(PowerSourceKind powerSource, CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            if (_getProfile?.Invoke() is { } document)
                await RazerRipsawProfileApplier.ApplyAsync(_service,
                    ProfileResolver.ResolveRipsaw(document, powerSource, _deviceKey), cancellationToken);
            ApplyState(await _service.ReadStateAsync(cancellationToken));
        }, cancellationToken);

    [ProfileSetting("ripsaw.channel", DeviceCategory.Accessory)]
    public Task ApplyChannelAsync(RazerRipsawChannelViewModel row, CancellationToken cancellationToken = default)
    {
        if (!Channels.Contains(row)) throw new ArgumentException("Unknown Ripsaw row.", nameof(row));
        var volume = (int)row.Volume;
        var muted = row.Muted;
        return RunAsync(async () =>
        {
            var document = _getProfile?.Invoke();
            var previous = await _service.ReadStateAsync(cancellationToken);
            var key = row.Channel.ToString();
            var desired = new RazerRipsawProfileSettings
            {
                ChannelVolumes = new() { [key] = volume },
                ChannelMuted = new() { [key] = muted },
            };
            await RazerRipsawProfileApplier.ApplyAsync(_service, desired, cancellationToken);
            try
            {
                if (!await PeripheralProfileStore.SaveAsync(document, _saveProfile, _deviceKey, device =>
                {
                    device.Ripsaw.ChannelVolumes[key] = volume;
                    device.Ripsaw.ChannelMuted[key] = muted;
                }, cancellationToken)) throw new IOException(AppStrings.Text("RazerHidSettingProfileSaveFailed"));
            }
            catch (Exception saveFailure)
            {
                try
                {
                    await RazerRipsawProfileApplier.ApplyAsync(_service, new RazerRipsawProfileSettings
                    {
                        ChannelVolumes = new() { [key] = previous.Volumes[(int)row.Channel] },
                        ChannelMuted = new() { [key] = previous.Muted[(int)row.Channel] },
                    }, CancellationToken.None);
                    ApplyState(await _service.ReadStateAsync(CancellationToken.None));
                }
                catch (Exception restoreFailure)
                {
                    throw new AggregateException("Ripsaw profile save and restore failed; refresh before retrying.", saveFailure, restoreFailure);
                }
                throw;
            }
            ApplyState(await _service.ReadStateAsync(cancellationToken));
            StatusText = AppStrings.Text("BlackSharkSaved");
        }, cancellationToken);
    }

    public void RefreshLocalization()
    {
        foreach (var row in Channels) row.RefreshLocalization();
        Notify(string.Empty);
    }

    private void ApplyState(RazerRipsawState state)
    {
        foreach (var row in Channels)
        {
            if (state.Volumes[(int)row.Channel] > 100) throw new InvalidDataException("Ripsaw returned an out-of-range monitoring volume.");
            row.Volume = state.Volumes[(int)row.Channel];
            row.Muted = state.Muted[(int)row.Channel];
        }
        HdmiText = state.Hdmi is { Connected: true } hdmi
            ? $"HDMI · {hdmi.Width} × {hdmi.Height} · {hdmi.FramesPerSecond} fps"
            : $"HDMI · {(state.Hdmi is null ? "—" : AppStrings.Text("TaskbarBatteryDisconnected"))}";
        InfoText = $"FW {state.Firmware ?? "—"} · S/N {state.Serial ?? "—"} · Edition {state.Edition ?? "—"}";
        ErrorText = string.Join(Environment.NewLine, state.Errors);
        HasState = true;
    }

    private async Task RunAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try { await _gate.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        IsBusy = true;
        ErrorText = StatusText = string.Empty;
        try { await action(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { HasState = false; }
        catch (Exception exception) when (exception is IOException or Win32Exception or TimeoutException or
            UnauthorizedAccessException or InvalidOperationException or ArgumentException or AggregateException)
        {
            HasState = false;
            ErrorText = exception.Message;
        }
        finally { IsBusy = false; _gate.Release(); }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Notify(name);
    }
    private void Notify(string? name) => PropertyChanged?.Invoke(this, new(name));
}
