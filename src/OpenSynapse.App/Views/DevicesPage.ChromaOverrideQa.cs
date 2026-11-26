#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage
{
    // Show the compiled device-page controls with real capabilities, without discovery or hardware access.
    internal static FrameworkElement CreateChromaOverridePreview()
    {
        var root = new StackPanel { Spacing = 20 };
        root.Children.Add(new TextBlock { Text = "Chroma 接管 · 全局服务关闭时的设备入口", FontSize = 24 });
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, new NoCaptureDeviceTransport());
        foreach (var productId in new ushort[] { 0x02B0, 0x0F0D, 0x00A6 })
        {
            var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
            var connection = new RazerHidDeviceConnection(definition, "qa-only", "qa-only",
                RazerHidEndpointState.Resolved, RazerHidDeviceService.GetCapabilities(definition),
                RazerHidDeviceService.GetLightingZoneCapabilities(definition), null);
            var vm = new RazerHidDeviceViewModel(service, connection,
                lightingSettingsSaver: (_, _, _) => throw new InvalidOperationException("Preview must not save."),
                chromaIntegrationEnabled: false);
            var page = new DevicesPage();
            var group = page.RazerHidLightingSettingsGroup;
            var row = group.Children.OfType<StackPanel>().Single(panel => panel.Children.OfType<ToggleSwitch>()
                .Any(toggle => toggle.Tag is "chromaOverride"));
            group.Children.Remove(row);
            row.DataContext = vm;
            var toggle = row.Children.OfType<ToggleSwitch>().Single(toggle => toggle.Tag is "chromaOverride");
            AutomationProperties.SetAutomationId(toggle, $"QaChromaToggle{productId:X4}");
            var status = new TextBlock();
            AutomationProperties.SetAutomationId(status, $"QaChromaStatus{productId:X4}");
            void RefreshStatus() => status.Text = vm.ChromaOverrideVisibility == Visibility.Collapsed
                ? "PASS no lighting control" : $"PASS override={vm.ChromaOverrideEnabled}";
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.ChromaOverrideEnabled)) RefreshStatus();
            };
            RefreshStatus();
            var card = new StackPanel { Spacing = 12 };
            card.Children.Add(new TextBlock { Text = definition.DisplayName, FontSize = 18 });
            card.Children.Add(row);
            card.Children.Add(status);
            root.Children.Add(new Border { Style = (Style)Application.Current.Resources["CardStyle"],
                Padding = new Thickness(18), Child = card });
        }
        Localized.RefreshTree(root);
        return root;
    }

    private sealed class NoCaptureDeviceTransport : IRazerFeatureTransport
    {
        public Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize,
            byte commandClass, byte commandId, ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new InvalidOperationException("Preview must not access hardware.");

        public Task<byte[]> QueryPreparedAsync(string devicePath, ReadOnlyMemory<byte> request,
            TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new InvalidOperationException("Preview must not access hardware.");
    }
}
#endif
