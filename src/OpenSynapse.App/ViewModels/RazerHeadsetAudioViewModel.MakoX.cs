using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>Mako X firmware effects and auto-sleep controls.</summary>
public sealed partial class RazerHeadsetAudioViewModel
{
    private int _waveDirectionIndex;
    private bool _autoSleepEnabled;
    private int _selectedAutoSleepMinutesIndex = 3;

    private static readonly RazerChromaEffect[] DefaultLightingEffects =
        [RazerChromaEffect.Off, RazerChromaEffect.Static, RazerChromaEffect.Breathing, RazerChromaEffect.Spectrum];

    private static readonly RazerChromaEffect[] JolietLightingEffects =
    [
        RazerChromaEffect.Off, RazerChromaEffect.Static, RazerChromaEffect.Breathing,
        RazerChromaEffect.Spectrum, RazerChromaEffect.Wave, RazerChromaEffect.Wheel,
        RazerChromaEffect.Starlight, RazerChromaEffect.Fire, RazerChromaEffect.AudioMeter,
        RazerChromaEffect.CustomFrame,
    ];

    private static readonly (RazerChromaEffect Effect, string Key)[] DefaultLightingEffectOptions =
    [
        (RazerChromaEffect.Off, "RazerLightingEffectOff"),
        (RazerChromaEffect.Static, "RazerLightingEffectStatic"),
        (RazerChromaEffect.Breathing, "RazerLightingEffectBreathing"),
        (RazerChromaEffect.Spectrum, "RazerLightingEffectSpectrum"),
    ];

    private static readonly (RazerChromaEffect Effect, string Key)[] JolietLightingEffectOptions =
    [
        (RazerChromaEffect.Off, "RazerLightingEffectOff"),
        (RazerChromaEffect.Static, "RazerLightingEffectStatic"),
        (RazerChromaEffect.Breathing, "RazerLightingEffectBreathing"),
        (RazerChromaEffect.Spectrum, "RazerLightingEffectSpectrum"),
        (RazerChromaEffect.Wave, "RazerHidEffectWave"),
        (RazerChromaEffect.Wheel, "RazerLightingEffectWheel"),
        (RazerChromaEffect.Starlight, "RazerHidEffectStarlightSingle"),
        (RazerChromaEffect.Fire, "RazerHidEffectFire"),
        (RazerChromaEffect.AudioMeter, "RazerHidEffectAudioMeter"),
        (RazerChromaEffect.CustomFrame, "RazerHeadsetAmbientAwareness"),
    ];

    private static readonly (byte Flag, string Key)[] MakoXWaveDirections =
    [
        (1, "CoolingPadLightingDirectionLeft"),
        (2, "CoolingPadLightingDirectionRight"),
        (3, "CoolingPadLightingDirectionUp"),
        (4, "CoolingPadLightingDirectionDown"),
    ];

    /// <summary>The rotation effect takes the engine's two directions, not four.</summary>
    private static readonly (bool Counterclockwise, string Key)[] MakoXWheelDirections =
    [
        (false, "CoolingPadLightingDirectionClockwise"),
        (true, "CoolingPadLightingDirectionCounterClockwise"),
    ];

    private static readonly int[] AutoSleepMinuteChoices = [15, 20, 25, 30, 35, 40, 45, 50, 55, 60];

    private RazerChromaEffect[] LightingEffects => IsJolie ? JolietLightingEffects : DefaultLightingEffects;

    private IReadOnlyList<(RazerChromaEffect Effect, string Key)> LightingEffectChoices =>
        IsJolie ? JolietLightingEffectOptions : DefaultLightingEffectOptions;

    private bool IsWheelEffect => LightingEffects[_selectedEffectIndex] == RazerChromaEffect.Wheel;

    private static bool IsDirectionEffect(RazerChromaEffect effect) =>
        effect is RazerChromaEffect.Wave or RazerChromaEffect.Wheel;

    public IReadOnlyList<string> WaveDirectionOptions => (IsWheelEffect
            ? MakoXWheelDirections.Select(direction => direction.Key)
            : MakoXWaveDirections.Select(direction => direction.Key))
        .Select(key => AppStrings.Text(key))
        .ToArray();

    public int SelectedWaveDirectionIndex
    {
        get => _waveDirectionIndex;
        set => SetField(ref _waveDirectionIndex, Math.Clamp(value, 0, DirectionOptionCount - 1));
    }

    private int DirectionOptionCount => IsWheelEffect ? MakoXWheelDirections.Length : MakoXWaveDirections.Length;

    public Visibility WaveDirectionVisibility => IsJolie &&
        IsDirectionEffect(LightingEffects[_selectedEffectIndex])
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility MakoXPowerVisibility => IsJolie ? Visibility.Visible : Visibility.Collapsed;

    public bool AutoSleepEnabled
    {
        get => _autoSleepEnabled;
        set
        {
            if (!SetField(ref _autoSleepEnabled, value)) return;
            OnPropertyChanged(nameof(AutoSleepStateText));
        }
    }

    public string AutoSleepStateText => AppStrings.Text(
        AutoSleepEnabled ? "Text_RazerHidAutoSleepOn" : "Text_RazerHidAutoSleepOff");

    public IReadOnlyList<int> AutoSleepMinuteOptions => AutoSleepMinuteChoices;

    public int SelectedAutoSleepMinutesIndex
    {
        get => _selectedAutoSleepMinutesIndex;
        set => SetField(ref _selectedAutoSleepMinutesIndex,
            Math.Clamp(value, 0, AutoSleepMinuteChoices.Length - 1));
    }

    public int AutoSleepMinutes => AutoSleepMinuteChoices[_selectedAutoSleepMinutesIndex];
}
