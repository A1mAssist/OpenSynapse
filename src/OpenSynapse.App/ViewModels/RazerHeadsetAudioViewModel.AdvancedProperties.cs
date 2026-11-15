using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    public bool Dnd { get => _dnd; set => SetField(ref _dnd, value); }
    public Visibility DndVisibility => _hasDnd && !IsProtocol40 ? Visibility.Visible : Visibility.Collapsed;

    public bool AutoPause { get => _autoPause; set => SetField(ref _autoPause, value); }
    public Visibility AutoPauseVisibility => _hasAutoPause ? Visibility.Visible : Visibility.Collapsed;

    public bool GestureControl { get => _gestureControl; set => SetField(ref _gestureControl, value); }
    public Visibility GestureControlVisibility => _hasGestureControl ? Visibility.Visible : Visibility.Collapsed;

    public bool Dolby { get => _dolby; set => SetField(ref _dolby, value); }
    public Visibility DolbyVisibility => _hasDolby ? Visibility.Visible : Visibility.Collapsed;

    public bool NightMode { get => _nightMode; set => SetField(ref _nightMode, value); }
    public Visibility NightModeVisibility => _hasNightMode ? Visibility.Visible : Visibility.Collapsed;

    public bool VoicePrompts { get => _voicePrompts; set => SetField(ref _voicePrompts, value); }
    public Visibility VoicePromptsVisibility => _hasVoicePrompts ? Visibility.Visible : Visibility.Collapsed;

    public double MicDeEsser
    {
        get => _micDeEsser;
        set
        {
            if (!SetField(ref _micDeEsser, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(MicDeEsserText));
        }
    }

    public string MicDeEsserText => $"{Math.Round(MicDeEsser)}";
    public Visibility MicDeEsserVisibility => _hasMicDeEsser ? Visibility.Visible : Visibility.Collapsed;

    public double MicLimiterDb
    {
        get => _micLimiterDb;
        set
        {
            if (!SetField(ref _micLimiterDb, Math.Clamp(value, -60, 0))) return;
            OnPropertyChanged(nameof(MicLimiterText));
        }
    }

    public string MicLimiterText => $"{Math.Round(MicLimiterDb)} dB";
    public Visibility MicLimiterVisibility => _hasMicLimiter ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<string> MicReverbOptions =>
    [
        AppStrings.Text("RazerHeadsetReverbSmall"),
        AppStrings.Text("RazerHeadsetReverbMedium"),
        AppStrings.Text("RazerHeadsetReverbLarge"),
        AppStrings.Text("RazerHeadsetReverbPlate"),
    ];

    public int SelectedMicReverbIndex
    {
        get => _micReverbRoomModeIndex;
        set => SetField(ref _micReverbRoomModeIndex, Math.Clamp(value, 0, MicReverbModes.Length - 1));
    }

    public double MicReverbIntensity
    {
        get => _micReverbIntensity;
        set
        {
            if (!SetField(ref _micReverbIntensity, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(MicReverbIntensityText));
        }
    }

    public string MicReverbIntensityText => $"{Math.Round(MicReverbIntensity)}";
    public Visibility MicReverbVisibility => _hasMicReverb ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AdvancedAudioVisibility =>
        !IsProtocol40 && (_hasDnd || _hasAutoPause || _hasGestureControl || _hasDolby || _hasNightMode || _hasVoicePrompts)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility MicProcessingVisibility =>
        _hasMicDeEsser || _hasMicLimiter || _hasMicReverb
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool HapticsEnabled { get => _hapticsEnabled; set => SetField(ref _hapticsEnabled, value); }

    public double HapticsStrength
    {
        get => _hapticsStrength;
        set
        {
            if (!SetField(ref _hapticsStrength, Math.Clamp(value, 0, ushort.MaxValue))) return;
            OnPropertyChanged(nameof(HapticsStrengthText));
        }
    }

    public string HapticsStrengthText => $"{Math.Round(HapticsStrength)}";
    public Visibility HapticsVisibility => _hasHaptics ? Visibility.Visible : Visibility.Collapsed;

    public bool MicCompressorEnabled
    {
        get => _micCompressorEnabled;
        set => SetField(ref _micCompressorEnabled, value);
    }

    public double MicCompressorDb
    {
        get => _micCompressorDb;
        set => SetField(ref _micCompressorDb, Math.Clamp(value, -128, 0));
    }

    // The middleware stores ratio, attack and release as raw 16-bit values and never
    // defines a scale, so the UI exposes them unchanged instead of inventing units.
    public double MicCompressorRatio
    {
        get => _micCompressorRatio;
        set => SetField(ref _micCompressorRatio, Math.Clamp(value, 0, ushort.MaxValue));
    }

    public double MicCompressorAttack
    {
        get => _micCompressorAttack;
        set => SetField(ref _micCompressorAttack, Math.Clamp(value, 0, ushort.MaxValue));
    }

    public double MicCompressorRelease
    {
        get => _micCompressorRelease;
        set => SetField(ref _micCompressorRelease, Math.Clamp(value, 0, ushort.MaxValue));
    }

    public Visibility MicCompressorVisibility =>
        _hasMicCompressor ? Visibility.Visible : Visibility.Collapsed;

    public bool MicMonitoringEnabled
    {
        get => _micMonitoringEnabled;
        set => SetField(ref _micMonitoringEnabled, value);
    }

    public Visibility MicMonitoringVisibility =>
        _hasMicMonitoring ? Visibility.Visible : Visibility.Collapsed;

    public int ParametricEqBandIndex
    {
        get => _parametricEqBandIndex;
        set => SetField(ref _parametricEqBandIndex, Math.Clamp(value, 0, 9));
    }

    public bool ParametricEqEnabled
    {
        get => _parametricEqEnabled;
        set => SetField(ref _parametricEqEnabled, value);
    }

    public double ParametricEqFrequency
    {
        get => _parametricEqFrequency;
        set => SetField(ref _parametricEqFrequency, Math.Clamp(value, 0, ushort.MaxValue));
    }

    public double ParametricEqGain
    {
        get => _parametricEqGain;
        set => SetField(ref _parametricEqGain, Math.Clamp(value, -128, 127));
    }

    public double ParametricEqQ
    {
        get => _parametricEqQ;
        set => SetField(ref _parametricEqQ, Math.Clamp(value, 0, 255));
    }

    public Visibility ParametricEqVisibility =>
        _hasParametricEq ? Visibility.Visible : Visibility.Collapsed;

    private RazerAncState[] DeviceAncStates => AncStates;

    public IReadOnlyList<string> AncOptions =>
        DeviceAncStates.Select(state => AppStrings.Text(state switch
        {
            RazerAncState.Off => "RazerHeadsetAncOff",
            RazerAncState.On => "RazerHeadsetAncOn",
            _ => "RazerHeadsetAncAmbient",
        })).ToArray();

    public int SelectedAncIndex
    {
        get => _selectedAncIndex;
        set
        {
            if (!SetField(ref _selectedAncIndex, Math.Clamp(value, 0, DeviceAncStates.Length - 1))) return;
            OnPropertyChanged(nameof(AncLevelVisibility));
        }
    }

    public double AncLevel
    {
        get => _ancLevel;
        set
        {
            if (!SetField(ref _ancLevel, Math.Clamp(value, 0, 10))) return;
            OnPropertyChanged(nameof(AncLevelText));
        }
    }

    public string AncLevelText => $"{Math.Round(AncLevel)} / 10";
    public Visibility AncLevelVisibility => _selectedAncIndex == 1
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility AncVisibility => _hasAnc ? Visibility.Visible : Visibility.Collapsed;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanRefresh));
            OnPropertyChanged(nameof(CanApplyLighting));
            OnPropertyChanged(nameof(CanInstallThx));
            OnPropertyChanged(nameof(CanApplyThx));
        }
    }

    public bool CanEdit => !IsBusy && _probeState is RazerHeadsetProbeState.Ready or RazerHeadsetProbeState.Partial;
    public bool CanRefresh => !IsBusy;
    public bool CanApplyLighting => CanEdit && Lighting is { CanApply: true };
    public string ErrorText { get => _errorText; private set => SetField(ref _errorText, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string OperationStatusText
    {
        get => _operationStatusText;
        private set => SetField(ref _operationStatusText, value);
    }
}
