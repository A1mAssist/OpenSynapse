using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views.DevicePanels;

public sealed partial class SurfaceCalibrationPanel : UserControl
{
    private SurfaceCalibrationViewModel? _model;
    public SurfaceCalibrationPanel() => InitializeComponent();

    private async void PanelDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        var previous = _model;
        _model = DataContext as SurfaceCalibrationViewModel;
        Visibility = Card.Visibility = _model is null ? Visibility.Collapsed : Visibility.Visible;
        if (previous is not null && !ReferenceEquals(previous, _model)) await previous.CancelAsync();
    }

    private async void PanelUnloaded(object sender, RoutedEventArgs args)
    { if (_model is { } model) await model.CancelAsync(); }
    private async void ReadClick(object sender, RoutedEventArgs args)
    { if (_model is { } model) await model.InitializeAsync(); }
    private async void ApplyClick(object sender, RoutedEventArgs args)
    { if (_model is { } model) await model.ApplyAsync(); }
    private async void RestoreClick(object sender, RoutedEventArgs args)
    { if (_model is { } model) await model.RestoreSavedAsync(); }
    private async void DefaultsClick(object sender, RoutedEventArgs args)
    { if (_model is { } model) await model.RestoreDefaultsAsync(); }
    private async void PrepareClick(object sender, RoutedEventArgs args)
    { if (_model is { } model) await model.PrepareAsync(); }
    private async void MiniClick(object sender, RoutedEventArgs args)
    { if (_model is { } model) await model.PrepareMiniAsync(); }
    private async void StartClick(object sender, RoutedEventArgs args)
    { if (_model is { } model) await model.StartAsync(); }
    private async void FinishClick(object sender, RoutedEventArgs args)
    { if (_model is { } model) await model.FinishAsync(); }
    private async void CancelClick(object sender, RoutedEventArgs args)
    { if (_model is { } model) await model.CancelAsync(); }
    private async void RetrySaveClick(object sender, RoutedEventArgs args)
    { if (_model is { } model) await model.RetrySaveAsync(); }
}
