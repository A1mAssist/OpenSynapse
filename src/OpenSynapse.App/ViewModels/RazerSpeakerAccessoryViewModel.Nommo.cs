using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerSpeakerAccessoryViewModel
{
    static RazerSpeakerAccessoryViewModel() => ProfileSettingCatalog.Register(
        new ProfileSettingDescriptor("speaker.nommo", DeviceCategory.Speaker, PowerScoped: false));
    private readonly DeviceDescriptor? _nommoDescriptor;
    private readonly Func<ProfileDocument>? _getProfile;
    private readonly Func<CancellationToken, Task<bool>>? _saveProfile;
    private bool _dolby;
    private bool _hasDolby;
    private bool _hasBands;

    internal RazerSpeakerAccessoryViewModel(RazerHidDeviceConnection connection,
        Func<ProfileDocument>? getProfile, Func<CancellationToken, Task<bool>>? saveProfile)
        : this(connection.Definition.ProductId)
    {
        _nommoDescriptor = connection.ToDescriptor();
        _getProfile = getProfile;
        _saveProfile = saveProfile;
        _service = RazerSpeakerAccessoryService.OpenNommo(connection);
    }

    public IReadOnlyList<NommoEqBandViewModel> NommoBands { get; } =
        new[] { 60, 250, 500, 1000, 2000, 4000, 8000, 16000 }
            .Select(hz => new NommoEqBandViewModel(hz)).ToArray();
    public IReadOnlyList<string> NommoPresets => _productId == 0x0518
        ? [.. EqPresetOptions.Take(4), AppStrings.Text("LeviathanPresetCustom")]
        : EqPresetOptions.Take(4).ToArray();
    public IReadOnlyList<string> NommoInputs => InputSourceOptions.Skip(1).ToArray();
    public int NommoInputIndex
    {
        get => InputSourceIndex - 1;
        set
        {
            if (value >= 0 && value < 4) InputSourceIndex = value + 1;
            OnPropertyChanged();
        }
    }
    public Visibility NommoProVisibility => _productId == 0x0518 ? Visibility.Visible : Visibility.Collapsed;
    public bool Dolby
    {
        get => _dolby;
        set
        {
            if (SetField(ref _dolby, value) && value && EqPresetIndex == 4) EqPresetIndex = 0;
        }
    }
    public bool CanEditDolby => CanEdit && _hasDolby;
    public bool CanEditBands => CanEdit && _hasBands && EqPresetIndex == 4;
    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    private void ApplyNommoState(RazerSpeakerState state)
    {
        _hasDolby = state.Dolby is not null;
        _dolby = state.Dolby ?? _dolby;
        _hasBands = state.EqBands is { Count: 8 };
        if (state.EqBands is { Count: 8 } bands)
            for (var i = 0; i < bands.Count; i++) NommoBands[i].Level = bands[i] * 18d / 100 - 9;
    }

    internal async Task LoadNommoAsync(CancellationToken ct)
    {
        if (_getProfile is null) await RefreshAsync(ct);
        else await ApplyPersistedProfileAsync(PowerSourceKind.Unknown, ct);
    }

    [NotProfileScoped]
    public async Task ApplyPersistedProfileAsync(PowerSourceKind powerSource, CancellationToken ct = default)
    {
        if (IsBusy || _service is null || _nommoDescriptor is null || _getProfile?.Invoke() is not { } document) return;
        IsBusy = true;
        var applied = false;
        try
        {
            await _service.ApplyNommoProfileAsync(document, _nommoDescriptor, powerSource, ct);
            ErrorText = string.Empty;
            applied = true;
        }
        catch (Exception e) when (IsExpected(e)) { ErrorText = e.Message; }
        finally { IsBusy = false; }
        if (applied) await RefreshAsync(ct);
    }

    [ProfileSetting("speaker.nommo", DeviceCategory.Speaker)]
    private async Task ApplyNommoAndSaveAsync(CancellationToken ct)
    {
        var settings = new DeviceProfileSettings();
        if (_hasVolume) settings.Headset.VolumePercent = (int)Math.Round(Volume);
        if (_hasMute) settings.Extras["nommo.muted"] = new() { Kind = "bool", Value = IsMuted.ToString() };
        if (_hasEq) settings.Headset.EqPreset = (EqPresetIndex == 4 ? 16 : EqPresetIndex).ToString();
        if (_productId == 0x0518)
        {
            if (_hasBass) settings.Headset.Bass = (int)Math.Round(Bass);
            if (_hasDolby) settings.Headset.Dolby = EqPresetIndex == 4 ? false : Dolby;
            if (_hasInput && InputSourceIndex > 0) settings.Headset.AudioSourceIndex = InputSourceIndex;
            if (_hasBands && EqPresetIndex == 4)
                settings.Headset.EqBands = NommoBands.Select((b, i) => (i, b))
                    .ToDictionary(x => x.i.ToString(), x => (int)Math.Floor((x.b.Level + 9) / 18 * 100 + 0.5));
        }
        await _service!.ApplyNommoProfileAsync(settings, ct);
        if (settings.Headset.Dolby is { } appliedDolby) Dolby = appliedDolby;
        var saved = await PeripheralProfileStore.SaveAsync(_getProfile?.Invoke(), _saveProfile,
            ProfileResolver.GetDeviceKey(_nommoDescriptor!), target =>
            {
                if (settings.Headset.VolumePercent is { } volume) target.Headset.VolumePercent = volume;
                if (settings.Headset.EqPreset is { } preset) target.Headset.EqPreset = preset;
                if (settings.Headset.Bass is { } bass) target.Headset.Bass = bass;
                if (settings.Headset.Dolby is { } dolby) target.Headset.Dolby = dolby;
                if (settings.Headset.AudioSourceIndex is { } source) target.Headset.AudioSourceIndex = source;
                if (settings.Headset.EqBands is not null) target.Headset.EqBands = settings.Headset.EqBands;
                if (settings.Extras.TryGetValue("nommo.muted", out var mute)) target.Extras["nommo.muted"] = mute;
            }, ct);
        ErrorText = saved ? string.Empty : AppStrings.Text("ViperSettingAppliedProfileSaveFailed");
        StatusText = saved ? AppStrings.Text("BlackSharkSaved") : string.Empty;
    }
}
