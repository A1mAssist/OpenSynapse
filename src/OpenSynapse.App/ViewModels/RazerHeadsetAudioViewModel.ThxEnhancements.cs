using Microsoft.UI.Xaml;
using OpenSynapse.Core.Audio;

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
        _thxState is not null ? Visibility.Visible : Visibility.Collapsed;

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

    public Task ApplyThxNormalizationAsync(CancellationToken cancellationToken = default) =>
        RunThxAsync(
            (controller, token) => controller.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.SoundNormalization,
                ThxNormalizationEnabled,
                ThxNormalizationLevel,
                token),
            cancellationToken);

    public Task ApplyThxVoiceClarityAsync(CancellationToken cancellationToken = default) =>
        RunThxAsync(
            (controller, token) => controller.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.VoiceClarity,
                ThxVoiceClarityEnabled,
                ThxVoiceClarityLevel,
                token),
            cancellationToken);

    public Task ApplyThxBassBoostAsync(CancellationToken cancellationToken = default) =>
        RunThxAsync(
            (controller, token) => controller.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.BassBoost,
                ThxBassBoostEnabled,
                ThxBassBoostLevel,
                token),
            cancellationToken);

    public Task ApplyThxSidetoneAsync(CancellationToken cancellationToken = default) =>
        RunThxAsync(
            (controller, token) => controller.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.Sidetone,
                ThxSidetoneEnabled,
                ThxSidetoneLevel,
                token),
            cancellationToken);
}