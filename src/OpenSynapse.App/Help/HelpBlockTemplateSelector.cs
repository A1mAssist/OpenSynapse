using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Help;

namespace OpenSynapse.App.Help;

public sealed partial class HelpBlockTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ParagraphTemplate { get; set; }
    public DataTemplate? StepsTemplate { get; set; }
    public DataTemplate? SpecimenTemplate { get; set; }
    public DataTemplate? CalloutTemplate { get; set; }
    public DataTemplate? FaqTemplate { get; set; }
    public DataTemplate? RelatedTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => Select(item);

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        Select(item);

    private DataTemplate? Select(object item) =>
        item is HelpBlockViewModel block
            ? block.Kind switch
            {
                HelpBlockKind.Paragraph => ParagraphTemplate,
                HelpBlockKind.Steps => StepsTemplate,
                HelpBlockKind.Specimen => SpecimenTemplate,
                HelpBlockKind.Callout => CalloutTemplate,
                HelpBlockKind.Faq => FaqTemplate,
                HelpBlockKind.Related => RelatedTemplate,
                _ => ParagraphTemplate,
            }
            : ParagraphTemplate;
}
