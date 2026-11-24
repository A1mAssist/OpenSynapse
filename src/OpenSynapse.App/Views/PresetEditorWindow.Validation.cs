using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.Views;

public sealed partial class PresetEditorWindow
{
    private async void ValidateAllClick(object sender, RoutedEventArgs e)
    {
        ValidateAllButton.IsEnabled = false;
        try
        {
            var results = await Task.Run(() => StudioPresetLibrary.ValidateAll(renderSmoke: false));
            var failures = results.Where(result => result.Issues.Count > 0).ToArray();
            var panel = new StackPanel { Spacing = 8, MinWidth = 520 };
            panel.Children.Add(new TextBlock
            {
                Text = PresetEditorText.Format("PresetEditorValidateAllSummary",
                    "Checked {0} presets: {1} with issues.", results.Count, failures.Length),
                TextWrapping = TextWrapping.Wrap,
            });

            ListView? list = null;
            if (failures.Length == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = PresetEditorText.Get("PresetEditorNoLibraryIssues",
                        "All built-in and user presets passed validation."),
                    TextWrapping = TextWrapping.Wrap,
                });
            }
            else
            {
                list = new ListView
                {
                    ItemsSource = failures.Select(result => new PresetEditorLibraryIssueItem(result)).ToArray(),
                    SelectionMode = ListViewSelectionMode.Single,
                    MaxHeight = 420,
                };
                panel.Children.Add(new ScrollViewer
                {
                    Content = list,
                    MaxHeight = 420,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                });
            }

            var dialog = new ContentDialog
            {
                Title = PresetEditorText.Get("PresetEditorValidateAllTitle", "Preset library validation"),
                Content = panel,
                PrimaryButtonText = failures.Length > 0
                    ? PresetEditorText.Get("PresetEditorOpenSelected", "Open selected")
                    : string.Empty,
                CloseButtonText = PresetEditorText.Get("PresetEditorCancel", "Cancel"),
                DefaultButton = failures.Length > 0
                    ? ContentDialogButton.Primary
                    : ContentDialogButton.Close,
                XamlRoot = RootLayout.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary || list?.SelectedItem is not PresetEditorLibraryIssueItem selected)
            {
                return;
            }

            if (!await ConfirmDiscardAsync()) return;
            _editor.ReplaceDocument(
                StudioPresetDraft.FromConfig(selected.Result.Entry.Preset),
                selected.Result.Entry.Path,
                clean: true);
            SetStatus(PresetEditorText.Format("PresetEditorStatusOpened", "Opened {0}",
                selected.Result.Entry.Path ?? selected.Result.Entry.Id));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            FormatException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus(PresetEditorText.Format("PresetEditorOpenFailed", "Could not validate: {0}",
                exception.Message));
        }
        finally
        {
            ValidateAllButton.IsEnabled = true;
        }
    }
}
