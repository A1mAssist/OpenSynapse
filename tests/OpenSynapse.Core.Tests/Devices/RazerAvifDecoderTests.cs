using OpenSynapse.Core.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerAvifDecoderTests
{
    [Fact]
    public void KeepsAlphaFromAvif()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "avif-alpha.avif");
        var data = File.ReadAllBytes(path);

        Assert.True(RazerAvifDecoder.TryDecode(data, out var bgra, out var width, out var height));
        Assert.Equal(4, width);
        Assert.Equal(4, height);
        Assert.Equal(width * height * 4, bgra.Length);

        Assert.Equal(0, bgra[3]);
        var center = ((height / 2) * width + (width / 2)) * 4;
        Assert.Equal(255, bgra[center + 3]);
    }

    [Fact]
    public void RejectsGarbage()
    {
        Assert.False(RazerAvifDecoder.TryDecode([1, 2, 3, 4], out _, out _, out _));
    }
}
