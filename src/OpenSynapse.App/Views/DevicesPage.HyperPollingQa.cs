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
    internal static FrameworkElement CreateKeyboardFullscreenPollingQa()
    {
        var profile = ProfileDocument.CreateDefault();
        var saves = 0;
        var acceptSave = true;
        var keyboard = RazerKeyboardViewModel.CreateFullscreenPollingPreview(profile,
            _ => { saves++; return Task.FromResult(acceptSave); });
        var page = new DevicesPage();
        var original = page.KeyboardPollingAutoApply;
        var card = page.KeyboardControlsGrid.Children.OfType<Border>()
            .Single(border => border.Child is StackPanel panel && panel.Children.Contains(original));
        page.KeyboardControlsGrid.Children.Remove(card);
        var content = (StackPanel)card.Child;
        content.Children.Remove(original);
        var fullscreen = content.Children.OfType<StackPanel>().Last();
        var combo = fullscreen.Children.OfType<ComboBox>().Single();
        var toggle = fullscreen.Children.OfType<ToggleSwitch>().Single();
        AutomationProperties.SetAutomationId(combo, "KeyboardFullscreenQaCombo");
        card.DataContext = keyboard;
        var status = new TextBlock { Text = "READY", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, "KeyboardFullscreenQaStatus");
        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(card);
        root.Children.Add(status);
        AddButton("KeyboardFullscreenQaLoad", "读取键盘配置", async () =>
        {
            await keyboard.ApplyPersistedProfileAsync(OpenSynapse.Core.Sensors.PowerSourceKind.FullPowerAdapter);
            status.Text = combo.SelectedItem as string == "8000 Hz" && toggle.IsOn && saves == 0
                ? "PASS keyboard load 8000" : "FAIL keyboard load";
        });
        AddButton("KeyboardFullscreenQaVerify", "验证选择与失败恢复", async () =>
        {
            if (combo.SelectedItem as string != "4000 Hz" || saves != 1 ||
                profile.Devices.Values.Single().Keyboard.FullscreenPollingRateHertz != 4000)
            {
                status.Text = $"FAIL keyboard selection: selected={combo.SelectedItem}; saves={saves}";
                return;
            }
            acceptSave = false;
            await keyboard.SetFullscreenPollingAsync(true, 0);
            var rollback = !string.IsNullOrEmpty(keyboard.ErrorText) && combo.SelectedItem as string == "4000 Hz" &&
                profile.Devices.Values.Single().Keyboard.FullscreenPollingRateHertz == 4000;
            acceptSave = true;
            profile.Devices.Values.Single().Keyboard.FullscreenPollingRateHertz = 2000;
            await keyboard.ApplyPersistedProfileAsync(OpenSynapse.Core.Sensors.PowerSourceKind.FullPowerAdapter);
            status.Text = rollback && combo.SelectedItem as string == "2000 Hz" && saves == 2
                ? "PASS keyboard save rollback and binding refresh" : "FAIL keyboard rollback or refresh";
        });
        AddButton("KeyboardFullscreenQaInherited", "验证继承配置的失败恢复", async () =>
        {
            profile.Devices.Values.Single().Keyboard.FullscreenPollingRateHertz = null;
            profile.Global.Keyboard.FullscreenPollingRateHertz = 8000;
            await keyboard.ApplyPersistedProfileAsync(OpenSynapse.Core.Sensors.PowerSourceKind.FullPowerAdapter);
            acceptSave = false;
            await keyboard.SetFullscreenPollingAsync(true, 1);
            acceptSave = true;
            status.Text = !string.IsNullOrEmpty(keyboard.ErrorText) && toggle.IsOn && combo.SelectedItem as string == "8000 Hz" &&
                profile.Devices.Values.Single().Keyboard.FullscreenPollingRateHertz is null && saves == 3
                ? "PASS inherited keyboard value restored without override" : "FAIL inherited keyboard rollback";
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

    internal static FrameworkElement CreateFullscreenPollingQa()
    {
        var transport = new PollingRateQaTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var profile = new ProfileDefinition();
        var saves = 0;
        var acceptSave = true;
        RazerHidDeviceViewModel Device()
        {
            var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00C5)!;
            var connection = new RazerHidDeviceConnection(definition, "qa-fullscreen", "qa-fullscreen",
                RazerHidEndpointState.Resolved,
                new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.PollingRateRead, RazerHidBackendCapability.PollingRateWrite },
                new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);
            return new RazerHidDeviceViewModel(service, connection, getActiveProfile: () => profile,
                saveProfileAsync: _ => { saves++; return Task.FromResult(acceptSave); });
        }
        var mouse = Device();
        profile.Devices[ProfileResolver.GetDeviceKey(mouse.Connection.ToDescriptor())] = new DeviceProfileSettings
        {
            Viper = new() { PollingRateHertz = 1000, FullscreenPollingRateHertz = 8000 },
        };
        var page = new DevicesPage();
        var original = page.HidPollingAutoApply;
        var card = page.RazerHidControlsGrid.Children.OfType<Border>()
            .Single(border => border.Child is StackPanel panel && panel.Children.Contains(original));
        page.RazerHidControlsGrid.Children.Remove(card);
        var content = (StackPanel)card.Child;
        var editor = original.Children.OfType<Grid>().First();
        original.Children.Remove(editor);
        var editorPosition = content.Children.IndexOf(original);
        content.Children.Remove(original);
        content.Children.Insert(editorPosition, editor);
        var fullscreen = content.Children.OfType<StackPanel>().Last();
        var toggle = fullscreen.Children.OfType<ToggleSwitch>().Single();
        var combo = fullscreen.Children.OfType<ComboBox>().Single();
        AutomationProperties.SetAutomationId(combo, "FullscreenQaCombo");
        AutomationProperties.SetAutomationId(toggle, "FullscreenQaToggle");
        card.DataContext = mouse;
        var status = new TextBlock { Text = "READY", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, "FullscreenQaStatus");
        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(card);
        root.Children.Add(status);
        AddButton("FullscreenQaLoad", "读取高速接收器", async () =>
        {
            await mouse.LoadBasicStateAsync();
            await Task.Delay(100);
            Check("load", 8000);
        });
        AddButton("FullscreenQaReselect", "切换设备后返回", async () =>
        {
            card.DataContext = null;
            mouse = Device();
            card.DataContext = mouse;
            await Task.Delay(100);
            await mouse.LoadBasicStateAsync();
            await Task.Delay(100);
            Check("reselect", 8000);
        });
        AddButton("FullscreenQaRefresh", "重复刷新", async () =>
        {
            for (var index = 0; index < 4; index++) await mouse.LoadBasicStateAsync();
            await Task.Delay(100);
            Check("refresh", 8000);
        });
        AddButton("FullscreenQaCached", "返回已读取的设备", async () =>
        {
            var cached = Device();
            await cached.LoadBasicStateAsync();
            card.DataContext = null;
            mouse = cached;
            card.DataContext = mouse;
            await Task.Delay(100);
            Check("cached", 8000);
        });
        AddButton("FullscreenQaSave", "保存所选全屏轮询率", async () =>
        {
            var selected = combo.SelectedItem as string;
            await mouse.LoadBasicStateAsync();
            await Task.Delay(100);
            status.Text = selected == "4000 Hz" && combo.SelectedItem as string == "4000 Hz" &&
                profile.Devices.Values.Single().Viper.FullscreenPollingRateHertz == 4000 && saves == 1 && transport.Writes == 0
                ? "PASS saved 4000 without hardware writes"
                : $"FAIL save: selected={combo.SelectedItem ?? "EMPTY"}; saves={saves}; writes={transport.Writes}";
        });
        AddButton("FullscreenQaRollback", "验证保存失败恢复", async () =>
        {
            acceptSave = false;
            await mouse.ApplyFullscreenPollingAsync(true, mouse.PollingOptions.Count - 1);
            await Task.Delay(100);
            status.Text = !string.IsNullOrEmpty(mouse.ErrorText) && combo.SelectedItem as string == "4000 Hz" &&
                profile.Devices.Values.Single().Viper.FullscreenPollingRateHertz == 4000 && transport.Writes == 0
                ? "PASS failed save restored 4000"
                : $"FAIL rollback: selected={combo.SelectedItem ?? "EMPTY"}; error={mouse.ErrorText}";
            acceptSave = true;
        });
        AddButton("FullscreenQaToggleVerify", "验证开关保存与恢复", async () =>
        {
            if (toggle.IsOn || profile.Devices.Values.Single().Viper.FullscreenPollingRateHertz is not null || saves != 3)
            {
                status.Text = $"FAIL toggle: enabled={toggle.IsOn}; saves={saves}";
                return;
            }
            acceptSave = false;
            await mouse.ApplyFullscreenPollingAsync(true, mouse.SelectedFullscreenPollingRateIndex);
            var restored = !toggle.IsOn && !string.IsNullOrEmpty(mouse.ErrorText);
            acceptSave = true;
            await mouse.LoadBasicStateAsync();
            status.Text = restored && !toggle.IsOn && saves == 4 && transport.Writes == 0
                ? "PASS toggle save rollback and binding refresh" : "FAIL toggle rollback or refresh";
        });
        Localized.RefreshTree(root);
        return root;

        void Check(string step, int rate) => status.Text =
            combo.SelectedItem as string == $"{rate} Hz" && combo.Items.Count == mouse.PollingOptions.Count &&
            toggle.IsOn && saves == 0 && transport.Writes == 0
                ? $"PASS {step} {rate} Hz; writes=0; saves=0"
                : $"FAIL {step}: selected={combo.SelectedItem ?? "EMPTY"}; items={combo.Items.Count}; " +
                    $"index={combo.SelectedIndex}; expectedIndex={mouse.SelectedFullscreenPollingRateIndex}; " +
                    $"bound={combo.GetBindingExpression(Microsoft.UI.Xaml.Controls.Primitives.Selector.SelectedIndexProperty) is not null}; saves={saves}; writes={transport.Writes}";

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
