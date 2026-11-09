using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace OpenSynapse.App.Views;

/// <summary>Studio keyboard shortcuts; text editors keep their own key handling.</summary>
public sealed partial class LightingStudioPage
{
    private void UndoAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused()) return;
        _historyTimer.Stop();
        _viewModel?.CommitHistory();
        _viewModel?.Undo();
        args.Handled = true;
    }

    private void RedoAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused()) return;
        _historyTimer.Stop();
        _viewModel?.CommitHistory();
        _viewModel?.Redo();
        args.Handled = true;
    }

    private void RenameAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused()) return;
        LayerNameBox.Focus(FocusState.Programmatic);
        LayerNameBox.SelectAll();
        args.Handled = true;
    }

    private void DuplicateAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused()) return;
        _viewModel?.DuplicateLayer();
        args.Handled = true;
    }

    private void DeleteAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused()) return;
        _viewModel?.RemoveLayer();
        args.Handled = true;
    }

    private void PenAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused()) return;
        ToolPicker.SelectedIndex = 1;
        args.Handled = true;
    }

    private void BucketAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused()) return;
        ToolPicker.SelectedIndex = 2;
        args.Handled = true;
    }

    private void ToggleInspectorClick(object sender, RoutedEventArgs e)
    {
        if (InspectorPanel is null) return;
        var visible = InspectorPanel.Visibility == Visibility.Visible;
        InspectorPanel.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        InspectorChevron.Glyph = visible ? "\uE70D" : "\uE70E";
    }

    private bool IsTextInputFocused() =>
        XamlRoot is not null &&
        FocusManager.GetFocusedElement(XamlRoot) is TextBox or PasswordBox or RichEditBox;
}
