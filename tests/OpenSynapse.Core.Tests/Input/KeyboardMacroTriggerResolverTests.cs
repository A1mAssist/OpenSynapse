using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class KeyboardMacroTriggerResolverTests
{
    [Fact]
    public void ResolvesTypingKeysAsDeviceScopedTriggers()
    {
        var input = RazerKeyboardMacroTriggerResolver.Resolve(0x04, 0x028D);

        Assert.NotNull(input);
        Assert.Equal(MacroInputKind.DeviceButton, input.Kind);
        Assert.Equal(0x028D, input.DeviceId);
        Assert.Equal(0x41, input.VirtualKey);
        Assert.Equal(0x1E, input.ScanCode);
        Assert.False(input.Extended);
    }

    [Theory]
    [InlineData(0x1E, 0x31)] // 1
    [InlineData(0x3A, 0x70)] // F1
    [InlineData(0x50, 0x25)] // Left arrow
    public void ResolvesFunctionAndNavigationKeys(byte usage, int virtualKey)
    {
        var input = RazerKeyboardMacroTriggerResolver.Resolve(usage, 0x02D0);

        Assert.NotNull(input);
        Assert.Equal(virtualKey, input.VirtualKey);
    }

    [Theory]
    [InlineData(0x5C)] // Keypad 4 shares scancode 0x4B with Left arrow
    [InlineData(0xE0)] // Modifier
    [InlineData(0xFF)] // Unknown
    [InlineData(0x60)] // 028D knob slot; also happens to be Keypad Enter
    public void LeavesAmbiguousOrUnsupportedKeysUnbound(byte usage)
    {
        var productId = usage == 0x60 ? (ushort)0x028D : (ushort)0x02D0;
        Assert.Null(RazerKeyboardMacroTriggerResolver.Resolve(usage, productId));
    }

    [Fact]
    public void RequiresAProductId()
    {
        Assert.Null(RazerKeyboardMacroTriggerResolver.Resolve(0x04, 0));
    }
}
