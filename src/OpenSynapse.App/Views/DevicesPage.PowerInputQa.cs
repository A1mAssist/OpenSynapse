#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage
{
    internal static async Task<FrameworkElement> CreatePowerInputQaAsync()
    {
        var transport = new PowerInputQaTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00D7)!;
        var connection = new RazerHidDeviceConnection(definition, "qa-power", "qa-power",
            RazerHidEndpointState.Resolved, new HashSet<RazerHidBackendCapability>
            {
                RazerHidBackendCapability.IdleTimeoutRead, RazerHidBackendCapability.IdleTimeoutWrite,
                RazerHidBackendCapability.LowBatteryThresholdRead, RazerHidBackendCapability.LowBatteryThresholdWrite,
            }, new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);
        var profile = new ProfileDefinition();
        var saves = 0;
        var pairClicks = 0;
        var mouse = new RazerHidDeviceViewModel(service, connection, getActiveProfile: () => profile,
            saveProfileAsync: _ => { saves++; return Task.FromResult(true); });
        await mouse.LoadBasicStateAsync();
        var page = new DevicesPage();
        var card = page.RazerHidControlsGrid.Children.OfType<Border>()
            .Single(border => border.Child is StackPanel panel && panel.Children.OfType<Grid>()
                .Any(grid => grid.Children.Contains(page.HidIdleAutoApply)));
        var content = (StackPanel)card.Child;
        var grid = content.Children.OfType<Grid>().Single(grid => grid.Children.Contains(page.HidIdleAutoApply));
        page.RazerHidControlsGrid.Children.Remove(card);
        card.DataContext = mouse;
        var queue = new SettingApplyQueue();
        Replace(page.HidIdleAutoApply, "QaPowerIdle", () => mouse.PrepareIdleAutoApply(default));
        Replace(page.HidLowBatteryAutoApply, "QaPowerThreshold", () => mouse.PrepareLowBatteryAutoApply(default));
        var root = new StackPanel { Spacing = 16 };
        var pair = new Button { Content = "配对新设备", Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
        AutomationProperties.SetAutomationId(pair, "QaPowerPair");
        pair.Click += (_, _) => pairClicks++;
        root.Children.Add(pair);
        root.Children.Add(card);
        var blank = new Border { Height = 90, Background = (Brush)Application.Current.Resources["CanvasBrush"],
            Child = new TextBlock { Text = "点击此处空白区域结束输入", VerticalAlignment = VerticalAlignment.Center } };
        AutomationProperties.SetAutomationId(blank, "QaPowerBlank");
        root.Children.Add(blank);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, "QaPowerStatus");
        root.Children.Add(status);
        var fail = new CheckBox { Content = "模拟下一次写入超时" };
        fail.Checked += (_, _) => transport.FailNext = true;
        root.Children.Add(fail);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            var saved = profile.Devices.Values.FirstOrDefault()?.Viper;
            var focus = root.XamlRoot is { } xamlRoot ? Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot) : null;
            status.Text = $"writes={transport.Writes}; saves={saves}; pair={pairClicks}; " +
                $"device={transport.Idle}/{transport.ThresholdPercent}; saved={saved?.IdleSeconds}/{saved?.LowBatteryThresholdPercent}; focus={focus?.GetType().Name}";
        };
        root.Loaded += (_, _) => timer.Start();
        root.Unloaded += (_, _) => timer.Stop();
        Localized.RefreshTree(root);
        return root;

        void Replace(StackPanel original, string name, Func<Func<Task<string?>>?> prepare)
        {
            var group = new StackPanel { Name = name, Spacing = original.Spacing };
            Grid.SetColumn(group, Grid.GetColumn(original));
            foreach (var child in original.Children.ToArray())
            {
                // Keep the actual XAML inputs, replacing only the helper whose lifetime belongs to the full page.
                if (ReferenceEquals(child, original.Children.Last())) break;
                original.Children.Remove(child);
                group.Children.Add(child);
            }
            var index = grid.Children.IndexOf(original);
            grid.Children.Remove(original);
            grid.Children.Insert(index, group);
            var editor = group.Children.OfType<Grid>().Single();
            AutomationProperties.SetAutomationId(editor.Children.OfType<NumberBox>().Single(), name);
            SettingAutoApply.Attach(group, queue, () => mouse, prepare);
        }
    }

    private sealed class PowerInputQaTransport : IRazerFeatureTransport
    {
        internal int Idle = 300;
        internal byte Threshold = 39;
        internal int ThresholdPercent => (int)Math.Floor(Threshold * 100d / 255d);
        internal int Writes;
        internal bool FailNext;
        public Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize,
            byte commandClass, byte commandId, ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new InvalidOperationException("QA must not access hardware.");

        public async Task<byte[]> QueryPreparedAsync(string devicePath, ReadOnlyMemory<byte> request,
            TimeSpan deviceWait, CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
        {
            var response = request.ToArray();
            if (response[7] == 0x07 && response[8] is 0x03 or 0x01)
            {
                await Task.Delay(400, cancellationToken);
                if (FailNext) { FailNext = false; throw new RazerDeviceTimeoutException("QA write timeout", retryableBusy: true); }
                Writes++;
            }
            switch (response[7], response[8])
            {
                case (0x07, 0x03): Idle = (response[9] << 8) | response[10]; break;
                case (0x07, 0x83): response[9] = (byte)(Idle >> 8); response[10] = (byte)Idle; break;
                case (0x07, 0x01): Threshold = response[9]; break;
                case (0x07, 0x81): response[9] = Threshold; break;
                default: throw new InvalidOperationException($"Unexpected power QA command {response[7]:X2}:{response[8]:X2}.");
            }
            response[1] = 0x02;
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return response;
        }
    }
}
#endif
