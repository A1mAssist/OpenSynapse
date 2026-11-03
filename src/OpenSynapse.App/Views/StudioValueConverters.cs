using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Lighting;
using Windows.UI;

namespace OpenSynapse.App.Views;

public sealed class StudioEffectLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        StudioEffectKind.Solid => AppStrings.Text("LightingStudioEffectSolid"),
        StudioEffectKind.Breathing => AppStrings.Text("LightingStudioEffectBreathing"),
        StudioEffectKind.Spectrum => AppStrings.Text("LightingStudioEffectSpectrum"),
        StudioEffectKind.Wave => AppStrings.Text("LightingStudioEffectWave"),
        StudioEffectKind.Gradient => AppStrings.Text("LightingStudioEffectGradient"),
        StudioEffectKind.Fire => AppStrings.Text("LightingStudioEffectFire"),
        StudioEffectKind.Reactive => AppStrings.Text("LightingStudioEffectReactive"),
        StudioEffectKind.Ripple => AppStrings.Text("LightingStudioEffectRipple"),
        StudioEffectKind.Starlight => AppStrings.Text("LightingStudioEffectStarlight"),
        StudioEffectKind.Ambient => AppStrings.Text("LightingStudioEffectAmbient"),
        StudioEffectKind.Audio => AppStrings.Text("LightingStudioEffectAudio"),
        StudioEffectKind.Wheel => AppStrings.Text("LightingStudioEffectWheel"),
        StudioEffectKind.ChromaGenerate => AppStrings.Text("LightingStudioEffectGenerate"),
        StudioEffectKind.Tidal => AppStrings.Text("LightingStudioEffectTidal"),
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class StudioDepthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        new Thickness(value is int depth ? Math.Clamp(depth, 0, 8) * 14 : 0, 0, 0, 0);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class StudioRgbBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is StudioRgb color
            ? new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B))
            : new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
