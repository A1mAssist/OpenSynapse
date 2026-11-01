using OpenSynapse.Windows.Input;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class GameModeKeyboardSuppressorTests
{
    [Theory]
    [InlineData(0x5B, 0u, true)]
    [InlineData(0x5C, 0u, true)]
    [InlineData(0x5B, 0x10u, false)]
    [InlineData(0x41, 0u, false)]
    public void OnlyPhysicalWindowsKeysAreSuppressed(int virtualKey, uint flags, bool expected)
    {
        Assert.Equal(expected, GameModeKeyboardSuppressor.ShouldSuppress(virtualKey, flags));
    }
}
