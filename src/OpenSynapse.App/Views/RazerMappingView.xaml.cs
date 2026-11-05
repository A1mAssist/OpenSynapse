using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class RazerMappingView : UserControl
{
    public RazerMappingView() => InitializeComponent();

    private RazerHidMappingViewModel? Mapping => DataContext as RazerHidMappingViewModel;

    private async void ReadClick(object sender, RoutedEventArgs e) =>
        await (Mapping?.ReadAsync() ?? Task.CompletedTask);

    private async void ApplyClick(object sender, RoutedEventArgs e) =>
        await (Mapping?.ApplyAsync() ?? Task.CompletedTask);
}
