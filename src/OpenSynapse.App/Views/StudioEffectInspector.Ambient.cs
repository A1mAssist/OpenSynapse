using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.Views;

public sealed partial class StudioEffectInspector
{
    public StudioAmbientLayerScope AmbientLayerScope { get; set; }
    public bool CanUseCurrentAmbientDevice { get; set; }
    public event EventHandler<StudioAmbientLayerScope>? AmbientScopeRequested;

    private FrameworkElement BuildAmbientScopeRow()
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var scope in Enum.GetValues<StudioAmbientLayerScope>())
        {
            var item = new ComboBoxItem
            {
                Content = AppStrings.Text($"LightingStudioAmbientScope{scope}"),
                Tag = scope,
                IsEnabled = scope != StudioAmbientLayerScope.Current || CanUseCurrentAmbientDevice,
            };
            AutomationProperties.SetAutomationId(item, $"StudioAmbientLayerScope{scope}");
            combo.Items.Add(item);
        }
        combo.SelectedIndex = (int)AmbientLayerScope;
        AutomationProperties.SetAutomationId(combo, "StudioAmbientLayerScope");
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: StudioAmbientLayerScope scope })
                AmbientScopeRequested?.Invoke(this, scope);
        };
        return Rowed("LightingStudioAmbientLayerScope", combo);
    }

    private FrameworkElement BuildAmbientMappingRow()
    {
        var options = Enum.GetValues<StudioAmbientMapping>()
            .Where(value => value != StudioAmbientMapping.LegacyAuto ||
                _parameters?.AmbientMapping == StudioAmbientMapping.LegacyAuto)
            .Select(value => new KeyValuePair<StudioAmbientMapping, string>(
                value, AppStrings.Text($"LightingStudioAmbientMapping{value}"))).ToList();
        return BuildStyleCombo("LightingStudioAmbientMapping", options,
            parameters => parameters.AmbientMapping,
            (parameters, value) =>
            {
                parameters.AmbientMapping = value;
                if (value != StudioAmbientMapping.LegacyAuto &&
                    options.Any(option => option.Key == StudioAmbientMapping.LegacyAuto)) Refresh();
            }, "StudioAmbientMapping");
    }
}
