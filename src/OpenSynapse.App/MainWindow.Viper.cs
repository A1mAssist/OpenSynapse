using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App;

public sealed partial class MainWindow
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
        await _viewModel.Viper.ApplyPollingRateAsync(_lifetime.Token);

    private async void ApplyDpiClick(object sender, RoutedEventArgs e) =>
        await _viewModel.Viper.ApplyDpiAsync(_lifetime.Token);

    private async void ApplyIdleClick(object sender, RoutedEventArgs e) =>
        await _viewModel.Viper.ApplyIdleAsync(_lifetime.Token);

    private async void ApplyLowBatteryThresholdClick(object sender, RoutedEventArgs e) =>
        await _viewModel.Viper.ApplyLowBatteryThresholdAsync(_lifetime.Token);

    private async void ApplyBatteryChemistryClick(object sender, RoutedEventArgs e) =>
        await _viewModel.Viper.ApplyBatteryChemistryAsync(_lifetime.Token);

    private async void ApplyDpiStagesClick(object sender, RoutedEventArgs e) =>
        await _viewModel.Viper.ApplyDpiStagesAsync(_lifetime.Token);

    private async void ReadViperButtonMappingsClick(object sender, RoutedEventArgs e) =>
        await _viewModel.Viper.ReadButtonMappingsAsync(_lifetime.Token);

    private async void ApplyAllViperButtonMappingsClick(object sender, RoutedEventArgs e) =>
        await _viewModel.Viper.ApplyAllButtonMappingsAsync(_lifetime.Token);
}
