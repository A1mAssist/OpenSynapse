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

    // Synapse 4 shows these quick effects for the Mako X, in its own order and
    // wording; Wave has no counterpart on this device and is not offered.
    private static readonly RazerChromaEffect[] JolietLightingEffects =
    [
        RazerChromaEffect.Off, RazerChromaEffect.AudioMeter, RazerChromaEffect.Breathing,
        RazerChromaEffect.Fire, RazerChromaEffect.Spectrum, RazerChromaEffect.Starlight,
        RazerChromaEffect.Static, RazerChromaEffect.Wheel,
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
        (RazerChromaEffect.AudioMeter, "MakoXLightingEffectAudioMeter"),
        (RazerChromaEffect.Breathing, "MakoXLightingEffectBreathing"),
        (RazerChromaEffect.Fire, "MakoXLightingEffectFire"),
        (RazerChromaEffect.Spectrum, "MakoXLightingEffectSpectrum"),
        (RazerChromaEffect.Starlight, "MakoXLightingEffectStarlight"),
        (RazerChromaEffect.Static, "MakoXLightingEffectStatic"),
        (RazerChromaEffect.Wheel, "MakoXLightingEffectWheel"),
    ];

    /// <summary>The rotation effect takes the engine's two directions.</summary>
    private static readonly (bool Counterclockwise, string Key)[] MakoXWheelDirections =
    [
        (false, "CoolingPadLightingDirectionClockwise"),
        (true, "CoolingPadLightingDirectionCounterClockwise"),
    ];

    private static readonly int[] AutoSleepMinuteChoices = [15, 20, 25, 30, 35, 40, 45, 50, 55, 60];

    private RazerChromaEffect[] LightingEffects => IsJolie ? JolietLightingEffects : DefaultLightingEffects;

    private IReadOnlyList<(RazerChromaEffect Effect, string Key)> LightingEffectChoices =>
        IsJolie ? JolietLightingEffectOptions : DefaultLightingEffectOptions;

    private static bool IsDirectionEffect(RazerChromaEffect effect) =>
        effect == RazerChromaEffect.Wheel;

    private string[]? _wheelDirectionLabels;

    public IReadOnlyList<string> WaveDirectionOptions => _wheelDirectionLabels ??=
        MakoXWheelDirections.Select(direction => AppStrings.Text(direction.Key)).ToArray();

    public int SelectedWaveDirectionIndex
    {
        get => _waveDirectionIndex;
        set => SetField(ref _waveDirectionIndex, Math.Clamp(value, 0, DirectionOptionCount - 1));
    }

    private int DirectionOptionCount => MakoXWheelDirections.Length;

    public Visibility WaveDirectionVisibility => IsJolie &&
        IsDirectionEffect(LightingEffects[_selectedEffectIndex])
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility MakoXPowerVisibility => IsJolie || IsCarol && _hasCarolPower ? Visibility.Visible : Visibility.Collapsed;

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
