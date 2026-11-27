using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.Core.Help;

namespace OpenSynapse.App.Help;

public sealed partial class HelpSpecimenTemplateSelector : DataTemplateSelector
{
    public DataTemplate? DpiStagesTemplate { get; set; }
    public DataTemplate? SceneCardTemplate { get; set; }
    public DataTemplate? ApplyScopeTemplate { get; set; }
    public DataTemplate? MacroStepTemplate { get; set; }
    public DataTemplate? KeyBindingTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => Select(item);

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        Select(item);

    private DataTemplate? Select(object item) =>
        item is HelpSpecimenKind kind
            ? kind switch
            {
                HelpSpecimenKind.DpiStages => DpiStagesTemplate,
                HelpSpecimenKind.SceneCard => SceneCardTemplate,
                HelpSpecimenKind.ApplyScope => ApplyScopeTemplate,
                HelpSpecimenKind.MacroStep => MacroStepTemplate,
                HelpSpecimenKind.KeyBinding => KeyBindingTemplate,
                _ => DpiStagesTemplate,
            }
            : DpiStagesTemplate;
}
