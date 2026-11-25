using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.App.Views;

public sealed partial class OverviewPage : UserControl
{
    internal event Action<object>? DeviceRequested;
    internal event Action? VisibilityChanged;
    private MainViewModel? _viewModel;

    public OverviewPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachViewModel();
    }

    internal FrameworkElement TelemetrySection => SystemTelemetrySection;

    internal void ScrollToTop() => PageScrollViewer.ChangeView(
        0, 0, null, disableAnimation: true);

    internal void RefreshLayout()
    {
        if (_viewModel is null) return;
        _viewModel.Home.SetDevices(_viewModel.HomeDevices);
        SyncPerformanceState();
        ApplyLayout();
    }

    private void SyncPerformanceState()
    {
        if (_viewModel is null) return;
        var canCycle = _viewModel.Blade.CanWritePerformanceMode;
        _viewModel.Home.SetPerformanceState(
            canCycle ? _viewModel.Blade.PerformanceModeText : string.Empty,
            canCycle ? _viewModel.ActivePowerSourceText : string.Empty,
            canCycle);
    }

    private void AttachViewModel()
    {
        if (ReferenceEquals(_viewModel, DataContext)) return;
        if (_viewModel is not null)
        {
            _viewModel.Home.PropertyChanged -= HomeChanged;
            _viewModel.PropertyChanged -= ViewModelChanged;
            _viewModel.DeviceRules.PropertyChanged -= ApplicationSwitchingChanged;
            _viewModel.Blade.PropertyChanged -= BladeChanged;
        }
        _viewModel = DataContext as MainViewModel;
        if (_viewModel is null) return;
        _viewModel.Home.PropertyChanged += HomeChanged;
        _viewModel.PropertyChanged += ViewModelChanged;
        _viewModel.DeviceRules.PropertyChanged += ApplicationSwitchingChanged;
        _viewModel.Blade.PropertyChanged += BladeChanged;
        _viewModel.Home.UpdateApplicationSwitching(_viewModel.DeviceRules.Enabled);
        RefreshLayout();
    }

    private void BladeChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (Visibility == Visibility.Visible &&
            args.PropertyName is null or "" or nameof(BladeWorkspace.PerformanceModeText) or nameof(BladeWorkspace.CanWritePerformanceMode))
            SyncPerformanceState();
    }

    private void HomeChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (Visibility == Visibility.Visible &&
            args.PropertyName is nameof(HomeWorkspace.CardOrder) or nameof(HomeWorkspace.IsEditing)) ApplyLayout();
    }

    private void ApplicationSwitchingChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_viewModel is not null) _viewModel.Home.UpdateApplicationSwitching(_viewModel.DeviceRules.Enabled);
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_viewModel is null || Visibility != Visibility.Visible) return;
        if (args.PropertyName is null or nameof(MainViewModel.DeviceSelectorItems)) _viewModel.Home.SetDevices(_viewModel.HomeDevices);
        if (args.PropertyName is null or nameof(MainViewModel.ActivePowerSourceText)) SyncPerformanceState();
    }

    private void ApplyLayout()
    {
        if (_viewModel is null) return;
        var home = _viewModel.Home;
        var cards = new[] { StatusSection, DevicesSection, QuickActionsSection, ProfilesSection, SystemTelemetrySection };
        HomeEditorPanel.Visibility = home.EditorVisibility;
        HomeCardsPanel.Visibility = home.BrowseVisibility;
        foreach (var card in cards)
        {
            card.Visibility = home.IsCardVisible((string)card.Tag) ? Visibility.Visible : Visibility.Collapsed;
            HomeCardsPanel.Children.Remove(card);
        }
        HomeCardsPanel.RowDefinitions.Clear();
        var visibleCards = home.CardOrder.Select(id => cards.Single(card => (string)card.Tag == id))
            .Where(card => card.Visibility == Visibility.Visible)
            .ToArray();
        var row = 0;
        var column = 0;
        for (var index = 0; index < visibleCards.Length; index++)
        {
            var card = visibleCards[index];
            if ((string)card.Tag == "telemetry")
            {
                HomeCardsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(card, row);
                Grid.SetColumn(card, 0);
                Grid.SetColumnSpan(card, 2);
                row++;
                column = 0;
            }
            else
            {
                if (column == 0) HomeCardsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(card, row);
                Grid.SetColumn(card, column);
                // A card with no neighbour in its row takes the whole width instead
                // of leaving half the page empty.
                var alone = column == 0 &&
                    (index + 1 >= visibleCards.Length || (string)visibleCards[index + 1].Tag == "telemetry");
                Grid.SetColumnSpan(card, alone ? 2 : 1);
                if (alone)
                {
                    row++;
                    column = 0;
                }
                else
                {
                    column++;
                    if (column == 2)
                    {
                        row++;
                        column = 0;
                    }
                }
            }
            HomeCardsPanel.Children.Add(card);
        }
        VisibilityChanged?.Invoke();
    }

    private void ConnectedDeviceCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: { } source })
        {
            DeviceRequested?.Invoke(source);
        }
    }

    private void CancelHomeClick(object sender, RoutedEventArgs e) => _viewModel?.Home.CancelEdit();
    private async void SaveHomeClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is { } viewModel) await viewModel.Home.SaveEditAsync();
    }

    private async void ResetHomeClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is { } viewModel) await viewModel.Home.ResetAsync();
    }

    private static HomeCardEditorRow? Row(object sender) =>
        (sender as FrameworkElement)?.DataContext as HomeCardEditorRow;

    private void MoveHomeCardUpClick(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is { } row) _viewModel?.Home.Move(row, -1);
    }

    private void MoveHomeCardDownClick(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is { } row) _viewModel?.Home.Move(row, 1);
    }

    private void PinHomeCardClick(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is { } row) _viewModel?.Home.Pin(row);
    }

    private async void CyclePerformanceModeClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is { } viewModel) await viewModel.Blade.CyclePerformanceModeAsync();
    }

    private async void OpenStudioClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is { } viewModel) await viewModel.Home.InvokeQuickActionAsync(HomeLayoutSettings.OpenStudio);
    }
}
