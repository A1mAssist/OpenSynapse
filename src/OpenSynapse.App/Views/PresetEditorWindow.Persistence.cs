using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Lighting;
using Windows.Storage.Pickers;

namespace OpenSynapse.App.Views;

public sealed partial class PresetEditorWindow
{
    private async Task NewDocumentAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        _editor.ReplaceDocument(PresetEditorViewModel.CreateNewDraft(), null, clean: true);
        StudioPresetAutosave.Clear();
        SetStatus(string.Empty);
    }

    private async Task OpenDocumentAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add(StudioPresetCodec.Extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
            var file = await picker.PickSingleFileAsync();
            if (file is null || string.IsNullOrWhiteSpace(file.Path)) return;
            var draft = StudioPresetLibrary.LoadDraft(file.Path);
            _editor.ReplaceDocument(draft, file.Path, clean: true);
            StudioPresetAutosave.Clear();
            SetStatus(string.Empty);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            FormatException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus(PresetEditorText.Format("PresetEditorOpenFailed", "Could not open preset: {0}",
                exception.Message));
        }
    }

    private async Task<bool> SaveAsync()
    {
        if (_editor.IsBuiltIn)
        {
            SetStatus(PresetEditorText.Get("PresetEditorSaveAsBuiltIn",
                "Built-in presets save as a user copy."));
            return await SaveAsAsync();
        }

        var id = _editor.Draft.Id.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            _editor.Validate(true);
            SetStatus(PresetEditorText.Get("PresetEditorInvalidSave",
                "Fix the validation issues before saving."));
            return false;
        }

        var path = _editor.Path ?? StudioPresetLibrary.PathForId(id);
        return await SaveToPathAsync(path);
    }

    private async Task<bool> SaveAsAsync()
    {
        if (_editor.HasIssues)
        {
            _editor.Validate(true);
            SetStatus(PresetEditorText.Get("PresetEditorInvalidSave",
                "Fix the validation issues before saving."));
            return false;
        }

        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = string.IsNullOrWhiteSpace(_editor.Draft.Id)
                    ? "preset"
                    : _editor.Draft.Id,
            };
            picker.FileTypeChoices.Add(
                PresetEditorText.Get("PresetEditorPresetLibrary", "OpenSynapse preset"),
                [StudioPresetCodec.Extension]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
            var file = await picker.PickSaveFileAsync();
            if (file is null || string.IsNullOrWhiteSpace(file.Path)) return false;
            return await SaveToPathAsync(file.Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException or COMException)
        {
            SetStatus(PresetEditorText.Format("PresetEditorSaveFailed", "Could not save preset: {0}",
                exception.Message));
            return false;
        }
    }

    private async Task<bool> SaveToPathAsync(string path)
    {
        try
        {
            var compiled = StudioPresetLibrary.Save(_editor.Draft, path);
            _editor.MarkSaved(path);
            _onSaved?.Invoke(compiled);
            SetStatus(string.Empty);
            UpdateUiState();
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            FormatException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            _editor.Validate(true);
            SetStatus(PresetEditorText.Format("PresetEditorSaveFailed", "Could not save preset: {0}",
                exception.Message));
            return false;
        }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_editor.IsDirty) return true;
        var dialog = new ContentDialog
        {
            Title = PresetEditorText.Get("PresetEditorConfirmDiscardTitle", "Discard unsaved changes?"),
            Content = new TextBlock
            {
                Text = PresetEditorText.Get("PresetEditorConfirmDiscardMessage",
                    "The current preset has unsaved changes. Discard them?"),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = PresetEditorText.Get("PresetEditorConfirmDiscard", "Discard"),
            CloseButtonText = PresetEditorText.Get("PresetEditorCancel", "Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootLayout.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
        StudioPresetAutosave.Clear();
        return true;
    }

    /// <summary>
    /// Reuses the open window for another preset request. Unsaved work is never
    /// discarded without an explicit choice.
    /// </summary>
    internal async Task<bool> TryLoadDocumentAsync(
        StudioPresetConfig preset,
        string? path,
        Action<StudioPresetConfig>? onSaved)
    {
        try
        {
            if (IsCurrentDocument(preset, path)) return true;
            if (!await ConfirmSaveBeforeSwitchAsync()) return false;
            _onSaved = onSaved;
            _editor.ReplaceDocument(StudioPresetDraft.FromConfig(preset), path, clean: true);
            StudioPresetAutosave.Clear();
            SetStatus(string.Empty);
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            FormatException or InvalidOperationException or UnauthorizedAccessException or
            ArgumentException or COMException)
        {
            SetStatus(PresetEditorText.Format("PresetEditorOpenFailed", "Could not open preset: {0}",
                exception.Message));
            return false;
        }
    }

    private bool IsCurrentDocument(StudioPresetConfig preset, string? path) =>
        _editor.Path is { } currentPath
            ? path is not null && string.Equals(currentPath, path, StringComparison.OrdinalIgnoreCase)
            : path is null &&
              string.Equals(_editor.Draft.Id, preset.Id, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> ConfirmSaveBeforeSwitchAsync()
    {
        if (!_editor.IsDirty) return true;
        var dialog = new ContentDialog
        {
            Title = PresetEditorText.Get("PresetEditorUnsavedCloseTitle", "Save changes?"),
            Content = new TextBlock
            {
                Text = PresetEditorText.Get("PresetEditorUnsavedSwitchMessage",
                    "Save changes to the current preset before switching?"),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = PresetEditorText.Get("PresetEditorSaveAndSwitch", "Save and switch"),
            SecondaryButtonText = PresetEditorText.Get("PresetEditorSwitchWithoutSaving",
                "Switch without saving"),
            CloseButtonText = PresetEditorText.Get("PresetEditorCancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootLayout.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary) return await SaveAsync();
        if (result == ContentDialogResult.Secondary)
        {
            StudioPresetAutosave.Clear();
            return true;
        }

        return false;
    }

    private void AutosaveIfDirty()
    {
        if (!_loaded || _allowClose || !_editor.IsDirty) return;
        StudioPresetAutosave.Save(_editor.Draft, _editor.Path);
    }

    private async Task HandleRecoveryAsync()
    {
        if (_recoveryPrompted) return;
        _recoveryPrompted = true;
        if (!StudioPresetAutosave.TryLoad(out var state)) return;
        if (!string.Equals(state.Draft.Id, _editor.Draft.Id, StringComparison.Ordinal) &&
            !string.Equals(state.SourcePath, _editor.Path, StringComparison.OrdinalIgnoreCase) &&
            _editor.Path is not null)
        {
            return;
        }

        if (_editor.Path is { } path && File.Exists(path) &&
            state.SavedAt <= File.GetLastWriteTime(path))
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = PresetEditorText.Get("PresetEditorRecoverTitle", "Recover unsaved preset?"),
            Content = new TextBlock
            {
                Text = PresetEditorText.Format("PresetEditorRecoverMessage",
                    "A newer autosaved draft from {0} is available.", state.SavedAt.ToLocalTime()),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = PresetEditorText.Get("PresetEditorRecover", "Recover"),
            CloseButtonText = PresetEditorText.Get("PresetEditorDiscard", "Discard"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootLayout.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _editor.ReplaceDocument(state.Draft, state.SourcePath ?? _editor.Path, clean: false);
            _editor.MarkRecovered();
            SetStatus(PresetEditorText.Get("PresetEditorStatusRecovered", "Recovered unsaved draft"));
            return;
        }

        StudioPresetAutosave.Clear();
    }

    private void OnPresetEditorClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || !_editor.IsDirty) return;
        args.Cancel = true;
        if (_closePromptActive) return;
        _closePromptActive = true;
        // The dialog cannot be awaited inside Closing without re-entering window
        // teardown, so let the event return and close from the dispatcher.
        DispatcherQueue.TryEnqueue(() => _ = CompleteCloseAsync());
    }

    private async Task CompleteCloseAsync()
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = PresetEditorText.Get("PresetEditorUnsavedCloseTitle", "Save changes?"),
                Content = new TextBlock
                {
                    Text = PresetEditorText.Get("PresetEditorUnsavedCloseMessage",
                        "Save this preset before closing?"),
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = PresetEditorText.Get("PresetEditorSaveAndClose", "Save and close"),
                SecondaryButtonText = PresetEditorText.Get("PresetEditorCloseWithoutSaving", "Don't save"),
                CloseButtonText = PresetEditorText.Get("PresetEditorCancel", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootLayout.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (!await SaveAsync()) return;
            }
            else if (result == ContentDialogResult.Secondary)
            {
                StudioPresetAutosave.Clear();
            }
            else
            {
                return;
            }

            _allowClose = true;
            Close();
        }
        finally
        {
            _closePromptActive = false;
        }
    }
}
