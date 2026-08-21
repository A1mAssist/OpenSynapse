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
        if (element is TextBlock textBlock && AppStrings.TryGet($"{uid}/Text") is { } text)
        {
            textBlock.Text = text;
        }
        else if (element is MenuFlyoutItem menuItem && AppStrings.TryGet($"{uid}/Text") is { } menuText)
        {
            menuItem.Text = menuText;
        }
        else if (element is ToggleMenuFlyoutItem toggleMenuItem && AppStrings.TryGet($"{uid}/Text") is { } toggleMenuText)
        {
            toggleMenuItem.Text = toggleMenuText;
        }

        if (element is ContentControl contentControl && AppStrings.TryGet($"{uid}/Content") is { } content)
        {
            contentControl.Content = content;
        }
        if (element is NumberBox numberBox && AppStrings.TryGet($"{uid}/Header") is { } header)
        {
            numberBox.Header = header;
        }
        if (element is TextBox textBox && AppStrings.TryGet($"{uid}/PlaceholderText") is { } placeholder)
        {
            textBox.PlaceholderText = placeholder;
        }
        if (element is ToggleSwitch toggleSwitch)
        {
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
        var uid = GetUid(element);
        if (!string.IsNullOrWhiteSpace(uid))
        {
            Apply(element, uid);
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            RefreshDescendants(VisualTreeHelper.GetChild(element, index));
        }
    }
}
