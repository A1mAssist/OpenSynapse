#if DEBUG
using System.Reflection;
using System.Text.Json;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App;

public partial class App
{
    private static async Task VerifyKeypadMacroTransactionsAsync(ushort productId)
    {
        var document = ProfileDocument.CreateDefault();
        var profile = document.Profiles[document.ActiveProfileName];
        MacroDefinition Macro(string id) => new()
        {
            Id = id, Name = id, Enabled = true,
            Steps = [new MacroStep { Kind = MacroStepKind.Text, Text = "qa" }],
        };
        var first = Macro("first");
        var second = Macro("second");
        profile.Macros.AddRange([first, second]);
        var failSave = false;
        var saves = 0;
        var persisted = string.Empty;
        Task<bool> Save(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            saves++;
            if (failSave) throw new IOException("Simulated profile save failure.");
            persisted = JsonSerializer.Serialize(profile);
            return Task.FromResult(true);
        }
        var macros = new MacroWorkspace(() => profile, Save, new LocalDiagnosticLog(), new KeypadQaSink());
        macros.RefreshMacroState();
        var connection = new RazerHidDeviceConnection(RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!,
            "keypad-macro-qa", "keypad-macro-qa", RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.MappingRead, RazerHidBackendCapability.MappingWrite },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);
        var keyId = RazerKeypadInputCatalog.IsLegacy(productId) ? (byte)(productId is 0x0201 or 0x0208 ? 6 : 1) : (byte)0x40;
        var runtime = new RazerKeypadMappingRuntime(connection.InstanceId, new KeypadQaSink(), productId: productId);
        var service = new RazerHidDeviceService();
        service.KeypadSessionFactory = (_, _, _) =>
            Task.FromResult(new RazerKeypadMappingSession(connection.DevicePath!, runtime));
        await using var devices = new RazerHidWorkspace(service, null, () => PowerSourceKind.FullPowerAdapter,
            () => document, () => profile, Save, new LocalDiagnosticLog(), () => false,
            () => profile.Macros, macros.TryApplyMacroBindingAsync, macros.TryClearMacroTriggerAsync,
            () => new MachineSettings(), _ => Task.FromResult(true), _ => Task.CompletedTask, () => { }, _ => { }, null);
        typeof(RazerHidWorkspace).GetField("_connectionSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(devices, new[] { connection });
        macros.SoftwareBindingRestoreTransactionFactory = devices.BeginSoftwareBindingRestoreAsync;
        var deviceKey = ProfileResolver.GetDeviceKey(connection.ToDescriptor());
        var settings = new DeviceProfileSettings();
        profile.Devices[deviceKey] = settings;
        MacroInput Input(byte button) => new()
        {
            Kind = MacroInputKind.RazerKey, DeviceId = productId,
            InstanceId = connection.InstanceId, RazerKeyCode = button,
        };
        var mapping = new RazerHidMappingViewModel(new RazerHidMappingTransport(service, connection), true,
            productId: productId, deviceKind: RazerMappingDeviceKind.Keyboard,
            actionCatalog: new RazerTartarusProMappingActionCatalog(productId),
            localMacros: () => profile.Macros, systemMacros: () => profile.SystemMacros,
            softwareTriggerResolver: Input, softwareTriggerBinder: macros.TryApplyMacroBindingAsync,
            softwareTriggerClearer: macros.TryClearMacroTriggerAsync,
            keypadMacroTransactionFactory: macros.BeginKeypadMacroTransactionAsync,
            mappingProfileSaver: async (mappings, token) =>
            {
                var previous = settings.Keyboard.ButtonMappings;
                settings.Keyboard.ButtonMappings = mappings.Select(item => item.Clone()).ToList();
                try { await Save(token); }
                catch { settings.Keyboard.ButtonMappings = previous; throw; }
            }) { DeviceInstanceId = connection.InstanceId };
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        void SelectMacro(int index)
        {
            var row = mapping.Rows.Single(item => item.ButtonId == keyId);
            row.GroupIndex = (int)RazerHidMappingGroup.Macro;
            row.MacroTargetIndex = (int)MacroExecutionTarget.Software;
            row.OptionIndex = index;
        }
        bool IsOff() => runtime.GetAssignment(keyId).FunctionId == RazerHidMappingFunctions.Off;
        bool PersistedOff() => JsonSerializer.Deserialize<ProfileDefinition>(persisted)!
            .Devices[deviceKey].Keyboard.ButtonMappings!.Single(item => item.ButtonId == keyId).FunctionId == RazerHidMappingFunctions.Off;

        try
        {
            await mapping.ReadAsync();
            var original = runtime.GetAssignment(keyId);
            SelectMacro(0);
            failSave = true;
            await mapping.ApplyAsync();
            Require(mapping.HasError && !first.AllTriggers.Any() &&
                runtime.GetAssignment(keyId).Data.SequenceEqual(original.Data), "New binding survived a failed save.");
            failSave = false;
            SelectMacro(0);
            var previousSaves = saves;
            await mapping.ApplyAsync();
            Require(!mapping.HasError && first.AllTriggers.Count() == 1 && IsOff() && PersistedOff() &&
                saves == previousSaves + 1, "Binding did not save macro and Off exactly once: " + mapping.ErrorText);

            await mapping.ReadAsync();
            SelectMacro(0);
            await mapping.ApplyAsync();
            var retainedBinding = first.Trigger!.SoftwareBinding ?? first.SoftwareBinding!;
            Require(retainedBinding.OriginalFunctionId == original.FunctionId &&
                retainedBinding.OriginalData.SequenceEqual(original.Data), "Editing the same macro overwrote its original assignment.");

            await mapping.ReadAsync();
            SelectMacro(1);
            failSave = true;
            await mapping.ApplyAsync();
            Require(first.AllTriggers.Count() == 1 && !second.AllTriggers.Any() && IsOff(), "Replacing a macro lost the original binding.");

            var clearError = await macros.TryClearMacroTriggerAsync(first.Id, Input(keyId), false);
            Require(clearError is not null && first.AllTriggers.Count() == 1 && IsOff() && PersistedOff(),
                "Failed clear did not restore the macro and disabled assignment.");
            macros.SelectedMacro = macros.Items.Single(row => row.Id == first.Id);
            await macros.DeleteSelectedMacroAsync();
            Require(profile.Macros.Contains(first) && macros.Items.Any(row => row.Id == first.Id) && IsOff(),
                "Failed deletion did not restore the macro and disabled assignment.");

            failSave = false;
            clearError = await macros.TryClearMacroTriggerAsync(first.Id, Input(keyId), false);
            Require(clearError is null && !first.AllTriggers.Any() && !IsOff() && !PersistedOff() &&
                runtime.GetAssignment(keyId).Data.SequenceEqual(original.Data), "Successful clear did not persist the original mapping.");

            await mapping.ReadAsync();
            failSave = true;
            var failedBind = await mapping.BindSoftwareMacroAsync(second.Id, Input(keyId), MacroTriggerMode.Press,
                MacroPlaybackMode.Once, 1, false, null, CancellationToken.None);
            Require(failedBind is not null && !second.AllTriggers.Any() && !IsOff(), "Failed macro-page binding was not rolled back.");
            failSave = false;
            previousSaves = saves;
            var bindError = await mapping.BindSoftwareMacroAsync(second.Id, Input(keyId), MacroTriggerMode.Press,
                MacroPlaybackMode.Once, 1, false, null, CancellationToken.None);
            Require(bindError is null && second.AllTriggers.Count() == 1 && IsOff() && PersistedOff() &&
                saves == previousSaves + 1, "Macro-page binding did not persist Off once: " + bindError);
            macros.SelectedMacro = macros.Items.Single(row => row.Id == second.Id);
            await macros.DeleteSelectedMacroAsync();
            Require(!profile.Macros.Contains(second) && !IsOff() && !PersistedOff(), "Successful deletion left a disabled mapping.");
        }
        finally { macros.Dispose(); }
    }
}
#endif
