using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using Windows.Storage.Pickers;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private async void CreateMacroClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CreateMacroAsync(_lifetime.Token);

    private async void MacroRowEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch
            {
                DataContext: MacroRowViewModel row,
                FocusState: not FocusState.Unfocused,
            } toggle)
        {
            row.Enabled = toggle.IsOn;
            await _viewModel.SaveMacroEnabledAsync(row, _lifetime.Token);
        }
    }

    private async void CopyMacroRowClick(object sender, RoutedEventArgs e)
    {
        if (MacroRowFromSender(sender) is { } row)
        {
            _viewModel.SelectedMacro = row;
            await _viewModel.CopySelectedMacroAsync(_lifetime.Token);
        }
    }

    private async void DeleteMacroRowClick(object sender, RoutedEventArgs e)
    {
        if (MacroRowFromSender(sender) is not { } row)
        {
            return;
        }

        if (_viewModel.IsMacroRecording &&
            !await _viewModel.StopMacroRecordingAsync(_lifetime.Token))
        {
            return;
        }

        if (!ReferenceEquals(_viewModel.SelectedMacro, row))
        {
            _viewModel.SelectedMacro = row;
        }

        var dialog = new ContentDialog
        {
            Title = AppStrings.Text("MacroDeleteMacroTitle"),
            Content = AppStrings.FormatText("MacroDeleteMacroMessage", row.Name),
            PrimaryButtonText = AppStrings.Text("MacroDeleteConfirm"),
            CloseButtonText = AppStrings.Text("MacroCancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootNavigationView.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _viewModel.DeleteSelectedMacroAsync(_lifetime.Token);
        }
    }

    private async void ExportMacroRowClick(object sender, RoutedEventArgs e)
    {
        if (MacroRowFromSender(sender) is { } row)
        {
            _viewModel.SelectedMacro = row;
            await ExportSelectedMacroAsync();
        }
    }

    private async void ImportMacroClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add(".xml");
        InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await _viewModel.ImportMacroAsync(file.Path, _lifetime.Token);
        }
    }

    private async void ExportSelectedMacroClick(object sender, RoutedEventArgs e) =>
        await ExportSelectedMacroAsync();

    private async Task ExportSelectedMacroAsync()
    {
        if (_viewModel.SelectedMacro is not { } selected)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = SafeMacroFileName(selected.Name),
        };
        picker.FileTypeChoices.Add(AppStrings.Text("MacroJsonFileType"), [".json"]);
        InitializePicker(picker);
        var file = await picker.PickSaveFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await _viewModel.ExportSelectedMacroAsync(file.Path, _lifetime.Token);
        }
    }

    private async void CaptureMacroTriggerClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CaptureMacroTriggerAsync(_lifetime.Token);

    private static MacroRowViewModel? MacroRowFromSender(object sender) =>
        sender switch
        {
            MenuFlyoutItem { Tag: MacroRowViewModel row } => row,
            MenuFlyoutItem { DataContext: MacroRowViewModel row } => row,
            _ => null,
        };

    private static string SafeMacroFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray())
            .Trim();
        return string.IsNullOrWhiteSpace(safe) ? "OpenSynapse-macro" : safe;
    }
}
