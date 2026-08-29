using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private async void ProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string profileName })
        {
            await _viewModel.SelectProfileAsync(profileName, _lifetime.Token);
        }
    }

    private async void CreateProfileClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CreateProfileAsync(_lifetime.Token);

    private async void CloneProfileClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CloneActiveProfileAsync(_lifetime.Token);

    private async void RenameProfileClick(object sender, RoutedEventArgs e) =>
        await _viewModel.RenameActiveProfileAsync(_lifetime.Token);

    private async void DeleteProfileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = AppStrings.Get("删除当前配置？"),
            Content = AppStrings.FormatText("DeleteProfileMessage",
                _viewModel.ActiveProfileName),
            PrimaryButtonText = AppStrings.Get("删除"),
            CloseButtonText = AppStrings.Get("取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootNavigationView.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _viewModel.DeleteActiveProfileAsync(_lifetime.Token);
        }
    }

    private async void BindApplicationClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add(".exe");
        InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await _viewModel.BindApplicationAsync(file.Path, _lifetime.Token);
        }
    }

    private async void UnbindApplicationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string executablePath })
        {
            await _viewModel.UnbindApplicationAsync(executablePath, _lifetime.Token);
        }
    }

    private async void ImportProfilesClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".json");
        InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await _viewModel.ImportProfilesAsync(file.Path, _lifetime.Token);
        }
    }

    private async void ExportProfilesClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"OpenSynapse-{_viewModel.ActiveProfileName}",
        };
        picker.FileTypeChoices.Add(AppStrings.Get("OpenSynapse 配置"), [".json"]);
        InitializePicker(picker);
        var file = await picker.PickSaveFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await _viewModel.ExportProfilesAsync(file.Path, _lifetime.Token);
        }
    }

    private void InitializePicker(object picker) =>
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this));
}
