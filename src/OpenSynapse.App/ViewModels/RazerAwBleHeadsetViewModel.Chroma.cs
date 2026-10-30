using Microsoft.UI.Xaml;
using Windows.UI;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerAwBleHeadsetViewModel
{
    public double ChromaBrightness
    {
        get => _chromaBrightness;
        set
        {
            if (SetField(ref _chromaBrightness, Math.Clamp(value, 0d, 100d)))
            {
                OnPropertyChanged(nameof(ChromaBrightnessText));
            }
        }
    }

    public string ChromaBrightnessText =>
        AppStrings.FormatText("AwBleHeadsetPercent", (int)Math.Round(ChromaBrightness));

    public IReadOnlyList<string> ChromaEffectOptions =>
        ChromaEffects.Select(effect => AppStrings.Text(effect.Key)).ToArray();

    public int SelectedChromaEffectIndex
    {
        get => _selectedChromaEffectIndex;
        set
        {
            if (SetField(ref _selectedChromaEffectIndex, Math.Clamp(value, 0, ChromaEffects.Length - 1)))
            {
                NotifyChromaColorVisibility();
            }
        }
    }

    public Color ChromaPrimaryColor
    {
        get => _chromaPrimaryColor;
        set => SetField(ref _chromaPrimaryColor, value);
    }

    public Color ChromaSecondaryColor
    {
        get => _chromaSecondaryColor;
        set => SetField(ref _chromaSecondaryColor, value);
    }

    public Visibility ChromaBrightnessVisibility
    {
        get => _chromaBrightnessVisibility;
        private set => SetVisibility(
            ref _chromaBrightnessVisibility, value, nameof(ChromaBrightnessVisibility));
    }

    public Visibility ChromaEffectVisibility
    {
        get => _chromaEffectVisibility;
        private set => SetVisibility(
            ref _chromaEffectVisibility, value, nameof(ChromaEffectVisibility));
    }

    public Visibility ChromaPrimaryColorVisibility
    {
        get => _chromaPrimaryColorVisibility;
        private set => SetVisibility(
            ref _chromaPrimaryColorVisibility, value, nameof(ChromaPrimaryColorVisibility));
    }

    public Visibility ChromaSecondaryColorVisibility
    {
        get => _chromaSecondaryColorVisibility;
        private set => SetVisibility(
            ref _chromaSecondaryColorVisibility, value, nameof(ChromaSecondaryColorVisibility));
    }

    public Task ApplyChromaBrightnessAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetChromaBrightnessAsync(
                (int)Math.Round(ChromaBrightness), cancellationToken),
            applied => ChromaBrightness = applied);

    public Task ApplyChromaEffectAsync(CancellationToken cancellationToken = default)
    {
        var effect = ChromaEffects[
            Math.Clamp(SelectedChromaEffectIndex, 0, ChromaEffects.Length - 1)].Value;
        return RunWriteAsync(
            service => service.SetChromaEffectAsync(
                effect,
                ToRgb(ChromaPrimaryColor),
                ToRgb(ChromaSecondaryColor),
                cancellationToken),
            ApplyChromaState);
    }

    private void ApplyChromaState(RazerAwChromaState state)
    {
        var index = Array.FindIndex(ChromaEffects, effect => effect.Value == state.Effect);
        if (index >= 0)
        {
            SelectedChromaEffectIndex = index;
        }

        ChromaEffectVisibility = index >= 0 ? Visibility.Visible : Visibility.Collapsed;
        if (state.PrimaryColor.Length == 3)
        {
            ChromaPrimaryColor = ToColor(state.PrimaryColor);
        }

        if (state.SecondaryColor.Length == 3)
        {
            ChromaSecondaryColor = ToColor(state.SecondaryColor);
        }

        NotifyChromaColorVisibility();
    }

    private void NotifyChromaColorVisibility()
    {
        var effect = ChromaEffects[
            Math.Clamp(SelectedChromaEffectIndex, 0, ChromaEffects.Length - 1)].Value;
        ChromaPrimaryColorVisibility =
            effect is RazerAwChromaEffect.Static or RazerAwChromaEffect.Breathing or
                RazerAwChromaEffect.AudioMeter
                ? Visibility.Visible
                : Visibility.Collapsed;
        ChromaSecondaryColorVisibility =
            effect == RazerAwChromaEffect.Breathing
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private static byte[] ToRgb(Color color) => [color.R, color.G, color.B];

    private static Color ToColor(IReadOnlyList<byte> rgb) =>
        Color.FromArgb(255, rgb[0], rgb[1], rgb[2]);
}
