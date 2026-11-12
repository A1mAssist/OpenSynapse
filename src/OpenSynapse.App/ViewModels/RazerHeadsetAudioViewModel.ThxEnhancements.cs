using Microsoft.UI.Xaml;
using OpenSynapse.Core.Audio;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

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
        _thxState?.SoundNormalization is not null ? Visibility.Visible : Visibility.Collapsed;

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
        _thxState?.VoiceClarity is not null ? Visibility.Visible : Visibility.Collapsed;

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
        _thxState?.BassBoost is not null ? Visibility.Visible : Visibility.Collapsed;

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
        _thxState?.Sidetone is not null ? Visibility.Visible : Visibility.Collapsed;

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
}
