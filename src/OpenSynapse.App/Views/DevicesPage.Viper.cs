using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private void ViperActionComboBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            ApplyViperActionSelection(comboBox);
        }
    }

    private void ViperActionComboBoxDataContextChanged(
        FrameworkElement sender,
        DataContextChangedEventArgs args)
    {
        if (sender is ComboBox comboBox)
        {
            comboBox.DispatcherQueue.TryEnqueue(() => ApplyViperActionSelection(comboBox));
        }
    }

    private void ViperActionComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: ViperButtonAssignmentRowViewModel row, SelectedIndex: >= 0 } comboBox)
        {
            row.SelectedActionIndex = comboBox.SelectedIndex;
        }
    }

    private static void ApplyViperActionSelection(ComboBox comboBox)
    {
        if (comboBox.DataContext is ViperButtonAssignmentRowViewModel row)
        {
            comboBox.SelectedIndex = row.SelectedActionIndex;
        }
    }

    private async void ApplyPollingRateClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Viper.ApplyPollingRateAsync(Lifetime);

    private async void ApplyDpiClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Viper.ApplyDpiAsync(Lifetime);

    private async void ApplyIdleClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Viper.ApplyIdleAsync(Lifetime);

    private async void ApplyLowBatteryThresholdClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Viper.ApplyLowBatteryThresholdAsync(Lifetime);

    private async void ApplyBatteryChemistryClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Viper.ApplyBatteryChemistryAsync(Lifetime);

    private async void ApplyDpiStagesClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Viper.ApplyDpiStagesAsync(Lifetime);

    private async void ReadViperButtonMappingsClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Viper.ReadButtonMappingsAsync(Lifetime);

    private async void ApplyAllViperButtonMappingsClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Viper.ApplyAllButtonMappingsAsync(Lifetime);
}
