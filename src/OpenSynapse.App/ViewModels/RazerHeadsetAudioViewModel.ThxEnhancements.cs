using Microsoft.UI.Xaml;
using OpenSynapse.Core.Audio;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Audio;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private bool _thxNormalizationEnabled;
    private int _thxNormalizationLevel;
    private bool _thxVoiceClarityEnabled;
    private int _thxVoiceClarityLevel;
    private bool _thxBassBoostEnabled;
    private int _thxBassBoostLevel;
    private bool _thxSidetoneEnabled;
    private int _thxSidetoneLevel;
    private bool _thxCaptureNormalizationEnabled;
    private int _thxCaptureNormalizationLevel;
    private bool _thxCaptureVocalClarityEnabled;
    private int _thxCaptureVocalClarityLevel;
    private bool _thxCaptureNoiseReductionEnabled;
    private int _thxCaptureNoiseReductionLevel;
    private bool _thxCaptureVoiceGateEnabled;
    private int _thxCaptureVoiceGateLevel;

    public bool ThxNormalizationEnabled
    {
        get => _thxNormalizationEnabled;
        set => SetField(ref _thxNormalizationEnabled, value);
    }

    public int ThxNormalizationLevel
    {
        get => _thxNormalizationLevel;
        set
        {
            if (!SetField(ref _thxNormalizationLevel, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(ThxNormalizationText));
        }
    }

    public string ThxNormalizationText => $"{ThxNormalizationLevel}%";
    public Visibility ThxNormalizationVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool ThxVoiceClarityEnabled
    {
        get => _thxVoiceClarityEnabled;
        set => SetField(ref _thxVoiceClarityEnabled, value);
    }

    public int ThxVoiceClarityLevel
    {
        get => _thxVoiceClarityLevel;
        set
        {
            if (!SetField(ref _thxVoiceClarityLevel, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(ThxVoiceClarityText));
        }
    }

    public string ThxVoiceClarityText => $"{ThxVoiceClarityLevel}%";
    public Visibility ThxVoiceClarityVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool ThxBassBoostEnabled
    {
        get => _thxBassBoostEnabled;
        set => SetField(ref _thxBassBoostEnabled, value);
    }

    public int ThxBassBoostLevel
    {
        get => _thxBassBoostLevel;
        set
        {
            if (!SetField(ref _thxBassBoostLevel, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(ThxBassBoostText));
        }
    }

    public string ThxBassBoostText => $"{ThxBassBoostLevel}%";
    public Visibility ThxBassBoostVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool ThxSidetoneEnabled
    {
        get => _thxSidetoneEnabled;
        set => SetField(ref _thxSidetoneEnabled, value);
    }

    public int ThxSidetoneLevel
    {
        get => _thxSidetoneLevel;
        set
        {
            if (!SetField(ref _thxSidetoneLevel, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(ThxSidetoneText));
        }
    }

    public string ThxSidetoneText => $"{ThxSidetoneLevel}%";
    public Visibility ThxSidetoneVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available &&
        !IsMxicSidetone &&
        (ThxHeadsetAdmission.GetProfile(_connection.ProductId)?.HasSidetone ?? false)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility ThxCaptureVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available &&
        _thxState is { } state &&
        (state.CaptureNormalization is not null ||
         state.CaptureVocalClarity is not null ||
         state.CaptureNoiseReduction is not null ||
         state.CaptureVoiceGate is not null)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool ThxCaptureNormalizationEnabled
    {
        get => _thxCaptureNormalizationEnabled;
        set => SetField(ref _thxCaptureNormalizationEnabled, value);
    }

    public int ThxCaptureNormalizationLevel
    {
        get => _thxCaptureNormalizationLevel;
        set
        {
            if (!SetField(ref _thxCaptureNormalizationLevel, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(ThxCaptureNormalizationText));
        }
    }

    public string ThxCaptureNormalizationText => $"{ThxCaptureNormalizationLevel}%";
    public Visibility ThxCaptureNormalizationVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available &&
        _thxState?.CaptureNormalization is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool ThxCaptureVocalClarityEnabled
    {
        get => _thxCaptureVocalClarityEnabled;
        set => SetField(ref _thxCaptureVocalClarityEnabled, value);
    }

    public int ThxCaptureVocalClarityLevel
    {
        get => _thxCaptureVocalClarityLevel;
        set
        {
            if (!SetField(ref _thxCaptureVocalClarityLevel, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(ThxCaptureVocalClarityText));
        }
    }

    public string ThxCaptureVocalClarityText => $"{ThxCaptureVocalClarityLevel}%";
    public Visibility ThxCaptureVocalClarityVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available &&
        _thxState?.CaptureVocalClarity is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool ThxCaptureNoiseReductionEnabled
    {
        get => _thxCaptureNoiseReductionEnabled;
        set => SetField(ref _thxCaptureNoiseReductionEnabled, value);
    }

    public int ThxCaptureNoiseReductionLevel
    {
        get => _thxCaptureNoiseReductionLevel;
        set
        {
            if (!SetField(ref _thxCaptureNoiseReductionLevel, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(ThxCaptureNoiseReductionText));
        }
    }

    public string ThxCaptureNoiseReductionText => $"{ThxCaptureNoiseReductionLevel}%";
    public Visibility ThxCaptureNoiseReductionVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available &&
        _thxState?.CaptureNoiseReduction is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool ThxCaptureVoiceGateEnabled
    {
        get => _thxCaptureVoiceGateEnabled;
        set => SetField(ref _thxCaptureVoiceGateEnabled, value);
    }

    public int ThxCaptureVoiceGateLevel
    {
        get => _thxCaptureVoiceGateLevel;
        set
        {
            if (!SetField(ref _thxCaptureVoiceGateLevel, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(ThxCaptureVoiceGateText));
        }
    }

    public string ThxCaptureVoiceGateText => $"{ThxCaptureVoiceGateLevel}%";
    public Visibility ThxCaptureVoiceGateVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available &&
        _thxState?.CaptureVoiceGate is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

    [ProfileSetting("headset.thx.normalization", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxNormalizationAsync(CancellationToken cancellationToken = default)
    {
        var previousEnabled = ThxNormalizationEnabled;
        var previousLevel = ThxNormalizationLevel;
        if (!await RunThxAsync(
                (controller, token) => controller.SetEnhancementAsync(
                    ThxAudioEndpoint.Headphones,
                    ThxAudioEnhancementKind.SoundNormalization,
                    ThxNormalizationEnabled,
                    ThxNormalizationLevel,
                    token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.ThxNormalizationEnabled = ThxNormalizationEnabled;
                settings.ThxNormalizationLevel = ThxNormalizationLevel;
            },
            settings =>
            {
                settings.ThxNormalizationEnabled = previousEnabled;
                settings.ThxNormalizationLevel = previousLevel;
            },
            () =>
            {
                ThxNormalizationEnabled = previousEnabled;
                ThxNormalizationLevel = previousLevel;
            },
            async token => await ThxController.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.SoundNormalization,
                previousEnabled,
                previousLevel,
                token),
            cancellationToken);
    }

    [ProfileSetting("headset.thx.voice-clarity", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxVoiceClarityAsync(CancellationToken cancellationToken = default)
    {
        var previousEnabled = ThxVoiceClarityEnabled;
        var previousLevel = ThxVoiceClarityLevel;
        if (!await RunThxAsync(
                (controller, token) => controller.SetEnhancementAsync(
                    ThxAudioEndpoint.Headphones,
                    ThxAudioEnhancementKind.VoiceClarity,
                    ThxVoiceClarityEnabled,
                    ThxVoiceClarityLevel,
                    token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.ThxVoiceClarityEnabled = ThxVoiceClarityEnabled;
                settings.ThxVoiceClarityLevel = ThxVoiceClarityLevel;
            },
            settings =>
            {
                settings.ThxVoiceClarityEnabled = previousEnabled;
                settings.ThxVoiceClarityLevel = previousLevel;
            },
            () =>
            {
                ThxVoiceClarityEnabled = previousEnabled;
                ThxVoiceClarityLevel = previousLevel;
            },
            async token => await ThxController.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.VoiceClarity,
                previousEnabled,
                previousLevel,
                token),
            cancellationToken);
    }

    [ProfileSetting("headset.thx.bass-boost", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxBassBoostAsync(CancellationToken cancellationToken = default)
    {
        var previousEnabled = ThxBassBoostEnabled;
        var previousLevel = ThxBassBoostLevel;
        if (!await RunThxAsync(
                (controller, token) => controller.SetEnhancementAsync(
                    ThxAudioEndpoint.Headphones,
                    ThxAudioEnhancementKind.BassBoost,
                    ThxBassBoostEnabled,
                    ThxBassBoostLevel,
                    token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.ThxBassBoostEnabled = ThxBassBoostEnabled;
                settings.ThxBassBoostLevel = ThxBassBoostLevel;
            },
            settings =>
            {
                settings.ThxBassBoostEnabled = previousEnabled;
                settings.ThxBassBoostLevel = previousLevel;
            },
            () =>
            {
                ThxBassBoostEnabled = previousEnabled;
                ThxBassBoostLevel = previousLevel;
            },
            async token => await ThxController.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.BassBoost,
                previousEnabled,
                previousLevel,
                token),
            cancellationToken);
    }

    [ProfileSetting("headset.thx.sidetone", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxSidetoneAsync(CancellationToken cancellationToken = default)
    {
        var previousEnabled = ThxSidetoneEnabled;
        var previousLevel = ThxSidetoneLevel;
        if (!await RunThxAsync(
                (controller, token) => controller.SetEnhancementAsync(
                    ThxAudioEndpoint.Headphones,
                    ThxAudioEnhancementKind.Sidetone,
                    ThxSidetoneEnabled,
                    ThxSidetoneLevel,
                    token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.ThxSidetoneEnabled = ThxSidetoneEnabled;
                settings.ThxSidetoneLevel = ThxSidetoneLevel;
            },
            settings =>
            {
                settings.ThxSidetoneEnabled = previousEnabled;
                settings.ThxSidetoneLevel = previousLevel;
            },
            () =>
            {
                ThxSidetoneEnabled = previousEnabled;
                ThxSidetoneLevel = previousLevel;
            },
            async token => await ThxController.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.Sidetone,
                previousEnabled,
                previousLevel,
                token),
            cancellationToken);
    }

    [ProfileSetting("headset.thx.capture-normalization", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxCaptureNormalizationAsync(CancellationToken cancellationToken = default)
    {
        var previousEnabled = ThxCaptureNormalizationEnabled;
        var previousLevel = ThxCaptureNormalizationLevel;
        if (!await RunThxAsync(
                (controller, token) => controller.SetEnhancementAsync(
                    ThxAudioEndpoint.Headphones,
                    ThxAudioEnhancementKind.CaptureNormalization,
                    ThxCaptureNormalizationEnabled,
                    ThxCaptureNormalizationLevel,
                    token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.ThxCaptureNormalizationEnabled = ThxCaptureNormalizationEnabled;
                settings.ThxCaptureNormalizationLevel = ThxCaptureNormalizationLevel;
            },
            settings =>
            {
                settings.ThxCaptureNormalizationEnabled = previousEnabled;
                settings.ThxCaptureNormalizationLevel = previousLevel;
            },
            () =>
            {
                ThxCaptureNormalizationEnabled = previousEnabled;
                ThxCaptureNormalizationLevel = previousLevel;
            },
            async token => await ThxController.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.CaptureNormalization,
                previousEnabled,
                previousLevel,
                token),
            cancellationToken);
    }

    [ProfileSetting("headset.thx.capture-vocal-clarity", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxCaptureVocalClarityAsync(CancellationToken cancellationToken = default)
    {
        var previousEnabled = ThxCaptureVocalClarityEnabled;
        var previousLevel = ThxCaptureVocalClarityLevel;
        if (!await RunThxAsync(
                (controller, token) => controller.SetEnhancementAsync(
                    ThxAudioEndpoint.Headphones,
                    ThxAudioEnhancementKind.CaptureVocalClarity,
                    ThxCaptureVocalClarityEnabled,
                    ThxCaptureVocalClarityLevel,
                    token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.ThxCaptureVocalClarityEnabled = ThxCaptureVocalClarityEnabled;
                settings.ThxCaptureVocalClarityLevel = ThxCaptureVocalClarityLevel;
            },
            settings =>
            {
                settings.ThxCaptureVocalClarityEnabled = previousEnabled;
                settings.ThxCaptureVocalClarityLevel = previousLevel;
            },
            () =>
            {
                ThxCaptureVocalClarityEnabled = previousEnabled;
                ThxCaptureVocalClarityLevel = previousLevel;
            },
            async token => await ThxController.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.CaptureVocalClarity,
                previousEnabled,
                previousLevel,
                token),
            cancellationToken);
    }

    [ProfileSetting("headset.thx.capture-noise-reduction", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxCaptureNoiseReductionAsync(CancellationToken cancellationToken = default)
    {
        var previousEnabled = ThxCaptureNoiseReductionEnabled;
        var previousLevel = ThxCaptureNoiseReductionLevel;
        if (!await RunThxAsync(
                (controller, token) => controller.SetEnhancementAsync(
                    ThxAudioEndpoint.Headphones,
                    ThxAudioEnhancementKind.CaptureNoiseReduction,
                    ThxCaptureNoiseReductionEnabled,
                    ThxCaptureNoiseReductionLevel,
                    token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.ThxCaptureNoiseReductionEnabled = ThxCaptureNoiseReductionEnabled;
                settings.ThxCaptureNoiseReductionLevel = ThxCaptureNoiseReductionLevel;
            },
            settings =>
            {
                settings.ThxCaptureNoiseReductionEnabled = previousEnabled;
                settings.ThxCaptureNoiseReductionLevel = previousLevel;
            },
            () =>
            {
                ThxCaptureNoiseReductionEnabled = previousEnabled;
                ThxCaptureNoiseReductionLevel = previousLevel;
            },
            async token => await ThxController.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.CaptureNoiseReduction,
                previousEnabled,
                previousLevel,
                token),
            cancellationToken);
    }

    [ProfileSetting("headset.thx.capture-voice-gate", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxCaptureVoiceGateAsync(CancellationToken cancellationToken = default)
    {
        var previousEnabled = ThxCaptureVoiceGateEnabled;
        var previousLevel = ThxCaptureVoiceGateLevel;
        if (!await RunThxAsync(
                (controller, token) => controller.SetEnhancementAsync(
                    ThxAudioEndpoint.Headphones,
                    ThxAudioEnhancementKind.CaptureVoiceGate,
                    ThxCaptureVoiceGateEnabled,
                    ThxCaptureVoiceGateLevel,
                    token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.ThxCaptureVoiceGateEnabled = ThxCaptureVoiceGateEnabled;
                settings.ThxCaptureVoiceGateLevel = ThxCaptureVoiceGateLevel;
            },
            settings =>
            {
                settings.ThxCaptureVoiceGateEnabled = previousEnabled;
                settings.ThxCaptureVoiceGateLevel = previousLevel;
            },
            () =>
            {
                ThxCaptureVoiceGateEnabled = previousEnabled;
                ThxCaptureVoiceGateLevel = previousLevel;
            },
            async token => await ThxController.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.CaptureVoiceGate,
                previousEnabled,
                previousLevel,
                token),
            cancellationToken);
    }
}
