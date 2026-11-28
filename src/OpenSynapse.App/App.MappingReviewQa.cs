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
                await VerifyLegacyNagaBindingsAsync();
                await VerifySupportedMouseBindingsAsync();
                legacyMacro.Trigger = null;
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
            status.Text = $"PASS {ids.Count} inputs; both layers and rollback" +
                (naga ? "; legacy bindings, shared claims, final clear and supported host remaps" : string.Empty);
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

    private static async Task VerifyLegacyNagaBindingsAsync()
    {
        foreach (var productId in new ushort[] { 0x008F, 0x0090 })
        foreach (var legacy in new[]
        {
            new MacroDefinition { Id = "legacy-naga", Name = "Legacy side macro" },
            new NativeMouseButtonAction(4).CreateMacro(),
            SoftwareShortcutCatalog.Actions[0].CreateMacro(),
        })
        {
            MacroInput Input(byte id)
            {
                var input = RazerMouseMacroTriggerResolver.Resolve(productId, id, isNaga: true)!;
                input.InstanceId = "mapping-review-qa";
                return input;
            }
            var input = Input(0x40);
            legacy.Trigger = new MacroTrigger { Input = input, Mode = MacroTriggerMode.Press };
            var otherLayer = new MacroTrigger { Input = input.Clone(), HyperShiftLayer = true };
            legacy.AdditionalTriggers = [otherLayer];
            var system = legacy.Id != "legacy-naga";
            var clears = 0;
            var transport = new MappingReviewQaTransport([1, 0x40, 0x50]);
            await transport.SetAssignmentAsync(0x40, 0, RazerHidMappingFunctions.Off, []);
            await transport.SetAssignmentAsync(0x50, 0, RazerHidMappingFunctions.Off, []);
            transport.WrittenButtons.Clear();
            IRazerMappingActionCatalog catalog = new RazerNagaMappingActionCatalog(productId);
            var mapping = new RazerHidMappingViewModel(transport, canWrite: true,
                localMacros: () => system ? [] : [legacy], systemMacros: () => system ? [legacy] : [],
                productId: productId, actionCatalog: catalog, softwareTriggerResolver: Input,
                softwareTriggerBinder: (_, _, _, _, _, _, _, _) =>
                    throw new InvalidOperationException("Unavailable Naga binding reached the binder."),
                softwareTriggerClearer: (id, source, layer, _) =>
                {
                    Require(id == legacy.Id && source.Matches(input) && !layer,
                        "Cleared the wrong macro, input or layer.");
                    clears++;
                    legacy.Trigger = null;
                    return Task.FromResult<string?>(null);
                });
            void Require(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException($"{productId:X4}/{legacy.Id}: {message}");
            }
            void Keyboard(byte id)
            {
                var row = mapping.Rows.Single(row => row.ButtonId == id);
                row.GroupIndex = (int)RazerHidMappingGroup.KeyboardKey;
                row.OptionIndex = catalog.Decode(RazerHidMappingFunctions.KeyCode, [0, 7])!.OptionIndex;
            }
            async Task Save()
            {
                await mapping.ApplyAsync();
                Require(!mapping.HasError, mapping.ErrorText);
            }

            await mapping.ReadAsync();
            Require(mapping.IsLoaded && !mapping.HasError, "Could not load the legacy binding.");
            foreach (var row in mapping.Rows.Where(row => row.ButtonId is 0x40 or 0x50))
                Require(row.Group == RazerHidMappingGroup.Keep && row.SoftwareMacroId == legacy.Id &&
                    !row.CanApply && !row.CanUseSoftwareMacro &&
                    row.CurrentActionText.Contains(AppStrings.Text("RazerMappingMacroSoftwareUnavailable")),
                    "Legacy binding was not retained as unavailable Keep.");

            Keyboard(1);
            await Save();
            Require(transport.WrittenButtons.SequenceEqual(new[] { ((byte)1, (byte)0) }) &&
                clears == 0 && legacy.Trigger is not null,
                "Saving an unrelated key wrote or cleared a legacy binding.");
            var writes = transport.Writes;
            var error = await mapping.BindSoftwareMacroAsync(legacy.Id, input,
                MacroTriggerMode.Press, MacroPlaybackMode.Once, 1, false, null, CancellationToken.None);
            Require(error is not null && transport.Writes == writes && clears == 0,
                "Unavailable Naga binding was accepted.");

            foreach (var row in mapping.Rows) row.GroupIndex = (int)RazerHidMappingGroup.Keep;
            Keyboard(0x50);
            transport.WrittenButtons.Clear();
            await Save();
            Require(transport.WrittenButtons.SequenceEqual(new[] { ((byte)0x50, (byte)0) }) &&
                clears == 0 && legacy.Trigger is not null &&
                mapping.Rows.Single(row => row.ButtonId == 0x40).SoftwareMacroId == legacy.Id,
                "Moving one plate cleared the binding retained by the other plate.");

            mapping.Rows.Single(row => row.ButtonId == 0x50).GroupIndex = (int)RazerHidMappingGroup.Keep;
            Keyboard(0x40);
            transport.WrittenButtons.Clear();
            await Save();
            Require(transport.WrittenButtons.SequenceEqual(new[] { ((byte)0x40, (byte)0) }) &&
                clears == 1 && legacy.Trigger is null && legacy.AdditionalTriggers.Single() == otherLayer,
                "The final reference did not clear exactly once in its own layer.");
        }
    }

    private static async Task VerifySupportedMouseBindingsAsync()
    {
        const ushort productId = 0x0090;
        MacroInput Input(byte id) => RazerMouseMacroTriggerResolver.Resolve(productId, id, isNaga: true)!;
        var owner = new MacroDefinition
        {
            Id = "target-owner", Name = "Target owner",
            Trigger = new MacroTrigger { Input = Input(4) },
            Steps = [new MacroStep { Kind = MacroStepKind.Text, Text = "qa" }],
        };
        var native = new NativeMouseButtonAction(4).CreateMacro();
        native.Trigger = new MacroTrigger { Input = Input(2) };
        var shortcut = SoftwareShortcutCatalog.Actions[0].CreateMacro();
        shortcut.Trigger = new MacroTrigger { Input = Input(5) };
        var transport = new MappingReviewQaTransport([1, 2, 4, 5]);
        foreach (var id in new byte[] { 2, 4, 5 })
            await transport.SetAssignmentAsync(id, 0, RazerHidMappingFunctions.ButtonCode, [id]);
        transport.WrittenButtons.Clear();
        var bindings = new List<(string MacroId, MacroInput Input, bool HyperShiftLayer)>();
        var mapping = new RazerHidMappingViewModel(transport, canWrite: true, productId: productId,
            actionCatalog: new RazerNagaMappingActionCatalog(productId), localMacros: () => [owner],
            systemMacros: () => [native, shortcut], softwareTriggerResolver: Input,
            softwareTriggerBinder: (id, input, _, _, _, layer, _, _) =>
            {
                bindings.Add((id, input, layer));
                return Task.FromResult<string?>(null);
            },
            softwareTriggerClearer: (_, _, _, _) =>
                throw new InvalidOperationException("Saving supported mouse bindings cleared their triggers."));
        await mapping.ReadAsync();
        var remap = mapping.Rows.Single(row => row.ButtonId == 2);
        var shortcutRow = mapping.Rows.Single(row => row.ButtonId == 5);
        if (mapping.HasError || remap.Group != RazerHidMappingGroup.MouseButton ||
            remap.OptionIndex != RazerHidMappingActions.MouseButtonOptionIndex(4) ||
            remap.SoftwareMacroId != native.Id || shortcutRow.Group != RazerHidMappingGroup.WindowsShortcut ||
            shortcutRow.SoftwareMacroId != shortcut.Id)
            throw new InvalidOperationException("Supported mouse or shortcut binding did not read back.");
        var firmware = mapping.Rows.Single(row => row.ButtonId == 1);
        firmware.GroupIndex = (int)RazerHidMappingGroup.MouseButton;
        firmware.OptionIndex = RazerHidMappingActions.MouseButtonOptionIndex(3);
        await mapping.ApplyAsync();
        var firmwareAssignment = await transport.GetAssignmentAsync(1, 0);
        var hostAssignment = await transport.GetAssignmentAsync(2, 0);
        if (mapping.HasError || bindings.Count != 2 ||
            !transport.WrittenButtons.SequenceEqual(new[] { ((byte)1, (byte)0), ((byte)2, (byte)0) }) ||
            !bindings.Any(binding => binding.MacroId == native.Id && binding.Input.Matches(Input(2)) && !binding.HyperShiftLayer) ||
            !bindings.Any(binding => binding.MacroId == shortcut.Id && binding.Input.Matches(Input(5)) && !binding.HyperShiftLayer) ||
            firmwareAssignment.FunctionId != RazerHidMappingFunctions.ButtonCode ||
            !firmwareAssignment.Data.SequenceEqual(new byte[] { 3 }) ||
            hostAssignment.FunctionId != RazerHidMappingFunctions.ButtonCode ||
            !hostAssignment.Data.SequenceEqual(new byte[] { 2 }) ||
            native.Trigger is null || shortcut.Trigger is null || owner.Trigger is null)
            throw new InvalidOperationException("Supported firmware, host remap or shortcut save regressed: " + mapping.ErrorText);
    }

    private sealed class MappingReviewQaTransport(IReadOnlyList<byte> reportedIds) : IRazerMappingTransport
    {
        private readonly Dictionary<(byte, byte), RazerMappingAssignment> _assignments = [];
        public List<(byte ButtonId, byte Mode)> WrittenButtons { get; } = [];
        public int Writes => WrittenButtons.Count;
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
            WrittenButtons.Add((buttonId, mode));
            _assignments[(buttonId, mode)] = new(functionId, data.ToArray());
            return Task.CompletedTask;
        }
    }
}
#endif
