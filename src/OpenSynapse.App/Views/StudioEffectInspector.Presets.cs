using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.Views;

public sealed partial class StudioEffectInspector
{
    private FrameworkElement BuildPresetRow()
    {
        var parameters = _parameters!;
        var current = parameters.PresetDefinition ?? StudioPresetCatalog.Default.Find(parameters.Preset);
        var summary = new TextBlock
        {
            Text = current is null
                ? AppStrings.Text("LightingStudioPresetEmpty")
                : string.IsNullOrWhiteSpace(current.Note) ? current.Name : current.Name + "\n" + current.Note,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        AutomationProperties.SetAutomationId(summary, "StudioPresetSummary");
        return Rowed("LightingStudioParamPreset", summary);
    }
}
