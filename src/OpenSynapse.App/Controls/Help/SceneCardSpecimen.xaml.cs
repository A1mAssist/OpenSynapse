using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Controls.Help;

public sealed partial class SceneCardSpecimen : UserControl
{
    public SceneCardSpecimen()
    {
        InitializeComponent();
        AuroraCard.IsChecked = true;
    }

    private void SceneClick(object sender, RoutedEventArgs e)
    {
        foreach (var button in new[] { AuroraCard, EmberCard, TideCard })
        {
            button.IsChecked = ReferenceEquals(button, sender);
        }
    }
}
