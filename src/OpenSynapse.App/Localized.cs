using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OpenSynapse.App;

public sealed class Localized : DependencyObject
{
    private static readonly List<WeakReference<DependencyObject>> Elements = [];

    public static readonly DependencyProperty UidProperty = DependencyProperty.RegisterAttached(
        "Uid",
        typeof(string),
        typeof(Localized),
        new PropertyMetadata(null, UidChanged));

    public static void SetUid(DependencyObject element, string value) =>
        element.SetValue(UidProperty, value);

    public static string GetUid(DependencyObject element) =>
        (string)element.GetValue(UidProperty);

    public static void Refresh()
    {
        for (var index = Elements.Count - 1; index >= 0; index--)
        {
            if (Elements[index].TryGetTarget(out var element))
            {
                try
                {
                    Apply(element, GetUid(element));
                }
                catch
                {
                    // A stale template element must not prevent the rest of the window from updating.
                }
            }
            else
            {
                Elements.RemoveAt(index);
            }
        }
    }

    public static void Refresh(DependencyObject element) => Apply(element, GetUid(element));

    public static void RefreshTree(DependencyObject root)
    {
        Refresh();
        RefreshDescendants(root);
    }

    private static void UidChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not string uid)
        {
            return;
        }

        Elements.Add(new(element));
        Apply(element, uid);
    }

    private static void Apply(DependencyObject element, string uid)
    {
        // Bare keys are also used by AppStrings.Text; keep them as a primary-property
        // fallback instead of creating a conflicting PRI value-and-subtree resource.
        var primary = AppStrings.TryGet(uid);

        if (element is TextBlock textBlock && (AppStrings.TryGet($"{uid}/Text") ?? primary) is { } text)
        {
            textBlock.Text = text;
        }
        else if (element is MenuFlyoutItem menuItem && (AppStrings.TryGet($"{uid}/Text") ?? primary) is { } menuText)
        {
            menuItem.Text = menuText;
        }
        else if (element is ToggleMenuFlyoutItem toggleMenuItem && (AppStrings.TryGet($"{uid}/Text") ?? primary) is { } toggleMenuText)
        {
            toggleMenuItem.Text = toggleMenuText;
        }
        else if (element is MenuFlyoutSubItem submenu && (AppStrings.TryGet($"{uid}/Text") ?? primary) is { } submenuText)
        {
            submenu.Text = submenuText;
        }

        if (element is ContentControl contentControl && element is not Expander &&
            (AppStrings.TryGet($"{uid}/Content") ?? primary) is { } content)
        {
            contentControl.Content = content;
        }
        var header = AppStrings.TryGet($"{uid}/Header") ?? primary;
        if (element is NumberBox numberBox && header is not null)
        {
            numberBox.Header = header;
        }
        if (element is ComboBox comboBox)
        {
            if (header is not null) comboBox.Header = header;
            if (AppStrings.TryGet($"{uid}/PlaceholderText") is { } comboPlaceholder)
                comboBox.PlaceholderText = comboPlaceholder;
        }
        if (element is Expander expander && header is not null) expander.Header = header;
        if (element is TextBox textBox && AppStrings.TryGet($"{uid}/PlaceholderText") is { } placeholder)
        {
            textBox.PlaceholderText = placeholder;
        }
        if (element is ToggleSwitch toggleSwitch)
        {
            if (header is not null) toggleSwitch.Header = header;
            toggleSwitch.OnContent = AppStrings.TryGet($"{uid}/OnContent") ?? toggleSwitch.OnContent;
            toggleSwitch.OffContent = AppStrings.TryGet($"{uid}/OffContent") ?? toggleSwitch.OffContent;
        }
        if (AppStrings.TryGet($"{uid}/[using:Microsoft.UI.Xaml.Automation]AutomationProperties/Name") is { } name)
        {
            AutomationProperties.SetName(element, name);
        }
        if (AppStrings.TryGet($"{uid}/[using:Microsoft.UI.Xaml.Controls]ToolTipService/ToolTip") is { } tooltip)
        {
            ToolTipService.SetToolTip(element, tooltip);
        }
    }

    private static void RefreshDescendants(DependencyObject element)
    {
        try
        {
            var uid = GetUid(element);
            if (!string.IsNullOrWhiteSpace(uid))
            {
                Apply(element, uid);
            }
        }
        catch
        {
            // A template element whose native peer is already gone must not take the
            // whole tree down: this runs from a WinRT event callback, where a
            // managed exception cannot cross the ABI boundary and would kill the app.
            // Its children are still visited below.
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            RefreshDescendants(VisualTreeHelper.GetChild(element, index));
        }
    }
}
