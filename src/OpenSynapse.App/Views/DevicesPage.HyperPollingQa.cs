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
    internal static FrameworkElement CreateHyperPollingPreview()
    {
        var transport = new HyperPollingQaTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        RazerHidDeviceViewModel Device(ushort productId)
        {
            var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
            return new RazerHidDeviceViewModel(service, new RazerHidDeviceConnection(definition, "qa-only", "qa-only",
                RazerHidEndpointState.Resolved, RazerHidDeviceService.GetCapabilities(definition),
                RazerHidDeviceService.GetLightingZoneCapabilities(definition), null));
        }
        var receiver = Device(0x00B3);
        receiver.HyperPollingIndicatorMode = 2;
        var page = new DevicesPage();
        var root = new StackPanel { Spacing = 20 };
        var card = page.HyperPollingCard;
        page.RazerHidDevicePanel.Children.Remove(card);
        card.DataContext = receiver;
        var grid = ((StackPanel)card.Child).Children.OfType<Grid>().Single();
        var apply = grid.Children.OfType<Button>().Single(button => Grid.GetColumn(button) == 1);
        apply.Click -= page.RazerHidApplyHyperPollingIndicatorClick;
        AutomationProperties.SetAutomationId(apply, "HyperPollingQaApply");
        var status = new TextBlock { Text = "READY mode=2", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, "HyperPollingQaStatus");
        apply.Click += async (_, _) =>
        {
            await receiver.SetHyperPollingIndicatorAsync();
            status.Text = $"mode={transport.Mode}; selected={receiver.HyperPollingIndicatorMode}; " +
                $"error={!string.IsNullOrEmpty(receiver.ErrorText)}; visible={receiver.HyperPollingIndicatorVisibility}";
        };
        root.Children.Add(card);
        root.Children.Add(status);
        var fail = new Button { Content = "模拟设备未接受写入", Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
        AutomationProperties.SetAutomationId(fail, "HyperPollingQaReject");
        fail.Click += (_, _) => transport.IgnoreWrites = true;
        root.Children.Add(fail);

        var mouse = Device(0x00C5);
        for (byte index = 1; index <= 5; index++)
            mouse.DpiStages.Add(new RazerHidDpiStageRowViewModel(index, index * 400, index * 400, 0, true));
        mouse.ActiveDpiStage = 2;
        var stages = page.HidDpiStagesAutoApply;
        var dpiCard = page.RazerHidDevicePanel.Children.OfType<Border>().Single(border => border.Child == stages);
        page.RazerHidDevicePanel.Children.Remove(dpiCard);
        dpiCard.DataContext = mouse;
        root.Children.Add(dpiCard);
        Localized.RefreshTree(root);
        return root;
    }

    private sealed class HyperPollingQaTransport : IRazerFeatureTransport
    {
        internal byte Mode = 2;
        internal bool IgnoreWrites;
        public Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize,
            byte commandClass, byte commandId, ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new InvalidOperationException("QA must not access hardware.");

        public Task<byte[]> QueryPreparedAsync(string devicePath, ReadOnlyMemory<byte> request,
            TimeSpan deviceWait, CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
        {
            var response = request.ToArray();
            if (response[7] != 0x07 || response[8] is not (0x10 or 0x90))
                throw new InvalidOperationException("Unexpected QA command.");
            if (response[8] == 0x10 && !IgnoreWrites) Mode = response[9];
            response[1] = 0x02;
            response[9] = Mode;
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }
}
#endif
