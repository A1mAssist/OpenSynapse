using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.Views;

public sealed record StudioPresetEditRequest(StudioEffectParams Parameters, StudioPresetConfig Preset);

public sealed partial class StudioEffectInspector
{
    private FrameworkElement BuildPresetRow()
    {
        var parameters = _parameters!;
        var current = parameters.PresetDefinition ?? StudioPresetLibrary.Find(parameters.Preset);
        var options = StudioPresetLibrary.Enumerate().Select(entry => entry.Preset).ToList();
        if (current is not null &&
            !options.Any(preset => string.Equals(preset.Id, current.Id, StringComparison.OrdinalIgnoreCase)))
        {
            options.Insert(0, current);
        }
        var summary = new TextBlock
        {
            Text = current is null
                ? AppStrings.Text("LightingStudioPresetEmpty")
                : string.IsNullOrWhiteSpace(current.Note) ? current.Name : current.Name + "\n" + current.Note,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        AutomationProperties.SetAutomationId(summary, "StudioPresetSummary");
        var search = new TextBox
        {
            PlaceholderText = AppStrings.Text("LightingStudioPresetSearch"),
        };
        AutomationProperties.SetAutomationId(search, "StudioPresetSearch");
        var combo = new ComboBox
        {
            ItemsSource = options,
            DisplayMemberPath = nameof(StudioPresetConfig.Name),
            SelectedItem = current,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(combo, "StudioPresetPicker");
        search.TextChanged += (_, _) =>
        {
            var query = search.Text.Trim();
            combo.ItemsSource = options.Where(option => query.Length == 0 ||
                option.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                option.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (query.Length == 0) combo.SelectedItem = current;
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not StudioPresetConfig preset) return;
            parameters.Preset = preset.Id;
            parameters.PresetDefinition = preset;
            RaiseChanged();
            Refresh();
        };
        var edit = new Button
        {
            Content = AppStrings.Text("LightingStudioPresetEditCurrent"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = current is not null,
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
        };
        AutomationProperties.SetAutomationId(edit, "StudioPresetOpenEditor");
        AutomationProperties.SetName(edit, AppStrings.Text("LightingStudioPresetEditCurrent"));
        edit.Click += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (current is not null) EditPresetRequested?.Invoke(this, new(parameters, current));
        });
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(summary);
        content.Children.Add(search);
        content.Children.Add(combo);
        content.Children.Add(edit);
        return Rowed("LightingStudioParamPreset", content);
    }
}
