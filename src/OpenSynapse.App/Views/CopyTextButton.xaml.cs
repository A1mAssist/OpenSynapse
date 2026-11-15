using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace OpenSynapse.App.Views;

/// <summary>Copies one short value, used beside serial numbers and device IDs.</summary>
public sealed partial class CopyTextButton : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(CopyTextButton),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public CopyTextButton()
    {
        InitializeComponent();
        var tooltip = AppStrings.Text("CopyTextTooltip");
        ToolTipService.SetToolTip(CopyButton, tooltip);
        AutomationProperties.SetName(CopyButton, tooltip);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var button = (CopyTextButton)sender;
        button.CopyButton.Visibility = IsCopyable(e.NewValue as string)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // Serial placeholders are dashes in the device view models; do not offer them as a value.
    private static bool IsCopyable(string? text)
    {
        var value = text?.Trim();
        return !string.IsNullOrEmpty(value) &&
            value.AsSpan().IndexOfAnyExcept('-', '\u2014') >= 0;
    }

    private async void CopyClick(object sender, RoutedEventArgs e)
    {
        if (!IsCopyable(Text))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(Text.Trim());
        try
        {
            Clipboard.SetContent(package);
        }
        catch (COMException)
        {
            // The clipboard can be held open by another process for a few milliseconds.
            await Task.Delay(80);
            try
            {
                Clipboard.SetContent(package);
            }
            catch (COMException)
            {
                return;
            }
        }

        Glyph.Glyph = "\uE73E";
        await Task.Delay(1200);
        Glyph.Glyph = "\uE8C8";
    }
}
