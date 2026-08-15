using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.Core.Tests;

public sealed class WindowsKeyboardLightingAdapterTests
{
    [Fact]
    public void MapsPhysicalKeyAndRejectsKeyUpOrUnknownInput()
    {
        var adapter = new WindowsKeyboardLightingAdapter();

        Assert.True(adapter.TryTranslate(0x1E, 0, TimeSpan.FromMilliseconds(100), out var keyEvent));
        Assert.Equal(new QuickLightingKeyEvent(3, 1, TimeSpan.FromMilliseconds(100)), keyEvent);
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

    [Theory]
    [InlineData(0x2C, 0, 4, 2)]
    [InlineData(0x35, 0, 4, 11)]
    [InlineData(0x49, WindowsKeyboardLightingAdapter.ExtendedFlag, 1, 15)]
    [InlineData(0x51, WindowsKeyboardLightingAdapter.ExtendedFlag, 2, 15)]
    [InlineData(0x4D, WindowsKeyboardLightingAdapter.ExtendedFlag, 5, 15)]
    [InlineData(0x50, WindowsKeyboardLightingAdapter.ExtendedFlag, 6, 13)]
    public void MapsFifthRowAndArrowKeys(
        uint scanCode,
        uint flags,
        int expectedRow,
        int expectedColumn)
    {
        var adapter = new WindowsKeyboardLightingAdapter();

        Assert.True(adapter.TryTranslate(
            scanCode,
            flags,
            TimeSpan.FromMilliseconds(100),
            out var keyEvent));
        Assert.Equal((expectedRow, expectedColumn), (keyEvent.Row, keyEvent.Column));
    }

    [Fact]
    public void DistinguishesExtendedKeysAndMapsBackslashPastOfficialGap()
    {
        var adapter = new WindowsKeyboardLightingAdapter();

        Assert.False(adapter.TryTranslate(0x47, 0, TimeSpan.FromMilliseconds(100), out _));
        Assert.True(adapter.TryTranslate(
            0x2B,
            0,
            TimeSpan.FromMilliseconds(110),
            out var backslash));
        Assert.Equal((2, 14), (backslash.Row, backslash.Column));
        Assert.True(adapter.TryTranslate(
            0x1D,
            0,
            TimeSpan.FromMilliseconds(120),
            out var leftControl));
        Assert.True(adapter.TryTranslate(
            0x1D,
            WindowsKeyboardLightingAdapter.ExtendedFlag,
            TimeSpan.FromMilliseconds(120),
            out var rightControl));
        Assert.NotEqual(leftControl.Column, rightControl.Column);
    }

    [Theory]
    [InlineData("\\\\?\\HID#VID_1532&PID_02C6&MI_01&COL01#123")]
    [InlineData("\\\\?\\hid#vid_1532&pid_02c6&mi_00#456")]
    public void AcceptsOnlyBladeRawInputDevices(string deviceName)
    {
        Assert.True(WindowsKeyboardLightingAdapter.IsBladeKeyboardDevice(deviceName));
        Assert.False(WindowsKeyboardLightingAdapter.IsBladeKeyboardDevice(
            "\\\\?\\HID#VID_1532&PID_00B8&MI_01&COL01#123"));
        Assert.False(WindowsKeyboardLightingAdapter.IsBladeKeyboardDevice(
            "\\\\?\\HID#VID_046D&PID_C33A#123"));
    }

    [Fact]
    public void MapsPhysicallyVerifiedBladeRightSideKeysFromReportFour()
    {
        var adapter = new WindowsKeyboardLightingAdapter();
        var events = new List<QuickLightingKeyEvent>();

        adapter.TranslateRazerKeyReport([0x04, 0x03, 0, 0], TimeSpan.FromMilliseconds(100), events);
        adapter.TranslateRazerKeyReport([0x04, 0x03, 0, 0], TimeSpan.FromMilliseconds(110), events);
        adapter.TranslateRazerKeyReport([0x04, 0, 0, 0], TimeSpan.FromMilliseconds(120), events);
        adapter.TranslateRazerKeyReport([0x04, 0xD3, 0, 0], TimeSpan.FromMilliseconds(130), events);
        adapter.TranslateRazerKeyReport([0x04, 0, 0, 0], TimeSpan.FromMilliseconds(140), events);
        adapter.TranslateRazerKeyReport([0x04, 0xD4, 0, 0], TimeSpan.FromMilliseconds(150), events);

        Assert.Equal(
            [
                new QuickLightingKeyEvent(3, 15, TimeSpan.FromMilliseconds(100)),
                new QuickLightingKeyEvent(4, 15, TimeSpan.FromMilliseconds(130)),
                new QuickLightingKeyEvent(5, 15, TimeSpan.FromMilliseconds(150)),
            ],
            events);
    }

}
