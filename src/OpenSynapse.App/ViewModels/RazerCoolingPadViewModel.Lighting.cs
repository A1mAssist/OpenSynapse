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
    private Color _lightingColor = Color.FromArgb(255, 0, 255, 0);
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
                OnPropertyChanged(nameof(LightingColorVisibility));
            }
        }
    }

    public Color LightingColor
    {
        get => _lightingColor;
        set => SetField(ref _lightingColor, value);
    }

    public Visibility LightingColorVisibility =>
        _lightingEffects.Count > 0 && _lightingEffects[SelectedLightingEffectIndex] is 1 or 2
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
            byte[] colors = effect is 1 or 2
                ? [LightingColor.R, LightingColor.G, LightingColor.B]
                : [];
            var state = await _service.SetLightingAsync(
                LightingBrightness,
                effect,
                flags: effect == 2 ? (byte)1 : (byte)0,
                rate: 0,
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
        _lightingEffects = state.EffectIds.Where(id => id is 1 or 2 or 3).ToArray();
        LightingBrightness = state.BrightnessPercent;
        var selected = _lightingEffects.ToList().IndexOf(state.EffectId);
        _selectedLightingEffectIndex = selected >= 0 ? selected : 0;
        if (state.Colors.Count >= 3)
        {
            LightingColor = Color.FromArgb(255, state.Colors[0], state.Colors[1], state.Colors[2]);
        }

        OnPropertyChanged(nameof(LightingVisibility));
        OnPropertyChanged(nameof(LightingEffectOptions));
        OnPropertyChanged(nameof(SelectedLightingEffectIndex));
        OnPropertyChanged(nameof(LightingColorVisibility));
        OnPropertyChanged(nameof(CanApplyLighting));
    }

    private static string DescribeEffect(byte effectId) => effectId switch
    {
        1 => AppStrings.Text("RazerLightingEffectStatic"),
        2 => AppStrings.Text("RazerLightingEffectBreathing"),
        3 => AppStrings.Text("RazerLightingEffectSpectrum"),
        _ => $"Effect {effectId}",
    };
}
