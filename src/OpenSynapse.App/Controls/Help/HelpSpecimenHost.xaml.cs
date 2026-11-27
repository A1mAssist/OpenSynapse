using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Controls.Help;

public sealed partial class HelpSpecimenHost : UserControl
{
    public static readonly DependencyProperty SpecimenContentProperty = DependencyProperty.Register(
        nameof(SpecimenContent),
        typeof(object),
        typeof(HelpSpecimenHost),
        new PropertyMetadata(null));

    public HelpSpecimenHost() => InitializeComponent();

    public object? SpecimenContent
    {
        get => GetValue(SpecimenContentProperty);
        set => SetValue(SpecimenContentProperty, value);
    }
}
