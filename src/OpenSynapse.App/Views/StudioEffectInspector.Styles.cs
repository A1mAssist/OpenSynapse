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
        Action<StudioEffectParams, T> write,
        string? automationId = null)
        where T : struct, Enum
    {
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var option in options)
        {
            var item = new ComboBoxItem { Content = option.Value, Tag = option.Key };
            if (automationId is not null)
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, $"{automationId}{option.Key}");
            combo.Items.Add(item);
        }
        if (automationId is not null)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(combo, automationId);
        if (_parameters is { } parameters)
        {
            combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(item =>
                item.Tag is T value && EqualityComparer<T>.Default.Equals(value, read(parameters)));
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: T value }) write(parameters, value);
                RaiseChanged();
            };
        }

        return Rowed(labelKey, combo);
    }
}
