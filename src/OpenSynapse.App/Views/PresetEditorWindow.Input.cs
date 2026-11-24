using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace OpenSynapse.App.Views;

public sealed partial class PresetEditorWindow
{
    private bool IsTextEditingFocus()
    {
        var focused = FocusManager.GetFocusedElement(RootLayout.XamlRoot) as DependencyObject;
        for (var depth = 0; focused is not null && depth < 12; depth++)
        {
            if (focused is TextBox or RichEditBox or PasswordBox) return true;
            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
    }

    private void NewAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = NewDocumentAsync();
    }

    private void OpenAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = OpenDocumentAsync();
    }

    private void SaveAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = SaveAsync();
    }

    private void SaveAsAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = SaveAsAsync();
    }

    private void UndoAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextEditingFocus()) return;
        args.Handled = true;
        _editor.Undo();
    }

    private void RedoAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextEditingFocus()) return;
        args.Handled = true;
        _editor.Redo();
    }

    private void PlayAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextEditingFocus()) return;
        args.Handled = true;
        TogglePlayback();
    }
}
