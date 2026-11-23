using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.Views;

public sealed partial class StudioEffectInspector
{
    private FrameworkElement BuildScreenModeRow()
    {
        var options = new List<KeyValuePair<StudioScreenMode, string>>
        {
            new(StudioScreenMode.Follow, AppStrings.Text("LightingStudioScreenModeFollow")),
            new(StudioScreenMode.Band, AppStrings.Text("LightingStudioScreenModeBand")),
            new(StudioScreenMode.Dominant, AppStrings.Text("LightingStudioScreenModeDominant")),
            new(StudioScreenMode.Monet, AppStrings.Text("LightingStudioScreenModeMonet")),
        };
        return BuildStyleCombo(
            "LightingStudioParamScreenMode",
            options,
            parameters => parameters.ScreenMode,
            (parameters, value) => parameters.ScreenMode = value);
    }

    private FrameworkElement BuildAudioStyleRow()
    {
        var options = Enum.GetValues<StudioAudioStyle>()
            .Select(style => new KeyValuePair<StudioAudioStyle, string>(
                style,
                AppStrings.Text($"LightingStudioAudioStyle{style}")))
            .ToList();
        return BuildStyleCombo(
            "LightingStudioParamAudioStyle",
            options,
            parameters => parameters.AudioStyle,
            (parameters, value) => parameters.AudioStyle = value);
    }

    private FrameworkElement BuildReactiveStyleRow()
    {
        var options = Enum.GetValues<StudioReactiveStyle>()
            .Select(style => new KeyValuePair<StudioReactiveStyle, string>(
                style,
                AppStrings.Text($"LightingStudioReactiveStyle{style}")))
            .ToList();
        return BuildStyleCombo(
            "LightingStudioParamReactiveStyle",
            options,
            parameters => parameters.ReactiveStyle,
            (parameters, value) => parameters.ReactiveStyle = value);
    }

    private FrameworkElement BuildStyleCombo<T>(
        string labelKey,
        IReadOnlyList<KeyValuePair<T, string>> options,
        Func<StudioEffectParams, T> read,
        Action<StudioEffectParams, T> write)
        where T : struct, Enum
    {
        var combo = new ComboBox
        {
            ItemsSource = options,
            DisplayMemberPath = "Value",
            SelectedValuePath = "Key",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        if (_parameters is { } parameters)
        {
            combo.SelectedValue = read(parameters);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedValue is T value) write(parameters, value);
                RaiseChanged();
            };
        }

        return Rowed(labelKey, combo);
    }
}
