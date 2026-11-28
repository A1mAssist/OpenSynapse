#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.App.Views;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App;

public partial class App
{
    private Window? _keypadMappingQaWindow;

    private bool TryLaunchKeypadMappingQa()
    {
        if (Environment.GetEnvironmentVariable("OPENSSYNAPSE_KEYPAD_QA") != "1") return false;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE")))
            throw new InvalidOperationException("Keypad QA requires an isolated instance.");

        var productId = ushort.TryParse(Environment.GetEnvironmentVariable("OPENSSYNAPSE_KEYPAD_QA_PID"),
            System.Globalization.NumberStyles.HexNumber, null, out var parsed) ? parsed : (ushort)0x0244;
        var transport = new KeypadQaTransport(productId);
        var host = RazerKeypadInputCatalog.UsesHostMapping(productId);
        IRazerMappingActionCatalog catalog = host ? new RazerTartarusProMappingActionCatalog(productId)
            : new RazerKeypadMappingActionCatalog(productId);
        var failSave = false;
        var mapping = new RazerHidMappingViewModel(transport, canWrite: true,
            productId: productId, deviceKind: RazerMappingDeviceKind.Keyboard,
            actionCatalog: catalog,
            mappingProfileSaver: (_, _) => failSave
                ? Task.FromException(new IOException("Simulated profile save failure."))
                : Task.CompletedTask)
        { DeviceInstanceId = "keypad-qa" };
        var root = new StackPanel { Padding = new Thickness(24), Spacing = 12,
            Background = (Brush)Resources["CanvasBrush"] };
        var status = new TextBlock { Text = "READY", FontSize = 16 };
        AutomationProperties.SetAutomationId(status, "QaKeypadResult");
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        root.Children.Add(toolbar);
        root.Children.Add(status);
        root.Children.Add(new RazerMappingView { DataContext = mapping });

        void Add(string id, string label, Func<Task> action)
        {
            var button = new Button { Content = label };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += async (_, _) =>
            {
                try { await action(); }
                catch (Exception exception) { status.Text = "FAIL " + exception.Message; }
            };
            toolbar.Children.Add(button);
        }
        Add("QaKeypadVerify", "Read / save / rollback", async () =>
        {
            await mapping.ReadAsync();
            var expectedCount = host ? RazerKeypadInputCatalog.GetButtonIds(productId).Count : 27;
            if (!mapping.IsLoaded || mapping.Rows.Count != expectedCount)
                throw new InvalidOperationException($"Expected all {expectedCount} keypad inputs.");
            var buttonId = host ? RazerKeypadInputCatalog.GetButtonIds(productId)
                .First(id => RazerKeypadInputCatalog.GetDefaultAssignment(id, productId: productId).FunctionId == RazerHidMappingFunctions.KeyCode)
                : (byte)1;
            var row = mapping.Rows.Single(item => item.ButtonId == buttonId);
            row.GroupIndex = (int)RazerHidMappingGroup.KeyboardKey;
            row.OptionIndex = catalog.Decode(
                RazerHidMappingFunctions.KeyCode, [0, 0x04])!.OptionIndex;
            await mapping.ApplyAsync();
            if (!(await transport.GetAssignmentAsync(buttonId, 0)).Data.SequenceEqual(new byte[] { 0, 0x04 }))
                throw new InvalidOperationException("Save did not reach the runtime.");
            failSave = true;
            row.OptionIndex = catalog.Decode(
                RazerHidMappingFunctions.KeyCode, [0, 0x05])!.OptionIndex;
            await mapping.ApplyAsync();
            failSave = false;
            if (!(await transport.GetAssignmentAsync(buttonId, 0)).Data.SequenceEqual(new byte[] { 0, 0x04 }))
                throw new InvalidOperationException("Failed save did not restore the previous mapping.");
            await mapping.ReadAsync();
            status.Text = $"PASS {expectedCount} inputs; save and rollback";
        });
        Add("QaKeypadReadFailure", "Input unavailable", async () =>
        {
            transport.FailRead = true;
            await mapping.ReadAsync();
            status.Text = !mapping.IsLoaded && !mapping.CanApply && mapping.ReadErrorText.Length > 0
                ? "PASS unavailable input blocks apply" : "FAIL missing readiness error";
        });
        Add("QaKeypadReload", "Reconnect", async () =>
        {
            transport.FailRead = false;
            await mapping.ReadAsync();
            status.Text = mapping.IsLoaded ? "PASS reconnected" : "FAIL reconnect";
        });
        if (host) Add("QaKeypadMacros", "Macro transactions", async () =>
        {
            await VerifyKeypadMacroTransactionsAsync(productId);
            status.Text = "PASS macro bind, replace, clear, delete and rollback";
        });

        var scroll = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Add("QaKeypadDetails", "Direction controls", () =>
        {
            scroll.ChangeView(null, 440, null, disableAnimation: true);
            return Task.CompletedTask;
        });

        _keypadMappingQaWindow = new Window { Title = "Keypad mapping QA",
            Content = scroll };
        _keypadMappingQaWindow.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1240, 940));
        _keypadMappingQaWindow.Closed += async (_, _) =>
        {
            if (transport.Runtime is { } runtime) await runtime.DisposeAsync();
        };
        _keypadMappingQaWindow.Activate();
        return true;
    }

    private sealed class KeypadQaTransport(ushort productId) : IRazerMappingTransport
    {
        public RazerKeypadMappingRuntime? Runtime { get; } = RazerKeypadInputCatalog.UsesHostMapping(productId)
            ? new("keypad-qa", new KeypadQaSink(), productId: productId) : null;
        private readonly Dictionary<(byte Button, byte Mode), RazerMappingAssignment> _assignments = [];
        public bool FailRead { get; set; }
        public Task<IReadOnlyList<byte>> GetButtonIdsAsync(CancellationToken cancellationToken = default) =>
            FailRead ? throw new RazerKeypadMappingUnavailableException(
                RazerKeypadMappingUnavailableReason.DriverMissing, "Keypad input driver is unavailable.")
                : Task.FromResult(Runtime is not null ? RazerKeypadInputCatalog.GetButtonIds(productId)
                    : OpenSynapse.Core.Devices.RazerKeypadKeyCatalog.TartarusV2ProMappingIds);
        public Task<RazerMappingAssignment> GetAssignmentAsync(byte buttonId, byte mode,
            CancellationToken cancellationToken = default)
        {
            if (Runtime is { } runtime)
            {
                var assignment = runtime.GetAssignment(buttonId, mode);
                return Task.FromResult(new RazerMappingAssignment(assignment.FunctionId, assignment.Data));
            }
            return Task.FromResult(_assignments.GetValueOrDefault((buttonId, mode)) ??
                new RazerMappingAssignment(RazerHidMappingFunctions.KeyCode, [0, 4]));
        }
        public Task SetAssignmentAsync(byte buttonId, byte mode, byte functionId, IReadOnlyList<byte> data,
            CancellationToken cancellationToken = default)
        {
            if (Runtime is { } runtime)
                return runtime.SetAssignmentAsync(new(buttonId, mode, functionId, data), cancellationToken);
            _assignments[(buttonId, mode)] = new(functionId, data.ToArray());
            return Task.CompletedTask;
        }
    }

    private sealed class KeypadQaSink : IMacroInputSink
    {
        public Task SendAsync(MacroStep step, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendMouseMoveAsync(MacroMouseMoveMode mode, int x, int y,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
#endif
