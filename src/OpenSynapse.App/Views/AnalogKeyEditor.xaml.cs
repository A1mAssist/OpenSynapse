using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class AnalogKeyEditor : UserControl
{
    public AnalogKeyEditor()
    {
        InitializeComponent();
    }

    private void KeyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel)
        {
            viewModel.SynchronizeAnalogKeySelection();
        }
    }

    private void SelectAllClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel)
        {
            viewModel.SelectAllAnalogKeys();
        }
    }

    private void ClearSelectionClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel)
        {
            viewModel.ClearAnalogKeySelection();
        }
    }

    private async void ApplySelectedClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel)
        {
            await viewModel.ApplyAnalogKeyToSelectionAsync();
        }
    }

    private async void ApplyAllClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel)
        {
            await viewModel.ApplyAnalogKeyToAllAsync();
        }
    }
    private void AddSnapTapPairClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel)
        {
            viewModel.AddAnalogSnapTapPair();
        }
    }

    private void RemoveSnapTapPairClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel &&
            sender is FrameworkElement { DataContext: AnalogSnapTapPairViewModel pair })
        {
            viewModel.RemoveAnalogSnapTapPair(pair);
        }
    }

    private async void ApplySnapTapClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RazerHidDeviceViewModel viewModel)
        {
            await viewModel.ApplyAnalogSnapTapAsync();
        }
    }
}
