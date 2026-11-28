#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage
{
    internal static async Task<FrameworkElement> CreateApplicationRulesPreview()
    {
        var page = new DevicesPage();
        page.Loaded -= page.DevicesPageLoaded;
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00A7)!;
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, new HyperPollingQaTransport());
        RazerHidDeviceViewModel Device(bool mapping) => new(service, new(
            definition, "qa-rules", "qa-rules", RazerHidEndpointState.Resolved,
            mapping ? new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.MappingRead } : [],
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null));
        var mouse = Device(true);
        var document = ProfileDocument.CreateDefault();
        document.Profiles["游戏"] = new ProfileDefinition();
        var coordinator = new DeviceApplicationSwitchCoordinator();
        var rules = new DeviceApplicationRulesWorkspace(
            new DeviceApplicationRuleStore(AppDataPaths.Combine("application-rules-card-qa.json")),
            () => document, () => [mouse.Connection], () => coordinator, () => { });
        rules.Select(new DeviceBindingKey(0x1532, definition.ProductId, "qa-rules"));
        rules.RefreshRunningApplications([@"C:\Games\Counter-Strike 2\game\bin\win64\cs2.exe"]);
        await rules.AddAsync(@"C:\Games\Counter-Strike 2\game\bin\win64\cs2.exe", "游戏", default);
        page.ApplicationRulesPanel.DataContext = rules;
        page.ApplicationRulesPanel.BindDeviceRules();
        page.RazerHidDevicePanel.DataContext = mouse;
        page.RazerHidDevicePanel.Visibility = Visibility.Visible;
        var children = page.RazerHidDevicePanel.Children;
        if (children.IndexOf(page.ApplicationRulesPanel) != children.IndexOf(page.RazerHidMappingCard) + 1)
            throw new InvalidOperationException("Rules must immediately follow the mapping card.");
        foreach (var child in children.ToArray())
            if (child != page.RazerHidMappingCard && child != page.ApplicationRulesPanel) children.Remove(child);
        var mapping = (RazerMappingView)page.RazerHidMappingCard.Child;
        AutomationProperties.SetAutomationId((Button)mapping.FindName("CollapseButton"), "RulesQaCollapseMapping");
        var root = (StackPanel)page.PageScrollViewer.Content;
        root.Children.Clear();
        root.Children.Add(new TextBlock { Text = "Razer Naga V2 Pro", FontSize = 24 });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var status = new TextBlock();
        AutomationProperties.SetAutomationId(status, "RulesQaStatus");
        foreach (var supported in new[] { true, false })
        {
            var button = new Button
            {
                Content = supported ? "支持映射的设备" : "无映射能力的设备",
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            };
            AutomationProperties.SetAutomationId(button, supported ? "RulesQaSupported" : "RulesQaUnsupported");
            button.Click += async (_, _) =>
            {
                page.RazerHidDevicePanel.DataContext = supported ? mouse : Device(false);
                await Task.Yield();
                page.UpdateLayout();
                var expected = supported ? Visibility.Visible : Visibility.Collapsed;
                status.Text = page.ApplicationRulesPanel.Visibility == expected && page.RazerHidMappingCard.Visibility == expected
                    ? supported ? "PASS cards visible" : "PASS cards hidden" : "FAIL card visibility";
            };
            toolbar.Children.Add(button);
        }
        root.Children.Add(toolbar);
        root.Children.Add(status);
        root.Children.Add(page.RazerHidDevicePanel);
        Localized.RefreshTree(page);
        return page;
    }
}
#endif
