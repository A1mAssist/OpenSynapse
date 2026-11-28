#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage
{
    internal static FrameworkElement CreateLightingSettingsPreview()
    {
        var page = new DevicesPage();
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00AB)!;
        var connection = new RazerHidDeviceConnection(definition, "qa-only", "qa-only",
            RazerHidEndpointState.Resolved, RazerHidDeviceService.GetCapabilities(definition),
            RazerHidDeviceService.GetLightingZoneCapabilities(definition), null);
        var receiver = new RazerHidDeviceViewModel(new RazerHidDeviceService(
            RazerHidDeviceCatalog.BuiltIn, new HyperPollingQaTransport()), connection);
        var root = new StackPanel { Spacing = 16 };
        root.Children.Add(new TextBlock { Text = "Basilisk V3 Pro · 接收器", FontSize = 24 });
        var indicator = page.HidIndicatorAutoApply;
        page.RazerHidDevicePanel.Children.OfType<Border>().Select(card => card.Child).OfType<StackPanel>()
            .SelectMany(panel => panel.Children).OfType<Grid>()
            .Single(grid => grid.Children.Contains(indicator)).Children.Remove(indicator);
        // Preview the production controls without the page's hardware auto-apply callbacks.
        var receiverPreview = new StackPanel { Spacing = indicator.Spacing, DataContext = receiver };
        foreach (var child in indicator.Children.ToArray())
        {
            indicator.Children.Remove(child);
            receiverPreview.Children.Add(child);
        }
        root.Children.Add(receiverPreview);

        root.Children.Add(new TextBlock { Text = "Chroma Wireless ARGB", FontSize = 24 });
        var argb = new RazerChromaViewModel(new RazerChromaDeviceConnection(
            "qa-only", 0x0F2C, "Chroma Wireless ARGB", DeviceCategory.Lighting));
        var card = page.ChromaPanel.Children.OfType<Border>().First();
        page.ChromaPanel.Children.Remove(card);
        card.DataContext = argb;
        var controls = (StackPanel)card.Child;
        var effect = controls.Children.OfType<Grid>().First().Children.OfType<StackPanel>().First()
            .Children.OfType<ComboBox>().Single();
        AutomationProperties.SetAutomationId(effect, "QaWirelessArgbEffect");
        // Only the local selector is enabled; hardware actions remain disabled.
        effect.IsEnabled = true;
        root.Children.Add(card);
        Localized.RefreshTree(root);
        return root;
    }
}
#endif
