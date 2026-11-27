using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Controls.Help;

public sealed partial class ScopeSelectorSpecimen : UserControl
{
    public ScopeSelectorSpecimen()
    {
        InitializeComponent();
        AllScope.IsChecked = true;
    }

    private void ScopeChecked(object sender, RoutedEventArgs e)
    {
        DeviceSelection.Visibility = ReferenceEquals(sender, SelectedScope)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
