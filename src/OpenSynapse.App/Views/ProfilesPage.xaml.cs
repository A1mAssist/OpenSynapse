using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using Windows.Storage.Pickers;

namespace OpenSynapse.App.Views;

public sealed partial class ProfilesPage : UserControl
{
    private bool _behaviorUiReady;
    private int[] _refreshRateCycleOptions = [];

    internal MainWindow Host { get; set; } = null!;

    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private CancellationToken Lifetime => Host.LifetimeToken;

    public ProfilesPage() => InitializeComponent();

    internal void InitializeBehaviorSettingsUi()
    {
        _behaviorUiReady = false;
        var performanceAvailable = ViewModel.Blade.IsPerformanceModeAvailable(BladePerformanceMode.Performance);
        PerformanceCyclePerformanceCheckBox.Visibility =
            performanceAvailable ? Visibility.Visible : Visibility.Collapsed;
        if (!performanceAvailable)
        {
            PerformanceCyclePerformanceCheckBox.IsChecked = false;
        }
        foreach (var checkBox in PerformanceCycleModesPanel.Children.OfType<CheckBox>())
        {
            checkBox.IsChecked = checkBox.Tag is string tag &&
                Enum.TryParse<BladePerformanceMode>(tag, out var mode) &&
                ViewModel.Blade.PerformanceCycleModes.Contains(mode);
        }
        RebuildRefreshRateCycleOptions();
        _behaviorUiReady = true;
    }

    internal void OnViewModelPropertyChanged(string? propertyName)
    {
        if (propertyName == nameof(DisplayWorkspace.InternalDisplayRefreshRates))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _behaviorUiReady = false;
                RebuildRefreshRateCycleOptions();
                _behaviorUiReady = true;
            });
        }
        if (propertyName == nameof(BladeWorkspace.PerformanceCycleModes))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _behaviorUiReady = false;
                InitializeBehaviorSettingsUi();
            });
        }
        if (propertyName == nameof(MainViewModel.InternalDisplayRefreshRateCycleHertz))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _behaviorUiReady = false;
                RebuildRefreshRateCycleOptions(force: true);
                _behaviorUiReady = true;
            });
        }
    }

    private void RebuildRefreshRateCycleOptions(bool force = false)
    {
        var rates = ViewModel.Display.InternalDisplayRefreshRates.Distinct().Order().ToArray();
        if (!force && _refreshRateCycleOptions.SequenceEqual(rates))
        {
            return;
        }
        _refreshRateCycleOptions = rates;
        var selected = ViewModel.InternalDisplayRefreshRateCycleHertz is { Count: > 0 } configured
            ? rates.Where(configured.Contains).ToHashSet()
            : rates.ToHashSet();
        if (selected.Count == 0)
        {
            selected = rates.ToHashSet();
        }

        RefreshRateCycleModesPanel.Children.Clear();
        RefreshRateCycleModesPanel.ColumnDefinitions.Clear();
        for (var index = 0; index < rates.Length; index++)
        {
            RefreshRateCycleModesPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var checkBox = new CheckBox
            {
                Tag = rates[index],
                Content = $"{rates[index]} Hz",
                IsChecked = selected.Contains(rates[index]),
            };
            checkBox.Checked += RefreshRateCycleModeChanged;
            checkBox.Unchecked += RefreshRateCycleModeChanged;
            Grid.SetColumn(checkBox, index);
            RefreshRateCycleModesPanel.Children.Add(checkBox);
        }

        RefreshRateCycleSection.Visibility = rates.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (selected.Count > 0)
        {
            ViewModel.SetInternalDisplayRefreshRateCycle(selected);
        }
    }

    private async void PerformanceCycleModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady)
        {
            return;
        }

        var selected = PerformanceCycleModesPanel.Children
            .OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => checkBox.Tag as string)
            .Select(tag => Enum.TryParse<BladePerformanceMode>(tag, out var mode) ? mode : (BladePerformanceMode?)null)
            .OfType<BladePerformanceMode>()
            .ToHashSet();
        if (selected.Count == 0 && sender is CheckBox lastCheckBox)
        {
            _behaviorUiReady = false;
            lastCheckBox.IsChecked = true;
            _behaviorUiReady = true;
            return;
        }

        if (!await ViewModel.Blade.SavePerformanceCycleModesAsync(selected, Lifetime))
        {
            _behaviorUiReady = false;
            InitializeBehaviorSettingsUi();
        }
    }

    private async void RefreshRateCycleModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady)
        {
            return;
        }

        var selected = RefreshRateCycleModesPanel.Children
            .OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true && checkBox.Tag is int)
            .Select(checkBox => (int)checkBox.Tag)
            .ToHashSet();
        if (selected.Count == 0 && sender is CheckBox lastCheckBox)
        {
            _behaviorUiReady = false;
            lastCheckBox.IsChecked = true;
            _behaviorUiReady = true;
            return;
        }

        if (!await ViewModel.SaveRefreshRateCycleAsync(selected, Lifetime))
        {
            _behaviorUiReady = false;
            RebuildRefreshRateCycleOptions(force: true);
            _behaviorUiReady = true;
        }
    }

    private async void ProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string profileName })
        {
            await ViewModel.Profiles.SelectProfileAsync(profileName, Lifetime);
        }
    }

    private async void CreateProfileClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Profiles.CreateProfileAsync(Lifetime);

    private async void CloneProfileClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Profiles.CloneActiveProfileAsync(Lifetime);

    private async void RenameProfileClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Profiles.RenameActiveProfileAsync(Lifetime);

    private async void DeleteProfileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = AppStrings.Text("Text_4BC87173"),
            Content = AppStrings.FormatText("DeleteProfileMessage",
                ViewModel.Profiles.ActiveProfileName),
            PrimaryButtonText = AppStrings.Text("Text_EA8D5381"),
            CloseButtonText = AppStrings.Text("Text_949856B3"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.Profiles.DeleteActiveProfileAsync(Lifetime);
        }
    }

    private async void BindApplicationClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add(".exe");
        Host.InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await ViewModel.Profiles.BindApplicationAsync(file.Path, Lifetime);
        }
    }

    private async void UnbindApplicationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string executablePath })
        {
            await ViewModel.Profiles.UnbindApplicationAsync(executablePath, Lifetime);
        }
    }

    private async void ImportProfilesClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".json");
        Host.InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await ViewModel.Profiles.ImportProfilesAsync(file.Path, Lifetime);
        }
    }

    private async void ExportProfilesClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"OpenSynapse-{ViewModel.Profiles.ActiveProfileName}",
        };
        picker.FileTypeChoices.Add(AppStrings.Text("Text_07E747F7"), [".json"]);
        Host.InitializePicker(picker);
        var file = await picker.PickSaveFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await ViewModel.Profiles.ExportProfilesAsync(file.Path, Lifetime);
        }
    }
}
