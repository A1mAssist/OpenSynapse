#if DEBUG
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.App.Views.DevicePanels;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App;

public partial class App
{
    private Window? _surfaceCalibrationQaWindow;

    // Fake clients only; this returns before production discovery, input hooks or transports are created.
    private bool TryLaunchSurfaceCalibrationQa()
    {
        var output = Environment.GetEnvironmentVariable("OPENSSYNAPSE_SURFACE_CALIBRATION_QA_OUT");
        if (string.IsNullOrWhiteSpace(output)) return false;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE")))
            throw new InvalidOperationException("Surface calibration QA requires an isolated data instance.");
        var harness = new SurfaceCalibrationQaHarness(Path.GetFullPath(output), Resources);
        _surfaceCalibrationQaWindow = new Window { Title = "OpenSynapse Surface Calibration QA", Content = harness.Root };
        _surfaceCalibrationQaWindow.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1120, 1120));
        harness.Root.Loaded += async (_, _) => await harness.InitializeAsync();
        _surfaceCalibrationQaWindow.Closed += async (_, _) => await harness.DisposeAsync();
        _surfaceCalibrationQaWindow.Activate();
        return true;
    }

    private sealed class SurfaceCalibrationQaHarness : IAsyncDisposable
    {
        private readonly string _output;
        private readonly string _data;
        private readonly SurfaceCalibrationPanel _panel = new();
        private readonly TextBlock _status = new() { Text = "STARTING", TextWrapping = TextWrapping.Wrap };
        private readonly StackPanel _toolbar = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        private readonly List<string> _checks = [];
        private readonly List<string> _uiChecks = [];
        private readonly Dictionary<ushort, SurfaceCalibrationQaDevice> _devices = [];
        private SurfaceCalibrationQaDevice? _selected;
        private ushort _smartPid;
        private bool _initialized;
        public Grid Root { get; } = new() { Padding = new Thickness(24), RowSpacing = 16 };

        public SurfaceCalibrationQaHarness(string output, ResourceDictionary resources)
        {
            _output = output;
            _data = Path.Combine(Path.GetDirectoryName(output)!, "isolated-calibration");
            Directory.CreateDirectory(_data);
            Root.Background = (Brush)resources["CanvasBrush"];
            Root.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            var header = new StackPanel { Spacing = 10 };
            header.Children.Add(new TextBlock { Text = "Surface calibration · isolated QA", FontSize = 24 });
            AutomationProperties.SetAutomationId(_status, "SurfaceCalibrationQaStatus");
            header.Children.Add(_status);
            header.Children.Add(_toolbar);
            AddButton("Viper 0078", "Qa0078", () => ShowAsync(0x0078));
            AddButton("Viper 00B8", "Qa00B8", () => ShowAsync(0x00B8));
            AddButton("Lift-only", "Qa3349", () => ShowAsync(0x00A3));
            AddButton("Smart tracking", "QaSmart", () => ShowAsync(_smartPid));
            AddButton("No device", "QaNull", async () =>
            {
                if (_selected is { } selected) await selected.Model.CancelAsync();
                _selected = null;
                _panel.DataContext = null;
                Check(_panel.Visibility == Visibility.Collapsed, "Null DataContext collapses the whole card", _uiChecks);
                await WriteReportAsync();
            });
            AddButton("Verify UI run", "QaVerify", VerifyUiAsync);
            Root.Children.Add(header);
            var scroll = new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 1);
            Root.Children.Add(scroll);
        }

        private void AddButton(string name, string id, Func<Task> action)
        {
            var button = new Button { Content = name, IsEnabled = false };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += async (_, _) =>
            {
                try { await action(); }
                catch (Exception error) { await FailAsync(error); }
            };
            _toolbar.Children.Add(button);
        }

        public async Task InitializeAsync()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                _smartPid = Enumerable.Range(1, 255).Select(value => (ushort)value).First(pid =>
                    RazerProximitySensorProfiles.TryGet(pid, out var profile) && profile.Kind == RazerProximityCalibrationKind.SmartPixart &&
                    profile.Calibration?.Capabilities.HasFlag(RazerCalibrationCapabilities.Asymmetric) == true);
                await CheckModelsAsync();
                foreach (var pid in new ushort[] { 0x0078, 0x00B8, 0x00A3, _smartPid })
                    _devices.Add(pid, CreateDevice(pid, $"ui-{pid:X4}"));
                await ShowAsync(0x0078);
                foreach (var button in _toolbar.Children.OfType<Control>()) button.IsEnabled = true;
                _status.Text = $"READY · {_checks.Count} behavior checks passed";
                await WriteReportAsync();
            }
            catch (Exception error) { await FailAsync(error); }
        }

        private SurfaceCalibrationQaDevice CreateDevice(ushort pid, string name)
        {
            if (!RazerProximitySensorProfiles.TryGet(pid, out var profile) || profile.Calibration is null)
                throw new InvalidOperationException($"The real product calibration table is missing for {pid:X4}.");
            var path = Path.Combine(_data, name + ".json");
            var identity = new RazerSurfaceCalibrationIdentity(name, name, pid, "qa-control:" + name);
            var input = new SurfaceCalibrationQaInput(name);
            var client = new SurfaceCalibrationQaClient(profile, input);
            var store = new RazerSurfaceCalibrationStore(path);
            return new(new SurfaceCalibrationViewModel(client, identity, input: input, store: store), client, input, store, identity, path);
        }

        private async Task CheckModelsAsync()
        {
            await using var pixart = CreateDevice(0x0078, "checks-pixart");
            var vm = pixart.Model;
            Check(vm.SelectedMode is null && double.IsNaN(vm.Lod), "Before first read no default is presented as current");
            await vm.EnsureLoadedAsync();
            await vm.EnsureLoadedAsync();
            Check(pixart.Client.Reads == 1 && pixart.Client.Applies == 0, "EnsureLoaded reads once and performs zero writes");
            await using (var other = CreateDevice(0x0078, "checks-other-pixart"))
            {
                await other.Model.EnsureLoadedAsync();
                using (var restoration = await RazerSurfaceCalibrationOperations.AcquireAsync(pixart.Identity))
                {
                    Check(!vm.CanRead && !vm.CanEdit && !vm.CanPrepare && other.Model.CanEdit,
                        "A reconnect workflow disables only its exact mouse, not another same-PID mouse");
                    await vm.ApplyAsync();
                    Check(pixart.Client.Applies == 0, "UI Apply cannot interleave a reconnect restoration lease");
                }
                Check(vm.CanEdit, "Releasing the reconnect workflow restores editing availability");
            }
            Check(vm.Presets.Count == 24 && vm.LodMaximum == 10 && vm.CanPrepare, "0078 exposes its 24 presets, ten LOD levels and manual calibration");
            SelectPreset(vm);
            await vm.ApplyAsync();
            Check(pixart.Client.Applies == 1 && !vm.HasPendingSave, "Preset apply succeeds and saves");
            Check(pixart.Store.Load(pixart.Identity)?.AppliedState is not null, "Successful readback is retained in the saved record");
            var saved = pixart.Store.Load(pixart.Identity)!;
            vm.Lod = vm.Lod == vm.LodMinimum ? vm.LodMaximum : vm.LodMinimum;
            pixart.Client.NextApply = SurfaceCalibrationQaFault.Mismatch;
            await vm.ApplyAsync();
            Check(pixart.Client.Restores == 1 && pixart.Store.Load(pixart.Identity)!.SavedAt == saved.SavedAt,
                "Mismatched readback restores the original state and preserves the saved record");
            pixart.Client.NextApply = SurfaceCalibrationQaFault.PartialWrite;
            await vm.ApplyAsync();
            Check(pixart.Client.Restores == 2, "A partially failed write restores the original state");
            pixart.Client.NextApply = SurfaceCalibrationQaFault.Unavailable;
            await vm.ApplyAsync();
            Check(vm.StatusText == AppStrings.Text("SurfaceCalibrationReadbackUnavailable") && !vm.HasPendingSave,
                "Unavailable post-write readback is saved and reported separately");
            using (var locked = new FileStream(pixart.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                await vm.ApplyAsync();
                Check(vm.HasPendingSave && vm.CanRetrySave && !vm.CanApply, "Disk failure keeps an applied-but-unsaved candidate");
            }
            var readsBeforeRestore = pixart.Client.Reads;
            var writesBeforeRestore = pixart.Client.Applies;
            var backgroundRestore = await pixart.Store.RestoreAsync(pixart.Identity, pixart.Client);
            Check(backgroundRestore is null && pixart.Client.Reads == readsBeforeRestore &&
                pixart.Client.Applies == writesBeforeRestore,
                "A pending save makes background restoration skip without reading or overwriting the candidate");
            var applies = pixart.Client.Applies;
            await vm.RetrySaveAsync();
            Check(!vm.HasPendingSave && pixart.Client.Applies == applies && !RazerSurfaceCalibrationOperations.IsBusy(pixart.Identity),
                "Retry saving performs no device write and releases retained ownership");

            await vm.PrepareAsync();
            Check(vm.CanStart && pixart.Input.Active == 1, "Manual preparation subscribes the exact fake input once");
            await vm.StartAsync();
            Check(vm.CanFinish, "Firmware start acknowledgement reaches sampling");
            pixart.Input.Emit(RazerSurfaceCalibrationEventKind.TooFast);
            await WaitAsync(() => vm.SpeedText == AppStrings.Text("SurfaceCalibrationTooFast"));
            await Task.WhenAll(vm.FinishAsync(), vm.FinishAsync());
            Check(pixart.Client.Stops == 1 && pixart.Client.Results == 1 && pixart.Input.Active == 0,
                "Duplicate finish stops and reads once and releases the input subscription");
            var raw = pixart.Store.Load(pixart.Identity)!.Configuration.RawData!.Parameters.ToArray();
            Check(raw.SequenceEqual(SurfaceCalibrationQaClient.RawParameters) && vm.SavedSurfaces.Count == 1,
                "Manual completion saves the named surface and original raw data");
            vm.Lod = vm.Lod == vm.LodMaximum ? vm.LodMinimum : vm.LodMaximum;
            await vm.ApplyAsync();
            Check(pixart.Store.Load(pixart.Identity)!.Configuration.RawData!.Parameters.SequenceEqual(raw),
                "Changing LOD preserves the original raw sampling data");
            saved = pixart.Store.Load(pixart.Identity)!;
            await vm.PrepareAsync();
            await vm.StartAsync();
            await vm.CancelAsync();
            Check(!vm.IsSessionActive && pixart.Input.Active == 0 && pixart.Store.Load(pixart.Identity)!.SavedAt == saved.SavedAt,
                "Cancel cleans up active sampling without replacing the saved surface");
            await vm.PrepareAsync();
            pixart.Input.Emit(RazerSurfaceCalibrationEventKind.Escape);
            await WaitAsync(() => !vm.IsSessionActive);
            Check(pixart.Input.Active == 0, "Escape releases the input subscription");

            await using var viper = CreateDevice(0x00B8, "checks-viper");
            await viper.Model.EnsureLoadedAsync();
            Check(viper.Model.Modes.Any(mode => mode.Mode == RazerSurfaceCalibrationMode.Adaptive) && !viper.Model.CanPrepare,
                "00B8 dedicated-client capability shape exposes adaptive tracking without manual sampling");
            await using var lift = CreateDevice(0x00A3, "checks-3349");
            await lift.Model.EnsureLoadedAsync();
            lift.Model.SelectedMode = lift.Model.Modes.Single(mode => mode.Mode == RazerSurfaceCalibrationMode.Preset);
            Check(lift.Model.LiftVisibility == Visibility.Visible && lift.Model.PresetVisibility == Visibility.Collapsed &&
                lift.Model.LodVisibility == Visibility.Collapsed && lift.Model.CanApply,
                "3349 exposes one effective lift-off control without duplicate LOD or preset controls");
            await lift.Model.ApplyAsync();
            Check(lift.Client.Applies == 1, "3349 enabled configuration applies");
            foreach (var pid in new ushort[] { 0x00D3, 0x00D4 })
            {
                await using var unknownRange = CreateDevice(pid, $"checks-range-{pid:X4}");
                await unknownRange.Model.EnsureLoadedAsync();
                Check(unknownRange.Model.Modes.All(option => option.Mode != RazerSurfaceCalibrationMode.Preset) &&
                    unknownRange.Model.LiftVisibility == Visibility.Collapsed && unknownRange.Model.LodVisibility == Visibility.Collapsed &&
                    unknownRange.Model.AdjustmentHintVisibility == Visibility.Visible && unknownRange.Model.CanRead &&
                    unknownRange.Model.Modes.Any(option => option.Mode == RazerSurfaceCalibrationMode.Disabled),
                    $"{pid:X4} keeps read/disable but exposes no invented enabled mode or zero-range input");
            }
            await using var smart = CreateDevice(_smartPid, "checks-smart");
            await smart.Model.EnsureLoadedAsync();
            smart.Model.SelectedMode = smart.Model.Modes.Single(mode => mode.Mode == RazerSurfaceCalibrationMode.Adaptive);
            smart.Model.IsAsymmetric = true;
            Check(smart.Model.LiftMaximum == smart.Client.Profile.Calibration!.LiftOffDistance!.Maximum,
                "Smart adaptive mode uses the product adaptive range");
            smart.Model.IsAsymmetric = false;
            smart.Client.UnknownOriginalMode = true;
            await smart.Model.ApplyAsync();
            Check(smart.Client.Applies == 0 && smart.Client.Restores == 0 &&
                smart.Model.StatusText == AppStrings.Text("SurfaceCalibrationRecoveryUnavailable"),
                "Unknown original SmartPixart mode prevents every device write");
            smart.Client.UnknownOriginalMode = false;
            smart.Model.IsAsymmetric = true;
            smart.Model.SelectedMode = smart.Model.Modes.Single(mode => mode.Mode == RazerSurfaceCalibrationMode.Manual);
            Check(smart.Model.LiftMaximum == smart.Client.Profile.Calibration!.SurfaceLiftOffDistance!.Maximum,
                "Smart surface mode switches to the product surface range");
            _checks.AddRange(await CheckCalibrationLifecycleAsync(_data));
        }

        private static void SelectPreset(SurfaceCalibrationViewModel model)
        {
            model.SelectedMode = model.Modes.Single(mode => mode.Mode == RazerSurfaceCalibrationMode.Preset);
            model.SelectedPreset = model.Presets.First();
        }

        private async Task ShowAsync(ushort pid)
        {
            if (_selected is { } previous) await previous.Model.CancelAsync();
            _selected = _devices[pid];
            _panel.DataContext = _selected.Model;
            await _selected.Model.EnsureLoadedAsync();
        }

        private async Task VerifyUiAsync()
        {
            var pixart = _devices[0x0078];
            Check(pixart.Client.Reads >= 2, "UI Read invokes the shared VM", _uiChecks);
            Check(pixart.Client.Applies >= 2 && pixart.Model.SavedSurfaces.Count != 0,
                "UI preset and manual actions apply and save a real record", _uiChecks);
            Check(pixart.Client.Starts >= 2 && pixart.Client.Results == 1 && pixart.Input.Active == 0,
                "UI manual finish and cancel release their subscriptions", _uiChecks);
            Check(_uiChecks.Contains("Null DataContext collapses the whole card"), "UI empty-device state was exercised", _uiChecks);
            _status.Text = $"VERIFIED · {_checks.Count} behavior checks · {_uiChecks.Count} UI checks";
            await WriteReportAsync();
        }

        private void Check(bool condition, string label, List<string>? list = null)
        {
            if (!condition) throw new InvalidOperationException(label);
            (list ?? _checks).Add(label);
        }

        private static async Task WaitAsync(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!condition()) await Task.Delay(20, timeout.Token);
        }

        private Task WriteReportAsync(Exception? error = null) => File.WriteAllTextAsync(_output,
            JsonSerializer.Serialize(new { status = error is null ? "passed" : "failed", checks = _checks, uiChecks = _uiChecks,
                smartProductId = _smartPid.ToString("X4"), error = error?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));

        private async Task FailAsync(Exception error)
        {
            _status.Text = "FAILED · " + error.Message;
            await WriteReportAsync(error);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var device in _devices.Values) await device.DisposeAsync();
        }
    }

    private sealed record SurfaceCalibrationQaDevice(SurfaceCalibrationViewModel Model, SurfaceCalibrationQaClient Client,
        SurfaceCalibrationQaInput Input, RazerSurfaceCalibrationStore Store, RazerSurfaceCalibrationIdentity Identity, string Path) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Model.DisposeAsync();
    }

    private enum SurfaceCalibrationQaFault { None, Mismatch, Unavailable, PartialWrite }

    private sealed class SurfaceCalibrationQaClient(RazerProximitySensorProfile profile, SurfaceCalibrationQaInput input) : IRazerSurfaceCalibrationClient
    {
        internal static readonly byte[] RawParameters = [10, 16, 131, 0, 0, 0, 0, 20, 89];
        private RazerSurfaceCalibrationState _state = new()
        {
            SensorState = 0,
            Configuration = RazerSurfaceCalibrationAlgorithms.CreateDefaultConfiguration(profile) ?? new(),
        };
        private bool _failRead;
        public RazerProximitySensorProfile Profile => profile;
        public int Reads { get; private set; }
        public int Applies { get; private set; }
        public int Restores { get; private set; }
        public int Aborts { get; private set; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public int Results { get; private set; }
        public SurfaceCalibrationQaFault NextApply { get; set; }
        public bool UnknownOriginalMode { get; set; }
        public Task<RazerSurfaceCalibrationState> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            if (_failRead) { _failRead = false; throw new IOException("Simulated unavailable readback"); }
            return Task.FromResult(UnknownOriginalMode ? _state with { LiftMode = null } :
                profile.Kind == RazerProximityCalibrationKind.SmartPixart ? _state with { LiftMode = 1 } : _state);
        }
        public Task ApplyAsync(RazerSurfaceCalibrationConfiguration configuration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Applies++;
            _state = new() { Configuration = configuration, SensorState = 1, Parameters = configuration.RawData?.Parameters ?? [] };
            var fault = NextApply;
            NextApply = SurfaceCalibrationQaFault.None;
            if (fault == SurfaceCalibrationQaFault.PartialWrite) throw new IOException("Simulated partial write");
            if (fault == SurfaceCalibrationQaFault.Mismatch) _state = _state with { Configuration = configuration with { Lod = configuration.Lod + 1 } };
            _failRead = fault == SurfaceCalibrationQaFault.Unavailable;
            return Task.CompletedTask;
        }
        public Task PrepareAsync(RazerSurfaceCalibrationConfiguration candidate, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartAsync(RazerSurfaceCalibrationMode mode, CancellationToken cancellationToken = default)
        { Starts++; input.Emit(RazerSurfaceCalibrationEventKind.Started); return Task.CompletedTask; }
        public Task StopAsync(RazerSurfaceCalibrationMode mode, CancellationToken cancellationToken = default)
        { Stops++; input.Emit(RazerSurfaceCalibrationEventKind.Ended); return Task.CompletedTask; }
        public Task<RazerSurfaceCalibrationRawData> ReadResultAsync(RazerSurfaceCalibrationMode mode, CancellationToken cancellationToken = default)
        { Results++; return Task.FromResult(new RazerSurfaceCalibrationRawData { Parameters = RawParameters.ToArray() }); }
        public Task RestoreAsync(RazerSurfaceCalibrationState snapshot, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Restores++; _state = snapshot; return Task.CompletedTask; }
        public Task AbortAsync(RazerSurfaceCalibrationState snapshot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Aborts++;
            _state = _state with { SensorState = snapshot.SensorState, LiftMode = snapshot.LiftMode, LiftHeight = snapshot.LiftHeight };
            return Task.CompletedTask;
        }
        public Task RestoreDeviceModeAsync(RazerSurfaceCalibrationState snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public RazerSurfaceCalibrationConfiguration CreateCandidate(RazerSurfaceCalibrationConfiguration configuration, RazerSurfaceCalibrationRawData raw) => configuration with { RawData = raw };
        public RazerSurfaceCalibrationReadback CompareReadback(RazerSurfaceCalibrationConfiguration configuration, RazerSurfaceCalibrationState state) =>
            configuration with { RawData = null } == state.Configuration with { RawData = null }
                ? RazerSurfaceCalibrationReadback.Consistent : RazerSurfaceCalibrationReadback.Mismatch;
    }

    private sealed class SurfaceCalibrationQaInput(string instanceId) : IRazerSurfaceCalibrationInputSource
    {
        private Action<RazerSurfaceCalibrationInput>? _handler;
        private Guid _session;
        public int Active { get; private set; }
        public Task<IDisposable> SubscribeAsync(Guid sessionId, bool philips, Action<RazerSurfaceCalibrationInput> handler,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _session = sessionId;
            _handler = handler;
            Active++;
            return Task.FromResult<IDisposable>(new SurfaceCalibrationQaSubscription(() => { _handler = null; Active--; }));
        }
        public void Emit(RazerSurfaceCalibrationEventKind kind) => _handler?.Invoke(new(instanceId, _session, kind));
    }

    private sealed class SurfaceCalibrationQaSubscription(Action dispose) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) dispose(); }
    }
}
#endif
