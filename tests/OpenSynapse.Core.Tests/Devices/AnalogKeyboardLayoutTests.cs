using OpenSynapse.Core.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class AnalogKeyboardLayoutTests
{
    [Theory]
    [InlineData(0x02A6, true, true)]
    [InlineData(0x02D0, false, true)]
    [InlineData(0x02D1, false, false)]
    [InlineData(0x02E4, false, false)]
    [InlineData(0x02EA, false, true)]
    public void SupportedLayoutsExposeUniqueMiddlewareKeyIds(
        ushort productId,
        bool hasNumpad,
        bool hasFunctionRow)
    {
        Assert.True(AnalogKeyboardLayouts.TryGet(productId, out var layout));
        Assert.NotEmpty(layout.Keys);
        Assert.Equal(layout.Keys.Count, layout.Keys.Select(key => key.KeyId).Distinct().Count());
        Assert.Equal(hasNumpad, layout.Keys.Any(key => key.Label.StartsWith("Num ", StringComparison.Ordinal)));
        Assert.Equal(hasFunctionRow, layout.Keys.Any(key => key.KeyId == 112));
        Assert.Contains(layout.Keys, key => key.KeyId == 31 && key.Label == "A");
        Assert.Contains(layout.Keys, key => key.KeyId == 61 && key.Label == "Space");
    }

    [Fact]
    public void UnlistedProductsDoNotGetAGuessedLayout()
    {
        Assert.False(AnalogKeyboardLayouts.TryGet(0x028D, out _));
    }
}
