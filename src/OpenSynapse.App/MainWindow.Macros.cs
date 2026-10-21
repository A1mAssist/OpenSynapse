using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    // The two rails shrink before the step list does, because the step list is
    // the only column that cannot work at a narrow width.
    private const int MacroWideListWidth = 240;
    private const int MacroNarrowListWidth = 200;
    private const int MacroWideKeyBindsWidth = 260;
    private const int MacroNarrowKeyBindsWidth = 210;
    private const int MacroBodyColumnSpacing = 16;
    private const int MacroWideBodyWidth = 1000;
    private const int MacroToolbarMinWidth = 240;
    private const int MacroPlaybackBarMinWidth = 320;

    private int _macroListColumnWidth = MacroWideListWidth;
    private int _macroKeyBindsColumnWidth = MacroWideKeyBindsWidth;
    private bool _macroToolbarStacked;
    private bool _macroDurationHidden;

    private void InitializeMacroLayout()
    {
        MacroBodyLayout.SizeChanged += (_, _) => UpdateMacroLayout();
        UpdateMacroLayout();
    }

    private void UpdateMacroLayout()
    {
        var bodyWidth = MacroBodyLayout.ActualWidth;
        if (bodyWidth <= 0)
        {
            return;
        }

        var wide = bodyWidth >= MacroWideBodyWidth;
        var listWidth = wide ? MacroWideListWidth : MacroNarrowListWidth;
        var keyBindsWidth = wide ? MacroWideKeyBindsWidth : MacroNarrowKeyBindsWidth;
        if (listWidth != _macroListColumnWidth)
        {
            _macroListColumnWidth = listWidth;
            MacroBodyLayout.ColumnDefinitions[0].Width = new GridLength(listWidth);
        }

        if (keyBindsWidth != _macroKeyBindsColumnWidth)
        {
            _macroKeyBindsColumnWidth = keyBindsWidth;
            MacroBodyLayout.ColumnDefinitions[2].Width = new GridLength(keyBindsWidth);
        }

        var editorWidth = bodyWidth - listWidth - keyBindsWidth - (MacroBodyColumnSpacing * 2);

        var toolbarStacked = editorWidth < MacroToolbarMinWidth;
        if (toolbarStacked != _macroToolbarStacked)
        {
            _macroToolbarStacked = toolbarStacked;
            MacroToolbar.Orientation = toolbarStacked ? Orientation.Vertical : Orientation.Horizontal;
            MacroToolbar.Spacing = toolbarStacked ? 6 : 8;
        }

        // The duration chip is the only element in the playback row that can go
        // away; the play and save buttons always stay.
        var hideDuration = editorWidth < MacroPlaybackBarMinWidth;
        if (hideDuration != _macroDurationHidden)
        {
            _macroDurationHidden = hideDuration;
            MacroDurationChip.Visibility = hideDuration ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private async void CreateMacroClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CreateMacroAsync(_lifetime.Token);

    private async void DeleteMacroRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { DataContext: MacroRowViewModel row })
        {
            return;
        }

        if (!ReferenceEquals(_viewModel.SelectedMacro, row))
        {
            _viewModel.SelectedMacro = row;
        }

        await _viewModel.DeleteSelectedMacroAsync(_lifetime.Token);
    }

    private async void MacroRecordStartClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsMacroRecording)
        {
            await _viewModel.ToggleMacroRecordingAsync(_lifetime.Token);
        }
    }

    private async void MacroRecordStopClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsMacroRecording)
        {
            await _viewModel.ToggleMacroRecordingAsync(_lifetime.Token);
        }
    }

    private void MacroRecordDelaysClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem item)
        {
            _viewModel.IsMacroRecordingDelays = item.IsChecked;
        }
    }


    // A MenuFlyoutItem cannot host arbitrary content in WinUI, so the on-screen
    // key grid lives in a second flyout on the Insert button itself.
    private void MacroInsertPickKeyClick(object sender, RoutedEventArgs e) =>
        ((FlyoutBase)MacroKeyPickerAnchor.Flyout).ShowAt(MacroInsertButton);

    private async void MacroInsertCaptureClick(object sender, RoutedEventArgs e) =>
        await _viewModel.AddMacroInputStepAsync(_lifetime.Token);

    private async void AddMacroDelayClick(object sender, RoutedEventArgs e) =>
        await _viewModel.AddMacroDelayAsync(_lifetime.Token);

    private async void MacroKeyOptionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MacroKeyOptionViewModel option })
        {
            return;
        }

        _viewModel.MacroKeyPicker.Selected = option;
        await _viewModel.AddMacroStepFromPickerAsync(_lifetime.Token);
    }

    private async void CaptureMacroTriggerClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CaptureMacroTriggerAsync(_lifetime.Token);

    private async void PlayMacroClick(object sender, RoutedEventArgs e) =>
        await _viewModel.PlaySelectedMacroAsync(_lifetime.Token);

    private async void SaveMacroClick(object sender, RoutedEventArgs e) =>
        await _viewModel.SaveSelectedMacroAsync(_lifetime.Token);

    private async void RecaptureMacroStepClick(object sender, RoutedEventArgs e) =>
        await _viewModel.RecaptureMacroStepInputAsync(_lifetime.Token);

    private async void MoveMacroStepUpClick(object sender, RoutedEventArgs e) =>
        await _viewModel.MoveSelectedMacroStepAsync(-1, _lifetime.Token);

    private async void MoveMacroStepDownClick(object sender, RoutedEventArgs e) =>
        await _viewModel.MoveSelectedMacroStepAsync(1, _lifetime.Token);

    private async void DeleteMacroStepClick(object sender, RoutedEventArgs e) =>
        await _viewModel.DeleteSelectedMacroStepAsync(_lifetime.Token);
}
