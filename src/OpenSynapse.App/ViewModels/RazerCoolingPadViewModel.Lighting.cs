using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerCoolingPadViewModel
{
    private IReadOnlyList<byte> _lightingEffects = [];
    private int _lightingBrightness = 100;
    private int _selectedLightingEffectIndex;
    private int _selectedLightingDirectionIndex;
    private Color _lightingColor = Color.FromArgb(255, 0, 255, 0);
    private Color _lightingSecondColor = Color.FromArgb(255, 0, 0, 255);
    private int _starlightColorModeIndex = 1;
    private int _starlightSpeedIndex = 1;
    private string _lightingStatusText = string.Empty;
    private bool _hasLighting;

    public Visibility LightingVisibility =>
        _hasLighting ? Visibility.Visible : Visibility.Collapsed;

    public int LightingBrightness
    {
        get => _lightingBrightness;
        set
        {
            if (SetField(ref _lightingBrightness, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(LightingBrightnessText));
            }
        }
    }

    public string LightingBrightnessText => $"{LightingBrightness}%";

    public IReadOnlyList<string> LightingEffectOptions =>
        _lightingEffects.Select(DescribeEffect).ToArray();

    public int SelectedLightingEffectIndex
    {
        get => _selectedLightingEffectIndex;
        set
        {
            if (SetField(ref _selectedLightingEffectIndex,
                    Math.Clamp(value, 0, Math.Max(0, _lightingEffects.Count - 1))))
            {
                _selectedLightingDirectionIndex = 0;
                OnPropertyChanged(nameof(LightingColorVisibility));
                OnPropertyChanged(nameof(LightingDirectionVisibility));
                OnPropertyChanged(nameof(LightingDirectionOptions));
                OnPropertyChanged(nameof(SelectedLightingDirectionIndex));
                OnPropertyChanged(nameof(StarlightColorModeVisibility));
                OnPropertyChanged(nameof(StarlightSpeedVisibility));
                OnPropertyChanged(nameof(LightingSecondColorVisibility));
            }
        }
    }

    private byte SelectedLightingEffectId =>
        _lightingEffects.Count == 0
            ? (byte)0
            : _lightingEffects[Math.Clamp(_selectedLightingEffectIndex, 0, _lightingEffects.Count - 1)];

    public Visibility LightingDirectionVisibility =>
        SelectedLightingEffectId is 4 or 13 ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<string> LightingDirectionOptions =>
        SelectedLightingEffectId == 13
            ? [AppStrings.Text("CoolingPadLightingDirectionClockwise"),
               AppStrings.Text("CoolingPadLightingDirectionCounterClockwise")]
            : [AppStrings.Text("CoolingPadLightingDirectionLeft"),
               AppStrings.Text("CoolingPadLightingDirectionRight"),
               AppStrings.Text("CoolingPadLightingDirectionUp"),
               AppStrings.Text("CoolingPadLightingDirectionDown")];

    public int SelectedLightingDirectionIndex
    {
        get => _selectedLightingDirectionIndex;
        set
        {
            var maximum = SelectedLightingEffectId == 13 ? 1 : 3;
            if (SetField(ref _selectedLightingDirectionIndex, Math.Clamp(value, 0, maximum)))
            {
                OnPropertyChanged(nameof(LightingDirectionOptions));
            }
        }
    }

    public Color LightingColor
    {
        get => _lightingColor;
        set => SetField(ref _lightingColor, value);
    }

    public Color LightingSecondColor
    {
        get => _lightingSecondColor;
        set => SetField(ref _lightingSecondColor, value);
    }

    public Visibility StarlightColorModeVisibility =>
        SelectedLightingEffectId == 7 ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<string> StarlightColorModeOptions =>
        AppStrings.Texts("BladeStarlightRandom", "BladeStarlightSingle", "BladeStarlightDual");

    public int StarlightColorModeIndex
    {
        get => _starlightColorModeIndex;
        set
        {
            if (SetField(ref _starlightColorModeIndex, Math.Clamp(value, 0, 2)))
            {
                OnPropertyChanged(nameof(LightingColorVisibility));
                OnPropertyChanged(nameof(LightingSecondColorVisibility));
            }
        }
    }

    public Visibility StarlightSpeedVisibility =>
        SelectedLightingEffectId == 7 ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<string> StarlightSpeedOptions =>
        AppStrings.Texts("BladeAnimationSpeedSlow", "BladeAnimationSpeedMedium", "BladeAnimationSpeedFast");

    public int StarlightSpeedIndex
    {
        get => _starlightSpeedIndex;
        set => SetField(ref _starlightSpeedIndex, Math.Clamp(value, 0, 2));
    }

    public Visibility LightingColorVisibility =>
        SelectedLightingEffectId is 1 or 2 ||
        (SelectedLightingEffectId == 7 && _starlightColorModeIndex != 0)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility LightingSecondColorVisibility =>
        SelectedLightingEffectId == 7 && _starlightColorModeIndex == 2
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool CanApplyLighting =>
        IsConnected && !IsBusy && _hasLighting && _lightingEffects.Count > 0;
    public string LightingStatusText
    {
        get => _lightingStatusText;
        private set => SetField(ref _lightingStatusText, value);
    }

    public async Task ApplyLightingAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyLighting || _service is null || _lightingEffects.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var effect = _lightingEffects[SelectedLightingEffectIndex];
            byte[] colors;
            byte flags;
            byte rate;
            if (effect == 7)
            {
                var random = _starlightColorModeIndex == 0;
                var dualColor = _starlightColorModeIndex == 2;
                colors = random
                    ? []
                    : dualColor
                        ? [LightingColor.R, LightingColor.G, LightingColor.B,
                           LightingSecondColor.R, LightingSecondColor.G, LightingSecondColor.B]
                        : [LightingColor.R, LightingColor.G, LightingColor.B];
                flags = RazerCoolingPadProtocol.LightingFlagsForStarlight(random, dualColor);
                rate = RazerCoolingPadProtocol.LightingRateForStarlight(_starlightSpeedIndex + 1);
            }
            else
            {
                colors = effect is 1 or 2
                    ? [LightingColor.R, LightingColor.G, LightingColor.B]
                    : [];
                flags = RazerCoolingPadProtocol.LightingFlagsFor(effect, SelectedLightingDirectionIndex);
                rate = RazerCoolingPadProtocol.LightingRateFor(effect);
            }

            var state = await _service.SetLightingAsync(
                LightingBrightness,
                effect,
                flags,
                rate,
                colors,
                cancellationToken).ConfigureAwait(false);
            ApplyLightingState(state);
            LightingStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            LightingStatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadLightingCoreAsync(CancellationToken cancellationToken)
    {
        if (_service is null)
        {
            return;
        }

        try
        {
            var state = await _service.ReadLightingAsync(cancellationToken).ConfigureAwait(false);
            if (state is null)
            {
                _hasLighting = false;
                LightingStatusText = string.Empty;
                OnPropertyChanged(nameof(LightingVisibility));
                OnPropertyChanged(nameof(CanApplyLighting));
                return;
            }

            ApplyLightingState(state);
            LightingStatusText = string.Empty;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _hasLighting = false;
            LightingStatusText = exception.Message;
            OnPropertyChanged(nameof(LightingVisibility));
            OnPropertyChanged(nameof(CanApplyLighting));
        }
    }

    private void ApplyLightingState(RazerCoolingPadLightingState state)
    {
        _hasLighting = true;
        _lightingEffects = state.EffectIds
            .Where(id => id is 1 or 2 or 3 or 4 or 7 or 8 or 11 or 12 or 13 or 19)
            .ToArray();
        LightingBrightness = state.BrightnessPercent;
        var selected = _lightingEffects.ToList().IndexOf(state.EffectId);
        _selectedLightingEffectIndex = selected >= 0 ? selected : 0;
        if (state.EffectId is 4 or 13)
        {
            _selectedLightingDirectionIndex = state.EffectId == 13
                ? Math.Clamp(state.Flags - 1, 0, 1)
                : Math.Clamp(state.Flags - 1, 0, 3);
        }
        if (state.Colors.Count >= 3)
        {
            LightingColor = Color.FromArgb(255, state.Colors[0], state.Colors[1], state.Colors[2]);
        }
        if (state.EffectId == 7)
        {
            _starlightColorModeIndex = Math.Clamp((int)state.Flags, 0, 2);
            _starlightSpeedIndex = Math.Clamp(state.Rate - 1, 0, 2);
        }
        if (state.Colors.Count >= 6)
        {
            LightingSecondColor = Color.FromArgb(255, state.Colors[3], state.Colors[4], state.Colors[5]);
        }

        OnPropertyChanged(nameof(LightingVisibility));
        OnPropertyChanged(nameof(LightingEffectOptions));
        OnPropertyChanged(nameof(SelectedLightingEffectIndex));
        OnPropertyChanged(nameof(LightingColorVisibility));
        OnPropertyChanged(nameof(LightingSecondColorVisibility));
        OnPropertyChanged(nameof(LightingDirectionVisibility));
        OnPropertyChanged(nameof(LightingDirectionOptions));
        OnPropertyChanged(nameof(SelectedLightingDirectionIndex));
        OnPropertyChanged(nameof(StarlightColorModeVisibility));
        OnPropertyChanged(nameof(StarlightColorModeOptions));
        OnPropertyChanged(nameof(StarlightColorModeIndex));
        OnPropertyChanged(nameof(StarlightSpeedVisibility));
        OnPropertyChanged(nameof(StarlightSpeedOptions));
        OnPropertyChanged(nameof(StarlightSpeedIndex));
        OnPropertyChanged(nameof(CanApplyLighting));
    }

    private static string DescribeEffect(byte effectId) => effectId switch
    {
        1 => AppStrings.Text("RazerLightingEffectStatic"),
        2 => AppStrings.Text("RazerLightingEffectBreathing"),
        3 => AppStrings.Text("RazerLightingEffectSpectrum"),
        4 => AppStrings.Text("RazerLightingEffectWave"),
        7 => AppStrings.Text("Text_51212D25"),
        8 => AppStrings.Text("RazerLightingEffectFire"),
        11 => AppStrings.Text("RazerLightingEffectAmbient"),
        12 => AppStrings.Text("RazerLightingEffectAudioMeter"),
        13 => AppStrings.Text("RazerLightingEffectWheel"),
        19 => AppStrings.Text("RazerLightingEffectTidal"),
        _ => $"Effect {effectId}",
    };
}
