#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.App.Views;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App;

public partial class App
{
    private Window? _mappingReviewQaWindow;

    private bool TryLaunchMappingReviewQa()
    {
        if (!ushort.TryParse(Environment.GetEnvironmentVariable("OPENSSYNAPSE_MAPPING_REVIEW_QA_PID"),
            System.Globalization.NumberStyles.HexNumber, null, out var productId)) return false;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE")))
            throw new InvalidOperationException("Mapping QA requires an isolated instance.");

        var naga = RazerNagaSideButtons.TryGetMappingButtonIds(productId, out var nagaIds);
        var ids = naga ? nagaIds : RazerKeyboardExtraKeyCatalog.ExtraButtonIds(productId);
        var transport = new MappingReviewQaTransport(naga ? [] : new byte[] { 4, 5 });
        IRazerMappingActionCatalog catalog = naga ? new RazerNagaMappingActionCatalog(productId)
            : new RazerKeyboardMappingActionCatalog(productId);
        var legacyTrigger = naga ? RazerNagaSideButtonResolver.ResolveFirmware(productId, 0x40) : null;
        if (legacyTrigger is not null) legacyTrigger.InstanceId = "mapping-review-qa";
        var legacyMacro = new MacroDefinition { Id = "legacy-naga", Name = "Legacy side macro" };
        if (legacyTrigger is not null)
            legacyMacro.Trigger = new MacroTrigger { Input = legacyTrigger, Mode = MacroTriggerMode.Press };
        var failSave = false;
        var mapping = new RazerHidMappingViewModel(transport, canWrite: true,
            localMacros: () => naga ? [legacyMacro] : [],
            productId: productId, deviceKind: naga ? RazerMappingDeviceKind.Mouse : RazerMappingDeviceKind.Keyboard,
            softwareTriggerResolver: naga ? id => RazerMouseMacroTriggerResolver.Resolve(productId, id, isNaga: true) : null,
            softwareTriggerBinder: (_, _, _, _, _, _, _, _) => throw new InvalidOperationException("Unsafe Naga macro reached the binder."),
            actionCatalog: catalog, mappingProfileSaver: (_, _) => failSave
                ? Task.FromException(new IOException("Simulated profile save failure.")) : Task.CompletedTask)
        { DeviceInstanceId = "mapping-review-qa" };
        var status = new TextBlock { Text = "READY", FontSize = 16 };
        AutomationProperties.SetAutomationId(status, "QaMappingResult");
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var root = new StackPanel { Padding = new Thickness(24), Spacing = 12,
            Background = (Brush)Resources["CanvasBrush"] };
        root.Children.Add(toolbar);
        root.Children.Add(status);
        root.Children.Add(new RazerMappingView { DataContext = mapping });
        var scroll = new ScrollViewer { Content = root };

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
        async Task ReadLayer(int layer)
        {
            mapping.LayerIndex = layer;
            await mapping.ReadAsync();
            for (var attempt = 0; mapping.IsBusy && attempt < 100; attempt++) await Task.Delay(50);
            if (!mapping.IsLoaded || mapping.IsBusy || mapping.ErrorText.Length > 0)
                throw new InvalidOperationException("Mapping did not load: " + mapping.ErrorText);
        }
        Add("QaMappingVerify", "Verify mappings", async () =>
        {
            if (naga)
            {
                await ReadLayer(0);
                var legacy = mapping.Rows.Single(row => row.ButtonId == 0x40);
                if (legacy.GroupIndex != (int)RazerHidMappingGroup.Keep || legacy.SoftwareMacroId != legacyMacro.Id)
                    throw new InvalidOperationException("Legacy software binding changed execution target.");
                var unrelated = mapping.Rows.Single(row => row.ButtonId == 1);
                unrelated.GroupIndex = (int)RazerHidMappingGroup.KeyboardKey;
                unrelated.OptionIndex = catalog.Decode(RazerHidMappingFunctions.KeyCode, [0, 7])!.OptionIndex;
                var legacyBefore = await transport.GetAssignmentAsync(0x40, 0);
                await mapping.ApplyAsync();
                if (!(await transport.GetAssignmentAsync(0x40, 0)).Data.SequenceEqual(legacyBefore.Data))
                    throw new InvalidOperationException("Saving another key changed the legacy macro.");
                var beforeWrites = transport.Writes;
                var error = await mapping.BindSoftwareMacroAsync(legacyMacro.Id, legacyTrigger!,
                    MacroTriggerMode.Press, MacroPlaybackMode.Once, 1, false, null, CancellationToken.None);
                if (error is null || transport.Writes != beforeWrites)
                    throw new InvalidOperationException("Unavailable side-key macro wrote a device assignment.");
                legacyMacro.Trigger = null;
                await mapping.ReadAsync();
            }
            for (byte mode = 0; mode <= 1; mode++)
            {
                await ReadLayer(mode);
                foreach (var id in ids)
                {
                    var row = mapping.Rows.Single(row => row.ButtonId == id);
                    if (!row.CanApply) throw new InvalidOperationException($"Unreadable button {id}.");
                    if (naga && RazerNagaSideButtonResolver.ResolveFirmware(productId, id) is not null &&
                        (row.CanUseSoftwareMacro || row.GroupNames.Contains(AppStrings.Text("RazerMappingGroupWindowsShortcut"))))
                        throw new InvalidOperationException($"Unavailable software action offered on side key {id}.");
                    if (!naga && RazerKeyboardExtraKeyCatalog.DescribeResourceKey(productId, id) is { } name &&
                        row.ButtonName != AppStrings.Text(name))
                        throw new InvalidOperationException($"Wrong media name for {id}: {row.ButtonName}");
                    row.GroupIndex = (int)RazerHidMappingGroup.KeyboardKey;
                    row.OptionIndex = catalog.Decode(RazerHidMappingFunctions.KeyCode, [0, (byte)(4 + mode + id % 12)])!.OptionIndex;
                }
                await mapping.ApplyAsync();
                if (mapping.ErrorText.Length > 0) throw new InvalidOperationException(mapping.ErrorText);
                foreach (var id in ids)
                    if (!(await transport.GetAssignmentAsync(id, mode)).Data.SequenceEqual(new byte[] { 0, (byte)(4 + mode + id % 12) }))
                        throw new InvalidOperationException($"Wrong saved key/layer {id}/{mode}.");
            }
            await ReadLayer(0);
            var target = mapping.Rows.Single(row => row.ButtonId == ids[0]);
            var before = await transport.GetAssignmentAsync(target.ButtonId, 0);
            target.OptionIndex = catalog.Decode(RazerHidMappingFunctions.KeyCode, [0, 0x1D])!.OptionIndex;
            failSave = true;
            await mapping.ApplyAsync();
            failSave = false;
            var after = await transport.GetAssignmentAsync(target.ButtonId, 0);
            if (!after.Data.SequenceEqual(before.Data)) throw new InvalidOperationException("Rollback changed the assignment.");
            await ReadLayer(0);
            status.Text = $"PASS {ids.Count} inputs; both layers and rollback";
        });
        Add("QaMappingDetails", "Show controls", () =>
        {
            scroll.ChangeView(null, naga ? 360 : 320, null, disableAnimation: true);
            return Task.CompletedTask;
        });
        Add("QaMappingUnreadable", "Unreadable input", async () =>
        {
            transport.Unreadable = ids[0];
            await mapping.ReadAsync();
            var row = mapping.Rows.Single(row => row.ButtonId == ids[0]);
            status.Text = !row.CanApply && mapping.ReadErrorText.Length > 0
                ? "PASS unreadable input blocks editing" : "FAIL unreadable input is editable";
        });
        _mappingReviewQaWindow = new Window { Title = "Mapping review QA", Content = scroll };
        _mappingReviewQaWindow.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1320, 1020));
        _mappingReviewQaWindow.Activate();
        return true;
    }

    private sealed class MappingReviewQaTransport(IReadOnlyList<byte> reportedIds) : IRazerMappingTransport
    {
        private readonly Dictionary<(byte, byte), RazerMappingAssignment> _assignments = [];
        public int Writes { get; private set; }
        public byte? Unreadable { get; set; }
        public Task<IReadOnlyList<byte>> GetButtonIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(reportedIds);
        public Task<RazerMappingAssignment> GetAssignmentAsync(byte buttonId, byte mode,
            CancellationToken cancellationToken = default) => buttonId == Unreadable
                ? throw new NotSupportedException("Simulated unavailable key.")
                : Task.FromResult(_assignments.GetValueOrDefault((buttonId, mode)) ?? new(RazerHidMappingFunctions.KeyCode, [0, 4]));
        public Task SetAssignmentAsync(byte buttonId, byte mode, byte functionId, IReadOnlyList<byte> data,
            CancellationToken cancellationToken = default)
        {
            Writes++;
            _assignments[(buttonId, mode)] = new(functionId, data.ToArray());
            return Task.CompletedTask;
        }
    }
}
#endif
