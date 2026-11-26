#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.Views;

// In-memory fixture for the isolated QA window; never opens a device or saves a file.
internal sealed class ViperAutoApplyQa
{
    private readonly RecordingReader _reader = new();
    private ProfileDocument _profile = ProfileDocument.CreateDefault();
    private DeviceDescriptor _device = new("qa-viper-a", "QA Viper", 0x1532, 0x00B8,
        DeviceAccessState.Available, DeviceCapabilityState.PendingValidation, 91, 1, 2,
        DeviceProtocolFamilies.Viper, DeviceCategory.Mouse);
    private Task _operationBarrier = Task.CompletedTask;

    internal ViperWorkspace Workspace { get; }
    internal bool FailNextSave { get; set; }
    internal int WriteCount => _reader.Writes.Count;

    internal ViperAutoApplyQa()
    {
        var settings = _profile.Global.Viper;
        settings.PollingRateHertz = 500;
        settings.DpiX = settings.DpiY = 800;
        settings.IdleSeconds = 300;
        settings.LowBatteryThresholdPercent = 15;
        settings.BatteryChemistry = 0;
        Workspace = new ViperWorkspace(_reader, () => _profile, value => _profile = value,
            _ => { var saved = !FailNextSave; FailNextSave = false; return Task.FromResult(saved); },
            () => [_device], () => PowerSourceKind.Battery, () => null,
            async (_, operation, token, restore, _, _) =>
            {
                try { await _operationBarrier.WaitAsync(token); await operation(); }
                catch (Exception) { restore?.Invoke(); }
            }, _ => { });
        Workspace.SetDevice(_device, "QA", "QA");
        Readback();
    }

    internal void Readback() => Workspace.ApplyTelemetry(new RazerDeviceTelemetry(
        null, null, null, null, null, 80, 500, 800, 800, 300, [], DateTimeOffset.UtcNow,
        ViperDpiStages: new ViperDpiStagesTelemetry(1,
            [new ViperDpiStageTelemetry(1, 800, 800), new ViperDpiStageTelemetry(2, 1600, 1600)]),
        ViperLowBatteryThresholdRaw: ViperLowBatteryThresholdProtocol.ToRaw(15)));

    internal StackPanel CreatePreview()
    {
        var queue = new SettingApplyQueue();
        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(new TextBlock { Text = "Viper · real workspace / fake device", FontSize = 18 });
        var dpi = new StackPanel { Name = "QaViperDpi", Spacing = 8 };
        var fields = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        NumberBox Number(string id, string property)
        {
            var number = new NumberBox { Header = id.EndsWith('X') ? "X" : "Y", Width = 210,
                Minimum = 100, Maximum = 30000, SmallChange = 50,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            AutomationProperties.SetAutomationId(number, id);
            number.SetBinding(NumberBox.ValueProperty, new Binding
                { Source = Workspace, Path = new PropertyPath(property), Mode = BindingMode.TwoWay });
            return number;
        }
        fields.Children.Add(Number("QaViperDpiX", nameof(Workspace.DpiXValue)));
        fields.Children.Add(Number("QaViperDpiY", nameof(Workspace.DpiYValue)));
        dpi.Children.Add(fields);
        root.Children.Add(dpi);
        var polling = new StackPanel { Name = "QaViperPolling", Spacing = 8 };
        var combo = new ComboBox { Header = "Polling", ItemsSource = new[] { "125 Hz", "500 Hz", "1000 Hz" } };
        AutomationProperties.SetAutomationId(combo, "QaViperPollingCombo");
        combo.SetBinding(ComboBox.SelectedIndexProperty, new Binding
            { Source = Workspace, Path = new PropertyPath(nameof(Workspace.PollingRateIndex)), Mode = BindingMode.TwoWay });
        polling.Children.Add(combo);
        root.Children.Add(polling);
        SettingAutoApply.Attach(dpi, queue, () => Workspace.MouseAutoApplyContext,
            () => Workspace.PrepareDpiAutoApply(CancellationToken.None));
        SettingAutoApply.Attach(polling, queue, () => Workspace.MouseAutoApplyContext,
            () => Workspace.PreparePollingAutoApply(CancellationToken.None));
        return root;
    }

    internal static async Task<string> VerifyAsync()
    {
        var qa = new ViperAutoApplyQa();
        var vm = qa.Workspace;
        var token = CancellationToken.None;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Viper auto apply: " + message);
        }
        void NoOp()
        {
            Check(vm.PreparePollingAutoApply(token) is null && vm.PrepareDpiAutoApply(token) is null &&
                vm.PrepareDpiStagesAutoApply(token) is null && vm.PrepareIdleAutoApply(token) is null &&
                vm.PrepareLowBatteryAutoApply(token) is null && vm.PrepareBatteryChemistryAutoApply(token) is null,
                "unchanged readback prepared a write");
        }
        NoOp();
        qa.Readback();
        NoOp();
        Check(qa.WriteCount == 0, "initialization wrote");

        (vm.DpiXValue, vm.DpiYValue) = (1200, 1800);
        var dpi = vm.PrepareDpiAutoApply(token) ?? throw new InvalidOperationException("Missing DPI commit");
        (vm.DpiXValue, vm.DpiYValue) = (2400, 2400);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        qa._operationBarrier = gate.Task;
        var applying = dpi();
        (vm.DpiXValue, vm.DpiYValue) = (3600, 3600);
        Check(qa.WriteCount == 0, "write bypassed operation gate");
        gate.SetResult();
        Check(await applying is null, "captured DPI failed");
        Check(Equals(qa._reader.Writes.Single().Value, (1200, 1800)) &&
            qa._profile.Global.Viper.DpiX == 1200 && qa._profile.Global.Viper.DpiY == 1800,
            "queued DPI read a later draft");
        Check(vm.PrepareDpiAutoApply(token) is null, "DPI readback remains dirty");

        vm.DpiStages[0].X = 950;
        var stages = vm.PrepareDpiStagesAutoApply(token) ?? throw new InvalidOperationException("Missing stage commit");
        vm.DpiStages[0].X = 3000;
        Check(await stages() is null && qa._reader.Writes[^1].Value is ViperDpiStagesTelemetry written &&
            written.Stages[0].X == 950, "DPI stages did not capture row values");

        var previousPolling = qa._profile.Global.Viper.PollingRateHertz;
        vm.PollingRateIndex = 2;
        var retry = vm.PreparePollingAutoApply(token) ?? throw new InvalidOperationException("Missing polling commit");
        var beforeFailure = qa.WriteCount;
        qa.FailNextSave = true;
        Check(!string.IsNullOrEmpty(await retry()) && qa.WriteCount == beforeFailure + 1 &&
            qa._profile.Global.Viper.PollingRateHertz == previousPolling && qa._profile.Global.Viper.DpiX == 950,
            "failed save reported success or lost previous profile value");
        var beforeRetry = qa.WriteCount;
        Check(await retry() is null && qa.WriteCount == beforeRetry + 1 &&
            qa._profile.Global.Viper.PollingRateHertz == 1000, "retry did not save the captured value");

        vm.IdleMinutesValue = 8;
        var stale = vm.PrepareIdleAutoApply(token) ?? throw new InvalidOperationException("Missing idle commit");
        var beforeSwitch = qa.WriteCount;
        qa._device = qa._device with { Id = "qa-viper-b" };
        vm.SetDevice(qa._device, "QA-B", "QA");
        Check(!string.IsNullOrEmpty(await stale()) && qa.WriteCount == beforeSwitch,
            "queued commit retargeted another device");
        Check(qa._reader.Writes.All(write => write.DeviceId == "qa-viper-a"), "unexpected write target");
        return "PASS Viper: no-op/readback, DPI and stage snapshots, failed-save rollback/retry, stale target";
    }

    private sealed class RecordingReader : IRazerDeviceTelemetryReader
    {
        internal List<(string DeviceId, object Value)> Writes { get; } = [];
        private ValueTask<T> Write<T>(IReadOnlyList<DeviceDescriptor> devices, T value) where T : notnull
        {
            if (devices.Count != 1 || !devices[0].Id.StartsWith("qa-viper-", StringComparison.Ordinal))
                throw new InvalidOperationException("QA received a non-fixture device");
            Writes.Add((devices[0].Id, value));
            return ValueTask.FromResult(value);
        }
        public ValueTask<int> SetViperPollingRateAsync(IReadOnlyList<DeviceDescriptor> devices, int hertz, CancellationToken cancellationToken = default) => Write(devices, hertz);
        public ValueTask<(int X, int Y)> SetViperDpiAsync(IReadOnlyList<DeviceDescriptor> devices, int x, int y, CancellationToken cancellationToken = default) => Write(devices, (x, y));
        public ValueTask<ViperDpiStagesTelemetry> SetViperDpiStagesAsync(IReadOnlyList<DeviceDescriptor> devices, ViperDpiStagesTelemetry stages, CancellationToken cancellationToken = default) => Write(devices, stages);
        public ValueTask<int> SetViperIdleSecondsAsync(IReadOnlyList<DeviceDescriptor> devices, int seconds, CancellationToken cancellationToken = default) => Write(devices, seconds);
        public ValueTask<int> SetViperLowBatteryThresholdAsync(IReadOnlyList<DeviceDescriptor> devices, int percent, CancellationToken cancellationToken = default) => Write(devices, percent);
        public ValueTask<byte> SetViperBatteryChemistryAsync(IReadOnlyList<DeviceDescriptor> devices, byte chemistry, CancellationToken cancellationToken = default) => Write(devices, chemistry);
        public ValueTask<RazerDeviceTelemetry> ReadAsync(IReadOnlyList<DeviceDescriptor> devices, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<byte> SetBladeKeyboardBrightnessAsync(IReadOnlyList<DeviceDescriptor> devices, byte brightness, CancellationToken cancellationToken = default, bool verifyReadback = true) => throw new NotSupportedException();
        public ValueTask<byte> ReadBladeKeyboardBrightnessAsync(IReadOnlyList<DeviceDescriptor> devices, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BladeLighting2023Effect> SetBlade2023KeyboardEffectAsync(IReadOnlyList<DeviceDescriptor> devices, BladeLighting2023Effect effect, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BladePerformanceMode> SetBladePerformanceModeAsync(IReadOnlyList<DeviceDescriptor> devices, BladePerformanceMode mode, CancellationToken cancellationToken = default, bool coolingPadConnected = false) => throw new NotSupportedException();
        public ValueTask<BladeCpuBoostMode> SetBladeCpuBoostModeAsync(IReadOnlyList<DeviceDescriptor> devices, BladeCpuBoostMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BladeGpuBoostMode> SetBladeGpuBoostModeAsync(IReadOnlyList<DeviceDescriptor> devices, BladeGpuBoostMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BladeMaxFanMode> SetBladeMaxFanModeAsync(IReadOnlyList<DeviceDescriptor> devices, BladeMaxFanMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> SetBladeChargeLimitAsync(IReadOnlyList<DeviceDescriptor> devices, int percent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BladeLogoMode> SetBladeLogoModeAsync(IReadOnlyList<DeviceDescriptor> devices, BladeLogoMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
#endif
