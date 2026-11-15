using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Lighting;
using Windows.UI;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BladeWorkspace
{
    /// <summary>
    /// Effect names parallel <see cref="BladeLightingModes"/>, so a model that admits only
    /// firmware-rendered uniform effects gets a matching short list instead of entries that
    /// would not apply to it.
    /// </summary>
    public IReadOnlyList<string> LightingModeOptions =>
        BladeLightingDialectPresentation.ModeTextKeys(
                LightingDialect,
                _blade._bladeChromaAnimation is not null)
            .Select(AppStrings.Text)
            .ToArray();

    public Visibility LightingEffectVisibility => ActiveBladeLightingModes.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    private void SetChromaAnimation(ChromaFileAnimation? animation)
    {
        var hadAnimation = _blade._bladeChromaAnimation is not null;
        _blade._bladeChromaAnimation = animation;
        if (hadAnimation == (animation is not null))
        {
            return;
        }

        OnPropertyChanged(nameof(LightingModeOptions));
        OnPropertyChanged(nameof(LightingEffectVisibility));
        if (animation is null &&
            LightingModeIndex ==
            Array.IndexOf(BladeLightingModes, BladeLightingMode.ChromaFile))
        {
            LightingModeIndex = -1;
        }
    }
    public IReadOnlyList<string> LightingPowerProfileOptions =>
        [
            AppStrings.Text("BladeLightingPowerCurrent"),
            AppStrings.Text("BladeLightingPowerPluggedIn"),
            AppStrings.Text("BladeLightingPowerLowAdapter"),
            AppStrings.Text("BladeLightingPowerBattery"),
        ];
    public IReadOnlyList<string> PerformancePowerProfileOptions => LightingPowerProfileOptions;
    public IReadOnlyList<string> RefreshRatePowerProfileOptions => LightingPowerProfileOptions;
    public IReadOnlyList<string> LogoPowerProfileOptions => LightingPowerProfileOptions;
    public bool LightingEnabled
    {
        get => _blade._bladeLightingEnabled;
        set => SetField(ref _blade._bladeLightingEnabled, value);
    }
    public bool ChromaOverrideEnabled
    {
        get => _blade._bladeChromaOverrideEnabled;
        set => SetField(ref _blade._bladeChromaOverrideEnabled, value);
    }
    public string ChromaStatusText => _blade._bladeChromaStatusText;
    public Visibility ChromaStatusVisibility =>
        string.IsNullOrWhiteSpace(_blade._bladeChromaStatusText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    private void SetChromaStatus(string text)
    {
        if (string.Equals(_blade._bladeChromaStatusText, text, StringComparison.Ordinal))
        {
            return;
        }

        _blade._bladeChromaStatusText = text;
        OnPropertyChanged(nameof(ChromaStatusText));
        OnPropertyChanged(nameof(ChromaStatusVisibility));
    }
    /// <summary>
    /// The 2023 generation writes brightness and effects through the telemetry reader, so it
    /// needs no lighting controller. Every other generation still requires one.
    /// </summary>
    public bool CanSetLightingSettings => _blade._canSetBladeBrightness &&
        BladeModelCapabilities.AllowsControlWrites(_blade._bladeProductId) &&
        CanApplyLightingDialect;
    public int LightingPowerProfileIndex
    {
        get => _bladeLightingPowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 3);
            if (SetField(ref _bladeLightingPowerProfileIndex, next))
            {
                RefreshLightingEditor();
            }
        }
    }
    public int PerformancePowerProfileIndex
    {
        get => _bladePerformancePowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 3);
            if (SetField(ref _bladePerformancePowerProfileIndex, next))
            {
                RefreshPerformanceEditor();
            }
        }
    }
    public int RefreshRatePowerProfileIndex
    {
        get => _bladeRefreshRatePowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 3);
            if (SetField(ref _bladeRefreshRatePowerProfileIndex, next))
            {
                RefreshInternalDisplayRateEditor();
            }
        }
    }
    public int LogoPowerProfileIndex
    {
        get => _bladeLogoPowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 3);
            if (SetField(ref _bladeLogoPowerProfileIndex, next))
            {
                RefreshLogoEditor();
            }
        }
    }
    public int LightingModeIndex
    {
        get => _blade._bladeLightingModeIndex;
        set
        {
            if (SetField(ref _blade._bladeLightingModeIndex, value))
            {
                if (SelectedLightingMode is { } mode)
                {
                    RestoreLightingColors(mode);
                }
                OnPropertyChanged(nameof(LightingColorVisibility));
                OnPropertyChanged(nameof(LightingPresetVisibility));
                OnPropertyChanged(nameof(LightingRenderModeVisibility));
                OnPropertyChanged(nameof(LightingSecondColorVisibility));
                OnPropertyChanged(nameof(LightingTertiaryColorVisibility));
                OnPropertyChanged(nameof(WaveDirectionVisibility));
                OnPropertyChanged(nameof(ReactiveSpeedVisibility));
                OnPropertyChanged(nameof(StarlightSpeedVisibility));
                OnPropertyChanged(nameof(StarlightColorModeVisibility));
                OnPropertyChanged(nameof(AnimationSpeedVisibility));
            }
        }
    }
    /// <summary>
    /// Resolves the selected index against the list this model actually renders. Indexing
    /// the full catalog would map the short 2023 list onto the wrong effects.
    /// </summary>
    private BladeLightingMode? SelectedLightingMode
    {
        get
        {
            var modes = ActiveBladeLightingModes;
            return LightingModeIndex >= 0 && LightingModeIndex < modes.Count
                ? modes[LightingModeIndex]
                : null;
        }
    }
    public IReadOnlyList<string> LightingRenderModeOptions => AppStrings.Texts(
        "LightingRenderFirmware", "LightingRenderSoftware");
    public int LightingRenderModeIndex
    {
        get => _blade._bladeLightingRenderModeIndex;
        set
        {
            if (value is not (0 or 1))
            {
                OnPropertyChanged();
                return;
            }
            if (SetField(ref _blade._bladeLightingRenderModeIndex, value))
            {
                OnPropertyChanged(nameof(LightingColorVisibility));
                OnPropertyChanged(nameof(LightingPresetVisibility));
                OnPropertyChanged(nameof(LightingSecondColorVisibility));
                OnPropertyChanged(nameof(LightingTertiaryColorVisibility));
            }
        }
    }
    public Visibility LightingRenderModeVisibility =>
        LightingDialect is not (BladeLightingDialect.FirmwareEffect or BladeLightingDialect.Unknown or BladeLightingDialect.LedMatrixBlade) &&
        SelectedLightingMode is BladeLightingMode.Wave or BladeLightingMode.Spectrum
        ? Visibility.Visible
        : Visibility.Collapsed;
    private bool BladeFirmwarePaletteHidden => LightingRenderModeVisibility == Visibility.Visible &&
        LightingRenderModeIndex == 0;
    public Visibility LightingPresetVisibility => SelectedLightingMode == BladeLightingMode.Starlight
        ? LightingColorVisibility : LightingSecondColorVisibility;
    public Visibility LightingColorVisibility => !BladeFirmwarePaletteHidden &&
        (LightingDialect == BladeLightingDialect.LedMatrixBlade
            ? SelectedLightingMode is BladeLightingMode.Static or BladeLightingMode.Breathing or BladeLightingMode.Reactive
            : !UsesFirmwareKeyboardLighting &&
                (SelectedLightingMode is
                BladeLightingMode.Static or BladeLightingMode.Breathing or BladeLightingMode.Reactive or
                BladeLightingMode.Ripple or BladeLightingMode.Tidal or BladeLightingMode.Spectrum or
                BladeLightingMode.Wave or BladeLightingMode.Fire or BladeLightingMode.Wheel or BladeLightingMode.AudioMeter ||
                (SelectedLightingMode == BladeLightingMode.Starlight && StarlightColorModeIndex != 0)))
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility LightingSecondColorVisibility => !BladeFirmwarePaletteHidden &&
        (LightingDialect == BladeLightingDialect.LedMatrixBlade
            ? false
            : !UsesFirmwareKeyboardLighting &&
                (SelectedLightingMode is BladeLightingMode.Tidal or
                BladeLightingMode.Spectrum or BladeLightingMode.Wave or BladeLightingMode.Fire or BladeLightingMode.Wheel or BladeLightingMode.AudioMeter ||
                (SelectedLightingMode == BladeLightingMode.Starlight && StarlightColorModeIndex == 2)))
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility LightingTertiaryColorVisibility => !BladeFirmwarePaletteHidden &&
        (LightingDialect == BladeLightingDialect.LedMatrixBlade
            ? false
            : !UsesFirmwareKeyboardLighting &&
                SelectedLightingMode is
                BladeLightingMode.Spectrum or BladeLightingMode.Wave or BladeLightingMode.Fire or
                BladeLightingMode.Wheel or BladeLightingMode.AudioMeter)
        ? Visibility.Visible
        : Visibility.Collapsed;
    /// <summary>
    /// The 2023 generation stores a fixed rightward wave direction, so offering the
    /// direction selector there would present a control that cannot change anything.
    /// </summary>
    public Visibility WaveDirectionVisibility => !UsesFirmwareKeyboardLighting &&
        SelectedLightingMode is BladeLightingMode.Wave or BladeLightingMode.Wheel
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility ReactiveSpeedVisibility => SelectedLightingMode == BladeLightingMode.Reactive
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility StarlightSpeedVisibility => SelectedLightingMode == BladeLightingMode.Starlight
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility StarlightColorModeVisibility => SelectedLightingMode == BladeLightingMode.Starlight
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility AnimationSpeedVisibility => SelectedLightingMode is BladeLightingMode.Wheel or BladeLightingMode.Tidal
        ? Visibility.Visible
        : Visibility.Collapsed;
    public IReadOnlyList<string> AnimationSpeedOptions => AppStrings.Texts("BladeAnimationSpeedSlow", "BladeAnimationSpeedMedium", "BladeAnimationSpeedFast");
    public int AnimationSpeedIndex { get => _blade._bladeAnimationSpeedIndex; set => SetField(ref _blade._bladeAnimationSpeedIndex, value); }
    public IReadOnlyList<string> WaveDirectionOptions => AppStrings.Texts("Text_FB3FF0D8", "Text_883A50D7");
    public int WaveDirectionIndex
    {
        get => _blade._bladeWaveDirectionIndex;
        set
        {
            if (value >= 0 && value < BladeWaveDirections.Length)
            {
                SetField(ref _blade._bladeWaveDirectionIndex, value);
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }
    public IReadOnlyList<string> ReactiveSpeedOptions => AppStrings.Texts("BladeReactiveSpeedLevel1", "BladeReactiveSpeedLevel2", "BladeReactiveSpeedLevel3", "BladeReactiveSpeedLevel4");
    public int ReactiveSpeedIndex { get => _blade._bladeReactiveSpeedIndex; set => SetField(ref _blade._bladeReactiveSpeedIndex, value); }
    public IReadOnlyList<string> StarlightSpeedOptions => AnimationSpeedOptions;
    public int StarlightSpeedIndex { get => _blade._bladeStarlightSpeedIndex; set => SetField(ref _blade._bladeStarlightSpeedIndex, value); }
    public IReadOnlyList<string> StarlightColorModeOptions => AppStrings.Texts("BladeStarlightRandom", "BladeStarlightSingle", "BladeStarlightDual");
    public int StarlightColorModeIndex { get => _blade._bladeStarlightColorModeIndex; set { if (SetField(ref _blade._bladeStarlightColorModeIndex, value)) { OnPropertyChanged(nameof(LightingColorVisibility)); OnPropertyChanged(nameof(LightingPresetVisibility)); OnPropertyChanged(nameof(LightingSecondColorVisibility)); } } }
    public Color LightingColor { get => _blade._bladeLightingColor; set => SetField(ref _blade._bladeLightingColor, value); }
    public Color LightingSecondColor { get => _blade._bladeLightingSecondColor; set => SetField(ref _blade._bladeLightingSecondColor, value); }
    public Color LightingTertiaryColor { get => _blade._bladeLightingTertiaryColor; set => SetField(ref _blade._bladeLightingTertiaryColor, value); }
    /// <summary>
    /// Gate for the effect selector. The 2023 generation selects firmware effects through the
    /// telemetry reader, so the matrix-based lighting controller is not required there.
    /// </summary>
    public bool CanSetLighting => _blade._canSetBladeBrightness &&
        BladeModelCapabilities.AllowsControlWrites(_blade._bladeProductId) &&
        CanApplyLightingDialect;

    /// <summary>
    /// Both Blade generations have an admitted keyboard-lighting report, so the card is
    /// always available; only the effect list and color controls differ by model.
    /// </summary>
    public Visibility LightingCardVisibility => Visibility.Visible;

    /// <summary>
    /// Effect list for the selected model. A 2023 machine renders a short firmware list;
    /// every other generation keeps the full effect set with the optional Chroma entry.
    /// </summary>
    /// <summary>
    /// Page-facing view of the selected effect. Controls that need to react to the
    /// selection resolve it here so the per-model list stays in one place.
    /// </summary>
    public BladeLightingMode? SelectedLightingModeForPage => SelectedLightingMode;

    private IReadOnlyList<BladeLightingMode> ActiveBladeLightingModes =>
        BladeLightingDialectPresentation.Modes(
            LightingDialect,
            _blade._bladeChromaAnimation is not null);
}
