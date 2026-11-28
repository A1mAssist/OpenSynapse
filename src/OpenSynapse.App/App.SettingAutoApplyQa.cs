#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.Views;
using OpenSynapse.App.Controls;
using OpenSynapse.App.ViewModels;
using OpenSynapse.App.Views.DevicePanels;
using OpenSynapse.Core.Audio;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using System.Collections.ObjectModel;

namespace OpenSynapse.App;

public partial class App
{
    private Window? _settingAutoApplyQaWindow;

    private bool TryLaunchSettingAutoApplyQa()
    {
        if (Environment.GetEnvironmentVariable("OPENSSYNAPSE_SETTING_AUTO_APPLY_QA") != "1") return false;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE")))
            throw new InvalidOperationException("Auto-apply QA requires an isolated instance.");
        UnhandledException += (_, args) =>
        {
            var directory = OpenSynapse.Core.Storage.AppDataPaths.Root;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "qa-unhandled.txt"), args.Exception.ToString());
        };
        var root = new StackPanel { Padding = new Thickness(28), Spacing = 16,
            Background = (Brush)Resources["CanvasBrush"] };
        var queue = new SettingApplyQueue();
        object context = new();
        var writes = 0;
        var writesAtValues = new List<double>();
        var failNext = false;
        var status = new TextBlock { Text = "READY writes=0", FontSize = 16 };
        AutomationProperties.SetAutomationId(status, "QaResult");
        root.Children.Add(new TextBlock { Text = "自动应用 · 隔离验收", FontSize = 26 });
        root.Children.Add(status);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        root.Children.Add(toolbar);
        void Button(string id, string label, Func<Task> action)
        {
            if (toolbar.Children.Count == 4)
            {
                toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                root.Children.Insert(3, toolbar);
            }
            var button = new Button { Content = label, Style = (Style)Resources["SecondaryButtonStyle"] };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += async (_, _) =>
            {
                try
                {
                    button.Focus(FocusState.Programmatic);
                    await Task.Yield();
                    await action();
                }
                catch (Exception error) { status.Text = "FAIL " + error; }
            };
            toolbar.Children.Add(button);
        }
        StackPanel Group(string name, string label)
        {
            var group = new StackPanel { Name = name, Spacing = 8 };
            group.Children.Add(new TextBlock { Text = label, FontSize = 18 });
            root.Children.Add(new Border { Style = (Style)Resources["CardStyle"], Padding = new Thickness(18), Child = group });
            return group;
        }
        var dpi = Group("QaDpi", "当前 DPI");
        var fields = new Grid { ColumnSpacing = 16 };
        fields.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var x = new NumberBox { Header = "X", Value = 1600, Minimum = 100, Maximum = 30000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var y = new NumberBox { Header = "Y", Value = 1600, Minimum = 100, Maximum = 30000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        AutomationProperties.SetAutomationId(x, "QaDpiX");
        AutomationProperties.SetAutomationId(y, "QaDpiY");
        Grid.SetColumn(y, 1);
        fields.Children.Add(x); fields.Children.Add(y); dpi.Children.Add(fields);
        (double X, double Y) confirmedDpi = (1600, 1600);
        var normalization = Group("QaNormalization", "音量标准化");
        var toggle = new ToggleSwitch { IsOn = false };
        var slider = new Slider { Value = 25, Minimum = 0, Maximum = 100, StepFrequency = 1 };
        AutomationProperties.SetAutomationId(toggle, "QaToggle");
        AutomationProperties.SetAutomationId(slider, "QaSlider");
        normalization.Children.Add(toggle); normalization.Children.Add(slider);
        (bool On, double Value) confirmedNormalization = (false, 25);
        var polling = Group("QaPolling", "轮询率");
        var combo = new ComboBox { ItemsSource = new[] { "125 Hz", "500 Hz", "1000 Hz" }, SelectedIndex = 1 };
        AutomationProperties.SetAutomationId(combo, "QaCombo");
        polling.Children.Add(combo);
        var confirmedPolling = 1;
        var eqGroup = Group("QaEq", "THX 均衡器");
        var bands = new ObservableCollection<RazerHeadsetThxBandViewModel>(
            new[] { 31, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 }.Select(hz => new RazerHeadsetThxBandViewModel(hz)));
        var eq = new EqualizerCurveEditor { Bands = bands };
        eqGroup.Children.Add(eq);
        var confirmedBands = bands.Select(band => band.Level).ToArray();
        async Task<string?> Apply(Action confirm)
        {
            writes++;
            writesAtValues.Add(slider.Value);
            status.Text = $"BUSY writes={writes}";
            await Task.Delay(120);
            if (failNext) { failNext = false; return "模拟设备拒绝写入"; }
            confirm();
            status.Text = $"READY writes={writes}";
            return null;
        }
        SettingAutoApply.Attach(dpi, queue, () => context, () =>
        {
            var desired = (x.Value, y.Value);
            return desired == confirmedDpi ? null : () => Apply(() => confirmedDpi = desired);
        });
        SettingAutoApply.Attach(normalization, queue, () => context, () =>
        {
            var desired = (toggle.IsOn, slider.Value);
            return desired == confirmedNormalization ? null : () => Apply(() => confirmedNormalization = desired);
        });
        SettingAutoApply.Attach(polling, queue, () => context, () =>
        {
            var desired = combo.SelectedIndex;
            return desired == confirmedPolling ? null : () => Apply(() => confirmedPolling = desired);
        });
        SettingAutoApply.Attach(eqGroup, queue, () => context, () =>
        {
            var desired = bands.Select(band => band.Level).ToArray();
            return desired.SequenceEqual(confirmedBands) ? null : () => Apply(() => confirmedBands = desired);
        });
        Button("QaReadback", "模拟设备读回", async () =>
        {
            var before = writes;
            confirmedDpi = (2400, 2400); x.Value = 2400; y.Value = 2400;
            confirmedNormalization = (true, 40); toggle.IsOn = true; slider.Value = 40;
            confirmedPolling = 2; combo.SelectedIndex = 2;
            await Task.Delay(500);
            status.Text = writes == before ? $"PASS readback writes={writes}" : "FAIL readback wrote to device";
        });
        Button("QaFailure", "下次写入失败", () => { failNext = true; return Task.CompletedTask; });
        Button("QaBlur", "完成输入", () => Task.CompletedTask);
        Button("QaNumbers", "验证分组输入", async () =>
        {
            var before = writes;
            x.Focus(FocusState.Programmatic);
            x.Value = confirmedDpi.X + 100;
            await Task.Delay(60);
            y.Focus(FocusState.Programmatic);
            y.Value = confirmedDpi.Y + 100;
            await Task.Delay(60);
            if (writes != before) throw new InvalidOperationException("DPI committed while moving from X to Y");
            toggle.Focus(FocusState.Programmatic);
            await Task.Delay(300);
            await queue.WhenIdle;
            status.Text = writes == before + 1 && confirmedDpi == (x.Value, y.Value)
                ? "PASS grouped numbers" : "FAIL grouped numbers";
        });
        Button("QaSliderBurst", "验证连续调节", async () =>
        {
            var before = writes;
            slider.Value = 60;
            await Task.Delay(40);
            slider.Value = 65;
            await Task.Delay(40);
            slider.Value = 70;
            await Task.Delay(550);
            await queue.WhenIdle;
            status.Text = writes == before + 1 && confirmedNormalization.Value == 70
                ? "PASS slider debounce" : $"FAIL slider debounce writes={writes - before} values={string.Join(",", writesAtValues.Skip(before))} confirmed={confirmedNormalization.Value} draft={slider.Value}";
        });
        Button("QaStale", "验证切换设备", async () =>
        {
            var before = writes;
            var gate = new TaskCompletionSource();
            queue.Enqueue("block", () => gate.Task);
            toggle.IsOn = !toggle.IsOn;
            await Task.Delay(50);
            context = new();
            confirmedNormalization = (toggle.IsOn, slider.Value);
            gate.SetResult();
            await queue.WhenIdle;
            status.Text = writes == before ? "PASS stale device" : "FAIL stale write";
        });
        Button("QaQueue", "验证提交队列", async () =>
        {
            var checkQueue = new SettingApplyQueue();
            var gate = new TaskCompletionSource();
            var calls = new List<int>();
            checkQueue.Enqueue("a", async () => { calls.Add(1); await gate.Task; });
            checkQueue.Enqueue("a", () => { calls.Add(2); return Task.CompletedTask; });
            checkQueue.Enqueue("b", () => { calls.Add(3); return Task.CompletedTask; });
            checkQueue.Enqueue("a", () => { calls.Add(4); return Task.CompletedTask; });
            gate.SetResult();
            await checkQueue.WhenIdle;
            status.Text = calls.SequenceEqual([1, 4, 3]) ? "PASS queue" : "FAIL queue";
        });
        Button("QaEqRebuild", "验证 EQ 重建", async () =>
        {
            var before = writes;
            bands[0] = new RazerHeadsetThxBandViewModel(31);
            await Task.Delay(100);
            Slider? FirstSlider(DependencyObject element)
            {
                if (element is Slider match) return match;
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                    if (FirstSlider(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
                return null;
            }
            var first = FirstSlider(eq) ?? throw new InvalidOperationException("No rebuilt EQ slider");
            first.Value = 2;
            await Task.Delay(550);
            await queue.WhenIdle;
            status.Text = writes == before + 1 && confirmedBands[0] == 2
                ? "PASS rebuilt EQ" : "FAIL rebuilt EQ";
        });
        Button("QaUnload", "验证离开页面", async () =>
        {
            var before = writes;
            var gate = new TaskCompletionSource();
            queue.Enqueue("block", () => gate.Task);
            toggle.IsOn = !toggle.IsOn;
            var desired = toggle.IsOn;
            await Task.Delay(50);
            var card = (UIElement)normalization.Parent;
            var index = root.Children.IndexOf(card);
            root.Children.Remove(card);
            await Task.Delay(50);
            gate.SetResult();
            await queue.WhenIdle;
            root.Children.Insert(index, card);
            status.Text = writes == before + 1 && confirmedNormalization.On == desired
                ? "PASS queued edit after unload" : "FAIL queued edit after unload";
        });
        Button("QaViperVerify", "验证鼠标写入", async () => status.Text = await ViperAutoApplyQa.VerifyAsync());
        Button("QaKeyboardVerify", "验证键盘写入", async () => status.Text = await RazerKeyboardViewModel.VerifyAutoApplyAsync());
        Button("QaAnalogKeyboardVerify", "验证模拟轴选区", async () =>
        {
            await RazerHidDeviceViewModel.VerifyKeyboardAutoApplyAsync();
            status.Text = "PASS analog keyboard selection/profile guards and Snap Tap validation";
        });
        Button("QaKeyboardRows", "验证新增键盘行", async () =>
        {
            var group = Group("QaKeyboardRowsGroup", "旋钮档位");
            var items = new ItemsControl();
            group.Children.Add(items);
            var writes = 0;
            var confirmed = false;
            var check = new CheckBox { Content = "启用档位" };
            SettingAutoApply.Attach(group, queue, () => group, () =>
            {
                var desired = check.IsChecked == true;
                return desired == confirmed ? null : () =>
                {
                    confirmed = desired;
                    writes++;
                    return Task.FromResult<string?>(null);
                };
            });
            await Task.Delay(150);
            items.Items.Add(check);
            await Task.Delay(150);
            if (writes != 0) throw new InvalidOperationException("New keyboard row wrote before editing");
            check.IsChecked = true;
            await Task.Delay(300);
            await queue.WhenIdle;
            if (writes != 1 || !confirmed) throw new InvalidOperationException("Dynamic checkbox did not commit");
            items.Items.Remove(check);
            check.IsChecked = false;
            await Task.Delay(300);
            if (writes != 1) throw new InvalidOperationException("Detached keyboard row wrote");
            if (group.Parent is UIElement card) root.Children.Remove(card);
            status.Text = "PASS keyboard dynamic rows and checkbox";
        });
        Button("QaKeyboardPreview", "键盘面板预览", () =>
        {
            var vm = RazerKeyboardViewModel.CreateAutoApplyPreview();
            root.Children.Clear();
            root.Children.Add(new TextBlock { Text = "Razer Pro Type · 自动应用验收", FontSize = 26 });
            var game = Group("QaKeyboardGame", "游戏模式");
            var toggle = new ToggleSwitch { Header = "游戏模式" };
            toggle.SetBinding(ToggleSwitch.IsOnProperty, new Microsoft.UI.Xaml.Data.Binding
            { Source = vm, Path = new PropertyPath(nameof(vm.GameModeEnabled)), Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay });
            AutomationProperties.SetAutomationId(toggle, "QaKeyboardGameToggle");
            game.Children.Add(toggle);
            var polling = Group("QaKeyboardPolling", "轮询率");
            var combo = new ComboBox { ItemsSource = vm.PollingOptions, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SetBinding(ComboBox.SelectedIndexProperty, new Microsoft.UI.Xaml.Data.Binding
            { Source = vm, Path = new PropertyPath(nameof(vm.SelectedPollingIndex)), Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay });
            AutomationProperties.SetAutomationId(combo, "QaKeyboardPollingCombo");
            polling.Children.Add(combo);
            var brightness = Group("QaKeyboardBrightness", "亮度");
            var slider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1 };
            slider.SetBinding(Slider.ValueProperty, new Microsoft.UI.Xaml.Data.Binding
            { Source = vm, Path = new PropertyPath(nameof(vm.Brightness)), Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay });
            brightness.Children.Add(slider);
            SettingAutoApply.Attach(game, queue, () => vm.KeyboardAutoApplyContext, () => vm.PrepareGameModeAutoApply(CancellationToken.None));
            SettingAutoApply.Attach(polling, queue, () => vm.KeyboardAutoApplyContext, () => vm.PreparePollingAutoApply(CancellationToken.None));
            SettingAutoApply.Attach(brightness, queue, () => vm.KeyboardAutoApplyContext, () => vm.PrepareBrightnessAutoApply(CancellationToken.None));
            return Task.CompletedTask;
        });
        Button("QaViperPreview", "鼠标面板预览", () =>
        {
            root.Children.Clear();
            root.Children.Add(new ViperAutoApplyQa().CreatePreview());
            return Task.CompletedTask;
        });
        Button("QaChromaOverridePreview", "联动开关预览", () =>
        {
            var preview = DevicesPage.CreateChromaOverridePreview();
            root.Children.Clear();
            root.Children.Add(preview);
            return Task.CompletedTask;
        });
        Button("QaProfileCreate", "配置新建", async () =>
        {
            var preview = await ProfilesPage.CreateProfileQaAsync();
            root.Children.Clear();
            root.Children.Add(preview);
        });
        Button("QaHyperPollingPreview", "接收器指示灯", () =>
        {
            var preview = DevicesPage.CreateHyperPollingPreview();
            root.Children.Clear();
            root.Children.Add(preview);
            return Task.CompletedTask;
        });
        Button("QaApplicationRules", "按程序自动切换卡片", async () =>
        {
            _settingAutoApplyQaWindow?.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1280, 1000));
            var preview = await DevicesPage.CreateApplicationRulesPreview();
            root.Children.Clear();
            root.Children.Add(preview);
        });
        Button("QaPollingRate", "轮询率刷新", async () =>
        {
            var preview = await DevicesPage.CreatePollingRateQaAsync();
            root.Children.Clear();
            root.Children.Add(preview);
        });
        Button("QaFullscreenPolling", "全屏轮询率刷新", () =>
        {
            var preview = DevicesPage.CreateFullscreenPollingQa();
            root.Children.Clear();
            root.Children.Add(preview);
            return Task.CompletedTask;
        });
        Button("QaKeyboardFullscreenPolling", "键盘全屏轮询率", () =>
        {
            var preview = DevicesPage.CreateKeyboardFullscreenPollingQa();
            root.Children.Clear();
            root.Children.Add(preview);
            return Task.CompletedTask;
        });
        Button("QaHapticScroll", "Naga Hapticore 曲线", async () =>
        {
            var preview = await HapticScrollQa.CreatePanel();
            root.Children.Clear();
            root.Children.Add(preview);
        });
        Button("QaBleHapticScroll", "Naga 蓝牙滚轮档位", async () =>
        {
            var preview = await HapticScrollQa.CreateBlePanel();
            root.Children.Clear();
            root.Children.Add(preview);
        });
        Button("QaPowerInput", "鼠标电源输入", async () =>
        {
            var preview = await DevicesPage.CreatePowerInputQaAsync();
            root.Children.Clear();
            root.Children.Add(preview);
        });
        Button("QaBladeThxReadback", "验证 Blade THX 失败恢复", async () =>
        {
            status.Text = await ThxAudioViewModel.VerifyReadbackAsync();
        });
        Button("QaThxLocalization", "验证 THX 预设翻译", () =>
        {
            status.Text = ThxAudioViewModel.VerifyPresetLocalization();
            return Task.CompletedTask;
        });
        Button("QaThxPartialRead", "验证 THX 部分读取", async () =>
        {
            var viewModel = new RazerHeadsetAudioViewModel(new RazerHeadsetAudioConnection(
                "qa-thx", 0x0529, "Razer USB Sound Card", 91, HasControlInterface: false));
            var audio = new RazerHeadsetAudioState(null, null, null, null, null, null, null, null, null, null, null, null, []);
            var good = new ThxAudioState(ThxAudioEndpoint.Headphones, true, true, "Custom",
                [new("Custom", "Custom")], new[] { 31, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 }
                    .Select(hz => new ThxAudioBand(hz, 0.5)).ToArray(), new Dictionary<string, IReadOnlyList<double>>(),
                new(true, 40), new(false, 25), true, Sidetone: new(false, 30), BassBoost: new(true, 20),
                CaptureNormalization: new(true, 35), CaptureVocalClarity: new(false, 45),
                CaptureNoiseReduction: new(true, 55), CaptureVoiceGate: new(false, 65));
            var missing = good with { SoundNormalization = null, VoiceClarity = null, BassBoost = null,
                Sidetone = null, CaptureNormalization = null, CaptureVocalClarity = null,
                CaptureNoiseReduction = null, CaptureVoiceGate = null };
            bool[] Availability() => [viewModel.CanApplyThxNormalization, viewModel.CanApplyThxVoiceClarity,
                viewModel.CanApplyThxBassBoost, viewModel.CanApplyThxSidetone,
                viewModel.CanApplyThxCaptureNormalization, viewModel.CanApplyThxCaptureVocalClarity,
                viewModel.CanApplyThxCaptureNoiseReduction, viewModel.CanApplyThxCaptureVoiceGate];
            viewModel.SetAutoApplyPreviewState(audio, missing);
            if (Availability().Any(value => value) || viewModel.PrepareAutoApply(HeadsetAudioSetting.ThxNormalization) is not null)
                throw new InvalidOperationException("Unknown settings remained editable");
            viewModel.SetAutoApplyPreviewState(audio, good with { SelectedPresetId = null });
            if (viewModel.SelectedThxPresetIndex != -1 || viewModel.CanApplyThxEqualizer ||
                viewModel.PrepareAutoApply(HeadsetAudioSetting.ThxBands) is not null)
                throw new InvalidOperationException("Missing preset became an editable first preset");
            viewModel.SetAutoApplyPreviewState(audio, good);
            if (Availability().Any(value => !value)) throw new InvalidOperationException("Readback did not enable settings");
            viewModel.ThxNormalizationLevel = 75;
            if (viewModel.PrepareAutoApply(HeadsetAudioSetting.ThxNormalization) is null)
                throw new InvalidOperationException("Known setting did not prepare an edit");
            viewModel.SetAutoApplyPreviewState(audio, missing);
            if (Availability().Any(value => value) || !viewModel.CanApplyThx ||
                viewModel.ThxNormalizationLevel != 40 || viewModel.ThxBassBoostLevel != 20 ||
                viewModel.PrepareAutoApply(HeadsetAudioSetting.ThxNormalization) is not null ||
                viewModel.ThxStatusText != AppStrings.Text("RazerHeadsetThxPartialRead"))
                throw new InvalidOperationException("Partial read lost known values or allowed stale writes");
            root.Children.Clear();
            root.Children.Add(status);
            status.Text = "PASS THX partial read: 8 settings gated, confirmed values retained";
            ComboBox? presetCombo = null;
            var recovery = new Button { Content = "模拟刷新成功", Style = (Style)Resources["SecondaryButtonStyle"] };
            AutomationProperties.SetAutomationId(recovery, "QaThxRecover");
            recovery.Click += async (_, _) =>
            {
                viewModel.SetAutoApplyPreviewState(audio, good);
                await Task.Delay(50);
                status.Text = Availability().All(value => value) &&
                    presetCombo?.SelectedItem is string selected && selected == AppStrings.Text("ThxPresetCustom")
                    ? "PASS THX recovered: settings editable" : "FAIL THX recovery";
            };
            root.Children.Add(recovery);
            // No Accessories attachment: this production panel cannot send hardware writes.
            var panel = new HeadsetAudioPanel { DataContext = viewModel };
            var reveal = new Button { Content = "查看音效设置", Style = (Style)Resources["SecondaryButtonStyle"] };
            AutomationProperties.SetAutomationId(reveal, "QaThxReveal");
            reveal.Click += (_, _) => (panel.FindName("ThxNormalizationGroup") as FrameworkElement)?.StartBringIntoView();
            root.Children.Add(reveal);
            root.Children.Add(panel);
            await Task.Yield();
            presetCombo = (panel.FindName("ThxPresetGroup") as StackPanel)?.Children.OfType<ComboBox>().Single();
            if (presetCombo is not null) AutomationProperties.SetAutomationId(presetCombo, "QaThxPresetCombo");
            if (panel.FindName("ThxNormalizationGroup") is StackPanel normalizationGroup)
            {
                foreach (var control in normalizationGroup.Children.OfType<ToggleSwitch>())
                    AutomationProperties.SetAutomationId(control, "QaThxNormalizationToggle");
            }
        });
        Button("QaHeadsetPreview", "耳机面板预览", async () =>
        {
            var profile = ProfileDocument.CreateDefault();
            var viewModel = new RazerHeadsetAudioViewModel(new RazerHeadsetAudioConnection(
                "qa-headset", 0x0529, "Razer USB Sound Card", 91, HasControlInterface: false),
                getActiveProfile: () => profile);
            viewModel.SetAutoApplyPreviewState(
                new RazerHeadsetAudioState(null, null, null, null, null, null, null, null, null, null, null, null, []),
                new ThxAudioState(ThxAudioEndpoint.Headphones, true, true, "custom",
                    [new("flat", "Flat"), new("custom", "Custom"), new("EDM", "EDM"),
                     new("EndOfAbyss_imm", "EndOfAbyss_imm"), new("HellLetLooseVietnam_cmm", "HellLetLooseVietnam_cmm")],
                    new[] { 31, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 }
                        .Select(hz => new ThxAudioBand(hz, 0)).ToArray(),
                    new Dictionary<string, IReadOnlyList<double>>(),
                    new(true, 40), new(false, 25), true, Sidetone: new(false, 30), BassBoost: new(true, 20)));
            var called = false;
            var captured = viewModel.PrepareAutoApplyProfileCheck(() => called = true)
                ?? throw new InvalidOperationException("Missing headset profile check");
            profile = ProfileDocument.CreateDefault();
            if (await captured(CancellationToken.None) != AppStrings.Text("SettingAutoApplyCancelled") || called)
                throw new InvalidOperationException("Headset write crossed profiles");
            root.Children.Clear();
            root.Children.Add(new TextBlock { Text = "Razer USB Sound Card · 模拟数据预览", FontSize = 26 });
            var check = new TextBlock { Text = "PASS headset profile capture" };
            AutomationProperties.SetAutomationId(check, "QaHeadsetProfileResult");
            root.Children.Add(check);
            // No Accessories attachment: the production panel cannot dispatch hardware operations.
            var panel = new HeadsetAudioPanel { DataContext = viewModel };
            var saved = false;
            var save = new Button { Content = "模拟保存提示", Style = (Style)Resources["SecondaryButtonStyle"] };
            AutomationProperties.SetAutomationId(save, "QaHeadsetSavedStatus");
            save.Click += async (_, _) =>
            {
                panel.UpdateLayout();
                var controls = (FrameworkElement)panel.FindName("ThxSpatialGroup");
                var before = controls.TransformToVisual(panel).TransformPoint(default).Y;
                var height = panel.ActualHeight;
                saved = !saved;
                viewModel.SetAutoApplyPreviewStatus(saved ? AppStrings.Text("BlackSharkSaved") : string.Empty);
                await Task.Delay(50);
                panel.UpdateLayout();
                var after = controls.TransformToVisual(panel).TransformPoint(default).Y;
                check.Text = Math.Abs(before - after) < 0.1 && Math.Abs(height - panel.ActualHeight) < 0.1
                    ? $"PASS saved status layout: visible={saved}, offset={after - before:0.0}"
                    : $"FAIL saved status moved controls: {before} -> {after}, height {height} -> {panel.ActualHeight}";
            };
            root.Children.Add(save);
            root.Children.Add(panel);
        });
        Button("QaLeviathanPreview", "Leviathan V2 Pro", () =>
        {
            var viewModel = new RazerHeadsetAudioViewModel(new RazerHeadsetAudioConnection(
                "qa-leviathan", 0x0548, "Leviathan V2 Pro", 65,
                Transport: RazerHeadsetAudioTransport.Protocol25Feature65));
            viewModel.SetAudioPreviewState(new RazerHeadsetAudioState(null, null, null, 1,
                Enumerable.Repeat((byte)50, 10).ToArray(), 4, null, null, null, null, null, null, [])
                { SpatialMode = 1 });
            root.Children.Clear();
            root.Children.Add(new TextBlock { Text = "Leviathan V2 Pro", FontSize = 26 });
            var panel = new HeadsetAudioPanel { DataContext = viewModel };
            var reveal = new Button { Content = "查看空间音频", Style = (Style)Resources["SecondaryButtonStyle"] };
            AutomationProperties.SetAutomationId(reveal, "QaLeviathanReveal");
            reveal.Click += (_, _) => (panel.FindName("SpatialModeGroup") as FrameworkElement)?.StartBringIntoView();
            root.Children.Add(reveal);
            root.Children.Add(panel);
            return Task.CompletedTask;
        });
        Button("QaLightingSettings", "接收器与无线 ARGB", () =>
        {
            var preview = DevicesPage.CreateLightingSettingsPreview();
            root.Children.Clear();
            root.Children.Add(preview);
            return Task.CompletedTask;
        });
        _settingAutoApplyQaWindow = new Window { Title = "OpenSynapse Auto Apply QA", Content = new ScrollViewer { Content = root } };
        _settingAutoApplyQaWindow.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1000, 950));
        _settingAutoApplyQaWindow.Activate();
        return true;
    }
}
#endif
