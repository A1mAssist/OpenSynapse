using System.ComponentModel;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class WindowsKeyboardInputSinkTests
{
    [Fact]
    public void BuildsScanCodeAndExtendedFlagsWithInjectionTag()
    {
        WindowsKeyboardInputSink.Input[]? sent = null;
        var sink = new WindowsKeyboardInputSink((inputs, _) =>
        {
            sent = inputs;
            return new((uint)inputs.Length, 0);
        });

        sink.Send(
        [
            new BladeMappingOutputEvent(0x1D, true),
            new BladeMappingOutputEvent(0x5D, false, true),
        ]);

        Assert.NotNull(sent);
        Assert.Equal((ushort)0x1D, sent![0].Data.Keyboard.ScanCode);
        Assert.Equal(0x0008u, sent[0].Data.Keyboard.Flags);
        Assert.Equal((ushort)0x5D, sent[1].Data.Keyboard.ScanCode);
        Assert.Equal(0x000Bu, sent[1].Data.Keyboard.Flags);
        Assert.Equal(WindowsKeyboardInputSink.InjectionTag, sent[1].Data.Keyboard.ExtraInfo);
    }

    [Fact]
    public void PartialBatchRollsBackOnlyPressedEvents()
    {
        var calls = new List<WindowsKeyboardInputSink.Input[]>();
        var sink = new WindowsKeyboardInputSink((inputs, _) =>
        {
            calls.Add(inputs);
            return calls.Count == 1
                ? new(1, 5)
                : new((uint)inputs.Length, 0);
        });

        Assert.Throws<Win32Exception>(() => sink.Send(
        [
            new BladeMappingOutputEvent(30, true),
            new BladeMappingOutputEvent(31, true),
        ]));
        Assert.Equal(2, calls.Count);
        Assert.Single(calls[1]);
        Assert.Equal(30, calls[1][0].Data.Keyboard.ScanCode);
        Assert.Equal(0x000Au, calls[1][0].Data.Keyboard.Flags);
    }
}
