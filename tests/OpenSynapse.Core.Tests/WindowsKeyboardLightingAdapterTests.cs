using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.Core.Tests;

public sealed class WindowsKeyboardLightingAdapterTests
{
    [Fact]
    public void MapsPhysicalKeyAndRejectsInjectedKeyUpOrUnknownInput()
    {
        var adapter = new WindowsKeyboardLightingAdapter();

        Assert.True(adapter.TryTranslate(0x1E, 0, TimeSpan.FromMilliseconds(100), out var keyEvent));
        Assert.Equal(new QuickLightingKeyEvent(3, 1, TimeSpan.FromMilliseconds(100)), keyEvent);
        Assert.False(adapter.TryTranslate(0x1F, WindowsKeyboardLightingAdapter.InjectedFlag, TimeSpan.FromMilliseconds(120), out _));
        Assert.False(adapter.TryTranslate(0x20, WindowsKeyboardLightingAdapter.KeyUpFlag, TimeSpan.FromMilliseconds(140), out _));
        Assert.False(adapter.TryTranslate(0xFF, 0, TimeSpan.FromMilliseconds(160), out _));
    }

    [Fact]
    public void DebouncesOnlyRepeatedScanCodeInsideWindow()
    {
        var adapter = new WindowsKeyboardLightingAdapter();

        Assert.True(adapter.TryTranslate(0x1E, 0, TimeSpan.FromMilliseconds(100), out _));
        Assert.False(adapter.TryTranslate(0x1E, 0, TimeSpan.FromMilliseconds(119), out _));
        Assert.True(adapter.TryTranslate(0x1F, 0, TimeSpan.FromMilliseconds(119), out _));
        Assert.True(adapter.TryTranslate(0x1E, 0, TimeSpan.FromMilliseconds(120), out _));
    }

    [Fact]
    public void RejectsLogicalPositionsThatHaveNoDeviceLed()
    {
        var adapter = new WindowsKeyboardLightingAdapter();

        Assert.False(adapter.TryTranslate(0x2B, 0, TimeSpan.FromMilliseconds(100), out _));
    }
}
