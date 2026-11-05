using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidMappingTests
{
    [Fact]
    public void SetAssignmentUsesTheMiddlewarePayloadLayout()
    {
        var request = RazerHidMappingProtocol.SetAssignment(
            0x04, RazerHidMappingProtocol.NormalMode, RazerHidMappingFunctions.ButtonCode, [0x05]);

        Assert.Equal(0x02, request.Report[7]);
        Assert.Equal(0x0C, request.Report[8]);
        Assert.Equal(80, request.Report[6]);
        Assert.Equal(
            new byte[] { 0x01, 0x04, 0x00, 0x01, 0x01, 0x05 },
            request.Report.AsSpan(RazerFeatureReport.ArgumentsOffset, 6).ToArray());
    }

    [Fact]
    public void GetAssignmentSendsProfileButtonAndMode()
    {
        var request = RazerHidMappingProtocol.GetAssignment(0x03, RazerHidMappingProtocol.HypershiftMode);

        Assert.Equal(0x02, request.Report[7]);
        Assert.Equal(0x8C, request.Report[8]);
        Assert.Equal(
            new byte[] { 0x01, 0x03, 0x01 },
            request.Report.AsSpan(RazerFeatureReport.ArgumentsOffset, 3).ToArray());
    }

    [Fact]
    public void AssignmentResponseParsesButtonModeFunctionAndData()
    {
        var response = Response([0x01, 0x04, 0x00, 0x01, 0x01, 0x05]);

        var assignment = RazerHidMappingProtocol.ParseAssignment(response);

        Assert.Equal(0x04, assignment.ButtonId);
        Assert.Equal(0x00, assignment.Mode);
        Assert.Equal(RazerHidMappingFunctions.ButtonCode, assignment.FunctionId);
        Assert.Equal([0x05], assignment.Data);
    }

    [Fact]
    public void ButtonListResponseCarriesTheDeviceIds()
    {
        var response = Response([0x03, 0x01, 0x02, 0x04]);

        Assert.Equal([0x01, 0x02, 0x04], RazerHidMappingProtocol.ParseButtonList(response));
    }

    [Fact]
    public void MappingCapabilitiesFollowTheMiddlewareMouseList()
    {
        Assert.True(RazerHidDeviceCatalog.HasOnboardMapping(0x00E5));
        Assert.True(RazerHidDeviceCatalog.HasOnboardMapping(0x00A2));
        Assert.False(RazerHidDeviceCatalog.HasOnboardMapping(0x00C1));
    }

    [Fact]
    public async Task MappingProbeCachesOnlyPositiveResults()
    {
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00E5)!;
        var positive = new ProbeTransport(_ => Response([0x03, 0x01, 0x02, 0x04]));
        var positiveService = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, positive);

        Assert.Contains(RazerHidBackendCapability.MappingRead,
            await positiveService.ProbeMiddlewareCapabilitiesAsync(definition, "test"));
        await positiveService.ProbeMiddlewareCapabilitiesAsync(definition, "test");
        Assert.Equal(1, positive.Requests.Count(request => request[7] == 0x02 && request[8] == 0x84));

        var negative = new ProbeTransport(_ => throw new NotSupportedException());
        var negativeService = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, negative);

        Assert.Empty(await negativeService.ProbeMiddlewareCapabilitiesAsync(definition, "test"));
        Assert.Empty(await negativeService.ProbeMiddlewareCapabilitiesAsync(definition, "test"));
        Assert.Equal(2, negative.Requests.Count(request => request[7] == 0x02 && request[8] == 0x84));
    }

    [Fact]
    public async Task MappingProbeSkipsNonPointerDevices()
    {
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x054A)!;
        var transport = new ProbeTransport(_ => throw new InvalidOperationException("must not probe"));
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        Assert.Empty(await service.ProbeMiddlewareCapabilitiesAsync(definition, "test"));
        Assert.Equal(0, transport.Count);
    }

    [Fact]
    public async Task ScrollWheelProbeAddsTheMiddlewareCapabilities()
    {
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00E5)!;
        var transport = new ProbeTransport(request => request[8] switch
        {
            0x94 => Echo(request, 0x00, (byte)RazerScrollFreeNotchedMode.FreeSpinMode),
            0xA2 => Echo(request, 0x00, 0x05),
            _ => Response([0x03, 0x01, 0x02, 0x04]),
        });
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var capabilities = await service.ProbeMiddlewareCapabilitiesAsync(definition, "test");

        Assert.Contains(RazerHidBackendCapability.MappingRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.ScrollFreeNotchedModeRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.ScrollAccelerationLevelRead, capabilities);
    }

    [Fact]
    public void CatalogUsesTheMiddlewareFunctionIds()
    {
        AssertFrame(RazerHidMappingGroup.Off, 0, RazerHidMappingFunctions.Off, []);
        AssertFrame(RazerHidMappingGroup.MouseButton, 0, RazerHidMappingFunctions.ButtonCode, [0x01]);
        AssertFrame(RazerHidMappingGroup.KeyboardKey, IndexOf(RazerHidMappingGroup.KeyboardKey, "A"),
            RazerHidMappingFunctions.KeyCode, [0x00, 0x04]);
        AssertFrame(RazerHidMappingGroup.KeyboardKey, IndexOf(RazerHidMappingGroup.KeyboardKey, "Left Shift"),
            RazerHidMappingFunctions.KeyCode, [0xE1, 0x00]);
        AssertFrame(RazerHidMappingGroup.Dpi, IndexOf(RazerHidMappingGroup.Dpi, "DPI cycle up"),
            RazerHidMappingFunctions.Dpi, [0x06]);
        AssertFrame(RazerHidMappingGroup.Profile, IndexOf(RazerHidMappingGroup.Profile, "Profile cycle down"),
            RazerHidMappingFunctions.Profile, [0x05]);
        AssertFrame(RazerHidMappingGroup.MediaKey, IndexOf(RazerHidMappingGroup.MediaKey, "Mute"),
            RazerHidMappingFunctions.MediaKeys, [0x00, 0xE2]);
        AssertFrame(RazerHidMappingGroup.PowerKey, IndexOf(RazerHidMappingGroup.PowerKey, "Sleep"),
            RazerHidMappingFunctions.PowerKeys, [0x82]);
        AssertFrame(RazerHidMappingGroup.DoubleClick, 0, RazerHidMappingFunctions.DoubleClick, [0x01]);
        AssertFrame(RazerHidMappingGroup.ScrollWheelMode,
            IndexOf(RazerHidMappingGroup.ScrollWheelMode, "Scroll cycle up"),
            RazerHidMappingFunctions.ScrollWheelMode, [0x04]);
        AssertFrame(RazerHidMappingGroup.RazerKey, IndexOf(RazerHidMappingGroup.RazerKey, "HyperShift"),
            RazerHidMappingFunctions.RazerKey, [0x59]);
        AssertFrame(RazerHidMappingGroup.WindowsShortcut,
            IndexOf(RazerHidMappingGroup.WindowsShortcut, "Screenshot"),
            RazerHidMappingFunctions.Win8ShortcutsKey, [0x63]);
        AssertFrame(RazerHidMappingGroup.HyperShiftToggle, 0,
            RazerHidMappingFunctions.ModeButtonkey, [0x01]);
    }

    [Fact]
    public void KeyboardFrameCarriesTheModifierMask()
    {
        var action = RazerHidMappingActions.CreateAction(
            RazerHidMappingGroup.KeyboardKey,
            IndexOf(RazerHidMappingGroup.KeyboardKey, "C"),
            modifierMask: 0x01);

        Assert.NotNull(action);
        Assert.Equal(RazerHidMappingFunctions.KeyCode, action.FunctionId);
        Assert.Equal([0x01, 0x06], action.Data);
        Assert.Equal("Ctrl+C", RazerHidMappingActions.Describe(action.FunctionId, action.Data));
    }

    [Fact]
    public void EveryCatalogOptionRoundTripsThroughTheDecoder()
    {
        foreach (var group in Enum.GetValues<RazerHidMappingGroup>())
        {
            if (group == RazerHidMappingGroup.Keep)
            {
                continue;
            }

            IReadOnlyList<RazerHidMacroOption>? macros = group == RazerHidMappingGroup.Macro
                ? [new RazerHidMacroOption(3, "Macro 3")]
                : null;
            var names = RazerHidMappingActions.OptionNames(group, macros);
            Assert.NotEmpty(names);
            for (var index = 0; index < names.Count; index++)
            {
                var action = RazerHidMappingActions.CreateAction(group, index, macros: macros);
                Assert.NotNull(action);
                Assert.Equal(names[index], action.Name);
                Assert.Equal(names[index],
                    RazerHidMappingActions.Describe(action.FunctionId, action.Data, macros));
            }
        }
    }

    [Fact]
    public void MacroAssignmentUsesTheOnboardMacroId()
    {
        RazerHidMacroOption[] macros = [new(7, "Macro 7")];

        var action = RazerHidMappingActions.CreateAction(
            RazerHidMappingGroup.Macro, 0, macros: macros);

        Assert.NotNull(action);
        Assert.Equal(RazerHidMappingFunctions.MacroTypeI, action.FunctionId);
        Assert.Equal([0x00, 0x07, 0x01], action.Data);
        Assert.Equal("Macro 7", RazerHidMappingActions.Describe(
            action.FunctionId, action.Data, macros));

        var request = RazerHidMappingProtocol.SetAssignment(
            0x04, RazerHidMappingProtocol.NormalMode, action.FunctionId, action.Data);
        Assert.Equal(
            new byte[] { 0x01, 0x04, 0x00, 0x03, 0x03, 0x00, 0x07, 0x01 },
            request.Report.AsSpan(RazerFeatureReport.ArgumentsOffset, 8).ToArray());
    }

    [Fact]
    public void TurboKeyUsesModifierUsageAndInterval()
    {
        var action = RazerHidMappingActions.CreateAction(
            RazerHidMappingGroup.TurboKey,
            IndexOf(RazerHidMappingGroup.TurboKey, "Turbo A"),
            modifierMask: 0x01,
            turboRate: 7);

        Assert.NotNull(action);
        Assert.Equal(RazerHidMappingFunctions.TurboModeKey, action.FunctionId);
        Assert.Equal([0x01, 0x04, 0x00, 0x8E], action.Data);
    }

    [Fact]
    public void TurboButtonUsesButtonAndInterval()
    {
        var action = RazerHidMappingActions.CreateAction(
            RazerHidMappingGroup.TurboButton,
            IndexOf(RazerHidMappingGroup.TurboButton, "Turbo Forward"),
            turboRate: 10);

        Assert.NotNull(action);
        Assert.Equal(RazerHidMappingFunctions.TurboModeButton, action.FunctionId);
        Assert.Equal([0x05, 0x00, 0x64], action.Data);
    }

    [Fact]
    public void DpiClutchUsesTheFiveByteXYPayload()
    {
        var action = RazerHidMappingActions.CreateAction(
            RazerHidMappingGroup.DpiClutch,
            0,
            dpiClutchX: 800,
            dpiClutchY: 1600);

        Assert.NotNull(action);
        Assert.Equal(RazerHidMappingFunctions.Dpi, action.FunctionId);
        Assert.Equal([0x05, 0x03, 0x20, 0x06, 0x40], action.Data);
        Assert.Equal("DPI clutch", RazerHidMappingActions.Describe(action.FunctionId, action.Data));
    }

    [Fact]
    public void SpecificProfileUsesTheProfileId()
    {
        var action = RazerHidMappingActions.CreateAction(
            RazerHidMappingGroup.ProfileSpecific,
            0,
            profileId: 3);

        Assert.NotNull(action);
        Assert.Equal(RazerHidMappingFunctions.Profile, action.FunctionId);
        Assert.Equal([0x03, 0x03], action.Data);
        Assert.Equal(
            "Specific profile",
            RazerHidMappingActions.Describe(action.FunctionId, action.Data));
    }

    [Theory]
    [InlineData(RazerHidMacroPlayback.Once, RazerHidMappingFunctions.MacroTypeI, new byte[] { 0x00, 0x07, 0x01 })]
    [InlineData(RazerHidMacroPlayback.Repeat, RazerHidMappingFunctions.MacroTypeI, new byte[] { 0x00, 0x07, 0x04 })]
    [InlineData(RazerHidMacroPlayback.Hold, RazerHidMappingFunctions.MacroTypeII, new byte[] { 0x00, 0x07 })]
    [InlineData(RazerHidMacroPlayback.Toggle, RazerHidMappingFunctions.MacroTypeIII, new byte[] { 0x00, 0x07 })]
    [InlineData(RazerHidMacroPlayback.Sequence, RazerHidMappingFunctions.MacroTypeIV, new byte[] { 0x00, 0x07 })]
    public void MacroPlaybackModesUseTheMiddlewareFunctionIds(
        RazerHidMacroPlayback playback,
        byte functionId,
        byte[] data)
    {
        RazerHidMacroOption[] macros = [new(7, "Macro 7")];

        var action = RazerHidMappingActions.CreateAction(
            RazerHidMappingGroup.Macro,
            0,
            macros: macros,
            macroPlayback: playback,
            macroRepeatCount: 4);

        Assert.NotNull(action);
        Assert.Equal(functionId, action.FunctionId);
        Assert.Equal(data, action.Data);
    }

    [Fact]
    public void MacroGroupExplainsWhenTheDeviceHasNoSlots()
    {
        Assert.Equal(
            ["No readable macro slots"],
            RazerHidMappingActions.OptionNames(RazerHidMappingGroup.Macro));
        Assert.Null(RazerHidMappingActions.CreateAction(
            RazerHidMappingGroup.Macro, 0));
    }

    [Fact]
    public void MediaKeyAssignmentUsesTheTwoByteFrame()
    {
        var action = RazerHidMappingActions.CreateAction(
            RazerHidMappingGroup.MediaKey, IndexOf(RazerHidMappingGroup.MediaKey, "Volume up"));
        Assert.NotNull(action);

        var request = RazerHidMappingProtocol.SetAssignment(
            0x04, RazerHidMappingProtocol.NormalMode, action.FunctionId, action.Data);

        Assert.Equal(
            new byte[] { 0x01, 0x04, 0x00, 0x0A, 0x02, 0x00, 0xE9 },
            request.Report.AsSpan(RazerFeatureReport.ArgumentsOffset, 7).ToArray());
    }

    private static void AssertFrame(
        RazerHidMappingGroup group,
        int index,
        byte functionId,
        byte[] data,
        byte modifierMask = 0)
    {
        var action = RazerHidMappingActions.CreateAction(group, index, modifierMask);

        Assert.NotNull(action);
        Assert.Equal(functionId, action.FunctionId);
        Assert.Equal(data, action.Data);
    }

    private static int IndexOf(RazerHidMappingGroup group, string name)
    {
        var index = RazerHidMappingActions.OptionNames(group).ToList().IndexOf(name);
        Assert.True(index >= 0, $"{group} has no option {name}");
        return index;
    }

    private static byte[] Response(byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[6] = (byte)arguments.Length;
        arguments.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }

    /// <summary>Reply that echoes the request header so <c>IsSuccessfulResponse</c> accepts it.</summary>
    private static byte[] Echo(byte[] request, params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[2] = request[2];
        response[3] = request[3];
        response[4] = request[4];
        response[6] = request[6];
        response[7] = request[7];
        response[8] = request[8];
        arguments.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }

    private sealed class ProbeTransport(Func<byte[], byte[]> handler) : IRazerFeatureTransport
    {
        public int Count { get; private set; }
        public List<byte[]> Requests { get; } = [];

        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(
            string devicePath,
            ReadOnlyMemory<byte> request,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            Count++;
            var report = request.ToArray();
            Requests.Add(report);
            return Task.FromResult(handler(report));
        }
    }
}
