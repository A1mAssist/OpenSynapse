#if DEBUG
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    // Run on the UI thread from the isolated auto-apply QA window.
    internal static async Task VerifyKeyboardAutoApplyAsync()
    {
        var transport = new KeyboardAutoApplyQaTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x028D)!;
        var connection = new RazerHidDeviceConnection(definition, "qa-keyboard", "qa-keyboard",
            RazerHidEndpointState.Resolved, new HashSet<RazerHidBackendCapability>
            {
                RazerHidBackendCapability.AnalogKeyWrite,
            }, new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);
        var document = ProfileDocument.CreateDefault();
        var profile = new ProfileDefinition();
        await using var viewModel = new RazerHidDeviceViewModel(service, connection,
            getActiveProfile: () => profile, getProfileDocument: () => document);
        var first = new AnalogKeyKeyViewModel(31, "A", 44) { ActuationMillimeters = 1.1 };
        var second = new AnalogKeyKeyViewModel(33, "D", 44) { ActuationMillimeters = 2.5 };
        viewModel._analogKeyKeys.AddRange([first, second]);
        viewModel._confirmedAnalogKeys.UnionWith([first.KeyId, second.KeyId]);

        first.IsSelected = true;
        viewModel.SynchronizeAnalogKeySelection();
        Require(viewModel.PrepareAnalogKeySelectionAutoApply(default) is null, "Selecting one key submitted a write.");
        second.IsSelected = true;
        viewModel.SynchronizeAnalogKeySelection();
        Require(viewModel.PrepareAnalogKeySelectionAutoApply(default) is null, "Selecting mixed-value keys submitted a write.");
        viewModel.AnalogKeyActuationPointMillimeters = 1.7;
        var pending = viewModel.PrepareAnalogKeySelectionAutoApply(default);
        Require(pending is not null, "A valid selected-key edit was ignored.");
        first.IsSelected = false;
        viewModel.SynchronizeAnalogKeySelection();
        Require(await pending!() is not null, "An obsolete selection was accepted.");
        Require(viewModel.AnalogKeyActuationPointMillimeters == 2.5 && transport.Requests.Count == 0,
            "An old draft reached the newly selected key.");
        Require(viewModel.PrepareAnalogKeySelectionAutoApply(default) is null, "Selection readback submitted a write.");

        viewModel.AnalogKeyActuationPointMillimeters = 1.8;
        pending = viewModel.PrepareAnalogKeySelectionAutoApply(default);
        document.ActiveProfileName = "another-profile";
        Require(pending is not null && await pending() is not null && transport.Requests.Count == 0,
            "Changing ActiveProfileName did not cancel a queued write.");
        pending = viewModel.PrepareAnalogKeySelectionAutoApply(default);
        viewModel.ActiveOnboardProfileId++;
        Require(pending is not null && await pending() is not null && transport.Requests.Count == 0,
            "Changing the onboard profile did not cancel a queued write.");

        viewModel._analogSnapTapAvailable = true;
        viewModel._confirmedAnalogSnapTap = (false, []);
        viewModel.AddAnalogSnapTapPair();
        Require(viewModel.PrepareAnalogSnapTapAutoApply(default) is null &&
            viewModel.AnalogSnapTapStatusText == AppStrings.Text("SnapTapNeedTwoKeys"), "An empty pair has no validation feedback.");
        viewModel._analogSnapTapPairs[0].Key1 = first;
        Require(viewModel.PrepareAnalogSnapTapAutoApply(default) is null, "A half-filled pair was accepted.");
        viewModel._analogSnapTapPairs[0].Key2 = first;
        Require(viewModel.PrepareAnalogSnapTapAutoApply(default) is null &&
            viewModel.AnalogSnapTapStatusText == AppStrings.Text("SnapTapDuplicateInPair"), "A duplicate key was accepted.");

        await VerifyKeyboardAutoApplyRollbackAsync();

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    private static async Task VerifyKeyboardAutoApplyRollbackAsync()
    {
        var initialStages = Enumerable.Range(0, RazerKnobProtocol.StageCount)
            .Select(index => new RazerKnobStage(0, (byte)index, (byte)index, 10, 20, 30)).ToArray();
        var initialKnob = RazerKnobProtocol.CreateSetConfigurationRequest(1, 1, KnobId, initialStages)[9..59];
        var initialMode = RazerKnobProtocol.CreateSetModeRequest(1, 1, KnobId, initialStages[0])[9..17];
        RazerAnalogSnapTapPair[] initialPairs = [new(31, 33, 1), new(17, 18, 0)];
        var initialSnapTap = RazerAnalogKeyProtocol.CreateSetAnalogSnapTapRequest(1, 1, true, initialPairs)[9..24];
        var knob = initialKnob.ToArray();
        var mode = initialMode.ToArray();
        var snapTap = initialSnapTap.ToArray();
        byte deviceMode = 0;
        var knobWrites = 0;
        var snapTapWrites = 0;
        var transport = new KeyboardAutoApplyQaTransport(request =>
        {
            var response = request.ToArray();
            response[1] = 2;
            switch (request[7], request[8])
            {
                case (3, 0x12): knob = request[9..59]; knobWrites++; break;
                case (3, 0x92): knob.CopyTo(response, 9); break;
                case (3, 0x13): mode = request[9..17]; break;
                case (3, 0x93): mode.CopyTo(response, 9); break;
                case (2, 0x27): snapTap = request[9..24]; snapTapWrites++; break;
                case (2, 0xA7): snapTap.CopyTo(response, 9); break;
                case (0, 0x04): deviceMode = request[9]; break;
                case (0, 0x84): response[9] = deviceMode; break;
                default: throw new InvalidOperationException($"Unexpected rollback QA command {request[7]:X2}:{request[8]:X2}.");
            }
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return response;
        });
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x028D)!,
            "qa-keyboard-rollback", "qa-keyboard-rollback", RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability>
            {
                RazerHidBackendCapability.KnobRead, RazerHidBackendCapability.KnobWrite,
                RazerHidBackendCapability.AnalogKeyWrite,
            }, new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);
        var profile = new ProfileDefinition();
        var saveSucceeds = false;
        var saveCalls = 0;
        await using var viewModel = new RazerHidDeviceViewModel(service, connection,
            getActiveProfile: () => profile,
            saveProfileAsync: _ => { saveCalls++; return Task.FromResult(saveSucceeds); });
        var settings = viewModel.ResolveDeviceProfileSettings()!;
        settings.Keyboard.KnobMode = initialStages[0].ModeId;
        settings.Keyboard.SnapTapEnabled = true;

        await viewModel.LoadKnobAsync();
        Require(viewModel._confirmedKnob is not null && viewModel.PrepareKnobAutoApply(default) is null,
            "Initial knob readback was not confirmed.");
        viewModel.KnobStages[0].IsEnabled = false;
        viewModel.KnobStages[1].Red = 99;
        viewModel.SelectedKnobStage = viewModel.KnobStages[1];
        var requestedStages = viewModel.KnobStages.Select(row => row.ToStage()).ToArray();
        var applyKnob = viewModel.PrepareKnobAutoApply(default)!;
        Require(await applyKnob() == AppStrings.Text("RazerHidSettingProfileSaveFailed") && saveCalls == 1,
            "Knob did not report the injected persistence failure.");
        Require(knobWrites == 2 && knob.SequenceEqual(initialKnob) && mode.SequenceEqual(initialMode),
            "Knob rollback did not restore the hardware table and active stage.");
        Require(viewModel.KnobStages.Select(row => row.ToStage()).SequenceEqual(initialStages) &&
            viewModel.SelectedKnobStage?.Stage == 0 && viewModel._knobCurrentStage == initialStages[0] &&
            viewModel._confirmedKnob is { } restoredKnob && restoredKnob.Selected == 0 &&
            restoredKnob.Stages.SequenceEqual(initialStages) && settings.Keyboard.KnobMode == initialStages[0].ModeId &&
            viewModel.PrepareKnobAutoApply(default) is null, "Knob rollback left a stale UI, profile or confirmed snapshot.");
        saveSucceeds = true;
        Require(await applyKnob() is null && saveCalls == 2 && knobWrites == 3 &&
            viewModel.KnobStages.Select(row => row.ToStage()).SequenceEqual(requestedStages) &&
            viewModel.SelectedKnobStage?.Stage == 1 && settings.Keyboard.KnobMode == requestedStages[1].ModeId &&
            viewModel.PrepareKnobAutoApply(default) is null, "Knob retry lost the captured edit or confirmation.");

        AnalogKeyboardLayouts.TryGet(0x02A7, out var layout);
        viewModel._analogKeyLayout = layout;
        foreach (var id in new byte[] { 31, 33, 17, 18 })
            viewModel._analogKeyKeys.Add(new AnalogKeyKeyViewModel(id, id.ToString(), 44));
        await viewModel.RefreshAnalogSnapTapAsync(default);
        Require(viewModel._confirmedAnalogSnapTap is not null && viewModel.PrepareAnalogSnapTapAutoApply(default) is null,
            "Initial Snap Tap readback was not confirmed.");
        viewModel.AnalogSnapTapEnabled = false;
        viewModel._analogSnapTapPairs.Clear();
        viewModel._analogSnapTapPairs.Add(new AnalogSnapTapPairViewModel(viewModel._analogSnapTapKeyChoices,
            viewModel._analogKeyKeys[0], viewModel._analogKeyKeys[3], 2));
        var applySnapTap = viewModel.PrepareAnalogSnapTapAutoApply(default)!;
        saveSucceeds = false;
        Require(await applySnapTap() == AppStrings.Text("RazerHidSettingProfileSaveFailed") && saveCalls == 3,
            "Snap Tap did not report the injected persistence failure.");
        Require(snapTapWrites == 2 && snapTap.SequenceEqual(initialSnapTap), "Snap Tap rollback did not restore hardware.");
        Require(viewModel.AnalogSnapTapEnabled &&
            viewModel.AnalogSnapTapPairs.Select(pair => new RazerAnalogSnapTapPair(pair.Key1!.KeyId, pair.Key2!.KeyId, pair.Mode))
                .SequenceEqual(initialPairs) &&
            viewModel._confirmedAnalogSnapTap is { Enabled: true } restoredSnapTap && restoredSnapTap.Pairs.SequenceEqual(initialPairs) &&
            settings.Keyboard.SnapTapEnabled == true && viewModel.PrepareAnalogSnapTapAutoApply(default) is null,
            "Snap Tap rollback left stale keys, modes, enabled state, profile or confirmed snapshot.");
        saveSucceeds = true;
        Require(await applySnapTap() is null && saveCalls == 4 && snapTapWrites == 3 &&
            !viewModel.AnalogSnapTapEnabled && viewModel.AnalogSnapTapPairs.Count == 1 &&
            viewModel.AnalogSnapTapPairs[0] is { Key1.KeyId: 31, Key2.KeyId: 18, Mode: 2 } &&
            settings.Keyboard.SnapTapEnabled == false && viewModel.PrepareAnalogSnapTapAutoApply(default) is null,
            "Snap Tap retry lost the captured edit or confirmation.");

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    internal sealed class KeyboardAutoApplyQaTransport(Func<byte[], byte[]>? respond = null) : IRazerFeatureTransport
    {
        internal List<byte[]> Requests { get; } = [];

        public Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize,
            byte commandClass, byte commandId, ReadOnlyMemory<byte> arguments, TimeSpan deviceWait,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false) =>
            throw new InvalidOperationException("Unexpected keyboard QA transport request.");

        public Task<byte[]> QueryPreparedAsync(string devicePath, ReadOnlyMemory<byte> request,
            TimeSpan deviceWait, CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
        {
            Requests.Add(request.ToArray());
            if (respond is not null) return Task.FromResult(respond(request.ToArray()));
            throw new InvalidOperationException("Selection and context checks must not reach the transport.");
        }
    }
}
#endif
