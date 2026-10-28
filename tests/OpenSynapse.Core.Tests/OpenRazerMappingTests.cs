using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerMappingTests
{
    [Fact]
    public void SetAssignmentUsesTheMiddlewarePayloadLayout()
    {
        var request = OpenRazerMappingProtocol.SetAssignment(
            0x04, OpenRazerMappingProtocol.NormalMode, OpenRazerMappingFunctions.ButtonCode, [0x05]);

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
        var request = OpenRazerMappingProtocol.GetAssignment(0x03, OpenRazerMappingProtocol.HypershiftMode);

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

        var assignment = OpenRazerMappingProtocol.ParseAssignment(response);

        Assert.Equal(0x04, assignment.ButtonId);
        Assert.Equal(0x00, assignment.Mode);
        Assert.Equal(OpenRazerMappingFunctions.ButtonCode, assignment.FunctionId);
        Assert.Equal([0x05], assignment.Data);
    }

    [Fact]
    public void ButtonListResponseCarriesTheDeviceIds()
    {
        var response = Response([0x03, 0x01, 0x02, 0x04]);

        Assert.Equal([0x01, 0x02, 0x04], OpenRazerMappingProtocol.ParseButtonList(response));
    }

    [Fact]
    public void MappingCapabilitiesFollowTheMiddlewareMouseList()
    {
        Assert.True(OpenRazerDeviceCatalog.HasOnboardMapping(0x00E5));
        Assert.True(OpenRazerDeviceCatalog.HasOnboardMapping(0x00A2));
        Assert.False(OpenRazerDeviceCatalog.HasOnboardMapping(0x00C1));
    }

    [Fact]
    public async Task MappingProbeCachesPositiveAndNegativeResults()
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00E5)!;
        var positive = new ProbeTransport(_ => Response([0x03, 0x01, 0x02, 0x04]));
        var positiveService = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, positive);

        Assert.Contains(OpenRazerBackendCapability.MappingRead,
            await positiveService.ProbeMiddlewareCapabilitiesAsync(definition, "test"));
        await positiveService.ProbeMiddlewareCapabilitiesAsync(definition, "test");
        Assert.Equal(6, positive.Count);

        var negative = new ProbeTransport(_ => throw new NotSupportedException());
        var negativeService = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, negative);

        Assert.Empty(await negativeService.ProbeMiddlewareCapabilitiesAsync(definition, "test"));
        Assert.Empty(await negativeService.ProbeMiddlewareCapabilitiesAsync(definition, "test"));
        Assert.Equal(6, negative.Count);
    }

    [Fact]
    public async Task MappingProbeSkipsNonPointerDevices()
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x054A)!;
        var transport = new ProbeTransport(_ => throw new InvalidOperationException("must not probe"));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);

        Assert.Empty(await service.ProbeMiddlewareCapabilitiesAsync(definition, "test"));
        Assert.Equal(0, transport.Count);
    }

    [Fact]
    public async Task ScrollWheelProbeAddsTheMiddlewareCapabilities()
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00E5)!;
        var transport = new ProbeTransport(request => request[8] switch
        {
            0x94 => Echo(request, 0x00, (byte)RazerScrollFreeNotchedMode.FreeSpinMode),
            0xA2 => Echo(request, 0x00, 0x05),
            _ => Response([0x03, 0x01, 0x02, 0x04]),
        });
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);

        var capabilities = await service.ProbeMiddlewareCapabilitiesAsync(definition, "test");

        Assert.Contains(OpenRazerBackendCapability.MappingRead, capabilities);
        Assert.Contains(OpenRazerBackendCapability.ScrollFreeNotchedModeRead, capabilities);
        Assert.Contains(OpenRazerBackendCapability.ScrollAccelerationLevelRead, capabilities);
    }

    [Fact]
    public void CatalogUsesTheMiddlewareFunctionIds()
    {
        AssertFrame(OpenRazerMappingGroup.Off, 0, OpenRazerMappingFunctions.Off, []);
        AssertFrame(OpenRazerMappingGroup.MouseButton, 0, OpenRazerMappingFunctions.ButtonCode, [0x01]);
        AssertFrame(OpenRazerMappingGroup.KeyboardKey, IndexOf(OpenRazerMappingGroup.KeyboardKey, "A"),
            OpenRazerMappingFunctions.KeyCode, [0x00, 0x04]);
        AssertFrame(OpenRazerMappingGroup.KeyboardKey, IndexOf(OpenRazerMappingGroup.KeyboardKey, "Left Shift"),
            OpenRazerMappingFunctions.KeyCode, [0xE1, 0x00]);
        AssertFrame(OpenRazerMappingGroup.Dpi, IndexOf(OpenRazerMappingGroup.Dpi, "DPI cycle up"),
            OpenRazerMappingFunctions.Dpi, [0x06]);
        AssertFrame(OpenRazerMappingGroup.Profile, IndexOf(OpenRazerMappingGroup.Profile, "Profile cycle down"),
            OpenRazerMappingFunctions.Profile, [0x05]);
        AssertFrame(OpenRazerMappingGroup.MediaKey, IndexOf(OpenRazerMappingGroup.MediaKey, "Mute"),
            OpenRazerMappingFunctions.MediaKeys, [0x00, 0xE2]);
        AssertFrame(OpenRazerMappingGroup.PowerKey, IndexOf(OpenRazerMappingGroup.PowerKey, "Sleep"),
            OpenRazerMappingFunctions.PowerKeys, [0x82]);
        AssertFrame(OpenRazerMappingGroup.DoubleClick, 0, OpenRazerMappingFunctions.DoubleClick, [0x01]);
        AssertFrame(OpenRazerMappingGroup.ScrollWheelMode,
            IndexOf(OpenRazerMappingGroup.ScrollWheelMode, "Scroll cycle up"),
            OpenRazerMappingFunctions.ScrollWheelMode, [0x04]);
        AssertFrame(OpenRazerMappingGroup.RazerKey, IndexOf(OpenRazerMappingGroup.RazerKey, "HyperShift"),
            OpenRazerMappingFunctions.RazerKey, [0x59]);
        AssertFrame(OpenRazerMappingGroup.WindowsShortcut,
            IndexOf(OpenRazerMappingGroup.WindowsShortcut, "Screenshot"),
            OpenRazerMappingFunctions.Win8ShortcutsKey, [0x63]);
        AssertFrame(OpenRazerMappingGroup.HyperShiftToggle, 0,
            OpenRazerMappingFunctions.ModeButtonkey, [0x01]);
    }

    [Fact]
    public void KeyboardFrameCarriesTheModifierMask()
    {
        var action = OpenRazerMappingActions.CreateAction(
            OpenRazerMappingGroup.KeyboardKey,
            IndexOf(OpenRazerMappingGroup.KeyboardKey, "C"),
            modifierMask: 0x01);

        Assert.NotNull(action);
        Assert.Equal(OpenRazerMappingFunctions.KeyCode, action.FunctionId);
        Assert.Equal([0x01, 0x06], action.Data);
        Assert.Equal("Ctrl+C", OpenRazerMappingActions.Describe(action.FunctionId, action.Data));
    }

    [Fact]
    public void EveryCatalogOptionRoundTripsThroughTheDecoder()
    {
        foreach (var group in Enum.GetValues<OpenRazerMappingGroup>())
        {
            if (group == OpenRazerMappingGroup.Keep)
            {
                continue;
            }

            IReadOnlyList<OpenRazerMacroOption>? macros = group == OpenRazerMappingGroup.Macro
                ? [new OpenRazerMacroOption(3, "Macro 3")]
                : null;
            var names = OpenRazerMappingActions.OptionNames(group, macros);
            Assert.NotEmpty(names);
            for (var index = 0; index < names.Count; index++)
            {
                var action = OpenRazerMappingActions.CreateAction(group, index, macros: macros);
                Assert.NotNull(action);
                Assert.Equal(names[index], action.Name);
                Assert.Equal(names[index],
                    OpenRazerMappingActions.Describe(action.FunctionId, action.Data, macros));
            }
        }
    }

    [Fact]
    public void MacroAssignmentUsesTheOnboardMacroId()
    {
        OpenRazerMacroOption[] macros = [new(7, "Macro 7")];

        var action = OpenRazerMappingActions.CreateAction(
            OpenRazerMappingGroup.Macro, 0, macros: macros);

        Assert.NotNull(action);
        Assert.Equal(OpenRazerMappingFunctions.MacroTypeI, action.FunctionId);
        Assert.Equal([0x00, 0x07, 0x01], action.Data);
        Assert.Equal("Macro 7", OpenRazerMappingActions.Describe(
            action.FunctionId, action.Data, macros));

        var request = OpenRazerMappingProtocol.SetAssignment(
            0x04, OpenRazerMappingProtocol.NormalMode, action.FunctionId, action.Data);
        Assert.Equal(
            new byte[] { 0x01, 0x04, 0x00, 0x03, 0x03, 0x00, 0x07, 0x01 },
            request.Report.AsSpan(RazerFeatureReport.ArgumentsOffset, 8).ToArray());
    }

    [Fact]
    public void MacroGroupExplainsWhenTheDeviceHasNoSlots()
    {
        Assert.Equal(
            ["No readable macro slots"],
            OpenRazerMappingActions.OptionNames(OpenRazerMappingGroup.Macro));
        Assert.Null(OpenRazerMappingActions.CreateAction(
            OpenRazerMappingGroup.Macro, 0));
    }

    [Fact]
    public void MediaKeyAssignmentUsesTheTwoByteFrame()
    {
        var action = OpenRazerMappingActions.CreateAction(
            OpenRazerMappingGroup.MediaKey, IndexOf(OpenRazerMappingGroup.MediaKey, "Volume up"));
        Assert.NotNull(action);

        var request = OpenRazerMappingProtocol.SetAssignment(
            0x04, OpenRazerMappingProtocol.NormalMode, action.FunctionId, action.Data);

        Assert.Equal(
            new byte[] { 0x01, 0x04, 0x00, 0x0A, 0x02, 0x00, 0xE9 },
            request.Report.AsSpan(RazerFeatureReport.ArgumentsOffset, 7).ToArray());
    }

    private static void AssertFrame(
        OpenRazerMappingGroup group,
        int index,
        byte functionId,
        byte[] data,
        byte modifierMask = 0)
    {
        var action = OpenRazerMappingActions.CreateAction(group, index, modifierMask);

        Assert.NotNull(action);
        Assert.Equal(functionId, action.FunctionId);
        Assert.Equal(data, action.Data);
    }

    private static int IndexOf(OpenRazerMappingGroup group, string name)
    {
        var index = OpenRazerMappingActions.OptionNames(group).ToList().IndexOf(name);
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
            return Task.FromResult(handler(request.ToArray()));
        }
    }
}
