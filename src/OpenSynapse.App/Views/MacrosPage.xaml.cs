using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using Windows.Storage.Pickers;

namespace OpenSynapse.App.Views;

public sealed partial class MacrosPage : UserControl
{
    internal MainWindow Host { get; set; } = null!;

    internal event EventHandler? OpenEditorRequested;

    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private CancellationToken Lifetime => Host.LifetimeToken;

    public MacrosPage() => InitializeComponent();

    private async void CreateMacroClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Macros.CreateMacroAsync(Lifetime);

    private async void MacroRowEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch
            {
                DataContext: MacroRowViewModel row,
                FocusState: not FocusState.Unfocused,
            } toggle)
        {
            row.Enabled = toggle.IsOn;
            await ViewModel.Macros.SaveMacroEnabledAsync(row, Lifetime);
        }
    }

    private async void CopyMacroRowClick(object sender, RoutedEventArgs e)
    {
        if (MacroRowFromSender(sender) is { } row)
        {
            ViewModel.Macros.SelectedMacro = row;
            await ViewModel.Macros.CopySelectedMacroAsync(Lifetime);
        }
    }

    private async void DeleteMacroRowClick(object sender, RoutedEventArgs e)
    {
        if (MacroRowFromSender(sender) is not { } row)
        {
            return;
        }

        if (ViewModel.Macros.IsMacroRecording &&
            !await ViewModel.Macros.StopMacroRecordingAsync(Lifetime))
        {
            return;
        }

        if (!ReferenceEquals(ViewModel.Macros.SelectedMacro, row))
        {
            ViewModel.Macros.SelectedMacro = row;
        }

        var dialog = new ContentDialog
        {
            Title = AppStrings.Text("MacroDeleteMacroTitle"),
            Content = AppStrings.FormatText("MacroDeleteMacroMessage", row.Name),
            PrimaryButtonText = AppStrings.Text("MacroDeleteConfirm"),
            CloseButtonText = AppStrings.Text("MacroCancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.Macros.DeleteSelectedMacroAsync(Lifetime);
        }
    }

    private async void ExportMacroRowClick(object sender, RoutedEventArgs e)
    {
        if (MacroRowFromSender(sender) is { } row)
        {
            ViewModel.Macros.SelectedMacro = row;
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
        Host.InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await ViewModel.Macros.ImportMacroAsync(file.Path, Lifetime);
        }
    }

    private async void ExportSelectedMacroClick(object sender, RoutedEventArgs e) =>
        await ExportSelectedMacroAsync();

    private async Task ExportSelectedMacroAsync()
    {
        if (ViewModel.Macros.SelectedMacro is not { } selected)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = SafeMacroFileName(selected.Name),
        };
        picker.FileTypeChoices.Add(AppStrings.Text("MacroJsonFileType"), [".json"]);
        Host.InitializePicker(picker);
        var file = await picker.PickSaveFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await ViewModel.Macros.ExportSelectedMacroAsync(file.Path, Lifetime);
        }
    }

    private async void CaptureMacroTriggerClick(object sender, RoutedEventArgs e) =>
        await ViewModel.Macros.CaptureMacroTriggerAsync(Lifetime);

    private void OpenMacroEditorWindowClick(object sender, RoutedEventArgs e) =>
        OpenEditorRequested?.Invoke(this, EventArgs.Empty);

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
