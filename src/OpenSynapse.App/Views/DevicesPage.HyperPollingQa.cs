#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.Core.Profiles;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage
{
    internal static async Task<FrameworkElement> CreatePollingRateQaAsync()
    {
        var transport = new PollingRateQaTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00C5)!
            .WithPollingRates([125, 500, 1000, 2000, 4000, 8000]);
        var connection = new RazerHidDeviceConnection(definition, "qa-polling", "qa-polling",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.PollingRateRead, RazerHidBackendCapability.PollingRateWrite },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);
        connection.PollingRateUsesHighRate = true;
        var profile = new ProfileDefinition();
        var saves = 0;
        var mouse = new RazerHidDeviceViewModel(service, connection, getActiveProfile: () => profile,
            saveProfileAsync: _ => { saves++; return Task.FromResult(true); });
        await mouse.LoadBasicStateAsync();
        var page = new DevicesPage();
        var original = page.HidPollingAutoApply;
        var card = page.RazerHidControlsGrid.Children.OfType<Border>()
            .Single(border => border.Child is StackPanel panel && panel.Children.Contains(original));
        var content = (StackPanel)card.Child;
        page.RazerHidControlsGrid.Children.Remove(card);
        var editor = original.Children.OfType<Grid>().First();
        original.Children.Remove(editor);
        var group = new StackPanel { Name = "PollingQa", Spacing = 8 };
        group.Children.Add(editor);
        var position = content.Children.IndexOf(original);
        content.Children.Remove(original);
        content.Children.Insert(position, group);
        var fullscreen = content.Children.OfType<StackPanel>().Last();
        fullscreen.Children.OfType<ToggleSwitch>().Single().Toggled -= page.RazerHidFullscreenPollingToggled;
        fullscreen.Children.OfType<ComboBox>().Single().SelectionChanged -= page.RazerHidFullscreenPollingRateChanged;
        card.DataContext = mouse;
        var combo = editor.Children.OfType<ComboBox>().Single();
        AutomationProperties.SetAutomationId(combo, "PollingQaCombo");
        var status = new TextBlock { Text = "READY", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, "PollingQaStatus");
        var root = new StackPanel { Spacing = 16 };
        root.Children.Add(card);
        root.Children.Add(status);
        var queue = new SettingApplyQueue();
        SettingAutoApply.Attach(group, queue, () => mouse, () => mouse.PreparePollingAutoApply(default));
        AddButton("PollingQaRefresh", "刷新设备状态", async () =>
        {
            var writes = transport.Writes;
            await mouse.LoadBasicStateAsync();
            await Task.Delay(150);
            status.Text = $"selected={combo.SelectedItem ?? "EMPTY"}; device={mouse.PollingRateText}; extraWrites={transport.Writes - writes}; canWrite={mouse.CanWritePolling}";
        });
        var savesBeforeFailure = 0;
        AddButton("PollingQaReadbackFailure", "模拟读回失败", () =>
        {
            savesBeforeFailure = saves;
            transport.FailReadback = true;
            status.Text = "READY fail readback";
            return Task.CompletedTask;
        });
        AddButton("PollingQaRecover", "恢复读回", () =>
        {
            status.Text = !string.IsNullOrEmpty(mouse.ErrorText) && mouse.CanWritePolling &&
                mouse.PollingWriteVisibility == Visibility.Visible && saves == savesBeforeFailure &&
                mouse.PollingRateText == "8000 Hz" && combo.SelectedItem as string == "4000 Hz"
                ? "PASS failed readback kept controls and profile"
                : $"FAIL readback: error={mouse.ErrorText}; canWrite={mouse.CanWritePolling}; saves={saves - savesBeforeFailure}";
            transport.FailReadback = false;
            return Task.CompletedTask;
        });
        AddButton("PollingQaVerifyRetry", "验证重试", () =>
        {
            status.Text = string.IsNullOrEmpty(mouse.ErrorText) && mouse.PollingRateText == "4000 Hz" &&
                combo.SelectedItem as string == "4000 Hz" && saves == savesBeforeFailure + 1 &&
                profile.Devices.Values.Single().Viper.PollingRateHertz == 4000
                ? "PASS retry confirmed and saved 4000 Hz"
                : $"FAIL retry: error={mouse.ErrorText}; selected={combo.SelectedItem ?? "EMPTY"}; saves={saves - savesBeforeFailure}";
            return Task.CompletedTask;
        });
        Localized.RefreshTree(root);
        return root;

        void AddButton(string id, string label, Func<Task> action)
        {
            var button = new Button { Content = label, Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += async (_, _) =>
            {
                try { await action(); }
                catch (Exception error) { status.Text = "FAIL " + error; }
            };
            root.Children.Add(button);
        }
    }

    private sealed class PollingRateQaTransport : IRazerFeatureTransport
    {
        internal byte Rate = 0x08;
        internal int Writes;
        internal bool FailReadback;
        public Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize,
            byte commandClass, byte commandId, ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new InvalidOperationException("QA must not access hardware.");

        public Task<byte[]> QueryPreparedAsync(string devicePath, ReadOnlyMemory<byte> request,
            TimeSpan deviceWait, CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
        {
            var response = request.ToArray();
            switch (response[7], response[8])
            {
                case (0x05, 0x80): response[9] = 1; break;
                case (0x00, 0x40): Rate = response[10]; Writes++; break;
                case (0x00, 0xC0):
                    if (FailReadback) throw new RazerDeviceTimeoutException("QA readback busy", retryableBusy: true);
                    response[10] = Rate;
                    break;
                default: throw new InvalidOperationException($"Unexpected polling QA command {response[7]:X2}:{response[8]:X2}.");
            }
            response[1] = 0x02;
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }

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
