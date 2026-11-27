using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Controls.Help;

public sealed partial class KeyBindingSpecimen : UserControl
{
    public KeyBindingSpecimen() => InitializeComponent();

    private void ControlLoaded(object sender, RoutedEventArgs e) => ShowSelectedKey();

    private void KeyChanged(object sender, SelectionChangedEventArgs e) => ShowSelectedKey();

    private void ShowSelectedKey()
    {
        if (SelectedKeyText is null)
        {
            return;
        }

        SelectedKeyText.Text = (KeyPicker.SelectedItem as ComboBoxItem)?.Content as string ?? string.Empty;
    }
}
