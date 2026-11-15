using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Audio;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Some Kraken models expose their 37-byte lighting on a second collection. Those
    /// devices get one card that carries both the audio settings and that lighting.
    /// </summary>
    public RazerHidKrakenViewModel? Lighting { get; }

    public RazerMixer053EViewModel? Mixer053E { get; }

    public Visibility Mixer053EVisibility => Mixer053E is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GenericStatusVisibility =>
        _hasFirmware || _hasBattery || _hasCharging ? Visibility.Visible : Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility LightingVisibility =>
        _hasLighting && (IsFeature91 || IsKrakenBase || IsProtocol25) && Lighting is not null
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>
    /// THX is an APO plus service on this headset, so the card only appears for the
    /// model whose installer we have pinned. Missing pieces get an install entry.
    /// </summary>
    public Visibility ThxVisibility => ThxHeadsetAdmission.IsSupported(_connection.ProductId)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ThxInstallerVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available || _thxStatus.RebootRequired
            ? Visibility.Collapsed
            : ThxVisibility;

    public bool CanInstallThx =>
        !IsBusy && ThxVisibility == Visibility.Visible &&
        _thxStatus.Availability != ThxHeadsetAvailability.Available &&
        !_thxStatus.RebootRequired;

    public string ThxGameProfilesUri =>
        ThxHeadsetAdmission.GetProfile(_connection.ProductId)?.GameProfilesUri ?? string.Empty;

    public Visibility ThxGameProfilesVisibility =>
        ThxGameProfilesUri.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string ThxPatcherUri =>
        ThxHeadsetAdmission.GetProfile(_connection.ProductId)?.PatcherUri ?? string.Empty;

    /// <summary>The pinned official installer, for a manual retry when the silent run fails.</summary>
    public string ThxInstallerUri =>
        ThxHeadsetAdmission.GetProfile(_connection.ProductId)?.InstallerUri ?? string.Empty;

    public Visibility ThxInstallerLinkVisibility =>
        ThxVisibility == Visibility.Visible &&
        ThxInstallerUri.Length > 0 &&
        ((_thxStatus.ErrorCode is "ThxServiceMissing" or "ThxNativeMissing" or
             "ThxDownloadFailed" or "ThxDownloadVerificationFailed") ||
         _thxStatus.ErrorCode?.StartsWith("ThxInstaller", StringComparison.Ordinal) == true)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility ThxPatcherVisibility =>
        ThxPatcherUri.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string ThxStatusText => !string.IsNullOrEmpty(_thxErrorText)
        ? _thxErrorText
        : _thxStatus.ErrorCode switch
        {
            "ThxServiceMissing" => AppStrings.Text("RazerHeadsetThxServiceMissing"),
            "ThxNativeMissing" => AppStrings.Text("RazerHeadsetThxNativeMissing"),
            "ThxDownloadFailed" or "ThxDownloadVerificationFailed" =>
                AppStrings.Text("RazerHeadsetThxDownloadFailed"),
            "ThxRebootRequired" => AppStrings.Text("RazerHeadsetThxRebootRequired"),
            _ when _thxStatus.ErrorCode?.StartsWith("ThxInstaller", StringComparison.Ordinal) == true =>
                AppStrings.Text("RazerHeadsetThxDownloadFailed"),
            _ when _thxStatus.Availability == ThxHeadsetAvailability.Available =>
                AppStrings.Text("RazerHeadsetThxReady"),
            _ => string.Empty,
        };

    public Visibility ThxControlsVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>Synapse 3 baseline applies only to the two affected Kraken models.</summary>
    public Visibility ThxS3BaselineVisibility =>
        SupportsS3Baseline(_connection.ProductId) ? Visibility.Visible : Visibility.Collapsed;

    public bool CanApplyThx => !IsBusy && _thxState is not null &&
        _thxStatus.Availability == ThxHeadsetAvailability.Available;

    public bool ThxSpatialEnabled
    {
        get => _thxSpatialEnabled;
        set
        {
            if (SetField(ref _thxSpatialEnabled, value) && CanApplyThx)
            {
                _ = ApplyThxSpatialAsync();
            }
        }
    }

    public bool ThxEqualizerEnabled
    {
        get => _thxEqualizerEnabled;
        set => SetField(ref _thxEqualizerEnabled, value);
    }

    public IReadOnlyList<string> ThxPresetOptions =>
        _thxState?.Presets.Select(preset => preset.Id == "default"
            ? AppStrings.Text("ThxPresetDefault")
            : preset.DisplayName).ToArray() ?? [];

    public int SelectedThxPresetIndex
    {
        get => _thxSelectedPresetIndex;
        set => SetField(ref _thxSelectedPresetIndex,
            Math.Clamp(value, 0, Math.Max(0, (_thxState?.Presets.Count ?? 0) - 1)));
    }

    public ObservableCollection<RazerHeadsetThxBandViewModel> ThxBands { get; } = new();

    public Visibility ThxBandsVisibility =>
        _thxState is { EqualizerBands.Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<string> LightingEffectOptions =>
        LightingEffectChoices.Select(choice => AppStrings.Text(choice.Key)).ToArray();

    public int SelectedLightingEffectIndex
    {
        get => _selectedEffectIndex;
        set
        {
            if (!SetField(ref _selectedEffectIndex, Math.Clamp(value, 0, LightingEffects.Length - 1))) return;
            OnPropertyChanged(nameof(SecondaryColorVisibility));
            OnPropertyChanged(nameof(WaveDirectionVisibility));
            OnPropertyChanged(nameof(WaveDirectionOptions));
            // Replacing ItemsSource clears the ComboBox selection; push the value
            // back so the direction box is never left blank.
            SelectedWaveDirectionIndex = Math.Clamp(_waveDirectionIndex, 0, DirectionOptionCount - 1);
            OnPropertyChanged(nameof(SelectedWaveDirectionIndex));
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
        LightingEffects[_selectedEffectIndex] == RazerChromaEffect.Breathing
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string Name => _connection.Name;
    public string CategoryText => AppStrings.Text(
        RazerHeadsetAudioService.GetCategory(_connection.ProductId) switch
        {
            OpenSynapse.Core.Devices.DeviceCategory.Speaker => "RazerSpeakerCategory",
            OpenSynapse.Core.Devices.DeviceCategory.Microphone => "RazerMicrophoneCategory",
            _ => "BlackSharkHeadsetCategory",
        });
    public string Identity => $"VID_1532 / PID_{_connection.ProductId:X4}";
    public string DevicePath => _connection.DevicePath;
    internal ushort ProductId => _connection.ProductId;
    public string StatusText => _probeState switch
    {
        RazerHeadsetProbeState.Probing => AppStrings.Text("BlackSharkCapabilityReading"),
        RazerHeadsetProbeState.Partial => AppStrings.Text("BlackSharkStatusPartial"),
        RazerHeadsetProbeState.Failed => AppStrings.Text("BlackSharkCapabilityReadFailed"),
        RazerHeadsetProbeState.NotAvailable => AppStrings.Text("BlackSharkComponentRequired"),
        RazerHeadsetProbeState.ControlInterfaceMissing =>
            AppStrings.Text("RazerHeadsetControlInterfaceMissing"),
        _ => AppStrings.Text("BlackSharkStatusConnected"),
    };

    public string FirmwareText { get => _firmwareText; private set => SetField(ref _firmwareText, value); }
    public string BatteryText { get => _batteryText; private set => SetField(ref _batteryText, value); }
    public int? BatteryPercent { get; private set; }
    public string ChargingText { get => _chargingText; private set => SetField(ref _chargingText, value); }
    public IReadOnlyList<string> EqPresetOptions =>
        ActivePresets.Select(preset => AppStrings.Text(preset.Key)).ToArray();

    public int SelectedEqPresetIndex
    {
        get => _selectedEqPresetIndex;
        set => SetField(ref _selectedEqPresetIndex, Math.Clamp(value, 0, ActivePresets.Length - 1));
    }

    public ObservableCollection<RazerHeadsetEqBandViewModel> EqBands { get; } = new();

    public Visibility EqBandsVisibility => _hasEqBands ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EqPresetVisibility => _hasEqPreset &&
        !(IsMixer053E || IsProtocol40 || IsKrakenBase || IsAvnera65)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public double Bass
    {
        get => _bass;
        set
        {
            if (!SetField(ref _bass, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(BassText));
        }
    }

    public string BassText => $"{Math.Round(Bass)}";

    public Visibility BassVisibility => _hasBass ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MicCardVisibility =>
        IsProtocol40
            ? Visibility.Collapsed
            : _hasMicMuted || _hasMicVolume ? Visibility.Visible : Visibility.Collapsed;

    public bool MicMuted
    {
        get => _micMuted;
        set => SetField(ref _micMuted, value);
    }

    public double MicVolume
    {
        get => _micVolume;
        set
        {
            if (!SetField(ref _micVolume, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(MicVolumeText));
        }
    }

    public string MicVolumeText => $"{Math.Round(MicVolume)}";
    public Visibility MicVolumeVisibility =>
        (IsFeature91 || IsProtocol25) && _hasMicVolume ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<string> AudioSourceOptions =>
    [
        AppStrings.Text("RazerHeadsetSourceNone"),
        AppStrings.Text("RazerHeadsetSourceSpdif"),
        AppStrings.Text("RazerHeadsetSourceAux"),
        AppStrings.Text("RazerHeadsetSourceBluetooth"),
        AppStrings.Text("RazerHeadsetSourceUsb"),
    ];

    public Visibility AudioSourceVisibility =>
        (IsFeature91 || IsProtocol25) && _hasAudioSource ? Visibility.Visible : Visibility.Collapsed;

    public int SelectedSourceIndex
    {
        get => _selectedSourceIndex;
        set => SetField(ref _selectedSourceIndex, Math.Clamp(value, 0, Sources.Length - 1));
    }

    public bool NoiseSuppression
    {
        get => _noiseSuppression;
        set => SetField(ref _noiseSuppression, value);
    }

    public Visibility NoiseSuppressionVisibility =>
        (IsFeature91 || IsProtocol25) && _hasNoiseSuppression ? Visibility.Visible : Visibility.Collapsed;
}
