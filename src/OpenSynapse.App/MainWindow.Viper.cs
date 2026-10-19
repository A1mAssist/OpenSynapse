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
        await _viewModel.ApplyViperPollingRateAsync(_lifetime.Token);

    private async void ApplyDpiClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperDpiAsync(_lifetime.Token);

    private async void ApplyIdleClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperIdleAsync(_lifetime.Token);

    private async void ApplyLowBatteryThresholdClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperLowBatteryThresholdAsync(_lifetime.Token);

    private async void ApplyBatteryChemistryClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperBatteryChemistryAsync(_lifetime.Token);

    private async void ApplyDpiStagesClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperDpiStagesAsync(_lifetime.Token);

    private async void ReadViperButtonMappingsClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ReadViperButtonMappingsAsync(_lifetime.Token);

    private async void ApplyAllViperButtonMappingsClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyAllViperButtonMappingsAsync(_lifetime.Token);
}
