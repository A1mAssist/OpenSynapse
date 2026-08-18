using System.ComponentModel;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Core.Tests;

public sealed class WindowsTouchpadControllerTests
{
    [Fact]
    public void MissingOrNonDwordStateIsUnknown()
    {
        Assert.Null(CreateController(() => null).GetEnabled());
        Assert.Null(CreateController(() => 1L).GetEnabled());
        Assert.Null(CreateController(() => "1").GetEnabled());
    }

    [Fact]
    public void ToggleUsesExactSourceBackedEightEventSequence()
    {
        var reads = new Queue<object?>([0, 1]);
        WindowsTouchpadController.Input[]? sentInputs = null;
        var inputSize = 0;
        var controller = new WindowsTouchpadController(
            reads.Dequeue,
            (inputs, size) =>
            {
                sentInputs = inputs;
                inputSize = size;
                return new((uint)inputs.Length, 0);
            },
            _ => throw new InvalidOperationException("Successful first readback must not delay."),
            readbackAttempts: 1);

        Assert.True(controller.ToggleVerified());
        Assert.NotNull(sentInputs);
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, inputSize);
        Assert.Equal(System.Runtime.InteropServices.Marshal.SizeOf<WindowsTouchpadController.Input>(), inputSize);
        Assert.All(sentInputs, input => Assert.Equal(1U, input.Type));
        Assert.Equal(
            new ushort[] { 0x87, 0x11, 0x5B, 0x87, 0x87, 0x11, 0x5B, 0x87 },
            sentInputs.Select(input => input.Data.Keyboard.VirtualKey));
        Assert.Equal(
            sentInputs.Select(input => input.Data.Keyboard.VirtualKey),
            sentInputs.Select(input => input.Data.Keyboard.ScanCode));
        Assert.Equal(
            new uint[] { 1, 1, 1, 1, 3, 3, 3, 3 },
            sentInputs.Select(input => input.Data.Keyboard.Flags));
        Assert.All(sentInputs, input => Assert.Equal(0U, input.Data.Keyboard.Time));
        Assert.All(sentInputs, input => Assert.Equal((nuint)0, input.Data.Keyboard.ExtraInfo));
    }

    [Fact]
    public void PartialSendReportsNativeErrorAndDoesNotReadBack()
    {
        var reads = 0;
        var controller = new WindowsTouchpadController(
            () =>
            {
                reads++;
                return 1;
            },
            (_, _) => new(7, 5),
            _ => { });

        var error = Assert.Throws<Win32Exception>(() => controller.ToggleVerified());

        Assert.Equal(5, error.NativeErrorCode);
        Assert.Contains("7/8", error.Message);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void UnchangedReadbackFailsAfterBoundedPolling()
    {
        var reads = 0;
        var delays = 0;
        var controller = new WindowsTouchpadController(
            () =>
            {
                reads++;
                return 1;
            },
            (inputs, _) => new((uint)inputs.Length, 0),
            _ => delays++,
            readbackAttempts: 3,
            readbackInterval: TimeSpan.Zero);

        var error = Assert.Throws<InvalidOperationException>(() => controller.ToggleVerified());

        Assert.Contains("回读未变化", error.Message);
        Assert.Equal(4, reads);
        Assert.Equal(2, delays);
    }

    private static WindowsTouchpadController CreateController(Func<object?> read) => new(
        read,
        (inputs, _) => new((uint)inputs.Length, 0),
        _ => { },
        readbackAttempts: 1);
}
